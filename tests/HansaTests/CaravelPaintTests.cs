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
            const string lid = "part:gun-starboard-4-lid";
            bool lidTheirs = file.PaintQuads?.ContainsKey(lid) == true;
            if (!lidTheirs && file.Groups.TryGetValue(lid, out var lq))
            {
                int bulwarks = Array.FindIndex(def.PaintZones, z => z.Code == "waist-bulwarks"), topsides = Array.FindIndex(def.PaintZones, z => z.Code == "topsides");
                file.PaintQuads[lid] = Runs(lq, q => (q[8] + q[16] + q[24] + q[32]) / 4 < 0 ? bulwarks : topsides);
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

            // ---- note 68: the lid's inside from the main deck, while it's shut; not where it was while it's open
            var n68 = Aim(new[] { 2.906, 2.818, 0.908 }, new[] { 0.1802, -0.4282, 0.8855 });
            var n68open = Aim(new[] { 2.906, 2.818, 0.908 }, new[] { 0.1802, -0.4282, 0.8855 }, g => g != lid);
            Console.WriteLine($"         note 68 ({(lidTheirs ? "the mesh's lid zones" : "lid zones made here")}): collision at {n68.Wall:0.000}, lid at {n68.T:0.000}: {n68.Now}; open: {n68open.Now}");
            bool openMissesLid = n68open.Now != "waist-bulwarks" || n68open.T > n68.T + 0.05;
            Check(n68.Now == "waist-bulwarks" && openMissesLid,
                $"note 68: a gun port lid's inside (its quads in a zone) painted from the main deck while it's shut ({n68.Now}), not where it was while it's open ({n68open.Now})");
            if (!lidTheirs) Console.WriteLine("         (the caravel's mesh has no paint zone on its gun port lids yet: tools/caravel paint_quads)");
        }
    }
}
