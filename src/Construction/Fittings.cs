using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa.Construction
{
    /// <summary>A vertical cell face: the face on the + side of Cell along Axis (0 = x, 2 = z),
    /// between Cell and Cell + 1 on that axis. Walls and doors are addressed by faces, so a wall
    /// between two cells takes neither cell.</summary>
    public readonly struct FaceId : IEquatable<FaceId>, IComparable<FaceId>
    {
        public readonly CellId Cell;
        public readonly int Axis;
        public FaceId(CellId cell, int axis)
        {
            if (axis != 0 && axis != 2) throw new ArgumentException("Walls stand on x or z faces");
            Cell = cell; Axis = axis;
        }
        /// <summary>The face of a cell on the given side ("+x", "-x", "+z", "-z").</summary>
        public static FaceId Of(CellId c, string side)
        {
            var d = CellId.Dir(side);
            int axis = d.X != 0 ? 0 : 2;
            return d.X + d.Z > 0 ? new FaceId(c, axis) : new FaceId(c - d, axis);
        }
        public CellId Beyond => Cell + (Axis == 0 ? new CellId(1, 0, 0) : new CellId(0, 0, 1));
        public FaceId Up => new(Cell + CellId.Up, Axis);
        public bool Equals(FaceId o) => Cell == o.Cell && Axis == o.Axis;
        public override bool Equals(object o) => o is FaceId f && Equals(f);
        public override int GetHashCode() => HashCode.Combine(Cell, Axis);
        public int CompareTo(FaceId o) => Cell != o.Cell ? Cell.CompareTo(o.Cell) : Axis.CompareTo(o.Axis);
        public static bool operator ==(FaceId a, FaceId b) => a.Equals(b);
        public static bool operator !=(FaceId a, FaceId b) => !a.Equals(b);
        public override string ToString() => $"{Cell}{(Axis == 0 ? "+x" : "+z")}";
        public static FaceId Parse(string s) => new(CellId.Parse(s[..^2]), s[^1] == 'x' ? 0 : 2);
    }

    public sealed class WallState
    {
        /// <summary>"wall" (one row) or "door" (this row and the one above).</summary>
        public string Kind = "wall";
        public string Material;
        public bool Open;
        public WallState Copy() => (WallState)MemberwiseClone();
        public bool Same(WallState o) => o != null && Kind == o.Kind && Material == o.Material && Open == o.Open;
    }

    /// <summary>A companion ladder-stair (Magelan's final form, docs/legacy/modular-pieces.md §7.1):
    /// feet in the middle of the foot cell, leaning up to the far edge of the head cell at the deck
    /// above, a run of 1.5 for any rise. Climbed like a ladder; nobody walks its treads.</summary>
    public sealed class StairFitting
    {
        public int[] Foot;
        public string Dir;
        public string Material;
        public CellId FootCell => CellId.From(Foot);
        public CellId Head => FootCell + CellId.Dir(Dir);
        public StairFitting Copy() => new() { Foot = (int[])Foot.Clone(), Dir = Dir, Material = Material };
    }

    /// <summary>What an engine adapter needs to climb a ladder-stair: the line its climber follows.</summary>
    public readonly struct LadderLine
    {
        public readonly V3 Bottom, Top, Dir;
        public LadderLine(V3 bottom, V3 top, V3 dir) { Bottom = bottom; Top = top; Dir = dir; }
    }

    public static class FittingShapes
    {
        public const double Board = 0.125, DoorWidthMargin = 0.125, DoorHeight = 1.875;

        /// <summary>A vertical board on a face, rows y0..y1 (absolute heights), trimmed to the hull's
        /// inner surface section by section. Pieces share their cut faces, which cancel when meshed.</summary>
        public static List<Convex> Trimmed(ShipLattice l, Convex board, double xSection)
        {
            var result = new List<Convex>();
            if (board == null) return result;
            var sec = HullForm.Offset(l.Form.SectionAt(Math.Clamp(xSection, l.Form.XMin, l.Form.XMax)), l.Design.Form.Skin);
            for (int i = 0; i + 1 < sec.Length; i++)
            {
                double ya = sec[i][0], yb = sec[i + 1][0], wa = sec[i][1], wb = sec[i + 1][1];
                if (yb - ya < 1e-9) continue;
                double m = (wb - wa) / (yb - ya), b = wa - m * ya;
                var piece = board.Clip(1, ya, -1)?.Clip(1, yb, 1)
                    ?.ClipPlane(new V3(0, -m, 1), b)?.ClipPlane(new V3(0, -m, -1), b);
                if (piece != null) result.Add(piece);
            }
            return result;
        }

        /// <summary>The top of a wall in this row: under any deck or beam of the two cells beside it
        /// that reaches the wall's plane. A beam in the middle of a rib cell does not.</summary>
        public static double WallTop(ShipLattice l, CellId a, CellId b, int axis, double plane)
        {
            double top = l.Bounds(a).hi.Y;
            foreach (var c in new[] { a, b })
                if (l.Cells.TryGetValue(c, out var cell))
                    foreach (var p in cell.Pieces.Where(p => p.Part is Parts.Deck or Parts.Beam))
                    {
                        var (lo, hi) = p.Solid.Bounds();
                        if (lo[axis] < plane + Board / 2 && hi[axis] > plane - Board / 2) top = Math.Min(top, lo.Y);
                    }
            return top;
        }

        static (V3 lo, V3 hi) FaceSpan(ShipLattice l, FaceId f, int rows)
        {
            var (lo, hi) = l.Bounds(f.Cell);
            double plane = hi[f.Axis];
            double top = WallTop(l, f.Cell + new CellId(0, rows - 1, 0), f.Beyond + new CellId(0, rows - 1, 0), f.Axis, plane);
            return (lo.With(f.Axis, plane).With(1, lo.Y), hi.With(f.Axis, plane).With(1, top));
        }

        static Convex Slab(V3 lo, V3 hi, int axis, double plane, double from, double to) =>
            Convex.Box(lo.With(axis, plane + from), hi.With(axis, plane + to));

        /// <summary>Drawn boards and their collision (fattened to one voxel on the Cell side).</summary>
        public static (List<(string part, Convex solid)> draw, List<Convex> collide) Wall(ShipLattice l, FaceId f, WallState w)
        {
            var draw = new List<(string, Convex)>();
            var collide = new List<Convex>();
            int rows = w.Kind == "door" ? 2 : 1;
            var (lo, hi) = FaceSpan(l, f, rows);
            int a = f.Axis, u = 2 - a;
            double plane = lo[a], xs = a == 0 ? plane : (lo.X + hi.X) / 2;
            void Add(V3 blo, V3 bhi, string part)
            {
                foreach (var p in Trimmed(l, Slab(blo, bhi, a, plane, -Board / 2, Board / 2), xs)) draw.Add((part, p));
                foreach (var p in Trimmed(l, Slab(blo, bhi, a, plane, -l.Voxel, 0), xs)) collide.Add(p);
            }
            if (w.Kind != "door") { Add(lo, hi, "wall"); return (draw, collide); }

            double h0 = lo[u] + DoorWidthMargin, h1 = hi[u] - DoorWidthMargin, head = lo.Y + DoorHeight;
            Add(lo, hi.With(u, h0), "wall");
            Add(lo.With(u, h1), hi, "wall");
            if (hi.Y > head) Add(lo.With(u, h0).With(1, head), hi.With(u, h1), "wall");
            var leafTop = Math.Min(head, hi.Y);
            if (!w.Open) Add(lo.With(u, h0), hi.With(u, h1).With(1, leafTop), "door");
            else
            {
                // Swung open into the Cell side about the hinge at h0, standing square to the wall.
                var leafLo = lo.With(u, h0).With(a, plane - (h1 - h0));
                var leafHi = hi.With(u, h0 + Board / 2).With(1, leafTop).With(a, plane);
                var leaf = Convex.Box(leafLo, leafHi);
                draw.Add(("door", leaf));
                collide.Add(Convex.Box(leafLo, leafHi.With(u, h0 + l.Voxel)));
            }
            return (draw, collide);
        }

        public static int Rise(ShipLattice l, StairFitting s)
        {
            int y0 = s.FootCell.Y;
            double floor = l.Origin.Y + y0;
            var above = l.Design.Decks.Select(d => d.Top).Where(t => t > floor + 1e-9).DefaultIfEmpty(int.MinValue).Min();
            return above == int.MinValue ? 0 : (int)Math.Round(above - floor);
        }

        /// <summary>Cells the stair stands in: the foot cell up to the deck above, the head cell from
        /// one row up (a chest fits under its high end). The top row of both is the 2 x 1 well.</summary>
        public static IEnumerable<CellId> Occupied(ShipLattice l, StairFitting s)
        {
            int rise = Rise(l, s);
            for (int r = 0; r < rise; r++) yield return s.FootCell + new CellId(0, r, 0);
            for (int r = 1; r < rise; r++) yield return s.Head + new CellId(0, r, 0);
        }

        public static LadderLine Ladder(ShipLattice l, StairFitting s)
        {
            var dir = CellId.Dir(s.Dir);
            int a = dir.X != 0 ? 0 : 2, b = 2 - a, sign = dir.X + dir.Z;
            var (lo, hi) = l.Bounds(s.FootCell);
            double near = sign > 0 ? lo[a] : hi[a];
            double cross = (lo[b] + hi[b]) / 2;
            var dv = V3.Axis(a, sign);
            V3 At(double run, double y) => new V3(0, y, 0).With(a, near + sign * run).With(b, cross);
            return new LadderLine(At(0.5, lo.Y), At(2.0, lo.Y + Rise(l, s)), dv);
        }

        /// <summary>Two stringers whose undersides are one straight line, and flat treads about 0.3 apart.</summary>
        public static List<(string part, Convex solid)> Stair(ShipLattice l, StairFitting s)
        {
            var parts = new List<(string, Convex)>();
            var dir = CellId.Dir(s.Dir);
            int a = dir.X != 0 ? 0 : 2, b = 2 - a, sign = dir.X + dir.Z;
            var (lo, hi) = l.Bounds(s.FootCell);
            double near = sign > 0 ? lo[a] : hi[a], y0 = lo.Y;
            int rise = Rise(l, s);
            double top = y0 + rise, k = rise / 1.5, depth = 0.25;
            V3 P(double run, double y, double c) => new V3(0, y, 0).With(a, near + sign * run).With(b, c);
            const double inset = 1.0 / 16, width = 1.0 / 16;
            foreach (var (c0, c1) in new[] { (lo[b] + inset, lo[b] + inset + width), (hi[b] - inset - width, hi[b] - inset) })
            {
                // Lower edge from the feet (run 0.5, deck) to the coaming (run 2, deck above).
                V3[] Q(double c) => new[] { P(0.5, y0, c), P(2.0, top, c), P(2.0, top + depth, c), P(0.5, y0 + depth, c) };
                var q0 = Q(c0); var q1 = Q(c1);
                var stringer = Convex.Outward(new[]
                {
                    q0, q1, new[] { q0[0], q0[1], q1[1], q1[0] }, new[] { q0[1], q0[2], q1[2], q1[1] },
                    new[] { q0[2], q0[3], q1[3], q1[2] }, new[] { q0[3], q0[0], q1[0], q1[3] },
                })?.Clip(1, top, 1);
                if (stringer != null) parts.Add(("stringer", stringer));
            }
            double t0 = lo[b] + inset + width, t1 = hi[b] - inset - width;
            for (double h = y0 + 0.3; h < top - 0.15; h += 0.3)
            {
                double run = 0.5 + (h - y0 - 0.1) / k;
                double r0 = run - 0.125, r1 = run + 0.125;
                var p0 = P(r0, h - 1.0 / 16, t0); var p1 = P(r1, h, t1);
                parts.Add(("tread", Convex.Box(new V3(Math.Min(p0.X, p1.X), p0.Y, Math.Min(p0.Z, p1.Z)), new V3(Math.Max(p0.X, p1.X), p1.Y, Math.Max(p0.Z, p1.Z)))));
            }
            return parts;
        }
    }
}
