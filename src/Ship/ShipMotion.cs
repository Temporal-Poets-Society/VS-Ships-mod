using System;

namespace Hansa
{
    /// <summary>How a design answers the waves and the wind (ships/*.json "motion"; the defaults suit the carrack).</summary>
    public class MotionDef
    {
        /// <summary>Natural periods (seconds): how fast the hull would bob, pitch and roll back on its own.</summary>
        public float HeavePeriod = 3.5f, PitchPeriod = 3.8f, RollPeriod = 7.5f;
        /// <summary>Damping, as a fraction of critical: heave and pitch die out fast, roll rings on longer.</summary>
        public float HeaveDamping = 0.3f, PitchDamping = 0.35f, RollDamping = 0.2f;
        /// <summary>
        /// More roll damping with her sails set and drawing: a ship under sail is steadied by her canvas (the wind's
        /// pressure resists every roll), and rolls far less than one lying ahull. Full sail in a fresh wind adds this.
        /// </summary>
        public float SailRollDamping = 0.3f;
        /// <summary>Degrees the waves can pitch and roll her at most (approached smoothly).</summary>
        public float MaxPitch = 12, MaxRoll = 25;
        /// <summary>Heel (degrees) under full sail in a beam wind of strength 1 (a storm); it grows with the wind squared.</summary>
        public float HeelPerWind = 18;
        /// <summary>
        /// The most she heels (degrees, approached smoothly): past it the crew spills wind and shortens sail. 20 till
        /// 0.3.5; the user found her rolling too much under sail, and 12 is about where a ship of hers would reef.
        /// </summary>
        public float MaxHeel = 12;
        /// <summary>
        /// Seconds her lean takes to follow the wind in her sails (a time constant): a heavy hull with her sails steadying
        /// her answers a gust slowly, and the crew eases the sheets in it. 0.67 till 0.3.5, when every gust (they come and
        /// go over 7–15 s) rocked her by several degrees.
        /// </summary>
        public float HeelResponse = 4;
        /// <summary>The waterplane as the waves feel it, as a fraction of the waterline footprint (a pointed hull has less).</summary>
        public float WaterplaneFraction = 0.9f;

        public MotionDef Clone() => (MotionDef)MemberwiseClone();
    }

    /// <summary>The ship's motion on the water: heave (blocks up), pitch (radians, bow up +), roll (radians, starboard down +).</summary>
    public struct ShipAttitude
    {
        public double Heave, Pitch, Roll;

        public ShipAttitude(double heave, double pitch, double roll) { Heave = heave; Pitch = pitch; Roll = roll; }

        public ShipAttitude Scaled(double k) => new(Heave * k, Pitch * k, Roll * k);
    }

