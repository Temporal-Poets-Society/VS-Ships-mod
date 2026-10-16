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
                $"grid carrack: {def.Grid.CountSolid()} solid voxels, {def.Masts.Length} masts, {def.ModularPieces?.Count(e => e.Kind == "ladder") ?? 0} ladder-stairs in her layout");
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
            // (her scuttles opened to go up, as a climber opens one; shut again after)
            var scuttles = ship.Pieces.All.Where(p => p.Role == PieceRole.Hatch && !p.Open).ToList();
            foreach (var h in scuttles) ship.ToggleHatch(null, h);
            int climbed = 0;
            foreach (var st in ladders)
            {
                var lad = ship.Ladders.FirstOrDefault(x => x.Code == "piece-" + st.Id);
                if (lad != null && DeckMover.ClimbStair(ship.PlaceGrid ?? ship.Grid, lad, out double top, out double back, out _)) climbed++;
                else Log($"grid carrack: her {st.Mount} ladder-stair not climbed");
            }
            Check(climbed == ladders.Count && climbed > 0, $"grid carrack: every ladder-stair climbed up and down in her collision ({climbed} of {ladders.Count})");
            foreach (var h in scuttles) ship.ToggleHatch(null, h);
            int covered = ladders.Count(st => ship.Ladders.FirstOrDefault(x => x.Code == "piece-" + st.Id) is LadderDef lad && !double.IsNaN(HatchCover.Under(ship.Pieces.All, lad)));
            Check(covered == 4 && scuttles.All(h => !h.Open), $"grid carrack: shut, a scuttle stops a climber's head on each of her 4 ladders up through a weather deck ({covered})");
            var sideLadders = def.Ladders.Where(l => l.Outside).ToList();
            int lanterns = ship.Pieces.All.Count(p => p.Stack?.Collectible?.Code?.Path?.StartsWith("lantern") == true);
            Check(sideLadders.Count == 2 && sideLadders.All(l => !ship.LadderTopClosed(l)) && lanterns >= 8,
                $"grid carrack: her side ladders lead up through her gateways (top open), and she starts with her lanterns hung ({lanterns})");
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
                    ship.SailSet = 0;
                    ship.Anchored = true;
                    GridSlots(ship, () => { ship.Die(EnumDespawnReason.Removed); done(); });
                }, 100);
            }, 8000);
        }

        /// <summary>Her blocks as Magelan's slots in the game: hatches, a deck cell taken up and laid, a panel under her
        /// waterline stove in (the sea comes in), mended.</summary>
        void GridSlots(EntityShip ship, Action done)
        {
            var def = ship.Def;
            // her castles' doors and her galley, from her design's pieces
            var doors = ship.Pieces.All.Where(p => p.Role == PieceRole.Door).ToList();
            bool galley = ship.Pieces.All.Any(p => p.Role == PieceRole.Hearth) && ship.Pieces.All.Any(p => p.Role == PieceRole.Oven);
            var cabin = ship.Mounts.Floors.FirstOrDefault(m => m.Room == "greatcabin" && !ship.Pieces.Taken(m.Id));
            // (the great cabin's door: in the quarterdeck's bulkhead, x -2.25 to -2)
            var cabinDoor = doors.FirstOrDefault(d => d.Solids.Count > 0 && PiecePlanner.Union(d.Solids) is double[] db && db[3] > -3 && db[0] < -1 && db[1] < 3);
            string leak = null;
            ShipCompartments.Why = w => leak = w;
            bool shut = cabin != null && ship.SealedAt(cabin.X + 0.5, cabin.Y + 0.6, cabin.Z + 0.5), opened = false;
            ShipCompartments.Why = null;
            var cabinRoom = cabin == null ? null : def.RoomAt(cabin.X + 0.5, cabin.Y + 0.6, cabin.Z + 0.5);
            if (!shut) Log($"grid carrack: her great cabin ({cabinRoom?.Code}, enclosed {cabinRoom?.Enclosed}) leaks: {leak ?? "no flood (not enclosed, or the leaks' list)"}; " +
                           $"wells {ShipCompartments.WellLeaks(def, cabinRoom, ship.Pieces.All).Count}, cells {ship.CellLeaks(cabinRoom).Count}, hull {ship.HullLeaks(cabinRoom).Count}, open {string.Join(",", ship.OpenFittings)}");
            if (cabinDoor != null)
            {
                ship.ToggleDoorPiece(null, cabinDoor);
                opened = cabin != null && !ship.SealedAt(cabin.X + 0.5, cabin.Y + 0.6, cabin.Z + 0.5);
                ship.ToggleDoorPiece(null, cabinDoor);
            }
            Check(doors.Count == 3 && galley && shut && opened,
                $"grid carrack: her three castle doors and her galley laid; her great cabin sealed with its door shut ({shut}), open with it open ({opened})");
            // a chest put in her hold: a storage of hers
            var chest = sapi.World.GetBlock(new AssetLocation("game", "chest-east"));
            var chestStack = new ItemStack(chest);
            var holdPlace = ship.Mounts.Floors.FirstOrDefault(m => m.Deck == "hold" &&
                ship.PlaceProblem(PiecePlanner.Plan(ship.Mounts, m, new VanillaPieceBlock(sapi.World, chestStack), 1), true) == null);
            ShipPiece chestP = null;
            string put = holdPlace == null ? "no place" : ship.PlacePiece(PiecePlanner.Plan(ship.Mounts, holdPlace, new VanillaPieceBlock(sapi.World, chestStack), 1), chestStack, true, out chestP);
            Check(put == null && chestP?.Role == PieceRole.Storage && ship.Storages.ContainsKey(chestP.StorageCode),
                $"grid carrack: a chest put on her hold's floor ({holdPlace?.Id}) is a storage of hers ({put ?? "ok"})");
            var hatches = ship.Pieces.All.Where(p => p.Role == PieceRole.Hatch).ToList();
            bool toggled = hatches.Count > 0 && ship.ToggleHatch(null, hatches[0]) && hatches[0].Open && ship.ToggleHatch(null, hatches[0]) && !hatches[0].Open;
            Check(hatches.Count == 6 && toggled, $"grid carrack: her two hatches laid over her hatchways and four scuttles over her ladder wells, one opened and shut ({hatches.Count})");

            var deck = Enumerable.Range(0, def.Slots.Count).FirstOrDefault(n => def.DeckSlots[n].Deck == "main" && def.DeckSlots[n].IsCell && ship.Slots.Filled(n) && ship.TakeProblem(n) == null, -1);
            if (deck < 0) Check(false, "grid carrack: a main deck cell that can be taken up");
            else
            {
                var b = def.DeckSlots[deck].SolidBoxes[0];
                double cx = (b[0] + b[3]) / 2, cy = (b[1] + b[4]) / 2, cz = (b[2] + b[5]) / 2;
                string take = ship.TakeCell(deck, null);
                bool gone = !ship.Slots.Filled(deck) && !ship.Grid.SolidAt(cx, cy, cz);
                string lay = ship.LayCell(deck, false);
                Check(take == null && gone && lay == null && ship.Slots.Filled(deck) && ship.Grid.SolidAt(cx, cy, cz),
                    $"grid carrack: her deck cell {def.DeckSlots[deck].Id} taken up (out of her collision: {gone}) and laid again ({take ?? "ok"}, {lay ?? "ok"})");
            }

            int panel = Enumerable.Range(0, def.Hull.Count).FirstOrDefault(n => def.HullSlots[n].Head < def.Waterline - 0.5 && def.HullSlots[n].Room != null, -1);
            if (panel < 0) { Check(false, "grid carrack: a hull panel under her waterline"); done(); return; }
            double water0 = ship.Water;
            ship.DamagePart(panel, 1f);
            Check(ship.HullOpen(panel), $"grid carrack: her panel {def.HullSlots[panel].Id} under the waterline stove in");
            sapi.Event.RegisterCallback(_ =>
            {
                double inflow = ship.Water - water0;
                Check(inflow > 0.05, $"grid carrack: the sea came in through it ({inflow:0.00} t in 6 s)");
                ship.RepairAll();
                Check(!ship.HullOpen(panel) && ship.Water < 0.05, "grid carrack: mended and pumped dry");
                // her guns: mounted as a new ship has them, a broadside out of her port side, none of it into herself
                int mounted = def.Fixtures.Count(f => f.Type == "gun" && ship.GunMounted(f));
                int fired = ship.FireBroadside(-1, null, null, 2, 0);
                sapi.Event.RegisterCallback(__ =>
                {
                    var hurt = Enumerable.Range(0, def.Hull.Count).Where(n => ship.StateOfPart(n) != PartState.Sound).Select(n => def.HullSlots[n].Id).ToList();
                    Check(mounted == 8 && fired == 4 && hurt.Count == 0,
                        $"grid carrack: her {mounted} guns mounted, a port broadside of {fired} fired, her own planking unhurt{(hurt.Count > 0 ? " (hurt: " + string.Join(", ", hurt.Take(4)) + ")" : "")}");
                    done();
                }, 3000);
            }, 6000);
        }
    }
}
