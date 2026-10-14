using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// Laying the guns (Hansa 0.3.6, the gun captain's seat): each gun's limits, rates and moving parts; the lay held
        /// within its limits and laid at its crew's pace; the lay toward where someone looks, from their view, whatever her
        /// pose; the muzzle and the shot's way at the corners of the lay range as she lies tilted; the moving parts
        /// composed as the renderer composes them put the barrel's tip (from its mesh) where the shot leaves, and the gun
        /// captain where the seat says, run out and run back; and at every corner of the lay range, run out and run back,
        /// the gun clear of her side round its port, her fittings (the capstan, the pump, the hatches, the companionway,
        /// the stairs, the gates swung open), the main channels, and its neighbours however they're laid.
        /// </summary>
        static void GunLays(ShipDefinition def, string assets)
        {
            Console.WriteLine("gun laying: its limits and pace, laid where you look, the muzzle laid, the moving parts, the clearances");
            var guns = def.Fixtures.Where(f => f.Type == "gun").ToArray();
            var sg = guns.First(f => f.Out[2] > 0);
            MovingPartDef P(string code) => Array.Find(def.MovingParts, p => p.Code == code);
            int Order(string code) => Array.FindIndex(def.MovingParts, p => p.Code == code);

            // ---- the buttons (the eighth client test: "reversing right to left"): hold right to lay, left-click fires
            {
                var right = Vintagestory.API.Common.EnumEntityAction.InWorldRightMouseDown;
                var left = Vintagestory.API.Common.EnumEntityAction.InWorldLeftMouseDown;
                string lang = System.IO.File.ReadAllText(System.IO.Path.Combine(assets, "lang", "en.json"));
                bool hints = lang.Contains("\"hint-gun\": \"Hold right-click on the {0} to lay it where you look") && lang.Contains("left-click: fire · Shift + right-click: house it") &&
                             lang.Contains("\"hud-gun-keys\": \"Hold right and move the view: lay it (from where it's laid) · Left-click: fire (while laying too)") &&
                             lang.Contains("\"hud-gun-keys-seat\": \"Hold right and move the view: lay it (from where it's laid) · Left-click: fire (while laying too)");
                Check(GunButtons.Lays(right) && !GunButtons.Lays(left) && GunButtons.Fires(left) && !GunButtons.Fires(right) &&
                      GunButtons.LayHeld(false, true) && !GunButtons.LayHeld(true, false) && hints,
                    "by a gun: holding right mouse lays it, a left-click fires it, letting go of right stops laying; the hints say so");
            }

            // ---- what each gun says about laying it, and its moving parts in order
            {
                string bad = "";
                foreach (var f in guns)
                {
                    GunLaying.Limits(f, out float t0, out float t1, out float e0, out float e1);
                    var tr = P(f.Code + "-train");
                    var sl = P(f.Code);
                    var br = P(f.Code + "-barrel");
                    var trucks = def.MovingParts.Where(q => q.Parent == f.Code && q.Code.StartsWith(f.Code + "-trucks-")).ToList();
                    var breeching = def.MovingParts.Where(q => q.Kind == "follow" && q.Code.StartsWith(f.Code + "-breeching-")).ToList();
                    bool ok = f.Pivot != null && f.Trunnion != null && f.Seat != null && f.Port?.Length == 4 && f.Wall?.Length == 4 &&
                              t0 < 0 && t1 >= 0 && t0 >= -12 && t1 <= 12 && e0 < 0 && e1 > 0 && e0 >= -15 && e1 <= 15 && f.TrainRate > 0 && f.ElevationRate > 0 &&
                              tr?.Kind == "rotate" && tr.Source == "train:" + f.Code && tr.Parent == null && Math.Abs(tr.AxisDir[0]) + Math.Abs(tr.AxisDir[2]) < 1e-9 &&
                              sl?.Kind == "slide" && sl.Source == "fire:" + f.Code && sl.Parent == tr.Code &&
                              br?.Kind == "rotate" && br.Source == "elevate:" + f.Code && br.Parent == sl.Code &&
                              // (0.3.8, the user's note 45: the breeching no longer runs through the cheek and the breech: from each
                              // ring bolt on her side to an eyebolt on that cheek, going with the carriage, and from there round the
                              // cascabel, its end on the cheek going with the carriage and the other with the barrel)
                              trucks.Count == 2 && breeching.Count == 4 &&
                              breeching.Count(b => b.Code.EndsWith("-out") && b.Follow == sl.Code && b.AnchorFollow == null) == 2 &&
                              breeching.Count(b => !b.Code.EndsWith("-out") && b.Follow == br.Code && b.AnchorFollow == sl.Code) == 2 &&
                              Order(tr.Code) < Order(sl.Code) && Order(sl.Code) < Order(br.Code) &&
                              trucks.All(q => Order(sl.Code) < Order(q.Code));
                    if (!ok) bad += " " + f.Code;
                }
                string limits = string.Join(", ", guns.Select(f => $"{f.Code} {f.TrainLimits[0]:0.#}..{f.TrainLimits[1]:+0.#;-0.#}°"));
                Check(bad.Length == 0, $"each gun trains about the upright in its port and elevates about its trunnions, within its limits ({limits}; " +
                    $"elevation {sg.ElevationLimits[0]:0.#}..{sg.ElevationLimits[1]:+0.#}°), {sg.TrainRate:0}°/s and {sg.ElevationRate:0}°/s; its moving parts: " +
                    $"the turn, the carriage running back on it, the barrel on that, the trucks; the breeching from her side to the cheeks follows the carriage, " +
                    $"from the cheeks round the cascabel the barrel{(bad.Length > 0 ? ": not" + bad : "")}");
            }

            // ---- held within its limits, laid at its crew's pace
            {
                GunLaying.Limits(sg, out float t0, out float t1, out float e0, out float e1);
                var hi = GunLaying.Clamp(sg, new GunLay(40, 30));
                var lo = GunLaying.Clamp(sg, new GunLay(-40, -30));
                var mid = GunLaying.Clamp(sg, new GunLay(3, 2));
                bool clamps = hi.Train == t1 && hi.Elevation == e1 && lo.Train == t0 && lo.Elevation == e0 && mid.Train == 3 && mid.Elevation == 2 &&
                              GunLaying.Beyond(sg, new GunLay(40, 0)) && !GunLaying.Beyond(sg, mid);
                // laid toward far beyond its limits from square, a twentieth of a second at a time
                var lay = new GunLay(0, 0);
                double tTrain = -1, tElev = -1, after05Train = 0, after05Elev = 0, overshoot = 0;
                for (int i = 1; i <= 80; i++)
                {
                    lay = GunLaying.Step(sg, lay, new GunLay(40, 30), 0.05);
                    double t = i * 0.05;
                    if (i == 10) { after05Train = lay.Train; after05Elev = lay.Elevation; }
                    if (tTrain < 0 && lay.Train >= t1) tTrain = t;
                    if (tElev < 0 && lay.Elevation >= e1) tElev = t;
                    overshoot = Math.Max(overshoot, Math.Max(lay.Train - t1, lay.Elevation - e1));
                }
                var back = GunLaying.Step(sg, new GunLay(t1, e1), new GunLay(t1 - 0.3, e1 - 0.2), 0.05);
                Check(clamps && Math.Abs(after05Train - 0.5 * sg.TrainRate) < 1e-3 && Math.Abs(after05Elev - 0.5 * sg.ElevationRate) < 1e-3 &&
                      Math.Abs(tTrain - Math.Ceiling(t1 / sg.TrainRate / 0.05 - 1e-6) * 0.05) < 1e-6 && Math.Abs(tElev - Math.Ceiling(e1 / sg.ElevationRate / 0.05 - 1e-6) * 0.05) < 1e-6 &&
                      overshoot <= 0 && Math.Abs(back.Train - (t1 - 0.3)) < 1e-5 && Math.Abs(back.Elevation - (e1 - 0.2)) < 1e-5,
                    $"laid far beyond its limits it stops at them ({hi.Train:0}°, {hi.Elevation:0}°; {lo.Train:0}°, {lo.Elevation:0}°), turning {after05Train:0.0}° and " +
                    $"rising {after05Elev:0.0}° in half a second: at its limits after {tTrain:0.00} s and {tElev:0.00} s, never past them; a small change it makes at once");
            }

            // ---- the lay toward where someone looks, and back: from her frame and from a view as she lies tilted
            {
                double worstDir = 0, worstView = 0;
                foreach (var f in guns)
                {
                    GunLaying.Limits(f, out float t0, out float t1, out float e0, out float e1);
                    foreach (double t in new double[] { t0, t0 / 2, 0, t1 / 3, t1 })
                        foreach (double e in new double[] { e0, 0, 1.5, e1 })
                        {
                            var lay = new GunLay(t, e);
                            var d = GunLaying.Dir(f, lay);
                            var back = GunLaying.FromLocalDir(f, d.X * 3, d.Y * 3, d.Z * 3);
                            worstDir = Math.Max(worstDir, Math.Abs(back.Train - t) + Math.Abs(back.Elevation - e));
                            foreach (var (yaw, pitch, roll) in new[] { (Math.PI / 2, 0.0, 0.0), (0.3, 0.05, -0.2), (2.0, -0.12, 0.3), (4.4, 0.2, 0.44) })
                            {
                                GunLaying.Muzzle(f, lay, yaw, pitch, roll, out _, out var wd);
                                GunLaying.LookAlong(wd, out float vy, out float vp);
                                var seen = GunLaying.FromView(f, vy, vp, yaw, pitch, roll);
                                worstView = Math.Max(worstView, Math.Abs(seen.Train - t) + Math.Abs(seen.Elevation - e));
                            }
                        }
                }
                // a gun on her starboard side, trained toward the bow and up: its barrel points forward of square and up
                var ds = GunLaying.Dir(sg, new GunLay(5, 3));
                var pg = guns.First(f => f.Out[2] < 0);
                var dp = GunLaying.Dir(pg, new GunLay(5, 3));
                Check(worstDir < 1e-3 && worstView < 0.01 && ds.X > 0.08 && ds.Z > 0.9 && Math.Abs(Math.Asin(ds.Y) * 180 / Math.PI - 3) < 1e-3 && dp.X > 0.08 && dp.Z < -0.9,
                    $"laid toward where someone looks: the lay that points the barrel along a way and the way it points agree (off by {worstDir:0.0e0}°), and from a view as " +
                    $"she lies pitched and rolled (off by {worstView:0.0e0}°); trained 5° to the bow both sides' guns point forward of square ({ds.X:0.000}, {dp.X:0.000})");
            }

            // ---- the muzzle at the corners of the lay range as she lies tilted, as she's drawn
            {
                double worst = 0, radius = 0, radiusSpread = 0;
                double minR = double.MaxValue, maxR = 0;
                foreach (var f in guns)
                    foreach (var lay in Corners(f))
                    {
                        GunLaying.MuzzleLocal(f, lay, out var ml, out var dl);
                        var tr = GunLaying.TrunnionOf(f);
                        // the muzzle turns about the trunnions and the upright: it stays as far from the trunnions' line
                        double r = Math.Sqrt(Math.Pow(ml.X - GunLaying.Place(f, lay, 0, tr, false).X, 2) + Math.Pow(ml.Z - GunLaying.Place(f, lay, 0, tr, false).Z, 2) +
                                             Math.Pow(ml.Y - tr.Y, 2));
                        minR = Math.Min(minR, r);
                        maxR = Math.Max(maxR, r);
                        foreach (var (yaw, pitch, roll) in new[] { (Math.PI / 2, 0.0, 0.0), (0.3, 0.05, -0.2), (2.0, -0.12, 0.3), (4.4, 0.2, 0.44) })
                        {
                            GunLaying.Muzzle(f, lay, yaw, pitch, roll, out var mw, out var dw);
                            var model = new Matrixf().Identity().RotateY(ShipFrame.ModelYaw(yaw)).RotateZ((float)pitch).RotateX((float)roll);
                            var a = model.TransformVector(new Vec4f((float)ml.X, (float)ml.Y, (float)ml.Z, 1));
                            var c = model.TransformVector(new Vec4f((float)(ml.X + dl.X), (float)(ml.Y + dl.Y), (float)(ml.Z + dl.Z), 1));
                            worst = Math.Max(worst, Math.Max(Math.Abs(a.X - mw.X) + Math.Abs(a.Y - mw.Y) + Math.Abs(a.Z - mw.Z),
                                Math.Abs(c.X - a.X - dw.X) + Math.Abs(c.Y - a.Y - dw.Y) + Math.Abs(c.Z - a.Z - dw.Z)));
                        }
                    }
                radius = maxR;
                radiusSpread = maxR - minR;
                // level and heading east, a starboard gun laid fully up and trained full forward fires that way; rolled 10° to
                // starboard it fires 10° lower
                GunLaying.Limits(sg, out _, out float st1, out _, out float se1);
                GunLaying.Muzzle(sg, new GunLay(st1, se1), Math.PI / 2, 0, 0, out _, out var d0);
                GunLaying.Muzzle(sg, new GunLay(st1, se1), Math.PI / 2, 0, 10 * Math.PI / 180, out _, out var dRoll);
                double el(Vec3d v) => Math.Asin(v.Y) * 180 / Math.PI;
                // heading east (yaw π/2) her bow is +x in the world: trained toward it, the shot goes a little east of south
                double az = Math.Atan2(d0.X, d0.Z) * 180 / Math.PI;
                Check(worst < 1e-4 && radiusSpread < 1e-6 && Math.Abs(el(d0) - se1) < 1e-3 && Math.Abs(el(dRoll) - (se1 - 10)) < 0.5,
                    $"at every corner of the lay range the shot leaves the muzzle as she's drawn, turned, pitched and rolled (off by at most {worst:0.0e0}); the muzzle " +
                    $"swings {radius:0.00} from the trunnions; heading east, laid {se1:0.#}° up and {st1:0.#}° to the bow it fires {el(d0):0.0}° up, {az:0.0}° from north " +
                    $"(rolled 10° to starboard: {el(dRoll):0.0}°)");
            }

            // ---- the moving parts composed as the renderer composes them: the barrel's tip (its mesh) is where the shot leaves
            var skin = LoadSkinFile(def, assets);
            {
                double worstTip = 0, worstDir = 0, worstSeat = 0, restTip = 0;
                foreach (var f in guns)
                {
                    GunLaying.OutOf(f, out double ox, out double oz);
                    // the muzzle's face: the barrel's mesh furthest out along the way it points
                    var verts = Vertices(skin, "part:" + f.Code + "-barrel");
                    double far = verts.Max(v => v[0] * ox + v[2] * oz);
                    var face = verts.Where(v => v[0] * ox + v[2] * oz > far - 1e-3).ToList();
                    var tip = new[] { face.Average(v => v[0]) + ox * 0.05, face.Average(v => v[1]), face.Average(v => v[2]) + oz * 0.05 };
                    restTip = Math.Max(restTip, Dist(tip, f.Muzzle));
                    var seat0 = f.Seat;
                    foreach (var lay in Corners(f))
                        foreach (float recoil in new[] { 0f, 1f })
                        {
                            var mats = GunParts(def, f, lay, recoil);
                            double back = recoil * P(f.Code).Distance;
                            GunLaying.MuzzleLocal(f, lay, out var mz, out var dir, back);
                            var mb = mats[f.Code + "-barrel"];
                            worstTip = Math.Max(worstTip, Dist(Apply(mb, tip), new[] { mz.X, mz.Y, mz.Z }));
                            var dv = new[] { mb[0] * ox + mb[8] * oz, mb[1] * ox + mb[9] * oz, mb[2] * ox + mb[10] * oz };
                            worstDir = Math.Max(worstDir, Dist(dv, new[] { dir.X, dir.Y, dir.Z }));
                            // (the gun captain steps back with it once it has come back as far as he stood clear)
                            var st = GunLaying.SeatLocal(f, lay, back);
                            var stepped = GunParts(def, f, lay, (float)(Math.Max(0, back - GunLaying.SeatSlack) / P(f.Code).Distance));
                            worstSeat = Math.Max(worstSeat, Dist(Apply(stepped[f.Code], seat0), new[] { st.X, st.Y, st.Z }));
                        }
                }
                Check(restTip < 0.01 && worstTip < 2e-3 && worstDir < 1e-4 && worstSeat < 2e-3,
                    $"the moving parts composed as the renderer composes them (the turn, the carriage run back on it, the barrel elevated on that) put the barrel's tip " +
                    $"from its mesh where the shot leaves, at every corner, run out and run back (off by {worstTip:0.0e0}; its way by {worstDir:0.0e0}; laid square the mesh's " +
                    $"tip is {restTip:0.000} from the fixture's muzzle), and the gun captain where the seat says (off by {worstSeat:0.0e0})");
            }

            // ---- the clearances: at every corner, run out and run back
            {
                var g = def.Grid;
                // the gates: where each one's leaf is as it swings from shut to wide open (each step's turn back, to test a
                // point against the shut leaf's box)
                var gates = def.Fixtures.Where(f => f.Type == "gate" && f.Box != null && P(f.Code) != null).Select(f =>
                {
                    var part = P(f.Code);
                    var back = Enumerable.Range(0, 26).Select(k =>
                    {
                        var m = new float[16];
                        PartMotion.PartMatrix(part, -PartMotion.TurnDegrees(part, k / 25f) * GameMath.DEG2RAD, m);
                        return m;
                    }).ToArray();
                    return (f.Code, f.Box, back);
                }).ToList();
                var shrouds = def.Ladders.FirstOrDefault(l => l.Code.StartsWith("main-") && l.KindOrDefault == "ratlines");
                // her collision as a ship's is (the guns' boxes in it), for where the gun captain stands
                var solid = g.Clone();
                ShipFittings.ApplySolids(def, solid, _ => false, _ => null);
                string[] groups = { "", "-barrel", "-trucks-fore", "-trucks-aft" };
                var shape = guns.ToDictionary(f => f.Code, f => groups.Select(s => SamplePoints(skin, "part:" + f.Code + s, 0.02)).ToArray());
                int poses = 0, points = 0;
                var hits = new List<string>();
                int seatsOk = 0, seatsTried = 0, muzzlesOut = 0, shotsFree = 0, muzzlesTried = 0;
                foreach (var f in guns)
                {
                    var side = new Dictionary<string, int>();
                    void Hit(string what) => side[what] = side.GetValueOrDefault(what) + 1;
                    GunLaying.OutOf(f, out double ox, out double oz);
                    double deck = f.Pos[1], rail = deck + 1.0;
                    // (a gun beside a gate: laid as far as it goes with the gate shut, and as far as it goes while it's open)
                    foreach (var lay in Corners(f).Concat(f.Gate != null ? Corners(f, true) : Enumerable.Empty<GunLay>()))
                        // (run back a step at a time: its muzzle comes back in through the port)
                        foreach (float recoil in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                        {
                            poses++;
                            // trained further than it may with its gate open, that gate is shut (it won't open: EntityShip.GunInGateway)
                            bool gateShut = f.Gate != null && GunLaying.Beyond(f, lay, true);
                            var mats = GunParts(def, f, lay, recoil);
                            for (int gi = 0; gi < groups.Length; gi++)
                            {
                                var m = mats[f.Code + groups[gi]];
                                foreach (var p0 in shape[f.Code][gi])
                                {
                                    var p = Apply(m, p0);
                                    points++;
                                    double depth = p[0] * ox + p[2] * oz;
                                    double u = (p[1] - f.Port[1]) / (f.Port[3] - f.Port[1]);
                                    double inner = f.Wall[0] + (f.Wall[1] - f.Wall[0]) * u, outer = f.Wall[2] + (f.Wall[3] - f.Wall[2]) * u;
                                    // her side round the port: inside its thickness, only through the port (a hair clear of its sides)
                                    // (a port up to the rail is a notch open at the top)
                                    if (depth > inner - 0.01 && depth < outer + 0.01 && p[1] < rail &&
                                        !(p[0] > f.Port[0] + 0.01 && p[0] < f.Port[2] - 0.01 && p[1] > f.Port[1] + 0.01 && (p[1] < f.Port[3] - 0.01 || f.Port[3] >= rail - 1e-6)))
                                        Hit(p[1] < f.Port[1] + 0.01 ? "the side under the port" : p[1] > f.Port[3] - 0.01 ? "the port's head" : "the port's side");
                                    // outside her: clear of the main channels
                                    if (depth > outer + 0.01 && shrouds != null && Math.Abs(p[0] - shrouds.Bottom[0]) < 1.8 && p[1] > shrouds.Bottom[1] - ChannelDepth) Hit("the main channel");
                                    // nothing of her own (her planking aside: the port is cut in it), above the deck it stands on
                                    if (p[1] > deck + 0.02 && depth < inner - 0.01)
                                    {
                                        // (her collision is in quarter-block cells, which can fall short of what they stand for,
                                        // like a post's face between two cells' edges: away from her side, where nothing should
                                        // come near the gun, a point within half a cell of a solid one counts)
                                        double pad = depth < inner - 0.3 ? 0.125 : 0;
                                        int j = g.CellY(p[1]);
                                        bool solidNear = false;
                                        foreach (double dx in new[] { -pad, 0, pad })
                                            foreach (double dz in new[] { -pad, 0, pad })
                                            {
                                                int i = g.CellX(p[0] + dx), k = g.CellZ(p[2] + dz);
                                                if (g.Solid(i, j, k) && !def.PartMap.IsShell(i, j, k)) solidNear = true;
                                            }
                                        if (solidNear) Hit($"her collision near {p[0]:0.00},{p[1]:0.00},{p[2]:0.00}");
                                    }
                                    // no other fixture (the capstan, the pump, the hatches, the companionway; not the guns) or a gate swung open
                                    foreach (var o in def.Fixtures)
                                        if (o.Type != "gun" && (Inside(p, o.Box) || Inside(p, o.Sel))) Hit(o.Code);
                                    foreach (var (code, box, back) in gates)
                                        if (!(gateShut && code == f.Gate) && back.Any(m => Inside(Apply(m, p), box))) Hit(code + " (swinging)");
                                }
                            }
                            // the gun captain's place: on the deck, room to stand there
                            var st = GunLaying.SeatLocal(f, lay, recoil * P(f.Code).Distance);
                            seatsTried++;
                            if (!solid.Collides(st.X - DeckMover.HalfWidth, st.Y + 0.01, st.Z - DeckMover.HalfWidth, st.X + DeckMover.HalfWidth, st.Y + DeckMover.Height, st.Z + DeckMover.HalfWidth) &&
                                DeckMover.HasFloor(solid, st.X, st.Y, st.Z)) seatsOk++;
                            else Hit($"no room to stand at {st.X:0.00},{st.Y:0.00},{st.Z:0.00}");
                            if (recoil == 0)
                            {
                                // its muzzle out through the port past her planking, the shot's way out clear of her
                                muzzlesTried++;
                                GunLaying.MuzzleLocal(f, lay, out var mz, out var dir);
                                if (mz.X * ox + mz.Z * oz > Math.Max(f.Wall[2], f.Wall[3]) + 0.1) muzzlesOut++;
                                if (g.Raycast(mz, dir, 40) > 40) shotsFree++;
                            }
                        }
                    if (side.Count > 0) hits.Add(f.Code + ": " + string.Join(", ", side.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} ({kv.Value})")));
                }
                Check(hits.Count == 0 && seatsOk == seatsTried,
                    $"laid at every corner of its range, run out and run back ({poses} poses, {points} points of its mesh), each gun is clear of her side round its port, her " +
                    $"fittings, the stairs and companionway, the gates swung open and the main channels; the gun captain has room to stand behind it ({seatsOk} of {seatsTried})" +
                    (hits.Count > 0 ? ": " + string.Join("; ", hits) : ""));
                Check(muzzlesOut == muzzlesTried && shotsFree == muzzlesTried,
                    $"at every corner its muzzle stands out of the port past her planking ({muzzlesOut} of {muzzlesTried}), and its shot's way out is clear of her ({shotsFree})");

                // its neighbours, however each is laid: at every pair of corners, run out and run back
                var cells = new Dictionary<(string, int), HashSet<long>>();
                HashSet<long> CellsOf(FixtureDef f, int pose)
                {
                    if (cells.TryGetValue((f.Code, pose), out var set)) return set;
                    var lays = Corners(f).ToArray();
                    var mats = GunParts(def, f, lays[pose / 2], pose % 2);
                    set = new HashSet<long>();
                    for (int gi = 0; gi < groups.Length; gi++)
                    {
                        var m = mats[f.Code + groups[gi]];
                        foreach (var p0 in shape[f.Code][gi]) set.Add(CellKey(Apply(m, p0), 0.04));
                    }
                    return cells[(f.Code, pose)] = set;
                }
                int pairs = 0, combos = 0;
                var fouled = new List<string>();
                foreach (var row in guns.GroupBy(f => Math.Sign(f.Out[2])))
                {
                    var byX = row.OrderBy(f => f.Pos[0]).ToArray();
                    for (int i = 0; i + 1 < byX.Length; i++)
                    {
                        var a = byX[i];
                        var b = byX[i + 1];
                        if (b.Pos[0] - a.Pos[0] > 4) continue;
                        pairs++;
                        int n = Corners(a).Count() * 2, worstA = -1, worstB = -1;
                        for (int pa = 0; pa < n; pa++)
                            for (int pb = 0; pb < n; pb++)
                            {
                                combos++;
                                var sa = CellsOf(a, pa);
                                var sb = CellsOf(b, pb);
                                if ((sa.Count < sb.Count ? sa.Any(sb.Contains) : sb.Any(sa.Contains)) && worstA < 0) { worstA = pa; worstB = pb; }
                            }
                        if (worstA >= 0)
                        {
                            var la = Corners(a).ToArray()[worstA / 2];
                            var lb = Corners(b).ToArray()[worstB / 2];
                            fouled.Add($"{a.Code} ({la}{(worstA % 2 == 1 ? ", run back" : "")}) and {b.Code} ({lb}{(worstB % 2 == 1 ? ", run back" : "")})");
                        }
                    }
                }
                Check(fouled.Count == 0, $"neighbouring guns ({pairs} pairs, {combos} pairs of corners, each run out or back) never meet{(fouled.Count > 0 ? ": " + string.Join("; ", fouled) : "")}");
            }
        }

        /// <summary>
        /// The corners of a gun's lay range and the middles of its sides (trained aft, square, forward; elevated down, level,
        /// up); with the gate beside it standing open, its narrower range then.
        /// </summary>
        static IEnumerable<GunLay> Corners(FixtureDef f, bool gateOpen = false)
        {
            GunLaying.Limits(f, out float t0, out float t1, out float e0, out float e1, gateOpen);
            foreach (float t in new[] { t0, 0f, t1 })
                foreach (float e in new[] { e0, 0f, e1 })
                    yield return new GunLay(t, e);
        }

        /// <summary>A gun's moving parts placed as the renderer places them (PartMotion.Place, parents first), laid so and run back so far (0..1).</summary>
        static Dictionary<string, float[]> GunParts(ShipDefinition def, FixtureDef gun, GunLay lay, float recoil)
        {
            var mats = new Dictionary<string, float[]>();
            foreach (var p in def.MovingParts)
            {
                if (p.Kind == "follow" || !(p.Code == gun.Code || p.Code.StartsWith(gun.Code + "-"))) continue;
                float v = p.Source == "train:" + gun.Code ? lay.Train : p.Source == "elevate:" + gun.Code ? lay.Elevation : p.Source == "fire:" + gun.Code ? recoil : 0;
                var m = new float[16];
                PartMotion.Place(p, v, m, mats);
                mats[p.Code] = m;
            }
            return mats;
        }

        /// <summary>Every corner of a skin group's quads (ship-local).</summary>
        static List<double[]> Vertices(ShipSkinFile skin, string group)
        {
            var list = new List<double[]>();
            if (skin.Groups.TryGetValue(group, out var quads))
                foreach (var q in quads)
                    for (int c = 0; c < 4; c++) list.Add(new double[] { q[1 + 8 * c], q[2 + 8 * c], q[3 + 8 * c] });
            return list;
        }

        /// <summary>Points over a skin group's quads, about `spacing` apart (each quad filled bilinearly).</summary>
        static List<double[]> SamplePoints(ShipSkinFile skin, string group, double spacing)
        {
            var pts = new List<double[]>();
            if (!skin.Groups.TryGetValue(group, out var quads)) return pts;
            foreach (var q in quads)
            {
                double[] C(int c) => new double[] { q[1 + 8 * c], q[2 + 8 * c], q[3 + 8 * c] };
                double[] a = C(0), b = C(1), c2 = C(2), d = C(3);
                int nu = Math.Clamp((int)Math.Ceiling(Math.Max(Dist(a, b), Dist(d, c2)) / spacing), 1, 80);
                int nv = Math.Clamp((int)Math.Ceiling(Math.Max(Dist(a, d), Dist(b, c2)) / spacing), 1, 80);
                for (int i = 0; i <= nu; i++)
                    for (int j = 0; j <= nv; j++)
                    {
                        double u = i / (double)nu, v = j / (double)nv;
                        var p = new double[3];
                        for (int k = 0; k < 3; k++) p[k] = (1 - u) * (1 - v) * a[k] + u * (1 - v) * b[k] + u * v * c2[k] + (1 - u) * v * d[k];
                        pts.Add(p);
                    }
            }
            return pts;
        }

        static bool Inside(double[] p, double[] box) =>
            box != null && p[0] > box[0] && p[0] < box[3] && p[1] > box[1] && p[1] < box[4] && p[2] > box[2] && p[2] < box[5];

        /// <summary>The cell of a cube grid (side v) a point is in, as one number.</summary>
        static long CellKey(double[] p, double v)
        {
            long i = (long)Math.Floor(p[0] / v) & 0x1FFFFF, j = (long)Math.Floor(p[1] / v) & 0x1FFFFF, k = (long)Math.Floor(p[2] / v) & 0x1FFFFF;
            return i | j << 21 | k << 42;
        }
    }
}
