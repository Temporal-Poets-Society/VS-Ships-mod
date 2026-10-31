using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>Visible ship surfaces used by placement, including thin ropes and fittings outside the walking grid.
    /// Indexed once per design; slot visibility and moving part poses are supplied by the ship at query time.</summary>
    public sealed class PlacementMesh
    {
        sealed class Face
        {
            public string Group;
            public int Index;
            public double[][] Points;
        }
        sealed class Group
        {
            public readonly Dictionary<(int, int, int), List<Face>> Cells = new();
        }
        readonly Dictionary<string, Group> groups = new();
        public readonly Dictionary<string,int[]> SlotRefs;
        static int Cell(double n) => (int)Math.Floor(n);

        public static ShipSkinFile Prepare(ShipDefinition def,ShipSkinFile mesh)
        {
            if(mesh?.Groups==null || mesh.Groups.Keys.Any(k=>k.StartsWith("rig:shroud:"))) return mesh;
            var copy=new ShipSkinFile { Textures=mesh.Textures,SlotQuads=mesh.SlotQuads,
                Groups=mesh.Groups.ToDictionary(k=>k.Key,k=>k.Value.Select(q=>(float[])q.Clone()).ToArray()) };
            LateenShrouds.Split(def,copy);
            return copy;
        }

        /// <summary>The same yard, follower and cast-off shroud transforms used for drawing the rig.</summary>
        public static void RigPoses(ShipDefinition def,Dictionary<string,float[]> mats,float lee,double sheet,float fraction)
        {
            var lateens=new Dictionary<string,(LateenRig Rig,LateenPose Pose,float[] Frame)>();
            foreach(var rig in LateenRig.Of(def)) {
                if(rig==null) continue;
                var pose=rig.Pose(lee,sheet,PartMotion.DrawnSail(fraction));
                var frame=new float[16];rig.YardFrame(pose,frame);
                var drawn=new float[16];rig.Drawn(pose,frame,drawn);mats[rig.Yard.Code]=drawn;
                lateens[rig.Yard.Code]=(rig,pose,frame);
            }
            foreach(var part in def.MovingParts) {
                if(part.Kind!="follow") continue;
                mats.TryGetValue(part.Follow??"",out var follow);
                bool onLateen=lateens.TryGetValue(part.Follow??"",out var lt);
                if(onLateen) follow=lt.Frame;
                var sail=Array.Find(def.Sails,s=>s.Code==part.Sail);
                if(follow!=null && sail!=null) {
                    var furl=new float[16];PartMotion.FurlMatrix(sail,onLateen?lt.Pose.Frac:PartMotion.DrawnSail(fraction),furl);
                    var move=new float[16];Mat4f.Mul(move,follow,furl);follow=move;
                }
                mats.TryGetValue(part.AnchorFollow??"",out var anchor);
                var m=new float[16];PartMotion.FollowMatrix(part,follow,m,anchor);
                if(onLateen && lt.Pose.Mirror) { var own=(float[])m.Clone();lt.Rig.Drawn(lt.Pose,own,m); }
                mats[part.Code]=m;
            }
            foreach(var shroud in LateenShrouds.Of(def)) if(!LateenShrouds.SetUp(def,shroud,lee)) {
                var m=new float[16];LateenShrouds.HangMatrix(shroud,m);mats[shroud.Group]=m;
            }
        }

        public PlacementMesh(ShipSkinFile mesh)
        {
            SlotRefs=mesh?.SlotQuads;
            foreach (var g in mesh?.Groups ?? new Dictionary<string, float[][]>())
            {
                if (g.Key == "shot" || g.Key.Contains("bottomclad") || g.Key.StartsWith("sail:") || g.Key.StartsWith(LateenShrouds.ExportPrefix)) continue;
                var group = groups[g.Key] = new Group();
                for (int i = 0; i < g.Value.Length; i++)
                {
                    var q = g.Value[i];
                    if (q == null || q.Length < 33) continue;
                    var points = Enumerable.Range(0, 4).Select(k => new double[] { q[1 + 8*k], q[2 + 8*k], q[3 + 8*k] }).ToArray();
                    var ab=points[1].Zip(points[0],(a,b)=>a-b).ToArray();var ac=points[2].Zip(points[0],(a,b)=>a-b).ToArray();
                    if(Math.Pow(ab[1]*ac[2]-ab[2]*ac[1],2)+Math.Pow(ab[2]*ac[0]-ab[0]*ac[2],2)+Math.Pow(ab[0]*ac[1]-ab[1]*ac[0],2)<1e-16) continue;
                    var f = new Face { Group = g.Key, Index = i, Points = points };
                    var b = Bounds(points);
                    for (int x = Cell(b[0]); x <= Cell(b[3]); x++)
                        for (int y = Cell(b[1]); y <= Cell(b[4]); y++)
                            for (int z = Cell(b[2]); z <= Cell(b[5]); z++)
                            {
                                if (!group.Cells.TryGetValue((x,y,z), out var list)) group.Cells[(x,y,z)] = list = new();
                                list.Add(f);
                            }
                }
            }
        }

        public static double[] Bounds(IEnumerable<double[]> points) => new[] {
            points.Min(p=>p[0]), points.Min(p=>p[1]), points.Min(p=>p[2]),
            points.Max(p=>p[0]), points.Max(p=>p[1]), points.Max(p=>p[2]) };

        /// <summary>The first drawn group penetrating the box (touching its outside is allowed), or null.</summary>
        public string Hit(double[] box, Func<string,int,bool> visible = null, Func<string,float[]> pose = null, float[] boxPose = null)
        {
            const double inset = 0.003;
            var b = (double[])box.Clone();
            for (int a=0;a<3;a++) { b[a]+=inset; b[a+3]-=inset; if (b[a]>=b[a+3]) return null; }
            float[] inverseBox=null;
            var worldQuery=b;
            if(boxPose!=null) {
                inverseBox=new float[16];Vintagestory.API.MathTools.Mat4f.Invert(inverseBox,boxPose);
                worldQuery=Bounds(Corners(b).Select(p=>Transform(boxPose,p)));
            }
            foreach(var g in groups)
            {
                var m = pose?.Invoke(g.Key);
                var query=worldQuery;
                if(m != null)
                {
                    var inv=new float[16];
                    if(Vintagestory.API.MathTools.Mat4f.Invert(inv,m)==null) continue;
                    query=Bounds(Corners(worldQuery).Select(p=>Transform(inv,p)));
                }
                var seen=new HashSet<Face>();
                for(int x=Cell(query[0]);x<=Cell(query[3]);x++)
                    for(int y=Cell(query[1]);y<=Cell(query[4]);y++)
                        for(int z=Cell(query[2]);z<=Cell(query[5]);z++)
                        {
                            if(!g.Value.Cells.TryGetValue((x,y,z),out var list)) continue;
                            foreach(var f in list)
                            {
                                if(!seen.Add(f) || visible != null && !visible(f.Group,f.Index)) continue;
                                var p=m==null?f.Points:f.Points.Select(v=>Transform(m,v)).ToArray();
                                if(inverseBox!=null) p=p.Select(v=>Transform(inverseBox,v)).ToArray();
                                if(Intersects(p[0],p[1],p[2],b) || Intersects(p[0],p[2],p[3],b)) return g.Key;
                            }
                        }
            }
            return null;
        }
        public static IEnumerable<double[]> Corners(double[] b)
        {
            for(int k=0;k<8;k++) yield return new[] { b[(k&1)==0?0:3],b[(k&2)==0?1:4],b[(k&4)==0?2:5] };
        }
        public static double[] Transform(float[] m,double[] p) => new double[] {
            m[0]*p[0]+m[4]*p[1]+m[8]*p[2]+m[12],m[1]*p[0]+m[5]*p[1]+m[9]*p[2]+m[13],m[2]*p[0]+m[6]*p[1]+m[10]*p[2]+m[14] };
        static bool Intersects(double[] a,double[] b,double[] c,double[] box)
        {
            var polygon=new List<double[]> { a,b,c };
            for(int axis=0;axis<3;axis++) for(int side=0;side<2;side++)
            {
                double sign=side==0?1:-1, plane=box[axis+(side==0?0:3)];
                var next=new List<double[]>();
                for(int i=0;i<polygon.Count;i++)
                {
                    var p=polygon[i];var q=polygon[(i+1)%polygon.Count];
                    double dp=sign*(p[axis]-plane),dq=sign*(q[axis]-plane);
                    if(dp>=0) next.Add(p);
                    if((dp>=0)!=(dq>=0)) { double t=dp/(dp-dq);next.Add(new[] { p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1]),p[2]+t*(q[2]-p[2]) }); }
                }
                polygon=next;
                if(polygon.Count<3) return false;
            }
            return true;
        }
    }
}
