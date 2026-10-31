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
            var place=ship.GunPlaceOf(gun);
            Check(place==null || ship.TakeGunProblem(place)=="hansa:gun-take-charged","charged guns refuse removal before losing their powder or shot");
            st.Charge=0;
            var empty=ShipWrecks.LootOf(ship,false).SelectMany(l=>l.Items).ToList();
            st.Charge=GunLoading.Loaded;
            var loaded=ShipWrecks.LootOf(ship,false).SelectMany(l=>l.Items).ToList();
            int Count(System.Collections.Generic.List<ItemStack> items,string code)=>items.Where(s=>s.Collectible.Code.ToString()==code).Sum(s=>s.StackSize);
            Check(Count(loaded,GunLoading.Powder)-Count(empty,GunLoading.Powder)==1 && Count(loaded,GunLoading.Shot)-Count(empty,GunLoading.Shot)==4,
                "a wreck's loot preserves the powder and all four shot from a loaded carriage gun");
            ship.OpenGunPort(gun,true);
            string fired=ship.FireGun(gun,null,null);
            string recoilLoad=ship.LoadGunCharge(gun,p,false);
            Check(fired==null && recoilLoad=="hansa:gun-load-run-in" && p.Itemstack.StackSize==1,
                "automatic recoil starts a fresh run-in timer and refuses powder until it finishes");
            st.FiredMs=long.MinValue/2;
            ship.HouseGun(gun);st.Charge=0;st.RunIn=false;
        }

        void RuntimePlacementAboard(EntityShip ship)
        {
            if(ship.Def.Code!="caravel") return;
            var lantern=sapi.World.GetBlock(new AssetLocation("game:lantern-small-down"));
            if(lantern!=null && lantern.Id!=0)
            {
                var held=new VanillaPieceBlock(sapi.World,new ItemStack(lantern));
                var mast=new MountPoint {Id="h:old-mainmast",Kind=MountKind.Hook,X=-3.1117,Y=3.685,Z=0,Hang="down"};
                var plan=PiecePlanner.Plan(ship.Mounts,mast,held,0);
                Check(held.VisualBounds!=null && plan.Box!=null && Math.Abs(plan.Box[4]-mast.Y)<1e-5,
                    "117: the real lantern shape is fitted to its hook, with the same visible bounds used by placement");
                float lee=ship.LeeShown;ship.LeeShown=1;
                Check(ship.VisibleObstructed(plan) && ship.LastPlacementObstacle?.StartsWith("rig:shroud:")==true,
                    "117: placement refuses the real lantern crossing the cast-off shroud beside the mainmast ("+ship.LastPlacementObstacle+")");
                ship.LeeShown=lee;
                foreach(var code in new[]{"lantern-mainmast","lantern-mizzenmast"}) {
                    var safe=ship.Mounts.Get("h:"+code);var candidate=PiecePlanner.Plan(ship.Mounts,safe,held,0);
                    Check(!ship.VisibleObstructed(candidate),"117: the moved "+code+" hook clears the ship and rig");
                }
            }
            foreach(var at in new[]{new double[]{-9,1.125,-1},new double[]{-7,3.125,2},new double[]{-7,3.125,3}}) {
                var entry=ship.Def.ModularPieces.FirstOrDefault(e=>e.Kind=="partition" && e.At.SequenceEqual(at));
                var mount=entry==null?null:ship.Mounts.ForModular(entry);
                Check(mount!=null && ship.Pieces.At(mount.Id)?.Role==PieceRole.Partition,"108/109: default partition laid at "+string.Join(',',at));
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
