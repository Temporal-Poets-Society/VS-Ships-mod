using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        static readonly string[] NoteTextures={"game:block/wood/debarked/oak","game:block/wood/planks/oak1","ships:block/glass-clear","game:block/metal/plate/lead","game:block/metal/plate/iron","game:block/clay/brick/four/running/red1","game:block/coal/bituminous"};
        static void Notes135(string root)
        {
            int before=failures;
            var (def,file)=LoadHullDesign(root,"caravel");var l=def.Mounts;
            def.Surfaces=ShipSurfaces.From(file,def.Slots);
            var outDir=Path.Combine(root,"tmp/notes135-views");Directory.CreateDirectory(outDir);
            void Mesh(string name,IEnumerable<ModularPart> parts) => File.WriteAllText(Path.Combine(outDir,name+".json"),JsonConvert.SerializeObject(new {textures=NoteTextures,groups=new Dictionary<string,List<float[]>>{{name,ModularMesh.Quads(parts,keepShared:false)}}}));
            var set=new ShipPieceSet();var chestMount=l.Get("f:main:-13:-2");
            var chest=new FakePiece {Code="game:chest-east",Container=true,Slots=16,Sel=new[]{.0625,0,.0625,.9375,.875,.9375},Col=new[]{.0625,0,.0625,.9375,.875,.9375}};
            var cp=PiecePlanner.Plan(l,chestMount,chest,0);var pc=ShipPiece.From(cp,1,null);set.Add(pc);
            var stack=l.FloorAt(chestMount.Deck,chestMount.I,chestMount.K,1);
            bool Free(MountPoint m)=>!set.Taken(m.Id) && set.Supports(l,m);
            foreach(var hit in new[]{(new Vec3d(-12.468,1.938,-.5625),new double[]{0,0,1}),(new Vec3d(-12.5,2,-1),new double[]{0,1,0})})
                Check(AimPlace.Candidates(l,set,hit.Item1,hit.Item2,pc,Free).FirstOrDefault()==stack,"135: a chest's actual upper side or lid offers its supported stacked cell");

            var shrouds=LateenShrouds.Of(def);
            Check(shrouds.Length==10 && shrouds.All(s=>LateenShrouds.SetUp(def,s,-1,0)),"136/138: both shroud sets and their ratlines are fastened while furled");
            Check(shrouds.All(s=>LateenShrouds.SetUp(def,s,-1,1)==(s.Side>0)),"136/138: under sail only windward shrouds stay set up");
            Check(shrouds.All(s=>Math.Abs(s.Foot[2])<3.4 && s.Foot[1]>(s.Mast.Code=="main"?2.1:5.1)),"141: shrouds terminate at eyes on the upper board, without outboard channels");
            Check(def.Fixtures.All(f=>!f.Swivel) && def.GunPlaces.All(p=>!p.Id.StartsWith("swivel")),"140: no swivel guns or swivel places on the caravel");
            foreach(var name in new[]{"main","mizzen"})
            {
                var hook=l.Get("h:lantern-"+name+"mast");
                var lamp=new FakePiece {Code="game:lantern-small-up",Family="game:lantern-small",Orient=PieceOrient.Omni,States=new[]{"up","down"},Light=true,Sel=new[]{.25,0,.25,.75,.75,.75},Col=null};
                var plan=PiecePlanner.Plan(l,hook,lamp,3);
                Check(plan.Error==null && plan.Dir==0 && plan.Parts[0].Quarter==1,"139/150: "+name+" mast lantern faces the bow, including an omni-attached lantern");
            }

            var doorEntry=def.ModularPieces.Single(e=>e.Code=="door-cabin-starboard");
            var dm=l.ForModular(doorEntry);var dp=PiecePlanner.Plan(l,dm,ModularFake(doorEntry,"door"),ModularToward(doorEntry,"door",dm));
            var door=ShipPiece.From(dp,1,null);door.Open=true;var doors=new ShipPieceSet();doors.Add(door);
            var centre=door.DoorPoint(new[]{(door.Box[0]+door.Box[3])/2,(door.Box[1]+door.Box[4])/2,(door.Box[2]+door.Box[5])/2});
            bool faces=true;
            foreach(int side in new[]{-1,1})
            {
                var eye=new Vec3d(centre[0],centre[1],centre[2]+side*1.5);var dir=new Vec3d(0,0,-side);
                faces &= doors.Pick(eye,dir,4,out _) == door;
            }
            Check(faces && door.SeenBox.SequenceEqual(door.OpenLeafBox),"142: both faces of the rotated leaf are pickable and server visibility checks that leaf");
            var noteEye=new Vec3d(-8.844,2.815,1.759);var noteDir=new Vec3d(.6915,-.3756,-.617);noteDir.Normalize();
            Console.WriteLine("142 exact note ray: "+(doors.Pick(noteEye,noteDir,4,out double rayT)==door?"door at "+rayT:"misses the leaf after the corrected fit"));
            Check(def.ModularPieces.All(e=>e.Code!="window-cabin-starboard") && def.ModularPieces.Any(e=>e.Kind=="partition" && Math.Abs(e.At[0]+7)<.01 && Math.Abs(e.At[1]-2.125)<.01 && Math.Abs(e.At[2]-2)<.01),"143: the default cabin-front window is a wall");
            // (1 November 2026, chalk note 158: the hatch one block toward the main mast, over x -5..-3) the marked whole beam beside
            // the hatch is rib -6's at its after end now (rib -4's is cut by the opening): the note's eye moved with it, 2 aft
            var beam=def.Slots.Get("beam:main:-6:-1");
            Check(beam?.IsBeam==true && beam.Mat.Block=="game:supportbeam-oak","145: the marked beam is a replaceable support-beam length");
            if(beam==null) return;
            var states=new SlotStates(def.Slots);int bi=beam.Index;
            var beamEye=new Vec3d(-5.141,-.18,-1.27);var beamLook=new Vec3d(-.4161,.459,.785);beamLook.Normalize();
            double beamHit=def.Grid.RaycastCell(beamEye,beamLook,4,out int vi,out int vj,out int vk,out _);
            Check(beamHit<4 && def.Slots.AtVoxel(def.Grid,vi,vj,vk)==bi,"145: the exact note ray selects the marked removable beam length");
            foreach(int cover in def.Slots.BesideOf[bi]) states.Set(cover,SlotState.Empty);
            Check(BeamRules.TakeProblem(def.Slots,states,bi)==null,"145: removing its covering deck cell permits beam removal");
            Check(!def.Grid.SolidAt(-6.99,.584,-.049),"146: the old hatch's after headledge no longer leaves a wall fragment at the mark");
            var walls=new ShipPieceSet();
            foreach(var entry in def.ModularPieces.Where(e=>e.Kind=="partition")) {
                var mount=l.ForModular(entry);var plan=PiecePlanner.Plan(l,mount,ModularFake(entry,"partition"),ModularToward(entry,"partition",mount));
                if(plan.Error==null) walls.Add(ShipPiece.From(plan,walls.NextId,null));
            }
            Check(walls.All.All(p=>ModularGeometry.Partition(l,def.Surfaces,walls.All,p).All(part=>part.Box[1]>=l.Get(p.Mount).Y-1e-8)),
                "146: drawn partitions cannot extend below their own cell into the hold");

            var stair=def.ModularPieces.Single(e=>e.Code=="stair-hold");var hatch=def.ModularPieces.Single(e=>e.Code=="hatch-main");
            // (1 November 2026, chalk note 158: "move hatch and stairs one block to main mast"; the user on note 147's clear cell:
            // "Not keep one cell clear. Ideally player could move himself but current placement of stairs is janky") the opening
            // ends on the main mast's cell's after face (x -3), the stair in the cell aft of the hatch's forward cell
            int mainCell=-3;
            Check(stair.Up=="-x" && hatch.Size.SequenceEqual(new[]{2,1}) && Math.Abs(hatch.At[0]+hatch.Size[0]/2.0-mainCell)<1e-6 && Math.Abs(stair.At[0]-(hatch.At[0]-.5))<1e-6,
                "147/158: hold stairs descend toward the bow; the opening reaches the main mast's cell (note 158: no clear cell)");
            var cells=def.Slots.Rect(hatch.At[1],hatch.At[0]-1,-.5,hatch.At[0]+1,.5);
            var hp=HatchPlanner.Plan(def.Slots,cells,false,hatch.Hinge,hatch.Coaming,hatch.DoubleLeaf);
            var leaves=HatchPlanner.Leaves(hp.Box,hp.Dir,hp.DoubleLeaf);
            Check(hp.Error==null && hp.Coaming==.25 && leaves.Count==2 && leaves[0].Sign==-leaves[1].Sign && hp.Box[1]>def.DeckY,"147: a quarter-block coaming and two independently hinged side leaves");
            var hatchParts=HatchPlanner.CoamingParts(hp.Box,hp.Coaming);
            foreach(var leaf in leaves) hatchParts.AddRange(ModularGeometry.HatchLeaf(leaf.Box,leaf.Axis,leaf.Sign<0,false));
            Mesh("hatch-closed",hatchParts);
            var openQuads=ModularMesh.Quads(HatchPlanner.CoamingParts(hp.Box,hp.Coaming),keepShared:false);
            foreach(var leaf in leaves)
            {
                var matrix=new Matrixf().Translate((float)leaf.Hinge[0],(float)leaf.Hinge[1],(float)leaf.Hinge[2]);
                if(leaf.Axis==0) matrix.RotateX((float)(leaf.Sign*HatchPlanner.OpenAngle*Math.PI/180));
                else matrix.RotateZ((float)(leaf.Sign*HatchPlanner.OpenAngle*Math.PI/180));
                matrix.Translate((float)-leaf.Hinge[0],(float)-leaf.Hinge[1],(float)-leaf.Hinge[2]);
                foreach(var q in ModularMesh.Quads(ModularGeometry.HatchLeaf(leaf.Box,leaf.Axis,leaf.Sign<0,false),keepShared:false))
                {
                    for(int k=0;k<4;k++) {var p=Mat4f.MulWithVec4(matrix.Values,new[]{q[1+8*k],q[2+8*k],q[3+8*k],1f});q[1+8*k]=p[0];q[2+8*k]=p[1];q[3+8*k]=p[2];var n=Mat4f.MulWithVec4(matrix.Values,new[]{q[6+8*k],q[7+8*k],q[8+8*k],0f});q[6+8*k]=n[0];q[7+8*k]=n[1];q[8+8*k]=n[2];}
                    openQuads.Add(q);
                }
            }
            File.WriteAllText(Path.Combine(outDir,"hatch-open.json"),JsonConvert.SerializeObject(new {textures=NoteTextures,groups=new Dictionary<string,List<float[]>>{{"hatch-open",openQuads}}}));
            var native=new List<object>();
            string game=Environment.GetEnvironmentVariable("VINTAGE_STORY")??"C:/Users/andri/AppData/Roaming/Vintagestory";
            foreach(var name in new[]{"main","mizzen"})
            {
                const string path="block/metal/lantern/small/ceiling";
                var shape=JsonConvert.DeserializeObject<Shape>(File.ReadAllText(Path.Combine(game,"assets/survival/shapes",path+".json")));
                var plan=PiecePlanner.Plan(l,l.Get("h:lantern-"+name+"mast"),new FakePiece {Code="game:lantern-small-down",Light=true,VisualBounds=PieceShape.Bounds(shape)},0);
                native.Add(new {code="lantern-"+name,path,origin=plan.Parts[0].Origin,quarter=plan.Parts[0].Quarter});
            }
            File.WriteAllText(Path.Combine(outDir,"native-fits.json"),JsonConvert.SerializeObject(native));
            var doorsForView=new List<object>();
            foreach(var entry in def.ModularPieces.Where(e=>e.Kind=="door"))
            {
                var mount=l.ForModular(entry);var block=ModularFake(entry,"door");
                var shape=JsonConvert.DeserializeObject<Shape>(File.ReadAllText(Path.Combine(game,"assets/survival/shapes/block/wood/door/sleek-windowed.json")));
                block.VisualBounds=PieceShape.Bounds(shape);
                var plan=PiecePlanner.Plan(l,mount,block,ModularToward(entry,"door",mount));
                var piece=ShipPiece.From(plan,1,null);
                doorsForView.Add(new {code=entry.Code,box=piece.Box,pivot=piece.Pivot,hinge=piece.DoorHinge,angle=piece.DoorAngle,high=piece.HingeHighEnd});
            }
            File.WriteAllText(Path.Combine(outDir,"doors.json"),JsonConvert.SerializeObject(doorsForView));
            var saved=ShipPiece.From(hp,99,null);saved.Open=true;var tree=new TreeAttribute();saved.ToTree(tree);var restored=ShipPiece.FromTree(tree);
            Check(restored.Coaming==.25 && restored.DoubleLeaf && restored.Open,"147: coaming, double leaves and open state survive saving");
            var pose=new FloodPose {X=0,Y=0,Z=0,Yaw=0,Pitch=0,Roll=0,Surface=(x,z)=>def.DeckY+.1};
            string opening="cell:"+cells[0];
            Check(ShipFlood.Downflooding(def.DamageParts,pose,c=>c==opening,c=>c==opening?.25:0) < ShipFlood.Downflooding(def.DamageParts,pose,c=>c==opening),"147: shallow deck water is kept below the raised hatch sill");

            var hull=new SlotStates(def.Hull);var transom=def.HullSlots.FirstOrDefault(s=>s.Cell!=null && s.Cell[0]==-16 && s.Sill>1.5 && s.Box[2]<1.072 && s.Box[5]>1.072);
            Check(transom!=null && HullWindows.Fits(def,transom),"148: the marked transom has a removable panel which can take a window");
            Check(def.HullSlots.All(s=>s.Mat.Block=="game:plank-oak") && def.DeckSlots.Where(s=>s.IsBeam || s.IsStructure).All(s=>s.Mat.Block=="game:supportbeam-oak"),"148: hull boards and structural support beams have matching replacement/return materials");
            Check(def.HullSlots.Any(s=>s.Sill<def.Waterline-2) && def.HullSlots.Any(s=>s.Cell!=null && s.Normal[0]<-.7),"148: bottom and transom panels are slots");
            Check(def.DeckSlots.Count(s=>s.Kind=="rib")>=190,"148: every sliced rib cell is removable");
            if(transom!=null)
            {
                hull.Set(transom.Index,SlotState.Glazed);var ht=new TreeAttribute();hull.ToTree(ht);var again=new SlotStates(def.Hull);again.FromTree(ht);
                Check(again[transom.Index]==SlotState.Glazed && again.Filled(transom.Index) && !again.MeshFilled(transom.Index),"148: a hull window is saved, sealed and replaces the wood mesh");
                Mesh("transom-window",HullWindows.Parts(transom));
            }
            var pump=def.Fixtures.Single(f=>f.Type=="pump");PumpJet.Spout(pump,out var spout,out var direction);double flight=PumpJet.Landing(pump,out _);
            var jet=PumpJet.Stream(direction,flight,.2);
            Check(jet.Count==240 && PumpJet.Stream(direction,flight,0).Count==0 && Enumerable.Range(0,20).All(n=>PumpJet.Pulse(n*.17,1)==1),"149: continuous connected stream while pumping, no drips or stroke gaps, none dry");
            File.WriteAllText(Path.Combine(outDir,"pump-stream.json"),JsonConvert.SerializeObject(new {textures=new[]{"game:block/liquid/waterportion"},groups=new Dictionary<string,List<float[]>>{{"pump-stream",jet.Select(q=>{var r=(float[])q.Clone();for(int k=0;k<4;k++){r[1+8*k]+=(float)spout.X;r[2+8*k]+=(float)spout.Y;r[3+8*k]+=(float)spout.Z;}return r;}).ToList()}}}));

            var floor=new VoxelGrid(0,0,0,24,20,24,.25,new byte[(24*20*24+7)/8]);floor.Fill(new[]{0.0,0,0,6,.25,6});
            var elk=new CreatureDeckMotion(3,2,3,1.2,1.8,.9);
            for(int i=0;i<100;i++) elk.Move(floor,0,-.04,0);
            Check(elk.OnGround && Math.Abs(elk.Y-.25)<1e-8 && Math.Abs(elk.HalfX-.45)<1e-9 && Math.Abs(elk.HalfZ-.6)<1e-9,"elk: a full creature collision box lands and stands on the deck");
            floor.Fill(new[]{2.0,0,2,4,.25,4},false);
            Check(!elk.Move(floor,0,-.5,0),"elk: removing the deck lets the creature fall through the real opening");
            Creatures(def);
            Check(!def.Fixtures.Any(f=>f.Type=="hitch"),"155: hitching is supplied by placed native fence posts, not a fixed fixture");
            Console.WriteLine(failures==before?"notes135 ALL OK":$"notes135 {failures-before} FAILED");
        }
    }
}
