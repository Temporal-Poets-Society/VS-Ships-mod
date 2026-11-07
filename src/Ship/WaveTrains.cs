using System;
using System.Collections.Generic;

namespace Hansa
{
    /// <summary>
    /// The wave trains running at a ship, as Ships reads them: whatever produces the sea (Sea's typed API, or the open
    /// sea contracts' <c>seacontracts:surface</c>) fills one of these, and <see cref="ShipMotion"/> works from it alone.
    /// Each train is the height <c>Amp · sin(Kx·x + Kz·z − Omega·t + Phase)</c> in blocks, before shelter.
    /// </summary>
    public sealed class WaveTrains
    {
        /// <summary>Room for this many trains (Sea's <c>WavePlace.MaxWaves</c>; the contract asks for at least 24).</summary>
        public const int MaxWaves = 24;

        /// <summary>Wave clock time the trains were sampled at.</summary>
        public double Time;
        /// <summary>How many of the arrays' entries are running.</summary>
        public int Count;
        public readonly double[] Kx = new double[MaxWaves], Kz = new double[MaxWaves], Omega = new double[MaxWaves],
            Phase = new double[MaxWaves], Amp = new double[MaxWaves];

        // the producer's own surface height (x, z, t), when it draws more than the plain sum (Sea's crest shaping)
        Func<double, double, double, double> surface;

        /// <summary>Significant height (4σ) of what's running here now, as Sea's <c>WavePlace.SignificantHeight</c>.</summary>
        public double SignificantHeight
        {
            get
            {
                double s = 0;
                for (int i = 0; i < Count; i++) s += Amp[i] * Amp[i];
                return 4 * Math.Sqrt(s / 2);
            }
        }

        /// <summary>Height of the open-sea surface above its rest level at a world position, at <see cref="Time"/>.</summary>
        public double HeightAt(double x, double z) => HeightAt(x, z, Time);

        /// <summary>
        /// The same at wave time t: the producer's own surface when it gave one (Sea's typed waves: exactly
        /// <c>WavePlace.HeightAt</c>), otherwise the contract's plain sum of the trains.
        /// </summary>
        public double HeightAt(double x, double z, double t)
        {
            var f = surface;
            if (f != null) return f(x, z, t);
            double y = 0;
            for (int i = 0; i < Count; i++) y += Amp[i] * Math.Sin(Kx[i] * x + Kz[i] * z - Omega[i] * t + Phase[i]);
            return y;
        }

        /// <summary>Use the producer's own surface (x, z, t) for <see cref="HeightAt(double, double, double)"/>; null: the plain sum.</summary>
        public void SetSurface(Func<double, double, double, double> height) => surface = height;

        /// <summary>No trains (calm), at wave time t; the plain sum from here on.</summary>
        public void Clear(double t)
        {
            Time = t;
            Count = 0;
            surface = null;
        }

        /// <summary>Take exactly these trains at wave time t (the plain sum for the surface).</summary>
        public void Set(double t, int count, double[] kx, double[] kz, double[] omega, double[] phase, double[] amp)
        {
            Clear(t);
            int n = Math.Clamp(count, 0, MaxWaves);
            for (int i = 0; i < n; i++)
            {
                Kx[i] = kx[i]; Kz[i] = kz[i]; Omega[i] = omega[i]; Phase[i] = phase[i]; Amp[i] = amp[i];
            }
            Count = n;
        }
    }

    /// <summary>Where a ship's waves come from (picked per sample by <see cref="EntityShip"/>).</summary>
    public interface IWaveTrainSource
    {
        /// <summary>
        /// Fill <paramref name="into"/> with the trains at (x, z) now. False: this source has no waves to give now
        /// (<paramref name="into"/> may be left as it was); the ship rides calm water for that sample.
        /// </summary>
        bool Sample(WaveTrains into, double x, double z);

        /// <summary>The share of the open sea's waves that reaches (x, z), 0–1.</summary>
        double Shelter(double x, double z);
    }

