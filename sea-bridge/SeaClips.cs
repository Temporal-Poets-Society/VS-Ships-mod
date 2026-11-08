using System;
using System.Runtime.CompilerServices;

namespace Hansa
{
    /// <summary>
    /// Ships' <see cref="HullWaterClip"/> and Sea's <see cref="Sea.WaterClipBox"/>, field for field (they are the same
    /// struct in two assemblies): what Ships computes is what Sea's shader gets, unchanged.
    /// </summary>
    public static class SeaClips
    {
        /// <summary>
        /// (8 November 2026) Does the bound Sea have the dry outline's tops (<c>WaterClipBox.DryTops</c>)? A Sea from before
        /// them has no such field: the bridge then leaves them out (the cut reaches DryHeight all along, as before).
        /// </summary>
        static bool tops = true;

        public static Sea.WaterClipBox ToSea(this HullWaterClip c)
        {
            var b = new Sea.WaterClipBox
            {
                X = c.X, Z = c.Z, WaterY = c.WaterY, Yaw = c.Yaw,
                HalfLength = c.HalfLength, CentreOffset = c.CentreOffset, Profile = c.Profile, HalfWidth = c.HalfWidth,
                SlopeAlong = c.SlopeAlong, SlopeAcross = c.SlopeAcross, CalmSlopeAcross = c.CalmSlopeAcross, CalmZone = c.CalmZone,
                Dry = c.Dry, DryInset = c.DryInset, DryDepth = c.DryDepth, DryHeight = c.DryHeight,
                DryProfile = c.DryProfile, DryLevel0 = c.DryLevel0, DryLevelStep = c.DryLevelStep, DryRake = c.DryRake,
                Flooded = c.Flooded, FloodY = c.FloodY,
            };
            if (c.DryTops != null && tops)
            {
                try { SetTops(ref b, c.DryTops); }
                catch (MissingFieldException) { tops = false; }
            }
            return b;
        }

        public static HullWaterClip ToShips(this Sea.WaterClipBox c)
        {
            var h = new HullWaterClip
            {
                X = c.X, Z = c.Z, WaterY = c.WaterY, Yaw = c.Yaw,
                HalfLength = c.HalfLength, CentreOffset = c.CentreOffset, Profile = c.Profile, HalfWidth = c.HalfWidth,
                SlopeAlong = c.SlopeAlong, SlopeAcross = c.SlopeAcross, CalmSlopeAcross = c.CalmSlopeAcross, CalmZone = c.CalmZone,
                Dry = c.Dry, DryInset = c.DryInset, DryDepth = c.DryDepth, DryHeight = c.DryHeight,
                DryProfile = c.DryProfile, DryLevel0 = c.DryLevel0, DryLevelStep = c.DryLevelStep, DryRake = c.DryRake,
                Flooded = c.Flooded, FloodY = c.FloodY,
            };
            if (tops)
            {
                try { h.DryTops = GetTops(c); }
                catch (MissingFieldException) { tops = false; }
            }
            return h;
        }

        // (kept apart, so a Sea without the field fails only here, when the method is compiled, not the whole copy)
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void SetTops(ref Sea.WaterClipBox b, float[] t) => b.DryTops = t;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float[] GetTops(in Sea.WaterClipBox c) => c.DryTops;
    }
}
