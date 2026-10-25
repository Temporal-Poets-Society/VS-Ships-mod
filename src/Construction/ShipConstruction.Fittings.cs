using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hansa.Construction
{
    /// <summary>Walls, doors and ladder-stairs: fittings beside the master blocks. A wall is addressed
    /// by its cell face and takes neither cell; a stair takes the cells it stands in. Each edit
    /// still changes one fitting only.</summary>
    public sealed partial class ShipConstruction
    {
        readonly Dictionary<FaceId, WallState> designWalls = new();
        readonly List<StairFitting> designStairs = new();
        Dictionary<FaceId, WallState> walls = new();
        List<StairFitting> stairs = new();
        readonly Dictionary<FaceId, FittingShape> wallShapes = new();
        public const double MinWallArea = 0.3;

        public sealed class FittingShape
        {
            public readonly List<ShipFace> Faces = new();
            public readonly List<(int, int, int)> Voxels = new();
            public double Area;
        }

        // ---- layout --------------------------------------------------------------------------

        bool ApplyFittingLayout(JObject op)
        {
            if (op["stair"] is JObject st)
            {
                var s = new StairFitting { Foot = st["foot"].ToObject<int[]>(), Dir = (string)st["dir"], Material = (string)st["material"] };
                designStairs.Add(s);
                // The well: the stair's top row in the deck above is opened.
                foreach (var c in FittingShapes.Occupied(Lattice, s)) design.Remove(c);
                return true;
            }
            if (op["wall"] is JObject w)
            {
                foreach (var f in Faces(w)) designWalls[f] = new WallState { Material = (string)w["material"] };
                return true;
            }
            if (op["door"] is JObject d)
            {
                foreach (var f in Faces(d))
                {
                    designWalls.Remove(f.Up);
                    designWalls[f] = new WallState { Kind = "door", Material = (string)d["material"] };
                }
                return true;
            }
            return false;
        }

        /// <summary>"at": cells, or "from"/"to": an inclusive box of cells; "side": which face of each.</summary>
        static IEnumerable<FaceId> Faces(JObject o)
        {
            string side = (string)o["side"];
            IEnumerable<CellId> cells;
            if (o["at"] != null) cells = o["at"].ToObject<int[][]>().Select(CellId.From);
            else
            {
                var a = CellId.From(o["from"].ToObject<int[]>()); var b = CellId.From(o["to"].ToObject<int[]>());
                cells = from x in Range(a.X, b.X) from y in Range(a.Y, b.Y) from z in Range(a.Z, b.Z) select new CellId(x, y, z);
            }
            return cells.Select(c => FaceId.Of(c, side));
        }

        static IEnumerable<int> Range(int a, int b) => Enumerable.Range(Math.Min(a, b), Math.Abs(b - a) + 1);

        void ResetFittings()
        {
            walls = designWalls.ToDictionary(kv => kv.Key, kv => kv.Value.Copy());
            stairs = designStairs.Select(s => s.Copy()).ToList();
            wallShapes.Clear();
        }

        // ---- queries -------------------------------------------------------------------------

        public IReadOnlyList<StairFitting> Stairs => stairs;
        public IEnumerable<FaceId> WallFaces => walls.Keys;

        public StairFitting StairAt(CellId c) => stairs.FirstOrDefault(s => FittingShapes.Occupied(Lattice, s).Contains(c));

        /// <summary>The wall or door standing on a face; a door also answers for the face above it.</summary>
        public WallState WallAt(FaceId f) =>
            walls.TryGetValue(f, out var w) ? w :
            walls.TryGetValue(new FaceId(f.Cell - CellId.Up, f.Axis), out var below) && below.Kind == "door" ? below : null;

        public bool WallSeals(FaceId f) => WallAt(f) is WallState w && (w.Kind == "wall" || !w.Open);

        public FittingShape WallShape(FaceId f)
        {
            if (!walls.TryGetValue(f, out var w)) return null;
            if (!wallShapes.TryGetValue(f, out var shape)) wallShapes[f] = shape = BuildWall(f, w);
            return shape;
        }

        FittingShape BuildWall(FaceId f, WallState w)
        {
            var (draw, collide) = FittingShapes.Wall(Lattice, f, w);
            var shape = new FittingShape();
            string material = w.Material ?? Lattice.Design.Material;
            AddFaces(shape, draw, material, V3.Axis(1));
            shape.Area = draw.Where(d => d.part == "wall" || d.part == "door" && !w.Open).Sum(d => d.solid.Volume()) / FittingShapes.Board;
            foreach (var v in collide.SelectMany(Lattice.SampleVoxels).Distinct().OrderBy(v => v)) shape.Voxels.Add(v);
            return shape;
        }

        public FittingShape StairShape(StairFitting s)
        {
            var shape = new FittingShape();
            var dir = CellId.Dir(s.Dir);
            AddFaces(shape, FittingShapes.Stair(Lattice, s), s.Material ?? Lattice.Design.Material, V3.Axis(dir.X != 0 ? 0 : 2));
            return shape;
        }

        static void AddFaces(FittingShape shape, List<(string part, Convex solid)> solids, string material, V3 grain)
        {
            var count = new Dictionary<string, int>();
            foreach (var (_, solid) in solids)
                foreach (var f in solid.Faces) { var k = Convex.FaceKey(f); count[k] = count.TryGetValue(k, out int n) ? n + 1 : 1; }
            foreach (var (part, solid) in solids)
                foreach (var f in solid.Faces)
                {
                    if (count[Convex.FaceKey(f)] > 1) continue;
                    var n = Convex.Normal(f).Value;
                    shape.Faces.Add(new ShipFace { Points = f, Normal = n, Part = part, Material = material, Uv = Uvs.Metric(f, n, grain) });
                }
        }

        public LadderLine Ladder(StairFitting s) => FittingShapes.Ladder(Lattice, s);

        // ---- stairs ----------------------------------------------------------------------------

        /// <summary>Faces a climber passes through: across the stair, its foot approach and its exit.</summary>
        IEnumerable<FaceId> Passage(StairFitting s)
        {
            int rise = FittingShapes.Rise(Lattice, s);
            var dir = CellId.Dir(s.Dir);
            string side = s.Dir;
            for (int r = 0; r < rise; r++) yield return FaceId.Of(s.FootCell + new CellId(0, r, 0), side);
            for (int r = 0; r < 2; r++) yield return FaceId.Of(s.FootCell - dir + new CellId(0, r, 0), side);
            for (int r = 0; r < 2; r++) yield return FaceId.Of(s.Head + new CellId(0, rise + r, 0), side);
        }

        public string StairProblem(StairFitting s, StairFitting ignore = null)
        {
            int rise = FittingShapes.Rise(Lattice, s);
            if (rise < 2 || rise > 4) return "A stair needs a deck 2 to 4 blocks above its foot";
            var dir = CellId.Dir(s.Dir);
            foreach (var c in FittingShapes.Occupied(Lattice, s))
            {
                if (Solid(c)) return c.Y == s.FootCell.Y + rise - 1 ? $"Open the deck above first ({c})" : $"Cell {c} is occupied";
                var other = StairAt(c);
                if (other != null && other != ignore) return $"Cell {c} has another stair";
                var slot = Lattice.SlotKey(c);
                if (slot != Parts.Deck && !(slot.Length == 0 && Lattice.Interior(c))) return $"The stair would cut through structure at {c}";
            }
            var before = s.FootCell - dir;
            if (Solid(before) || !Solid(before - CellId.Up)) return $"No floor to stand on at the stair's foot ({before})";
            var exit = s.Head + dir + new CellId(0, rise, 0);
            if (Solid(exit) || !Solid(exit - CellId.Up)) return $"No deck to step off onto at the top ({exit})";
            foreach (var f in Passage(s)) if (WallAt(f) is WallState w && (w.Kind == "wall" || !w.Open)) return $"A wall blocks the stair at {f}";
            return null;
        }

        public void PlaceStair(CellId foot, string dir, string material)
        {
            var s = new StairFitting { Foot = new[] { foot.X, foot.Y, foot.Z }, Dir = dir, Material = material };
            if (!Kinds.Get("deck").Accepts(material)) throw new InvalidOperationException("A stair is built from planks");
            var problem = StairProblem(s);
            if (problem != null) throw new InvalidOperationException(problem);
            stairs.Add(s);
        }

        /// <summary>Takes up the stair standing in a cell.</summary>
        public StairFitting RemoveStair(CellId anyCell)
        {
            var s = StairAt(anyCell) ?? throw new InvalidOperationException("No stair here");
            stairs.Remove(s);
            return s;
        }

        // ---- walls and doors -------------------------------------------------------------------

        public string WallProblem(FaceId f, string kind = "wall")
        {
            if (kind is not ("wall" or "door")) return "Unknown wall kind";
            var rows = kind == "door" ? new[] { f, f.Up } : new[] { f };
            foreach (var r in rows)
            {
                if (WallAt(r) != null) return $"A wall already stands at {r}";
                if (HitsTimber(r)) return $"A wall at {r} would cut through rib or keel timber";
                if (stairs.Any(s => Passage(s).Contains(r))) return $"The wall would block a stair at {r}";
            }
            var probe = FittingShapes.Wall(Lattice, f, new WallState { Kind = kind });
            double area = probe.draw.Sum(d => d.solid.Volume()) / FittingShapes.Board;
            if (area < MinWallArea * rows.Length) return "Too little space between floor and hull for a wall";
            return null;
        }

        /// <summary>Whether a board on this face would pass through rib or keel timber beside it.
        /// Decks and beams only lower the wall's top; the hull's surface trims its ends.</summary>
        bool HitsTimber(FaceId f)
        {
            var (lo, hi) = Lattice.Bounds(f.Cell);
            double plane = hi[f.Axis];
            foreach (var c in new[] { f.Cell, f.Beyond })
                if (Solid(c) && Lattice.Cells.TryGetValue(c, out var cell))
                    foreach (var p in cell.Pieces.Where(p => p.Part is Parts.Rib or Parts.Keel))
                    {
                        var (plo, phi) = p.Solid.Bounds();
                        if (plo[f.Axis] < plane + FittingShapes.Board / 2 && phi[f.Axis] > plane - FittingShapes.Board / 2) return true;
                    }
            return false;
        }

        public void PlaceWall(FaceId f, string kind, string material)
        {
            if (!Kinds.Get("deck").Accepts(material)) throw new InvalidOperationException("Walls and doors are built from planks");
            var problem = WallProblem(f, kind);
            if (problem != null) throw new InvalidOperationException(problem);
            walls[f] = new WallState { Kind = kind, Material = material };
            wallShapes.Remove(f);
        }

        /// <summary>Where the wall or door covering this face is kept (a door's upper row is kept at its lower one), or null.</summary>
        FaceId? WallKey(FaceId f)
        {
            if (walls.ContainsKey(f)) return f;
            return WallAt(f) == null ? null : new FaceId(f.Cell - CellId.Up, f.Axis);
        }

        /// <summary>(review H1-28) Taking down a wall is refused while a wall above stands on it.</summary>
        public string RemoveWallProblem(FaceId f)
        {
            if (WallKey(f) is not FaceId key) return "No wall here";
            var held = Supported();
            var before = SupportProblems(held).ToHashSet();
            var w = walls[key];
            walls.Remove(key);
            var after = SupportProblems(held).ToList();
            walls[key] = w;
            var fitting = after.FirstOrDefault(p => !before.Contains(p));
            return fitting == null ? null : $"Removing it would leave a fitting unsupported: {fitting}";
        }

        public WallState RemoveWall(FaceId f)
        {
            var problem = RemoveWallProblem(f);
            if (problem != null) throw new InvalidOperationException(problem);
            f = WallKey(f).Value;
            var w = walls[f];
            walls.Remove(f); wallShapes.Remove(f);
            return w;
        }

        public void ToggleDoor(FaceId f)
        {
            var w = WallAt(f);
            if (w?.Kind != "door") throw new InvalidOperationException("No door here");
            var key = walls.ContainsKey(f) ? f : new FaceId(f.Cell - CellId.Up, f.Axis);
            var n = w.Copy(); n.Open = !n.Open;
            walls[key] = n; wallShapes.Remove(key);
        }

        // ---- rules and saves -----------------------------------------------------------------

        /// <summary>
        /// (review H1-28) What of the stairs and walls would stand on nothing (or a stair without its landings): what taking a
        /// block or a wall away may cause. Only cell and wall states, no shapes (they're cached).
        /// </summary>
        IEnumerable<string> SupportProblems(HashSet<CellId> held)
        {
            foreach (var s in stairs)
            {
                var p = StairProblem(s, s);
                if (p != null) yield return $"Stair at {s.FootCell}: {p}";
                if (!held.Contains(s.FootCell - CellId.Up)) yield return $"Stair at {s.FootCell} has no supported floor";
            }
            foreach (var (f, w) in walls.OrderBy(kv => kv.Key))
            {
                var below = new FaceId(f.Cell - CellId.Up, f.Axis);
                bool rests = WallAt(below) != null || held.Contains(f.Cell - CellId.Up) || held.Contains(f.Beyond - CellId.Up);
                if (!rests) yield return $"{w.Kind} at {f} stands on nothing";
            }
        }

        IEnumerable<string> FittingProblems(HashSet<CellId> held)
        {
            foreach (var s in stairs)
            {
                var p = StairProblem(s, s);
                if (p != null) yield return $"Stair at {s.FootCell}: {p}";
                if (!held.Contains(s.FootCell - CellId.Up)) yield return $"Stair at {s.FootCell} has no supported floor";
            }
            foreach (var (f, w) in walls.OrderBy(kv => kv.Key))
            {
                var below = new FaceId(f.Cell - CellId.Up, f.Axis);
                bool rests = WallAt(below) != null || held.Contains(f.Cell - CellId.Up) || held.Contains(f.Beyond - CellId.Up);
                if (!rests) yield return $"{w.Kind} at {f} stands on nothing";
                if (WallShape(f).Area < MinWallArea) yield return $"{w.Kind} at {f} has no room";
            }
        }

        void SaveFittings(out Dictionary<string, WallState> wallChanges, out List<StairFitting> stairList)
        {
            wallChanges = new Dictionary<string, WallState>();
            foreach (var f in designWalls.Keys.Union(walls.Keys).OrderBy(f => f))
            {
                designWalls.TryGetValue(f, out var a); walls.TryGetValue(f, out var b);
                if (a != null && a.Same(b)) continue;
                wallChanges[f.ToString()] = b;
            }
            bool same = stairs.Count == designStairs.Count && stairs.Zip(designStairs).All(p =>
                p.First.Foot.SequenceEqual(p.Second.Foot) && p.First.Dir == p.Second.Dir && p.First.Material == p.Second.Material);
            stairList = same ? null : stairs.Select(s => s.Copy()).ToList();
        }

        void LoadFittings(Dictionary<string, WallState> wallChanges, List<StairFitting> stairList)
        {
            ResetFittings();
            foreach (var (key, w) in wallChanges ?? new Dictionary<string, WallState>())
            {
                var f = FaceId.Parse(key);
                if (w == null) walls.Remove(f); else walls[f] = w;
            }
            if (stairList != null) stairs = stairList;
        }
    }
}
