using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (22 October 2026, caravel chalk notes 51, 52: "cannot place 1 block stairs here"; "cannot also place horizontal stairs
        /// - says something about beams") Ship's stairs from her hold up to the main deck on the centreline cell x -3..-2, z
        /// -0.5..0.5, between her main deck's beams (each in the middle of a rib cell: x -3.75..-3.25 and -1.75..-1.25).
        /// Across her (along z) the short and steep stairs go up through the cells over them; along her the cell before the
        /// foot is a rib cell, its beam over the walker's head: with her beams fixed that's refused as a fixed beam, and her
        /// deck still laid over a stair across her is "open the deck", not a beam (it said beam). With one-cell beam segments
        /// (docs/legacy/slots.md §10, the cell over a rib cell's beam) the beam's length comes out with the stair along her.
        ///   dotnet run --project tests/HansaTests -- holdstair (and with design caravel)
        /// </summary>
        static void CaravelHoldStair(string root)
        {
            string path = Path.Combine(root, "assets", "ships", "config", "ships", "caravel.json");
            if (!File.Exists(path)) { Console.WriteLine("  --   no caravel"); return; }
            Console.WriteLine("22 October 2026: caravel notes 51, 52 (stairs from her hold between her beams)");
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            var foot = HoldFoot(def);
            Check(foot != null, $"caravel: the hold's cell at note 51's place ({FootX}, {FootZ}): {foot?.Id}");
            if (foot == null) return;
            if (Environment.GetEnvironmentVariable("HANSA_HOLDSTAIR") != null)
                for (double z = -2.375; z < 2.5; z += 0.25)
                {
                    var row = new System.Text.StringBuilder();
                    for (double x = -5.875; x < 0; x += 0.25)
                    {
                        int n = 0;
                        for (double y = 0.125 + 0.25; y < 0.9; y += 0.25) n = n * 2 + (def.Grid.SolidAt(x, y, z) ? 1 : 0);
                        row.Append(n);
                    }
                    Console.WriteLine($"    z {z,6:0.000}: {row}");
                }
            bool segmented = def.Slots.Beams.Count > 0;
            // as she is: across her the stairs go up through the cells over them; along her a beam is in the way
            var said = new List<string>();
            foreach (int cells in new[] { 1, 2 })
                foreach (int toward in new[] { 0, 1, 2, 3 })
                {
                    var (plan, st, g) = HoldStairPlan(def, foot, cells, toward);
                    var clash = PiecePlanner.GridClash(def.Mounts, plan, g);
                    bool across = toward % 2 == 1;
                    string beam = clash == null ? null : BeamRules.ClashSays(def.Mounts, def.Slots, st, plan, g, b => BeamAtOffline(def, st, b));
                    // ((the deck laid over it: what it's told))
                    var laid = new SlotStates(def.Slots);
                    var lg = def.Grid.Clone();
                    laid.Apply(lg);
                    var laidClash = PiecePlanner.GridClash(def.Mounts, plan, lg);
                    string laidSays = laidClash == null ? "fits" : BeamRules.ClashSays(def.Mounts, def.Slots, laid, plan, lg, b => BeamAtOffline(def, laid, b)) ?? "ships:flight-no-well";
                    bool walked = clash == null && WalkWith(g, plan, foot, cells);
                    if (across && !(clash == null && walked)) said.Add($"{cells} long across ({toward}): {(clash == null ? "not walked" : beam ?? "no well")}");
                    if (!across && !segmented && beam != "ships:flight-beam-fixed") said.Add($"{cells} long along ({toward}): {beam ?? (clash == null ? "fits" : "no well")}, not a fixed beam");
                    // ((along her the beam is in the way under her laid deck too))
                    if (across && laidSays != "ships:flight-no-well") said.Add($"{cells} long across ({toward}) under her laid deck: {laidSays}");
                }
            Check(said.Count == 0, $"caravel{(segmented ? "" : " (fixed beams)")}: the short and steep stairs from the hold cell {foot.Id} across her through the main deck's cells over them walked; " +
                                   $"{(segmented ? "" : "along her refused for the fixed beam over the cell before the foot; ")}under her laid deck \"open the deck\" (flight-no-well), not a beam{(said.Count > 0 ? ": " + string.Join("; ", said) : "")}");
            if (segmented) Console.WriteLine($"         caravel has {def.Slots.Beams.Count} beams in lengths (the exporter's): the checks below use hers");
            // with her main deck's beams in one-cell lengths (the cell over each): along her the beam's length comes out
            var sdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            // ((22 October 2026) her own, since the exporter makes them; the synthesized ones only for a caravel without)
            int added = segmented ? sdef.DeckSlots.Count(s => s.Kind == "beam") : AddRibBeamSegments(sdef, "main");
            sdef.Init();
            var sfoot = HoldFoot(sdef);
            var problems = sdef.Slots.Problems.Where(p => p.Contains("beam")).ToList();
            Check(added > 0 && problems.Count == 0, $"caravel with {added} one-cell beam segments under her main deck (each the cell over a rib cell's beam): her slot layout takes them ({string.Join("; ", problems.Take(3))})");
            // the stairs along her: their beams' lengths out with them, walked; or what's still in the way
            string Along(MountPoint f, int cells, int toward)
            {
                var (plan, st, g) = HoldStairPlan(sdef, f, cells, toward);
                if (plan.Error != null) return plan.Error;
                var need = BeamRules.InTheWay(sdef.Mounts, sdef.Slots, st, plan, g, null);
                if (need == null || need.Count == 0)
                    return PiecePlanner.GridClash(sdef.Mounts, plan, g) == null ? (WalkWith(g, plan, f, cells) ? "walked" : "not walked")
                         : BeamRules.ClashSays(sdef.Mounts, sdef.Slots, st, plan, g, b => BeamAtOffline(sdef, st, b)) ?? (StairLanding.At(plan, g) ? "ships:flight-no-landing" : "ships:flight-no-well");
                foreach (int n in need) st.Set(n, SlotState.Empty);
                var go = sdef.Grid.Clone();
                st.Apply(go);
                bool walked = PiecePlanner.GridClash(sdef.Mounts, plan, go) == null && WalkWith(go, plan, f, cells);
                return (walked ? "walked, " : "not walked, ") + string.Join(" ", need.Select(n => sdef.Slots.Slots[n].Id)) + " out";
            }
            var starboard = sdef.Mounts.FloorAt("hold", sfoot.I, sfoot.K + 1);
            var rows = new List<(string What, string Got, bool Want)>();
            foreach (var f in new[] { sfoot, starboard })
                foreach (int cells in new[] { 1, 2 })
                    foreach (int toward in new[] { 0, 2 })
                    {
                        string got = f == null ? "no cell" : Along(f, cells, toward);
                        // (what has to work: the short stair up aft from the note's cell (the beam at x -2's length out), both
                        // ways from the cell to starboard of it (x -2's or x -4's); the others meet her mainmast at x -1..0 or
                        // the old hold hatchway's carling at z -0.5..-0.25 under main:-4:-1, fixed. (24 October 2026, note 73)
                        // the place now under the steerage, cell -9: the beams at x -8 and -10; both ways from the note's
                        // cell, up aft from the one to starboard (up forward its landing is the hold's narrow after part,
                        // where her side fills the cell))
                        bool want = cells == 1 && (toward == 0 || f == sfoot);
                        rows.Add(($"{f?.Id} {cells} long up {(toward == 0 ? "aft" : "forward")}", got, want));
                    }
            foreach (var r in rows) Console.WriteLine($"         {r.What}: {r.Got}");
            var missing = rows.Where(r => r.Want && !r.Got.StartsWith("walked")).Select(r => $"{r.What}: {r.Got}").ToList();
            Check(missing.Count == 0, $"with them, the short stair along her takes the beam's length over the cell before its foot out and is walked (both ways from {sfoot?.Id}, up aft from {starboard?.Id}){(missing.Count > 0 ? ": " + string.Join("; ", missing) : "")}");
            // its rule: the cell over it open first
            int seg = Enumerable.Range(0, sdef.Slots.Count).FirstOrDefault(n => sdef.Slots.Slots[n].IsBeam, -1);
            if (seg >= 0)
            {
                var st0 = new SlotStates(sdef.Slots);
                string before = BeamRules.TakeProblem(sdef.Slots, st0, seg);
                st0.Set(sdef.Slots.BesideOf[seg][0], SlotState.Empty);
                string after = BeamRules.TakeProblem(sdef.Slots, st0, seg);
                Check(before == "ships:beam-deck-in" && after == null, $"a one-cell beam length ({sdef.Slots.Slots[seg].Id}) comes out only with the cell over it open ({before ?? "ok"}, then {after ?? "ok"})");
            }
        }

        // (24 October 2026, caravel note 73: 2 blocks longer, her hold's hatch and stair on the centreline over the note's
        // cell (x -2.241, z -0.172); the same place between her frames, a rib cell either side, where her hold is as wide:
        // x -6.241, under the cabin's front. (25 October 2026, draft D: the hatch -7..-4 over that cell, the main mast at -3)
        // cell -9, the one left between two ribs clear of a hatch, a mast, the gates, the guns and the capstan
        const double FootX = -8.241, FootZ = -0.172;
        static MountPoint HoldFoot(ShipDefinition def) =>
            def.Mounts.Floors.FirstOrDefault(m => m.Deck == "hold" && m.Level == 0 && m.X <= FootX && m.X + 1 > FootX && m.Z <= FootZ && m.Z + 1 > FootZ);

        /// <summary>A ship's stair from the hold cell toward a way, with the main deck's cells over its cells and the one before its foot taken up: the plan, her cells' states, her collision with them.</summary>
        static (PiecePlan Plan, SlotStates St, VoxelGrid Grid) HoldStairPlan(ShipDefinition def, MountPoint foot, int cells, int toward)
        {
            var block = new FakePiece { Code = ModularPieceDef.StairBlock(cells), Family = "x", Stairs = true, StairCells = cells, StairRails = true };
            var plan = PiecePlanner.Plan(def.Mounts, foot, block, toward);
            var sl = def.Slots;
            var st = new SlotStates(sl);
            if (plan.Error == null)
                for (int c = -1; c < cells; c++)
                {
                    double cx = foot.X + 0.5 + MountLayout.DX[plan.Dir] * c, cz = foot.Z + 0.5 + MountLayout.DZ[plan.Dir] * c;
                    for (int n = 0; n < sl.Count; n++)
                        if (sl.Slots[n].IsCell && sl.Slots[n].Deck == "main" && Math.Abs(sl.Slots[n].CX - cx) < 0.3 && Math.Abs(sl.Slots[n].CZ - cz) < 0.3) st.Set(n, SlotState.Empty);
                }
            var g = def.Grid.Clone();
            st.Apply(g);
            return (plan, st, g);
        }

        static bool WalkWith(VoxelGrid g, PiecePlan plan, MountPoint foot, int cells)
        {
            var gs = g.Clone();
            foreach (var sb in plan.Solids) gs.Fill(sb);
            return PiecePlanner.WalkFlight(gs, foot, plan.Dir, plan.Rise, out _, out _, cells);
        }

        /// <summary>EntityShip.BeamAt offline: a filled beam segment in the box, else -1 for her collision just under a deck (a beam with no lengths there), else null.</summary>
        static int? BeamAtOffline(ShipDefinition def, SlotStates st, double[] box)
        {
            var l = def.Slots;
            foreach (var kv in l.Beams)
                foreach (int n in kv.Value)
                    if (st.Filled(n) && PiecePlanner.Overlap(l.Slots[n].Box, box, 0.005)) return n;
            foreach (var dk in def.Decks)
                if (box[4] > dk.Y - 1.0 && box[1] < dk.Y - 0.2 && box[4] <= dk.Y + 0.01) return -1;
            return null;
        }

        /// <summary>
        /// One-cell beam segments for a deck's beams, as the caravel's exporter would give them (docs/legacy/slots.md §10): for
        /// each of that deck's cells over a rib cell, her collision under its planking within the cell (a beam's voxels, the
        /// whole cell's width along the beam, nothing else of her there), "beside" the cell over it. How many were added.
        /// </summary>
        static int AddRibBeamSegments(ShipDefinition def, string deckCode)
        {
            var deck = def.Decks.First(d => d.Code == deckCode);
            var probe = JsonConvert.DeserializeObject<ShipDefinition>(JsonConvert.SerializeObject(def));
            probe.Init();
            var g = probe.Grid;
            var ribs = Enumerable.Range(0, def.Frames.Count).Select(k => def.Frames.X0 + k * def.Frames.Space).ToHashSet();
            var added = new List<DeckSlotDef>();
            foreach (var c in def.DeckSlots.Where(s => s.Kind == null && s.Deck == deckCode && ribs.Contains(Math.Round(s.Box[0], 3))))
            {
                double x0 = c.Box[0], z0 = c.Box[2], top = deck.Y - 0.25;
                double bx0 = double.MaxValue, bx1 = double.MinValue, by0 = double.MaxValue;
                bool whole = true;
                for (double z = z0 + 0.125; z < z0 + 1; z += 0.25)
                {
                    bool any = false;
                    for (double x = x0 + 0.125; x < x0 + 1; x += 0.25)
                        for (double y = top - 0.125; y > top - 1.0; y -= 0.25)
                        {
                            if (!g.SolidAt(x, y, z)) continue;
                            any = true;
                            bx0 = Math.Min(bx0, x - 0.125); bx1 = Math.Max(bx1, x + 0.125); by0 = Math.Min(by0, y - 0.125);
                        }
                    whole &= any;
                }
                // (a clean beam: within the cell, not at its fore or aft edge, and nothing of her side in it)
                if (!whole || bx0 <= x0 + 1e-6 || bx1 >= x0 + 1 - 1e-6 || by0 < top - 0.75 - 1e-6) continue;
                var box = new[] { bx0, by0, z0, bx1, top, z0 + 1 };
                bool solid = true;
                for (double x = bx0 + 0.125; x < bx1; x += 0.25)
                    for (double y = by0 + 0.125; y < top; y += 0.25)
                        for (double z = z0 + 0.125; z < z0 + 1; z += 0.25) solid &= g.SolidAt(x, y, z);
                if (!solid) continue;
                added.Add(new DeckSlotDef
                {
                    Id = $"beam:{c.Id}", Deck = deckCode, Kind = "beam", Beam = $"{deckCode}:{x0}", Beside = new[] { c.Id }, Box = box,
                    Material = new SlotMaterialDef { Block = "game:planks-oak-ud", Count = 1 }
                });
            }
            if (Environment.GetEnvironmentVariable("HANSA_HOLDSTAIR") != null)
                foreach (var a in added) Console.WriteLine($"    {a.Id} [{string.Join(", ", a.Box.Select(v => v.ToString("0.###")))}]");
            def.DeckSlots = def.DeckSlots.Concat(added).ToArray();
            return added.Count;
        }
    }
}