    /// <summary>
    /// A ship riding Sea's waves, as a pure function of the waves at its place, the wave clock and its own position,
    /// heading and speed, so the server and every client work out the same motion without sending it.
    ///
    /// Each wave train is a sine running over the waterplane. For each one:
    ///  - the push up (heave) is the surface averaged over the waterplane, and the tip (pitch, roll) its best-fit
    ///    slope along and across it, so waves much shorter than the ship hardly move her;
    ///  - the hull answers like a damped spring with its own period, at the frequency it meets the wave with
    ///    (sailing into the waves meets them faster).
    /// The sum over the trains is the motion. Pitch and roll are soft-capped.
    /// </summary>
    public static class ShipMotion
    {
        /// <param name="waves">the waves at the ship (Sea.SeaSystem.SampleWaves at its origin)</param>
        /// <param name="factor">how much of the open-sea waves reach here (Sea.SeaSystem.WaveFactorAt)</param>
        /// <param name="t">wave clock time</param>
        /// <param name="x">origin (waterline midships) x</param>
        /// <param name="yaw">heading (an entity's yaw: the bow points (sin yaw, cos yaw))</param>
        /// <param name="speed">blocks per second along the heading</param>
        /// <param name="halfLength">half the waterplane's length</param>
        /// <param name="halfBeam">half its width</param>
        /// <param name="centreX">where its centre is along the keel</param>
        /// <param name="steady">how much her sails steady her (0..1, <see cref="SailSteadiness"/>)</param>
        public static ShipAttitude Waves(Sea.WavePlace waves, double factor, double t, double x, double z, double yaw, double speed,
            double halfLength, double halfBeam, double centreX, MotionDef m, double steady = 0)
        {
            double rollDamping = m.RollDamping + m.SailRollDamping * Math.Clamp(steady, 0, 1);
            if (waves == null || waves.Count == 0 || factor <= 0) return default;
            double s = Math.Sin(yaw), c = Math.Cos(yaw);
            // the waterplane's centre in the world
            double cx = x + s * centreX, cz = z + c * centreX;
            double l = halfLength * m.WaterplaneFraction, b = halfBeam * m.WaterplaneFraction;
            double heave = 0, pitch = 0, roll = 0;
            for (int i = 0; i < waves.Count; i++)
            {
                double kx = waves.Kx[i], kz = waves.Kz[i], a = waves.Amp[i] * factor;
                double ku = kx * s + kz * c;      // along the keel, toward the bow
                double kv = -kx * c + kz * s;     // across, toward starboard
                double theta = kx * cx + kz * cz - waves.Omega[i] * t + waves.Phase[i];
                double we = waves.Omega[i] - ku * speed;   // how fast the ship meets it
                double su = Sinc(ku * l), sv = Sinc(kv * b);
                heave += Respond(a * su * sv, theta, we, m.HeavePeriod, m.HeaveDamping);
                // slopes go with cos θ: a quarter turn ahead of the height
                pitch += Respond(a * ku * Slope(ku * l) * sv, theta + Math.PI / 2, we, m.PitchPeriod, m.PitchDamping);
                roll += Respond(a * kv * Slope(kv * b) * su, theta + Math.PI / 2, we, m.RollPeriod, rollDamping);
            }
            double p = SoftCap(Math.Atan(pitch), m.MaxPitch * Math.PI / 180);
            double r = SoftCap(-Math.Atan(roll), m.MaxRoll * Math.PI / 180);   // the surface rising to starboard lifts starboard
            // heave above was for the waterplane's centre; the origin sits centreX behind it
            return new ShipAttitude(heave - Math.Sin(p) * centreX, p, r);
        }

        /// <summary>
        /// Wave-frequency yaw, in rad/s (+ port). The transverse orbital velocity varies along the keel:
        /// its first moment turns the hull, while the immersed lateral area and rudder resist it. A first-order
        /// inertia/damping response, not a spring to an arbitrary compass heading. Uniform beam or head waves
        /// give no yaw moment; oblique trains in a real spectrum give a small, slow sheer.
        /// </summary>
        public static double WaveYawRate(Sea.WavePlace waves, double factor, double t, double x, double z,
            double yaw, double speed, ShipWaveHull hull, double rudder = 1)
        {
            if (waves == null || factor <= 0 || hull == null) return 0;
            double s = Math.Sin(yaw), c = Math.Cos(yaw), rate = 0;
            double cx = x + s * hull.LateralCentre, cz = z + c * hull.LateralCentre;
            // Hull inertia grows with L², lateral damping with L. Forward flow over the hull and the
            // (centred or lashed) rudder adds resistance. Rudder damage removes only its contribution.
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
                    Slope(ku * hull.Length / 2) * Sinc(kv * hull.Beam / 2);
                double im = we * tau, d = damping * damping + im * im;
                rate += force * (damping * Math.Cos(theta) - im * Math.Sin(theta)) / d;
            }
            return SoftCap(rate, 2 * Math.PI / 180);
        }

        /// <summary>
        /// How much her sails steady her against rolling (0..1): the sail set, and a wind that fills it (full from a fresh
        /// breeze, 10 m/s, up).
        /// </summary>
        public static double SailSteadiness(double sail, double windStrength) => Math.Clamp(sail, 0, 1) * Math.Min(1, windStrength / 0.4);

        /// <summary>
        /// The lean from the wind in the sails (radians, starboard down +): full sail and a beam wind of strength 1
        /// give <see cref="MotionDef.HeelPerWind"/> degrees, less with less sail, head or following winds.
        /// </summary>
        /// <param name="windX">where the true air goes (Sea.Wind X, Z), in vanilla wind units</param>
        /// <param name="speed">her own way (blocks/s): the sails feel the apparent wind, stronger and further forward
        /// close-hauled, weaker running before it</param>
        /// <param name="sail">how far the sails are set (0..1)</param>
        public static double Heel(double windX, double windZ, double yaw, double speed, double sail, MotionDef m, SailingDef s)
        {
            if (sail <= 0) return 0;
            Sailing.Apparent(windX, windZ, yaw, speed, out double ax, out double az);
            double ws = -ax * Math.Cos(yaw) + az * Math.Sin(yaw);     // blowing toward starboard
            double strength = Math.Sqrt(ax * ax + az * az);
            // luffing sails (the wind too far forward) don't press her over
            double drawing = Sailing.Drawing(Sailing.WindAngle(ax, az, yaw, out _), s);
            double deg = m.HeelPerWind * sail * strength * ws * drawing;
            return SoftCap(deg, m.MaxHeel) * Math.PI / 180;
        }

