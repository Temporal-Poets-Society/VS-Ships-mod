using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Hansa
{
    /// <summary>
    /// (0.3.23, the user: "maybe for ships stairs better to have a special block") A ship's stair (hansa:shipstair-steep, 2
    /// cells about 56°; hansa:shipstair-long, 3 cells about 45°; 0.1.0-dev.6, note 36: hansa:shipstair-short, 1 cell about 72°): put aboard a ship in building mode it's a walked stair with
    /// its own handrails (<see cref="PiecePlanner"/>). It isn't a block of the world: placing it ashore is refused, saying so.
    /// </summary>
    public class BlockShipStair : Block
    {
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            failureCode = "__ignore__";
            if (world.Side == EnumAppSide.Client && world.Api is ICoreClientAPI capi)
                capi.TriggerIngameError(this, "hansa-shipstair", Lang.Get("hansa:shipstair-aboard"));
            return false;
        }
    }
}
