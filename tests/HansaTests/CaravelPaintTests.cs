using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.1.0-dev.8, caravel chalk notes 68, 71, 77) Painting the caravel: the crosshair on her bulwarks' inside planking
        /// from the main deck (note 77, where the user aimed), on a gun port lid's inside (note 68: the lid's quads in a zone
        /// as the mesh would give them, only while it's shut), and a painted vanilla block's texture keeping its quarter turn
        /// (note 71: a partition of planks laid up-down).
        ///   dotnet run --project tests/HansaTests -- paint
        /// </summary>
        static void CaravelPaint(string root)
        {
            Console.WriteLine("0.1.0-dev.8: caravel notes 68, 71, 77 (painting her)");

            // ---- note 71: a block's baked texture painted with its turn kept
            Check(ShipPaint.PaintedBaked("game:block/wood/planks/pine2@90", "red") == "hansa:block/paint/oak2++3~hansa:block/dye/red@90" &&
                  ShipPaint.PaintedBaked("game:block/wood/planks/oak1@90", null, "dmg-cracked") == "game:block/wood/planks/oak1++0~hansa:block/dmg-cracked@90" &&
                  ShipPaint.PaintedBaked("game:block/wood/planks/oak3", "blue", "dmg-cracked") == "hansa:block/paint/oak3++3~hansa:block/dye/blue++0~hansa:block/dmg-cracked" &&
                  ShipPaint.PaintedBaked("game:block/wood/debarked/oak@270", "white") == "hansa:block/paint/timber++3~hansa:block/dye/white@270" &&
                  ShipPaint.PaintedBaked("game:block/wood/planks/oak1@90", null) == "game:block/wood/planks/oak1@90",
                "note 71: a vanilla block's texture as baked (planks laid up-down: \"@90\") painted and cracked with its quarter turn kept, any wood's planks oak's grain");

            // (HANSA_TEST_CARAVEL_ROOT: another checkout's caravel, read in place: a new export checked before it's here)
            root = Environment.GetEnvironmentVariable("HANSA_TEST_CARAVEL_ROOT") is string other && other.Length > 0 ? other : root;
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", "caravel.json");
            string meshPath = Path.Combine(root, "assets", "hansa", "config", "shipmeshes", "caravel.json");
            if (!File.Exists(path) || !File.Exists(meshPath)) { Console.WriteLine("  --   no caravel"); return; }
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            var file = JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(meshPath));
            var shapes = new Dictionary<string, Dictionary<string, double[]>>();
            foreach (var (role, loc) in new[] { ("exterior", def.Shapes?.Exterior), ("interior", def.Shapes?.Interior) })
            {
                if (loc == null) continue;
                var al = new AssetLocation(loc);
                string sp = Path.Combine(root, "assets", al.Domain, "shapes", al.Path + ".json");
                if (File.Exists(sp)) shapes[role] = PaintPlan.ShapeBoxes(File.ReadAllText(sp));
            }

            // ---- note 68: the lid's quads in a zone (the mesh's own if it has them; else here: inside faces as the bulwarks'
            // inside, outside faces as her topsides)
            // (every gun port lid of hers, port and starboard: her layout numbers her guns)
            var lids = file.Groups.Keys.Where(g => g.StartsWith("part:gun-") && g.EndsWith("-lid")).OrderBy(g => g).ToList();
            bool lidTheirs = lids.Count > 0 && lids.All(g => file.PaintQuads?.ContainsKey(g) == true);
            foreach (string g in lids.Where(g => file.PaintQuads?.ContainsKey(g) != true))
            {
                int bulwarks = Array.FindIndex(def.PaintZones, z => z.Code == "waist-bulwarks"), topsides = Array.FindIndex(def.PaintZones, z => z.Code == "topsides");
                double side = g.Contains("starboard") ? 1 : -1;
                file.PaintQuads[g] = Runs(file.Groups[g], q => (q[8] + q[16] + q[24] + q[32]) / 4 * side < 0 ? bulwarks : topsides);
            }
            var plan = PaintPlan.Build(def, file, shapes);

            string Zone(int z) => z < 0 ? "-" : plan.Zones[z].Code;
            (string Old, string Now, double Wall, double T) Aim(double[] eye, double[] look, System.Func<string, bool> partShut = null)
            {
                var o = new Vec3d(eye[0], eye[1], eye[2]);
                var d = new Vec3d(look[0], look[1], look[2]);
                d.Normalize();
                const double range = 5;
                double wall = def.Grid.Raycast(o, d, range + 1);
                int old = plan.Pick(o, d, Math.Min(range, wall + 0.4), out _, null, null, partShut);
                int now = plan.Pick(o, d, PaintPlan.Reach(def.Grid, o, d, range, wall), out double t, null, null, partShut);
                return (Zone(old), Zone(now), wall, t);
            }

            // ---- note 77: from the main deck at the starboard bulwark amidships (station 4, band 4), the user's own aim
            var n77 = Aim(new[] { -2.544, 2.823, 0.936 }, new[] { 0.0929, -0.4697, 0.8779 });
            Console.WriteLine($"         note 77: collision at {n77.Wall:0.000}, planking at {n77.T:0.000}: till dev.8 {n77.Old}, now {n77.Now}");
            Check(n77.Old == "-" && n77.Now == "waist-bulwarks",
                $"note 77: the crosshair on her bulwarks' inside from the main deck finds them through her collision's solid layer ({n77.Wall:0.00} → {n77.T:0.00}: {n77.Now})");

            // ---- her inside elsewhere: rays all round from spots on her main deck and in her hold, at her sides
            {
                int rays = 0, found = 0, foundOld = 0;
                // (the hold's floor under her middle: the first floor a man stands on under the main deck's)
                double hold = double.NaN;
                foreach (double hz in new[] { 0.0, 0.5, -0.5, 1.0, -1.0 })
                    for (double y = 0.6; y > def.KeelY && double.IsNaN(hold); y -= 0.0625)
                        if (DeckMover.HasFloor(def.Grid, 0, y, hz) &&
                            !def.Grid.Collides(-DeckMover.HalfWidth, y, hz - DeckMover.HalfWidth, DeckMover.HalfWidth, y + DeckMover.Height, hz + DeckMover.HalfWidth)) hold = y;
                // (a ray at -0.2 from the main deck passes over her rail)
                var eyes = new List<(double Y, double Look)> { (2.823, -0.47), (2.823, -0.3) };
                if (!double.IsNaN(hold)) eyes.AddRange(new[] { (hold + 1.7, -0.2), (hold + 1.7, 0.1) });
                foreach (double x in new[] { -6.0, -3.0, 0.0, 3.0, 6.0 })
                    foreach (double side in new[] { -1.0, 1.0 })
                        foreach (var (y, look) in eyes)
                        {
                            var eye = new[] { x, y, 0.9 * side };
                            var dir = new[] { 0.0, look, 0.88 * side };
                            var a = Aim(eye, dir);
                            if (a.Wall > 5) continue;
                            rays++;
                            if (a.Now != "-") found++;
                            else Console.WriteLine($"           none: from ({x}, {y:0.00}, {0.9 * side}) looking {look} {(side > 0 ? "starboard" : "port")}: her collision at {a.Wall:0.00}");
                            if (a.Old != "-") foundOld++;
                        }
                Console.WriteLine($"         her sides from inside (main deck; hold floor at {hold:0.00}): {found} of {rays} rays find a zone (till dev.8: {foundOld})");
                Check(rays > 0 && found == rays, $"her sides from inside (main deck, hold): a zone in the crosshair for every ray at them ({found}/{rays}, was {foundOld})");
            }
            // ... and through her deck from above: still her deck (no zone), not the hold's ceiling under it
            {
                var down = Aim(new[] { -2.5, 2.8, 0.0 }, new[] { 0.2, -1, 0.1 });
                Check(down.Now == "-", $"through her main deck from above: her deck (no zone), not what's under it ({down.Now})");
            }

            // ---- note 68: the lid's inside from the main deck, while it's shut; not where it was while it's open. Told apart by
            // where the aim lands, not by the zone: the bulwark behind the lid is the same zone as the lid's inside. Aimed
            // at the middle of each lid's largest inboard face, from about eye height on its deck, its side of the middle line
            var wrong68 = new List<string>();
            int waist68 = 0, castle68 = 0;
            foreach (string lid in lids)
            {
                var lidQuads = file.Groups[lid];
                double side = lid.Contains("starboard") ? 1 : -1;
                var face = lidQuads.Where(q => (q[8] + q[16] + q[24] + q[32]) / 4 * side < -0.5).OrderByDescending(q => QuadArea(q)).FirstOrDefault();
                if (face == null) { wrong68.Add($"{lid}: no inboard face"); continue; }
                var at = new[] { (face[1] + face[9] + face[17] + face[25]) / 4.0, (face[2] + face[10] + face[18] + face[26]) / 4.0, (face[3] + face[11] + face[19] + face[27]) / 4.0 };
                // (its upper part, over the gun's barrel through the port)
                double topY = Math.Max(Math.Max(face[2], face[10]), Math.Max(face[18], face[26]));
                at[1] += 0.6 * (topY - at[1]);
                // (from the deck it's on: her waist's guns from the main deck, her castle's from its own)
                var eye68 = new[] { at[0] - 0.3, at[1] + 1.0, 0.9 * side };
                var look68 = new[] { at[0] - eye68[0], at[1] - eye68[1], at[2] - eye68[2] };
                var shut = Aim(eye68, look68);
                var open = Aim(eye68, look68, g => g != lid);
                bool OnLid(double t)
                {
                    if (t > 100) return false;
                    double l = Math.Sqrt(look68[0] * look68[0] + look68[1] * look68[1] + look68[2] * look68[2]);
                    var hp = new[] { eye68[0] + look68[0] / l * t, eye68[1] + look68[1] / l * t, eye68[2] + look68[2] / l * t };
                    return lidQuads.Any(q => OnQuad(q, hp, 0.01));
                }
                bool shutOnLid = OnLid(shut.T), openOnLid = OnLid(open.T);
                string Dist(double t) => t > 100 ? "nothing" : t.ToString("0.000");
                Console.WriteLine($"         note 68, {lid.Substring(5)} at ({at[0]:0.00}, {at[1]:0.00}, {at[2]:0.00}): shut {shut.Now} at {Dist(shut.T)} (on the lid: {shutOnLid}); " +
                    $"open {open.Now} at {Dist(open.T)} (on the lid: {openOnLid})");
                // (note 68 is a waist gun's lid: the lids over her main deck are checked; a castle's, older exports' only, shown)
                if (at[1] > def.DeckY + 2) { castle68++; continue; }
                waist68++;
                if (!(shut.Now == "waist-bulwarks" && shutOnLid && !openOnLid)) wrong68.Add(lid.Substring(5));
            }
            Check(waist68 > 0 && wrong68.Count == 0,
                $"note 68: each gun port lid's inside over her main deck ({waist68}; {castle68} in a castle not checked) painted from the main deck while it's shut (the aim on the lid, the bulwarks' inside), " +
                $"not where it was while it's open (the aim past it) ({(wrong68.Count == 0 ? "all" : "not: " + string.Join(", ", wrong68))})");
            if (!lidTheirs) Console.WriteLine("         (the caravel's mesh has no paint zone on its gun port lids yet: tools/caravel paint_quads)");
        }

        /// <summary>Is a point on a quad (within tol of one of its two triangles' planes, inside it)?</summary>
        static bool OnQuad(float[] q, double[] p, double tol)
        {
            bool OnTri(int a, int b, int c)
            {
                double[] A = { q[1 + 8 * a], q[2 + 8 * a], q[3 + 8 * a] }, B = { q[1 + 8 * b], q[2 + 8 * b], q[3 + 8 * b] }, C = { q[1 + 8 * c], q[2 + 8 * c], q[3 + 8 * c] };
                double[] u = { B[0] - A[0], B[1] - A[1], B[2] - A[2] }, v = { C[0] - A[0], C[1] - A[1], C[2] - A[2] }, w = { p[0] - A[0], p[1] - A[1], p[2] - A[2] };
                double[] n = { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
                double nl = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
                if (nl < 1e-12 || Math.Abs(w[0] * n[0] + w[1] * n[1] + w[2] * n[2]) / nl > tol) return false;
                double uu = u[0] * u[0] + u[1] * u[1] + u[2] * u[2], vv = v[0] * v[0] + v[1] * v[1] + v[2] * v[2], uv = u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
                double wu = w[0] * u[0] + w[1] * u[1] + w[2] * u[2], wv = w[0] * v[0] + w[1] * v[1] + w[2] * v[2], den = uu * vv - uv * uv;
                if (Math.Abs(den) < 1e-18) return false;
                double s1 = (vv * wu - uv * wv) / den, t1 = (uu * wv - uv * wu) / den, e = 1e-3;
                return s1 >= -e && t1 >= -e && s1 + t1 <= 1 + e;
            }
            return OnTri(0, 1, 2) || OnTri(0, 2, 3);
        }

        /// <summary>
        /// (0.1.0-dev.9, caravel chalk note 101: "cannot add copper plates to the hull bottom. Also hull below water line + some
        /// extra should not be paintable. Above water line we should be able to paint hull in block level stripes.") What
        /// sheathes her bottom (copper plates, copper sheets two a plate); her topsides painted by rows (the design's topsides
        /// zone with "rows", as her export is asked to write it): a zone each block row from the second row over the
        /// waterline's, none under it, from the user's own aim at her bottom; a zone painted whole before, split into its rows.
        ///   dotnet run --project tests/HansaTests -- paint
        /// </summary>
        static void CaravelNote101(string root)
        {
            Console.WriteLine("0.1.0-dev.9: caravel note 101 (copper on her bottom; paint by rows over the waterline)");
            int Per(string code) => ShipFittings.CladdingItemsPerPlate(new ItemStack(new Item { Code = new AssetLocation(code) }));
            Check(Per("game:metalplate-copper") == 1 && Per("game:metalsheet-copper-down") == 2 && Per("game:metalsheet-copper-north") == 2 &&
                  Per("game:metalplate-iron") == 0 && Per("game:metalsheet-iron-down") == 0 && Per("game:ingot-copper") == 0 && Per("hansa:metalplate-copper") == 0,
                "note 101: her bottom sheathed with copper plates or vanilla's copper sheets (two a plate: a sheet is one ingot, a plate two), nothing else");

            // (HANSA_TEST_CARAVEL_ROOT: another checkout's caravel, read in place: a new export checked before it's here)
            root = Environment.GetEnvironmentVariable("HANSA_TEST_CARAVEL_ROOT") is string other && other.Length > 0 ? other : root;
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", "caravel.json");
            string meshPath = Path.Combine(root, "assets", "hansa", "config", "shipmeshes", "caravel.json");
            if (!File.Exists(path) || !File.Exists(meshPath)) { Console.WriteLine("  --   no caravel"); return; }
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            var file = JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(meshPath));
            var topsides = def.PaintZones.First(z => z.Code == "topsides");
            bool theirs = topsides.Rows;
            var asBuilt = PaintPlan.Build(def, file, null);
            topsides.Rows = true;
            var plan = PaintPlan.Build(def, file, null);
            var rows = plan.Zones.Where(z => z.Parent == "topsides").OrderBy(z => z.Band).ToList();
            Console.WriteLine($"         {(theirs ? "her design's" : "with \"rows\" set here on")} topsides: floor band {plan.RowFloor} (waterline {def.Waterline} in band {plan.RowOf(def.Waterline)}); " +
                string.Join(", ", rows.Select(z => $"band {z.Band} {plan.Area[plan.ZoneIndex(z.Code)]:0.0} m²")));
            Check(plan.RowOf(def.Waterline) == 2 && plan.RowFloor == 3 && plan.RowOf(1.2) == 4 && plan.RowOf(def.DeckY + 1e-3) == 4,
                "her rows are her hull slots' bands: the waterline in band 2, the margin band 3 (to her main deck's height), band 4 the first painted");
            int ti = plan.ZoneIndex("topsides");
            Check(rows.Count >= 2 && rows.All(z => z.Band > plan.RowFloor && plan.Area[plan.ZoneIndex(z.Code)] > 0) && plan.Area[ti] == 0,
                $"her topsides a zone each band over the margin ({rows.Count}: {string.Join(", ", rows.Select(z => z.Band))}), the whole zone none of its own");
            // nothing at or under the margin's band in them (her bottom's panels least of all)
            var low = new List<string>();
            foreach (var (g, zs) in plan.QuadZones)
                for (int q = 0; q < zs.Length; q++)
                {
                    if (zs[q] < 0 || plan.Zones[zs[q]].Parent != "topsides") continue;
                    var quad = file.Groups[g][q];
                    double top = Math.Max(Math.Max(quad[2], quad[10]), Math.Max(quad[18], quad[26]));
                    if (top < def.DeckY - 0.01 || g.Contains("bottom")) low.Add($"{g} q{q} (top {top:0.00})");
                }
            Check(low.Count == 0, $"no quad of her topsides' rows under band 4 or on her bottom ({low.Count}: {string.Join("; ", low.Take(3))})");
            bool bottomZoned = asBuilt.QuadZones.TryGetValue("slot:hull:bottom", out var bz) && bz.Any(z => z >= 0 && asBuilt.Zones[z].Code == "topsides");
            Check(!plan.QuadZones.TryGetValue("slot:hull:bottom", out var bz2) || bz2.All(z => z < 0 || plan.Zones[z].Parent != "topsides"),
                $"her bottom's panels (slot:hull:bottom) in no band of her topsides (her mesh puts them in topsides as a whole: {bottomZoned})");

            // the crosshair: the user's own aim at her bottom from the water (station 4, band 2) finds no zone; at her side
            // over the margin, its band
            string Aim(double[] eye, double[] look)
            {
                var o = new Vec3d(eye[0], eye[1], eye[2]);
                var d = new Vec3d(look[0], look[1], look[2]);
                d.Normalize();
                double wall = def.Grid.Raycast(o, d, 10);
                int z = plan.Pick(o, d, PaintPlan.Reach(def.Grid, o, d, 10, wall), out _);
                return z < 0 ? "-" : plan.Zones[z].Code;
            }
            string bottom = Aim(new[] { -6.512, 1.274, 8.111 }, new[] { 0.1615, -0.3746, -0.9130 });
            string side = Aim(new[] { -6.0, 1.6, 8.0 }, new[] { 0, 0, -1.0 });
            Console.WriteLine($"         from the water: at her bottom (note 101) {bottom}; at her side at 1.6 {side}");
            Check(bottom == "-" && side == "topsides-band4", $"from the water: her bottom (note 101's aim) no zone ({bottom}); her side over the margin its band's ({side})");

            // painted whole before: each row that colour until a row is painted itself
            var st = new ShipPaintState();
            st.Zones["topsides"] = "red";
            int b4 = plan.ZoneIndex("topsides-band4");
            Check(plan.PaintOf(st, b4) == "red" && plan.SplitRows(st) && st.Of("topsides") == null && rows.All(z => st.Of(z.Code) == "red"),
                "her topsides painted whole before they were by rows: each band shows it, and painting a band splits it into its bands first");
            topsides.Rows = theirs;
        }
    }
}
