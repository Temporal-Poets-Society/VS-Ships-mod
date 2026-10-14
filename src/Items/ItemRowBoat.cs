using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// Hansa's rowing boat as an item: vanilla's boat item (right-click the water and she's put in there, her bow
    /// the way you look), except that not into a ship: looking down over the side from a deck the water under the hull
    /// is under the crosshair too, and a boat put there would be inside the ship.
    /// (0.3.19) Vanilla's item puts one in at every start of a use, the first press or the use starting again while the
    /// button stays down: one per press now, and not again within <see cref="LaunchGuard.CooldownMs"/> (<see cref="LaunchGuard"/>).
    /// </summary>
    public class ItemRowBoat : ItemBoat
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            // (0.3.19) one boat per click: the use going on while the button stays down (firstEvent false) does nothing
            if (blockSel != null && !firstEvent) { handling = EnumHandHandling.PreventDefault; return; }
            if (blockSel != null)
            {
                var mod = api.ModLoader.GetModSystem<HansaMod>();
                var ships = api.Side == EnumAppSide.Server ? mod?.ServerShips : (System.Collections.Generic.IEnumerable<EntityShip>)mod?.Client?.Ships;
                var bp = blockSel.Position;
                double x = bp.X + 0.5, y = bp.Y + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Y), z = bp.Z + 0.5;
                if (ships != null && EntityRowBoat.TouchesAnyShip(ships, x, y, z, byEntity.Pos.Yaw, byEntity.Pos.Dimension))
                {
                    handling = EnumHandHandling.PreventDefault;
                    if (api is ICoreClientAPI capi && firstEvent) capi.TriggerIngameError(this, "hansa-boat", Lang.Get("hansa:boat-no-room"));
                    return;
                }
            }
            // (0.3.19) and not again within the cooldown (the server decides; the client would draw one that isn't there)
            if (blockSel != null && api.Side == EnumAppSide.Server && !LaunchGuard.TryLaunch(api, byEntity, -1, out _)) { handling = EnumHandHandling.PreventDefault; return; }
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get("hansa:rowboat-help"));
        }
    }
}
