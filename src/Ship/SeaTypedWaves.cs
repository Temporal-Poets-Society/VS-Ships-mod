using System;

namespace Hansa
{
    /// <summary>
    /// Sea's typed API as a wave source: <see cref="Sea.SeaSystem.SampleWaves(Sea.WavePlace, double, double)"/> into a
    /// place of its own, copied into the ship's <see cref="WaveTrains"/>, and <see cref="Sea.SeaSystem.WaveFactorAt"/>
    /// for the shelter. The default whenever Sea is loaded: exactly what Ships read before the contracts.
    /// (Sea 0.6.0) Over coastal water and in the lee of land (<see cref="Sea.SeaSystem.CoastAt"/>) the trains also carry
    /// what the coast does to the waves, and <see cref="Current"/> is Sea's surf current there (longshore and rips).
    /// </summary>
    public sealed class SeaTypedWaves : IWaveTrainSource
    {
        public readonly Sea.SeaSystem SeaSys;
        readonly Sea.WavePlace place = new();
        readonly Func<double, double, double, double> height;
        readonly Func<double, double, double> surf;
        double surfShelter;
        // where and when the place was last sampled (Current reads it there rather than sampling again)
        double sampledX = double.NaN, sampledZ = double.NaN, sampledT = double.NaN;

        public SeaTypedWaves(Sea.SeaSystem sea)
        {
            SeaSys = sea;
            height = place.HeightAt;
            surf = SurfAt;
        }

        public bool Sample(WaveTrains into, double x, double z)
        {
            if (SeaSys.Spectrum == null) return false;
            SeaSys.SampleWaves(place, x, z);
            sampledX = x; sampledZ = z; sampledT = place.Time;
            into.CopyFrom(place, height);
            // (0.6.0) the beach's surf, only over coastal water or in the lee of land: elsewhere nothing more is worked out
            if (place.Coast != null && SeaSys.CoastAt(x, z).Affects)
            {
                surfShelter = SeaSys.WaveFactorAt(x, z);
                into.Surf = surf;
            }
            return true;
        }

        /// <summary>
        /// The drawn surface (the place's own, with the coast under it) less the same surface without the coast: what the
        /// beach adds to the open sea's waves (crest shaping alike in both), under the shelter at the sampled place.
        /// </summary>
        double SurfAt(double x, double z)
        {
            double drawn = place.HeightAt(x, z);
            var coast = place.Coast;
            double open;
            place.Coast = null;
            try { open = place.HeightAt(x, z); }
            finally { place.Coast = coast; }
            return (drawn - open) * surfShelter;
        }

        public double Shelter(double x, double z) => SeaSys.WaveFactorAt(x, z);

        /// <summary>(0.6.0) Sea's longshore current at (x, z) now (m/s), from this source's place sampled there; none off the coast.</summary>
        public (double X, double Z) Current(double x, double z)
        {
            if (SeaSys.Spectrum == null || !SeaSys.CoastAt(x, z).Coastal) return (0, 0);
            if (x != sampledX || z != sampledZ || SeaSys.WaveTime != sampledT)
            {
                SeaSys.SampleWaves(place, x, z);
                sampledX = x; sampledZ = z; sampledT = place.Time;
            }
            var v = SeaSys.CurrentAt(place, x, z);
            return double.IsFinite(v.X) && double.IsFinite(v.Z) ? v : (0, 0);
        }
    }
}
