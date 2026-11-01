using System;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// (1 November 2026, the user: "flute does not work on ship") The elk's own come-to-owner task, called for its owner
        /// standing on her main deck while the elk is ashore: the walk counts as stuck, vanilla's teleport search (patched)
        /// gives a place on her deck beside the owner, and native physics takes the elk aboard there.
        /// </summary>
        void FluteAboard(EntityShip ship)
        {
            EntityAgent elk=null;TetherTestEntity ep=null;
            var original=new EntityPos().SetFrom(ship.Pos);
            try {
                ep=new TetherTestEntity {Api=sapi,World=sapi.World,EntityId=-1101};ep.LocalEyePos.Set(0,1.62,0);
                var player=new HearthTestPlayer((ServerMain)sapi.World,ep);
                ep.WatchedAttributes.SetString("playerUID",player.PlayerUID);
                // on her main deck, just inside the starboard entry port
                var gate=ship.Def.Fixtures.Single(f=>f.Code=="gate-starboard");
                var seat=new DeckSeat(ship,player.PlayerUID,(gate.Box[0]+gate.Box[3])/2,ship.Def.DeckY,gate.Box[2]-1.2);ep.OnDeck(seat);
                var pos=seat.SeatPosition;ep.Pos.SetPos(pos.X,pos.Y,pos.Z);
                var type=sapi.World.GetEntityType(new AssetLocation("game:tameddeer-elk-male-adult"));
                elk=sapi.World.ClassRegistry.CreateEntity(type) as EntityAgent;
                ship.LocalToWorld(0,ship.Def.DeckY+6,30,out double x,out double y,out double z);
                elk.Pos.SetPos(x,y,z);sapi.World.SpawnEntity(elk);
                var owner=new TreeAttribute();owner.SetString("uid",player.PlayerUID);elk.WatchedAttributes["ownedby"]=owner;
                var task=elk.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.AllTasks.OfType<AiTaskComeToOwner>().FirstOrDefault();
                Check(task!=null,"flute: the tamed elk has vanilla's come-to-owner task the flute runs");
                if(task==null) return;
                AccessTools.FieldRefAccess<AiTaskStayCloseToEntity,Entity>("targetEntity")(task)=ep;
                CreatureCalls.StartAfter(task);
                Check(AccessTools.FieldRefAccess<AiTaskStayCloseToEntity,bool>("stuck")(task),"flute: with its owner aboard, its walk by the world's paths counts as stuck (so vanilla teleports it)");
                var at=(Vec3d)AccessTools.Method(typeof(AiTaskStayCloseToEntity),"findDecentTeleportPos").Invoke(task,null);
                var local=at==null?null:ship.WorldToLocal(at.X,at.Y,at.Z);
                var mine=ship.WorldToLocal(ep.Pos.X,ep.Pos.Y,ep.Pos.Z);
                double away=local==null?double.NaN:Math.Sqrt((local.X-mine.X)*(local.X-mine.X)+(local.Z-mine.Z)*(local.Z-mine.Z));
                Check(local!=null && Math.Abs(local.Y-ship.Def.DeckY)<.05 && away>=1.2 && away<=4.6,
                    $"flute: vanilla's teleport search, with its owner aboard, gives a place on her main deck beside them (local {local}, {away:0.##} from the owner)");
                if(at==null) return;
                // vanilla's teleport, then the creature's own physics ticks
                elk.Pos.SetPos(at);elk.State=EnumEntityState.Active;
                var physics=elk.GetBehavior<EntityBehaviorControlledPhysics>();
                for(int i=0;i<40;i++) physics?.OnPhysicsTick(.02f);
                var now=ship.WorldToLocal(elk.Pos.X,elk.Pos.Y,elk.Pos.Z);
                Check(DeckCreatures.IsAboard(elk,ship) && elk.OnGround && Math.Abs(now.Y-ship.Def.DeckY)<.05 && now.DistanceTo(local)<.5,
                    $"flute: the elk teleported there stands aboard on her deck (local {now}, ground {elk.OnGround})");
                ep.OnDeck(null);
                Check(CreatureCalls.ShipUnder(ep)==null,"flute: with its owner ashore vanilla's own search runs unchanged");
            }
            catch(Exception e) {Check(false,"flute: "+e);}
            finally {
                elk?.Die(EnumDespawnReason.Removed);ship.Pos.SetFrom(original);
            }
        }
    }
}
