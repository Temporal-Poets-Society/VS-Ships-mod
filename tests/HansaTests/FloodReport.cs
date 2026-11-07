using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        static readonly double[] BeaufortMs = { 0.3, 1.0, 2.5, 4.4, 6.7, 9.4, 12.3, 15.5, 19.0, 22.6, 26.5, 30.6, 37.5 };

        /// <summary>
        /// A sound ship at sea in a wind of this Beaufort force for so many seconds, as the server's flood tick works it out
        /// (the waves' pitch and roll, the heel from her sails, her list, the sea over her side and what of it gets below):
        /// the water she took, her greatest list (radians) and sinkage, and where it came in (tonnes each).
        /// </summary>
        static (double Water, double MaxList, double MaxSink, double Heel, Dictionary<string, double> By) SeaRun(ShipDefinition def, int force, double sail, bool gatesOpen, double seconds)
        {
            var l = def.DamageParts;
            var d = l.D;
            var md = def.Motion;
            var sd = def.Sailing;
            def.Waterplane(out double hl, out double hb, out double cx);
            var spec = new Sea.WaveSpectrum(4242, 0);
            string Kind(DownfloodPoint f) => f.OpenFixture != null ? "open " + f.OpenFixture : f.Weight < 1 ? "deck edge" : $"side {f.Y - def.DeckY:0.00} up";
            double s = BeaufortMs[force] / Sea.Beaufort.MetersPerSecondPerUnit;
            double hs = 0.1 + 2.4 * s * s, T = 2.5 + 3.2 * Math.Sqrt(hs);
            var sea = new Sea.SeaState(hs, 0, 1, T);
            var wind = Sea.Wind.FromCompass(0, s);
            double yaw = Math.PI / 2;                       // a beam sea, a beam reach
            double v = sail > 0 ? def.MaxSpeed * Sailing.SpeedFactor(90, s, sail, sd) : 0;
            double heel = ShipMotion.Heel(wind.X, wind.Z, yaw, v, sail, md, sd);
            var place = new Sea.WavePlace();
            double water = 0, list = 0, sink = 0, maxList = 0, maxSink = 0, leakSide = 0;
            int listSide = 1;
            var by = new Dictionary<string, double>();
            double x0 = 1000, z0 = 2000, step = 0.25;
            for (double t = 600; t < 600 + seconds; t += step)
            {
                double x = x0 + Math.Sin(yaw) * v * (t - 600), z = z0 + Math.Cos(yaw) * v * (t - 600);
                place.Update(spec, t, sea, wind);
                // the server's attitude: as EntityShip.WaveMotion and TruePose
                var a = ShipMotion.Waves(place.ToTrains(), 1, t, x, z, yaw, v, hl, hb, cx, md, ShipMotion.SailSteadiness(sail, s));
                double t0 = t;
                var pose = new FloodPose
                {
                    X = x, Y = -sink + a.Heave, Z = z, Yaw = yaw, Pitch = a.Pitch, Roll = a.Roll + heel + list,
                    Surface = (wx, wz) => place.HeightAt(wx, wz, t0)
                };
                // (as ShipFlood.Downflooding, place by place; no hatch open)
                foreach (var p in l.Downflood)
                {
                    if (p.OpenFixture != null && !(gatesOpen && !p.Hatch)) continue;
                    pose.ToWorld(p.X, p.Y, p.Z, out double wx, out double wy, out double wz);
                    double q = ShipFlood.Pour(d, p.Length, p.Weight, pose.Surface(wx, wz) - wy) * step * (p.Hatch ? 1 : d.DeckShare);
                    if (q <= 0) continue;
                    string k = Kind(p);
                    by[k] = by.GetValueOrDefault(k) + q;
                    water += q;
                    leakSide += q * Math.Sign(p.Z);
                }
                water = Math.Min(water, l.Capacity);
                double want = l.SinkageFor(water);
                sink += Math.Clamp(want - sink, -0.3 * step, 0.3 * step);
                if (Math.Abs(heel) > 1 * Math.PI / 180) listSide = Math.Sign(heel);
                else if (Math.Abs(leakSide) > 1e-4) listSide = Math.Sign(leakSide);
                double lw = ShipFlood.List(l, water, heel, listSide);
                list += Math.Clamp(lw - list, -2 * Math.PI / 180 * step, 2 * Math.PI / 180 * step);
                maxList = Math.Max(maxList, Math.Abs(list));
                maxSink = Math.Max(maxSink, sink);
            }
            return (water, maxList, maxSink, heel, by);
        }

        /// <summary>
        /// A sound carrack at sea in each wind for ten minutes (<see cref="SeaRun"/>): how much water she takes over her side
        /// and where it comes in (not checks): dotnet run -c Release -- sailflood
        /// </summary>
        static void SailFloodReport(ShipDefinition def)
        {
            var l = def.DamageParts;
            Console.WriteLine($"{def.Code}: {l.Downflood.Length} places the sea comes aboard; the side's lowest edge over the deck (blocks of it): " +
                string.Join(", ", l.Downflood.Where(f => f.OpenFixture == null && f.Weight >= 1).GroupBy(f => Math.Round(f.Y - def.DeckY, 2)).OrderBy(gr => gr.Key)
                    .Select(gr => $"{gr.Key:0.00} up ({gr.Sum(f => f.Length):0.00})")));
            foreach (var (label, sail, gatesOpen) in new[] { ("sails furled, beam sea", 0.0, false), ("full sail, beam reach", 1.0, false), ("full sail, beam reach, gates open", 1.0, true) })
            {
                Console.WriteLine($"-- {label}");
                Console.WriteLine(" F | heel | water after 10 min (t)  max list  max sinkage | where it came in (t)");
                for (int f = 1; f <= 12; f++)
                {
                    var r = SeaRun(def, f, sail, gatesOpen, 600);
                    Console.WriteLine($"{f,2} | {r.Heel * 180 / Math.PI,4:0.0}° | {r.Water,8:0.0} {r.MaxList * 180 / Math.PI,16:0.0}° {r.MaxSink,10:0.00} | " +
                        string.Join(", ", r.By.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0.0}")));
                }
            }
        }
    }
}
