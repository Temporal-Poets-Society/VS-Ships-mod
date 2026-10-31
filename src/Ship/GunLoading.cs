using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>One powder charge; separate shot is optional. Charge bits preserve what was actually consumed.</summary>
    public static class GunLoading
    {
        public const string Powder="game:blastingpowder", Shot="game:bullets-lead";
        public const byte PowderIn=1, Loaded=3;
        public static IEnumerable<ItemSlot> PlayerInventory(IPlayer player)
        {
            // Open chests and the creative catalogue are not ammunition in the firing player's inventory.
            if(player==null) yield break;
            foreach(string name in new[]{"hotbar","backpack"}) {
                var inventory=player.InventoryManager.GetOwnInventory(name);
                if(inventory!=null) for(int i=0;i<inventory.Count;i++) yield return inventory[i];
            }
        }
        /// <summary>One real inventory item per automatic shot, including creative mode. Never reserve the next shot.</summary>
        public static bool TakePowder(IEnumerable<ItemSlot> inventory)
        {
            if(inventory!=null) foreach(var slot in inventory)
                if(slot?.Itemstack?.Collectible?.Code?.ToString()==Powder && slot.StackSize>0) {
                    slot.TakeOut(1);slot.MarkDirty();return true;
                }
            return false;
        }
        public static bool IsLoad(string code,bool needShot=false) => code==Powder || needShot && code==Shot;
        public static bool Ready(byte charge,bool needShot=false) => needShot ? charge==Loaded : (charge & PowderIn)!=0;
        public static byte After(byte charge,bool needShot=false) => needShot && charge==PowderIn ? Loaded : PowderIn;
        public static string Hint(byte charge,bool needShot=false) => Ready(charge,needShot)?"ships:gun-load-full":charge==0 || !needShot?"ships:gun-load-powder":"ships:gun-load-shot";
        public static string Problem(byte charge,bool runIn,bool moving,string code,int count,int need,bool needShot=false)
        {
            if(!runIn || moving) return "ships:gun-load-run-in";
            if(Ready(charge,needShot)) return "ships:gun-load-full";
            if(charge==0 && code!=Powder) return "ships:gun-load-powder";
            if(charge==PowderIn && code!=Shot) return "ships:gun-load-shot";
            return count<need?"ships:gun-load-more":null;
        }
    }
}
