using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace Hansa
{
    public partial class EntityShip
    {
        PlacementMesh VisibleStructure
        {
            get
            {
                if(Def.Placement!=null) return Def.Placement;
                var file=Def.SkinFile;
                if(file==null && Def.Shapes?.Skin!=null)
                    file=World.Api.Assets.Get(AssetLocation.Create(Def.Shapes.Skin,"hansa").WithPathAppendixOnce(".json")).ToObject<ShipSkinFile>();
                return Def.Placement=new PlacementMesh(PlacementMesh.Prepare(Def,file));
            }
        }

        Dictionary<string,float[]> PlacementPoses()
        {
            var mats=new Dictionary<string,float[]>();
            foreach(var part in Def.MovingParts)
            {
                if(part.Kind=="follow") continue;
                string source=part.Source??"rudder";
                float v=source=="rudder"?Rudder:source=="brace"?(float)Brace:source=="sheet"?(float)Sheet:source=="anchor"?(Anchored?1:0):0;
                if(source.StartsWith("open:")) v=OpenFittings.Contains(source.Substring(5))?1:0;
                if(source.StartsWith("train:")) v=GunLayNow(source.Substring(6)).Train;
                if(source.StartsWith("elevate:")) v=GunLayNow(source.Substring(8)).Elevation;
                if(source.StartsWith("fire:"))
                {
                    var f=Def.Fixture(source.Substring(5));
                    v=Api.Side==EnumAppSide.Client?GunRecoil(f.Code):!GunOpen(f)||GunRunIn(f)?1:0;
                }
                var m=new float[16];
                PartMotion.Place(part,v,m,mats);
                mats[part.Code]=m;
            }
            PlacementMesh.RigPoses(Def,mats,LeeShown,LateenSheet,SailFraction);
            return mats;
        }

        bool PlacementFaceVisible(string group,int index,ISet<int> takingBeams=null)
        {
            if(VisibleStructure.SlotRefs!=null && VisibleStructure.SlotRefs.TryGetValue(group,out var refs) && index*2+1<refs.Length)
            {
                var st=group.StartsWith("slot:hull")?HullPanels:Slots;
                int owner=refs[index*2],facing=refs[index*2+1];
                if(!group.StartsWith("slot:hull") && takingBeams!=null && owner>=0 && takingBeams.Contains(owner)) return false;
                if(st!=null && (!(owner<0 || st.Filled(owner)) || !(facing<0 || !st.Filled(facing)))) return false;
            }
            if(group.StartsWith("part:") && Def.GunOfPart.TryGetValue(group.Substring(5),out var gun) && GunAbsent(Def.Fixture(gun))) return false;
            return true;
        }

        /// <summary>Thin fittings and ropes count as obstructions, even when they have no walking voxels.</summary>
        string VisiblePlacementProblem(PiecePlan plan)
        {
            var hash=new HashCode();hash.Add(FitVersion);hash.Add(Rudder);hash.Add(Brace);hash.Add(Sheet);hash.Add(Anchored);
            hash.Add(LeeShown);hash.Add(LateenSheet);hash.Add(SailFraction);
            foreach(string code in OpenFittings) hash.Add(code);
            foreach(var f in Def.Fixtures) if(f.Type=="gun") { var lay=GunLayNow(f);hash.Add(lay.Train);hash.Add(lay.Elevation);hash.Add(GunBack(f)); }
            int stamp=hash.ToHashCode();
            if(ReferenceEquals(plan,visiblePlan) && stamp==visibleStamp) return visibleProblem;
            visibleProblem=VisiblePlacementProblemNow(plan);visiblePlan=plan;visibleStamp=stamp;
            return visibleProblem;
        }
        PiecePlan visiblePlan;
        int visibleStamp;
        string visibleProblem;

        string VisiblePlacementProblemNow(PiecePlan plan)
        {
            LastPlacementObstacle=null;
            var structure=VisibleStructure;
            var boxes=plan.Role==PieceRole.Stair?plan.Sel.Take(Math.Max(0,plan.Sel.Count-1)):plan.Sel;
            var poses=PlacementPoses();
            var taking=BeamsInTheWay(plan)?.ToHashSet();
            var piecePose=PieceShape.Tilt(plan.Pivot,plan.Tilt,plan.Dir);
            var volumes=plan.Volumes.Count>0 ? plan.Volumes : boxes.Select(b=>new PieceVolume { Box=b,Pose=piecePose }).ToList();
            foreach(var volume in volumes)
            {
                var b=(double[])volume.Box.Clone();
                for(int axis=0;axis<3;axis++) if(b[axis+3]-b[axis]<.006) { b[axis]-=.004;b[axis+3]+=.004; }
                var worldBox=volume.Pose==null?b:PlacementMesh.Bounds(PlacementMesh.Corners(b).Select(p=>PlacementMesh.Transform(volume.Pose,p)));
                // The lantern's hanging ring meets the hook's drop. The body below that small attachment is checked.
                if(plan.Kind==MountKind.Hook && plan.Hang=="down" && worldBox[1]>plan.Mount.Y-.08) continue;
                foreach(var f in Def.Fixtures.Concat(PlacedFixtures))
                {
                    if(f.Type=="piece" || f.Code?.StartsWith("piece-")==true || GunAbsent(f)) continue;
                    var fb=f.Type=="attach" ? Attached.TryGetValue(f.Code??"",out var stack)?ShipFittings.AttachBox(f,stack,Def):null : f.Type=="gun"?GunSelectionBounds(f):f.Box??f.Sel;
                    if(fb!=null && PiecePlanner.Overlap(worldBox,fb,.003)) {
                        LastPlacementObstacle=f.Code+" at "+string.Join(",",worldBox);
                        return "hansa:piece-blocked";
                    }
                }
                // Border mounts already cut partitions to the fixed interior timbers (PanelPiece/DrawnClip).
                // Those joints are fitted surfaces, while ropes, movable parts and fixtures remain obstacles.
                bool Visible(string group,int index) => !(plan.Role==PieceRole.Partition && group=="interior") && PlacementFaceVisible(group,index,taking);
                var obstacle=structure.Hit(b,Visible,g=>poses.TryGetValue(g.StartsWith("part:")?g.Substring(5):g,out var m)?m:null,volume.Pose);
                if(obstacle!=null)
                {
                    LastPlacementObstacle=obstacle+" at "+string.Join(",",worldBox);
                    return "hansa:piece-blocked";
                }
            }
            return null;
        }
        public string LastPlacementObstacle;
        public bool VisibleObstructed(PiecePlan plan) => plan!=null && VisiblePlacementProblem(plan)!=null;
    }
}
