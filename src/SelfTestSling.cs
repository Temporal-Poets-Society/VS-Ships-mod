using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// (1 November 2026, the user: "elk cannot get down by stairs", chose the sling through a hatch) An elk on her main
        /// deck beside her open hold hatch: with the ladder standing in it the sling is refused; with the ladder taken up it's
        /// lowered onto her hold floor, stands there under its own physics, and is hoisted back up beside the hatch.
        /// </summary>
        void SlingAboard(EntityShip ship)
        {
            EntityAgent elk=null;ShipPiece hatch=null;bool wasOpen=true,ladderOut=false;string ladderMount=null;
            try {
                hatch=ship.Pieces.All.FirstOrDefault(p=>p.Role==PieceRole.Hatch);
                var ladder=ship.Pieces.All.FirstOrDefault(p=>p.Role==PieceRole.Stair && p.Box[1]<0);
                Check(hatch!=null && ladder!=null,"sling: her hold hatch and the ladder under it are aboard");
                if(hatch==null || ladder==null) return;
                wasOpen=hatch.Open;
                if(!hatch.Open) ship.ToggleHatch(null,hatch);
                Check(hatch.Open,"sling: her hold hatch opens");
                double deck=hatch.Box[1]-hatch.Coaming;
                var type=sapi.World.GetEntityType(new AssetLocation("game:tameddeer-elk-male-adult"));
                elk=sapi.World.ClassRegistry.CreateEntity(type) as EntityAgent;
                // on her deck beside the hatch, along her length
                ship.LocalToWorld((hatch.Box[0]+hatch.Box[3])/2,deck+.05,hatch.Box[5]+.75,out double x,out double y,out double z);
                elk.Pos.SetPos(x,y,z);elk.Pos.Yaw=ship.Pos.Yaw+(float)(Math.PI/2);sapi.World.SpawnEntity(elk);elk.State=EnumEntityState.Active;
                var physics=elk.GetBehavior<EntityBehaviorControlledPhysics>();
                void Settle() {for(int i=0;i<40;i++) physics?.OnPhysicsTick(.02f);}
                double LocalY() => ship.WorldToLocal(elk.Pos.X,elk.Pos.Y,elk.Pos.Z).Y;
                Settle();
                Check(DeckCreatures.IsAboard(elk,ship) && Math.Abs(LocalY()-deck)<.05,$"sling: the elk stands aboard on her deck beside the hatch (local y {LocalY():0.###})");
                string why=DeckCreatures.Sling(ship,elk,hatch);
                Check(why=="ships:sling-blocked",$"sling: with the hold ladder standing in the hatchway it can't be slung through ({why ?? "slung"})");
                ladderMount=ladder.Mount;
                string taken=ship.RemovePiece(ladder,null);ladderOut=taken==null;
                Check(ladderOut,$"sling: the hold ladder is taken up ({taken ?? "ok"})");
                why=DeckCreatures.Sling(ship,elk,hatch);
                Check(why==null && DeckCreatures.SlungDown(elk),$"sling: with the ladder up it goes into the sling, down ({why ?? "slung"})");
                int ticks=0;while(why==null && DeckCreatures.Hoisting(elk,.1) && ticks<200) ticks++;
                Settle();
                double floor=LocalY();
                Check(DeckCreatures.IsAboard(elk,ship) && floor<deck-2.5 && elk.OnGround,
                    $"sling: lowered in {ticks*.1:0.#} s, it stands on her hold floor under its own physics (local y {floor:0.###}, ground {elk.OnGround})");
                why=DeckCreatures.Sling(ship,elk,hatch);
                Check(why==null && !DeckCreatures.SlungDown(elk),$"sling: from under the hatch it goes into the sling, up ({why ?? "slung"})");
                ticks=0;while(why==null && DeckCreatures.Hoisting(elk,.1) && ticks<200) ticks++;
                Settle();
                Check(DeckCreatures.IsAboard(elk,ship) && Math.Abs(LocalY()-deck)<.05 && elk.OnGround,
                    $"sling: hoisted in {ticks*.1:0.#} s, it stands on her deck again (local y {LocalY():0.###})");
            }
            catch(Exception e) {Check(false,"sling: "+e);}
            finally {
                elk?.Die(EnumDespawnReason.Removed);
                // (what a player takes up never comes back by itself: put back by its own entry in her design)
                var entry=ladderOut?ship.Def.ModularPieces.FirstOrDefault(e=>e.Kind is "stair" or "ladder" && ship.Mounts.ForModular(e)?.Id==ladderMount):null;
                string back=entry==null?"no entry":ship.PlaceModular(entry,out _);
                if(ladderOut) Check(back==null && ship.Pieces.All.Any(p=>p.Role==PieceRole.Stair && p.Box[1]<0),$"sling: the hold ladder is put back ({back ?? "ok"})");
                if(hatch!=null && hatch.Open!=wasOpen) ship.ToggleHatch(null,hatch);
            }
        }
    }
}
