using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Hansa
{
    // A headless player's connection is replaced; the real packet handlers, inventories and fire still run.
    public class HearthTestProxy : DispatchProxy
    {
        public System.Func<MethodInfo, object[], object> Call;
        protected override object Invoke(MethodInfo method, object[] args) => Call(method, args);
        public static T Make<T>(System.Func<MethodInfo, object[], object> call) where T : class
        {
            var proxy = Create<T, HearthTestProxy>();
            ((HearthTestProxy)(object)proxy).Call = call;
            return proxy;
        }
        public static object Default(MethodInfo method) => method.ReturnType == typeof(void) || !method.ReturnType.IsValueType ? null : Activator.CreateInstance(method.ReturnType);
    }

    public class HearthTestInventories : PlayerInventoryManager
    {
        public HearthTestInventories(IPlayer player) : base(new Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase>(), player) { }
        public override ItemSlot CurrentHoveredSlot { get; set; }
        public override void BroadcastHotbarSlot() { }
        public override void NotifySlot(IPlayer player, ItemSlot slot) { }
        public override bool DropItem(ItemSlot slot, bool fullStack) => false;
    }

    // IServerPlayer has an engine-internal method, so a DispatchProxy cannot implement it.
    // Use the native player with no connection and skip its persistent player-data initialization.
    public class HearthTestPlayer : ServerPlayer
    {
        protected override void Init() { }
        public HearthTestPlayer(ServerMain server, EntityPlayer entity) : base(server, new ServerWorldPlayerData { EntityPlayer = entity })
        {
            typeof(ServerWorldPlayerData).GetField("PlayerUID", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(WorldData, "ships-hearth-test");
            typeof(ServerPlayer).GetField("inventoryMgr", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(this, new HearthTestInventories(this));
        }
    }

    public partial class SelfTest
    {
        void HearthInteractionWorks(HansaMod mod, ShipDefinition def, BlockPos spawn, double wy, Action then)
        {
            var type = sapi.World.GetEntityType(new AssetLocation("ships", def.Code));
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X - 25.5, wy, spawn.Z + .5);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            if (ship.HearthFixture == null) { ship.Die(EnumDespawnReason.Removed); then(); return; }
            // No player entity is spawned or connected, and no world/player save is created.
            var ep = new EntityPlayer { Api = sapi };
            ep.LocalEyePos.Set(0, 1.62, 0);
            var player = new HearthTestPlayer((ServerMain)sapi.World, ep);
            var manager = (HearthTestInventories)player.InventoryManager;
            var hotbar = new InventoryGeneric(12, "hotbar", player.PlayerUID, sapi);
            manager.Inventories[hotbar.InventoryID] = hotbar;
            void StandBy(FixtureDef f)
            {
                ship.LocalToWorld(f.Pos[0] + 1.4, def.DeckY, f.Pos[2], out var x, out var y, out var z);
                ep.Pos.SetPos(x, y, z);
            }
            var openReplies = new List<FixturePacket>();
            var channelField = typeof(HansaMod).GetField("serverChannel", BindingFlags.Instance | BindingFlags.NonPublic);
            var realChannel = (IServerNetworkChannel)channelField.GetValue(mod);
            var capture = HearthTestProxy.Make<IServerNetworkChannel>((m, a) =>
            {
                if (m.Name == "SendPacket" && a[0] is FixturePacket reply) { openReplies.Add(reply); return null; }
                return m.Invoke(realChannel, a);
            });
            void Click(FixtureDef f, bool sneak = false)
            {
                StandBy(f);
                // Only the outgoing open-dialog connection is captured, for this synchronous handler call.
                channelField.SetValue(mod, capture);
                try { typeof(HansaMod).GetMethod("OnFixture", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mod,
                    new object[] { player, new FixturePacket { ShipId = ship.EntityId, Fixture = f.Index, Sneak = sneak } }); }
                finally { channelField.SetValue(mod, realChannel); }
            }
            void MoveInto(int target, ItemStack stack)
            {
                hotbar[0].Itemstack = stack; hotbar[0].MarkDirty();
                var inv = ship.HearthInv;
                var packet = new Packet_Client { Id = 8, MoveItemstack = new Packet_MoveItemstack
                {
                    SourceInventoryId = hotbar.InventoryID, SourceSlot = 0, SourceLastChanged = hotbar.lastChangedSinceServerStart,
                    TargetInventoryId = inv.InventoryID, TargetSlot = target, TargetLastChanged = inv.lastChangedSinceServerStart,
                    Quantity = stack.StackSize, MouseButton = (int)EnumMouseButton.Left, Priority = (int)EnumMergePriority.DirectMerge
                }};
                ship.OnReceivedClientPacket(player, ((ship.HearthFixture.Index + 1) << 11) | 8, Packet_ClientSerializer.SerializeToBytes(packet));
            }
            void FinishInteraction()
            {
                ship.HearthInv?.Close(player);
                ship.Die(EnumDespawnReason.Removed);
                then();
            }
            try
            {
                hotbar.Open(player);
                var hf = ship.HearthFixture;
                var eye = new Vec3d(hf.Pos[0] + 1.4, def.DeckY + 1.62, hf.Pos[2]);
                var direction = new Vec3d(hf.Pos[0] - eye.X, hf.Pos[1] + .25 - eye.Y, hf.Pos[2] - eye.Z).Normalize();
                var picked = ShipFittings.Pick(def, ship.Grid, eye, direction, 6, f => f.Type != "attach", f => false, f => null,
                    out _, ship.PlacedFixtures, ship.GunAbsent, ship.GunPick);
                Check(picked == hf, "firepit player-eye ray from the galley approach picks the active hearth fixture");
                Click(hf);
                Check(ship.HearthInv.HasOpened(player) && openReplies.LastOrDefault()?.Fixture == hf.Index && ship.FixtureByIndex(hf.Index) == hf,
                    "firepit empty-hand right-click reaches the real server handler, opens its inventory and replies with the hearth dialog target");
                foreach (var code in new[] { "firewood", "redmeat-raw" })
                {
                    hotbar[0].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", code)));
                    int before = openReplies.Count; Click(hf);
                    Check(openReplies.Count == before + 1 && hotbar[0].StackSize == 1,
                        $"firepit right-click holding {code} opens the dialog and retains the held item");
                }
                var hearthPiece = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Hearth);
                if (hearthPiece != null)
                {
                    ship.HearthInv.Close(player);
                    string removed = ship.RemovePiece(hearthPiece, null);
                    hotbar[0].Itemstack = new ItemStack(sapi.World.GetBlock(new AssetLocation("game:firepit-extinct")));
                    string placed = removed ?? ship.TryPlacePiece(player, hearthPiece.Mount, hearthPiece.Dir);
                    hf = ship.HearthFixture;
                    Check(placed == null && hf != null && hf.Code != hearthPiece.StorageCode && ship.FixtureByIndex(hf.Index) == hf,
                        $"a player places an ordinary firepit aboard after taking the default hearth up: {placed ?? "placed"}, active packet target restored");
                    if (hf == null) { FinishInteraction(); return; }
                    hotbar[0].Itemstack = null; Click(hf);
                    Check(ship.HearthInv.HasOpened(player) && openReplies.LastOrDefault()?.Fixture == hf.Index,
                        "a hand-placed firepit opens through the same empty-hand right-click handler");
                }
                // Ordinary dialog inventory packets, rather than setting the hearth slots directly.
                ship.HearthInv.Close(player);
                MoveInto(0, new ItemStack(sapi.World.GetItem(new AssetLocation("game:firewood")), 3));
                Check(ship.HearthInv[0].Empty && hotbar[0].StackSize == 3, "a closed firepit inventory refuses slot packets");
                Click(hf);
                MoveInto(0, new ItemStack(sapi.World.GetItem(new AssetLocation("game:firewood")), 3));
                MoveInto(1, new ItemStack(sapi.World.GetItem(new AssetLocation("game:redmeat-raw"))));
                Check(ship.HearthInv[0].StackSize == 3 && ship.HearthInv[1].StackSize == 1 && hotbar[0].Empty,
                    "firepit dialog slot packets transfer fuel and food through native InventorySmelting");
                var of = ship.OvenFixture;
                if (of != null)
                {
                    hotbar[0].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game:firewood")), 6);
                    Click(of);
                    Check(ship.FixtureByIndex(of.Index) == of && ship.OvenFuel == 1 && hotbar[0].StackSize == 5,
                        "oven right-click packet resolves its active fixture and puts one firewood in");
                    for (int i = 0; i < 5; i++) Click(of);
                    Check(ship.OvenFuel == 6 && hotbar[0].Empty, "oven right-click loads six firewood, one per click");
                    hotbar[0].Itemstack = new ItemStack(sapi.World.GetBlock(new AssetLocation("game:torch-basic-lit-up")));
                    Click(of);
                    Check(ship.Oven.Burning && FirepitTarget.SmokeOutlet(of) != null,
                        "oven right-click with a lit torch lights its fuel; an unflued oven has a smoke outlet");
                }
                StandBy(hf);
                hotbar[0].Itemstack = new ItemStack(sapi.World.GetBlock(new AssetLocation("game:torch-basic-lit-up")));
                var handler = typeof(HansaMod).GetMethod("OnHearth", BindingFlags.Instance | BindingFlags.NonPublic);
                handler.Invoke(mod, new object[] { player, new HearthPacket { ShipId = ship.EntityId, Fixture = hf.Index, Start = true } });
                Check(!ship.Fire.CanIgniteFuel, "firepit lighting start packet alone does not ignite the fuel");
                sapi.Event.RegisterCallback(_ =>
                {
                    try
                    {
                        handler.Invoke(mod, new object[] { player, new HearthPacket { ShipId = ship.EntityId, Fixture = hf.Index, Seconds = 3.4f } });
                        WaitFor(() => ship.Fire.InputStackCookingTime > 0, 40, progress =>
                        {
                            try
                            {
                                Check(progress && ship.Fire.IsBurning && ship.HearthInputTemp() >= 150 &&
                                      ship.WatchedAttributes.GetTreeAttribute("hansaHearth")?.GetFloat("furnaceTemperature") > 20,
                                    $"firepit held-lighting packets start the real burn tick and cooking progress ({ship.Fire.InputStackCookingTime:0.0} s, {ship.HearthInputTemp():0} C)");
                                if (of != null) Check(ship.Oven.Temperature > 20, $"oven heats through its real server tick ({ship.Oven.Temperature:0} C)");
                                ship.SaveHearth(true); ship.SaveOven(true);
                                var state = new TreeAttribute();
                                using (var bytes = new System.IO.MemoryStream())
                                {
                                    ship.WatchedAttributes.ToBytes(new System.IO.BinaryWriter(bytes));
                                    state.FromBytes(new System.IO.BinaryReader(new System.IO.MemoryStream(bytes.ToArray())));
                                }
                                var copy = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
                                foreach (var attribute in state) copy.WatchedAttributes[attribute.Key] = attribute.Value;
                                copy.Pos.SetPos(spawn.X + 14.5, wy, spawn.Z + .5);
                                sapi.World.SpawnEntity(copy);
                                Check(copy.HearthFixture?.Index == hf.Index && copy.FixtureByIndex(hf.Index) == copy.HearthFixture &&
                                      copy.Fire.IsBurning && copy.Fire.InputStackCookingTime > 0 && copy.HearthInv?[1].StackSize == 1 &&
                                      (of == null || copy.Oven.Burning && copy.OvenFuel == 6),
                                    "save/relog roundtrip preserves the hand-placed hearth, its inventory, cooking progress and packet target, and the burning oven");
                                copy.Die(EnumDespawnReason.Removed);
                                if (of != null)
                                {
                                    // Advance the real server tick with vanilla assets; no several-minute wall-clock wait.
                                    var tick = typeof(EntityShip).GetMethod("OvenTick", BindingFlags.Instance | BindingFlags.NonPublic);
                                    for (int i = 0; i < 700 && ship.Oven.Burning; i++) tick.Invoke(ship, new object[] { .1f });
                                    hotbar[0].Itemstack = null;
                                    if (!ship.OvenInv.Empty) Click(of); // any fuel residue comes out with an empty hand
                                    var dough = sapi.World.GetItem(new AssetLocation("game:dough-spelt"));
                                    hotbar[0].Itemstack = dough == null ? null : new ItemStack(dough);
                                    Click(of);
                                    Check(dough != null && ship.OvenHasBakeables && hotbar[0].Empty, "preheated oven accepts vanilla dough through right-click");
                                    for (int i = 0; i < 6000 && ship.OvenInv[0].Itemstack?.Collectible == dough; i++) tick.Invoke(ship, new object[] { .1f });
                                    Check(ship.OvenInv[0].Itemstack?.Collectible?.Code?.Path.StartsWith("bread-") == true,
                                        $"oven real baking tick makes bread from the clicked-in dough ({ship.OvenInv[0].Itemstack?.Collectible?.Code})");
                                    hotbar[0].Itemstack = null; Click(of);
                                    Check(ship.OvenInv.Empty, "empty-hand oven right-click takes the bread out");
                                }
                            }
                            catch (Exception e) { Check(false, "firepit cooking/relog: " + e); }
                            finally { FinishInteraction(); }
                        });
                    }
                    catch (Exception e) { Check(false, "firepit lighting packets: " + e); FinishInteraction(); }
                }, 3400);
            }
            catch (Exception e) { Check(false, "firepit interaction: " + e); FinishInteraction(); }
        }
    }
}
