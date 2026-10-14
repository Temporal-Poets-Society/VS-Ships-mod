using System;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>How far under the shrouds' foot (the chain plates' top) the main channel reaches down (blocks).</summary>
        const double ChannelDepth = 0.2;
        /// <summary>
        /// The guns (Hansa 0.3.5, item 16): where they stand and that they're clear of everything on deck run out and run
        /// back; the muzzle's frame as she lies (tilted, it's where the drawn muzzle is); the ball's flight (range, drag,
        /// the same path however it's stepped, the water, a ricochet); a ball's path meeting a hull, holing it and going on
        /// through her; the recoil; the gun block's shape; and how many hits below the waterline sink her how fast.
        /// </summary>
        static void Cannons(ShipDefinition def, string assets)
        {
            Console.WriteLine("cannons: the guns, their shot, what it does");
            var d = def.Cannon;
            var g = def.Grid;
            var guns = def.Fixtures.Where(f => f.Type == "gun").ToArray();
            Check(guns.Length >= 6 && guns.Count(f => f.Out[2] < 0) * 2 == guns.Length && guns.Count(f => f.Out[2] > 0) * 2 == guns.Length &&
                  guns.All(f => f.Material == "bronze" && f.Muzzle != null && f.Box != null && f.Sel != null && f.Shot > 3 && f.Bore > 0.1),
                $"{guns.Length / 2} bronze guns a side on the main deck ({string.Join(", ", guns.Select(f => f.Code))}), each with its muzzle, shot, and the box it stands in");
            // its report: a mono sound (the game places it and it fades with distance; a stereo one plays unplaced, as loud
            // everywhere: vanilla's medium explosion till 0.3.7), heard far
            {
                string vs = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? System.IO.Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? "", "Vintagestory");
                // ((0.3.25) our own short shots, Report + 1..ReportVariants, in the mod's assets)
                string dom = d.Report.Contains(':') ? d.Report.Substring(0, d.Report.IndexOf(':')) : "game", path = d.Report.Substring(d.Report.IndexOf(':') + 1);
                for (int v = 1; v <= Math.Max(1, d.ReportVariants); v++)
                {
                    string name = path + (d.ReportVariants > 0 ? v.ToString() : "") + ".ogg";
                    string file = (dom == "hansa" ? new[] { System.IO.Path.Combine(assets, name) } :
                        new[] { "survival", "game" }.Select(x => System.IO.Path.Combine(vs, "assets", x, name))).FirstOrDefault(System.IO.File.Exists);
                    int channels = -1;
                    long bytes = 0;
                    if (file != null)
                    {
                        var b = System.IO.File.ReadAllBytes(file);
                        bytes = b.Length;
                        int j = System.Text.Encoding.ASCII.GetString(b, 0, Math.Min(b.Length, 512)).IndexOf("\u0001vorbis", StringComparison.Ordinal);
                        if (j >= 0 && j + 11 < b.Length) channels = b[j + 11];
                    }
                    Check(channels == 1 && d.ReportRange >= 150 && d.ReportVolume > 0 && bytes > 2000,
                        $"its report {v} ({name}, {bytes} bytes) is a mono sound ({channels} channel{(channels == 1 ? "" : "s")}): placed at the gun and fading with distance, heard out to {d.ReportRange:0} blocks");
                }
            }
            MovingPartDef P(string code) => Array.Find(def.MovingParts, p => p.Code == code);
            Check(guns.All(f => P(f.Code) is MovingPartDef m && m.Kind == "slide" && m.Source == "fire:" + f.Code && m.Distance >= 1 && m.Distance <= 1.5 &&
                                m.AxisDir[2] * f.Out[2] < -0.99 &&
                                def.MovingParts.Count(q => q.Parent == f.Code && q.Kind == "rotate" && q.Code.StartsWith(f.Code + "-trucks-")) == 2 &&
                                def.MovingParts.Count(q => q.Follow == f.Code + "-barrel" && q.Kind == "follow") == 2),
                "each runs back 1..1.5 blocks inboard when fired (its own moving part), its two pairs of trucks turn on it, its breeching follows its barrel");

            // ---- where they stand: run out and all the way back, clear of the rest of her
            var gateOpen = def.Fixtures.Where(f => f.Type == "gate").Select(f => OpenBox(def, f)).ToList();
            int clear = 0;
            string why = "";
            foreach (var f in guns)
            {
                var m = P(f.Code);
                double back = m.Distance;
                var b = f.Box;
                // the space it sweeps: its box from run out to run back, a little shrunk
                double z0 = Math.Min(b[2], b[2] + m.AxisDir[2] * back), z1 = Math.Max(b[5], b[5] + m.AxisDir[2] * back);
                var swept = new[] { b[0] + 0.05, b[1] + 0.05, z0 + 0.05, b[3] - 0.05, b[4] - 0.05, z1 - 0.05 };
                // nothing of her own there but the hull's planking (the ceiling it stands against)
                int solid = 0;
                for (int i = g.CellX(swept[0]); i <= g.CellX(swept[3]); i++)
                    for (int j = g.CellY(swept[1] + 0.1); j <= g.CellY(swept[4]); j++)
                        for (int k = g.CellZ(swept[2]); k <= g.CellZ(swept[5]); k++)
                            if (g.Solid(i, j, k) && !def.PartMap.IsShell(i, j, k)) solid++;
                // no other fixture's box or place (the capstan, the pump, the hatches, the companionway, a gate swung open)
                var hits = def.Fixtures.Where(o => o != f && (Overlaps(swept, o.Box) || o.Type != "gun" && Overlaps(swept, o.Sel))).Select(o => o.Code).ToList();
                hits.AddRange(gateOpen.Where(x => Overlaps(swept, x.Box)).Select(x => x.Code + " (open)"));
                // the deck under it, and its port clear of the main shrouds' channel
                bool deck = DeckMover.HasFloor(g, (b[0] + b[3]) / 2, def.DeckY, (z0 + z1) / 2);
                var shrouds = def.Ladders.FirstOrDefault(l => l.Code.StartsWith("main-") && l.KindOrDefault == "ratlines");
                // (0.3.10: the mainmast at x 2.5 has guns either side of its channels: they're at the rail, over the ports)
                bool channel = shrouds == null || Math.Abs(f.Pos[0] - shrouds.Bottom[0]) > 1.8 + 0.375 || f.Port?.Length >= 4 && f.Port[3] <= shrouds.Bottom[1] - ChannelDepth;
                if (solid == 0 && hits.Count == 0 && deck && channel && f.Pos[1] == def.DeckY) clear++;
                else why += $" {f.Code}: {solid} cells, {string.Join("/", hits)}{(deck ? "" : ", no deck")}{(channel ? "" : ", at the channel")};";
            }
            Check(clear == guns.Length, $"run out and run back, each gun is clear of her fittings (the capstan, pump, hatches, companionway, stairs, gallows, the gates swung open), " +
                                        $"on the deck, its port clear of the main shrouds' channels ({clear} of {guns.Length}){why}");

            // ---- the muzzle out through the port, the shot clear of her own hull
            int outside = 0, free = 0;
            foreach (var f in guns)
            {
                Ballistics.MuzzleLocal(f, f.Elevation, out var mz, out var dir);
                bool outOfHull = !g.SolidAt(mz.X, mz.Y, mz.Z) && !(def.DryMask?.SolidAt(mz.X, mz.Y, mz.Z) ?? false) &&
                                 Math.Abs(mz.Z) > Math.Abs(Array.Find(def.MovingParts, p => p.Follow == f.Code + "-barrel").Anchor[2]);
                if (outOfHull) outside++;
                // from the muzzle out, and back along the barrel to the port: nothing of hers in the way
                if (g.Raycast(mz, dir, 40) > 40) free++;
            }
            Check(outside == guns.Length && free == guns.Length, $"each muzzle stands out through its port past her planking, and its shot's way out is clear of her ({outside}, {free})");

            // ---- the muzzle's frame as she lies: where the drawn muzzle is, whatever her pitch and roll
            var sg = guns.First(f => f.Out[2] > 0);
            var pg = guns.First(f => f.Out[2] < 0);
            double worst = 0;
            foreach (var (yaw, pitch, roll) in new[] { (Math.PI / 2, 0.0, 0.0), (0.3, 0.05, -0.2), (2.0, -0.12, 0.3), (4.4, 0.2, 0.44) })
                foreach (var f in guns)
                {
                    Ballistics.Muzzle(f, f.Elevation, yaw, pitch, roll, out var mw, out var dw);
                    // the renderer's model matrix (ShipRenderer.SetModel) takes the muzzle and a point along the barrel there
                    var model = new Matrixf().Identity().RotateY(ShipFrame.ModelYaw(yaw)).RotateZ((float)pitch).RotateX((float)roll);
                    Ballistics.MuzzleLocal(f, f.Elevation, out var ml, out var dl);
                    var a = model.TransformVector(new Vec4f((float)ml.X, (float)ml.Y, (float)ml.Z, 1));
                    var c = model.TransformVector(new Vec4f((float)(ml.X + dl.X), (float)(ml.Y + dl.Y), (float)(ml.Z + dl.Z), 1));
                    worst = Math.Max(worst, Math.Max(Math.Abs(a.X - mw.X) + Math.Abs(a.Y - mw.Y) + Math.Abs(a.Z - mw.Z),
                        Math.Abs(c.X - a.X - dw.X) + Math.Abs(c.Y - a.Y - dw.Y) + Math.Abs(c.Z - a.Z - dw.Z)));
                }
            Check(worst < 1e-4, $"the shot leaves the muzzle as she's drawn, turned, pitched and rolled (off by at most {worst:0.0e0})");
            {
                double e = sg.Elevation * Math.PI / 180;
                // (the muzzle laid: the barrel turned up about its trunnions)
                Ballistics.MuzzleLocal(sg, sg.Elevation, out var ml0, out _);
                Ballistics.Muzzle(sg, sg.Elevation, Math.PI / 2, 0, 0, out var m0, out var d0);
                Ballistics.Muzzle(sg, sg.Elevation, Math.PI / 2, 0, 10 * Math.PI / 180, out _, out var dRoll);
                Ballistics.Muzzle(pg, pg.Elevation, Math.PI / 2, 0, 10 * Math.PI / 180, out _, out var pRoll);
                Ballistics.Muzzle(sg, sg.Elevation, Math.PI / 2, 5 * Math.PI / 180, 0, out var mPitch, out var dPitch);
                double el(Vec3d v) => Math.Asin(v.Y) * 180 / Math.PI;
                Check(Math.Abs(d0.Z - Math.Cos(e)) < 1e-6 && Math.Abs(d0.Y - Math.Sin(e)) < 1e-6 && Math.Abs(m0.X - ml0.X) < 1e-9 && Math.Abs(m0.Y - ml0.Y) < 1e-9 &&
                      Math.Abs(el(dRoll) - (sg.Elevation - 10)) < 0.05 && Math.Abs(el(pRoll) - (pg.Elevation + 10)) < 0.05 &&
                      Math.Abs(el(dPitch) - sg.Elevation) < 0.05 &&
                      Math.Abs(mPitch.Y - m0.Y - ml0.X * Math.Sin(5 * Math.PI / 180) - ml0.Y * (Math.Cos(5 * Math.PI / 180) - 1)) < 1e-6,
                    $"level and heading east, a starboard gun fires east-south at {sg.Elevation:0.0}° up; rolled 10° to starboard it points {el(dRoll):0.0}° " +
                    $"and a port gun {el(pRoll):0.0}°; pitched 5° its aim is the same and its muzzle {mPitch.Y - m0.Y:+0.00;-0.00} higher");
            }

            // ---- the ball's flight
            double mass = Ballistics.MassOf(sg, d), e0 = Ballistics.Energy(d, mass, d.MuzzleVelocity), h = sg.Muzzle[1] - def.Waterline;
            double level = Ballistics.RangeOverWater(d, h, 0, out double tLevel), own = Ballistics.RangeOverWater(d, h, sg.Elevation, out _);
            double r10 = Ballistics.RangeOverWater(d, h, 10, out _), r20 = Ballistics.RangeOverWater(d, h, 20, out _), r45 = Ballistics.RangeOverWater(d, h, 45, out double t45);
            Check(Math.Abs(e0 - 147.6) < 0.5 && Math.Abs(Ballistics.SpeedFor(d, mass, e0) - d.MuzzleVelocity) < 1e-6,
                $"a {mass:0.0} kg ball ({mass / 0.4536:0} lb) at {d.MuzzleVelocity} b/s hits with {e0:0} kJ (½mv² × {d.EnergyFactor}), and the speed back from it is the same");
            Check(level > 38 && level < 50 && own > level && r10 > 120 && r20 > 200 && r45 > 280,
                $"fired from her waist ({h:0.00} over the water): level it meets the sea {level:0} blocks out ({tLevel:0.00} s), at its own {sg.Elevation:0}° {own:0}, " +
                $"at 10° {r10:0}, at 20° {r20:0}, at 45° {r45:0} ({t45:0.0} s): a couple of hundred blocks");
            {
                var s = new ShotState { VX = d.MuzzleVelocity, Y = 100 };
                var s1 = Ballistics.At(s, d, 1.0);
                double horiz = s1.VX / d.MuzzleVelocity;
                // the same path stepped as the server does (its ticks) and as a client draws it (its frames)
                var server = s;
                double carry = 0;
                foreach (double tick in Enumerable.Repeat(0.05, 40)) { carry += tick; while (carry >= Ballistics.Step) { carry -= Ballistics.Step; Ballistics.Advance(ref server, d); } }
                var client = new CannonClientLike { D = d, Now = s };
                Vec3d drawn = null;
                for (double t = 0; t <= server.Time - 0.0173; t += 0.0173) drawn = client.Drawn(t);
                drawn = client.Drawn(server.Time + 1e-9);
                var again = Ballistics.At(s, d, server.Time + 1e-9);
                Check(horiz > 0.95 && horiz < 0.99 && drawn.DistanceTo(server.Pos) < 1e-6 && again.Pos.DistanceTo(server.Pos) < 1e-6 && server.Time > 1.95,
                    $"a little drag ({(1 - horiz) * 100:0.0}% of its speed in the first second); stepped in the server's ticks or drawn in a client's frames it's the same path");
            }
            {
                // into the water (0.3.7: as a real ball, not as in the air): the blow on the surface takes two thirds of its
                // energy, the water's drag three fifths of what's left each block; it's spent (can't hurt planking) within
                // about two blocks, still cracking a plank half a block in. It skips at a shallow angle, not at a steep one
                var w = new ShotState { VX = 55, VY = -10 };
                double eBefore = Ballistics.Energy(d, mass, w.Speed);
                Ballistics.EnterWater(d, ref w);
                double eIn = Ballistics.Energy(d, mass, w.Speed), eHalf = double.NaN, e1 = double.NaN, travelled = 0;
                int n = 0;
                double ePrev = eIn;
                while (w.Speed >= d.Spent && n++ < 2000)
                {
                    var before = w.Pos;
                    Ballistics.Advance(ref w, d);
                    double step = before.DistanceTo(w.Pos), e = Ballistics.Energy(d, mass, w.Speed);
                    // (where it's half a block and a block in: between two steps, a step being some 0.4 of a block)
                    if (double.IsNaN(eHalf) && travelled + step >= 0.5) eHalf = ePrev + (e - ePrev) * (0.5 - travelled) / step;
                    if (double.IsNaN(e1) && travelled + step >= 1) e1 = ePrev + (e - ePrev) * (1 - travelled) / step;
                    travelled += step;
                    ePrev = e;
                }
                double spentEnergy = Ballistics.Energy(d, mass, d.Spent), crack = def.Damage.HullToughness * def.Damage.Cracked;
                var skim = new ShotState { VX = 55, VY = -3 };
                var steep = new ShotState { VX = 55, VY = -8 };
                bool skims = Ballistics.Ricochets(d, skim), plunges = !Ballistics.Ricochets(d, steep);
                Ballistics.Ricochet(d, ref skim);
                Check(eIn < eBefore * 0.4 && eIn > eBefore * 0.3 && e1 < eIn * 0.5 && e1 > eIn * 0.3 && travelled > 1.0 && travelled < 3.0 &&
                      eHalf > crack * 0.75 && spentEnergy < crack * 0.5 && skims && plunges && skim.VY > 0 && skim.VX < 55,
                    $"into the water it keeps {eIn / eBefore * 100:0}% of its energy, {e1 / eIn * 100:0}% of that after a block, and is spent after {travelled:0.0} " +
                    $"(at {d.Spent} b/s, {spentEnergy:0} kJ); half a block in it about cracks a plank ({eHalf:0} kJ, {crack:0} cracks one); " +
                    $"at {Ballistics.Descent(new ShotState { VX = 55, VY = -3 }):0.0}° it skips off (up at {skim.VY:0.0} b/s), at {Ballistics.Descent(steep):0.0}° it goes in");
            }

            // ---- a ball's path meeting her hull, holing it and going on through her
            {
                // a target heading east (yaw π/2) at the origin; the ball fired level at her port side from 30 blocks off,
                // two blocks over the waterline (a gun's height), amidships
                var start = new ShotState { X = 4.4, Y = def.Waterline + 2.0, Z = -30, VZ = d.MuzzleVelocity };
                var s = start;
                ShipRayHit hit = default;
                bool met = false;
                for (int n = 0; n < 600 && !met; n++)
                {
                    var a = s.Pos;
                    Ballistics.Advance(ref s, d);
                    var seg = s.Pos.SubCopy(a.X, a.Y, a.Z);
                    double len = seg.Length();
                    met = ShipParts.Ray(def, g, 0, 0, 0, Math.PI / 2, 0, 0, a, seg, len, f => false, f => false, f => null, out hit);
                    if (met) s = new ShotState { X = hit.World.X, Y = hit.World.Y, Z = hit.World.Z, VX = s.VX, VY = s.VY, VZ = s.VZ };
                }
                double eHit = Ballistics.Energy(d, mass, s.Speed);
                var l = def.DamageParts;
                var st = new DamageState(l.Count);
                var res = met ? ShipDamage.Apply(l, st, g, hit.Local, new Vec3d(s.VX, s.VY, s.VZ), eHit, d.HitRadius) : null;
                var near = res?.Hits.Where(x => l.Parts[x.Part].Kind == DamageKind.Hull && l.Parts[x.Part].Part.Side < 0).OrderByDescending(x => st.Hurt[x.Part]).ToList();
                var farSide = res?.Hits.Where(x => l.Parts[x.Part].Kind == DamageKind.Hull && l.Parts[x.Part].Part.Side > 0).ToList();
                bool holed = near?.Count > 0 && l.StateOf(DamageKind.Hull, st.Hurt[near[0].Part], 0) == PartState.Holed;
                Check(met && hit.Part.Kind == ShipPartKind.Hull && hit.Part.Side < 0 && hit.Local.Y < def.Waterline + 2.0 && hit.Local.Y > def.Waterline && holed &&
                      res.Pierced && farSide.Count > 0,
                    $"a ball fired level at her port side from 30 blocks meets her hull at {hit.Local.Y - def.Waterline:+0.00} over the waterline with {eHit:0} kJ, holes it " +
                    $"({(holed ? l.Parts[near[0].Part].Part.ToString() : "-")}, hurt {(near?.Count > 0 ? st.Hurt[near[0].Part] : 0):0.00}), goes on across her and hurts her far side " +
                    $"({(farSide?.Count > 0 ? farSide.Max(x => st.Hurt[x.Part]) : 0):0.00}), where it stops ({res?.Hits.Count} parts hurt)");
                // a heavier shot goes out of her far side and flies on with what it has left
                var st2 = new DamageState(l.Count);
                var res2 = met ? ShipDamage.Apply(l, st2, g, hit.Local, new Vec3d(0, 0, 1), eHit * 2, d.HitRadius) : null;
                double vOn = res2?.Remaining > 0 ? Ballistics.SpeedFor(d, mass, res2.Remaining) : 0;
                Check(res2?.Pierced == true && res2.Exit != null && res2.Exit.Z > 0 && res2.Remaining > 1 && vOn < s.Speed,
                    $"one with twice as much ({eHit * 2:0} kJ) goes out of her far side (at z {res2?.Exit?.Z:0.0}) and flies on with {res2?.Remaining:0} kJ ({vOn:0} b/s)");

                // (the eighth client test) broken through her side the ball tumbles: her far side cracked and leaking, not
                // holed; a second ball the same way holes it; one in through the hole the first made (nothing broken going
                // in: it doesn't tumble) holes it at once
                if (met && farSide.Count > 0)
                {
                    PartState Far(DamageState x) => farSide.Select(f => l.StateOf(DamageKind.Hull, x.Hurt[f.Part], x.Wear[f.Part])).Max();
                    var first = Far(st);
                    ShipDamage.Apply(l, st, g, hit.Local, new Vec3d(s.VX, s.VY, s.VZ), eHit, d.HitRadius);
                    var second = Far(st);
                    var st3 = new DamageState(l.Count);
                    foreach (var x in near) st3.Hurt[x.Part] = 1;
                    ShipDamage.Apply(l, st3, g, hit.Local, new Vec3d(s.VX, s.VY, s.VZ), eHit, d.HitRadius);
                    var through = Far(st3);
                    Check(first == PartState.Cracked && second == PartState.Holed && through == PartState.Holed && l.D.ThroughKeeps < 1,
                        $"broken through her side the ball tumbles (keeps {l.D.ThroughKeeps:0.00} of what's left): her far side {first}, a second ball the same way: {second}; " +
                        $"one in through the first one's hole: {through}");
                }
                else Check(false, "the far side's checks ran (the ball met her and reached her far side)");
            }

            // ---- the recoil
            {
                float r0 = PartMotion.Recoil(0), rBack = PartMotion.Recoil(PartMotion.RecoilBack), rHold = PartMotion.Recoil(PartMotion.RecoilBack + PartMotion.RecoilHold * 0.9);
                float rMid = PartMotion.Recoil(PartMotion.RecoilBack + PartMotion.RecoilHold + PartMotion.RecoilOut / 2);
                float rEnd = PartMotion.Recoil(PartMotion.RecoilBack + PartMotion.RecoilHold + PartMotion.RecoilOut), rNever = PartMotion.Recoil(double.NaN);
                bool monotone = true;
                float prev = 1;
                for (double t = PartMotion.RecoilBack; t < 6; t += 0.01) { float v = PartMotion.Recoil(t); if (v > prev + 1e-6) monotone = false; prev = v; }
                Check(r0 == 0 && rBack == 1 && rHold == 1 && Math.Abs(rMid - 0.5) < 1e-3 && rEnd == 0 && rNever == 0 && monotone && PartMotion.Recoil(0.05) > 0.4,
                    $"fired, a gun runs back in {PartMotion.RecoilBack:0.00} s (half way in 0.05 s: {PartMotion.Recoil(0.05):0.00}), stands {PartMotion.RecoilHold:0.0} s, and is run out again over {PartMotion.RecoilOut:0.0} s");
                // its trucks turn as it rolls: a point on a truck's rim (the one on the deck) stays where it touches
                var gm = P(sg.Code);
                var fore = Array.Find(def.MovingParts, q => q.Parent == sg.Code && q.Code.EndsWith("fore"));
                var slide = new float[16];
                PartMotion.SlideMatrix(gm, gm.Distance, slide);
                var turn = new float[16];
                PartMotion.PartMatrix(fore, PartMotion.TurnDegrees(fore, 1) * (float)(Math.PI / 180), turn);
                Mat4f.Mul(turn, slide, turn);
                double r = fore.AxisPoint[1] - def.DeckY;
                double[] contact = { fore.AxisPoint[0], def.DeckY, fore.AxisPoint[2] };
                var moved = Apply(turn, contact);
                // after rolling back its whole run, the wheel has turned run/r radians: the point that touched the deck is where it
                // would be on a wheel that rolled without slipping
                double ang = gm.Distance / r, zc = fore.AxisPoint[2] + gm.AxisDir[2] * gm.Distance;
                double[] expect = { contact[0], def.DeckY + r - r * Math.Cos(ang), zc - gm.AxisDir[2] * r * Math.Sin(ang) };
                Check(Dist(moved, expect) < 0.02, $"its trucks roll with it, not slide (a point of a rim ends {Dist(moved, expect):0.000} from where rolling puts it)");
            }

            // ---- the gun as a block (for a wreck to give back)
            {
                string shapePath = Path.Combine(assets, "shapes", "block", "gun-bronze.json"), blockPath = Path.Combine(assets, "blocktypes", "gun.json");
                var shape = File.Exists(shapePath) ? JObject.Parse(File.ReadAllText(shapePath)) : null;
                string vs = Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? "", "Vintagestory", "assets", "survival", "textures");
                var tex = shape?["textures"] as JObject;
                bool texOk = tex != null && tex.Properties().All(p => File.Exists(Path.Combine(vs, ((string)p.Value).Replace("game:", "") + ".png")));
                int els = (shape?["elements"] as JArray)?.Count ?? 0;
                Check(els > 30 && texOk && File.Exists(blockPath), $"the gun as a block: {els} boxes in its shape (the same gun, octagons for the round parts), its vanilla textures there, its block type (hansa:gun-bronze-*)");
            }

            // ---- sinking her: hits below the waterline, and how long she takes
            {
                var l = def.DamageParts;
                // (the 65 kJ of a ball that strikes her side right at the waterline from some way off; through the water
                // under it, since 0.3.7, it arrives with a good deal less: SinkTime with 40 kJ cracks her instead)
                foreach (int n in new[] { 10, 20 })
                {
                    double t = SinkTime(def, n, 65, out int holed, out double rate);
                    Check(holed >= n * 0.8 && t > 60 && t < 420, $"{n} shots at her waterline, just under it (65 kJ each) hole {holed} panels, letting in {rate:0} l/s at first: " +
                        $"her reserve buoyancy is gone and she goes down after {t / 60:0.0} minutes");
                }
            }

            // ---- laying them (0.3.6)
            GunLays(def, assets);
        }

        /// <summary>The guns' range table and how fast shots below the waterline sink her (dotnet run -c Release -- cannons).</summary>
        static void CannonReport(ShipDefinition def)
        {
            var d = def.Cannon;
            var gun = def.Fixtures.FirstOrDefault(f => f.Type == "gun");
            double mass = Ballistics.MassOf(gun, d), h = (gun?.Muzzle?[1] ?? def.DeckY + 0.5) - def.Waterline;
            Console.WriteLine($"{def.Code}: guns at {h:0.00} over the water, {mass:0.0} kg shot at {d.MuzzleVelocity} b/s, {Ballistics.Energy(d, mass, d.MuzzleVelocity):0} kJ at the muzzle");
            Console.WriteLine("  elevation  meets the sea at  after  descending  energy there (kJ)");
            foreach (double e in new[] { -5.0, -2, 0, 1, 2, 5, 10, 20, 30, 45 })
            {
                double r = Ballistics.RangeOverWater(d, h, e, out double t);
                var s = Ballistics.At(new ShotState { Y = h, VX = d.MuzzleVelocity * Math.Cos(e * Math.PI / 180), VY = d.MuzzleVelocity * Math.Sin(e * Math.PI / 180) }, d, t);
                Console.WriteLine($"  {e,8:0}°  {r,14:0}  {t,5:0.00} s  {Ballistics.Descent(s),8:0.0}°  {Ballistics.Energy(d, mass, s.Speed),10:0}{(Ballistics.Ricochets(d, s) ? "  (skips)" : "")}");
            }
            foreach (double e in new[] { 88.0, 65.0 })
            {
                Console.WriteLine($"  shots below the waterline ({e:0} kJ each): minutes till she goes down");
                foreach (int n in new[] { 3, 5, 10, 15, 20, 30 })
                {
                    double t = SinkTime(def, n, e, out int holed, out double rate);
                    Console.WriteLine($"  {n,4} shots, {holed,3} panels holed, {rate,6:0} l/s at first: {t / 60,5:0.0} min");
                }
            }
            Console.WriteLine("  (cracked instead of holed, 40 kJ each)");
            foreach (int n in new[] { 10, 20 })
            {
                double t = SinkTime(def, n, 40, out int holed, out double rate);
                Console.WriteLine($"  {n,4} shots, {holed,3} panels holed, {rate,6:0} l/s at first: {t / 60,5:0.0} min");
            }
        }

        /// <summary>A client's way of drawing a ball (CannonClient.Flying), for comparing with the server's.</summary>
        class CannonClientLike
        {
            public CannonDef D;
            public ShotState Now;

            public Vec3d Drawn(double t)
            {
                while (Now.Time + Ballistics.Step <= t) Ballistics.Advance(ref Now, D);
                double rest = Math.Max(0, t - Now.Time);
                return new Vec3d(Now.X + Now.VX * rest, Now.Y + Now.VY * rest, Now.Z + Now.VZ * rest);
            }
        }

        /// <summary>
        /// Shots (energy kJ each) into as many panels just below her waterline, both sides amidships, and then how long till
        /// her reserve buoyancy is gone (she settles and goes down): the leaks (and the sea over her side once she's that
        /// low), seconds.
        /// </summary>
        static double SinkTime(ShipDefinition def, int shots, double energy, out int holed, out double firstRate)
        {
            var l = def.DamageParts;
            var st = new DamageState(l.Count);
            var panels = l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Center.Y < def.Waterline - 0.1 && p.Center.Y > def.Waterline - 1.2)
                .OrderBy(p => Math.Abs(p.Center.X)).Take(shots).ToList();
            foreach (var p in panels) ShipDamage.Apply(l, st, def.Grid, p.Center, new Vec3d(0, 0, -Math.Sign(p.Center.Z)), energy, def.Cannon.HitRadius);
            holed = panels.Count(p => l.StateOf(DamageKind.Hull, st.Hurt[p.Index], st.Wear[p.Index]) == PartState.Holed);
            double water = 0, freeboard = def.DeckY - def.Waterline, t = 0;
            firstRate = -1;
            while (t < 3600)
            {
                double sink = l.SinkageFor(water);
                if (sink >= freeboard - 1e-3) return t;
                var pose = new FloodPose { Y = -sink, Surface = (x, z) => 0 };
                double q = ShipFlood.Leaks(l, st, water, pose, 1, out _) + ShipFlood.Downflooding(l, pose, _ => false);
                if (firstRate < 0) firstRate = q * 1000;
                water = Math.Min(l.Capacity, water + q * 0.5);
                t += 0.5;
            }
            return t;
        }

        /// <summary>A gate swung all the way open: the box its leaf sweeps (from the moving part), and its code.</summary>
        static (string Code, double[] Box) OpenBox(ShipDefinition def, FixtureDef gate)
        {
            var part = Array.Find(def.MovingParts, p => p.Code == gate.Code);
            var b = gate.Box;
            if (part == null || b == null) return (gate.Code, b);
            var lo = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            var hi = new[] { double.MinValue, double.MinValue, double.MinValue };
            var m = new float[16];
            for (int step = 0; step <= 10; step++)
            {
                PartMotion.PartMatrix(part, PartMotion.TurnDegrees(part, step / 10f) * (float)(Math.PI / 180), m);
                foreach (double x in new[] { b[0], b[3] })
                    foreach (double y in new[] { b[1], b[4] })
                        foreach (double z in new[] { b[2], b[5] })
                        {
                            var q = Apply(m, new[] { x, y, z });
                            for (int i = 0; i < 3; i++) { lo[i] = Math.Min(lo[i], q[i]); hi[i] = Math.Max(hi[i], q[i]); }
                        }
            }
            return (gate.Code, new[] { lo[0], lo[1], lo[2], hi[0], hi[1], hi[2] });
        }

        static bool Overlaps(double[] a, double[] b) =>
            a != null && b != null && a[0] < b[3] && b[0] < a[3] && a[1] < b[4] && b[1] < a[4] && a[2] < b[5] && b[2] < a[5];
    }
}
