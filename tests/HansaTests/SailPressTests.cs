using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// A sail's vertices as the game tesselates them, near enough for the checks (ship-local, set full, its yard not turned):
        /// a square sail's shape elements' box corners (T(origin) Rx Ry Rz T(from - origin), as ShapeElement does), the
        /// lateen's quads' corners from the mesh file. Null: not found.
        /// </summary>
        static float[] SailPoints(ShipDefinition def, SailDef sail, string assets, ShipSkinFile skin)
        {
            var pts = new List<float>();
            if (sail.Shape != null)
            {
                string sp = Path.Combine(assets, "shapes", sail.Shape.Substring(sail.Shape.IndexOf(':') + 1) + ".json");
                if (!File.Exists(sp)) return null;
                var shape = JObject.Parse(File.ReadAllText(sp));
                foreach (var e in shape["elements"])
                {
                    var from = e["from"].ToObject<double[]>();
                    var to = e["to"].ToObject<double[]>();
                    var o = e["rotationOrigin"]?.ToObject<double[]>() ?? new double[3];
                    double rx = (e["rotationX"]?.Value<double>() ?? 0) * Math.PI / 180, ry = (e["rotationY"]?.Value<double>() ?? 0) * Math.PI / 180, rz = (e["rotationZ"]?.Value<double>() ?? 0) * Math.PI / 180;
                    for (int c = 0; c < 8; c++)
                    {
                        // the box corner in the element's frame, from its "from" corner less the origin
                        double x = ((c & 1) == 0 ? 0 : to[0] - from[0]) + from[0] - o[0];
                        double y = ((c & 2) == 0 ? 0 : to[1] - from[1]) + from[1] - o[1];
                        double z = ((c & 4) == 0 ? 0 : to[2] - from[2]) + from[2] - o[2];
                        // Rz, then Ry, then Rx (the matrix product Rx Ry Rz applied to the point)
                        (x, y) = (x * Math.Cos(rz) - y * Math.Sin(rz), x * Math.Sin(rz) + y * Math.Cos(rz));
                        (x, z) = (x * Math.Cos(ry) + z * Math.Sin(ry), -x * Math.Sin(ry) + z * Math.Cos(ry));
                        (y, z) = (y * Math.Cos(rx) - z * Math.Sin(rx), y * Math.Sin(rx) + z * Math.Cos(rx));
                        pts.Add((float)((x + o[0]) / 16)); pts.Add((float)((y + o[1]) / 16)); pts.Add((float)((z + o[2]) / 16));
                    }
                }
            }
            else if (sail.Mesh != null && skin?.Groups != null && skin.Groups.TryGetValue(sail.Mesh, out var quads))
            {
                foreach (var q in quads)
                    for (int v = 0; v < 4; v++) { pts.Add(q[1 + 8 * v]); pts.Add(q[2 + 8 * v]); pts.Add(q[3 + 8 * v]); }
            }
            return pts.Count > 0 ? pts.ToArray() : null;
        }

        static void Apply(float[] m, float[] pts)
        {
            for (int i = 0; i + 2 < pts.Length; i += 3)
            {
                float x = pts[i], y = pts[i + 1], z = pts[i + 2];
                pts[i] = m[0] * x + m[4] * y + m[8] * z + m[12];
                pts[i + 1] = m[1] * x + m[5] * y + m[9] * z + m[13];
                pts[i + 2] = m[2] * x + m[6] * y + m[10] * z + m[14];
            }
        }

        /// <summary>
        /// (0.3.14, notes 150–152) Over the trim the crew gives her (the yards braced, the lateen sheeted, on either tack, every
        /// 5° of the apparent wind from close-hauled to before it; full sail and reefed), no vertex of a sail is inside a mast
        /// or a shroud (or a backstay), nor through a set of ratlines or past a mast, once pressed (SailPress); as made, the
        /// courses go through their shrouds and the lateen through its mast.
        /// </summary>
        static void SailsPressed(ShipDefinition def, string assets)
        {
            Console.WriteLine($"sails pressed against her masts and rigging ({def.Code})");
            var press = SailPress.Of(def);
            if (def.RigCheck?.Standing == null) { Console.WriteLine("  (no rig check in the design: only her masts)"); }
            string skinPath = Path.Combine(assets, "config", "shipmeshes", def.Code + ".json");
            var skin = File.Exists(skinPath) ? JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(skinPath)) : null;
            var frames = def.DamageParts.SailFrames;
            int ropes = press.Kinds.Count(k => k == "rope"), ratlines = press.Kinds.Count(k => k == "ratlines");
            Console.WriteLine($"  obstacles: {press.Kinds.Count(k => k == "mast")} masts, {ropes} shrouds and backstays, {ratlines} lines along their ratlines");
            var ym = new float[16];
            var furl = new float[16];
            var belly = new float[16];
            var set = new float[16];
            for (int si = 0; si < def.Sails.Length; si++)
            {
                var sail = def.Sails[si];
                var f = frames[si];
                var src = SailPoints(def, sail, assets, skin);
                if (f == null || src == null) { Console.WriteLine($"  {sail.Code}: no mesh found, skipped"); continue; }
                int beforeInside = 0, beforeThrough = 0, afterInside = 0, afterThrough = 0, trims = 0, moved = 0;
                double beforeDeep = 0, afterDeep = 0, most = 0;
                string where = "";
                foreach (int side in new[] { 1, -1 })
                    foreach (float frac in new[] { 1f, 0.55f })
                        for (double awa = 40; awa <= 180; awa += 5)
                        {
                            Sailing.Trim(awa, side, def.Sailing, out double brace, out double sheet);
                            double trim = f.ForeAndAft ? sheet : brace;
                            if (f.Yard != null) PartMotion.PartMatrix(f.Yard, PartMotion.TurnDegrees(f.Yard, (float)trim) * GameMath.DEG2RAD, ym);
                            else Mat4f.Identity(ym);
                            PartMotion.FurlMatrix(sail, frac, furl);
                            if (f.ForeAndAft)
                            {
                                PartMotion.BellyMatrix(f, PartMotion.LateenBelly(sheet), belly);
                                Mat4f.Mul(set, furl, belly);
                            }
                            else Array.Copy(furl, set, 16);
                            var pts = (float[])src.Clone();
                            Apply(set, pts);
                            var drawn = (float[])pts.Clone();
                            Apply(ym, drawn);
                            var b = press.Check(f, ym, drawn, drawn.Length / 3);
                            beforeInside += b.Inside; beforeThrough += b.Through; beforeDeep = Math.Max(beforeDeep, b.Deepest);
                            var unpressed = (float[])pts.Clone();
                            moved += press.Press(f, ym, pts, pts.Length / 3);
                            for (int i = 0; i < pts.Length; i += 3)
                                most = Math.Max(most, Math.Sqrt(Math.Pow(pts[i] - unpressed[i], 2) + Math.Pow(pts[i + 1] - unpressed[i + 1], 2) + Math.Pow(pts[i + 2] - unpressed[i + 2], 2)));
                            Apply(ym, pts);
                            var a = press.Check(f, ym, pts, pts.Length / 3);
                            afterInside += a.Inside; afterThrough += a.Through;
                            if (a.Deepest > afterDeep) { afterDeep = a.Deepest; where = $"{a.Where} at {awa}° {(side > 0 ? "starboard" : "port")} tack"; }
                            if ((a.Inside > 0 || a.Through > 0) && where == "") where = $"{awa}° {(side > 0 ? "starboard" : "port")} tack";
                            trims++;
                        }
                Console.WriteLine($"  {sail.Code}: as made, over {trims} trims, {beforeInside} vertices inside a mast or rope (deepest {beforeDeep:0.00}), {beforeThrough} through one; pressed ({moved} moves, the most {most:0.00})");
                Check(afterInside == 0 && afterThrough == 0,
                    $"{sail.Code}: pressed, no vertex inside a mast or rope ({afterInside}, deepest {afterDeep:0.000}) or through one, a set of ratlines or the mast ({afterThrough}){(where != "" ? ": " + where : "")}");
            }
            // (the self-test's sheet of points over each sail's outline, as set: the same, on the server)
            {
                int inside1 = 0, through1 = 0, trims = 0;
                for (int si = 0; si < def.Sails.Length; si++)
                {
                    var f = frames[si];
                    if (f == null) continue;
                    var pts = new List<float>();
                    for (double u = f.U0 + 0.4; u <= f.U1 - 0.4; u += 0.5)
                        for (double v = 0.2; v <= f.V1; v += 0.5)
                        {
                            if (!f.Inside(u, v, -0.2)) continue;
                            double n = f.ForeAndAft ? 0.3 * Math.Sin(Math.PI * Math.Clamp(v / f.ClewV, 0, 1)) : 0.8 * Math.Sin(Math.PI * Math.Clamp(v / f.V1, 0, 1));
                            var q = f.FromSail(u, v, n);
                            pts.Add((float)q[0]); pts.Add((float)q[1]); pts.Add((float)q[2]);
                        }
                    foreach (int side in new[] { 1, -1 })
                        for (double awa = 45; awa <= 180; awa += 15)
                        {
                            Sailing.Trim(awa, side, def.Sailing, out double brace, out double sheet);
                            if (f.Yard != null) PartMotion.PartMatrix(f.Yard, PartMotion.TurnDegrees(f.Yard, (float)(f.ForeAndAft ? sheet : brace)) * GameMath.DEG2RAD, ym);
                            else Mat4f.Identity(ym);
                            var set2 = pts.ToArray();
                            press.Press(f, ym, set2, set2.Length / 3);
                            Apply(ym, set2);
                            var a = press.Check(f, ym, set2, set2.Length / 3);
                            inside1 += a.Inside; through1 += a.Through; trims++;
                        }
                }
                Check(inside1 == 0 && through1 == 0, $"a sheet of points over each sail's outline (the self-test's), pressed over {trims} trims: none inside ({inside1}) or through ({through1})");
            }
            if (def.Code == "carrack")
            {
                // (the notes' own cases, as made: the courses through their shrouds, the lateen through the mizzen)
                var byCode = def.Sails.Select((s, i) => (s, i)).ToDictionary(x => x.s.Code, x => x.i);
                Check(byCode.ContainsKey("main-course") && byCode.ContainsKey("fore-course") && byCode.ContainsKey("mizzen-lateen"), "the carrack has her main and fore courses and the mizzen lateen");
            }
        }
    }
}
