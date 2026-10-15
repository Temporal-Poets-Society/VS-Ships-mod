using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hansa.Construction
{
    /// <summary>An authored ship: hull form, structure positions and the default block map.
    /// A few KB of data; everything else is derived at load (docs/proposals/ship-design-architecture.md).</summary>
    public sealed class ShipDesign
    {
        public int Schema;
        public string Code, Name;
        public DesignGrid Grid = new();
        public HullFormSpec Form;
        public KeelSpec Keel;
        public FrameSpec Frames;
        public DeckSpec[] Decks = Array.Empty<DeckSpec>();
        public double BeamDepth = 0.5, Waterline;
        /// <summary>Default planks for wood blocks and glass for window panes.</summary>
        public string Material = "game:planks-oak-ud", Glass = "game:glasspane-plain-ew";
        /// <summary>How far, in cells along the keel, planking may reach from its nearest frame.</summary>
        public int PlankSpan = 2;
        /// <summary>Cells whose whole share of the compiled structure is smaller than this join
        /// the neighbour they were cut from, so no block is a sliver.</summary>
        public double MinBlockVolume = 0.04;
        public JArray Layout = new();

        public static ShipDesign Read(string json)
        {
            var d = JsonConvert.DeserializeObject<ShipDesign>(json) ?? throw new ArgumentException("Empty ship design");
            if (d.Schema != 1) throw new ArgumentException("Unsupported ship design schema");
            return d;
        }
    }

    public sealed class DesignGrid
    {
        /// <summary>Corner of cell (0,0,0). The default puts the centreline through the middle of z-cell 0.</summary>
        public double[] Origin = { 0, 0, -0.5 };
        public double CollisionVoxel = 0.25;
    }

    public sealed class HullFormSpec
    {
        /// <summary>Every section corner lies on this sub-grid (the faceted look).</summary>
        public double CornerGrid = 0.5;
        /// <summary>Allowed section slopes as [rise, run] pairs, in either direction across.</summary>
        public double[][] Slopes;
        public double Skin = 0.25;
        public StationSpec[] Stations;
    }

    public sealed class StationSpec
    {
        public double X;
        /// <summary>Outer half-section from the keel centreline up to the sheer: [y, half-width] pairs.</summary>
        public double[][] Section;
    }

    public sealed class KeelSpec { public double From, To, Top, Width = 1, Height = 1; }

    public sealed class FrameSpec
    {
        /// <summary>Rib cells along x. Each owns one whole cell; visible timber is aesthetic.</summary>
        public int[] Cells;
        /// <summary>Allowed counts of clear cells between neighbouring ribs.</summary>
        public int[] ClearGaps = { 1, 3 };
        public double VisibleWidth = 0.375, Depth = 0.5;
    }

    public sealed class DeckSpec
    {
        public string Code;
        public int Top;
        public double Thickness = 0.25;
        public double? From, To;
    }

    /// <summary>The hull form evaluated as straight-line facets. Sections between stations are
    /// interpolated corner by corner, so every station needs the same number of corners.</summary>
    public sealed class HullForm
    {
        public readonly HullFormSpec Spec;
        public readonly double XMin, XMax;
        readonly StationSpec[] stations;

        public HullForm(HullFormSpec spec)
        {
            Spec = spec;
            stations = spec.Stations.OrderBy(s => s.X).ToArray();
            XMin = stations[0].X; XMax = stations[^1].X;
        }

        public IEnumerable<double> StationXs => stations.Select(s => s.X);

        public IEnumerable<string> Problems()
        {
            if (stations.Length < 2) { yield return "The form needs at least two stations"; yield break; }
            int count = stations[0].Section.Length;
            double g = Spec.CornerGrid;
            foreach (var s in stations)
            {
                if (s.Section.Length != count) yield return $"Station {s.X} has {s.Section.Length} corners, expected {count}";
                if (!OnGrid(s.X, g)) yield return $"Station {s.X} is off the {g} corner grid";
                if (s.Section.Length < 2 || s.Section[0][1] != 0) yield return $"Station {s.X} must start on the centreline";
                foreach (var p in s.Section)
                    if (!OnGrid(p[0], g) || !OnGrid(p[1], g)) yield return $"Station {s.X} corner [{p[0]}, {p[1]}] is off the {g} corner grid";
                for (int i = 0; i + 1 < s.Section.Length; i++)
                {
                    double dy = s.Section[i + 1][0] - s.Section[i][0], dw = s.Section[i + 1][1] - s.Section[i][1];
                    if (Math.Abs(dy) < 1e-9 && Math.Abs(dw) < 1e-9) continue;
                    if (dy < -1e-9 || Math.Abs(dy) < 1e-9 && dw < 0)
                        yield return $"Station {s.X} folds back between corners {i} and {i + 1}";
                    else if (!Spec.Slopes.Any(sl => Math.Abs(Math.Abs(dy) * sl[1] - Math.Abs(dw) * sl[0]) < 1e-9))
                        yield return $"Station {s.X} segment {i} has slope {dy}:{Math.Abs(dw)}, not in the slope set";
                }
            }
        }

        static bool OnGrid(double v, double g) => Math.Abs(v / g - Math.Round(v / g)) < 1e-9;

        /// <summary>Outer half-section at x: [y, w] corners.</summary>
        public double[][] SectionAt(double x)
        {
            int i = 0;
            while (i < stations.Length - 2 && x > stations[i + 1].X) i++;
            var a = stations[i]; var b = stations[i + 1];
            double t = Math.Clamp((x - a.X) / (b.X - a.X), 0, 1);
            return a.Section.Select((p, k) => new[] { p[0] + (b.Section[k][0] - p[0]) * t, p[1] + (b.Section[k][1] - p[1]) * t }).ToArray();
        }

        /// <summary>The section moved inward by a constant distance, with mitred corners.</summary>
        public static double[][] Offset(double[][] section, double distance)
        {
            if (distance == 0) return section;
            int n = section.Length;
            var normals = new (double y, double w)?[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                double dy = section[i + 1][0] - section[i][0], dw = section[i + 1][1] - section[i][1];
                double len = Math.Sqrt(dy * dy + dw * dw);
                if (len > 1e-9) normals[i] = (-dw / len, dy / len);
            }
            var result = new double[n][];
            for (int i = 0; i < n; i++)
            {
                (double y, double w)? before = null, after = null;
                for (int k = i - 1; k >= 0 && before == null; k--) before = normals[k];
                for (int k = i; k < n - 1 && after == null; k++) after = normals[k];
                // The centreline corner mirrors the opposite side, keeping the two halves symmetric.
                if (before == null && after != null && Math.Abs(section[i][1]) < 1e-9) before = (after.Value.y, -after.Value.w);
                var a = before ?? after.Value; var b = after ?? before.Value;
                double dot = a.y * b.y + a.w * b.w;
                double my = (a.y + b.y) / (1 + dot), mw = (a.w + b.w) / (1 + dot);
                result[i] = new[] { section[i][0] - my * distance, Math.Max(0, section[i][1] - mw * distance) };
            }
            return result;
        }

        /// <summary>Half-width of a section at height y; the widest crossing if several.</summary>
        public static double HalfWidth(double[][] section, double y)
        {
            double best = double.NaN;
            for (int i = 0; i + 1 < section.Length; i++)
            {
                double ya = section[i][0], yb = section[i + 1][0];
                if (y < Math.Min(ya, yb) - 1e-9 || y > Math.Max(ya, yb) + 1e-9) continue;
                double w = Math.Abs(yb - ya) < 1e-9 ? Math.Max(section[i][1], section[i + 1][1])
                    : section[i][1] + (section[i + 1][1] - section[i][1]) * (y - ya) / (yb - ya);
                if (double.IsNaN(best) || w > best) best = w;
            }
            return best;
        }

        public double[] StripBreaks(double x0, double x1) =>
            new[] { x0 }.Concat(stations.Select(s => s.X).Where(x => x > x0 + 1e-9 && x < x1 - 1e-9)).Append(x1).ToArray();

        /// <summary>The layer between two inward offsets of the hull surface, for both sides, from
        /// x0 to x1, as tetrahedra. Skin is offsets (0, skin); a rib is (skin, skin + depth).</summary>
        public IEnumerable<Convex> Band(double x0, double x1, double off0, double off1)
        {
            x0 = Math.Max(x0, XMin); x1 = Math.Min(x1, XMax);
            if (x1 - x0 < 1e-9) yield break;
            var xs = StripBreaks(x0, x1);
            for (int s = 0; s + 1 < xs.Length; s++)
            {
                double xa = xs[s], xb = xs[s + 1];
                double[][] secA = SectionAt(xa), secB = SectionAt(xb);
                double[][] oa = Offset(secA, off0), ia = Offset(secA, off1), ob = Offset(secB, off0), ib = Offset(secB, off1);
                foreach (int side in new[] { 1, -1 })
                {
                    V3 P(double x, double[] p) => new(x, p[0], side * p[1]);
                    for (int i = 0; i + 1 < oa.Length; i++)
                    {
                        var v = new[] { P(xa, oa[i]), P(xa, oa[i + 1]), P(xb, ob[i + 1]), P(xb, ob[i]),
                                        P(xa, ia[i]), P(xa, ia[i + 1]), P(xb, ib[i + 1]), P(xb, ib[i]) };
                        foreach (var t in Convex.Hexahedron(v)) yield return t;
                    }
                }
            }
        }

        /// <summary>A horizontal slab y0..y1 spanning between the hull's inner surfaces.</summary>
        public IEnumerable<Convex> Slab(double x0, double x1, double y0, double y1, double inset)
        {
            x0 = Math.Max(x0, XMin); x1 = Math.Min(x1, XMax);
            if (x1 - x0 < 1e-9) yield break;
            var xs = StripBreaks(x0, x1);
            for (int s = 0; s + 1 < xs.Length; s++)
            {
                double xa = xs[s], xb = xs[s + 1];
                var ia = Offset(SectionAt(xa), inset); var ib = Offset(SectionAt(xb), inset);
                double ta = HalfWidth(ia, y1), la = HalfWidth(ia, y0), tb = HalfWidth(ib, y1), lb = HalfWidth(ib, y0);
                if (new[] { ta, la, tb, lb }.Any(double.IsNaN)) continue;
                var v = new[] { new V3(xa, y1, -ta), new V3(xa, y1, ta), new V3(xb, y1, tb), new V3(xb, y1, -tb),
                                new V3(xa, y0, -la), new V3(xa, y0, la), new V3(xb, y0, lb), new V3(xb, y0, -lb) };
                foreach (var t in Convex.Hexahedron(v)) yield return t;
            }
        }

        /// <summary>True when a point lies inside the hull's inner surface (usable space).</summary>
        public bool Inside(V3 p, double inset)
        {
            if (p.X < XMin || p.X > XMax) return false;
            var sec = Offset(SectionAt(p.X), inset);
            if (p.Y < sec[0][0] || p.Y > sec[^1][0]) return false;
            double w = HalfWidth(sec, p.Y);
            return !double.IsNaN(w) && Math.Abs(p.Z) < w;
        }
    }
}
