using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>Native rope systems, pinned to a data-authored ship post. Saved offsets use the same local frame as her decks.</summary>
    public static class ShipTethers
    {
        public static bool AttachToShip(EntityAgent byEntity,EntitySelection toEntitySel,ItemSlot slot,ClothSystem sys,
            ref bool relayRopeInteractions,ref ClothSystem __result)
        {
            if(toEntitySel?.Entity is not EntityShip ship) return true;
            relayRopeInteractions=false;__result=null;
            var eye=byEntity.Pos.XYZ.Add(byEntity.LocalEyePos);
            if(!ship.RayHit(eye,byEntity.Pos.GetViewVector().ToVec3d(),5,slot.Itemstack,out var hit) || hit.Fixture?.Type!="hitch" || ship.GoneDown) return false;
            // Native rope creation and consumption still run in ItemRope.OnHeldInteractStart. Only its target differs.
            if(ship.World.Side!=EnumAppSide.Server) return false;
            var manager=ship.Api.ModLoader.GetModSystem<ClothManager>();
            bool created=sys==null;
            if(sys==null)
            {
                ship.LocalToWorld(hit.Fixture.Pos[0],hit.Fixture.Pos[1],hit.Fixture.Pos[2],out double x,out double y,out double z);
                sys=ClothSystem.CreateRope(ship.Api,manager,byEntity.Pos.XYZ,new Vec3d(x,y,z),null);
                sys.FirstPoint.PinTo(byEntity,new Vec3f(0,(float)byEntity.LocalEyePos.Y-.25f,0));
                manager.RegisterCloth(sys);
                slot.Itemstack.Attributes.SetLong("ropeHeldByEntityId",byEntity.EntityId);
                slot.Itemstack.Attributes.SetInt("clothId",sys.ClothId);slot.MarkDirty();
            }
            var point=created?sys.LastPoint:sys.Ends.FirstOrDefault(p=>p.PinnedToEntity?.EntityId==byEntity.EntityId) ?? sys.Ends.FirstOrDefault(p=>!p.Pinned);
            if(point==null) return false;
            var at=hit.Fixture.Pos;
            point.PinTo(ship,new Vec3f((float)at[0],(float)at[1],(float)at[2]));
            point.NoAttachTransform=true;
            var t=ship.WatchedAttributes.GetTreeAttribute("hansaTethers")??new TreeAttribute();
            t.SetString(sys.ClothId.ToString(),hit.Fixture.Code);ship.WatchedAttributes["hansaTethers"]=t;ship.WatchedAttributes.MarkPathDirty("hansaTethers");
            __result=sys;return false;
        }

        public static void TakeLead(EntityShip ship,FixtureDef post,IServerPlayer player)
        {
            var links=ship.WatchedAttributes.GetTreeAttribute("hansaTethers");
            if(links==null) return;
            var manager=ship.Api.ModLoader.GetModSystem<ClothManager>();
            foreach(var entry in links.ToArray())
            {
                if(links.GetString(entry.Key)!=post.Code || !int.TryParse(entry.Key,out int id)) continue;
                var rope=manager.GetClothSystem(id);var point=rope?.Ends.FirstOrDefault(p=>p.PinnedToEntity?.EntityId==ship.EntityId);
                if(point==null) {links.RemoveAttribute(entry.Key);continue;}
                var animal=rope.Ends.Select(p=>p.PinnedToEntity).FirstOrDefault(e=>e!=null && e!=ship);
                if(animal?.GetBehavior<EntityBehaviorOwnable>() is EntityBehaviorOwnable owner && !owner.IsOwner(player.Entity)) return;
                var stack=new ItemStack(ship.World.GetItem(new AssetLocation("game:rope")));
                stack.Attributes.SetInt("clothId",id);stack.Attributes.SetLong("ropeHeldByEntityId",player.Entity.EntityId);
                if(!player.InventoryManager.TryGiveItemstack(stack,true)) {ship.Notify(player,"hansa:piece-inventory-full");return;}
                point.PinTo(player.Entity,new Vec3f(0,(float)player.Entity.LocalEyePos.Y-.25f,0));point.NoAttachTransform=false;
                links.RemoveAttribute(entry.Key);ship.WatchedAttributes.MarkPathDirty("hansaTethers");return;
            }
        }

        public static void DetachShip(ClothSystem sys,Entity toEntity)
        {
            if(toEntity is not EntityShip ship || ship.World.Side!=EnumAppSide.Server) return;
            foreach(var point in sys.Ends.Where(p=>p.PinnedToEntity?.EntityId==ship.EntityId)) point.UnPin();
            ship.WatchedAttributes.GetTreeAttribute("hansaTethers")?.RemoveAttribute(sys.ClothId.ToString());
            ship.WatchedAttributes.MarkPathDirty("hansaTethers");
        }

        public struct PinState {public Vec3f Offset;public bool NoTransform;public Vec3d Motion;}
        public static void PinBefore(ClothPoint __instance,out PinState __state)
        {
            __state=default;
            if(__instance.PinnedToEntity is not EntityShip ship) return;
            var local=__instance.pinnedToOffset;
            __state=new PinState {Offset=local,NoTransform=__instance.NoAttachTransform,Motion=ship.Pos.Motion.Clone()};
            ship.LocalToWorld(local.X,local.Y,local.Z,out double x,out double y,out double z);
            __instance.pinnedToOffset=new Vec3f((float)(x-ship.Pos.X),(float)(y-ship.Pos.Y),(float)(z-ship.Pos.Z));
            __instance.NoAttachTransform=true;
        }
        public static void PinAfter(ClothPoint __instance,PinState __state)
        {
            if(__state.Offset==null) return;
            __instance.pinnedToOffset=__state.Offset;__instance.NoAttachTransform=__state.NoTransform;
            if(__instance.PinnedToEntity is EntityShip ship) ship.Pos.Motion.Set(__state.Motion);
        }
    }
}
