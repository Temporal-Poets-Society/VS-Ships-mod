using System;
using System.Linq;
using System.Text;
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
    /// The ship's clay oven (config Oven; a fixture of type "oven", the user's note 51): vanilla's clay oven, which only
    /// works as a block in the world, rebuilt aboard as the firehearth was. Its inventory is vanilla's (InventoryOven: its
    /// first slot the fuel or a thing to bake, four to bake in), its fire and baking vanilla's ported number for number
    /// (<see cref="OvenFire"/>) and run on the server against it through vanilla's own collectible methods (BakingProperties,
    /// combustible properties, smelting, freshness carried over); used as vanilla's is (right-click: firewood in, up to six,
    /// while it's empty and cold; something bakeable in, up to four, while it's out; an empty hand takes the last thing
    /// out; a lit torch or a firestarter lights its fuel); saved with her and sent to clients in WatchedAttributes
    /// "hansaOven" (twice a second while it changes). Clients draw it (ShipRenderer: vanilla's oven block, its fuel and
    /// what's baking as vanilla draws them), its crackle and its smoke out of the hearth's stovepipe (HearthClient).
    /// </summary>
    public partial class EntityShip
    {
        public const string OvenInventoryClass = "hansaoven";

        /// <summary>Her clay oven (null: she has none), its vanilla inventory, its fire (the server's; on clients as sent).</summary>
        public FixtureDef OvenFixture { get; private set; }
        public InventoryOven OvenInv { get; private set; }
        public readonly OvenFire Oven = new();
        /// <summary>The oven works (server: config Oven; clients: as the server says).</summary>
        public bool OvenOn = true;
        /// <summary>Client: bumped whenever the oven's state comes in (what's drawn looks again).</summary>
        public int OvenVersion;

        OvenContents ovenContents;
        float ovenAccum, ovenSyncAccum, ovenSentTemp = float.NaN;
        bool loadingOven;
        ItemStack ovenLastRemoved;

        void InitOven()
        {
            ovenReady = true;
            // (0.3.18, note 165) the design's oven, or one put aboard (ShipPieceFire)
            SetOven(Def.Fixtures.FirstOrDefault(f => f.Type == "oven") ?? PlacedOven);
        }

        bool ovenReady;

        /// <summary>(0.3.18) Her oven is this fixture (the design's, or a placed oven's stand-in), or none.</summary>
        void SetOven(FixtureDef f)
        {
            if (f == OvenFixture && (f == null) == (OvenInv == null)) return;
            OvenFixture = f;
            if (f == null)
            {
                OvenInv = null;
                ovenContents = null;
                OvenVersion++;
                return;
            }
            if (OvenInv == null)
            {
                OvenInv = new InventoryOven(OvenInventoryClass + "-" + EntityId, OvenFire.Capacity);
                OvenInv.LateInitialize(OvenInventoryClass + "-" + EntityId, Api);
                if (Api.Side == EnumAppSide.Server)
                {
                    ovenContents = new OvenContents(this);
                    OvenInv.SlotModified += _ => { if (!loadingOven) SaveOven(true); };
                }
                LoadOven();
            }
            OvenVersion++;
        }

        /// <summary>The world block the oven stands in (where vanilla drops what can't be handed over).</summary>
        public BlockPos OvenBlockPos()
        {
            var f = OvenFixture;
            if (f == null) return Pos.AsBlockPos;
            LocalToWorld(f.Pos[0], f.Pos[1] + 0.25, f.Pos[2], out double x, out double y, out double z);
            return new BlockPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z), Pos.Dimension);
        }

        /// <summary>How many logs of fuel lie in it (vanilla's HasFuel: its first slot's, isClayOvenFuel).</summary>
        public int OvenFuel => OvenInv?[0].Itemstack?.Collectible?.Attributes?.IsTrue("isClayOvenFuel") == true ? OvenInv[0].StackSize : 0;

        /// <summary>Is there something to bake in it (not the fuel)?</summary>
        public bool OvenHasBakeables
        {
            get
            {
                if (OvenInv == null) return false;
                for (int i = 0; i < OvenFire.Capacity; i++) if (!OvenInv[i].Empty && (i != 0 || OvenFuel == 0)) return true;
                return false;
            }
        }

        /// <summary>Can a firestarter or a lit torch light it now?</summary>
        public bool OvenIgnitable => OvenInv != null && OvenOn && !GoneDown && Oven.CanIgnite(OvenFuel);

        /// <summary>Server, every tick: vanilla's oven tick every 100 ms, and the oven sent to clients twice a second while it changes.</summary>
        void OvenTick(float dt)
        {
            if (OvenInv == null) return;
            ovenAccum += dt;
            if (ovenAccum < 0.1f) return;
            float step = ovenAccum;
            ovenAccum = 0;
            bool on = Mod.Config.Oven;
            if (on != OvenOn)
            {
                OvenOn = on;
                SaveState(true);
            }
            int env = ovenContents.EnvironmentTemperature;
            if (!on || GoneDown)
            {
                if (Oven.Burning || Oven.Temperature > env + 1)
                {
                    Oven.Douse(env);
                    SaveOven(true);
                }
                return;
            }
            bool burning = Oven.Burning;
            OvenInv.pos = OvenBlockPos();
            bool changed = Oven.Tick(step, ovenContents);
            ovenSyncAccum += step;
            if (changed || burning != Oven.Burning) SaveOven(true);
            else if (ovenSyncAccum >= 0.5f)
            {
                ovenSyncAccum = 0;
                if (Oven.Burning || Math.Abs(Oven.Temperature - ovenSentTemp) > 0.5f) SaveOven(true);
            }
        }

        /// <summary>The oven's contents as vanilla's oven sees its own: vanilla's collectible methods on its inventory.</summary>
        class OvenContents : IOvenContents
        {
            readonly EntityShip ship;
            public OvenContents(EntityShip ship) { this.ship = ship; }

            IWorldAccessor World => ship.World;
            InventoryOven Inv => ship.OvenInv;

            public int FuelCount => ship.OvenFuel;

            public void FuelBurntOut()
            {
                var slot = Inv[0];
                var cp = slot.Itemstack?.Collectible.GetCombustibleProperties(World, slot.Itemstack, null);
                if (cp?.SmeltedStack?.ResolvedItemstack == null) slot.Itemstack = null;
                else
                {
                    int n = slot.StackSize;
                    slot.Itemstack = cp.SmeltedStack.ResolvedItemstack.Clone();
                    slot.Itemstack.StackSize = n * cp.SmeltedRatio;
                }
                slot.MarkDirty();
            }

            public bool Has(int i) => !Inv[i].Empty && (i != 0 || FuelCount == 0);

            public int MaxTemperature(int i)
            {
                var st = Inv[i].Itemstack;
                if (st == null) return 0;
                int a = st.Collectible.GetCombustibleProperties(World, st, null)?.MaxTemperature ?? 0;
                return Math.Max(a, st.ItemAttributes?["maxTemperature"].AsInt(0) ?? 0);
            }

            public bool Baking(int i, out float levelFrom, out float levelTo, out float startScaleY, out float endScaleY)
            {
                var bp = BakingProperties.ReadFrom(Inv[i].Itemstack);
                levelFrom = bp?.LevelFrom ?? 0;
                levelTo = bp?.LevelTo ?? 1;
                startScaleY = bp?.StartScaleY ?? 1;
                endScaleY = bp?.EndScaleY ?? 1;
                return bp != null;
            }

            /// <summary>Baked through: vanilla's IncrementallyBake's end (its result by BakingProperties, else its smelting).</summary>
            public bool Bake(int i)
            {
                var slot = Inv[i];
                var stack = slot.Itemstack;
                if (stack == null) return false;
                float temp = ship.Oven.Slots[i].Temp;
                var bp = BakingProperties.ReadFrom(stack);
                ItemStack result = null;
                if (bp?.ResultCode != null)
                {
                    if (stack.Class == EnumItemClass.Block) { var b = World.GetBlock(new AssetLocation(bp.ResultCode)); if (b != null) result = new ItemStack(b); }
                    else { var it = World.GetItem(new AssetLocation(bp.ResultCode)); if (it != null) result = new ItemStack(it); }
                    if (result != null)
                    {
                        var perish = result.Collectible.GetTransitionableProperties(World, stack, null)?.FirstOrDefault(p => p.Type == EnumTransitionType.Perish);
                        if (perish != null) CollectibleObject.CarryOverFreshness(ship.Api, slot, result, perish);
                        stack.Collectible.GetCollectibleInterface<IBakeableCallback>()?.OnBaked(stack, result);
                    }
                }
                else
                {
                    var into = new DummySlot(null);
                    if (stack.Collectible.CanSmelt(World, Inv, stack, null))
                    {
                        stack.Collectible.DoSmelt(World, Inv, slot, into);
                        result = into.Itemstack;
                    }
                }
                if (result == null) return false;
                slot.Itemstack = result;
                ship.Oven.Slots[i] = SlotFor(result, World);
                ship.Oven.Slots[i].Temp = temp;
                slot.MarkDirty();
                return true;
            }

            public int EnvironmentTemperature
            {
                get
                {
                    try { return (int)World.BlockAccessor.GetClimateAt(ship.OvenBlockPos(), EnumGetClimateMode.NowValues, World.Calendar.TotalDays).Temperature; }
                    catch (Exception) { return 20; }
                }
            }
        }

        /// <summary>A thing put in to bake: vanilla's OvenItemData(stack).</summary>
        static OvenSlot SlotFor(ItemStack stack, IWorldAccessor world)
        {
            var bp = BakingProperties.ReadFrom(stack);
            var cp = stack.Collectible.GetCombustibleProperties(world, stack, null);
            return new OvenSlot(bp?.Temp ?? 160, cp != null ? cp.MeltingDuration * 10 : 150, bp?.LevelFrom ?? 0, bp?.StartScaleY ?? 1, 20);
        }

        // ------------------------------------------------------------------ using it (server: a right-click on it)

        /// <summary>Is this bakeable in vanilla's oven (BakingProperties, or it cooks below 260 °C)?</summary>
        bool OvenBakeable(ItemStack stack)
        {
            var c = stack?.Collectible;
            if (c == null) return false;
            var cp = c.GetCombustibleProperties(World, stack, null);
            return c.Attributes?["bakingProperties"].Exists == true || cp != null && cp.SmeltingType == EnumSmeltType.Bake && cp.MeltingPoint < 260;
        }

        /// <summary>
        /// Vanilla's oven's OnInteract: an empty hand takes the last thing out (not while it burns); firewood goes in as its
        /// fuel (while it's cold and empty of bakeables, up to six); something bakeable goes in (while it's out and there's no
        /// fuel in it, up to four, a large thing alone); a lit torch or a firestarter lights its fuel. Returns what to tell them.
        /// </summary>
        public string UseOven(IServerPlayer player, FixtureDef f)
        {
            if (OvenInv == null || f != OvenFixture) return null;
            if (!OvenOn) return "ships:oven-off";
            if (GoneDown) return "ships:gone-down";
            var slot = player.InventoryManager.ActiveHotbarSlot;
            var stack = slot?.Itemstack;
            bool done;
            if (stack == null) done = OvenTake(player);
            else if (Igniter(stack) > 0 && Oven.CanIgnite(OvenFuel))
            {
                done = Oven.TryIgnite(OvenFuel);
                if (done && stack.Collectible is ItemFirestarter) stack.Collectible.DamageItem(World, player.Entity, slot);
                if (done) PlayOvenSound("sounds/torch-ignite");
            }
            else if (stack.Collectible.Attributes?.IsTrue("isClayOvenFuel") == true)
            {
                if (Oven.Burning || OvenHasBakeables) return "ships:oven-busy";
                done = OvenInv[0].Empty || OvenInv[0].StackSize < OvenFire.FuelCapacity && OvenInv[0].Itemstack.Equals(World, stack, GlobalConstants.IgnoredStackAttributes)
                    ? slot.TryPutInto(World, OvenInv[0], 1) > 0 : false;
                if (done) { ovenLastRemoved = null; PlayOvenSound("sounds/block/planks"); }
                else return "ships:oven-full";
            }
            else if (OvenBakeable(stack))
            {
                if (stack.Equals(World, ovenLastRemoved, GlobalConstants.IgnoredStackAttributes) && !OvenInv[0].Empty) done = OvenTake(player);
                else
                {
                    done = OvenPut(slot);
                    if (done) PlayOvenSound("sounds/player/buildhigh");
                    else return Oven.Burning ? "ships:oven-burning" : OvenFuel > 0 ? "ships:oven-fuelled" : "ships:oven-full";
                }
            }
            else done = OvenTake(player);
            if (done)
            {
                slot?.MarkDirty();
                SaveOven(true);
            }
            return null;
        }

        bool OvenPut(ItemSlot slot)
        {
            if (Oven.Burning || OvenFuel > 0) return false;
            var bp = BakingProperties.ReadFrom(slot.Itemstack);
            if (bp == null || !slot.Itemstack.Attributes.GetBool("bakeable", true)) return false;
            if (bp.LargeItem && OvenHasBakeables) return false;
            for (int i = 0; i < OvenFire.Capacity; i++)
            {
                if (OvenInv[i].Empty)
                {
                    int moved = slot.TryPutInto(World, OvenInv[i], 1);
                    if (moved > 0)
                    {
                        Oven.Slots[i] = SlotFor(OvenInv[i].Itemstack, World);
                        ovenLastRemoved = null;
                    }
                    return moved > 0;
                }
                if (i == 0 && BakingProperties.ReadFrom(OvenInv[0].Itemstack)?.LargeItem == true) return false;
            }
            return false;
        }

        bool OvenTake(IServerPlayer player)
        {
            if (Oven.Burning) return false;
            for (int i = OvenFire.Capacity; i >= 0; i--)
            {
                if (OvenInv[i].Empty) continue;
                var taken = OvenInv[i].TakeOut(1);
                ovenLastRemoved = taken?.Clone();
                if (!player.InventoryManager.TryGiveItemstack(taken)) { }
                if (taken != null && taken.StackSize > 0)
                {
                    // (what doesn't fit in their hands drops at their feet: aboard, not through her deck)
                    World.SpawnItemEntity(taken, player.Entity.Pos.XYZ);
                }
                if (i < OvenFire.Capacity) Oven.Slots[i].CurHeightMul = 1;
                OvenInv[i].MarkDirty();
                PlayOvenSound("sounds/player/throw");
                return true;
            }
            return false;
        }

        void PlayOvenSound(string sound)
        {
            var f = OvenFixture;
            LocalToWorld(f.Pos[0], f.Pos[1] + 0.5, f.Pos[2], out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation(sound), x, y, z, null, true, 16);
        }

        // ------------------------------------------------------------------ saved with her, and sent to clients

        /// <summary>The oven into WatchedAttributes "hansaOven" (its inventory and its fire), and to clients if sync.</summary>
        public void SaveOven(bool sync)
        {
            if (OvenInv == null) return;
            var t = new TreeAttribute();
            var inv = new TreeAttribute();
            OvenInv.ToTreeAttributes(inv);
            t["inv"] = inv;
            Oven.ToTree(t);
            WatchedAttributes["hansaOven"] = t;
            if (sync)
            {
                WatchedAttributes.MarkPathDirty("hansaOven");
                ovenSentTemp = Oven.Temperature;
            }
        }

        void LoadOven()
        {
            if (OvenInv == null) return;
            if (WatchedAttributes.GetTreeAttribute("hansaOven") is ITreeAttribute t)
            {
                loadingOven = true;
                try
                {
                    if (t.GetTreeAttribute("inv") is ITreeAttribute inv)
                    {
                        OvenInv.FromTreeAttributes(inv);
                        OvenInv.AfterBlocksLoaded(World);
                    }
                    Oven.FromTree(t);
                }
                finally { loadingOven = false; }
            }
            OvenVersion++;
        }

        /// <summary>Its line in the look-at box (vanilla's GetBlockInfo): the heat, a word on preheating or reheating, what's in it and how hot.</summary>
        public void OvenInfo(StringBuilder sb)
        {
            if (OvenInv == null) return;
            if (!OvenOn) { sb.AppendLine(Lang.Get("ships:oven-off")); return; }
            if (Oven.Temperature <= 25)
            {
                sb.AppendLine(Lang.Get("Temperature: {0}", Lang.Get("Cold")));
                if (!Oven.Burning) sb.AppendLine(Lang.Get("clayoven-preheat-warning"));
            }
            else
            {
                sb.AppendLine(Lang.Get("Temperature: {0}°C", (int)Oven.Temperature));
                if (Oven.Temperature < 100 && !Oven.Burning) sb.AppendLine(Lang.Get("Reheat to continue baking"));
            }
            if (OvenFuel > 0) sb.AppendLine(Lang.Get("ships:oven-fuel", OvenFuel, Oven.Burning ? Lang.Get("ships:oven-burning-now") : ""));
            for (int i = 0; i < OvenFire.Capacity; i++)
            {
                if (OvenInv[i].Empty || i == 0 && OvenFuel > 0) continue;
                sb.AppendLine(OvenInv[i].Itemstack.GetName() + " (" + Lang.Get("{0}°C", (int)Oven.Slots[i].Temp) + ")");
            }
        }

        /// <summary>Going down: the oven goes out; what's in it goes with her loose things (ShipWrecks.LootOf).</summary>
        public ItemStack[] OvenStacks() =>
            OvenInv == null ? Array.Empty<ItemStack>() : Enumerable.Range(0, OvenInv.Count).Select(i => OvenInv[i].Itemstack).Where(s => s != null).ToArray();
    }
}
