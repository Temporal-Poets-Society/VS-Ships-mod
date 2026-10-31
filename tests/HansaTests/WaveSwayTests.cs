using System;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        // Seeded Sea spectrum, 4 minutes at 30 Hz. Amplitudes are half peak-to-peak;
        // periods are mean upward mean-crossing intervals (irregular sea, not a single sine).
        static void WaveSway(string root)
        {
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets/ships/config/ships/caravel.json")));
            def.Init();
            def.Waterplane(out double hl, out double hb, out double cx);
            var hull = new ShipWaveHull(def);
            cx = hull.FlotationCentre;
            Console.WriteLine($"waterplane {2 * hl:F2} x {2 * hb:F2}, centre {cx:F3}; roll T={def.Motion.RollPeriod:F2}, damping={def.Motion.RollDamping:F2}");
            Console.WriteLine($"lateral centre {hull.LateralCentre:F3}; rudder resistance {hull.RudderResistance:F3}");
            Check(cx > -4 && cx < -2.6 && hull.LateralCentre > -4 && hull.LateralCentre < -2.6,
                "pivots measured near amidships, aft of main mast, independent of entity origin");
            MotionPivots(hull);
            WaveYawResponse(hull);
            var spec = new Sea.WaveSpectrum(4242, 0);
            string traceDir = Path.Combine(root, "testserver/wave-sway-offline");
            Directory.CreateDirectory(traceDir);
            foreach (bool storm in new[] { false, true })
            foreach (bool underway in new[] { false, true })
            foreach (bool beam in new[] { true, false })
            {
                double hs = storm ? 6 : 0.7;
                double period = storm ? Math.Sqrt(2 * Math.PI * 30 * hs / 9.81) : 2.5 + 3.2 * Math.Sqrt(hs);
                var sea = new Sea.SeaState(hs, 0, 1, period);
                var wind = new Sea.Wind(0, storm ? 1.5 : 0.5);
                var place = new Sea.WavePlace();
                double initialYaw = beam ? Math.PI / 2 : Math.PI, yaw = initialYaw, speed = underway ? 3 : 0;
                double steady = underway ? ShipMotion.SailSteadiness(0.55, wind.Strength) : 0;
                double[] h = new double[7200], p = new double[h.Length], r = new double[h.Length], y = new double[h.Length];
                string name = $"{(storm ? "storm" : "moderate")}-{(underway ? "way" : "rest")}-{(beam ? "beam" : "head")}";
                using var csv = new StreamWriter(Path.Combine(traceDir, name + ".csv"));
                csv.WriteLine("seconds,yawDeg,pitchDeg,rollDeg,heave");
                double x = 1000, z = 2000;
                for (int i = 0; i < h.Length; i++)
                {
                    double t = 600 + i / 30.0;
                    place.Update(spec, t, sea, wind, sea, wind);
                    var a = ShipMotion.Waves(place, 1, t, x, z, yaw, speed, hl, hb, cx, def.Motion, steady);
                    h[i] = a.Heave; p[i] = a.Pitch * 180 / Math.PI; r[i] = a.Roll * 180 / Math.PI;
                    y[i] = (yaw - initialYaw) * 180 / Math.PI;
                    csv.WriteLine(FormattableString.Invariant($"{i / 30.0:F3},{y[i]:F6},{p[i]:F6},{r[i]:F6},{h[i]:F6}"));
                    double rate = ShipMotion.WaveYawRate(place, 1, t, x, z, yaw, speed, hull);
                    HelmMath.Advance(x, z, yaw, yaw + rate / 30, speed, 0, 1 / 30.0, hull.LateralCentre, out x, out z);
                    yaw += rate / 30;
                }
                double Amp(double[] v) => (v.Max() - v.Min()) / 2;
                double T(double[] v)
                {
                    double mean = v.Average();
                    var crossings = Enumerable.Range(1, v.Length - 1).Where(i => v[i - 1] < mean && v[i] >= mean).ToArray();
                    return crossings.Length < 2 ? 0 : (crossings[^1] - crossings[0]) / (30.0 * (crossings.Length - 1));
                }
                Console.WriteLine($"{name,-23} yaw {Amp(y):F3}/{T(y):F2}s pitch {Amp(p):F2}/{T(p):F2}s roll {Amp(r):F2}/{T(r):F2}s heave {Amp(h):F2}m");
                Check(Amp(r) < 25 && Amp(p) < 12, name + ": finite wave response within limits");
                Check(Amp(y) > 0.005 && Amp(y) < (storm ? 5 : 1.5) && T(y) > 2 && T(y) < 16,
                    name + ": small wave yaw, period of several seconds");
                Check(Amp(r) > (beam ? (storm ? 7 : 1) : 0.02) && Amp(r) < (beam ? (storm ? 15 : 4.5) : 5),
                    name + ": responds to waves without excessive roll resonance");
                double jump = Enumerable.Range(1, y.Length - 1).Max(i => Math.Abs(y[i] - y[i - 1]));
                double acceleration = Enumerable.Range(2, y.Length - 2).Max(i => Math.Abs(y[i] - 2 * y[i - 1] + y[i - 2]));
                Check(jump < 0.07 && acceleration < 0.02, name + ": no yaw jitter");
            }
        }

        static void MotionPivots(ShipWaveHull hull)
        {
            double yaw = 1.2, turned = yaw + 0.15;
            HelmMath.Advance(10, 20, yaw, turned, 0, 0, 0.1, hull.LateralCentre, out double nx, out double nz);
            double dx = nx + Math.Sin(turned) * hull.LateralCentre - 10 - Math.Sin(yaw) * hull.LateralCentre;
            double dz = nz + Math.Cos(turned) * hull.LateralCentre - 20 - Math.Cos(yaw) * hull.LateralCentre;
            Check(Math.Abs(dx) + Math.Abs(dz) < 1e-10 && Math.Abs(nx - 10) + Math.Abs(nz - 20) > 0.1,
                "yaw axis at lateral centre, not x=0 (centre stays fixed during pure turn)");
            double err = 0;
            foreach (double angle in new[] { -0.2, -0.1, 0.1, 0.2 })
            {
                // The waterplane centre stays at its heave height through a pitch; the origin is offset.
                double heave = -Math.Sin(angle) * hull.FlotationCentre;
                var matrix = ShipFrame.ModelMatrix(new Vintagestory.API.Client.Matrixf(), 0, heave, 0, Math.PI / 2, angle, 0);
                var p = matrix.TransformVector(new Vec4f((float)hull.FlotationCentre, 0, 0, 1));
                err = Math.Max(err, Math.Abs(p.Y));
                Check(Math.Abs(p.X - hull.FlotationCentre) < 0.06,
                    $"pitch {angle * 180 / Math.PI:F1}deg: flotation centre within 6 cm horizontally");
                var roll = ShipFrame.ModelMatrix(new Vintagestory.API.Client.Matrixf(), 0, 0, 0, Math.PI / 2, 0, angle);
                var axis = roll.TransformVector(new Vec4f((float)hull.FlotationCentre, 0, 0, 1));
                err = Math.Max(err, Math.Abs(axis.Y) + Math.Abs(axis.Z));
            }
            Check(err < 1e-5, $"drawn pitch crosses flotation centre; roll axis at waterline (error {err:E2}m)");
            HelmMath.Advance(10, 20, yaw, turned, 3, 0.3, 0.1, hull.LateralCentre, out nx, out nz);
            HelmMath.Advance(10, 20, yaw, (yaw + turned) / 2, 3, 0.3, 0.05, hull.LateralCentre, out double hx, out double hz);
            HelmMath.Advance(hx, hz, (yaw + turned) / 2, turned, 3, 0.3, 0.05, hull.LateralCentre, out hx, out hz);
            Check(Math.Abs(hx - nx) + Math.Abs(hz - nz) < 1e-10,
                "server tick and two client frames describe the same turn and pivot");
        }

        static void WaveYawResponse(ShipWaveHull hull)
        {
            var wave = new Sea.WavePlace();
            double k = 2 * Math.PI / 90, period = Math.Sqrt(2 * Math.PI * 90 / 9.81);
            double Integrate(double dt, double yaw0, double speed, double rudder, out double measuredPeriod)
            {
                double yaw = yaw0, x = 0, z = 0, lo = yaw0, hi = yaw0, previous = 0;
                var crosses = new System.Collections.Generic.List<double>();
                for (double t = 0; t < 120; t += dt)
                {
                    wave.SetTrains(t, 1, new[] { k / Math.Sqrt(2) }, new[] { k / Math.Sqrt(2) }, new[] { 2.0 }, new[] { 0.0 });
                    double rate = ShipMotion.WaveYawRate(wave, 1, t, x, z, yaw0, speed, hull, rudder);
                    if (t > dt && rate > 0 && previous <= 0) crosses.Add(t);
                    previous = rate;
                    yaw += rate * dt; lo = Math.Min(lo, yaw); hi = Math.Max(hi, yaw);
                    x += Math.Sin(yaw0) * speed * dt; z += Math.Cos(yaw0) * speed * dt;
                }
                measuredPeriod = crosses.Count < 2 ? 0 : (crosses[^1] - crosses[0]) / (crosses.Count - 1);
                return (hi - lo) / 2;
            }
            double a30 = Integrate(1 / 30.0, 0, 0, 1, out double t30);
            double a144 = Integrate(1 / 144.0, 0, 0, 1, out _);
            Check(a30 > 0.005 && a30 < 0.07 && Math.Abs(t30 - period) < 0.04,
                $"oblique 90m swell: yaw amplitude {a30 * 180 / Math.PI:F2}deg, period {t30:F2}s (wave {period:F2}s)");
            Check(Math.Abs(a30 - a144) < 0.0002, "yaw amplitude stable at 30/144 Hz");
            double intact = Integrate(1 / 60.0, Math.PI, 4, 1, out _), missing = Integrate(1 / 60.0, Math.PI, 4, 0, out _);
            Check(intact < missing && intact < a30,
                $"forward flow and intact centred rudder resist wave yaw ({intact * 180 / Math.PI:F4} vs {missing * 180 / Math.PI:F4}deg, at rest {a30 * 180 / Math.PI:F4})");
            wave.SetTrains(0, 1, new[] { 0.0 }, new[] { k }, new[] { 2.0 }, new[] { 0.0 });
            Check(Math.Abs(ShipMotion.WaveYawRate(wave, 1, 0, 0, 0, 0, 0, hull)) < 1e-12 &&
                  Math.Abs(ShipMotion.WaveYawRate(wave, 1, 0, 0, 0, Math.PI / 2, 0, hull)) < 1e-12,
                "pure head and beam waves have no asymmetric yaw moment");
        }
    }
}
