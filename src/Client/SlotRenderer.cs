using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// (0.3.20, step B; docs/slots.md) Her slots drawn: the mesh file's slot groups baked per ship into chunks, each chunk's
    /// quads those her slots' states show (a slot's own while it's in, the edges round one that's out), on her paint's
    /// textures. Two families: her deck cells ("slot:deck" in the light outside, "slot:deck:interior" in her rooms' light;
    /// chunks of 8 × 8 cells of a deck) and (0.3.26, step D1; §9) her hull panels ("slot:hull" outside, "slot:hull:bottom" or
    /// "slot:hull:bottomclad" under her copper line as she's sheathed, "slot:hull:interior" her ceiling; chunks of a side's
    /// two frame spaces), each panel's planking with her damage's overlay on it (cracked, scraped, worn, leaking). A change
    /// builds again only the chunks whose quads refer to the slots that changed (a panel's look: every half second at
    /// most), a few a frame; a new ship's chunks are built over her first frames. A design without slots draws nothing here.
    /// </summary>
    public partial class ShipRenderer
    {
        enum SlotFamily { Deck = 0, Hull = 1 }

        class SlotChunks
        {
            public readonly Dictionary<(string Group, int Chunk), MultiTextureMeshRef> Mesh = new();
            public readonly SortedSet<int> Pending = new();
            public int PaintVersion = int.MinValue;
            public bool Started;
            /// <summary>(0.3.26) Hull panels: each one's damage look as last built, the damage version and when it was looked at.</summary>
            public byte[] Looks;
            public int DamageVersion = int.MinValue;
            public long CheckedMs;

            public void Dispose()
            {
                foreach (var r in Mesh.Values) r?.Dispose();
                Mesh.Clear();
            }
        }

        /// <summary>A design's slot quads sorted into chunks, and the textures they're drawn with (her paint's and the damage overlays added as needed).</summary>
        class SlotDesign
        {
            public SlotMeshPlan Plan;
            public readonly List<string> TexNames = new();
            public readonly List<TextureAtlasPosition> Tex = new();
            public readonly Dictionary<string, int> TexIndex = new();
            public TextureAtlasPosition[] TexArray;
        }

        readonly Dictionary<(long, SlotFamily), SlotChunks> slotChunks = new();
        readonly Dictionary<(string, SlotFamily), SlotDesign> slotDesigns = new();
        bool slotsFailed;
        /// <summary>Chunks built at most a frame (a ship coming into view builds hers over a few frames).</summary>
        const int SlotChunksPerFrame = 8;

        /// <summary>(0.3.20) The last chunk builds' times (ms), for <c>.ships slots</c> and the reports.</summary>
        public double SlotBuildMs, SlotBuildMaxMs;
        public int SlotBuilds;

        static SlotStates StatesOf(EntityShip ship, SlotFamily fam) => fam == SlotFamily.Deck ? ship.Slots : ship.HullPanels;
        static ISlotLayout LayoutOf(ShipDefinition def, SlotFamily fam) => fam == SlotFamily.Deck ? def.Slots : def.Hull;
        static string[] GroupsOf(SlotFamily fam) => fam == SlotFamily.Deck ? SlotMeshPlan.Groups : SlotMeshPlan.HullGroups;

        SlotDesign SlotDesignFor(EntityShip ship, Meshes m, SlotFamily fam)
        {
            if (slotDesigns.TryGetValue((ship.Def.Code, fam), out var sd)) return sd;
            sd = new SlotDesign { Plan = new SlotMeshPlan(LayoutOf(ship.Def, fam), m.File, GroupsOf(fam)) };
            sd.TexNames.AddRange(m.File.Textures);
            sd.Tex.AddRange(m.Tex);
            for (int i = 0; i < sd.TexNames.Count; i++) sd.TexIndex[sd.TexNames[i]] = i;
            sd.TexArray = sd.Tex.ToArray();
            foreach (var p in sd.Plan.Problems) capi.Logger.Warning("[ships] {0}'s {1}: {2}", ship.Def.Code, fam == SlotFamily.Deck ? "deck cells" : "hull panels", p);
            slotDesigns[(ship.Def.Code, fam)] = sd;
            return sd;
        }

        int SlotTex(SlotDesign sd, string name)
        {
            if (sd.TexIndex.TryGetValue(name, out int i)) return i;
            capi.BlockTextureAtlas.GetOrInsertTexture(AssetLocation.Create(name), out _, out var pos);
            i = sd.TexNames.Count;
            sd.TexNames.Add(name);
            sd.Tex.Add(pos ?? capi.BlockTextureAtlas.UnknownTexturePosition);
            sd.TexIndex[name] = i;
            sd.TexArray = sd.Tex.ToArray();
            return i;
        }

        /// <summary>Every frame (after her paint): the chunks her slots' changes reach queued, and a few built.</summary>
        void UpdateSlots(EntityShip ship, Meshes m)
        {
            if (slotsFailed || m.SkinLoad != null || m.File == null || m.Tex == null) return;
            try
            {
                foreach (var fam in new[] { SlotFamily.Deck, SlotFamily.Hull })
                {
                    var states = StatesOf(ship, fam);
                    if (states == null || LayoutOf(ship.Def, fam) == null) continue;
                    var sd = SlotDesignFor(ship, m, fam);
                    if (!slotChunks.TryGetValue((ship.EntityId, fam), out var sc)) slotChunks[(ship.EntityId, fam)] = sc = new SlotChunks();
                    if (!sc.Started)
                    {
                        sc.Started = true;
                        states.TakeChanged();
                        for (int c = 0; c < sd.Plan.ChunkCount; c++) sc.Pending.Add(c);
                        sc.PaintVersion = ship.PaintVersion;
                        sc.Looks = SlotLooks(ship, fam, states.Count);
                        sc.DamageVersion = ship.DamageVersion;
                    }
                    else
                    {
                        foreach (int c in sd.Plan.Affected(states.TakeChanged())) sc.Pending.Add(c);
                        if (sc.PaintVersion != ship.PaintVersion)
                        {
                            // (her paint changed: every chunk again; a deck's underside is a ceiling's zone)
                            sc.PaintVersion = ship.PaintVersion;
                            for (int c = 0; c < sd.Plan.ChunkCount; c++) sc.Pending.Add(c);
                        }
                        // ((0.3.26) her damage changed: the panels whose look changed, every half second at most)
                        long now = capi.ElapsedMilliseconds;
                        // ((0.3.28, step D2) and her deck cells' (cracked, scraped))
                        if (sc.DamageVersion != ship.DamageVersion && now - sc.CheckedMs >= DamageCheckMs)
                        {
                            sc.DamageVersion = ship.DamageVersion;
                            sc.CheckedMs = now;
                            var looks = SlotLooks(ship, fam, states.Count);
                            var changed = new List<int>();
                            for (int n = 0; n < looks.Length; n++) if (sc.Looks == null || n >= sc.Looks.Length || sc.Looks[n] != looks[n]) changed.Add(n);
                            sc.Looks = looks;
                            foreach (int c in sd.Plan.Affected(changed)) sc.Pending.Add(c);
                        }
                    }
                    for (int n = 0; n < SlotChunksPerFrame && sc.Pending.Count > 0; n++)
                    {
                        int c = sc.Pending.Min;
                        sc.Pending.Remove(c);
                        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        BuildSlotChunk(ship, m, sd, sc, c, fam);
                        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        SlotBuildMs = ms;
                        SlotBuildMaxMs = Math.Max(SlotBuildMaxMs, ms);
                        SlotBuilds++;
                    }
                }
            }
            catch (Exception e)
            {
                slotsFailed = true;
                capi.Logger.Error("[ships] Drawing her slots (deck cells, hull panels) failed; they're off until the game is restarted: {0}", e);
            }
        }

        /// <summary>(0.3.28) Each slot's damage look now: a hull panel's (<see cref="PanelLooks"/>), a deck cell's (<see cref="CellLooks"/>).</summary>
        static byte[] SlotLooks(EntityShip ship, SlotFamily fam, int count) => fam == SlotFamily.Hull ? PanelLooks(ship, count) : CellLooks(ship, count);

        /// <summary>
        /// (0.3.28, step D2) Each deck slot's damage look now (its part's: <see cref="DamageLayout.CellPart"/>): cracked or
        /// scraped (a cell stove in is out, not drawn); none for a beam's length or without damage.
        /// </summary>
        static byte[] CellLooks(EntityShip ship, int count)
        {
            var looks = new byte[count];
            var l = ship.DamageParts;
            var st = ship.PartDamage;
            if (l == null || st == null || !ship.DamageOn || !l.CellSlotted) return looks;
            for (int n = 0; n < count; n++)
            {
                int cp = l.CellPart(n);
                if (cp < 0 || cp >= st.Count) continue;
                float h = st.Hurt[cp];
                looks[n] = (byte)(h >= l.D.Holed ? 0 : h >= l.D.Cracked ? HullLook.Cracked : h >= l.D.Scraped ? HullLook.Scraped : HullLook.Sound);
            }
            return looks;
        }

        /// <summary>(0.3.26) Each hull panel's damage look now (<see cref="HullLook"/>; its part is its index): none without damage.</summary>
        static byte[] PanelLooks(EntityShip ship, int count)
        {
            var looks = new byte[count];
            var l = ship.DamageParts;
            var st = ship.PartDamage;
            if (l == null || st == null || !ship.DamageOn) return looks;
            for (int n = 0; n < count && n < l.Count && n < st.Count; n++)
                if (l.Parts[n].Slot == n) looks[n] = HullLook.Of(l, n, st.Hurt[n], st.Wear[n]);
            return looks;
        }

        void BuildSlotChunk(EntityShip ship, Meshes m, SlotDesign sd, SlotChunks sc, int chunk, SlotFamily fam)
        {
            var plan = sys.Hansa.PaintPlanFor(ship.Def);
            bool painted = ship.Paint.Zones.Count > 0 && plan != null;
            var states = StatesOf(ship, fam);
            foreach (string g in GroupsOf(fam))
            {
                var key = (g, chunk);
                if (sc.Mesh.TryGetValue(key, out var old)) { old?.Dispose(); sc.Mesh.Remove(key); }
                if (!m.File.Groups.TryGetValue(g, out var quads)) continue;
                var idx = sd.Plan.Visible(g, chunk, states);
                if (idx.Count == 0) continue;
                int[] zones = null;
                if (painted) plan.QuadZones.TryGetValue(g, out zones);
                var (owners, facings) = sd.Plan.Refs[g];
                bool inside = g == "slot:hull:interior" || g == "slot:deck:interior";
                var list = new float[idx.Count][];
                for (int n = 0; n < idx.Count; n++)
                {
                    int q = idx[n];
                    var quad = quads[q];
                    string name = null;
                    if (zones != null && q < zones.Length && zones[q] >= 0 && zones[q] < plan.Zones.Length &&
                        plan.PaintOf(ship.Paint, zones[q]) is string paint)
                        name = ShipPaint.Painted(sd.TexNames[(int)quads[q][0]], paint);
                    // ((0.3.26) a hull panel's own planking with its damage's look on it; (0.3.28) a deck cell's)
                    if (sc.Looks != null && owners[q] >= 0 && facings[q] < 0 && owners[q] < sc.Looks.Length && sc.Looks[owners[q]] != 0)
                    {
                        string overlay = HullLook.Overlay(HullLook.Look(sc.Looks[owners[q]]), inside, (q + owners[q]) & 1);
                        if (overlay != null) name = (name ?? sd.TexNames[(int)quads[q][0]]) + "++0~ships:block/" + overlay;
                    }
                    if (name != null)
                    {
                        quad = (float[])quad.Clone();
                        quad[0] = SlotTex(sd, name);
                    }
                    list[n] = quad;
                }
                var mesh = BuildSkin(list, sd.TexArray);
                if (mesh != null) sc.Mesh[key] = mesh;
            }
        }

        /// <summary>Her slots of one group (deck cells or hull panels, outside or inside light), drawn or cast into the shadow map.</summary>
        void DrawSlots(IRenderAPI rapi, EntityShip ship, string group, bool shadow)
        {
            if (slotsFailed) return;
            var fam = group.StartsWith("slot:hull") ? SlotFamily.Hull : SlotFamily.Deck;
            if (StatesOf(ship, fam) == null || !slotChunks.TryGetValue((ship.EntityId, fam), out var sc)) return;
            foreach (var kv in sc.Mesh)
            {
                if (kv.Key.Group != group) continue;
                if (shadow) CastShadow(rapi, kv.Value, null); else Draw(rapi, kv.Value);
            }
        }

        void DisposeSlots(long shipId)
        {
            foreach (var fam in new[] { SlotFamily.Deck, SlotFamily.Hull })
                if (slotChunks.TryGetValue((shipId, fam), out var sc))
                {
                    sc.Dispose();
                    slotChunks.Remove((shipId, fam));
                }
        }

        void DisposeAllSlots()
        {
            foreach (var sc in slotChunks.Values) sc.Dispose();
            slotChunks.Clear();
            slotDesigns.Clear();
        }

        /// <summary>(0.3.20) What's drawn of a ship's deck cells and (0.3.26) hull panels (<c>.ships slots</c>, <c>.ships hull</c>).</summary>
        public string SlotReport(EntityShip ship)
        {
            if (ship?.Slots == null && ship?.HullPanels == null) return "no slots";
            var parts = new List<string>();
            foreach (var fam in new[] { SlotFamily.Deck, SlotFamily.Hull })
            {
                if (StatesOf(ship, fam) == null) continue;
                string what = fam == SlotFamily.Deck ? "deck cells" : "hull panels";
                if (!slotChunks.TryGetValue((ship.EntityId, fam), out var sc)) { parts.Add($"{what} not drawn yet"); continue; }
                parts.Add($"{what}: {sc.Mesh.Count} chunk meshes, {sc.Pending.Count} chunks waiting");
            }
            return string.Join("; ", parts) + $"; last build {SlotBuildMs:0.00} ms, slowest {SlotBuildMaxMs:0.00} ms over {SlotBuilds} builds";
        }
    }
}
