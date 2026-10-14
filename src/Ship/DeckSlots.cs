using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Datastructures;

namespace Hansa
{
    /// <summary>(0.3.20, step B; docs/slots.md) A slot's state: in (sound), cracked (step D: drawn damaged, still solid), taken up.</summary>
    public enum SlotState : byte { Filled = 0, Cracked = 1, Empty = 2 }

    /// <summary>What laying a slot costs and taking it up gives back (docs/slots.md §2).</summary>
    public class SlotMaterialDef
    {
        public string Block = "game:planks-oak-ud";
        public int Count = 1;
    }

    /// <summary>
    /// (0.3.20, step B; docs/slots.md) One deck cell the design lets be taken up: a block cell of a deck's planking on her
    /// grid. Its index is its place in the definition's <c>deckSlots</c> (the mesh file's <c>slotQuads</c> refer to it).
    /// </summary>
    public class DeckSlotDef
    {
        public string Id;
        public string Deck;
        public double[] Box;
        public double[][] Solids;
        public SlotMaterialDef Material;
        /// <summary>"filled" (default) or "empty": an opening as designed (a hatchway, a stair well).</summary>
        public string Start;
        /// <summary>
        /// (0.3.27, the user's stairs and beams; docs/slots.md §10) null: a deck cell; "beam": a length of a deck beam under
        /// one cell's width (a beam segment), taken out of a well's beam once the deck either side of it is open. Its Box is
        /// the beam's collision there, its top the deck's underside.
        /// </summary>
        public string Kind;
        /// <summary>(0.3.27) A beam segment: its beam ("main:-6": the deck and the beam's x), and the two deck cells either side of it (ids).</summary>
        public string Beam;
        public string[] Beside;

        [JsonIgnore] public int Index;
        [JsonIgnore] public bool StartsEmpty => Start == "empty";
        [JsonIgnore] public SlotState DesignState => StartsEmpty ? SlotState.Empty : SlotState.Filled;
        [JsonIgnore] public double[][] SolidBoxes => Kind == "gunport" ? Array.Empty<double[]>() : Solids != null && Solids.Length > 0 ? Solids : new[] { Box };
        [JsonIgnore] public double Top => Box[4];
        [JsonIgnore] public double CX => (Box[0] + Box[3]) / 2;
        [JsonIgnore] public double CZ => (Box[2] + Box[5]) / 2;
        [JsonIgnore] public SlotMaterialDef Mat => Material ?? new SlotMaterialDef();
        /// <summary>(0.3.27) A length of a deck beam, not a deck cell.</summary>
        [JsonIgnore] public bool IsBeam => Kind == "beam";
        /// <summary>(0.3.28, step D2) A plain deck cell (no kind: not a beam's length nor any other kind of deck slot): one that takes shots as a damage part.</summary>
        [JsonIgnore] public bool IsCell => Kind == null;
        /// <summary>
        /// (0.3.28, step D2.4; docs/slots.md §11) A port's cover: the planking over a castle gun port's hole through her side
        /// (filled: planked over, no lid, no gun; empty: the lidded port). <see cref="Gun"/> its gun fixture.
        /// </summary>
        [JsonIgnore] public bool IsPort => Kind == "port";
        /// <summary>(0.3.28) A port cover's gun (fixture code).</summary>
        public string Gun;
        /// <summary>
        /// (0.3.29, docs/slots.md §12) A castle port itself (kind "gunport", filled: the lidded port is in her side: its lid,
        /// its ring bolts, its gun place), Cover its board (the deck slot of kind "port" over its hole, filled: boarded over).
        /// Neither in: an opening. It has no voxels of its own (the hole's are the board's).
        /// </summary>
        public string Cover;
        [JsonIgnore] public bool IsGunPort => Kind == "gunport";
    }

    /// <summary>
    /// (0.3.20, step B) A design's deck slots worked out once (both sides alike, from the definition only): by id, by cell
    /// (the deck's top, the cell on her grid), by collision voxel; each one's neighbours on its deck (the four ways), and the
    /// chunks they're drawn in (8 × 8 cells of a deck).
    /// </summary>
    public class DeckSlotLayout : ISlotLayout
    {
        public const int ChunkCells = 8;
        public readonly ShipDefinition Def;
        public readonly DeckSlotDef[] Slots;
        readonly Dictionary<string, int> byId = new();
        readonly Dictionary<(int Top, int I, int K), int> byCell = new();
        readonly Dictionary<long, int> byVoxel = new();
        /// <summary>The decks' tops that have slots (ship-local y), and the planking's thickness under each.</summary>
        public readonly List<(double Top, double Thick)> Tops = new();
        /// <summary>The grid's offsets (the cells' corners at whole blocks plus these).</summary>
        public readonly double GX, GZ;
        /// <summary>Each slot's neighbour the four ways (0 +x, 1 +z, 2 -x, 3 -z) on its deck, or -1.</summary>
        public readonly int[][] Neighbours;
        /// <summary>Each slot's chunk, and each chunk's slots.</summary>
        public readonly int[] ChunkOf;
        public readonly List<List<int>> Chunks = new();
        /// <summary>Problems found building it (a slot with no box, two with one id, a voxel in two): the offline checks list them.</summary>
        public readonly List<string> Problems = new();
        /// <summary>(0.3.27) The beam segments (by their beam, along it: -z first), and each one's index in its beam's list.</summary>
        public readonly Dictionary<string, List<int>> Beams = new();
        /// <summary>(0.3.27) Each beam segment's two deck cells beside it (-1 none).</summary>
        public readonly int[][] BesideOf;
        /// <summary>(0.3.28, D2.4) The port covers (deck slots of kind "port"), each drawn in a chunk of its own.</summary>
        public readonly List<int> Ports = new();

