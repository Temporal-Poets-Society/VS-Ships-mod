using System;
using System.Linq;
using Vintagestory.API.Common;

namespace Hansa
{
    public partial class SelfTest
    {
        // Ballistic tests set up a loaded gun explicitly; ordinary firing always checks its charge.
        static string FirePrepared(EntityShip ship,FixtureDef gun,double? elevation=null,double? train=null)
        {
            ship.Gun(gun.Code).Charge=GunLoading.Loaded;
            ship.OpenGunPort(gun,true);
            ship.Gun(gun.Code).RunIn=false;
            return ship.FireGun(gun,null,null,elevation,train);
        }

        void RuntimeLoadingAboard(EntityShip ship,FixtureDef gun)
        {
            var powder=sapi.World.GetItem(new AssetLocation(GunLoading.Powder));
            var shot=sapi.World.GetItem(new AssetLocation(GunLoading.Shot));
            Check(powder!=null && shot!=null,"132: vanilla blasting powder and lead shot exist on the server");
            if(powder==null || shot==null) return;
            var p=new DummySlot(new ItemStack(powder,2));var s=new DummySlot(new ItemStack(shot,5));
            var st=ship.Gun(gun.Code);st.Charge=0;
            ship.OpenGunPort(gun,true);
            string outError=ship.LoadGunCharge(gun,p,false);
            ship.RunGunIn(gun);
            string moving=ship.LoadGunCharge(gun,p,false);
            ship.RunGunIn(gun,true);
            string shotFirst=ship.LoadGunCharge(gun,s,false);
            string powderIn=ship.LoadGunCharge(gun,p,false);
            string ballIn=ship.LoadGunCharge(gun,s,false);
            string twice=ship.LoadGunCharge(gun,s,false);
            Check(outError=="hansa:gun-load-run-in" && moving==outError && shotFirst=="hansa:gun-load-powder" &&
                  powderIn==null && ballIn==null && twice=="hansa:gun-load-full" && p.Itemstack.StackSize==1 && s.Itemstack.StackSize==1 &&
                  st.Charge==GunLoading.Loaded && ship.GunOpen(gun) && ship.GunRunIn(gun),
                $"132: run out/moving refused ({outError}, {moving}); run in with port open, powder then four shot consumed once ({powderIn??"ok"}, {ballIn??"ok"}, {twice})");
            Check(ship.FireGun(gun,null,null)=="hansa:gun-load-run-out","132: a loaded gun must be run out to fire");
            Check(ship.WatchedAttributes.GetTreeAttribute("hansaGuns")?.GetTreeAttribute("loads")?.GetInt(gun.Code)==7,
                "132: powder, shot and run-in state are saved together for this gun");
            ship.HouseGun(gun);st.Charge=0;st.RunIn=false;
        }

        void RuntimePlacementAboard(EntityShip ship)
        {
            if(ship.Def.Code!="caravel") return;
            var lantern=sapi.World.GetBlock(new AssetLocation("game:lantern-small-down"));
            if(lantern!=null && lantern.Id!=0)
            {
                var held=new VanillaPieceBlock(sapi.World,new ItemStack(lantern));
                var mast=ship.Mounts.Get("h:lantern-mainmast");
                var plan=PiecePlanner.Plan(ship.Mounts,mast,held,0);
                Check(held.VisualBounds!=null && plan.Box!=null && Math.Abs(plan.Box[4]-mast.Y)<1e-5,
                    "117: the real lantern shape is fitted to its hook, with the same visible bounds used by placement");
                float lee=ship.LeeShown;ship.LeeShown=1;
                Check(ship.VisibleObstructed(plan) && ship.LastPlacementObstacle?.StartsWith("rig:shroud:")==true,
                    "117: placement refuses the real lantern crossing the cast-off shroud beside the mainmast ("+ship.LastPlacementObstacle+")");
                ship.LeeShown=lee;
            }
            var f=ship.Mounts.Floors.FirstOrDefault(m=>m.Deck=="main" && m.X==-13 && m.Z==-.5);
            var plank=sapi.World.GetBlock(new AssetLocation("game:planks-oak-ud"));
            if(f!=null && plank!=null)
            {
                var plan=PiecePlanner.Plan(ship.Mounts,f,new VanillaPieceBlock(sapi.World,new ItemStack(plank),false),0);
                string error=ship.PlacePiece(plan,new ItemStack(plank),false,out var first);
                if(first!=null) {
                    Check(ship.PlaceProblem(plan,false)!=null,"117/123: a second ordinary block cannot overlap an existing block in the cabin");
                    ship.RemovePiece(first,null);
                }
                else Log("117/123: cabin test cell already occupied: "+error);
            }
        }
    }
}
