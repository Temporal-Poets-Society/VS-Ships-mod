using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    /// <summary>
    /// A design's smooth mesh (config/shipmeshes/&lt;code&gt;.json, from the generator): a list of quads per group, each
    /// [texture index, then per corner x y z u v nx ny nz] with u, v in texels of that texture. HullOut and HullIn say which
    /// of a group's quads are the hull's planking outside and its inside planking (the ceiling), as half-open index ranges
    /// [start, end, start, end, ...]; HullThickness is the planking's thickness.
    /// </summary>
    public class ShipSkinFile
    {
        public string[] Textures;
        public Dictionary<string, float[][]> Groups;
        public Dictionary<string, int[]> HullOut, HullIn;
        public float HullThickness;
        /// <summary>
        /// Paint zones (ShipPaint): per group, quad ranges [start, end, zone, ...] (zone: index into the definition's
        /// paintZones); per shape ("exterior", "interior"), element name → face name → zone.
        /// </summary>
        public Dictionary<string, int[]> PaintQuads;
        public Dictionary<string, Dictionary<string, Dictionary<string, int>>> PaintFaces;
        /// <summary>
        /// (0.3.20, step B; docs/slots.md §3) Per slot group ("slot:deck", "slot:deck:interior"): each quad's owner and facing
        /// slot, two ints a quad (-1 none). Drawn while (owner -1 or filled) and (facing -1 or empty).
        /// </summary>
        public Dictionary<string, int[]> SlotQuads;
    }

    /// <summary>
    /// How a hull panel is drawn, from its state: the overlay on its planking and, holed, how big the hole is. One byte a
    /// panel: the look in the low 4 bits, the hole's size step (1 just holed .. <see cref="HoleSteps"/> stove in) above.
    /// </summary>
    public static class HullLook
    {
        public const int Sound = 0, Worn = 1, Scraped = 2, Cracked = 3, Leaking = 4, Holed = 5;
        public const int HoleSteps = 4;

        public static byte Of(DamageLayout l, int part, float hurt, float wear)
        {
            var p = l.Parts[part];
            if (p.Kind != DamageKind.Hull) return 0;
            int look = l.StateOf(p.Kind, hurt, wear) switch
            {
                PartState.Worn => Worn,
                PartState.Scraped => Scraped,
                PartState.Cracked => Cracked,
                PartState.Leaking => Leaking,
                PartState.Holed => Holed,
                _ => Sound
            };
            if (look != Holed) return (byte)look;
            double f = Math.Clamp((hurt - l.D.Holed) / Math.Max(1e-3, 1 - l.D.Holed), 0, 1);
            int step = Math.Min(HoleSteps, 1 + (int)Math.Floor(f * HoleSteps));
            return (byte)(Holed | step << 4);
        }

        public static int Look(byte b) => b & 15;
        public static int HoleStep(byte b) => Look(b) == Holed ? Math.Max(1, b >> 4) : 0;

        /// <summary>
        /// The overlay (textures/block/dmg-*.png) for a look, on the planking outside or inside (the ceiling shows the cracks,
        /// the wet of leaking seams and the break round a hole; worn and scraped are outside only), two of each; null: none.
        /// </summary>
        public static string Overlay(int look, bool inside, int variant)
        {
            // inside (0.3.10, the user: "inside, damage textures are not visible, at least not as well as outside"): her
            // ceiling is lit dimly, so its overlays are their own, stronger ones ("-in"): worn seams weep (damp), cracks
            // show darker and wider, a leak's wet stain and a hole's splinters stronger; a scrape is only outside
            string s = look switch
            {
                Worn => inside ? "damp-in" : "worn",
                // ((0.3.27, note 210: "not visible damage from inside") a scrape shows inside too: faint splits in the ceiling)
                Scraped => inside ? "scraped-in" : "scraped",
                Cracked => inside ? "cracked-in" : "cracked",
                Leaking => inside ? "wet-in" : "leaking",
                Holed => inside ? "holed-in" : "holed",
                _ => null
            };
            return s == null ? null : "dmg-" + s + (variant == 0 ? "-a" : "-b");
        }
    }

    /// <summary>A part of a hull group's planking the mod rebuilds as one mesh: one side of her, a few frame spaces long.</summary>
    public class HullChunk
    {
        public int Index;
        public string Group;
        /// <summary>Its quads (indices into the group), the damage part each is the planking of, and whether it's the ceiling.</summary>
        public int[] Quads, Panel;
        public bool[] Inside;
        /// <summary>Round all its quads (ship-local).</summary>
        public double[] Min, Max;
        /// <summary>
        /// The panels whose planking is here; those whose outside planking is here; those whose hole's reveal is drawn with
        /// this chunk (with the outside planking: the topsides', else the bottom's, tarred and clad alike); those whose
        /// biggest hole reaches in here.
        /// </summary>
        public int[] Panels, OuterPanels, RevealPanels, HolePanels;
    }

    /// <summary>
    /// A hole through a hull panel, as broken planking (the eighth client test: "Holes in ship are very round. Can we make
    /// them jagged? Could be also square and two sides jagged as broken boards"): the strakes it breaks through, each from
    /// seam to seam and off at its own length either way along the planks, its ends splintered; outside (the planking) and
    /// in (the ceiling) each its own way through the same strakes. <see cref="HullDamageSkin.HoleOf"/>.
    /// </summary>
    public struct HullHole
    {
        public int Panel;
        /// <summary>Its middle on the panel's outer face; outward; along the planks; across them (unit vectors).</summary>
        public double[] C, N, U, W;
        /// <summary>How far it reaches along U and W (its ends and seams furthest out), and along N either side of the outer face.</summary>
        public double Ru, Rw, Reach;
        /// <summary>Her shell's thickness at the hole (from her collision; the mesh file's one thickness at least).</summary>
        public double Depth;
        /// <summary>Round all it can cut (ship-local).</summary>
        public double[] Min, Max;
        /// <summary>The strakes it breaks: the planking's, and the ceiling's each paired with the planking's at its height.</summary>
        public HoleStrake[] Strakes;
        /// <summary>A planking quad's strake among <see cref="Strakes"/> (by the mesh file's own array).</summary>
        public Dictionary<float[], HoleStrake> ByQuad;
        /// <summary>What it takes out of the planking outside and out of the ceiling (m², across its strakes as they cross its middle).</summary>
        public double AreaOut, AreaIn;
    }

    /// <summary>
    /// One strake a hole breaks: its planking quads round the hole (each across the whole strake: t, corner 0 to 3, from
    /// its lower seam to its upper; s, corner 0 to 1, along it), and where it's broken off either way at points across it
    /// (along the hole's U from its middle; straight between them).
    /// </summary>
    public sealed class HoleStrake
    {
        public bool Inside;
        public List<float[]> Quads = new();
        /// <summary>Its seams where it crosses the hole's middle: across the hole (along its W) and their heights.</summary>
        public double WLo, WHi, YLo, YHi;
        /// <summary>Where its broken ends turn across it (0 .. 1), and where they are there (Left below 0, Right above).</summary>
        public double[] T, Left, Right;
        /// <summary>The other layer's strake at its height (the ceiling's for the planking's and back), or null.</summary>
        public HoleStrake Pair;
        /// <summary>
        /// Something lying over the planking (a wale, a rail cap: marked as the planking outside, standing out from it or
        /// its top and bottom faces): broken where the strake under it (Host) is, but not part of the hole's outline.
        /// </summary>
        public bool Over;
        public HoleStrake Host;
        /// <summary>Its ends before they're sized: each a share of the hole's half-length, and their teeth (relative to it).</summary>
        public double[] PL, PR;
        public double BL, BR;
        /// <summary>How far out it stands at the hole's middle (along its N), and how much it faces out (its normals' mean along N).</summary>
        public double NOff, Facing;

        public double WMid => (WLo + WHi) / 2;
        public double LeftAt(double t) => At(Left, t);
        public double RightAt(double t) => At(Right, t);

        double At(double[] v, double t)
        {
            if (v == null || T == null || T.Length == 0) return 0;
            if (t <= T[0]) return v[0];
            for (int i = 0; i + 1 < T.Length; i++)
                if (t <= T[i + 1]) return T[i + 1] - T[i] < 1e-12 ? v[i + 1] : v[i] + (v[i + 1] - v[i]) * (t - T[i]) / (T[i + 1] - T[i]);
            return v[^1];
        }
    }

    /// <summary>
    /// The damage drawn on a design's hull (per design; each ship's own damage picks what's built from it). The planking's
    /// quads (the mesh file's HullOut and HullIn) each belong to the hull panel their middle is in (the ceiling's: the
    /// panel outside it), and are grouped into chunks, one side a few frame spaces long, so a change rebuilds only the
    /// chunks it touches. A chunk's quads get their panel's overlay (<see cref="HullLook"/>); a holed panel's planking,
    /// outside and in, is broken through (<see cref="HoleOf"/>): the strakes it breaks are cut out exactly between their
    /// splintered ends, and quads for the planking's thickness close the hole round its outline. The collision grid stays
    /// whole. Pure geometry: the offline tests build it, and the client uploads what it gives.
    /// </summary>
    public class HullDamageSkin
    {
        public static readonly string[] HullGroups = { "exterior", "bottom", "bottomclad", "interior" };
        /// <summary>Frame spaces a chunk runs along her.</summary>
        public const int StationsPerChunk = 3;
        const string OverlayDomain = "++0~ships:block/";
        const string RawOak = "game:block/wood/planks/oak1";

        public readonly ShipSkinFile File;
        public readonly DamageLayout Layout;
        public readonly double Thickness;
        /// <summary>Per hull group: the quads that aren't a panel's planking (the wales, rails, masts, rigging...), drawn as they are.</summary>
        public readonly Dictionary<string, int[]> Static = new();
        public readonly List<HullChunk> Chunks = new();
        /// <summary>The textures the damaged chunks use: the file's own, then the overlaid ones as they're asked for.</summary>
        public readonly List<string> Textures;
        readonly List<int>[] panelChunks, holeChunks;
        readonly Dictionary<(int, string), int> overlaid = new();

        public HullDamageSkin(ShipSkinFile file, DamageLayout layout)
        {
            File = file;
            Layout = layout;
            Thickness = file.HullThickness > 0.01f ? file.HullThickness : 0.3;
            Textures = new List<string>(file.Textures ?? Array.Empty<string>());
            panelChunks = new List<int>[layout.Count];
            holeChunks = new List<int>[layout.Count];
            for (int i = 0; i < layout.Count; i++) { panelChunks[i] = new List<int>(); holeChunks[i] = new List<int>(); }
            if (file.Groups == null) return;
            foreach (string g in HullGroups)
            {
                if (!file.Groups.TryGetValue(g, out var quads) || quads == null) continue;
                var outer = Marks(quads.Length, file.HullOut, g);
                var inner = Marks(quads.Length, file.HullIn, g);
                var statics = new List<int>();
                var buckets = new SortedDictionary<int, (List<int> Q, List<int> P, List<bool> I)>();
                for (int i = 0; i < quads.Length; i++)
                {
                    int panel = outer[i] || inner[i] ? PanelOf(quads[i], inner[i]) : -1;
                    if (panel < 0) { statics.Add(i); continue; }
                    var part = layout.Parts[panel].Part;
                    int key = (part.Station / StationsPerChunk) << 1 | (part.Side > 0 ? 1 : 0);
                    if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = (new List<int>(), new List<int>(), new List<bool>());
                    b.Q.Add(i);
                    b.P.Add(panel);
                    b.I.Add(inner[i]);
                }
                Static[g] = statics.ToArray();
                foreach (var b in buckets.Values)
                {
                    var c = new HullChunk
                    {
                        Index = Chunks.Count, Group = g, Quads = b.Q.ToArray(), Panel = b.P.ToArray(), Inside = b.I.ToArray(),
                        Min = new[] { double.MaxValue, double.MaxValue, double.MaxValue }, Max = new[] { double.MinValue, double.MinValue, double.MinValue }
                    };
                    var panels = new SortedSet<int>();
                    var outers = new SortedSet<int>();
                    for (int n = 0; n < c.Quads.Length; n++)
                    {
                        var q = quads[c.Quads[n]];
                        for (int k = 0; k < 4; k++)
                            for (int a = 0; a < 3; a++)
                            {
                                c.Min[a] = Math.Min(c.Min[a], q[1 + 8 * k + a]);
                                c.Max[a] = Math.Max(c.Max[a], q[1 + 8 * k + a]);
                            }
                        panels.Add(c.Panel[n]);
                        if (!c.Inside[n]) outers.Add(c.Panel[n]);
                    }
                    c.Panels = new int[panels.Count];
                    panels.CopyTo(c.Panels);
                    c.OuterPanels = new int[outers.Count];
                    outers.CopyTo(c.OuterPanels);
                    foreach (int p in c.Panels) panelChunks[p].Add(c.Index);
                    Chunks.Add(c);
                }
            }
            // which chunks each panel's biggest hole can reach
            var reach = new List<int>[Chunks.Count];
            for (int n = 0; n < Chunks.Count; n++) reach[n] = new List<int>();
            for (int p = 0; p < layout.Count; p++)
            {
                if (layout.Parts[p].Kind != DamageKind.Hull || panelChunks[p].Count == 0) continue;
                // (round its hole at every size: a smaller one sits off the panel's middle more)
                var h = HoleOf(p, HullLook.HoleSteps);
                for (int step = 1; step < HullLook.HoleSteps; step++)
                {
                    var hs = HoleOf(p, step);
                    for (int a = 0; a < 3; a++) { h.Min[a] = Math.Min(h.Min[a], hs.Min[a]); h.Max[a] = Math.Max(h.Max[a], hs.Max[a]); }
                }
                foreach (var c in Chunks)
                {
                    if (!Overlaps(h.Min, h.Max, c.Min, c.Max)) continue;
                    holeChunks[p].Add(c.Index);
                    reach[c.Index].Add(p);
                }
            }
            foreach (var c in Chunks) c.HolePanels = reach[c.Index].ToArray();
            // a hole's reveal goes with its panel's outside planking: the topsides' (exterior) if it has any, else the
            // bottom's (tarred or clad: whichever is drawn)
            var reveals = new List<int>[Chunks.Count];
            for (int n = 0; n < Chunks.Count; n++) reveals[n] = new List<int>();
            for (int p = 0; p < layout.Count; p++)
            {
                bool topsides = false;
                foreach (int n in panelChunks[p]) if (Chunks[n].Group == "exterior" && Array.IndexOf(Chunks[n].OuterPanels, p) >= 0) topsides = true;
                foreach (int n in panelChunks[p])
                {
                    var c = Chunks[n];
                    if (Array.IndexOf(c.OuterPanels, p) < 0) continue;
                    if (topsides ? c.Group == "exterior" : c.Group == "bottom" || c.Group == "bottomclad") reveals[n].Add(p);
                }
            }
            foreach (var c in Chunks) c.RevealPanels = reveals[c.Index].ToArray();
        }

        /// <summary>Has planking marked in the mesh file to draw damage on.</summary>
        public bool Usable => Chunks.Count > 0;

        static bool[] Marks(int n, Dictionary<string, int[]> ranges, string group)
        {
            var m = new bool[n];
            if (ranges == null || !ranges.TryGetValue(group, out var r) || r == null) return m;
            for (int i = 0; i + 1 < r.Length; i += 2)
                for (int q = Math.Max(0, r[i]); q < Math.Min(n, r[i + 1]); q++) m[q] = true;
            return m;
        }

        static bool Overlaps(double[] a0, double[] a1, double[] b0, double[] b1) =>
            a0[0] <= b1[0] && a1[0] >= b0[0] && a0[1] <= b1[1] && a1[1] >= b0[1] && a0[2] <= b1[2] && a1[2] >= b0[2];

        /// <summary>
        /// The hull panel a planking quad belongs to: the one its middle is in (the crosshair's rule: side, frame space,
        /// band), for the ceiling the one outside it; -1 if it's not near one.
        /// </summary>
        public int PanelOf(float[] q, bool inside)
        {
            double x = 0, y = 0, z = 0, nx = 0, ny = 0, nz = 0;
            for (int k = 0; k < 4; k++)
            {
                int o = 1 + 8 * k;
                x += q[o] / 4.0; y += q[o + 1] / 4.0; z += q[o + 2] / 4.0;
                nx += q[o + 5]; ny += q[o + 6]; nz += q[o + 7];
            }
            if (inside)
            {
                // the ceiling faces inboard: its outside is the planking's thickness the other way
                double ln = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (ln > 1e-6)
                {
                    double t = ThicknessAt(x, y, z, -nx / ln, -ny / ln, -nz / ln);
                    x -= nx / ln * t; y -= ny / ln * t; z -= nz / ln * t;
                }
            }
            var map = Layout.Def.PartMap;
            if (map == null) return -1;
            int i = Layout.Find(ShipPart.Hull(z < 0 ? -1 : 1, map.StationAt(x), map.BandAt(y)));
            return i >= 0 && Layout.Parts[i].DistanceTo(new Vintagestory.API.MathTools.Vec3d(x, y, z)) < 0.75 ? i : -1;
        }

        // ------------------------------------------------------------------ what a ship's damage changes

        /// <summary>Each part's look (<see cref="HullLook"/>) for this damage; 0 for all but hull panels.</summary>
        public byte[] Looks(DamageState st)
        {
            var b = new byte[Layout.Count];
            if (st == null || st.Count != Layout.Count) return b;
            for (int i = 0; i < b.Length; i++) b[i] = HullLook.Of(Layout, i, st.Hurt[i], st.Wear[i]);
            return b;
        }

        /// <summary>The chunks to build again when the looks change from before to after (before null: all that differ from sound).</summary>
        public SortedSet<int> Affected(byte[] before, byte[] after)
        {
            var set = new SortedSet<int>();
            for (int p = 0; p < Layout.Count; p++)
            {
                byte a = before == null ? (byte)0 : before[p], b = after[p];
                if (a == b) continue;
                foreach (int c in panelChunks[p]) set.Add(c);
                if (HullLook.Look(a) == HullLook.Holed || HullLook.Look(b) == HullLook.Holed)
                    foreach (int c in holeChunks[p]) set.Add(c);
            }
            return set;
        }

        /// <summary>Does this chunk look any different from the sound design with these looks?</summary>
        public bool Damaged(HullChunk c, byte[] looks)
        {
            foreach (int p in c.Panels) if (looks[p] != 0) return true;
            foreach (int p in c.HolePanels) if (HullLook.Look(looks[p]) == HullLook.Holed) return true;
            return false;
        }

        /// <summary>The group's quads that are no panel's planking, as the file has them.</summary>
        public List<float[]> StaticQuads(string group)
        {
            var list = new List<float[]>();
            if (!Static.TryGetValue(group, out var idx)) return list;
            var quads = File.Groups[group];
            foreach (int i in idx) list.Add(quads[i]);
            return list;
        }

        /// <summary>A chunk as the sound design has it.</summary>
        public List<float[]> SoundQuads(HullChunk c)
        {
            var quads = File.Groups[c.Group];
            var list = new List<float[]>(c.Quads.Length);
            foreach (int i in c.Quads) list.Add(quads[i]);
            return list;
        }

        /// <summary>
        /// A chunk as a ship with these looks has it (quads in the file's format, texture indices into <see cref="Textures"/>;
        /// a triangle is a quad with its last corner twice): each panel's overlay on its planking, the planking inside a
        /// hole cut away, and the hole's reveal with the panel's outside planking. baseTexture: a quad's texture before its
        /// look's overlay, by its index in the group (a painted zone's paint, ShipPaint; null: its own).
        /// </summary>
        public List<float[]> Build(HullChunk c, byte[] looks, Func<int, int> baseTexture = null)
        {
            var holes = new List<HullHole>();
            foreach (int p in c.HolePanels)
                if (HullLook.Look(looks[p]) == HullLook.Holed) holes.Add(HoleOf(p, HullLook.HoleStep(looks[p])));
            var quads = File.Groups[c.Group];
            var list = new List<float[]>(c.Quads.Length + 16);
            var near = new List<HullHole>();
            for (int n = 0; n < c.Quads.Length; n++)
            {
                var q = quads[c.Quads[n]];
                int tex = TextureFor(baseTexture?.Invoke(c.Quads[n]) ?? (int)q[0], HullLook.Look(looks[c.Panel[n]]), c.Inside[n], (c.Quads[n] * 7 + 3) >> 1 & 1);
                near.Clear();
                foreach (var h in holes) if (QuadNear(q, h)) near.Add(h);
                if (near.Count == 0 || !Cut(q, tex, near, list))
                {
                    var copy = (float[])q.Clone();
                    copy[0] = tex;
                    list.Add(copy);
                }
            }
            foreach (var h in holes)
                if (Array.IndexOf(c.RevealPanels, h.Panel) >= 0) Reveal(h, list);
            return list;
        }

        /// <summary>A texture's index among <see cref="Textures"/> (added the first time: a painted zone's, ShipPaint).</summary>
        public int TextureIndex(string name)
        {
            int i = Textures.IndexOf(name);
            if (i >= 0) return i;
            Textures.Add(name);
            return Textures.Count - 1;
        }

        /// <summary>The texture index for a quad of this texture with a panel's look on it (the overlaid one added the first time).</summary>
        public int TextureFor(int tex, int look, bool inside, int variant)
        {
            string overlay = HullLook.Overlay(look, inside, variant);
            if (overlay == null || tex < 0 || tex >= Textures.Count) return tex;
            if (overlaid.TryGetValue((tex, overlay), out int i)) return i;
            i = Textures.Count;
            Textures.Add(Textures[tex] + OverlayDomain + overlay);
            overlaid[(tex, overlay)] = i;
            return i;
        }

        int rawOak = -1;

        int RawOakTexture()
        {
            if (rawOak >= 0) return rawOak;
            rawOak = Textures.IndexOf(RawOak);
            if (rawOak < 0)
            {
                rawOak = Textures.Count;
                Textures.Add(RawOak);
            }
            return rawOak;
        }

        /// <summary>
        /// Her shell's thickness at a point on one of its faces, going the other way through it (dir, unit): through her
        /// collision to the first free cell beyond (hull R's shell is 1.0 thick at the keel, 0.65 under the flat floor, 0.3
        /// up the side), never less than the mesh file's thickness nor more than 1.5.
        /// </summary>
        public double ThicknessAt(double x, double y, double z, double dx, double dy, double dz)
        {
            var g = Layout.Def?.Grid;
            if (g == null) return Thickness;
            bool any = false;
            double t = 0.03;
            for (; t < 1.6; t += 0.0625)
            {
                if (g.SolidAt(x + dx * t, y + dy * t, z + dz * t)) any = true;
                else if (any || t > 0.35) break;
            }
            // (the surface lies somewhere in the last solid cell: half of it back)
            return any ? Math.Clamp(t - 0.03 - g.V / 2, Thickness, 1.5) : Thickness;
        }

        // ------------------------------------------------------------------ holes

        /// <summary>A panel's hole: its frame, how big it gets, and the strakes round it (the same at every size).</summary>
        sealed class HoleSite
        {
            public double[] C, N, U, W;
            public double RuMax, RwMax;
            /// <summary>Her shell's thickness there (hull R: thicker low down), from her collision.</summary>
            public double Depth;
            /// <summary>The planking's strakes near it (bottom up), each paired with the ceiling's at its height if there's one.</summary>
            public List<HoleStrake> Out = new();
            /// <summary>What lies over the planking there (wales, rail caps), each on its strake.</summary>
            public List<HoleStrake> Over = new();
        }

        readonly Dictionary<int, HoleSite> sites = new();
        readonly Dictionary<(int, int), HullHole> holeCache = new();
        readonly object holeLock = new();

        /// <summary>
        /// The hole in a panel at a size step (1 just holed .. <see cref="HullLook.HoleSteps"/> stove in), as broken
        /// planking: the strakes whose middles are within its half-height of its middle (and the one through its middle)
        /// are broken through from seam to seam, each off at its own length either way along the planks (the outer ones
        /// a little shorter), its ends splintered: a few teeth, the break slanting, sometimes a splinter sticking out into
        /// the hole. The ceiling inside is broken through the same strakes, its own way. Fixed by the panel (its middle a
        /// little off the panel's, the same at every size); a bigger step holds the smaller: more strakes, every end
        /// further out, the same teeth.
        /// </summary>
        public HullHole HoleOf(int panel, int step)
        {
            step = Math.Clamp(step, 1, HullLook.HoleSteps);
            lock (holeLock)
            {
                if (holeCache.TryGetValue((panel, step), out var h)) return h;
                if (!sites.TryGetValue(panel, out var site)) sites[panel] = site = Site(panel);
                h = MakeHole(panel, step, site);
                holeCache[(panel, step)] = h;
                return h;
            }
        }

        /// <summary>
        /// A panel's hole frame (outward, along the planks, across them), its middle and its biggest half-sizes, and the
        /// strakes round it: the planking quads near it (outside and the ceiling), joined where they share an end (one
        /// strake runs on from quad to quad) and where they're the same strake at the same heights (on past a port).
        /// </summary>
        HoleSite Site(int panel)
        {
            var p = Layout.Parts[panel];
            var n = Norm(new[] { p.Normal.X, p.Normal.Y, p.Normal.Z }) ?? new double[] { 0, 0, p.Part.Side };
            var u = Norm(new[] { 1 - n[0] * n[0], -n[0] * n[1], -n[0] * n[2] });
            if (u == null || Math.Abs(n[0]) > 0.8) u = Norm(new[] { -n[2] * n[0], -n[2] * n[1], 1 - n[2] * n[2] }) ?? new double[] { 0, 1, 0 };
            var w = Cross(n, u);
            double HalfAlong(double[] d) => 0.5 * (Math.Abs(d[0]) * (p.Max[0] - p.Min[0]) + Math.Abs(d[1]) * (p.Max[1] - p.Min[1]) + Math.Abs(d[2]) * (p.Max[2] - p.Min[2]));
            double ruMax = Math.Max(0.12, Math.Min(0.6, HalfAlong(u) - 0.1)), rwMax = Math.Max(0.1, Math.Min(0.4, HalfAlong(w) - 0.08));
            // not every hole in the middle of its panel (a fixed pick by the panel), still well inside it
            uint hash = (uint)p.Code * 2654435761u;
            double su = ((hash >> 8 & 255) / 127.5 - 1) * Math.Max(0, HalfAlong(u) - 0.1 - ruMax) * 0.6;
            double sw = ((hash >> 16 & 255) / 127.5 - 1) * Math.Max(0, HalfAlong(w) - 0.08 - rwMax) * 0.6;
            var site = new HoleSite
            {
                N = n, U = u, W = w, RuMax = ruMax, RwMax = rwMax,
                C = new[] { p.Center.X + su * u[0] + sw * w[0], p.Center.Y + su * u[1] + sw * w[1], p.Center.Z + su * u[2] + sw * w[2] }
            };

            // the planking quads round it, outside and in
            site.Depth = ThicknessAt(site.C[0], site.C[1], site.C[2], -n[0], -n[1], -n[2]);
            double reachU = ruMax * 1.25 + 0.35, reachW = rwMax + 0.6, reachN = site.Depth + 0.45;
            var lo = new double[3];
            var hi = new double[3];
            for (int a = 0; a < 3; a++)
            {
                double e = Math.Abs(u[a]) * reachU + Math.Abs(w[a]) * reachW + Math.Abs(n[a]) * reachN;
                lo[a] = site.C[a] - e;
                hi[a] = site.C[a] + e;
            }
            var quads = new List<float[]>();
            var inside = new List<bool>();
            foreach (var c in Chunks)
            {
                if (!Overlaps(lo, hi, c.Min, c.Max)) continue;
                var gq = File.Groups[c.Group];
                for (int k = 0; k < c.Quads.Length; k++)
                {
                    var q = gq[c.Quads[k]];
                    if (!QuadIn(q, lo, hi)) continue;
                    quads.Add(q);
                    inside.Add(c.Inside[k]);
                }
            }
            // one strake: quads sharing an end (both corners), in the same layer
            var parent = new int[quads.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            var ends = new Dictionary<string, int>();
            for (int i = 0; i < quads.Count; i++)
                foreach (var (a, b) in new[] { (0, 3), (1, 2) })
                {
                    string key = (inside[i] ? "i" : "o") + EdgeKey(quads[i], a, b);
                    if (ends.TryGetValue(key, out int other)) parent[Find(i)] = Find(other);
                    else ends[key] = i;
                }
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < quads.Count; i++)
            {
                int r = Find(i);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                g.Add(i);
            }
            // where each crosses the hole's middle; the same strake past an opening (same layer, side and seams) is one
            var byKey = new Dictionary<string, HoleStrake>();
            foreach (var g in groups.Values)
            {
                var st = new HoleStrake { Inside = inside[g[0]] };
                foreach (int i in g) st.Quads.Add(quads[i]);
                Seams(st, site);
                double zMid = 0;
                foreach (var q in st.Quads) zMid += (q[3] + q[11] + q[19] + q[27]) / 4 / st.Quads.Count;
                string key = $"{(st.Inside ? 'i' : 'o')}{(zMid > 0.05 ? 1 : zMid < -0.05 ? -1 : 0)}:{Math.Round(st.YLo, 2)}:{Math.Round(st.YHi, 2)}";
                if (byKey.TryGetValue(key, out var same))
                {
                    same.Quads.AddRange(st.Quads);
                    Seams(same, site);
                }
                else byKey[key] = st;
            }
            var outs = new List<HoleStrake>();
            var ins = new List<HoleStrake>();
            var over = new List<HoleStrake>();
            foreach (var st in byKey.Values)
            {
                if (Math.Abs(st.WMid) > rwMax + 0.35) continue;
                double f = 0;
                foreach (var q in st.Quads)
                    for (int k = 0; k < 4; k++) f += (q[6 + 8 * k] * n[0] + q[7 + 8 * k] * n[1] + q[8 + 8 * k] * n[2]) / (4.0 * st.Quads.Count);
                st.Facing = st.Inside ? -f : f;
                bool flat = Math.Abs(st.WHi - st.WLo) < 0.02;
                if (st.Inside) { if (!flat) ins.Add(st); }
                // (a face turned away from outward: a wale's or rail cap's top or bottom)
                else if (flat || st.Facing < 0.6) over.Add(st);
                else outs.Add(st);
            }
            // two strakes at the same height outside: the one standing further out lies over the other
            foreach (var a in outs)
                foreach (var b in outs)
                {
                    if (a == b || a.Over || b.Over) continue;
                    double olo = Math.Max(Math.Min(a.WLo, a.WHi), Math.Min(b.WLo, b.WHi)), ohi = Math.Min(Math.Max(a.WLo, a.WHi), Math.Max(b.WLo, b.WHi));
                    if (ohi - olo > 0.5 * Math.Min(Math.Abs(a.WHi - a.WLo), Math.Abs(b.WHi - b.WLo))) (a.NOff > b.NOff ? a : b).Over = true;
                }
            foreach (var o in outs) if (o.Over) over.Add(o);
            outs.RemoveAll(o => o.Over);
            foreach (var o in over)
            {
                o.Over = true;
                // its strake: the one under its middle, else the nearest
                HoleStrake under = null, nearest = null;
                foreach (var g in outs)
                {
                    if (Math.Min(g.WLo, g.WHi) <= o.WMid && o.WMid <= Math.Max(g.WLo, g.WHi)) under = g;
                    if (nearest == null || Math.Abs(g.WMid - o.WMid) < Math.Abs(nearest.WMid - o.WMid)) nearest = g;
                }
                o.Host = under ?? nearest;
            }
            site.Over = over;
            // (bottom up: across them as each one's lower seam to its upper goes)
            double up = 0;
            foreach (var o in outs) up += o.WHi - o.WLo;
            outs.Sort((a, b) => (up >= 0 ? 1 : -1) * a.WMid.CompareTo(b.WMid));
            // the ceiling's strake at each one's height (the same seams, else the nearest across)
            foreach (var o in outs)
            {
                HoleStrake best = null;
                double bd = double.MaxValue;
                foreach (var i in ins)
                {
                    if (i.Pair != null) continue;
                    double d = Math.Abs(i.YLo - o.YLo) + Math.Abs(i.YHi - o.YHi);
                    if (d > 0.12) d = 1 + Math.Abs(i.WMid - o.WMid) / Math.Max(0.05, o.WHi - o.WLo);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best != null && bd < 1.6) { o.Pair = best; best.Pair = o; }
            }
            // each strake's breaks (its order from the one through the hole's middle picks them)
            int middle = 0;
            for (int k = 0; k < outs.Count; k++) if (Math.Abs(outs[k].WMid) < Math.Abs(outs[middle].WMid)) middle = k;
            uint seed = (uint)p.Code * 0x9E3779B1u ^ 0x5bd1e995u;
            for (int k = 0; k < outs.Count; k++)
            {
                var o = outs[k];
                var rng = new Mix(seed + (uint)(k - middle + 64) * 0x85EBCA6Bu);
                double taper = 1 - 0.3 * Math.Min(1, Sq(o.WMid / rwMax));
                o.T = Breaks(ref rng, out int splinter);
                Teeth(o, ref rng, taper, 0.6, 0.92, splinter);
                if (o.Pair != null)
                {
                    o.Pair.T = o.T;
                    var own = new Mix(seed ^ (uint)(k - middle + 64) * 0xC2B2AE35u);
                    Teeth(o.Pair, ref own, taper, 0.5, 0.88, splinter);
                }
            }
            // her shell's thickness there: between the planking's strakes and the ceiling's (hull R: thicker low down)
            var gaps = outs.Where(o => o.Pair != null).Select(o => o.NOff - o.Pair.NOff).Where(v => v > 0.05 && v < 1.6).OrderBy(v => v).ToList();
            if (gaps.Count > 0) site.Depth = Math.Max(Thickness, gaps[gaps.Count / 2]);
            site.Out = outs;
            return site;
        }

        static string EdgeKey(float[] q, int a, int b)
        {
            string P(int k) => $"{Math.Round(q[1 + 8 * k] * 500)},{Math.Round(q[2 + 8 * k] * 500)},{Math.Round(q[3 + 8 * k] * 500)}";
            string pa = P(a), pb = P(b);
            return string.CompareOrdinal(pa, pb) < 0 ? pa + "|" + pb : pb + "|" + pa;
        }

        static bool QuadIn(float[] q, double[] lo, double[] hi)
        {
            for (int a = 0; a < 3; a++)
            {
                double ql = Math.Min(Math.Min(q[1 + a], q[9 + a]), Math.Min(q[17 + a], q[25 + a]));
                double qh = Math.Max(Math.Max(q[1 + a], q[9 + a]), Math.Max(q[17 + a], q[25 + a]));
                if (qh < lo[a] || ql > hi[a]) return false;
            }
            return true;
        }

        static double UOf(float[] q, int k, double[] c, double[] d) =>
            (q[1 + 8 * k] - c[0]) * d[0] + (q[2 + 8 * k] - c[1]) * d[1] + (q[3 + 8 * k] - c[2]) * d[2];

        /// <summary>A strake's seams where it crosses the hole's middle (along U = 0): across the hole and their heights.</summary>
        static void Seams(HoleStrake st, HoleSite site)
        {
            float[] best = null;
            double bd = double.MaxValue;
            foreach (var q in st.Quads)
            {
                double a = UOf(q, 0, site.C, site.U), b = UOf(q, 1, site.C, site.U);
                double d = Math.Max(0, Math.Max(Math.Min(a, b), -Math.Max(a, b)));
                if (d < bd) { bd = d; best = q; }
            }
            if (best == null) return;
            double Along(int k0, int k1, double[] dir)
            {
                double a = UOf(best, k0, site.C, site.U), b = UOf(best, k1, site.C, site.U);
                double f = Math.Abs(b - a) < 1e-9 ? 0.5 : Math.Clamp(-a / (b - a), -0.5, 1.5);
                return UOf(best, k0, site.C, dir) + f * (UOf(best, k1, site.C, dir) - UOf(best, k0, site.C, dir));
            }
            var up = new double[] { 0, 1, 0 };
            st.WLo = Along(0, 1, site.W);
            st.WHi = Along(3, 2, site.W);
            st.NOff = (Along(0, 1, site.N) + Along(3, 2, site.N)) / 2;
            st.YLo = Along(0, 1, up) + site.C[1];
            st.YHi = Along(3, 2, up) + site.C[1];
        }

        /// <summary>A small fixed sequence of numbers from a seed (SplitMix), the same everywhere.</summary>
        struct Mix
        {
            ulong s;
            public Mix(uint seed) { s = seed * 0x9E3779B97F4A7C15ul + 0x632BE59BD9B4E019ul; }
            public double Next()
            {
                ulong z = s += 0x9E3779B97F4A7C15ul;
                z = (z ^ z >> 30) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ z >> 27) * 0x94D049BB133111EBul;
                return ((z ^ z >> 31) >> 11) * (1.0 / (1ul << 53));
            }
            public double Range(double a, double b) => a + (b - a) * Next();
        }

        /// <summary>
        /// Where a strake's broken ends turn across it (0 at its lower seam .. 1 at its upper): two or three teeth, and
        /// maybe a splinter (its tip's index, else -1).
        /// </summary>
        static double[] Breaks(ref Mix rng, out int splinter)
        {
            var t = new List<double> { 0, 1 };
            int teeth = 2 + (rng.Next() < 0.5 ? 1 : 0);
            for (int k = 0; k < teeth; k++) t.Add((k + 0.2 + 0.6 * rng.Next()) / teeth);
            double tip = -1;
            if (rng.Next() < 0.6)
            {
                tip = rng.Range(0.25, 0.75);
                t.Add(tip - 0.07);
                t.Add(tip);
                t.Add(tip + 0.07);
            }
            t.Sort();
            var o = new List<double>();
            foreach (double v in t)
            {
                // (two too close together: the splinter's tip and the upper seam stay)
                if (o.Count > 0 && v - o[^1] <= 0.03)
                {
                    if (v == tip || v == 1) o[^1] = v;
                    continue;
                }
                o.Add(v);
            }
            splinter = tip < 0 ? -1 : o.IndexOf(tip);
            return o.ToArray();
        }

        /// <summary>
        /// A strake's two broken ends, relative (sized by the hole's step in <see cref="MakeHole"/>): each its length (a
        /// share of the hole's half-length, between lo and hi, times taper) and its teeth (in and out of the hole by up to
        /// a fifth, the break slanting); a splinter, on one end, sticks half its length out into the hole.
        /// </summary>
        static void Teeth(HoleStrake st, ref Mix rng, double taper, double lo, double hi, int splinter)
        {
            int n = st.T.Length;
            st.PL = new double[n];
            st.PR = new double[n];
            st.BL = rng.Range(lo, hi) * taper;
            st.BR = rng.Range(lo, hi) * taper;
            bool splinterRight = rng.Next() < 0.5, hasSplinter = splinter >= 0 && rng.Next() < 0.75;
            foreach (var p in new[] { st.PL, st.PR })
            {
                double slope = rng.Range(-0.3, 0.3);
                double sign = rng.Next() < 0.5 ? 1 : -1;
                for (int i = 0; i < n; i++)
                {
                    p[i] = Math.Clamp(sign * rng.Range(0.05, 0.2) + slope * (st.T[i] - 0.5), -0.4, 0.3);
                    sign = -sign;
                }
            }
            if (hasSplinter) (splinterRight ? st.PR : st.PL)[splinter] = -0.5;
        }

        /// <summary>A panel's hole at a size step: its broken strakes, their ends sized, and what it reaches.</summary>
        HullHole MakeHole(int panel, int step, HoleSite site)
        {
            double s = 0.55 + 0.45 * (step - 1) / (HullLook.HoleSteps - 1.0);
            double rw = site.RwMax * s, ru = site.RuMax * s;
            var strakes = new List<HoleStrake>();
            var byQuad = new Dictionary<float[], HoleStrake>();
            double areaOut = 0, areaIn = 0, maxU = 0.05, maxW = 0.05;
            HoleStrake Sized(HoleStrake g)
            {
                var st = new HoleStrake
                {
                    Inside = g.Inside, Quads = g.Quads, WLo = g.WLo, WHi = g.WHi, YLo = g.YLo, YHi = g.YHi, T = g.T,
                    PL = g.PL, PR = g.PR, BL = g.BL, BR = g.BR, Left = new double[g.T.Length], Right = new double[g.T.Length]
                };
                for (int i = 0; i < st.T.Length; i++)
                {
                    st.Left[i] = -ru * g.BL * (1 + g.PL[i]);
                    st.Right[i] = ru * g.BR * (1 + g.PR[i]);
                    maxU = Math.Max(maxU, Math.Max(-st.Left[i], st.Right[i]));
                }
                maxW = Math.Max(maxW, Math.Max(Math.Abs(st.WLo), Math.Abs(st.WHi)));
                double area = 0;
                for (int i = 0; i + 1 < st.T.Length; i++)
                    area += (st.T[i + 1] - st.T[i]) * (st.Right[i] - st.Left[i] + st.Right[i + 1] - st.Left[i + 1]) / 2;
                area *= Math.Abs(st.WHi - st.WLo);
                if (st.Inside) areaIn += area; else areaOut += area;
                foreach (var q in st.Quads) byQuad[q] = st;
                strakes.Add(st);
                return st;
            }
            HoleStrake middle = null;
            foreach (var g in site.Out) if (middle == null || Math.Abs(g.WMid) < Math.Abs(middle.WMid)) middle = g;
            var broken = new Dictionary<HoleStrake, HoleStrake>();
            foreach (var g in site.Out)
            {
                if (g.T == null || Math.Abs(g.WMid) > rw && !(Math.Min(g.WLo, g.WHi) <= 0 && Math.Max(g.WLo, g.WHi) >= 0) && g != middle) continue;
                var o = Sized(g);
                broken[g] = o;
                if (g.Pair?.T != null)
                {
                    var i = Sized(g.Pair);
                    o.Pair = i;
                    i.Pair = o;
                }
            }
            // what lies over a broken strake (a wale) is broken with it: its ends where the strake's are at its height
            foreach (var g in site.Over)
            {
                if (g.Host == null || !broken.TryGetValue(g.Host, out var host)) continue;
                var tl = new List<double> { 0, 1 };
                double span = g.WHi - g.WLo, hs = host.WHi - host.WLo;
                double HostT(double t) => Math.Abs(hs) < 1e-9 ? 0.5 : Math.Clamp((g.WLo + t * span - host.WLo) / hs, 0, 1);
                if (Math.Abs(span) > 1e-6)
                    foreach (double th in host.T)
                    {
                        double t = (host.WLo + th * hs - g.WLo) / span;
                        if (t > 1e-6 && t < 1 - 1e-6) tl.Add(t);
                    }
                tl.Sort();
                var st = new HoleStrake
                {
                    Inside = false, Over = true, Host = host, Quads = g.Quads, WLo = g.WLo, WHi = g.WHi, YLo = g.YLo, YHi = g.YHi,
                    NOff = g.NOff - g.Host.NOff, Facing = g.Facing,
                    T = tl.ToArray(), Left = new double[tl.Count], Right = new double[tl.Count]
                };
                for (int i = 0; i < tl.Count; i++)
                {
                    st.Left[i] = host.LeftAt(HostT(tl[i]));
                    st.Right[i] = host.RightAt(HostT(tl[i]));
                }
                foreach (var q in st.Quads) byQuad[q] = st;
                strakes.Add(st);
            }
            var h = new HullHole
            {
                Panel = panel, C = site.C, N = site.N, U = site.U, W = site.W, Ru = maxU, Rw = maxW, Reach = site.Depth + 0.45, Depth = site.Depth,
                Strakes = strakes.ToArray(), ByQuad = byQuad, AreaOut = areaOut, AreaIn = areaIn,
                Min = new double[3], Max = new double[3]
            };
            for (int a = 0; a < 3; a++)
            {
                double e = Math.Abs(h.U[a]) * h.Ru + Math.Abs(h.W[a]) * h.Rw + Math.Abs(h.N[a]) * h.Reach + 0.05;
                h.Min[a] = h.C[a] - e;
                h.Max[a] = h.C[a] + e;
            }
            return h;
        }

        /// <summary>
        /// Where a point (ship-local) is for a hole: below 0 inside it (on one of the strakes it breaks, between that
        /// one's broken ends: how far in from the nearer, along the planks), above 0 on them outside it, 1 off them. For the
        /// offline checks (the cut itself works on each quad's own coordinates).
        /// </summary>
        public static double HoleValue(in HullHole h, double x, double y, double z)
        {
            double best = 1;
            if (h.Strakes == null) return best;
            double u = (x - h.C[0]) * h.U[0] + (y - h.C[1]) * h.U[1] + (z - h.C[2]) * h.U[2];
            foreach (var st in h.Strakes)
                foreach (var q in st.Quads)
                    if (OnQuad(q, x, y, z, out double t))
                    {
                        double v = Math.Max(st.LeftAt(t) - u, u - st.RightAt(t));
                        // (on its seams it's the hole's edge: the strake over or under it is whole there)
                        if (t <= 0.002 || t >= 0.998) v = Math.Max(v, 0);
                        best = Math.Min(best, v);
                    }
            return best;
        }

        /// <summary>Is a point on a quad (within a couple of centimetres of it): where across it (t, as the cut has it).</summary>
        static bool OnQuad(float[] q, double x, double y, double z, out double t)
        {
            t = 0;
            foreach (var (a, b, c, ta, tb, tc) in new[] { (0, 1, 2, 0.0, 0.0, 1.0), (0, 2, 3, 0.0, 1.0, 1.0) })
            {
                double ax = q[1 + 8 * a], ay = q[2 + 8 * a], az = q[3 + 8 * a];
                double e1x = q[1 + 8 * b] - ax, e1y = q[2 + 8 * b] - ay, e1z = q[3 + 8 * b] - az;
                double e2x = q[1 + 8 * c] - ax, e2y = q[2 + 8 * c] - ay, e2z = q[3 + 8 * c] - az;
                double nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl < 1e-12) continue;
                double px = x - ax, py = y - ay, pz = z - az;
                if (Math.Abs((px * nx + py * ny + pz * nz) / nl) > 0.02) continue;
                // barycentric coordinates in the triangle's plane
                double d11 = e1x * e1x + e1y * e1y + e1z * e1z, d12 = e1x * e2x + e1y * e2y + e1z * e2z, d22 = e2x * e2x + e2y * e2y + e2z * e2z;
                double p1 = px * e1x + py * e1y + pz * e1z, p2 = px * e2x + py * e2y + pz * e2z;
                double den = d11 * d22 - d12 * d12;
                if (Math.Abs(den) < 1e-18) continue;
                double l1 = (d22 * p1 - d12 * p2) / den, l2 = (d11 * p2 - d12 * p1) / den;
                if (l1 < -1e-4 || l2 < -1e-4 || l1 + l2 > 1 + 1e-4) continue;
                t = ta + l1 * (tb - ta) + l2 * (tc - ta);
                return true;
            }
            return false;
        }

        static bool QuadNear(float[] q, in HullHole h)
        {
            for (int a = 0; a < 3; a++)
            {
                double lo = Math.Min(Math.Min(q[1 + a], q[9 + a]), Math.Min(q[17 + a], q[25 + a]));
                double hi = Math.Max(Math.Max(q[1 + a], q[9 + a]), Math.Max(q[17 + a], q[25 + a]));
                if (hi < h.Min[a] || lo > h.Max[a]) return false;
            }
            return true;
        }

        /// <summary>
        /// Cut the holes out of one planking quad: if it's part of a strake a hole breaks, what's between that strake's
        /// broken ends is taken out of it, exactly: across it in slabs between the points where an end turns (or crosses
        /// another, or the quad's own ends), each piece kept a quad between two straight edges. False if no hole breaks it
        /// here (keep it whole); a quad all inside a hole gives nothing.
        /// </summary>
        static bool Cut(float[] q, int tex, List<HullHole> holes, List<float[]> list)
        {
            var cuts = new List<(HoleStrake S, double U0, double U1, double U2, double U3)>(2);
            foreach (var h in holes)
            {
                if (h.ByQuad == null || !h.ByQuad.TryGetValue(q, out var st)) continue;
                cuts.Add((st, UOf(q, 0, h.C, h.U), UOf(q, 1, h.C, h.U), UOf(q, 2, h.C, h.U), UOf(q, 3, h.C, h.U)));
            }
            if (cuts.Count == 0) return false;
            int lines = 2 * cuts.Count;
            // a broken end (line: 2 × the cut, + 1 for its right end) as a place along the quad (s) at t across it
            double S(int line, double t)
            {
                var (st, u0, u1, u2, u3) = cuts[line >> 1];
                double a = u0 + (u3 - u0) * t, b = u1 + (u2 - u1) * t - a;
                double target = (line & 1) == 0 ? st.LeftAt(t) : st.RightAt(t);
                if (Math.Abs(b) < 1e-9) return target > a ? 1e9 : -1e9;
                return (target - a) / b;
            }
            var ts = new List<double> { 0, 1 };
            foreach (var c in cuts) foreach (double t in c.S.T) if (t > 0 && t < 1) ts.Add(t);
            ts.Sort();
            // where an end crosses another or the quad's own ends between those
            var extra = new List<double>();
            for (int k = 0; k + 1 < ts.Count; k++)
            {
                double ta = ts[k], tb = ts[k + 1];
                if (tb - ta < 1e-9) continue;
                for (int i = 0; i < lines; i++)
                {
                    Crossing(t => S(i, t), ta, tb, extra);
                    Crossing(t => S(i, t) - 1, ta, tb, extra);
                    for (int j = i + 1; j < lines; j++) Crossing(t => S(i, t) - S(j, t), ta, tb, extra);
                }
            }
            ts.AddRange(extra);
            ts.Sort();
            var pieces = new List<float[]>();
            bool cutAny = false;
            var spans = new List<(double Lo, int LoLine, double Hi, int HiLine)>();
            for (int k = 0; k + 1 < ts.Count; k++)
            {
                double ta = ts[k], tb = ts[k + 1];
                if (tb - ta < 1e-7) continue;
                double tm = (ta + tb) / 2;
                spans.Clear();
                for (int e = 0; e < cuts.Count; e++)
                {
                    double sl = S(2 * e, tm), sr = S(2 * e + 1, tm);
                    var span = sl <= sr ? (sl, 2 * e, sr, 2 * e + 1) : (sr, 2 * e + 1, sl, 2 * e);
                    if (span.Item3 <= 0 || span.Item1 >= 1) continue;
                    spans.Add(span);
                }
                if (spans.Count == 0)
                {
                    pieces.Add(Piece(q, tex, (0, ta), (1, ta), (1, tb), (0, tb)));
                    continue;
                }
                cutAny = true;
                spans.Sort((a, b) => a.Lo.CompareTo(b.Lo));
                // what's kept: from the quad's start to the first span, between spans that don't overlap, to its end
                int left = -1;
                double at = 0;
                for (int i = 0; i <= spans.Count; i++)
                {
                    double lo = i < spans.Count ? spans[i].Lo : 1;
                    int loLine = i < spans.Count ? spans[i].LoLine : -2;
                    if (lo > at + 1e-6)
                    {
                        double Edge(int line, double t) => line == -1 ? 0 : line == -2 ? 1 : Math.Clamp(S(line, t), 0, 1);
                        double a0 = Edge(left, ta), a1 = Edge(loLine, ta), b0 = Edge(left, tb), b1 = Edge(loLine, tb);
                        if (a1 < a0) a0 = a1 = (a0 + a1) / 2;
                        if (b1 < b0) b0 = b1 = (b0 + b1) / 2;
                        if (a1 - a0 > 1e-6 || b1 - b0 > 1e-6) pieces.Add(Piece(q, tex, (a0, ta), (a1, ta), (b1, tb), (b0, tb)));
                    }
                    if (i == spans.Count) break;
                    // this span and any it overlaps
                    double hi = spans[i].Hi;
                    int hiLine = spans[i].HiLine;
                    while (i + 1 < spans.Count && spans[i + 1].Lo <= hi)
                    {
                        i++;
                        if (spans[i].Hi > hi) { hi = spans[i].Hi; hiLine = spans[i].HiLine; }
                    }
                    left = hiLine;
                    at = hi;
                    if (at >= 1) break;
                }
            }
            if (!cutAny) return false;
            list.AddRange(pieces);
            return true;
        }

        /// <summary>Where f (continuous) changes sign between ta and tb, to a hair (bisection), added to the list.</summary>
        static void Crossing(Func<double, double> f, double ta, double tb, List<double> into)
        {
            double fa = f(ta), fb = f(tb);
            if (double.IsNaN(fa) || double.IsNaN(fb) || Math.Abs(fa) > 1e8 || Math.Abs(fb) > 1e8 || fa * fb >= 0) return;
            for (int k = 0; k < 40; k++)
            {
                double tm = (ta + tb) / 2, fm = f(tm);
                if (fa * fm <= 0) { tb = tm; fb = fm; }
                else { ta = tm; fa = fm; }
            }
            into.Add((ta + tb) / 2);
        }

        /// <summary>A point of a quad at (s, t), bilinear between its corners: x y z u v nx ny nz.</summary>
        static void At(float[] q, double s, double t, double[] o)
        {
            double w0 = (1 - s) * (1 - t), w1 = s * (1 - t), w2 = s * t, w3 = (1 - s) * t;
            for (int a = 0; a < 8; a++) o[a] = w0 * q[1 + a] + w1 * q[9 + a] + w2 * q[17 + a] + w3 * q[25 + a];
        }

        static float[] Piece(float[] q, int tex, (double S, double T) a, (double S, double T) b, (double S, double T) c, (double S, double T) d)
        {
            var r = new float[33];
            r[0] = tex;
            var p = new double[8];
            var corners = new[] { a, b, c, d };
            for (int k = 0; k < 4; k++)
            {
                At(q, corners[k].S, corners[k].T, p);
                double ln = Math.Sqrt(p[5] * p[5] + p[6] * p[6] + p[7] * p[7]);
                if (ln > 1e-9) { p[5] /= ln; p[6] /= ln; p[7] /= ln; }
                for (int e = 0; e < 8; e++) r[1 + 8 * k + e] = (float)p[e];
            }
            return r;
        }

        /// <summary>A hole's reveal on its own (the quads <see cref="Build"/> adds with its panel's outside planking).</summary>
        public List<float[]> Reveal(in HullHole h)
        {
            var list = new List<float[]>();
            Reveal(h, list);
            return list;
        }

        /// <summary>
        /// The hole's reveal (raw oak: the broken planks' ends and the edges of those left): round its outline on the
        /// planking outside (up its strakes' right ends, across its top seam, down their left ends, across its bottom
        /// seam) to the same points of the ceiling's (its strake at each one's height, broken its own way; the planking's
        /// thickness in where there's no ceiling), facing into the hole. Where the two run opposite ways (a step between
        /// strakes the other way round inside), the face is two triangles meeting where it crosses itself.
        /// </summary>
        void Reveal(in HullHole h, List<float[]> list)
        {
            if (h.Strakes == null) return;
            var outs = new List<HoleStrake>();
            double up = 0;
            foreach (var st in h.Strakes) if (!st.Inside && !st.Over) { outs.Add(st); up += st.WHi - st.WLo; }
            if (outs.Count == 0) return;
            // bottom up (as each one's lower seam to its upper goes across them)
            outs.Sort((a, b) => (up >= 0 ? 1 : -1) * a.WMid.CompareTo(b.WMid));
            var ring = new List<(double[] O, double[] I)>();
            var hole = h;
            void Add(HoleStrake st, double t, bool right)
            {
                var o = PointOn(st, t, right ? st.RightAt(t) : st.LeftAt(t), hole);
                var i = st.Pair != null
                    ? PointOn(st.Pair, t, right ? st.Pair.RightAt(t) : st.Pair.LeftAt(t), hole)
                    : new[] { o[0] - hole.N[0] * hole.Depth, o[1] - hole.N[1] * hole.Depth, o[2] - hole.N[2] * hole.Depth };
                ring.Add((o, i));
            }
            foreach (var st in outs)
                foreach (double t in st.T) Add(st, t, true);
            for (int j = outs.Count - 1; j >= 0; j--)
                for (int k = outs[j].T.Length - 1; k >= 0; k--) Add(outs[j], outs[j].T[k], false);
            int tex = RawOakTexture();
            // which way round it goes seen from outside (the hole on its left: anticlockwise)
            double area = 0;
            for (int k = 0; k < ring.Count; k++)
            {
                var a = ring[k].O;
                var b = ring[(k + 1) % ring.Count].O;
                double ua = UOf3(a, h.C, h.U), wa = UOf3(a, h.C, h.W), ub = UOf3(b, h.C, h.U), wb = UOf3(b, h.C, h.W);
                area += ua * wb - ub * wa;
            }
            double sign = area >= 0 ? 1 : -1;
            for (int k = 0; k < ring.Count; k++) Wall(h.N, sign, ring[k], ring[(k + 1) % ring.Count], k, tex, list);
            // a wale's broken ends: from its face back to the planking it stands on (its top and bottom are cut to them)
            foreach (var st in h.Strakes)
            {
                if (!st.Over || st.Facing < 0.6 || Math.Abs(st.WHi - st.WLo) < 0.02 || st.NOff < 0.01) continue;
                foreach (bool right in new[] { true, false })
                {
                    var end = new List<(double[] O, double[] I)>();
                    for (int i = 0; i < st.T.Length; i++)
                    {
                        double t = right ? st.T[i] : st.T[st.T.Length - 1 - i];
                        var o = PointOn(st, t, right ? st.RightAt(t) : st.LeftAt(t), hole);
                        end.Add((o, new[] { o[0] - hole.N[0] * st.NOff, o[1] - hole.N[1] * st.NOff, o[2] - hole.N[2] * st.NOff }));
                    }
                    for (int k = 0; k + 1 < end.Count; k++) Wall(h.N, sign, end[k], end[k + 1], k, tex, list);
                }
            }
        }

        /// <summary>The point of a strake's planking at t across it and u along the hole (the nearest end of it, past its ends).</summary>
        static double[] PointOn(HoleStrake st, double t, double u, in HullHole h)
        {
            float[] best = null;
            double bestS = 0, bd = double.MaxValue;
            foreach (var q in st.Quads)
            {
                double u0 = UOf(q, 0, h.C, h.U), u1 = UOf(q, 1, h.C, h.U), u2 = UOf(q, 2, h.C, h.U), u3 = UOf(q, 3, h.C, h.U);
                double a = u0 + (u3 - u0) * t, b = u1 + (u2 - u1) * t - a;
                if (Math.Abs(b) < 1e-9) continue;
                double s = (u - a) / b, d = s < 0 ? -s * Math.Abs(b) : s > 1 ? (s - 1) * Math.Abs(b) : 0;
                if (d < bd) { bd = d; best = q; bestS = Math.Clamp(s, 0, 1); }
                if (d == 0) break;
            }
            var p = new double[8];
            if (best == null) return (double[])h.C.Clone();
            At(best, bestS, t, p);
            return new[] { p[0], p[1], p[2] };
        }

        /// <summary>One face of the reveal between two points of the outline (outside, inside) and the next two.</summary>
        static void Wall(double[] n, double sign, (double[] O, double[] I) a, (double[] O, double[] I) b, int k, int tex, List<float[]> list)
        {
            var eo = Sub(b.O, a.O);
            var ei = Sub(b.I, a.I);
            double lo = Len(eo), li = Len(ei);
            if (lo < 1e-5 && li < 1e-5) return;
            if (lo > 1e-5 && li > 1e-5 && Dot(eo, ei) < 0)
            {
                // the edge across shrinks to nothing on the way in: two triangles meeting there
                double lam = Math.Clamp(Dot(eo, eo) / Math.Max(1e-12, Dot(eo, Sub(eo, ei))), 0, 1);
                var pa = Lerp(a.O, a.I, lam);
                var pb = Lerp(b.O, b.I, lam);
                var x = Lerp(pa, pb, 0.5);
                Face(n, sign, eo, a.O, b.O, x, x, k, tex, list);
                Face(n, sign, ei, x, x, b.I, a.I, k, tex, list);
                return;
            }
            Face(n, sign, lo >= li ? eo : ei, a.O, b.O, b.I, a.I, k, tex, list);
        }

        /// <summary>A reveal face (p0 p1 outside, p2 p3 inside; a triangle repeats a corner) facing into the hole: left of the outline's way seen from outside.</summary>
        static void Face(double[] n, double sign, double[] along, double[] p0, double[] p1, double[] p2, double[] p3, int k, int tex, List<float[]> list)
        {
            var into = Norm(Cross(n, along));
            if (into != null && sign < 0) into = new[] { -into[0], -into[1], -into[2] };
            if (into == null) return;
            var f = Norm(Cross(Sub(p1, p0), Sub(p3, p0))) ?? Norm(Cross(Sub(p2, p1), Sub(p0, p1))) ?? into;
            if (Dot(f, into) < 0) f = new[] { -f[0], -f[1], -f[2] };
            double len = Math.Max(Len(Sub(p1, p0)), Len(Sub(p2, p3)));
            double depth = Math.Max(Len(Sub(p3, p0)), Len(Sub(p2, p1)));
            float u0 = (float)(k * 5 % 16 * 0.6), u1 = u0 + (float)Math.Min(6, Math.Max(0.25, len * 16)), v1 = (float)Math.Min(16, Math.Max(0.5, depth * 16));
            var q = new float[33];
            q[0] = tex;
            void C(int c, double[] p, float u, float v)
            {
                int o = 1 + 8 * c;
                q[o] = (float)p[0]; q[o + 1] = (float)p[1]; q[o + 2] = (float)p[2];
                q[o + 3] = u; q[o + 4] = v;
                q[o + 5] = (float)f[0]; q[o + 6] = (float)f[1]; q[o + 7] = (float)f[2];
            }
            C(0, p0, u0, 0);
            C(1, p1, u1, 0);
            C(2, p2, u1, v1);
            C(3, p3, u0, v1);
            list.Add(q);
        }

        static double UOf3(double[] p, double[] c, double[] d) => (p[0] - c[0]) * d[0] + (p[1] - c[1]) * d[1] + (p[2] - c[2]) * d[2];
        static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static double[] Lerp(double[] a, double[] b, double f) => new[] { a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f };
        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static double Len(double[] a) => Math.Sqrt(Dot(a, a));

        static double Sq(double v) => v * v;

        static double[] Norm(double[] v)
        {
            double l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return l < 1e-6 ? null : new[] { v[0] / l, v[1] / l, v[2] / l };
        }

        static double[] Cross(double[] a, double[] b) =>
            new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    }
}