        public int Count => Slots.Length;
        // ((0.3.26) the slot family's interface: SlotStates and SlotMeshPlan for deck cells and hull panels alike)
        public int ChunkCount => Chunks.Count;
        public string IdOf(int n) => n >= 0 && n < Slots.Length ? Slots[n]?.Id : null;
        public SlotState DesignStateOf(int n) => n >= 0 && n < Slots.Length ? Slots[n]?.DesignState ?? SlotState.Filled : SlotState.Filled;
        public double[][] SolidBoxesOf(int n) => n >= 0 && n < Slots.Length && Slots[n]?.Box != null ? Slots[n].SolidBoxes : null;
        public int ChunkOfSlot(int n) => n >= 0 && n < ChunkOf.Length ? ChunkOf[n] : -1;

        DeckSlotLayout(ShipDefinition def, DeckSlotDef[] slots)
        {
            Def = def;
            Slots = slots;
            var first = slots.FirstOrDefault(s => s?.Box?.Length == 6);
            if (def.BlockGrid?.Origin != null) { GX = def.BlockGrid.Frac(0); GZ = def.BlockGrid.Frac(2); }
            else if (first != null) { GX = Frac(first.Box[0]); GZ = Frac(first.Box[2]); }
            Neighbours = new int[slots.Length][];
            ChunkOf = new int[slots.Length];
            BesideOf = new int[slots.Length][];
            var g = def.Grid;
            for (int n = 0; n < slots.Length; n++)
            {
                var s = slots[n];
                Neighbours[n] = new[] { -1, -1, -1, -1 };
                ChunkOf[n] = -1;
                if (s == null) { Problems.Add($"slot {n} is null"); continue; }
                s.Index = n;
                if (s.Box == null || s.Box.Length != 6) { Problems.Add($"slot {s.Id ?? n.ToString()} has no box"); continue; }
                if (string.IsNullOrEmpty(s.Id)) s.Id = $"{s.Deck}:{CellI(s.CX)}:{CellK(s.CZ)}";
                if (!byId.TryAdd(s.Id, n)) Problems.Add($"two slots {s.Id}");
                if (s.IsBeam)
                {
                    // ((0.3.27) a beam segment: by its beam, not on the deck's cells)
                    string bk = s.Beam ?? s.Id;
                    if (!Beams.TryGetValue(bk, out var bl)) Beams[bk] = bl = new List<int>();
                    bl.Add(n);
                }
                else if (s.IsPort || s.IsGunPort) Ports.Add(n);        // ((0.3.28) a port's cover, (0.3.29) a port: in her side, not on a deck's cells
                else
                {
                    var key = (TopKey(s.Top), CellI(s.CX), CellK(s.CZ));
                    if (!byCell.TryAdd(key, n)) Problems.Add($"slots {Slots[byCell[key]].Id} and {s.Id} on one cell");
                    double thick = s.Box[4] - s.Box[1];
                    if (!Tops.Any(t => Math.Abs(t.Top - s.Top) < 1e-3)) Tops.Add((s.Top, thick));
                }
                if (g != null)
                    foreach (var b in s.SolidBoxes)
                        foreach (long v in Voxels(g, b))
                            if (!byVoxel.TryAdd(v, n)) Problems.Add($"a voxel of {s.Id} is {Slots[byVoxel[v]].Id}'s too");
            }
            // neighbours and chunks
            var chunkIx = new Dictionary<(int, int, int), int>();
            for (int n = 0; n < slots.Length; n++)
            {
                var s = slots[n];
                if (s?.Box == null || s.Box.Length != 6 || !s.IsCell) continue;
                int top = TopKey(s.Top), i = CellI(s.CX), k = CellK(s.CZ);
                for (int dir = 0; dir < 4; dir++)
                    if (byCell.TryGetValue((top, i + MountLayout.DX[dir], k + MountLayout.DZ[dir]), out int nb)) Neighbours[n][dir] = nb;
                var ck = (top, FloorDiv(i, ChunkCells), FloorDiv(k, ChunkCells));
                if (!chunkIx.TryGetValue(ck, out int c)) { chunkIx[ck] = c = Chunks.Count; Chunks.Add(new List<int>()); }
                ChunkOf[n] = c;
                Chunks[c].Add(n);
            }
            // ((0.3.28) a port cover: a chunk of its own)
            foreach (int n in Ports) { ChunkOf[n] = Chunks.Count; Chunks.Add(new List<int> { n }); }
            // ((0.3.27) a beam segment: its cells beside it, and drawn in the first one's chunk (its quads face them))
            foreach (var kv in Beams) kv.Value.Sort((a, b) => slots[a].CZ.CompareTo(slots[b].CZ));
            for (int n = 0; n < slots.Length; n++)
            {
                var s = slots[n];
                if (s?.Box == null || !s.IsBeam) continue;
                BesideOf[n] = (s.Beside ?? Array.Empty<string>()).Select(IndexOf).ToArray();
                if (BesideOf[n].Length != 2 || BesideOf[n].Any(b => b < 0)) Problems.Add($"beam segment {s.Id}: its cells beside it ({string.Join(", ", s.Beside ?? Array.Empty<string>())}) aren't two deck cells");
                int c0 = BesideOf[n].FirstOrDefault(b => b >= 0, -1);
                if (c0 >= 0 && ChunkOf[c0] >= 0) { ChunkOf[n] = ChunkOf[c0]; Chunks[ChunkOf[c0]].Add(n); }
                else Problems.Add($"beam segment {s.Id} has no chunk");
            }
        }

        /// <summary>(0.3.27) A beam segment's neighbours along its beam (the one at -z, the one at +z; -1 none or not next to it).</summary>
        public (int Minus, int Plus) BeamNeighbours(int n)
        {
            var s = n >= 0 && n < Slots.Length ? Slots[n] : null;
            if (s?.IsBeam != true || !Beams.TryGetValue(s.Beam ?? s.Id, out var list)) return (-1, -1);
            int i = list.IndexOf(n);
            int m = i > 0 && Math.Abs(Slots[list[i - 1]].Box[5] - s.Box[2]) < 0.01 ? list[i - 1] : -1;
            int p = i >= 0 && i + 1 < list.Count && Math.Abs(Slots[list[i + 1]].Box[2] - s.Box[5]) < 0.01 ? list[i + 1] : -1;
            return (m, p);
        }

