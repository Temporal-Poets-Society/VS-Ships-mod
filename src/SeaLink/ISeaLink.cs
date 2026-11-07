using System;
using System.Collections.Generic;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// What Ships uses of the Sea mod's typed API, in Ships' own types. Implemented by the bridge assembly ShipsSea.dll
    /// (<c>sea-bridge/</c>, shipped as <c>native/ShipsSea.dll</c>), which references Sea.dll and is loaded by
    /// <see cref="SeaBridgeLoader"/> only when Sea is installed (and new enough). The main Ships assembly has no reference
    /// to Sea.dll: without Sea it reads the open sea contracts (<see cref="ContractWaves"/>, <see cref="ContractHulls"/>)
    /// or uses its own defaults. Every member does exactly what Ships did with Sea's types before the bridge, so a game with
    /// Sea behaves as it did.
    /// </summary>
    public interface ISeaLink
    {
        /// <summary>The Sea mod's version, as its modinfo gives it.</summary>
        string SeaVersion { get; }

        /// <summary>Sea's wind at a place now (vanilla's units), copied into Ships' <see cref="Wind"/>.</summary>
        Wind WindAt(double x, double z);
        /// <summary>Sea's drawn surface above still water at (x, z) now (blocks).</summary>
        double WaveHeightAt(double x, double z);
        /// <summary>Sea's wave factor (the shelter: the share of the open sea's waves that reaches here).</summary>
        double WaveFactorAt(double x, double z);
        /// <summary>Sea's sea state at (x, z) now: significant height (blocks, before the shelter), period (s), wavelength (blocks).</summary>
        (double Height, double Period, double Wavelength) SeaAt(double x, double z);
        /// <summary>Is any dry place registered with Sea dry here (Sea's own answer).</summary>
        bool IsDry(double x, double y, double z);
        bool IsDryWithin(double x, double y, double z, double seconds);

        /// <summary>A wave source of its own for one ship: Sea's <c>SampleWaves</c> into a place of its own, copied into <see cref="WaveTrains"/>.</summary>
        IWaveTrainSource NewWaveSource();

        /// <summary>Register the hulls' dry insides with Sea (both sides). Dispose to take them away again.</summary>
        IDisposable AddDrySpaces(ShipDrySpaces dry);
        /// <summary>
        /// Register a source of hull water clips (client): Sea calls it every frame before drawing water, and each
        /// <see cref="HullWaterClip"/> it adds is handed to Sea as its <c>WaterClipBox</c>, field for field. Dispose to take it away.
        /// </summary>
        IDisposable AddWaterClipSource(Action<List<HullWaterClip>> addClips);
        /// <summary>Sea's vanilla-boat wave pose applied to a boat's clip (<c>Sea.BoatWaves.ApplyToClip</c>).</summary>
        void ApplyBoatPose(EntityBoat boat, ref HullWaterClip clip);

        /// <summary>Sea's default calm ring round a still hull (<c>WaterShader.CalmZone</c>).</summary>
        float CalmZone { get; }
        /// <summary>The name of Sea's dry-outline uniform in the water programs (for the water report).</summary>
        string DryOutlineUniform { get; }
        /// <summary>One line on Sea's water renderer (what it injected), for the water report.</summary>
        string RendererReport();
        /// <summary>For the water report: does Sea's shader use this clip's detailed outline, and is the point inside its cut.</summary>
        (bool Detailed, bool Inside) CutAt(HullWaterClip clip, double x, double y, double z);

        // ---- for the in-game self-tests (SelfTest*.cs): a fixed wind, the wave clock, a replay of the waves

        /// <summary>Sea's wind model and wave spectrum are ready on this side.</summary>
        bool ModelReady { get; }
        bool SpectrumReady { get; }
        /// <summary>Sea's wave clock now (s).</summary>
        double WaveTime { get; }
        /// <summary>A fixed wind from this bearing (degrees) and strength (vanilla units), as if it had blown for the last 24 hours.</summary>
        void SetWindOverride(double fromDegrees, double strength);
        void ClearWindOverride();
        /// <summary>The waves at (x, z) under the sea state and wind there now, replayed at any wave time (Sea's spectrum).</summary>
        ISeaReplay Replay(double x, double z);
    }

    /// <summary>A sea state frozen at one place (<see cref="ISeaLink.Replay"/>), its waves at any time.</summary>
    public interface ISeaReplay
    {
        /// <summary>The trains at wave time t (the same object each call, refilled).</summary>
        WaveTrains At(double t);
        /// <summary>The drawn surface's height at (x, z), at the time last asked.</summary>
        Func<double, double, double> Surface { get; }
    }
}
