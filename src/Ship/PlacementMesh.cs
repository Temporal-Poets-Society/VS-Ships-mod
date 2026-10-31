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
            public int Seen;
        }
        sealed class Group
        {
            public readonly Dictionary<(int, int, int), List<Face>> Cells = new();
        }
        readonly Dictionary<string, Group> groups = new();
        public readonly Dictionary<string,int[]> SlotRefs;
        int queryId;
        public int LastFace { get; private set; }
        readonly double[] insetBox = new double[6], worldBounds = new double[6], localBounds = new double[6];
        readonly float[] boxInverse = new float[16], groupInverse = new float[16];
        static int Cell(double n) => (int)Math.Floor(n);

        public static ShipSkinFile Prepare(ShipDefinition def,ShipSkinFile mesh)
        {
            if(mesh?.Groups==null || mesh.Groups.Keys.Any(k=>k.StartsWith("rig:shroud:"))) return mesh;
            var copy=new ShipSkinFile { Textures=mesh.Textures,SlotQuads=mesh.SlotQuads,
                Groups=mesh.Groups.ToDictionary(k=>k.Key,k=>k.Value.Select(q=>(float[])q.Clone()).ToArray()) };
            LateenShrouds.Split(def,copy);
            return copy;
        }

        public sealed class RigPoseCache
        {
            public readonly Dictionary<string,(LateenRig Rig,LateenPose Pose,float[] Frame)> Lateens=new();
            internal readonly Dictionary<string,float[]> Frames=new();
            internal readonly float[] Furl=new float[16],Follow=new float[16],Mirror=new float[16];
        }

        /// <summary>Shared by drawing and placement: yards, follower ropes and cast-off shrouds from one trim snapshot.</summary>
        public static void RigPoses(ShipDefinition def,Dictionary<string,float[]> mats,float lee,double sheet,float fraction,RigPoseCache cache=null)
        {
            cache??=new RigPoseCache();var lateens=cache.Lateens;lateens.Clear();
            float[] Matrix(string key) {
                if(!mats.TryGetValue(key,out var m)) mats[key]=m=new float[16];
                return m;
            }
            foreach(var rig in LateenRig.Of(def)) {
                if(rig==null) continue;
                var pose=rig.Pose(lee,sheet,PartMotion.DrawnSail(fraction));
                if(!cache.Frames.TryGetValue(rig.Yard.Code,out var frame)) cache.Frames[rig.Yard.Code]=frame=new float[16];
                rig.YardFrame(pose,frame);
                rig.Drawn(pose,frame,Matrix(rig.Yard.Code));
                lateens[rig.Yard.Code]=(rig,pose,frame);
            }
            foreach(var part in def.MovingParts) {
                if(part.Kind!="follow") continue;
                mats.TryGetValue(part.Follow??"",out var follow);
                bool onLateen=lateens.TryGetValue(part.Follow??"",out var lt);
                if(onLateen) follow=lt.Frame;
                var sail=Array.Find(def.Sails,s=>s.Code==part.Sail);
                if(follow!=null && sail!=null) {
                    PartMotion.FurlMatrix(sail,onLateen?lt.Pose.Frac:PartMotion.DrawnSail(fraction),cache.Furl);
                    Mat4f.Mul(cache.Follow,follow,cache.Furl);follow=cache.Follow;
                }
                mats.TryGetValue(part.AnchorFollow??"",out var anchor);
                var m=Matrix(part.Code);PartMotion.FollowMatrix(part,follow,m,anchor);
                if(onLateen && lt.Pose.Mirror) { Array.Copy(m,cache.Mirror,16);lt.Rig.Drawn(lt.Pose,cache.Mirror,m); }
            }
            foreach(var shroud in LateenShrouds.Of(def)) {
                var m=Matrix(shroud.Group);
                if(LateenShrouds.SetUp(def,shroud,lee)) Mat4f.Identity(m);
                else LateenShrouds.HangMatrix(shroud,m);
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
        public string Hit(double[] box, Func<string,int,bool> visible = null, Func<string,float[]> pose = null, float[] boxPose = null,
            Func<string,float[]> inversePose = null, double[] contact = null, double[] footContact = null)
        {
            const double inset = 0.003;
            var b = insetBox;
            Array.Copy(box,b,6);
            for (int a=0;a<3;a++) { b[a]+=inset; b[a+3]-=inset; if (b[a]>=b[a+3]) return null; }
            float[] inverseBox=null;
            var worldQuery=b;
            if(boxPose!=null) {
                inverseBox=boxInverse;Mat4f.Invert(inverseBox,boxPose);
                TransformBounds(b,boxPose,worldBounds);worldQuery=worldBounds;
            }
            foreach(var g in groups)
            {
                var m = pose?.Invoke(g.Key);
                var query=worldQuery;
                if(m != null)
                {
                    var inv=inversePose?.Invoke(g.Key);
                    if(inv==null) { inv=groupInverse;if(Mat4f.Invert(inv,m)==null) continue; }
                    TransformBounds(worldQuery,inv,localBounds);query=localBounds;
                }
                int seen=++queryId;
                for(int x=Cell(query[0]);x<=Cell(query[3]);x++)
                    for(int y=Cell(query[1]);y<=Cell(query[4]);y++)
                        for(int z=Cell(query[2]);z<=Cell(query[5]);z++)
                        {
                            if(!g.Value.Cells.TryGetValue((x,y,z),out var list)) continue;
                            foreach(var f in list)
                            {
                                if(f.Seen==seen) continue;f.Seen=seen;
                                if(visible != null && !visible(f.Group,f.Index)) continue;
                                var a=PointOf(f.Points[0],m,inverseBox);var c=PointOf(f.Points[2],m,inverseBox);
                                bool deckContact=!g.Key.StartsWith("part:") && !g.Key.StartsWith("rig:") && !g.Key.StartsWith("slot:hull");
                                if(Intersects(a,PointOf(f.Points[1],m,inverseBox),c,b,deckContact?contact:null,deckContact?footContact:null,boxPose) ||
                                   Intersects(a,c,PointOf(f.Points[3],m,inverseBox),b,deckContact?contact:null,deckContact?footContact:null,boxPose)) { LastFace=f.Index;return g.Key; }
                            }
                        }
            }
            return null;
        }

        public static void TransformBounds(double[] b,float[] m,double[] result)
        {
            for(int a=0;a<3;a++) { result[a]=double.MaxValue;result[a+3]=double.MinValue; }
            for(int k=0;k<8;k++) {
                var p=new Point(b[(k&1)==0?0:3],b[(k&2)==0?1:4],b[(k&4)==0?2:5]).Transform(m);
                for(int a=0;a<3;a++) { result[a]=Math.Min(result[a],p[a]);result[a+3]=Math.Max(result[a+3],p[a]); }
            }
        }
        public static IEnumerable<double[]> Corners(double[] b)
        {
            for(int k=0;k<8;k++) yield return new[] { b[(k&1)==0?0:3],b[(k&2)==0?1:4],b[(k&4)==0?2:5] };
        }
        public static double[] Transform(float[] m,double[] p) => new double[] {
            m[0]*p[0]+m[4]*p[1]+m[8]*p[2]+m[12],m[1]*p[0]+m[5]*p[1]+m[9]*p[2]+m[13],m[2]*p[0]+m[6]*p[1]+m[10]*p[2]+m[14] };
        readonly struct Point
        {
            public readonly double X,Y,Z;
            public Point(double x,double y,double z) { X=x;Y=y;Z=z; }
            public double this[int a] => a==0?X:a==1?Y:Z;
            public Point Transform(float[] m) => m==null?this:new Point(m[0]*X+m[4]*Y+m[8]*Z+m[12],m[1]*X+m[5]*Y+m[9]*Z+m[13],m[2]*X+m[6]*Y+m[10]*Z+m[14]);
        }
        static Point PointOf(double[] p,float[] m,float[] inv) => new Point(p[0],p[1],p[2]).Transform(m).Transform(inv);
        static bool Intersects(Point a,Point b,Point c,double[] box,double[] contact,double[] footContact,float[] boxPose)
        {
            Span<Point> polygon=stackalloc Point[12];Span<Point> next=stackalloc Point[12];
            polygon[0]=a;polygon[1]=b;polygon[2]=c;int count=3;
            for(int axis=0;axis<3;axis++) for(int side=0;side<2;side++)
            {
                double sign=side==0?1:-1, plane=box[axis+(side==0?0:3)];
                int n=0;
                for(int i=0;i<count;i++)
                {
                    var p=polygon[i];var q=polygon[(i+1)%count];
                    double dp=sign*(p[axis]-plane),dq=sign*(q[axis]-plane);
                    if(dp>=0) next[n++]=p;
                    if((dp>=0)!=(dq>=0)) { double t=dp/(dp-dq);next[n++]=new Point(p.X+t*(q.X-p.X),p.Y+t*(q.Y-p.Y),p.Z+t*(q.Z-p.Z)); }
                }
                var swap=polygon;polygon=next;next=swap;count=n;
                if(count<3) return false;
            }
            for(int which=0;which<2;which++) {
                var region=which==0?contact:footContact;if(region==null) continue;
                bool inside=true;
                for(int i=0;i<count;i++) for(int axis=0;axis<3;axis++)
                    if(polygon[i].Transform(boxPose)[axis]<region[axis]-.003 || polygon[i].Transform(boxPose)[axis]>region[axis+3]+.003) inside=false;
                if(inside) return false;
            }
            return true;
        }
    }
}
