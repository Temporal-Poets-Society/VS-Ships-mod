using System;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (SH-0036, Ships 0.1.2; the author, 16 November 2026: "Yes, build 0.1.2", "maybe we can set default pointer to
        /// 50 deg") The caravel's lateens have a polar of their own and point to 50° off the true wind. Until 0.1.1 she had
        /// noGo 60 and no polar, so she sailed on <see cref="SailingDef"/>'s default, the carrack's square-rig polar (0 at
        /// 62°): about 75° a side before she made useful way. Printed for the record (her best progress to windward and its
        /// angle, her speed on four points, with and without the server's PointCloser 10) and checked: her own numbers,
        /// better to windward than before, PointCloser still stretching her polar and stopping at MinNoGo.
        /// </summary>
        static void CaravelPolar(string root)
        {
            Console.WriteLine("SH-0036: the caravel's own lateen polar");
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "ships", "config", "ships", "caravel.json")));
            var now = def.Sailing;
            var before = new SailingDef { NoGo = 60 };     // 0.1.1: her noGo, SailingDef's default polar
            float[][] want = { new[] { 0f, 0f }, new[] { 50f, 0f }, new[] { 55f, 0.45f }, new[] { 65f, 0.68f }, new[] { 75f, 0.8f }, new[] { 90f, 0.92f },
                               new[] { 115f, 1f }, new[] { 140f, 0.95f }, new[] { 165f, 0.8f }, new[] { 180f, 0.7f } };
            bool hers = now.NoGo == 50 && now.MaxBrace == 36 && now.MaxSheet == 25 && now.Polar?.Length == want.Length &&
                        now.Polar.Zip(want).All(p => Math.Abs(p.First[0] - p.Second[0]) < 1e-6 && Math.Abs(p.First[1] - p.Second[1]) < 1e-6);
            Check(hers, $"her sailing: noGo {now.NoGo}, maxBrace {now.MaxBrace}, maxSheet {now.MaxSheet}, polar " +
                        string.Join(" ", now.Polar?.Select(p => $"{p[0]}:{p[1]}") ?? Array.Empty<string>()));

            // her best progress to windward (speed made good: polar × cos of the angle off the true wind) and where
            (double Vmg, double At) Best(SailingDef d)
            {
                double best = 0, at = 0;
                for (double a = d.NoGo; a <= 180; a += 0.1)
                {
                    double v = Sailing.Polar(a, d) * Math.Cos(a * Math.PI / 180);
                    if (v > best) { best = v; at = a; }
                }
                return (best, at);
            }
            string Row(string name, SailingDef d)
            {
                var (v, at) = Best(d);
                return $"{name,-28} noGo {d.NoGo,5:0.0}  best to windward {v:0.000} of top speed at {at:0.0}°;  at 55° {Sailing.Polar(55, d):0.00}, 65° {Sailing.Polar(65, d):0.00}, " +
                       $"75° {Sailing.Polar(75, d):0.00}, 90° {Sailing.Polar(90, d):0.00}";
            }
            var beforeTuned = Sailing.Tuned(before, 10);
            var nowTuned = Sailing.Tuned(now, 10);
            foreach (var (name, d) in new[] { ("0.1.1 (carrack's polar)", before), ("0.1.2 (her own)", now),
                                              ("0.1.1, PointCloser 10", beforeTuned), ("0.1.2, PointCloser 10", nowTuned) })
                Console.WriteLine("         " + Row(name, d));
            var (vb, ab) = Best(before);
            var (vn, an) = Best(now);
            var (vt, at10) = Best(nowTuned);
            Check(vn > vb && an < ab, $"to windward she makes {vn:0.000} of her top speed at {an:0.0}° off the true wind (was {vb:0.000} at {ab:0.0}°)");
            // PointCloser on her polar: 10 closer gives noGo 40 and keeps her shape (the speed at her old no-go now at 40,
            // the same from 120° aft); more stops at MinNoGo 35
            var far = Sailing.Tuned(now, 30);
            Check(Math.Abs(nowTuned.NoGo - 40) < 1e-4 && Math.Abs(Sailing.Polar(120, nowTuned) - Sailing.Polar(120, now)) < 1e-6 &&
                  Math.Abs(Sailing.Polar(40, nowTuned) - Sailing.Polar(50, now)) < 1e-6 && vt >= vn &&
                  Math.Abs(far.NoGo - Sailing.MinNoGo) < 1e-4,
                $"PointCloser 10 points her to {nowTuned.NoGo:0}° ({vt:0.000} to windward at {at10:0.0}°), the polar the same from 120° aft; 30 stops at {far.NoGo:0}° (MinNoGo)");
            // what the lower no-go moves: her sails draw down to a 30° apparent wind (luff from 15°), close-hauled from 50° to 80°
            Check(Sailing.Drawing(30, now) > 0.999 && Sailing.Drawing(15, now) < 1e-6 && Sailing.Point(49.9, now) == PointOfSail.InIrons &&
                  Sailing.Point(50, now) == PointOfSail.CloseHauled && Sailing.Point(79.9, now) == PointOfSail.CloseHauled,
                "her sails draw fully from a 30° apparent wind (luffing below 15°); in irons under 50° true, close-hauled from 50° to 80°");
        }
    }
}
