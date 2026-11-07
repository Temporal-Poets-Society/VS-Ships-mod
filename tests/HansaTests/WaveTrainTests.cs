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
        /// (7 November 2026) Ships reads the sea through its own <see cref="WaveTrains"/>: from Sea's typed waves (copied)
        /// or from the open sea contracts (<see cref="ContractWaves"/>). The motion must be bit for bit what it was over
        /// <c>Sea.WavePlace</c>: the reference below is <see cref="ShipMotion"/>'s arithmetic as it stood, frozen.
        /// dotnet run -c Release -- wave-trains
        /// </summary>
        static void WaveTrainTests(string root)
        {
            Console.WriteLine("wave trains: Sea's typed waves and the open sea contracts");
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets/ships/config/ships/caravel.json")));
            def.Init();
            def.Waterplane(out double hl, out double hb, out _);
            var hull = new ShipWaveHull(def);
            double cx = hull.FlotationCentre;
            var md = def.Motion;

            var spec = new Sea.WaveSpectrum(4242, 0);
            var sea = new Sea.SeaState(5.5, -1, 0, Math.Sqrt(2 * Math.PI * 165 / 9.81));
            var wind = Sea.Wind.FromCompass(90, 1.5);
            var place = new Sea.WavePlace();
            var typed = new WaveTrains();
            var filled = new WaveTrains();
            var fromContract = new WaveTrains();

            // a fake side: the contract's trains are the place's numbers, its clock the test's
            double clock = 0;
            var cache = new Dictionary<string, object>();
            var warnings = new List<string>();
            Func<double, double, double, double[], double[], double[], double[], double[], int> trainsOf(Sea.WavePlace p) =>
                (x, z, t, kx, kz, om, ph, am) =>
                {
                    for (int i = 0; i < p.Count; i++) { kx[i] = p.Kx[i]; kz[i] = p.Kz[i]; om[i] = p.Omega[i]; ph[i] = p.Phase[i]; am[i] = p.Amp[i]; }
                    return p.Count;
                };
            Dictionary<string, object> Surface(Func<double, double, double, double[], double[], double[], double[], double[], int> trains) => new()
            {
                ["owner"] = "test", ["version"] = 1, ["priority"] = 50, ["revision"] = new Func<long>(() => 0),
                ["seconds"] = new Func<double>(() => clock), ["trains"] = trains,
            };
            cache[ContractWaves.SurfaceKey] = Surface(trainsOf(place));
            var contract = new ContractWaves(cache, warnings.Add);

            static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
            static bool SameAttitude(ShipAttitude a, ShipAttitude b) => Same(a.Heave, b.Heave) && Same(a.Pitch, b.Pitch) && Same(a.Roll, b.Roll);

            int samples = 0, motionDiff = 0, yawDiff = 0, heightDiff = 0, hsDiff = 0, contractDiff = 0;
            double maxTrains = 0;
            foreach (double yaw in new[] { 0.0, 0.7, Math.PI / 2, 2.4, Math.PI })
            foreach (double speed in new[] { 0.0, 3.0, -1.0 })
            foreach (double factor in new[] { 1.0, 0.2, 0.55 })
            for (double t = 600; t < 660; t += 0.35)
            {
                double x = 1000 + Math.Sin(yaw) * speed * (t - 600), z = 2000 + Math.Cos(yaw) * speed * (t - 600);
                double steady = speed > 0 ? 0.6 : 0;
                place.Update(spec, t, sea, wind);
                maxTrains = Math.Max(maxTrains, place.Count);
                typed.CopyFrom(place);
                filled.Set(place.Time, place.Count, place.Kx, place.Kz, place.Omega, place.Phase, place.Amp);
                clock = t;
                if (!contract.Sample(fromContract, x, z)) contractDiff++;

                var reference = LegacyWaves(place, factor, t, x, z, yaw, speed, hl, hb, cx, md, steady);
                double referenceYaw = LegacyWaveYawRate(place, factor, t, x, z, yaw, speed, hull, 0.7);
                foreach (var w in new[] { typed, filled, fromContract })
                {
                    if (!SameAttitude(ShipMotion.Waves(w, factor, w.Time, x, z, yaw, speed, hl, hb, cx, md, steady), reference)) motionDiff++;
                    if (!Same(ShipMotion.WaveYawRate(w, factor, w.Time, x, z, yaw, speed, hull, 0.7), referenceYaw)) yawDiff++;
                    if (!Same(w.SignificantHeight, place.SignificantHeight)) hsDiff++;
                }
                if (!Same(typed.HeightAt(x + 3, z - 2), place.HeightAt(x + 3, z - 2))) heightDiff++;
                if (!Same(fromContract.Time, place.Time) || fromContract.Count != place.Count) contractDiff++;
                samples++;
            }
            Check(maxTrains >= 10, $"a storm sea runs many trains ({maxTrains} at most, room for {WaveTrains.MaxWaves})");
            Check(motionDiff == 0, $"heave, pitch and roll are bit for bit the same from Sea's place, its copy, the same numbers filled in, and the contract ({samples} samples × 3, {motionDiff} differ)");
            Check(yawDiff == 0, $"the wave yaw likewise ({yawDiff} differ)");
            Check(hsDiff == 0, $"significant height as Sea's ({hsDiff} differ)");
            Check(heightDiff == 0, $"the copied waves' surface is Sea's own, crest shaping and all ({heightDiff} differ)");
            Check(contractDiff == 0, $"the contract's trains come at its own clock ({contractDiff} differ)");

            // the contract's surface is its plain sum of trains
            {
                double x = 1003.5, z = 1998.25, sum = 0;
                for (int i = 0; i < fromContract.Count; i++)
                    sum += fromContract.Amp[i] * Math.Sin(fromContract.Kx[i] * x + fromContract.Kz[i] * z - fromContract.Omega[i] * fromContract.Time + fromContract.Phase[i]);
                Check(Same(fromContract.HeightAt(x, z), sum), "the contract's surface is amp · sin(kx·x + kz·z − omega·t + phase), summed");
            }

            // shelter: the sea state's, else the open sea's
            Check(contract.Shelter(0, 0) == 1, "no seastate published: all of the open sea's waves reach her");
            cache[ContractWaves.SeaStateKey] = new Dictionary<string, object> { ["owner"] = "test", ["shelter"] = new Func<double, double, double>((x, z) => x < 0 ? 0.2 : 1.7) };
            Check(contract.Shelter(-5, 0) == 0.2 && contract.Shelter(5, 0) == 1, "seastate.shelter is read, and kept within 0–1");

            // a new entry (a new owner, or a revision) is read at once: nothing of the old one is kept
            var one = new Sea.WavePlace();
            one.SetTrains(10, 1, new[] { 0.1 }, new[] { 0.0 }, new[] { 0.5 }, new[] { 0.25 });
            cache[ContractWaves.SurfaceKey] = Surface(trainsOf(one));
            clock = 10;
            Check(contract.Sample(fromContract, 0, 0) && fromContract.Count == 1 && fromContract.Amp[0] == 0.5, "a replaced surface entry is used from the next sample on");

            // more trains than room: capped at 24
            cache[ContractWaves.SurfaceKey] = Surface((x, z, t, kx, kz, om, ph, am) =>
            {
                for (int i = 0; i < kx.Length; i++) { kx[i] = 0.01 * (i + 1); kz[i] = 0; om[i] = 1; ph[i] = 0; am[i] = 0.1; }
                return 40;
            });
            Check(contract.Sample(fromContract, 0, 0) && fromContract.Count == WaveTrains.MaxWaves, $"a count beyond the arrays is cut to {WaveTrains.MaxWaves}");

            // no answer (R7): NaN or an exception is calm water, logged once
            cache[ContractWaves.SurfaceKey] = Surface((x, z, t, kx, kz, om, ph, am) => { am[0] = double.NaN; kx[0] = kz[0] = om[0] = ph[0] = 0; return 1; });
            bool nan = contract.Sample(fromContract, 0, 0);
            cache[ContractWaves.SurfaceKey] = Surface((x, z, t, kx, kz, om, ph, am) => throw new InvalidOperationException("test"));
            bool thrown = contract.Sample(fromContract, 0, 0);
            Check(!nan && !thrown && fromContract.Count == 0 && fromContract.SignificantHeight == 0, "NaN or an exception from the surface: calm water for that sample");
            Check(warnings.Count <= 1, $"… logged once ({warnings.Count})");
            var calm = ShipMotion.Waves(fromContract, 1, fromContract.Time, 0, 0, 0, 0, hl, hb, cx, md);
            Check(calm.Heave == 0 && calm.Pitch == 0 && calm.Roll == 0, "… and she lies still on it");

            // none published: no waves
            cache.Remove(ContractWaves.SurfaceKey);
            Check(!contract.Sample(fromContract, 0, 0), "no surface published: no waves to read");
        }

        // ---- ShipMotion.Waves and WaveYawRate over Sea.WavePlace, as they stood before WaveTrains (frozen; do not edit)

        static ShipAttitude LegacyWaves(Sea.WavePlace waves, double factor, double t, double x, double z, double yaw, double speed,
            double halfLength, double halfBeam, double centreX, MotionDef m, double steady = 0)
        {
            double rollDamping = m.RollDamping + m.SailRollDamping * Math.Clamp(steady, 0, 1);
            if (waves == null || waves.Count == 0 || factor <= 0) return default;
            double s = Math.Sin(yaw), c = Math.Cos(yaw);
            double cx = x + s * centreX, cz = z + c * centreX;
            double l = halfLength * m.WaterplaneFraction, b = halfBeam * m.WaterplaneFraction;
            double heave = 0, pitch = 0, roll = 0;
            for (int i = 0; i < waves.Count; i++)
            {
                double kx = waves.Kx[i], kz = waves.Kz[i], a = waves.Amp[i] * factor;
                double ku = kx * s + kz * c;
                double kv = -kx * c + kz * s;
                double theta = kx * cx + kz * cz - waves.Omega[i] * t + waves.Phase[i];
                double we = waves.Omega[i] - ku * speed;
                double su = ShipMotion.Sinc(ku * l), sv = ShipMotion.Sinc(kv * b);
                heave += ShipMotion.Respond(a * su * sv, theta, we, m.HeavePeriod, m.HeaveDamping);
                pitch += ShipMotion.Respond(a * ku * ShipMotion.Slope(ku * l) * sv, theta + Math.PI / 2, we, m.PitchPeriod, m.PitchDamping);
                roll += ShipMotion.Respond(a * kv * ShipMotion.Slope(kv * b) * su, theta + Math.PI / 2, we, m.RollPeriod, rollDamping);
            }
            double p = ShipMotion.SoftCap(Math.Atan(pitch), m.MaxPitch * Math.PI / 180);
            double r = ShipMotion.SoftCap(-Math.Atan(roll), m.MaxRoll * Math.PI / 180);
            return new ShipAttitude(heave - Math.Sin(p) * centreX, p, r);
        }

        static double LegacyWaveYawRate(Sea.WavePlace waves, double factor, double t, double x, double z,
            double yaw, double speed, ShipWaveHull hull, double rudder = 1)
        {
            if (waves == null || factor <= 0 || hull == null) return 0;
            double s = Math.Sin(yaw), c = Math.Cos(yaw), rate = 0;
            double cx = x + s * hull.LateralCentre, cz = z + c * hull.LateralCentre;
            double tau = 1.5 * Math.Sqrt(hull.Length / 9.81);
            double damping = 1 + Math.Abs(speed) / Math.Sqrt(9.81 * hull.Draft) *
                (1 + Math.Clamp(rudder, 0, 1) * hull.RudderResistance);
            for (int i = 0; i < waves.Count; i++)
            {
                double kx = waves.Kx[i], kz = waves.Kz[i], k = Math.Sqrt(kx * kx + kz * kz);
                if (k < 1e-9) continue;
                double ku = kx * s + kz * c, kv = -kx * c + kz * s;
                double theta = kx * cx + kz * cz - waves.Omega[i] * t + waves.Phase[i];
                double we = waves.Omega[i] - ku * speed;
                double force = -waves.Amp[i] * factor * waves.Omega[i] * kv / k * ku *
                    ShipMotion.Slope(ku * hull.Length / 2) * ShipMotion.Sinc(kv * hull.Beam / 2);
                double im = we * tau, d = damping * damping + im * im;
                rate += force * (damping * Math.Cos(theta) - im * Math.Sin(theta)) / d;
            }
            return ShipMotion.SoftCap(rate, 2 * Math.PI / 180);
        }
    }
}
