using System;
using System.Collections.Generic;
using Hansa;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ShipsSea
{
    /// <summary>
    /// Ships' bridge to the Sea mod (ShipsSea.dll, shipped in Ships' <c>native/</c> folder). The only assembly of Ships
    /// that references Sea.dll: it implements Ships' <see cref="ISeaLink"/> with Sea's typed API, doing exactly what Ships
    /// did with Sea's types before the bridge. Ships loads it by reflection (<see cref="SeaBridgeLoader"/>) only when Sea is
    /// installed and new enough, and calls <see cref="Bind"/> once per side.
    /// </summary>
    public static class SeaBridge
    {
        /// <summary>Sea's typed API on this side, or null when Sea's mod system isn't running here.</summary>
        public static ISeaLink Bind(ICoreAPI api)
        {
            var sea = api?.ModLoader?.GetModSystem<Sea.SeaSystem>();
            return sea == null ? null : new SeaLink(api, sea);
        }
    }

    sealed class SeaLink : ISeaLink
    {
        readonly ICoreAPI api;
        readonly Sea.SeaSystem sea;

        public SeaLink(ICoreAPI api, Sea.SeaSystem sea)
        {
            this.api = api;
            this.sea = sea;
        }

        public string SeaVersion => api.ModLoader.GetMod("sea")?.Info?.Version;

        public Wind WindAt(double x, double z) { var w = sea.WindAt(x, z); return new Wind(w.X, w.Z); }
        public double WaveHeightAt(double x, double z) => sea.WaveHeightAt(x, z);
        public double WaveFactorAt(double x, double z) => sea.WaveFactorAt(x, z);
        public (double Height, double Period, double Wavelength) SeaAt(double x, double z)
        {
            var s = sea.SeaAt(x, z);
            return (s.Height, s.Period, s.Wavelength);
        }
        public bool IsDry(double x, double y, double z) => sea.IsDry(x, y, z);
        public bool IsDryWithin(double x, double y, double z, double seconds) => sea.IsDryWithin(x, y, z, seconds);

        public IWaveTrainSource NewWaveSource() => new SeaTypedWaves(sea);

        public IDisposable AddDrySpaces(ShipDrySpaces dry)
        {
            var d = new DryAdapter(dry);
            sea.AddDrySpaces(d);
            return new Undo(() => sea.RemoveDrySpaces(d));
        }

        public IDisposable AddWaterClipSource(Action<List<HullWaterClip>> addClips)
        {
            var s = new ClipSource(addClips);
            sea.AddWaterClipSource(s);
            return new Undo(() => sea.RemoveWaterClipSource(s));
        }

        public void ApplyBoatPose(EntityBoat boat, ref HullWaterClip clip)
        {
            var c = clip.ToSea();
            Sea.BoatWaves.ApplyToClip(boat, ref c);
            clip = c.ToShips();
        }

        public float CalmZone => Sea.WaterShader.CalmZone;
        public string DryOutlineUniform => Sea.WaterShader.DryOutlineUniform;
        public string RendererReport() =>
            FormattableString.Invariant($"Sea: water injected={Sea.WaterRenderer.Injected}, waves injected={Sea.WaterRenderer.WavesInjected}, dry injected={Sea.Hooks.DryHullsInjected}, fallback level={Sea.Hooks.WaterCodeLevel}");
        public (bool Detailed, bool Inside) CutAt(HullWaterClip clip, double x, double y, double z)
        {
            var c = clip.ToSea();
            return (c.HasDryProfile, c.InDryHull(x, y, z));
        }

        public bool ModelReady => sea.Model != null;
        public bool SpectrumReady => sea.Spectrum != null;
        public double WaveTime => sea.WaveTime;
        public void SetWindOverride(double fromDegrees, double strength) =>
            sea.SetOverride(new Sea.WindOverride { FromDegrees = fromDegrees, Strength = strength, StartHours = sea.Hours - 24 });
        public void ClearWindOverride() => sea.SetOverride(null);
        public ISeaReplay Replay(double x, double z) => new Replayed(sea, x, z);

        sealed class DryAdapter : Sea.IDrySpaces
        {
            readonly ShipDrySpaces dry;
            public DryAdapter(ShipDrySpaces dry) { this.dry = dry; }
            public bool IsDry(double x, double y, double z) => dry.IsDry(x, y, z);
            public bool IsDryWithin(double x, double y, double z, double seconds) => dry.IsDryWithin(x, y, z, seconds);
        }

        sealed class ClipSource : Sea.IWaterClipSource
        {
            readonly Action<List<HullWaterClip>> addClips;
            readonly List<HullWaterClip> buffer = new();
            public ClipSource(Action<List<HullWaterClip>> addClips) { this.addClips = addClips; }
            public void AddWaterClips(List<Sea.WaterClipBox> clips)
            {
                buffer.Clear();
                try { addClips(buffer); }
                finally { foreach (var c in buffer) clips.Add(c.ToSea()); }
            }
        }

        /// <summary>A place's sea state and wind taken once, its waves replayed at any time (the self-tests' storm).</summary>
        sealed class Replayed : ISeaReplay
        {
            readonly Sea.SeaSystem sea;
            readonly Sea.SeaState state;
            readonly Sea.Wind wind;
            readonly Sea.WavePlace place = new();
            readonly WaveTrains trains = new();
            public System.Func<double, double, double> Surface { get; }

            public Replayed(Sea.SeaSystem sea, double x, double z)
            {
                this.sea = sea;
                state = sea.SeaAt(x, z);
                wind = sea.WindAt(x, z);
                Surface = place.HeightAt;
            }

            public WaveTrains At(double t)
            {
                place.Update(sea.Spectrum, t, state, wind);
                return trains.CopyFrom(place);
            }
        }

        sealed class Undo : IDisposable
        {
            Action undo;
            public Undo(Action undo) { this.undo = undo; }
            public void Dispose() { var u = undo; undo = null; u?.Invoke(); }
        }
    }
}
