using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    /// <summary>The generator's rig check (the design's "rigCheck"): her standing rigging as segments (x0 y0 z0 x1 y1 z1 radius).</summary>
    public class RigCheckDef
    {
        public double[][] Standing;
    }

    /// <summary>
    /// (0.3.14, notes 150–152: the mizzen lateen through the mizzen mast on one tack; "main sails penetrate the ratlines. How
    /// to fix it?"; the fore course through the foremast shrouds) Her sails pressed against what they meet, as cloth is: her
    /// masts (cylinders) and her standing rigging off the middle line (shrouds and backstays: <see cref="RigCheckDef"/>), with
    /// lines between each two neighbouring shrouds of a set for the ratlines between them. A sail's vertices, as it's set
    /// (furled, bellied) before its yard turns it, are worked in its own frame (<see cref="SailFrame"/>: along the yard, down
    /// it, across it), each obstacle turned back by the yard's turn into that frame: one aft of the sail's plane (n &lt; 0 there
    /// before the yard turned: a mast, the shrouds behind a square sail) keeps the cloth at least its radius and Clear on its
    /// fore side, one before it (the lateen's lee shrouds) on its after side, falling off by Slope with the distance in the
    /// sail's plane (the cloth draped round it, not creased). Each vertex is only moved across the sail, so the cloth stays
    /// on its yard and clews. Done when a sail's mesh is built again for its trim (<see cref="ShipRenderer"/>), not every frame.
    /// The middle line's stays aren't obstacles (the main topmast stay leaves the masthead behind the topsail's head).
    /// </summary>
    public sealed class SailPress
    {
        /// <summary>How far the cloth is kept off an obstacle past its radius.</summary>
        public const double Clear = 0.04;
        /// <summary>How fast the cloth falls away from an obstacle it's pressed against, across over along (a drape of about 30°).</summary>
        public const double Slope = 0.6;

        /// <summary>The obstacles: x0 y0 z0 x1 y1 z1 radius, ship-local, and what each is ("mast", "rope", "ratlines").</summary>
        public readonly List<double[]> Lines = new();
        public readonly List<string> Kinds = new();

        public static SailPress Of(ShipDefinition def)
        {
            var p = new SailPress();
            foreach (var m in def.Masts ?? Array.Empty<MastDef>())
                p.Add(new[] { m.X, m.Y0, m.Z, m.X, m.Y1, m.Z, m.Radius }, "mast");
            var standing = def.RigCheck?.Standing ?? Array.Empty<double[]>();
            var ropes = standing.Where(s => s != null && s.Length >= 7 && (Math.Abs(s[2]) > 0.2 || Math.Abs(s[5]) > 0.2)).ToList();
            foreach (var s in ropes) p.Add(s, "rope");
            // a set of shrouds: from one channel (their feet at one height, on one side) to one masthead (their heads at one
            // height): the ratlines between each two
            foreach (var set in ropes.GroupBy(s => (Math.Round(s[1], 2), Math.Round(s[4], 2), Math.Sign(s[2]), Math.Round(Math.Abs(s[5]), 1))))
            {
                var list = set.OrderBy(s => s[0]).ToList();
                for (int i = 0; i + 1 < list.Count; i++)
                    for (int k = 1; k < 4; k++)
                    {
                        double w = k / 4.0;
                        var a = list[i];
                        var b = list[i + 1];
                        var line = new double[7];
                        for (int j = 0; j < 6; j++) line[j] = a[j] + (b[j] - a[j]) * w;
                        line[6] = Math.Min(a[6], b[6]);
                        p.Add(line, "ratlines");
                    }
            }
            return p;
        }

        /// <summary>(note 64) The same obstacles mirrored across the plane z = mz (a dipping lateen's mast's): where they are in its frame on its other side.</summary>
        public SailPress Mirrored(double mz)
        {
            var p = new SailPress();
            for (int i = 0; i < Lines.Count; i++)
            {
                var l = (double[])Lines[i].Clone();
                l[2] = 2 * mz - l[2];
                l[5] = 2 * mz - l[5];
                p.Add(l, Kinds[i]);
            }
            return p;
        }

        void Add(double[] line, string kind)
        {
            Lines.Add(line);
            Kinds.Add(kind);
        }

        /// <summary>A point (ship-local) moved by the inverse of a rigid matrix (column-major, as Mat4f): where it is in the frame before the turn.</summary>
        static double[] Unturn(float[] m, double x, double y, double z)
        {
            if (m == null) return new[] { x, y, z };
            double px = x - m[12], py = y - m[13], pz = z - m[14];
            return new[]
            {
                m[0] * px + m[1] * py + m[2] * pz,
                m[4] * px + m[5] * py + m[6] * pz,
                m[8] * px + m[9] * py + m[10] * pz
            };
        }

        /// <summary>The obstacles in a sail's frame: (u, v, n) of both ends with the yard turned (by yard), and before it turned.</summary>
        List<(double[] Now, double[] Ref, double R)> InFrame(SailFrame f, float[] yard)
        {
            var list = new List<(double[], double[], double)>();
            foreach (var l in Lines)
            {
                var a = Unturn(yard, l[0], l[1], l[2]);
                var b = Unturn(yard, l[3], l[4], l[5]);
                f.ToSail(a[0], a[1], a[2], out double ua, out double va, out double na, true);
                f.ToSail(b[0], b[1], b[2], out double ub, out double vb, out double nb, true);
                f.ToSail(l[0], l[1], l[2], out double ra, out double sa, out double ta, true);
                f.ToSail(l[3], l[4], l[5], out double rb, out double sb, out double tb, true);
                // (nowhere near the sail's outline, with room for the drape: left out)
                double reach = (l[6] + Clear) * 1.2 + 3 / Slope;
                if (Math.Max(ua, ub) < f.U0 - reach || Math.Min(ua, ub) > f.U1 + reach || Math.Max(va, vb) < -0.5 - reach || Math.Min(va, vb) > f.V1 + 0.5 + reach) continue;
                list.Add((new[] { ua, va, na, ub, vb, nb }, new[] { ra, sa, ta, rb, sb, tb }, l[6]));
            }
            return list;
        }

        /// <summary>The point of a segment (in the sail's plane: u, v; with n along it) nearest (u, v): its n and how far off it is in the plane.</summary>
        static (double N, double D) Nearest(double[] s, double u, double v)
        {
            double du = s[3] - s[0], dv = s[4] - s[1], len = du * du + dv * dv;
            double t = len < 1e-12 ? 0 : Math.Clamp(((u - s[0]) * du + (v - s[1]) * dv) / len, 0, 1);
            double pu = s[0] + du * t - u, pv = s[1] + dv * t - v;
            return (s[2] + (s[5] - s[2]) * t, Math.Sqrt(pu * pu + pv * pv));
        }

        /// <summary>
        /// Press a sail's vertices (xyz: x y z each, count of them, ship-local as the sail is set before its yard turns it: furled
        /// and bellied) off the obstacles, its yard turned by yard (column-major, ship-local; null: not turned). Moved across
        /// the sail (its N) only. How many vertices moved.
        /// </summary>
        public int Press(SailFrame f, float[] yard, float[] xyz, int count)
        {
            if (f == null || xyz == null) return 0;
            var obs = InFrame(f, yard);
            if (obs.Count == 0) return 0;
            int moved = 0;
            for (int i = 0; i < count; i++)
            {
                double x = xyz[i * 3], y = xyz[i * 3 + 1], z = xyz[i * 3 + 2];
                f.ToSail(x, y, z, out double u, out double v, out double n, true);
                double lo = double.MinValue, hi = double.MaxValue;
                foreach (var (now, rf, r) in obs)
                {
                    var (nn, d) = Nearest(now, u, v);
                    // (the drape: the line of that slope touching the circle of the obstacle's radius and Clear round it, so the
                    // cloth is off it by that much in every way, not only straight across)
                    double keep = (r + Clear) * Math.Sqrt(1 + Slope * Slope) - Slope * d;
                    if (keep <= -3) continue;
                    // which side of the sail it was on before the yard turned (the flat sail's plane, n = 0)
                    var (nr, _) = Nearest(rf, u, v);
                    if (nr < 0) lo = Math.Max(lo, nn + keep);
                    else hi = Math.Min(hi, nn - keep);
                }
                double n2 = n;
                if (n2 > hi) n2 = hi;
                if (n2 < lo) n2 = lo;      // (behind one obstacle and before another: the one behind wins, a mast)
                if (Math.Abs(n2 - n) < 1e-5) continue;
                double dn = n2 - n;
                xyz[i * 3] = (float)(x + dn * f.N[0]);
                xyz[i * 3 + 1] = (float)(y + dn * f.N[1]);
                xyz[i * 3 + 2] = (float)(z + dn * f.N[2]);
                moved++;
            }
            return moved;
        }

        /// <summary>
        /// For the checks: how deep the deepest of these points (ship-local, x y z each, as drawn: the yard turned) is inside a
        /// mast or a rope (its radius less its distance from the segment), and how many are; and how many are on the wrong side
        /// of a set's ratlines (in the sail's frame, within 0.05 of a line between two shrouds in the sail's plane, on the far
        /// side of it from where the sail was before the yard turned). yard: the yard's matrix (to find the sail's frame).
        /// </summary>
        public (double Deepest, int Inside, int Through, string Where) Check(SailFrame f, float[] yard, float[] drawn, int count)
        {
            double deepest = 0;
            int inside = 0, through = 0;
            string where = "";
            var obs = InFrame(f, yard);
            for (int i = 0; i < count; i++)
            {
                double x = drawn[i * 3], y = drawn[i * 3 + 1], z = drawn[i * 3 + 2];
                for (int k = 0; k < Lines.Count; k++)
                {
                    var l = Lines[k];
                    double dx = l[3] - l[0], dy = l[4] - l[1], dz = l[5] - l[2], len = dx * dx + dy * dy + dz * dz;
                    double t = len < 1e-12 ? 0 : Math.Clamp(((x - l[0]) * dx + (y - l[1]) * dy + (z - l[2]) * dz) / len, 0, 1);
                    double px = l[0] + dx * t - x, py = l[1] + dy * t - y, pz = l[2] + dz * t - z;
                    double depth = l[6] - Math.Sqrt(px * px + py * py + pz * pz);
                    if (depth > 0.005) { inside++; if (depth > deepest) { deepest = depth; where = $"{Kinds[k]} at ({x:0.00}, {y:0.00}, {z:0.00})"; } }
                }
                // the frame before the turn: the point there
                var q = Unturn(yard, x, y, z);
                f.ToSail(q[0], q[1], q[2], out double u, out double v, out double n, true);
                foreach (var (now, rf, r) in obs)
                {
                    var (nn, d) = Nearest(now, u, v);
                    if (d > 0.05) continue;
                    var (nr, _) = Nearest(rf, u, v);
                    if (nr < 0 ? n < nn - 1e-3 : n > nn + 1e-3) through++;
                }
            }
            return (deepest, inside, through, where);
        }
    }
}
