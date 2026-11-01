using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// (1 November 2026, the user: "flute does not work on ship") The flute calls its player's elk with vanilla's
    /// come-to-owner: the elk walks there by the world's paths, and when it's stuck vanilla teleports it onto a free block
    /// near its owner. Neither finds her deck, which isn't the world's blocks. With its owner aboard, the walk counts as
    /// stuck from the start, and the teleport puts the elk on her deck beside its owner where its body fits (none: it stays).
    /// </summary>
    public static class CreatureCalls
    {
        static readonly AccessTools.FieldRef<AiTaskStayCloseToEntity,Entity> target=AccessTools.FieldRefAccess<AiTaskStayCloseToEntity,Entity>("targetEntity");
        static readonly AccessTools.FieldRef<AiTaskStayCloseToEntity,bool> stuck=AccessTools.FieldRefAccess<AiTaskStayCloseToEntity,bool>("stuck");
        static readonly AccessTools.FieldRef<AiTaskBase,EntityAgent> agent=AccessTools.FieldRefAccess<AiTaskBase,EntityAgent>("entity");

        /// <summary>The ship a player stands or sits aboard, or null.</summary>
        public static EntityShip ShipUnder(Entity player) =>
            (player as EntityAgent)?.MountedOn is ShipSeat s && s.Ship?.Grid!=null && s.Ship.Alive ? s.Ship : null;

        /// <summary>Postfix of AiTaskComeToOwner.StartExecute: its owner is aboard, so no world path reaches them.</summary>
        public static void StartAfter(AiTaskComeToOwner __instance)
        {
            if(ShipUnder(target(__instance))!=null) stuck(__instance)=true;
        }

        /// <summary>Prefix of AiTaskStayCloseToEntity.findDecentTeleportPos: with its owner aboard, a place on her deck beside them.</summary>
        public static bool TeleportPos(AiTaskStayCloseToEntity __instance,ref Vec3d __result)
        {
            var owner=target(__instance);var ship=ShipUnder(owner);
            if(ship==null) return true;
            var e=agent(__instance);
            var at=e==null?null:Beside(ship,e,owner);
            __result=null;
            if(at==null) return false;
            ship.LocalToWorld(at.X,at.Y,at.Z,out double x,out double y,out double z);
            DeckCreatures.Expect(e,ship,at);
            __result=new Vec3d(x,y,z);
            return false;
        }

        /// <summary>Where on her deck a creature stands beside its owner aboard (ship-local feet): the nearest place on their deck its body fits, 1.25 to 4.5 away.</summary>
        public static Vec3d Beside(EntityShip ship,Entity e,Entity owner)
        {
            var p=ship.WorldToLocal(owner.Pos.X,owner.Pos.Y,owner.Pos.Z);
            var b=e.CollisionBox;
            double side=Math.Max(b?.XSize??.6,b?.ZSize??.6),height=b?.YSize??1,yaw=DeckCreatures.LocalYaw(e,ship);
            for(double r=1.25;r<=4.5+1e-9;r+=.25)
                for(int k=0;k<16;k++)
                {
                    double a=k*Math.PI/8;
                    var m=CreatureDeckMotion.Of(p.X+r*Math.Sin(a),p.Y+.6,p.Z+r*Math.Cos(a),side,height,yaw);
                    if(!m.Catch(ship.Grid,yaw)) continue;
                    m.Move(ship.Grid,0,-1.8,0);
                    if(!m.OnGround || m.Stuck(ship.Grid) || Math.Abs(m.Y-p.Y)>1.1) continue;
                    return new Vec3d(m.X,m.Y,m.Z);
                }
            return null;
        }
    }
}
