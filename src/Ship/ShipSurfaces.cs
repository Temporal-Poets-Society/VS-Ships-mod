using System;
using System.Collections.Generic;

namespace Hansa
{
    /// <summary>
    /// (0.3.14, notes 148, 149) Her timbers and planking as drawn: the mesh file's triangles a partition meets (every quad of
    /// the "interior" group: frames, beams, knees, clamps, the ceiling; of the "exterior": her planking, the strips closing
    /// her decks to her sides; and the inside planking marked in the other groups,
    /// <see cref="ShipSkinFile.HullIn"/>), and the faces her shapes draw on her cells (decks, cabin walls: <see cref="AddShape"/>),
    /// in a grid of half-block cells, for short rays. Her collision is on a quarter-block
    /// grid and covers each timber's cells whole, so a partition cut to its free cells stopped up to a quarter block short of
    /// a beam or her curved side; it's drawn out to these surfaces now (<see cref="MountLayout.DrawnClip"/>).
    /// </summary>
    public sealed class ShipSurfaces
    {
        const double Cell = 0.5;
        readonly List<float> tris = new();
        readonly Dictionary<(int, int, int), List<int>> cells = new();

        public int Count => tris.Count / 9;

        public static ShipSurfaces From(ShipSkinFile file)
        {
            var s = new ShipSurfaces();
            if (file?.Groups == null) return s;
            foreach (var kv in file.Groups)
            {
                int[] ranges = null;
                // (her hull and decks' edge strips are in the exterior group: a deck closed to her curved side)
                if (kv.Key == "interior" || kv.Key == "exterior") ranges = new[] { 0, kv.Value.Length };
                else if (file.HullIn != null && file.HullIn.TryGetValue(kv.Key, out var r)) ranges = r;
                else if (kv.Key.StartsWith("slot:hull") && file.SlotQuads != null && file.SlotQuads.TryGetValue(kv.Key, out var refs))
                {
                    // ((0.3.26, step D1) her hull panels' planking and ceilings, as they're drawn all in: their own quads)
                    for (int q = 0; q < kv.Value.Length && 2 * q + 1 < refs.Length; q++)
                    {
                        var quad = kv.Value[q];
                        if (refs[2 * q] < 0 || refs[2 * q + 1] >= 0 || quad == null || quad.Length < 33) continue;
                        s.AddTri(quad, 0, 1, 2);
                        s.AddTri(quad, 0, 2, 3);
                    }
                    continue;
                }
                if (ranges == null) continue;
                for (int n = 0; n + 1 < ranges.Length; n += 2)
                    for (int q = Math.Max(0, ranges[n]); q < Math.Min(kv.Value.Length, ranges[n + 1]); q++)
                    {
                        var quad = kv.Value[q];
                        if (quad == null || quad.Length < 33) continue;
                        s.AddTri(quad, 0, 1, 2);
                        s.AddTri(quad, 0, 2, 3);
                    }
            }
            return s;
        }

        /// <summary>The faces a shape draws (her interior and exterior: boxes on her cells, not turned), as surfaces.</summary>
        public void AddShape(Vintagestory.API.Common.Shape shape)
        {
            if (shape?.Elements == null) return;
            void Add(Vintagestory.API.Common.ShapeElement e)
            {
                if (e.From != null && e.To != null && e.RotationX == 0 && e.RotationY == 0 && e.RotationZ == 0)
                {
                    IEnumerable<string> faces = null;
                    if (e.FacesResolved != null)
                    {
                        var l = new List<string>();
                        for (int i = 0; i < e.FacesResolved.Length && i < 6; i++)
                            if (e.FacesResolved[i] != null) l.Add(Vintagestory.API.MathTools.BlockFacing.ALLFACES[i].Code);
                        faces = l;
                    }
#pragma warning disable CS0618
                    faces ??= e.Faces?.Keys;
#pragma warning restore CS0618
                    if (faces != null) AddBoxFaces(e.From, e.To, faces);
                }
                if (e.Children != null) foreach (var ch in e.Children) Add(ch);
            }
            foreach (var e in shape.Elements) Add(e);
        }