        /// <summary>
        /// The steady answer of a damped spring (natural period T, damping ζ) to a push F·sin(θ) whose phase θ moves at
        /// −we per second: F·|H|·sin(θ + lag), with H(w) = ωn² / (ωn² − w² + 2iζωn·w) at w = −we.
        /// </summary>
        public static double Respond(double f, double theta, double we, double period, double damping)
        {
            if (f == 0) return 0;
            double wn = 2 * Math.PI / Math.Max(0.1, period);
            double re = wn * wn - we * we, im = -2 * damping * wn * we;    // denominator at w = -we
            double d = re * re + im * im;
            double hr = wn * wn * re / d, hi = -wn * wn * im / d;
            return f * (Math.Sin(theta) * hr + Math.Cos(theta) * hi);
        }

        /// <summary>sin x / x: the average of a sine over a stretch of the hull.</summary>
        public static double Sinc(double x) => Math.Abs(x) < 1e-4 ? 1 - x * x / 6 : Math.Sin(x) / x;

        /// <summary>The best-fit slope of a sine over a stretch of the hull, as a fraction of its slope in the middle.</summary>
        public static double Slope(double x) => Math.Abs(x) < 1e-2 ? 1 - x * x / 10 : 3 * (Math.Sin(x) - x * Math.Cos(x)) / (x * x * x);

        /// <summary>Close to v when small, never beyond ±max.</summary>
        public static double SoftCap(double v, double max) => max <= 0 ? 0 : max * Math.Tanh(v / max);
    }

    /// <summary>Motion geometry measured once from the design, independent of masts, rig trim and fittings.</summary>
    public sealed class ShipWaveHull
    {
        public readonly double Length, Beam, Draft, FlotationCentre, LateralCentre, RudderResistance;

        public ShipWaveHull(ShipDefinition def)
        {
            def.Waterplane(out double hl, out double hb, out double mid);
            Length = Math.Max(1, hl * 2); Beam = Math.Max(0.5, hb * 2); Draft = Math.Max(0.25, def.Draft);
            double area = 0, first = 0, lateral = 0, lateralFirst = 0;
            var g = def.Grid;
            var outline = def.WaterClip?.DryOutline;
            for (double x = mid - hl + 0.125; x < mid + hl; x += 0.25)
            {
                // The actual waterplane's centroid, not the midpoint of its stem/transom bounding box.
                double width = outline != null ? 2 * outline.At(x, 0) : 0;
                double bottom = 0;
                if (g != null)
                {
                    int i = g.CellX(x), j = g.CellY(0);
                    if (i >= 0 && i < g.NX)
                    {
                        double lo = double.MaxValue, hi = double.MinValue;
                        for (int k = 0; k < g.NZ; k++)
                        {
                            if (j >= 0 && j < g.NY && g.Solid(i, j, k))
                            { lo = Math.Min(lo, g.OZ + k * g.V); hi = Math.Max(hi, g.OZ + (k + 1) * g.V); }
                            for (int y = 0; y < Math.Min(j + 1, g.NY); y++)
                                if (g.Solid(i, y, k)) { bottom = Math.Min(bottom, g.OY + y * g.V); break; }
                        }
                        if (outline == null && lo <= hi) width = hi - lo;
                    }
                }
                area += width; first += x * width;
                double depth = Math.Clamp(-bottom, 0, Draft);
                lateral += depth * 0.25; lateralFirst += x * depth * 0.25;
            }
            FlotationCentre = area > 0 ? first / area : mid;
            LateralCentre = lateral > 0 ? lateralFirst / lateral : FlotationCentre;
            var rb = def.Damage?.Rudder;
            if (rb?.Length == 6)
            {
                double rudderArea = (rb[3] - rb[0]) * Math.Max(0, Math.Min(0, rb[4]) - rb[1]);
                double arm = (rb[0] + rb[3]) / 2 - LateralCentre;
                RudderResistance = rudderArea * arm * arm / Math.Max(1, lateral * Length * Length / 12);
            }
        }
    }
}
