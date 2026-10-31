using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace HansaTests
{
    /// <summary>
    /// (0.3.20, step B; docs/slots.md) The deck cells: a design's slots and their geometry checked against the contract
    /// (<see cref="DesignSlots"/>, run by <c>-- design &lt;code&gt;</c> for a design with deckSlots), and the mechanics on a
    /// carrack-m given deck slots here (<see cref="SynthSlots"/>: whole free cells of her decks, the hatchways as 3 × 3
    /// openings, the wells; box faces for quads) until the generator makes hers: <c>-- slots</c>.
    /// </summary>
    static partial class Program
    {
        static void Slots(string root)
        {
            // (the generator's carrack-m with her own deck cells, once she has them; else the test's own)
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", "carrack-m.json");
            var real = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            ShipDefinition def;
            ShipSkinFile file;
            if (real.DeckSlots?.Length > 0 && Environment.GetEnvironmentVariable("HANSA_SYNTH_SLOTS") == null)
            {
                Console.WriteLine("deck cells (0.3.20, step B) on the generator's carrack-m");
                real.Init();
                def = real;
                file = JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(Path.Combine(root, "assets", "hansa", def.Shapes.Skin.Replace("hansa:", "").Replace('/', Path.DirectorySeparatorChar))));
            }
            else
            {
                Console.WriteLine("deck cells (0.3.20, step B) on a carrack-m given deck slots by the test");
                (def, file) = SynthSlots(root);
            }
            DesignSlots(def, file);
            SlotMechanics(def, file);
        }

        // ------------------------------------------------------------------ a carrack-m with deck slots (the test's own)

        /// <summary>
        /// carrack-m with deck slots made here: every whole cell of her lower, main, castle and poop decks whose planking
        /// layer is solid in her collision, nothing of hers on it (her collision over it, a fixture's or gun's keep-clear),
        /// filled; the hatchways (x 6..9, z −1.5..1.5 on the lower and main decks) empty 3 × 3 openings, the hatch fixtures
        /// gone; the wells' cells empty. Quads: each cell's top and underside, its sides toward neighbouring cells (owner,
        /// facing), toward the open air (owner), and her fixed planking's edge toward it (facing).
        /// </summary>
        static (ShipDefinition, ShipSkinFile) SynthSlots(string root)
        {
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", "carrack-m.json");
            var jo = JObject.Parse(File.ReadAllText(path));
            var probe = jo.ToObject<ShipDefinition>();
            probe.Init();
            var g = probe.Grid;
            var l = probe.Mounts;
            double gx = probe.BlockGrid.Frac(0), gz = probe.BlockGrid.Frac(2);
            var hatchCells = new List<(double Top, double X0, double Z0)>();
            foreach (double top in new[] { -0.875, 2.125 })
                for (double x = 6; x < 9; x++)
                    for (double z = -1.5; z < 1.5; z++) hatchCells.Add((top, x, z));
            bool IsHatch(double top, double x0, double z0) => hatchCells.Any(h => Math.Abs(h.Top - top) < 1e-6 && Math.Abs(h.X0 - x0) < 1e-6 && Math.Abs(h.Z0 - z0) < 1e-6);
            var wells = (probe.Wells ?? Array.Empty<WellDef>()).Select(w => w.Box).ToList();
            bool InWell(double top, double x0, double z0) => wells.Any(b => Math.Abs(b[4] - top) < 0.01 && x0 >= b[0] - 1e-6 && x0 + 1 <= b[3] + 1e-6 && z0 >= b[2] - 1e-6 && z0 + 1 <= b[5] + 1e-6);
            bool AllSolid(double x0, double y0, double z0, double x1, double y1, double z1)
            {
                for (double x = x0 + 0.125; x < x1; x += 0.25)
                    for (double y = y0 + 0.125; y < y1; y += 0.25)
                        for (double z = z0 + 0.125; z < z1; z += 0.25)
                            if (!g.SolidAt(x, y, z)) return false;
                return true;
            }
            var slots = new JArray();
            foreach (var deck in probe.Decks)
            {
                if (deck.Code is "hold" or "top") continue;
                double top = deck.Y;
                for (double x0 = Math.Ceiling(deck.X0 - gx) + gx; x0 + 1 <= deck.X1 + 1e-6; x0 += 1)
                {
                    if (x0 < g.OX || x0 + 1 > g.MaxX) continue;
                    for (double z0 = Math.Ceiling(-7 - gz) + gz; z0 + 1 <= 7; z0 += 1)
                    {
                        bool hatch = IsHatch(top, x0, z0), well = InWell(top, x0, z0);
                        bool open = hatch || well;
                        if (!open)
                        {
                            if (!AllSolid(x0, top - 0.25, z0, x0 + 1, top, z0 + 1)) continue;
                            if (g.Collides(x0, top, z0, x0 + 1, top + 0.25, z0 + 1)) continue;
                            if (l.ClearHitCore(x0 + 0.01, top, z0 + 0.01, x0 + 0.99, top + 1, z0 + 0.99) != null) continue;
                            if (probe.Masts.Any(m => Math.Abs(m.X - (x0 + 0.5)) < 0.5 + m.Radius && Math.Abs(m.Z - (z0 + 0.5)) < 0.5 + m.Radius)) continue;
                        }
                        var o = new JObject
                        {
                            ["id"] = $"{deck.Code}:{(int)Math.Round(x0 - gx)}:{(int)Math.Round(z0 - gz)}",
                            ["deck"] = deck.Code,
                            ["box"] = new JArray(x0, top - 0.25, z0, x0 + 1, top, z0 + 1)
                        };
                        if (open) o["start"] = "empty";
                        slots.Add(o);
                    }
                }
            }
            jo["deckSlots"] = slots;
            // (the hatches are pieces now: the fixtures gone)
            jo["fixtures"] = new JArray(((JArray)jo["fixtures"]).Where(f => (string)f["type"] != "hatch"));
            var def = jo.ToObject<ShipDefinition>();
            def.Init();
            // (her collision as the design would have it: the hatchways open)
            foreach (var s in def.DeckSlots) if (s.StartsEmpty) def.Grid.Fill(s.Box, false);
            def.DamageParts = new DamageLayout(def);
            return (def, SynthQuads(def));
        }

        /// <summary>The box faces of the cells as the contract's quads (docs/slots.md §3), texture 0, u v by position.</summary>
        static ShipSkinFile SynthQuads(ShipDefinition def)
        {
            var l = def.Slots;
            var ext = new List<float[]>();
            var inn = new List<float[]>();
            var extRefs = new List<int>();
            var innRefs = new List<int>();
            void Quad(bool outside, int owner, int facing, double[] p, double nx, double ny, double nz)
            {
                var q = new float[33];
                for (int k = 0; k < 4; k++)
                {
                    q[1 + 8 * k] = (float)p[3 * k]; q[2 + 8 * k] = (float)p[3 * k + 1]; q[3 + 8 * k] = (float)p[3 * k + 2];
                    q[4 + 8 * k] = (float)((p[3 * k] * 16) % 16); q[5 + 8 * k] = (float)((p[3 * k + 2] * 16) % 16);
                    q[6 + 8 * k] = (float)nx; q[7 + 8 * k] = (float)ny; q[8 + 8 * k] = (float)nz;
                }
                (outside ? ext : inn).Add(q);
                (outside ? extRefs : innRefs).AddRange(new[] { owner, facing });
            }
            var g = def.Grid;
            for (int n = 0; n < l.Count; n++)
            {
                var s = l.Slots[n];
                var b = s.Box;
                double x0 = b[0], y0 = b[1], z0 = b[2], x1 = b[3], y1 = b[4], z1 = b[5];
                bool covered = def.RoomAt(s.CX, y1 + 0.5, s.CZ)?.Enclosed == true;
                Quad(!covered, n, -1, new[] { x0, y1, z0, x0, y1, z1, x1, y1, z1, x1, y1, z0 }, 0, 1, 0);
                Quad(false, n, -1, new[] { x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1 }, 0, -1, 0);
                for (int dir = 0; dir < 4; dir++)
                {
                    int dx = MountLayout.DX[dir], dz = MountLayout.DZ[dir];
                    // the side face on the cell's edge that way, facing that way
                    double fx = dx > 0 ? x1 : x0, fz = dz > 0 ? z1 : z0;
                    double[] face = dx != 0
                        ? new[] { fx, y0, z0, fx, y1, z0, fx, y1, z1, fx, y0, z1 }
                        : new[] { x0, y0, fz, x1, y0, fz, x1, y1, fz, x0, y1, fz };
                    int nb = l.Neighbours[n][dir];
                    if (nb >= 0) { Quad(false, n, nb, face, dx, 0, dz); continue; }
                    // beyond it: her fixed planking (its edge faces this cell: shown while this one's out) or the open air
                    double cx = s.CX + dx, cz = s.CZ + dz;
                    if (g.SolidAt(cx, y0 + 0.125, cz) && !l.Slots.Any(o => o.StartsEmpty && Math.Abs(o.CX - cx) < 0.01 && Math.Abs(o.CZ - cz) < 0.01 && Math.Abs(o.Top - y1) < 0.01))
                        Quad(false, -1, n, face, -dx, 0, -dz);
                    else Quad(false, n, -1, face, dx, 0, dz);
                }
            }
            return new ShipSkinFile
            {
                Textures = new[] { "game:block/wood/planks/oak1" },
                Groups = new Dictionary<string, float[][]> { ["slot:deck"] = ext.ToArray(), ["slot:deck:interior"] = inn.ToArray() },
                SlotQuads = new Dictionary<string, int[]> { ["slot:deck"] = extRefs.ToArray(), ["slot:deck:interior"] = innRefs.ToArray() }
            };
        }

        // ------------------------------------------------------------------ the contract's checks (docs/slots.md §6)

        /// <summary>A design's deck slots against the contract: boxes on the grid at their deck's top, solids as the start says, no voxel in two, quads for each, cells meeting, the hatch entries fitting.</summary>
        static void DesignSlots(ShipDefinition def, ShipSkinFile file)
        {
            var l = def.Slots;
            Check(l != null && l.Count > 0, $"{def.Code}: {l?.Count ?? 0} deck slots ({string.Join(", ", (l?.Slots ?? Array.Empty<DeckSlotDef>()).GroupBy(s => s.Deck).Select(x => $"{x.Key} {x.Count()}"))})");
            if (l == null) return;
            foreach (var p in l.Problems.Take(10)) Console.WriteLine("         " + p);
            Check(l.Problems.Count == 0, $"{def.Code}: the slots' ids, cells and voxels are each one slot's ({l.Problems.Count} problems)");
            var g = def.Grid;
            double gx = def.BlockGrid?.Frac(0) ?? 0, gz = def.BlockGrid?.Frac(2) ?? 0;
            var bad = new List<string>();
            foreach (var s in l.Slots)
            {
                var b = s.Box;
                var deck = def.Decks.FirstOrDefault(d => d.Code == s.Deck);
                if (deck == null) { bad.Add($"{s.Id}: no deck {s.Deck}"); continue; }
                if (s.IsPort)
                {
                    // ((0.3.28, D2.4, docs/slots.md §11) a port's cover: the hole through her side, solid as its start says, its gun's place names it
                    if (def.Fixture(s.Gun)?.Type != "gun") bad.Add($"{s.Id}: its gun {s.Gun} isn't a gun");
                    if (def.GunPlaces?.Any(gp => gp.Gun == s.Gun && gp.Port == s.Id) != true) bad.Add($"{s.Id}: no gun place names it");
                    foreach (var sb in s.SolidBoxes)
                        foreach (long v in DeckSlotLayout.Voxels(g, sb))
                        {
                            int k = (int)(v % g.NZ), j = (int)(v / g.NZ % g.NY), i = (int)(v / g.NZ / g.NY);
                            if (g.Solid(i, j, k) != !s.StartsEmpty) { bad.Add($"{s.Id}: a voxel not as its start says"); break; }
                        }
                    continue;
                }
                if(s.IsStructure)
                {
                    if(s.Cell?.Length!=3) bad.Add(s.Id+": no model cell");
                    foreach(var sb in s.SolidBoxes) foreach(long v in DeckSlotLayout.Voxels(g,sb))
                    {
                        int k=(int)(v%g.NZ),j=(int)(v/g.NZ%g.NY),i=(int)(v/g.NZ/g.NY);
                        if(!g.Solid(i,j,k)) bad.Add(s.Id+": a structural voxel is not solid");
                    }
                    continue;
                }
                if (s.IsBeam)
                {
                    // ((0.3.27, docs/slots.md §10) a beam's length: under its deck's planking, its cells beside it, solid)
                    // ((22 October 2026, caravel notes 51, 52) or a rib cell's beam: within the one cell over it, its voxel columns)
                    if (Math.Abs(b[4] - (deck.Y - 0.25)) > 1e-3) bad.Add($"{s.Id}: its top {b[4]} not the deck's underside {deck.Y - 0.25}");
                    bool overOne = s.Beside?.Length == 1 && l.Get(s.Beside[0])?.Box is double[] cb &&
                                   b[0] >= cb[0] - 1e-6 && b[3] <= cb[3] + 1e-6 && b[2] >= cb[2] - 1e-6 && b[5] <= cb[5] + 1e-6;
                    if (Math.Abs(b[5] - b[2] - 1) > 1e-6 || (overOne ? b[3] - b[0] < 0.25 - 1e-6 : Math.Abs(b[3] - b[0] - 0.25) > 1e-6)) bad.Add($"{s.Id}: not a cell's width of a beam's voxel column{(overOne ? "s" : "")}");
                    if (s.Beside?.Length is not (1 or 2) || s.Beside.Any(id => l.Get(id) == null) || s.Beside.Length == 1 && !overOne) bad.Add($"{s.Id}: its cells beside it aren't deck cells (two either side, or the one over it)");
                    foreach (var sb in s.SolidBoxes)
                        foreach (long v in DeckSlotLayout.Voxels(g, sb))
                        {
                            int k = (int)(v % g.NZ), j = (int)(v / g.NZ % g.NY), i = (int)(v / g.NZ / g.NY);
                            if (!g.Solid(i, j, k)) { bad.Add($"{s.Id}: a voxel not solid"); break; }
                        }
                    continue;
                }
                if (Math.Abs(b[4] - deck.Y) > 1e-3) bad.Add($"{s.Id}: top {b[4]} not the deck's {deck.Y}");
                if (Math.Abs(b[3] - b[0] - 1) > 1e-6 || Math.Abs(b[5] - b[2] - 1) > 1e-6) bad.Add($"{s.Id}: not a whole cell");
                double fx = b[0] - gx - Math.Round(b[0] - gx), fz = b[2] - gz - Math.Round(b[2] - gz);
                if (Math.Abs(fx) > 1e-6 || Math.Abs(fz) > 1e-6) bad.Add($"{s.Id}: off her grid");
                foreach (var sb in s.SolidBoxes)
                {
                    if (sb[0] < b[0] - 1e-6 || sb[3] > b[3] + 1e-6 || sb[2] < b[2] - 1e-6 || sb[5] > b[5] + 1e-6) bad.Add($"{s.Id}: solids outside its column");
                    foreach (long v in DeckSlotLayout.Voxels(g, sb))
                    {
                        int k = (int)(v % g.NZ), j = (int)(v / g.NZ % g.NY), i = (int)(v / g.NZ / g.NY);
                        if (g.Solid(i, j, k) != !s.StartsEmpty) { bad.Add($"{s.Id}: a voxel {(s.StartsEmpty ? "solid though it starts empty" : "not solid though it starts filled")}"); break; }
                    }
                }
            }
            foreach (var x in bad.Take(10)) Console.WriteLine("         " + x);
            Check(bad.Count == 0, $"{def.Code}: every slot a whole cell on her grid at its deck's top, its solids its own column, solid as its start says ({bad.Count} problems)");
            var plan = new SlotMeshPlan(l, file);
            foreach (var p in plan.Problems.Take(10)) Console.WriteLine("         " + p);
            Check(plan.Problems.Count == 0 && SlotMeshPlan.Groups.All(gr => file.Groups?.ContainsKey(gr) == true), $"{def.Code}: the mesh file has both slot groups, slotQuads for every quad, none naming a missing slot");
            // each slot: a top and an underside of its own; its quads within its box (or its neighbours' edge)
            var tops = new int[l.Count];
            var unders = new int[l.Count];
            var ownedFaces=new int[l.Count];
            var outside = new List<string>();
            var facingPairs = new HashSet<(int, int)>();
            foreach (string gr in SlotMeshPlan.Groups)
            {
                if (!file.Groups.TryGetValue(gr, out var quads)) continue;
                var (own, fac) = plan.Refs[gr];
                for (int q = 0; q < quads.Length; q++)
                {
                    var qd = quads[q];
                    double ny = qd[7] + qd[15] + qd[23] + qd[31];
                    int o = own[q], f = fac[q];
                    if(o>=0) ownedFaces[o]++;
                    // ((22 October 2026) a beam's length: its top the deck's underside, over its box's top (her voxel row) by
                    // the deck's thickness short of 0.25: the caravel's 0.2-thick deck, 0.05)
                    if (o >= 0 && f < 0 && ny > 3.5 && Math.Abs(qd[2] - l.Slots[o].Top) < (l.Slots[o].IsBeam ? 0.1 : 0.05)) tops[o]++;
                    if (o >= 0 && f < 0 && ny < -3.5) unders[o]++;
                    if (o >= 0 && f >= 0) facingPairs.Add((o, f));
                    int who = o >= 0 ? o : f;
                    if (who < 0) continue;
                    // ((0.3.27) a beam's length's timber and its carlings: along x from the beam aft to the beam forward)
                    if (!l.Slots[who].IsCell || f >= 0 && !l.Slots[f].IsCell) continue;
                    var b = l.Slots[who].Box;
                    for (int k = 0; k < 4; k++)
                    {
                        double x = qd[1 + 8 * k], y = qd[2 + 8 * k], z = qd[3 + 8 * k];
                        if (x < b[0] - 0.3 || x > b[3] + 0.3 || z < b[2] - 0.3 || z > b[5] + 0.3 || y < b[1] - 0.6 || y > b[4] + 0.05) { outside.Add($"{gr} quad {q} of {l.Slots[who].Id}"); break; }
                    }
                }
            }
            var noTop = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].IsStructure ? ownedFaces[n]==0 : !l.Slots[n].IsPort && (tops[n] == 0 || unders[n] == 0)).ToList();
            foreach (int n in noTop.Take(8)) Console.WriteLine($"         {l.Slots[n].Id}: {tops[n]} tops, {unders[n]} undersides");
            Check(noTop.Count == 0, $"{def.Code}: every slot has a top and an underside of its own ({noTop.Count} without)");
            foreach (var x in outside.Take(8)) Console.WriteLine("         " + x);
            Check(outside.Count == 0, $"{def.Code}: every slot's quads lie at its cell ({outside.Count} elsewhere)");
            // neighbours: they share an edge exactly, and each side has its facing quad (both ways)
            var noFace = new List<string>();
            for (int n = 0; n < l.Count; n++)
                for (int dir = 0; dir < 4; dir++)
                {
                    int nb = l.Neighbours[n][dir];
                    if (nb < 0) continue;
                    var a = l.Slots[n].Box;
                    var b = l.Slots[nb].Box;
                    bool meet = dir switch { 0 => Math.Abs(a[3] - b[0]) < 1e-6, 2 => Math.Abs(a[0] - b[3]) < 1e-6, 1 => Math.Abs(a[5] - b[2]) < 1e-6, _ => Math.Abs(a[2] - b[5]) < 1e-6 };
                    if (!meet || Math.Abs(a[1] - b[1]) > 1e-6 || Math.Abs(a[4] - b[4]) > 1e-6) noFace.Add($"{l.Slots[n].Id} and {l.Slots[nb].Id} don't meet");
                    else if (!facingPairs.Contains((n, nb))) noFace.Add($"{l.Slots[n].Id}: no side toward {l.Slots[nb].Id}");
                }
            foreach (var x in noFace.Take(8)) Console.WriteLine("         " + x);
            Check(noFace.Count == 0, $"{def.Code}: neighbouring cells meet edge to edge, each with its side toward the other ({noFace.Count} problems)");
            // the design's states drawn: no two quads in one plane facing one way overlapping
            var st = new SlotStates(l);
            Check(Coplanars(def, file, plan, st, null) == 0, $"{def.Code}: as designed, no two of the cells' quads drawn in one plane (they'd flicker)");
            // the hatch entries fit their openings
            foreach (var e in (def.ModularPieces ?? Array.Empty<ModularPieceDef>()).Where(e => e.Kind == "hatch"))
            {
                int nx = e.Size?.Length > 0 ? e.Size[0] : 1, nz = e.Size?.Length > 1 ? e.Size[1] : 1;
                var cells = l.Rect(e.At[1], e.At[0] - nx / 2.0, e.At[2] - nz / 2.0, e.At[0] + nx / 2.0, e.At[2] + nz / 2.0);
                var hp = cells == null ? null : HatchPlanner.Plan(l, cells, VanillaPieceBlock.IsGlazedTrapdoor(e.Block), e.Hinge);
                Check(cells != null && cells.All(c => l.Slots[c].StartsEmpty) && hp?.Error == null,
                      $"{def.Code}: the hatch at {string.Join(", ", e.At)} ({nx} × {nz}) lies over an opening of empty cells{(hp?.Error != null ? ": " + hp.Error : cells == null ? ": not all deck cells" : "")}");
                if (hp?.Error == null && hp?.Box != null)
                {
                    // (0.3.20) its leaves swing clear of her collision and her ladders, hinged as laid; the other way, say
                    var clash = HatchPlanner.SwingClash(hp.Box, hp.Dir, def.Grid.SolidAt, def.Ladders);
                    var other = HatchPlanner.SwingClash(hp.Box, 1 - hp.Dir, def.Grid.SolidAt, def.Ladders);
                    Check(clash == null, $"{def.Code}: its leaves, hinged along {(hp.Dir == 0 ? "x" : "z")}{(e.Hinge == null ? " (the default)" : "")}, swing open clear of her and her ladders" +
                        (clash != null ? $": they meet something at {string.Join(", ", clash.Select(v => v.ToString("0.##")))}" : "") +
                        $"; hinged along {(hp.Dir == 0 ? "z" : "x")} they'd {(other != null ? $"meet something at {string.Join(", ", other.Select(v => v.ToString("0.##")))}" : "swing clear too")}");
                }
            }
        }

        /// <summary>Visible quads (of all the chunks) lying two in one plane, facing one way, overlapping: how many pairs (some listed).</summary>
        static int Coplanars(ShipDefinition def, ShipSkinFile file, SlotMeshPlan plan, SlotStates st, string label)
        {
            var faces = new List<(double[] B, double[] N, double D, double[][] P)>();
            foreach (string gr in SlotMeshPlan.Groups)
            {
                if (!file.Groups.TryGetValue(gr, out var quads)) continue;
                for (int c = 0; c < plan.ChunkCount; c++)
                    foreach (int q in plan.Visible(gr, c, st))
                    {
                        var qd = quads[q];
                        double nx = qd[6] + qd[14] + qd[22] + qd[30], ny = qd[7] + qd[15] + qd[23] + qd[31], nz = qd[8] + qd[16] + qd[24] + qd[32];
                        var normal=new[]{nx,ny,nz};double length=Math.Sqrt(Dot(normal,normal));
                        if(length<1e-8) continue;
                        normal=normal.Select(v=>v/length).ToArray();
                        var points=Enumerable.Range(0,4).Select(k=>new double[]{qd[1+8*k],qd[2+8*k],qd[3+8*k]}).ToArray();
                        var b = new[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
                        for (int k = 0; k < 4; k++)
                            for (int a = 0; a < 3; a++) { b[a] = Math.Min(b[a], qd[1 + 8 * k + a]); b[a + 3] = Math.Max(b[a + 3], qd[1 + 8 * k + a]); }
                        faces.Add((b, normal, Dot(normal,points[0]),points));
                    }
            }
            int pairs = 0;
            var byPlane = faces.GroupBy(f => (Math.Round(f.N[0]*20),Math.Round(f.N[1]*20),Math.Round(f.N[2]*20),Math.Round(f.D*100)));
            foreach (var grp in byPlane)
            {
                var list = grp.ToList();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var a = list[i].B;
                        var b = list[j].B;
                        if(Enumerable.Range(0,3).Any(k=>a[k]>b[k+3]+1e-5 || b[k]>a[k+3]+1e-5)) continue;
                        if(Dot(list[i].N,list[j].N)<.99996 || Math.Abs(list[i].D-list[j].D)>.00025) continue;
                        if (OverlapArea(list[i].N,list[i].P,list[j].P)>2e-6)
                        {
                            if (pairs < 4 && label != null) Console.WriteLine($"         {label}: two faces in one plane at {string.Join(", ", a.Select(x => x.ToString("0.###")))}");
                            pairs++;
                        }
                    }
            }
            return pairs;
        }

        // ------------------------------------------------------------------ the mechanics

        static void SlotMechanics(ShipDefinition def, ShipSkinFile file)
        {
            var l = def.Slots;
            var plan = new SlotMeshPlan(l, file);
            var st = new SlotStates(l);
            // a waist cell of the main deck away from the hatchway and the guns: (3, 2) area
            // (a man's width clear of her beams under it somewhere in the cell: a beam takes a quarter block under one edge)
            double FallX(int n) => new[] { 0.5, 0.375, 0.625 }.Select(f => l.Slots[n].Box[0] + f).FirstOrDefault(x =>
                !def.Grid.Collides(x - 0.32, l.Slots[n].Top - 2.0, l.Slots[n].CZ - 0.32, x + 0.32, l.Slots[n].Top - 0.25, l.Slots[n].CZ + 0.32), double.NaN);
            int waist = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Deck == "main" && !l.Slots[n].StartsEmpty &&
                def.RoomAt(l.Slots[n].CX, l.Slots[n].Top + 0.5, l.Slots[n].CZ) == null && !double.IsNaN(FallX(n)))
                .OrderBy(n => Math.Abs(l.Slots[n].CX - 6.5) + Math.Abs(l.Slots[n].CZ - 2.0)).FirstOrDefault(-1);
            Check(waist >= 0, $"a waist cell of the main deck to take up: {(waist >= 0 ? l.Slots[waist].Id : "none")}");
            if (waist < 0) return;
            var d = l.Slots[waist];

            // 1. collision: taken up and laid again, her grid as designed
            var grid = def.Grid.Clone();
            var before = grid.CountSolid();
            st.Set(waist, SlotState.Empty);
            grid.CopyFrom(def.Grid);
            st.Apply(grid);
            int gone = before - grid.CountSolid();
            Check(gone == 16 && !grid.SolidAt(d.CX, d.Top - 0.1, d.CZ), $"taken up: its 16 voxels gone from her grid ({gone})");
            // people fall through it
            var mover = new DeckMover(FallX(waist), d.Top, d.CZ) { OnGround = true };
            for (int i = 0; i < 60; i++) mover.Step(grid, 1 / 60.0, 0, 0, 3.5, false, false);
            Check(mover.Y < d.Top - 1.5, $"someone standing on it falls through (feet {d.Top:0.00} → {mover.Y:0.00})");
            // the delta saved and loaded
            var tree = new TreeAttribute();
            st.ToTree(tree);
            var st2 = new SlotStates(l);
            st2.FromTree(tree);
            Check(tree.Count == 1 && tree.GetInt(d.Id) == (int)SlotState.Empty && !st2.Filled(waist) && st2.Differences == 1,
                  $"saved as one difference ({tree.Count}: {d.Id} = {tree.GetInt(d.Id)}) and loaded back");
            st.Set(waist, SlotState.Filled);
            grid.CopyFrom(def.Grid);
            st.Apply(grid);
            var tree2 = new TreeAttribute();
            st.ToTree(tree2);
            Check(grid.CountSolid() == before && tree2.Count == 0, "laid again: her grid as designed, nothing saved");

            // 2. drawing: what a cell taken up shows and hides, which chunks are built again
            var shownFilled = Visible(plan, file, st);
            st.Set(waist, SlotState.Empty);
            var shownEmpty = Visible(plan, file, st);
            var lost = shownFilled.Except(shownEmpty).ToList();
            var got = shownEmpty.Except(shownFilled).ToList();
            bool ownGone = lost.All(x => plan.Refs[x.G].Owner[x.Q] == waist);
            bool edgesUp = got.All(x => plan.Refs[x.G].Facing[x.Q] == waist) && got.Count >= 4;
            Check(ownGone && edgesUp && lost.Count >= 2, $"taken up: its own quads hidden ({lost.Count}), the edges round the hole shown ({got.Count}: its neighbours' sides, the fixed planking's)");
            var affected = plan.Affected(new[] { waist });
            Check(affected.Count >= 1 && affected.Count <= 4, $"its change builds again {affected.Count} chunk(s) of {plan.ChunkCount}");
            Check(Coplanars(def, file, plan, st, "one cell out") == 0, "with the cell out, still no two quads drawn in one plane");
            // a chequerboard of cells out round it: still none
            var st3 = new SlotStates(l);
            foreach (int n in Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Deck == "main" && ((int)Math.Floor(l.Slots[n].CX) + (int)Math.Floor(l.Slots[n].CZ)) % 2 == 0)) st3.Set(n, SlotState.Empty);
            Check(Coplanars(def, file, plan, st3, "chequerboard") == 0, "the main deck taken up in a chequerboard: no two quads drawn in one plane");

            // 3. rooms: a hole in the waist opens the lower deck (no hatch over it); one under the great cabin doesn't
            var lower = def.Rooms.First(r => r.Code == "lowerdeck");
            var leaks = DeckCells.Leaks(def, n => n == waist, lower);
            int cabinCell = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Deck == "main" && def.RoomAt(l.Slots[n].CX, l.Slots[n].Top + 0.5, l.Slots[n].CZ)?.Code == "greatcabin").FirstOrDefault(-1);
            var cabinLeaks = cabinCell >= 0 ? DeckCells.Leaks(def, n => n == cabinCell, lower) : null;
            var cabin = def.Rooms.First(r => r.Code == "greatcabin");
            var cabinFloor = cabinCell >= 0 ? DeckCells.Leaks(def, n => n == cabinCell, cabin) : null;
            Check(leaks.Count == 1 && cabinLeaks?.Count == 0 && cabinFloor?.Count == 0,
                  $"an open waist cell opens the lower deck ({leaks.Count}); one in the great cabin's floor opens neither it nor the lower deck ({cabinLeaks?.Count}, {cabinFloor?.Count}: enclosed rooms both sides)");
            var openHatchway = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].StartsEmpty && l.Slots[n].Deck == "main" && Math.Abs(l.Slots[n].CX - 7.5) < 0.6).ToList();
            var hatchLeaks = DeckCells.Leaks(def, n => openHatchway.Contains(n), lower);
            Check(hatchLeaks.Count == openHatchway.Count && openHatchway.Count > 0, $"the waist's hatchway with no hatch shut over it opens the lower deck ({hatchLeaks.Count} cells)");
            grid.CopyFrom(def.Grid);
            new SlotStates(l).Apply(grid);
            foreach (int n in openHatchway.Concat(Enumerable.Range(0, l.Count).Where(n => l.Slots[n].StartsEmpty && l.Slots[n].Deck == "main"))) foreach (var b in l.Slots[n].SolidBoxes) grid.Fill(b);
            var dm = def.Mounts;
            double sx = 2.5, sy = lower.Min[1] + 0.1, sz = 3.0;
            bool sealedShut = ShipCompartments.Sealed(dm, grid, Array.Empty<ShipPiece>(), lower, Array.Empty<double[]>(), sx, sy, sz);
            bool sealedHole = ShipCompartments.Sealed(dm, grid, Array.Empty<ShipPiece>(), lower, leaks, sx, sy, sz);
            Check(!sealedHole, $"the lower deck with the waist cell out isn't sealed ({sealedHole}); with her hatchways shut and no hole: {sealedShut}");

            // 4. the sea: an open waist cell is a downflooding point like an open hatch
            var dl = def.DamageParts;
            int cellPoints = dl.Downflood.Count(f => f.Cell);
            int mainWaist = Enumerable.Range(0, l.Count).Count(n => Math.Abs(l.Slots[n].Top - def.DeckY) < 0.05 && def.RoomAt(l.Slots[n].CX, l.Slots[n].Top + 0.5, l.Slots[n].CZ)?.Enclosed != true);
            Check(cellPoints == mainWaist && cellPoints > 0, $"every main-deck cell out in the open is a downflooding point ({cellPoints} of {mainWaist}); none under the castles");
            var pose = new FloodPose { X = 0, Y = 0, Z = 0, Surface = (x, z) => def.DeckY + 0.3 };
            double shut = ShipFlood.Downflooding(dl, pose, c => false);
            double open = ShipFlood.Downflooding(dl, pose, c => c == "cell:" + waist);
            double expect = ShipFlood.Pour(dl.D, 1, 1, 0.3);
            Check(open - shut > expect * 0.99, $"the sea 0.3 over her deck pours through the open cell: {open - shut:0.000} more a second (a weir a block long: {expect:0.000}; with it all shut {shut:0.000})");

            // 5. hatches: the 3 × 3 hatchway takes one; an L-shaped opening, a too-long one don't; shut it's walked on
            st = new SlotStates(l);
            int hc = openHatchway.FirstOrDefault(-1);
            var opening = HatchPlanner.Opening(l, hc, n => !st.Filled(n));
            var hp = HatchPlanner.Plan(l, opening, true);
            // ((0.3.24) the size of the design's waist hatch: 2 × 3 between the beams at x 6 and 8 since the generator's 0.3.24; 3 × 3 before)
            var waistEntry = def.ModularPieces?.FirstOrDefault(e => e.Kind == "hatch" && e.At?.Length >= 3 && Math.Abs(e.At[1] - def.DeckY) < 0.05 && e.Size?.Length >= 2);
            int hsx = waistEntry != null ? waistEntry.Size[0] : 3, hsz = waistEntry != null ? waistEntry.Size[1] : 3;
            bool sizeOk = Math.Abs(hp.Box[3] - hp.Box[0] - hsx) < 1e-6 && Math.Abs(hp.Box[5] - hp.Box[2] - hsz) < 1e-6 || Math.Abs(hp.Box[3] - hp.Box[0] - hsz) < 1e-6 && Math.Abs(hp.Box[5] - hp.Box[2] - hsx) < 1e-6;
            Check(hp.Error == null && hp.Mounts.Count == hsx * hsz && sizeOk && Math.Abs(hp.Box[4] - def.DeckY) < 1e-6,
                  $"the waist's hatchway ({hsx} × {hsz}) takes a glazed hatch: {hp.Mounts.Count} cells, {hp.Error ?? "flush with the deck"}");
            var leaves = HatchPlanner.Leaves(hp.Box, hp.Dir);
            Check(leaves.Count == 2 && leaves.All(x => Math.Abs(x.Box[4] - def.DeckY) < 1e-6), $"two leaves hinged on its long sides ({leaves.Count})");
            int coplanarLeaf = leaves.Sum(x => ModularGeometry.Coplanar(ModularGeometry.HatchLeaf(x.Box, x.Axis, true, true).Select(p => p.Box).ToList()).Count +
                                               ModularGeometry.Coplanar(ModularGeometry.HatchLeaf(x.Box, x.Axis, true, false).Select(p => p.Box).ToList()).Count);
            foreach (var x in leaves)
                foreach (bool gl in new[] { true, false })
                {
                    var bx = ModularGeometry.HatchLeaf(x.Box, x.Axis, true, gl).Select(p => p.Box).ToList();
                    foreach (var (i, j, area) in ModularGeometry.Coplanar(bx))
                        Console.WriteLine($"         {(gl ? "glazed" : "cargo")} leaf: parts {i} and {j} in one plane ({area:0.00000}): {string.Join(" ", bx[i].Select(v => v.ToString("0.###")))} / {string.Join(" ", bx[j].Select(v => v.ToString("0.###")))}");
                }
            Check(coplanarLeaf == 0, $"a leaf's parts (cargo and glazed) have no faces in one plane ({coplanarLeaf})");
            // an opening that isn't a rectangle (the hatchway with a corner laid): none
            var corner = opening.OrderBy(n => l.Slots[n].CX + l.Slots[n].CZ).First();
            st.Set(corner, SlotState.Filled);
            var lshape = HatchPlanner.Plan(l, HatchPlanner.Opening(l, hc == corner ? opening.Last() : hc, n => !st.Filled(n)));
            Check(lshape.Error == "hansa:hatch-not-rect", $"with a corner laid the opening isn't a rectangle: {lshape.Error ?? "planned"}");
            st.Set(corner, SlotState.Empty);
            var row = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Deck == "main" && Math.Abs(l.Slots[n].CZ - 3.0) < 0.01 && l.Slots[n].CX > -2 && l.Slots[n].CX < 5).Take(5).ToList();
            if (row.Count == 5)
            {
                Check(HatchPlanner.Plan(l, row).Error == "hansa:hatch-too-big", "five cells in a row are too long for a hatch");
            }
            // shut, its solids are the deck's: walked over
            grid.CopyFrom(def.Grid);
            foreach (var b in hp.Solids) grid.Fill(b);
            // (0.3.24: over the opening's middle, wherever its beams are)
            double hmx = (hp.Box[0] + hp.Box[3]) / 2, hmz = (hp.Box[2] + hp.Box[5]) / 2;
            var walker = new DeckMover(hmx, def.DeckY, hmz) { OnGround = true };
            for (int i = 0; i < 60; i++) walker.Step(grid, 1 / 60.0, 0, 0, 3.5, false, false);
            Check(Math.Abs(walker.Y - def.DeckY) < 0.02, $"shut, it's walked on ({walker.Y:0.00}); open, the hatchway is a hole");
            var faller = new DeckMover(hmx, def.DeckY, hmz) { OnGround = true };
            grid.CopyFrom(def.Grid);
            for (int i = 0; i < 60; i++) faller.Step(grid, 1 / 60.0, 0, 0, 3.5, false, false);
            Check(faller.Y < def.DeckY - 1.5, $"open, someone over it falls ({faller.Y:0.00})");

            // 6. how long a cell's change takes: her grid again, and the chunks it reaches built (the quads picked and copied)
            var sw = Stopwatch.StartNew();
            int edits = 200;
            var rng = new Random(7);
            double worstChunk = 0;
            int built = 0;
            for (int e = 0; e < edits; e++)
            {
                int n = rng.Next(l.Count);
                st.Set(n, st.Filled(n) ? SlotState.Empty : SlotState.Filled);
                grid.CopyFrom(def.Grid);
                st.Apply(grid);
                foreach (int c in plan.Affected(st.TakeChanged()))
                {
                    var t0 = Stopwatch.GetTimestamp();
                    foreach (string gr in SlotMeshPlan.Groups)
                    {
                        if (!file.Groups.TryGetValue(gr, out var quads)) continue;
                        var vis = plan.Visible(gr, c, st);
                        var arr = new float[vis.Count][];
                        for (int k = 0; k < vis.Count; k++) arr[k] = (float[])quads[vis[k]].Clone();
                    }
                    worstChunk = Math.Max(worstChunk, (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                    built++;
                }
            }
            sw.Stop();
            double perEdit = sw.Elapsed.TotalMilliseconds / edits;
            int quadsAll = SlotMeshPlan.Groups.Sum(gr => file.Groups.TryGetValue(gr, out var q) ? q.Length : 0);
            var chunkSizes = Enumerable.Range(0, plan.ChunkCount).Select(c => SlotMeshPlan.Groups.Sum(gr => plan.ChunkQuads.TryGetValue((gr, c), out var q) ? q.Count : 0)).ToList();
            Console.WriteLine($"         {l.Count} cells, {quadsAll} quads in {plan.ChunkCount} chunks (most {chunkSizes.Max()} quads, mean {chunkSizes.Average():0}); " +
                              $"an edit: {perEdit:0.000} ms (her grid {grid.NX}×{grid.NY}×{grid.NZ} copied and the states applied, {built / (double)edits:0.0} chunks' quads picked); slowest chunk {worstChunk:0.000} ms");
            Check(perEdit < 5, $"a cell's change costs {perEdit:0.000} ms on the CPU before the upload (under 5)");

            SlotPlaces(def, waist);
            SlotPaint(def, file, waist);
            SlotWreck(def, waist);
        }

        /// <summary>(0.3.20, step B) Her places follow her cells: a design opening laid takes a chest, a cell taken up loses its places; her own places keep their ids.</summary>
        static void SlotPlaces(ShipDefinition def, int waist)
        {
            var l = def.Slots;
            var ml = def.Mounts;
            var st = new SlotStates(l);
            var extra = ml.Floors.Where(m => m.SlotUnder >= 0 && l.Slots[m.SlotUnder].StartsEmpty).ToList();
            Check(extra.Count > 0 && extra.All(m => !DeckCells.MountOpen(m, st)),
                  $"places over her design's openings, for when they're laid: {extra.Count} floor places (none usable while the cells are out)");
            var chest = new FakePiece { Code = "game:chest-east", Family = "game:chest", Container = true, TurnMesh = true, Slots = 16, Sel = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 }, Col = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 } };
            var over = extra.FirstOrDefault(m => l.Slots[m.SlotUnder].Deck == "main") ?? extra.FirstOrDefault();
            if (over != null)
            {
                st.Set(over.SlotUnder, SlotState.Filled);
                var grid = def.Grid.Clone();
                st.Apply(grid);
                var plan = PiecePlanner.Plan(ml, over, chest, 1);
                var clash = plan.Error == null ? PiecePlanner.GridClash(ml, plan, grid) : null;
                Check(DeckCells.MountOpen(over, st) && plan.Error == null && clash == null,
                      $"the opening's cell {l.Slots[over.SlotUnder].Id} laid: a chest goes on {over.Id} ({plan.Error ?? (clash != null ? "clashes with her" : "ok")})");
                st.Set(over.SlotUnder, SlotState.Empty);
            }
            // a cell taken up: the floor place on it, the places stacked on it, the borders anchored on it and the hook under it are gone
            // (a cell of the waist with a floor place on it)
            int cell = ml.Floors.Where(m => m.SlotUnder >= 0 && !l.Slots[m.SlotUnder].StartsEmpty && l.Slots[m.SlotUnder].Deck == "main" &&
                def.RoomAt(m.CX, m.Y + 0.5, m.CZ) == null).Select(m => m.SlotUnder).FirstOrDefault(waist);
            var d = l.Slots[cell];
            var on = ml.All.Where(m => m.SlotUnder == cell).ToList();
            // ((0.3.28, note 230) a twin anchored on it is usable only while the cell across its line is out: not here)
            bool before = on.Where(m => !m.TwinSide).All(m => DeckCells.MountOpen(m, st));
            st.Set(cell, SlotState.Empty);
            bool after = on.All(m => !DeckCells.MountOpen(m, st));
            st.Set(cell, SlotState.Filled);
            Check(on.Any(m => m.Kind == MountKind.Floor) && before && after,
                  $"{d.Id} taken up: its places go ({string.Join(", ", on.GroupBy(m => m.Kind).Select(x => $"{x.Count()} {x.Key}"))}: {string.Join(" ", on.Take(6).Select(m => m.Id))}); laid, they're back");
            var hook = ml.Hooks.FirstOrDefault(m => m.SlotUnder >= 0 && l.Slots[m.SlotUnder].Deck == "main");
            if (hook != null)
            {
                st.Set(hook.SlotUnder, SlotState.Empty);
                Check(!DeckCells.MountOpen(hook, st), $"a hook under a cell ({hook.Id}) goes with it");
                st.Set(hook.SlotUnder, SlotState.Filled);
            }
            // her own places: the same ids and spaces as her layout without the openings'
            var own = MountLayout.Build(def);
            var moved = own.All.Where(m => ml.Get(m.Id) is not MountPoint mm || mm.Box == null || m.Box == null || mm.Box.Zip(m.Box, (x, y) => Math.Abs(x - y)).Max() > 1e-9).ToList();
            Check(moved.Count == 0 && ml.All.Count >= own.All.Count, $"her own places keep their ids and spaces ({own.All.Count} of them; {ml.All.Count - own.All.Count} added over openings; {moved.Count} moved)");
            var sw = Stopwatch.StartNew();
            MountLayout.Build(def);
            Console.WriteLine($"         her layout worked out in {sw.Elapsed.TotalMilliseconds:0} ms (twice that with the openings' places, once at load)");
        }

        /// <summary>(0.3.20) Painting her deck cells: their zones in the paint plan, picked only where they're drawn.</summary>
        static void SlotPaint(ShipDefinition def, ShipSkinFile file, int waist)
        {
            var l = def.Slots;
            int zone = Array.FindIndex(def.PaintZones ?? Array.Empty<PaintZoneDef>(), z => z.Code == "lower-deck-ceiling");
            if (zone < 0) { Check(false, "carrack-m has a lower-deck-ceiling paint zone"); return; }
            // the main deck's cells' undersides in it (the test's own paintQuads)
            var inn = file.Groups["slot:deck:interior"];
            var refs = file.SlotQuads["slot:deck:interior"];
            var ranges = new List<int>();
            for (int q = 0; q < inn.Length; q++)
            {
                int o = refs[2 * q], f = refs[2 * q + 1];
                double ny = inn[q][7] + inn[q][15] + inn[q][23] + inn[q][31];
                if (o >= 0 && f < 0 && ny < -3.5 && l.Slots[o].Deck == "main") ranges.AddRange(new[] { q, q + 1, zone });
            }
            // (the generator's own paintQuads when she has them)
            if (file.PaintQuads == null || !file.PaintQuads.ContainsKey("slot:deck:interior")) file.PaintQuads = new Dictionary<string, int[]> { ["slot:deck:interior"] = ranges.ToArray() };
            var plan = PaintPlan.Build(def, file, null);
            var d = l.Slots[waist];
            var st = new SlotStates(l);
            Func<int, int, bool> Shown(SlotStates x) => (ow, fa) => (ow < 0 || x.Filled(ow)) && (fa < 0 || !x.Filled(fa));
            var o0 = new Vintagestory.API.MathTools.Vec3d(d.CX + 0.1, d.Box[1] - 1.0, d.CZ + 0.1);
            var up = new Vintagestory.API.MathTools.Vec3d(0, 1, 0);
            int inZone = plan.Pick(o0, up, 3, out _, Shown(st));
            st.Set(waist, SlotState.Empty);
            int outZone = plan.Pick(o0, up, 3, out _, Shown(st));
            Check(plan.QuadZones.ContainsKey("slot:deck:interior") && plan.Area[zone] > 50 && inZone == zone && outZone != zone,
                  $"the main deck's cells' undersides are the lower deck's ceiling ({plan.Area[zone]:0} m²): picked from below a cell that's in ({inZone}), not where it's taken up ({outZone})");
        }

        /// <summary>(0.3.20) A wreck from a ship with a cell taken up and an opening laid: the one missing, the other there (no planks twice).</summary>
        static void SlotWreck(ShipDefinition def, int waist)
        {
            var l = def.Slots;
            var d = l.Slots[waist];
            int laid = Enumerable.Range(0, l.Count).FirstOrDefault(n => l.Slots[n].StartsEmpty && l.Slots[n].Deck == "main", -1);
            WreckInput Plain(Dictionary<string, int> slots) => new()
            {
                Def = def, X = 1000, Y = 60, Z = 2000, Yaw = 0, Seed = 1, Settle = 0, Slots = slots,
                Decay = false, Holes = false, Masts = false, Guns = false, Anchor = false,
            };
            // (her wreck's cells are the world's, half a cell off hers up and down: the planking's layer and the one each side of it)
            int CellsIn(WreckPlan p, double[] box) => LocalCells(p).Count(c => c.X > box[0] && c.X < box[3] && c.Y > box[1] - 0.2 && c.Y < box[4] + 0.2 && c.Z > box[2] && c.Z < box[5]);
            var site = new TestSite();
            var asBuilt = WreckBuilder.Build(Plain(null), site);
            var st = new SlotStates(l);
            st.Set(waist, SlotState.Empty);
            if (laid >= 0) st.Set(laid, SlotState.Filled);
            var changed = WreckBuilder.Build(Plain(st.Delta()), new TestSite());
            int cellBefore = CellsIn(asBuilt, d.Box), cellAfter = CellsIn(changed, d.Box);
            int laidBefore = laid >= 0 ? CellsIn(asBuilt, l.Slots[laid].Box) : -1, laidAfter = laid >= 0 ? CellsIn(changed, l.Slots[laid].Box) : -1;
            Check(cellBefore - cellAfter == 16 && (laid < 0 || laidAfter - laidBefore == 16) && changed.CellsDesign == asBuilt.CellsDesign - 16 + (laid >= 0 ? 16 : 0),
                  $"her wreck: the cell taken up isn't in it ({cellBefore} → {cellAfter} cells; its planks went to whoever took it up), an opening laid is ({laidBefore} → {laidAfter}); {asBuilt.CellsDesign} → {changed.CellsDesign} cells in all");
        }

        /// <summary>The quads drawn now (group, index).</summary>
        static HashSet<(string G, int Q)> Visible(SlotMeshPlan plan, ShipSkinFile file, SlotStates st)
        {
            var set = new HashSet<(string, int)>();
            foreach (string gr in SlotMeshPlan.Groups)
                for (int c = 0; c < plan.ChunkCount; c++)
                    foreach (int q in plan.Visible(gr, c, st)) set.Add((gr, q));
            return set;
        }
    }
}
