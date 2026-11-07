using System;
using System.Collections.Generic;
using System.IO;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (7 November 2026, Sea 0.6.0) The coast and the open sea contracts' other members as Ships reads them: the
        /// beach's surf under her (a plane fitted over her waterplane, nothing off the coast), her drift in a longshore
        /// current, a surface's pose when it gives no trains, and the weather's wind and sea state.
        /// dotnet run -c Release -- sea-coast
        /// </summary>
        static void SeaCoastTests(string root)
        {
            Console.WriteLine("sea coast: surf under her, the current, the pose, the contract's wind");
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets/ships/config/ships/caravel.json")));
            def.Init();
            def.Waterplane(out double hl, out double hb, out _);
            double cx = new ShipWaveHull(def).FlotationCentre;
            var md = def.Motion;
            static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
            static bool SameAttitude(ShipAttitude a, ShipAttitude b) => Same(a.Heave, b.Heave) && Same(a.Pitch, b.Pitch) && Same(a.Roll, b.Roll);
            static bool Near(double a, double b, double tol = 1e-9) => Math.Abs(a - b) <= tol;

            // a fake side: three trains, a shelter, a coast whose depth the test sets, and a drawn surface that is the
            // sheltered trains plus a known plane (the surf as far as a ship's waterplane can feel it)
            double clock = 300, depth = 40, shelter = 0.7;
            double h0 = 0.35, gx = 0.04, gz = -0.025;
            var kx = new[] { 0.09, -0.03, 0.05 }; var kz = new[] { 0.02, 0.11, -0.07 };
            var om = new[] { 0.95, 1.05, 0.8 }; var ph = new[] { 0.3, 2.1, 4.4 }; var am = new[] { 0.6, 0.35, 0.2 };
            static double Plain(double x, double z, double t, double[] kx, double[] kz, double[] om, double[] ph, double[] am)
            {
                double y = 0;
                for (int i = 0; i < kx.Length; i++) y += am[i] * Math.Sin(kx[i] * x + kz[i] * z - om[i] * t + ph[i]);
                return y;
            }
            double Plane(double x, double z) => h0 + gx * x + gz * z;
            var surface = new Dictionary<string, object>
            {
                ["owner"] = "test", ["version"] = 1, ["priority"] = 50, ["revision"] = new Func<long>(() => 0),
                ["seconds"] = new Func<double>(() => clock),
                ["trains"] = new Func<double, double, double, double[], double[], double[], double[], double[], int>((x, z, t, a, b, c, d, e) =>
                {
                    for (int i = 0; i < kx.Length; i++) { a[i] = kx[i]; b[i] = kz[i]; c[i] = om[i]; d[i] = ph[i]; e[i] = am[i]; }
                    return kx.Length;
                }),
                ["height"] = new Func<double, double, double>((x, z) => shelter * Plain(x, z, clock, kx, kz, om, ph, am) + (depth < 32 ? Plane(x, z) : 0)),
            };
            var seastate = new Dictionary<string, object>
            {
                ["owner"] = "test",
                ["shelter"] = new Func<double, double, double>((x, z) => shelter),
                ["surf"] = new Func<double, double, (double Depth, double Breaking, double Foam)>((x, z) => (depth, 0.2, 0.1)),
            };
            var cache = new Dictionary<string, object> { [ContractWaves.SurfaceKey] = surface, [ContractWaves.SeaStateKey] = seastate };
            var warnings = new List<string>();
            var contract = new ContractWaves(cache, warnings.Add);
            var waves = new WaveTrains();

            // ---- 1. surf: nothing off the coast, the plane over coastal water
            {
                int offDiff = 0, offSurf = 0, n = 0;
                double worst = 0;
                foreach (double yaw in new[] { 0.0, 0.6, Math.PI / 2, 2.5, -1.9 })
                foreach (double speed in new[] { 0.0, 2.5 })
                for (clock = 300; clock < 320; clock += 0.7)
                {
                    double x = 1200 + 3 * Math.Sin(clock), z = -800 + 2 * Math.Cos(clock);
                    depth = 40;
                    var off = ShipMotion.Ride(contract, waves, x, z, yaw, speed, hl, hb, cx, md, 0.3, out var offSurfA);
                    if (waves.Surf != null || offSurfA.Heave != 0 || offSurfA.Pitch != 0 || offSurfA.Roll != 0) offSurf++;
                    var plain = ShipMotion.Waves(waves, shelter, waves.Time, x, z, yaw, speed, hl, hb, cx, md, 0.3);
                    if (!SameAttitude(off, plain) || !SameAttitude(ShipMotion.AddSurf(off, offSurfA), plain)) offDiff++;

                    depth = 6;
                    var on = ShipMotion.Ride(contract, waves, x, z, yaw, speed, hl, hb, cx, md, 0.3, out var surf);
                    // the trains' motion is the same over the coast; the surf comes beside it
                    if (!SameAttitude(on, plain)) offDiff++;
                    double s = Math.Sin(yaw), c = Math.Cos(yaw);
                    double wx = x + s * cx, wz = z + c * cx;
                    double p = ShipMotion.SoftCap(Math.Atan(gx * s + gz * c), md.MaxPitch * Math.PI / 180);
                    double r = ShipMotion.SoftCap(-Math.Atan(-gx * c + gz * s), md.MaxRoll * Math.PI / 180);
                    var want = new ShipAttitude(Plane(wx, wz) - Math.Sin(p) * cx, p, r);
                    worst = Math.Max(worst, Math.Max(Math.Abs(surf.Heave - want.Heave), Math.Max(Math.Abs(surf.Pitch - want.Pitch), Math.Abs(surf.Roll - want.Roll))));
                    n++;
                }
                Check(offSurf == 0, $"off the coast (depth 40) there is no surf, not even worked out ({offSurf} of {n} had some)");
                Check(offDiff == 0, $"… and her motion is bit for bit the trains' alone, as before ({offDiff} differ)");
                Check(worst < 1e-9, $"over coastal water (depth 6) a planar surf of {h0} + {gx}·x + {gz}·z gives its mean as heave, its rise toward the bow as pitch and toward starboard as roll (starboard up), within {worst:0.0e0}");
            }
            // which way: the water rising to starboard rolls her to port (roll < 0), rising ahead pitches her bow up
            {
                clock = 300; depth = 6;
                double save = gx; gx = 0.05; double saveZ = gz; gz = 0;
                ShipMotion.Ride(contract, waves, 0, 0, Math.PI / 2, 0, hl, hb, cx, md, 0, out var east);   // bow east: rising ahead
                ShipMotion.Ride(contract, waves, 0, 0, Math.PI, 0, hl, hb, cx, md, 0, out var north);     // bow north (yaw π: (0, −1)): east is starboard
                Check(east.Pitch > 0 && Math.Abs(east.Roll) < 1e-12, $"bow toward the rising water: bow up ({east.Pitch * 180 / Math.PI:0.00}°), no roll");
                Check(north.Roll < 0 && Math.Abs(north.Pitch) < 1e-12, $"rising to starboard: starboard lifted, roll {north.Roll * 180 / Math.PI:0.00}° (starboard down +)");
                gx = save; gz = saveZ;
            }
            // the surf's height from the drawn surface carries the shelter: the trains under it are taken at it
            {
                depth = 6; clock = 305; shelter = 0.3;
                ShipMotion.Ride(contract, waves, 50, 60, 0.4, 0, hl, hb, cx, md, 0, out var lee);
                double s = Math.Sin(0.4), c = Math.Cos(0.4);
                Check(waves.Surf != null && Near(waves.Surf(50, 60), Plane(50, 60), 1e-12), "the surf is the drawn height less the trains under the shelter here (0.3), the plane alone");
                shelter = 0.7;
            }
            // a surface with no height, or a sea state with no surf: the open sea's waves alone
            {
                depth = 6;
                seastate.Remove("surf");
                ShipMotion.Ride(contract, waves, 0, 0, 0, 0, hl, hb, cx, md, 0, out var none1);
                seastate["surf"] = new Func<double, double, (double Depth, double Breaking, double Foam)>((x, z) => (depth, 0.2, 0.1));
                var height = surface["height"]; surface.Remove("height");
                ShipMotion.Ride(contract, waves, 0, 0, 0, 0, hl, hb, cx, md, 0, out var none2);
                surface["height"] = height;
                Check(none1.Heave == 0 && none2.Heave == 0 && waves.Surf == null, "no seastate.surf, or no surface.height: no surf");
                // a height with no answer (NaN) at one point: no surf for that sample
                var drawn = (Func<double, double, double>)height;
                surface["height"] = new Func<double, double, double>((x, z) => x > 0 ? double.NaN : drawn(x, z));
                ShipMotion.Ride(contract, waves, 0, 0, 0, 0, hl, hb, cx, md, 0, out var nan);
                surface["height"] = height;
                Check(nan.Heave == 0 && nan.Pitch == 0 && nan.Roll == 0, "a height with no answer (NaN): no surf for that sample");
            }
            // eased: it follows the surf in a few tenths of a second, and off the coast settles to exactly nothing
            {
                var target = new ShipAttitude(0.4, 0.05, -0.03);
                var e = default(ShipAttitude);
                for (int i = 0; i < 60; i++) e = ShipMotion.EaseSurf(e, target, 0.05);
                bool follows = Near(e.Heave, 0.4, 1e-3) && Near(e.Pitch, 0.05, 1e-3) && Near(e.Roll, -0.03, 1e-3);
                var half = ShipMotion.EaseSurf(default, target, ShipMotion.SurfResponse);
                bool lag = Near(half.Heave, 0.4 * (1 - Math.Exp(-1)), 1e-12);
                int steps = 0;
                while ((e.Heave != 0 || e.Pitch != 0 || e.Roll != 0) && steps < 1000) { e = ShipMotion.EaseSurf(e, default, 0.05); steps++; }
                Check(follows && lag, $"eased with a {ShipMotion.SurfResponse} s lag toward the surf");
                Check(steps < 1000 && e.Heave == 0, $"… and back to exactly zero off the coast ({steps} ticks of 0.05 s)");
                var w = new ShipAttitude(0.123456789, -0.0314, 0.0271);
                Check(SameAttitude(ShipMotion.AddSurf(w, default), w), "no surf: the waves' motion untouched, bit for bit");
            }

            // ---- 2. the current: she goes with the water
            {
                int diff = 0;
                double worst = 0;
                foreach (double yaw in new[] { 0.0, 1.1, -2.7 })
                foreach (double speed in new[] { 0.0, 3.5, -0.4 })
                foreach (double leeway in new[] { 0.0, 0.3 })
                foreach (double rate in new[] { 0.0, 0.08 })
                {
                    double dt = 0.05;
                    HelmMath.Advance(100, 200, yaw, yaw + rate * dt, speed, leeway, dt, 1.75, out double ax, out double az);
                    HelmMath.Advance(100, 200, yaw, yaw + rate * dt, speed, leeway, dt, 1.75, 0, 0, out double bx, out double bz);
                    if (!Same(ax, bx) || !Same(az, bz)) diff++;
                    HelmMath.Advance(100, 200, yaw, yaw + rate * dt, speed, leeway, dt, 1.75, 0.6, -0.25, out double cx2, out double cz2);
                    worst = Math.Max(worst, Math.Max(Math.Abs(cx2 - ax - 0.6 * dt), Math.Abs(cz2 - az + 0.25 * dt)));
                }
                Check(diff == 0, $"no current: her advance is bit for bit as before ({diff} differ)");
                Check(worst < 1e-12, $"a current of 0.6 east, 0.25 north (m/s) carries her 0.03 east and 0.0125 north in a 0.05 s tick, on top of her way, leeway and turn (within {worst:0.0e0})");
                // drifting with no way on: ten seconds in the current
                double x = 0, z = 0;
                for (int i = 0; i < 200; i++) HelmMath.Advance(x, z, 0.3, 0.3, 0, 0, 0.05, 1.75, 0.6, -0.25, out x, out z);
                Check(Near(x, 6, 1e-9) && Near(z, -2.5, 1e-9), $"lying still in it she drifts with it: 10 s, ({x:0.000}, {z:0.000})");

                Check(contract.Current(0, 0) == (0, 0), "no seastate.current: still water");
                seastate["current"] = new Func<double, double, (double X, double Z)>((px, pz) => px > 0 ? (0.4, -0.1) : (double.NaN, 0));
                Check(contract.Current(5, 0) == (0.4, -0.1) && contract.Current(-5, 0) == (0, 0), "seastate.current is read; NaN is still water");
                IWaveTrainSource none = new NoCurrent();
                Check(none.Current(1, 2) == (0, 0), "a source that knows no current: still water");
            }

            // ---- 3. a surface with no trains, but a pose
            {
                var posed = new Dictionary<string, object>
                {
                    ["owner"] = "test", ["version"] = 1, ["priority"] = 50, ["revision"] = new Func<long>(() => 0),
                    ["seconds"] = new Func<double>(() => clock),
                    // Sea's own pose (src/Contracts/ContractMath.cs PoseOn) on a tilted plane: the shape's +X turned by yaw + 90°
                    ["pose"] = new Func<double, double, double, double, double, (double Hold, double Lift, double Roll, double Pitch)>((x, z, yaw, length, beam) =>
                    {
                        var (sin, cos) = Math.SinCos(yaw + Math.PI / 2);
                        double At(double ax, double az) => Plane(x + cos * ax + sin * az, z - sin * ax + cos * az);
                        double px = At(length / 2, 0), mx = At(-length / 2, 0), pz = At(0, beam / 2), mz = At(0, -beam / 2);
                        return (0.8, (px + mx + pz + mz) / 4, -Math.Atan((pz - mz) / beam), Math.Atan((px - mx) / length));
                    }),
                };
                var pc = new Dictionary<string, object> { [ContractWaves.SurfaceKey] = posed };
                var posedContract = new ContractWaves(pc, warnings.Add);
                double worst = 0;
                foreach (double yaw in new[] { 0.0, 0.6, Math.PI / 2, 2.5, -1.9 })
                {
                    var a = ShipMotion.Ride(posedContract, waves, 40, -30, yaw, 2, hl, hb, cx, md, 0, out var surf);
                    double s = Math.Sin(yaw), c = Math.Cos(yaw);
                    double p = ShipMotion.SoftCap(Math.Atan(gx * s + gz * c), md.MaxPitch * Math.PI / 180);
                    double r = ShipMotion.SoftCap(-Math.Atan(-gx * c + gz * s), md.MaxRoll * Math.PI / 180);
                    var want = new ShipAttitude(Plane(40 + s * cx, -30 + c * cx) - Math.Sin(p) * cx, p, r);
                    worst = Math.Max(worst, Math.Max(Math.Abs(a.Heave - want.Heave), Math.Max(Math.Abs(a.Pitch - want.Pitch), Math.Abs(a.Roll - want.Roll))));
                    if (surf.Heave != 0) worst = 1;
                }
                Check(worst < 1e-9, $"no trains but a pose: she lies as it says, its Pitch bow up and its Roll starboard down at every heading (within {worst:0.0e0})");
                posed["trains"] = new Func<double, double, double, double[], double[], double[], double[], double[], int>((x, z, t, a, b, c, d, e) => 0);
                var zero = ShipMotion.Ride(posedContract, waves, 40, -30, 0.6, 2, hl, hb, cx, md, 0, out _);
                Check(zero.Pitch != 0 && zero.Heave != 0, "trains that give none (0): the pose too");
                posed["trains"] = new Func<double, double, double, double[], double[], double[], double[], double[], int>((x, z, t, a, b, c, d, e) =>
                {
                    a[0] = 0.1; b[0] = 0; c[0] = 1; d[0] = 0; e[0] = 0.5; return 1;
                });
                var trains = ShipMotion.Ride(posedContract, waves, 40, -30, 0.6, 2, hl, hb, cx, md, 0, out _);
                var fromTrains = ShipMotion.Waves(waves, 1, waves.Time, 40, -30, 0.6, 2, hl, hb, cx, md, 0);
                Check(SameAttitude(trains, fromTrains), "trains given: the trains, not the pose");
                posed.Remove("trains");
                var pose = (Delegate)posed["pose"];
                posed["pose"] = new Func<double, double, double, double, double, (double Hold, double Lift, double Roll, double Pitch)>((x, z, yaw, l, b) => (0, 1, 0.1, 0.1));
                var hold0 = ShipMotion.Ride(posedContract, waves, 40, -30, 0.6, 2, hl, hb, cx, md, 0, out _);
                posed.Remove("pose");
                var nothing = ShipMotion.Ride(posedContract, waves, 40, -30, 0.6, 2, hl, hb, cx, md, 0, out _);
                Check(hold0.Heave == 0 && hold0.Pitch == 0 && nothing.Heave == 0 && nothing.Roll == 0, "Hold 0 (no swell drawn), or neither trains nor pose: calm water");
            }

            // ---- 4. the weather's wind and the sea state, for a side without Sea
            {
                var c = new Dictionary<string, object>();
                var weather = new ContractWaves(c, warnings.Add);
                Check(!weather.HasWind && !weather.Wind(0, 0, 10, out _, out _), "no weather published: no wind (calm)");
                bool gusts = false;
                c[ContractWaves.WeatherKey] = new Dictionary<string, object>
                {
                    ["owner"] = "test",
                    ["wind"] = new Func<double, double, double, bool, (double X, double Z)>((x, z, h, g) => { gusts = g; return (h, -5); }),
                };
                Check(weather.HasWind && weather.Wind(0, 0, 12.5, out double wx, out double wz) && wx == 12.5 && wz == -5 && gusts,
                    "weather.wind is read at the hour asked, with its gusts, in m/s (Ships divides by 25 for Sea's units)");
                c[ContractWaves.SeaStateKey] = new Dictionary<string, object>
                {
                    ["owner"] = "test",
                    ["at"] = new Func<double, double, double, (double Height, double Period, double DirX, double DirZ, double WindX, double WindZ, double Storm)>((x, z, h) => (2.5, 7.5, 1, 0, 10, 0, 0.1)),
                };
                Check(weather.SeaState(0, 0, 3, out double hs, out double period) && hs == 2.5 && period == 7.5, "seastate.at gives the sea's height and period");
            }
        }

        sealed class NoCurrent : IWaveTrainSource
        {
            public bool Sample(WaveTrains into, double x, double z) => false;
            public double Shelter(double x, double z) => 1;
        }
    }
}
