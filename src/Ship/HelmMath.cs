using System;

namespace Hansa
{
    /// <summary>
    /// The helm and the wind relative to her, as pure functions (the server's sailing, the HUD's wind panel, the map
    /// layer and the offline tests share them). Her frame: the bow points along (sin yaw, cos yaw) in the world, her
    /// starboard side is (-cos yaw, sin yaw) (+X east, +Z south), so turning to starboard makes the yaw smaller.
    /// </summary>
    public static class HelmMath
    {
        /// <summary>Where the bow points in the world (x, z).</summary>
        public static (double X, double Z) Bow(double yaw) => (Math.Sin(yaw), Math.Cos(yaw));

        /// <summary>Her starboard side in the world (x, z).</summary>
        public static (double X, double Z) Starboard(double yaw) => (-Math.Cos(yaw), Math.Sin(yaw));

        /// <summary>
        /// The helm from the keys: +1 to starboard (D, the "right" key), -1 to port (A). The same at a boat's oars: D pulls
        /// the port oar harder, which turns her bow to starboard.
        /// </summary>
        public static float RudderInput(bool left, bool right) => (right ? 1 : 0) - (left ? 1 : 0);

        /// <summary>
        /// Her turn (radians a second, + = the yaw growing = the bow swinging to port) for the helm (rudder -1 port ..
        /// +1 starboard), her turn rate (degrees a second) and how well she answers (<see cref="Sailing.Steer"/>: positive
        /// with way on, negative making sternway). With headway the helm to starboard swings her bow to starboard.
        /// </summary>
        public static double YawRate(double rudder, double turnRateDeg, double steer) => -rudder * turnRateDeg * Math.PI / 180 * steer;

        /// <summary>
        /// A world direction (x, z) relative to her bow, as an angle (radians) clockwise seen from above: 0 right ahead,
        /// +π/2 to starboard, -π/2 to port, ±π right astern. What the wind panel turns its arrows by.
        /// </summary>
        public static double Relative(double x, double z, double yaw)
        {
            var b = Bow(yaw);
            var s = Starboard(yaw);
            return Math.Atan2(x * s.X + z * s.Z, x * b.X + z * b.Z);
        }

        /// <summary>
        /// How she lies to the wind, for the helmsman: "inirons" (head to wind, the sails won't draw), "aback" (the wind
        /// on the wrong side of the square sails: within the no-go angle with sail set and her head still swinging), or
        /// the point of sail ("closehauled", "beamreach", "broadreach", "running"); tack: +1 starboard tack (the wind from
        /// starboard), -1 port tack.
        /// </summary>
        /// <param name="windX">where the true air goes (Sea.Wind X, Z)</param>
        /// <param name="sail">how much sail she has set (0..1)</param>
        /// <param name="speed">her way (blocks/s, negative: sternway)</param>
        public static string PointOfSail(double windX, double windZ, double yaw, double sail, double speed, SailingDef d, out int tack)
        {
            double angle = Sailing.WindAngle(windX, windZ, yaw, out tack);
            var p = Sailing.Point(angle, d);
            if (p == Hansa.PointOfSail.InIrons) return sail > 0.05 && speed < -0.05 ? "aback" : "inirons";
            return p.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// Her motion through the water from her way (blocks/s along the bow, + ahead) and leeway (blocks/s to starboard):
        /// her speed over ground, its direction relative to the bow (radians clockwise: 0 ahead, ±π astern, +π/2 sideways
        /// to starboard) and a word for it: "stopped", "ahead", "astern", or "sideways" when she goes more sideways than
        /// along her keel (drifting to leeward).
        /// </summary>
        public static string Motion(double speed, double leeway, out double over, out double course)
        {
            over = Math.Sqrt(speed * speed + leeway * leeway);
            course = Math.Atan2(leeway, speed);
            if (over < 0.05) return "stopped";
            if (Math.Abs(leeway) > Math.Abs(speed)) return "sideways";
            return speed > 0 ? "ahead" : "astern";
        }

        /// <summary>
        /// (0.3.12) The no-go wedge the wind panel draws: the bow directions (relative to her bow, radians clockwise as
        /// <see cref="Relative"/>) she can't point, centred on where the true wind comes from, the design's no-go angle
        /// (degrees) either side. She's in irons when it covers her bow (0).
        /// </summary>
        public static void NoGoWedge(double windX, double windZ, double yaw, double noGoDeg, out double from, out double to)
        {
            double centre = Relative(windX, windZ, yaw) + Math.PI;
            double half = noGoDeg * Math.PI / 180;
            from = centre - half;
            to = centre + half;
        }

        /// <summary>
        /// (after 0.1.0-dev.5) A direction relative to her bow (radians clockwise, as <see cref="Relative"/>) as the map draws
        /// it: radians from the map's +x (east) toward its +y (south, down the screen), what GL's RotateZ turns a mesh
        /// along +x by.
        /// </summary>
        public static double MapAngle(double rel, double yaw)
        {
            var b = Bow(yaw);
            var s = Starboard(yaw);
            double c = Math.Cos(rel), sn = Math.Sin(rel);
            return Math.Atan2(b.Z * c + s.Z * sn, b.X * c + s.X * sn);
        }

        /// <summary>
        /// (after 0.1.0-dev.5) A direction relative to her bow as the wind panel's card draws it (her bow up): cairo's angle,
        /// radians clockwise from the card's +x (right).
        /// </summary>
        public static double CardAngle(double rel) => rel - Math.PI / 2;

        /// <summary>
        /// (after 0.1.0-dev.5) The no-go wedge on the map: its centre (<see cref="MapAngle"/>) and its whole width (radians),
        /// the same wedge as the wind panel's (<see cref="CardNoGo"/>). The user (0.1.0-dev.5): "red wind zone does not
        /// match minimap and HUD": the map's fan had been built half as wide.
        /// </summary>
        public static void MapNoGo(double windX, double windZ, double yaw, double noGoDeg, out double centre, out double width)
        {
            NoGoWedge(windX, windZ, yaw, noGoDeg, out double from, out double to);
            centre = MapAngle((from + to) / 2, yaw);
            width = to - from;
        }

        /// <summary>(after 0.1.0-dev.5) The no-go wedge on the wind panel's card: cairo's angles (<see cref="CardAngle"/>) from and to.</summary>
        public static void CardNoGo(double windX, double windZ, double yaw, double noGoDeg, out double from, out double to)
        {
            NoGoWedge(windX, windZ, yaw, noGoDeg, out double f, out double t);
            from = CardAngle(f);
            to = CardAngle(t);
        }

        /// <summary>(after 0.1.0-dev.5) The map fan's whole width in whole degrees (its mesh is cached by it).</summary>
        public static int FanDegrees(double width) => Math.Clamp((int)Math.Round(width * 180 / Math.PI), 1, 359);

        /// <summary>Does the wedge (from..to, radians) cover this relative direction?</summary>
        public static bool InWedge(double rel, double from, double to)
        {
            double mid = (from + to) / 2, half = (to - from) / 2;
            return Math.Abs(Math.IEEERemainder(rel - mid, 2 * Math.PI)) < half;
        }

        /// <summary>Wind units (Sea's, vanilla's) to metres a second, and metres a second to knots.</summary>
        public static double MetresPerSecond(double strength) => strength * Sailing.WindUnit;
        public const double KnotsPerMetreSecond = 1.94384;

        /// <summary>True and apparent wind use knots only; b/s is reserved for the ship's own speed.</summary>
        public static string WindSpeed(double metresPerSecond) =>
            string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:0} kn", metresPerSecond * KnotsPerMetreSecond);
    }
}
