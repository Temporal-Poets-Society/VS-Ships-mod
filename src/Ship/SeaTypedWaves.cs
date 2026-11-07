using System;

namespace Hansa
{
    /// <summary>
    /// Sea's typed API as a wave source: <see cref="Sea.SeaSystem.SampleWaves(Sea.WavePlace, double, double)"/> into a
    /// place of its own, copied into the ship's <see cref="WaveTrains"/>, and <see cref="Sea.SeaSystem.WaveFactorAt"/>
    /// for the shelter. The default whenever Sea is loaded: exactly what Ships read before the contracts.
    /// </summary>
    public sealed class SeaTypedWaves : IWaveTrainSource
    {
        public readonly Sea.SeaSystem SeaSys;
        readonly Sea.WavePlace place = new();
        readonly Func<double, double, double, double> height;

        public SeaTypedWaves(Sea.SeaSystem sea)
        {
            SeaSys = sea;
            height = place.HeightAt;
        }

        public bool Sample(WaveTrains into, double x, double z)
        {
            if (SeaSys.Spectrum == null) return false;
            SeaSys.SampleWaves(place, x, z);
            into.CopyFrom(place, height);
            return true;
        }

        public double Shelter(double x, double z) => SeaSys.WaveFactorAt(x, z);
    }
}