        /// <summary>A design's slots (null: it has none).</summary>
        public static DeckSlotLayout Build(ShipDefinition def) =>
            def?.DeckSlots == null || def.DeckSlots.Length == 0 ? null : new DeckSlotLayout(def, def.DeckSlots);

        static double Frac(double v)
        {
            double f = v - Math.Floor(v);
            return f > 1 - 1e-9 ? 0 : f;
        }

        static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
        static int TopKey(double y) => (int)Math.Round(y * 64);
        public int CellI(double x) => (int)Math.Floor(x - GX + 1e-9);
        public int CellK(double z) => (int)Math.Floor(z - GZ + 1e-9);

        /// <summary>The voxels (grid indices packed) whose middles are in a box.</summary>
        public static IEnumerable<long> Voxels(VoxelGrid g, double[] box)
        {
            int i0 = Math.Max(0, (int)Math.Ceiling((box[0] - g.OX) / g.V - 0.5)), i1 = Math.Min(g.NX - 1, (int)Math.Floor((box[3] - g.OX) / g.V - 0.5));
            int j0 = Math.Max(0, (int)Math.Ceiling((box[1] - g.OY) / g.V - 0.5)), j1 = Math.Min(g.NY - 1, (int)Math.Floor((box[4] - g.OY) / g.V - 0.5));
            int k0 = Math.Max(0, (int)Math.Ceiling((box[2] - g.OZ) / g.V - 0.5)), k1 = Math.Min(g.NZ - 1, (int)Math.Floor((box[5] - g.OZ) / g.V - 0.5));
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    for (int k = k0; k <= k1; k++)
                        yield return ((long)i * g.NY + j) * g.NZ + k;
        }

        public int IndexOf(string id) => id != null && byId.TryGetValue(id, out int n) ? n : -1;
        public DeckSlotDef Get(string id) => IndexOf(id) is int n && n >= 0 ? Slots[n] : null;

        /// <summary>The slot whose collision voxel this is (grid indices), or -1.</summary>
        public int AtVoxel(VoxelGrid g, int i, int j, int k) =>
            g != null && i >= 0 && j >= 0 && k >= 0 && i < g.NX && j < g.NY && k < g.NZ && byVoxel.TryGetValue(((long)i * g.NY + j) * g.NZ + k, out int n) ? n : -1;

        /// <summary>The slot whose planking a point (ship-local) is in (its top counts, within 0.01), or -1.</summary>
        public int At(double x, double y, double z)
        {
            foreach (var (top, thick) in Tops)
            {
                if (y > top + 0.01 || y < top - thick - 0.01) continue;
                if (byCell.TryGetValue((TopKey(top), CellI(x), CellK(z)), out int n)) return n;
            }
            return -1;
        }

        /// <summary>The slot on a deck's top (y) at a cell's (x, z), or -1.</summary>
        public int OnDeck(double top, double x, double z) =>
            byCell.TryGetValue((TopKey(top), CellI(x), CellK(z)), out int n) ? n : -1;

        /// <summary>The slots a box's footprint (x, z) covers more than eps of, on a deck's top (any deck if NaN).</summary>
        public IEnumerable<int> Under(double[] box, double top, double eps = 0.02)
        {
            int i0 = CellI(box[0] + eps), i1 = CellI(box[3] - eps), k0 = CellK(box[2] + eps), k1 = CellK(box[5] - eps);
            foreach (var t in Tops)
            {
                if (!double.IsNaN(top) && Math.Abs(t.Top - top) > 0.01) continue;
                int tk = TopKey(t.Top);
                for (int i = i0; i <= i1; i++)
                    for (int k = k0; k <= k1; k++)
                        if (byCell.TryGetValue((tk, i, k), out int n)) yield return n;
            }
        }

        /// <summary>The deck slots in a deck's (by its top) rectangle of cells (x0..x1, z0..z1 on the grid lines), or null if a cell in it isn't one.</summary>
        public List<int> Rect(double top, double x0, double z0, double x1, double z1)
        {
            var list = new List<int>();
            for (int i = CellI(x0 + 0.5); i <= CellI(x1 - 0.5); i++)
                for (int k = CellK(z0 + 0.5); k <= CellK(z1 - 0.5); k++)
                {
                    if (!byCell.TryGetValue((TopKey(top), i, k), out int n)) return null;
                    list.Add(n);
                }
            return list;
        }
    }

    /// <summary>
    /// (0.3.20, step B) A ship's deck slots' states: each in, cracked or taken up; saved (WatchedAttributes "hansaSlots")
    /// as the differences from the design (slot id → state), applied to her collision grid.
    /// </summary>
    public class SlotStates
    {
        public readonly ISlotLayout Layout;
        readonly SlotState[] s;
        /// <summary>Bumped on every change (the renderer and her caches follow it).</summary>
        public int Version;
        /// <summary>The slots changed since the renderer last looked (it takes them: <see cref="TakeChanged"/>).</summary>
        readonly HashSet<int> changed = new();

        public SlotStates(ISlotLayout layout)
        {
            Layout = layout;
            s = new SlotState[layout?.Count ?? 0];
            Reset();
        }

        public int Count => s.Length;
        public SlotState this[int i] => i >= 0 && i < s.Length ? s[i] : SlotState.Filled;
        /// <summary>In her (sound or cracked): solid, drawn.</summary>
        public bool Filled(int i) => i >= 0 && i < s.Length && s[i] != SlotState.Empty;

        public void Reset()
        {
            for (int i = 0; i < s.Length; i++) s[i] = Layout.DesignStateOf(i);
            Version++;
        }

        public void Set(int i, SlotState state)
        {
            if (i < 0 || i >= s.Length || s[i] == state) return;
            s[i] = state;
            changed.Add(i);
            Version++;
        }

        /// <summary>The slots changed since the last call (the renderer's chunks to build again).</summary>
        public List<int> TakeChanged()
        {
            var list = changed.ToList();
            changed.Clear();
            return list;
        }

