using System;
using System.Diagnostics;
using Vintagestory.API.Common;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// (SH-0034, review M5) What riding a beach's surf costs her a server tick. Over coastal water a ride fits a plane to
        /// the surf under her (<see cref="ShipMotion.Surf"/>: 18 of Sea's inverse surface solves), and since SH-0034 she fits
        /// it every <see cref="EntityShip.SurfFitEveryMs"/> only, easing between. A place with surf is looked for round the
        /// world's spawn (land kept there in the mostly-ocean test world, so its beaches are near); her ride is timed there
        /// with and without the fit, and the average a 30-a-second tick pays at that rate is held to the same 50 µs as a ride
        /// on the open sea. No surf found (no coast solved near the spawn yet): said, not checked.
        /// </summary>
        void SurfRideCost(EntityShip ship, IWaveTrainSource src)
        {
            var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
            var probe = new WaveTrains();
            double sx = double.NaN, sz = double.NaN;
            int tried = 0;
            for (int r = 0; r <= 256 && double.IsNaN(sx); r += 8)
                for (int i = -r; i <= r && double.IsNaN(sx); i += 8)
                    foreach (var (dx, dz) in new[] { (i, -r), (i, r), (-r, i), (r, i) })
                    {
                        double x = spawn.X + dx + 0.5, z = spawn.Z + dz + 0.5;
                        tried++;
                        if (src.Sample(probe, x, z) && probe.Surf != null) { sx = x; sz = z; break; }
                    }
            if (double.IsNaN(sx)) { Log($"surf ride cost: no surf found round the spawn ({tried} places): not timed"); return; }
            var w = Stopwatch.StartNew();
            for (int i = 0; i < 400; i++) ship.WaveMotion(sx, sz, ship.Pos.Yaw + i * 1e-4, out _);
            double withFit = w.Elapsed.TotalMilliseconds * 1000 / 400;
            w.Restart();
            for (int i = 0; i < 400; i++) ship.WaveMotionNoSurfFit(sx, sz, ship.Pos.Yaw + i * 1e-4);
            double without = w.Elapsed.TotalMilliseconds * 1000 / 400;
            // a 30-a-second tick fits the plane every SurfFitEveryMs: one tick in (that / 33.3)
            double ticksPerFit = EntityShip.SurfFitEveryMs / (1000.0 / 30);
            double perTick = without + (withFit - without) / ticksPerFit;
            Log($"surf ride cost at {sx - spawn.X:+0;-0}, {sz - spawn.Z:+0;-0} from the spawn ({tried} places looked at): {withFit:0.0} µs with the plane fitted, " +
                $"{without:0.0} without; fitted every {EntityShip.SurfFitEveryMs} ms, {perTick:0.0} µs a tick");
            Check(perTick < 50, $"riding a beach's surf costs {perTick:0.0} µs a tick on average (the plane fitted every {EntityShip.SurfFitEveryMs} ms: {withFit:0.0} µs that tick)");
        }
    }
}
