using System;

namespace Hansa
{
    /// <summary>
    /// Wind at a place, as Ships reads it: the air moves toward (X, Z) in world axes (+X east, +Z south), its length the
    /// strength in vanilla's wind units (1 = <see cref="Beaufort.MetersPerSecondPerUnit"/> m/s). Ships' own copy of Sea's
    /// <c>Sea.Wind</c> (the same fields and arithmetic), so the main assembly needs nothing of Sea.dll: with Sea it is
    /// Sea's wind copied, without it the open sea contracts' or vanilla's (<see cref="HansaMod.WindHere"/>).
    /// </summary>
    public readonly struct Wind
    {
        public readonly double X, Z;

        public Wind(double x, double z) { X = x; Z = z; }

        public double Strength => Math.Sqrt(X * X + Z * Z);

        /// <summary>Compass bearing the wind blows from: 0 north, 90 east, 180 south, 270 west.</summary>
        public double FromDegrees => WindCompass.Bearing(-X, -Z);

        /// <summary>Compass bearing the wind blows toward.</summary>
        public double ToDegrees => WindCompass.Bearing(X, Z);

        public static Wind FromCompass(double fromDegrees, double strength)
        {
            double to = (fromDegrees + 180) * Math.PI / 180;
            return new Wind(Math.Sin(to) * strength, -Math.Cos(to) * strength);
        }

        public override string ToString() => $"{Strength:0.00} from {WindCompass.Name(FromDegrees)} ({FromDegrees:0}°)";
    }

    /// <summary>Compass bearings and their 16 point names (north = -Z), as Sea's <c>Sea.Compass</c>.</summary>
    public static class WindCompass
    {
        static readonly string[] Points = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

        /// <summary>Compass bearing of a direction in world axes (north = -Z).</summary>
        public static double Bearing(double x, double z)
        {
            if (x * x + z * z < 1e-18) return 0;
            double deg = Math.Atan2(x, -z) * 180 / Math.PI;
            return deg < 0 ? deg + 360 : deg;
        }

        public static string Name(double bearing) => Points[(int)Math.Round((bearing - 360 * Math.Floor(bearing / 360)) / 22.5) % 16];
    }

    /// <summary>The Beaufort scale on vanilla's wind units, as Sea's <c>Sea.Beaufort</c>.</summary>
    public static class Beaufort
    {
        /// <summary>Metres per second for one vanilla wind unit (vanilla's "storm" ≈ Beaufort 10).</summary>
        public const double MetersPerSecondPerUnit = 25;

        static readonly double[] Upper = { 0.5, 1.6, 3.4, 5.5, 8.0, 10.8, 13.9, 17.2, 20.8, 24.5, 28.5, 32.7 };
        static readonly string[] Names =
        {
            "calm", "light air", "light breeze", "gentle breeze", "moderate breeze", "fresh breeze", "strong breeze",
            "near gale", "gale", "strong gale", "storm", "violent storm", "hurricane"
        };

        public static int Force(double strength)
        {
            double ms = strength * MetersPerSecondPerUnit;
            int f = 0;
            while (f < Upper.Length && ms >= Upper[f]) f++;
            return f;
        }

        public static string Name(double strength) => Names[Force(strength)];
    }
}
