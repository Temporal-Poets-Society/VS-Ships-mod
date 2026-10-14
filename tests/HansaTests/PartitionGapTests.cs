using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        static ShipSurfaces surfacesCache;
        static string surfacesFor;

        /// <summary>(0.3.14) Her surfaces as the renderer has them: the mesh file's interior and inside planking, her shapes' faces.</summary>
        static ShipSurfaces SurfacesOf(ShipDefinition def, string assets)
        {
            if (surfacesFor == def.Code && surfacesCache != null) return surfacesCache;
            string p = Path.Combine(assets, "config", "shipmeshes", def.Code + ".json");
            if (!File.Exists(p)) return null;
            var s = ShipSurfaces.From(JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(p)));
            foreach (var shapeLoc in new[] { def.Shapes?.Interior, def.Shapes?.Exterior })
            {
                if (shapeLoc == null) continue;
                string sp = Path.Combine(assets, "shapes", shapeLoc.Substring(shapeLoc.IndexOf(':') + 1) + ".json");
                if (!File.Exists(sp)) continue;
                var shape = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(sp));
                foreach (var e in shape["elements"])
                {
                    var faces = (e["faces"] as Newtonsoft.Json.Linq.JObject)?.Properties().Select(pr => pr.Name).ToList();
                    if (faces == null || e["rotationX"] != null || e["rotationY"] != null || e["rotationZ"] != null) continue;
                    s.AddBoxFaces(e["from"].ToObject<double[]>(), e["to"].ToObject<double[]>(), faces);
                }
            }
            surfacesCache = s;
            surfacesFor = def.Code;
            return s;
        }

        /// <summary>An ascii map of a border place and round it: '#' her cell, '.' drawn, '+' drawn on her cell, ' ' neither.</summary>
        static void DumpPlace(ShipDefinition def, MountPoint m, List<double[]> drawn)
        {
            var g = def.Grid;
            var (line, alongX) = MountLayout.BorderLine(m);
            double layer = line + (m.In < 2 ? 1 : -1) * g.V / 2, along0 = (alongX ? m.X : m.Z) - 0.5;
            int n = 32, pad = 16;
            Console.WriteLine($"    {m.Id} line {line} along {(alongX ? "x" : "z")} {along0}..{along0 + 1} y {m.Y}..{m.Y + 1}");
            for (int b = n + pad - 1; b >= -pad; b--)
            {
                var sb = new System.Text.StringBuilder("      ");
                for (int a = -pad; a < n + pad; a++)
                {
                    if (a == 0 || a == n) sb.Append('|');
                    double al = along0 + (a + 0.5) / n, y = m.Y + (b + 0.5) / n;
                    bool solid = alongX ? g.SolidAt(al, y, layer) : g.SolidAt(layer, y, al);
                    bool d = drawn != null && drawn.Any(r => al > r[0] && al < r[2] && y > r[1] && y < r[3]);
                    sb.Append(d ? (solid ? '+' : '.') : solid ? '#' : ' ');
                }
                Console.WriteLine(sb);
            }
        }

        /// <summary>
        /// (0.3.14, notes 148, 149) The slits a border place's drawn partition leaves to her: each edge of what's drawn (1/64
        /// cells) facing one of her cells, in open air (no surface of hers just behind it: not inside a timber it reaches into),
        /// measured to her nearest surface that way within her cells, at either face of the partition; where none of her
        /// surfaces is there (a deck drawn on her cells), to her cell's face. Past the place's block only where no other place
        /// on the line is (that one's partition covers it). Slits over 0.3 aren't counted (a fixture's collision, drawn by its
        /// own mesh). (most, edges, slits over 0.02, where the most is)
        /// </summary>
        static (double Max, int Edges, int Over, string Worst) DrawnGaps(ShipDefinition def, ShipSurfaces s, MountPoint m, List<double[]> drawn, int[] hist = null)
        {
            var g = def.Grid;
            var (line, alongX) = MountLayout.BorderLine(m);
            double layer = line + (m.In < 2 ? 1 : -1) * g.V / 2;
            const int n = 64, pad = 66;
            int w = n + 2 * pad;
            double c = 1.0 / n, along0 = (alongX ? m.X : m.Z) - 0.5 - pad * c, y0 = m.Y - pad * c, h = MountLayout.PartitionThickness / 2 * 0.9;
            var cov = new bool[w, w];
            foreach (var r in drawn)
                for (int a = Math.Max(0, (int)Math.Floor((r[0] - along0) / c)); a < Math.Min(w, (int)Math.Ceiling((r[2] - along0) / c)); a++)
                    for (int b = Math.Max(0, (int)Math.Floor((r[1] - y0) / c)); b < Math.Min(w, (int)Math.Ceiling((r[3] - y0) / c)); b++)
                    {
                        double al = along0 + (a + 0.5) * c, y = y0 + (b + 0.5) * c;
                        if (al > r[0] && al < r[2] && y > r[1] && y < r[3]) cov[a, b] = true;
                    }
            bool Solid(double al, double y) => alongX ? g.SolidAt(al, y, layer) : g.SolidAt(layer, y, al);
            double Ray(double al, double y, double o, double da, double dy, double max) =>
                alongX ? s.Ray(al, y, o, da, dy, 0, max) : s.Ray(o, y, al, 0, dy, da, max);
            double worst = 0;
            int edges = 0, over = 0;
            string where = "";
            for (int a = 1; a < w - 1; a++)
                for (int b = 1; b < w - 1; b++)
                {
                    if (!cov[a, b]) continue;
                    foreach (var (da, db) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        if (cov[a + da, b + db]) continue;
                        double al = along0 + (a + 0.5 + da * 0.5) * c, y = y0 + (b + 0.5 + db * 0.5) * c;
                        if (!Solid(al + da * c * 0.5, y + db * c * 0.5)) continue;
                        double ua = al + da * c * 0.5 - ((alongX ? m.X : m.Z) - 0.5), uy = y + db * c * 0.5 - m.Y;
                        int ba = (int)Math.Floor(ua), by = (int)Math.Floor(uy);
                        if ((ba != 0 || by != 0) && (def.Mounts.BorderNext(m, ba, by) != null || ba != 0 && def.Mounts.BorderNext(m, ba, 0) != null ||
                                                     by != 0 && def.Mounts.BorderNext(m, 0, by) != null)) continue;
                        edges++;
                        double gap = 0;
                        foreach (double off in new[] { -h, h })
                        {
                            double back = Ray(al + da * 0.001, y + db * 0.001, line + off, -da, -db, 0.05);
                            if (back < double.MaxValue) continue;
                            double run = 0;
                            while (run < 1.05 && Solid(al + da * (run + 0.005), y + db * (run + 0.005))) run += 0.005;
                            double t = Ray(al, y, line + off, da, db, Math.Min(run + 0.01, MountLayout.DrawReach));
                            // (her surface slanting steeply past the edge: the slit is across, not along it)
                            foreach (int side in new[] { -1, 1 })
                            {
                                double sa = da + side * db, sy = db + side * da, k = Math.Sqrt(0.5);
                                double td = Ray(al, y, line + off, sa * k, sy * k, 0.3);
                                if (td < t && Solid(al + sa * k * Math.Min(td, 0.02), y + sy * k * Math.Min(td, 0.02))) t = td;
                            }
                            if (t == double.MaxValue)
                            {
                                double v = da != 0 ? (alongX ? al - g.OX : al - g.OZ) : y - g.OY;
                                t = Math.Abs(v / g.V - Math.Round(v / g.V)) * g.V;
                            }
                            gap = Math.Max(gap, t);
                        }
                        if (hist != null) hist[gap <= 0.02 ? 0 : gap <= 0.05 ? 1 : gap <= 0.1 ? 2 : gap <= 0.3 ? 3 : 4]++;
                        if (gap > 0.3) continue;
                        if (gap > 0.02) over++;
                        if (gap > worst) { worst = gap; where = $"{(alongX ? "x" : "z")} {al:0.###} y {y:0.###} way ({da},{db})"; }
                    }
                }
            return (worst, edges, over, where);
        }

        /// <summary>
        /// (0.3.14, note 147: "above doors cannot place partition wall fragments. When pointing at the sides of other walls
        /// placement does not work. Also facing a beam it does not work and does not highlight") A partition goes up over a
        /// door; the crosshair near a partition's edge picks the free place past it; on a beam over a cut place, that place.
        /// </summary>
        static void PartitionPicks(ShipDefinition def)
        {
            var l = def.Mounts;
            var planks = new FakePiece { Code = "game:planks-oak-hor", Family = "game:planks-oak", Planks = true };
            var doorBlock = new FakePiece { Code = "game:door-solid-oak", Family = "game:door-solid-oak", Door = true };
            // over a door: a row 0 place with a whole row 1 over it and a top row over that
            MountPoint at = null, top = null;
            foreach (var b in l.Borders.Where(b => b.Row == 0 && b.Clip == null && b.Room == "lowerdeck"))
            {
                var up = l.BorderAbove(b);
                var t = up == null ? null : l.BorderNext(up, 0, 1);
                if (up?.Clip == null && t?.Under == up?.Id && t != null) { at = b; top = t; break; }
            }
            if (at == null) { Check(false, "a border place with room for a door and a top row over it"); return; }
            var set = new ShipPieceSet();
            var doorPlan = PiecePlanner.Plan(l, at, doorBlock, at.In);
            Check(doorPlan.Error == null, $"a door goes on {at.Id} ({doorPlan.Error})");
            if (doorPlan.Error == null)
            {
                set.Add(ShipPiece.From(doorPlan, set.NextId, null));
                var over = PiecePlanner.Plan(l, top, planks, top.In);
                bool ok = over.Error == null && set.Supports(l, top) && set.Blocking(over) == null;
                Check(ok, $"note 147: over the door on {at.Id} a partition goes on {top.Id} (supported {set.Supports(l, top)}, blocked by {set.Blocking(over)?.Role.ToString() ?? "nothing"}, {over.Error})");
            }
            // from a partition's side: a partition on a place, the crosshair near its end (and on its thin end) picks the next one
            MountPoint a0 = null, a1 = null;
            foreach (var b in l.Borders.Where(b => b.Row == 0 && b.Clip == null && b.Room == "lowerdeck"))
                if (l.BorderNext(b, 1, 0) is MountPoint nx && nx.Clip == null) { a0 = b; a1 = nx; break; }
            if (a0 != null)
            {
                var s2 = new ShipPieceSet();
                var plan = PiecePlanner.Plan(l, a0, planks, a0.In);
                s2.Add(ShipPiece.From(plan, s2.NextId, null));
                var (_, ax) = MountLayout.BorderLine(a0);
                double mid = ax ? a0.X : a0.Z;
                bool Free(MountPoint x) => !s2.Taken(x.Id) && s2.Supports(l, x);
                var nearEnd = l.NextToPiece(a0, 1, mid + 0.42, a0.Y + 0.5, Free);
                var onEnd = l.NextToPiece(a0, 1, mid + 0.5, a0.Y + 0.5, Free);
                var middle = l.NextToPiece(a0, 1, mid, a0.Y + 0.5, Free);
                var nearTop = l.NextToPiece(a0, 1, mid, a0.Y + 0.9, Free);
                Check(nearEnd?.Id == a1.Id && onEnd?.Id == a1.Id && middle == null && nearTop?.Id == l.BorderAbove(a0)?.Id,
                    $"note 147: the crosshair near the end of a partition on {a0.Id} picks {nearEnd?.Id ?? "nothing"} (on its end {onEnd?.Id ?? "nothing"}; its middle {middle?.Id ?? "nothing"}; near its top {nearTop?.Id ?? "nothing"})");
            }
            else Check(false, "two border places side by side");
            // facing a beam: a top-row place cut under a beam (its clip lower than its block's top), the crosshair on the beam's
            // side just over it
            var cutTop = l.Borders.Where(b => b.Room == "lowerdeck" && b.Row == 2 && b.Clip != null && b.Clip.Max(c => c[3]) < b.Y + 0.5).ToList();
            int found = 0, tried = 0;
            foreach (var b in cutTop.Take(40))
            {
                var (line, ax) = MountLayout.BorderLine(b);
                double clipTop = b.Clip.Max(c => c[3]), mid = ax ? b.X : b.Z;
                // from the anchor side, level, a little over the cut, square to the line
                double inS = b.In < 2 ? 1 : -1, y = clipTop + 0.15;
                var o = ax ? new Vintagestory.API.MathTools.Vec3d(mid, y, line + inS * 0.75) : new Vintagestory.API.MathTools.Vec3d(line + inS * 0.75, y, mid);
                var d = ax ? new Vintagestory.API.MathTools.Vec3d(0, 0, -inS) : new Vintagestory.API.MathTools.Vec3d(-inS, 0, 0);
                double wall = def.Grid.Raycast(o, d, 3);
                if (wall > 1 || wall < 0.05) continue;
                tried++;
                var p = l.NearTimber(o.X + d.X * wall, o.Y + d.Y * wall, o.Z + d.Z * wall, x => x.Kind == MountKind.Border);
                if (p?.Id == b.Id) found++;
            }
            Check(tried > 5 && found == tried, $"note 147: on the beam over a top-row place cut under it the crosshair finds that place ({found} of {tried})");
        }

        /// <summary>
        /// (0.3.14, notes 147–149) Partitions meet her: drawn out to her real surfaces (a beam's sides and underside, her
        /// ceiling planking) with no slit over 0.02; no place outside her side; the places by the user's spots; a partition
        /// over a door; the place next to a partition picked from its side; the place in front of a beam picked from the beam.
        /// </summary>
        static void PartitionsMeetHer(ShipDefinition def, string assets)
        {
            Console.WriteLine($"partitions meet her timbers and planking ({def.Code})");
            var l = def.Mounts;
            var g = def.Grid;
            // no place's free part is outside her (0.3.13 made wedge places past her planking, in the air beside her)
            int outside = 0;
            foreach (var b in l.Borders.Where(b => b.Clip != null))
                foreach (var r in b.Clip)
                {
                    var (line, alongX) = MountLayout.BorderLine(b);
                    double al = (r[0] + r[2]) / 2, y = (r[1] + r[3]) / 2, lay = line + (b.In < 2 ? 1 : -1) * g.V / 2;
                    if (!(alongX ? l.InsideHer(al, y, lay) : l.InsideHer(lay, y, al))) { outside++; break; }
                }
            Check(outside == 0, $"no border place's free part is outside her side ({outside})");
            PartitionPicks(def);
            var s = SurfacesOf(def, assets);
            if (s == null) { Console.WriteLine("  (no mesh file: the drawn partitions aren't checked)"); return; }
            foreach (var id in (Environment.GetEnvironmentVariable("HANSA_DUMP_IDS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (l.Get(id) is MountPoint dm) DumpPlace(def, dm, l.DrawnClip(dm, s) ?? dm.Clip);
            var hist = new int[5];
            double w0 = 0, w1 = 0;
            int e0 = 0, o0 = 0, e1 = 0, o1 = 0;
            string ww = "";
            var worstPlaces = new List<(double, string)>();
            string only = Environment.GetEnvironmentVariable("HANSA_ROOM");
            foreach (var b in l.Borders)
            {
                if (only != null && b.Room != only) continue;
                // (0.3.17: the closed rooms' walls; the open decks' places, railings' mostly, are new and not drawn to her surfaces)
                if (b.Room.EndsWith("-deck")) continue;
                if (Environment.GetEnvironmentVariable("HANSA_ROW") is string rw && b.Row.ToString() != rw) continue;
                var (_, ax) = MountLayout.BorderLine(b);
                double mid = ax ? b.X : b.Z;
                var coarse = b.Clip ?? new List<double[]> { new[] { mid - 0.5, b.Y, mid + 0.5, b.Y + 1 } };
                var before = DrawnGaps(def, s, b, coarse);
                var after = DrawnGaps(def, s, b, l.DrawnClip(b, s) ?? coarse, hist);
                w0 = Math.Max(w0, before.Max); e0 += before.Edges; o0 += before.Over;
                e1 += after.Edges; o1 += after.Over;
                if (after.Max > w1) { w1 = after.Max; ww = b.Id + " at " + after.Worst; }
                if (after.Over > 0) worstPlaces.Add((after.Max, b.Id + " " + after.Worst));
            }
            Console.WriteLine($"  edges of partitions facing her cells: as cut to her cells {e0}, {o0} a slit over 0.02 from her (at most {w0:0.000}); drawn out to her surfaces {e1}");
            Console.WriteLine($"  drawn out, the slits: to 0.02 {hist[0]}, to 0.05 {hist[1]}, to 0.1 {hist[2]}, to 0.3 {hist[3]}, more (not hers) {hist[4]}");
            foreach (var (gm, id) in worstPlaces.OrderByDescending(x => x.Item1).Take(8)) Console.WriteLine($"    {gm:0.000} {id}");
            // (the slits left over: where her cells have no surface of her mesh or shapes in them, a fixture's collision or the
            // stem's deadwood, and staircases of her cells along a steeply slanting surface the rays run along)
            Check(only != null || o1 * 4 < o0, $"drawn out to her surfaces, slits over 0.02 to her: {o1} edges (cut to her cells: {o0})");
            // by the user's spots: between the lower deck's beams (note 148), at her curved side (note 149) (the carrack's)
            foreach (var (note, spot, rows) in new[] { (148, new[] { -6.48, 1.50, -0.75 }, new[] { 2 }), (149, new[] { -6.52, 0.54, -5.5 }, new[] { 0, 1, 2 }) })
            {
                if (def.Code != "carrack") break;
                var near = l.Borders.Where(b => Math.Abs(b.CX - spot[0]) < 1.6 && Math.Abs(b.CZ - spot[2]) < 1.6 && b.Room == "lowerdeck" && rows.Contains(b.Row)).ToList();
                int slits = 0, edges = 0;
                double most = 0;
                foreach (var b in near)
                {
                    var (_, ax) = MountLayout.BorderLine(b);
                    double mid = ax ? b.X : b.Z;
                    var r = DrawnGaps(def, s, b, l.DrawnClip(b, s) ?? b.Clip ?? new List<double[]> { new[] { mid - 0.5, b.Y, mid + 0.5, b.Y + 1 } });
                    slits += r.Over; edges += r.Edges; most = Math.Max(most, r.Max);
                }
                // (at her side, where the deck's edge or a knee meets her planking, the partition's faces see her surface at
                // different heights across its eighth of a block: a sliver up to 0.075 at one face there)
                double allowed = note == 148 ? 0.02 : 0.08;
                Check(near.Count > 3 && edges > 0 && most <= allowed, $"note {note}: by ({spot[0]}, {spot[1]}, {spot[2]}) {near.Count} places' partitions meet her ({edges} edges facing her cells, {slits} over 0.02, most {most:0.000}, allowed {allowed})");
            }
            // the whole place there runs on past its block to her ceiling (it ended at the cell under her planking)
            var side = l.Get("b:lower:-6:-6:w:1");
            var sd = side == null ? null : l.DrawnClip(side, s);
            if (def.Code == "carrack") Check(sd != null && sd.Min(r => r[0]) < -5.6, $"note 149: b:lower:-6:-6:w:1 is drawn out to her ceiling past its block (to z {sd?.Min(r => r[0]):0.000})");
        }

        /// <summary>
        /// (0.3.14) The second design, carrack-a (the companion door and stairs a block inboard: option A): its definition, its
        /// entity and charter, its mesh and shapes, and the general checks of this version (partitions, sails pressed). Its
        /// whole set as the carrack's: dotnet run -- design carrack-a.
        /// </summary>
        static void SecondDesign(string path, string root)
        {
            Console.WriteLine("======== carrack-a (general checks)");
            string assets = Path.Combine(root, "assets", "hansa");
            ShipDefinition def = null;
            try
            {
                def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
                def.Init();
            }
            catch (Exception e) { Console.WriteLine("  " + e.Message); }
            Check(def?.Grid != null && def.Code == "carrack-a", $"carrack-a's definition loads ({def?.Name})");
            if (def?.Grid == null) return;
            Check(def.Mounts.Floors.Any() && def.Mounts.Borders.Any() && def.Sails.Length > 0 && def.DamageParts.SailFrames.All(f => f != null),
                $"her places ({def.Mounts.Floors.Count()} floor, {def.Mounts.Borders.Count()} border) and her {def.Sails.Length} sails are worked out");
            string entity = Path.Combine(assets, "entities", "ship", "carrack-a.json");
            string deed = File.ReadAllText(Path.Combine(assets, "itemtypes", "shipdeed.json"));
            var lang = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Path.Combine(assets, "lang", "en.json")));
            Check(File.Exists(entity) && File.ReadAllText(entity).Contains("carrack-a") && deed.Contains("\"carrack-a\"") &&
                  lang.ContainsKey("entity-carrack-a") && lang.ContainsKey("item-shipdeed-carrack-a"),
                "her entity (ship: carrack-a), her charter (shipdeed-carrack-a) and their names are there");
            bool mesh = def.Shapes?.Skin != null && File.Exists(Path.Combine(assets, def.Shapes.Skin.Substring(def.Shapes.Skin.IndexOf(':') + 1)));
            bool shapes = new[] { def.Shapes?.Exterior, def.Shapes?.Interior }.All(sh => sh != null && File.Exists(Path.Combine(assets, "shapes", sh.Substring(sh.IndexOf(':') + 1) + ".json")));
            bool sails = def.Sails.All(sl => sl.Mesh != null || sl.Shape != null && File.Exists(Path.Combine(assets, "shapes", sl.Shape.Substring(sl.Shape.IndexOf(':') + 1) + ".json")));
            Check(mesh && shapes && sails, $"her mesh file ({def.Shapes?.Skin}), shapes and sails are there to draw her");
            PartitionsToHer(def);
            PartitionsMeetHer(def, assets);
            SailsPressed(def, assets);
        }
    }
}
