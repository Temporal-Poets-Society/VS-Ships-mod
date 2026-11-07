using System;

namespace Hansa
{
    /// <summary>
    /// A hull footprint where water must stay below the waterline, in world coordinates: Ships' own copy of Sea's
    /// <c>Sea.WaterClipBox</c>, field for field (the bridge copies one into the other; see that type for the meaning of
    /// each). <see cref="ShipWater.Clip"/> makes one for a ship, <see cref="EntityRowBoat.WaterClip"/> for a rowing boat.
    /// Without Sea a <c>seacontracts:hulls</c> owner gets it as a hull record (<see cref="ContractHulls.ToRecord"/>), and
    /// otherwise Ships hides the water inside with a lid of its own (<see cref="ShipWaterLids"/>).
    /// </summary>
    public struct HullWaterClip
    {
        public double X, Z;
        /// <summary>Highest the water may be drawn inside the footprint at (X, Z) (world y).</summary>
        public double WaterY;
        public double Yaw;
        public float HalfLength, CentreOffset;
        /// <summary>Half-widths at 16 evenly spaced stations from stern to bow; null = HalfWidth everywhere.</summary>
        public float[] Profile;
        public float HalfWidth;
        public float SlopeAlong, SlopeAcross;
        public float CalmSlopeAcross;
        public float CalmZone;
        public bool Dry;
        public float DryInset, DryDepth, DryHeight;
        public float[] DryProfile;
        public float DryLevel0, DryLevelStep;
        public float DryRake;
        public bool Flooded;
        public double FloodY;

        /// <summary>The footprint's half-width at u along it (-1 stern .. 1 bow), as Sea's shader interpolates it.</summary>
        public double HalfWidthAt(double u)
        {
            if (Profile == null || Profile.Length != WaterClipDef.Stations) return HalfWidth;
            double s = Math.Clamp((u + 1) * 7.5, 0, 15);
            int i = (int)Math.Floor(s), j = Math.Min(i + 1, 15);
            return Profile[i] + (Profile[j] - Profile[i]) * (s - i);
        }
    }
}
