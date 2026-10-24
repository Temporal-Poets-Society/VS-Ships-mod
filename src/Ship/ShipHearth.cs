using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// Vanilla's firepit inventory (InventorySmelting: fuel, input, output and the four cooking slots a pot opens), kept
    /// on the ship instead of a block entity. It works without one: its only use of the firepit's block position is to
    /// drop the cooking slots' contents at it when a pot is taken out, and aboard those go to whoever took it out (else
    /// they drop at the hearth, as vanilla's do).
    /// </summary>
    public class HearthInventory : InventorySmelting
    {
        readonly EntityShip ship;

        public HearthInventory(EntityShip ship, ICoreAPI api) : base(null, null)
        {
            this.ship = ship;
            pos = new BlockPos(0);
            // (as vanilla's firepit does: made empty, then given its id and the api)
            LateInitialize(EntityShip.HearthInventoryClass + "-" + ship.EntityId, api);
        }

        public override void DidModifyItemSlot(ItemSlot slot, ItemStack extractedStack = null)
        {
            pos = ship.HearthBlockPos();
            // the pot taken out with something still in it: vanilla drops that at the firepit; aboard it would fall through
            // her deck, so it goes to the one who took the pot out
            if (Api?.Side == EnumAppSide.Server && slot == this[1] && slot.Itemstack?.ItemAttributes?["storageType"].Exists != true)
                ship.RescueCookingSlots(this);
            base.DidModifyItemSlot(slot, extractedStack);
        }
    }

    /// <summary>
    /// The ship's firehearth (config Hearth; a fixture of type "hearth"): vanilla's firepit, which only works as a block
    /// in the world, rebuilt aboard with vanilla's own pieces. Its inventory is vanilla's (<see cref="HearthInventory"/>),
    /// its fire vanilla's burn tick ported number for number (<see cref="HearthFire"/>) and run on the server against the
    /// inventory through vanilla's own collectible methods (melting points and durations, CanSmelt, DoSmelt, the cooking
    /// pot's recipes, item temperatures); it's lit as vanilla's is (a firestarter or a lit torch held to it for 3 s, with
    /// fuel in it); its contents and fire are saved with her and sent to clients in WatchedAttributes "hansaHearth"
    /// (twice a second while it burns, as vanilla's firepit sends itself). Clients draw it (ShipRenderer: the firepit's
    /// own model as it burns and what's cooking on it; HearthClient: flames, smoke, the crackle; the light), open its
    /// dialog (HearthDialog) and light it (ShipClientSystem). It warms whoever is near it (ShipShelter.HearthHeat).
    /// </summary>
    public partial class EntityShip
    {
        public const string HearthInventoryClass = "hansahearth";

        /// <summary>Her firehearth (null: she has none), its vanilla inventory, and its fire (the server's; on clients as it was sent).</summary>
        public FixtureDef HearthFixture { get; private set; }
        public HearthInventory HearthInv { get; private set; }
        public readonly HearthFire Fire = new();
        /// <summary>The hearth works (server: config Hearth; clients: as the server says). Off, it doesn't burn and can't be lit.</summary>
        public bool HearthOn = true;
        /// <summary>Client: bumped whenever the hearth's state comes in (the dialog and what's drawn look again).</summary>
        public int HearthVersion;

        HearthContents hearthContents;
        float hearthAccum, hearthSyncAccum, hearthSentTemp = float.NaN;
        bool loadingHearth;
        // server: when each player last tried to light it (one try in a few seconds, as holding it to the fire takes)
        readonly Dictionary<string, long> hearthTries = new();
        // (review H1-25) when each player began holding something to it, as the server saw it
        // (review H2-10: with what, in which hotbar slot)
        readonly Dictionary<string, (long Ms, int Slot, string Igniter)> hearthHeldSince = new();

        void InitHearth()
        {
            hearthReady = true;
            // (0.3.18, note 165) the design's hearth, or one put aboard (ShipPieceFire)
            SetHearth(Def.Fixtures.FirstOrDefault(f => f.Type == "hearth") ?? PlacedHearth);
        }

        /// <summary>Her hearth is set up (InitHearth ran): a hearth put aboard or taken up after that is set up or gone at once.</summary>
        bool hearthReady;

        /// <summary>(0.3.18) Her hearth is this fixture (the design's, or a placed hearth's stand-in), or none: its inventory made and loaded, or gone.</summary>
        void SetHearth(FixtureDef f)
        {
            if (f == HearthFixture && (f == null) == (HearthInv == null)) return;
            HearthFixture = f;
            if (f == null)
            {
                HearthInv = null;
                hearthContents = null;
                HearthVersion++;
                return;
            }
            if (HearthInv == null)
            {
                HearthInv = new HearthInventory(this, Api);
                if (Api.Side == EnumAppSide.Server)
                {
                    hearthContents = new HearthContents(this);
                    HearthInv.SlotModified += _ => { if (!loadingHearth) SaveHearth(true); };
                }
                LoadHearth();
            }
            HearthVersion++;
        }

        /// <summary>The world block the hearth's firepit stands in (vanilla's firepit position: where its cooking slots' contents would drop).</summary>
        public BlockPos HearthBlockPos()
        {
            var f = HearthFixture;
            if (f == null) return Pos.AsBlockPos;
            LocalToWorld(f.Pos[0], f.Pos[1] + 0.25, f.Pos[2], out double x, out double y, out double z);
            return new BlockPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z), Pos.Dimension);
        }

        /// <summary>Is the fuel slot empty?</summary>
        public bool HearthFuelEmpty => HearthInv == null || HearthInv[0].Empty;

        /// <summary>Can a firestarter or a lit torch be held to it now (it works, there's fuel in it, it isn't burning)?</summary>
        public bool HearthIgnitable => HearthInv != null && HearthOn && !GoneDown && HearthFlued && Fire.IgnitableState(0, HearthFuelEmpty) == HearthFire.IgniteState.Ignitable;

        /// <summary>What lights a fire (vanilla): 1 a firestarter (one try in four catches), 2 a lit torch or anything else that ignites (always); 0 nothing.</summary>
        public static int Igniter(ItemStack stack)
        {
            if (stack?.Collectible is ItemFirestarter) return 1;
            if (stack?.Block != null && stack.Block.HasBlockBehavior<BlockBehaviorCanIgnite>()) return 2;
            return 0;
        }

        // ------------------------------------------------------------------ the fire (server)

        /// <summary>
        /// Server, every tick: vanilla's burn tick every 100 ms (<see cref="HearthFire.Tick"/>), and the hearth sent to
        /// clients twice a second while it burns or its heat changes (vanilla's firepit marks itself dirty so).
        /// </summary>
        void HearthTick(float dt)
        {
            if (HearthInv == null) return;
            hearthAccum += dt;
            if (hearthAccum < 0.1f) return;
            float step = hearthAccum;
            hearthAccum = 0;
            bool on = Mod.Config.Hearth;
            if (on != HearthOn)
            {
                HearthOn = on;
                SaveState(true);
            }
            if (!on || !HearthFlued)
            {
                // switched off: it goes out (what's in it stays, and can be taken out)
                // (0.3.18: a placed hearth whose flue no longer reaches open air goes out as well)
                if (Fire.IsBurning || Fire.CanIgniteFuel || Fire.BurnState != "cold")
                {
                    Fire.Douse();
                    SaveHearth(true);
                }
                return;
            }
            string state = Fire.BurnState;
            HearthInv.pos = HearthBlockPos();
            Fire.Tick(step, hearthContents);
            hearthSyncAccum += step;
            if (Fire.BurnState != state) SaveHearth(true);
            else if (hearthSyncAccum >= 0.5f)
            {
                hearthSyncAccum = 0;
                if (Fire.IsBurning || Fire.FurnaceTemperature != hearthSentTemp) SaveHearth(true);
            }
        }

        /// <summary>
        /// The hearth's contents seen as vanilla's firepit sees its own: vanilla's collectible methods on its inventory (the
        /// input's temperature is its ingredients' in a pot, the coldest of them; vanilla's GetTemp and SetTemp).
        /// </summary>
        public class HearthContents : IHearthContents
        {
            readonly EntityShip ship;
            public HearthContents(EntityShip ship) { this.ship = ship; }

            HearthInventory Inv => ship.HearthInv;
            IWorldAccessor World => ship.World;
            ItemSlot FuelSlot => Inv[0];
            ItemSlot InputSlot => Inv[1];
            ItemSlot OutputSlot => Inv[2];

            public bool Fuel(out int burnTemperature, out float burnDuration, out float smokeLevel)
            {
                var stack = FuelSlot.Itemstack;
                var props = stack?.Collectible.GetCombustibleProperties(World, stack, null);
                burnTemperature = props?.BurnTemperature ?? 0;
                burnDuration = props?.BurnDuration ?? 0;
                smokeLevel = props?.SmokeLevel ?? 0;
                return props != null;
            }

            public void TakeFuel()
            {
                var stack = FuelSlot.Itemstack;
                if (stack == null) return;
                stack.StackSize--;
                if (stack.StackSize <= 0) FuelSlot.Itemstack = null;
                FuelSlot.MarkDirty();
            }

            public bool InputEmpty => InputSlot.Empty;

            public bool CanSmeltInput()
            {
                var input = InputSlot.Itemstack;
                if (input == null) return false;
                // (a dirty pot empties itself into the cooking slots here: vanilla's firepit then sends itself)
                if (input.Collectible.OnSmeltAttempt(Inv)) ship.SaveHearth(true);
                input = InputSlot.Itemstack;
                if (input == null) return false;
                var props = input.Collectible.GetCombustibleProperties(World, input, null);
                return input.Collectible.CanSmelt(World, Inv, input, OutputSlot.Itemstack) && (props == null || !props.RequiresContainer);
            }

            public bool InputAllowsHeating => InputSlot.Itemstack?.ItemAttributes?["allowHeating"].AsBool(false) == true;
            public bool OutputAllowsHeating => OutputSlot.Itemstack?.ItemAttributes?["allowHeating"].AsBool(false) == true;

            public float InputTemp
            {
                get => GetTemp(InputSlot.Itemstack);
                set => SetTemp(InputSlot.Itemstack, value);
            }

            public float OutputTemp
            {
                get => GetTemp(OutputSlot.Itemstack);
                set => SetTemp(OutputSlot.Itemstack, value);
            }

            float GetTemp(ItemStack stack)
            {
                if (stack == null) return HearthFire.EnvironmentTemperature;
                var cooking = Inv.CookingSlots;
                if (cooking.Length != 0)
                {
                    bool any = false;
                    float t = 0;
                    foreach (var s in cooking)
                    {
                        if (s.Itemstack == null) continue;
                        float st = s.Itemstack.Collectible.GetTemperature(World, s.Itemstack);
                        t = any ? Math.Min(t, st) : st;
                        any = true;
                    }
                    return t;
                }
                return stack.Collectible.GetTemperature(World, stack);
            }

            void SetTemp(ItemStack stack, float value)
            {
                if (stack == null) return;
                var cooking = Inv.CookingSlots;
                if (cooking.Length != 0)
                {
                    foreach (var s in cooking) s.Itemstack?.Collectible.SetTemperature(World, s.Itemstack, value, true);
                }
                else stack.Collectible.SetTemperature(World, stack, value, true);
            }

            public float InputMeltingPoint => InputSlot.Itemstack?.Collectible.GetMeltingPoint(World, Inv, InputSlot) ?? 0;
            public int InputStackSize => InputSlot.Itemstack?.StackSize ?? 1;
            public int InputMaxTemperature => MaxTemp(InputSlot.Itemstack);
            public int OutputMaxTemperature => MaxTemp(OutputSlot.Itemstack);

            int MaxTemp(ItemStack stack)
            {
                if (stack == null) return 0;
                int props = stack.Collectible.GetCombustibleProperties(World, stack, null)?.MaxTemperature ?? 0;
                return Math.Max(props, stack.ItemAttributes?["maxTemperature"].AsInt(0) ?? 0);
            }

            public float MaxCookingTime => InputSlot.Itemstack != null ? InputSlot.Itemstack.Collectible.GetMeltingDuration(World, Inv, InputSlot) : 30f;

            public void Smelt()
            {
                InputSlot.Itemstack?.Collectible.DoSmelt(World, Inv, InputSlot, OutputSlot);
                InputSlot.MarkDirty();
            }

            public double TotalHours => World.Calendar.TotalHours;
        }

        // ------------------------------------------------------------------ using it (server)

        /// <summary>
        /// Right-click on the hearth (server), as vanilla's firepit takes it: Shift with fuel or something to cook puts one
        /// in; a cooking pot (or a bowl) goes in as the pot, or a bowl is served from the cooked pot in it; a crucible goes
        /// in; anything else opens it. (Lighting it is held right-click with a firestarter or a lit torch:
        /// <see cref="TryIgniteHearth"/>.)
        /// </summary>
        public void UseHearth(IServerPlayer player, FixtureDef f, bool sneak)
        {
            if (HearthInv == null) return;
            var slot = player.InventoryManager.ActiveHotbarSlot;
            var held = slot?.Itemstack;
            if (held != null && !GoneDown)
            {
                bool done = false;
                if (sneak)
                {
                    var props = held.Collectible.GetCombustibleProperties(World, held, null);
                    if (props != null && props.MeltingPoint > 0)
                    {
                        var op = new ItemStackMoveOperation(World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);
                        slot.TryPutInto(HearthInv[1], ref op);
                        done |= op.MovedQuantity > 0;
                    }
                    if (props != null && props.BurnTemperature > 0)
                    {
                        var op = new ItemStackMoveOperation(World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);
                        slot.TryPutInto(HearthInv[0], ref op);
                        done |= op.MovedQuantity > 0;
                    }
                }
                if (!done && held.Collectible.Attributes?.IsTrue("mealContainer") == true)
                {
                    // a bowl: served from a cooked pot in it; a pot: in it goes
                    ItemSlot pot = HearthInv[1].Itemstack?.Collectible is BlockCookedContainer ? HearthInv[1]
                        : HearthInv[2].Itemstack?.Collectible is BlockCookedContainer ? HearthInv[2] : null;
                    if (pot != null)
                    {
                        var meal = (BlockCookedContainer)pot.Itemstack.Collectible;
                        if (slot.StackSize > 1)
                        {
                            var one = new DummySlot(slot.TakeOut(1));
                            slot.MarkDirty();
                            meal.ServeIntoStack(one, pot, World);
                            if (!player.InventoryManager.TryGiveItemstack(one.Itemstack, true)) World.SpawnItemEntity(one.Itemstack, player.Entity.Pos.XYZ);
                        }
                        else meal.ServeIntoStack(slot, pot, World);
                        slot.MarkDirty();
                        pot.MarkDirty();
                        done = true;
                    }
                    else if (HearthInv[1].Empty && slot.TryPutInto(World, HearthInv[1], 1) > 0) done = true;
                }
                if (!done && held.Collectible is BlockSmeltingContainer or BlockSmeltedContainer && slot.TryPutInto(World, HearthInv[1], 1) > 0) done = true;
                if (done)
                {
                    string place = held.ItemAttributes?["placeSound"].AsString(null);
                    LocalToWorld(f.Pos[0], f.Pos[1] + 0.3, f.Pos[2], out double x, out double y, out double z);
                    if (place != null)
                        World.PlaySoundAt(AssetLocation.Create(place, held.Collectible.Code.Domain).WithPathPrefixOnce("sounds/"), x, y, z, null,
                            0.88f + (float)World.Rand.NextDouble() * 0.24f, 16);
                    return;
                }
            }
            player.InventoryManager.OpenInventory(HearthInv);
            Mod.SendOpenStorage(player, this, f.Index);
        }

        /// <summary>
        /// A firestarter or a lit torch held to the hearth for this many seconds (the client counts them; the server allows
        /// one try in a few seconds). As vanilla: there must be fuel in it and it mustn't be burning; held more than 3 s a
        /// torch lights it, a firestarter one time in four (roll: 0..1, under the chance catches) and loses a point of
        /// durability when it does. Then the fuel catches at the next burn tick. Returns an error lang code, or null.
        /// </summary>
        public string TryIgniteHearth(EntityAgent by, ItemSlot slot, float seconds, double roll)
        {
            if (HearthInv == null) return "hansa:hearth-none";
            if (!HearthOn) return "hansa:hearth-off";
            if (GoneDown) return "hansa:gone-down";
            // (0.3.18, note 165) a hearth put aboard draws only with a flue up to open air
            if (!HearthFlued) return "hansa:hearth-no-flue";
            int kind = Igniter(slot?.Itemstack);
            if (kind == 0) return "hansa:hearth-needs-igniter";
            var state = Fire.IgnitableState(seconds, HearthFuelEmpty);
            if (state == HearthFire.IgniteState.NotIgnitablePreventDefault) return Fire.IsBurning ? "hansa:hearth-burning" : "hansa:hearth-no-fuel";
            if (state != HearthFire.IgniteState.IgniteNow) return "hansa:hearth-not-yet";
            if (kind == 1)
            {
                if (roll > HearthFire.FirestarterChance) return "hansa:hearth-no-catch";
                slot.Itemstack.Collectible.DamageItem(World, by, slot, 1);
            }
            Fire.Ignited(World.Calendar.TotalHours);
            SaveHearth(true);
            return null;
        }

        /// <summary>Server: a player has begun holding a firestarter or a torch to it (HearthPacket.Start).</summary>
        public void HearthHeldBy(IServerPlayer player)
        {
            // (review H2-10) only with a firestarter or a torch in hand
            var held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (Igniter(held) == 0) { hearthHeldSince.Remove(player.PlayerUID); return; }
            hearthHeldSince[player.PlayerUID] = (World.ElapsedMilliseconds, player.InventoryManager.ActiveHotbarSlotNumber, held.Collectible.Code.ToString());
        }

        /// <summary>(review H3-8) Server: a player out of reach of it, or refused it: their holding something to it ends.</summary>
        public void HearthLeftBy(IServerPlayer player) => hearthHeldSince.Remove(player.PlayerUID);

        /// <summary>Server: a player's try at lighting it (HearthPacket), at most one in 2.5 s each.</summary>
        public void HearthIgniteFrom(IServerPlayer player, float seconds)
        {
            long now = World.ElapsedMilliseconds;
            // (review H1-25) no longer than the server has seen them hold it there (a little for the lag); counted afresh from
            // now, as their client does
            // (review H2-10) not counted from a start long ago (their client tries again every 3.2 s while it's held), nor
            // with something else in hand since
            var hand = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            int slot = player.InventoryManager.ActiveHotbarSlotNumber;
            string igniter = hand?.Collectible?.Code?.ToString();
            double held = 0;
            if (hearthHeldSince.TryGetValue(player.PlayerUID, out var since) && since.Slot == slot && since.Igniter == igniter &&
                now - since.Ms <= (HearthFire.IgniteSeconds + 2) * 1000)
                held = (now - since.Ms) / 1000.0 + 0.3;
            if (Igniter(hand) != 0) hearthHeldSince[player.PlayerUID] = (now, slot, igniter);
            else hearthHeldSince.Remove(player.PlayerUID);
            seconds = (float)Math.Min(seconds, held);
            if (hearthTries.TryGetValue(player.PlayerUID, out long last) && now - last < 2500) return;
            hearthTries[player.PlayerUID] = now;
            string err = TryIgniteHearth(player.Entity, player.InventoryManager.ActiveHotbarSlot, seconds, World.Rand.NextDouble());
            if (err != null) player.SendIngameError("hansa-hearth", Lang.GetL(player.LanguageCode, err));
        }

        /// <summary>The sea in her (she's going down): the fire's out and cold, as water puts out vanilla's firepit.</summary>
        void DouseHearth()
        {
            if (HearthInv == null || !Fire.IsBurning && !Fire.CanIgniteFuel && Fire.BurnState == "cold") return;
            Fire.Douse();
            var f = HearthFixture;
            LocalToWorld(f.Pos[0], f.Pos[1] + 0.3, f.Pos[2], out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/effect/extinguish"), x, y, z, null, true, 16);
            SaveHearth(true);
        }

        /// <summary>
        /// Server: a pot taken out of the hearth with something left in its cooking slots: that goes to a player who has the
        /// hearth open (it was one of them), else it drops at the hearth (as vanilla drops it at its firepit).
        /// </summary>
        public void RescueCookingSlots(HearthInventory inv)
        {
            foreach (var slot in inv.Slots)
            {
                var stack = slot.Itemstack;
                if (stack == null) continue;
                slot.Itemstack = null;
                IServerPlayer to = null;
                foreach (string uid in inv.openedByPlayerGUIds)
                    if (World.PlayerByUid(uid) is IServerPlayer p && p.Entity != null) { to = p; break; }
                if (to != null && to.InventoryManager.TryGiveItemstack(stack, true)) continue;
                World.SpawnItemEntity(stack, to?.Entity?.Pos.XYZ ?? HearthBlockPos().ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }

        // ------------------------------------------------------------------ saved with her, and sent to clients

        /// <summary>The hearth into WatchedAttributes "hansaHearth" (its inventory and its fire), and to clients if sync.</summary>
        public void SaveHearth(bool sync)
        {
            if (HearthInv == null) return;
            var t = new TreeAttribute();
            var inv = new TreeAttribute();
            HearthInv.ToTreeAttributes(inv);
            t["inv"] = inv;
            Fire.ToTree(t);
            WatchedAttributes["hansaHearth"] = t;
            if (sync)
            {
                WatchedAttributes.MarkPathDirty("hansaHearth");
                hearthSentTemp = Fire.FurnaceTemperature;
            }
        }

        void LoadHearth()
        {
            if (HearthInv == null) return;
            if (WatchedAttributes.GetTreeAttribute("hansaHearth") is ITreeAttribute t)
            {
                loadingHearth = true;
                try
                {
                    if (t.GetTreeAttribute("inv") is ITreeAttribute inv)
                    {
                        HearthInv.FromTreeAttributes(inv);
                        HearthInv.AfterBlocksLoaded(World);
                    }
                    Fire.FromTree(t);
                }
                finally { loadingHearth = false; }
            }
            HearthVersion++;
        }

        // ------------------------------------------------------------------ what clients show

        /// <summary>
        /// The values vanilla's firepit dialog shows (BlockEntityFirepit.SetDialogValues): the fire's temperature and fuel,
        /// the input's temperature and cooking time, what it will make, and whether a pot opens the cooking slots.
        /// </summary>
        public void HearthDialogValues(ITreeAttribute t)
        {
            if (HearthInv == null) return;
            t.SetFloat("furnaceTemperature", Fire.FurnaceTemperature);
            t.SetInt("maxTemperature", Fire.MaxTemperature);
            t.SetFloat("oreCookingTime", Fire.InputStackCookingTime);
            t.SetFloat("maxFuelBurnTime", Fire.MaxFuelBurnTime);
            t.SetFloat("fuelBurnTime", Fire.FuelBurnTime);
            var input = HearthInv[1];
            if (input.Itemstack != null)
            {
                t.SetFloat("oreTemperature", HearthInputTemp());
                t.SetFloat("maxOreCookingTime", input.Itemstack.Collectible.GetMeltingDuration(World, HearthInv, input));
            }
            else t.RemoveAttribute("oreTemperature");
            string text = null;
            try { text = HearthInv.GetOutputText(); } catch (Exception) { }
            t.SetString("outputText", text ?? "");
            t.SetInt("haveCookingContainer", HearthInv.HaveCookingContainer ? 1 : 0);
            t.SetInt("quantityCookingSlots", HearthInv.CookingSlots.Length);
        }

        /// <summary>The input's temperature as vanilla's firepit reads it (a pot's: its ingredients', the coldest; 20 with nothing in).</summary>
        public float HearthInputTemp()
        {
            var stack = HearthInv?[1].Itemstack;
            if (stack == null) return HearthFire.EnvironmentTemperature;
            var cooking = HearthInv.CookingSlots;
            if (cooking.Length == 0) return stack.Collectible.GetTemperature(World, stack);
            bool any = false;
            float t = 0;
            foreach (var s in cooking)
            {
                if (s.Itemstack == null) continue;
                float st = s.Itemstack.Collectible.GetTemperature(World, s.Itemstack);
                t = any ? Math.Min(t, st) : st;
                any = true;
            }
            return t;
        }

        /// <summary>How the hearth stands, for the look-at box and the hint: "burning, 640 °C", "embers", "cold".</summary>
        public string HearthStateText()
        {
            if (HearthInv == null) return "";
            if (!HearthOn) return Lang.Get("hansa:hearth-state-off");
            if (Fire.IsBurning) return Lang.Get("hansa:hearth-state-burning", (int)Fire.FurnaceTemperature);
            if (Fire.BurnState == "extinct" && Fire.CanIgniteFuel) return Lang.Get("hansa:hearth-state-embers");
            return Fire.FurnaceTemperature > HearthFire.EnvironmentTemperature + 1
                ? Lang.Get("hansa:hearth-state-cooling", (int)Fire.FurnaceTemperature) : Lang.Get("hansa:hearth-state-cold");
        }

        /// <summary>The look-at box on the hearth: how it stands, and what's cooking (its temperature).</summary>
        void HearthInfo(System.Text.StringBuilder sb)
        {
            if (HearthInv == null) return;
            string state = HearthStateText();
            sb.AppendLine(state.Length > 0 ? char.ToUpperInvariant(state[0]) + state.Substring(1) : state);
            var input = HearthInv[1].Itemstack;
            if (input != null) sb.AppendLine(Lang.Get("hansa:hearth-info-input", input.GetName(), (int)HearthInputTemp()));
            var output = HearthInv[2].Itemstack;
            if (output != null) sb.AppendLine(Lang.Get("hansa:hearth-info-output", output.StackSize, output.GetName()));
        }
    }
}
