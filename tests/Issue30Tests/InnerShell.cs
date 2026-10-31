using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

readonly record struct P(double X, double Y, double Z)
{
    public static P operator +(P a, P b) => new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static P operator *(P a, double f) => new(a.X*f,a.Y*f,a.Z*f);
    public static P operator -(P a, P b) => a+b*(-1);
    public double Length => Math.Sqrt(X*X+Y*Y+Z*Z);
}

// Actual inner planking triangles, independent of the dry mask/outline and ribs.
// Width casts a ray across the hull at exact (x,y); it does not use voxel widths.
sealed class InnerShell
{
    readonly record struct Tri(P A, P B, P C, P N);
    readonly List<Tri> tris = new();
    readonly Dictionary<(int,int), List<Tri>> bins = new();
    static int Cell(double v) => (int)Math.Floor(v*4);

    public InnerShell(JObject mesh)
    {
        foreach (string group in new[] { "interior", "slot:hull:interior" })
        foreach (JArray q in mesh["groups"][group]) {
            // The exporter assigns texture 1 to the inner shell; texture 4 is ribs.
            if ((int)q[0] != 1) continue;
            P V(int k) => new((double)q[1+8*k],(double)q[2+8*k],(double)q[3+8*k]);
            P n = new((double)q[6],(double)q[7],(double)q[8]);
            foreach (var t in new[] { new Tri(V(0),V(1),V(2),n), new Tri(V(0),V(2),V(3),n) }) {
                var mid = (t.A+t.B+t.C)*(1.0/3);
                if (mid.Z*t.N.Z >= -.001) continue; // inward-facing side, excluding cut ends
                tris.Add(t);
                for (int i=Cell(Math.Min(t.A.X,Math.Min(t.B.X,t.C.X))); i<=Cell(Math.Max(t.A.X,Math.Max(t.B.X,t.C.X))); i++)
                for (int j=Cell(Math.Min(t.A.Y,Math.Min(t.B.Y,t.C.Y))); j<=Cell(Math.Max(t.A.Y,Math.Max(t.B.Y,t.C.Y))); j++) {
                    if (!bins.TryGetValue((i,j),out var list)) bins[(i,j)] = list = new();
                    list.Add(t);
                }
            }
        }
    }

    public double Width(double x, double y)
    {
        if (!bins.TryGetValue((Cell(x),Cell(y)),out var list)) return 0;
        double width = 0;
        foreach (var t in list) {
            double den = (t.B.Y-t.C.Y)*(t.A.X-t.C.X)+(t.C.X-t.B.X)*(t.A.Y-t.C.Y);
            if (Math.Abs(den)<1e-12) continue;
            double a = ((t.B.Y-t.C.Y)*(x-t.C.X)+(t.C.X-t.B.X)*(y-t.C.Y))/den;
            double b = ((t.C.Y-t.A.Y)*(x-t.C.X)+(t.A.X-t.C.X)*(y-t.C.Y))/den;
            double c = 1-a-b;
            if (a < -1e-7 || b < -1e-7 || c < -1e-7) continue;
            width = Math.Max(width,Math.Abs(t.A.Z*a+t.B.Z*b+t.C.Z*c));
        }
        return width;
    }

    public List<P> Samples()
    {
        var samples = new HashSet<P>();
        foreach (var t in tris) {
            int n = Math.Max(1,(int)Math.Ceiling(Math.Max((t.A-t.B).Length,Math.Max((t.A-t.C).Length,(t.B-t.C).Length))/.15));
            for (int a=0;a<=n;a++) for (int b=0;b<=n-a;b++) {
                P p = t.A*((double)a/n)+t.B*((double)b/n)+t.C*(1.0-(double)(a+b)/n);
                // The cargo hold, including bilge below its floor, away from the posts.
                if (p.X < -14.5 || p.X > 8 || p.Y < -2.7 || p.Y > .525) continue;
                p += t.N*.005;
                samples.Add(new P(Math.Round(p.X,6),Math.Round(p.Y,6),Math.Round(p.Z,6)));
            }
        }
        return samples.ToList();
    }

    // Below DryRowY0 the shader holds row 0: its width is linear in x in each
    // station interval. Inner planking is planar. Therefore the width difference
    // is affine on each clipped triangle and its minimum occurs at a vertex.
    // Testing these vertices and station crossings covers the continuous surface,
    // including the x positions between the regular diagnostic samples.
    public (int Vertices, int Missed, double Margin, P Worst) ExactBilgeCoverage(
        double bottom, double top, double[] stations, Func<P,double> dryWidth)
    {
        int vertices=0,missed=0; double margin=double.PositiveInfinity;P worst=default;
        foreach (var t in tris) {
            var poly=new List<P>{t.A,t.B,t.C};
            poly=Clip(poly,p=>p.X+14.5);poly=Clip(poly,p=>8-p.X);
            poly=Clip(poly,p=>p.Y-bottom);poly=Clip(poly,p=>top-p.Y);
            if(poly.Count==0)continue;
            var tests=new List<P>(poly);
            foreach(double x in stations) for(int i=0;i<poly.Count;i++) {
                P a=poly[i],b=poly[(i+1)%poly.Count];
                if ((a.X<x && b.X>x)||(b.X<x && a.X>x)) tests.Add(a+(b-a)*((x-a.X)/(b.X-a.X)));
            }
            foreach(P p in tests) {
                vertices++;double m=dryWidth(p)-Math.Abs(p.Z);
                if(m<margin){margin=m;worst=p;}
                if(m<0)missed++;
            }
        }
        return(vertices,missed,margin,worst);
    }

    static List<P> Clip(List<P> input,Func<P,double> signedDistance)
    {
        var output=new List<P>();
        if(input.Count==0)return output;
        P a=input[^1];double da=signedDistance(a);
        foreach(P b in input) {
            double db=signedDistance(b);
            if ((da>=0)!=(db>=0)) output.Add(a+(b-a)*(da/(da-db)));
            if(db>=0)output.Add(b);
            a=b;da=db;
        }
        return output;
    }
}
