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
    /// <summary>Native rope systems pinned to player-placed RopeTieable blocks. Offsets use the decks' local frame.</summary>
    public static class ShipTethers
    {
        public static bool IsPost(ShipPiece piece) => piece?.Stack?.Block?.HasBehavior<BlockBehaviorRopeTieable>()==true;
        public static string PostKey(ShipPiece piece) => "piece:"+piece.Id;
        // Vanilla attachToBlock uses the block centre, not its collision-box centre.
        public static double[] PostPosition(ShipPiece piece) => piece.Parts.Count>0 ?
            new[]{piece.Parts[0].Origin[0]+.5,piece.Parts[0].Origin[1]+.5,piece.Parts[0].Origin[2]+.5} : piece.Center;

        // Set only while UsePiece's already reach/visibility-checked post goes through vanilla's item flow.
        [ThreadStatic] static ShipPiece usingPost;
        public static void UsePost(EntityShip ship,ShipPiece post,IServerPlayer player)
        {
            var slot=player.InventoryManager.ActiveHotbarSlot;
            if(slot?.Itemstack?.Collectible is not ItemRope rope) {TakeLead(ship,post,player);return;}
            var previous=usingPost;usingPost=post;
            try {
                var handling=EnumHandHandling.NotHandled;
                rope.OnHeldInteractStart(slot,player.Entity,null,new EntitySelection {Entity=ship,Position=ship.Pos.XYZ},true,ref handling);
            } finally {usingPost=previous;}
        }

        public static bool AttachToShip(EntityAgent byEntity,EntitySelection toEntitySel,ItemSlot slot,ClothSystem sys,
            ref bool relayRopeInteractions,ref ClothSystem __result)
        {
            if(toEntitySel?.Entity is not EntityShip ship) return true;
            relayRopeInteractions=false;__result=null;
            var post=usingPost;
            if(post==null) {
                var eye=byEntity.Pos.XYZ.Add(byEntity.LocalEyePos);
                double range=(byEntity as EntityPlayer)?.Player?.WorldData?.PickingRange??4.5;
                if(!ship.RayHit(eye,byEntity.Pos.GetViewVector().ToVec3d(),range,slot.Itemstack,out var hit) || hit.Part.Kind!=ShipPartKind.Piece) return false;
                post=ship.Pieces.Get(hit.Part.Index);
            }
            if(!IsPost(post)) return false;
            // Native rope creation and consumption still run in ItemRope.OnHeldInteractStart. Only its target differs.
            if(ship.World.Side!=EnumAppSide.Server) return false;
            __result=Attach(ship,post,byEntity,slot,sys);return false;
        }

        public static ClothSystem Attach(EntityShip ship,ShipPiece post,EntityAgent byEntity,ItemSlot slot,ClothSystem sys)
        {
            if(!IsPost(post) || ship.World.Side!=EnumAppSide.Server || ship.GoneDown) return null;
            var at=PostPosition(post);
            var manager=ship.Api.ModLoader.GetModSystem<ClothManager>();
            bool created=sys==null;
            if(sys==null)
            {
                ship.LocalToWorld(at[0],at[1],at[2],out double x,out double y,out double z);
                sys=ClothSystem.CreateRope(ship.Api,manager,byEntity.Pos.XYZ,TetherGeometry.RopeTarget(byEntity.Pos.XYZ,new Vec3d(x,y,z)),null);
                sys.FirstPoint.PinTo(byEntity,new Vec3f(0,(float)byEntity.LocalEyePos.Y-.25f,0));
                manager.RegisterCloth(sys);
                slot.Itemstack.Attributes.SetLong("ropeHeldByEntityId",byEntity.EntityId);
                slot.Itemstack.Attributes.SetInt("clothId",sys.ClothId);slot.MarkDirty();
            }
            var point=created?sys.LastPoint:sys.Ends.FirstOrDefault(p=>p.PinnedToEntity?.EntityId==byEntity.EntityId) ?? sys.Ends.FirstOrDefault(p=>!p.Pinned);
            if(point==null) return null;
            // PinTo(Entity) retains a previous player UID in vanilla. Clear it before transferring the end,
            // otherwise a late load can reconnect this ship-local offset to the former holder.
            point.UnPin();point.PinTo(ship,new Vec3f((float)at[0],(float)at[1],(float)at[2]));
            point.NoAttachTransform=true;
            var t=ship.WatchedAttributes.GetTreeAttribute("hansaTethers")??new TreeAttribute();
            t.SetString(sys.ClothId.ToString(),PostKey(post));ship.WatchedAttributes["hansaTethers"]=t;ship.WatchedAttributes.MarkPathDirty("hansaTethers");
            return sys;
        }

        public static void TakeLead(EntityShip ship,FixtureDef post,IServerPlayer player)
            => TakeLead(ship,post.Code,player);
        public static void TakeLead(EntityShip ship,ShipPiece post,IServerPlayer player)
            => TakeLead(ship,PostKey(post),player);
        static void TakeLead(EntityShip ship,string key,IServerPlayer player)
        {
            var links=ship.WatchedAttributes.GetTreeAttribute("hansaTethers");
            if(links==null) return;
            var manager=ship.Api.ModLoader.GetModSystem<ClothManager>();
            foreach(var entry in links.ToArray())
            {
                if(links.GetString(entry.Key)!=key || !int.TryParse(entry.Key,out int id)) continue;
                var rope=manager.GetClothSystem(id);var point=rope?.Ends.FirstOrDefault(p=>p.PinnedToEntity?.EntityId==ship.EntityId);
                if(point==null) {links.RemoveAttribute(entry.Key);continue;}
                var animal=rope.Ends.Select(p=>p.PinnedToEntity).FirstOrDefault(e=>e!=null && e!=ship);
                if(animal?.GetBehavior<EntityBehaviorOwnable>() is EntityBehaviorOwnable owner && !owner.IsOwner(player.Entity)) return;
                var stack=new ItemStack(ship.World.GetItem(new AssetLocation("game:rope")));
                stack.Attributes.SetInt("clothId",id);stack.Attributes.SetLong("ropeHeldByEntityId",player.Entity.EntityId);
                if(!player.InventoryManager.TryGiveItemstack(stack,true)) {ship.Notify(player,"ships:piece-inventory-full");return;}
                point.UnPin();point.PinTo(player.Entity,new Vec3f(0,(float)player.Entity.LocalEyePos.Y-.25f,0));point.NoAttachTransform=false;
                links.RemoveAttribute(entry.Key);ship.WatchedAttributes.MarkPathDirty("hansaTethers");return;
            }
        }

        public static void ReleasePiece(EntityShip ship,ShipPiece piece)
        {
            var links=ship.WatchedAttributes.GetTreeAttribute("hansaTethers");if(links==null) return;
            var manager=ship.Api.ModLoader.GetModSystem<ClothManager>();
            foreach(var entry in links.ToArray()) if(links.GetString(entry.Key)==PostKey(piece)) {
                if(int.TryParse(entry.Key,out int id)) {
                    var rope=manager.GetClothSystem(id);
                    if(rope!=null) foreach(var point in rope.Ends.Where(p=>p.PinnedToEntity?.EntityId==ship.EntityId)) {point.UnPin();point.NoAttachTransform=false;}
                }
                links.RemoveAttribute(entry.Key);
            }
            ship.WatchedAttributes.MarkPathDirty("hansaTethers");
        }

        public static void DetachShip(ClothSystem sys,Entity toEntity)
        {
            if(toEntity is not EntityShip ship || ship.World.Side!=EnumAppSide.Server) return;
            foreach(var point in sys.Ends.Where(p=>p.PinnedToEntity?.EntityId==ship.EntityId)) point.UnPin();
            ship.WatchedAttributes.GetTreeAttribute("hansaTethers")?.RemoveAttribute(sys.ClothId.ToString());
            ship.WatchedAttributes.MarkPathDirty("hansaTethers");
        }

        public struct PinState {
            public Vec3f Offset;public bool NoTransform,CanPull;
            public EntityShip Ship;public TetherFrame Frame;public Vec3d Before;
        }
        public static void PinBefore(ClothPoint __instance,ClothSystem ___cs,out PinState __state)
        {
            __state=default;
            if(!__instance.Pinned) {
                if(ShipRopes.Frame(___cs,out var aboard,out var pose))
                    __state=new PinState {Ship=aboard,Frame=pose,Before=pose.Local(__instance.Pos)};
                return;
            }
            var ship=__instance.PinnedToEntity as EntityShip;
            var holder=__instance.PinnedToEntity as EntityAgent;
            if(ship==null && holder?.MountedOn is not ShipSeat) return;
            __state=new PinState {Offset=__instance.pinnedToOffset,NoTransform=__instance.NoAttachTransform,CanPull=___cs.CanPull};
            // ClothPoint applies tension to MountedOn.Entity. A deck walker's hand and a fixed post
            // must not accelerate the whole ship. The animal's own point still receives native tension.
            ___cs.CanPull=false;
            if(ship==null) {
                var hand=new Vec3d(0,holder.LocalEyePos.Y-.3,0).AheadCopy(.1,holder.Pos.Pitch,holder.Pos.Yaw)
                    .AheadCopy(.4,holder.Pos.Pitch,holder.Pos.Yaw-(float)Math.PI/2);
                // A mounted first-person hand pose can be stale/hidden; use the same eye-relative hand
                // offset as vanilla's fallback, in the camera frame used to draw her decks and creatures.
                if(holder.Api is Vintagestory.API.Client.ICoreClientAPI api && api.World.Player?.Entity==holder)
                    hand.Add(((EntityPlayer)holder).CameraPos.SubCopy(holder.Pos.X,holder.Pos.Y,holder.Pos.Z));
                __instance.pinnedToOffset=hand.ToVec3f();__instance.NoAttachTransform=true;return;
            }
            var links=ship.WatchedAttributes.GetTreeAttribute("hansaTethers");
            // The old fixed fixture was removed. Free a legacy lead rather than pinning it to empty air.
            if(ship.World.Side==EnumAppSide.Server && links!=null) {
                var key=links.GetString(___cs.ClothId.ToString());
                if(key!=null && (key.StartsWith("piece:") ? !int.TryParse(key.Substring(6),out int id) || !IsPost(ship.Pieces.Get(id)) : ship.Def.Fixture(key)==null)) {
                    __instance.UnPin();__instance.NoAttachTransform=false;
                    links.RemoveAttribute(___cs.ClothId.ToString());ship.WatchedAttributes.MarkPathDirty("hansaTethers");return;
                }
            }
            var local=__instance.pinnedToOffset;
            var world=ShipRopes.Pose(ship).World(new Vec3d(local.X,local.Y,local.Z));
            __instance.pinnedToOffset=new Vec3f((float)(world.X-ship.Pos.X),(float)(world.Y-ship.Pos.Y),(float)(world.Z-ship.Pos.Z));
            __instance.NoAttachTransform=true;
        }
        public static void PinAfter(ClothPoint __instance,ClothSystem ___cs,PinState __state)
        {
            if(__state.Offset!=null) {
                ___cs.CanPull=__state.CanPull;
                if(__instance.Pinned) {__instance.pinnedToOffset=__state.Offset;__instance.NoAttachTransform=__state.NoTransform;}
            }
            if(__state.Before==null) return;
            var to=__state.Frame.Local(__instance.Pos);
            int stopped=TetherGeometry.Move(__state.Ship.Grid,__state.Before,to);
            if(stopped==0) return;
            __instance.Pos.Set(__state.Frame.World(to));
            ShipFrame.ToLocal(__state.Frame.Yaw,__state.Frame.Pitch,__state.Frame.Roll,__instance.Velocity.X,__instance.Velocity.Y,__instance.Velocity.Z,out double x,out double y,out double z);
            ShipFrame.ToWorld(__state.Frame.Yaw,__state.Frame.Pitch,__state.Frame.Roll,(stopped&1)!=0?0:x,(stopped&2)!=0?0:y,(stopped&4)!=0?0:z,out x,out y,out z);
            __instance.Velocity.Set((float)x,(float)y,(float)z);
        }
    }
}
