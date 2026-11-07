using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Hansa
{
    public partial class SelfTest
    {
        // (7 November 2026) The open sea contracts against the real Sea, on a dedicated server (tools/run-sea-contracts.ps1):
        // with Sea installed and bound, what a mod reads through api.ObjectCache only (Ships' own ContractWaves, the reader
        // it uses without Sea) equals what Sea's typed API gives through Ships' bridge, at the same places and wave time:
        // the trains, the surface height, the shelter, the pose, the current and the sea state; and a dry place registered
        // through seacontracts:hulls is dry in Sea's own answer. No ship, client or player needed.
        void SeaContractsRun(HansaMod mod)
        {
            var sea = mod.SeaLink;
            if (sea?.SpectrumReady != true || mod.Contracts == null) { Check(false, "sea-contracts: Sea is not bound, or its waves are not ready"); Finish(); return; }
            var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
            // every 256 blocks out to 1536 around spawn: the ocean map (the shelter) comes with the chunks
            var columns = new List<(int X, int Z)>();
            for (int i = -6; i <= 6; i++)
                for (int j = -6; j <= 6; j++)
                    columns.Add(((spawn.X >> 5) + i * 8, (spawn.Z >> 5) + j * 8));
            int pending = columns.Count;
            foreach (var (cx, cz) in columns)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { OnLoaded = () => pending-- });
            long wait = 0;
            int waited = 0;
            wait = sapi.Event.RegisterGameTickListener(_ =>
            {
                if (pending > 0 && ++waited < 3000) return;
                sapi.Event.UnregisterGameTickListener(wait);
                if (pending > 0) Log($"note sea-contracts: {pending} of {columns.Count} columns not loaded in time; comparing where they are");
                try { SeaContractsCompare(mod, sea, columns); }
                catch (Exception e) { Check(false, "sea-contracts threw: " + e); }
                Finish();
            }, 100);
        }

        void SeaContractsCompare(HansaMod mod, ISeaLink sea, List<(int X, int Z)> columns)
        {
            var contracts = mod.Contracts;
            var typed = sea.NewWaveSource();
            var a = new WaveTrains();
            var b = new WaveTrains();
            int places = 0, missing = 0, open = 0, retried = 0, trainCount = 0;
            double trainError = 0, heightError = 0, shelterError = 0, poseError = 0, currentError = 0, seaError = 0, maxHeight = 0, maxRoll = 0;
            const double length = 30, beam = 8;
            foreach (var (cx, cz) in columns)
                for (int k = 0; k < 3; k++)
                {
                    double x = cx * 32 + 5.3 + 9.7 * k, z = cz * 32 + 7.9 + 6.1 * k, yaw = 0.7 + 1.3 * k;
                    // the two readings at one wave time: the server's clock runs on, so retry until a reading starts and ends
                    // in the same millisecond
                    for (int attempt = 0; ; attempt++)
                    {
                        double t0 = sea.WaveTime;
                        bool gotA = contracts.Sample(a, x, z), gotB = typed.Sample(b, x, z);
                        double hc = contracts.Height(x, z), ht = sea.WaveHeightAt(x, z);
                        bool posed = contracts.Pose(x, z, yaw, length, beam, out var pose);
                        var own = PoseFrom(sea, x, z, yaw, length, beam);
                        if (sea.WaveTime != t0 && attempt < 20) { retried++; continue; }
                        places++;
                        if (!gotA || !gotB || !posed) { missing++; break; }
                        if (a.Count != b.Count || a.Time != b.Time) trainError = double.PositiveInfinity;
                        else
                            for (int i = 0; i < a.Count; i++)
                                trainError = Math.Max(trainError, Math.Max(Math.Max(Math.Abs(a.Kx[i] - b.Kx[i]), Math.Abs(a.Kz[i] - b.Kz[i])),
                                    Math.Max(Math.Max(Math.Abs(a.Omega[i] - b.Omega[i]), Math.Abs(a.Phase[i] - b.Phase[i])), Math.Abs(a.Amp[i] - b.Amp[i]))));
                        trainCount += a.Count;
                        heightError = Math.Max(heightError, Math.Abs(hc - ht));
                        maxHeight = Math.Max(maxHeight, Math.Abs(ht));
                        double shelter = Math.Clamp(sea.WaveFactorAt(x, z), 0, 1);
                        shelterError = Math.Max(shelterError, Math.Abs(contracts.Shelter(x, z) - shelter));
                        if (shelter > 0.3) open++;
                        poseError = Math.Max(poseError, Math.Max(Math.Max(Math.Abs(pose.Hold - own.Hold), Math.Abs(pose.Lift - own.Lift)),
                            Math.Max(Math.Abs(pose.Roll - own.Roll), Math.Abs(pose.Pitch - own.Pitch))));
                        maxRoll = Math.Max(maxRoll, Math.Abs(own.Roll));
                        var cc = contracts.Current(x, z);
                        var ct = typed.Current(x, z);
                        currentError = Math.Max(currentError, Math.Abs(cc.X - ct.X) + Math.Abs(cc.Z - ct.Z));
                        double hours = sapi.World.Calendar.TotalHours;
                        if (contracts.SeaState(x, z, hours, out double hs, out double period))
                        {
                            var s = sea.SeaAt(x, z);
                            seaError = Math.Max(seaError, Math.Abs(hs - s.Height) + Math.Abs(period - s.Period));
                        }
                        else seaError = double.PositiveInfinity;
                        break;
                    }
                }
            Log($"sea-contracts: {places} places ({open} on open sea, {trainCount} trains), {retried} readings retried across a clock tick");
            Check(missing == 0 && places >= 100, $"sea-contracts: the contracts answer everywhere Sea's typed API does ({missing} of {places} places without trains or a pose)");
            Check(open >= 5 && maxHeight > 0.01, $"sea-contracts: some places are on open sea with waves running ({open} places, waves up to {maxHeight:0.00} blocks)");
            Check(trainError < 1e-12, $"sea-contracts: surface.trains = Sea's typed trains, train by train (max difference {trainError:E2})");
            Check(heightError < 1e-9, $"sea-contracts: surface.height = Sea's WaveHeightAt (max difference {heightError:E2} blocks)");
            Check(shelterError < 1e-12, $"sea-contracts: seastate.shelter = Sea's WaveFactorAt (max difference {shelterError:E2})");
            Check(poseError < 1e-9 && maxRoll > 1e-4, $"sea-contracts: surface.pose = the pose on Sea's typed heights (max difference {poseError:E2}; rolls up to {maxRoll * 180 / Math.PI:0.00}°)");
            Check(currentError < 1e-12, $"sea-contracts: seastate.current = Sea's typed current (max difference {currentError:E2} m/s)");
            Check(seaError < 1e-9, $"sea-contracts: seastate.at = Sea's sea state (max difference in height plus period {seaError:E2})");
            HullsContract(sea);
        }

        /// <summary>The pose by the contract's rule (Rough Seas' <c>PoseOf</c> meaning) on Sea's typed heights.</summary>
        static (double Hold, double Lift, double Roll, double Pitch) PoseFrom(ISeaLink sea, double x, double z, double yaw, double length, double beam)
        {
            double hold = Math.Clamp(sea.WaveFactorAt(x, z), 0, 1);
            if (hold <= 0) return default;
            var (sin, cos) = Math.SinCos(yaw + Math.PI / 2);
            double At(double alongX, double alongZ) => sea.WaveHeightAt(x + cos * alongX + sin * alongZ, z - sin * alongX + cos * alongZ);
            double plusX = At(length / 2, 0), minusX = At(-length / 2, 0), plusZ = At(0, beam / 2), minusZ = At(0, -beam / 2);
            return (hold, (plusX + minusX + plusZ + minusZ) / 4, -Math.Atan((plusZ - minusZ) / beam), Math.Atan((plusX - minusX) / length));
        }

        // a dry place registered through seacontracts:hulls is in Sea's own answer, and goes when unregistered
        void HullsContract(ISeaLink sea)
        {
            IDictionary<string, object> e;
            lock (sapi.ObjectCache) e = sapi.ObjectCache.TryGetValue(ContractHulls.HullsKey, out var o) ? o as IDictionary<string, object> : null;
            var register = e != null && e.TryGetValue("registerDry", out var r) ? r as Action<string, System.Func<double, double, double, bool>> : null;
            var unregister = e != null && e.TryGetValue("unregisterDry", out var u) ? u as Action<string> : null;
            var isDry = e != null && e.TryGetValue("isDry", out var d) ? d as System.Func<double, double, double, bool> : null;
            int recordLength = e != null && e.TryGetValue("recordLength", out var rl) && rl is int n ? n : 0;
            Check(e != null && e.TryGetValue("owner", out var owner) && owner as string == "sea" && register != null && unregister != null && isDry != null,
                "sea-contracts: Sea publishes seacontracts:hulls with registerDry, unregisterDry and isDry");
            Check(recordLength == ContractHulls.DetailedWithDryProfile, $"sea-contracts: Sea reads the detailed hull record ({recordLength} values; Ships writes {ContractHulls.DetailedWithDryProfile})");
            if (register == null || unregister == null || isDry == null) return;
            const double X = 1000.5, Y = 120.5, Z = -2000.5;
            bool Box(double x, double y, double z) => Math.Abs(x - X) < 2 && Math.Abs(y - Y) < 2 && Math.Abs(z - Z) < 2;
            bool before = sea.IsDry(X, Y, Z);
            register("ships-selftest", Box);
            bool inside = sea.IsDry(X, Y, Z) && isDry(X, Y, Z), outside = sea.IsDry(X + 5, Y, Z);
            unregister("ships-selftest");
            bool after = sea.IsDry(X, Y, Z);
            Check(!before && inside && !outside && !after, "sea-contracts: a dry place registered through the contract is dry in Sea's own answer, only there, and not once unregistered");
        }
    }
}
