using System;
using System.Linq;

namespace Hansa
{
    /// <summary>How a design sails (ships/*.json "sailing"; the defaults suit the carrack).</summary>
    public class SailingDef
    {
        /// <summary>Closest she points to the true wind (degrees off it). Closer, the sails won't draw: she's in irons.</summary>
        public float NoGo = 62;
        /// <summary>
        /// Her best speed as a fraction of her top speed, by the true wind's angle off the bow (degrees; 0 = right
        /// ahead, 180 = right astern), under full sail in a wind that fills them. Square-riggers do best on a broad
        /// reach and still run well before the wind.
        /// </summary>
        public float[][] Polar =
        {
            new[] { 0f, 0f }, new[] { 62f, 0f }, new[] { 70f, 0.45f }, new[] { 80f, 0.65f }, new[] { 95f, 0.85f },
            new[] { 120f, 1f }, new[] { 145f, 1f }, new[] { 180f, 0.85f },
        };
        /// <summary>
        /// Wind (vanilla units: 0.3 medium breeze) that gets her going well under full sail. Light airs give less, a gale
        /// hardly more (the hull limits her). Less sail needs more wind for the same speed.
        /// </summary>
        public float FullWind = 0.3f;
        /// <summary>Taken aback: sternway (fraction of top speed) with the wind right ahead and all sail set.</summary>
        public float Aback = 0.08f;
        /// <summary>Farthest the yards brace round from square (degrees).</summary>
        public float MaxBrace = 45;
        /// <summary>
        /// Farthest a fore-and-aft sail swings out to leeward (degrees). The carrack's lateen meets its lee mizzen shrouds
        /// past 31°, so it's trimmed no further (and the eased trim never runs ahead of what's drawn).
        /// </summary>
        public float MaxSheet = 30;
        /// <summary>
        /// Steering with little way on: the fraction of her turn rate left when she's barely moving, and while she's
        /// head to wind with sails set (the crew backs the head sails to push her bow round when tacking).
        /// </summary>
        public float SlowSteer = 0.25f, TackSteer = 0.6f;
        /// <summary>
        /// Windage (0.3.10, the user: "without sails I am stranded"): the wind on her hull, castles, masts and rigging
        /// drives her downwind at this fraction of the wind's speed under bare poles, the wind right astern (real ships:
        /// 1-2 knots in a strong breeze to a gale); beam-on she makes LeewayShare of it sideways; she keeps headway only
        /// within BarePolesSteer degrees of dead downwind (the wind on her castles turns her off it beyond that).
        /// </summary>
        public float Windage = 0.045f, LeewayShare = 0.8f, BarePolesSteer = 40;

        public SailingDef Clone()
        {
            var c = (SailingDef)MemberwiseClone();
            c.Polar = Polar?.Select(p => (float[])p.Clone()).ToArray();
            return c;
        }
    }

    /// <summary>Where the wind is, relative to the ship, and what it does for her.</summary>
    public enum PointOfSail { InIrons, CloseHauled, BeamReach, BroadReach, Running }

    /// <summary>
    /// Sailing by the wind, as pure functions of the wind (Sea's, the same on every side), the heading, the speed and
    /// how much sail is set. The helmsman steers and sets sail; the crew trims the sails.
    ///
    ///  - Speed: the design's polar (by the true wind's angle off the bow) × how well the wind fills the sails. The
    ///    push grows with the sail set and the wind squared, the hull's drag with speed squared, so speed goes with
    ///    √sail × wind until the hull limits her. In a gale a reefed ship is nearly as fast.
    ///  - In irons: head to wind (closer than <see cref="SailingDef.NoGo"/>) the sails don't draw, and right ahead
    ///    they're taken aback and push her astern.
    ///  - Trim: the yards are braced round from square (running) to hard over (close-hauled), the lee yardarm hauled
    ///    aft; a fore-and-aft sail swings out to leeward. Worked out from the apparent wind (what the sails feel).
    /// </summary>
    public static class Sailing
    {
        /// <summary>Blocks per second of wind for one vanilla wind unit.</summary>
        public const double WindUnit = Beaufort.MetersPerSecondPerUnit;

        /// <summary>
        /// Angle (degrees) of the wind off the bow, from where it comes: 0 right ahead, 180 right astern. side: +1
        /// from starboard, -1 from port.
        /// </summary>
        /// <param name="windX">where the air goes (Wind X, Z)</param>
        public static double WindAngle(double windX, double windZ, double yaw, out int side)
        {
            double s = Math.Sin(yaw), c = Math.Cos(yaw);
            double ahead = -windX * s - windZ * c;     // "from" direction along the bow
            double stbd = windX * c - windZ * s;       // … toward starboard
            side = stbd >= 0 ? 1 : -1;
            if (ahead * ahead + stbd * stbd < 1e-18) return 180;
            return Math.Atan2(Math.Abs(stbd), ahead) * 180 / Math.PI;
        }

