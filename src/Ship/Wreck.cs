using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    /// <summary>What's at a world block, for a wreck: free (air or water), soft (a plant: it may go), ground, or one of her own wreck blocks.</summary>
    public enum WreckGround : byte { Free = 0, Soft = 1, Ground = 2, Ours = 3 }

    /// <summary>The world a wreck is laid in (the game's on the server; a made-up seabed in the offline tests).</summary>
    public interface IWreckSite
    {
        /// <summary>What's at this block (its solid layer: water in the fluid layer is free); a ground block's top over its foot (0..1).</summary>
        WreckGround At(int x, int y, int z, out float top);
    }

    /// <summary>
    /// What a wreck is made from: a ship design, where and how she lies, her damage, what she carried. Nothing of a live
    /// ship is needed: the Earth map's worldgen can make one up (a design, a place, a yaw, a damage state, a loot list).
    /// </summary>
    public class WreckInput
    {
        public ShipDefinition Def;
        /// <summary>Her origin (the waterline amidships) in the world, as she lies on the bottom.</summary>
        public double X, Y, Z;
        /// <summary>Radians: her heading (EntityShip.Pos.Yaw), her list (+ starboard down) and trim (+ bow up), as she went down.</summary>
        public double Yaw, List, Trim;
        /// <summary>Each damage part's hurt (0..1, in ShipDefinition.DamageParts order), or null: sound.</summary>
        public float[] Hurt;
        /// <summary>Sheathed in copper: her bottom's outside cells are copper.</summary>
        public bool Clad;
        /// <summary>(1 November 2026) What in: "copper", "brass" or "lead".</summary>
        public string CladMetal = "copper";
        /// <summary>
        /// (0.3.20, step B) Her deck cells that differ from her design (slot id → state: 2 taken up): a cell taken up isn't
        /// in her wreck (its planks went to whoever took it up), one laid over a design opening is. Null: as designed. A
        /// hatch over an opening is a piece (its trapdoors in her loot), not her cells.
        /// </summary>
        public Dictionary<string, int> Slots;
        /// <summary>(0.3.21, step C) Her gun places without their gun (the gun fixtures' codes): no gun of theirs in her wreck. Null: all there.</summary>
        public string[] GunsGone;
        /// <summary>Her anchor lies on the seabed beside her bow, as iron.</summary>
        public bool Anchor = true;
        /// <summary>Picks every random choice (her entity id's); the same seed and input make the same wreck.</summary>
        public long Seed;
        /// <summary>Whose wreck blocks are hers (her entity id; a made-up one for worldgen): built again over them, they're free.</summary>
        public long WreckId;
        /// <summary>The fixtures that held a container (a chest, trunk, vessel or crate): a place is found for each.</summary>
        public string[] Containers = Array.Empty<string>();
        /// <summary>Containers that stood where her design has no fixture (pieces put aboard): ship-local x, y, z and their yaw (degrees).</summary>
        public System.Collections.Generic.Dictionary<string, double[]> ContainerPlaces;
        /// <summary>How many more chests (for what she carried loose) go on the seabed beside her.</summary>
        public int ExtraChests;
        /// <summary>Broken up (decay, the upper works, a castle), holed where she was, her masts fallen beside her, her guns placed (the tests turn them off).</summary>
        public bool Decay = true, Holes = true, Masts = true, Guns = true;
        /// <summary>How far she settles into the seabed (blocks).</summary>
        public double Settle = 0.3;
        public int MaxSediment = 24;
    }

    /// <summary>A place for one thing: a container, a gun, an extra chest, a heap of sand. Side: which way it faces (north, east, south, west).</summary>
    public class WreckSpot
    {
        public string Fixture;
        public int X, Y, Z;
        /// <summary>In her hull where it stood (else on the seabed beside her).</summary>
        public bool InWreck;
        public string Side = "north";
        public override string ToString() => $"{Fixture ?? "-"} at {X},{Y},{Z}{(InWreck ? " (in her)" : "")} {Side}";
    }

    /// <summary>A wreck worked out: her blocks (world block → cells) and where everything else goes, and some numbers.</summary>
    public class WreckPlan
    {
        public WreckInput Input;
        public readonly Dictionary<long, WreckCells> Blocks = new();
        /// <summary>For each of Input.Containers, in order (null where nothing could be found).</summary>
        public readonly List<WreckSpot> Containers = new();
        public readonly List<WreckSpot> Extra = new(), Guns = new(), Sediment = new();
        public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        /// <summary>Her cells kept by the design (her hull up to her castles, the mast stumps), lost to decay and holes, laid in the world, buried, in the way of ground, as timbers and the anchor.</summary>
        public int CellsDesign, CellsDecayed, CellsHoled, CellsPlaced, CellsBuried, CellsBlocked, CellsTimber, CellsAnchor;
        /// <summary>(0.3.26) The hull panels her wreck's decay stove in (their ids).</summary>
        public readonly System.Collections.Generic.List<string> PanelsBreached = new();

        public static long Key(int x, int y, int z) => ((long)(x + (1 << 25)) & 0x3FFFFFF) << 38 | ((long)y & 0xFFF) << 26 | ((long)(z + (1 << 25)) & 0x3FFFFFF);

        public static void Unkey(long k, out int x, out int y, out int z)
        {
            x = (int)(k >> 38 & 0x3FFFFFF) - (1 << 25);
            y = (int)(k >> 26 & 0xFFF);
            z = (int)(k & 0x3FFFFFF) - (1 << 25);
        }

        /// <summary>Her blocks bottom up (the order they're placed in).</summary>
        public List<long> Order()
        {
            var keys = Blocks.Keys.ToList();
            keys.Sort((a, b) =>
            {
                Unkey(a, out int ax, out int ay, out int az);
                Unkey(b, out int bx, out int by, out int bz);
                return ay != by ? ay.CompareTo(by) : ax != bx ? ax.CompareTo(bx) : az.CompareTo(bz);
            });
            return keys;
        }

        /// <summary>A fingerprint of her blocks and spots (the same wreck made twice must match).</summary>
        public ulong Fingerprint()
        {
            ulong h = 1469598103934665603UL;
            void Add(ulong v) { h ^= v; h *= 1099511628211UL; h ^= h >> 29; }
            foreach (long k in Order())
            {
                Add((ulong)k);
                var c = Blocks[k];
                for (int i = 0; i < c.Mats.Length; i++) { Add(c.Mats[i]); Add(c.Masks[i]); }
            }
            foreach (var s in Containers.Concat(Extra).Concat(Guns).Concat(Sediment))
                if (s != null) Add((ulong)Key(s.X, s.Y, s.Z) ^ (ulong)(s.Side?.GetHashCode() ?? 0) ^ (s.InWreck ? 7UL : 0));
            return h;
        }
    }

    /// <summary>
    /// Makes a wreck out of a ship design, where and how she lies on the seabed (Hansa 0.3.5, task 5):
    /// - her hull from the design's collision grid (0.25 cells), turned by her yaw, list and trim and laid in the world's
    ///   own 0.25 cells (each world cell takes the design cell its middle falls in): tarred oak outside (copper below her
    ///   waterline if she was sheathed), weathered oak inside, her decks and woodwork; the masts' stumps in pine
    /// - broken up: holes where she was holed (an ellipse on the panel, through her planking), a few where she was cracked;
    ///   planks gone here and there, her woodwork inside more, her upper works more the higher they are, one castle fallen
    ///   in; her masts above the stumps fallen as a few long timbers on the seabed beside her, to the side she lies over to
    /// - settled into the seabed: cells below the seabed's surface (the top of the first ground block under them) and in
    ///   any ground block are left out; plants (no collision) are no obstacle; water stays where she isn't
    /// - her anchor beside her bow as iron, her guns where they stood if there's room, else tumbled onto the seabed beside
    ///   her; her cargo's containers where they stood in her hold if there's room, else beside her; extra chests; a little
    ///   sand or gravel heaped against her lowest blocks
    /// Every choice comes from the seed (her entity id), so the same input makes the same wreck: built again after a
    /// reload it lays the same blocks over the ones it laid before (they're hers: <see cref="WreckGround.Ours"/>).
    /// </summary>
    public static class WreckBuilder
    {
        public static WreckPlan Build(WreckInput w, IWreckSite site) => new Run(w, site).Make();

        // ------------------------------------------------------------------ hashing and noise (the same everywhere)

        public static double H(long seed, int salt, int a, int b = 0, int c = 0)
        {
            ulong h = (ulong)seed ^ (ulong)(uint)salt * 0x9E3779B97F4A7C15UL;
            h = Mix(h ^ (ulong)(uint)a * 0xBF58476D1CE4E5B9UL);
            h = Mix(h ^ (ulong)(uint)b * 0x94D049BB133111EBUL);
            h = Mix(h ^ (ulong)(uint)c * 0xD6E8FEB86659FD93UL);
            return (h >> 11) * (1.0 / 9007199254740992.0);
        }

        static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Value noise (0..1) on a lattice a unit apart, smoothly between its points.</summary>
        static double Value(long seed, int salt, double x, double y, double z)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
            double fx = x - ix, fy = y - iy, fz = z - iz;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            fz = fz * fz * (3 - 2 * fz);
            double L(int a, int b, int c) => H(seed, salt, ix + a, iy + b, iz + c);
            double x00 = L(0, 0, 0) + (L(1, 0, 0) - L(0, 0, 0)) * fx, x10 = L(0, 1, 0) + (L(1, 1, 0) - L(0, 1, 0)) * fx;
            double x01 = L(0, 0, 1) + (L(1, 0, 1) - L(0, 0, 1)) * fx, x11 = L(0, 1, 1) + (L(1, 1, 1) - L(0, 1, 1)) * fx;
            double y0 = x00 + (x10 - x00) * fy, y1 = x01 + (x11 - x01) * fy;
            return y0 + (y1 - y0) * fz;
        }

        /// <summary>
        /// The decay's noise at a ship-local point: coarse along her length (planks go in runs) and a finer one, made even
        /// (0..1 equally often, by its own measured spread), so "below p" takes about p of her.
        /// </summary>
        public static double Rot(long seed, double x, double y, double z)
        {
            double n = 0.65 * Value(seed, 51, x / 2.0, y / 0.5, z / 1.0) + 0.35 * Value(seed, 52, x / 0.75, y / 0.75, z / 0.75);
            int i = Array.BinarySearch(spread, n);
            if (i < 0) i = ~i;
            return (double)i / spread.Length;
        }

        static readonly double[] spread = MeasureSpread();

        static double[] MeasureSpread()
        {
            var s = new double[8192];
            for (int i = 0; i < s.Length; i++)
            {
                double x = H(1234, 1, i) * 400 - 200, y = H(1234, 2, i) * 40 - 20, z = H(1234, 3, i) * 40 - 20;
                s[i] = 0.65 * Value(77, 51, x / 2.0, y / 0.5, z / 1.0) + 0.35 * Value(77, 52, x / 0.75, y / 0.75, z / 0.75);
            }
            Array.Sort(s);
            return s;
        }

        /// <summary>The compass side a level world direction points to (north is -z).</summary>
        public static string Compass(double dx, double dz) =>
            Math.Abs(dx) > Math.Abs(dz) ? (dx > 0 ? "east" : "west") : (dz > 0 ? "south" : "north");

        static readonly string[] Sides = { "north", "east", "south", "west" };

        // ------------------------------------------------------------------ one wreck

        sealed class Run
        {
            readonly WreckInput w;
            readonly IWreckSite site;
            readonly ShipDefinition def;
            readonly VoxelGrid g;
            readonly ShipPartMap map;
            readonly long seed;
            readonly WreckPlan plan = new();
            // world → ship-local: local = ex·dx + ey·dy + ez·dz
            readonly double[] ex = new double[3], ey = new double[3], ez = new double[3];
            readonly double ox, oy, oz;
            byte[] dm;
            double[] stump;
            readonly Dictionary<long, byte[]> cells = new();
            readonly HashSet<long> reserved = new();
            readonly Dictionary<long, (WreckGround G, float Top)> at = new();
            readonly Dictionary<long, double> surface = new();
            int scanTop, scanBottom;

            public Run(WreckInput w, IWreckSite site)
            {
                this.w = w;
                this.site = site;
                def = w.Def;
                // ((0.3.20) her deck cells as they were)
                g = DeckCells.Applied(def, w.Slots);
                map = def.PartMap;
                seed = w.Seed;
                plan.Input = w;
                ox = w.X;
                oy = w.Y - w.Settle;
                oz = w.Z;
                ShipFrame.ToLocal(w.Yaw, w.Trim, w.List, 1, 0, 0, out ex[0], out ex[1], out ex[2]);
                ShipFrame.ToLocal(w.Yaw, w.Trim, w.List, 0, 1, 0, out ey[0], out ey[1], out ey[2]);
                ShipFrame.ToLocal(w.Yaw, w.Trim, w.List, 0, 0, 1, out ez[0], out ez[1], out ez[2]);
            }

            void ToWorld(double lx, double ly, double lz, out double x, out double y, out double z)
            {
                ShipFrame.ToWorld(w.Yaw, w.Trim, w.List, lx, ly, lz, out x, out y, out z);
                x += ox;
                y += oy;
                z += oz;
            }

            public WreckPlan Make()
            {
                DesignCells();
                Hull();
                if (w.Masts) Timbers();
                if (w.Anchor) Anchor();
                if (w.Decay) Specks();
                foreach (long k in cells.Keys.ToList()) if (CellsIn(k) == 0) cells.Remove(k);
                for (int i = 0; i < w.Containers.Length; i++) plan.Containers.Add(Container(w.Containers[i]));
                if (w.Guns) foreach (var f in def.Fixtures) if (f.Type == "gun" && !f.Swivel && (w.GunsGone == null || Array.IndexOf(w.GunsGone, f.Code) < 0)) { var s = Gun(f); if (s != null) plan.Guns.Add(s); }
                for (int i = 0; i < w.ExtraChests; i++) { var s = ExtraChest(i); if (s != null) plan.Extra.Add(s); }
                Sediment();
                Finish();
                return plan;
            }

            // ---------------------------------------------------------- what's at a block, and where the seabed is

            WreckGround At(int x, int y, int z, out float top)
            {
                long k = WreckPlan.Key(x, y, z);
                if (!at.TryGetValue(k, out var a))
                {
                    var gr = site.At(x, y, z, out float t);
                    at[k] = a = (gr, t);
                }
                top = a.Top;
                return a.G;
            }

            WreckGround At(int x, int y, int z) => At(x, y, z, out _);

            /// <summary>The seabed's height in a column: the top of the first ground block down from above her (−∞ if none).</summary>
            double Surface(int x, int z)
            {
                long k = WreckPlan.Key(x, 0, z);
                if (surface.TryGetValue(k, out double s)) return s;
                s = double.NegativeInfinity;
                for (int y = scanTop; y >= scanBottom; y--)
                {
                    if (At(x, y, z, out float top) != WreckGround.Ground) continue;
                    s = y + top;
                    break;
                }
                surface[k] = s;
                return s;
            }

            // ---------------------------------------------------------- her design's cells, broken up

            int Idx(int i, int j, int k) => (i * g.NY + j) * g.NZ + k;

            void DesignCells()
            {
                dm = new byte[g.NX * g.NY * g.NZ];
                double hullTop = map.HullTop, bowCut = map.HullX1 + 1.25, copperTop = def.Waterline + 0.75;
                stump = new double[def.Masts.Length];
                for (int n = 0; n < stump.Length; n++) stump[n] = def.DeckY + 0.5 + 1.0 * H(seed, 11, n);
                // which castle falls in: the stern castle (aft of the quarterdeck's front) or the forecastle
                var quarter = def.Decks.FirstOrDefault(d => d.Code == "quarter");
                var fore = def.Decks.FirstOrDefault(d => d.Code == "forecastle");
                bool sternFalls = H(seed, 12, 0) < 0.5;
                var castle = sternFalls ? quarter : fore;
                for (int i = 0; i < g.NX; i++)
                    for (int j = 0; j < g.NY; j++)
                        for (int k = 0; k < g.NZ; k++)
                        {
                            if (!g.Solid(i, j, k)) continue;
                            double x = g.OX + (i + 0.5) * g.V, y = g.OY + (j + 0.5) * g.V, z = g.OZ + (k + 0.5) * g.V;
                            // over her castles: the masts and the top (they fall); forward of her stem: the bowsprit (broken off)
                            if (y > hullTop || x > bowCut && y > def.DeckY) continue;
                            int mast = MastAt(x, y, z);
                            byte mat;
                            if (mast >= 0)
                            {
                                if (y > stump[mast]) continue;
                                mat = WreckMat.Pine;
                            }
                            else if (map.IsOuterSkin(i, j, k)) mat = w.Clad && y < copperTop ? (w.CladMetal == "brass" ? WreckMat.Brass : w.CladMetal == "lead" ? WreckMat.Lead : WreckMat.Copper) : WreckMat.Tarred;
                            else mat = WreckMat.Oak;
                            plan.CellsDesign++;
                            if (w.Decay && mast < 0 && Decays(i, j, k, x, y, z, castle, sternFalls))
                            {
                                plan.CellsDecayed++;
                                continue;
                            }
                            dm[Idx(i, j, k)] = mat;
                        }
                if (def.DamageParts != null && (w.Holes && w.Hurt != null || w.Decay)) Holes();
            }

            int MastAt(double x, double y, double z)
            {
                for (int n = 0; n < def.Masts.Length; n++)
                {
                    var m = def.Masts[n];
                    double dx = x - m.X, dz = z - m.Z, r = m.Radius + 0.15;
                    if (y >= m.Y0 - 0.25 && y <= m.Y1 && dx * dx + dz * dz <= r * r) return n;
                }
                return -1;
            }

            bool IsDeck(double x, double y)
            {
                double top = y + g.V / 2;
                foreach (var d in def.Decks)
                    if (d.Code != "top" && x >= d.X0 && x <= d.X1 && Math.Abs(top - d.Y) < 0.13) return true;
                return false;
            }

            /// <summary>
            /// Is this cell gone? A plank here and there anywhere; her woodwork inside (bulkheads, stairs, cabins) more;
            /// her upper works more the higher they are over the main deck; the fallen castle's deck and all over it nearly
            /// all, its walls under it a third.
            /// </summary>
            bool Decays(int i, int j, int k, double x, double y, double z, DeckDef castle, bool stern)
            {
                bool shell = map.IsShell(i, j, k), deck = IsDeck(x, y);
                double h = y - def.DeckY;
                double p = 0.03;
                if (!shell && !deck) p += 0.1;
                if (h > 0.25) p += 0.14 + 0.07 * (h - 0.25) + (shell ? 0.04 : 0);
                if (castle != null && (stern ? x < castle.X1 : x > castle.X0))
                {
                    if (y > castle.Y - 0.4) p = Math.Max(p, 0.85);
                    else if (h > 0.3) p = Math.Max(p, 0.35);
                }
                return Rot(seed, x, y, z) < Math.Min(0.95, p);
            }

            /// <summary>
            /// Her holes: through the planking where a panel was holed (an ellipse along its planks, ragged), smaller where it
            /// was only cracked; through a deck where it was holed. Broken up, a few more panels of her sides between her
            /// bilge and her main deck are stove in at random (where she struck the bottom, or rotted through).
            /// </summary>
            void Holes()
            {
                var l = def.DamageParts;
                var d = l.D;
                var hurts = new float[l.Count];
                if (w.Holes && w.Hurt != null) Array.Copy(w.Hurt, hurts, Math.Min(w.Hurt.Length, hurts.Length));
                if (w.Decay && def.Hull != null)
                {
                    // ((0.3.26, step D1) hull panels: 2-4 whole panels between her bilge and her main deck stove in, gone from her
                    // wreck as a panel taken out is)
                    var panels = l.Parts.Where(p => p.Slot >= 0 && p.Center.Y > def.KeelY + 1.5 && p.Center.Y < def.DeckY && hurts[p.Index] < d.Holed).ToList();
                    int breaches = 2 + (int)(H(seed, 24, 0) * 3);
                    for (int b = 0; b < breaches && panels.Count > 0; b++)
                    {
                        var p = panels[(int)(H(seed, 25, b) * panels.Count) % panels.Count];
                        panels.Remove(p);
                        foreach (var box in def.Hull.Slots[p.Slot].SolidBoxes)
                            foreach (long v in DeckSlotLayout.Voxels(g, box))
                            {
                                int n = (int)v;
                                if (dm[n] == WreckMat.None) continue;
                                dm[n] = WreckMat.None;
                                plan.CellsHoled++;
                            }
                        plan.PanelsBreached.Add(def.Hull.Slots[p.Slot].Id);
                    }
                }
                else if (w.Decay)
                {
                    var sides = l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Center.Y > def.KeelY + 1.5 && p.Center.Y < def.DeckY).ToList();
                    int breaches = 2 + (int)(H(seed, 24, 0) * 3);
                    for (int b = 0; b < breaches && sides.Count > 0; b++)
                    {
                        var p = sides[(int)(H(seed, 25, b) * sides.Count) % sides.Count];
                        hurts[p.Index] = Math.Max(hurts[p.Index], d.Holed + (0.4f + 0.6f * (float)H(seed, 26, b)) * (1 - d.Holed));
                    }
                }
                for (int n = 0; n < l.Count; n++)
                {
                    var p = l.Parts[n];
                    float hurt = hurts[n];
                    // ((0.3.26) a hull panel: out of her wreck's cells already when it was out (WreckInput.Slots); cracked it stays)
                    // ((0.3.28) a deck cell: likewise, out of her wreck's cells when it was stove in or taken up)
                    if (p.Slot >= 0 || p.Cell >= 0) continue;
                    if (p.Kind == DamageKind.Hull)
                    {
                        double ru, rw;
                        if (hurt >= d.Holed)
                        {
                            double stove = Math.Clamp((hurt - d.Holed) / Math.Max(1e-3, 1 - d.Holed), 0, 1);
                            ru = 0.5 + 0.45 * stove;
                            rw = 0.35 + 0.3 * stove;
                        }
                        else if (hurt >= d.Cracked && H(seed, 21, p.Code) < 0.4)
                        {
                            ru = 0.3;
                            rw = 0.2;
                        }
                        else continue;
                        var nv = Unit(p.Normal.X, p.Normal.Y, p.Normal.Z) ?? new[] { 0.0, 0, p.Part.Side };
                        var u = Unit(1 - nv[0] * nv[0], -nv[0] * nv[1], -nv[0] * nv[2]);
                        if (u == null || Math.Abs(nv[0]) > 0.8) u = Unit(-nv[2] * nv[0], -nv[2] * nv[1], 1 - nv[2] * nv[2]) ?? new[] { 0.0, 1, 0 };
                        var wv = new[] { nv[1] * u[2] - nv[2] * u[1], nv[2] * u[0] - nv[0] * u[2], nv[0] * u[1] - nv[1] * u[0] };
                        double su = (H(seed, 22, p.Code) * 2 - 1) * 0.25, sw = (H(seed, 23, p.Code) * 2 - 1) * 0.15;
                        var c = new[] { p.Center.X + su * u[0] + sw * wv[0], p.Center.Y + su * u[1] + sw * wv[1], p.Center.Z + su * u[2] + sw * wv[2] };
                        const double reach = 0.9;
                        Carve(c, ru, rw, reach, (dx, dy, dz) =>
                        {
                            double a = dx * nv[0] + dy * nv[1] + dz * nv[2];
                            if (Math.Abs(a) > reach) return false;
                            double eu = (dx * u[0] + dy * u[1] + dz * u[2]) / ru, ew = (dx * wv[0] + dy * wv[1] + dz * wv[2]) / rw;
                            return eu * eu + ew * ew + 0.6 * (Rot(seed, c[0] + dx, c[1] + dy, c[2] + dz) - 0.5) < 1;
                        });
                    }
                    else if (p.Kind == DamageKind.Deck && hurt >= d.Holed)
                    {
                        double stove = Math.Clamp((hurt - d.Holed) / Math.Max(1e-3, 1 - d.Holed), 0, 1), r = 0.5 + 0.35 * stove;
                        var c = new[] { p.Center.X, p.Center.Y, p.Center.Z };
                        Carve(c, r, r, 0.45, (dx, dy, dz) => Math.Abs(dy) <= 0.45 &&
                            (dx * dx + dz * dz) / (r * r) + 0.6 * (Rot(seed, c[0] + dx, c[1] + dy, c[2] + dz) - 0.5) < 1);
                    }
                }
            }

            void Carve(double[] c, double ru, double rw, double reach, Func<double, double, double, bool> inside)
            {
                double e = Math.Max(ru, rw) + reach + 0.3;
                int i0 = Math.Max(0, g.CellX(c[0] - e)), i1 = Math.Min(g.NX - 1, g.CellX(c[0] + e));
                int j0 = Math.Max(0, g.CellY(c[1] - e)), j1 = Math.Min(g.NY - 1, g.CellY(c[1] + e));
                int k0 = Math.Max(0, g.CellZ(c[2] - e)), k1 = Math.Min(g.NZ - 1, g.CellZ(c[2] + e));
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                        for (int k = k0; k <= k1; k++)
                        {
                            int n = Idx(i, j, k);
                            if (dm[n] == WreckMat.None) continue;
                            double dx = g.OX + (i + 0.5) * g.V - c[0], dy = g.OY + (j + 0.5) * g.V - c[1], dz = g.OZ + (k + 0.5) * g.V - c[2];
                            if (!inside(dx, dy, dz)) continue;
                            dm[n] = WreckMat.None;
                            plan.CellsHoled++;
                        }
            }

            static double[] Unit(double x, double y, double z)
            {
                double l = Math.Sqrt(x * x + y * y + z * z);
                return l < 1e-9 ? null : new[] { x / l, y / l, z / l };
            }

            // ---------------------------------------------------------- her hull laid in the world

            void Hull()
            {
                double ly1 = Math.Min(g.MaxY, map.HullTop + 0.5);
                double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
                for (int c = 0; c < 8; c++)
                {
                    ToWorld((c & 1) == 0 ? g.OX : g.MaxX, (c & 2) == 0 ? g.OY : ly1, (c & 4) == 0 ? g.OZ : g.MaxZ, out double x, out double y, out double z);
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z);
                    x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
                }
                int bx0 = (int)Math.Floor(x0), by0 = (int)Math.Floor(y0), bz0 = (int)Math.Floor(z0);
                int bx1 = (int)Math.Floor(x1), by1 = (int)Math.Floor(y1), bz1 = (int)Math.Floor(z1);
                scanTop = by1 + 2;
                scanBottom = by0 - 24;
                var one = new byte[64];
                for (int bx = bx0; bx <= bx1; bx++)
                    for (int bz = bz0; bz <= bz1; bz++)
                        for (int by = by0; by <= by1; by++)
                        {
                            int n = 0;
                            for (int b = 0; b < 64; b++)
                            {
                                WreckCells.Cell(b, out int cx, out int cy, out int cz);
                                double dx = bx + (cx + 0.5) * WreckCells.Size - ox, dy = by + (cy + 0.5) * WreckCells.Size - oy, dz = bz + (cz + 0.5) * WreckCells.Size - oz;
                                double lx = ex[0] * dx + ey[0] * dy + ez[0] * dz;
                                double ly = ex[1] * dx + ey[1] * dy + ez[1] * dz;
                                double lz = ex[2] * dx + ey[2] * dy + ez[2] * dz;
                                int i = g.CellX(lx), j = g.CellY(ly), k = g.CellZ(lz);
                                byte m = i < 0 || j < 0 || k < 0 || i >= g.NX || j >= g.NY || k >= g.NZ ? WreckMat.None : dm[Idx(i, j, k)];
                                one[b] = m;
                                if (m != WreckMat.None) n++;
                            }
                            if (n == 0) continue;
                            if (At(bx, by, bz) == WreckGround.Ground)
                            {
                                plan.CellsBlocked += n;
                                continue;
                            }
                            double s = Surface(bx, bz);
                            for (int b = 0; b < 64; b++)
                            {
                                if (one[b] == WreckMat.None) continue;
                                WreckCells.Cell(b, out _, out int cy, out _);
                                if (by + (cy + 0.5) * WreckCells.Size >= s) continue;
                                one[b] = WreckMat.None;
                                plan.CellsBuried++;
                                n--;
                            }
                            if (n > 0) cells[WreckPlan.Key(bx, by, bz)] = (byte[])one.Clone();
                        }
            }

            /// <summary>Put a cell of this material at a world point (not into ground, not under the seabed, not over one of hers already there).</summary>
            bool Lay(double x, double y, double z, byte mat)
            {
                int bx = (int)Math.Floor(x), by = (int)Math.Floor(y), bz = (int)Math.Floor(z);
                int cx = Math.Clamp((int)((x - bx) / WreckCells.Size), 0, 3), cy = Math.Clamp((int)((y - by) / WreckCells.Size), 0, 3), cz = Math.Clamp((int)((z - bz) / WreckCells.Size), 0, 3);
                if (At(bx, by, bz) == WreckGround.Ground || by + (cy + 0.5) * WreckCells.Size < Surface(bx, bz)) return false;
                long k = WreckPlan.Key(bx, by, bz);
                if (!cells.TryGetValue(k, out var one)) cells[k] = one = new byte[64];
                int bit = WreckCells.Bit(cx, cy, cz);
                if (one[bit] != WreckMat.None) return false;
                one[bit] = mat;
                return true;
            }

            // ---------------------------------------------------------- her masts on the seabed beside her

            /// <summary>
            /// Each mast above its stump, fallen over her side (the side she lies over to, else either), across her give or
            /// take 40°, as two or three long timbers (at most 24 blocks of it) lying on the seabed one after the other with a
            /// little gap and a kink between them, thinner toward the top.
            /// </summary>
            void Timbers()
            {
                for (int n = 0; n < def.Masts.Length; n++)
                {
                    var m = def.Masts[n];
                    double length = Math.Min(m.Y1 - stump[n], 24);
                    if (length < 2) continue;
                    int side = w.List > 1e-3 ? 1 : w.List < -1e-3 ? -1 : H(seed, 61, n) < 0.5 ? -1 : 1;
                    double ang = (90 + (H(seed, 62, n) * 2 - 1) * 40) * Math.PI / 180;
                    ShipFrame.ToWorld(w.Yaw, Math.Cos(ang), 0, side * Math.Sin(ang), out double dx, out _, out double dz);
                    ToWorld(m.X, stump[n], m.Z, out double fx, out _, out double fz);
                    double reach = def.Beam / 2 + 0.75;
                    double px = fx + dx * reach, pz = fz + dz * reach;
                    int pieces = length > 16 ? 3 : length > 7 ? 2 : 1;
                    double done = 0;
                    for (int q = 0; q < pieces; q++)
                    {
                        double len = q == pieces - 1 ? length - done : length / pieces * (0.8 + 0.4 * H(seed, 63, n, q));
                        len = Math.Max(1, Math.Min(len, length - done));
                        double kink = (H(seed, 64, n, q) * 2 - 1) * 12 * Math.PI / 180;
                        double kx = dx * Math.Cos(kink) - dz * Math.Sin(kink), kz = dx * Math.Sin(kink) + dz * Math.Cos(kink);
                        double r = Math.Max(0.15, m.Radius * (1 - 0.45 * (done + len / 2) / Math.Max(1, length)));
                        double mx = px + kx * len / 2, mz = pz + kz * len / 2;
                        double s = Surface((int)Math.Floor(mx), (int)Math.Floor(mz));
                        if (!double.IsNegativeInfinity(s)) Spar(px, pz, kx, kz, len, s + r, r);
                        done += len;
                        double gap = 0.4 + 0.6 * H(seed, 65, n, q);
                        px += kx * (len + gap);
                        pz += kz * (len + gap);
                    }
                }
            }

            /// <summary>A level round timber from (x, z) along (dx, dz) for len, its axis at height y, of radius r, in pine.</summary>
            void Spar(double x, double z, double dx, double dz, double len, double y, double r)
            {
                double xa = Math.Min(x, x + dx * len) - r - 0.25, xb = Math.Max(x, x + dx * len) + r + 0.25;
                double za = Math.Min(z, z + dz * len) - r - 0.25, zb = Math.Max(z, z + dz * len) + r + 0.25;
                const double v = WreckCells.Size;
                for (double cx = Math.Floor(xa / v) * v + v / 2; cx <= xb; cx += v)
                    for (double cz = Math.Floor(za / v) * v + v / 2; cz <= zb; cz += v)
                    {
                        double t = (cx - x) * dx + (cz - z) * dz;
                        if (t < 0 || t > len) continue;
                        double qx = cx - (x + dx * t), qz = cz - (z + dz * t);
                        double h2 = r * r - qx * qx - qz * qz;
                        if (h2 < 0) continue;
                        double hh = Math.Sqrt(h2);
                        for (double cy = Math.Floor((y - hh) / v) * v + v / 2; cy <= y + hh; cy += v)
                        {
                            if (Math.Abs(cy - y) > hh) continue;
                            if (Lay(cx, cy, cz, WreckMat.Pine)) plan.CellsTimber++;
                        }
                    }
            }

            // ---------------------------------------------------------- her anchor

            /// <summary>Her anchor (a shank, a stock across its head, two arms with their flukes) lying flat on the seabed beside her bow, outboard of where it hung.</summary>
            void Anchor()
            {
                var part = def.MovingParts.FirstOrDefault(p => p.Source == "anchor" && p.Kind == "slide");
                if (part == null) return;
                var cable = def.MovingParts.FirstOrDefault(p => p.Kind == "follow" && p.Follow == part.Code);
                double[] tip = cable?.Tip ?? new[] { def.Length / 2 - 2.0, (double)def.DeckY, -def.Beam / 2 };
                double side = tip[2] < 0 ? -1 : 1;
                ToWorld(tip[0], tip[1], tip[2] + side * 1.75, out double ax, out _, out double az);
                double s = Surface((int)Math.Floor(ax), (int)Math.Floor(az));
                if (double.IsNegativeInfinity(s)) return;
                double y = s + 0.125, a = H(seed, 71, 0) * Math.PI * 2, ca = Math.Cos(a), sa = Math.Sin(a);
                // (along the shank from the crown, across): segments and their thickness
                var parts = new[]
                {
                    (0.0, 0.0, 2.5, 0.0, 0.16), (2.25, -1.0, 2.25, 1.0, 0.14),
                    (0.0, 0.0, 0.6, 0.75, 0.14), (0.0, 0.0, 0.6, -0.75, 0.14),
                    (0.45, 0.6, 0.85, 0.95, 0.2), (0.45, -0.6, 0.85, -0.95, 0.2),
                };
                const double v = WreckCells.Size;
                foreach (var (u0, w0, u1, w1, r) in parts)
                {
                    for (double t = 0; t <= 1.0001; t += 0.05)
                    {
                        double u = u0 + (u1 - u0) * t, ww = w0 + (w1 - w0) * t;
                        double x = ax + u * ca - ww * sa, z = az + u * sa + ww * ca;
                        for (double qx = -r; qx <= r + 1e-9; qx += v / 2)
                            for (double qz = -r; qz <= r + 1e-9; qz += v / 2)
                                if (qx * qx + qz * qz <= r * r && Lay(x + qx, y, z + qz, WreckMat.Iron)) plan.CellsAnchor++;
                    }
                }
            }

            /// <summary>Cells left with no cell against any of their faces (specks from turning her on a grid, or from decay) go.</summary>
            void Specks()
            {
                var gone = new List<(long, int)>();
                foreach (var kv in cells)
                {
                    WreckPlan.Unkey(kv.Key, out int bx, out int by, out int bz);
                    for (int b = 0; b < 64; b++)
                    {
                        if (kv.Value[b] == WreckMat.None) continue;
                        WreckCells.Cell(b, out int cx, out int cy, out int cz);
                        bool alone = true;
                        for (int f = 0; f < 6 && alone; f++)
                        {
                            int a = WreckCells.FaceAxis[f], dir = WreckCells.FaceSign[f];
                            int[] c = { cx, cy, cz }, blk = { bx, by, bz };
                            c[a] += dir;
                            if (c[a] < 0 || c[a] > 3) { blk[a] += dir; c[a] = (c[a] + 4) % 4; }
                            if (cells.TryGetValue(WreckPlan.Key(blk[0], blk[1], blk[2]), out var nb) && nb[WreckCells.Bit(c[0], c[1], c[2])] != WreckMat.None) alone = false;
                        }
                        if (alone) gone.Add((kv.Key, b));
                    }
                }
                foreach (var (k, b) in gone) cells[k][b] = WreckMat.None;
            }

            // ---------------------------------------------------------- places for things

            int CellsIn(long k)
            {
                if (!cells.TryGetValue(k, out var one)) return 0;
                int n = 0;
                foreach (byte b in one) if (b != WreckMat.None) n++;
                return n;
            }

            /// <summary>Can something go at this block in her (taking out what of hers is there, at most this many cells)?</summary>
            bool Room(int x, int y, int z, int most)
            {
                long k = WreckPlan.Key(x, y, z);
                return !reserved.Contains(k) && At(x, y, z) != WreckGround.Ground && y + 0.5 > Surface(x, z) && CellsIn(k) <= most;
            }

            void Take(int x, int y, int z)
            {
                long k = WreckPlan.Key(x, y, z);
                reserved.Add(k);
                cells.Remove(k);
            }

            /// <summary>
            /// The nearest free block on the seabed to (x, z): the first one over the ground there, not hers or taken, with the
            /// block over it clear of her too; for a gun, its barrel's blocks either side along its axis free as well (it's 3
            /// long), trying the side asked for, then across. Null if none within 12 blocks.
            /// </summary>
            WreckSpot Seabed(double x, double z, string side, bool gun, int salt)
            {
                int x0 = (int)Math.Floor(x), z0 = (int)Math.Floor(z);
                for (int r = 0; r <= 12; r++)
                {
                    var ring = new List<(int X, int Z, double D)>();
                    for (int dx = -r; dx <= r; dx++)
                        for (int dz = -r; dz <= r; dz++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                            ring.Add((x0 + dx, z0 + dz, dx * dx + dz * dz + H(seed, salt, x0 + dx, z0 + dz)));
                        }
                    foreach (var (cx, cz, _) in ring.OrderBy(c => c.D))
                    {
                        double s = Surface(cx, cz);
                        if (double.IsNegativeInfinity(s)) continue;
                        int y = (int)Math.Ceiling(s - 1e-6);
                        if (!Free(cx, y, cz) || cells.ContainsKey(WreckPlan.Key(cx, y + 1, cz))) continue;
                        if (!gun)
                        {
                            Take(cx, y, cz);
                            return new WreckSpot { X = cx, Y = y, Z = cz, Side = side };
                        }
                        foreach (string sd in new[] { side, Across(side) })
                        {
                            bool alongZ = sd == "north" || sd == "south";
                            int ax = alongZ ? 0 : 1, az = alongZ ? 1 : 0;
                            if (!Free(cx - ax, y, cz - az) || !Free(cx + ax, y, cz + az)) continue;
                            Take(cx, y, cz);
                            reserved.Add(WreckPlan.Key(cx - ax, y, cz - az));
                            reserved.Add(WreckPlan.Key(cx + ax, y, cz + az));
                            return new WreckSpot { X = cx, Y = y, Z = cz, Side = sd };
                        }
                    }
                }
                return null;
            }

            bool Free(int x, int y, int z)
            {
                long k = WreckPlan.Key(x, y, z);
                return !reserved.Contains(k) && !cells.ContainsKey(k) && At(x, y, z) != WreckGround.Ground && y + 0.5 > Surface(x, z);
            }

            static string Across(string side) => side is "north" or "south" ? "east" : "north";

            /// <summary>A container where it stood in her (a block of hers with little in it), else on the seabed beside her, nearest that.</summary>
            WreckSpot Container(string code)
            {
                var f = def.Fixture(code);
                double[] placed = f == null && w.ContainerPlaces != null && w.ContainerPlaces.TryGetValue(code, out var cp) && cp?.Length >= 3 ? cp : null;
                double[] p = f?.Pos ?? placed ?? new[] { 0.0, (double)def.KeelY + 1, 0 };
                ToWorld(p[0], p[1] + 0.45, p[2], out double x, out double y, out double z);
                var (fx, fz) = f?.Facing ?? (placed?.Length >= 4 ? (Math.Cos(placed[3] * Math.PI / 180), -Math.Sin(placed[3] * Math.PI / 180)) : (1.0, 0.0));
                ShipFrame.ToWorld(w.Yaw, fx, 0, fz, out double dx, out _, out double dz);
                string side = Compass(dx, dz);
                int bx = (int)Math.Floor(x), by = (int)Math.Floor(y), bz = (int)Math.Floor(z);
                if (Room(bx, by, bz, 24))
                {
                    Take(bx, by, bz);
                    return new WreckSpot { Fixture = code, X = bx, Y = by, Z = bz, InWreck = true, Side = side };
                }
                var s = Seabed(x, z, side, false, 81);
                if (s != null) s.Fixture = code;
                return s;
            }

            /// <summary>
            /// A gun where it stood on her deck if there's room for it and its barrel (her cells there are few and go),
            /// facing out of its port; else tumbled off her deck onto the seabed outboard of its port, facing anyhow.
            /// </summary>
            WreckSpot Gun(FixtureDef f)
            {
                ToWorld(f.Pos[0], f.Pos[1] + 0.4, f.Pos[2], out double x, out double y, out double z);
                double[] o = f.Out ?? new[] { 0.0, 0, f.Pos[2] < 0 ? -1.0 : 1.0 };
                ShipFrame.ToWorld(w.Yaw, o[0], 0, o[2], out double dx, out _, out double dz);
                string side = Compass(dx, dz);
                int bx = (int)Math.Floor(x), by = (int)Math.Floor(y), bz = (int)Math.Floor(z);
                bool alongZ = side is "north" or "south";
                int ax = alongZ ? 0 : 1, az = alongZ ? 1 : 0;
                if (Room(bx, by, bz, 16) && Room(bx - ax, by, bz - az, 8) && Room(bx + ax, by, bz + az, 8))
                {
                    Take(bx, by, bz);
                    Take(bx - ax, by, bz - az);
                    Take(bx + ax, by, bz + az);
                    return new WreckSpot { Fixture = f.Code, X = bx, Y = by, Z = bz, InWreck = true, Side = side };
                }
                double l = Math.Sqrt(dx * dx + dz * dz);
                if (l > 1e-9) { dx /= l; dz /= l; }
                string tumbled = Sides[(int)(H(seed, 91, f.Index) * 4) & 3];
                var s = Seabed(x + dx * 2.5, z + dz * 2.5, tumbled, true, 92);
                if (s != null) s.Fixture = f.Code;
                return s;
            }

            /// <summary>The extra chests on the seabed beside her middle, on the side she lies over to.</summary>
            WreckSpot ExtraChest(int i)
            {
                int side = w.List >= 0 ? 1 : -1;
                ToWorld(0, def.KeelY, side * (def.Beam / 2 + 2), out double x, out _, out double z);
                ShipFrame.ToWorld(w.Yaw, 0, 0, -side, out double dx, out _, out double dz);
                return Seabed(x, z, Compass(dx, dz), false, 101 + i);
            }

            /// <summary>Is this world point inside her hull (in her own frame between her two sides, at its height and station)?</summary>
            bool InsideHull(double x, double y, double z)
            {
                double dx = x - ox, dy = y - oy, dz = z - oz;
                int i = g.CellX(ex[0] * dx + ey[0] * dy + ez[0] * dz), j = g.CellY(ex[1] * dx + ey[1] * dy + ez[1] * dz), k = g.CellZ(ex[2] * dx + ey[2] * dy + ez[2] * dz);
                if (i < 0 || j < 0 || i >= g.NX || j >= g.NY) return false;
                bool port = false, starboard = false;
                for (int n = 0; n < g.NZ && !(port && starboard); n++)
                    if (g.Solid(i, j, n)) { if (n <= k) port = true; if (n >= k) starboard = true; }
                return port && starboard;
            }

            /// <summary>
            /// Sand or gravel heaped against her hull's lowest blocks: some of the free blocks on the seabed beside a block
            /// of her hull that's on it, outside her (not between her sides).
            /// </summary>
            void Sediment()
            {
                var found = new List<(long K, double H)>();
                var seen = new HashSet<long>();
                foreach (long k in cells.Keys.OrderBy(k => k))
                {
                    WreckPlan.Unkey(k, out int x, out int y, out int z);
                    if (!cells[k].Any(m => m is WreckMat.Oak or WreckMat.Tarred or WreckMat.Copper or WreckMat.Brass or WreckMat.Lead) || At(x, y - 1, z) != WreckGround.Ground) continue;
                    foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                    {
                        long nk = WreckPlan.Key(nx, y, nz);
                        if (!seen.Add(nk) || cells.ContainsKey(nk) || reserved.Contains(nk)) continue;
                        if (At(nx, y, nz) != WreckGround.Free || At(nx, y - 1, nz) != WreckGround.Ground) continue;
                        if (InsideHull(nx + 0.5, y + 0.5, nz + 0.5)) continue;
                        double h = H(seed, 31, nx, y, nz);
                        if (h < 0.3) found.Add((nk, h));
                    }
                }
                foreach (var (k, _) in found.OrderBy(f => f.H).Take(w.MaxSediment).OrderBy(f => f.K))
                {
                    WreckPlan.Unkey(k, out int x, out int y, out int z);
                    reserved.Add(k);
                    plan.Sediment.Add(new WreckSpot { X = x, Y = y, Z = z });
                }
            }

            void Finish()
            {
                int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
                void Grow(int x, int y, int z)
                {
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z);
                    x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
                }
                foreach (var kv in cells)
                {
                    var c = WreckCells.FromCells(kv.Value);
                    if (c.Empty) continue;
                    plan.Blocks[kv.Key] = c;
                    plan.CellsPlaced += c.Count;
                    WreckPlan.Unkey(kv.Key, out int x, out int y, out int z);
                    Grow(x, y, z);
                }
                foreach (var s in plan.Containers.Concat(plan.Extra).Concat(plan.Guns).Concat(plan.Sediment))
                    if (s != null) Grow(s.X, s.Y, s.Z);
                if (x0 > x1) { x0 = x1 = (int)Math.Floor(ox); y0 = y1 = (int)Math.Floor(oy); z0 = z1 = (int)Math.Floor(oz); }
                plan.MinX = x0; plan.MinY = y0; plan.MinZ = z0;
                plan.MaxX = x1; plan.MaxY = y1; plan.MaxZ = z1;
            }
        }
    }
}
