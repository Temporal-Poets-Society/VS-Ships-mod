using System;
using System.Linq;
using Hansa;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        static void Notes153(string root)
        {
            int before=failures;
            Tethers();
            var (def,file)=LoadHullDesign(root,"caravel");
            var entry=def.ModularPieces.Single(e=>e.Code=="hatch-main");
            // (1 November 2026, chalk note 158: "move hatch and stairs one block to main mast") the hatch over x -5..-3: the
            // note's eye, bodies and rays moved with it, one block forward
            var cells=def.Slots.Rect(entry.At[1],-5,-.5,-3,.5);
            var hatch=ShipPiece.From(HatchPlanner.Plan(def.Slots,cells,false,entry.Hinge,entry.Coaming,entry.DoubleLeaf),1,null);
            var pieces=new ShipPieceSet();pieces.Add(hatch);
            var eye=new Vec3d(-5.698,2.823,.972);var look=new Vec3d(.681,-.6223,-.386).Normalize();
            Check(pieces.Pick(eye,look,5,out double t)==hatch && Math.Abs(t-2.126)<.06,"153: exact note ray hits the drawn leaf");
            bool Body(double x,double y,double z)=>HatchPlanner.BodyInSwing(hatch.Box,hatch.Dir,true,false,new[]{x-.3,y,z-.3,x+.3,y+1.85,z+.3});
            Check(!Body(-5.7,1.125,.965),"153: the recorded player's body beside the coaming does not block the swing");
            int tested=0,skipped=0;bool picks=true,seen=true,clear=true,skipOnlyAhead=true;
            // (1 November 2026, note 158: the hatch's forward end on the main mast's cell) no player stands in the mast: an eye whose
            // body (0.6 wide, up to the eye) meets her collision is left out; only ones at the forward end may be
            bool Stands(Vec3d o)=>!new[]{-.3,0,.3}.Any(dx=>new[]{-.3,0,.3}.Any(dz=>new[]{1.3,2.0,2.8}.Any(y=>def.Grid.SolidAt(o.X+dx,y,o.Z+dz))));
            foreach(bool open in new[]{false,true}) {
                hatch.Open=open;
                var grid=def.Grid.Clone();
                foreach(var part in HatchPlanner.CoamingParts(hatch.Box,hatch.Coaming)) grid.Fill(part.Box);
                if(!open) foreach(var b in hatch.Solids) grid.Fill(b);
                var eyes=Enumerable.Range(0,13).SelectMany(i=>new[]{new Vec3d(-5.425+i*2.85/12,2.823,-.925),new Vec3d(-5.425+i*2.85/12,2.823,.925)})
                    .Concat(Enumerable.Range(0,9).SelectMany(i=>new[]{new Vec3d(-5.425,2.823,-.925+i*1.85/8),new Vec3d(-2.575,2.823,-.925+i*1.85/8)}));
                foreach(var o in eyes) {
                    if(!Stands(o)) {skipped++;skipOnlyAhead&=o.X>hatch.Box[3];continue;}
                    bool blocked=Body(o.X,1.125,o.Z);
                    clear &= !blocked;
                    foreach(var leaf in HatchPlanner.Leaves(hatch.Box,hatch.Dir,true)) {
                        var centre=PlacementMesh.Transform(HatchPlanner.LeafPose(leaf,open?100:0),new[]{(leaf.Box[0]+leaf.Box[3])/2,(leaf.Box[1]+leaf.Box[4])/2,(leaf.Box[2]+leaf.Box[5])/2});
                        var d=new Vec3d(centre[0]-o.X,centre[1]-o.Y,centre[2]-o.Z).Normalize();
                        picks &= pieces.Pick(o,d,5,out _)==hatch;
                        seen &= ShipPieceSet.SeenPiece(grid,o,hatch);
                        tested++;
                    }
                }
            }
            Check(picks && seen && tested+skipped*2==176 && skipOnlyAhead && skipped<=2*5,$"153: {tested} rays around both sides, both ends and corners, closed/open, pick and permit server use ({skipped/2} eyes ahead of it in the mast left out)");
            Check(clear,"153: standing against any edge/corner of the coaming leaves the swing clear");
            Check(Body(-4,1.125,0) && Body(-4,.3,.1),"153: a real body on the lid or rising through the swept leaves blocks opening/closing");
            Check(!Body(-4,-1.875,0),"153: a body entirely below the raised leaf swing does not block it");
            Check(pieces.Pick(new Vec3d(-4,3,0),new Vec3d(0,-1,0),4,out _)==null,"153: an open hatch has no selection box across its old closed plane");
            hatch.Open=false;
            Check(pieces.Pick(new Vec3d(-4,3,0),new Vec3d(0,-1,0),4,out _)==null,"153: the gap between closed leaves is not a phantom face");
            Check(pieces.Pick(new Vec3d(-4,1.25,1),new Vec3d(0,0,-1),1,out _)==null,"153: fixed coaming is not a moving leaf's selection box");

            var mounts=MountLayout.Build(def);
            Check(!MountLayout.ShelvesEnabled && !mounts.Horizontals.Any(m=>m.Id.StartsWith("hb:") && m.Level<3) &&
                !(def.StartPieces??Array.Empty<StartPieceDef>()).Any(p=>p.Mount.StartsWith("hb:hold:")),"154: no default shelves or shelf places");
            MountLayout.ShelvesEnabled=true;
            var legacy=MountLayout.Build(def);
            var slab=new FakePiece {Code="game:plankslab-oak-down-free",Slab=true};
            var oldShelf=ShipPiece.From(PiecePlanner.Plan(legacy,legacy.HorizontalAt("hold",-4,0,1),slab,0),2,null);
            var oldPieces=new ShipPieceSet();oldPieces.Add(oldShelf);var tree=new TreeAttribute();oldPieces.ToTree(tree);
            MountLayout.ShelvesEnabled=false;
            var restored=new ShipPieceSet();restored.FromTree(tree);
            Check(restored.Get(2)?.Role==PieceRole.Shelf && restored.Get(2).Box.SequenceEqual(oldShelf.Box),"154: saved shelves retain their geometry and load as pieces");
            Check(mounts.Horizontals.Where(m=>m.Level==3).Select(m=>m.Id).OrderBy(s=>s).SequenceEqual(legacy.Horizontals.Where(m=>m.Level==3).Select(m=>m.Id).OrderBy(s=>s)),"154: well-top places unchanged by shelf switch");
            Check(!def.Fixtures.Any(f=>f.Type=="hitch"),"155: no fixed hitching-post fixture");

            var powder=new Item {Code=new AssetLocation(GunLoading.Powder)};
            var wrong=new Item {Code=new AssetLocation(GunLoading.Shot)};
            var stock=new DummySlot(new ItemStack(powder,2));var bullets=new DummySlot(new ItemStack(wrong,8));
            var inventory=new ItemSlot[]{new DummySlot(),bullets,stock};
            Check(GunLoading.TakePowder(inventory) && stock.StackSize==1 && bullets.StackSize==8 && GunLoading.TakePowder(inventory) && stock.Empty &&
                !GunLoading.TakePowder(inventory),"156: inventory transaction takes one blasting powder per shot, never shot or a nonexistent charge");
            Console.WriteLine(failures==before?"notes153 ALL OK":$"notes153 {failures-before} FAILED");
        }
    }
}
