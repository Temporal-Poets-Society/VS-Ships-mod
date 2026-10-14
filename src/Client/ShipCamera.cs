using System;

namespace Hansa
{
    /// <summary>
    /// The local player's view on a moving deck. The deck's tilt along where you look goes into your view pitch (as
    /// a change, like vanilla's rocking mounts, so the mouse still aims where you look), the tilt across it rolls the
    /// view about its own axis (so the crosshair stays on what it's on). A Harmony postfix on the game's camera
    /// applies <see cref="Roll"/> to the view and the frustum it culls with.
    /// </summary>
    public static class ShipCamera
    {
        /// <summary>Radians the view is rolled this frame (+ = your head tipped right, so the world turns anticlockwise).</summary>
        public static double Roll;

        /// <summary>
        /// How a deck tilted by (pitch: bow up +, roll: starboard down +) tips a view looking relYaw from the bow
        /// (relYaw = look yaw − ship yaw): lookUp (+ = the deck rises ahead, you look up), headRoll (+ = your right
        /// side goes down).
        /// </summary>
        public static void ViewTilt(double pitch, double roll, double relYaw, out double lookUp, out double headRoll)
        {
            double c = Math.Cos(relYaw), s = Math.Sin(relYaw);
            lookUp = pitch * c + roll * s;
            headRoll = roll * c - pitch * s;
        }

        /// <summary>m ← Rz(roll) · m: turn a view matrix (column-major) about the view axis.</summary>
        public static void ApplyRoll(double[] m, double roll)
        {
            if (m == null || roll == 0) return;
            double c = Math.Cos(roll), s = Math.Sin(roll);
            for (int col = 0; col < 4; col++)
            {
                double m0 = m[col * 4], m1 = m[col * 4 + 1];
                m[col * 4] = c * m0 - s * m1;
                m[col * 4 + 1] = s * m0 + c * m1;
            }
        }
    }
}
