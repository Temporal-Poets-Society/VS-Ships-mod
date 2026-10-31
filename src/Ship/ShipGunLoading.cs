using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Hansa
{
    public partial class EntityShip
    {
        public bool GunRunIn(FixtureDef gun) => gun!=null && !gun.Swivel && (!GunOpen(gun) || Gun(gun.Code).RunIn);

        /// <summary>Pull back without shutting the port. Loading and housing are separate operations.</summary>
        public string RunGunIn(FixtureDef gun, bool instant = false)
        {
            if(gun?.Type!="gun" || gun.Swivel) return "ships:gun-cannot";
            if(GunAbsent(gun)) return "ships:gunplace-no-gun";
            if(GoneDown) return "ships:gone-down";
            Gun(gun.Code).RunIn=true;
            gunOpenedMs[gun.Code]=World.ElapsedMilliseconds-(instant?RunOutMs:0);
            gunLayers.Remove(gun.Code);
            SaveGunLays(true);
            return null;
        }

        public string TryLoadGun(FixtureDef gun,IServerPlayer player)
        {
            if(gun?.Type!="gun" || player?.Entity==null || !player.Entity.Alive || GunAbsent(gun)) return "ships:gun-cannot";
            if(GoneDown) return "ships:gone-down";
            if(!CannonsOn) return "ships:gun-off";
            if(!ByGun(gun,player.Entity,LayReach)) return "ships:gun-far";
            var slot=player.InventoryManager.ActiveHotbarSlot;
            return LoadGunCharge(gun,slot,Creative(player));
        }

        /// <summary>Server inventory transaction, shared with the dedicated-server tests.</summary>
        public string LoadGunCharge(FixtureDef gun,ItemSlot slot,bool creative)
        {
            if(Api.Side!=EnumAppSide.Server || gun?.Type!="gun" || GunAbsent(gun) || GoneDown || !CannonsOn) return "ships:gun-cannot";
            var held=slot?.Itemstack;
            var st=Gun(gun.Code);
            int need=held?.Collectible?.Code?.ToString()==GunLoading.Shot && !gun.Swivel?4:1;
            string error=GunLoading.Problem(st.Charge,gun.Swivel || GunRunIn(gun),
                gunOpenedMs.TryGetValue(gun.Code,out long at) && World.ElapsedMilliseconds-at<RunOutMs,
                held?.Collectible?.Code?.ToString(),creative?need:held?.StackSize??0,need,Mod.Config.CannonsNeedShot);
            if(error!=null) return error;
            if(!creative) { slot.TakeOut(need);slot.MarkDirty(); }
            st.Charge=GunLoading.After(st.Charge,Mod.Config.CannonsNeedShot);
            SaveGunLays(true);
            return null;
        }

        public string ToggleGunRun(FixtureDef gun)
        {
            if(!GunRunIn(gun)) return RunGunIn(gun);
            // A second Shift-click houses an already run-in gun; ordinary right-click runs it out.
            return HouseGun(gun);
        }
    }
}
