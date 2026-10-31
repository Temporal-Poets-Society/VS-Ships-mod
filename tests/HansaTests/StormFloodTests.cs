using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        static void StormFloodTests(string root)
        {
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets/ships/config/ships/caravel.json")));
            def.Init();
            var l = def.DamageParts;
            Console.WriteLine($"ISSUE30 displacement {l.Displacement:F2}, reserve {l.Reserve:F2}, capacity {l.Capacity:F2}, hold bottom {l.HoldBottom:F3}");
            foreach (var group in l.Downflood.GroupBy(f => f.OpenFixture != null ? f.OpenFixture.Split(':')[0] : f.Weight < 1 ? "scupper" : "rail"))
                Console.WriteLine($"  {group.Key}: {group.Count()} points, length {group.Sum(f => f.Length):F2}, height {group.Min(f => f.Y):F3}..{group.Max(f => f.Y):F3}");
            var set = new ShipPieceSet();
            foreach (var e in def.ModularPieces.Where(e => e.Kind == "stair"))
            {
                var m = def.Mounts.ForModular(e);
                var plan = PiecePlanner.Plan(def.Mounts, m, ModularFake(e, e.Kind), ModularToward(e, e.Kind, m));
                set.Add(ShipPiece.From(plan, set.NextId, null));
            }
            var he = def.ModularPieces.Single(e => e.Kind == "hatch");
            var cells = l.Def.Slots.Rect(he.At[1], he.At[0] - he.Size[0] / 2.0, he.At[2] - he.Size[1] / 2.0, he.At[0] + he.Size[0] / 2.0, he.At[2] + he.Size[1] / 2.0);
            var hp = HatchPlanner.Plan(def.Slots, cells);
            Console.WriteLine($"  default hatch blocker: {set.Blocking(hp)?.Role}, cells {string.Join(',', cells)}");
            var blocker = set.Blocking(hp);
            Check(blocker == null, "the default closed hatch fits over the hold stair");
            if (blocker != null)
                foreach (var v in blocker.PlacementVolumes)
                    foreach (var h in hp.PlacementVolumes)
                        if (PieceShape.Overlap(v, h)) Console.WriteLine("    overlap stair " + string.Join(',', v.Bounds) + " hatch " + string.Join(',', h.Bounds));
            Console.WriteLine($"  weather deck area {l.DeckArea:F2} m2; bilge below hold floor {l.AirVolume(-1.875):F2} t");
            Check(def.Fixtures.Where(f => f.Type is "gun" or "gate").All(f => !f.StartOpen), "gun lids and entry gates start shut");
            Check(def.Fixtures.Where(f => f.Type == "gun" && f.Lid != null).All(f => l.Downflood.Any(p => p.OpenFixture == f.Code)), "every lidded gun has its explicit flood opening");
            foreach (double heading in new[] { 0.0, 45.0, 90.0 })
            foreach (bool missingHatch in new[] { false, true })
            {
                var spec = new Sea.WaveSpectrum(4242, 0);
                var sea = new Sea.SeaState(5.5, -1, 0, Math.Sqrt(2 * Math.PI * 165 / 9.81));
                var wind = Sea.Wind.FromCompass(90, 1.5);
                var waves = new Sea.WavePlace();
                def.Waterplane(out double hl, out double hb, out double cx);
                double yaw = heading * Math.PI / 180, water = 0, sink = 0, list = 0, peak = 0, rate = 0, maxRate = 0, firstNotice = -1, atFour = 0;
                var by = new Dictionary<string, double>();
                bool IsOpen(string code) => missingHatch && code.StartsWith("cell:") && cells.Contains(int.Parse(code.AsSpan(5)));
                for (double t = 600; t < 1200; t += .25)
                {
                    waves.Update(spec, t, sea, wind);
                    var a = ShipMotion.Waves(waves, 1, t, 1000, 2000, yaw, 0, hl, hb, cx, def.Motion);
                    var pose = new FloodPose { X = 1000, Z = 2000, Y = a.Heave - sink, Yaw = yaw, Pitch = a.Pitch, Roll = a.Roll + list, Surface = waves.HeightAt };
                    double q = ShipFlood.Downflooding(l, pose, IsOpen);
                    rate += (q - rate) * .125;
                    maxRate = Math.Max(maxRate, rate);
                    if (firstNotice < 0 && rate > l.D.FounderRate) firstNotice = t - 600;
                    foreach (var f in l.Downflood.Where(f => f.OpenFixture == null || IsOpen(f.OpenFixture)))
                    {
                        pose.ToWorld(f.X, f.Y, f.Z, out double x, out double y, out double z);
                        double depth = pose.Surface(x, z) - y;
                        peak = Math.Max(peak, depth);
                        string k = f.Hatch ? "open cells" : f.Weight < 1 ? "scuppers" : "rail";
                        double share = f.Hatch ? 1 : l.D.DeckShare + (missingHatch ? l.D.OpenHatchShare * cells.Count / 6.0 : 0);
                        by[k] = by.GetValueOrDefault(k) + ShipFlood.Pour(l.D, f.Length, f.Weight, depth) * share * .25;
                    }
                    water = Math.Min(l.Capacity, water + q * .25);
                    if (t < 840) atFour = water;
                    sink += Math.Clamp(l.SinkageFor(water) - sink, -.075, .075);
                    list += Math.Clamp(ShipFlood.List(l, water, 0, 1) - list, -Math.PI / 360, Math.PI / 360);
                }
                Console.WriteLine($"  baseline heading {heading}, hatch {(missingHatch ? "MISSING" : "shut")}: 4 min {atFour:F2}t, 10 min {water:F2}t, max rate {maxRate:F3}t/s, first warning {firstNotice:F2}s, peak sea over point {peak:F2}, " + string.Join(", ", by.Select(k => $"{k.Key} {k.Value:F2}t")));
            }
            foreach (double heading in new[] { 0.0, 45.0, 90.0 })
                foreach (int crew in new[] { 0, 2 })
                {
                    var r = StormRun(def, heading, 1800, _ => false, crew);
                    Console.WriteLine($"  fixed shut {heading} deg, {crew} pumpers: hold {r.Water:F3}, deck {r.Deck:F3}, aboard {r.Aboard:F2}, drained {r.Drained:F2}, below {r.Below:F2}, pumped {r.Pumped:F2}");
                    Check(r.Water < (crew > 0 ? .5 : 5) && r.Water + r.Deck < .1 * l.Reserve, "closed ship stays low after 30 minutes in force 12");
                    Check(Math.Abs(r.Aboard - r.Drained - r.Pumped - r.Water - r.Deck) < 1e-6, "deck, hold, scuppers and pumps conserve water");
                }
            var open = StormRun(def, 0, 600, c => c.StartsWith("cell:") && cells.Contains(int.Parse(c.AsSpan(5))) || def.Fixture(c)?.Type is "gun" or "gate", 0);
            Console.WriteLine($"  open ship 10 min: hold {open.Water:F2}, deck {open.Deck:F2}, aboard {open.Aboard:F2}, drained {open.Drained:F2}");
            Check(open.Water > 10, "open ports, gates and hatch admit substantial water in the same sea");
            var holed = StormRun(def, 0, 120, _ => false, 2, true);
            Console.WriteLine($"  holed ship 2 min, pumps manned: hold {holed.Water:F2}");
            Check(holed.Water > 5, "a submerged hull hole still floods with pumps manned");
            var pumpedDown = StormRun(def, 0, 120, _ => false, 2, initialWater: 5);
            Check(pumpedDown.Water < .5 && pumpedDown.Pumped > 4.5, $"two pumpers clear a 5 t bilge in the storm ({pumpedDown.Water:F3} t left, {pumpedDown.Pumped:F2} t pumped)");
            Check(l.DeckArea > 50 && l.DeckArea < 150, "weather deck footprint is measured below the dry-mask cutoff");
            var calm = new FloodPose { Surface = (_, _) => 0 };
            double pond = 5;
            var draining = DeckFlooding.Step(l, calm, _ => false, 10, ref pond);
            Console.WriteLine($"  5 t on deck in calm: scuppers {draining.Drained:F3} t/s, seepage {draining.Below:F4} t/s; {pond:F3} t left after 10s");
            Check(draining.Drained > .1 && draining.Below < .01 && pond < 4 && pond >= 0, "scuppers drain standing deck water while closed hatches keep the bilge nearly dry");
            double wet = 0, dry = 0;
            var atPort = new FloodPose { Surface = (_, _) => 1.8 };
            var shutPort = DeckFlooding.Step(l, atPort, _ => false, .25, ref dry);
            var openPort = DeckFlooding.Step(l, atPort, c => c.StartsWith("gun-"), .25, ref wet);
            Check(openPort.Aboard > shutPort.Aboard + .1, "actual gun-port sills admit water when opened, with the sea below the rail");
            float scupperArea = l.D.ScupperAreaPerLength;
            try
            {
                l.D.ScupperAreaPerLength = 0;
                double emptyDeck = 0;
                var shelteredHatch = DeckFlooding.Step(l, atPort, c => c.StartsWith("cell:"), .25, ref emptyDeck);
                Check(shelteredHatch.Aboard == 0 && shelteredHatch.Below == 0, "a crest above an open hatch but below intact bulwarks cannot teleport into the hold");
            }
            finally { l.D.ScupperAreaPerLength = scupperArea; }
            var aboveStair = new ShipPieceSet();
            aboveStair.Add(new ShipPiece { Role = PieceRole.Stair, Box = hp.Box, Sel = new List<double[]> { new[] { -6.0, 1.0, -.25, -5.0, 2.0, .25 } } });
            Check(aboveStair.Blocking(hp) != null, "a stair projecting above the deck still blocks a hatch");
            foreach (double volume in new[] { 1.0, 5.0, 20.0 })
            {
                double level = l.FloodLevel(volume);
                var surface = ShipWater.Surface(def, level, 0, 0, ShipWater.DefaultInset);
                Check(Math.Abs(l.AirVolume(level) - volume) < 1e-6 && surface.Count > 0 && surface.SelectMany(r => r).All(row => Enumerable.Range(0, row.Length / 3).All(i =>
                    Math.Abs(row[3 * i + 1] - level) < 1e-9 && Math.Abs(row[3 * i + 2]) <= ShipWater.InnerHalfWidth(def, row[3 * i], level, ShipWater.DefaultInset) + 1e-6)),
                    $"{volume} t: drawn flood plane and stored volume agree inside the bilge at y {level:F3}");
            }
        }

        static (double Water, double Deck, double Aboard, double Drained, double Below, double Pumped) StormRun(
            ShipDefinition def, double heading, double seconds, Func<string, bool> open, int pumpers, bool holed = false, double initialWater = 0)
        {
            var l = def.DamageParts;
            var st = new DamageState(l.Count);
            if (holed) st.Hurt[l.Parts.First(p => p.Kind == DamageKind.Hull && !p.Inner && p.Center.Y < -1 && Math.Abs(p.Center.X) < 3).Index] = 1;
            var waves = new Sea.WavePlace();
            var spec = new Sea.WaveSpectrum(4242, 0);
            var sea = new Sea.SeaState(5.5, -1, 0, Math.Sqrt(2 * Math.PI * 165 / 9.81));
            var wind = Sea.Wind.FromCompass(90, 1.5);
            def.Waterplane(out double hl, out double hb, out double cx);
            double water = initialWater, deck = 0, sink = 0, list = 0, aboard = 0, drained = 0, below = 0, pumped = 0;
            bool dryPump = true;
            double yaw = heading * Math.PI / 180;
            for (double t = 600; t < 600 + seconds; t += .25)
            {
                waves.Update(spec, t, sea, wind);
                var motion = ShipFlood.Motion(l, def.Motion, water);
                var a = ShipMotion.Waves(waves, 1, t, 1000, 2000, yaw, 0, hl, hb, cx, motion);
                var pose = new FloodPose { X = 1000, Z = 2000, Y = a.Heave - sink, Yaw = yaw, Pitch = a.Pitch, Roll = a.Roll + list, Surface = waves.HeightAt };
                var f = DeckFlooding.Step(l, pose, open, .25, ref deck);
                double leak = holed ? ShipFlood.Leaks(l, st, water, pose, 1, out _) : 0;
                if (water <= l.D.PumpResidual) dryPump = true;
                else if (water >= 3 * l.D.PumpResidual) dryPump = false;
                double pump = dryPump ? 0 : Math.Min(ShipFlood.PumpRate(l.D, pumpers), Math.Max(0, water / .25 + leak + f.Below));
                water = Math.Clamp(water + (leak + f.Below - pump) * .25, 0, l.Capacity);
                aboard += f.Aboard * .25; drained += f.Drained * .25; below += f.Below * .25; pumped += pump * .25;
                sink += Math.Clamp(l.SinkageFor(water + deck) - sink, -.075, .075);
                list += Math.Clamp(ShipFlood.List(l, water, 0, 1) - list, -Math.PI / 360, Math.PI / 360);
            }
            return (water, deck, aboard, drained, below, pumped);
        }
    }
}
