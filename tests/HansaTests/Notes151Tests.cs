using System;
using System.Linq;
using Hansa;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        static void Notes151(string root)
        {
            int before=failures;
            var (def,file)=LoadHullDesign(root,"caravel");
            var entry=def.ModularPieces.Single(e=>e.Code=="hatch-main");
            // (1 November 2026, chalk note 158: "move hatch and stairs one block to main mast") the hatch over x -5..-3 now: the
            // note's eye and rays moved with it, one block forward
            var cells=def.Slots.Rect(entry.At[1],-5,-.5,-3,.5);
            var plan=HatchPlanner.Plan(def.Slots,cells,false,entry.Hinge,entry.Coaming,entry.DoubleLeaf);
            var hatch=ShipPiece.From(plan,1,null);var pieces=new ShipPieceSet();pieces.Add(hatch);
            var eye=new Vec3d(-5.552,2.827,.919);var look=new Vec3d(.6448,-.6503,-.4016).Normalize();
            var grid=def.Grid.Clone();foreach(var b in hatch.Solids) grid.Fill(b);
            double coarse=grid.Raycast(eye,look,5);
            Check(pieces.Pick(eye,look,5,out double exact)==hatch && exact>coarse+.02 && ShipPieceSet.HatchVoxelHit(hatch,grid,eye,look,coarse),
                $"152: exact player ray finds hatch at {exact:0.000}, ahead of its own voxel at {coarse:0.000}");
            Check(ShipPieceSet.SeenPiece(grid,eye,hatch),"152: server visibility accepts the note's eye despite the hatch's rounded collision");
            var blocked=grid.Clone();blocked.Fill(new double[]{-5.75,-2,-2,-5.5,5,2});
            Check(!ShipPieceSet.SeenPiece(blocked,new Vec3d(-6,2.8,1),hatch),"152: unrelated timber still blocks use through it");
            foreach(bool open in new[]{false,true}) {
                hatch.Open=open;bool all=true;
                foreach(var leaf in HatchPlanner.Leaves(hatch.Box,hatch.Dir,hatch.DoubleLeaf)) {
                    var part=new MovingPartDef {AxisPoint=leaf.Hinge,AxisDir=new double[]{1,0,0},Angle=leaf.Sign*HatchPlanner.OpenAngle};
                    var matrix=new float[16];PartMotion.Place(part,open?1:0,matrix,null);
                    var c=new[]{(leaf.Box[0]+leaf.Box[3])/2,(leaf.Box[1]+leaf.Box[4])/2,(leaf.Box[2]+leaf.Box[5])/2};
                    var centre=PlacementMesh.Transform(matrix,c);
                    foreach(int side in new[]{-1,1}) foreach(double distance in new[]{.6,2,4.25}) foreach(double along in new[]{-.25,0,.25}) {
                        var p=PlacementMesh.Transform(matrix,new[]{c[0]+along*distance,c[1]+side*distance,c[2]});
                        var o=new Vec3d(p[0],p[1],p[2]);var d=new Vec3d(centre[0]-p[0],centre[1]-p[1],centre[2]-p[2]).Normalize();
                        all &= pieces.Pick(o,d,5,out _)==hatch;
                    }
                }
                Check(all,$"152: both {(open?"open":"closed")} leaves picked from either face, oblique and 0.6–4.25 blocks away");
            }
            Check(pieces.Pick(new Vec3d(-4,3,0),new Vec3d(0,-1,0),4,out _)==null,"152: open leaves leave the hatchway clear, including its former closed plane");
            Check(!def.Grid.SolidAt(-4,1.1,0) && cells.All(c=>def.Slots.Slots[c].Start=="empty"),"152: exported deck collision has no solid lid over the stair opening");
            bool InTriangle(float[] q,int a,int b,int c,double x,double z) {
                double X(int i)=>q[1+8*i];
                double Z(int i)=>q[3+8*i];
                double det=(Z(b)-Z(c))*(X(a)-X(c))+(X(c)-X(b))*(Z(a)-Z(c));
                if(Math.Abs(det)<1e-10) return false;
                double u=((Z(b)-Z(c))*(x-X(c))+(X(c)-X(b))*(z-Z(c)))/det;
                double v=((Z(c)-Z(a))*(x-X(c))+(X(a)-X(c))*(z-Z(c)))/det;
                return u>=0 && v>=0 && u+v<=1;
            }
            int covered=0;var states=new SlotStates(def.Slots);
            foreach(var group in file.Groups) for(int i=0;i<group.Value.Length;i++) {
                var q=group.Value[i];if(q.Length<33 || Enumerable.Range(0,4).Any(k=>Math.Abs(q[2+8*k]-entry.At[1])>.001)) continue;
                if(group.Key.StartsWith("slot:deck") && file.SlotQuads.TryGetValue(group.Key,out var refs)) {
                    int owner=refs[2*i],facing=refs[2*i+1];
                    if(owner>=0 && !states.Filled(owner) || facing>=0 && states.Filled(facing)) continue;
                }
                foreach(double x in new[]{-4.75,-4.25,-3.75,-3.25}) foreach(double z in new[]{-.3,0,.3})
                    if(InTriangle(q,0,1,2,x,z)||InTriangle(q,0,2,3,x,z)) covered++;
            }
            Check(covered==0,"152: the exported game mesh draws no deck top across the hatchway");

            var l=def.Mounts;var mount=l.FloorAt("hold",-9,0);
            var trunk=new FakePiece {Code="game:trunk-north",Family="game:trunk",Orient=PieceOrient.Orientable,States=Sides,Container=true,TurnMesh=true,Slots=36,
                Sel=new[]{.0625,0,.0625,1,.8125,.9375},Col=new[]{.0625,0,.0625,1,.8125,.9375},
                CellsBy=new() { ["west"]=new(){new[]{0,0,0},new[]{0,0,-1}} }};
            var lower=ShipPiece.From(PiecePlanner.Plan(l,mount,trunk,0),1,null);var containers=new ShipPieceSet();containers.Add(lower);
            var trunkEye=new Vec3d(-7.759,-.175,.896);var trunkLook=new Vec3d(-.6205,-.5615,-.5475).Normalize();
            bool got=AimPlace.PieceFace(lower,trunkEye,trunkLook,5,out double trunkT,out var normal);
            var point=trunkEye.AddCopy(trunkLook.X*trunkT,trunkLook.Y*trunkT,trunkLook.Z*trunkT);
            var candidates=AimPlace.Candidates(l,containers,point,normal,lower,m=>m.Kind==MountKind.Floor && m.Level==1 && containers.Supports(l,m));
            Check(got && candidates.FirstOrDefault()?.K==-1 && candidates.Any(m=>PiecePlanner.Plan(l,m,trunk,0).Mounts.All(id=>containers.Supports(l,l.Get(id)))),
                "151: the exact trunk ray offers the aimed half's upper cell and a supported two-cell placement");
            Check(GunLoading.After(0)==GunLoading.PowderIn && GunLoading.Ready(GunLoading.PowderIn) && !GunLoading.Ready(0) &&
                GunLoading.Problem(0,true,false,GunLoading.Powder,1,1,false)==null && GunLoading.Problem(1,true,false,GunLoading.Powder,1,1,false)=="ships:gun-load-full" &&
                !GunLoading.IsLoad(GunLoading.Shot) && GunLoading.IsLoad(GunLoading.Shot,true),"cannons: one powder loads, no double charge, shot recognized only with switch enabled");
            Check(!GunLoading.Ready(1,true) && GunLoading.After(1,true)==3 && GunLoading.Ready(3,true) && GunLoading.Ready(3,false),"cannons: optional two-stage loading and saved legacy full charges remain usable");
            Console.WriteLine(failures==before?"notes151 ALL OK":$"notes151 {failures-before} FAILED");
        }
    }
}
