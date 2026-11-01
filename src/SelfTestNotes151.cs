using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public partial class SelfTest
    {
        void Notes151Aboard(EntityShip ship)
        {
            var hatch=ship.Pieces.All.SingleOrDefault(p=>p.Role==PieceRole.Hatch);
            Check(hatch!=null,"152: a new ship has the main hatch");
            if(hatch==null) return;
            bool Pick(Vec3d eye,Vec3d look) {
                ship.LocalToWorld(eye.X,eye.Y,eye.Z,out double x,out double y,out double z);
                var d=ship.LocalDirToWorld(look.X,look.Y,look.Z);
                return ship.RayHit(new Vec3d(x,y,z),d,5,null,out var hit) && hit.Part.Kind==ShipPartKind.Piece && hit.Part.Index==hatch.Id;
            }
            // (1 November 2026, chalk note 158: "move hatch and stairs one block to main mast") the hatch over x -5..-3: the
            // notes' eyes and bodies moved with it, one block forward
            Check(!hatch.Open && Pick(new Vec3d(-5.552,2.827,.919),new Vec3d(.6448,-.6503,-.4016)),"152: exact note ray selects the hatch instead of Woodwork");
            Check(ShipPieceSet.SeenPiece(ship.DesignGrid,new Vec3d(-5.552,2.827,.919),hatch),"152: server use visibility accepts the exact player's eye above the closed hatch");
            bool closed=hatch.Mounts.All(id=>!ship.CellOpen(ship.SlotLayout.IndexOf(id.Substring(5))));
            Check(ship.ToggleHatch(null,hatch) && hatch.Open && hatch.Mounts.All(id=>ship.CellOpen(ship.SlotLayout.IndexOf(id.Substring(5)))) &&
                !ship.Grid.SolidAt(-4,1.45,0),"152: new ship's double hatch opens, unseals both cells and clears the hold stair");
            bool openPicked=true;
            foreach(var b in hatch.PlacementBoxes) {
                var c=new Vec3d((b[0]+b[3])/2,(b[1]+b[4])/2,(b[2]+b[5])/2);
                foreach(int side in new[]{-1,1}) openPicked &= Pick(c.AddCopy(0,.1,side*.75),new Vec3d(0,-.1,-side*.75));
            }
            Check(openPicked,"152: either open leaf is selectable from inside and outside the opening");
            var stair=ship.Pieces.All.FirstOrDefault(p=>p.Role==PieceRole.Stair && p.Box[1]<0);
            var foot=stair==null?null:ship.Mounts.Get(stair.Mount);
            bool walked=foot!=null && WalkNear(ship.Grid,foot,stair.Dir,stair.Rise,PiecePlanner.RunOf(stair),out _,out _,out _);
            Check(walked,"152: the default hold stair is walked up and down through the opened coaming");
            Check(ship.ToggleHatch(null,hatch) && !hatch.Open && closed && hatch.Mounts.All(id=>!ship.CellOpen(ship.SlotLayout.IndexOf(id.Substring(5)))),"152: new ship's hatch closes and seals both deck cells again");
            Check(hatch.Mounts.All(id=>ship.HatchCoaming("cell:"+ship.SlotLayout.IndexOf(id.Substring(5)))==.25),"152: both cells retain the flooding coaming height");
            // A real mounted agent exercises the body's authoritative ship-local position, including a person
            // standing right against the coaming. No client or player save is needed for this regression.
            var actorType=sapi.World.GetEntityType(new AssetLocation("game:chicken-hen"));
            var actor=actorType==null?null:sapi.World.ClassRegistry.CreateEntity(actorType) as EntityAgent;
            if(actor!=null) {
                ship.LocalToWorld(-5.7,1.125,.965,out double ax,out double ay,out double az);actor.Pos.SetPos(ax,ay,az);sapi.World.SpawnEntity(actor);
                var seat=ship.GetOrCreateDeckSeat("notes153-hatch-body",-5.7,1.125,.965);
                try {
                    bool mounted=actor.TryMount(seat), toggled=mounted;
                    foreach(var at in new[]{new[]{-5.7,.965},new[]{-4.0,.925},new[]{-4.0,-.925},new[]{-5.425,0.0},new[]{-2.575,0.0},new[]{-5.425,-.925},new[]{-5.425,.925},new[]{-2.575,-.925},new[]{-2.575,.925}}) {
                        seat.SetLocal(at[0],1.125,at[1],DeckSeat.FlagOnGround);
                        toggled &= ship.ToggleHatch(null,hatch) && hatch.Open && ship.ToggleHatch(null,hatch) && !hatch.Open;
                    }
                    Check(toggled,"153: actual server opens and closes with a body at the recorded location, both sides, ends and all coaming corners");
                    seat.SetLocal(-4,1.125,0,DeckSeat.FlagOnGround);
                    Check(!ship.ToggleHatch(null,hatch) && !hatch.Open,"153: opening refuses a body really standing on the leaves");
                    hatch.Open=true;
                    Check(!ship.ToggleHatch(null,hatch) && hatch.Open,"153: closing refuses a body really in the leaf swing");
                    seat.SetLocal(-5.7,1.125,.965,DeckSeat.FlagOnGround);
                    ship.ToggleHatch(null,hatch);
                } finally {actor.TryUnmount();actor.Die(Vintagestory.API.Common.EnumDespawnReason.Removed);}
            } else Check(false,"153: native body actor available");
            Check(ship.World.GridRecipes.Any(r=>r.Name?.ToString()=="ships:leadshot")==ship.Mod.Config.CannonsNeedShot,"cannons: temporary sling-bullet recipe exists only when CannonsNeedShot is enabled");

            var l=ship.Mounts;
            var blocks=new[] {
                sapi.World.Blocks.FirstOrDefault(b=>b?.Code?.Domain=="game" && b.Code.Path=="chest-east"),
                sapi.World.Blocks.FirstOrDefault(b=>b is Vintagestory.GameContent.BlockGenericTypedContainerTrunk),
                sapi.World.Blocks.FirstOrDefault(b=>b is Vintagestory.GameContent.BlockCrate && b.Id>0),
                sapi.World.Blocks.FirstOrDefault(b=>b?.Code?.Path?.StartsWith("storagevessel-")==true && b.Code.Path.EndsWith("-fired"))
            };
            foreach(var block in blocks) {
                Check(block!=null,"151: native stackable container resolves ("+(block?.Code.ToString()??"missing")+")");
                if(block==null) continue;
                var stack=new ItemStack(block);var native=new VanillaPieceBlock(sapi.World,stack,false);
                // Full-height crates/vessels need the clear centre aisle, rather than the new outer cargo shelf.
                var mount=l.FloorAt("hold",-9,block is Vintagestory.GameContent.BlockGenericTypedContainerTrunk?0:-1);
                var plan=PiecePlanner.Plan(l,mount,native,0);
                string error=ship.PlacePiece(plan,stack,false,out var lower);
                Check(error==null,"151: lower native "+block.Code+" placed ("+(error??"ok")+")");
                if(lower==null) continue;
                var eyes=lower.Sel.Select(b=>new Vec3d((b[0]+b[3])/2,b[4]+1,(b[2]+b[5])/2)).ToList();
                if(block is Vintagestory.GameContent.BlockGenericTypedContainerTrunk) eyes.Insert(0,new Vec3d(-7.759,-.175,.896));
                bool stacked=true;string details="";
                foreach(var eye in eyes) {
                    var d=block is Vintagestory.GameContent.BlockGenericTypedContainerTrunk && eye==eyes[0]?new Vec3d(-.6205,-.5615,-.5475).Normalize():new Vec3d(0,-1,0);
                    bool got=AimPlace.PieceFace(lower,eye,d,5,out double t,out var normal);
                    var hit=eye.AddCopy(d.X*t,d.Y*t,d.Z*t);
                    var candidates=got?AimPlace.Candidates(l,ship.Pieces,hit,normal,lower,m=>m.Kind==MountKind.Floor && m.Level==1 && !ship.Pieces.Taken(m.Id)&&ship.Pieces.Supports(l,m)):new();
                    ShipPiece upper=null;
                    foreach(var m in candidates) {
                        var upperPlan=PiecePlanner.Plan(l,m,native,0);
                        error=ship.PlacePiece(upperPlan,stack,false,out upper);
                        if(upper!=null) break;
                    }
                    stacked &= upper!=null;
                    details += $" hit={got} candidates={candidates.Count} result={error??"ok"}";
                    if(upper!=null) { string removed=ship.RemovePiece(upper,null);stacked &= removed==null;details+=" removed="+(removed??"ok"); }
                }
                Check(stacked,"151: Shift-place on native "+block.Code+" on its lid"+(block is Vintagestory.GameContent.BlockGenericTypedContainerTrunk?" (both halves and exact note ray)":"")+":"+details);
                Check(ship.RemovePiece(lower,null)==null,"151: lower container taken up after the stacked one");
            }

            // Emulate a dev.17 shelf in a saved ship. Its mount is no longer offered, but loading/migration must
            // retain the piece and let the player take it up. The ordinary native stack is kept, not converted.
            PiecePlan savedShelf;
            var slab=sapi.World.GetBlock(new AssetLocation("game:plankslab-oak-down-free"));
            try {
                MountLayout.ShelvesEnabled=true;var oldLayout=MountLayout.Build(ship.Def);
                savedShelf=PiecePlanner.Plan(oldLayout,oldLayout.HorizontalAt("hold",-4,0,1),new VanillaPieceBlock(sapi.World,new ItemStack(slab),true),0);
            } finally {MountLayout.ShelvesEnabled=false;}
            var shelf=ShipPiece.From(savedShelf,ship.Pieces.NextId,new ItemStack(slab));ship.Pieces.Add(shelf);
            var savedPieces=new Vintagestory.API.Datastructures.TreeAttribute();ship.Pieces.ToTree(savedPieces);ship.WatchedAttributes["hansaPieces"]=savedPieces;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(EntityShip).GetMethod("LoadPieces",flags).Invoke(ship,null);
            typeof(EntityShip).GetMethod("MigrateDecor",flags).Invoke(ship,null);
            var loadedShelf=ship.Pieces.Get(shelf.Id);
            Check(loadedShelf?.Role==PieceRole.Shelf && loadedShelf.Box.SequenceEqual(shelf.Box) && loadedShelf.Stack?.Block==slab &&
                ship.RemovePiece(loadedShelf,null)==null,"154: a saved native shelf survives real loading/migration and remains removable while new shelf places are disabled");
        }
    }
}
