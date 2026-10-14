using System;

namespace Hansa
{
    /// <summary>Rowing a boat, as pure functions (the server's and the offline test's).</summary>
    public static class Oars
    {
        /// <summary>
        /// The speed the rowers drive her to. pulling: the rowers' strokes together, +1 for each one pulling, -1 for each
        /// one backing. Two rowers go √2 as fast as one (twice the push against water resistance rising with the square
        /// of the speed).
        /// </summary>
        public static float Target(int pulling, float rowSpeed)
        {
            if (pulling == 0 || rowSpeed <= 0) return 0;
            return Math.Sign(pulling) * rowSpeed * (float)Math.Sqrt(Math.Abs(pulling));
        }

        /// <summary>Oars and sail together: the drives add up, to her top speed at most (the oars alone may go past it astern).</summary>
        public static float Combine(float sail, float row, float maxSpeed)
        {
            float t = sail + row;
            return Math.Clamp(t, -Math.Max(maxSpeed, Math.Abs(row)), Math.Max(maxSpeed, row));
        }
    }
}
