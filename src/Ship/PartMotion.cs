using System;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// Where a moving part of a ship is drawn (ship-local 4x4 matrices, column-major like Mat4f): turned about its axis,
    /// slid along it, or a rope stretched between a fixed anchor and a tip that follows another part.
    /// </summary>
    public static class PartMotion
    {
        /// <summary>How far a turning part turns for this value: Angle degrees per unit, never past its Limit.</summary>
        public static float TurnDegrees(MovingPartDef part, float value)
        {
            float deg = value * part.Angle;
            return part.Limit > 0 ? Math.Clamp(deg, -part.Limit, part.Limit) : deg;
        }

        /// <summary>Moved dist blocks along the part's direction.</summary>
        public static void SlideMatrix(MovingPartDef part, float dist, float[] m)
        {
            Mat4f.Identity(m);
            if (part.AxisDir == null) return;
            m[12] = (float)part.AxisDir[0] * dist;
            m[13] = (float)part.AxisDir[1] * dist;
            m[14] = (float)part.AxisDir[2] * dist;
        }

        /// <summary>
        /// A rope from its anchor to a tip that moves with another part: turned about the anchor and stretched along its
        /// length so its tip lands where the other part took it. anchorMove (or null) moves the anchor too (a rope
        /// between two yards).
        /// </summary>
        public static void FollowMatrix(MovingPartDef part, float[] follow, float[] m, float[] anchorMove = null)
        {
            Mat4f.Identity(m);
            if (follow == null || part.Anchor == null || part.Tip == null) return;
            double ax = part.Anchor[0], ay = part.Anchor[1], az = part.Anchor[2];
            double tx = part.Tip[0], ty = part.Tip[1], tz = part.Tip[2];
            // where the followed part takes the tip, and where the anchor is now
            double nx = follow[0] * tx + follow[4] * ty + follow[8] * tz + follow[12];
            double ny = follow[1] * tx + follow[5] * ty + follow[9] * tz + follow[13];
            double nz = follow[2] * tx + follow[6] * ty + follow[10] * tz + follow[14];
            double bx = ax, by = ay, bz = az;
            if (anchorMove != null)
            {
                bx = anchorMove[0] * ax + anchorMove[4] * ay + anchorMove[8] * az + anchorMove[12];
                by = anchorMove[1] * ax + anchorMove[5] * ay + anchorMove[9] * az + anchorMove[13];
                bz = anchorMove[2] * ax + anchorMove[6] * ay + anchorMove[10] * az + anchorMove[14];
            }
            double[] u0 = { tx - ax, ty - ay, tz - az }, u1 = { nx - bx, ny - by, nz - bz };
            double l0 = Math.Sqrt(u0[0] * u0[0] + u0[1] * u0[1] + u0[2] * u0[2]);
            double l1 = Math.Sqrt(u1[0] * u1[0] + u1[1] * u1[1] + u1[2] * u1[2]);
            if (l0 < 1e-6 || l1 < 1e-6) return;
            for (int i = 0; i < 3; i++) { u0[i] /= l0; u1[i] /= l1; }
            // R turns u0 onto u1 (Rodrigues), S stretches along u0
            double[] v = { u0[1] * u1[2] - u0[2] * u1[1], u0[2] * u1[0] - u0[0] * u1[2], u0[0] * u1[1] - u0[1] * u1[0] };
            double c = u0[0] * u1[0] + u0[1] * u1[1] + u0[2] * u1[2];
            var R = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            if (c > -0.9999)
            {
                double[,] K = { { 0, -v[2], v[1] }, { v[2], 0, -v[0] }, { -v[1], v[0], 0 } };
                double k = 1 / (1 + c);
                for (int r = 0; r < 3; r++)
                    for (int q = 0; q < 3; q++)
                    {
                        double kk = 0;
                        for (int j = 0; j < 3; j++) kk += K[r, j] * K[j, q];
                        R[r, q] += K[r, q] + kk * k;
                    }
            }
            double s = l1 / l0;
            var L = new double[3, 3];
            for (int r = 0; r < 3; r++)
                for (int q = 0; q < 3; q++)
                {
                    double acc = 0;
                    for (int j = 0; j < 3; j++) acc += R[r, j] * ((j == q ? 1 : 0) + (s - 1) * u0[j] * u0[q]);
                    L[r, q] = acc;
                }
            double[] a = { ax, ay, az }, b = { bx, by, bz };
            for (int q = 0; q < 3; q++)
                for (int r = 0; r < 3; r++)
                    m[q * 4 + r] = (float)L[r, q];
            for (int r = 0; r < 3; r++) m[12 + r] = (float)(b[r] - (L[r, 0] * a[0] + L[r, 1] * a[1] + L[r, 2] * a[2]));
        }

        /// <summary>Scale everything toward the sail's yard line (its axis point and direction) by s: a sail furled to s of its drop.</summary>
        public static void FurlMatrix(SailDef sail, float s, float[] m)
        {
            double[] d = { sail.AxisDir[0], sail.AxisDir[1], sail.AxisDir[2] }, a = { sail.AxisPoint[0], sail.AxisPoint[1], sail.AxisPoint[2] };
            var S = new double[3, 3];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    S[r, c] = d[r] * d[c] + s * ((r == c ? 1 : 0) - d[r] * d[c]);
            for (int c = 0; c < 3; c++)
                for (int r = 0; r < 3; r++)
                    m[c * 4 + r] = (float)S[r, c];
            for (int r = 0; r < 3; r++)
            {
                m[3 * 4 + r] = (float)(a[r] - (S[r, 0] * a[0] + S[r, 1] * a[1] + S[r, 2] * a[2]));
                m[r * 4 + 3] = 0;
            }
            m[15] = 1;
        }

        /// <summary>
        /// A fore-and-aft sail's belly (the lateen's mesh is generated bellied to its +N side, starboard): +1 as drawn, -1
        /// mirrored through its own plane, by the sheet (radians, + the after end to starboard: the wind from port, the lee
        /// to starboard). Through the tack it passes 0: the sail shivers flat while it comes over.
        /// </summary>
        public static float LateenBelly(double sheet) => (float)Math.Clamp(sheet / (4 * Math.PI / 180), -1, 1);

        /// <summary>The fore-and-aft sail's belly b (see <see cref="LateenBelly"/>): scaled by b across its own plane (b = -1 mirrors it).</summary>
        public static void BellyMatrix(SailFrame f, float b, float[] m)
        {
            Mat4f.Identity(m);
            if (f == null) return;
            var S = new double[3, 3];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    S[r, c] = (r == c ? 1 : 0) + (b - 1) * f.N[r] * f.N[c];
            for (int c = 0; c < 3; c++)
                for (int r = 0; r < 3; r++)
                    m[c * 4 + r] = (float)S[r, c];
            for (int r = 0; r < 3; r++) m[12 + r] = (float)(f.A[r] - (S[r, 0] * f.A[0] + S[r, 1] * f.A[1] + S[r, 2] * f.A[2]));
        }

        /// <summary>How far the sails are drawn set: never quite gone, so a furled sail still shows on its yard.</summary>
        public static float DrawnSail(float sailFraction) => Math.Max(0.06f, sailFraction);

        /// <summary>A fired gun: seconds it takes to run back, stands there (the breeching taut), and is run out again by its crew.</summary>
        public const double RecoilBack = 0.18, RecoilHold = 0.6, RecoilOut = 3.5;

        /// <summary>
        /// How far a gun has run back (0 run out .. 1 all the way back) this long after it was fired: back in a fraction of
        /// a second, slowing as the breeching takes it up; there a moment; then run out again slowly, easing in and out.
        /// Never fired (or not yet): 0.
        /// </summary>
        public static float Recoil(double seconds)
        {
            if (!(seconds >= 0)) return 0;
            if (seconds < RecoilBack)
            {
                double u = 1 - seconds / RecoilBack;
                return (float)(1 - u * u);
            }
            double t = seconds - RecoilBack - RecoilHold;
            if (t <= 0) return 1;
            if (t >= RecoilOut) return 0;
            double f = t / RecoilOut;
            return (float)(1 - f * f * (3 - 2 * f));
        }

        /// <summary>
        /// A turning or sliding part's matrix (ship-local) for the value that moves it, riding on its parent if it has one (a
        /// gun's trucks and barrel on its carriage, the carriage on the turn it's trained by): mats holds the parts placed so
        /// far, and a design lists a parent before what rides on it. The renderer, the shadows and the offline checks.
        /// </summary>
        public static void Place(MovingPartDef part, float value, float[] mat, System.Collections.Generic.IDictionary<string, float[]> mats)
        {
            if (part.Kind == "slide") SlideMatrix(part, value * part.Distance, mat);
            else PartMatrix(part, TurnDegrees(part, value) * GameMath.DEG2RAD, mat);
            if (part.Parent != null && mats != null && mats.TryGetValue(part.Parent, out var pm)) Mat4f.Mul(mat, pm, mat);
        }

        /// <summary>
        /// Where a ray (ship-local, o + t d) meets a sail as it's set now: turned with its yard by the trim (radians: the
        /// brace, or the sheet for a fore-and-aft sail), furled to frac of its drop; its rolled-up bundle on the yard too.
        /// t along the ray, or -1. (The yard's turn and the furl are affine, so t is the same in the sail's own frame.)
        /// </summary>
        public static double RaySail(ShipDefinition def, SailFrame f, double trim, float frac, Vec3d o, Vec3d d, double range)
        {
            if (f == null) return -1;
            double[] po = { o.X, o.Y, o.Z }, pd = { d.X, d.Y, d.Z };
            var yard = f.Yard;
            if (yard != null && yard.AxisPoint != null && yard.AxisDir != null && Math.Abs(trim) > 1e-9)
            {
                var m = new float[16];
                PartMatrix(yard, -TurnDegrees(yard, (float)trim) * GameMath.DEG2RAD, m);
                po = new double[] { m[0] * o.X + m[4] * o.Y + m[8] * o.Z + m[12], m[1] * o.X + m[5] * o.Y + m[9] * o.Z + m[13], m[2] * o.X + m[6] * o.Y + m[10] * o.Z + m[14] };
                pd = new double[] { m[0] * d.X + m[4] * d.Y + m[8] * d.Z, m[1] * d.X + m[5] * d.Y + m[9] * d.Z, m[2] * d.X + m[6] * d.Y + m[10] * d.Z };
            }
            f.ToSail(po[0], po[1], po[2], out double u, out double v, out double n, true);
            f.ToSail(pd[0], pd[1], pd[2], out double du, out double dv, out double dn, false);
            double best = -1;
            // the bundle: the yard and the sail rolled up on it
            double tb = Slab(u, v, n, du, dv, dn, f.U0, f.U1, -0.35, 0.35, -0.35, 0.35);
            if (tb > 1e-6 && tb <= range) best = tb;
            // the sail, furled to frac of its drop (and of its belly); from inside its box (in its belly) it isn't met
            if (frac > 0.1f)
            {
                double s = 1 / frac;
                double ts = Slab(u, v * s, n * s, du, dv * s, dn * s, f.U0, f.U1, -0.3, f.V1 + 0.2, f.N0, f.N1);
                if (ts > 1e-6 && ts <= range && (best < 0 || ts < best) && f.Inside(u + du * ts, (v + dv * ts) * s, 0.4)) best = ts;
            }
            return best;
        }

        /// <summary>Where o + t d enters the box (in the same coordinates): t ≥ 0, 0 from inside, -1 if it misses.</summary>
        static double Slab(double ox, double oy, double oz, double dx, double dy, double dz, double x0, double x1, double y0, double y1, double z0, double z1)
        {
            double t0 = 0, t1 = double.MaxValue;
            if (!Clip(ox, dx, x0, x1, ref t0, ref t1) || !Clip(oy, dy, y0, y1, ref t0, ref t1) || !Clip(oz, dz, z0, z1, ref t0, ref t1)) return -1;
            return t0;
        }

        static bool Clip(double o, double d, double lo, double hi, ref double t0, ref double t1)
        {
            if (Math.Abs(d) < 1e-12) return o >= lo && o <= hi;
            double a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) (a, b) = (b, a);
            t0 = Math.Max(t0, a);
            t1 = Math.Min(t1, b);
            return t0 <= t1;
        }

        /// <summary>
        /// (0.1.0-dev.9, caravel note 61) A gate's, door's or hatch's selection box as it stands all the way open: its Sel
        /// turned (or slid) with its leaf, the one moving part whose source is "open:" + its code, as drawn; the box round
        /// its corners. Null without a Sel or without exactly one such part (two leaves share one box: left as it is).
        /// </summary>
        public static Cuboidd OpenBox(ShipDefinition def, FixtureDef f)
        {
            if (f?.Sel == null || f.Sel.Length < 6 || f.Code == null || def?.MovingParts == null) return null;
            MovingPartDef leaf = null;
            foreach (var p in def.MovingParts)
            {
                if (p?.Source != "open:" + f.Code || p.Kind == "follow") continue;
                if (leaf != null) return null;
                leaf = p;
            }
            if (leaf == null || leaf.Parent != null || (leaf.Kind != "slide" && (leaf.AxisPoint == null || leaf.AxisDir == null))) return null;
            var m = new float[16];
            Place(leaf, 1, m, null);
            double[] lo = { double.MaxValue, double.MaxValue, double.MaxValue }, hi = { double.MinValue, double.MinValue, double.MinValue };
            for (int c = 0; c < 8; c++)
            {
                double x = f.Sel[(c & 1) != 0 ? 3 : 0], y = f.Sel[(c & 2) != 0 ? 4 : 1], z = f.Sel[(c & 4) != 0 ? 5 : 2];
                double[] w =
                {
                    m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]
                };
                for (int a = 0; a < 3; a++) { lo[a] = Math.Min(lo[a], w[a]); hi[a] = Math.Max(hi[a], w[a]); }
            }
            return new Cuboidd(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2]);
        }

        /// <summary>Rotation by angle about the part's axis (through its axis point), ship-local.</summary>
        public static void PartMatrix(MovingPartDef part, float angle, float[] m)
        {
            float px = (float)part.AxisPoint[0], py = (float)part.AxisPoint[1], pz = (float)part.AxisPoint[2];
            Mat4f.Identity(m);
            Mat4f.Translate(m, m, px, py, pz);
            Mat4f.Rotate(m, m, angle, new[] { (float)part.AxisDir[0], (float)part.AxisDir[1], (float)part.AxisDir[2] });
            Mat4f.Translate(m, m, -px, -py, -pz);
        }
    }

    /// <summary>
    /// A sail's own frame and outline, as it's set full and not turned (ship-local): A its axis point, U along its yard,
    /// N its normal (a square sail's forward side; the side a fore-and-aft sail's mesh is bellied to), V in its plane away
    /// from the yard toward its foot. Its outline from its clews (the tips of the sheets that follow it): a square sail
    /// from yardarm to yardarm down to its clews; a fore-and-aft sail (one whose yard is turned by the sheet) a triangle,
    /// its yard from A up along U, its clew below. For looking at a sail, a shot meeting it, and the lateen's belly.
    /// </summary>
    public sealed class SailFrame
    {
        public double[] A, U, V, N;
        /// <summary>Along the yard (U), down the sail (0 at the yard .. V1), across it (the belly: N0 .. N1).</summary>
        public double U0, U1, V1, N0, N1;
        public bool ForeAndAft;
        /// <summary>A fore-and-aft sail's clew (U, V) and the length of its yard from A.</summary>
        public double ClewU, ClewV, YardLength;
        /// <summary>The moving part its yard is (yard-&lt;code&gt;), or null.</summary>
        public MovingPartDef Yard;

        public static SailFrame Of(ShipDefinition def, SailDef sail, double belly)
        {
            if (sail?.AxisPoint == null || sail.AxisDir == null) return null;
            var f = new SailFrame { A = (double[])sail.AxisPoint.Clone() };
            f.U = Norm(sail.AxisDir);
            f.Yard = Array.Find(def.MovingParts ?? Array.Empty<MovingPartDef>(), p => p.Code == "yard-" + sail.Code);
            f.ForeAndAft = f.Yard != null ? f.Yard.Source == "sheet" : sail.Mesh != null;
            var clews = new System.Collections.Generic.List<double[]>();
            foreach (var p in def.MovingParts ?? Array.Empty<MovingPartDef>())
                if (p.Sail == sail.Code && p.Tip != null && p.Tip.Length == 3) clews.Add(new[] { p.Tip[0] - f.A[0], p.Tip[1] - f.A[1], p.Tip[2] - f.A[2] });
            double area = Math.Max(1, sail.Area), w = Math.Sqrt(area * 1.4);
            // no sheets: a square sail hanging straight down from its yard, as deep as its area makes it
            if (clews.Count == 0)
            {
                double[] down = { f.U[1] * f.U[0], -1 + f.U[1] * f.U[1], f.U[1] * f.U[2] };
                double l = Math.Sqrt(down[0] * down[0] + down[1] * down[1] + down[2] * down[2]);
                double drop = area / w;
                clews.Add(l > 1e-6 ? new[] { down[0] / l * drop, down[1] / l * drop, down[2] / l * drop } : new[] { 0, -drop, 0.0 });
            }
            double[] mean = new double[3];
            foreach (var c in clews) for (int i = 0; i < 3; i++) mean[i] += c[i] / clews.Count;
            double along = Dot(mean, f.U);
            double[] perp = { mean[0] - along * f.U[0], mean[1] - along * f.U[1], mean[2] - along * f.U[2] };
            f.N = Norm(Cross(f.U, perp));
            f.V = Cross(f.N, f.U);
            double umin = double.MaxValue, umax = double.MinValue, vmax = 0;
            foreach (var c in clews)
            {
                umin = Math.Min(umin, Dot(c, f.U));
                umax = Math.Max(umax, Dot(c, f.U));
                vmax = Math.Max(vmax, Dot(c, f.V));
            }
            f.V1 = Math.Max(0.5, vmax);
            if (f.ForeAndAft)
            {
                f.ClewU = Dot(clews[0], f.U);
                f.ClewV = Math.Max(0.5, Dot(clews[0], f.V));
                // a triangle of its area (at least past its clew)
                f.YardLength = Math.Max(2 * area / f.ClewV, f.ClewU + 0.5);
                f.U0 = Math.Min(-0.3, f.ClewU - 0.3);
                f.U1 = f.YardLength + 0.3;
                f.N0 = -0.9;
                f.N1 = 0.9;
            }
            else
            {
                f.U0 = Math.Min(-w / 2, umin) - 0.4;
                f.U1 = Math.Max(w / 2, umax) + 0.4;
                f.N0 = -0.4;
                f.N1 = Math.Max(0.4, belly + 0.2);
            }
            return f;
        }

        /// <summary>A ship-local point (or a direction: point false) in the sail's frame.</summary>
        public void ToSail(double x, double y, double z, out double u, out double v, out double n, bool point)
        {
            if (point) { x -= A[0]; y -= A[1]; z -= A[2]; }
            u = x * U[0] + y * U[1] + z * U[2];
            v = x * V[0] + y * V[1] + z * V[2];
            n = x * N[0] + y * N[1] + z * N[2];
        }

        /// <summary>A point of the sail's frame, ship-local.</summary>
        public double[] FromSail(double u, double v, double n) => new[]
        {
            A[0] + u * U[0] + v * V[0] + n * N[0], A[1] + u * U[1] + v * V[1] + n * N[1], A[2] + u * U[2] + v * V[2] + n * N[2]
        };

        /// <summary>Is (u, v) within its outline (by margin)?</summary>
        public bool Inside(double u, double v, double margin)
        {
            if (v < -0.3 - margin || v > V1 + 0.2 + margin || u < U0 - margin || u > U1 + margin) return false;
            if (!ForeAndAft) return true;
            double f = Math.Clamp(v / ClewV, 0, 1);
            double from = ClewU * f, to = YardLength + (ClewU - YardLength) * f;
            return u >= Math.Min(from, to) - margin && u <= Math.Max(from, to) + margin && v <= ClewV + margin;
        }

        /// <summary>Its corners (the box a sail takes, set full and not turned, belly and all), ship-local.</summary>
        public System.Collections.Generic.IEnumerable<double[]> Corners()
        {
            foreach (double n in new[] { N0, N1 })
            {
                if (ForeAndAft)
                {
                    yield return FromSail(U0, -0.2, n);
                    yield return FromSail(U1, -0.2, n);
                    yield return FromSail(ClewU, ClewV + 0.2, n);
                }
                else
                {
                    yield return FromSail(U0, -0.3, n);
                    yield return FromSail(U1, -0.3, n);
                    yield return FromSail(U0, V1 + 0.2, n);
                    yield return FromSail(U1, V1 + 0.2, n);
                }
            }
        }

        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        static double[] Norm(double[] a)
        {
            double l = Math.Sqrt(Dot(a, a));
            return l < 1e-9 ? new double[] { 0, 0, 1 } : new[] { a[0] / l, a[1] / l, a[2] / l };
        }
    }
}
