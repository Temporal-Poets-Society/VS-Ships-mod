using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// The grid carrack (GridShips: the construction core's design on carrack-m's rig) in the game: her design built
        /// and registered with her entity and charter, launched into the pool, her ladder-stairs laid by the piece system
        /// and climbed in her own collision, her hold sealed, and afloat a few seconds later without taking water.
        /// tools/run_selftest.sh with "Only": "grid" runs this alone.
        /// </summary>
        void GridLaunch(HansaMod mod, BlockPos spawn, int y0, Action done)
        {
            var def = mod.GetShip("hansa-carrack");
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", "hansa-carrack"));
            var deed = sapi.World.GetItem(new AssetLocation("hansa", "shipdeed-hansa-carrack"));
            Check(def != null && type != null && deed?.Attributes?["ship"].AsString() == "hansa-carrack" && deed.CreativeInventoryTabs?.Contains("general") == true,
                "grid carrack: her design built from the construction core, her entity and charter registered, the charter in the creative inventory");
            if (def == null || type == null) { done(); return; }
            Check(def.Grid.CountSolid() > 20000 && def.Masts.Length == 3 && def.ModularPieces?.Count(e => e.Kind == "ladder") == 5,
                $"grid carrack: {def.Grid.CountSolid()} solid voxels, {def.Masts.Length} masts, {def.ModularPieces?.Length ?? 0} ladder-stairs in her layout");
            // her bottom is a whole block (planking, floor timbers, the hold floor): a draft of 4.875, as deep as the
            // pool's water; the pool dug a block deeper for her (water down to y0, its floor at y0 - 1)
            {
                var ba = sapi.World.BlockAccessor;
                var water = sapi.World.GetBlock(new AssetLocation("game", "water-still-7"));
                var stone = sapi.World.GetBlock(new AssetLocation("game", "rock-granite"));
                var bp = new BlockPos(0);
                for (int x = spawn.X - 45; x <= spawn.X + 45; x++)
                    for (int z = spawn.Z - 13; z <= spawn.Z + 13; z++)
                    {
                        bool wall = x == spawn.X - 45 || x == spawn.X + 45 || z == spawn.Z - 13 || z == spawn.Z + 13;
                        bp.Set(x, y0 - 1, z); ba.SetBlock(stone.Id, bp);
                        bp.Set(x, y0, z);
                        if (wall) ba.SetBlock(stone.Id, bp);
                        else { ba.SetBlock(0, bp); ba.SetBlock(water.Id, bp, BlockLayersAccess.Fluid); }
                    }
            }
            double wy = EntityShip.FindWaterSurface(sapi.World, new BlockPos(spawn.X, y0 + 5, spawn.Z, 0), 2, 6);
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            // at the pool's west end, her bow toward the east wall, with room to sail
            ship.Pos.SetPos(spawn.X - 20.5, wy, spawn.Z + 0.5);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            var ladders = ship.Pieces.All.Where(p => p.Role == PieceRole.Ladder).ToList();
            Check(ladders.Count == 5, $"grid carrack: a new ship has her 5 ladder-stairs laid ({ladders.Count}: {string.Join(", ", ladders.Select(p => p.Mount))})");
            int climbed = 0;
            foreach (var st in ladders)
            {
                var lad = ship.Ladders.FirstOrDefault(x => x.Code == "piece-" + st.Id);
                if (lad != null && DeckMover.ClimbStair(ship.PlaceGrid ?? ship.Grid, lad, out double top, out double back, out _)) climbed++;
                else Log($"grid carrack: her {st.Mount} ladder-stair not climbed");
            }
            Check(climbed == ladders.Count && climbed > 0, $"grid carrack: every ladder-stair climbed up and down in her collision ({climbed} of {ladders.Count})");
            var hold = ship.Mounts.Floors.FirstOrDefault(m => m.Deck == "hold");
            Check(hold != null && ship.SealedAt(hold.X + 0.5, hold.Y + 0.6, hold.Z + 0.5), "grid carrack: her hold is sealed");
            double y = ship.Pos.Y;
            sapi.Event.RegisterCallback(_ =>
            {
                Check(ship.Alive && ship.Stage == SinkStage.Afloat && ship.Water < 0.5 && Math.Abs(ship.Pos.Y - y) < 1.5,
                    $"grid carrack: afloat after 8 s (stage {ship.Stage}, water {ship.Water:0.00} t, y moved {ship.Pos.Y - y:0.00})");
                // sailing: a strong breeze from the north on her port beam as she heads east, sails set
                mod.Config.StopWhenUnmanned = false;
                var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
                sea?.SetOverride(new Sea.WindOverride { FromDegrees = 0, Strength = 0.6, StartHours = sea.Hours - 24 });
                ship.Anchored = false;
                ship.SailSet = 2;
                var start = ship.Pos.XYZ;
                int t = 0;
                double top = 0;
                long id = 0;
                id = sapi.Event.RegisterGameTickListener(dt =>
                {
                    t++;
                    top = Math.Max(top, ship.Speed);
                    if (t < 150 && !ship.Aground) return;
                    sapi.Event.UnregisterGameTickListener(id);
                    Check(ship.Pos.X - start.X > 5 && Math.Abs(ship.Pos.Z - start.Z) < 1 && ship.Stage == SinkStage.Afloat && ship.Water < 0.5,
                        $"grid carrack: sailed east {ship.Pos.X - start.X:0.0} blocks in {t / 10.0:0.0} s on the beam wind (top speed {top:0.00} b/s, sails {ship.SailFraction:0.00}), held her course, dry");
                    ship.Die(EnumDespawnReason.Removed);
                    done();
                }, 100);
            }, 8000);
        }
    }
}
