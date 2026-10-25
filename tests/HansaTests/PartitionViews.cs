using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (25 October 2026, caravel notes 88, 90: "small visual protrusions" where partitions meet her side) Close-ups of
        /// partitions as the game draws them (<see cref="MountLayout.DrawnClip"/>: boxes the partition's thickness on its line)
        /// against her drawn surfaces (her mesh file's quads as built, slot quads of her filled cells), ray-cast to PNG: her
        /// surfaces grey, partitions red (lit by their faces), so a fin or a gap shows.
        ///   dotnet run --project tests/HansaTests -- partition-views &lt;dir&gt; &lt;tag&gt;
        /// </summary>
        static void PartitionViews(string root, string outDir, string tag)
        {
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", "caravel.json");
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            var l = def.Mounts;
            var surf = SurfacesOf(def, Path.Combine(root, "assets", "hansa"));
            var skin = JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "shipmeshes", "caravel.json")));
            Directory.CreateDirectory(outDir);
            // her drawn triangles: every group but the moving parts, sails and shot; a slot group's quads as drawn with her
            // cells and panels as she starts
            var deckStart = new SlotStates(def.Slots);
            var tris = new List<(double[] A, double[] B, double[] C, int Kind)>();
            foreach (var kv in skin.Groups)
            {
                if (kv.Key.StartsWith("part:") || kv.Key.StartsWith("sail") || kv.Key == "shot" || kv.Key.Contains("bottomclad")) continue;
                int[] refs = skin.SlotQuads != null && skin.SlotQuads.TryGetValue(kv.Key, out var r) ? r : null;
                bool hull = kv.Key.StartsWith("slot:hull");
                for (int q = 0; q < kv.Value.Length; q++)
                {
                    var quad = kv.Value[q];
                    if (quad == null || quad.Length < 33) continue;
                    if (refs != null && 2 * q + 1 < refs.Length)
                    {
                        int owner = refs[2 * q], facing = refs[2 * q + 1];
                        // (her hull panels all in at the start: a panel's quads facing one out aren't drawn)
                        bool ownerIn = owner < 0 || hull || deckStart.Filled(owner), facingOut = facing >= 0 && (hull ? false : !deckStart.Filled(facing));
                        if (!ownerIn || facing >= 0 && !facingOut) continue;
                    }
                    double[] V(int i) => new double[] { quad[1 + i * 8], quad[2 + i * 8], quad[3 + i * 8] };
                    tris.Add((V(0), V(1), V(2), 0));
                    tris.Add((V(0), V(2), V(3), 0));
                }
            }
            // (each view's walls: lines across her at x (along z), or along her at z)
            var views = new (string Name, double[] Eye, double[] At, string Lines)[]
            {
                // the cabin front's starboard outer cell, the cap rail over it: from the steerage and from the waist
                ("front-stbd-from-steerage", new[] { -8.6, 2.7, 1.4 }, new[] { -7.0, 2.9, 2.6 }, "x-7"),
                ("caprail-stbd-from-steerage", new[] { -7.9, 3.3, 1.9 }, new[] { -7.0, 3.2, 2.65 }, "x-7"),
                ("caprail-stbd-from-waist", new[] { -6.1, 3.3, 1.9 }, new[] { -7.0, 3.2, 2.65 }, "x-7"),
                ("caprail-port-from-steerage", new[] { -7.9, 3.3, -1.9 }, new[] { -7.0, 3.2, -2.65 }, "x-7"),
                ("front-stbd-from-waist", new[] { -5.6, 2.7, 1.5 }, new[] { -7.0, 2.9, 2.6 }, "x-7"),
                ("front-port-from-steerage", new[] { -8.6, 2.7, -1.4 }, new[] { -7.0, 2.9, -2.6 }, "x-7"),
                ("front-port-from-waist", new[] { -5.6, 2.7, -1.5 }, new[] { -7.0, 2.9, -2.6 }, "x-7"),
                // the default wall only (the outer cells open, note 87), as the user saw it for note 88
                ("inner-stbd-from-waist", new[] { -5.6, 2.8, 1.6 }, new[] { -7.0, 2.6, 2.6 }, "x-7 inner"),
                ("inner-stbd-from-steerage", new[] { -8.5, 2.8, 1.5 }, new[] { -7.0, 2.6, 2.6 }, "x-7 inner"),
                ("inner-port-from-waist", new[] { -5.6, 2.8, -1.6 }, new[] { -7.0, 2.6, -2.6 }, "x-7 inner"),
                ("note88-inner", new[] { -5.722, 2.828, 1.769 }, new[] { -7.15, 2.196, 2.75 }, "x-7 inner"),
                // wider: the corner where the cabin front meets her side, all its places and the default wall only
                ("wide-stbd-from-waist", new[] { -4.6, 3.0, 0.6 }, new[] { -7.0, 2.6, 2.4 }, "x-7"),
                ("wide-stbd-from-waist-inner", new[] { -4.6, 3.0, 0.6 }, new[] { -7.0, 2.6, 2.4 }, "x-7 inner"),
                ("wide-stbd-from-steerage", new[] { -9.4, 3.0, 0.6 }, new[] { -7.0, 2.6, 2.4 }, "x-7"),
                // her side from outside at the cabin front (a partition run on through her planking shows there)
                ("outside-stbd", new[] { -6.2, 2.6, 4.6 }, new[] { -7.0, 2.4, 3.0 }, "x-7"),
                ("outside-port", new[] { -6.2, 2.6, -4.6 }, new[] { -7.0, 2.4, -3.0 }, "x-7"),
                ("outside-stbd-high", new[] { -6.0, 3.6, 4.4 }, new[] { -7.0, 3.3, 2.9 }, "x-7"),
                // note 88's look (her side at starboard station 3, band 5) and note 90's (Piece 20, her port side in the steerage)
                ("note88", new[] { -5.722, 2.828, 1.769 }, new[] { -7.15, 2.196, 2.75 }, "x-7"),
                ("note90", new[] { -8.496, 2.817, -1.014 }, new[] { -7.357, 2.599, -2.25 }, "x-7 z-2.5"),
            };
            foreach (var (name, eye, at, lines) in views)
            {
                // the partitions round the view: every place (not a twin) on its walls within 1.6 of where it looks, on the main deck
                bool OnLines(MountPoint b)
                {
                    var (line, alongX) = MountLayout.BorderLine(b);
                    // ("inner": the design's default wall only, her outer cells by her side left open, as the user had it)
                    if (lines.Contains("inner") && Math.Abs(alongX ? b.X : b.Z) > 2.5) return false;
                    return lines.Split(' ').Where(w => w != "inner").Any(w => (w[0] == 'x') == !alongX && Math.Abs(double.Parse(w.Substring(1), System.Globalization.CultureInfo.InvariantCulture) - line) < 1e-6);
                }
                var near = l.Borders.Where(b => !b.TwinSide && b.Deck == "main" && b.Box != null && OnLines(b) &&
                                                Math.Max(Math.Max(b.Box[0] - at[0], at[0] - b.Box[3]), 0) < 1.6 && Math.Max(Math.Max(b.Box[2] - at[2], at[2] - b.Box[5]), 0) < 1.6 &&
                                                Math.Max(Math.Max(b.Box[1] - at[1], at[1] - b.Box[4]), 0) < 1.6).ToList();
                var boxes = new List<(double[] Box, bool Past)>();
                foreach (var b in near)
                {
                    var own = b.Clip ?? new List<double[]> { Whole(b) };
                    var rects = l.DrawnClip(b, surf) ?? own;
                    // (each drawn rect split into its part within the place's own free parts and what's drawn past them)
                    foreach (var c in rects)
                        foreach (var (part, past) in SplitBy(c, own))
                            boxes.Add((PiecePlanner.ClipBox(b, part, MountLayout.PartitionThickness / 2), past));
                }
                var png = Render(tris, boxes, eye, at, 640, 480, name.StartsWith("wide") ? 70 : 55);
                string file = Path.Combine(outDir, $"{name}-{tag}.png");
                File.WriteAllBytes(file, png);
                Console.WriteLine($"  {file}: {near.Count} partitions, {boxes.Count} boxes");
            }
        }

        /// <summary>A ray-cast picture: triangles grey, boxes red, each lit by its face's normal; sky dark blue.</summary>
        /// <summary>A rect (a0, y0, a1, y1) cut into its parts inside the others' union (false) and outside it (true), on a 1/32 grid.</summary>
        static List<(double[] Rect, bool Past)> SplitBy(double[] c, List<double[]> own)
        {
            var list = new List<(double[], bool)>();
            const double s = 1.0 / 32;
            for (double a = c[0]; a < c[2] - 1e-9; a += s)
                for (double y = c[1]; y < c[3] - 1e-9; y += s)
                {
                    double a1 = Math.Min(c[2], a + s), y1 = Math.Min(c[3], y + s), am = (a + a1) / 2, ym = (y + y1) / 2;
                    bool inside = own.Any(o => am > o[0] && am < o[2] && ym > o[1] && ym < o[3]);
                    list.Add((new[] { a, y, a1, y1 }, !inside));
                }
            return list;
        }

        static byte[] Render(List<(double[] A, double[] B, double[] C, int Kind)> allTris, List<(double[] Box, bool Past)> boxes, double[] eye, double[] at, int w, int h, double fovDeg)
        {
            // (only the triangles near: within 3 of where it looks, in a grid of 0.25 cells walked along each ray)
            const double R = 3, C = 0.25;
            double[] lo = { at[0] - R, at[1] - R, at[2] - R };
            int N = (int)Math.Ceiling(2 * R / C);
            var cells = new Dictionary<int, List<int>>();
            var tris = allTris.Where(t => new[] { t.A, t.B, t.C }.Any(p => Math.Abs(p[0] - at[0]) < R && Math.Abs(p[1] - at[1]) < R && Math.Abs(p[2] - at[2]) < R)).ToList();
            for (int i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                int[] c0 = new int[3], c1 = new int[3];
                for (int a = 0; a < 3; a++)
                {
                    double mn = Math.Min(t.A[a], Math.Min(t.B[a], t.C[a])), mx = Math.Max(t.A[a], Math.Max(t.B[a], t.C[a]));
                    c0[a] = Math.Max(0, (int)Math.Floor((mn - lo[a]) / C - 1e-6));
                    c1[a] = Math.Min(N - 1, (int)Math.Floor((mx - lo[a]) / C + 1e-6));
                }
                for (int x = c0[0]; x <= c1[0]; x++)
                    for (int y = c0[1]; y <= c1[1]; y++)
                        for (int z = c0[2]; z <= c1[2]; z++)
                        {
                            int key = (x * N + y) * N + z;
                            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                            list.Add(i);
                        }
            }
            double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
            double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
            double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
            double[] Norm(double[] a) { double n = Math.Sqrt(Dot(a, a)); return new[] { a[0] / n, a[1] / n, a[2] / n }; }
            double TriHit((double[] A, double[] B, double[] C, int Kind) t, double[] d)
            {
                var e1 = Sub(t.B, t.A);
                var e2 = Sub(t.C, t.A);
                var pv = Cross(d, e2);
                double det = Dot(e1, pv);
                if (Math.Abs(det) < 1e-12) return double.MaxValue;
                double inv = 1 / det;
                var tv = Sub(eye, t.A);
                double u = Dot(tv, pv) * inv;
                if (u < 0 || u > 1) return double.MaxValue;
                var qv = Cross(tv, e1);
                double v = Dot(d, qv) * inv;
                if (v < 0 || u + v > 1) return double.MaxValue;
                double tt = Dot(e2, qv) * inv;
                return tt > 1e-4 ? tt : double.MaxValue;
            }
            var fwd = Norm(Sub(at, eye));
            var right = Norm(Cross(fwd, new[] { 0.0, 1, 0 }));
            var up = Cross(right, fwd);
            double tan = Math.Tan(fovDeg * Math.PI / 360);
            var light = Norm(new[] { 0.4, 0.8, 0.3 });
            var img = new byte[w * h * 3];
            var depth = new double[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double sx = (2 * (x + 0.5) / w - 1) * tan * w / h, sy = (1 - 2 * (y + 0.5) / h) * tan;
                    var d = Norm(new[] { fwd[0] + right[0] * sx + up[0] * sy, fwd[1] + right[1] * sx + up[1] * sy, fwd[2] + right[2] * sx + up[2] * sy });
                    double best = double.MaxValue;
                    double[] nrm = null;
                    bool piece = false;
                    // (walk the grid from where the ray enters it)
                    double tin = 0, tout = double.MaxValue;
                    for (int a = 0; a < 3; a++)
                    {
                        if (Math.Abs(d[a]) < 1e-12) { if (eye[a] < lo[a] || eye[a] > lo[a] + N * C) tout = -1; continue; }
                        double ta = (lo[a] - eye[a]) / d[a], tb = (lo[a] + N * C - eye[a]) / d[a];
                        if (ta > tb) (ta, tb) = (tb, ta);
                        tin = Math.Max(tin, ta); tout = Math.Min(tout, tb);
                    }
                    if (tin < tout)
                    {
                        var seen = new HashSet<int>();
                        for (double t = tin; t < tout && t < best; t += C * 0.5)
                        {
                            int cx = (int)Math.Floor((eye[0] + d[0] * t - lo[0]) / C), cy = (int)Math.Floor((eye[1] + d[1] * t - lo[1]) / C), cz = (int)Math.Floor((eye[2] + d[2] * t - lo[2]) / C);
                            for (int ox = -1; ox <= 1; ox++) for (int oy = -1; oy <= 1; oy++) for (int oz = -1; oz <= 1; oz++)
                            {
                                int gx = cx + ox, gy = cy + oy, gz = cz + oz;
                                if (gx < 0 || gy < 0 || gz < 0 || gx >= N || gy >= N || gz >= N) continue;
                                if (!cells.TryGetValue((gx * N + gy) * N + gz, out var list)) continue;
                                foreach (int i in list)
                                {
                                    if (!seen.Add(i)) continue;
                                    double tt = TriHit(tris[i], d);
                                    if (tt < best) { best = tt; nrm = Norm(Cross(Sub(tris[i].B, tris[i].A), Sub(tris[i].C, tris[i].A))); }
                                }
                            }
                        }
                    }
                    bool past = false;
                    foreach (var (b, bpast) in boxes)
                    {
                        double t0 = 1e-4, t1 = best;
                        int axis = -1;
                        bool hit = true;
                        for (int a = 0; a < 3 && hit; a++)
                        {
                            if (Math.Abs(d[a]) < 1e-12) { if (eye[a] < b[a] || eye[a] > b[a + 3]) hit = false; continue; }
                            double ta = (b[a] - eye[a]) / d[a], tb = (b[a + 3] - eye[a]) / d[a];
                            if (ta > tb) (ta, tb) = (tb, ta);
                            if (ta > t0) { t0 = ta; axis = a; }
                            if (tb < t1) t1 = tb;
                            if (t0 > t1) hit = false;
                        }
                        if (!hit || axis < 0 || t0 >= best) continue;
                        best = t0;
                        nrm = new double[3];
                        nrm[axis] = d[axis] > 0 ? -1 : 1;
                        piece = true;
                        past = bpast;
                    }
                    depth[y * w + x] = best;
                    int o = 3 * (y * w + x);
                    if (nrm == null) { img[o] = 30; img[o + 1] = 40; img[o + 2] = 70; continue; }
                    double lam = Math.Abs(Dot(nrm, light)) * 0.75 + 0.25;
                    if (piece && past) { img[o] = (byte)(255 * lam); img[o + 1] = (byte)(170 * lam); img[o + 2] = (byte)(20 * lam); }
                    else if (piece) { img[o] = (byte)(230 * lam); img[o + 1] = (byte)(60 * lam); img[o + 2] = (byte)(50 * lam); }
                    else { img[o] = (byte)(200 * lam); img[o + 1] = (byte)(195 * lam); img[o + 2] = (byte)(180 * lam); }
                }
            // (outlines where the depth jumps)
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    double c = depth[y * w + x];
                    if (c == double.MaxValue) continue;
                    double m = Math.Max(Math.Max(Math.Abs(depth[y * w + x - 1] - c), Math.Abs(depth[y * w + x + 1] - c)), Math.Max(Math.Abs(depth[(y - 1) * w + x] - c), Math.Abs(depth[(y + 1) * w + x] - c)));
                    if (m > 0.04 * c) { int o = 3 * (y * w + x); img[o] = (byte)(img[o] / 3); img[o + 1] = (byte)(img[o + 1] / 3); img[o + 2] = (byte)(img[o + 2] / 3); }
                }
            return Png(img, w, h);
        }

        static byte[] Png(byte[] rgb, int w, int h)
        {
            var raw = new MemoryStream();
            for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgb, y * w * 3, w * 3); }
            var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) zs.Write(raw.ToArray());
            var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            void Chunk(string type, byte[] data)
            {
                var len = BitConverter.GetBytes(data.Length); Array.Reverse(len); png.Write(len);
                var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
                png.Write(td);
                var crc = BitConverter.GetBytes(Crc32(td)); Array.Reverse(crc); png.Write(crc);
            }
            var hdr = new byte[13];
            var bw = BitConverter.GetBytes(w); Array.Reverse(bw); Array.Copy(bw, 0, hdr, 0, 4);
            var bh = BitConverter.GetBytes(h); Array.Reverse(bh); Array.Copy(bh, 0, hdr, 4, 4);
            hdr[8] = 8; hdr[9] = 2;
            Chunk("IHDR", hdr);
            Chunk("IDAT", z.ToArray());
            Chunk("IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return ~c;
        }
    }
}
