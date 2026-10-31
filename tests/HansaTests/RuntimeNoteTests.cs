using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        static void RuntimeNotes(string root)
        {
            var (def,file)=LoadHullDesign(root,"caravel");
            Console.WriteLine("dev.14 runtime notes");
            var rigFile=PlacementMesh.Prepare(def,file);
            var mesh=new PlacementMesh(rigFile);
            var floorHit=new Vec3d(-3.827,-1.875,-0.069);
            var ceilingHit=new Vec3d(-3.574,0.375,-0.459);
            var foot=AimPlace.StairFloors(def.Mounts,floorHit,new double[]{0,1,0}).FirstOrDefault();
            var fromBelow=AimPlace.StairFloors(def.Mounts,ceilingHit,new double[]{0,-1,0}).FirstOrDefault();
            Check(foot?.Id=="f:hold:-4:-1" && fromBelow==foot,"113/114: the actual floor and deck underside identify the hold floor cell, without an air target");
            Check(!AimPlace.StairFloors(def.Mounts,new Vec3d(-3.8,-.7,0),new double[]{0,1,0}).Any(),"113: a point in the air is not a stair's foot");
            Console.WriteLine("112: removable slot at the marked headledge: "+def.DeckSlots.FirstOrDefault(s=>s.IsBeam && -1.052>s.Box[2] && -1.052<s.Box[5] && s.Box[0]<=-7.75 && s.Box[3]>=-7.75)?.Id);
            Console.WriteLine("114: removable slot at the hatch beam: "+def.DeckSlots.FirstOrDefault(s=>s.IsBeam && s.Box[0]<-3.574 && s.Box[3]>-3.574 && s.Box[2]<0 && s.Box[5]>0)?.Id);
            var mastClash=PiecePlanner.Plan(def.Mounts,foot,new FakePiece {Code="x",Stairs=true,StairCells=1},0);
            Check(PiecePlanner.GridClash(def.Mounts,mastClash,def.Grid)!=null,"113: the mainmast still protects the approach to a stair rising aft from the marked cell");

            string game=Environment.GetEnvironmentVariable("VINTAGE_STORY")??"C:/Users/andri/AppData/Roaming/Vintagestory";
            var lanternShape=JsonConvert.DeserializeObject<Shape>(File.ReadAllText(Path.Combine(game,"assets/survival/shapes/block/metal/lantern/small/ceiling.json")));
            var lanternBounds=PieceShape.Bounds(lanternShape);
            var lanternHook=new MountPoint {Id="h:old-mainmast",Kind=MountKind.Hook,X=-3.1117,Y=3.685,Z=0,Hang="down"};
            var lantern=PiecePlanner.Plan(def.Mounts,lanternHook,new FakePiece {Code="test:lantern",Light=true,VisualBounds=lanternBounds},0);
            PieceShape.FitVolumes(lantern,PieceShape.Volumes(lanternShape),lantern.Parts[0].Origin,lantern.Parts[0].Quarter);
            Console.WriteLine("117 actual small lantern "+string.Join(",",lantern.Box)+" mesh "+mesh.Hit(lantern.Box)+" elements "+string.Join(",",lantern.Volumes.Select(v=>mesh.Hit(v.Box,null,null,v.Pose)).Where(s=>s!=null)));
            Check(lanternBounds[3]-lanternBounds[0]<.6,"117: invisible lantern helper geometry is excluded from its footprint");
            var rigPose=new Dictionary<string,float[]>();PlacementMesh.RigPoses(def,rigPose,1,0,1);
            string obstruction=lantern.Volumes.Select(v=>mesh.Hit(v.Box,null,g=>rigPose.TryGetValue(g.StartsWith("part:")?g.Substring(5):g,out var p)?p:null,v.Pose)).FirstOrDefault(s=>s!=null);
            Check(obstruction?.StartsWith("rig:shroud:")==true,"117: the real lantern intersects a cast-off shroud hanging beside the mast ("+obstruction+")");
            foreach(var mast in new[]{("mainmast",-2.5,3.685),("mizzenmast",-10.5,6.685)}) {
                var safeHook=new MountPoint {Id="h:test",Kind=MountKind.Hook,X=mast.Item2,Y=mast.Item3,Z=-.7,Hang="down"};
                var safe=PiecePlanner.Plan(def.Mounts,safeHook,new FakePiece {Code="test:lantern",Light=true,VisualBounds=lanternBounds},0);
                PieceShape.FitVolumes(safe,PieceShape.Volumes(lanternShape),safe.Parts[0].Origin,safe.Parts[0].Quarter);
                foreach(float lee in new[]{-1f,1f}) {
                    var pose=new Dictionary<string,float[]>();PlacementMesh.RigPoses(def,pose,lee,0,0);
                    Check(safe.Volumes.All(v=>mesh.Hit(v.Box,null,g=>pose.TryGetValue(g.StartsWith("part:")?g.Substring(5):g,out var p)?p:null,v.Pose)==null),
                        "117: proposed port hook at "+mast.Item1+" clears the drawn rig on tack "+lee);
                }
            }
            var hanger=new MountPoint {Id="h:test",Kind=MountKind.Hook,X=-2.5,Y=3,Z=0,Hang="down"};
            var light=new FakePiece {Code="test:light",Light=true,VisualBounds=new double[]{.1,.05,.2,.5,.8,.6}};
            var hung=PiecePlanner.Plan(def.Mounts,hanger,light,0);
            Check(hung.Solids.Count==0 && Math.Abs(hung.Box[4]-3)<1e-6 && PiecePlanner.GridClash(def.Mounts,hung,def.Grid)!=null,
                "117: a hanging piece has a fitted visible footprint even though nobody collides with it");

            var bear=JsonConvert.DeserializeObject<Shape>(File.ReadAllText(Path.Combine(game,"assets/survival/shapes/entity/nonliving/boat/figurehead/bear.json")));
            var bounds=PieceShape.Bounds(bear);
            Console.WriteLine("bear bounds "+string.Join(",",bounds));
            var hook=def.Mounts.Get("h:figurehead");
            var figure=PiecePlanner.Plan(def.Mounts,hook,new FakePiece {Code="game:figurehead-bear",Figurehead=true,VisualBounds=bounds},0);
            PieceShape.FitVolumes(figure,PieceShape.Volumes(bear),figure.Parts[0].Origin,figure.Parts[0].Quarter);
            var tilt=PieceShape.Tilt(figure.Pivot,figure.Tilt,figure.Dir);
            Check(figure.Parts[0].Quarter==1 && Math.Abs((figure.Box[2]+figure.Box[5])/2)<1e-6 && Math.Abs(figure.Parts[0].Origin[0]+bounds[2]-hook.X)<1e-6,
                "118: the bear faces forward, its actual back is on the hook and its visible width is centred on the stem");
            Check(hook.Y>=2.4 && hook.Y<=2.9 && figure.Volumes.All(v=>mesh.Hit(v.Box,null,null,v.Pose)==null),"118: the high stem hook fits the actual bear and its backing without penetration");
            foreach(var v in figure.Volumes) {
                var group=mesh.Hit(v.Box,null,null,v.Pose);
                if(group!=null) Console.WriteLine("118 contact "+group+" face "+mesh.LastFace+" bounds "+string.Join(',',v.Bounds)+" quad "+string.Join(',',rigFile.Groups[group][mesh.LastFace]));
            }

            var plank=new ShipPiece { Sel=new List<double[]> {new double[]{-15,2.125,-1.5,-14,3.125,-.5}} };
            var eye=new Vec3d(-12.838,2.822,1.853);var dir=new Vec3d(-.5459,-.2209,-.8082).Normalize();
            bool met=AimPlace.PieceFace(plank,eye,dir,5,out double distance,out var normal);
            var hit=eye.AddCopy(dir.X*distance,dir.Y*distance,dir.Z*distance);
            Check(met && normal.SequenceEqual(new double[]{0,0,1}) && Math.Abs(hit.Z+.5)<1e-6,
                "123: the slanting cabin ray snaps from the plank's actual side at z=-0.5, not its voxel halo or the view direction");
            var beside=AimPlace.Candidates(def.Mounts,new ShipPieceSet(),hit,normal,plank,m=>m.Kind==MountKind.Floor).FirstOrDefault();
            Check(beside?.I==-15 && Math.Abs(beside.Z+.5)<1e-6 && beside.Level==1,"123: the offered block cell begins at the aimed face, z=-0.5, at its own level ("+beside+")");

            int selected=0;
            foreach(var gun in def.Fixtures.Where(f=>f.Type=="gun" && !f.Swivel))
                foreach(float train in new float[]{-15,0,15}) foreach(float elevation in new float[]{-8,0,12}) foreach(double back in new[]{0,.75})
                {
                    var lay=new GunLay(train,elevation);
                    var barrel=GunLaying.Place(gun,lay,back,new Vec3d(gun.Trunnion[0],gun.Trunnion[1],gun.Trunnion[2]),true);
                    var o=barrel.AddCopy(0,1,0);var d=new Vec3d(0,-1,0);
                    if(GunLaying.Pick(gun,lay,back,o,d)>=0) selected++;
                    var look=GunLaying.Dir(gun,lay);var aimed=GunLaying.FromLocalDir(gun,look.X,look.Y,look.Z);
                    Check(Math.Abs(aimed.Train-lay.Train)<1e-4 && Math.Abs(aimed.Elevation-lay.Elevation)<1e-4,"132: view and gun train/elevation agree ("+gun.Code+", "+lay+")");
                }
            Check(selected==def.Fixtures.Count(f=>f.Type=="gun"&&!f.Swivel)*18,"132: the full barrel remains selectable at every tested train, elevation and run-back");
            Check(GunLoading.Problem(0,false,false,GunLoading.Powder,1,1)=="hansa:gun-load-run-in" &&
                  GunLoading.Problem(0,true,true,GunLoading.Powder,1,1)=="hansa:gun-load-run-in" &&
                  GunLoading.Problem(0,true,false,GunLoading.Shot,4,4)=="hansa:gun-load-powder" &&
                  GunLoading.Problem(0,true,false,GunLoading.Powder,1,1)==null &&
                  GunLoading.Problem(1,true,false,GunLoading.Shot,3,4)=="hansa:gun-load-more" &&
                  GunLoading.Problem(1,true,false,GunLoading.Shot,4,4)==null &&
                  GunLoading.Problem(3,true,false,GunLoading.Shot,4,4)=="hansa:gun-load-full",
                  "132: load only after run-in stops, powder before shot, enough shot, no double charge");

            string outDir=Path.Combine(root,"tmp/runtime-meshes");Directory.CreateDirectory(outDir);
            var stairFoot=new MountPoint {X=0,Y=0,Z=-.5};
            var stairPiece=new ShipPiece {Id=1,Role=PieceRole.Stair,Dir=0,Rise=3,Cells=1,Rails=true,
                Parts=new List<PiecePart>{new PiecePart {Origin=new double[]{0,0,-.5}}}};
            var set=new ShipPieceSet();set.Add(stairPiece);
            var post=ModularGeometry.FlightHandrails(stairFoot,0,3,1).First(p=>p.Corners!=null).Corners;
            var centre=new[]{post.Average(p=>p[0]),post.Average(p=>p[1]),post.Average(p=>p[2])};
            var onPost=new PiecePlan();onPost.Sel.Add(new[]{centre[0]-.03,centre[1]-.03,centre[2]-.03,centre[0]+.03,centre[1]+.03,centre[2]+.03});
            var below=new PiecePlan();below.Sel.Add(new double[]{.3,0,-.1,.5,.3,.1});
            Check(set.Blocking(onPost)==stairPiece && set.Blocking(below)==null,
                "115/117: a block cannot penetrate a stair's slanted post, while clear space under the flight remains usable");
            foreach(int length in new[]{1,2,3}) foreach(int up in new[]{0,1,2,3}) foreach(bool rails in new[]{false,true})
            {
                var m=new MountPoint {X=0,Y=0,Z=-.5};
                var q=ModularMesh.Quads(ModularGeometry.Flight(m,up,3,length,rails));
                Check(q.Count>0 && q.All(face=>Enumerable.Range(0,4).All(k=>face[4+8*k]>=-.001 && face[4+8*k]<=16.001 && face[5+8*k]>=-.001 && face[5+8*k]<=16.001)),
                    $"115: {length}-cell stair, direction {up}, rails {rails}: exposed joints tiled at physical scale");
                File.WriteAllText(Path.Combine(outDir,$"stair-{length}-{up}-{rails}.json"),JsonConvert.SerializeObject(new{textures=new[]{"game:block/wood/debarked/oak","game:block/wood/planks/oak1"},groups=new Dictionary<string,List<float[]>>{{"exterior",q}}}));
            }
            var defaultFoot=def.Mounts.Floors.First(m=>m.Deck=="main" && m.X==-7 && m.Z==1.5);
            var actual=ModularMesh.Quads(ModularGeometry.Flight(defaultFoot,2,3,1,true));
            File.WriteAllText(Path.Combine(outDir,"note115.json"),JsonConvert.SerializeObject(new{textures=new[]{"game:block/wood/debarked/oak","game:block/wood/planks/oak1"},groups=new Dictionary<string,List<float[]>>{{"exterior",actual}}}));
            File.WriteAllText(Path.Combine(outDir,"figurehead.json"),JsonConvert.SerializeObject(new{hook=new[]{hook.X,hook.Y,hook.Z},origin=figure.Parts[0].Origin,quarter=figure.Parts[0].Quarter,tilt}));
            File.WriteAllText(Path.Combine(outDir,"rig-poses.json"),JsonConvert.SerializeObject(new{textures=rigFile.Textures,groups=rigFile.Groups.Where(g=>g.Key.StartsWith("rig:shroud:")).ToDictionary(g=>g.Key,g=>g.Value),poses=rigPose,lantern=lantern.Parts[0].Origin}));
            Check(SwivelGun.MuzzleReach>.95 && SwivelGun.TailReach>.6 && SwivelGun.Train.SequenceEqual(new float[]{-100,100}) && SwivelGun.Elevate.SequenceEqual(new float[]{-25,40}),
                "133: a larger berco, with 200 degrees of train and -25..40 elevation (rail clearance still applies)");
        }
    }
}
