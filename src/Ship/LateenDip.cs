using System;
using System.Runtime.CompilerServices;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (note 64, 24 October 2026: "Main sail penetrates the mast. How lateen sails should work when changing sides?"; the
    /// user chose the dip) A lateen that changes sides as real ones do (sails[].dip): its yard and sail lie on the lee side
    /// of the mast, and when the wind crosses her bow or stern the crew dips the yard round the fore side of the mast to
    /// the new lee side. Every field may be left out. Without this object the sail stays on the side it's made on.
    /// The other tack is the yard, the sail and the ropes that follow the yard mirrored across the mast's centre plane
    /// (z = the mast's z): a symmetric rig needs no mesh of its own for it.
    /// </summary>
    public class LateenDipDef
    {
        /// <summary>The mast it dips round (null: the one nearest its yard's axis).</summary>
        public string Mast;
        /// <summary>
        /// The yard's sling: the point on its centre line nearest the mast's axis (null: the tip of its halyard, the rope on the
        /// yard from the mast's head; without one, the yard part's axis point at the sail's z).
        /// </summary>
        public double[] Pivot;
        /// <summary>Blocks the yard is bowsed off the mast while it comes round, clear of the mast and what's on it.</summary>
        public float StandOff = 0.4f;
        /// <summary>Blocks the yard is carried forward as it's bowsed off (away from the shrouds, aft of the mast).</summary>
        public float Ahead = 0.2f;
        /// <summary>Blocks it's hoisted while upright (NaN: so far that its heel comes no lower than it hangs set).</summary>
        public float Hoist = float.NaN;
        /// <summary>How long a whole dip takes.</summary>
        public float Seconds = 8;
    }

    /// <summary>
    /// A lateen as it's drawn now, in its frame (the side it's made on): its yard's turn by the sheet (radians), how far
    /// it's set and bellied, how deep into a dip (0 set .. 1 upright before the mast), and whether all of it is drawn
    /// mirrored to the mast's other side.
    /// </summary>
    public struct LateenPose
    {
        public bool Mirror;
        public double Sheet, Depth;
        public float Frac, Belly;
    }

    /// <summary>
    /// A lateen's dip (<see cref="LateenDipDef"/>) worked out from her design, once a design. Positions are ship-local.
    /// The lee is the ship's (+1 starboard, -1 port); "lee shown" eases from one to the other over a dip: its sign is the
    /// side the lateens are drawn on, 1 - |lee shown| how deep into the dip they are (both sides meet at 0, with the yard
    /// upright before the mast and the sail brailed up to it, where the mirrored yard is the same).
    /// </summary>
    public sealed class LateenRig
    {
        public int Sail;
        public SailDef Def;
        public MovingPartDef Yard;
        public MastDef Mast;
        /// <summary>The side of its mast it's made on (+1 starboard, -1 port): its lee there.</summary>
        public int Side;
        /// <summary>The belly (<see cref="PartMotion.BellyMatrix"/>) that bellies its mesh away from the mast.</summary>
        public int BellyAway;
        public double[] Pivot;
        public double StandOff, Ahead, Hoist, Seconds;
        double[] tiltAxis;
        double tilt, front;

        /// <summary>
        /// Where the wind must be before the crew dips to the other side: at least this many degrees off her bow, off her stern
        /// (running, a lateen may stand a little by the lee), and there this many seconds on end (note 104: "some wobble around
        /// the main mast axis": a wind wavering across her stern dipped the yards back and forth).
        /// </summary>
        public const double BowMargin = 10, SternMargin = 20, HoldSeconds = 3;
        /// <summary>
        /// The share of a dip spent brailing up (and easing the sheet to fore-and-aft), bowsing the yard off the mast and
        /// forward (clear of the shrouds before it's peaked), peaking it upright and hoisting it, and going round.
        /// </summary>
        const double BrailTo = 0.3, OffFrom = 0.2, OffTo = 0.45, PeakFrom = 0.4, PeakTo = 0.7, RoundFrom = 0.6;

        static readonly ConditionalWeakTable<ShipDefinition, LateenRig[]> rigs = new();

        /// <summary>Her lateens that dip, by sail index (null where a sail doesn't).</summary>
        public static LateenRig[] Of(ShipDefinition def)
        {
            if (def?.Sails == null) return Array.Empty<LateenRig>();
            return rigs.GetValue(def, Make);
        }

        /// <summary>Has she a lateen that dips?</summary>
        public static bool Any(ShipDefinition def)
        {
            foreach (var r in Of(def)) if (r != null) return true;
            return false;
        }

        static LateenRig[] Make(ShipDefinition def)
        {
            var all = new LateenRig[def.Sails.Length];
            for (int i = 0; i < all.Length; i++) all[i] = Build(def, i);
            return all;
        }

        static LateenRig Build(ShipDefinition def, int si)
        {
            var sd = def.Sails[si];
            var dip = sd.Dip;
            if (dip == null || sd.AxisPoint == null || sd.AxisDir == null) return null;
            var yard = Array.Find(def.MovingParts ?? Array.Empty<MovingPartDef>(), p => p.Code == "yard-" + sd.Code && p.Source == "sheet" && p.AxisPoint != null);
            if (yard == null || def.Masts == null || def.Masts.Length == 0) return null;
            MastDef mast = dip.Mast != null ? Array.Find(def.Masts, m => m.Code == dip.Mast) : null;
            if (mast == null)
            {
                double best = double.MaxValue;
                foreach (var m in def.Masts)
                {
                    double d = Math.Pow(m.X - yard.AxisPoint[0], 2) + Math.Pow(m.Z - yard.AxisPoint[2], 2);
                    if (d < best) { best = d; mast = m; }
                }
            }
            var f = SailFrame.Of(def, sd, 0);
            if (f == null) return null;
            var r = new LateenRig { Sail = si, Def = sd, Yard = yard, Mast = mast };
            r.Side = sd.AxisPoint[2] >= mast.Z ? 1 : -1;
            // (its mesh is made bellied to its +N side: toward the mast or away from it)
            r.BellyAway = f.N[2] * r.Side >= 0 ? 1 : -1;
            var halyard = Array.Find(def.MovingParts, p => p.Kind == "follow" && p.Follow == yard.Code && p.Anchor?.Length == 3 && p.Tip?.Length == 3 &&
                                                          Math.Abs(p.Anchor[0] - mast.X) <= mast.Radius && Math.Abs(p.Anchor[2] - mast.Z) <= mast.Radius);
            r.Pivot = dip.Pivot?.Length == 3 ? (double[])dip.Pivot.Clone() : halyard != null ? (double[])halyard.Tip.Clone()
                : new[] { yard.AxisPoint[0], yard.AxisPoint[1], sd.AxisPoint[2] };
            r.StandOff = Math.Max(0, dip.StandOff);
            r.Ahead = Math.Max(0, dip.Ahead);
            r.Seconds = Math.Max(1, dip.Seconds);
            // peaked upright about the athwartships line through the sling: its yard (U, from the tack up) turned onto the vertical
            var u = f.U;
            double[] axis = { -u[2], 0, u[0] };       // U × up
            double al = Math.Sqrt(axis[0] * axis[0] + axis[2] * axis[2]);
            r.tiltAxis = al < 1e-6 ? new double[] { 0, 0, 1 } : new[] { axis[0] / al, 0, axis[2] / al };
            r.tilt = al < 1e-6 ? 0 : Math.Atan2(al, u[1]);
            // hoisted so its heel (the tack) comes no lower than it hangs
            double fore = -((sd.AxisPoint[0] - r.Pivot[0]) * u[0] + (sd.AxisPoint[1] - r.Pivot[1]) * u[1] + (sd.AxisPoint[2] - r.Pivot[2]) * u[2]);
            r.Hoist = float.IsNaN(dip.Hoist) ? Math.Max(0, sd.AxisPoint[1] - (r.Pivot[1] - Math.Max(0, fore))) : Math.Max(0, dip.Hoist);
            // round the mast's fore side: from its side (0, 0, Side) to before it (1, 0, 0), about the up axis
            r.front = Math.Atan2(r.Side, 0);
            return r;
        }

        /// <summary>Where the wind puts the lee (+1 starboard, -1 port), or 0 within <see cref="BowMargin"/> of her bow or <see cref="SternMargin"/> of her stern.</summary>
        public static int LeeOf(double apparentAngle, int side) =>
            apparentAngle < BowMargin || apparentAngle > 180 - SternMargin ? 0 : -side;

        /// <summary>
        /// Server: the lee after this tick, from the lee now and the lee the wind wants (<see cref="LeeOf"/>): changed only once
        /// it has wanted the other side for <see cref="HoldSeconds"/> on end and the crew can dip (wantFor: how long so far).
        /// </summary>
        public static int Decide(int lee, int want, bool canDip, ref float wantFor, float dt)
        {
            if (want == 0 || want == lee || !canDip) { wantFor = 0; return lee; }
            wantFor += dt;
            if (wantFor < HoldSeconds) return lee;
            wantFor = 0;
            return want;
        }

        /// <summary>
        /// (note 104) The lateens' sheet (radians, as <see cref="Sailing.Trim"/>'s: + the after end to starboard) for the
        /// apparent wind's angle and the server's lee: as far out as the crew trims for that wind, always toward the lee. Not
        /// from the side the wind is on this moment, which wavers across her stern or bow and swung the yards back and forth.
        /// </summary>
        public static double Sheet(double apparentAngle, int lee, SailingDef d) =>
            lee * Math.Clamp(apparentAngle / 2, Math.Min(5, d.MaxSheet), d.MaxSheet) * Math.PI / 180;

        /// <summary>How far (radians) the trim the wind calls for must move before the crew sheets the lateens to it: they trim in steps, not to every waver.</summary>
        public const double SheetStep = 3 * Math.PI / 180;

        /// <summary>The sheet the crew holds (goal) after the wind calls for target: the goal kept unless target is a step from it, or on the other side.</summary>
        public static double SheetGoal(double goal, double target) =>
            double.IsNaN(goal) || Math.Abs(target - goal) > SheetStep || goal * target < 0 ? target : goal;

        /// <summary>The lee shown eased toward the lee over a dip of this many seconds (NaN, or no lee yet: set at once).</summary>
        public static float Ease(float shown, int lee, double seconds, float dt)
        {
            if (lee == 0) return shown;
            if (float.IsNaN(shown)) return lee;
            float step = (float)(2 * dt / Math.Max(0.5, seconds));
            return GameMath.Clamp(shown + GameMath.Clamp(lee - shown, -step, step), -1, 1);
        }

        static double Smooth(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// Is its sail pressed against the rigging (<see cref="SailPress"/>) in this pose? Not once its yard is peaked up and going
        /// round: the press is for a yard turned by its sheet, and the sail is brailed up to the yard by then.
        /// </summary>
        public static bool Presses(LateenPose p) => p.Depth < OffFrom;

        /// <summary>How far into a dip (0..1) its sail is brailed up.</summary>
        public static double Brail(double depth) => Smooth(depth / BrailTo);

        /// <summary>The lateen as drawn for the lee shown, the crew's sheet (radians, as <see cref="Sailing.Trim"/>) and the sail set (as drawn).</summary>
        public LateenPose Pose(float leeShown, double sheet, float frac)
        {
            double x = float.IsNaN(leeShown) ? Side : Math.Clamp(leeShown, -1, 1);
            var p = new LateenPose { Mirror = (x >= 0 ? 1 : -1) != Side, Depth = 1 - Math.Abs(x) };
            double brail = Brail(p.Depth);
            // in its own frame the sheet takes its after end out to its lee (Side), never across to the mast's other side;
            // through a dip the crew eases it to fore-and-aft as they brail up
            double s = p.Mirror ? -sheet : sheet;
            if (s * Side < 0) s = 0;
            p.Sheet = s * (1 - brail);
            p.Frac = (float)(frac * (1 - brail));
            p.Belly = BellyAway * (float)Math.Min(1, Math.Abs(p.Sheet) / (4 * Math.PI / 180));
            return p;
        }

        /// <summary>Its yard's matrix in its own frame for a pose (before the mirror): turned by the sheet, and the dip.</summary>
        public void YardFrame(LateenPose p, float[] m)
        {
            double q = p.Depth;
            double off = Smooth((q - OffFrom) / (OffTo - OffFrom)), peak = Smooth((q - PeakFrom) / (PeakTo - PeakFrom)), round = Smooth((q - RoundFrom) / (1 - RoundFrom));
            // the dip: bowsed off to its side and forward (clear of the shrouds), peaked upright about the sling and hoisted,
            // carried round the mast's fore side (its sling on a circle about the mast, the yard not turned: before the mast its
            // sail's plane is athwartships, where a lateen's yard, two spars side by side in that plane, is the same mirrored)
            var d = Rot(tiltAxis, tilt * peak, Pivot);
            double qx = Pivot[0] - Mast.X, qz = Pivot[2] + Side * StandOff * off - Mast.Z;
            double c = Math.Cos(front * round), sn = Math.Sin(front * round);
            Pre(d, Trans(Ahead * off, Hoist * peak, Side * StandOff * off));
            Pre(d, Trans(c * qx + sn * qz - qx, 0, -sn * qx + c * qz - qz));
            var turn = new float[16];
            PartMotion.PartMatrix(Yard, PartMotion.TurnDegrees(Yard, (float)p.Sheet) * GameMath.DEG2RAD, turn);
            Mat4f.Mul(m, turn, d);
        }

        /// <summary>The mirror across the mast's centre plane (z = the mast's z).</summary>
        public void MirrorMatrix(float[] m)
        {
            Mat4f.Identity(m);
            m[10] = -1;
            m[14] = (float)(2 * Mast.Z);
        }

        /// <summary>A frame matrix as drawn: mirrored to the mast's other side when the pose is.</summary>
        public void Drawn(LateenPose p, float[] frame, float[] m)
        {
            if (!p.Mirror) { Array.Copy(frame, m, 16); return; }
            var mir = new float[16];
            MirrorMatrix(mir);
            Mat4f.Mul(m, mir, frame);
        }

        /// <summary>
        /// A ray (ship-local) into its sail's own frame as made, unturned (for <see cref="PartMotion.RaySail"/> with no trim):
        /// mirrored back when it's drawn mirrored, and through the inverse of its yard's frame.
        /// </summary>
        public void RayToFrame(LateenPose p, Vec3d o, Vec3d d, out Vec3d fo, out Vec3d fd)
        {
            double ox = o.X, oy = o.Y, oz = o.Z, dz = d.Z;
            if (p.Mirror) { oz = 2 * Mast.Z - oz; dz = -dz; }
            var m = new float[16];
            YardFrame(p, m);
            double px = ox - m[12], py = oy - m[13], pz = oz - m[14];
            fo = new Vec3d(m[0] * px + m[1] * py + m[2] * pz, m[4] * px + m[5] * py + m[6] * pz, m[8] * px + m[9] * py + m[10] * pz);
            fd = new Vec3d(m[0] * d.X + m[1] * d.Y + m[2] * dz, m[4] * d.X + m[5] * d.Y + m[6] * dz, m[8] * d.X + m[9] * d.Y + m[10] * dz);
        }

        /// <summary>How near a ray must pass the line of its sail's head (along its yard) to meet the yard, in blocks.</summary>
        public const double YardReach = 0.25;

        /// <summary>
        /// Where a ray in its sail's own frame (<see cref="RayToFrame"/>) meets its yard: within <see cref="YardReach"/> of the
        /// sail's head from its tack (U 0) to its peak (the yard's length). t along the ray, or -1.
        /// </summary>
        public double RayYard(SailFrame f, Vec3d o, Vec3d d, double range)
        {
            double[] a = f.A, u = f.U;
            double dl = Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (dl < 1e-12) return -1;
            double[] dd = { d.X / dl, d.Y / dl, d.Z / dl }, w = { o.X - a[0], o.Y - a[1], o.Z - a[2] };
            double b = dd[0] * u[0] + dd[1] * u[1] + dd[2] * u[2], dw = dd[0] * w[0] + dd[1] * w[1] + dd[2] * w[2], uw = u[0] * w[0] + u[1] * w[1] + u[2] * w[2];
            double den = 1 - b * b;
            double tr = den < 1e-9 ? 0 : (b * uw - dw) / den;
            double s = Math.Clamp(uw + b * tr, 0, f.YardLength);
            // (the nearest point of the head to the ray for that s, then that ray point)
            tr = Math.Max(0, (a[0] + u[0] * s - o.X) * dd[0] + (a[1] + u[1] * s - o.Y) * dd[1] + (a[2] + u[2] * s - o.Z) * dd[2]);
            double ex = o.X + dd[0] * tr - a[0] - u[0] * s, ey = o.Y + dd[1] * tr - a[1] - u[1] * s, ez = o.Z + dd[2] * tr - a[2] - u[2] * s;
            double dist2 = ex * ex + ey * ey + ez * ez;
            if (dist2 > YardReach * YardReach) return -1;
            double t = Math.Max(0, tr - Math.Sqrt(YardReach * YardReach - dist2)) / dl;
            return t <= range ? t : -1;
        }

        /// <summary>The share of her sail area drawing while her lateens are brailed up for a dip (1 with none dipping).</summary>
        public static double Drive(ShipDefinition def, float leeShown)
        {
            if (float.IsNaN(leeShown) || Math.Abs(leeShown) >= 1) return 1;
            double all = 0, off = 0;
            var rs = Of(def);
            for (int i = 0; i < def.Sails.Length; i++)
            {
                double a = Math.Max(0, def.Sails[i].Area);
                all += a;
                if (i < rs.Length && rs[i] != null) off += a * Brail(1 - Math.Abs(leeShown));
            }
            return all <= 0 ? 1 : 1 - off / all;
        }

        /// <summary>The longest dip of her lateens (seconds), 0 with none.</summary>
        public static double DipSeconds(ShipDefinition def)
        {
            double s = 0;
            foreach (var r in Of(def)) if (r != null) s = Math.Max(s, r.Seconds);
            return s;
        }

        static float[] Trans(double x, double y, double z)
        {
            var m = Mat4f.Create();
            m[12] = (float)x; m[13] = (float)y; m[14] = (float)z;
            return m;
        }

        /// <summary>Rotation by angle (radians, right-handed) about an axis through a point.</summary>
        static float[] Rot(double[] a, double angle, double[] at)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c, x = a[0], y = a[1], z = a[2];
            var r = new double[3, 3]
            {
                { t * x * x + c, t * x * y - s * z, t * x * z + s * y },
                { t * x * y + s * z, t * y * y + c, t * y * z - s * x },
                { t * x * z - s * y, t * y * z + s * x, t * z * z + c },
            };
            var m = Mat4f.Create();
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++) m[col * 4 + row] = (float)r[row, col];
                m[12 + row] = (float)(at[row] - (r[row, 0] * at[0] + r[row, 1] * at[1] + r[row, 2] * at[2]));
            }
            return m;
        }

        /// <summary>m = a · m (a applied after m).</summary>
        static void Pre(float[] m, float[] a) => Mat4f.Mul(m, a, (float[])m.Clone());
    }
}
