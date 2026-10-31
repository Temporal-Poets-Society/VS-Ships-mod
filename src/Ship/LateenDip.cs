using System;
using System.Linq;
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

        /// <summary>
        /// Its press in its own frame (<see cref="SailPress.Around"/>) from all her obstacles: its mast and that mast's windward
        /// shrouds (as made, those on its mast's other side), mirrored to it when it's drawn on the other side.
        /// </summary>
        public SailPress PressIn(SailPress all, bool mirror) =>
            mirror ? all.Around(Mast, Side).Mirrored(Mast.Z) : all.Around(Mast, -Side);

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

    /// <summary>
    /// (note 104, the user's decision: "windward only, as on real lateen ships") A shroud of a dipping lateen's mast, from
    /// the design's rig check (rigCheck.standing: x0 y0 z0 x1 y1 z1 radius): its masthead end, its foot at the channel, the
    /// side it's on (+1 starboard, -1 port) and its rank there (0 the foremost). Only the windward set is set up; the lee
    /// set is cast off and hangs from the masthead down the after side of the mast, and they change over in the middle of
    /// a dip, when the yard is upright before the mast.
    /// </summary>
    public sealed class LateenShroud
    {
        public MastDef Mast;
        public int Side, Rank;
        /// <summary>Its line in rigCheck.standing.</summary>
        public int Line;
        public double[] Head, Foot;
        public double Radius;
        /// <summary>The skin group it's drawn from (made by <see cref="LateenShrouds.Split"/>).</summary>
        public string Group => "rig:shroud:" + Line;
    }

    public static class LateenShrouds
    {
        /// <summary>
        /// Exported groups (the model's, optional): the shrouds of one mast on one side, "rig:shrouds-&lt;mast&gt;-port" and
        /// "-starboard", not in "exterior". Without them they're found in "exterior" (rope quads along a shroud's line).
        /// </summary>
        public const string ExportPrefix = "rig:shrouds-";
        public static string RatlineGroup(LateenShroud sh) => "rig:ratlines:"+sh.Mast.Code+":"+sh.Side;
        public static bool RatlinesVisible(ShipDefinition def,string group,float lee) =>
            !group.StartsWith("rig:ratlines:") || Of(def).Any(sh=>RatlineGroup(sh)==group && SetUp(def,sh,lee));

        /// <summary>How far past its radius a quad's corners may lie from a shroud's line and still be that shroud.</summary>
        const double Reach = 0.06;

        static readonly ConditionalWeakTable<ShipDefinition, LateenShroud[]> cache = new();

        /// <summary>The shrouds of her dipping lateens' masts (none without a dipping lateen or a rig check).</summary>
        public static LateenShroud[] Of(ShipDefinition def) => def == null ? Array.Empty<LateenShroud>() : cache.GetValue(def, Make);

        static LateenShroud[] Make(ShipDefinition def)
        {
            var list = new System.Collections.Generic.List<LateenShroud>();
            var masts = new System.Collections.Generic.List<MastDef>();
            foreach (var r in LateenRig.Of(def)) if (r != null && !masts.Contains(r.Mast)) masts.Add(r.Mast);
            var lines = def.RigCheck?.Standing;
            if (masts.Count == 0 || lines == null) return Array.Empty<LateenShroud>();
            for (int k = 0; k < lines.Length; k++)
            {
                var l = lines[k];
                if (l == null || l.Length < 7) continue;
                double[] a = { l[0], l[1], l[2] }, b = { l[3], l[4], l[5] };
                var (head, foot) = a[1] >= b[1] ? (a, b) : (b, a);
                foreach (var m in masts)
                {
                    if (Math.Sqrt((head[0] - m.X) * (head[0] - m.X) + (head[2] - m.Z) * (head[2] - m.Z)) > m.Radius + 0.5 || Math.Abs(foot[2] - m.Z) < 0.2) continue;
                    list.Add(new LateenShroud { Mast = m, Side = foot[2] > m.Z ? 1 : -1, Line = k, Head = head, Foot = foot, Radius = l[6] });
                    break;
                }
            }
            foreach (var s in list)
            {
                foreach (var o in list)
                    if (o != s && o.Mast == s.Mast && o.Side == s.Side && (o.Foot[0] > s.Foot[0] || o.Foot[0] == s.Foot[0] && o.Line < s.Line)) s.Rank++;
            }
            return list.ToArray();
        }

        /// <summary>The lee as drawn for the lee shown (its sign; before it's known, the side the mast's lateen is made on).</summary>
        public static int DrawnLee(ShipDefinition def, MastDef mast, float leeShown)
        {
            if (!float.IsNaN(leeShown)) return leeShown >= 0 ? 1 : -1;
            foreach (var r in LateenRig.Of(def)) if (r != null && r.Mast == mast) return r.Side;
            return 1;
        }

        /// <summary>Is it set up (the windward set) for the lee shown? The lee set is cast off.</summary>
        public static bool SetUp(ShipDefinition def, LateenShroud s, float leeShown) => s.Side != DrawnLee(def, s.Mast, leeShown);

        /// <summary>Where a cast-off shroud's foot hangs: down the after side of its mast (clear of the yard slung on its side), above its channel.</summary>
        public static double[] HungFoot(LateenShroud s) => new[]
        {
            s.Mast.X - (s.Mast.Radius + 0.12 + 0.12 * s.Rank), s.Foot[1] + 0.3, s.Mast.Z + s.Side * 0.03
        };

        /// <summary>Where a cast-off shroud's eye hangs: off the after side of its masthead, clear of the mast all the way down.</summary>
        public static double[] HungHead(LateenShroud s) => new[]
        {
            s.Mast.X - (s.Mast.Radius + 0.05 + 0.12 * s.Rank), s.Head[1] - 0.1, s.Mast.Z + s.Side * 0.03
        };

        /// <summary>A cast-off shroud's matrix (ship-local): turned and stretched from its eye (<see cref="HungHead"/>) down to its foot (<see cref="HungFoot"/>).</summary>
        public static void HangMatrix(LateenShroud s, float[] m)
        {
            float[] Move(double[] from, double[] to)
            {
                var mv = Mat4f.Create();
                for (int i = 0; i < 3; i++) mv[12 + i] = (float)(to[i] - from[i]);
                return mv;
            }
            PartMotion.FollowMatrix(new MovingPartDef { Anchor = s.Head, Tip = s.Foot }, Move(s.Foot, HungFoot(s)), m, Move(s.Head, HungHead(s)));
        }

        /// <summary>
        /// Its shrouds into a skin group each (<see cref="LateenShroud.Group"/>): from the exported groups
        /// (<see cref="ExportPrefix"/>) if there are any, else from "exterior", whose quads they were are collapsed to a point
        /// (its quads keep their places: its paint and planking ranges stay right). Each quad goes to the shroud whose line all
        /// its corners lie along. How many quads it moved.
        /// </summary>
        public static int Split(ShipDefinition def, ShipSkinFile file)
        {
            var shrouds = Of(def);
            if (shrouds.Length == 0 || file?.Groups == null) return 0;
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<float[]>>();
            foreach (var s in shrouds) groups[s.Group] = new System.Collections.Generic.List<float[]>();
            bool exported = false;
            foreach (var key in file.Groups.Keys) if (key.StartsWith(ExportPrefix)) exported = true;
            int moved = 0;
            foreach (var kv in file.Groups)
            {
                bool fromExport = kv.Key.StartsWith(ExportPrefix);
                if (exported ? !fromExport : kv.Key != "exterior") continue;
                foreach (var q in kv.Value)
                {
                    if (q == null || q.Length < 33) continue;
                    if (!fromExport)
                    {
                        int ti = (int)q[0];
                        if (file.Textures == null || ti < 0 || ti >= file.Textures.Length || !file.Textures[ti].Contains("rope")) continue;
                    }
                    var s = Along(shrouds, q);
                    if(s==null && fromExport)
                    {
                        var member=shrouds.FirstOrDefault(sh=>kv.Key==ExportPrefix+sh.Mast.Code+(sh.Side>0?"-starboard":"-port"));
                        if(member!=null) {
                            string key=RatlineGroup(member);
                            if(!groups.TryGetValue(key,out var ratlines)) groups[key]=ratlines=new System.Collections.Generic.List<float[]>();
                            ratlines.Add((float[])q.Clone());moved++;
                        }
                    }
                    if (s == null) continue;
                    groups[s.Group].Add((float[])q.Clone());
                    if (!fromExport)
                        for (int v = 1; v < 4; v++)
                            for (int c = 0; c < 3; c++) q[1 + 8 * v + c] = q[1 + c];
                    moved++;
                }
            }
            foreach (var kv in groups) file.Groups[kv.Key] = kv.Value.ToArray();
            return moved;
        }

        /// <summary>The shroud all of a quad's corners lie along (within its radius and <see cref="Reach"/>), or null.</summary>
        static LateenShroud Along(LateenShroud[] shrouds, float[] q)
        {
            foreach (var s in shrouds)
            {
                double ax = s.Foot[0], ay = s.Foot[1], az = s.Foot[2], dx = s.Head[0] - ax, dy = s.Head[1] - ay, dz = s.Head[2] - az;
                double len = dx * dx + dy * dy + dz * dz, lim = (s.Radius + Reach) * (s.Radius + Reach);
                bool all = len > 1e-9;
                for (int v = 0; v < 4 && all; v++)
                {
                    double px = q[1 + 8 * v] - ax, py = q[2 + 8 * v] - ay, pz = q[3 + 8 * v] - az;
                    double t = Math.Clamp((px * dx + py * dy + pz * dz) / len, 0, 1);
                    double ex = px - dx * t, ey = py - dy * t, ez = pz - dz * t;
                    all = ex * ex + ey * ey + ez * ez <= lim;
                }
                if (all) return s;
            }
            return null;
        }
    }
}
