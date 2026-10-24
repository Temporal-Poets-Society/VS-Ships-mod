using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// A part of her a player paints in one go (the generator's paint zones): its code, what it's called, what kind of work
    /// it is (panel, band, moulding, carving, board, ceiling, room) and what it is as built (oak, tarred, timber, deck, gold).
    /// </summary>
    public class PaintZoneDef
    {
        public string Code, Name, Kind, Default;
    }

    /// <summary>
    /// How her zones are painted (WatchedAttributes "hansaPaint": zone code → a vanilla dye's colour, or "gold"; a zone
    /// not in it is as built). The rules: <see cref="ShipPaint"/>.
    /// </summary>
    public class ShipPaintState
    {
        public readonly SortedDictionary<string, string> Zones = new();

        public string Of(string zone) => zone != null && Zones.TryGetValue(zone, out var v) ? v : null;

        public void ToTree(ITreeAttribute t)
        {
            foreach (var kv in Zones) t.SetString(kv.Key, kv.Value);
        }

        public void FromTree(ITreeAttribute t)
        {
            Zones.Clear();
            if (t == null) return;
            foreach (var kv in t)
            {
                string v = (kv.Value as StringAttribute)?.value;
                if (!string.IsNullOrEmpty(v) && ShipPaint.Known(v)) Zones[kv.Key] = v;
            }
        }
    }

    /// <summary>
    /// Painting a ship by zones. A liquid container of vanilla dye (game:dye-&lt;colour&gt;, in a bucket, jug or bowl) paints a
    /// whole zone that colour, using up DyeLitresPerM2 (0.02 L, two of vanilla's dye items) a square metre of it, rounded
    /// up; gold plates (game:metalplate-gold) gild a moulding or carving, one plate per GoldM2PerPlate (10 m²), at least
    /// one; a knife scrapes a zone back to what it is as built, free. In creative nothing is used up.
    /// Drawn: a painted zone's quads take a greyscale wood grain made from vanilla's oak (textures/block/paint, lifted so
    /// white dye reads white) multiplied by the dye's colour (textures/block/dye: the mean of vanilla's dye texture), the
    /// deck's caulked seams kept over it; gilt ones vanilla's gold plate.
    /// </summary>
    public static class ShipPaint
    {
        public const double DyeLitresPerM2 = 0.02;
        public const int DyeItemsPerLitre = 100;
        public const double GoldM2PerPlate = 10;
        public const string Gold = "gold";
        public const string GoldPlate = "game:metalplate-gold";
        public const string GoldTexture = "game:block/metal/plate/gold";

        /// <summary>Vanilla's eleven dyes and the mean colour of each one's texture (block/liquid/dye/*.png), as r, g, b.</summary>
        public static readonly Dictionary<string, int[]> Dyes = new()
        {
            ["blue"] = new[] { 56, 76, 136 }, ["red"] = new[] { 182, 39, 66 }, ["yellow"] = new[] { 182, 173, 39 },
            ["purple"] = new[] { 96, 56, 136 }, ["woad"] = new[] { 56, 76, 136 }, ["green"] = new[] { 66, 136, 56 },
            ["orange"] = new[] { 182, 66, 39 }, ["black"] = new[] { 29, 29, 29 }, ["gray"] = new[] { 85, 85, 85 },
            ["pink"] = new[] { 182, 39, 119 }, ["white"] = new[] { 242, 242, 242 },
        };

        public static bool Known(string paint) => paint == Gold || paint != null && Dyes.ContainsKey(paint);

        /// <summary>The dye's colour from its item code (game:dye-red → red), or null.</summary>
        public static string DyeColour(string itemCode)
        {
            if (itemCode == null) return null;
            int c = itemCode.IndexOf(':');
            string path = c >= 0 ? itemCode.Substring(c + 1) : itemCode;
            if (c >= 0 && itemCode.Substring(0, c) != "game") return null;
            if (!path.StartsWith("dye-")) return null;
            string colour = path.Substring(4);
            return Dyes.ContainsKey(colour) ? colour : null;
        }

        /// <summary>Dye items (100 a litre) a zone of this area takes.</summary>
        public static int DyeItems(double area) => Math.Max(1, (int)Math.Ceiling(area * DyeLitresPerM2 * DyeItemsPerLitre - 1e-9));

        /// <summary>Gold plates a zone of this area takes to gild.</summary>
        public static int GoldPlates(double area) => Math.Max(1, (int)Math.Ceiling(area / GoldM2PerPlate - 1e-9));

        /// <summary>Only mouldings and carvings are gilded.</summary>
        public static bool Gildable(PaintZoneDef z) => z != null && (z.Kind == "moulding" || z.Kind == "carving");

        /// <summary>
        /// A mesh texture as a zone painted thus is drawn: the paint's grain for the wood under it ("hansa:block/paint/oak1"..4
        /// for planks, "timber" for timbers and anything else) multiplied by the dye ("++3~hansa:block/dye/red"), a caulked
        /// deck's seams over that; gilt: vanilla's gold plate. As built (null paint): the texture itself.
        /// </summary>
        public static string Painted(string texture, string paint)
        {
            if (paint == null || texture == null) return texture;
            if (paint == Gold) return GoldTexture;
            if (!Dyes.ContainsKey(paint)) return texture;
            int plus = texture.IndexOf("++", StringComparison.Ordinal);
            string baseTex = plus >= 0 ? texture.Substring(0, plus) : texture;
            string grain;
            int at = baseTex.IndexOf("planks/oak", StringComparison.Ordinal);
            if (at >= 0 && baseTex.Length > at + 10 && char.IsDigit(baseTex[at + 10])) grain = "hansa:block/paint/oak" + baseTex[at + 10];
            else if (at >= 0) grain = "hansa:block/paint/oak1";
            else grain = "hansa:block/paint/timber";
            bool caulk = texture.Contains("~hansa:block/caulk");
            return grain + "++3~hansa:block/dye/" + paint + (caulk ? "++0~hansa:block/caulk" : "");
        }

        /// <summary>
        /// (0.1.0-dev.8, note 71: "when painting partitions then they change texture direction") A vanilla block's texture as
        /// the atlas has it baked ("game:block/wood/planks/pine2@90": vanilla's planks laid up-down or north-south turn their
        /// grain a quarter) in a paint (any wood's planks: oak's plank grain, the same plank) and with an overlay over it
        /// ("dmg-cracked"; null: none), its quarter turn and alpha kept: the atlas turns the paint's grain the same way.
        /// No paint and no overlay: the name as it is.
        /// </summary>
        public static string PaintedBaked(string baked, string paint, string overlay = null)
        {
            if (baked == null || paint == null && overlay == null) return baked;
            // (the turn, "@90", and the alpha, "å200", come last in a baked name: CompositeTexture.Bake)
            int cut = baked.IndexOfAny(new[] { '@', Vintagestory.API.Client.CompositeTexture.AlphaSeparator });
            string name = cut >= 0 ? baked.Substring(0, cut) : baked, tail = cut >= 0 ? baked.Substring(cut) : "";
            if (paint != null)
            {
                var mm = System.Text.RegularExpressions.Regex.Match(name, @"planks/[a-z]+(\d)");
                if (mm.Success) name = name.Substring(0, mm.Index) + "planks/oak" + mm.Groups[1].Value;
                name = Painted(name, paint);
            }
            if (overlay != null) name += "++0~hansa:block/" + overlay;
            return name + tail;
        }
    }

    /// <summary>
    /// A design's paint zones as her meshes have them (built from the mesh file's paintQuads and paintFaces and the shapes'
    /// elements): each zone's quads in each group of the smooth mesh, its faces of the voxel shapes, its area (m², a block
    /// being a metre), and where they are, for what the crosshair is on.
    /// </summary>
    public class PaintPlan
    {
        public static readonly string[] FaceNames = { "north", "east", "south", "west", "up", "down" };

        public readonly ShipDefinition Def;
        public readonly PaintZoneDef[] Zones;
        /// <summary>Per smooth-mesh group, each quad's zone (-1: none).</summary>
        public readonly Dictionary<string, int[]> QuadZones = new();
        /// <summary>Per shape ("exterior", "interior"), element name → each face's zone (north, east, south, west, up, down; -1: none).</summary>
        public readonly Dictionary<string, Dictionary<string, int[]>> FaceZones = new();
        public readonly double[] Area;
        public readonly int[] QuadCount, FaceCount;
        readonly Dictionary<string, int> index = new();
        readonly List<Patch> patches = new();
        readonly Dictionary<long, List<int>> cells = new();

        struct Patch
        {
            public int Zone;
            /// <summary>(0.3.20) A deck cell's quad: its owner and facing slot (docs/slots.md §3), else -1 both.</summary>
            public int Owner, Facing;
            /// <summary>(0.3.26) Owner and facing are hull panels (a "slot:hull*" group's quad), not deck cells.</summary>
            public bool Hull;
            /// <summary>(0.1.0-dev.8, note 68) A moving part's quad (a gate's, a gun port lid's): its group, else null.</summary>
            public string Part;
            public double[] P;      // four corners, x y z each
            public double NX, NY, NZ;
        }

        public bool Usable => Zones.Length > 0 && Area.Any(a => a > 0);

        public int ZoneIndex(string code) => code != null && index.TryGetValue(code, out int i) ? i : -1;
        public PaintZoneDef Zone(string code) => ZoneIndex(code) is int i && i >= 0 ? Zones[i] : null;

        PaintPlan(ShipDefinition def)
        {
            Def = def;
            Zones = def?.PaintZones ?? Array.Empty<PaintZoneDef>();
            for (int i = 0; i < Zones.Length; i++) if (Zones[i]?.Code != null) index[Zones[i].Code] = i;
            Area = new double[Zones.Length];
            QuadCount = new int[Zones.Length];
            FaceCount = new int[Zones.Length];
        }

        /// <summary>
        /// The plan from a design, its mesh file and its shapes' boxes (shape role → element name → x0 y0 z0 x1 y1 z1 in
        /// blocks, and the faces it has as a mask: <see cref="ShapeBoxes"/>). Every other surface of hers (her mesh's other
        /// quads but the sails', her shapes' other faces) is kept too, with no zone: what the crosshair meets first.
        /// </summary>
        public static PaintPlan Build(ShipDefinition def, ShipSkinFile file, Dictionary<string, Dictionary<string, double[]>> shapes)
        {
            var plan = new PaintPlan(def);
            int nz = plan.Zones.Length;
            if (nz == 0 || file == null) return plan;
            if (file.PaintQuads != null && file.Groups != null)
            {
                foreach (var kv in file.PaintQuads)
                {
                    if (!file.Groups.TryGetValue(kv.Key, out var quads) || quads == null || kv.Value == null) continue;
                    var z = new int[quads.Length];
                    Array.Fill(z, -1);
                    var r = kv.Value;
                    plan.refs = SlotRefs(file, kv.Key);
                    plan.hullRefs = kv.Key.StartsWith("slot:hull");
                    plan.part = PartOf(kv.Key);
                    for (int i = 0; i + 2 < r.Length; i += 3)
                    {
                        int zone = r[i + 2];
                        if (zone < 0 || zone >= nz) continue;
                        for (int q = Math.Max(0, r[i]); q < Math.Min(quads.Length, r[i + 1]); q++)
                        {
                            z[q] = zone;
                            plan.slotQuad = q;
                            plan.AddQuad(zone, quads[q]);
                        }
                    }
                    plan.QuadZones[kv.Key] = z;
                }
            }
            // the rest of her mesh: in front of a zone, it hides it (the deck over the beams under it)
            if (file.Groups != null)
                foreach (var kv in file.Groups)
                {
                    if (kv.Value == null || kv.Key.StartsWith("sail:")) continue;
                    plan.QuadZones.TryGetValue(kv.Key, out var zs);
                    plan.refs = SlotRefs(file, kv.Key);
                    plan.hullRefs = kv.Key.StartsWith("slot:hull");
                    plan.part = PartOf(kv.Key);
                    for (int q = 0; q < kv.Value.Length; q++)
                        if (zs == null || q >= zs.Length || zs[q] < 0) { plan.slotQuad = q; plan.AddQuad(-1, kv.Value[q]); }
                    plan.refs = null;
                }
            plan.refs = null;
            plan.part = null;
            if (file.PaintFaces != null && shapes != null)
            {
                foreach (var kv in file.PaintFaces)
                {
                    if (!shapes.TryGetValue(kv.Key, out var boxes) || kv.Value == null) continue;
                    var map = new Dictionary<string, int[]>();
                    foreach (var el in kv.Value)
                    {
                        if (!boxes.TryGetValue(el.Key, out var b) || el.Value == null) continue;
                        var f = new int[6];
                        Array.Fill(f, -1);
                        foreach (var fz in el.Value)
                        {
                            int fi = Array.IndexOf(FaceNames, fz.Key);
                            if (fi < 0 || fz.Value < 0 || fz.Value >= nz) continue;
                            f[fi] = fz.Value;
                            plan.AddFace(fz.Value, b, fi);
                        }
                        map[el.Key] = f;
                    }
                    plan.FaceZones[kv.Key] = map;
                }
            }
            if (shapes != null)
                foreach (var kv in shapes)
                {
                    Dictionary<string, int[]> map = null;
                    plan.FaceZones.TryGetValue(kv.Key, out map);
                    foreach (var el in kv.Value)
                    {
                        var b = el.Value;
                        int mask = b.Length > 6 ? (int)b[6] : 63;
                        int[] zf = null;
                        map?.TryGetValue(el.Key, out zf);
                        for (int fi = 0; fi < 6; fi++)
                            if ((mask >> fi & 1) != 0 && (zf == null || zf[fi] < 0)) plan.AddFace(-1, b, fi);
                    }
                }
            for (int i = 0; i < nz; i++) plan.Area[i] = Math.Round(plan.Area[i], 2);
            return plan;
        }

        // (0.3.20) while a slot group's quads are added: its slotQuads, and which quad; (0.3.26) whether they're hull panels'
        int[] refs;
        int slotQuad;
        bool hullRefs;
        // (0.1.0-dev.8) while a moving part's group's quads are added: the group
        string part;

        static string PartOf(string group) => group != null && group.StartsWith("part:") ? group : null;

        static int[] SlotRefs(ShipSkinFile file, string group) =>
            group != null && group.StartsWith("slot:") && file.SlotQuads != null && file.SlotQuads.TryGetValue(group, out var r) ? r : null;

        void AddQuad(int zone, float[] q)
        {
            if (q == null || q.Length < 33) return;
            var p = new double[12];
            double nx = 0, ny = 0, nz = 0;
            for (int k = 0; k < 4; k++)
            {
                p[k * 3] = q[1 + 8 * k]; p[k * 3 + 1] = q[2 + 8 * k]; p[k * 3 + 2] = q[3 + 8 * k];
                nx += q[6 + 8 * k]; ny += q[7 + 8 * k]; nz += q[8 + 8 * k];
            }
            if (zone >= 0)
            {
                Area[zone] += QuadArea(p);
                QuadCount[zone]++;
            }
            int owner = refs != null && 2 * slotQuad + 1 < refs.Length ? refs[2 * slotQuad] : -1, facing = refs != null && 2 * slotQuad + 1 < refs.Length ? refs[2 * slotQuad + 1] : -1;
            AddPatch(zone, p, nx, ny, nz, owner, facing, hullRefs, part);
        }

        void AddFace(int zone, double[] b, int face)
        {
            double x0 = b[0], y0 = b[1], z0 = b[2], x1 = b[3], y1 = b[4], z1 = b[5];
            double[] p;
            double nx = 0, ny = 0, nz = 0;
            switch (face)
            {
                case 0: p = new[] { x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0 }; nz = -1; break;
                case 1: p = new[] { x1, y0, z0, x1, y0, z1, x1, y1, z1, x1, y1, z0 }; nx = 1; break;
                case 2: p = new[] { x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1 }; nz = 1; break;
                case 3: p = new[] { x0, y0, z0, x0, y0, z1, x0, y1, z1, x0, y1, z0 }; nx = -1; break;
                case 4: p = new[] { x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1 }; ny = 1; break;
                default: p = new[] { x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1 }; ny = -1; break;
            }
            if (zone >= 0)
            {
                Area[zone] += QuadArea(p);
                FaceCount[zone]++;
            }
            AddPatch(zone, p, nx, ny, nz);
        }

        static double QuadArea(double[] p)
        {
            // half the cross product of the diagonals
            double ax = p[6] - p[0], ay = p[7] - p[1], az = p[8] - p[2];
            double bx = p[9] - p[3], by = p[10] - p[4], bz = p[11] - p[5];
            double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            return 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
        }

        static long Key(int i, int j, int k) => ((long)(i + 1024) << 42) | ((long)(j + 1024) << 21) | (long)(k + 1024);

        void AddPatch(int zone, double[] p, double nx, double ny, double nz, int owner = -1, int facing = -1, bool hull = false, string part = null)
        {
            double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (l > 1e-9) { nx /= l; ny /= l; nz /= l; }
            int id = patches.Count;
            patches.Add(new Patch { Zone = zone, P = p, NX = nx, NY = ny, NZ = nz, Owner = owner, Facing = facing, Hull = hull, Part = part });
            double x0 = Math.Min(Math.Min(p[0], p[3]), Math.Min(p[6], p[9])), x1 = Math.Max(Math.Max(p[0], p[3]), Math.Max(p[6], p[9]));
            double y0 = Math.Min(Math.Min(p[1], p[4]), Math.Min(p[7], p[10])), y1 = Math.Max(Math.Max(p[1], p[4]), Math.Max(p[7], p[10]));
            double z0 = Math.Min(Math.Min(p[2], p[5]), Math.Min(p[8], p[11])), z1 = Math.Max(Math.Max(p[2], p[5]), Math.Max(p[8], p[11]));
            for (int i = (int)Math.Floor(x0); i <= (int)Math.Floor(x1); i++)
                for (int j = (int)Math.Floor(y0); j <= (int)Math.Floor(y1); j++)
                    for (int k = (int)Math.Floor(z0); k <= (int)Math.Floor(z1); k++)
                    {
                        long key = Key(i, j, k);
                        if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                        list.Add(id);
                    }
        }

        /// <summary>
        /// The zone a ray (ship-local, d normalised) meets first within maxT (through her collision's first solid layer:
        /// <see cref="Reach"/>), on a face turned toward the ray (by its normal); -1 if
        /// none, or if what it meets first is a surface of hers in no zone.
        /// </summary>
        /// <param name="shown">(0.3.20) for a deck cell's quad (its owner and facing slot): is it drawn on this ship now; null: all are</param>
        /// <param name="partShut">(0.1.0-dev.8) for a moving part's quad (its group): is the part drawn where her mesh has it (a
        /// gate or a gun port's lid shut); null: all are</param>
        public int Pick(Vec3d o, Vec3d d, double maxT, out double dist, Func<int, int, bool> shown = null, Func<int, int, bool> hullShown = null, Func<string, bool> partShut = null)
        {
            dist = double.MaxValue;
            int best = -1;
            if (patches.Count == 0) return -1;
            // walk the 1-block cells along the ray (Amanatides & Woo)
            int i = (int)Math.Floor(o.X), j = (int)Math.Floor(o.Y), k = (int)Math.Floor(o.Z);
            int si = Math.Sign(d.X), sj = Math.Sign(d.Y), sk = Math.Sign(d.Z);
            double tx = si != 0 ? ((si > 0 ? i + 1 : i) - o.X) / d.X : double.MaxValue;
            double ty = sj != 0 ? ((sj > 0 ? j + 1 : j) - o.Y) / d.Y : double.MaxValue;
            double tz = sk != 0 ? ((sk > 0 ? k + 1 : k) - o.Z) / d.Z : double.MaxValue;
            double dx = si != 0 ? Math.Abs(1 / d.X) : double.MaxValue, dy = sj != 0 ? Math.Abs(1 / d.Y) : double.MaxValue, dz = sk != 0 ? Math.Abs(1 / d.Z) : double.MaxValue;
            double tCell = 0;
            var seen = new HashSet<int>();
            for (int n = 0; n < 512 && tCell <= maxT && tCell <= dist; n++)
            {
                if (cells.TryGetValue(Key(i, j, k), out var list))
                {
                    foreach (int id in list)
                    {
                        if (!seen.Add(id)) continue;
                        var pa = patches[id];
                        // ((0.3.26) a hull panel's quad by the hull panels' states)
                        var sh = pa.Hull ? hullShown : shown;
                        if (sh != null && (pa.Owner >= 0 || pa.Facing >= 0) && !sh(pa.Owner, pa.Facing)) continue;
                        // ((0.1.0-dev.8) a gate or a lid swung open isn't where her mesh has it)
                        if (partShut != null && pa.Part != null && !partShut(pa.Part)) continue;
                        // only a face turned toward the ray (the deck's underside isn't painted from over the deck: the
                        // ray meets the deck's top, and its underside lies within the allowance behind it)
                        if (pa.NX * d.X + pa.NY * d.Y + pa.NZ * d.Z > 0) continue;
                        double t = RayQuad(o, d, pa.P);
                        if (t < 0 || t > maxT || t >= dist) continue;
                        dist = t;
                        best = pa.Zone;
                    }
                }
                if (tx <= ty && tx <= tz) { tCell = tx; tx += dx; i += si; }
                else if (ty <= tz) { tCell = ty; ty += dy; j += sj; }
                else { tCell = tz; tz += dz; k += sk; }
            }
            return best;
        }

        /// <summary>
        /// How far along the crosshair's ray (ship-local, d normalised) her zones are looked for: past the solid layer of her
        /// collision it meets first (wall), as far as the ray comes out of it, at most 1.5 blocks; else a little past a
        /// piece in front of her (front). (0.1.0-dev.8, note 77: "trying to paint the hull but it does not register") Her
        /// collision covers whole quarter-block voxels where her surfaces are drawn anywhere in or behind them: the caravel's
        /// bulwarks are solid from half a block in front of their inside planking, and a fixed 0.4 past the collision fell
        /// short of it.
        /// </summary>
        public static double Reach(VoxelGrid grid, Vec3d o, Vec3d d, double range, double wall, double front = double.MaxValue)
        {
            double reach = Math.Min(wall, front) + 0.4;
            if (grid != null && wall <= front && wall < range)
                reach = Math.Max(reach, Math.Min(wall + 1.5, grid.SolidExit(o, d, wall, wall + 1.5) + 0.05));
            return Math.Min(range, reach);
        }

        /// <summary>Has this zone a surface within r of a point (the server's check of what a player says they painted)?</summary>
        public bool Near(int zone, double x, double y, double z, double r)
        {
            for (int i = (int)Math.Floor(x - r); i <= (int)Math.Floor(x + r); i++)
                for (int j = (int)Math.Floor(y - r); j <= (int)Math.Floor(y + r); j++)
                    for (int k = (int)Math.Floor(z - r); k <= (int)Math.Floor(z + r); k++)
                    {
                        if (!cells.TryGetValue(Key(i, j, k), out var list)) continue;
                        foreach (int id in list)
                        {
                            var pa = patches[id];
                            if (pa.Zone != zone) continue;
                            if (DistanceToQuad(pa.P, x, y, z) <= r) return true;
                        }
                    }
            return false;
        }

        static double DistanceToQuad(double[] p, double x, double y, double z)
        {
            // to the nearer of its two triangles, roughly: the distance to its bounding box and its plane
            double x0 = Math.Min(Math.Min(p[0], p[3]), Math.Min(p[6], p[9])), x1 = Math.Max(Math.Max(p[0], p[3]), Math.Max(p[6], p[9]));
            double y0 = Math.Min(Math.Min(p[1], p[4]), Math.Min(p[7], p[10])), y1 = Math.Max(Math.Max(p[1], p[4]), Math.Max(p[7], p[10]));
            double z0 = Math.Min(Math.Min(p[2], p[5]), Math.Min(p[8], p[11])), z1 = Math.Max(Math.Max(p[2], p[5]), Math.Max(p[8], p[11]));
            double dx = Math.Max(0, Math.Max(x0 - x, x - x1)), dy = Math.Max(0, Math.Max(y0 - y, y - y1)), dz = Math.Max(0, Math.Max(z0 - z, z - z1));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Where a ray meets a quad (two triangles 0-1-2, 0-2-3): t, or -1.</summary>
        static double RayQuad(Vec3d o, Vec3d d, double[] p)
        {
            double t = RayTri(o, d, p, 0, 1, 2);
            double t2 = RayTri(o, d, p, 0, 2, 3);
            if (t < 0 || t2 >= 0 && t2 < t) t = t2;
            return t;
        }

        static double RayTri(Vec3d o, Vec3d d, double[] p, int a, int b, int c)
        {
            double ax = p[a * 3], ay = p[a * 3 + 1], az = p[a * 3 + 2];
            double e1x = p[b * 3] - ax, e1y = p[b * 3 + 1] - ay, e1z = p[b * 3 + 2] - az;
            double e2x = p[c * 3] - ax, e2y = p[c * 3 + 1] - ay, e2z = p[c * 3 + 2] - az;
            double px = d.Y * e2z - d.Z * e2y, py = d.Z * e2x - d.X * e2z, pz = d.X * e2y - d.Y * e2x;
            double det = e1x * px + e1y * py + e1z * pz;
            if (Math.Abs(det) < 1e-12) return -1;
            double inv = 1 / det;
            double tx = o.X - ax, ty = o.Y - ay, tz = o.Z - az;
            double u = (tx * px + ty * py + tz * pz) * inv;
            if (u < -1e-6 || u > 1 + 1e-6) return -1;
            double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
            double v = (d.X * qx + d.Y * qy + d.Z * qz) * inv;
            if (v < -1e-6 || u + v > 1 + 1e-6) return -1;
            double t = (e2x * qx + e2y * qy + e2z * qz) * inv;
            return t >= 0 ? t : -1;
        }

        /// <summary>
        /// A shape file's elements' boxes (blocks, from/to in sixteenths; children nested in their parents, unrotated), by
        /// name: x0 y0 z0 x1 y1 z1, and which faces it has (bit 0 north .. 5 down, as <see cref="FaceNames"/>).
        /// </summary>
        public static Dictionary<string, double[]> ShapeBoxes(string json)
        {
            var map = new Dictionary<string, double[]>();
            if (string.IsNullOrEmpty(json)) return map;
            var root = JObject.Parse(json);
            void Walk(JArray els, double ox, double oy, double oz)
            {
                if (els == null) return;
                foreach (var e in els.OfType<JObject>())
                {
                    var from = e["from"] as JArray;
                    var to = e["to"] as JArray;
                    if (from == null || to == null || from.Count < 3 || to.Count < 3) continue;
                    double fx = ox + (double)from[0] / 16, fy = oy + (double)from[1] / 16, fz = oz + (double)from[2] / 16;
                    double tx = ox + (double)to[0] / 16, ty = oy + (double)to[1] / 16, tz = oz + (double)to[2] / 16;
                    string name = (string)e["name"];
                    int mask = 0;
                    if (e["faces"] is JObject faces)
                        foreach (var f in faces.Properties())
                        {
                            int fi = Array.IndexOf(FaceNames, f.Name);
                            if (fi >= 0 && (f.Value as JObject)?["enabled"]?.Type != JTokenType.Boolean | (bool?)(f.Value as JObject)?["enabled"] != false) mask |= 1 << fi;
                        }
                    if (name != null) map[name] = new[] { Math.Min(fx, tx), Math.Min(fy, ty), Math.Min(fz, tz), Math.Max(fx, tx), Math.Max(fy, ty), Math.Max(fz, tz), mask };
                    Walk(e["children"] as JArray, fx, fy, fz);
                }
            }
            Walk(root["elements"] as JArray, 0, 0, 0);
            return map;
        }
    }

    /// <summary>
    /// How her name is painted on the board (the user's note 68): the letters' colour (gilt, or one of vanilla's dyes), the
    /// board's ground behind them (as built, or a colour), how big (of the board's height) and upright or italic. Saved
    /// with her name (hansaFit), so every client draws it.
    /// </summary>
    public class NameStyle
    {
        /// <summary>The colours there are: gilt, and vanilla's dyes (black and white among them).</summary>
        public static readonly string[] Colours = new[] { ShipPaint.Gold }.Concat(ShipPaint.Dyes.Keys).ToArray();
        /// <summary>Sizes: how much of the board's height the letters fill (small, medium, large).</summary>
        public static readonly double[] Sizes = { 0.6, 0.8, 1.0 };

        public string Text = ShipPaint.Gold;
        /// <summary>The ground's colour, or null: the board as built.</summary>
        public string Back;
        public int Size = 2;
        public bool Italic;

        public static bool Known(string c) => c != null && Array.IndexOf(Colours, c) >= 0;

        /// <summary>This style with anything unknown back to the defaults.</summary>
        public NameStyle Clean() => new()
        {
            Text = Known(Text) ? Text : ShipPaint.Gold, Back = Known(Back) ? Back : null,
            Size = Math.Clamp(Size, 0, Sizes.Length - 1), Italic = Italic
        };

        /// <summary>A colour as r, g, b (0..1): gilt a warm gold, a dye its texture's mean.</summary>
        public static double[] Rgb(string c)
        {
            if (c == ShipPaint.Gold || c == null || !ShipPaint.Dyes.TryGetValue(c, out var d)) return new[] { 0.95, 0.8, 0.38 };
            return new[] { d[0] / 255.0, d[1] / 255.0, d[2] / 255.0 };
        }

        /// <summary>The letters' outline: gilt's dark brown, else the colour darkened (light on dark grounds).</summary>
        public double[] StrokeRgb()
        {
            if (Text == ShipPaint.Gold) return new[] { 0.25, 0.16, 0.05 };
            var c = Rgb(Text);
            double lum = 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2];
            return lum < 0.2 ? new[] { 0.55, 0.5, 0.42 } : new[] { c[0] * 0.3, c[1] * 0.3, c[2] * 0.3 };
        }

        public bool SameAs(NameStyle o) => o != null && o.Text == Text && o.Back == Back && o.Size == Size && o.Italic == Italic;

        public void ToTree(ITreeAttribute t)
        {
            t.SetString("nameText", Text);
            if (Back != null) t.SetString("nameBack", Back); else t.RemoveAttribute("nameBack");
            t.SetInt("nameSize", Size);
            t.SetBool("nameItalic", Italic);
        }

        public static NameStyle FromTree(ITreeAttribute t) => t == null ? new NameStyle() : new NameStyle
        {
            Text = t.GetString("nameText", ShipPaint.Gold), Back = t.GetString("nameBack"), Size = t.GetInt("nameSize", 2), Italic = t.GetBool("nameItalic")
        }.Clean();
    }
}