        /// <summary>The differences from the design (slot id → state), for a wreck.</summary>
        public Dictionary<string, int> Delta()
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < s.Length; i++)
                if (Layout.IdOf(i) is string id && s[i] != Layout.DesignStateOf(i)) d[id] = (int)s[i];
            return d;
        }

        /// <summary>How many differ from the design.</summary>
        public int Differences => Enumerable.Range(0, s.Length).Count(i => s[i] != Layout.DesignStateOf(i));

        /// <summary>
        /// The differences from the design (slot id → state). clear: the tree emptied first ((0.3.26) false: another family's
        /// differences are in it too: the deck cells' and the hull panels' share "hansaSlots").
        /// </summary>
        public void ToTree(ITreeAttribute t, bool clear = true)
        {
            if (clear) foreach (var key in t.Select(kv => kv.Key).ToList()) t.RemoveAttribute(key);
            for (int i = 0; i < s.Length; i++)
            {
                if (Layout.IdOf(i) is string id && s[i] != Layout.DesignStateOf(i)) t.SetInt(id, (int)s[i]);
            }
        }

        /// <summary>The design's states with the saved differences on them (ids the design no longer has are dropped). Every slot whose state changed is marked.</summary>
        public void FromTree(ITreeAttribute t)
        {
            var old = (SlotState[])s.Clone();
            for (int i = 0; i < s.Length; i++) s[i] = Layout.DesignStateOf(i);
            if (t != null)
                foreach (var kv in t)
                {
                    int n = Layout.IndexOf(kv.Key);
                    if (n < 0) continue;
                    int v = t.GetInt(kv.Key);
                    if (v >= 0 && v <= 2) s[n] = (SlotState)v;
                }
            for (int i = 0; i < s.Length; i++) if (s[i] != old[i]) changed.Add(i);
            Version++;
        }

        /// <summary>Her collision as the design has it, made as the states are: a taken-up slot's voxels cleared, one laid where the design has an opening set.</summary>
        public void Apply(VoxelGrid grid)
        {
            if (grid == null) return;
            for (int i = 0; i < s.Length; i++)
            {
                var boxes = Layout.SolidBoxesOf(i);
                if (boxes == null) continue;
                bool solid = s[i] != SlotState.Empty;
                if (solid == (Layout.DesignStateOf(i) != SlotState.Empty)) continue;
                foreach (var b in boxes) grid.Fill(b, solid);
            }
        }
    }

    /// <summary>
    /// (0.3.20, step B) The deck cells' quads in a design's mesh file (groups "slot:deck", "slot:deck:interior", with
    /// <c>slotQuads</c>: each quad's owner and facing slot) sorted by the chunk they're drawn in, and which chunks a slot's
    /// change reaches. A quad is drawn while (owner -1 or filled) and (facing -1 or empty) (docs/slots.md §3). Pure: the
    /// offline tests build chunks with it and time them.
    /// </summary>
    public class SlotMeshPlan
    {
        public static readonly string[] Groups = { "slot:deck", "slot:deck:interior" };
        /// <summary>(0.3.26, step D1; docs/slots.md §9) The hull panels' groups: the outside (the topsides'), the bottom tarred and clad (one of the two drawn), the ceiling and reveals in her rooms' light.</summary>
        public static readonly string[] HullGroups = { "slot:hull", "slot:hull:bottom", "slot:hull:bottomclad", "slot:hull:interior" };
        public readonly ISlotLayout Layout;
        /// <summary>The groups this plan is of (<see cref="Groups"/> or <see cref="HullGroups"/>).</summary>
        public readonly string[] MyGroups;
        /// <summary>Per group: each quad's owner and facing slot (-1 none).</summary>
        public readonly Dictionary<string, (int[] Owner, int[] Facing)> Refs = new();
        /// <summary>Per group and chunk: its quads (indices into the group).</summary>
        public readonly Dictionary<(string Group, int Chunk), List<int>> ChunkQuads = new();
        /// <summary>Per slot: the chunks whose quads refer to it (owner or facing).</summary>
        public readonly List<int>[] ChunksOfSlot;
        public readonly List<string> Problems = new();
        public int ChunkCount => Layout.ChunkCount;

        public SlotMeshPlan(ISlotLayout layout, ShipSkinFile file, string[] groups = null)
        {
            Layout = layout;
            MyGroups = groups ?? Groups;
            ChunksOfSlot = new List<int>[layout.Count];
            for (int i = 0; i < layout.Count; i++) ChunksOfSlot[i] = new List<int>();
            foreach (string g in MyGroups)
            {
                if (file?.Groups == null || !file.Groups.TryGetValue(g, out var quads) || quads == null) continue;
                int[] refs = null;
                file.SlotQuads?.TryGetValue(g, out refs);
                var owner = new int[quads.Length];
                var facing = new int[quads.Length];
                if (refs == null || refs.Length < quads.Length * 2) Problems.Add($"{g}: slotQuads has {(refs?.Length ?? 0) / 2} pairs for {quads.Length} quads");
                for (int q = 0; q < quads.Length; q++)
                {
                    int o = refs != null && 2 * q + 1 < refs.Length ? refs[2 * q] : -1, f = refs != null && 2 * q + 1 < refs.Length ? refs[2 * q + 1] : -1;
                    if (o >= layout.Count || f >= layout.Count) { Problems.Add($"{g} quad {q} refers to slot {Math.Max(o, f)} (there are {layout.Count})"); o = o >= layout.Count ? -1 : o; f = f >= layout.Count ? -1 : f; }
                    owner[q] = o;
                    facing[q] = f;
                    int c = o >= 0 ? layout.ChunkOfSlot(o) : f >= 0 ? layout.ChunkOfSlot(f) : -1;
                    if (c < 0) { c = 0; if (layout.ChunkCount == 0) continue; }
                    var key = (g, c);
                    if (!ChunkQuads.TryGetValue(key, out var list)) ChunkQuads[key] = list = new List<int>();
                    list.Add(q);
                    if (o >= 0 && !ChunksOfSlot[o].Contains(c)) ChunksOfSlot[o].Add(c);
                    if (f >= 0 && !ChunksOfSlot[f].Contains(c)) ChunksOfSlot[f].Add(c);
                }
                Refs[g] = (owner, facing);
            }
        }

        /// <summary>Is this quad of a group drawn with these states?</summary>
        public bool Shown(string group, int q, SlotStates st)
        {
            var (o, f) = Refs[group];
            return (o[q] < 0 || st.Filled(o[q])) && (f[q] < 0 || !st.Filled(f[q]));
        }

        /// <summary>A chunk's quads of a group drawn with these states (indices into the group).</summary>
        public List<int> Visible(string group, int chunk, SlotStates st)
        {
            var list = new List<int>();
            if (!ChunkQuads.TryGetValue((group, chunk), out var qs)) return list;
            foreach (int q in qs) if (Shown(group, q, st)) list.Add(q);
            return list;
        }

        /// <summary>The chunks to build again when these slots change.</summary>
        public HashSet<int> Affected(IEnumerable<int> slots)
        {
            var set = new HashSet<int>();
            foreach (int s in slots)
                if (s >= 0 && s < ChunksOfSlot.Length) foreach (int c in ChunksOfSlot[s]) set.Add(c);
            return set;
        }
    }
}

