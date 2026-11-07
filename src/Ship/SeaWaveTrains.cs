using System;

namespace Hansa
{
    /// <summary>Sea's typed waves (<see cref="Sea.WavePlace"/>) as Ships' <see cref="WaveTrains"/>: the same numbers, copied.</summary>
    public static class SeaWaveTrains
    {
        /// <summary>
        /// Copy a sampled place's trains, its time and (for <see cref="WaveTrains.HeightAt(double, double, double)"/>)
        /// its own surface. <paramref name="height"/> is <c>place.HeightAt</c>, made once by a caller that copies often.
        /// </summary>
        public static WaveTrains CopyFrom(this WaveTrains into, Sea.WavePlace place, Func<double, double, double, double> height = null)
        {
            into.Clear(place.Time);
            int n = Math.Min(place.Count, WaveTrains.MaxWaves);
            Array.Copy(place.Kx, into.Kx, n);
            Array.Copy(place.Kz, into.Kz, n);
            Array.Copy(place.Omega, into.Omega, n);
            Array.Copy(place.Phase, into.Phase, n);
            Array.Copy(place.Amp, into.Amp, n);
            into.Count = n;
            into.SetSurface(height ?? place.HeightAt);
            return into;
        }

        /// <summary>A new <see cref="WaveTrains"/> with this place's trains (tests and reports).</summary>
        public static WaveTrains ToTrains(this Sea.WavePlace place) => new WaveTrains().CopyFrom(place);
    }
}
