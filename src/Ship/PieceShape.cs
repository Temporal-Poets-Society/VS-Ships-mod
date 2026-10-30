using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public sealed class PieceVolume
    {
        public double[] Box;
        public float[] Pose;
        double[][] corners;
        double[] bounds;
        public double[][] Corners => corners??=PlacementMesh.Corners(Box).Select(p=>Pose==null?p:PlacementMesh.Transform(Pose,p)).ToArray();
        public double[] Bounds => bounds??=PlacementMesh.Bounds(Corners);
    }

    /// <summary>The visible elements of a block, including the transforms of nested elements. Invisible modelling
    /// helpers never become placement obstacles. The hook planner and renderer use the same visible bounds.</summary>
    public static class PieceShape
    {
        public static PieceVolume FromCorners(double[][] c)
        {
            var m=Mat4f.Create();
            for(int a=0;a<3;a++) { m[a]=(float)(c[1][a]-c[0][a]);m[4+a]=(float)(c[2][a]-c[0][a]);m[8+a]=(float)(c[4][a]-c[0][a]);m[12+a]=(float)c[0][a]; }
            return new PieceVolume { Box=new double[]{0,0,0,1,1,1},Pose=m };
        }

        public static IEnumerable<PieceVolume> StairVolumes(MountPoint foot,int direction,double rise,int cells,bool rails) =>
            ModularGeometry.Flight(foot,direction,rise,cells>0?cells:PiecePlanner.FlightCells,rails).Select(p=>FromCorners(ModularMesh.Corners(p)));

        /// <summary>Separating axes for convex block elements, including rotated and sheared stair timbers.</summary>
        public static bool Overlap(PieceVolume a,PieceVolume b)
        {
            // Border pieces' 1 cm selection sheets meet at corners; their drawn ends are trimmed together.
            if(a.Pose==null && b.Pose==null) return PiecePlanner.Overlap(a.Box,b.Box);
            if(!PiecePlanner.Overlap(a.Bounds,b.Bounds,.003)) return false;
            var ca=a.Corners;var cb=b.Corners;
            double[] Sub(double[] p,double[] q)=>p.Zip(q,(x,y)=>x-y).ToArray();
            double[] Cross(double[] p,double[] q)=>new[]{p[1]*q[2]-p[2]*q[1],p[2]*q[0]-p[0]*q[2],p[0]*q[1]-p[1]*q[0]};
            double Dot(double[] p,double[] q)=>p[0]*q[0]+p[1]*q[1]+p[2]*q[2];
            var ea=new[]{Sub(ca[1],ca[0]),Sub(ca[2],ca[0]),Sub(ca[4],ca[0])};
            var eb=new[]{Sub(cb[1],cb[0]),Sub(cb[2],cb[0]),Sub(cb[4],cb[0])};
            var axes=new List<double[]>();
            for(int i=0;i<3;i++) { axes.Add(Cross(ea[i],ea[(i+1)%3]));axes.Add(Cross(eb[i],eb[(i+1)%3])); }
            foreach(var u in ea) foreach(var v in eb) axes.Add(Cross(u,v));
            foreach(var axis in axes) {
                double length=Math.Sqrt(Dot(axis,axis));if(length<1e-10) continue;
                var pa=ca.Select(p=>Dot(p,axis)/length).ToArray();var pb=cb.Select(p=>Dot(p,axis)/length).ToArray();
                if(pa.Max()<=pb.Min()+.003 || pb.Max()<=pa.Min()+.003) return false;
            }
            return true;
        }
        public static float[] Tilt(double[] pivot, double[] normal, int direction)
        {
            if (pivot == null || normal == null || normal.Length < 3) return null;
            double sx = MountLayout.DX[direction], sz = MountLayout.DZ[direction];
            double ax = -sz * normal[1], ay = sz * normal[0] - sx * normal[2], az = sx * normal[1];
            double sin = Math.Sqrt(ax * ax + ay * ay + az * az), cos = sx * normal[0] + sz * normal[2];
            if (sin < 1e-8) return null;
            var m = Mat4f.Create();
            Mat4f.Translate(m, m, (float)pivot[0], (float)pivot[1], (float)pivot[2]);
            Mat4f.Rotate(m, m, (float)Math.Atan2(sin, cos), new[] { (float)(ax / sin), (float)(ay / sin), (float)(az / sin) });
            Mat4f.Translate(m, m, -(float)pivot[0], -(float)pivot[1], -(float)pivot[2]);
            return m;
        }

        public static double[] Bounds(Shape shape)
        {
            var volumes = Volumes(shape);
            return volumes.Count == 0 ? null : PlacementMesh.Bounds(volumes.SelectMany(v =>
                PlacementMesh.Corners(v.Box).Select(p => PlacementMesh.Transform(v.Pose, p))));
        }

        public static List<PieceVolume> Volumes(Shape shape)
        {
            var volumes = new List<PieceVolume>();
            void Element(ShapeElement e, float[] parent)
            {
                var m = (float[])parent.Clone();
                var o = e.RotationOrigin ?? new double[3];
                Mat4f.Translate(m, m, (float)o[0] / 16, (float)o[1] / 16, (float)o[2] / 16);
                Mat4f.RotateZ(m, m, (float)e.RotationZ * GameMath.DEG2RAD);
                Mat4f.RotateY(m, m, (float)e.RotationY * GameMath.DEG2RAD);
                Mat4f.RotateX(m, m, (float)e.RotationX * GameMath.DEG2RAD);
                Mat4f.Translate(m, m, -(float)o[0] / 16, -(float)o[1] / 16, -(float)o[2] / 16);
#pragma warning disable CS0618
                var faces = e.FacesResolved ?? e.Faces?.Values.ToArray() ?? Array.Empty<ShapeElementFace>();
#pragma warning restore CS0618
                if (e.From != null && e.To != null && faces.Any(f => f != null && f.Enabled && f.Texture != "#null"))
                {
                    var b = new[] { e.From[0] / 16, e.From[1] / 16, e.From[2] / 16, e.To[0] / 16, e.To[1] / 16, e.To[2] / 16 };
                    if ((b[3] > b[0] ? 1 : 0) + (b[4] > b[1] ? 1 : 0) + (b[5] > b[2] ? 1 : 0) >= 2)
                        volumes.Add(new PieceVolume { Box = b, Pose = (float[])m.Clone() });
                }
                if (e.Children == null) return;
                if (e.From != null) Mat4f.Translate(m, m, (float)e.From[0] / 16, (float)e.From[1] / 16, (float)e.From[2] / 16);
                foreach (var child in e.Children) Element(child, m);
            }
            foreach (var e in shape?.Elements ?? Array.Empty<ShapeElement>()) Element(e, Mat4f.Create());
            return volumes;
        }

        public static void FitVolumes(PiecePlan plan, IEnumerable<PieceVolume> volumes, double[] origin, int quarter)
        {
            if (volumes == null) return;
            var fit = Mat4f.Create();
            Mat4f.Translate(fit, fit, (float)origin[0] + .5f, (float)origin[1], (float)origin[2] + .5f);
            Mat4f.RotateY(fit, fit, quarter * GameMath.PIHALF);
            Mat4f.Translate(fit, fit, -.5f, 0, -.5f);
            var tilt = Tilt(plan.Pivot, plan.Tilt, plan.Dir);
            if (tilt != null) Mat4f.Mul(fit, tilt, (float[])fit.Clone());
            foreach (var volume in volumes)
            {
                var m = new float[16];
                Mat4f.Mul(m, fit, volume.Pose);
                plan.Volumes.Add(new PieceVolume { Box = volume.Box, Pose = m });
            }
        }
    }
}
