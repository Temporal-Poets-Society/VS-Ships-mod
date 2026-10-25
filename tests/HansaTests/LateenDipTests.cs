using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (note 64, 24 October 2026: "Main sail penetrates the mast. How lateen sails should work when changing sides?") The
        /// caravel's lateens dip round their masts (LateenRig). On both tacks, over the trim the crew gives her (every 5° of the
        /// apparent wind, full and reefed), each sail lies on the lee side of its mast, every vertex of it (pressed, as drawn)
        /// clear of every mast; through a dip both ways, with the sheet coming over, the sail stays clear of the masts, the
        /// yard comes no closer to its mast than it's slung or than the mast's radius, goes round its fore side, and the two
        /// sides meet where the dip turns; the crosshair finds the sail on its other side; the server's lee and the ease.
        /// </summary>
        static void CaravelLateenDip(string root)
        {
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", "caravel.json");
            string assets = Path.Combine(root, "assets", "hansa");
            if (!File.Exists(path)) { Console.WriteLine("  --   caravel.json not there: the lateens' dip isn't checked"); return; }
            Console.WriteLine("caravel: the lateens dip round their masts to the lee (note 64)");
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            string skinPath = Path.Combine(assets, "config", "shipmeshes", "caravel.json");
            var skin = File.Exists(skinPath) ? JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(skinPath)) : null;
            var rigs = LateenRig.Of(def);
            var codes = def.Sails.Where((s, i) => i < rigs.Length && rigs[i] != null).Select(s => s.Code).ToList();
            Check(codes.Contains("main") && codes.Contains("mizzen"), $"both her lateens dip ({string.Join(", ", codes)}): sails[].dip in caravel.json");
            var press = SailPress.Of(def);
            var ym = new float[16];
            var dm = new float[16];
            var mir = new float[16];
            var set = new float[16];
            var furl = new float[16];
            var belly = new float[16];

            foreach (var rig in rigs)
            {
                if (rig == null) continue;
                var sail = rig.Def;
                var f = def.DamageParts.SailFrames[rig.Sail];
                var src = SailPoints(def, sail, assets, skin);
                float[] yardSrc = skin?.Groups != null && skin.Groups.TryGetValue(rig.Yard.PartGroup, out var yq)
                    ? yq.SelectMany(q => Enumerable.Range(0, 4).SelectMany(v => new[] { q[1 + 8 * v], q[2 + 8 * v], q[3 + 8 * v] })).ToArray() : null;
                if (f == null || src == null || yardSrc == null) { Check(false, $"{sail.Code}: its sail and yard meshes are in caravel's mesh file"); continue; }
                Console.WriteLine($"  {sail.Code}: made on the {(rig.Side > 0 ? "starboard" : "port")} side of the {rig.Mast.Code}mast (radius {rig.Mast.Radius}), " +
                                  $"stand-off {rig.StandOff}, ahead {rig.Ahead}, hoist {rig.Hoist:0.00}, {rig.Seconds} s");

                // the sail as drawn for a pose: brailed and bellied in its frame, pressed there, turned with its yard, mirrored
                float[] SailDrawn(LateenPose p)
                {
                    rig.YardFrame(p, ym);
                    PartMotion.FurlMatrix(sail, p.Frac, furl);
                    PartMotion.BellyMatrix(f, p.Belly, belly);
                    Mat4f.Mul(set, furl, belly);
                    var pts = (float[])src.Clone();
                    Apply(set, pts);
                    if (LateenRig.Presses(p)) (p.Mirror ? press.Mirrored(rig.Mast.Z) : press).Press(f, ym, pts, pts.Length / 3);
                    rig.Drawn(p, ym, dm);
                    Apply(dm, pts);
                    return pts;
                }
                float[] YardDrawn(LateenPose p)
                {
                    rig.YardFrame(p, ym);
                    rig.Drawn(p, ym, dm);
                    var pts = (float[])yardSrc.Clone();
                    Apply(dm, pts);
                    return pts;
                }
                // how far a point is inside any mast (its radius less its distance from the axis, where the mast stands)
                double Into(float[] pts, out string where)
                {
                    double worst = double.MinValue;
                    where = "";
                    for (int i = 0; i < pts.Length; i += 3)
                        foreach (var m in def.Masts)
                        {
                            if (pts[i + 1] < m.Y0 || pts[i + 1] > m.Y1) continue;
                            double into = m.Radius - Math.Sqrt(Math.Pow(pts[i] - m.X, 2) + Math.Pow(pts[i + 2] - m.Z, 2));
                            if (into > worst) { worst = into; where = $"the {m.Code}mast at ({pts[i]:0.00}, {pts[i + 1]:0.00}, {pts[i + 2]:0.00})"; }
                        }
                    return worst;
                }

                // ---- both tacks, the crew's trim
                int bad = 0, windward = 0, trims = 0;
                double deepest = double.MinValue, leastLee = double.MaxValue;
                string worstAt = "";
                foreach (int side in new[] { 1, -1 })
                    foreach (float frac in new[] { 1f, 0.55f })
                        for (double awa = 45; awa <= 175; awa += 5)
                        {
                            Sailing.Trim(awa, side, def.Sailing, out _, out double sheet);
                            int lee = LateenRig.LeeOf(awa, side) is int l0 && l0 != 0 ? l0 : -side;
                            var pose = rig.Pose(lee, sheet, frac);
                            var pts = SailDrawn(pose);
                            double into = Into(pts, out string where);
                            if (into > deepest) { deepest = into; worstAt = $"{where}, {awa}° on the {(side > 0 ? "starboard" : "port")} tack"; }
                            if (into > -0.02) bad++;
                            // on the lee side of its mast: its middle, and its belly away from the wind
                            double mz = 0;
                            for (int i = 2; i < pts.Length; i += 3) mz += pts[i] - rig.Mast.Z;
                            mz /= pts.Length / 3;
                            leastLee = Math.Min(leastLee, mz * lee);
                            if (mz * lee <= 0 || pose.Belly == 0) windward++;
                            trims++;
                        }
                Check(bad == 0, $"{sail.Code}: on both tacks, over {trims} trims, every vertex of the sail is at least 0.02 clear of every mast " +
                                $"(closest: {-deepest:0.000} off {worstAt})");
                Check(windward == 0, $"{sail.Code}: on both tacks it lies on the lee side of its mast, bellied (its middle at least {leastLee:0.00} to leeward)");

                // ---- the dip, both ways, the sheet coming over as the crew trims to the new tack
                var steady = YardDrawn(rig.Pose(rig.Side, 0, 1));
                double[] slung = new double[steady.Length / 3];
                for (int i = 0; i < slung.Length; i++) slung[i] = Math.Sqrt(Math.Pow(steady[i * 3] - rig.Mast.X, 2) + Math.Pow(steady[i * 3 + 2] - rig.Mast.Z, 2));
                int sailIn = 0, yardIn = 0, aft = 0;
                double sailDeep = double.MinValue, jump = 0, upright = 0;
                string yardWhere = "", sailWhere = "", jumpAt = "";
                double ropeSet = double.MaxValue, ropeDip = double.MaxValue;
                string ropeNear = "", ropeDipAt = "";
                // how near the yard's vertices come to her standing rigging's centre lines, less the ropes' radius
                double RopeGap(float[] pts)
                {
                    double best = double.MaxValue;
                    foreach (var l in def.RigCheck?.Standing ?? Array.Empty<double[]>())
                    {
                        double dx = l[3] - l[0], dy = l[4] - l[1], dz = l[5] - l[2], len = dx * dx + dy * dy + dz * dz;
                        for (int i = 0; i < pts.Length; i += 3)
                        {
                            double t = Math.Clamp(((pts[i] - l[0]) * dx + (pts[i + 1] - l[1]) * dy + (pts[i + 2] - l[2]) * dz) / len, 0, 1);
                            double ex = l[0] + dx * t - pts[i], ey = l[1] + dy * t - pts[i + 1], ez = l[2] + dz * t - pts[i + 2];
                            double gap = Math.Sqrt(ex * ex + ey * ey + ez * ez) - l[6];
                            if (gap < best) { best = gap; ropeNear = $"({pts[i]:0.00}, {pts[i + 1]:0.00}, {pts[i + 2]:0.00})"; }
                        }
                    }
                    return best;
                }
                foreach (int from in new[] { 1, -1 })
                {
                    Sailing.Trim(100, -from, def.Sailing, out _, out double sheet0);
                    Sailing.Trim(100, from, def.Sailing, out _, out double sheet1);
                    float[] before = null;
                    for (int k = 0; k <= 200; k++)
                    {
                        float x = from * (1 - k / 100f);
                        double sheet = sheet0 + (sheet1 - sheet0) * Math.Clamp(k / 160.0, 0, 1);
                        var pose = rig.Pose(x, sheet, 1);
                        double into = Into(SailDrawn(pose), out string sw);
                        if (into > sailDeep) { sailDeep = into; sailWhere = $"{sw} at {x:0.00}"; }
                        if (into > -0.02) sailIn++;
                        var y = YardDrawn(pose);
                        // (not a check: how near the yard comes to her shrouds, set and while it goes round)
                        double ropes = RopeGap(y);
                        if (pose.Depth <= 0.2) ropeSet = Math.Min(ropeSet, ropes);
                        else if (ropes < ropeDip) { ropeDip = ropes; ropeDipAt = $"{ropeNear} {pose.Depth:0.00} into it"; }
                        for (int i = 0; i < slung.Length; i++)
                        {
                            double r = Math.Sqrt(Math.Pow(y[i * 3] - rig.Mast.X, 2) + Math.Pow(y[i * 3 + 2] - rig.Mast.Z, 2));
                            bool inMast = y[i * 3 + 1] >= rig.Mast.Y0 && y[i * 3 + 1] <= rig.Mast.Y1;
                            if (inMast && r < Math.Min(slung[i], rig.Mast.Radius + 0.02) - 1e-3)
                            {
                                yardIn++;
                                if (yardWhere == "") yardWhere = $"({y[i * 3]:0.00}, {y[i * 3 + 1]:0.00}, {y[i * 3 + 2]:0.00}) {r:0.00} off the axis at {x:0.00}";
                            }
                        }
                        // round the fore side: where it's nearest the mast while it goes round, it's before the mast
                        if (pose.Depth > 0.6)
                        {
                            int near = Enumerable.Range(0, slung.Length).OrderBy(i => Math.Pow(y[i * 3] - rig.Mast.X, 2) + Math.Pow(y[i * 3 + 2] - rig.Mast.Z, 2)).First();
                            if (y[near * 3] < rig.Mast.X - 0.05) aft++;
                        }
                        if (Math.Abs(pose.Depth - 1) < 1e-6)
                        {
                            double lo = double.MaxValue, hi = double.MinValue;
                            for (int i = 0; i < slung.Length; i++) { lo = Math.Min(lo, y[i * 3 + 1]); hi = Math.Max(hi, y[i * 3 + 1]); }
                            upright = hi - lo;
                        }
                        // where the sides meet (just either side of x = 0) the yard is drawn the same: every vertex of the one
                        // near a vertex of the other (its own vertex may be across it: a round spar mirrored is the same spar)
                        if (before != null && Math.Sign(x) != Math.Sign(x + from / 100f) && Math.Abs(x) < 0.02)
                        {
                            var cells = new Dictionary<(int, int, int), List<int>>();
                            (int, int, int) Cell(float[] a, int v) => ((int)Math.Floor(a[v] * 10), (int)Math.Floor(a[v + 1] * 10), (int)Math.Floor(a[v + 2] * 10));
                            for (int v = 0; v < before.Length; v += 3)
                            {
                                var c = Cell(before, v);
                                if (!cells.TryGetValue(c, out var list)) cells[c] = list = new List<int>();
                                list.Add(v);
                            }
                            for (int v = 0; v < y.Length; v += 3)
                            {
                                var (cx0, cy0, cz0) = Cell(y, v);
                                double near = 1;
                                for (int a = -1; a <= 1; a++)
                                    for (int b = -1; b <= 1; b++)
                                        for (int c = -1; c <= 1; c++)
                                            if (cells.TryGetValue((cx0 + a, cy0 + b, cz0 + c), out var list))
                                                foreach (int w in list)
                                                    near = Math.Min(near, Math.Sqrt(Math.Pow(y[v] - before[w], 2) + Math.Pow(y[v + 1] - before[w + 1], 2) + Math.Pow(y[v + 2] - before[w + 2], 2)));
                                if (near > jump) { jump = near; jumpAt = $"({y[v]:0.00}, {y[v + 1]:0.00}, {y[v + 2]:0.00})"; }
                            }
                        }
                        before = y;
                    }
                }
                Console.WriteLine($"  {sail.Code}: its yard's surface comes {ropeSet:0.00} from a shroud as it's set (0.33 its radius, the sling's), {ropeDip:0.00} while it's dipped, at {ropeDipAt} (not checked)");
                Check(sailIn == 0, $"{sail.Code}: through the dip both ways the sail never comes within 0.02 of a mast (closest {-sailDeep:0.000} off {sailWhere})");
                Check(yardIn == 0, $"{sail.Code}: through the dip the yard comes no closer to the mast than it's slung or than the mast's radius" +
                                   $"{(yardIn > 0 ? $" ({yardIn}: {yardWhere})" : "")}");
                Check(aft == 0 && upright > 0, $"{sail.Code}: it goes round the mast's fore side, upright before it ({upright:0.0} high)");
                Check(jump < 0.06, $"{sail.Code}: where the dip turns, the yard on one side and mirrored on the other meet ({jump:0.000} apart{(jump >= 0.06 ? ": " + jumpAt : "")})");

                // ---- the crosshair finds it on its other side
                var other = rig.Pose(-rig.Side, -0.4 * rig.Side, 1);
                var drawn = SailDrawn(other);
                double cx = 0, cy = 0, cz = 0;
                int n = drawn.Length / 3;
                for (int i = 0; i < drawn.Length; i += 3) { cx += drawn[i] / n; cy += drawn[i + 1] / n; cz += drawn[i + 2] / n; }
                var o = new Vec3d(cx, cy, cz - rig.Side * 6);
                var dir = new Vec3d(0, 0, rig.Side);
                int part = def.DamageParts.SailAt(o, dir, 12, 0, -0.4 * rig.Side, 1, out double t, -rig.Side);
                int asMade = def.DamageParts.SailAt(o, dir, 12, 0, -0.4 * rig.Side, 1, out double t0);
                bool its = part >= 0 && def.DamageParts.Parts[part].Sail == rig.Sail;
                Check(its && t < 6.5, $"{sail.Code}: on its other side the crosshair finds the sail where it's drawn ({(its ? $"{t:0.00} along" : "missed")}; as made: {(asMade >= 0 ? $"{t0:0.00}" : "missed")})");
            }

            // ---- which side, the ease, the drive
            Check(LateenRig.LeeOf(90, 1) == -1 && LateenRig.LeeOf(90, -1) == 1 && LateenRig.LeeOf(5, 1) == 0 && LateenRig.LeeOf(176, -1) == 0 && LateenRig.LeeOf(60, -1) == 1,
                $"the lee is the side away from the wind, and not decided within {LateenRig.Margin}° of her bow or stern");
            double secs = LateenRig.DipSeconds(def);
            float shown = 1;
            int steps = 0;
            while (shown > -1 && steps < 10000) { shown = LateenRig.Ease(shown, -1, secs, 0.05f); steps++; }
            Check(Math.Abs(steps * 0.05 - secs) < 0.11 && float.IsNaN(LateenRig.Ease(float.NaN, 0, secs, 0.05f)) && LateenRig.Ease(float.NaN, 1, secs, 0.05f) == 1,
                $"a dip from one side to the other takes {steps * 0.05:0.0} s ({secs} s); a ship seen first is drawn on her lee at once");
            double mid = LateenRig.Drive(def, 0), half = LateenRig.Drive(def, 0.9f);
            Check(LateenRig.Drive(def, 1) == 1 && LateenRig.Drive(def, -1) == 1 && LateenRig.Drive(def, float.NaN) == 1 && mid < 0.05 && half > mid && half < 1,
                $"her sails drive her fully on either tack; brailed up through a dip they don't (mid-dip {mid:0.00}, a tenth in {half:0.00})");
            double drive = Sailing.TargetSpeed(-0.3, 0, Math.PI / 2 * 0, 1, def.MaxSpeed, def.Sailing);
            double driveOther = Sailing.TargetSpeed(0.3, 0, Math.PI / 2 * 0, 1, def.MaxSpeed, def.Sailing);
            Check(Math.Abs(drive - driveOther) < 1e-9 && drive > 0, $"she makes the same way on either tack, the wind on her beam ({drive:0.00} b/s)");
        }
    }
}
