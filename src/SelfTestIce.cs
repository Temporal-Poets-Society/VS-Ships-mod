using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// (2 November 2026, the user: "test in VM client") Only "ice-scene": a scene for a graphical client to watch, left
        /// running. Once a player is on, they stand on a glass lookout over the pool; a caravel sails east at a band of ice one
        /// block thick, without BreakIce (she stops and sits), then with it (she breaks through), to a band two blocks thick
        /// (she stops), and then the whole pool freezes round her. Logs her place, speed and hurt parts every two seconds.
        /// </summary>
        void IceScene(HansaMod mod, ShipDefinition def, BlockPos spawn, int y0, int x0, int x1, int z0, int z1)
        {
            var ba = sapi.World.BlockAccessor;
            var ice = sapi.World.GetBlock(new AssetLocation("game", "lakeice"));
            var glass = sapi.World.GetBlock(new AssetLocation("game", "glass-plain"));
            int top = y0 + 5;
            void Band(int xa, int xb, int layers)
            {
                for (int x = xa; x <= xb; x++)
                    for (int z = z0 + 1; z < z1; z++)
                        for (int l = 0; l < layers; l++)
                            ba.SetBlock(ice.Id, new BlockPos(x, top - l, z, 0), BlockLayersAccess.Fluid);
            }
            int Count(int xa, int xb, int layers)
            {
                int n = 0;
                for (int x = xa; x <= xb; x++)
                    for (int z = z0 + 1; z < z1; z++)
                        for (int l = 0; l < layers; l++)
                            if (EntityShip.IsIce(ba.GetBlock(new BlockPos(x, top - l, z, 0), BlockLayersAccess.Fluid))) n++;
                return n;
            }
            Band(spawn.X, spawn.X + 1, 1);
            Band(spawn.X + 16, spawn.X + 19, 2);
            Log($"ice-scene: thin band at x {spawn.X}..{spawn.X + 1}, thick band at x {spawn.X + 16}..{spawn.X + 19}, water top y {top}");

            long waitId = 0;
            int ticks = 0;
            waitId = sapi.Event.RegisterGameTickListener(_ =>
            {
                ticks++;
                var player = sapi.World.AllOnlinePlayers.FirstOrDefault() as IServerPlayer;
                // (headless, tools/run_selftest.sh, which shuts down after: nobody comes, the scene runs after a minute;
                // a server left running waits for its watcher, however long the client takes)
                if (player?.Entity == null && cfg.Shutdown && ticks >= 120) { sapi.Event.UnregisterGameTickListener(waitId); Start(null); return; }
                // (a joining client is "online" a minute before it plays: a teleport then goes nowhere)
                if (player?.Entity == null || player.ConnectionState != EnumClientState.Playing || ticks % 10 != 0) return;
                sapi.Event.UnregisterGameTickListener(waitId);
                Log("ice-scene: player on, starting in 40 s");
                sapi.Event.RegisterCallback(__ => Start(player), 40000);
            }, 500);

            void Start(IServerPlayer player)
            {
                // the lookout: 28 blocks over the water, above x+5
                int lx = spawn.X + 5, ly = top + 28, lz = spawn.Z;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        ba.SetBlock(glass?.Id ?? 0, new BlockPos(lx + dx, ly, lz + dz, 0));
                void Lookout()
                {
                    if (player?.Entity == null || player.Entity.Pos.DistanceTo(new Vec3d(lx + 0.5, ly + 1, lz + 0.5)) < 2) return;
                    Log($"ice-scene: player at {player.Entity.Pos.X - spawn.X:0.0} {player.Entity.Pos.Y:0.0} {player.Entity.Pos.Z - spawn.Z:0.0}, to the lookout");
                    // (flying, so a slip off the glass doesn't drop them into the pool)
                    player.WorldData.FreeMove = true;
                    player.BroadcastPlayerData();
                    player.Entity.TeleportToDouble(lx + 0.5, ly + 1, lz + 0.5);
                }
                Lookout();

                double wy = EntityShip.FindWaterSurface(sapi.World, new BlockPos(spawn.X - 25, top, spawn.Z, 0), 2, 6);
                var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(sapi.World.GetEntityType(new AssetLocation("ships", cfg.Ship)));
                ship.Pos.SetPos(spawn.X - 25.5, wy, spawn.Z + 0.5);
                ship.Pos.Yaw = (float)(Math.PI / 2);
                sapi.World.SpawnEntity(ship);
                mod.Config.StopWhenUnmanned = false;
                mod.Config.BreakIce = false;
                var sea = sapi.ModLoader.GetModSystem<HansaMod>()?.SeaLink;
                sea?.SetWindOverride(0, 0.6);
                ship.Anchored = false;
                ship.SailSet = 2;
                Log($"ice-scene t=0: caravel launched at x {ship.Pos.X - spawn.X:0.0} (relative to the thin band), sails set, BreakIce off");

                var t0 = sapi.World.ElapsedMilliseconds;
                bool broke = false, froze = false;
                // (ramming ice at speed is a grounding blow, as a shore's: what counts is what comes after she stops)
                double hurt0 = double.NaN, x0held = double.NaN;
                long id = 0;
                id = sapi.Event.RegisterGameTickListener(__ =>
                {
                    double t = (sapi.World.ElapsedMilliseconds - t0) / 1000.0;
                    if (t < 20) Lookout();
                    Log($"ice-scene t={t:0}: x {ship.Pos.X - spawn.X:0.00} speed {ship.Speed:0.00} y {ship.BaseY:0.000} aground {ship.Aground} " +
                        $"hurt {Hurt(ship):0.000} water {ship.Water:0.000} BreakIce {mod.Config.BreakIce} ice in hull {IceInHull(ship, top)} " +
                        $"bands {Count(spawn.X, spawn.X + 1, 1)}/{(z1 - z0 - 1) * 2} {Count(spawn.X + 16, spawn.X + 19, 2)}/{(z1 - z0 - 1) * 8}");
                    if ((t >= 16 && !froze || t >= 66) && double.IsNaN(hurt0)) { hurt0 = Hurt(ship); x0held = ship.Pos.X; }
                    if (!broke && t >= 30)
                    {
                        broke = true;
                        // (her sails still drive her at it: she bumps it every few ticks at a crawl, as against a shore)
                        Check(Math.Abs(ship.Pos.X - x0held) < 0.3 && ship.Pos.X - spawn.X < 0,
                            $"ice-scene: held by thin ice without BreakIce, she stays put (x {x0held - spawn.X:0.00} → {ship.Pos.X - spawn.X:0.00}; " +
                            $"her sails pressing her on it, hurt {hurt0:0.000} → {Hurt(ship):0.000} in 14 s)");
                        mod.Config.BreakIce = true;
                        Log("ice-scene: BreakIce on");
                    }
                    if (!froze && t >= 60)
                    {
                        froze = true;
                        Check(ship.Pos.X - spawn.X > 2 && ship.Pos.X - spawn.X < 16, $"ice-scene: with BreakIce she broke the thin band and the thick band holds her (x {ship.Pos.X - spawn.X:0.0})");
                        // frozen in as a ship is: lying still, sails struck, anchored
                        ship.SailSet = 0;
                        ship.Anchored = true;
                        hurt0 = double.NaN;
                        for (int x = x0 + 1; x < x1; x++)
                            for (int z = z0 + 1; z < z1; z++)
                                ba.SetBlock(ice.Id, new BlockPos(x, top, z, 0), BlockLayersAccess.Fluid);
                        Log($"ice-scene: the whole pool frozen (ice in hull {IceInHull(ship, top)})");
                    }
                    if (t >= 95)
                    {
                        sapi.Event.UnregisterGameTickListener(id);
                        Check(Math.Abs(Hurt(ship) - hurt0) < 1e-6 && Math.Abs(ship.Pos.X - x0held) < 0.3 && IceInHull(ship, top) == 0,
                            $"ice-scene: frozen in, she stays put unhurt and no ice is left inside her (hurt {hurt0:0.000} → {Hurt(ship):0.000}, x {x0held - spawn.X:0.00} → {ship.Pos.X - spawn.X:0.00})");
                        Log("ice-scene: done (the server keeps running)");
                        Finish();
                    }
                }, 2000);
            }
        }

        /// <summary>Her parts' hurt added up (a blow anywhere raises it; leaking alone doesn't).</summary>
        static double Hurt(EntityShip ship) => ship.PartDamage?.Hurt.Sum(h => (double)h) ?? 0;

        int IceInHull(EntityShip ship, int top)
        {
            int n = 0;
            var ba = sapi.World.BlockAccessor;
            for (double x = -6; x <= 6; x += 1)
            {
                ship.LocalToWorld(x, 0, 0, out double wx, out _, out double wz);
                if (EntityShip.IsIce(ba.GetBlock(new BlockPos((int)Math.Floor(wx), top, (int)Math.Floor(wz), 0), BlockLayersAccess.Fluid))) n++;
            }
            return n;
        }
    }
}
