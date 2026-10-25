using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// (0.1.0-dev.10, the user's caravel after a crash: saved under the sail set under her lashed helm, she sailed on
        /// when the world was loaded again) A ship loaded from the save with nobody aboard comes up anchored, furled and
        /// dead in the water at her first tick, and that's what she saves; one only set going with nobody aboard keeps her
        /// way for the grace seconds (<see cref="Unmanned"/>). The game's load is <see cref="EntityShip.OnEntityLoaded"/>,
        /// which the engine calls when her chunk brings her back.
        /// </summary>
        void LoadedUnmanned(HansaMod mod, BlockPos spawn, double wy, Action then)
        {
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", cfg.Ship));
            bool stopWas = mod.Config.StopWhenUnmanned;
            mod.Config.StopWhenUnmanned = true;
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X - 25.5, wy, spawn.Z + 0.5);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            // as the crash saved her: anchor up, full sail, way on her and a turn
            ship.Anchored = false;
            ship.SailSet = 2;
            ship.SailFraction = 1;
            ship.Speed = 6;
            ship.YawRate = 0.02f;
            ship.SaveState(true);
            ship.OnEntityLoaded();
            var at = ship.Pos.XYZ;
            WaitFor(() => false, 0.5, _ =>
            {
                var t = ship.WatchedAttributes.GetTreeAttribute("hansa");
                double moved = ship.Pos.XYZ.HorizontalSquareDistanceTo(at);
                Check(ship.Anchored && ship.SailSet == 0 && ship.Speed == 0 && ship.YawRate == 0 && Math.Sqrt(moved) < 0.05 &&
                      t?.GetBool("anchored") == true && t.GetInt("sailSet") == 0 && t.GetFloat("speed") == 0,
                    $"loaded under sail with nobody aboard: anchored, furled and still at once, and saved so (moved {Math.Sqrt(moved):0.00})");
                // set going again with nobody aboard (no load): she keeps her way for the grace seconds
                ship.Anchored = false;
                ship.SailSet = 2;
                WaitFor(() => false, 0.5, __ =>
                {
                    Check(!ship.Anchored && ship.SailSet == 2 && mod.Config.UnmannedStopSeconds > 0.5f,
                        "set going with nobody aboard, not loaded: under sail for the grace seconds");
                    WaitFor(() => ship.Anchored && ship.SailSet == 0, mod.Config.UnmannedStopSeconds + 2, stopped =>
                    {
                        Check(stopped, $"and anchored and furled after them ({mod.Config.UnmannedStopSeconds:0} s)");
                        ship.Die(EnumDespawnReason.Removed);
                        mod.Config.StopWhenUnmanned = stopWas;
                        then();
                    });
                });
            });
        }
    }
}
