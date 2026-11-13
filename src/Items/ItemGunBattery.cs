using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// (13 November 2026, SH-0012, the author: the caravel craftable without cannons) The caravel's battery bundle (five
    /// bronze guns), which her charter took till 0.1.1: no longer made, kept registered so one in a 0.1.0 save still loads.
    /// Right-click with it in hand unpacks it into what its attribute "unpacks" names (the guns, one to a stack), each one
    /// given to the player or dropped at their feet.
    /// </summary>
    public class ItemGunBattery : Item
    {
        JsonItemStack unpacks;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            unpacks = Attributes?["unpacks"].AsObject<JsonItemStack>(null, Code.Domain);
            if (unpacks != null && !unpacks.Resolve(api.World, "ships battery's unpacks")) unpacks = null;
        }

        /// <summary>What one battery holds, each stack no bigger than its item stacks (a bronze gun: one).</summary>
        public List<ItemStack> Contents()
        {
            var stacks = new List<ItemStack>();
            var one = unpacks?.ResolvedItemstack;
            if (one == null) return stacks;
            int left = unpacks.Quantity, max = System.Math.Max(1, one.Collectible.MaxStackSize);
            while (left > 0)
            {
                var s = one.Clone();
                s.StackSize = System.Math.Min(left, max);
                left -= s.StackSize;
                stacks.Add(s);
            }
            return stacks;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            // a new press only (as the charter: the game starts the use again while the button stays down)
            if (!firstEvent || byEntity == null || unpacks?.ResolvedItemstack == null) return;
            handling = EnumHandHandling.PreventDefault;
            if (api.Side != EnumAppSide.Server || slot.Itemstack == null) return;
            var contents = Contents();
            slot.TakeOut(1);
            slot.MarkDirty();
            var player = (byEntity as EntityPlayer)?.Player as IServerPlayer;
            foreach (var s in contents)
                if (player?.InventoryManager.TryGiveItemstack(s, true) != true)
                    api.World.SpawnItemEntity(s, byEntity.Pos.XYZ.Add(0, 0.5, 0));
            api.World.PlaySoundAt(new AssetLocation("sounds/block/anvil"), byEntity, null, true, 16, 0.6f);
            player?.SendMessage(GlobalConstants.GeneralChatGroup,
                Lang.GetL(player.LanguageCode, "ships:shipkit-unpacked", unpacks.Quantity, unpacks.ResolvedItemstack.GetName()), EnumChatType.Notification);
        }
    }
}
