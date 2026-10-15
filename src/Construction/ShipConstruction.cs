using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hansa.Construction
{
    public sealed class BlockState
    {
        public string Kind, Material, Facing;
        /// <summary>1 = sound. Below CrackedBelow a block shows as cracked; at 0 it is gone.</summary>
        public double Health = 1;
        public bool Open;
        public const double CrackedBelow = 0.5;
        public bool Cracked => Health < CrackedBelow;
        public BlockState Copy() => (BlockState)MemberwiseClone();
        public bool Same(BlockState o) => o != null && Kind == o.Kind && Material == o.Material && Facing == o.Facing && Health == o.Health && Open == o.Open;
    }

    /// <summary>One ship's blocks: the design's default block map plus everything that changed.
    /// Every operation touches exactly one cell. Rules only read neighbours.</summary>
    public sealed partial class ShipConstruction
    {
        public readonly ShipLattice Lattice;
        public readonly BlockKinds Kinds;
        readonly Dictionary<CellId, BlockState> design = new();
        readonly Dictionary<CellId, BlockState> blocks = new();
        /// <summary>Blocks lost to damage, remembered for repair and for their guide shape.</summary>
        readonly Dictionary<CellId, BlockState> wrecked = new();
        readonly Dictionary<CellId, BlockShape> shapes = new();
        public readonly List<string> LayoutProblems = new();

        public ShipConstruction(ShipLattice lattice, BlockKinds kinds)
        {
            if (lattice.Problems.Count > 0) throw new ArgumentException("Design has problems: " + string.Join("; ", lattice.Problems));
            Lattice = lattice; Kinds = kinds;
            foreach (var cell in lattice.Cells.Values)
                design[cell.Id] = new BlockState { Kind = kinds.ForLead(cell.Lead).Code };
            foreach (var op in lattice.Design.Layout) ApplyLayout((JObject)op);
            foreach (var (id, s) in design) blocks[id] = s.Copy();
            ResetFittings();
        }

        // ---- the default block map ------------------------------------------------------------

        void ApplyLayout(JObject op)
        {
            if (op["set"] != null)
            {
                string kind = (string)op["set"], facing = (string)op["facing"], material = (string)op["material"];
                foreach (var at in op["at"].ToObject<int[][]>()) SetDesign(CellId.From(at), kind, facing, material);
            }
            else if (op["clear"] != null)
                foreach (var at in op["clear"].ToObject<int[][]>()) design.Remove(CellId.From(at));
            else if (ApplyFittingLayout(op)) { }
            else LayoutProblems.Add("Unknown layout entry: " + op.ToString(Formatting.None));
        }

        void SetDesign(CellId c, string kind, string facing, string material)
        {
            var k = Kinds.Get(kind);
            if (!k.Fits(Lattice.SlotKey(c), Lattice.Interior(c)))
                LayoutProblems.Add($"Layout puts {kind} at {c}, which holds '{Lattice.SlotKey(c)}'");
            design[c] = new BlockState { Kind = kind, Facing = facing, Material = material };
        }

        // ---- queries ---------------------------------------------------------------------------

        public BlockState At(CellId c) => blocks.TryGetValue(c, out var s) ? s : null;
        public BlockState DesignAt(CellId c) => design.TryGetValue(c, out var s) ? s : null;
        public bool Solid(CellId c) => blocks.ContainsKey(c);
        public IEnumerable<CellId> Present => blocks.Keys;

        public BlockKind KindAt(CellId c) => blocks.TryGetValue(c, out var s) ? Kinds.Get(s.Kind) : null;

        /// <summary>Whether this cell currently keeps water out of the hull.</summary>
        public bool Seals(CellId c)
        {
            var s = At(c);
            if (s == null || !Lattice.SlotKey(c).Split('+').Contains(Parts.Skin)) return false;
            var k = Kinds.Get(s.Kind);
            return k.Opening == null || k.Opening.Panel == "glass" || !s.Open;
        }

        public BlockShape Shape(CellId c)
        {
            var s = At(c);
            if (s == null) return null;
            if (!shapes.TryGetValue(c, out var shape)) shapes[c] = shape = BlockShapes.Build(Lattice, c, Kinds.Get(s.Kind), s);
            return shape;
        }

        /// <summary>Faces a renderer draws: a face lying on the cell boundary is hidden while the
        /// neighbour across it has the same face turned the other way.</summary>
        public IEnumerable<ShipFace> VisibleFaces(CellId c)
        {
            var shape = Shape(c);
            if (shape == null) yield break;
            foreach (var f in shape.Faces)
            {
                var across = Lattice.CellAt(Convex.Mean(f.Points) + f.Normal * 1e-4);
                if (!across.Equals(c) && Shape(across) is BlockShape other && other.FaceKeys.Contains(Convex.FaceKey(f.Points))) continue;
                yield return f;
            }
        }

        public IEnumerable<(int, int, int)> Collision() =>
            blocks.Keys.SelectMany(c => Shape(c).Voxels).Concat(walls.Keys.SelectMany(f => WallShape(f).Voxels)).Distinct();

        // ---- edits: each changes one cell --------------------------------------------------------

        public string PlaceProblem(CellId c, string kind, string material, string facing = null)
        {
            if (Solid(c)) return "The cell is occupied";
            if (StairAt(c) != null) return "A stair stands in this cell";
            if (!Kinds.ByCode.TryGetValue(kind ?? "", out var k)) return "Unknown block kind";
            if (!k.Fits(Lattice.SlotKey(c), Lattice.Interior(c))) return $"A {kind} does not fit here";
            if (!k.Accepts(material)) return $"A {kind} is built from {k.Material}";
            if (k.Facing && facing == null) return $"A {kind} needs a facing";
            return null;
        }

        public void Place(CellId c, string kind, string material, string facing = null)
        {
            var problem = PlaceProblem(c, kind, material, facing);
            if (problem != null) throw new InvalidOperationException(problem);
            Set(c, new BlockState { Kind = kind, Material = material, Facing = facing });
            wrecked.Remove(c);
        }

        /// <summary>A shipwright's removal. Refused when it would leave other blocks unsupported.</summary>
        public string RemoveProblem(CellId c)
        {
            if (!Solid(c)) return "Nothing to remove";
            var before = Supported();
            var saved = blocks[c];
            blocks.Remove(c);
            var after = Supported();
            blocks[c] = saved;
            var stranded = before.Where(x => x != c && !after.Contains(x)).OrderBy(x => x).ToList();
            return stranded.Count == 0 ? null : $"Removing it would leave {stranded.Count} block(s) unsupported, e.g. {stranded[0]}";
        }

        public BlockState Remove(CellId c)
        {
            var problem = RemoveProblem(c);
            if (problem != null) throw new InvalidOperationException(problem);
            var old = blocks[c];
            Set(c, null);
            return old;
        }

        /// <summary>Damage ignores support rules. A destroyed block is remembered for repair.</summary>
        public void Damage(CellId c, double amount)
        {
            var s = At(c);
            if (s == null) return;
            var n = s.Copy();
            n.Health = Math.Max(0, s.Health - amount / Kinds.Get(s.Kind).Health);
            if (n.Health <= 0) { wrecked[c] = s.Copy(); Set(c, null); }
            else Set(c, n);
        }

        /// <summary>Repairs a cracked block, or rebuilds a destroyed one with ordinary material.</summary>
        public void Repair(CellId c, string material)
        {
            var s = At(c);
            if (s != null)
            {
                if (!Kinds.Get(s.Kind).Accepts(material)) throw new InvalidOperationException("Wrong repair material");
                var n = s.Copy(); n.Health = 1; Set(c, n);
                return;
            }
            var was = wrecked.TryGetValue(c, out var w) ? w : DesignAt(c) ?? throw new InvalidOperationException("Nothing to repair here");
            if (!Kinds.Get(was.Kind).Accepts(material)) throw new InvalidOperationException("Wrong repair material");
            var r = was.Copy(); r.Health = 1; r.Open = false;
            if (Kinds.Get(was.Kind).Material == "planks") r.Material = material;
            Set(c, r);
            wrecked.Remove(c);
        }

        public void Toggle(CellId c)
        {
            var s = At(c);
            var k = s == null ? null : Kinds.Get(s.Kind);
            if (k?.Opening?.Operable != true) throw new InvalidOperationException("No operable fitting in this cell");
            var n = s.Copy(); n.Open = !n.Open; Set(c, n);
        }

        void Set(CellId c, BlockState s)
        {
            if (s == null) blocks.Remove(c); else blocks[c] = s;
            shapes.Remove(c);
        }

        // ---- rules -----------------------------------------------------------------------------

        bool Required(CellId c, BlockState s)
        {
            var req = Kinds.Get(s.Kind).Required;
            return req == "always" || req == "belowWaterline" && Lattice.Bounds(c).lo.Y < Lattice.Design.Waterline;
        }

        /// <summary>Missing required blocks, with the block their grey guide shows. Removing an
        /// optional default (a partition, a deck plank for a hatch) never produces a guide.</summary>
        public IEnumerable<(CellId cell, BlockState guide)> MissingRequired()
        {
            foreach (var (c, s) in design.OrderBy(kv => kv.Key))
                if (!Solid(c))
                {
                    var guide = wrecked.TryGetValue(c, out var w) ? w : s;
                    if (Required(c, s)) yield return (c, guide);
                }
        }

        static readonly HashSet<string> FrameLeads = new() { Parts.Keel, Parts.Rib, Parts.Beam };

        /// <summary>Present blocks with a load path: keel, ribs and beams joined to the keel; planking
        /// and openings within PlankSpan cells of supported framing; stairs and partitions resting on
        /// something supported.</summary>
        public HashSet<CellId> Supported()
        {
            var ok = new HashSet<CellId>();
            var queue = new Queue<CellId>();
            foreach (var c in blocks.Keys.Where(c => KindAt(c).Lead == Parts.Keel)) { ok.Add(c); queue.Enqueue(c); }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in CellId.Sides)
                {
                    var n = c + d;
                    if (!ok.Contains(n) && blocks.ContainsKey(n) && FrameLeads.Contains(KindAt(n).Lead ?? "")) { ok.Add(n); queue.Enqueue(n); }
                }
            }
            var frames = ok.ToHashSet();
            int span = Lattice.Design.PlankSpan;
            foreach (var c in blocks.Keys)
            {
                if (ok.Contains(c)) continue;
                bool held = false;
                foreach (int dir in new[] { 1, -1 })
                    for (int dx = 1; dx <= span && !held; dx++)
                    {
                        var x = c + new CellId(dir * dx, 0, 0);
                        held = Enumerable.Range(-1, 3).Any(dy => Enumerable.Range(-1, 3).Any(dz => frames.Contains(x + new CellId(0, dy, dz))));
                        if (!blocks.ContainsKey(x)) break;
                    }
                if (held) ok.Add(c);
            }
            return ok;
        }

        /// <summary>Everything wrong with the current blocks. The default ship must return none.</summary>
        public List<string> Problems()
        {
            var list = new List<string>(LayoutProblems);
            var held = Supported();
            foreach (var c in blocks.Keys.Where(c => !held.Contains(c)).OrderBy(c => c)) list.Add($"{At(c).Kind} at {c} is unsupported");
            foreach (var (c, _) in MissingRequired()) list.Add($"Required block missing at {c}");
            list.AddRange(FittingProblems(held));
            return list;
        }

        // ---- saves -----------------------------------------------------------------------------

        sealed class Save
        {
            public int Schema;
            public string Design;
            public Dictionary<string, BlockState> Changes = new();
            public Dictionary<string, BlockState> Wrecked = new();
            public Dictionary<string, WallState> Walls = new();
            public List<StairFitting> Stairs;
        }

        /// <summary>Only differences from the design are saved, by cell address. Geometry is never
        /// saved, so form, roughness or rib-width edits keep saves valid.</summary>
        public string Serialize()
        {
            var save = new Save { Schema = 1, Design = Lattice.Design.Code };
            foreach (var c in design.Keys.Union(blocks.Keys).OrderBy(c => c))
            {
                var a = DesignAt(c); var b = At(c);
                if (a == null && b == null || a != null && a.Same(b)) continue;
                save.Changes[c.ToString()] = b;
            }
            foreach (var (c, s) in wrecked.OrderBy(kv => kv.Key)) save.Wrecked[c.ToString()] = s;
            SaveFittings(out save.Walls, out save.Stairs);
            return JsonConvert.SerializeObject(save, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include });
        }

        public void Load(string json)
        {
            var save = JsonConvert.DeserializeObject<Save>(json);
            if (save?.Schema != 1 || save.Design != Lattice.Design.Code) throw new ArgumentException("Save is for another design or schema");
            foreach (var s in save.Changes.Values.Concat(save.Wrecked.Values))
                if (s != null) Kinds.Get(s.Kind);
            blocks.Clear(); wrecked.Clear(); shapes.Clear();
            foreach (var (c, s) in design) blocks[c] = s.Copy();
            foreach (var (key, s) in save.Changes)
            {
                var c = CellId.Parse(key);
                if (s == null) blocks.Remove(c); else blocks[c] = s;
            }
            foreach (var (key, s) in save.Wrecked) wrecked[CellId.Parse(key)] = s;
            LoadFittings(save.Walls, save.Stairs);
        }
    }
}
