using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa.Construction
{
    /// <summary>Integer address of a ship-local block cell. Saves and edits refer to cells by this.</summary>
    public readonly struct CellId : IEquatable<CellId>, IComparable<CellId>
    {
        public readonly int X, Y, Z;
        public CellId(int x, int y, int z) { X = x; Y = y; Z = z; }
        public static CellId operator +(CellId a, CellId b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static CellId operator -(CellId a, CellId b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static readonly CellId Up = new(0, 1, 0);
        public static readonly CellId[] Sides = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        public static CellId Dir(string facing) => facing switch
        {
            "+x" => new(1, 0, 0), "-x" => new(-1, 0, 0), "+z" => new(0, 0, 1), "-z" => new(0, 0, -1),
            _ => throw new ArgumentException($"Facing must be +x, -x, +z or -z, not {facing}"),
        };
        public static CellId From(int[] a) => a?.Length == 3 ? new(a[0], a[1], a[2]) : throw new ArgumentException("A cell needs three coordinates");
        public static CellId Parse(string s)
        {
            var p = s.Split(':');
            return p.Length == 3 ? new(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2])) : throw new ArgumentException($"Bad cell address {s}");
        }
        public bool Equals(CellId o) => X == o.X && Y == o.Y && Z == o.Z;
        public static bool operator ==(CellId a, CellId b) => a.Equals(b);
        public static bool operator !=(CellId a, CellId b) => !a.Equals(b);
        public override bool Equals(object o) => o is CellId c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public int CompareTo(CellId o) => X != o.X ? X.CompareTo(o.X) : Y != o.Y ? Y.CompareTo(o.Y) : Z.CompareTo(o.Z);
        public override string ToString() => $"{X}:{Y}:{Z}";
    }

    public sealed class Piece
    {
        public string Part;
        public Convex Solid;
    }

    /// <summary>What a cell's location contains, derived from the form and structure positions.
    /// Independent of what block currently stands there.</summary>
    public sealed class LatticeCell
    {
        public CellId Id;
        public readonly List<Piece> Pieces = new();
        public string SlotKey, Lead;
        public double Volume => Pieces.Sum(p => p.Solid.Volume());
    }

    /// <summary>The compiled cell model of a design. Pure data; no game types.</summary>
    public sealed class ShipLattice
    {
        public readonly ShipDesign Design;
        public readonly HullForm Form;
        public readonly V3 Origin;
        public readonly double Voxel;
        public readonly Dictionary<CellId, LatticeCell> Cells = new();
        public readonly List<string> Problems = new();
        /// <summary>Height of vanilla still water within its block.</summary>
        public const double StillWater = 0.875;
        /// <summary>Cells too small to be blocks, whose structure joined a neighbour.</summary>
        public readonly List<CellId> Merged = new();
        readonly Dictionary<(int, int, int), CellId> voxelOwner = new();
        /// <summary>The lowest planking on the centreline: the keel and, where the bottom rises,
        /// the stem and sternpost carrying it on.</summary>
        public readonly HashSet<CellId> Backbone = new();
        /// <summary>Cells of planking laid across the hull: transom, stem plank and castle fronts.</summary>
        public readonly HashSet<CellId> Across = new();

        public ShipLattice(ShipDesign design)
        {
            Design = design;
            Origin = new V3(design.Grid.Origin[0], design.Grid.Origin[1], design.Grid.Origin[2]);
            Voxel = design.Grid.CollisionVoxel;
            Form = new HullForm(design.Form);
            Problems.AddRange(Form.Problems());
            Problems.AddRange(StructureProblems());
            if (Problems.Count > 0) return;

            foreach (var t in Form.Band(Form.XMin, Form.XMax, 0, design.Form.Skin)) Add(Parts.Skin, t);
            double StepFloor(double x, double low) => design.Decks
                .Where(d => (d.From ?? Form.XMin) <= x + 1e-9 && (d.To ?? Form.XMax) >= x - 1e-9 && d.Top <= low + 1e-9)
                .Select(d => (double)d.Top).DefaultIfEmpty(low).Max();
            foreach (var t in Form.Caps(design.Form.Skin, StepFloor)) foreach (var c in Add(Parts.Skin, t)) Across.Add(c);
            double half = design.Frames.VisibleWidth / 2, skin = design.Form.Skin;
            foreach (int r in design.Frames.Cells)
            {
                double c = Origin.X + r + 0.5;
                foreach (var t in Form.Band(c - half, c + half, skin, skin + design.Frames.Depth, design.Frames.Bottom))
                    Add(Parts.Rib, design.Frames.Bottom is double b ? t.Clip(1, b, -1) : t);
            }
            var k = design.Keel;
            if (k != null) Add(Parts.Keel, Convex.Box(new V3(k.From, k.Top - k.Height, -k.Width / 2), new V3(k.To, k.Top, k.Width / 2)));
            // Decks and beams stop against the rib timber where a rib stands, instead of overlapping it.
            double framed = skin + design.Frames.Depth;
            var ribSpans = design.Frames.Cells.Select(r => (lo: Origin.X + r + 0.5 - half, hi: Origin.X + r + 0.5 + half)).ToArray();
            foreach (var d in design.Decks)
            {
                double from = d.From ?? Form.XMin, to = d.To ?? Form.XMax, low = d.Top - d.Thickness;
                var breaks = ribSpans.SelectMany(s => new[] { s.lo, s.hi }).Where(x => x > from && x < to).Append(from).Append(to).Distinct().OrderBy(x => x).ToArray();
                for (int i = 0; i + 1 < breaks.Length; i++)
                {
                    double a = breaks[i], b = breaks[i + 1], m = (a + b) / 2;
                    // A floor below where the ribs start (the hold floor on the bottom) meets the skin itself.
                    bool atRib = ribSpans.Any(s => m > s.lo && m < s.hi) && !(design.Frames.Bottom >= d.Top);
                    foreach (var t in Form.Slab(a, b, low, d.Top, atRib ? framed : skin)) Add(Parts.Deck, t);
                }
                foreach (var s in ribSpans)
                {
                    if (!d.Beams) continue;
                    if ((s.lo + s.hi) / 2 < from || (s.lo + s.hi) / 2 > to) continue;
                    foreach (var t in Form.Slab(s.lo, s.hi, low - design.BeamDepth, low, framed)) Add(Parts.Beam, t);
                }
            }
            MergeSlivers();
            foreach (var cell in Cells.Values)
            {
                var parts = cell.Pieces.Select(p => p.Part).Distinct().ToArray();
                cell.SlotKey = Parts.Key(parts);
                cell.Lead = Parts.Lead(parts);
            }
            AssignVoxels();
            var centre = CellAt(new V3(0, 0, 0)).Z;
            foreach (var column in Cells.Values.Where(c => c.Id.Z == centre && c.SlotKey.Split('+').Contains(Parts.Skin)).GroupBy(c => c.Id.X))
                Backbone.Add(column.OrderBy(c => c.Id.Y).First().Id);
            Across.RemoveWhere(c => !Cells.ContainsKey(c));
        }

        IEnumerable<string> StructureProblems()
        {
            var f = Design.Frames;
            if (f?.Cells == null || f.Cells.Length == 0) { yield return "The design has no rib cells"; yield break; }
            for (int i = 0; i + 1 < f.Cells.Length; i++)
            {
                int gap = f.Cells[i + 1] - f.Cells[i] - 1;
                if (!f.ClearGaps.Contains(gap)) yield return $"Ribs {f.Cells[i]} and {f.Cells[i + 1]} have {gap} clear cells between them; allowed: {string.Join(", ", f.ClearGaps)}";
            }
            if (f.VisibleWidth <= 0 || f.VisibleWidth > 1) yield return "Visible rib width must be within its one cell";
            foreach (var m in Design.Masts)
                if (!f.Cells.Contains(m.Cell - 1) || !f.Cells.Contains(m.Cell + 1) || f.Cells.Contains(m.Cell))
                    yield return $"Mast {m.Code} at cell {m.Cell} needs a rib in the cell on each side and none in its own";
            foreach (var d in Design.Decks)
                if (Math.Abs(d.Top - Origin.Y - Math.Round(d.Top - Origin.Y)) > 1e-9) yield return $"Deck {d.Code} is not on a cell boundary";
            // Vanilla still water stands 0.875 up its block. Floating at this waterline, every deck lies
            // on a world block border, level with a pier or quay.
            double water = Design.Waterline - Origin.Y;
            if (Math.Abs(water - Math.Floor(water) - StillWater) > 1e-9)
                yield return $"Waterline {Design.Waterline} must be {StillWater} above a cell boundary, as vanilla still water is";
        }

        public (V3 lo, V3 hi) Bounds(CellId c)
        {
            var lo = new V3(Origin.X + c.X, Origin.Y + c.Y, Origin.Z + c.Z);
            return (lo, lo + new V3(1, 1, 1));
        }

        public CellId CellAt(V3 p) => new((int)Math.Floor(p.X - Origin.X), (int)Math.Floor(p.Y - Origin.Y), (int)Math.Floor(p.Z - Origin.Z));

        public string SlotKey(CellId c) => Cells.TryGetValue(c, out var cell) ? cell.SlotKey : "";

        /// <summary>Open space inside the hull's inner surface, with no structure of its own.</summary>
        public bool Interior(CellId c)
        {
            if (Cells.ContainsKey(c)) return false;
            var (lo, hi) = Bounds(c);
            return Form.Inside((lo + hi) * 0.5, Design.Form.Skin);
        }

        List<CellId> Add(string part, Convex solid)
        {
            var touched = new List<CellId>();
            if (solid == null) return touched;
            var (lo, hi) = solid.Bounds();
            var a = CellAt(lo + new V3(1e-7, 1e-7, 1e-7));
            var b = CellAt(hi - new V3(1e-7, 1e-7, 1e-7));
            for (int x = a.X; x <= b.X; x++)
                for (int y = a.Y; y <= b.Y; y++)
                    for (int z = a.Z; z <= b.Z; z++)
                    {
                        var id = new CellId(x, y, z);
                        var (clo, chi) = Bounds(id);
                        var piece = solid.ClipBox(clo, chi);
                        if (piece == null) continue;
                        if (!Cells.TryGetValue(id, out var cell)) Cells[id] = cell = new LatticeCell { Id = id };
                        cell.Pieces.Add(new Piece { Part = part, Solid = piece });
                        touched.Add(id);
                    }
            return touched;
        }

        void MergeSlivers()
        {
            var small = Cells.Values.Where(c => c.Volume < Design.MinBlockVolume).OrderBy(c => c.Volume).ThenBy(c => c.Id).ToList();
            var smallIds = small.Select(c => c.Id).ToHashSet();
            foreach (var cell in small)
            {
                var (lo, hi) = Bounds(cell.Id);
                foreach (var piece in cell.Pieces.ToArray())
                {
                    CellId? best = null; double bestArea = 0;
                    foreach (var face in piece.Solid.Faces)
                        for (int axis = 0; axis < 3; axis++)
                            foreach (var (bound, step) in new[] { (lo[axis], -1), (hi[axis], 1) })
                            {
                                if (!face.All(p => Math.Abs(p[axis] - bound) < 1e-7)) continue;
                                var n = cell.Id + new CellId(axis == 0 ? step : 0, axis == 1 ? step : 0, axis == 2 ? step : 0);
                                double area = Convex.Area(face);
                                if (Cells.ContainsKey(n) && !smallIds.Contains(n) && area > bestArea) { best = n; bestArea = area; }
                            }
                    if (best == null) continue;
                    cell.Pieces.Remove(piece);
                    Cells[best.Value].Pieces.Add(piece);
                }
                if (cell.Pieces.Count == 0) { Cells.Remove(cell.Id); Merged.Add(cell.Id); }
            }
        }

        public (int, int, int) VoxelAt(V3 p) =>
            ((int)Math.Floor((p.X - Origin.X) / Voxel + 1e-9), (int)Math.Floor((p.Y - Origin.Y) / Voxel + 1e-9), (int)Math.Floor((p.Z - Origin.Z) / Voxel + 1e-9));

        public V3 VoxelLo((int x, int y, int z) v) => new(Origin.X + v.x * Voxel, Origin.Y + v.y * Voxel, Origin.Z + v.z * Voxel);

        /// <summary>Collision voxels touched by a solid: any of nine sample points inside it.
        /// Thin sloped skin still closes, because samples sit a quarter voxel apart.</summary>
        public IEnumerable<(int, int, int)> SampleVoxels(Convex solid)
        {
            var (lo, hi) = solid.Bounds();
            var a = VoxelAt(lo); var b = VoxelAt(hi - new V3(1e-9, 1e-9, 1e-9));
            for (int x = a.Item1; x <= b.Item1; x++)
                for (int y = a.Item2; y <= b.Item2; y++)
                    for (int z = a.Item3; z <= b.Item3; z++)
                    {
                        var vlo = VoxelLo((x, y, z));
                        bool hit = solid.Contains(vlo + new V3(0.5, 0.5, 0.5) * Voxel);
                        for (int s = 0; s < 8 && !hit; s++)
                            hit = solid.Contains(vlo + new V3((s & 1) == 0 ? 0.25 : 0.75, (s & 2) == 0 ? 0.25 : 0.75, (s & 4) == 0 ? 0.25 : 0.75) * Voxel);
                        if (hit) yield return (x, y, z);
                    }
        }

        /// <summary>Every structural voxel gets exactly one owning cell: the cell containing it when
        /// that cell claims it, otherwise the lowest claiming address.</summary>
        void AssignVoxels()
        {
            var claims = new Dictionary<(int, int, int), List<CellId>>();
            foreach (var cell in Cells.Values.OrderBy(c => c.Id))
                foreach (var piece in cell.Pieces)
                    foreach (var v in SampleVoxels(piece.Solid))
                    {
                        if (!claims.TryGetValue(v, out var list)) claims[v] = list = new List<CellId>();
                        if (!list.Contains(cell.Id)) list.Add(cell.Id);
                    }
            foreach (var (v, list) in claims)
            {
                var home = CellAt(VoxelLo(v) + new V3(0.5, 0.5, 0.5) * Voxel);
                voxelOwner[v] = list.Contains(home) ? home : list.Min();
            }
        }

        /// <summary>Whether this cell's block may stamp a voxel: owned by it, or unclaimed and inside it.</summary>
        public bool MayStamp(CellId c, (int, int, int) v)
        {
            if (voxelOwner.TryGetValue(v, out var owner)) return owner.Equals(c);
            return CellAt(VoxelLo(v) + new V3(0.5, 0.5, 0.5) * Voxel).Equals(c);
        }
    }
}
