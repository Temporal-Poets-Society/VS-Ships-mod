namespace Hansa
{
    /// <summary>
    /// Ships' <see cref="HullWaterClip"/> and Sea's <see cref="Sea.WaterClipBox"/>, field for field (they are the same
    /// struct in two assemblies): what Ships computes is what Sea's shader gets, unchanged.
    /// </summary>
    public static class SeaClips
    {
        public static Sea.WaterClipBox ToSea(this HullWaterClip c) => new Sea.WaterClipBox
        {
            X = c.X, Z = c.Z, WaterY = c.WaterY, Yaw = c.Yaw,
            HalfLength = c.HalfLength, CentreOffset = c.CentreOffset, Profile = c.Profile, HalfWidth = c.HalfWidth,
            SlopeAlong = c.SlopeAlong, SlopeAcross = c.SlopeAcross, CalmSlopeAcross = c.CalmSlopeAcross, CalmZone = c.CalmZone,
            Dry = c.Dry, DryInset = c.DryInset, DryDepth = c.DryDepth, DryHeight = c.DryHeight,
            DryProfile = c.DryProfile, DryLevel0 = c.DryLevel0, DryLevelStep = c.DryLevelStep, DryRake = c.DryRake,
            Flooded = c.Flooded, FloodY = c.FloodY,
        };

        public static HullWaterClip ToShips(this Sea.WaterClipBox c) => new HullWaterClip
        {
            X = c.X, Z = c.Z, WaterY = c.WaterY, Yaw = c.Yaw,
            HalfLength = c.HalfLength, CentreOffset = c.CentreOffset, Profile = c.Profile, HalfWidth = c.HalfWidth,
            SlopeAlong = c.SlopeAlong, SlopeAcross = c.SlopeAcross, CalmSlopeAcross = c.CalmSlopeAcross, CalmZone = c.CalmZone,
            Dry = c.Dry, DryInset = c.DryInset, DryDepth = c.DryDepth, DryHeight = c.DryHeight,
            DryProfile = c.DryProfile, DryLevel0 = c.DryLevel0, DryLevelStep = c.DryLevelStep, DryRake = c.DryRake,
            Flooded = c.Flooded, FloodY = c.FloodY,
        };
    }
}
