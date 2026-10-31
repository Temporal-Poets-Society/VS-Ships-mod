using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Hansa
{
    /// <summary>
    /// The surveyor's chalk (creative inventory only, no recipe; it looks like vanilla's lump of chalk): for reporting what
    /// you find. Right-click writes a note about what's in the crosshair (a part of a ship, a block, a creature) with
    /// everything Claude needs to find it; left-click a chalk mark reads its note, Shift + left-click rubs it out. The
    /// client does all of it (<see cref="SurveyorsChalk"/>, before anything else gets the click); here the item only keeps
    /// vanilla from swinging, breaking, placing or using anything with it.
    /// </summary>
    public class ItemSurveyorsChalk : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
        }

        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get("ships:chalk-help"));
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "ships:chalk-hint-write", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "ships:chalk-hint-read", MouseButton = EnumMouseButton.Left },
                new WorldInteraction { ActionLangCode = "ships:chalk-hint-rub", HotKeyCode = "shift", MouseButton = EnumMouseButton.Left },
            };
        }
    }
}
