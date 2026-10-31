using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    /// <summary>Exterior faces of joined stair timbers. Internal and duplicate faces are cut away before tiling
    /// at one texture per physical block; the renderer and geometry tests use these same quads.</summary>
    public static class ModularMesh
    {
        // Game matrices are floats; rotated native elements at the far end of a ship carry micrometre rounding.
        const double ContactTolerance=2e-6;
        sealed class Face
        {
            public double[][] P;
            public double[] N;
            public double D;
            public PartMat Mat;
        }
        static readonly int[][] indices={new[]{0,2,6,4},new[]{1,5,7,3},new[]{0,4,5,1},new[]{2,3,7,6},new[]{0,1,3,2},new[]{4,6,7,5}};
        static double Dot(double[] a,double[] b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
        static double[] Sub(double[] a,double[] b)=>new[]{a[0]-b[0],a[1]-b[1],a[2]-b[2]};
        static double[] Cross(double[] a,double[] b)=>new[]{a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};
        static double[] Unit(double[] p) { double n=Math.Sqrt(Dot(p,p));return p.Select(v=>v/Math.Max(n,1e-20)).ToArray(); }

        public static double[][] Corners(ModularPart part)
        {
            if(part.Corners!=null) return part.Corners;
            if(!part.IsBeam) return PlacementMesh.Corners(part.Box).ToArray();
            var c=new double[8][];
            for(int k=0;k<8;k++) {
                var p=(k&1)==0?part.P0:part.P1;
                double y=(k&2)==0?-part.H/2:part.H/2,w=(k&4)==0?-part.W/2:part.W/2;
                c[k]=part.AlongX?new[]{p[0],p[1]+y,p[2]+w}:new[]{p[0]+w,p[1]+y,p[2]};
            }
            return c;
        }
        static Face[] Faces(ModularPart part)
        {
            var c=Corners(part);var mid=new[]{c.Average(p=>p[0]),c.Average(p=>p[1]),c.Average(p=>p[2])};
            return indices.Select(ind=>{
                var p=ind.Select(i=>c[i]).ToArray();var n=Unit(Cross(Sub(p[1],p[0]),Sub(p[3],p[0])));
                if(Dot(n,Sub(p[0],mid))<0) { Array.Reverse(p);n=n.Select(v=>-v).ToArray(); }
                return new Face {P=p,N=n,D=Dot(n,p[0]),Mat=part.Mat};
            }).ToArray();
        }

        static List<double[]> Clip(List<double[]> p,Func<double[],double> distance)
        {
            var o=new List<double[]>();
            for(int k=0;k<p.Count;k++) {
                var a=p[k];var b=p[(k+1)%p.Count];double da=distance(a),db=distance(b);
                if(da>=-ContactTolerance) o.Add(a);
                if((da>ContactTolerance&&db< -ContactTolerance)||(da< -ContactTolerance&&db>ContactTolerance)) {
                    double t=da/(da-db);o.Add(new[]{a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1]),a[2]+t*(b[2]-a[2])});
                }
            }
            return o;
        }

        public static List<float[]> Quads(IEnumerable<ModularPart> parts,IEnumerable<ModularPart> contacts=null,bool keepShared=true)
        {
            var own=parts.ToArray();var all=own.Concat(contacts??Enumerable.Empty<ModularPart>()).ToArray();
            var solids=all.Select(Faces).ToArray();var boxes=all.Select(p=>PlacementMesh.Bounds(Corners(p))).ToArray();
            var result=new List<float[]>();
            for(int i=0;i<own.Length;i++) foreach(var face in solids[i])
            {
                var polygons=new List<List<double[]>> {face.P.ToList()};
                for(int j=0;j<solids.Length&&polygons.Count>0;j++)
                {
                    if(i==j || !PiecePlanner.Overlap(boxes[i],boxes[j],-1e-7)) continue;
                    // A shared outer plane has one owner. Opposing contact faces are internal on both sides.
                    if(j>i && (j<own.Length || keepShared) && solids[j].Any(f=>Dot(face.N,f.N)>1-1e-8 && Math.Abs(face.D-f.D)<ContactTolerance)) continue;
                    var outside=new List<List<double[]>>();
                    foreach(var polygon in polygons) {
                        var inside=polygon;
                        foreach(var plane in solids[j]) {
                            var outPart=Clip(inside,p=>Dot(plane.N,p)-plane.D);
                            if(outPart.Count>=3 && outPart.Any(p=>Dot(plane.N,p)-plane.D>ContactTolerance)) outside.Add(outPart);
                            inside=Clip(inside,p=>plane.D-Dot(plane.N,p));
                            if(inside.Count<3) break;
                        }
                    }
                    polygons=outside;
                }
                var u=Unit(Sub(face.P[1],face.P[0]));var v=Unit(Cross(face.N,u));var origin=face.P[0];
                double U(double[] p)=>Dot(Sub(p,origin),u);
                double V(double[] p)=>Dot(Sub(p,origin),v);
                foreach(var poly in polygons) {
                    int u0=(int)Math.Floor(poly.Min(U)+1e-8),u1=(int)Math.Ceiling(poly.Max(U)-1e-8);
                    int v0=(int)Math.Floor(poly.Min(V)+1e-8),v1=(int)Math.Ceiling(poly.Max(V)-1e-8);
                    for(int a=u0;a<u1;a++) for(int b=v0;b<v1;b++) {
                        var tile=Clip(Clip(Clip(Clip(poly,p=>U(p)-a),p=>a+1-U(p)),p=>V(p)-b),p=>b+1-V(p));
                        for(int k=1;k+1<tile.Count;k++) {
                            var tri=new[]{tile[0],tile[k],tile[k+1],tile[k+1]};
                            var n=Cross(Sub(tri[1],tri[0]),Sub(tri[2],tri[0]));
                            if(Dot(n,n)<1e-18) continue;
                            var q=new float[33];q[0]=(int)face.Mat;
                            for(int c=0;c<4;c++) {
                                for(int d=0;d<3;d++) {q[1+8*c+d]=(float)tri[c][d];q[6+8*c+d]=(float)face.N[d];}
                                q[4+8*c]=(float)((U(tri[c])-a)*16);q[5+8*c]=(float)((V(tri[c])-b)*16);
                            }
                            result.Add(q);
                        }
                    }
                }
            }
            return result;
        }
    }
}
