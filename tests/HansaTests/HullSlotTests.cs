using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    /// <summary>
    /// (0.3.26, step D1; docs/slots.md §9) The hull panels: a design's panels and their geometry checked against the contract
    /// (<see cref="DesignHullSlots"/>, run by <c>-- design &lt;code&gt;</c> for a design with hullSlots, and by <c>-- slots</c>),
    /// and the mechanics on carrack-m (<c>-- hull</c>): picking an opening, the states saved with the deck cells', her
    /// collision, damage to a panel (scraped, cracked, out, through and the far side), the leaks (cracked, an orifice, a
    /// weir), the downflooding points over her main deck, the rooms opened, the repairs, the old saves' damage moved onto
    /// the panels, her wreck, the chunks a change reaches.
    /// </summary>
    static partial class Program
    {
        static (ShipDefinition Def, ShipSkinFile File) LoadHullDesign(string root, string code = "carrack-m")
        {
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", code + ".json");
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            ShipSkinFile file = null;
            if (def.Shapes?.Skin != null)
            {
                var skinPath = Path.Combine(root, "assets", "hansa", def.Shapes.Skin.Replace("hansa:", "").Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(skinPath)) file = JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(skinPath));
            }
            return (def, file);
        }

        static void HullSlots(string root)
        {
            var (def, file) = LoadHullDesign(root);
            if (def.Hull == null)
            {
                Console.WriteLine("  --   carrack-m has no hull panels (waits on the generator): the test's own, from her shell");
                HullSlotSynth.Give(def, HullSlotSynth.Panels(def));
                file = null;
            }
            else DesignHullSlots(def, file);
            HullMechanics(def, file);
        }

        // ------------------------------------------------------------------ the contract (docs/slots.md §9)

        /// <summary>A design's hull panels against the contract: ids, voxels, quads, reveals, the draw rule in random states.</summary>
        static void DesignHullSlots(ShipDefinition def, ShipSkinFile file)
        {
            var l = def.Hull;
            // ((0.3.29, LINED) under her weather decks two layers: a planking panel and a ceiling panel each)
            Check(l != null && l.Count >= 120 && l.Slots.Count(s => !s.IsInner) <= 600, $"{def.Code}: {l?.Count ?? 0} hull panels (planking between 120 and 600; {l?.Slots.Count(s => s.IsInner) ?? 0} of them ceilings), {l?.Slots.Count(s => s.Sill < def.Waterline) ?? 0} reaching under her waterline");
            if (l == null) return;
            foreach (var p in l.Problems.Take(10)) Console.WriteLine("         " + p);
            Check(l.Problems.Count == 0, $"{def.Code}: the panels' ids, places and voxels are each one panel's ({l.Problems.Count} problems)");
            var g = def.Grid;
            var bad = new List<string>();
            foreach (var s in l.Slots)
            {
                var w = s.Id.Split(':');
                // ((0.3.28, D2.1) a frame space's half: a fifth part, "a" or "f")
                if (w.Length != (s.Half == null ? 4 : 5) || w[0] != (s.IsInner ? "ceil" : "hull") || s.Layer != null && (s.PartnerIndex < 0 || l.Slots[s.PartnerIndex].PartnerIndex != s.Index) || (w[1] == "s" ? 1 : w[1] == "p" ? -1 : 0) != s.Side || w[2] != s.Station.ToString() || w[3] != s.Band.ToString() ||
                    s.Half != null && w[4] != s.Half)
                    bad.Add($"{s.Id}: id doesn't say its side {s.Side}, station {s.Station}, band {s.Band}");
                if (def.PartMap.StationAt(s.Centre.X) != s.Station) bad.Add($"{s.Id}: its middle is in station {def.PartMap.StationAt(s.Centre.X)}");
                if (s.Outer?.Length != 4 || s.Normal?.Length != 3 || s.Area <= 0.3 || s.Head <= s.Sill) bad.Add($"{s.Id}: outer/normal/area/sill/head missing");
                if (s.Normal != null && s.Normal[2] * s.Side <= 0.2) bad.Add($"{s.Id}: its normal isn't outward ({string.Join(" ", s.Normal)})");
                int vox = 0;
                foreach (var b in s.SolidBoxes)
                    foreach (long v in DeckSlotLayout.Voxels(g, b))
                    {
                        int k = (int)(v % g.NZ), j = (int)(v / g.NZ % g.NY), i = (int)(v / g.NZ / g.NY);
                        vox++;
                        if (!g.Solid(i, j, k)) { bad.Add($"{s.Id}: a voxel not solid"); goto next; }
                        double vx = g.OX + (i + 0.5) * g.V, vy = g.OY + (j + 0.5) * g.V, vz = g.OZ + (k + 0.5) * g.V;
                        if (vx < s.Box[0] || vx > s.Box[3] || vy < s.Box[1] || vy > s.Box[4] || vz * s.Side <= 0) { bad.Add($"{s.Id}: a voxel outside its box"); goto next; }
                        if (def.Slots?.AtVoxel(g, i, j, k) >= 0) { bad.Add($"{s.Id}: a voxel of a deck cell's"); goto next; }
                    }
                if (vox < 4) bad.Add($"{s.Id}: {vox} voxels");
                next:;
            }
            foreach (var x in bad.Take(10)) Console.WriteLine("         " + x);
            Check(bad.Count == 0, $"{def.Code}: every panel's id its place, its middle in its station, its voxels solid, in its box on its side and no deck cell's ({bad.Count} problems)");
            Check(file != null && file.HullOut == null && file.HullIn == null, $"{def.Code}: her mesh has no hullOut/hullIn (the old jagged holes aren't hers)");
            if (file == null) return;
            var plan = new SlotMeshPlan(l, file, SlotMeshPlan.HullGroups);
            foreach (var p in plan.Problems.Take(10)) Console.WriteLine("         " + p);
            Check(plan.Problems.Count == 0 && SlotMeshPlan.HullGroups.All(gr => file.Groups.ContainsKey(gr)), $"{def.Code}: the four hull slot groups, slotQuads for every quad, none naming a missing panel");
            // each panel: its outside (normal outward) and its ceiling (inward) of its own, within its box
            var outs = new int[l.Count];
            var ins = new int[l.Count];
            var strips = new int[l.Count];
            var pairs = new HashSet<(int, int)>();
            var outside = new List<string>();
            foreach (string gr in SlotMeshPlan.HullGroups)
            {
                if (!file.Groups.TryGetValue(gr, out var quads)) continue;
                var (own, fac) = plan.Refs[gr];
                for (int q = 0; q < quads.Length; q++)
                {
                    int o = own[q], f = fac[q];
                    if (o >= 0 && f >= 0) pairs.Add((o, f));
                    if (o < 0 && f >= 0) strips[f]++;
                    if (o < 0 || f >= 0) continue;
                    var qd = quads[q];
                    var s = l.Slots[o];
                    double nz = (qd[8] + qd[16] + qd[24] + qd[32]) * s.Side, nn = s.Normal[0] * (qd[6] + qd[14] + qd[22] + qd[30]) + s.Normal[1] * (qd[7] + qd[15] + qd[23] + qd[31]) + s.Normal[2] * (qd[8] + qd[16] + qd[24] + qd[32]);
                    if (gr == "slot:hull:bottomclad") continue;
                    if (nn > 0) outs[o]++; else ins[o]++;
                    var b = s.Box;
                    for (int k = 0; k < 4; k++)
                    {
                        double x = qd[1 + 8 * k], y = qd[2 + 8 * k], z = qd[3 + 8 * k];
                        if (x < b[0] - 0.05 || x > b[3] + 0.05 || y < b[1] - 0.05 || y > b[4] + 0.05 || z < b[2] - 0.05 || z > b[5] + 0.05) { outside.Add($"{gr} quad {q} of {s.Id}"); break; }
                    }
                }
            }
            // ((0.3.29) a two-layer panel: the planking its outside, the ceiling its inside)
            // (a two-layer panel's edges: its back toward the other layer, the space between them walled always)
            var noFaces = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Layer == null ? outs[n] == 0 || ins[n] == 0 || strips[n] < 2
                : (l.Slots[n].IsInner ? ins[n] == 0 : outs[n] == 0) || !pairs.Contains((n, l.Slots[n].PartnerIndex))).ToList();
            foreach (int n in noFaces.Take(8)) Console.WriteLine($"         {l.Slots[n].Id}: {outs[n]} outside, {ins[n]} ceiling, {strips[n]} faces of her toward it");
            Check(noFaces.Count == 0, $"{def.Code}: every panel has its outside, its ceiling and the frames' strips' faces toward it ({noFaces.Count} without)");
            foreach (var x in outside.Take(8)) Console.WriteLine("         " + x);
            Check(outside.Count == 0, $"{def.Code}: every panel's own quads lie within its box ({outside.Count} elsewhere)");
            var noPair = new List<string>();
            for (int n = 0; n < l.Count; n++)
                foreach (int nb in l.Neighbours[n])
                    if (nb >= 0 && !pairs.Contains((n, nb))) noPair.Add($"{l.Slots[n].Id}: no edge toward {l.Slots[nb].Id}");
            foreach (var x in noPair.Take(8)) Console.WriteLine("         " + x);
            Check(noPair.Count == 0 && l.Slots.Count(s => true) > 0, $"{def.Code}: neighbouring panels (up and down) each have their edge toward the other ({noPair.Count} problems; {Enumerable.Range(0, l.Count).Sum(n => l.Neighbours[n].Count(x => x >= 0))} such edges)");
            // all in: no reveal drawn, every panel's own quads drawn (she's drawn as the design was)
            var all = new SlotStates(l);
            int revealsShown = 0, ownHidden = 0;
            foreach (string gr in SlotMeshPlan.HullGroups)
            {
                if (!file.Groups.TryGetValue(gr, out var quads)) continue;
                var (own, fac) = plan.Refs[gr];
                for (int q = 0; q < quads.Length; q++)
                {
                    bool shown = plan.Shown(gr, q, all);
                    if (shown && fac[q] >= 0) revealsShown++;
                    if (!shown && own[q] >= 0 && fac[q] < 0) ownHidden++;
                }
            }
            Check(revealsShown == 0 && ownHidden == 0, $"{def.Code}: every panel in, she's drawn as designed: no reveal shown ({revealsShown}), none of the panels' own quads hidden ({ownHidden})");
            // random states: no two drawn quads in one plane overlapping (the hull's own groups, the tarred bottom)
            var rng = new Random(26);
            int worst = 0;
            string worstAt = null;
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < 50; r++)
            {
                var st = new SlotStates(l);
                for (int n = 0; n < l.Count; n++) if (rng.NextDouble() < (r < 5 ? 0.9 : 0.5)) st.Set(n, SlotState.Empty);
                int c = HullCoplanars(file, plan, st, out string at);
                if (c > worst) { worst = c; worstAt = at; }
            }
            Check(worst == 0, $"{def.Code}: 50 random states of her panels (half out; five with nine in ten out): no two drawn quads in one plane overlapping ({worst}{(worstAt != null ? " at " + worstAt : "")}; {sw.ElapsedMilliseconds} ms)");
        }

        /// <summary>The hull groups' quads drawn in these states (not the clad bottom) lying two in one plane, facing one way, overlapping by more than 1e-4 m².</summary>
        static int HullCoplanars(ShipSkinFile file, SlotMeshPlan plan, SlotStates st, out string at)
        {
            at = null;
            var polys = new List<(double[] N, double D, double[][] P)>();
            foreach (string gr in SlotMeshPlan.HullGroups)
            {
                if (gr == "slot:hull:bottomclad" || !file.Groups.TryGetValue(gr, out var quads)) continue;
                for (int q = 0; q < quads.Length; q++)
                {
                    if (!plan.Shown(gr, q, st)) continue;
                    var qd = quads[q];
                    var P = Enumerable.Range(0, 4).Select(k => new double[] { qd[1 + 8 * k], qd[2 + 8 * k], qd[3 + 8 * k] }).ToArray();
                    var n = Cross(Sub(P[2], P[0]), Sub(P[3], P[1]));
                    double len = Math.Sqrt(Dot(n, n));
                    if (len < 1e-9) continue;
                    n = n.Select(v => v / len).ToArray();
                    polys.Add((n, Dot(n, P[0]), P));
                }
            }
            int count = 0;
            var buckets = polys.GroupBy(p => (Math.Round(p.N[0] * 20), Math.Round(p.N[1] * 20), Math.Round(p.N[2] * 20), Math.Round(p.D * 50)));
            foreach (var bk in buckets)
            {
                var list = bk.ToList();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var a = list[i];
                        var b = list[j];
                        if (Dot(a.N, b.N) < 0.9995 || Math.Abs(a.D - b.D) > 0.002) continue;
                        if (OverlapArea(a.N, a.P, b.P) > 1e-4)
                        {
                            count++;
                            at ??= $"({a.P[0][0]:0.00}, {a.P[0][1]:0.00}, {a.P[0][2]:0.00})";
                        }
                    }
            }
            return count;
        }

        static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        /// <summary>The overlap of two planar convex quads in one plane (normal n), by clipping one by the other in the plane's 2D frame.</summary>
        static double OverlapArea(double[] n, double[][] A, double[][] B)
        {
            var u = Math.Abs(n[0]) < 0.9 ? Cross(n, new double[] { 1, 0, 0 }) : Cross(n, new double[] { 0, 1, 0 });
            double ul = Math.Sqrt(Dot(u, u));
            u = u.Select(x => x / ul).ToArray();
            var v = Cross(n, u);
            List<(double X, double Y)> P2(double[][] Q)
            {
                var pts = Q.Select(p => (Dot(p, u), Dot(p, v))).ToList();
                // (counter-clockwise; drop repeats)
                double area = 0;
                for (int i = 0; i < pts.Count; i++) { var p = pts[i]; var q = pts[(i + 1) % pts.Count]; area += p.Item1 * q.Item2 - q.Item1 * p.Item2; }
                if (area < 0) pts.Reverse();
                return pts.Where((p, i) => Math.Abs(p.Item1 - pts[(i + 1) % pts.Count].Item1) + Math.Abs(p.Item2 - pts[(i + 1) % pts.Count].Item2) > 1e-9).ToList();
            }
            var subject = P2(A);
            var clip = P2(B);
            if (subject.Count < 3 || clip.Count < 3) return 0;
            for (int e = 0; e < clip.Count && subject.Count > 0; e++)
            {
                var c0 = clip[e];
                var c1 = clip[(e + 1) % clip.Count];
                double Side((double X, double Y) p) => (c1.X - c0.X) * (p.Y - c0.Y) - (c1.Y - c0.Y) * (p.X - c0.X);
                var input = subject;
                subject = new List<(double, double)>();
                for (int i = 0; i < input.Count; i++)
                {
                    var p = input[i];
                    var q = input[(i + 1) % input.Count];
                    double sp = Side(p), sq = Side(q);
                    if (sp >= -1e-12) subject.Add(p);
                    if (sp >= -1e-12 != sq >= -1e-12)
                    {
                        double t = sp / (sp - sq);
                        subject.Add((p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t));
                    }
                }
            }
            double a2 = 0;
            for (int i = 0; i < subject.Count; i++) { var p = subject[i]; var q = subject[(i + 1) % subject.Count]; a2 += p.Item1 * q.Item2 - q.Item1 * p.Item2; }
            return Math.Abs(a2) / 2;
        }

        // ------------------------------------------------------------------ the mechanics

        static void HullMechanics(ShipDefinition def, ShipSkinFile file)
        {
            var l = def.Hull;
            var dl = def.DamageParts;
            var D = dl.D;
            // a starboard panel amidships under the waterline, its far side's twin, one over the main deck
            int P = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Side > 0 && l.Slots[n].Head < def.Waterline && l.Slots[n].Room == "hold")
                .OrderBy(n => Math.Abs(l.Slots[n].Centre.X - 1.0) + Math.Abs(l.Slots[n].Centre.Y - (def.Waterline - 1.6))).FirstOrDefault(-1);
            Check(P >= 0, $"a starboard panel in the hold under her waterline: {(P >= 0 ? l.Slots[P].Id : "none")}");
            if (P < 0) return;
            var s = l.Slots[P];
            int far = l.IndexOf($"hull:p:{s.Station}:{s.Band}" + (s.Half != null ? ":" + s.Half : ""));
            var c = s.Centre;
            var nrm = new Vec3d(s.Normal[0], s.Normal[1], s.Normal[2]);

            // 1. the layout: part i is panel i
            bool partsOk = Enumerable.Range(0, l.Count).All(n => dl.Parts[n].Slot == n && dl.Parts[n].Kind == DamageKind.Hull) && !dl.Parts.Skip(l.Count).Any(p => p.Kind == DamageKind.Hull);
            Check(partsOk, $"her damage layout: the {l.Count} hull panels are its first parts, in order, and its only hull parts ({dl.Count} parts in all)");

            // 2. picking an opening: from outside through the outside, from inside through the ceiling; none while it's in
            var o1 = new Vec3d(c.X + nrm.X * 3, c.Y + nrm.Y * 3, c.Z + nrm.Z * 3);
            var d1 = new Vec3d(-nrm.X, -nrm.Y, -nrm.Z);
            int pickOut = l.PickOpening(o1, d1, 6, n => n == P, out double t1);
            var o2 = new Vec3d(c.X - nrm.X * 1.5, c.Y - nrm.Y * 1.5, c.Z - nrm.Z * 1.5);
            int pickIn = l.PickOpening(o2, nrm, 6, n => n == P, out double t2);
            int pickNone = l.PickOpening(o1, d1, 6, n => false, out _);
            Check(pickOut == P && Math.Abs(t1 - 3) < 0.3 && pickIn == P && t2 < 1.5 && pickNone < 0, $"its opening picked from outside ({t1:0.00}) and from inside ({t2:0.00}); none while it's in");

            // 3. states: saved with the deck cells' in one tree, loaded back; her collision without it, and with it again
            var hull = new SlotStates(l);
            var deck = def.Slots != null ? new SlotStates(def.Slots) : null;
            hull.Set(P, SlotState.Empty);
            int deckCell = deck != null ? Enumerable.Range(0, def.Slots.Count).First(n => !def.Slots.Slots[n].StartsEmpty) : -1;
            deck?.Set(deckCell, SlotState.Empty);
            var tree = new TreeAttribute();
            deck?.ToTree(tree, false);
            hull.ToTree(tree, false);
            var hull2 = new SlotStates(l);
            var deck2 = deck != null ? new SlotStates(def.Slots) : null;
            hull2.FromTree(tree);
            deck2?.FromTree(tree);
            Check(tree.Count == (deck != null ? 2 : 1) && tree.GetInt(s.Id) == 2 && !hull2.Filled(P) && hull2.Differences == 1 && (deck2 == null || !deck2.Filled(deckCell) && deck2.Differences == 1),
                  $"a panel out and a deck cell up saved in one tree ({tree.Count} entries) and loaded back, each family's own");
            var grid = def.Grid.Clone();
            int before = grid.CountSolid();
            int vox = s.SolidBoxes.Sum(b => DeckSlotLayout.Voxels(grid, b).Count());
            hull.Apply(grid);
            int gone = before - grid.CountSolid();
            hull.Set(P, SlotState.Filled);
            grid.CopyFrom(def.Grid);
            hull.Apply(grid);
            Check(gone == vox && vox > 4 && grid.CountSolid() == before && !def.Grid.Clone().Let(gg => { hull.Set(P, SlotState.Empty); hull.Apply(gg); hull.Set(P, SlotState.Filled); return gg.SolidAt(c.X - nrm.X * 0.2, c.Y, c.Z - nrm.Z * 0.2); }),
                  $"out: its {vox} voxels gone from her grid ({gone}), in again: her grid as designed");

            // 4. a shot on it: 20 kJ scrapes, 40 cracks, 100 stoves it in and goes on, 400 goes through and out the far side
            var hitAt = new Vec3d(c.X + nrm.X * 0.02, c.Y + nrm.Y * 0.02, c.Z + nrm.Z * 0.02);
            float radius = new CannonDef().HitRadius;
            var results = new List<string>();
            DamageState Shot(double e, out DamageResult res)
            {
                var st = new DamageState(dl.Count);
                // (a broadside's shot: square across her, level)
                res = ShipDamage.Apply(dl, st, def.Grid, hitAt, new Vec3d(0, 0, -1), e, radius, DamageKinds.All);
                return st;
            }
            var s20 = Shot(20, out _);
            var s40 = Shot(40, out _);
            var s100 = Shot(100, out var r100);
            var s400 = Shot(400, out var r400);
            // ((0.3.30, note 234: a shot leaves a slot cracked or out, nothing between) 20 kJ cracks it now (it scraped it before)
            Check(HullPanelRules.StateFor(D, s20.Hurt[P]) == SlotState.Cracked && HullPanelRules.StateFor(D, s40.Hurt[P]) == SlotState.Cracked,
                  $"a shot of 20 kJ cracks it (hurt {s20.Hurt[P]:0.00}; 0.3.30: cracked or out, no scraped panel from a shot), 40 cracks it ({s40.Hurt[P]:0.00}); its toughness {D.HullToughness} kJ, cracked from {D.Cracked}, out from {D.Holed}");
            // ((0.3.29, LINED) its ceiling behind it takes CeilBacking of the blow: 100 kJ stoves the planking in, the ceiling
            // takes the rest (a one-layer panel: the shot goes on))
            int PC = dl.Parts[P].Partner;
            Check(HullPanelRules.StateFor(D, s100.Hurt[P]) == SlotState.Empty && (PC >= 0 ? s100.Hurt[PC] >= D.Cracked : r100.Pierced),
                  $"100 kJ stoves it in ({s100.Hurt[P]:0.00}: out) and {(PC >= 0 ? $"its ceiling behind it takes the rest ({s100.Hurt[PC]:0.00})" : "the shot goes on")} ({r100.Hits.Count} parts hit)");
            Check(far >= 0 && HullPanelRules.StateFor(D, s400.Hurt[P]) == SlotState.Empty && s400.Hurt[far] > 0.05f,
                  $"400 kJ goes through her: its twin on the far side {(far >= 0 ? l.Slots[far].Id : "?")} #{far} hit too (hurt {(far >= 0 ? s400.Hurt[far] : 0):0.00}){(r400.Exit != null ? $", out with {r400.Remaining:0} kJ" : "")} " +
                  $"(hit: {string.Join(", ", r400.Hits.Select(h => dl.Parts[h.Part].Part + (dl.Parts[h.Part].Inner ? " (ceiling)" : "") + " #" + h.Part + " +" + h.Added.ToString("0.00")))})");
            // a shot on a frame's strip (fixed): her structure, nothing hurt there, it goes on
            double xs = Math.Floor((c.X - def.Frames.X0) / def.Frames.Space) * def.Frames.Space + def.Frames.X0;
            // ((0.3.28, D2.2) no strips: the frame (the rib) in her side's thickness, its collision the voxel column aft of its line)
            var strip = new Vec3d(s.Half != null ? xs - 0.125 : xs, c.Y, c.Z + nrm.Z * 0.02);
            var stStrip = new DamageState(dl.Count);
            var rStrip = ShipDamage.Apply(dl, stStrip, def.Grid, strip, new Vec3d(0, 0, -1), 60, radius, DamageKinds.All);
            Check(stStrip.Hurt.Take(l.Count).Where((h, n) => Math.Abs(l.Slots[n].Centre.X - xs) < 1.0 && l.Slots[n].Side > 0 && l.Slots[n].Band == s.Band).All(h => h < D.Cracked) ||
                  // ((0.3.28, D2.2) the rib's only a quarter block: a ball on it splinters the planking beside it, nothing further off)
                  s.Half != null && stStrip.Hurt.Take(l.Count).Where((h, n) => Math.Abs(l.Slots[n].Centre.X - xs) > 1.0).All(h => h < D.Cracked),
                  $"a shot of 60 kJ on the frame's strip (0.3.28: its rib) at x {xs:0.00}: her structure takes some, no panel {(s.Half != null ? "but those beside it" : "beside it")} cracked ({rStrip.Hits.Count} parts hit, pierced {rStrip.Pierced})");

            // 5. the leaks: cracked about a litre a second, out an orifice (0.75 t/s at 2 deep), a weir while the sea's between
            var pose = new FloodPose { X = 0, Y = 0, Z = 0, Surface = (x, z) => def.Waterline };
            var cracked = new DamageState(dl.Count);
            cracked.Hurt[P] = 0.5f;
            double qc = ShipFlood.Leaks(dl, cracked, 0, pose, 1, out _);
            var open = new DamageState(dl.Count);
            open.Hurt[P] = 1;
            double qo = ShipFlood.Leaks(dl, open, 0, pose, 1, out double side);
            // ((0.3.29) its ceiling behind it, in: LinedLeak of the flow)
            double lined = dl.Parts[P].Partner >= 0 ? D.LinedLeak : 1;
            double depth = def.Waterline - c.Y, expectO = D.LeakCoefficient * D.OpenLeak * dl.Parts[P].Area / 1.5 * Math.Sqrt(depth) * lined;
            Check(qc > 0.0003 && qc < 0.003, $"cracked it lets in {qc * 1000:0.0} l/s ({depth:0.00} under the water)");
            Check(Math.Abs(qo - expectO) < 1e-6 && side > 0, $"out it lets in {qo:0.00} t/s (an opening of {D.OpenLeak} m² a panel of 1.5; at 2 deep {D.LeakCoefficient * D.OpenLeak * Math.Sqrt(2):0.00}), to starboard");
            int wp = Enumerable.Range(0, l.Count).FirstOrDefault(n => l.Slots[n].Sill < def.Waterline && l.Slots[n].Head > def.Waterline && l.Slots[n].Side > 0 && !l.Slots[n].IsInner, -1);
            if (wp >= 0)
            {
                var ws = l.Slots[wp];
                var wopen = new DamageState(dl.Count);
                wopen.Hurt[wp] = 1;
                double mid = (ws.Sill + ws.Head) / 2;
                var wpose = new FloodPose { Surface = (x, z) => mid };
                double qw = ShipFlood.Leaks(dl, wopen, 0, wpose, 1, out _);
                double expectW = ShipFlood.Pour(D, ws.Length, 1, mid - ws.Sill) * (dl.Parts[wp].Partner >= 0 ? D.LinedLeak : 1);
                var full = new FloodPose { Surface = (x, z) => ws.Head + 0.5 };
                double qf = ShipFlood.Leaks(dl, wopen, 0, full, 1, out _);
                Check(Math.Abs(qw - expectW) < 1e-6 && qw > 0 && qf > 0, $"one out at her waterline ({ws.Id}) with the sea half way up it pours over its sill ({qw:0.00} t/s, a weir {ws.Length:0.0} long); the sea over its head, an orifice ({qf:0.00} t/s)");
            }
            // sinking time: one panel out 1-2 deep against two at the pumps
            double pump = ShipFlood.PumpRate(D, 2);
            double reserve = D.FounderAt * dl.Reserve;
            Console.WriteLine($"         one panel out ({s.Id}, {depth:0.0} deep): {qo:0.00} t/s in, two at the pumps {pump:0.00} t/s out: she founders in about {reserve / Math.Max(1e-3, qo - pump) / 60:0} minutes ({reserve:0} t to founder)");

            // 6. a panel over her main deck: a downflooding point while it's out
            int up = Enumerable.Range(0, l.Count).FirstOrDefault(n => l.Slots[n].Above(def), -1);
            if (up >= 0)
            {
                var us = l.Slots[up];
                bool listed = dl.Downflood.Any(f => f.OpenFixture == "hull:" + up && Math.Abs(f.Y - us.Sill) < 1e-6);
                var high = new FloodPose { Surface = (x, z) => us.Sill + 0.3 };
                double shut = ShipFlood.Downflooding(dl, high, x => false), opened = ShipFlood.Downflooding(dl, high, x => x == "hull:" + up);
                var stUp = new DamageState(dl.Count);
                stUp.Hurt[up] = 1;
                double leakUp = ShipFlood.Leaks(dl, stUp, 0, high, 1, out _);
                Check(listed && opened > shut && leakUp == 0, $"{us.Id} over her main deck{(us.Room != null ? " (" + us.Room + ")" : "")}: out, the sea over its sill comes aboard there ({opened - shut:0.000} t/s more), not as a leak below");
            }
            // 7. the room behind an open panel is open
            var hold = def.Rooms.First(r => r.Code == s.Room);
            // ((0.3.29) two layers: the room's open where both are out)
            int PC2 = s.PartnerIndex;
            Check(HullPanelRules.Leaks(l, n => n == P || n == PC2, hold).Count == 1 && HullPanelRules.Leaks(l, n => false, hold).Count == 0 &&
                  (PC2 < 0 || HullPanelRules.Leaks(l, n => n == P, hold).Count == 0 && HullPanelRules.Leaks(l, n => n == PC2, hold).Count == 0),
                  $"out{(PC2 >= 0 ? " (its planking and its ceiling)" : "")}, it opens the {hold.Code}; in{(PC2 >= 0 ? " (or one of the two)" : "")}, nothing does");

            // 8. repairs: cracked takes planks and nails; out, it's laid again (0.3.27: out of building mode too, planks blocks or boards)
            var needCracked = ShipRepairs.Needs(dl, cracked, P);
            var needOut = ShipRepairs.Needs(dl, open, P);
            Check(needCracked.Count > 0 && needCracked[0].What == "planks" && needCracked[0].Kits[0].Items.Any(i => i.Code == ShipRepairs.Plank && i.Count == 2) &&
                  needOut.Count > 0 && needOut[0].What == "lay" && needOut[0].AtSea && ShipRepairs.For(dl, open, P, "game:plank-oak")?.What == "lay",
                  $"cracked it's mended with 2 planks and a nail (the repair tool); out, it's laid again ({needOut.FirstOrDefault()?.What}: {s.Mat.Count} planks blocks or 4 boards each, the repair tool's way too since 0.3.27)");
            var mended = new DamageState(dl.Count);
            mended.Hurt[P] = 0.5f;
            ShipRepairs.Apply(mended, needCracked[0]);
            Check(HullPanelRules.StateFor(D, mended.Hurt[P]) == SlotState.Filled, "mended, it's in and sound");

            // 9. an old save's hull damage moved onto the panels (hansaDmgF v 1: whole-block bands from her keel)
            int oldBand = (int)Math.Floor(c.Y - def.KeelY);
            int oldCode = ShipPart.Hull(1, s.Station, oldBand).Encode();
            int mastCode = ShipPart.Mast(0).Encode();
            var (codes, hurts, wears) = HullMigration.Map(def, new[] { oldCode, mastCode }, new[] { 0.9f, 0.5f }, new[] { 0.4f, 0f });
            var migrated = new DamageState(dl.Count);
            migrated.Load(dl, codes, hurts, wears);
            var hitPanels = Enumerable.Range(0, l.Count).Where(n => migrated.Hurt[n] > 0).ToList();
            int mast = dl.Find(mastCode);
            Check(hitPanels.Count >= 1 && hitPanels.Count(n => migrated.Hurt[n] >= 0.9f - 1e-6) == 1 && hitPanels.All(n => l.Slots[n].Station == s.Station && l.Slots[n].Side > 0 && migrated.Wear[n] >= 0.4f - 1e-6) &&
                  hitPanels.Where(n => migrated.Hurt[n] < 0.9f).All(n => migrated.Hurt[n] <= D.Cracked + 1e-6) && mast >= 0 && Math.Abs(migrated.Hurt[mast] - 0.5f) < 1e-6,
                  $"an old save's hull part (band {oldBand} from the keel, hurt 0.9) onto {hitPanels.Count} panel(s) of its frame space ({string.Join(", ", hitPanels.Select(n => $"{l.Slots[n].Id} {migrated.Hurt[n]:0.00}"))}); the mast's as it was");

            // 10. her wreck: a panel out isn't in it; broken up, whole panels are stove in
            WreckInput Plain(Dictionary<string, int> slots, bool decay) => new()
            {
                Def = def, X = 1000, Y = 60, Z = 2000, Yaw = 0, Seed = 3, Settle = 0, Slots = slots,
                Decay = decay, Holes = false, Masts = false, Guns = false, Anchor = false,
            };
            var asBuilt = WreckBuilder.Build(Plain(null, false), new TestSite());
            var holed = WreckBuilder.Build(Plain(new Dictionary<string, int> { [s.Id] = 2 }, false), new TestSite());
            Check(asBuilt.CellsDesign - holed.CellsDesign == vox, $"her wreck with {s.Id} out: its {vox} voxels aren't in it ({asBuilt.CellsDesign} → {holed.CellsDesign} cells)");
            var broken = WreckBuilder.Build(Plain(null, true), new TestSite());
            int breachVox = broken.PanelsBreached.Sum(id => l.Get(id).SolidBoxes.Sum(b => DeckSlotLayout.Voxels(def.Grid, b).Count()));
            Check(broken.PanelsBreached.Count >= 2 && broken.PanelsBreached.Count <= 4 && broken.CellsHoled > 0 && broken.CellsHoled <= breachVox,
                  $"broken up, {broken.PanelsBreached.Count} whole panels stove in ({string.Join(", ", broken.PanelsBreached)}: {broken.CellsHoled} cells of their {breachVox})");

            // 11. drawing: a panel's change reaches its own chunk (and its neighbours' over the chunk's edge), and is quick
            if (file != null)
            {
                var plan = new SlotMeshPlan(l, file, SlotMeshPlan.HullGroups);
                var affected = plan.Affected(new[] { P });
                Check(affected.Count >= 1 && affected.Count <= 3, $"its change builds again {affected.Count} chunk(s) of {plan.ChunkCount}");
                var st = new SlotStates(l);
                var sw = Stopwatch.StartNew();
                int edits = 200, built = 0;
                var rng = new Random(9);
                double worstChunk = 0;
                for (int e = 0; e < edits; e++)
                {
                    int n = rng.Next(l.Count);
                    st.Set(n, st.Filled(n) ? SlotState.Empty : SlotState.Filled);
                    grid.CopyFrom(def.Grid);
                    st.Apply(grid);
                    foreach (int ch in plan.Affected(st.TakeChanged()))
                    {
                        var t0 = Stopwatch.GetTimestamp();
                        foreach (string gr in SlotMeshPlan.HullGroups)
                        {
                            if (!file.Groups.TryGetValue(gr, out var quads)) continue;
                            var vis = plan.Visible(gr, ch, st);
                            var arr = new float[vis.Count][];
                            for (int k = 0; k < vis.Count; k++) arr[k] = (float[])quads[vis[k]].Clone();
                        }
                        worstChunk = Math.Max(worstChunk, (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                        built++;
                    }
                }
                double perEdit = sw.Elapsed.TotalMilliseconds / edits;
                int quadsAll = SlotMeshPlan.HullGroups.Sum(gr => file.Groups.TryGetValue(gr, out var q) ? q.Length : 0);
                Console.WriteLine($"         {l.Count} panels, {quadsAll} quads in {plan.ChunkCount} chunks; an edit {perEdit:0.000} ms ({built / (double)edits:0.0} chunks), slowest chunk {worstChunk:0.000} ms");
                Check(perEdit < 5, $"a panel's change costs {perEdit:0.000} ms on the CPU before the upload (under 5)");
            }
        }
    }

    static class TestLet
    {
        public static TR Let<T, TR>(this T v, Func<T, TR> f) => f(v);
    }
}