namespace Hansa
{
    /// <summary>
    /// (0.3.20, step B; docs/slots.md §5) A hatch over an opening in a deck: the opening a cell is in (the empty cells
    /// joined to it on its deck), and the hatch piece that fits it (a rectangle of 1–4 × 1–4 cells), flush with the deck,
    /// its leaves hinged on the opening's two long sides. Pure: both sides and the tests get the same.
    /// </summary>
    public static class HatchPlanner
    {
        public const int MaxCells = 4;
        /// <summary>A leaf's thickness (its top at the deck's top).</summary>
        public const double Thick = 0.125;
        /// <summary>A ladder standing on a hatch's opening hangs its foot this far over the deck (its swing is checked against it from there up).</summary>
        public const double HangOver = 1.0;
        /// <summary>How far a hatch's leaves swing up open (degrees).</summary>
        public const float OpenAngle = 100f;

        /// <summary>The cells of the opening this one is in (open(n): an empty cell no hatch covers), on its deck.</summary>
        public static List<int> Opening(DeckSlotLayout l, int start, Func<int, bool> open)
        {
            var list = new List<int>();
            if (l == null || start < 0 || start >= l.Count || !open(start)) return list;
            var seen = new HashSet<int> { start };
            var q = new Queue<int>();
            q.Enqueue(start);
            while (q.Count > 0 && list.Count < 64)
            {
                int n = q.Dequeue();
                list.Add(n);
                foreach (int nb in l.Neighbours[n])
                    if (nb >= 0 && seen.Add(nb) && open(nb)) q.Enqueue(nb);
            }
            return list;
        }

        /// <summary>The bounds of some cells (x0, z0, x1, z1) and their deck's top.</summary>
        public static double[] Bounds(DeckSlotLayout l, IEnumerable<int> cells, out double top)
        {
            double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
            top = double.NaN;
            foreach (int n in cells)
            {
                var b = l.Slots[n].Box;
                x0 = Math.Min(x0, b[0]); z0 = Math.Min(z0, b[2]); x1 = Math.Max(x1, b[3]); z1 = Math.Max(z1, b[5]);
                top = b[4];
            }
            return new[] { x0, z0, x1, z1 };
        }

        /// <summary>A hatch over these cells (an opening), or why not (Error: a lang code). glazed: a window trapdoor's.</summary>
        /// <param name="hinge">"x": its leaves hinged along x (on its z sides), "z": along z; null: along its long sides, along x when square</param>
        public static PiecePlan Plan(DeckSlotLayout l, List<int> cells, bool glazed = false, string hinge = null)
        {
            var plan = new PiecePlan { Role = PieceRole.Hatch, Kind = MountKind.Horizontal };
            if (l == null || cells == null || cells.Count == 0) { plan.Error = "hansa:hatch-no-opening"; return plan; }
            var r = Bounds(l, cells, out double top);
            int nx = (int)Math.Round(r[2] - r[0]), nz = (int)Math.Round(r[3] - r[1]);
            if (nx > MaxCells || nz > MaxCells) { plan.Error = "hansa:hatch-too-big"; return plan; }
            if (nx * nz != cells.Count || cells.Any(n => Math.Abs(l.Slots[n].Top - top) > 0.01)) { plan.Error = "hansa:hatch-not-rect"; return plan; }
            plan.Box = new[] { r[0], top - Thick, r[1], r[2], top, r[3] };
            plan.Sel.Add(new[] { r[0], top - Thick, r[1], r[2], top + 0.0625, r[3] });
            foreach (int n in cells)
            {
                plan.Mounts.Add(CellMount(l.Slots[n]));
                foreach (var b in l.Slots[n].SolidBoxes) plan.Solids.Add((double[])b.Clone());
            }
            // (its leaves hinged along its long sides: along x unless it's longer across her)
            plan.Dir = hinge == "x" ? 0 : hinge == "z" ? 1 : nx >= nz ? 0 : 1;
            plan.Slots = glazed ? 1 : 0;
            return plan;
        }