    /// <summary>
    /// The open sea contracts as a wave source (Sea's docs/open-sea-contracts.md, sections 3, 6, 7, 9): the trains from
    /// <c>seacontracts:surface</c>'s <c>trains</c> at its <c>seconds</c>, the shelter from <c>seacontracts:seastate</c>'s
    /// <c>shelter</c>. Only .NET base types; nothing of the producer's DLL. Entries are looked up afresh on every call
    /// (never kept across a <c>revision</c> change, or at all). An exception, NaN or infinity is no answer (R7): calm
    /// water for that call, and one line in the log.
    /// </summary>
    public sealed class ContractWaves : IWaveTrainSource
    {
        public const string SurfaceKey = "seacontracts:surface", SeaStateKey = "seacontracts:seastate";

        readonly IDictionary<string, object> cache;
        readonly Action<string> warn;
        static int warned;

        /// <param name="cache">the side's <c>api.ObjectCache</c></param>
        /// <param name="warn">where the one warning goes</param>
        public ContractWaves(IDictionary<string, object> cache, Action<string> warn)
        {
            this.cache = cache;
            this.warn = warn;
        }

        /// <summary>Is there a surface with trains on this side now?</summary>
        public bool Available => Trains(out _) != null;

        IDictionary<string, object> Entry(string key)
        {
            if (cache == null) return null;
            lock (cache) return cache.TryGetValue(key, out var o) ? o as IDictionary<string, object> : null;
        }

        static T Member<T>(IDictionary<string, object> entry, string name) where T : class =>
            entry != null && entry.TryGetValue(name, out var o) ? o as T : null;

        Func<double, double, double, double[], double[], double[], double[], double[], int> Trains(out Func<double> seconds)
        {
            var e = Entry(SurfaceKey);
            seconds = Member<Func<double>>(e, "seconds");
            var trains = Member<Func<double, double, double, double[], double[], double[], double[], double[], int>>(e, "trains");
            return seconds != null ? trains : null;
        }

        public bool Sample(WaveTrains into, double x, double z)
        {
            var trains = Trains(out var seconds);
            if (trains == null) return false;
            double t = 0;
            try
            {
                t = seconds();
                if (!double.IsFinite(t)) { Fail("surface.seconds gave " + t); into.Clear(0); return false; }
                into.Clear(t);
                int n = trains(x, z, t, into.Kx, into.Kz, into.Omega, into.Phase, into.Amp);
                n = Math.Clamp(n, 0, WaveTrains.MaxWaves);
                for (int i = 0; i < n; i++)
                    if (!double.IsFinite(into.Kx[i]) || !double.IsFinite(into.Kz[i]) || !double.IsFinite(into.Omega[i]) ||
                        !double.IsFinite(into.Phase[i]) || !double.IsFinite(into.Amp[i]))
                    { Fail("surface.trains gave a non-finite train"); into.Clear(t); return false; }
                into.Count = n;
                return true;
            }
            catch (Exception ex)
            {
                Fail("surface failed: " + ex.Message);
                into.Clear(t);
                return false;
            }
        }

        public double Shelter(double x, double z)
        {
            var shelter = Member<Func<double, double, double>>(Entry(SeaStateKey), "shelter");
            // (a surface owner without a sea state: its trains are the open sea's, all of them reach here)
            if (shelter == null) return 1;
            try
            {
                double s = shelter(x, z);
                if (double.IsFinite(s)) return Math.Clamp(s, 0, 1);
                Fail("seastate.shelter gave " + s);
            }
            catch (Exception ex) { Fail("seastate.shelter failed: " + ex.Message); }
            return 0;
        }

        void Fail(string what)
        {
            if (System.Threading.Interlocked.Exchange(ref warned, 1) == 0) warn?.Invoke("[ships] sea contracts: " + what + "; calm water for it (logged once)");
        }
    }
}
