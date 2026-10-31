using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>One owned mechanical sound and pour per working pump; no overlapping one-shot clips.</summary>
    public sealed class PumpAudio : IDisposable
    {
        sealed class Sounds { public ILoadedSound Brake, Pour; }
        readonly Dictionary<string, Sounds> sounds = new();
        readonly Func<string, Vec3f, float, ILoadedSound> load;
        public PumpAudio(Func<string, Vec3f, float, ILoadedSound> load) => this.load = load;

        public void Update(string key, Vec3f pos, bool wet, float volume)
        {
            if (!sounds.TryGetValue(key, out var pair)) sounds[key] = pair = new();
            Play(ref pair.Brake, "game:sounds/block/pump.ogg", pos, wet ? 1 : .4f);
            if (wet) Play(ref pair.Pour, "game:sounds/effect/water-pour.ogg", pos, volume);
            else Stop(ref pair.Pour);
        }

        void Play(ref ILoadedSound sound, string location, Vec3f pos, float volume)
        {
            if (sound == null) { sound = load(location, pos, volume); sound?.Start(); }
            sound?.SetPosition(pos);
            sound?.SetVolume(volume);
        }

        static void Stop(ref ILoadedSound sound)
        {
            if (sound == null) return;
            sound.Stop(); sound.Dispose(); sound = null;
        }

        void Remove(string key)
        {
            var pair = sounds[key];
            Stop(ref pair.Brake); Stop(ref pair.Pour); sounds.Remove(key);
        }

        public void Retain(ISet<string> active)
        {
            foreach (string key in sounds.Keys.ToArray()) if (!active.Contains(key)) Remove(key);
        }

        public void RemoveShip(long ship)
        {
            foreach (string key in sounds.Keys.ToArray()) if (key.StartsWith(ship + ":", StringComparison.Ordinal)) Remove(key);
        }

        public void Dispose()
        {
            foreach (string key in sounds.Keys.ToArray()) Remove(key);
        }
    }
}