        /// <summary>
        /// (0.3.20) Where a hatch's leaves (its box, hinged as dir says) run into something as they swing open (0..100°, every
        /// 10°): her collision (solid(x, y, z)) or a ladder's line within 0.25 of a leaf; null: they swing clear. Points on
        /// each leaf every quarter block, at the middle of its thickness.
        /// </summary>
        public static double[] SwingClash(double[] box, int dir, Func<double, double, double, bool> solid, IEnumerable<LadderDef> ladders)
        {
            // (a ladder up to this deck through the opening ends under the leaves: the hatch is its gate; one standing over the deck counts)
            // (one whose foot is on the opening hangs over it, drawn from a block over the deck up: docs/slots.md §5)
            var lad = new List<(double[] P, double[] Q)>();
            foreach (var x in ladders ?? Enumerable.Empty<LadderDef>())
            {
                if (x?.Bottom == null || x.Top == null || Math.Max(x.Bottom[1], x.Top[1]) <= box[4] + 0.05) continue;
                double[] p = x.Bottom, q = x.Top;
                if (p[1] > q[1]) (p, q) = (q, p);
                bool onIt = p[1] < box[4] + HangOver && p[0] > box[0] && p[0] < box[3] && p[2] > box[2] && p[2] < box[5];
                if (onIt && q[1] > box[4] + HangOver)
                {
                    double t = (box[4] + HangOver - p[1]) / (q[1] - p[1]);
                    p = new[] { p[0] + (q[0] - p[0]) * t, box[4] + HangOver, p[2] + (q[2] - p[2]) * t };
                }
                else if (onIt) continue;
                lad.Add((p, q));
            }
            foreach (var (b, h, axis, sign) in Leaves(box, dir))
            {
                bool alongX = axis == 0;
                double a0 = alongX ? b[0] : b[2], a1 = alongX ? b[3] : b[5];
                double c0 = alongX ? b[2] : b[0], c1 = alongX ? b[5] : b[3];
                double hc = alongX ? h[2] : h[0];
                double far = Math.Abs(c1 - hc) > Math.Abs(c0 - hc) ? c1 : c0, width = Math.Abs(far - hc), cs = Math.Sign(far - hc);
                double dy = -Thick / 2;
                for (int deg = 10; deg <= OpenAngle; deg += 10)
                {
                    double t = sign * deg * Math.PI / 180, cos = Math.Cos(t), sin = Math.Sin(t);
                    for (double a = a0 + 0.1; a < a1 - 0.05; a += 0.25)
                        for (double w = 0.15; w < width - 0.02; w += 0.25)
                        {
                            double dc = cs * w;
                            double x, y, z;
                            if (alongX) { x = a; y = h[1] + dy * cos - dc * sin; z = hc + dy * sin + dc * cos; }
                            else { x = hc + dc * cos - dy * sin; y = h[1] + dc * sin + dy * cos; z = a; }
                            if (y < h[1] + 0.05) continue;
                            if (solid(x, y, z)) return new[] { x, y, z };
                            foreach (var (p, q) in lad)
                                if (SegmentDistance(p, q, x, y, z) < 0.25) return new[] { x, y, z };
                        }
                }
            }
            return null;
        }

        static double SegmentDistance(double[] p, double[] q, double x, double y, double z)
        {
            double vx = q[0] - p[0], vy = q[1] - p[1], vz = q[2] - p[2];
            double len = vx * vx + vy * vy + vz * vz;
            double t = len < 1e-12 ? 0 : Math.Clamp(((x - p[0]) * vx + (y - p[1]) * vy + (z - p[2]) * vz) / len, 0, 1);
            double dx = p[0] + vx * t - x, dy = p[1] + vy * t - y, dz = p[2] + vz * t - z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>The place a hatch takes over a cell ("cell:" + its id): a cell under a hatch is taken.</summary>
        public static string CellMount(DeckSlotDef d) => "cell:" + d.Id;

        /// <summary>
        /// A hatch's leaves (its box: x0 y0 z0 x1 y1 z1, Dir 0: hinged along x on its z sides, 1: along z on its x sides):
        /// each leaf's box shut, its hinge line (a point and the axis, 0 x or 2 z) and which way it swings up (+1, -1 about
        /// that axis, right-handed).
        /// </summary>
        public static List<(double[] Box, double[] Hinge, int Axis, int Sign)> Leaves(double[] box, int dir)
        {
            var list = new List<(double[], double[], int, int)>();
            if (box == null) return list;
            bool alongX = dir == 0;
            double a0 = alongX ? box[2] : box[0], a1 = alongX ? box[5] : box[3];
            double across = a1 - a0;
            double y0 = box[1], y1 = box[4];
            const double gap = 1.0 / 64;
            if (across < 1.5)
            {
                // one leaf, hinged on the -z (or -x) side
                list.Add((alongX ? new[] { box[0], y0, a0, box[3], y1, a1 } : new[] { a0, y0, box[2], a1, y1, box[5] },
                    alongX ? new[] { box[0], y1, a0 } : new[] { a0, y1, box[2] }, alongX ? 0 : 2, alongX ? -1 : 1));
                return list;
            }
            double mid = (a0 + a1) / 2;
            // (right-handed: about +x a leaf reaching +z from its hinge rises turned by a negative angle, one reaching -z by a
            // positive; about +z one reaching +x by a positive, one reaching -x by a negative)
            list.Add((alongX ? new[] { box[0], y0, a0, box[3], y1, mid - gap } : new[] { a0, y0, box[2], mid - gap, y1, box[5] },
                alongX ? new[] { box[0], y1, a0 } : new[] { a0, y1, box[2] }, alongX ? 0 : 2, alongX ? -1 : 1));
            list.Add((alongX ? new[] { box[0], y0, mid + gap, box[3], y1, a1 } : new[] { mid + gap, y0, box[2], a1, y1, box[5] },
                alongX ? new[] { box[0], y1, a1 } : new[] { a1, y1, box[2] }, alongX ? 0 : 2, alongX ? 1 : -1));
            return list;
        }
    }
}

namespace Hansa
{
    /// <summary>(0.3.20, step B) What open deck cells do to her rooms and places (pure: the ship and the offline tests share it).</summary>
    public static class DeckCells
    {
        /// <summary>Is a place usable with her cells as they are: its cell in (or none), and the cell it needs open open.</summary>
        public static bool MountOpen(MountPoint m, SlotStates st) =>
            m == null || st == null || (m.SlotUnder < 0 || st.Filled(m.SlotUnder)) && (m.SlotOpen < 0 || !st.Filled(m.SlotOpen));

        /// <summary>Her cells as a design's grid has them, with the differences (slot id → state) made: a wreck's, or a copy.</summary>
        public static VoxelGrid Applied(ShipDefinition def, IDictionary<string, int> delta) => SlotGrids.Applied(def, delta);

