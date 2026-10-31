using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public partial class SelfTest
    {
        // Isolated dedicated-server mode. Logs real ServerTick motion and replays delayed packets through
        // the actual client frame path and renderer matrix. No game client, graphics context or player needed.
        void WaveSwayRun(HansaMod mod, ShipDefinition def)
        {
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            if (def == null || sea?.Spectrum == null) { Check(false, "wave-sway: ship/Sea unavailable"); Finish(); return; }
            mod.Config.StopWhenUnmanned = false;
            mod.Config.Damage = false;
            mod.Config.SailByWind = false; // fixed throttle in head seas too; wind heel and rig still sampled
            mod.Config.Windage = 0;
            var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
            int y0 = sapi.WorldManager.MapSizeY - 58;
            int pending = 0;
            for (int x = (spawn.X - 145) >> 5; x <= (spawn.X + 145) >> 5; x++)
            for (int z = (spawn.Z - 145) >> 5; z <= (spawn.Z + 145) >> 5; z++)
            {
                pending++;
                sapi.WorldManager.LoadChunkColumnPriority(x, z, new Vintagestory.API.Server.ChunkLoadOptions
                    { KeepLoaded = true, OnLoaded = () => pending-- });
            }
            long wait = 0;
            int waited = 0;
            wait = sapi.Event.RegisterGameTickListener(_ =>
            {
                if (pending > 0 && ++waited < 600) return;
                sapi.Event.UnregisterGameTickListener(wait);
                if (pending > 0) { Check(false, "wave-sway chunks timed out"); Finish(); return; }
                var water = sapi.World.GetBlock(new AssetLocation("game", "water-still-7"));
                var stone = sapi.World.GetBlock(new AssetLocation("game", "rock-granite"));
                var bp = new BlockPos(0);
                // A cross of deep water: enough room for 30 seconds at half sail, in either heading.
                for (int x = -145; x <= 145; x++)
                for (int z = -145; z <= 145; z++)
                {
                    if (Math.Abs(x) > 18 && Math.Abs(z) > 18) continue;
                    for (int y = y0; y <= y0 + 29; y++)
                    {
                        bp.Set(spawn.X + x, y, spawn.Z + z);
                        sapi.World.BlockAccessor.SetBlock(y == y0 ? stone.Id : 0, bp);
                        if (y > y0 && y <= y0 + 26) sapi.World.BlockAccessor.SetBlock(water.Id, bp, BlockLayersAccess.Fluid);
                    }
                }
                Log("wave-sway pool ready; eight 30 s cases, real server ticks; 5 Hz packets + 100 ms delay, client frame replay");
                WaveSwayCase(mod, def, sea, spawn.X + 0.5, y0 + 26.875, spawn.Z + 0.5, 0);
            }, 100);
        }

        void WaveSwayCase(HansaMod mod, ShipDefinition def, Sea.SeaSystem sea, double x, double y, double z, int n)
        {
            if (n == 8) { sea.SetOverride(null); Finish(); return; }
            bool storm = n >= 4, way = n % 4 >= 2, beam = n % 2 == 0;
            string name = $"{(storm ? "storm" : "moderate")}-{(way ? "way" : "rest")}-{(beam ? "beam" : "head")}";
            sea.SetOverride(new Sea.WindOverride { FromDegrees = 0, Strength = storm ? 1.5 : 0.5, StartHours = sea.Hours - 24 });
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(sapi.World.GetEntityType(new AssetLocation("ships", def.Code)));
            double yaw0 = beam ? Math.PI / 2 : Math.PI;
            ship.Pos.SetPos(x, y, z); ship.Pos.Yaw = (float)yaw0;
            sapi.World.SpawnEntity(ship);
            ship.Anchored = false; ship.SailSet = way ? 1 : 0; ship.SailFraction = way ? 0.55f : 0;
            ship.Speed = way ? def.MaxSpeed * 0.55f : 0;
            var client = new EntityShip { Def = def, Mod = mod, SeaSys = sea, DamageOn = false, BaseY = y };
            client.ReceiveMotionPose(x, y, z, yaw0, sapi.World.ElapsedMilliseconds, true);
            var rows = new List<double[]>();
            var packets = new Queue<(long Time, double X, double Y, double Z, float Yaw, float Rate, float Speed)>();
            string dir = Path.Combine(sapi.GetOrCreateDataPath("Logs"), "wave-sway");
            Directory.CreateDirectory(dir);
            var csv = new StreamWriter(Path.Combine(dir, name + ".csv"));
            csv.WriteLine("seconds,serverYaw,drawYaw,pitch,roll,heave,drawHeave,poseError,yawError,serverPitch,serverRoll");
            long start = sapi.World.ElapsedMilliseconds, sent = start - 200;
            double maxYawError = 0, maxPosError = 0, settledMin = double.MaxValue, settledMax = 0;
            long tick = 0;
            tick = sapi.Event.RegisterGameTickListener(dt =>
            {
                try
                {
                    long now = sapi.World.ElapsedMilliseconds;
                    if (now - sent >= 200)
                    {
                        sent = now;
                        packets.Enqueue((now, ship.Pos.X, ship.Pos.Y, ship.Pos.Z, ship.Pos.Yaw, ship.YawRate, ship.Speed));
                    }
                    while (packets.Count > 0 && now - packets.Peek().Time >= 100)
                    {
                        var p = packets.Dequeue();
                        client.Speed = p.Speed; client.YawRate = p.Rate;
                        // Receipt timestamp, just like a real client (no knowledge of the server's send clock).
                        client.ReceiveMotionPose(p.X, p.Y, p.Z, p.Yaw, now);
                    }
                    client.SailFraction = ship.SailFraction; client.SailSet = ship.SailSet;
                    client.ClientMotionFrame(dt, now);
                    var m = ShipFrame.ModelMatrix(new Matrixf(), 0, 0, 0, client.Pos.Yaw, client.Pitch, client.Roll);
                    var bow = m.TransformVector(new Vec4f(1, 0, 0, 0));
                    double drawnYaw = Math.Atan2(bow.X, bow.Z);
                    double sy = Math.IEEERemainder(ship.Pos.Yaw - yaw0, 2 * Math.PI) * 180 / Math.PI;
                    double cy = Math.IEEERemainder(drawnYaw - yaw0, 2 * Math.PI) * 180 / Math.PI;
                    double err = Math.Sqrt(Math.Pow(ship.Pos.X - client.Pos.X, 2) + Math.Pow(ship.Pos.Z - client.Pos.Z, 2));
                    double elapsed = (now - start) / 1000.0;
                    if (elapsed > 2) { maxYawError = Math.Max(maxYawError, Math.Abs(sy - cy)); maxPosError = Math.Max(maxPosError, err); }
                    if (elapsed > 5) { settledMin = Math.Min(settledMin, err); settledMax = Math.Max(settledMax, err); }
                    rows.Add(new[] { elapsed, sy, cy, client.Pitch * 180 / Math.PI, client.Roll * 180 / Math.PI, ship.Heave });
                    csv.WriteLine(FormattableString.Invariant($"{elapsed:F3},{sy:F6},{cy:F6},{client.Pitch * 180 / Math.PI:F6},{client.Roll * 180 / Math.PI:F6},{ship.Heave:F6},{client.Heave:F6},{err:F6},{sy - cy:F6},{ship.Pitch:F6},{ship.Roll:F6}"));
                    if (elapsed < 30) return;
                    sapi.Event.UnregisterGameTickListener(tick); csv.Dispose();
                    string Metric(int column)
                    {
                        double mean = rows.Average(r => r[column]);
                        var crosses = Enumerable.Range(1, rows.Count - 1).Where(i => rows[i - 1][column] < mean && rows[i][column] >= mean).ToArray();
                        double period = crosses.Length < 2 ? 0 : (rows[crosses[^1]][0] - rows[crosses[0]][0]) / (crosses.Length - 1);
                        return $"{(rows.Max(r => r[column]) - rows.Min(r => r[column])) / 2:F3}/{period:F2}s";
                    }
                    var state = sea.SeaAt(x, z);
                    Log($"wave-sway {name}: Hs {state.Height:F2}, factor {sea.WaveFactorAt(x,z):F2}, server yaw {Metric(1)}, drawn yaw {Metric(2)}, pitch {Metric(3)}, roll {Metric(4)}, heave {Metric(5)}; max client error {maxYawError:F3}deg/{maxPosError:F3}m, settled lag range {settledMax - settledMin:F3}m");
                    // Position includes the deliberate 100 ms network delay, plus a held 5 Hz velocity snapshot.
                    // Bound that lag by speed, and separately reject oscillating packet corrections.
                    Check(!ship.Aground && !ship.Waiting && maxYawError < 0.6 &&
                        maxPosError < 0.15 + Math.Abs(ship.Speed) * 0.25 && settledMax - settledMin < 0.2,
                        "wave-sway " + name + ": afloat, server/client agree with delayed packets");
                    ship.Die(EnumDespawnReason.Removed);
                    WaveSwayCase(mod, def, sea, x, y, z, n + 1);
                }
                catch (Exception e)
                {
                    sapi.Event.UnregisterGameTickListener(tick); csv.Dispose();
                    ship.Die(EnumDespawnReason.Removed); sea.SetOverride(null);
                    Check(false, "wave-sway: " + e); Finish();
                }
            }, 33);
        }
    }
}
