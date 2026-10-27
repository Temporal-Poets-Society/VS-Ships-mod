using System.Collections.Generic;
using System;
using System.Linq;
using Hansa;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (25 October 2026, the user's decision: swivel guns on the caravel's quarterdeck rail) Each swivel gun place: its
        /// yoke, muzzle, arc, ball and reload filled in from its socket; the barrel drawn (its parts' matrices) with its muzzle
        /// where the laying puts the shot out, all over its arc; laid only within its arc; the man where it says he stands
        /// takes it with a click (its selection box, in reach of its breech); its muzzle clear of her all over its arc.
        ///   dotnet run --project tests/HansaTests -- swivels
        /// </summary>
        static void Swivels(string root)
        {
            Console.WriteLine("the caravel's swivel guns");
            var (def, file) = LoadHullDesign(root, "caravel");
            var sw = def.Fixtures.Where(f => f.Swivel).ToArray();
            // her level faces (the rail caps' tops among them), from her mesh: x0 x1 z0 z1 at y
            var level = new List<(double X0, double X1, double Y, double Z0, double Z1)>();
            foreach (var g in file?.Groups ?? new Dictionary<string, float[][]>())
                if (!g.Key.StartsWith("part:") && g.Value != null)
                    foreach (var q in g.Value)
                    {
                        if (q == null || q.Length < 32) continue;
                        double y0 = Math.Min(Math.Min(q[2], q[10]), Math.Min(q[18], q[26])), y1 = Math.Max(Math.Max(q[2], q[10]), Math.Max(q[18], q[26]));
                        if (y1 - y0 > 0.003) continue;
                        level.Add((Math.Min(Math.Min(q[1], q[9]), Math.Min(q[17], q[25])), Math.Max(Math.Max(q[1], q[9]), Math.Max(q[17], q[25])), y0,
                                   Math.Min(Math.Min(q[3], q[11]), Math.Min(q[19], q[27])), Math.Max(Math.Max(q[3], q[11]), Math.Max(q[19], q[27]))));
                    }
            Check(sw.Length == 4 && sw.Count(f => f.Out[2] < 0) == 2, $"four swivel guns, two a side ({string.Join(", ", sw.Select(f => f.Code))})");
            foreach (var f in sw)
            {
                var deck = def.Decks?.FirstOrDefault(d => Math.Abs(d.Y - f.Seat[1]) < 0.05);
                Check(f.Pivot.SequenceEqual(f.Pos) && Math.Abs(f.Trunnion[1] - f.Pos[1] - SwivelGun.PinHeight) < 1e-9 && f.TrainLimits[0] == -80 && f.TrainLimits[1] == 80 &&
                      f.ElevationLimits[0] == -25 && f.ElevationLimits[1] == 30 && f.Reload == SwivelGun.ReloadSeconds && f.Shot == SwivelGun.ShotKg &&
                      f.Shot < def.Cannon.ShotMass / 5 && f.Reload < def.Cannon.Cooldown && f.Box == null && deck != null,
                    $"{f.Code}: on its socket at {f.Pos[0]:0.00} {f.Pos[1]:0.00} {f.Pos[2]:0.00}, ±80° by -25..30°, a {f.Shot} kg ball, {f.Reload} s to fire again; its gunner on the {deck?.Code ?? "?"} deck (y {f.Seat[1]:0.00})");
                // the drawn barrel's muzzle (the shape's) where the shot leaves, laid anywhere
                double worst = 0;
                var s = new float[16]; var y = new float[16]; var b = new float[16];
                for (float t = -80; t <= 80; t += 20)
                    for (float e = -25; e <= 30; e += 11)
                    {
                        var lay = new GunLay(t, e);
                        SwivelGun.PartMatrices(f, lay, s, y, b);
                        var drawn = SwivelGun.Apply(b, 0, SwivelGun.PinHeight, SwivelGun.MuzzleReach);
                        GunLaying.MuzzleLocal(f, lay, out var m, out _);
                        worst = Math.Max(worst, Math.Sqrt((drawn[0] - m.X) * (drawn[0] - m.X) + (drawn[1] - m.Y) * (drawn[1] - m.Y) + (drawn[2] - m.Z) * (drawn[2] - m.Z)));
                    }
                Check(worst < 1e-4, $"{f.Code}: its drawn muzzle is where its shot leaves, all over its arc (off by {worst:0.000000} at most)");
                // only within its arc
                var c = GunLaying.Clamp(f, new GunLay(110, 45));
                var up = GunLaying.Clamp(f, new GunLay(30, 45));
                Check(c.Train == 80 && up.Train == 30 && up.Elevation <= 30 && up.Elevation >= 15 && GunLaying.Beyond(f, new GunLay(-95, 0)) && !GunLaying.Beyond(f, new GunLay(-45, -15)),
                    $"{f.Code}: laid 110° it goes to 80°, 45° up to {up.Elevation:0.0}° (30° its limit; less as its tail comes over its rail); 95° aft is past its arc, 45° aft and 15° down within it");
                // the gunner where he stands: his click takes it, and it's within his reach
                var eye = new Vec3d(f.Seat[0], f.Seat[1] + 1.62, f.Seat[2]);
                var sel = f.SelBox;
                var to = new Vec3d((sel.X1 + sel.X2) / 2 - eye.X, (sel.Y1 + sel.Y2) / 2 - eye.Y, (sel.Z1 + sel.Z2) / 2 - eye.Z);
                double len = to.Length();
                var hit = ShipFittings.Pick(def, def.Grid, eye, new Vec3d(to.X / len, to.Y / len, to.Z / len), 4.5, x => true, x => false, x => null, out _);
                bool reach = GunLaying.InReach(f, new GunLay(0, 0), f.Seat[0], f.Seat[1], f.Seat[2], 3.5);   // (EntityShip.LayReach)
                Check(hit == f && reach && GunButtons.LaysGun(null, hit, reach, false) == f, $"{f.Code}: from where its gunner stands, his click takes it ({hit?.Code ?? "nothing"}), in reach of its breech");
                // trained along her rail, it can't be depressed into it (nor its tail lowered into it); square to it, it goes
                // all the way down her side
                var along = GunLaying.Clamp(f, new GunLay(75, -25));
                var tail = GunLaying.Clamp(f, new GunLay(-80, 30));
                var square = GunLaying.Clamp(f, new GunLay(0, -25));
                Check(along.Elevation > -25 && SwivelGun.ClearOfRail(f, along) && tail.Elevation < 30 && SwivelGun.ClearOfRail(f, tail) && square.Elevation <= -20,
                    $"{f.Code}: laid 75° along her rail and 25° down it comes up to {along.Elevation:0.0}°; 80° aft and 30° up its tail keeps it to {tail.Elevation:0.0}°; square, {-square.Elevation:0.0}° down her side");
                // (27 October 2026) all over its arc, as it can be laid: its barrel and tail clear of her rail cap (her mesh's
                // level faces under them, by a barrel's radius), and its ball's start, past the cap, out of her collision. (Her
                // collision alone can't say it: a cap's top between two of its quarter-block rows is solid up to the next, so it
                // only says where the ball starts lower than the cap.)
                int touch = 0, inside = 0, tried = 0;
                string where = null;
                for (float t = -80; t <= 80; t += 10)
                    for (float e = -25; e <= 30; e += 5)
                    {
                        var lay = GunLaying.Clamp(f, new GunLay(t, e));
                        tried++;
                        bool hits = false;
                        for (double r = -SwivelGun.TailReach; r <= SwivelGun.MuzzleReach + 1e-9; r += 0.05)
                        {
                            var p = GunLaying.Place(f, lay, 0, new Vec3d(f.Trunnion[0] + f.Out[0] * r, f.Trunnion[1], f.Trunnion[2] + f.Out[2] * r), true);
                            foreach (var lv in level)
                                if (p.X >= lv.X0 && p.X <= lv.X1 && p.Z >= lv.Z0 && p.Z <= lv.Z1 && lv.Y <= p.Y + 0.5 && p.Y - lv.Y < 0.0625 - 1e-3)
                                { hits = true; where ??= $"{t} {e} -> {lay}: {r:0.00} along at {p.X:0.00} {p.Y:0.00} {p.Z:0.00}, {p.Y - lv.Y:0.000} over a face at {lv.Y:0.000}"; break; }
                            if (hits) break;
                        }
                        if (hits) touch++;
                        GunLaying.MuzzleLocal(f, lay, out var m0, out var md);
                        var m = new Vec3d(m0.X + md.X * 0.1, m0.Y + md.Y * 0.1, m0.Z + md.Z * 0.1);
                        bool overCap = level.Any(lv => m.X >= lv.X0 - 0.05 && m.X <= lv.X1 + 0.05 && m.Z >= lv.Z0 - 0.05 && m.Z <= lv.Z1 + 0.05 && lv.Y <= m.Y && m.Y - lv.Y < 0.3);
                        // (over the cap's top, Pos's height, nothing of her is there but the cap: her collision's row there is the
                        // cap rounded up)
                        if (!overCap && m.Y < f.Pos[1] && def.Grid.SolidAt(m.X, m.Y, m.Z)) inside++;
                    }
                Check(touch == 0 && inside == 0, $"{f.Code}: laid anywhere in its arc, barrel and tail clear over her rail cap ({touch} of {tried} lays touch it{(where != null ? ": " + where : "")}); its ball starts out of her ({inside} in her)");
            }
        }
    }
}