        /// <summary>
        /// The open cells (open(n): taken up, no shut hatch over it) in a room's ceiling or floor that open it: their other
        /// side isn't one of the design's enclosed rooms (as a well's: docs/modular-pieces.md). Their boxes.
        /// </summary>
        public static List<double[]> Leaks(ShipDefinition def, Func<int, bool> open, RoomDef r)
        {
            var list = new List<double[]>();
            var l = def?.Slots;
            if (l == null || r == null) return list;
            for (int n = 0; n < l.Count; n++)
            {
                var d = l.Slots[n];
                if (d?.Box == null || !d.IsCell || !open(n)) continue;
                double cx = d.CX, cz = d.CZ;
                if (cx < r.Min[0] || cx > r.Max[0] || cz < r.Min[2] || cz > r.Max[2]) continue;
                bool ceiling = Math.Abs(d.Box[1] - r.Max[1]) < 0.3, floor = Math.Abs(d.Top - r.Min[1]) < 0.3;
                if (!ceiling && !floor) continue;
                var other = ceiling ? def.RoomAt(cx, d.Top + 0.5, cz) : def.RoomAt(cx, d.Box[1] - 0.5, cz);
                if (other != null && other != r && other.Enclosed) continue;
                list.Add(d.Box);
            }
            return list;
        }
    }
}

namespace Hansa
{
    /// <summary>
    /// (0.3.20, step B) Deck slots made from a design that has none (the self-test's, until the generator makes a design's
    /// own: docs/slots.md): every whole cell of her decks (not the hold's floor, not the main top) whose planking layer is
    /// solid in her collision, with nothing of hers standing on it (her collision over it, a fixture's or a gun's keep-clear,
    /// a mast). All filled; no geometry (nothing's drawn of them: test worlds only).
    /// </summary>
    public static class DeckSlotSynth
    {
        public static DeckSlotDef[] Cells(ShipDefinition def)
        {
            var list = new List<DeckSlotDef>();
            var g = def?.Grid;
            var l = def?.Mounts;
            if (g == null || l == null) return list.ToArray();
            double gx = def.BlockGrid?.Frac(0) ?? 0, gz = def.BlockGrid?.Frac(2) ?? 0;
            bool AllSolid(double x0, double y0, double z0, double x1, double y1, double z1)
            {
                for (double x = x0 + g.V / 2; x < x1; x += g.V)
                    for (double y = y0 + g.V / 2; y < y1; y += g.V)
                        for (double z = z0 + g.V / 2; z < z1; z += g.V)
                            if (!g.SolidAt(x, y, z)) return false;
                return true;
            }
            foreach (var deck in def.Decks)
            {
                if (deck.Code is "hold" or "top") continue;
                double top = deck.Y;
                for (double x0 = Math.Ceiling(Math.Max(deck.X0, g.OX) - gx) + gx; x0 + 1 <= Math.Min(deck.X1, g.MaxX) + 1e-6; x0 += 1)
                    for (double z0 = Math.Ceiling(g.OZ - gz) + gz; z0 + 1 <= g.MaxZ + 1e-6; z0 += 1)
                    {
                        if (!AllSolid(x0, top - 0.25, z0, x0 + 1, top, z0 + 1)) continue;
                        if (g.Collides(x0, top, z0, x0 + 1, top + 0.25, z0 + 1)) continue;
                        if (l.ClearHitCore(x0 + 0.01, top, z0 + 0.01, x0 + 0.99, top + 1, z0 + 0.99) != null) continue;
                        if (def.Masts.Any(m => Math.Abs(m.X - (x0 + 0.5)) < 0.5 + m.Radius && Math.Abs(m.Z - (z0 + 0.5)) < 0.5 + m.Radius)) continue;
                        list.Add(new DeckSlotDef
                        {
                            Id = $"{deck.Code}:{(int)Math.Round(x0 - gx)}:{(int)Math.Round(z0 - gz)}", Deck = deck.Code,
                            Box = new[] { x0, top - 0.25, z0, x0 + 1, top, z0 + 1 }
                        });
                    }
            }
            return list.ToArray();
        }

        /// <summary>A design given these slots (null: none again): her slot layout and her damage layout (the sea through open cells) made again.</summary>
        public static void Give(ShipDefinition def, DeckSlotDef[] slots)
        {
            def.DeckSlots = slots;
            def.Slots = DeckSlotLayout.Build(def);
            def.DamageParts = new DamageLayout(def);
            def.Mounts?.BindSlots(def.Slots);
        }
    }
}

namespace Hansa
{
    /// <summary>
    /// (0.3.23, note 169) A shut hatch over a ladder's way up: the climber's head stops under it. Pure (the ship, the client's
    /// deck mover and the offline tests).
    /// </summary>
    public static class HatchCover
    {
        /// <summary>The underside (y) of the shut hatch whose cells the ladder's way up comes out under, or NaN.</summary>
        public static double Under(IEnumerable<ShipPiece> pieces, LadderDef l)
        {
            if (pieces == null || l?.Bottom == null || l.Top == null) return double.NaN;
            double[] lo = l.Bottom, hi = l.Top;
            if (lo[1] > hi[1]) (lo, hi) = (hi, lo);
            double best = double.NaN;
            foreach (var p in pieces)
            {
                if (p.Role != PieceRole.Hatch || p.Open || p.Solids.Count == 0) continue;
                var b = PiecePlanner.Union(p.Solids);
                double under = b[1], top = b[4];
                // (the ladder reaches up to the hatch's deck: its top within a little of the deck's top)
                if (hi[1] < under - 0.1 || lo[1] > under) continue;
                // where its way up is at the hatch's underside (the climber's head there), within the opening
                double t = Math.Abs(hi[1] - lo[1]) < 1e-6 ? 1 : Math.Clamp((under - DeckMover.Height - lo[1]) / (hi[1] - lo[1]), 0, 1);
                double x = lo[0] + (hi[0] - lo[0]) * t, z = lo[2] + (hi[2] - lo[2]) * t;
                if (x < b[0] - 0.05 || x > b[3] + 0.05 || z < b[2] - 0.05 || z > b[5] + 0.05) continue;
                if (double.IsNaN(best) || under < best) best = under;
            }
            return best;
        }
    }
}

namespace Hansa
{
    /// <summary>(0.3.27) The beam segments' rules (pure: the ship and the offline tests share them; docs/slots.md §10).</summary>
    public static class BeamRules
    {
        /// <summary>At most this many segments of one beam out (next to each other).</summary>
        public const int MaxOut = 2;

