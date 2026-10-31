using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>Keep native cloth physics/rendering, adding the moving ship frame and its solid surfaces.</summary>
    public static class ShipRopes
    {
        sealed class LastFrame {public EntityShip Ship;public TetherFrame Frame;}
        static readonly ConditionalWeakTable<ClothSystem,LastFrame> frames=new();

        static EntityShip Aboard(Entity entity)
        {
            if(entity is EntityShip ship) return ship;
            if(entity is EntityAgent {MountedOn:ShipSeat seat}) return seat.Ship;
            var id=entity?.WatchedAttributes.GetTreeAttribute(DeckCreatures.Key)?.GetLong("ship")??0;
            return id==0?null:entity.World.GetEntityById(id) as EntityShip;
        }
        public static TetherFrame Pose(EntityShip ship)
        {
            var origin=ship.Pos.XYZ;
            if(ship.Api is ICoreClientAPI api && api.World.Player?.Entity is EntityPlayer player)
                origin=ShipClientSystem.Instance?.Renderer?.RenderOrigin(ship,player)??origin;
            return new TetherFrame(origin.X,origin.Y,origin.Z,ship.Pos.Yaw,ship.Pitch,ship.Roll);
        }
        public static bool Frame(ClothSystem rope,out EntityShip ship,out TetherFrame pose)
        {
            ship=Aboard(rope.FirstPoint.PinnedToEntity)??Aboard(rope.LastPoint.PinnedToEntity);
            pose=default;
            if(ship?.Grid==null || ship.GoneDown) return false;
            pose=Pose(ship);return true;
        }
        public static void BeforeStep(ClothSystem __instance)
        {
            if(!Frame(__instance,out var ship,out var pose)) {frames.Remove(__instance);return;}
            var last=frames.GetValue(__instance,_=>new LastFrame());
            // Both ends share her motion, or one end is loose. A shore anchor/another ship must stay in world space.
            var a=__instance.FirstPoint;var b=__instance.LastPoint;
            bool carry=(!a.Pinned || Aboard(a.PinnedToEntity)==ship) && (!b.Pinned || Aboard(b.PinnedToEntity)==ship);
            if(carry && last.Ship==ship) __instance.WalkPoints(p=> {
                if(p.Pinned) return;
                p.Pos.Set(pose.Carry(p.Pos,last.Frame));
                ShipFrame.ToLocal(last.Frame.Yaw,last.Frame.Pitch,last.Frame.Roll,p.Velocity.X,p.Velocity.Y,p.Velocity.Z,out double x,out double y,out double z);
                ShipFrame.ToWorld(pose.Yaw,pose.Pitch,pose.Roll,x,y,z,out x,out y,out z);
                p.Velocity.Set((float)x,(float)y,(float)z);
            });
            last.Ship=ship;last.Frame=pose;
            // Native constraints run before point updates. Refresh the anchors now, so the first step
            // after a ship movement doesn't pull against yesterday's pins and today's loose points.
            if(a.Pinned) a.update(0,__instance.api.World);
            if(b.Pinned && b!=a) b.update(0,__instance.api.World);
        }
        public static void HeldBefore(ItemSlot slot,out int __state) => __state=slot.Itemstack?.Attributes.GetInt("clothId")??0;
        public static void CreateBefore(EntityAgent byEntity,ref Vec3d targetPos)
        {
            if(Aboard(byEntity)!=null) targetPos=TetherGeometry.RopeTarget(byEntity.Pos.XYZ,targetPos);
        }
        public static void HeldAfter(ItemSlot slot,EntityAgent byEntity,int __state)
        {
            if(byEntity.World.Side!=EnumAppSide.Server) return;
            int id=slot.Itemstack?.Attributes.GetInt("clothId")??0;
            if(id==__state) return;
            var rope=id==0?null:byEntity.Api.ModLoader.GetModSystem<ClothManager>().GetClothSystem(id);
            if(rope==null || !Frame(rope,out _,out _)) return;
            // Vanilla builds the rope between feet, below the hold floor's surface. Start the loose points
            // between the actual pins so deck collision can keep them above it from the first physics tick.
            rope.FirstPoint.update(0,byEntity.World);rope.LastPoint.update(0,byEntity.World);
            var points=new List<ClothPoint>();rope.WalkPoints(points.Add);
            var start=rope.FirstPoint.Pos.Clone();var end=rope.LastPoint.Pos.Clone();
            for(int i=1;i<points.Count-1;i++) {
                double t=i/(double)(points.Count-1);
                points[i].Pos.Set(start.X+(end.X-start.X)*t,start.Y+(end.Y-start.Y)*t,start.Z+(end.Z-start.Z)*t);
                points[i].Velocity.Set(0,0,0);points[i].MarkDirty();
            }
            rope.setRenderCenterPos();BeforeStep(rope);
        }
    }
}
