using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>The wave factor the pool is put at: Sea's sheltered water, about the 0.2 that Sea gave everywhere inland until 0.5.3.</summary>
        const double PoolFactor = 0.25;
        /// <summary>The least wave factor anywhere over the main pool.</summary>
        const double PoolFactorMin = 0.1;
        /// <summary>Candidate sites: every PoolSiteStep blocks out to PoolSiteSteps steps each way from the world's spawn.</summary>
        const int PoolSiteStep = 256, PoolSiteSteps = 5;

        /// <summary>
        /// (SH-0011) Where the test's pools are built in the sky. Sea 0.6 scales its waves by the world's ocean map (the wave
        /// factor: 1 on the open ocean, a fifth in sheltered water, none inland, since 0.5.3), so over the spawn, on land,
        /// she lies on a flat pool and the heave check has nothing to measure. The pools go over coastal water instead: of
        /// the sites every <see cref="PoolSiteStep"/> blocks round the spawn (their chunk columns loaded, which brings their
        /// ocean map), the one whose factor over the main pool is nearest <see cref="PoolFactor"/> (none under
        /// <see cref="PoolFactorMin"/> at its corners and middle, and no beach's surf there), then the nearest. Without Sea,
        /// or with no such site, the spawn. The test world is mostly ocean for it (tools/run_selftest.sh: landcover 0.3).
        /// </summary>
        void PoolSite(HansaMod mod, BlockPos spawn, Action<BlockPos> then)
        {
            var sea = mod.SeaLink;
            if (sea == null) { Log("pool site: no Sea, the pools over the spawn"); then(spawn); return; }
            var sites = new List<BlockPos>();
            for (int i = -PoolSiteSteps; i <= PoolSiteSteps; i++)
                for (int j = -PoolSiteSteps; j <= PoolSiteSteps; j++)
                    sites.Add(new BlockPos(spawn.X + i * PoolSiteStep, spawn.Y, spawn.Z + j * PoolSiteStep, spawn.dimension));
            int pending = sites.Count;
            foreach (var s in sites)
                sapi.WorldManager.LoadChunkColumnPriority(s.X >> 5, s.Z >> 5, new ChunkLoadOptions { OnLoaded = () => pending-- });
            long wait = 0;
            int waited = 0;
            wait = sapi.Event.RegisterGameTickListener(_ =>
            {
                if (pending > 0 && ++waited < 3000) return;
                sapi.Event.UnregisterGameTickListener(wait);
                BlockPos best = null;
                double bestScore = double.MaxValue, bestMid = 0, bestMin = 0, strongest = 0;
                int fit = 0, surfed = 0;
                var source = sea.NewWaveSource();
                var probe = new WaveTrains();
                foreach (var s in sites)
                {
                    // the main pool's corners, edges and middle (x ±45, z ±13)
                    var f = new List<double>();
                    bool surf = false;
                    foreach (int dx in new[] { -45, 0, 45 })
                        foreach (int dz in new[] { -13, 0, 13 })
                        {
                            f.Add(sea.WaveFactorAt(s.X + dx + 0.5, s.Z + dz + 0.5));
                            // (and off the beaches' surf, which the ship eases in over time: the heave check works her
                            // answer out again from the shared waves alone)
                            if (source.Sample(probe, s.X + dx + 0.5, s.Z + dz + 0.5) && probe.Surf != null) surf = true;
                        }
                    double mid = f[4], min = f.Min();
                    strongest = Math.Max(strongest, min);
                    if (surf) { surfed++; continue; }
                    if (min < PoolFactorMin) continue;
                    fit++;
                    double far = Math.Sqrt((double)(s.X - spawn.X) * (s.X - spawn.X) + (double)(s.Z - spawn.Z) * (s.Z - spawn.Z));
                    double score = Math.Abs(mid - PoolFactor) + far * 1e-5;
                    if (score < bestScore) { bestScore = score; best = s; bestMid = mid; bestMin = min; }
                }
                if (best == null)
                {
                    Log($"pool site: none of {sites.Count} sites round the spawn has waves over the whole pool (the most anywhere over one: a factor of {strongest:0.00}; " +
                        $"{pending} not loaded); the pools over the spawn");
                    then(spawn);
                    return;
                }
                Log($"pool site: {best.X - spawn.X:+0;-0}, {best.Z - spawn.Z:+0;-0} from the spawn, wave factor {bestMid:0.00} in the middle " +
                    $"(at least {bestMin:0.00}; {fit} of {sites.Count} sites with waves over the whole pool and no surf, {surfed} in a beach's surf)");
                then(best);
            }, 100);
        }
    }
}