        /// <summary>Why a beam segment can't come out with her cells as they are (a lang code), or null: it's out, the deck either side of it isn't open, too many of its beam are out.</summary>
        public static string TakeProblem(DeckSlotLayout l, SlotStates st, int n)
        {
            var d = l.Slots[n];
            if (!st.Filled(n)) return "hansa:beam-already-out";
            var beside = l.BesideOf[n];
            if (beside == null || beside.Length < 2 || beside.Any(b => b < 0 || st.Filled(b))) return "hansa:beam-deck-in";
            var mine = l.Beams.TryGetValue(d.Beam ?? d.Id, out var list) ? list : new List<int>();
            var outNow = mine.Where(i => i != n && !st.Filled(i)).ToList();
            if (outNow.Count >= MaxOut) return "hansa:beam-too-many";
            if (outNow.Count == 1)
            {
                var (minus, plus) = l.BeamNeighbours(n);
                if (outNow[0] != minus && outNow[0] != plus) return "hansa:beam-too-many";
            }
            return null;
        }

        /// <summary>
        /// The beam segments a planned stair or ladder-stair runs into that could come out for it (ok(n): its rules hold),
        /// when with those out it fits: their indices (only those it needs); null when it fits as it is, or when taking beams
        /// out wouldn't make it fit (its clash is something else), or the set breaks a beam's rule (more than two of one
        /// beam, or two not next to each other).
        /// </summary>
        public static List<int> InTheWay(MountLayout layout, DeckSlotLayout l, SlotStates st, PiecePlan plan, VoxelGrid grid, System.Func<int, bool> ok)
        {
            if (l == null || st == null || plan == null || plan.Error != null || plan.Role is not (PieceRole.Stair or PieceRole.Ladder) || l.Beams.Count == 0) return null;
            if (PiecePlanner.GridClash(layout, plan, grid) == null) return null;
            var bounds = PiecePlanner.PlaceBounds(layout, plan);
            if (bounds == null) return null;
            // (its room reaches up over its cells: the beams under the deck above them too)
            var cand = new List<int>();
            foreach (var kv in l.Beams)
                foreach (int n in kv.Value)
                {
                    if (!st.Filled(n) || !PiecePlanner.Overlap(l.Slots[n].Box, bounds, 0.02)) continue;
                    if (TakeProblem(l, st, n) != null || ok != null && !ok(n)) continue;
                    cand.Add(n);
                }
            if (cand.Count == 0) return null;
            var ignore = cand.SelectMany(n => l.Slots[n].SolidBoxes).ToList();
            if (PiecePlanner.GridClash(layout, plan, grid, ignore) != null) return null;
            var need = new List<int>();
            foreach (int n in cand)
            {
                var rest = cand.Where(c => c != n).SelectMany(c => l.Slots[c].SolidBoxes).ToList();
                if (PiecePlanner.GridClash(layout, plan, grid, rest) != null) need.Add(n);
            }
            if (need.Count == 0) need.Add(cand[0]);
            foreach (var g in need.GroupBy(n => l.Slots[n].Beam ?? l.Slots[n].Id))
            {
                var mine = l.Beams[g.Key];
                var outAfter = mine.Where(i => !st.Filled(i) || g.Contains(i)).OrderBy(i => l.Slots[i].CZ).ToList();
                if (outAfter.Count > MaxOut) return null;
                if (outAfter.Count == 2)
                {
                    var (minus, plus) = l.BeamNeighbours(outAfter[0]);
                    if (outAfter[1] != minus && outAfter[1] != plus) return null;
                }
            }
            return need;
        }
    }

    /// <summary>
    /// (0.3.29, docs/modular-pieces.md §14) A ship laid at an older layout revision of her design, brought to the design's
    /// (pure: the ship and the offline tests share it): her deck slots keep the states they had where a later revision
    /// changed their start (saved: her saved differences, id → state; a slot not in them had the old start); 0.3.28's port
    /// covers (filled: planked over) read as ports in her side's panels (filled: the port in), their halves with them; the
    /// modular entries the revisions brought are remembered as laid, not put (laid: her laid keys; NewlyLaid: added).
    /// </summary>
    public static class LayoutMigration
    {
        public class Result
        {
            public readonly List<string> Notes = new();
            public readonly List<string> NewlyLaid = new();
        }

        public static Result Apply(ShipDefinition def, int rev, IDictionary<string, int> saved, SlotStates slots, SlotStates hull, ISet<string> laid)
        {
            var r = new Result();
            if (def == null) return r;
            var l = def.Slots;
            bool Saved(string id) => saved != null && saved.ContainsKey(id);
            foreach (var step in def.LayoutBefore ?? Array.Empty<LayoutStepDef>())
            {
                if (step == null || step.Rev <= rev) continue;
                if (step.Cells != null && slots != null && l != null)
                    foreach (var kv in step.Cells)
                    {
                        int n = l.IndexOf(kv.Key);
                        if (n < 0 || Saved(kv.Key)) continue;
                        var was = kv.Value == "empty" ? SlotState.Empty : SlotState.Filled;
                        if (slots[n] == was) continue;
                        slots.Set(n, was);
                        r.Notes.Add(kv.Key + (was == SlotState.Empty ? " out" : " in"));
                    }
                if (step.GunPorts && slots != null && l != null)
                    foreach (int n in l.Ports)
                    {
                        // (0.3.28: a port was in wherever its board was out)
                        var d = l.Slots[n];
                        if (!d.IsGunPort || Saved(d.Id)) continue;
                        int c = l.IndexOf(d.Cover);
                        if (c < 0) continue;
                        var want = slots.Filled(c) ? SlotState.Empty : SlotState.Filled;
                        if (slots[n] == want) continue;
                        slots.Set(n, want);
                        r.Notes.Add(d.Id + (want == SlotState.Filled ? " in" : " out"));
                    }
            }
            foreach (var e in def.ModularPieces ?? Array.Empty<ModularPieceDef>())
                if (e != null && e.Since > rev && !laid.Contains(e.Key)) { laid.Add(e.Key); r.NewlyLaid.Add(e.Key); }
            return r;
        }
    }
}