        /// <summary>The wind the sails feel (vanilla units, where the air goes): the true wind less the ship's own way.</summary>
        public static void Apparent(double windX, double windZ, double yaw, double speed, out double ax, out double az)
        {
            ax = windX - Math.Sin(yaw) * speed / WindUnit;
            az = windZ - Math.Cos(yaw) * speed / WindUnit;
        }

        /// <summary>The closest any design may be tuned to point (degrees off the true wind).</summary>
        public const double MinNoGo = 35;
        /// <summary>Past this true wind angle (degrees) the polar is left as the design has it when tuning her pointing.</summary>
        public const double TuneUpTo = 120;

        /// <summary>
        /// (0.3.13, the user: "sailing up and down the coast of Portugal, can't get much west") The design's sailing,
        /// pointing closer degrees closer to the wind (the config's PointCloser; negative: less close): her no-go angle
        /// comes down by that much (not under <see cref="MinNoGo"/>), and her polar is stretched to match, the speed
        /// she had at her old no-go now at the new one, the same from <see cref="TuneUpTo"/> aft. 0 gives the design as it is.
        /// </summary>
        public static SailingDef Tuned(SailingDef d, double closer)
        {
            var t = d.Clone();
            double no = d.NoGo;
            if (closer == 0 || no <= 1 || no >= TuneUpTo - 1) return t;
            double nn = Math.Clamp(no - closer, Math.Min(MinNoGo, no), TuneUpTo - 5);
            double Map(double x) => x <= no ? x * nn / no : x >= TuneUpTo ? x : TuneUpTo - (TuneUpTo - x) * (TuneUpTo - nn) / (TuneUpTo - no);
            t.NoGo = (float)nn;
            if (t.Polar == null || t.Polar.Length == 0) return t;
            // (a point at her old no-go and at TuneUpTo first: the two stretches differ, and a segment across either would bend)
            var pts = t.Polar.Where(p => p.Length >= 2).Select(p => new[] { p[0], p[1] }).ToList();
            foreach (double at in new[] { no, TuneUpTo })
                if (!pts.Any(p => Math.Abs(p[0] - at) < 1e-4)) pts.Add(new[] { (float)at, (float)Polar(at, d) });
            t.Polar = pts.OrderBy(p => p[0]).Select(p => new[] { (float)Map(p[0]), p[1] }).ToArray();
            return t;
        }

        /// <summary>The design's polar at a true wind angle (degrees), linearly between its points.</summary>
        public static double Polar(double angle, SailingDef d)
        {
            var p = d.Polar;
            if (p == null || p.Length == 0) return 0;
            if (angle <= p[0][0]) return p[0][1];
            for (int i = 1; i < p.Length; i++)
            {
                if (angle > p[i][0]) continue;
                double f = (angle - p[i - 1][0]) / Math.Max(1e-6, p[i][0] - p[i - 1][0]);
                return p[i - 1][1] + (p[i][1] - p[i - 1][1]) * f;
            }
            return p[^1][1];
        }

        /// <summary>
        /// Her best speed as a fraction of her top speed, for a true wind angle (degrees), wind strength (vanilla
        /// units) and the sail set (0..1). Negative when taken aback.
        /// </summary>
        public static double SpeedFactor(double angle, double strength, double sail, SailingDef d)
        {
            if (sail <= 0 || strength <= 0) return 0;
            double fill = Math.Tanh(Math.Sqrt(sail) * strength / Math.Max(0.01, d.FullWind));
            if (angle < d.NoGo) return -d.Aback * (1 - angle / d.NoGo) * Math.Sqrt(sail) * fill;
            return Polar(angle, d) * fill;
        }

        /// <summary>The speed she's making for (blocks/s) with this wind, heading, sail set and top speed.</summary>
        public static double TargetSpeed(double windX, double windZ, double yaw, double sail, double maxSpeed, SailingDef d)
        {
            double angle = WindAngle(windX, windZ, yaw, out _);
            return maxSpeed * SpeedFactor(angle, Math.Sqrt(windX * windX + windZ * windZ), sail, d);
        }

