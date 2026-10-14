using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>Her damage parts, her hydrostatics and what water in her does, as a table (dotnet run -c Release -- damage).</summary>
        static void DamageReport(ShipDefinition def)
        {
            var l = def.DamageParts;
            var d = l.D;
            Console.WriteLine($"{def.Code}: {l.Count} damage parts: " + string.Join(", ", Enum.GetValues<DamageKind>().Select(k => $"{l.Parts.Count(p => p.Kind == k)} {k}")));
            Console.WriteLine($"  hull panels below the waterline: {l.Parts.Count(p => p.Kind == DamageKind.Hull && p.Center.Y < def.Waterline)}, " +
                $"panel area {l.Parts.Where(p => p.Kind == DamageKind.Hull).Average(p => p.Area):0.00} on average");
            Console.WriteLine($"  displacement {l.Displacement:0} t, reserve buoyancy to the main deck's edge {l.Reserve:0} t, the hold holds {l.Capacity:0} t");
            Console.WriteLine($"  waterplane {l.WaterplaneArea:0} m², BM {l.BM:0.00}, GM {l.GM:0.00}, hold bottom {l.HoldBottom:0.00}; {l.Downflood.Length} downflood points");
            Console.WriteLine("  water (t)  sinkage  level inside  GM eff  list (deg)  roll period  sluggish");
            foreach (double w in new[] { 0, 5, 20, 50, 100, 200, 300, 400, 500, 700, 1000 })
            {
                var mo = ShipFlood.Motion(l, def.Motion, w);
                Console.WriteLine($"  {w,8:0}  {l.SinkageFor(w),7:0.00}  {l.FloodLevel(w),12:0.00}  {ShipFlood.EffectiveGM(l, w),6:0.00}  " +
                    $"{ShipFlood.List(l, w, 0, 1) * 180 / Math.PI,10:0.0}  {mo.RollPeriod,11:0.0}  {ShipFlood.Sluggish(l, w),8:0.00}");
            }
            Console.WriteLine("  leak a panel at 2 blocks deep (l/s): " + string.Join(", ", new (string, float, float)[]
            {
                ("worn", 0, d.Worn), ("cracked", d.Cracked, 0), ("leaking", 0, d.Leaking), ("holed", d.Holed, 0), ("stove in", 1, 0)
            }.Select(t => $"{t.Item1} {1000 * ShipFlood.Flow(d.LeakCoefficient, l.LeakArea(l.Parts[0], t.Item2, t.Item3), 2, 0):0.0}")));
            Console.WriteLine($"  a pump: {1000 * ShipFlood.PumpRate(d, 1):0} l/s a pumper, {d.PumpersPerPump} at a brake");
            foreach (double v in new[] { 0.3, 0.5, 1, 2, 3, 3.5, 5, 7 })
            {
                var st = new DamageState(l.Count);
                var bow = BowContact(def);
                var r = ShipDamage.Apply(l, st, def.Grid, bow, new Vec3d(1, 0, 0), ShipDamage.ImpactEnergy(d, l.Displacement, v), d.GroundRadius, DamageKinds.Bottom);
                var worst = r.Hits.OrderByDescending(h => st.Hurt[h.Part]).FirstOrDefault();
                Console.WriteLine($"  aground at {v:0.0} b/s: {ShipDamage.ImpactEnergy(d, l.Displacement, v):0} kJ, {r.Hits.Count} parts, worst {st.Hurt[worst.Part]:0.00} ({worst.After})");
            }
        }

        /// <summary>A point on her stem at the waterline (where she meets a wall head on).</summary>
        static Vec3d BowContact(ShipDefinition def)
        {
            var g = def.Grid;
            int j = g.CellY(def.Waterline + 0.25), k = g.CellZ(0.1);
            for (int i = g.NX - 1; i >= 0; i--)
                if (g.Solid(i, j, k)) return new Vec3d(g.OX + (i + 0.9) * g.V, def.Waterline + 0.25, 0.1);
            return new Vec3d(0, 0, 0);
        }

        /// <summary>
        /// The damage model (Hansa 0.3.5): her parts, finding the one at a spot, a blow's spread and a shot going
        /// through, grounding, the leak math, how deep and how far over water puts her, the sea coming in over her side,
        /// the pumps against a leak and a hole, repairs taking their items, and the state saved and synced.
        /// </summary>
        static void Damage(ShipDefinition def)
        {
            Console.WriteLine("damage: her parts, blows, leaks, the water in her, pumps, repairs");
            var l = def.DamageParts;
            var d = l.D;
            var g = def.Grid;
            int Count(DamageKind k) => l.Parts.Count(p => p.Kind == k);
            var hull = l.Parts.Where(p => p.Kind == DamageKind.Hull).ToArray();
            int below = hull.Count(p => p.Center.Y < def.Waterline);
            Check(Count(DamageKind.Hull) > 300 && below > 100 && Count(DamageKind.Deck) > 40 && Count(DamageKind.Rudder) == 1 &&
                  Count(DamageKind.Mast) == def.Masts.Length && Count(DamageKind.Sail) == def.Sails.Length && Count(DamageKind.Rigging) == def.Masts.Length,
                $"her parts: {Count(DamageKind.Hull)} hull panels ({below} below the waterline), {Count(DamageKind.Deck)} deck parts, the rudder, " +
                $"{Count(DamageKind.Mast)} masts, {Count(DamageKind.Sail)} sails, {Count(DamageKind.Rigging)} riggings");
            Check(l.Parts.Select(p => p.Code).Distinct().Count() == l.Count && l.Parts.All(p => l.Find(p.Code) == p.Index && ShipPart.Decode(p.Code).Encode() == p.Code),
                "every part has a code of its own, the one it's found and saved by");

            // ---- torn and shredded sails draw less (the user, 0.3.9: "my sails are shredded but that doesn't seem to affect
            // the roll"): their drive, the heel the wind gives her and the steadying of her roll all go by what still draws
            {
                var sst = new DamageState(l.Count);
                double whole = SailArea.Drive(l, sst);
                foreach (var p in l.Parts.Where(p => p.Kind == DamageKind.Sail)) sst.Hurt[p.Index] = d.Torn + 0.01f;
                double torn = SailArea.Drive(l, sst);
                foreach (var p in l.Parts.Where(p => p.Kind == DamageKind.Sail)) sst.Hurt[p.Index] = d.Shredded + 0.01f;
                double shredded = SailArea.Drive(l, sst);
                var md = def.Motion; var sd = def.Sailing;
                double hWhole = ShipMotion.Heel(0, 1, Math.PI / 2, 0, 1 * whole, md, sd), hShred = ShipMotion.Heel(0, 1, Math.PI / 2, 0, 1 * shredded, md, sd);
                double stWhole = ShipMotion.SailSteadiness(whole, 1), stShred = ShipMotion.SailSteadiness(shredded, 1);
                Check(Math.Abs(whole - 1) < 1e-9 && Math.Abs(torn - d.TornArea) < 1e-6 && Math.Abs(shredded - d.ShreddedArea) < 1e-6 &&
                      Math.Abs(hShred) < Math.Abs(hWhole) * 0.3 && stShred < stWhole * 0.3,
                    $"torn sails draw {torn:0.00} of their cloth, shredded {shredded:0.00}: under full sail in a beam wind she heels {hWhole * 180 / Math.PI:0.0}° whole, " +
                    $"{hShred * 180 / Math.PI:0.0}° shredded, and her sails steady her roll {stWhole:0.00} → {stShred:0.00}");
            }

            // ---- mending from outside and from inside (0.3.10, the user: "trying to open chests placed inside", "the floors
            // prevent access, as well as chests or any other placed blocks")
            if (def.Mounts != null && def.Mounts.OnShipGrid)
            {
                const string planks = "game:plank-oak";
                var ml = def.Mounts;
                // a chest on the lower deck's outermost starboard cell, seen from the water abeam
                var cell = ml.Floors.Where(m => m.Room == "lowerdeck" && ml.FloorAt(m.Deck, m.I, m.K + 1) == null && Math.Abs(m.X) < 6).OrderByDescending(m => m.Z).First();
                var chestPc = new ShipPieceSet();
                var cplan = new PiecePlan { Kind = MountKind.Floor, Mount = cell };
                cplan.Mounts.Add(cell.Id);
                cplan.Sel.Add(new[] { cell.X + 0.05, cell.Y, cell.Z + 0.05, cell.X + 0.95, cell.Y + 0.9, cell.Z + 0.95 });
                cplan.Box = cplan.Sel[0];
                chestPc.Add(ShipPiece.From(cplan, 1, null));
                var eyeOut = new Vec3d(cell.X + 0.5, cell.Y + 0.45, cell.Z + 8);
                var dIn = new Vec3d(0, 0, -1);
                bool hullFirst = ShipParts.Ray(def, g, 0, 0, 0, Math.PI / 2, 0, 0, eyeOut, dIn, 12, f => false, f => false, f => null, out var outHit) && outHit.Part.Kind == ShipPartKind.Hull;
                var seenChest = chestPc.Pick(eyeOut, dIn, 12, out double tChest);
                bool chestHidden = seenChest != null && ShipPieceSet.Behind(g, eyeOut, dIn, tChest) && !ShipPieceSet.Seen(g, eyeOut, cplan.Box);
                // with planks in hand, the panel there (cracked) is what's picked, from outside only that panel
                int outPanel = l.Find(outHit.Part);
                var rs = new DamageState(l.Count);
                if (outPanel >= 0) rs.Hurt[outPanel] = d.Cracked + 0.05f;
                int picked = outPanel >= 0 ? ShipRepairs.Pick(l, rs, g, eyeOut, dIn, 12, planks, ShipRepairs.OutsideLookBehind, out _) : -1;
                // from inside: the chest seen, and over it the panel behind the ceiling
                var eyeIn = new Vec3d(cell.X + 0.5, cell.Y + 1.6, cell.Z - 2);
                var toWall = new Vec3d(0, -0.25, 1);
                toWall.Mul(1 / toWall.Length());
                int insidePick = outPanel >= 0 ? ShipRepairs.Pick(l, rs, g, eyeIn, toWall, 5, planks, 1.5, out _) : -1;
                bool chestSeenIn = ShipPieceSet.Seen(g, eyeIn, cplan.Box);
                Check(hullFirst && chestHidden && picked == outPanel && outPanel >= 0 && chestSeenIn,
                    $"from the water abeam, aiming at a chest just inside her side ({cell.Id}): the ray meets her hull ({outHit.Part}), the chest is behind it " +
                    $"(not used, not taken); with planks in hand the cracked panel there is the one mended (picked {picked}); from inside the chest is seen " +
                    $"and the same panel is mended through her ceiling (picked {insidePick})");

                // every hull panel below her waterline mendable from inside: from a floor place in reach, looking at it
                // (through what's aboard, her ceiling, the hold's floor), within the picking range
                var belowWl = l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Center.Y < def.Waterline).ToList();
                var eyes = ml.Floors.Where(m => m.Room != null && def.Rooms.Any(r => r.Code == m.Room && r.Enclosed)).Select(m => new Vec3d(m.X + 0.5, m.Y + 1.6, m.Z + 0.5)).ToList();
                var unreachable = new List<string>();
                foreach (var p in belowWl)
                {
                    var st2 = new DamageState(l.Count);
                    st2.Hurt[p.Index] = d.Cracked + 0.05f;
                    bool ok = false;
                    var aims = new List<Vec3d> { p.Center };
                    for (int n = 0; n < 8; n++)
                        aims.Add(new Vec3d((n & 1) == 0 ? p.Min[0] + 0.15 : p.Max[0] - 0.15, (n & 2) == 0 ? p.Min[1] + 0.15 : p.Max[1] - 0.15, (n & 4) == 0 ? p.Min[2] + 0.15 : p.Max[2] - 0.15));
                    foreach (var eye in eyes)
                    {
                        if (p.DistanceTo(eye) > 6) continue;
                        foreach (var aim in aims)
                        {
                            var dv = new Vec3d(aim.X - eye.X, aim.Y - eye.Y, aim.Z - eye.Z);
                            dv.Mul(1 / Math.Max(1e-9, dv.Length()));
                            if (ShipRepairs.Pick(l, st2, g, eye, dv, 4.5, planks, 1.5, out _) == p.Index) { ok = true; break; }
                        }
                        if (ok) break;
                    }
                    if (!ok) unreachable.Add(p.Part.ToString());
                }
                Console.WriteLine($"         below her waterline: {belowWl.Count} hull panels, {belowWl.Count - unreachable.Count} mendable from inside" +
                    (unreachable.Count > 0 ? $"; only from outside (diving) or in port: {string.Join("; ", unreachable)}" : ""));
                Check(unreachable.Count <= belowWl.Count / 10,
                    $"nearly every hull panel below her waterline can be mended from inside her (from a floor place, within the picking range, through her ceiling and the hold's floor): {belowWl.Count - unreachable.Count} of {belowWl.Count}");

                // from the water: right-clicking her side with something in hand never boards; empty-handed only aiming at a ladder
                var lad = def.Ladders.FirstOrDefault(x => x.Outside);
                if (lad != null)
                {
                    var foot = new Vec3d(lad.Bottom[0], lad.Bottom[1], lad.Bottom[2] + Math.Sign(lad.Bottom[2]) * 0.8);
                    var atLadder = new Vec3d((lad.Bottom[0] + lad.Top[0]) / 2, (lad.Bottom[1] + lad.Top[1]) / 2, (lad.Bottom[2] + lad.Top[2]) / 2);
                    var away = new Vec3d(lad.Bottom[0] - 4, lad.Bottom[1], lad.Bottom[2]);
                    var b1 = Boarding.OutsideClick(def.Ladders, foot, atLadder, false, out bool h1);
                    var b2 = Boarding.OutsideClick(def.Ladders, foot, atLadder, true, out bool h2);
                    var b3 = Boarding.OutsideClick(def.Ladders, foot, away, false, out bool h3);
                    var b4 = Boarding.OutsideClick(def.Ladders, foot, away, true, out bool h4);
                    var near15 = new Vec3d(atLadder.X + 1.5, atLadder.Y, atLadder.Z);
                    var b5 = Boarding.OutsideClick(def.Ladders, foot, near15, true, out _);
                    var b6 = Boarding.OutsideClick(def.Ladders, foot, near15, false, out _);
                    Check(b1 == lad && !h1 && b2 == lad && !h2 && b3 == null && h3 && b4 == null && !h4 && b5 == null && b6 == lad,
                        "from the water: a right-click on her rope ladder climbs it, empty-handed or with planks in hand; 1.5 blocks off it only empty-handed " +
                        "(with planks the click mends her side there); 4 blocks off it no boarding, a hint only empty-handed");
                }
            }
            // a panel is the planking between two frames by a block of her height (the lowest band takes in her keel and
            // her flat floor), its outer face outward
            double stationLen = def.Frames.Space;
            int outward = hull.Count(p => p.Center.Y < def.DeckY && Math.Abs(p.Normal.Z) > 0.5 ? Math.Sign(p.Normal.Z) == p.Part.Side : true);
            Check(hull.All(p => p.Max[0] - p.Min[0] <= stationLen + 0.26 + (p.Part.Station == 0 ? 1 : 0) && p.Max[1] - p.Min[1] <= 1.26 + (p.Part.Band == 0 ? 0.75 : 0) && p.Area > 0) &&
                  outward >= hull.Length * 0.97,
                $"a hull panel is at most a frame space long and a block high, with an outer face that faces out ({outward} of {hull.Length})");
            Check(d.HullToughness == 80 && d.Rudder?.Length == 6 && d.PumpersPerPump == 2 && def.Fixtures.Any(f => f.Type == "pump") &&
                  def.MovingParts.Any(m => m.Source == "pump:pump"),
                "the design's damage section is read (toughness, her rudder), and she has a bilge pump whose brake moves");
            Check(l.Displacement > 600 && l.Reserve > 300 && l.Capacity > l.Reserve && l.GM > 0.5 && l.BM > 1 && l.WaterplaneArea > 200,
                $"her hydrostatics: displacement {l.Displacement:0} t, reserve buoyancy {l.Reserve:0} t, the hold holds {l.Capacity:0} t, GM {l.GM:0.00}, BM {l.BM:0.00}");

            // ---- the panel lookup: where the crosshair meets her, the damage part is there
            ShipRayHit Ray(double ox, double oy, double oz, double dx, double dy, double dz)
            {
                ShipParts.Ray(def, g, 0, 0, 0, Math.PI / 2, 0, 0, new Vec3d(ox, oy, oz), new Vec3d(dx, dy, dz), 30, f => f.Type != "attach", f => false, f => null, out var h);
                return h;
            }
            int found = 0, rays = 0, agree = 0;
            foreach (double x in new[] { -10.0, -4.0, 0.5, 5.0, 11.0 })
                foreach (double y in new[] { def.Waterline - 2.5, def.Waterline - 1.0, def.Waterline + 0.5, def.DeckY + 0.5 })
                    foreach (int side in new[] { -1, 1 })
                    {
                        var h = Ray(x, y, side * 20, 0, 0, -side);
                        if (h.Part.Kind != ShipPartKind.Hull) continue;
                        rays++;
                        int i = l.Find(h.Part);
                        if (i >= 0 && l.Parts[i].Kind == DamageKind.Hull && l.Parts[i].DistanceTo(h.Local) < 0.3) found++;
                        if (i >= 0 && l.PartAt(h.Local, DamageKinds.Hull) == i) agree++;
                    }
            {
                // from inside the hold, its side; from under her, her bottom
                var hi = Ray(-5, (Array.Find(def.Decks, dk => dk.Code == "hold")?.Y ?? -3.0) + 1.1, 0.5, 0, 0, 1);
                var hb = Ray(2, def.KeelY - 3, 1.2, 0, 1, 0);
                foreach (var h in new[] { hi, hb })
                {
                    if (h.Part.Kind != ShipPartKind.Hull) continue;
                    rays++;
                    int i = l.Find(h.Part);
                    if (i >= 0 && l.Parts[i].DistanceTo(h.Local) < 0.6) found++;
                    if (i >= 0 && l.PartAt(h.Local, DamageKinds.Hull) == i) agree++;
                }
            }
            Check(rays >= 34 && found == rays && agree >= rays * 0.9,
                $"where the crosshair meets her hull (from the water, from under her, from the hold) is a damage panel's, and the panel at that spot is it ({found} and {agree} of {rays})");
            int mainMast = l.Find(ShipPart.Mast(0).Encode()), rudder = Array.FindIndex(l.Parts, p => p.Kind == DamageKind.Rudder);
            Check(mainMast >= 0 && l.Parts[mainMast].Kind == DamageKind.Mast && rudder >= 0 &&
                  l.PartAt(new Vec3d((d.Rudder[0] + d.Rudder[3]) / 2, (d.Rudder[1] + d.Rudder[4]) / 2, 0)) == rudder,
                "the mainmast and the rudder are parts of their own, found where they are");

            // ---- a blow's spread: the nearest takes most, none more than it has left, all within the radius
            var mid = hull.Where(p => p.Part.Side < 0 && p.Center.Y < def.Waterline - 1.5 && p.Center.Y > def.Waterline - 2.5 && Math.Abs(p.Center.X) < 4)
                .OrderBy(p => Math.Abs(p.Center.X)).First();
            var st = new DamageState(l.Count);
            var r1 = ShipDamage.Apply(l, st, g, mid.Center, new Vec3d(0, 0, 1), 60, 1.25, DamageKinds.Planking);
            var top = r1.Hits.OrderByDescending(h => h.Added).First();
            double took = r1.Hits.Sum(h => h.Added * l.Toughness(l.Parts[h.Part].Kind));
            Check(top.Part == mid.Index && r1.Hits.Count >= 3 && Math.Abs(took - 60) < 1e-4 && r1.Hits.All(h => l.Parts[h.Part].DistanceTo(mid.Center) < 1.25) &&
                  st.Wear[mid.Index] > 0 && !r1.Pierced,
                $"a blow (60 kJ over 1.25) at a panel's middle: that panel takes most ({st.Hurt[mid.Index]:0.00}), {r1.Hits.Count} round it some, all of it taken, its seams started, nothing goes through" +
                $" [most to {top.Part} (the panel {mid.Index}), pierced {r1.Pierced}, took {took:R}, farthest {r1.Hits.Max(h => l.Parts[h.Part].DistanceTo(mid.Center)):0.00}, wear {st.Wear[mid.Index]:0.###}]");
            ShipDamage.Apply(l, st, g, mid.Center, new Vec3d(0, 0, 1), 500, 1.25, DamageKinds.Planking);
            Check(st.Hurt.All(h => h <= 1) && st.Hurt[mid.Index] == 1 && l.StateOf(DamageKind.Hull, st.Hurt[mid.Index], st.Wear[mid.Index]) == PartState.Holed,
                "a big blow wrecks the panel (hurt 1, holed) and never more");
            // a shot: through her side, across the hold, into what's in its way
            st = new DamageState(l.Count);
            var shot = ShipDamage.Apply(l, st, g, mid.Center, new Vec3d(0, 0, 1), 400, 0.3);
            var hitSides = shot.Hits.Where(h => l.Parts[h.Part].Kind == DamageKind.Hull).Select(h => l.Parts[h.Part].Part.Side).Distinct().ToArray();
            Check(shot.Pierced && l.StateOf(DamageKind.Hull, st.Hurt[mid.Index], 0) == PartState.Holed && shot.Hits.Count >= 2 && shot.Hits.Any(h => h.Part != mid.Index),
                $"a shot (400 kJ on a spot) holes the planking and goes on through her ({shot.Hits.Count} parts: " +
                string.Join(", ", shot.Hits.Select(h => $"{l.Parts[h.Part].Part} {st.Hurt[h.Part]:0.00}")) + (shot.Exit != null ? $"; out at {shot.Exit.Z:0.0}" : "") + ")");
            st = new DamageState(l.Count);
            var weak = ShipDamage.Apply(l, st, g, mid.Center, new Vec3d(0, 0, 1), 20, 0.3);
            Check(!weak.Pierced && weak.Hits.Count >= 1 && st.Hurt[mid.Index] < d.Holed, "a weak shot (20 kJ) stays in the planking");

            // ---- grounding: a slow touch scrapes, a hard one holes
            var bow = BowContact(def);
            double Ground(double v)
            {
                var s = new DamageState(l.Count);
                ShipDamage.Apply(l, s, g, bow, new Vec3d(1, 0, 0), ShipDamage.ImpactEnergy(d, l.Displacement, v), d.GroundRadius, DamageKinds.Bottom);
                return s.Hurt.Max();
            }
            double g05 = Ground(0.5), g1 = Ground(1), g25 = Ground(2.5), g4 = Ground(4);
            Check(l.StateOf(DamageKind.Hull, (float)g05, 0) == PartState.Sound && l.StateOf(DamageKind.Hull, (float)g1, 0) == PartState.Scraped &&
                  l.StateOf(DamageKind.Hull, (float)g25, 0) == PartState.Cracked && l.StateOf(DamageKind.Hull, (float)g4, 0) == PartState.Holed,
                $"running her stem into rock: 0.5 b/s leaves her sound ({g05:0.00}), 1 scrapes ({g1:0.00}), 2.5 cracks ({g25:0.00}), 4 holes her ({g4:0.00})");

            // ---- the leak math
            double c = d.LeakCoefficient;
            Check(Math.Abs(ShipFlood.Flow(c, 0.01, 2, 0) - c * 0.01 * Math.Sqrt(2)) < 1e-12 && ShipFlood.Flow(c, 0.01, 1, 3) < 0 &&
                  ShipFlood.Flow(c, 0.01, -1, -1) == 0 && Math.Abs(ShipFlood.Flow(c, 0.01, 3, 1) - c * 0.01 * Math.Sqrt(2)) < 1e-12,
                "a hole lets in area × √(the head outside less the head inside), and out when the water inside stands higher");
            float Area(float hurt, float wear) => (float)l.LeakArea(mid, hurt, wear);
            Check(Area(0, 0) == 0 && Area(d.Scraped, 0) == 0 && Area(0, d.Worn) > 0 && Area(d.Cracked, 0) > Area(0, d.Worn) &&
                  Area(0, d.Leaking) > Area(d.Cracked, 0) && Area(d.Holed, 0) > 20 * Area(0, d.Leaking) && Area(1, 0) > 3 * Area(d.Holed, 0) &&
                  l.LeakArea(l.Parts[mainMast], 1, 1) == 0,
                $"leaks up the ladder: sound and scraped none, worn a trickle, cracked, leaking, holed ({1000 * ShipFlood.Flow(c, Area(d.Holed, 0), 2, 0):0} l/s at 2 deep), stove in more; spars none");

            // ---- how deep and how far over the water puts her
            Check(l.SinkageFor(0) == 0 && l.SinkageFor(100) > 0 && l.SinkageFor(300) > l.SinkageFor(100) &&
                  Math.Abs(l.SinkageFor(l.Reserve) - (def.DeckY - def.Waterline)) < 0.02 &&
                  Math.Abs(l.SinkageFor(100) - 100 / l.WaterplaneArea) < 0.1,
                $"water floats her deeper: 100 t {l.SinkageFor(100):0.00} (≈ the waterplane's {100 / l.WaterplaneArea:0.00}), 300 t {l.SinkageFor(300):0.00}; her reserve buoyancy takes her main deck's edge down to the water");
            Check(l.FloodLevel(0) == l.HoldBottom && l.FloodLevel(50) > l.HoldBottom && l.FloodLevel(300) > l.FloodLevel(50) &&
                  Math.Abs(l.AirVolume(l.FloodLevel(200)) - 200) < 0.5,
                $"the water in her stands at the level that holds it (200 t: {l.FloodLevel(200):0.00})");
            double list0 = ShipFlood.List(l, 0, 0, 1), list100 = ShipFlood.List(l, 100, 0, 1), list300 = ShipFlood.List(l, 300, 0, 1), listPort = ShipFlood.List(l, 100, 0, -1);
            double heeled = ShipFlood.List(l, 300, 5 * Math.PI / 180, 1);
            Check(list0 == 0 && list100 > 0 && list300 > list100 && Math.Abs(listPort + list100) < 1e-9 && heeled > list300 &&
                  ShipFlood.EffectiveGM(l, 300) < ShipFlood.EffectiveGM(l, 50) && ShipFlood.List(l, 5000, 0, 1) <= d.MaxList * Math.PI / 180 + 1e-9,
                $"the water runs to the low side: no list dry, {list100 * 180 / Math.PI:0.0}° at 100 t, {list300 * 180 / Math.PI:0.0}° at 300 t (to port when it came in to port), " +
                $"more heeled in the wind ({heeled * 180 / Math.PI:0.0}°), never past {d.MaxList}°");
            var mo = ShipFlood.Motion(l, def.Motion, 300);
            Check(mo.RollPeriod > def.Motion.RollPeriod && mo.RollDamping > def.Motion.RollDamping && mo.HeavePeriod == def.Motion.HeavePeriod &&
                  mo.PitchPeriod == def.Motion.PitchPeriod && ShipFlood.Motion(l, def.Motion, 0) == def.Motion &&
                  ShipFlood.Sluggish(l, 0) == 1 && ShipFlood.Sluggish(l, 300) < 0.8,
                $"with water in her she rolls slower ({mo.RollPeriod:0.0} s against {def.Motion.RollPeriod}) and more damped, heaves and pitches as ever (the server's heave and the clients' agree), and she's slower ({ShipFlood.Sluggish(l, 300):0.00})");

            // ---- the sea coming in over her side
            FloodPose Pose(double sink, double roll, double wave = 0) => new FloodPose { X = 0, Y = -sink, Z = 0, Yaw = Math.PI / 2, Pitch = 0, Roll = roll, Surface = (x, z) => wave };
            double free = def.DeckY - def.Waterline;
            double up = ShipFlood.Downflooding(l, Pose(0, 0), _ => false), low = ShipFlood.Downflooding(l, Pose(free - 0.3, 0), _ => false);
            double awash = ShipFlood.Downflooding(l, Pose(free + 0.2, 0), _ => false), over = ShipFlood.Downflooding(l, Pose(1.0, 25 * Math.PI / 180), _ => false);
            double wave = ShipFlood.Downflooding(l, Pose(free - 0.3, 0, 0.8), _ => false);
            double hatch = ShipFlood.Downflooding(l, Pose(free - 0.1, 0, 0.3), c2 => true) - ShipFlood.Downflooding(l, Pose(free - 0.1, 0, 0.3), _ => false);
            Check(up == 0 && low == 0 && awash > 0.01 && over > 0.3 && wave > awash * 0.5 && hatch > 0,
                $"the sea comes aboard once her deck's edge is under it: not upright and dry, nor with {free - 0.3:0.0} sunk; {awash:0.00} t/s gets below with her deck awash, " +
                $"{over:0.0} heeled 25° a block deep, {wave:0.00} when a wave's crest reaches it, more through an open hatch or gate (+{hatch:0.00})");

            // the side of the waist: its lowest edge is the bulwark's top (it leans in over the deck), a gun port's sill, a
            // shut gate's top; and what comes over it lands on the deck, most of it running off again
            var sides = l.Downflood.Where(f => f.OpenFixture == null && f.Weight >= 1).ToArray();
            var guns = def.Fixtures.Where(f => f.Type == "gun" && f.Box != null).ToArray();
            bool lids = guns.Length > 0 && guns.All(gn => gn.Lid != null);
            bool InPort(DownfloodPoint f) => guns.Any(gn => f.X >= gn.Box[0] - 0.01 && f.X <= gn.Box[3] + 0.01 && Math.Sign(gn.Box[2] + gn.Box[5]) == Math.Sign(f.Z));
            // (lidded ports: their sills only while open, the gun run out)
            var sills = lids ? l.Downflood.Where(f => guns.Any(gn => gn.Code == f.OpenFixture)).ToArray() : sides.Where(InPort).ToArray();
            double lowRail = sides.Where(f => lids || !InPort(f)).Min(f => f.Y - def.DeckY), lowPort = sills.DefaultIfEmpty().Min(f => f.Y - def.DeckY);
            double crest = ShipFlood.Downflooding(l, Pose(0, 0, free + 1.4), _ => false), openHatch = ShipFlood.Downflooding(l, Pose(0, 0, free + 1.4), c2 => c2.StartsWith("hatch"));
            Check(lowRail >= 0.9 && (guns.Length == 0 || lowPort >= 0.2 && lowPort < 0.9) && openHatch > crest * 2,
                $"the waist's side stands {lowRail:0.00} over the deck at its lowest (the bulwark's top or a shut gate{(lids ? ", a shut port's lid" : "")}), {lowPort:0.00} at the gun ports' sills{(lids ? " (open)" : "")}; " +
                $"the sea 0.4 over her rail all along the waist puts {crest:0.00} t/s below, {openHatch:0.00} with the waist hatch open");
            // (the eighth client test) lidded ports: with the sea a little over their sills (under the rail), shut they let
            // nothing in, open (the guns run out) they do
            if (lids)
            {
                double sea = lowPort + 0.25;
                double shut = ShipFlood.Downflooding(l, Pose(0, 0, free + sea), _ => false);
                double portsOpen = ShipFlood.Downflooding(l, Pose(0, 0, free + sea), c2 => guns.Any(gn => gn.Code == c2));
                Check(sills.Length > 0 && portsOpen > shut + 0.05,
                    $"the gun ports' lids: the sea {sea:0.00} over the deck (over the sills, under the rail) puts {shut:0.00} t/s below with them shut, {portsOpen:0.00} with the guns run out ({sills.Length} columns of sill)");
            }
            // (0.3.7) the companion house's door: open, a way below like an open hatch (over its sill); shut, not
            // (the way below: since the eighth test the great cabin's door, the companionway inside it; whichever door on the
            // main deck unseals a room below)
            var compDoor = def.Fixtures.FirstOrDefault(f => f.Type == "door" && f.Unseals != null && f.Box != null && f.Box[1] >= def.DeckY - 0.3);
            if (compDoor != null)
            {
                var pt = l.Downflood.FirstOrDefault(f => f.OpenFixture == compDoor.Code);
                double doorOpen = ShipFlood.Downflooding(l, Pose(0, 0, free + 1.4), c2 => c2 == compDoor.Code);
                Check(pt.Hatch && Math.Abs(pt.Y - compDoor.Box[1]) < 1e-6 && doorOpen > crest * 1.5,
                    $"the door below ({compDoor.Code}), open, lets the sea on deck below like an open hatch, over its sill {pt.Y - def.DeckY:0.00} up " +
                    $"({doorOpen:0.00} t/s against {crest:0.00} with it shut)");
            }
            foreach (var (label, setSail) in new[] { ("sails furled", 0.0), ("under full sail on a beam reach", 1.0) })
            {
                var calm = SeaRun(def, 8, setSail, false, 180);
                var gale = SeaRun(def, 9, setSail, false, 180);
                Check(calm.Water < 0.1 && gale.Water < 0.5 && gale.MaxList < 2 * Math.PI / 180,
                    $"a sound carrack {label} in a beam sea takes no water over her side in a gale (force 8: {calm.Water:0.00} t in 3 min; force 9: {gale.Water:0.00} t, " +
                    $"heeled {gale.Heel * 180 / Math.PI:0.0}°, listed at most {gale.MaxList * 180 / Math.PI:0.0}° more)");
            }

            // ---- pumps (the user found them too slow at 10 l a stroke): one at the brake holds a moderate leak, two a fresh
            // hole below the waterline, not two holes
            st = new DamageState(l.Count);
            var deep = hull.Where(p => p.Center.Y < def.Waterline - 1.5).OrderBy(p => p.Center.X).ToArray();
            for (int i = 0; i < 4; i++) st.Wear[deep[i * 7].Index] = d.Leaking;
            for (int i = 0; i < 3; i++) st.Hurt[deep[i * 7 + 3].Index] = d.Cracked;
            var still = Pose(0, 0);
            double moderate = ShipFlood.Leaks(l, st, 0, still, 1, out _), one = ShipFlood.PumpRate(d, 1), two = ShipFlood.PumpRate(d, 2);
            var holed = new DamageState(l.Count);
            holed.Hurt[deep[20].Index] = d.Holed;
            double hole = ShipFlood.Leaks(l, holed, 0, still, 1, out double holeSide);
            // (0.3.10, the user: about 200 l/s a man: one at the brake holds a fresh hole, two holes take two)
            Check(moderate < one && hole < one && 2 * hole > one && 2 * hole < two && Math.Sign(holeSide) == deep[20].Part.Side,
                $"the pumps: one at the brake ({1000 * one:0} l/s) holds 4 leaking and 3 cracked panels ({1000 * moderate:0} l/s) and a fresh hole below the waterline " +
                $"({1000 * hole:0} l/s); two holes take two ({1000 * two:0} l/s)");
            Check(ShipFlood.Leaks(l, holed, l.AirVolume(deep[20].Center.Y + 3), still, 1, out _) < hole && ShipFlood.Leaks(l, holed, l.Capacity, still, 1, out _) < 0,
                "water standing in her slows what comes in (and it runs out again when it stands higher than the sea)");

            // ---- repairs: what's held picks the job, the items come from the hand first, then the rest
            st = new DamageState(l.Count);
            int panel = mid.Index;
            st.Hurt[panel] = 0.8f;
            st.Wear[panel] = d.Leaking;
            var needs = ShipRepairs.Needs(l, st, panel);
            var plank = ShipRepairs.For(l, st, panel, "game:plank-oak");
            var caulk = ShipRepairs.For(l, st, panel, "game:flaxfibers");
            Check(needs.Count == 2 && needs[0].What == "planks" && plank?.What == "planks" && caulk?.What == "caulk" && ShipRepairs.For(l, st, panel, "game:stick") == null &&
                  plank.Kits[0].Items.Contains(("game:plank-*", 4)) && plank.Kits[0].Items.Contains(("game:metalnailsandstrips-*", 2)),
                "a holed, leaking panel needs planks and nails (4 and 2) and caulking; planks in hand mean the planking, flax the seams");
            var have = new List<(string Code, int Count)> { ("game:plank-oak", 3), ("game:metalnailsandstrips-iron", 5), ("game:stick", 9), ("game:plank-pine", 8) };
            bool can = ShipRepairs.Plan(plank, have, out int[] take, out _);
            Check(can && take.SequenceEqual(new[] { 3, 2, 0, 1 }), $"mending it takes the planks in hand first, then from the rest ({string.Join(",", take)})");
            Check(!ShipRepairs.Plan(plank, new List<(string, int)> { ("game:plank-oak", 3), ("game:metalnailsandstrips-iron", 5) }, out take, out _) && take.All(t => t == 0),
                "not enough planks: nothing is taken");
            Check(!ShipRepairs.Plan(caulk, have, out _, out _) && ShipRepairs.Plan(caulk, new List<(string, int)> { ("game:flaxfibers", 2), ("game:resin", 1) }, out take, out _) &&
                  take.SequenceEqual(new[] { 2, 1 }), "caulking takes flax fibres and resin");
            ShipRepairs.Apply(st, plank);
            Check(st.Hurt[panel] == 0 && st.Wear[panel] == d.Leaking && ShipRepairs.Needs(l, st, panel).Single().What == "caulk", "mended, the panel's planking is sound and its seams still want caulking");
            ShipRepairs.Apply(st, caulk);
            Check(l.StateOf(DamageKind.Hull, st.Hurt[panel], st.Wear[panel]) == PartState.Sound && ShipRepairs.Needs(l, st, panel).Count == 0, "caulked, it's sound");
            int sail = Array.FindIndex(l.Parts, p => p.Kind == DamageKind.Sail && p.Mast == 0), rig = Array.FindIndex(l.Parts, p => p.Kind == DamageKind.Rigging && p.Mast == 0);
            st.Hurt[sail] = d.Torn;
            st.Hurt[rig] = d.Frayed;
            st.Hurt[mainMast] = d.Sprung;
            var atMast = ShipRepairs.Needs(l, st, mainMast);
            var cloth = ShipRepairs.For(l, st, mainMast, "game:linen-normal-down");
            var rope = ShipRepairs.For(l, st, mainMast, "game:rope");
            Check(atMast.Select(j => j.What).SequenceEqual(new[] { "fish", "rigging", "sail" }) && cloth?.Part == sail && rope?.Part == rig &&
                  ShipRepairs.Plan(cloth, new List<(string, int)> { ("game:linen-normal-down", 4) }, out _, out var kit) && kit.Items[0].Code == ShipRepairs.Cloth &&
                  ShipRepairs.Plan(cloth, new List<(string, int)> { ("game:sail", 1) }, out _, out _),
                "at a sprung mast: its fish, its rigging and its torn sail; linen (4) or a sail patches the sail, rope splices the rigging");
            st.Hurt[mainMast] = d.Broken;
            Check(!ShipRepairs.Needs(l, st, mainMast)[0].AtSea, "a broken mast needs a port");

            // ---- the state saved and synced
            var rnd = new Random(3);
            var a = new DamageState(l.Count);
            for (int i = 0; i < l.Count; i++) { a.Hurt[i] = (float)rnd.NextDouble(); a.Wear[i] = (float)rnd.NextDouble(); }
            var b = new DamageState(l.Count);
            bool unpacked = b.Unpack(a.Pack());
            bool bytes = Enumerable.Range(0, l.Count).All(i => Math.Abs(a.Hurt[i] - b.Hurt[i]) <= 0.5 / 255 + 1e-6 && Math.Abs(a.Wear[i] - b.Wear[i]) <= 0.5 / 255 + 1e-6);
            // saved by part code: read back into a layout whose parts came in another order, unknown ones left out
            var codes = a.Codes(l).Reverse().Append(0x7F000001).ToArray();
            var hurt = a.Hurt.Reverse().Append(0.5f).ToArray();
            var wear = a.Wear.Reverse().Append(0.5f).ToArray();
            var c2s = new DamageState(l.Count);
            c2s.Load(l, codes, hurt, wear);
            Check(unpacked && bytes && c2s.Hurt.SequenceEqual(a.Hurt) && c2s.Wear.SequenceEqual(a.Wear) && !b.Unpack(new byte[3]),
                "the state round-trips: a byte a part to clients, and the exact values saved by part code (in any order; unknown parts left out)");
        }
    }
}
