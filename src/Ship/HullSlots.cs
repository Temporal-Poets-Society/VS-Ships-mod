using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.3.26, step D1; docs/slots.md §9) What the slot machinery needs of a family of slots (the deck cells, the hull's
    /// panels): their count, ids and design states, their collision, the chunks they're drawn in.
    /// </summary>
    public interface ISlotLayout
    {
        int Count { get; }
        string IdOf(int n);
        int IndexOf(string id);
        SlotState DesignStateOf(int n);
        /// <summary>Its collision voxels as boxes (null: none).</summary>
        double[][] SolidBoxesOf(int n);
        int ChunkOfSlot(int n);
        int ChunkCount { get; }
    }

    /// <summary>
    /// (0.3.26, step D1; docs/slots.md §9) One hull panel: a frame space (strip to strip) by a band of strakes on one side,
    /// its outside planking and its ceiling. Its index is its place in the definition's <c>hullSlots</c> (the mesh file's
    /// <c>slotQuads</c> of the "slot:hull*" groups refer to it), and the index of its damage part (hull parts come first).
    /// </summary>
    public class HullSlotDef
    {
        public string Id;
        /// <summary>-1 port, +1 starboard; the frame space (ShipPartMap.StationAt of its middle); the band from the keel up.</summary>
        public int Side, Station, Band;
        /// <summary>(0.3.28, D2.1) "a" the frame space's after half (its frame's middle to the space's), "f" its fore half; null: the whole space (0.3.26-0.3.27).</summary>
        public string Half;
        [JsonIgnore] public int HalfIndex => Half == "f" ? 1 : 0;
        /// <summary>Its shell (outside planking to the ceiling, its voxels): x0 y0 z0 x1 y1 z1.</summary>
        public double[] Box;
        /// <summary>Its outside's corners (aft-low, fwd-low, fwd-high, aft-high), and its ceiling's (null: none of its own).</summary>
        public double[][] Outer, Inner;
        /// <summary>Outward (the outside's mean).</summary>
        public double[] Normal;
        /// <summary>Its outside's area (square blocks).</summary>
        public double Area;
        /// <summary>Its outside's lowest and highest y.</summary>
        public double Sill, Head;
        /// <summary>The design room its ceiling faces (null: the open deck, a bulwark).</summary>
        public string Room;
        public double[][] Solids;
        public SlotMaterialDef Material;
        /// <summary>"filled" (default) or "empty" (step E: a hull built up from her frames; (0.3.29) a castle port's half where the port starts in).</summary>
        public string Start;
        /// <summary>(0.3.29, docs/slots.md §12) The port (a deck slot's id) that can take this half's and its fellow's place in her side, or null.</summary>
        public string Port;
        /// <summary>
        /// (0.3.29, docs/slots.md §13; LINED) Under her weather decks her side is two layers: "outer" her planking ("hull:..."),
        /// "inner" the ceiling inside the frames ("ceil:..."); null: one layer (a bulwark's). Partner: the other layer's id.
        /// </summary>
        public string Layer, Partner;
        [JsonIgnore] public bool IsInner => Layer == "inner";
        [JsonIgnore] public bool IsOuter => Layer != "inner";
        /// <summary>(0.3.29) The other layer's index (-1: one layer).</summary>
        [JsonIgnore] public int PartnerIndex = -1;

        [JsonIgnore] public int Index;
        [JsonIgnore] public bool StartsEmpty => Start == "empty";
        [JsonIgnore] public SlotState DesignState => StartsEmpty ? SlotState.Empty : SlotState.Filled;
        [JsonIgnore] public double[][] SolidBoxes => Solids != null && Solids.Length > 0 ? Solids : Box != null ? new[] { Box } : Array.Empty<double[]>();
        [JsonIgnore] public SlotMaterialDef Mat => Material ?? new SlotMaterialDef { Count = Math.Max(1, (int)Math.Round(2 * Area)) };
        /// <summary>The middle of its outside.</summary>
        [JsonIgnore] public Vec3d Centre
        {
            get
            {
                if (Outer == null || Outer.Length < 4) return Box == null ? new Vec3d() : new Vec3d((Box[0] + Box[3]) / 2, (Box[1] + Box[4]) / 2, (Box[2] + Box[5]) / 2);
                return new Vec3d(Outer.Average(p => p[0]), Outer.Average(p => p[1]), Outer.Average(p => p[2]));
            }
        }
        [JsonIgnore] public double Length => Box == null ? 1.5 : Box[3] - Box[0];
        /// <summary>Is it out over her main deck (a bulwark's, a castle's side): the sea through it is a downflooding point.</summary>
        public bool Above(ShipDefinition def) => Sill >= def.DeckY - 0.05;
        /// <summary>Its outside's z at height y (between its corners), on its side.</summary>
        public double OuterZ(double y)
        {
            if (Outer == null || Outer.Length < 4) return Box == null ? 0 : Side > 0 ? Box[5] : Box[2];
            double lo = (Outer[0][2] + Outer[1][2]) / 2, hi = (Outer[2][2] + Outer[3][2]) / 2;
            double f = Head - Sill > 1e-6 ? Math.Clamp((y - Sill) / (Head - Sill), 0, 1) : 0;
            return lo + (hi - lo) * f;
        }
    }

    /// <summary>
    /// (0.3.26, step D1) A design's hull panels worked out once (both sides alike, from the definition only): by id, by
    /// collision voxel, each one's neighbours up and down (the same side and frame space, the band under and over it), and
    /// the chunks they're drawn in (a side's two frame spaces).
    /// </summary>
    public class HullSlotLayout : ISlotLayout
    {
        public readonly ShipDefinition Def;
        public readonly HullSlotDef[] Slots;
        readonly Dictionary<string, int> byId = new();
        readonly Dictionary<long, int> byVoxel = new();
        /// <summary>Each panel's neighbour under it (0) and over it (1), or -1.</summary>
        public readonly int[][] Neighbours;
        public readonly int[] ChunkOf;
        public readonly List<List<int>> Chunks = new();
        public readonly List<string> Problems = new();

        public int Count => Slots.Length;
        public int ChunkCount => Chunks.Count;
        public string IdOf(int n) => n >= 0 && n < Slots.Length ? Slots[n]?.Id : null;
        public SlotState DesignStateOf(int n) => n >= 0 && n < Slots.Length ? Slots[n]?.DesignState ?? SlotState.Filled : SlotState.Filled;
        public double[][] SolidBoxesOf(int n) => n >= 0 && n < Slots.Length ? Slots[n]?.SolidBoxes : null;
        public int ChunkOfSlot(int n) => n >= 0 && n < ChunkOf.Length ? ChunkOf[n] : -1;

        HullSlotLayout(ShipDefinition def, HullSlotDef[] slots)
        {
            Def = def;
            Slots = slots;
            Neighbours = new int[slots.Length][];
            ChunkOf = new int[slots.Length];
            var g = def.Grid;
            var byPlace = new Dictionary<(int, int, int), int>();
            var chunkIx = new Dictionary<(int, int), int>();
            for (int n = 0; n < slots.Length; n++)
            {
                var s = slots[n];
                Neighbours[n] = new[] { -1, -1 };
                ChunkOf[n] = -1;
                if (s == null) { Problems.Add($"hull slot {n} is null"); continue; }
                s.Index = n;
                if (s.Box == null || s.Box.Length != 6) { Problems.Add($"hull slot {s.Id ?? n.ToString()} has no box"); continue; }
                if (string.IsNullOrEmpty(s.Id)) s.Id = $"hull:{(s.Side > 0 ? "s" : "p")}:{s.Station}:{s.Band}" + (s.Half != null ? ":" + s.Half : "");
                if (!byId.TryAdd(s.Id, n)) Problems.Add($"two hull slots {s.Id}");
                int lk = s.IsInner ? 1000 : 0;          // ((0.3.29) the two layers each their own places)
                if (!byPlace.TryAdd((s.Side, s.Station * 2 + s.HalfIndex, s.Band + lk), n)) Problems.Add($"hull slots {slots[byPlace[(s.Side, s.Station * 2 + s.HalfIndex, s.Band + lk)]].Id} and {s.Id} in one place");
                if (g != null)
                    foreach (var b in s.SolidBoxes)
                        foreach (long v in DeckSlotLayout.Voxels(g, b))
                            if (!byVoxel.TryAdd(v, n)) Problems.Add($"a voxel of {s.Id} is {slots[byVoxel[v]].Id}'s too");
                var ck = (s.Side, (int)Math.Floor(s.Station / 2.0));
                if (!chunkIx.TryGetValue(ck, out int c)) { chunkIx[ck] = c = Chunks.Count; Chunks.Add(new List<int>()); }
                ChunkOf[n] = c;
                Chunks[c].Add(n);
            }
            for (int n = 0; n < slots.Length; n++)
            {
                var s = slots[n];
                if (s?.Box == null) continue;
                // (a neighbour: the band under or over it whose edge is its edge: no fixed strake between)
                int sh = s.Station * 2 + s.HalfIndex, lk2 = s.IsInner ? 1000 : 0;
                if (byPlace.TryGetValue((s.Side, sh, s.Band - 1 + lk2), out int lo) && Math.Abs(slots[lo].Head - s.Sill) < 1e-3) Neighbours[n][0] = lo;
                if (byPlace.TryGetValue((s.Side, sh, s.Band + 1 + lk2), out int hi) && Math.Abs(slots[hi].Sill - s.Head) < 1e-3) Neighbours[n][1] = hi;
                if (s.Partner != null) s.PartnerIndex = IndexOf(s.Partner);
            }
        }

        /// <summary>A design's hull panels (null: it has none).</summary>
        public static HullSlotLayout Build(ShipDefinition def) =>
            def?.HullSlots == null || def.HullSlots.Length == 0 ? null : new HullSlotLayout(def, def.HullSlots);

        public int IndexOf(string id) => id != null && byId.TryGetValue(id, out int n) ? n : -1;
        public HullSlotDef Get(string id) => IndexOf(id) is int n && n >= 0 ? Slots[n] : null;

        /// <summary>The panel whose collision voxel this is (grid indices), or -1.</summary>
        public int AtVoxel(VoxelGrid g, int i, int j, int k) =>
            g != null && i >= 0 && j >= 0 && k >= 0 && i < g.NX && j < g.NY && k < g.NZ && byVoxel.TryGetValue(((long)i * g.NY + j) * g.NZ + k, out int n) ? n : -1;

        /// <summary>The panel whose collision voxel a point (ship-local) is in, or -1.</summary>
        public int AtPoint(VoxelGrid g, double x, double y, double z) => g == null ? -1 : AtVoxel(g, g.CellX(x), g.CellY(y), g.CellZ(z));

        /// <summary>
        /// The panel (of those open says) whose opening a ray (ship-local o + t d, d normalised) passes through first within
        /// range: its outside's or its ceiling's quad (each two triangles). -1 if none; t where it does.
        /// </summary>
        public int PickOpening(Vec3d o, Vec3d d, double range, Func<int, bool> open, out double t)
        {
            t = double.MaxValue;
            int best = -1;
            for (int n = 0; n < Slots.Length; n++)
            {
                var s = Slots[n];
                if (s?.Outer == null || open != null && !open(n)) continue;
                // (quick: its box, a little grown)
                if (ShipFittings.RayBox(o, d, new Cuboidd(s.Box[0] - 0.3, s.Box[1] - 0.3, s.Box[2] - 0.3, s.Box[3] + 0.3, s.Box[4] + 0.3, s.Box[5] + 0.3)) < 0) continue;
                foreach (var quad in new[] { s.Outer, s.Inner })
                {
                    if (quad == null || quad.Length < 4) continue;
                    double tq = RayQuad(o, d, quad);
                    if (tq >= 0 && tq <= range && tq < t) { t = tq; best = n; }
                }
            }
            return best;
        }

        /// <summary>Where a ray meets a quad (four corners, two triangles 0-1-2 and 0-2-3), or -1.</summary>
        public static double RayQuad(Vec3d o, Vec3d d, double[][] q)
        {
            double a = RayTri(o, d, q[0], q[1], q[2]), b = RayTri(o, d, q[0], q[2], q[3]);
            return a < 0 ? b : b < 0 ? a : Math.Min(a, b);
        }

        static double RayTri(Vec3d o, Vec3d d, double[] p0, double[] p1, double[] p2)
        {
            double e1x = p1[0] - p0[0], e1y = p1[1] - p0[1], e1z = p1[2] - p0[2];
            double e2x = p2[0] - p0[0], e2y = p2[1] - p0[1], e2z = p2[2] - p0[2];
            double px = d.Y * e2z - d.Z * e2y, py = d.Z * e2x - d.X * e2z, pz = d.X * e2y - d.Y * e2x;
            double det = e1x * px + e1y * py + e1z * pz;
            if (Math.Abs(det) < 1e-12) return -1;
            double inv = 1 / det;
            double tx = o.X - p0[0], ty = o.Y - p0[1], tz = o.Z - p0[2];
            double u = (tx * px + ty * py + tz * pz) * inv;
            if (u < -1e-6 || u > 1 + 1e-6) return -1;
            double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
            double v = (d.X * qx + d.Y * qy + d.Z * qz) * inv;
            if (v < -1e-6 || u + v > 1 + 1e-6) return -1;
            double t = (e2x * qx + e2y * qy + e2z * qz) * inv;
            return t >= 0 ? t : -1;
        }
    }

    /// <summary>
    /// (0.3.26, step D1) A hull panel's state from its damage part's hurt (the server keeps them so: <see cref="EntityShip.SyncSlotsFromHurt"/>):
    /// holed (stove in, or taken out) it's out, cracked it's cracked, else in.
    /// </summary>
    public static class HullPanelRules
    {
        public static SlotState StateFor(DamageDef d, float hurt) => hurt >= d.Holed ? SlotState.Empty : hurt >= d.Cracked ? SlotState.Cracked : SlotState.Filled;

        /// <summary>
        /// The open panels (out, their opening into a room) a room has: their boxes (an empty panel whose ceiling faces it).
        /// </summary>
        public static List<double[]> Leaks(HullSlotLayout l, Func<int, bool> open, RoomDef r)
        {
            var list = new List<double[]>();
            if (l == null || r == null || r.Code == null) return list;
            for (int n = 0; n < l.Count; n++)
            {
                var s = l.Slots[n];
                // ((0.3.29) two layers: the room's open only where both are out (counted once, at the planking))
                if (s?.Room != r.Code || s.IsInner || !open(n) || s.PartnerIndex >= 0 && !open(s.PartnerIndex)) continue;
                list.Add(s.Box);
            }
            return list;
        }
    }

    /// <summary>
    /// (0.3.26, step D1) Old damage saved for a design that has hull panels now (hansaDmgF "v" under 2: her hull's parts
    /// were whole-block bands, ShipPartMap.BandAt): each old hull part's hurt and wear moved to the panels where it was.
    /// Pure (the offline tests).
    /// </summary>
    public static class HullMigration
    {
        /// <summary>
        /// The codes, hurt and wear to load: deck, spar and sail codes as they were; each old hull code (side, station, a
        /// block's band from the keel) onto the panels of that side and frame space overlapping its band by more than 0.25
        /// (or the nearest): the most overlapped gets its hurt, the others at most cracked (if it was cracked or worse), the
        /// wear the worst of what lands there.
        /// </summary>
        public static (int[] Codes, float[] Hurt, float[] Wear) Map(ShipDefinition def, int[] codes, float[] hurt, float[] wear)
        {
            var l = def?.Hull;
            if (l == null || codes == null || hurt == null || wear == null) return (codes, hurt, wear);
            var d = def.Damage ?? new DamageDef();
            var outHurt = new Dictionary<int, float>();
            var outWear = new Dictionary<int, float>();
            var keep = new List<(int, float, float)>();
            double x0 = def.Frames?.X0 ?? 0, space = def.Frames?.Space ?? 1.5;
            for (int i = 0; i < codes.Length && i < hurt.Length && i < wear.Length; i++)
            {
                var p = ShipPart.Decode(codes[i]);
                if (p.Kind != ShipPartKind.Hull) { keep.Add((codes[i], hurt[i], wear[i])); continue; }
                double y0 = def.KeelY + p.Band, y1 = y0 + 1;
                double xa = x0 + p.Station * space, xb = xa + space;
                var cands = new List<(int N, double Overlap, double Dist)>();
                foreach (var s in l.Slots)
                {
                    // ((0.3.29) her ceilings (the inner layer) weren't hers before: sound)
                    if (s?.Box == null || s.Side != p.Side || s.IsInner) continue;
                    if (Math.Min(xb, s.Box[3]) - Math.Max(xa, s.Box[0]) <= 0.1) continue;
                    double ov = Math.Min(y1, s.Head) - Math.Max(y0, s.Sill);
                    cands.Add((s.Index, ov, Math.Abs((y0 + y1) / 2 - (s.Sill + s.Head) / 2)));
                }
                var hit = cands.Where(c => c.Overlap > 0.25).OrderByDescending(c => c.Overlap).ToList();
                if (hit.Count == 0 && cands.Count > 0) hit = new List<(int, double, double)> { cands.OrderBy(c => c.Dist).First() };
                for (int k = 0; k < hit.Count; k++)
                {
                    int n = hit[k].N;
                    float h = k == 0 ? hurt[i] : hurt[i] >= d.Cracked ? Math.Min(hurt[i], d.Cracked) : 0f;
                    outHurt[n] = Math.Max(outHurt.TryGetValue(n, out float oh) ? oh : 0f, h);
                    outWear[n] = Math.Max(outWear.TryGetValue(n, out float ow) ? ow : 0f, wear[i]);
                }
            }
            var layout = def.DamageParts;
            foreach (var kv in outHurt)
            {
                int code = layout != null && kv.Key < layout.Count ? layout.Parts[kv.Key].Code : ShipPart.Hull(l.Slots[kv.Key].Side, l.Slots[kv.Key].Station, l.Slots[kv.Key].Band, l.Slots[kv.Key].HalfIndex).Encode();
                keep.Add((code, kv.Value, outWear[kv.Key]));
            }
            return (keep.Select(k => k.Item1).ToArray(), keep.Select(k => k.Item2).ToArray(), keep.Select(k => k.Item3).ToArray());
        }
    }

    /// <summary>
    /// (0.3.26, step D1) Hull panels made from a design that has none (the tests', until a design has its own): per side,
    /// frame space and whole block of height from the keel, its hull shell's voxels (ShipPartMap.IsShell) between its
    /// frame strips. All filled; no geometry (nothing drawn: tests only).
    /// </summary>
    public static class HullSlotSynth
    {
        public static HullSlotDef[] Panels(ShipDefinition def)
        {
            var list = new List<HullSlotDef>();
            var g = def?.Grid;
            var map = def?.PartMap;
            if (g == null || map == null || def.Frames == null) return list.ToArray();
            double x0 = def.Frames.X0, sp = def.Frames.Space;
            var cells = new Dictionary<(int Side, int Station, int Band), List<(int, int, int)>>();
            for (int i = 0; i < g.NX; i++)
                for (int j = 0; j < g.NY; j++)
                    for (int k = 0; k < g.NZ; k++)
                    {
                        if (!g.Solid(i, j, k) || !map.IsShell(i, j, k)) continue;
                        double x = g.OX + (i + 0.5) * g.V, y = g.OY + (j + 0.5) * g.V, z = g.OZ + (k + 0.5) * g.V;
                        if (y >= def.DeckY - 0.25 || y < def.KeelY + 0.25) continue;
                        double fx = x - x0 - Math.Floor((x - x0) / sp) * sp;
                        if (fx < 0.25 || fx > sp - 0.25) continue;      // (the frames' strips)
                        int side = z < 0 ? -1 : 1, st = map.StationAt(x), band = (int)Math.Floor(y - def.KeelY);
                        if (!cells.TryGetValue((side, st, band), out var c)) cells[(side, st, band)] = c = new List<(int, int, int)>();
                        c.Add((i, j, k));
                    }
            foreach (var kv in cells.OrderBy(k => k.Key.Station).ThenBy(k => k.Key.Band).ThenBy(k => k.Key.Side))
            {
                if (kv.Value.Count < 6) continue;
                double bx0 = kv.Value.Min(c => g.OX + c.Item1 * g.V), bx1 = kv.Value.Max(c => g.OX + (c.Item1 + 1) * g.V);
                double by0 = kv.Value.Min(c => g.OY + c.Item2 * g.V), by1 = kv.Value.Max(c => g.OY + (c.Item2 + 1) * g.V);
                double bz0 = kv.Value.Min(c => g.OZ + c.Item3 * g.V), bz1 = kv.Value.Max(c => g.OZ + (c.Item3 + 1) * g.V);
                int side = kv.Key.Side;
                double zo = side > 0 ? bz1 : bz0, zi = side > 0 ? bz0 : bz1;
                list.Add(new HullSlotDef
                {
                    Id = $"hull:{(side > 0 ? "s" : "p")}:{kv.Key.Station}:{kv.Key.Band}", Side = side, Station = kv.Key.Station, Band = kv.Key.Band,
                    Box = new[] { bx0, by0, bz0, bx1, by1, bz1 },
                    Outer = new[] { new[] { bx0, by0, zo }, new[] { bx1, by0, zo }, new[] { bx1, by1, zo }, new[] { bx0, by1, zo } },
                    Inner = new[] { new[] { bx0, by0, zi }, new[] { bx1, by0, zi }, new[] { bx1, by1, zi }, new[] { bx0, by1, zi } },
                    Normal = new[] { 0.0, 0, side }, Area = (bx1 - bx0) * (by1 - by0), Sill = by0, Head = by1,
                    Room = def.RoomAt((bx0 + bx1) / 2, (by0 + by1) / 2, 0)?.Code,
                    Solids = kv.Value.Select(c => new[] { g.OX + c.Item1 * g.V, g.OY + c.Item2 * g.V, g.OZ + c.Item3 * g.V, g.OX + (c.Item1 + 1) * g.V, g.OY + (c.Item2 + 1) * g.V, g.OZ + (c.Item3 + 1) * g.V }).ToArray(),
                });
            }
            return list.ToArray();
        }

        /// <summary>A design given these hull panels (null: none again): her layouts made again.</summary>
        public static void Give(ShipDefinition def, HullSlotDef[] slots)
        {
            def.HullSlots = slots;
            def.Hull = HullSlotLayout.Build(def);
            def.DamageParts = new DamageLayout(def);
        }
    }

    /// <summary>(0.3.26) Her collision with both families' differences from the design made (slot id → state): a wreck's, or a copy.</summary>
    public static class SlotGrids
    {
        public static VoxelGrid Applied(ShipDefinition def, IDictionary<string, int> delta)
        {
            if (delta == null || delta.Count == 0 || def?.Slots == null && def?.Hull == null) return def?.Grid;
            var g = def.Grid.Clone();
            foreach (var kv in delta)
            {
                bool solid = kv.Value != (int)SlotState.Empty;
                if (def.Slots?.Get(kv.Key) is DeckSlotDef d && d.Box != null)
                {
                    if (solid == !d.StartsEmpty) continue;
                    foreach (var b in d.SolidBoxes) g.Fill(b, solid);
                }
                else if (def.Hull?.Get(kv.Key) is HullSlotDef h && h.Box != null)
                {
                    if (solid == !h.StartsEmpty) continue;
                    foreach (var b in h.SolidBoxes) g.Fill(b, solid);
                }
            }
            return g;
        }
    }
}