        /// <summary>
        /// The wind on her hull and rig (<see cref="SailingDef.Windage"/>, times factor: the config's Windage): headway
        /// (blocks/s along her heading: + ahead) and leeway (blocks/s to starboard: + starboard). Under bare poles (sail
        /// drawing under 0.05) her headway only within <see cref="SailingDef.BarePolesSteer"/> of dead downwind, easing
        /// off toward it, and leeway; with sail set it helps running and hinders on the wind (the sails' polar is beside
        /// it), and no leeway.
        /// </summary>
        public static void Windage(double windX, double windZ, double yaw, double sail, SailingDef d, double factor, out double ahead, out double leeway)
        {
            ahead = leeway = 0;
            double w = Math.Sqrt(windX * windX + windZ * windZ) * WindUnit * d.Windage * Math.Max(0, factor);
            if (w <= 0) return;
            double angle = WindAngle(windX, windZ, yaw, out int side);
            double off = 180 - angle;                       // degrees off dead downwind
            double c = Math.Cos(off * Math.PI / 180), sn = Math.Sin(off * Math.PI / 180);
            if (sail < 0.05)
            {
                double lim = Math.Cos(Math.Clamp(d.BarePolesSteer, 1, 89) * Math.PI / 180);
                ahead = w * Math.Clamp((c - lim) / (1 - lim), 0, 1);
            }
            else
            {
                // (under sail the sails' polar is her drive and her keel holds her to her course: no leeway of its own)
                ahead = w * c;
                return;
            }
            // (the wind from starboard pushes her to port)
            leeway = -side * w * sn * d.LeewayShare;
        }

        /// <summary>
        /// Her square sails taken aback, for their strain and the warning: her head within the no-go angle of the true
        /// wind. (0.3.13: it went by the apparent wind before, which comes from further forward whenever she has way on,
        /// so close-hauled at 70° true, the apparent wind near 55°, counted as aback: strain and warnings in a strong breeze.
        /// The no-go angle is the true wind's, as for her speed.)
        /// </summary>
        public static bool Aback(double windX, double windZ, double yaw, SailingDef d) => WindAngle(windX, windZ, yaw, out _) < d.NoGo;

        public static PointOfSail Point(double angle, SailingDef d)
        {
            if (angle < d.NoGo) return PointOfSail.InIrons;
            if (angle < 80) return PointOfSail.CloseHauled;
            if (angle < 110) return PointOfSail.BeamReach;
            if (angle < 160) return PointOfSail.BroadReach;
            return PointOfSail.Running;
        }

        /// <summary>How well the sails draw for an apparent wind angle (degrees): 0 when it's so far forward they luff.</summary>
        public static double Drawing(double apparentAngle, SailingDef d)
        {
            double lo = d.NoGo - 35, hi = d.NoGo - 20;
            double t = Math.Clamp((apparentAngle - lo) / (hi - lo), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// How the crew trims the sails for an apparent wind angle (degrees) coming from side (+1 starboard). Both are
        /// radians about the ship's up axis, + turning the starboard end forward and the after end to starboard:
        /// brace for the square yards (square before the wind, hard over close-hauled: the windward yardarm forward and
        /// the lee yardarm hauled aft, so the sail meets the wind nearly edge-on and it fills from aft),
        /// sheet for a fore-and-aft sail (it swings out to leeward, half the wind's angle, at most
        /// <see cref="SailingDef.MaxSheet"/>).
        /// </summary>
        public static void Trim(double apparentAngle, int side, SailingDef d, out double brace, out double sheet)
        {
            double close = d.NoGo - 20;
            double f = Math.Clamp((180 - apparentAngle) / (180 - close), 0, 1);
            // braced at least so far that the wind meets the sail from aft (5° to spare), as far as the yards go round
            // (hull R's yards stop at 36°: closer than 54° apparent the square sails can't but be aback)
            double deg = Math.Min(d.MaxBrace, Math.Max(d.MaxBrace * f, 90 - Math.Abs(apparentAngle) + 5));
            brace = side * deg * Math.PI / 180;
            sheet = -side * Math.Clamp(apparentAngle / 2, Math.Min(5, d.MaxSheet), d.MaxSheet) * Math.PI / 180;
        }

        /// <summary>
        /// How much she turns for the helm: full with way on, <see cref="SailingDef.SlowSteer"/> when she's barely
        /// moving, reversed going astern. Head to wind with sails set (tacking) the crew backs the head sails to push
        /// her bow round the way the helm is put, at least <see cref="SailingDef.TackSteer"/>, even if she's been
        /// taken aback and is making sternway.
        /// </summary>
        public static double Steer(double speed, double maxSpeed, bool tacking, SailingDef d)
        {
            double s = Math.Clamp(Math.Abs(speed) / (0.3 * Math.Max(0.1, maxSpeed)), d.SlowSteer, 1);
            if (tacking) return Math.Max(speed > 0 ? s : 0, d.TackSteer);
            return speed < -0.01 ? -s : s;
        }
    }
}
