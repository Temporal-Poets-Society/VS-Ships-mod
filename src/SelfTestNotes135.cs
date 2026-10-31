using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    public partial class SelfTest
    {
        void Notes135Aboard(EntityShip ship)
        {
            if(ship.Def.Code!="caravel") return;
            sapi.Event.RegisterCallback(_=>TethersAboard(ship),1500);
            var boards=sapi.World.GetItem(new AssetLocation("game:plank-oak"));
            var beam=sapi.World.GetBlock(new AssetLocation("game:supportbeam-oak"));
            var mast=sapi.World.GetBlock(new AssetLocation("game:debarkedlog-oak-ud"));
            var pane=sapi.World.GetBlock(new AssetLocation(ModularPieceDef.DefaultBlock("window")));
            Check(boards!=null && beam?.Id>0 && mast?.Id>0 && pane?.Id>0,"148: vanilla boards, support beams, debarked timber and leaded panes resolve");
            if(boards!=null) Check(EntityShip.LayNeed(new SlotMaterialDef {Block="game:plank-oak",Count=4},new ItemStack(boards,4))==4,"148: a hull panel costs four boards");
            if(beam?.Id>0) Check(EntityShip.LayNeed(new SlotMaterialDef {Block="game:supportbeam-oak"},new ItemStack(beam))==1,"148: a rib or beam length costs one native support beam");
            var below=ship.Def.HullSlots.FirstOrDefault(s=>s.Sill<-1 && ship.HullTakeProblem(s.Index)==null);
            if(below!=null)
            {
                Check(ship.TakeHull(below.Index,null)==null && ship.HullOpen(below.Index),"148: building can remove an underwater hull panel while afloat");
                Check(ship.LayHull(below.Index,false)==null && !ship.HullOpen(below.Index),"148: the underwater panel can be put back");
            }
            else Check(false,"148: an underwater panel is available for repair");
            var ribs=ship.Def.DeckSlots.Where(s=>s.IsStructure).ToArray();
            var rib=ribs.FirstOrDefault(s=>ship.TakeProblem(s.Index)==null);
            Check(ribs.Length>=190 && rib!=null,"148: the sliced ribs are removable structural slots");
            if(rib!=null) Check(ship.TakeCell(rib.Index,null)==null && ship.LayCell(rib.Index,false)==null,"148: a rib cell is taken up and replaced");
            var hatch=ship.Pieces.All.FirstOrDefault(p=>p.Role==PieceRole.Hatch);
            Check(hatch?.Coaming==.25 && hatch.DoubleLeaf && HatchPlanner.Leaves(hatch.Box,hatch.Dir,hatch.DoubleLeaf).Count==2,"147: the default raised double hatch is actually placed aboard");
            var hold=ship.Pieces.All.FirstOrDefault(p=>p.Role==PieceRole.Stair && p.Box[1]<0);
            Check(hold?.Dir==2,"147: the default hold stair is placed descending toward the bow");

            var type=sapi.World.GetEntityType(new AssetLocation("game:tameddeer-elk-male-adult"));
            var elk=type==null?null:sapi.World.ClassRegistry.CreateEntity(type) as EntityAgent;
            var fence=sapi.World.GetBlock(new AssetLocation("game:woodenfence-oak-ew-free"));
            ShipPiece post=null; string postError="no native fence";
            if(fence?.Id>0) foreach(var m in ship.Mounts.Floors.Where(m=>m.Deck=="main" && m.Level==0).OrderBy(m=>Math.Abs(m.X+5.5)+Math.Abs(m.Z+1))) {
                var plan=PiecePlanner.Plan(ship.Mounts,m,new VanillaPieceBlock(sapi.World,new ItemStack(fence),false),0);
                postError=ship.PlacePiece(plan,new ItemStack(fence),true,out post);if(post!=null) break;
            }
            Check(elk!=null && ShipTethers.IsPost(post) && post.Parts[0].Block.Contains("-empty-"),"155: an ordinary vanilla fence item places a native disconnected RopeTieable post on a chosen deck cell ("+(postError??"ok")+")");
            if(elk==null || post==null) return;
            ship.LocalToWorld(.5,ship.Def.DeckY+.15,0,out double x,out double y,out double z);elk.Pos.SetPos(x,y,z);sapi.World.SpawnEntity(elk);
            DeckCreatures.TestTrace[elk.EntityId]="no controlled-physics tick";
            var manager=sapi.ModLoader.GetModSystem<ClothManager>();
            var ropeSlot=new DummySlot(new ItemStack(sapi.World.GetItem(new AssetLocation("game:rope"))));
            var rope=ShipTethers.Attach(ship,post,elk,ropeSlot,null);
            var at=ShipTethers.PostPosition(post);
            sapi.Event.RegisterCallback(_=>
            {
                try
                {
                    // A headless server has no nearby client to activate native creature simulation. Drive its
                    // actual controlled-physics behavior here, including the Harmony prefix/postfix used in play.
                    var physics=elk.GetBehavior<EntityBehaviorControlledPhysics>();
                    Check(physics!=null,"elk: native controlled physics is available");
                    elk.State=EnumEntityState.Active;
                    for(int tick=0;tick<100 && physics!=null;tick++) physics.OnPhysicsTick(.02f);
                    var local=ship.WorldToLocal(elk.Pos.X,elk.Pos.Y,elk.Pos.Z);
                    Check(DeckCreatures.IsAboard(elk,ship) && local.Y>ship.Def.DeckY-.1 && elk.OnGround,"elk: native creature physics lands and stands on the moving deck ("+local+", ground "+elk.OnGround+"; "+DeckCreatures.TestTrace[elk.EntityId]+")");
                    var original=new EntityPos().SetFrom(ship.Pos);
                    try {
                        // Move and turn the actual ship, then drive its normal creature-carry tick (no client is present).
                        ship.Pos.X+=2;ship.Pos.Z+=1;ship.Pos.Yaw+=.2f;
                        typeof(DeckCreatures).GetMethod("Tick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(ship.Mod.DeckCreatures,new object[]{.02f});
                        rope.LastPoint.update(0,sapi.World);
                        ship.LocalToWorld(at[0],at[1],at[2],out double px,out double py,out double pz);
                        var moved=ship.WorldToLocal(elk.Pos.X,elk.Pos.Y,elk.Pos.Z);
                        Check(moved.DistanceTo(local)<.001 && elk.OnGround && DeckCreatures.IsAboard(elk,ship) && rope.LastPoint.Pos.SquareDistanceTo(px,py,pz)<.0001 &&
                            rope.LastPoint.pinnedToOffset.X==(float)at[0],"155: translate and turn the ship: the elk stays on deck and the native rope follows the placed post with a saved local offset");
                        Check(ship.RemovePiece(post,null)==null && !rope.LastPoint.Pinned && rope.FirstPoint.PinnedToEntity==elk,"155: taking the placed post up frees its lead, retaining the creature end");
                        rope.LastPoint.PinTo(ship,new Vec3f(-5.41f,1.855f,-1.75f));rope.LastPoint.NoAttachTransform=true;
                        var links=ship.WatchedAttributes.GetTreeAttribute("hansaTethers");links.SetString(rope.ClothId.ToString(),"hitch-main");
                        rope.LastPoint.update(0,sapi.World);
                        Check(!rope.LastPoint.Pinned && !links.HasAttribute(rope.ClothId.ToString()),"155: a saved lead on the removed fixed fixture is freed and its obsolete link cleared");
                    } finally {ship.Pos.SetFrom(original);}
                }
                catch(Exception e) {Check(false,"elk: native lead/deck check: "+e.Message);}
                finally {DeckCreatures.TestTrace.TryRemove(elk.EntityId,out string trace);manager.UnregisterCloth(rope.ClothId);elk.Die(EnumDespawnReason.Removed);}
            },1200);
        }
    }
}
