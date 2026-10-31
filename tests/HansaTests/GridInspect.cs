using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Hansa.Construction;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HansaTests
{
    static partial class Program
    {
        static GridShips.Built BuildGrid(string root)
        {
            string A(string p) => File.ReadAllText(Path.Combine(root, "assets", "ships", p));
            var kinds = BlockKinds.Read(A("config/construction/kinds.json"));
            var design = ShipDesign.Read(A("config/shipdesigns/carrack.json"));
            return GridShips.Build(design, kinds, A("config/ships/carrack-m.json"), A("config/shipmeshes/carrack-m.json"));
        }

        /// <summary>
        /// The grid carrack's mesh and where her fittings are, as the game builds them, for tools/HansaPreview --skin:
        ///   dotnet run --project tests/HansaTests -- export-grid &lt;dir&gt;
        /// writes hansa-carrack.mesh.json (her mesh file) and hansa-carrack.def.json (fixtures, hooks, moving parts, ladders).
        /// </summary>
        static void ExportGrid(string root, string dir)
        {
            var built = BuildGrid(root);
            Directory.CreateDirectory(dir);
            var camel = new JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(), NullValueHandling = NullValueHandling.Ignore };
            File.WriteAllText(Path.Combine(dir, "hansa-carrack.mesh.json"), JsonConvert.SerializeObject(built.Skin, camel));
            var def = built.Def;
            var o = new JObject
            {
                ["fixtures"] = new JArray(def.Fixtures.Select(f => new JObject { ["code"] = f.Code, ["type"] = f.Type, ["pos"] = f.Pos == null ? null : new JArray(f.Pos) })),
                ["hooks"] = new JArray((def.Hooks ?? Array.Empty<HookDef>()).Select(h => new JObject { ["code"] = h.Code, ["pos"] = h.Pos == null ? null : new JArray(h.Pos) })),
                ["movingParts"] = new JArray(def.MovingParts.Select(p => new JObject { ["code"] = p.Code, ["axisPoint"] = p.AxisPoint == null ? null : new JArray(p.AxisPoint) })),
                ["ladders"] = new JArray(def.Ladders.Select(l => new JObject { ["code"] = l.Code, ["bottom"] = l.Bottom == null ? null : new JArray(l.Bottom), ["top"] = l.Top == null ? null : new JArray(l.Top) })),
            };
            File.WriteAllText(Path.Combine(dir, "hansa-carrack.def.json"), o.ToString(Formatting.None));
            Console.WriteLine($"wrote {dir}: {built.Skin.Groups.Sum(g => g.Value.Length)} quads");
        }

        /// <summary>
        /// A search for holes in the grid carrack, in her collision and in what's drawn of her:
        /// - each deck's planking layer, everywhere inside her hull's inner surface along the deck, but her designed
        ///   openings (wells, hatchways): solid in her collision, and a face drawn on its top and its underside;
        /// - her side planking, every quarter block from her bottom up to her sheer, both sides, but her designed openings
        ///   (ports, windows, gateways): solid at mid-thickness, and a face drawn where a ray from outside first meets her.
        /// Holes are joined into clusters (touching quarter-block samples) and listed with where they are.
        ///   dotnet run --project tests/HansaTests -- holes
        /// Returns the number of clusters.
        /// </summary>
        static int GridHoles(string root, bool print)
        {
            var built = BuildGrid(root);
            var def = built.Def;
            var ship = built.Ship;
            var lattice = ship.Lattice;
            var design = lattice.Design;
            var form = lattice.Form;
            double wl = built.Waterline, skin = design.Form.Skin, step = 0.25;
            var openKinds = new HashSet<string> { "gunport", "window", "gate", "hatch" };
            bool Opening(V3 p)
            {
                var c = lattice.CellAt(p);
                var st = ship.At(c);
                if (st != null) return openKinds.Contains(st.Kind);
                // a cell of hers by design that's empty in her default: a well or hatchway
                return lattice.Cells.ContainsKey(c);
            }
            bool Mast(V3 p) => def.Masts.Any(m => Math.Abs(p.X - m.X) < m.Radius + 0.3 && Math.Abs(p.Z - m.Z) < m.Radius + 0.3);
            var mesh = new MeshRays(built.Skin);
            var found = new List<(string What, List<V3> Points)>();

            void Cluster(string what, List<V3> pts)
            {
                var left = new HashSet<(int, int, int)>(pts.Select(p => ((int)Math.Round(p.X / step * 2), (int)Math.Round(p.Y / step * 2), (int)Math.Round(p.Z / step * 2))));
                var at = pts.ToDictionary(p => ((int)Math.Round(p.X / step * 2), (int)Math.Round(p.Y / step * 2), (int)Math.Round(p.Z / step * 2)), p => p);
                while (left.Count > 0)
                {
                    var first = left.First();
                    left.Remove(first);
                    var q = new Queue<(int, int, int)>(); q.Enqueue(first);
                    var group = new List<V3>();
                    while (q.Count > 0)
                    {
                        var k = q.Dequeue();
                        group.Add(at[k]);
                        for (int dx = -2; dx <= 2; dx += 2) for (int dy = -2; dy <= 2; dy += 2) for (int dz = -2; dz <= 2; dz += 2)
                        {
                            var n = (k.Item1 + dx, k.Item2 + dy, k.Item3 + dz);
                            if (left.Remove(n)) q.Enqueue(n);
                        }
                    }
                    found.Add((what, group));
                }
            }

            // ---- decks
            foreach (var d in design.Decks)
            {
                double y = d.Top - d.Thickness / 2;
                var noSolid = new List<V3>(); var noTop = new List<V3>(); var noUnder = new List<V3>();
                for (double x = Math.Ceiling(form.XMin / step) * step + step / 2; x < form.XMax; x += step)
                {
                    if (d.From is double f0 && x < f0 || d.To is double f1 && x > f1) continue;
                    for (double z = -8 + step / 2; z < 8; z += step)
                    {
                        var p = new V3(x, y, z);
                        if (!form.Inside(p, skin) || Opening(p) || Mast(p)) continue;
                        if (!def.Grid.SolidAt(x, y - wl, z)) { noSolid.Add(p); continue; }
                        // its top, seen from just over it (unless something of hers stands there: a bulwark leaning in over its edge)
                        double ya = d.Top - wl + 0.1;
                        if (!def.Grid.SolidAt(x, ya, z))
                        {
                            double top = mesh.Down(x, ya, z, 0.3);
                            if (double.IsNaN(top) || Math.Abs(top - (d.Top - wl)) > 0.07) noTop.Add(p);
                        }
                        // its underside, from just under it (unless a beam or a rib is there)
                        double yb = d.Top - d.Thickness - wl - 0.1;
                        if (d.Beams && !def.Grid.SolidAt(x, yb, z))
                        {
                            double under = mesh.Up(x, yb, z, 0.3);
                            if (double.IsNaN(under) || Math.Abs(under - (d.Top - d.Thickness - wl)) > 0.06) noUnder.Add(p);
                        }
                    }
                }
                Cluster($"{d.Code} deck: not solid", noSolid);
                Cluster($"{d.Code} deck: no top face drawn", noTop);
                Cluster($"{d.Code} deck: no underside drawn", noUnder);
            }

            // ---- her sides
            var sideHole = new List<V3>(); var sideUndrawn = new List<V3>();
            for (double x = Math.Ceiling(form.XMin / step) * step + step / 2; x < form.XMax; x += step)
                foreach (int side in new[] { -1, 1 })
                {
                    var sec = form.SectionAt(x, side);
                    double sheer = form.SheerAt(x, side);
                    for (double y = sec[0][0] + step / 2; y < sheer - 0.05; y += step)
                    {
                        double w = HullForm.HalfWidth(sec, y);
                        if (double.IsNaN(w) || w < skin + 0.1) continue;
                        var p = new V3(x, y, side * (w - skin / 2));
                        if (Opening(p)) continue;
                        if (!def.Grid.SolidAt(p.X, p.Y - wl, p.Z)) sideHole.Add(p);
                        double hit = mesh.Across(x, y - wl, side * (w + 1.5), -side, 1.6);
                        if (double.IsNaN(hit) || Math.Abs(Math.Abs(hit) - w) > 0.12) sideUndrawn.Add(p);
                    }
                }
            Cluster("side: not solid", sideHole);
            Cluster("side: no outer face drawn", sideUndrawn);

            if (print)
            {
                foreach (var g in found.GroupBy(f => f.What))
                {
                    Console.WriteLine($"  {g.Key}: {g.Count()} holes, {g.Sum(c => c.Points.Count)} samples");
                    foreach (var c in g.OrderByDescending(c => c.Points.Count).Take(12))
                    {
                        var p = c.Points;
                        Console.WriteLine($"      {p.Count,4} at x {p.Min(q => q.X):0.00}..{p.Max(q => q.X):0.00}, y {p.Min(q => q.Y):0.00}..{p.Max(q => q.Y):0.00} (design), z {p.Min(q => q.Z):0.00}..{p.Max(q => q.Z):0.00}");
                    }
                }
                Console.WriteLine($"{found.Count} holes");
            }
            return found.Count;
        }

        /// <summary>What's at a point (design coordinates) of the grid carrack: its cell and those round it, their
        /// kinds, pieces and the faces of their shapes through the vertical line there.
        ///   dotnet run --project tests/HansaTests -- probe x y z</summary>
        static void GridProbe(string root, double x, double y, double z)
        {
            var built = BuildGrid(root);
            var ship = built.Ship; var lattice = ship.Lattice; double wl = built.Waterline;
            var c0 = lattice.CellAt(new V3(x, y, z));
            for (int dy = -1; dy <= 1; dy++)
            {
                var c = new CellId(c0.X, c0.Y + dy, c0.Z);
                var (lo, hi) = lattice.Bounds(c);
                lattice.Cells.TryGetValue(c, out var lc);
                var st = ship.At(c);
                Console.WriteLine($"cell {c} [{lo.X},{lo.Y},{lo.Z}..{hi.X},{hi.Y},{hi.Z}] slot {lc?.SlotKey} lead {lc?.Lead} parts {string.Join(",", lc?.Pieces.Select(p => p.Part) ?? Array.Empty<string>())} kind {st?.Kind} open {st?.Open}");
                if (st == null) continue;
                foreach (var f in ship.Shape(c).Faces)
                {
                    var pts = f.Points;
                    if (pts.Min(p => p.X) > x || pts.Max(p => p.X) < x || pts.Min(p => p.Z) > z || pts.Max(p => p.Z) < z) continue;
                    Console.WriteLine($"   face {f.Part} n ({f.Normal.X:0.##},{f.Normal.Y:0.##},{f.Normal.Z:0.##}) y {pts.Min(p => p.Y):0.###}..{pts.Max(p => p.Y):0.###} key {Convex.FaceKey(pts).GetHashCode()}");
                }
            }
            foreach (var (name, quads) in built.Skin.Groups)
                foreach (var q in quads ?? Array.Empty<float[]>())
                {
                    float x0 = Math.Min(Math.Min(q[1], q[9]), Math.Min(q[17], q[25])), x1 = Math.Max(Math.Max(q[1], q[9]), Math.Max(q[17], q[25]));
                    float z0 = Math.Min(Math.Min(q[3], q[11]), Math.Min(q[19], q[27])), z1 = Math.Max(Math.Max(q[3], q[11]), Math.Max(q[19], q[27]));
                    float y0 = Math.Min(Math.Min(q[2], q[10]), Math.Min(q[18], q[26]));
                    if (x0 <= x && x <= x1 && z0 <= z && z <= z1 && Math.Abs(y0 - (y - wl)) < 1.0)
                        Console.WriteLine($"   quad {name} {built.Skin.Textures[(int)q[0]].Split('/').Last()} y {y0 + wl:0.###} (design) n ({q[6]:0.##},{q[7]:0.##},{q[8]:0.##})");
                }
        }

        /// <summary>Rays against a mesh file's quads (each as two triangles), bucketed by x.</summary>
        sealed class MeshRays
        {
            readonly Dictionary<int, List<float[]>> bins = new();

            public MeshRays(ShipSkinFile file)
            {
                foreach (var (name, quads) in file.Groups)
                {
                    if (quads == null || name.StartsWith("sail") || name.StartsWith("part:") || name == "shot" || name.EndsWith("bottomclad")) continue;
                    foreach (var q in quads)
                    {
                        float x0 = Math.Min(Math.Min(q[1], q[9]), Math.Min(q[17], q[25])), x1 = Math.Max(Math.Max(q[1], q[9]), Math.Max(q[17], q[25]));
                        for (int b = (int)Math.Floor(x0); b <= (int)Math.Floor(x1); b++)
                        {
                            if (!bins.TryGetValue(b, out var l)) bins[b] = l = new List<float[]>();
                            l.Add(q);
                        }
                    }
                }
            }

            /// <summary>Nearest hit along (dx, dy, dz) from (x, y, z) within max: the distance, or NaN.</summary>
            double Cast(double x, double y, double z, double dx, double dy, double dz, double max)
            {
                double best = double.NaN;
                for (int b = (int)Math.Floor(Math.Min(x, x + dx * max)); b <= (int)Math.Floor(Math.Max(x, x + dx * max)); b++)
                {
                    if (!bins.TryGetValue(b, out var l)) continue;
                    foreach (var q in l)
                        foreach (var (i, j, k) in new[] { (0, 1, 2), (0, 2, 3) })
                        {
                            double t = Tri(x, y, z, dx, dy, dz, q, i, j, k);
                            if (t >= 0 && t <= max && (double.IsNaN(best) || t < best)) best = t;
                        }
                }
                return best;
            }

            static double Tri(double ox, double oy, double oz, double dx, double dy, double dz, float[] q, int i, int j, int k)
            {
                double ax = q[1 + 8 * i], ay = q[2 + 8 * i], az = q[3 + 8 * i];
                double e1x = q[1 + 8 * j] - ax, e1y = q[2 + 8 * j] - ay, e1z = q[3 + 8 * j] - az;
                double e2x = q[1 + 8 * k] - ax, e2y = q[2 + 8 * k] - ay, e2z = q[3 + 8 * k] - az;
                double px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
                double det = e1x * px + e1y * py + e1z * pz;
                if (Math.Abs(det) < 1e-12) return -1;
                double inv = 1 / det, tx = ox - ax, ty = oy - ay, tz = oz - az;
                double u = (tx * px + ty * py + tz * pz) * inv;
                if (u < -1e-6 || u > 1 + 1e-6) return -1;
                double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
                double v = (dx * qx + dy * qy + dz * qz) * inv;
                if (v < -1e-6 || u + v > 1 + 1e-6) return -1;
                return (e2x * qx + e2y * qy + e2z * qz) * inv;
            }

            /// <summary>The height of the first face under (x, y, z) within max, or NaN.</summary>
            public double Down(double x, double y, double z, double max) { double t = Cast(x, y, z, 0, -1, 0, max); return double.IsNaN(t) ? t : y - t; }
            /// <summary>The height of the first face over (x, y, z) within max, or NaN.</summary>
            public double Up(double x, double y, double z, double max) { double t = Cast(x, y, z, 0, 1, 0, max); return double.IsNaN(t) ? t : y + t; }
            /// <summary>The z of the first face met going across (dir ±1) from (x, y, z) within max, or NaN.</summary>
            public double Across(double x, double y, double z, int dir, double max) { double t = Cast(x, y, z, 0, 0, dir, max); return double.IsNaN(t) ? t : z + dir * t; }
        }
    }
}
