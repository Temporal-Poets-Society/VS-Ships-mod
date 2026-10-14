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
        /// <summary>Wind (vanilla units, 25 m/s each) in the middle of each Beaufort force (force 12: Sea's most, 1.5).</summary>
        static readonly (int Force, double Wind)[] BeaufortMid =
        {
            (3, 4.45 / 25), (4, 6.75 / 25), (5, 9.4 / 25), (6, 12.35 / 25), (7, 15.55 / 25), (8, 19.0 / 25), (9, 22.65 / 25),
            (10, 26.5 / 25), (11, 30.6 / 25), (12, 1.5),
        };

        /// <summary>
        /// The sails (0.3.10, the tenth test's sailing notes): how fast they wear by the wind and sail set (a table, and checks),
        /// looking at each sail and mending it there or at its mast, what mends it (and that the items exist), what the crosshair
        /// meets first (a deck over the hold, a mast), and the lateen bellying to leeward on both tacks.
        /// </summary>
        static void Sails(ShipDefinition def, string assets)
        {
            var l = def.DamageParts;
            var d = l.D;

            // ---- wear: chafe a game day, and (0.3.12) the strain: real minutes over-pressed before a sail starts to tear
            double Day(double set, double wind) => 24 * SailWear.Chafe(d, set, wind);
            double Mins(double load) => SailWear.SecondsToTear(d, load) / 60;
            double W(int force) => BeaufortMid.First(b => b.Force == force).Wind;
            Console.WriteLine("  sail chafe a game day (% of a sail; torn at 30, shredded at 75), and real minutes over-pressed before they tear (- never):");
            Console.WriteLine("  force  wind   chafe full  reefed | tears after: full  reefed | aback: full  reefed | tearing then, % a minute: full  aback");
            string M(double load) => double.IsInfinity(Mins(load)) ? "-" : Mins(load).ToString("0.0");
            foreach (var (f, w) in BeaufortMid)
                Console.WriteLine($"  {f,5}  {w,4:0.00}  {100 * Day(1, w),10:0.0##}  {100 * Day(0.55, w),6:0.0##} | {M(SailWear.GaleLoad(d, 1, w)),17}  {M(SailWear.GaleLoad(d, 0.55, w)),6} | " +
                    $"{M(SailWear.AbackLoad(d, 1, w)),11}  {M(SailWear.AbackLoad(d, 0.55, w)),6} | {6000 * SailWear.Gale(d, SailWear.GaleLoad(d, 1, w)),29:0.0}  {6000 * SailWear.Aback(d, SailWear.AbackLoad(d, 1, w)),5:0.0}");
            Check(Enumerable.Range(3, 5).All(f => Day(1, W(f)) < 0.005) && Enumerable.Range(3, 5).All(f => SailWear.GaleLoad(d, 1, W(f)) <= 1),
                $"no measurable wear in moderate winds: full sail to force 7 under 0.5 % a day of chafe and never over-pressed (force 5: {100 * Day(1, W(5)):0.00} %)");
            Check(20 * Day(1, W(5)) < 0.1 && 20 * Day(1, W(5)) > 0,
                $"slow chafe: twenty days at sea under full sail in force 5 cost {100 * 20 * Day(1, W(5)):0.0} % (a few % at most)");
            // (0.3.24, the user: "sails strain already at force 7-8 ... not fun: after a calm I want to go fast") full sail
            // through force 8; strain from force 9 (warned, but they hold); tearing only from force 10, after minutes of it
            double g9 = SailWear.GaleLoad(d, 1, W(9)), g10 = SailWear.GaleLoad(d, 1, W(10)), g12 = SailWear.GaleLoad(d, 1, 1.5);
            Check(SailWear.GaleLoad(d, 1, W(8)) <= 1 && SailWear.GaleLoad(d, 1, SailWear.ForceFloor(9) - 0.005) <= 1 && g9 > 1 && SailWear.Gale(d, g9) == 0 &&
                  SailWear.Gale(d, SailWear.GaleLoad(d, 1, SailWear.ForceFloor(10) + 0.005)) > 0 && Mins(g10) >= 3 && Mins(g10) <= 8 && Mins(g12) >= 2 && Mins(g12) <= 3.5,
                $"0.3.24: full sail is fine through force 8, strains from force 9 but holds there, tears only from force 10 after minutes of strain (force 10 {Mins(g10):0.0} min, force 12 {Mins(g12):0.0})");
            Check(BeaufortMid.All(b => SailWear.GaleLoad(d, 0.55, b.Wind) <= 1) && SailWear.Gale(d, SailWear.GaleLoad(d, 0.55, 1.5)) == 0 && SailWear.GaleLoad(d, 0, 1.5) == 0,
                $"reefed sails stand through force 12 (load {SailWear.GaleLoad(d, 0.55, 1.5):0.00}); furled ones never strain");
            {
                // the server's settings (SailStrainForce, SailTearForce): the old limits back with 8 and 9
                var old = SailWear.WithForces(d, 8, 9);
                var same = SailWear.WithForces(d, 9, 10);
                Check(Math.Abs(SailWear.ForceFloor(9) - 0.832) < 1e-9 && Math.Abs(SailWear.ForceFloor(10) - 0.98) < 1e-9 && SailWear.GaleLoad(old, 1, W(8)) > 1 &&
                      SailWear.Gale(old, SailWear.GaleLoad(old, 1, W(9))) > 0 && Math.Abs(same.SailGaleWind - d.SailGaleWind) < 1e-6 && Math.Abs(same.SailTearWind - d.SailTearWind) < 1e-6 &&
                      SailWear.WithForces(d, 11, 9).SailTearWind >= SailWear.WithForces(d, 11, 9).SailGaleWind,
                    $"SailStrainForce/SailTearForce: 8 and 9 strain full sail from force 8 and tear it in force 9 (the old way); 9 and 10 are the design's own");
            }
            {
                // a quarter of a minute over-pressed now and then (a gust, a tack) never adds up: the strain eases off between
                double strain = 0;
                for (int i = 0; i < 40; i++)
                {
                    for (int t = 0; t < 15; t++) strain = Math.Clamp(strain + SailWear.StrainRate(d, SailWear.GaleLoad(d, 1, 1.5)), 0, 1);
                    for (int t = 0; t < 45; t++) strain = Math.Clamp(strain + SailWear.StrainRate(d, 0.5), 0, 1);
                }
                // in force 12 under full sail, held there: the first tear, then torn within a few minutes more
                double held = 0, hurt = 0, load12 = SailWear.GaleLoad(d, 1, 1.5);
                int secs = 0;
                for (; secs < 3600 && hurt < d.Torn; secs++)
                {
                    held = Math.Clamp(held + SailWear.StrainRate(d, load12), 0, 1);
                    if (held >= 1) hurt += SailWear.Gale(d, load12);
                }
                Check(strain < 0.2 && secs > 150 && secs < 600,
                    $"gusts a quarter minute in four leave the strain at {strain:0.00} (no tear); held in force 12 under full sail a sail is torn after {secs / 60.0:0.0} real minutes");
            }
            double aTorn = Mins(SailWear.AbackLoad(d, 1, W(10))) + d.Torn / Math.Max(1e-9, 60 * SailWear.Aback(d, SailWear.AbackLoad(d, 1, W(10))));
            Check(SailWear.AbackLoad(d, 1, W(8)) <= 1 && SailWear.Aback(d, SailWear.AbackLoad(d, 1, W(9))) == 0 && SailWear.AbackLoad(d, 0.55, W(12)) <= 1 && aTorn < 10,
                $"taken aback: nothing strains through force 8, in force 9 they strain but hold; caught aback under full sail in force 10 they're torn in {aTorn:0.0} min, reefed not");
            Check(SailWear.Warning(d, 1, W(8), false, 0) == 0 && SailWear.Warning(d, 1, W(9), false, 0) == 1 && SailWear.Warning(d, 1, 1.5, false, 0) >= 1 &&
                  SailWear.Warning(d, 0.55, W(10), false, 0) == 0 && SailWear.Warning(d, 1, W(10), false, 0.8) == 3 && SailWear.Warning(d, 1, W(9), false, 0.8) == 1 &&
                  SailWear.Warning(d, 0.55, W(7), false, 0.8) == 0,
                "the panel's warning: none through force 8 under full sail, 'shorten sail' from force 9, 'about to tear' only where they'd tear (force 10 up) with the strain high");

            // ---- every sail on its mast, its box where the sail is
            var g = def.Grid;
            int mizzen = Array.FindIndex(def.Masts, m => m.Code == "mizzen");
            bool masts = true;
            foreach (var p in l.Parts.Where(p => p.Kind == DamageKind.Sail))
            {
                var sd = def.Sails[p.Sail];
                if (p.Mast < 0 || def.Masts[p.Mast].Code != sd.Code.Split('-')[0]) masts = false;
            }
            Check(masts && l.Parts.Count(p => p.Kind == DamageKind.Sail) == def.Sails.Length, "every sail is on its own mast (main-course on the mainmast, mizzen-lateen on the mizzen...)");
            int lateenSail = Array.FindIndex(def.Sails, s => s.Code == "mizzen-lateen");
            if (lateenSail >= 0)
            {
                var lp = l.Parts.First(p => p.Kind == DamageKind.Sail && p.Sail == lateenSail);
                var clew = def.MovingParts.First(m => m.Sail == "mizzen-lateen").Tip;
                var f = l.SailFrames[lateenSail];
                var mid = f.FromSail(f.YardLength / 2, f.ClewV / 3, 0);
                Check(f.ForeAndAft && lp.DistanceTo(new Vec3d(clew[0], clew[1], clew[2])) == 0 && lp.DistanceTo(new Vec3d(mid[0], mid[1], mid[2])) == 0 &&
                      lp.Min[0] < def.Masts[mizzen].X - 3 && lp.Max[1] > def.Masts[mizzen].Y1 - 12,
                    $"the lateen's part is the triangle under its slanting yard, aft of the mizzen (it was a square sail's box round the yard's foot, mostly forward of it; " +
                    $"x {lp.Min[0]:0.0}..{lp.Max[0]:0.0}, y {lp.Min[1]:0.0}..{lp.Max[1]:0.0})");
            }

            // ---- the crosshair: a deck over the hold, a mast before what's behind it
            bool Ray(Vec3d o, Vec3d dir, double range, out ShipRayHit h) =>
                ShipParts.Ray(def, g, 0, 0, 0, Math.PI / 2, 0, 0, o, dir, range, fx => fx.Type != "attach", fx => false, fx => null, out h);
            int mainDeck = Array.FindIndex(def.Decks, k => k.Code == "main");
            // (over a free floor place of the open main deck: the waist's hatch, pump and capstan move with the design)
            var free = def.Mounts?.Floors.FirstOrDefault(m => m.Deck == "main" && m.Room == "main-deck" && m.X > def.Masts[0].X - 6 && m.X < def.Masts[0].X + 8);
            double fx0 = free != null ? free.CX : 5.0, fz0 = free != null ? free.CZ : 1.0;
            bool deckHit = Ray(new Vec3d(fx0, def.DeckY + 1.6, fz0), new Vec3d(0, -1, 0), 5, out var dh);
            Check(deckHit && dh.Part.Kind == ShipPartKind.Deck && dh.Part.Index == mainDeck && Math.Abs(dh.Distance - (1.6 + def.DeckY - def.Decks[mainDeck].Y)) < 0.3,
                $"looking down from the main deck at the hold below: the main deck ({(deckHit ? dh.Part.ToString() : "nothing")})");
            var mm = def.Masts[0];
            bool mastHit = Ray(new Vec3d(mm.X + 2.5, def.DeckY + 1.6, 0.2), new Vec3d(-1, 0.1, 0), 5, out var mh);
            Check(mastHit && mh.Part.Kind == ShipPartKind.Mast && mh.Part.Index == 0 && mh.Distance < 2.5,
                $"looking at the mainmast from 2.5 blocks: the mast, not what's behind it ({(mastHit ? mh.Part.ToString() : "nothing")})");

            // ---- looking at each sail from a deck below it: the sail, nearer than anything behind it; mended there
            var seen = new List<string>();
            foreach (var p in l.Parts.Where(p => p.Kind == DamageKind.Sail))
            {
                var f = l.SailFrames[p.Sail];
                var target = f.FromSail((f.U0 + f.U1) / 2 * (f.ForeAndAft ? 0.6 : 0.3), f.ForeAndAft ? f.ClewV * 0.5 : f.V1 * 0.6, 0);
                bool ok = false;
                for (double dx = -4; dx <= 4 && !ok; dx += 0.5)
                    for (double dz = -2.5; dz <= 2.5 && !ok; dz += 0.5)
                        foreach (var dk in def.Decks)
                        {
                            var eye = new Vec3d(target[0] + dx, dk.Y + 1.6, dz);
                            if (eye.X < dk.X0 || eye.X > dk.X1 || g.SolidAt(eye.X, eye.Y, eye.Z) || g.SolidAt(eye.X, eye.Y - 0.9, eye.Z) || !g.SolidAt(eye.X, dk.Y - 0.1, eye.Z)) continue;
                            var dir = new Vec3d(target[0] - eye.X, target[1] - eye.Y, target[2] - eye.Z);
                            double len = dir.Length();
                            if (len > 12) continue;
                            dir = new Vec3d(dir.X / len, dir.Y / len, dir.Z / len);
                            int hitSail = l.SailAt(eye, dir, 20, 0, 0, 1, out double ts);
                            bool blocked = Ray(eye, dir, 20, out var wh) && wh.Distance < ts;
                            if (hitSail == p.Index && !blocked) { ok = true; break; }
                        }
                if (ok) seen.Add(def.Sails[p.Sail].Code);
            }
            Check(seen.Count == def.Sails.Length, $"each sail can be looked at from a deck below it, in front of whatever's behind it ({string.Join(", ", seen)})");

            // braced round, furled: still met where it's drawn
            {
                var course = l.Parts.First(p => p.Kind == DamageKind.Sail && def.Sails[p.Sail].Code == "main-course");
                var f = l.SailFrames[course.Sail];
                var yard = f.Yard;
                double brace = def.Sailing.MaxBrace * Math.PI / 180;
                var m = new float[16];
                PartMotion.PartMatrix(yard, PartMotion.TurnDegrees(yard, (float)brace) * GameMath.DEG2RAD, m);
                double[] At(double[] q) => new double[] { m[0] * q[0] + m[4] * q[1] + m[8] * q[2] + m[12], m[1] * q[0] + m[5] * q[1] + m[9] * q[2] + m[13], m[2] * q[0] + m[6] * q[1] + m[10] * q[2] + m[14] };
                var tip = At(f.FromSail(f.U1 - 1.5, f.V1 * 0.5, 0));
                var eye = new Vec3d(4.0, def.DeckY + 1.6, 2.0);
                var dir = new Vec3d(tip[0] - eye.X, tip[1] - eye.Y, tip[2] - eye.Z);
                bool braced = l.SailAt(eye, dir.Normalize(), 30, brace, 0, 1, out _) == course.Index;
                var unbraced = l.SailAt(eye, dir.Normalize(), 30, 0, 0, 1, out _);
                var yardMid = f.FromSail(f.U1 - 2, 0, 0);
                var furled = l.SailAt(eye, new Vec3d(yardMid[0] - eye.X, yardMid[1] - eye.Y, yardMid[2] - eye.Z).Normalize(), 30, 0, 0, PartMotion.DrawnSail(0), out _);
                var lowSail = f.FromSail(0.5, f.V1 * 0.8, 0);
                var under = l.SailAt(eye, new Vec3d(lowSail[0] - eye.X, lowSail[1] - eye.Y, lowSail[2] - eye.Z).Normalize(), 30, 0, 0, PartMotion.DrawnSail(0), out _);
                Check(braced && unbraced != course.Index && furled == course.Index && under != course.Index,
                    "the main course is met where it's drawn: braced round with its yard, furled up on its yard (and not where it hung before)");
            }

            // ---- mending a sail: there or at its mast, with what the hint says
            var st = new DamageState(l.Count);
            int lateenPart = l.Parts.First(p => p.Kind == DamageKind.Sail && p.Sail == Math.Max(0, lateenSail)).Index;
            int mizzenMast = l.Find(ShipPart.Mast(Math.Max(0, mizzen)).Encode());
            st.Hurt[lateenPart] = d.Torn + 0.01f;
            var atSail = ShipRepairs.Needs(l, st, lateenPart);
            var atMast = ShipRepairs.Needs(l, st, mizzenMast);
            string Kits(RepairJob j) => string.Join(" or ", j.Kits.Select(k => string.Join(" + ", k.Items.Select(it => $"{it.Count} {it.Code}"))));
            Check(atSail.Count == 1 && atSail[0].What == "sail" && atMast.Any(j => j.Part == lateenPart) &&
                  ShipRepairs.For(l, st, lateenPart, "game:cloth-blue")?.Part == lateenPart && ShipRepairs.For(l, st, mizzenMast, "game:cloth-plain")?.Part == lateenPart &&
                  ShipRepairs.For(l, st, lateenPart, "game:sail")?.Part == lateenPart && ShipRepairs.For(l, st, lateenPart, "game:sail-large-oak")?.Part == lateenPart &&
                  ShipRepairs.For(l, st, lateenPart, "game:linen-normal-down")?.Part == lateenPart && ShipRepairs.For(l, st, lateenPart, "game:stick") == null,
                $"a torn lateen is mended on it or at the mizzen: {Kits(atSail[0])}");
            var torn = atSail[0];
            bool p1 = ShipRepairs.Plan(torn, new List<(string, int)> { ("game:cloth-red", 4) }, out var t1, out _) && t1.SequenceEqual(new[] { 4 });
            bool p2 = !ShipRepairs.Plan(torn, new List<(string, int)> { ("game:cloth-red", 3) }, out _, out _);
            bool p3 = ShipRepairs.Plan(torn, new List<(string, int)> { ("game:cloth-red", 2), ("game:cloth-blue", 2) }, out var t3, out _) && t3.SequenceEqual(new[] { 2, 2 });
            bool p4 = ShipRepairs.Plan(torn, new List<(string, int)> { ("game:cloth-red", 4), ("game:sail", 1) }, out var t4, out _) && t4.SequenceEqual(new[] { 4, 0 });
            bool p5 = ShipRepairs.Plan(torn, new List<(string, int)> { ("game:sail-large-oak", 1), ("game:cloth-red", 9) }, out var t5, out _) && t5.SequenceEqual(new[] { 1, 0 });
            Check(torn.Kits.Length == 2 && torn.Kits[0].Items.Single().Count == 1 && torn.Kits[1].Items.Single().Count == 4 && p1 && p2 && p3 && p4 && p5,
                "torn: one sail, or 4 cloth of any colours (or linen); what's in hand is used first (cloth in hand with a sail aboard takes the cloth)");
            st.Hurt[lateenPart] = d.Shredded + 0.01f;
            var shred = ShipRepairs.Needs(l, st, lateenPart)[0];
            bool s1 = ShipRepairs.Plan(shred, new List<(string, int)> { ("game:sail", 2) }, out _, out _);
            bool s2 = ShipRepairs.Plan(shred, new List<(string, int)> { ("game:cloth-plain", 4), ("game:sail", 1) }, out var u2, out _) && u2.SequenceEqual(new[] { 4, 1 });
            // (0.3.10, the user stranded with shredded sails: 8 cloth alone do it too)
            bool s3 = ShipRepairs.Plan(shred, new List<(string, int)> { ("game:cloth-plain", 8) }, out _, out _) &&
                      !ShipRepairs.Plan(shred, new List<(string, int)> { ("game:cloth-plain", 7) }, out _, out _);
            Check(shred.Kits.Length == 3 && s1 && s2 && s3 && ShipRepairs.For(l, st, lateenPart, "game:cloth-plain") != null,
                $"shredded: {Kits(shred)} (8 cloth alone too, 7 won't do)");
            // what the hint names exists: a lang line for each item, and the items in the game
            var lang = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Path.Combine(assets, "lang", "en.json")));
            var patterns = new[] { ShipRepairs.Sail, ShipRepairs.Cloth, ShipRepairs.Plank, ShipRepairs.Nails, ShipRepairs.Flax, ShipRepairs.Resin, ShipRepairs.Rope };
            Check(patterns.All(pt => lang.ContainsKey("item-" + ShipRepairs.ItemKey(pt))) && lang.ContainsKey("repair-or") && !lang.ContainsKey("item-linen") &&
                  lang["repair-hint-sail"].Contains("{0}"), "the hint and the 'you need' message name every way to mend it (a line for each item, joined by 'or')");
            string vanilla = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vintagestory", "assets", "survival");
            if (Directory.Exists(vanilla))
            {
                var codes = new HashSet<string>();
                foreach (var file in Directory.EnumerateFiles(Path.Combine(vanilla, "itemtypes"), "*.json", SearchOption.AllDirectories)
                             .Concat(Directory.EnumerateFiles(Path.Combine(vanilla, "blocktypes"), "*.json", SearchOption.AllDirectories)))
                {
                    var mc = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(file), "^\\s*code:\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.Multiline);
                    if (mc.Success) codes.Add(mc.Groups[1].Value);
                }
                var missing = new List<string>();
                foreach (var pt in patterns)
                    foreach (var alt in pt.Split('|'))
                    {
                        string c = alt.Substring(alt.IndexOf(':') + 1);
                        if (c.EndsWith("-*")) c = c.Substring(0, c.Length - 2);
                        if (!codes.Contains(c)) missing.Add(alt);
                    }
                Check(missing.Count == 0, $"every item a repair takes is in the game (sail, sail-large, cloth, linen, plank, ...){(missing.Count > 0 ? ": missing " + string.Join(", ", missing) : "")}");
            }

            // ---- the lateen bellies to leeward on both tacks, its clew to leeward of the mizzen
            if (lateenSail >= 0)
            {
                var f = l.SailFrames[lateenSail];
                var sd = def.Sailing;
                var yard = f.Yard;
                int bad = 0;
                double worst = double.MaxValue;
                var ym = new float[16];
                var bm = new float[16];
                var mm2 = new float[16];
                double[] Apply(float[] m, double[] q) => new double[] { m[0] * q[0] + m[4] * q[1] + m[8] * q[2] + m[12], m[1] * q[0] + m[5] * q[1] + m[9] * q[2] + m[13], m[2] * q[0] + m[6] * q[1] + m[10] * q[2] + m[14] };
                foreach (int side in new[] { 1, -1 })
                    for (double awa = 50; awa <= 175; awa += 5)
                    {
                        double a = awa * Math.PI / 180, wx = -Math.Cos(a), wz = -side * Math.Sin(a);   // where the air goes
                        Sailing.Trim(awa, side, sd, out _, out double sheet);
                        PartMotion.PartMatrix(yard, PartMotion.TurnDegrees(yard, (float)sheet) * GameMath.DEG2RAD, ym);
                        PartMotion.BellyMatrix(f, PartMotion.LateenBelly(sheet), bm);
                        Mat4f.Mul(mm2, ym, bm);
                        // the mesh's belly (its +N side, as generated) at the middle of the sail, and the sail's plane there
                        var belly = Apply(mm2, f.FromSail(f.YardLength * 0.45, f.ClewV * 0.4, 0.7));
                        var flat = Apply(mm2, f.FromSail(f.YardLength * 0.45, f.ClewV * 0.4, 0));
                        double downwind = ((belly[0] - flat[0]) * wx + (belly[2] - flat[2]) * wz) / 0.7;
                        var clewNow = Apply(ym, f.FromSail(f.ClewU, f.ClewV, 0));
                        bool lee = (clewNow[2] - def.Masts[mizzen].Z) * side < 0;
                        worst = Math.Min(worst, downwind);
                        if (downwind <= 0 || !lee) bad++;
                    }
                Check(bad == 0, $"the lateen swings out to leeward and bellies away from the wind on both tacks (least {worst:0.00} of its belly downwind)");
                // the mesh is made bellied to its +N side (starboard), which the mod mirrors on the starboard tack
                string skinPath = Path.Combine(assets, "config", "shipmeshes", def.Code + ".json");
                if (File.Exists(skinPath))
                {
                    var skin = JsonConvert.DeserializeObject<SkinLite>(File.ReadAllText(skinPath));
                    if (skin?.Groups != null && def.Sails[lateenSail].Mesh is string gname && skin.Groups.TryGetValue(gname, out var quads))
                    {
                        double sum = 0;
                        int n = 0;
                        foreach (var q in quads)
                            for (int v = 0; v < 4; v++)
                            {
                                f.ToSail(q[1 + 8 * v], q[2 + 8 * v], q[3 + 8 * v], out _, out _, out double nn, true);
                                sum += nn;
                                n++;
                            }
                        Check(n > 0 && sum / n > 0.1, $"the lateen's mesh is bellied to its +N side ({(f.N[2] > 0 ? "starboard" : "port")}), as the mod mirrors it (mean {sum / Math.Max(1, n):0.00})");
                    }
                }
                // the drawn yard turns no further than the generator's rig check lets it (the shrouds)
                Check(sd.MaxSheet <= 26 + 1e-6 && (yard.Limit <= 0 || yard.Limit <= 26 + 1e-6), $"the lateen is sheeted out at most {sd.MaxSheet}° (its shrouds stop it at 26°)");
            }
        }
    }
}
