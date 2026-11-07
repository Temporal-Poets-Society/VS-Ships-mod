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

        /// <summary>
        /// (Sea 0.6.0, surf) Near a coast: how far the drawn surface stands above the open sea's waves at (x, z) now, in
        /// blocks, shelter included (the beach's surf: trains turning shoreward, shoaling and breaking over the bottom). Null
        /// off the coast, where the trains are the whole of it; <see cref="ShipMotion.Surf"/> fits a plane to it.
        /// </summary>
        public Func<double, double, double> Surf;

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

        /// <summary>The plain sum of the trains at (x, z) at <see cref="Time"/>, before shelter: the open sea as the contract's trains give it.</summary>
        public double OpenHeightAt(double x, double z)
        {
            double y = 0, t = Time;
            for (int i = 0; i < Count; i++) y += Amp[i] * Math.Sin(Kx[i] * x + Kz[i] * z - Omega[i] * t + Phase[i]);
            return y;
        }

        /// <summary>Use the producer's own surface (x, z, t) for <see cref="HeightAt(double, double, double)"/>; null: the plain sum.</summary>
        public void SetSurface(Func<double, double, double, double> height) => surface = height;

        /// <summary>No trains (calm), at wave time t; the plain sum from here on, and no surf.</summary>
        public void Clear(double t)
        {
            Time = t;
            Count = 0;
            surface = null;
            Surf = null;
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

        /// <summary>
        /// (Sea 0.6.0) The water's own drift at (x, z) now, m/s (+X east, +Z south): the longshore current in a beach's surf
        /// zone; (0, 0) elsewhere or when the source doesn't know one.
        /// </summary>
        (double X, double Z) Current(double x, double z) => (0, 0);
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
        public const string SurfaceKey = "seacontracts:surface", SeaStateKey = "seacontracts:seastate", WeatherKey = "seacontracts:weather";
        /// <summary>The contract's depth off the coast (<c>seastate.surf</c>: 32 blocks or more there).</summary>
        public const double OpenDepth = 32;

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
                SurfOver(into, x, z);
                return true;
            }
            catch (Exception ex)
            {
                Fail("surface failed: " + ex.Message);
                into.Clear(t);
                return false;
            }
        }

        /// <summary>
        /// (Sea 0.6.0) Over coastal water (<c>seastate.surf</c> shallower than <see cref="OpenDepth"/>), or in the lee of land
        /// (<c>seastate.lee</c> under 1, deep water too), with a drawn surface (<c>surface.height</c>): the surf is the drawn
        /// height less the trains' plain sum under the shelter here.
        /// </summary>
        bool InLee(double x, double z)
        {
            var lee = Member<Func<double, double, double>>(Entry(SeaStateKey), "lee");
            if (lee == null) return false;
            try { return lee(x, z) < 0.999; }
            catch (Exception ex) { Fail("seastate.lee failed: " + ex.Message); return false; }
        }

        void SurfOver(WaveTrains into, double x, double z)
        {
            var height = Member<Func<double, double, double>>(Entry(SurfaceKey), "height");
            var surf = Member<Func<double, double, (double Depth, double Breaking, double Foam)>>(Entry(SeaStateKey), "surf");
            if (height == null || surf == null) return;
            double depth;
            try { depth = surf(x, z).Depth; }
            catch (Exception ex) { Fail("seastate.surf failed: " + ex.Message); return; }
            if (!(depth < OpenDepth) && !InLee(x, z)) return;     // (NaN too: no answer, the open sea's waves alone)
            double shelter = Shelter(x, z);
            into.Surf = (px, pz) =>
            {
                try
                {
                    double d = height(px, pz) - shelter * into.OpenHeightAt(px, pz);
                    if (double.IsFinite(d)) return d;
                    Fail("surface.height gave " + d);
                }
                catch (Exception ex) { Fail("surface.height failed: " + ex.Message); }
                return double.NaN;
            };
        }

        /// <summary>(Sea 0.6.0) <c>seastate.current</c> at (x, z) now, m/s; (0, 0) when there is none or no answer.</summary>
        public (double X, double Z) Current(double x, double z)
        {
            var current = Member<Func<double, double, (double X, double Z)>>(Entry(SeaStateKey), "current");
            if (current == null) return (0, 0);
            try
            {
                var v = current(x, z);
                if (double.IsFinite(v.X) && double.IsFinite(v.Z)) return v;
                Fail("seastate.current gave " + v);
            }
            catch (Exception ex) { Fail("seastate.current failed: " + ex.Message); }
            return (0, 0);
        }

        /// <summary>Is there a <c>surface.pose</c> on this side (for a surface owner that gives no trains)?</summary>
        public bool HasPose => Member<Func<double, double, double, double, double, (double Hold, double Lift, double Roll, double Pitch)>>(Entry(SurfaceKey), "pose") != null;

        /// <summary>
        /// <c>surface.pose</c>: how a hull of length by beam centred at (x, z) lies on the drawn water now, in Rough Seas'
        /// <c>PoseOf</c> meaning (yaw as the renderer turns a shape whose length runs along its X axis). False: none, or no answer.
        /// </summary>
        public bool Pose(double x, double z, double yaw, double length, double beam, out (double Hold, double Lift, double Roll, double Pitch) pose)
        {
            pose = default;
            var f = Member<Func<double, double, double, double, double, (double Hold, double Lift, double Roll, double Pitch)>>(Entry(SurfaceKey), "pose");
            if (f == null) return false;
            try
            {
                pose = f(x, z, yaw, length, beam);
                if (double.IsFinite(pose.Hold) && double.IsFinite(pose.Lift) && double.IsFinite(pose.Roll) && double.IsFinite(pose.Pitch)) return true;
                Fail("surface.pose gave " + pose);
            }
            catch (Exception ex) { Fail("surface.pose failed: " + ex.Message); }
            pose = default;
            return false;
        }

        /// <summary><c>surface.height</c>: the drawn surface above still water at (x, z) now (m, shelter included); 0 without one.</summary>
        public double Height(double x, double z)
        {
            var f = Member<Func<double, double, double>>(Entry(SurfaceKey), "height");
            if (f == null) return 0;
            try
            {
                double h = f(x, z);
                if (double.IsFinite(h)) return h;
                Fail("surface.height gave " + h);
            }
            catch (Exception ex) { Fail("surface.height failed: " + ex.Message); }
            return 0;
        }

        /// <summary>Is there a <c>seacontracts:weather</c> wind on this side?</summary>
        public bool HasWind => Member<Func<double, double, double, bool, (double X, double Z)>>(Entry(WeatherKey), "wind") != null;

        /// <summary>
        /// <c>weather.wind</c> with its gusts at (x, z) and total game hours, m/s toward (X, Z); false: none, or no answer
        /// (calm).
        /// </summary>
        public bool Wind(double x, double z, double hours, out double windX, out double windZ)
        {
            windX = windZ = 0;
            var f = Member<Func<double, double, double, bool, (double X, double Z)>>(Entry(WeatherKey), "wind");
            if (f == null) return false;
            try
            {
                var v = f(x, z, hours, true);
                if (double.IsFinite(v.X) && double.IsFinite(v.Z)) { windX = v.X; windZ = v.Z; return true; }
                Fail("weather.wind gave " + v);
            }
            catch (Exception ex) { Fail("weather.wind failed: " + ex.Message); }
            return false;
        }

        /// <summary>
        /// <c>seastate.at</c>: the open sea at (x, z) and total game hours: significant height (m), peak period (s); false:
        /// none, or no answer.
        /// </summary>
        public bool SeaState(double x, double z, double hours, out double height, out double period)
        {
            height = period = 0;
            var f = Member<Func<double, double, double, (double Height, double Period, double DirX, double DirZ, double WindX, double WindZ, double Storm)>>(Entry(SeaStateKey), "at");
            if (f == null) return false;
            try
            {
                var st = f(x, z, hours);
                if (double.IsFinite(st.Height) && double.IsFinite(st.Period)) { height = st.Height; period = st.Period; return true; }
                Fail("seastate.at gave " + st);
            }
            catch (Exception ex) { Fail("seastate.at failed: " + ex.Message); }
            return false;
        }

        /// <summary>Is there a <c>seacontracts:seastate</c> with the sea's own wind (<c>at</c>) on this side?</summary>
        public bool HasSeaWind => Member<Func<double, double, double, (double Height, double Period, double DirX, double DirZ, double WindX, double WindZ, double Storm)>>(Entry(SeaStateKey), "at") != null;

        /// <summary>
        /// <c>seastate.at</c>'s wind at (x, z) and total game hours, m/s toward (X, Z): the wind the sea owner raises its
        /// waves by (Sea's own wind model when no mod publishes the weather). False: none, or no answer.
        /// </summary>
        public bool SeaWind(double x, double z, double hours, out double windX, out double windZ)
        {
            windX = windZ = 0;
            var f = Member<Func<double, double, double, (double Height, double Period, double DirX, double DirZ, double WindX, double WindZ, double Storm)>>(Entry(SeaStateKey), "at");
            if (f == null) return false;
            try
            {
                var st = f(x, z, hours);
                if (double.IsFinite(st.WindX) && double.IsFinite(st.WindZ)) { windX = st.WindX; windZ = st.WindZ; return true; }
                Fail("seastate.at gave the wind " + (st.WindX, st.WindZ));
            }
            catch (Exception ex) { Fail("seastate.at failed: " + ex.Message); }
            return false;
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
            if (System.Threading.Interlocked.Exchange(ref warned, 1) == 0) warn?.Invoke("sea contracts: " + what + "; calm water for it (logged once)");
        }
    }
}
