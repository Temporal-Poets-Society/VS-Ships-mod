using System;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// Pointing (0.3.13). The user: "sailing up and down the coast of Portugal, can't get much west" with the wind
        /// from the W or NW. Pins the config's PointCloser (Sailing.Tuned: her no-go angle closer, the polar stretched to
        /// match, the design as it is at 0) and that close-hauled with way on isn't "taken aback" (it went by the apparent
        /// wind, which comes from further forward than the true wind she's judged by).
        /// </summary>
        static void Pointing(ShipDefinition def)
        {
            Console.WriteLine("pointing: PointCloser and taken aback");
            var sd = def.Sailing;
            const double wind = 0.3;

            // best made good to windward (fraction of top speed) and the true wind angle it's made at
            double Vmg(SailingDef d, out double at)
            {
                double best = 0; at = 0;
                for (double a = 30; a <= 120; a += 0.5)
                {
                    double v = Sailing.SpeedFactor(a, wind, 1, d) * Math.Cos(a * Math.PI / 180);
                    if (v > best) { best = v; at = a; }
                }
                return best;
            }

            var same = Sailing.Tuned(sd, 0);
            bool unchanged = same.NoGo == sd.NoGo && same.Polar.Length == sd.Polar.Length && !ReferenceEquals(same.Polar, sd.Polar);
            for (int i = 0; unchanged && i < sd.Polar.Length; i++) unchanged = same.Polar[i][0] == sd.Polar[i][0] && same.Polar[i][1] == sd.Polar[i][1];
            Check(unchanged, $"PointCloser 0 sails her as designed (no-go {sd.NoGo:0}°), on a copy of her polar");

            var t7 = Sailing.Tuned(sd, 7);
            bool stretched = Math.Abs(t7.NoGo - (sd.NoGo - 7)) < 1e-4
                && Math.Abs(Sailing.SpeedFactor(t7.NoGo + 1e-3, wind, 1, t7) - Sailing.SpeedFactor(sd.NoGo + 1e-3, wind, 1, sd)) < 1e-3
                && Math.Abs(Sailing.SpeedFactor(130, wind, 1, t7) - Sailing.SpeedFactor(130, wind, 1, sd)) < 1e-9
                && Sailing.SpeedFactor(t7.NoGo - 1, wind, 1, t7) <= 0;
            Check(stretched, $"PointCloser 7: no-go {t7.NoGo:0}°, her speed at it what she had at {sd.NoGo:0}°, the same from 120° aft, in irons closer");

            var report = "";
            double prev = -1;
            bool rising = true;
            foreach (double closer in new[] { -5.0, 0, 3, 5, 7, 10, 15, 40 })
            {
                var t = Sailing.Tuned(sd, closer);
                double v = Vmg(t, out double at);
                rising &= v > prev;
                prev = v;
                report += $" {closer:+0;-0;0}: no-go {t.NoGo:0}°, best {v:0.000} at {at:0}°;";
            }
            Check(rising && Sailing.Tuned(sd, 40).NoGo >= Sailing.MinNoGo - 1e-4,
                $"the closer she points the more she makes to windward, never closer than {Sailing.MinNoGo:0}° (made good to windward, fraction of top speed, a {wind} breeze):" + report);

            // close-hauled at 70° true with the way she makes there: the apparent wind comes from further forward, inside
            // her no-go angle, but she isn't aback; head to wind she is
            double windZ = wind;                           // the air going south: the wind from the north
            double yaw = (180 - 70) * Math.PI / 180;       // the bow's yaw 110°: the true wind 70° off her bow
            double speed = Sailing.TargetSpeed(0, windZ, yaw, 1, def.MaxSpeed, sd);
            Sailing.Apparent(0, windZ, yaw, speed, out double ax, out double az);
            double apparent = Sailing.WindAngle(ax, az, yaw, out _);
            bool head = Sailing.Aback(0, windZ, 170 * Math.PI / 180, sd);
            Check(apparent < sd.NoGo && !Sailing.Aback(0, windZ, yaw, sd) && head,
                $"close-hauled at 70° true making {speed:0.0} b/s the apparent wind is {apparent:0}° off the bow, yet she isn't aback; 10° off the wind she is");
        }
    }
}
