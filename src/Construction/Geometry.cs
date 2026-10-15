using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa.Construction
{
    /// <summary>Ship-local point or direction, in blocks. x along the keel, y up, z across.</summary>
    public readonly struct V3 : IEquatable<V3>
    {
        public readonly double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double t) => new(a.X * t, a.Y * t, a.Z * t);
        public static V3 operator -(V3 a) => new(-a.X, -a.Y, -a.Z);
        public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public V3 Cross(V3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(Dot(this));
        public V3 Unit => this * (1 / Length);
        public V3 With(int axis, double v) => axis == 0 ? new(v, Y, Z) : axis == 1 ? new(X, v, Z) : new(X, Y, v);
        public static V3 Axis(int axis, double v = 1) => new V3(0, 0, 0).With(axis, v);
        public bool Equals(V3 o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object o) => o is V3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:0.###},{Y:0.###},{Z:0.###})";
    }

    /// <summary>A convex solid as outward-facing planar polygons. Everything the compiler
    /// produces is built from these, so clipping to a cell is always exact.</summary>
    public sealed class Convex
    {
        public const double Eps = 1e-8;
        public readonly List<V3[]> Faces;
        public Convex(List<V3[]> faces) { Faces = faces; }

        public static V3? Normal(IReadOnlyList<V3> p)
        {
            for (int i = 1; i < p.Count - 1; i++)
            {
                var n = (p[i] - p[0]).Cross(p[i + 1] - p[0]);
                if (n.Length > Eps) return n.Unit;
            }
            return null;
        }

        public static double Area(IReadOnlyList<V3> p)
        {
            double a = 0;
            for (int i = 1; i < p.Count - 1; i++) a += (p[i] - p[0]).Cross(p[i + 1] - p[0]).Length / 2;
            return a;
        }

        public static V3 Mean(IEnumerable<V3> points)
        {
            double x = 0, y = 0, z = 0; int n = 0;
            foreach (var p in points) { x += p.X; y += p.Y; z += p.Z; n++; }
            return new V3(x / n, y / n, z / n);
        }

        /// <summary>Drops repeated points; null when nothing with area remains.</summary>
        public static V3[] Clean(IEnumerable<V3> points)
        {
            var r = new List<V3>();
            foreach (var p in points) if (r.Count == 0 || (p - r[^1]).Length > 1e-9) r.Add(p);
            if (r.Count > 1 && (r[0] - r[^1]).Length < 1e-9) r.RemoveAt(r.Count - 1);
            return r.Count >= 3 && Area(r) > 1e-10 ? r.ToArray() : null;
        }

        /// <summary>Orients every face away from the solid's centre.</summary>
        public static Convex Outward(IEnumerable<V3[]> faces)
        {
            var list = faces.Select(Clean).Where(f => f != null).ToList();
            if (list.Count < 4) return null;
            var centre = Mean(list.SelectMany(f => f));
            var result = new List<V3[]>();
            foreach (var f in list)
            {
                var n = Normal(f);
                if (n == null) continue;
                result.Add(n.Value.Dot(Mean(f) - centre) < 0 ? f.Reverse().ToArray() : f);
            }
            var solid = new Convex(result);
            return solid.Volume() > 1e-9 ? solid : null;
        }

        public static Convex Tetra(V3 a, V3 b, V3 c, V3 d) =>
            Outward(new[] { new[] { a, b, c }, new[] { a, b, d }, new[] { a, c, d }, new[] { b, c, d } });

        public static Convex Box(V3 lo, V3 hi)
        {
            V3 P(int i) => new((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z);
            return Outward(new[]
            {
                new[] { P(0), P(2), P(6), P(4) }, new[] { P(1), P(3), P(7), P(5) },
                new[] { P(0), P(1), P(5), P(4) }, new[] { P(2), P(3), P(7), P(6) },
                new[] { P(0), P(1), P(3), P(2) }, new[] { P(4), P(5), P(7), P(6) },
            });
        }

        /// <summary>Six tetrahedra around the v0-v6 diagonal. v0..v3 is the outer quad,
        /// v4..v7 the inner quad below them. Every quad face is split through v0 or v6,
        /// so neighbouring hexahedra built with the same numbering split shared faces
        /// identically and their internal faces cancel exactly.</summary>
        public static IEnumerable<Convex> Hexahedron(V3[] v)
        {
            int[][] tets = { new[] { 0, 1, 2, 6 }, new[] { 0, 2, 3, 6 }, new[] { 0, 3, 7, 6 },
                             new[] { 0, 7, 4, 6 }, new[] { 0, 4, 5, 6 }, new[] { 0, 5, 1, 6 } };
            foreach (var t in tets)
            {
                var c = Tetra(v[t[0]], v[t[1]], v[t[2]], v[t[3]]);
                if (c != null) yield return c;
            }
        }

        public double Volume()
        {
            var o = Faces[0][0];
            double v = 0;
            foreach (var f in Faces)
                for (int i = 1; i < f.Length - 1; i++)
                    v += (f[0] - o).Dot((f[i] - o).Cross(f[i + 1] - o)) / 6;
            return Math.Abs(v);
        }

        public (V3 lo, V3 hi) Bounds()
        {
            var pts = Faces.SelectMany(f => f).ToArray();
            return (new V3(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)),
                    new V3(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z)));
        }

        public bool Contains(V3 p)
        {
            foreach (var f in Faces)
            {
                var n = Normal(f).Value;
                if (n.Dot(p - f[0]) > Eps) return false;
            }
            return true;
        }

        /// <summary>Keeps sign*(coordinate - bound) &lt;= 0 and closes the cut.</summary>
        public Convex Clip(int axis, double bound, int sign) => ClipPlane(V3.Axis(axis, sign), sign * bound, axis);

        /// <summary>Keeps n·p &lt;= d and closes the cut. An axis-aligned cut snaps its new
        /// corners exactly onto the plane, so neighbouring pieces share identical faces.</summary>
        public Convex ClipPlane(V3 n, double d, int snapAxis = -1)
        {
            double scale = n.Length;
            n = n * (1 / scale); d /= scale;
            var result = new List<V3[]>();
            var cuts = new List<V3>();
            foreach (var face in Faces)
            {
                var poly = new List<V3>();
                var prev = face[^1];
                double dp = n.Dot(prev) - d;
                foreach (var cur in face)
                {
                    double dc = n.Dot(cur) - d;
                    if ((dp <= Eps) != (dc <= Eps))
                    {
                        var hit = prev + (cur - prev) * (dp / (dp - dc));
                        if (snapAxis >= 0) hit = hit.With(snapAxis, d * n[snapAxis]);
                        poly.Add(hit); cuts.Add(hit);
                    }
                    if (dc <= Eps) poly.Add(cur);
                    prev = cur; dp = dc;
                }
                var cleaned = Clean(poly);
                if (cleaned != null) result.Add(cleaned);
            }
            var unique = cuts.GroupBy(Key).Select(g => g.First()).ToList();
            if (unique.Count >= 3)
            {
                var centre = Mean(unique);
                var u = (Math.Abs(n.X) < 0.9 ? V3.Axis(0) : V3.Axis(1));
                u = (u - n * u.Dot(n)).Unit;
                var w = n.Cross(u);
                var cap = Clean(unique.OrderBy(p => Math.Atan2((p - centre).Dot(w), (p - centre).Dot(u))));
                if (cap != null) result.Add(cap);
            }
            if (result.Count < 4) return null;
            var solid = new Convex(result);
            return solid.Volume() > 1e-9 ? solid : null;
        }

        public Convex ClipBox(V3 lo, V3 hi)
        {
            Convex c = this;
            for (int a = 0; a < 3 && c != null; a++)
            {
                c = c.Clip(a, lo[a], -1);
                if (c != null) c = c.Clip(a, hi[a], 1);
            }
            return c;
        }

        static string Key(V3 p) => $"{Math.Round(p.X, 7)}|{Math.Round(p.Y, 7)}|{Math.Round(p.Z, 7)}";
        public static string FaceKey(IEnumerable<V3> points) =>
            string.Join(";", points.Select(p => $"{Math.Round(p.X, 5)}|{Math.Round(p.Y, 5)}|{Math.Round(p.Z, 5)}").OrderBy(s => s, StringComparer.Ordinal));
    }

    /// <summary>A drawable planar polygon with UVs measured in texels along its own surface.</summary>
    public sealed class ShipFace
    {
        public V3[] Points;
        public V3 Normal;
        public double[][] Uv;
        public string Part, Material;
    }

    public static class Uvs
    {
        /// <summary>16 texels per block of actual surface distance. U follows the grain projected
        /// onto the face; V is perpendicular on the same plane. Coordinates are unwrapped: a
        /// texture-atlas consumer must tile them, never clamp them.</summary>
        public static double[][] Metric(V3[] points, V3 normal, V3 grain)
        {
            var tangent = grain - normal * grain.Dot(normal);
            if (tangent.Length < 1e-6)
                foreach (var f in new[] { V3.Axis(0), V3.Axis(2), V3.Axis(1) })
                {
                    tangent = f - normal * f.Dot(normal);
                    if (tangent.Length > 1e-6) break;
                }
            var u = tangent.Unit;
            var v = normal.Cross(u);
            return points.Select(p => new[] { Math.Round(16 * p.Dot(u), 7), Math.Round(16 * p.Dot(v), 7) }).ToArray();
        }
    }
}
