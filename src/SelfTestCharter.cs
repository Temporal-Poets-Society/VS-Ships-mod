using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// (31 October 2026) The caravel's charter is crafted from bundles that hold her bill of materials
        /// (docs/proposals/caravel-charter-bom.md): every recipe loads, every ingredient is a real item, and the charter's
        /// raw materials add up to the BOM. (13 November 2026, SH-0012) Without guns: five bundles, no battery; the battery
        /// is no longer made nor in the creative inventory, and one kept from 0.1.0 unpacks into its five guns.
        /// </summary>
        void CaravelCharterRecipes()
        {
            var recipes=sapi.World.GridRecipes.Where(r=>r.Output?.Code?.Domain=="ships" &&
                (r.Output.Code.Path.StartsWith("shipkit-") || r.Output.Code.Path=="shipdeed-caravel")).ToList();
            Check(recipes.Count==6 && !recipes.Any(r=>r.Output.Code.Path=="shipkit-battery"),
                $"charter: the five bundle recipes and the caravel's charter recipe load, none makes a battery ({recipes.Count})");
            var missing=new List<string>();
            foreach(var r in recipes)
                foreach(var ing in r.ResolvedIngredients.Where(i=>i!=null))
                {
                    bool found=ing.Code.Path.Contains("*")
                        ? (ing.Type==EnumItemClass.Block ? sapi.World.Blocks.Any(b=>b?.Code!=null && WildcardUtil.Match(ing.Code,b.Code))
                                                          : sapi.World.Items.Any(i=>i?.Code!=null && WildcardUtil.Match(ing.Code,i.Code)))
                        : ing.ResolvedItemStack!=null;
                    if(!found) missing.Add(r.Output.Code.Path+": "+ing.Code);
                }
            Check(missing.Count==0,"charter: every bundle and charter ingredient is a real item ("+(missing.Count==0?"all found":string.Join(", ",missing))+")");

            // what one charter takes in raw materials, through its bundles
            var byOutput=recipes.ToDictionary(r=>r.Output.Code.Path);
            var raw=new Dictionary<string,int>();
            void Add(string path,int times)
            {
                if(!byOutput.TryGetValue(path,out var r)) return;
                foreach(var ing in r.ResolvedIngredients.Where(i=>i!=null))
                {
                    if(ing.Code.Domain=="ships" && ing.Code.Path.StartsWith("shipkit-")) {Add(ing.Code.Path,times*ing.Quantity);continue;}
                    string key=ing.Code.Path.Split('-')[0]+(ing.Code.Path.StartsWith("ingot-")?"-iron":"");
                    raw[key]=raw.GetValueOrDefault(key)+times*ing.Quantity;
                }
            }
            Add("shipdeed-caravel",1);
            var want=new Dictionary<string,int> {
                ["plank"]=3584,["supportbeam"]=288,["debarkedlog"]=50,["linen"]=244,["rope"]=176,
                ["metalnailsandstrips"]=256,["ingot-iron"]=12,["resin"]=64,["flaxfibers"]=64
            };
            string got=string.Join(", ",raw.OrderBy(kv=>kv.Key).Select(kv=>kv.Key+" "+kv.Value));
            Check(raw.Count==want.Count && want.All(kv=>raw.GetValueOrDefault(kv.Key)==kv.Value),
                "charter: one caravel charter takes her bill of materials, no guns ("+got+")");

            // (SH-0012) the battery kept from 0.1.0: registered, not to be had, unpacked into its five guns
            var battery=sapi.World.GetItem(new AssetLocation("ships","shipkit-battery")) as ItemGunBattery;
            var contents=battery?.Contents()??new List<ItemStack>();
            bool inCreative=battery?.CreativeInventoryTabs?.Length>0;
            Check(battery!=null && !inCreative && contents.Count==5 && contents.All(c=>c.StackSize==1 && c.Collectible.Code.ToString()=="ships:gun-bronze-north"),
                $"charter: a battery kept from 0.1.0 still loads (its own class, not in the creative inventory) and unpacks into 5 bronze guns ({contents.Count}: "+
                string.Join(", ",contents.Select(c=>c.StackSize+" "+c.Collectible.Code))+")");
        }

        /// <summary>
        /// (13 November 2026, SH-0012, the author: craftable without cannons) The caravel from her charter: launched with none
        /// of her guns (each gun place empty and saved so, nothing of the gun there: not fired); a bronze gun in hand in
        /// building mode mounts on an empty place by the player's own path (the gun used up), every other place takes one, all
        /// in her collision; a mounted gun fires; and taken up again by hand the gun comes back.
        /// </summary>
        void CharterUnarmed(HansaMod mod, ShipDefinition def, BlockPos spawn, int y0)
        {
            var deed=sapi.World.GetItem(new AssetLocation("ships","shipdeed-caravel")) as ItemShipDeed;
            // (SH-0031) the mark is on the stack: the recipe's charter and the creative inventory's carry it; a charter crafted
            // under 0.1.0 (ten guns went into it) has none and launches her armed
            var made=sapi.World.GridRecipes.FirstOrDefault(r=>r.Output?.Code?.Path=="shipdeed-caravel")?.Output?.ResolvedItemStack;
            var fromTab=deed?.CreativeInventoryStacks?.SelectMany(t=>t.Stacks).FirstOrDefault()?.ResolvedItemstack;
            bool old=deed!=null && ItemShipDeed.Unarmed(new ItemStack(deed));
            Check(deed?.ShipCodeOf=="caravel" && ItemShipDeed.Unarmed(made) && ItemShipDeed.Unarmed(fromTab) && !old,
                $"SH-0012, SH-0031: the caravel's charter as crafted now and from the creative inventory launches her without guns ({ItemShipDeed.Unarmed(made)}, {ItemShipDeed.Unarmed(fromTab)}); one crafted under 0.1.0, unmarked, with them ({!old})");
            if(def?.Code!="caravel" || def.GunPlaces==null || def.GunPlaces.Length==0) { Log($"SH-0012: {def?.Code} has no gun places: the unarmed launch not checked"); return; }
            var type=sapi.World.GetEntityType(new AssetLocation("ships",def.Code));
            double wy=EntityShip.FindWaterSurface(sapi.World,new BlockPos(spawn.X,y0+5,spawn.Z,0),2,6);
            var ship=(EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X+0.5,wy,spawn.Z+0.5);
            ship.Pos.Yaw=(float)(System.Math.PI/2);
            EntityShip.LaunchUnarmed(ship,def);
            sapi.World.SpawnEntity(ship);
            try
            {
                var places=def.GunPlaces;
                var guns=def.Fixtures.Where(f=>f.Type=="gun").ToList();
                int absent=guns.Count(ship.GunAbsent);
                var p0=places[0];
                var g0=ship.GunOf(p0);
                string fire0=ship.FireGun(g0,null,null);
                Check(places.All(p=>!ship.GunPlaceLaid(p)) && absent==guns.Count && ship.GunsGone()?.Length==guns.Count && fire0=="ships:gunplace-no-gun" &&
                      ship.WatchedAttributes.GetTreeAttribute("hansaGunPlaces")?.Count==places.Length,
                    $"SH-0012: a caravel from her charter has none of her {guns.Count} guns ({absent} absent; firing one: {fire0}); her {places.Length} gun places empty, saved so");

                // the player's path: building mode, a bronze gun in hand, within reach of the place
                var gunBlock=sapi.World.GetBlock(new AssetLocation(GunPlaceDef.DefaultBlock));
                var ep=new EntityPlayer {Api=sapi};
                ep.LocalEyePos.Set(0,1.62,0);
                var player=new HearthTestPlayer((Vintagestory.Server.ServerMain)sapi.World,ep);
                var hotbar=new InventoryGeneric(12,"hotbar",player.PlayerUID,sapi);
                ((HearthTestInventories)player.InventoryManager).Inventories[hotbar.InventoryID]=hotbar;
                ship.LocalToWorld(g0.Pos[0],g0.Pos[1],g0.Pos[2],out double gx,out double gy,out double gz);
                ep.Pos.SetPos(gx,gy,gz);
                hotbar[0].Itemstack=new ItemStack(gunBlock);
                string notBuilding=ship.TryMountGun(player,p0.Id);
                VanillaPieceBlock.SetBuilding(ep,true);
                bool creative=EntityShip.Creative(player);
                string byHand=ship.TryMountGun(player,p0.Id);
                string twice=ship.MountGunProblem(p0,false);
                Check(notBuilding=="ships:cell-build-mode" && byHand==null && ship.GunPlaceLaid(p0) && ship.GunMounted(g0) && (creative || hotbar[0].Empty) &&
                      twice=="ships:gunplace-taken",
                    $"SH-0012: building mode, a bronze gun in hand: it's mounted on {p0.Id} ({byHand??"ok"}; not out of building mode: {notBuilding}), "+
                    (creative?"a creative player's gun kept":"the gun used up")+$"; that place takes no second ({twice})");

                // every other place takes a gun; where a piece put aboard stands in a gun's working room (SH-0012: the
                // mizzenmast's lantern on its band, in gun-port-quarterdeck-2's), it's refused till that piece is taken down
                var errs=places.Skip(1).Select(p=>(p.Id,Err:ship.MountGun(p,false))).Where(e=>e.Err!=null).ToList();
                var cleared=new List<string>();
                foreach(var e in errs.Where(e=>e.Err=="ships:gunplace-blocked").ToList())
                {
                    var room=MountLayout.GunRoom(ship.GunOf(ship.GunPlace(e.Id)));
                    var inRoom=ship.Pieces.All.Where(piece=>piece.Role!=PieceRole.Railing &&
                        piece.Solids.Concat(piece.Sel).Any(b=>room.Any(r=>PiecePlanner.Overlap(b,r)))).ToList();
                    foreach(var piece in inRoom)
                    {
                        string off=ship.RemovePiece(piece,null);
                        cleared.Add($"{piece.Stack?.Collectible?.Code} on {piece.Mount} ({off??"taken down"})");
                    }
                    if(inRoom.Count>0 && ship.MountGun(ship.GunPlace(e.Id),false)==null) errs.Remove(e);
                }
                if(cleared.Count>0) Log("SH-0012: in a gun's working room on a new caravel, taken down to mount it: "+string.Join("; ",cleared));
                bool Solid(FixtureDef f)=>f.Box is double[] b && ship.Grid.SolidAt((b[0]+b[3])/2,b[1]+0.3,(b[2]+b[5])/2);
                int solid=places.Count(p=>Solid(ship.GunOf(p)));
                Check(errs.Count==0 && solid==places.Length && ship.GunsGone()==null && ship.WatchedAttributes.GetTreeAttribute("hansaGunPlaces")?.Count==0,
                    $"SH-0012: every gun place of hers takes a gun ({(errs.Count==0?"all":string.Join(", ",errs.Select(e=>e.Id+": "+e.Err)))}"+
                    $"{(cleared.Count>0?$"; {cleared.Count} piece(s) in a gun's room taken down first":"")}); {solid} of {places.Length} in her collision, as designed");

                // a mounted gun fires as any
                ship.OpenGunPort(g0,true);
                string fired=FirePrepared(ship,g0);
                Check(fired==null && ship.Gun(g0.Code).Shots==1,$"SH-0012: the gun mounted by hand runs out and fires ({fired??"fired"})");
                ship.Gun(g0.Code).FiredMs=long.MinValue/2;
                ship.Gun(g0.Code).Charge=0;
                ship.HouseGun(g0);

                // taken up again by hand (empty hand, housed and cold): the gun back in the player's inventory
                // (or dropped at their feet: the test's stand-in has no inventory TryGiveItemstack fills)
                hotbar[0].Itemstack=null;
                string took=ship.TryTakeGun(player,p0.Id);
                bool given=player.InventoryManager.Inventories.Values.Any(inv=>inv.Any(sl=>sl?.Itemstack?.Collectible==gunBlock));
                var dropped=sapi.World.GetEntitiesAround(ep.Pos.XYZ,3,3,en=>en is EntityItem ei && ei.Itemstack?.Collectible==gunBlock);
                foreach(var d in dropped) d.Die(EnumDespawnReason.Removed);
                Check(took==null && !ship.GunPlaceLaid(p0) && (creative || given || dropped.Length==1),
                    $"SH-0012: taken up by hand again ({took??"ok"}), the gun back to the player ({(creative?"creative":given?"in their inventory":dropped.Length+" dropped at their feet")})");
            }
            finally
            {
                ship.Die(EnumDespawnReason.Removed);
            }
        }
    }
}