        /// <summary>A box's faces (from, to in sixteenths of a block, as a shape has them; faces by name: north, east, south, west, up, down).</summary>
        public void AddBoxFaces(double[] from, double[] to, IEnumerable<string> faces)
        {
            double x0 = Math.Min(from[0], to[0]) / 16, y0 = Math.Min(from[1], to[1]) / 16, z0 = Math.Min(from[2], to[2]) / 16;
            double x1 = Math.Max(from[0], to[0]) / 16, y1 = Math.Max(from[1], to[1]) / 16, z1 = Math.Max(from[2], to[2]) / 16;
            foreach (string f in faces)
            {
                double[][] c = f switch
                {
                    "north" => new[] { new[] { x0, y0, z0 }, new[] { x1, y0, z0 }, new[] { x1, y1, z0 }, new[] { x0, y1, z0 } },
                    "south" => new[] { new[] { x0, y0, z1 }, new[] { x1, y0, z1 }, new[] { x1, y1, z1 }, new[] { x0, y1, z1 } },
                    "west" => new[] { new[] { x0, y0, z0 }, new[] { x0, y0, z1 }, new[] { x0, y1, z1 }, new[] { x0, y1, z0 } },
                    "east" => new[] { new[] { x1, y0, z0 }, new[] { x1, y0, z1 }, new[] { x1, y1, z1 }, new[] { x1, y1, z0 } },
                    "down" => new[] { new[] { x0, y0, z0 }, new[] { x1, y0, z0 }, new[] { x1, y0, z1 }, new[] { x0, y0, z1 } },
                    "up" => new[] { new[] { x0, y1, z0 }, new[] { x1, y1, z0 }, new[] { x1, y1, z1 }, new[] { x0, y1, z1 } },
                    _ => null
                };
                if (c == null) continue;
                var q = new float[33];
                for (int k = 0; k < 4; k++) { q[1 + k * 8] = (float)c[k][0]; q[2 + k * 8] = (float)c[k][1]; q[3 + k * 8] = (float)c[k][2]; }
                AddTri(q, 0, 1, 2);
                AddTri(q, 0, 2, 3);
            }
        }

        void AddTri(float[] q, int a, int b, int c)
        {
            int id = tris.Count / 9;
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            foreach (int k in new[] { a, b, c })
            {
                int o = 1 + k * 8;
                tris.Add(q[o]); tris.Add(q[o + 1]); tris.Add(q[o + 2]);
                x0 = Math.Min(x0, q[o]); x1 = Math.Max(x1, q[o]);
                y0 = Math.Min(y0, q[o + 1]); y1 = Math.Max(y1, q[o + 1]);
                z0 = Math.Min(z0, q[o + 2]); z1 = Math.Max(z1, q[o + 2]);
            }
            for (int i = Key(x0); i <= Key(x1); i++)
                for (int j = Key(y0); j <= Key(y1); j++)
                    for (int k = Key(z0); k <= Key(z1); k++)
                    {
                        if (!cells.TryGetValue((i, j, k), out var list)) cells[(i, j, k)] = list = new List<int>();
                        list.Add(id);
                    }
        }

        static int Key(double v) => (int)Math.Floor(v / Cell);

        /// <summary>
        /// How far a ray (ship-local; d need not be normalised, t is in its lengths) goes before it meets one of her surfaces,
        /// within maxT; double.MaxValue if it doesn't. Either side of a surface counts.
        /// </summary>
        public double Ray(double ox, double oy, double oz, double dx, double dy, double dz, double maxT)
        {
            double best = double.MaxValue;
            double ex = ox + dx * maxT, ey = oy + dy * maxT, ez = oz + dz * maxT;
            int i0 = Key(Math.Min(ox, ex)), i1 = Key(Math.Max(ox, ex));
            int j0 = Key(Math.Min(oy, ey)), j1 = Key(Math.Max(oy, ey));
            int k0 = Key(Math.Min(oz, ez)), k1 = Key(Math.Max(oz, ez));
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    for (int k = k0; k <= k1; k++)
                    {
                        if (!cells.TryGetValue((i, j, k), out var list)) continue;
                        foreach (int id in list)
                        {
                            double t = HitTri(id, ox, oy, oz, dx, dy, dz);
                            if (t >= 0 && t <= maxT && t < best) best = t;
                        }
                    }
            return best;
        }

        /// <summary>Möller–Trumbore: t along the ray where it meets triangle id, or -1.</summary>
        double HitTri(int id, double ox, double oy, double oz, double dx, double dy, double dz)
        {
            int b = id * 9;
            double ax = tris[b], ay = tris[b + 1], az = tris[b + 2];
            double e1x = tris[b + 3] - ax, e1y = tris[b + 4] - ay, e1z = tris[b + 5] - az;
            double e2x = tris[b + 6] - ax, e2y = tris[b + 7] - ay, e2z = tris[b + 8] - az;
            double px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
            double det = e1x * px + e1y * py + e1z * pz;
            if (Math.Abs(det) < 1e-12) return -1;
            double inv = 1 / det;
            double tx = ox - ax, ty = oy - ay, tz = oz - az;
            double u = (tx * px + ty * py + tz * pz) * inv;
            if (u < -1e-9 || u > 1 + 1e-9) return -1;
            double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
            double v = (dx * qx + dy * qy + dz * qz) * inv;
            if (v < -1e-9 || u + v > 1 + 1e-9) return -1;
            return (e2x * qx + e2y * qy + e2z * qz) * inv;
        }
    }
}
