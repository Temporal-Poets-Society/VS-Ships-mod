using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace HansaTests
{
    static partial class Program
    {
        static void IntegrationNotes(string root)
        {
            var (def,file)=LoadHullDesign(root,"caravel");
            var set=new ShipPieceSet();var mesh=new PlacementMesh(PlacementMesh.Prepare(def,file));
            var poses=new Dictionary<string,float[]>();PlacementMesh.RigPoses(def,poses,-1,0,0);
            bool Visible(string group,int index) {
                if(file.SlotQuads==null || !file.SlotQuads.TryGetValue(group,out var refs)) return true;
                int owner=refs[index*2],facing=refs[index*2+1];
                var slots=group.StartsWith("slot:hull")?def.HullSlots.Select(s=>s.Start).ToArray():def.DeckSlots.Select(s=>s.Start).ToArray();
                return (owner<0 || slots[owner]!="empty") && (facing<0 || slots[facing]=="empty");
            }
            foreach(var entry in def.ModularPieces.OrderBy(e=>e.Kind=="stair"?0:e.Kind=="hearth"?1:2))
            {
                if(entry.Kind=="hatch" || entry.Kind=="oven") continue;
                var m=def.Mounts.ForModular(entry);string kind=def.Mounts.LaidKind(entry);
                var plan=m==null?null:PiecePlanner.Plan(def.Mounts,m,ModularFake(entry,kind),ModularToward(entry,kind,m));
                var block=plan==null?null:set.Blocking(plan);
                Check(plan!=null && plan.Error==null && block==null,$"merged default {entry.Key}: {m?.Id}, {plan?.Error}, blocker {block?.Mount}");
                if(plan==null || plan.Error!=null) continue;
                if(plan.Role==PieceRole.Stair) {
                    double run=plan.Cells;var contact=PiecePlanner.StairBox(m,plan.Dir,run-.25,plan.Rise-.25,-.5,run,plan.Rise,.5);
                    var footContact=PiecePlanner.StairBox(m,plan.Dir,-.3,-.25,-.5,.5,0,.5);
                    foreach(var v in plan.PlacementVolumes) {
                        var hit=mesh.Hit(v.Box,Visible,g=>poses.TryGetValue(g.StartsWith("part:")?g.Substring(5):g,out var pose)?pose:null,v.Pose,null,contact,footContact);
                        Check(hit==null,$"merged stair {entry.Key}: timber {string.Join(',',v.Bounds.Select(n=>n.ToString("0.###")))} clear ({hit})");
                        if(hit!=null) Console.WriteLine("  face "+mesh.LastFace+": "+string.Join(',',file.Groups[hit][mesh.LastFace]));
                    }
                }
                set.Add(ShipPiece.From(plan,set.NextId,null));
            }
            string game=Environment.GetEnvironmentVariable("VINTAGE_STORY")??"C:/Users/andri/AppData/Roaming/Vintagestory";
            var nativeContacts=new List<ModularPart>();
            void Native(string path,double[] origin,double rotation=0) {
                var shape=JsonConvert.DeserializeObject<Shape>(File.ReadAllText(Path.Combine(game,"assets/survival/shapes",path+".json")));
                nativeContacts.AddRange(PieceShape.PlacedVolumes(shape,origin,0,ry:rotation).Select(v=>new ModularPart {Corners=v.Corners}));
            }
            foreach(var entry in def.StartPieces.Where(p=>p.Block=="game:table-normal")) {
                var m=def.Mounts.Get(entry.Mount);Native("block/wood/table",new[]{m.X,m.Y,m.Z});
            }
            var oven=def.ModularPieces.Single(p=>p.Kind=="oven");
            Native("block/clay/oven",new[]{oven.At[0]-.5,oven.At[1],oven.At[2]-.5},270);
            var quads=new List<float[]>();
            foreach(var p in set.All) {
                var parts=ModularGeometry.ContactParts(def.Mounts,def.Surfaces,set,p);
                if(parts.Count==0) continue;
                var contacts=ModularGeometry.Contacts(def.Mounts,def.Surfaces,set,p);
                if(p.Role==PieceRole.Partition) contacts=contacts.Concat(nativeContacts);
                quads.AddRange(ModularMesh.Quads(parts,contacts,p.Role!=PieceRole.Partition));
            }
            Directory.CreateDirectory(Path.Combine(root,"tmp/integrate-views"));
            File.WriteAllText(Path.Combine(root,"tmp/integrate-views/modular.json"),JsonConvert.SerializeObject(new {
                textures=new[]{"game:block/wood/debarked/oak","game:block/wood/planks/oak1","hansa:block/glass-clear","game:block/metal/plate/lead","game:block/metal/plate/iron","game:block/clay/brick/four/running/red1","game:block/coal/bituminous"},
                groups=new Dictionary<string,List<float[]>>{{"runtime-modular",quads}}}));

            var states=new SlotStates(def.Slots);
            Check(OpeningFrames.Build(def,states).Count==0,"new ships add no duplicate opening frames");
            foreach(var scenario in new[]{new[]{"beam:main:0:-1"},new[]{"beam:main:0:-1","beam:main:2:-1"},new[]{"beam:main:0:-1","beam:main:0:0"},new[]{"beam:main:-8:-2"},new[]{"beam:main:-4:-1"}}) {
                states=new SlotStates(def.Slots);var empty=new List<string>();
                foreach(var id in scenario) {
                    int n=def.Slots.IndexOf(id);states.Set(n,SlotState.Empty);empty.Add(id);
                    foreach(string cover in def.DeckSlots[n].Beside) {states.Set(def.Slots.IndexOf(cover),SlotState.Empty);empty.Add(cover);}
                }
                var frame=OpeningFrames.Build(def,states);var q=OpeningFrames.Mesh(def,frame);
                Check(frame.Count>0 && frame.All(p=>Math.Abs(p.Box[4]-.925)<.0001),"112/114: shared carlings after "+string.Join(',',scenario));
                if(scenario.Length==2 && scenario[1].EndsWith(":0")) Check(frame.All(p=>p.Box[5]<=-.5+.001 || p.Box[2]>=1.5-.001),"no divider between adjacent removed lengths of one beam");
                string name="frame-"+string.Join('-',scenario.Select(s=>s.Replace(':','_')));
                File.WriteAllText(Path.Combine(root,"tmp/integrate-views/"+name+".json"),JsonConvert.SerializeObject(new {textures=new[]{"game:block/wood/debarked/oak"},groups=new Dictionary<string,List<float[]>>{{"runtime-frame",q}}}));
                File.WriteAllText(Path.Combine(root,"tmp/integrate-views/"+name+"-state.json"),JsonConvert.SerializeObject(new {emptyDeck=empty}));
            }
            foreach(string id in new[]{"beam:main:-8:-2","beam:main:-4:-1"}) {
                int n=def.Slots.IndexOf(id);var original=new SlotStates(def.Slots);var g=def.Grid.Clone();
                original.Set(n,SlotState.Empty);original.Apply(g);var b=def.DeckSlots[n].Box;
                Check(!g.SolidAt((b[0]+b[3])/2,(b[1]+b[4])/2,(b[2]+b[5])/2),"112/114: the marked beam length owns removable collision: "+id);
                original.Set(n,SlotState.Filled);g=def.Grid.Clone();original.Apply(g);
                Check(g.SolidAt((b[0]+b[3])/2,(b[1]+b[4])/2,(b[2]+b[5])/2),"112/114: the marked beam length can be put back: "+id);
            }
            var deck=ModularMesh.Quads(new[]{ModularPart.B(PartMat.Planks,-1,3.8,-1,1,4,1)}).ToArray();
            var flueContact=new double[]{-.25,3.75,-.25,.25,4,.25};var flue=new double[]{-.25,1,-.25,.25,5,.25};
            var flueMesh=new PlacementMesh(new ShipSkinFile {Groups=new Dictionary<string,float[][]>{{"interior",deck}}});
            Check(flueMesh.Hit(flue)!=null && flueMesh.Hit(flue,contact:flueContact)==null,"the designated flue penetration allows intact deck planking");
            var rigMesh=new PlacementMesh(new ShipSkinFile {Groups=new Dictionary<string,float[][]>{{"rig:test",deck}}});
            Check(rigMesh.Hit(flue,contact:flueContact)!=null,"a rig obstruction in the flue's deck penetration is still refused");
            var beam=ModularMesh.Quads(new[]{ModularPart.B(PartMat.Wood,-1,3.4,-1,1,3.8,1)}).ToArray();
            var beamMesh=new PlacementMesh(new ShipSkinFile {Groups=new Dictionary<string,float[][]>{{"interior",beam}}});
            Check(beamMesh.Hit(flue,contact:flueContact)!=null,"the flue penetration does not exempt a transverse beam");
        }
    }
}
