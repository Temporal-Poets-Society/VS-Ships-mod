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
        /// (0.3.27) Chalk notes 198-222 and the user's stairs and beams: trimmed beams (beam segments, their rules, a stair
        /// through a well with the beam taken out for it, walked; a stair across her between two beams, no cut), mending a
        /// hole outside building mode with planks or boards (the repair job, the crosshair's pick from inside, outside and
        /// above), relative gun laying (no jump when it starts), the stair's guards beside a deck (note 215), the balusters
        /// in the stringers (200), the mast hooks at the design's height (202), the lanterns on the mod's ceiling hooks (221),
        /// the poop's three guns a side (203), the bow guns' train about her side's normal and their lids' hinges (205, 206),
        /// the recoil (204), the brow's way (219).
        ///   dotnet run -c Debug -- notes0327
        /// </summary>
        static void Notes0327(string root)
        {
            Console.WriteLine("0.3.27: chalk notes 198-222, the stairs and beams, mending holes, gun laying");
            var (md, file) = LoadHullDesign(root);
            Beams0327(md, file);
            Mending0327(md);
            Laying0327();
            StairBits0327(md);
            Hooks0327(md);
            Guns0327(md);
            Brow0327(md);
        }

        static FakePiece ShipStair0327(int cells) =>
            new FakePiece { Code = cells == 2 ? ModularPieceDef.SteepStair : ModularPieceDef.LongStair, Family = "x", Stairs = true, StairCells = cells, StairRails = true };

        static void Beams0327(ShipDefinition md, ShipSkinFile file)
        {
            var l = md.Slots;
            var beams = Enumerable.Range(0, l?.Count ?? 0).Where(n => l.Slots[n].IsBeam).ToList();
            var decks = beams.GroupBy(n => l.Slots[n].Deck).ToDictionary(g => g.Key, g => g.Count());
            Check(beams.Count > 20 && beams.All(n => l.BesideOf[n]?.Length == 2 && l.BesideOf[n].All(b => b >= 0 && !l.Slots[b].IsBeam)) && l.Problems.Count == 0,
                $"the beams under her decks in lengths that come out: {beams.Count} ({string.Join(", ", decks.Select(kv => $"{kv.Key} {kv.Value}"))}), each with its two deck cells beside it; {l?.Problems.Count} problems");
            if (beams.Count == 0) return;
            // the beam's collision: the one voxel column aft of its line (a cell forward of a beam is free of it)
            var g = md.Grid;
            var b0 = l.Slots[beams[0]].Box;
            Check(Math.Abs(b0[3] - b0[0] - 0.25) < 1e-6 && Math.Abs(b0[3] - Math.Round(b0[3])) < 1e-6 && (int)Math.Round(b0[3]) % 2 == 0 &&
                  !g.SolidAt(b0[3] + 0.125, (b0[1] + b0[4]) / 2, (b0[2] + b0[5]) / 2) && g.SolidAt(b0[3] - 0.125, (b0[1] + b0[4]) / 2, (b0[2] + b0[5]) / 2),
                $"a beam's collision is the voxel column aft of its frame's line ({b0[0]:0.###}..{b0[3]:0.###}): the cell forward of it is free under the deck");
            // the rules: the deck either side open first; at most two of a beam, next to each other
            var main = beams.Where(n => l.Slots[n].Deck == "main").OrderBy(n => Math.Abs(l.Slots[n].CX - 4) + Math.Abs(l.Slots[n].CZ - 2.5)).ToList();
            var st = new SlotStates(l);
            int s0 = main.FirstOrDefault(n => l.BeamNeighbours(n).Plus >= 0, -1);
            Check(s0 >= 0, "a main deck beam with two lengths side by side that come out");
            if (s0 < 0) return;
            int s1 = l.BeamNeighbours(s0).Plus;
            string inDeck = BeamRules.TakeProblem(l, st, s0);
            foreach (int c in l.BesideOf[s0].Concat(l.BesideOf[s1])) st.Set(c, SlotState.Empty);
            string open = BeamRules.TakeProblem(l, st, s0);
            st.Set(s0, SlotState.Empty);
            string second = BeamRules.TakeProblem(l, st, s1);
            st.Set(s1, SlotState.Empty);
            var list = l.Beams[l.Slots[s0].Beam];
            int far = list.FirstOrDefault(n => n != s0 && n != s1, -1);
            if (far >= 0) foreach (int c in l.BesideOf[far]) st.Set(c, SlotState.Empty);
            string third = far >= 0 ? BeamRules.TakeProblem(l, st, far) : "ships:beam-too-many";
            Check(inDeck == "ships:beam-deck-in" && open == null && second == null && third == "ships:beam-too-many",
                $"a length comes out only with the deck either side open ({inDeck}, then {open ?? "allowed"}), its neighbour too ({second ?? "allowed"}), a third of that beam not ({third})");
            // drawn with them out: the carlings and the cut beam, no two quads in one plane
            var plan = new SlotMeshPlan(l, file);
            Check(plan.Problems.Count == 0 && Coplanars(md, file, plan, st, "beam lengths out") == 0, $"drawn with two lengths of a beam out (and the cells beside them): no two quads in one plane ({plan.Problems.Count} problems)");
            var carlings = new List<int>();
            foreach (string gr in SlotMeshPlan.Groups)
            {
                if (!file.Groups.TryGetValue(gr, out var quads)) continue;
                var (own, fac) = plan.Refs[gr];
                for (int q = 0; q < quads.Length; q++)
                    if ((fac[q] == s0 || fac[q] == s1) && quads[q].Where((v, i) => i % 8 == 1).Max() - quads[q].Where((v, i) => i % 8 == 1).Min() > 1.5) carlings.Add(q);
            }
            Check(carlings.Count > 0, $"the carlings along the opening's sides run from the beam aft to the beam forward, bay by bay ({carlings.Count} quads 1.5+ long shown with the lengths out)");

            // ---- a steep stair up through a well across a beam: the beam comes out for it, it's walked up and down
            var ml = md.Mounts;
            var mainY = md.Decks.First(d => d.Code == "main").Y;
            bool done = false;
            foreach (int seg in main)
            {
                var sd = l.Slots[seg];
                double xc = sd.Box[3];
                // the stair on the lower deck climbing +x: the well over P (xc-1..xc), F (xc..xc+1), H (xc+1..xc+2)
                var foot = ml.Floors.FirstOrDefault(m => m.Deck == "lower" && Math.Abs(m.X - xc) < 1e-6 && Math.Abs(m.Z + 0.5 - sd.CZ) < 1e-6);
                if (foot == null) continue;
                var wellCells = new[] { xc - 0.5, xc + 0.5, xc + 1.5 }.Select(x => l.OnDeck(mainY, x, sd.CZ)).ToList();
                if (wellCells.Any(c => c < 0)) continue;
                var stw = new SlotStates(l);
                foreach (int c in wellCells) stw.Set(c, SlotState.Empty);
                var gw = md.Grid.Clone();
                stw.Apply(gw);
                var sp = PiecePlanner.Plan(ml, foot, ShipStair0327(2), 2);
                if (sp.Error != null) continue;
                var clash = PiecePlanner.GridClash(ml, sp, gw);
                var need = BeamRules.InTheWay(ml, l, stw, sp, gw, null);
                if (clash == null || need == null || !need.Contains(seg)) continue;
                foreach (int n in need) stw.Set(n, SlotState.Empty);
                var gb = md.Grid.Clone();
                stw.Apply(gb);
                var after = PiecePlanner.GridClash(ml, sp, gb);
                var gs = gb.Clone();
                foreach (var b in sp.Solids) gs.Fill(b);
                bool walked = PiecePlanner.WalkFlight(gs, foot, sp.Dir, sp.Rise, out double top, out double back, 2);
                Check(after == null && walked && need.Count == 1,
                    $"a steep stair up through a 3 × 1 well over a main deck beam ({sd.Id}): blocked by the beam with the cells out, the beam's length comes out for it ({string.Join(", ", need.Select(n => l.Slots[n].Id))}); then its room is free and it's walked up ({top:0.00}) and down ({back:0.00})");
                done = true;
                break;
            }
            Check(done, "a lower deck stair through a well across a main deck beam was found and walked");

            // ---- a stair across her (along z) between two beams: no beam in its way
            bool across = false;
            foreach (var foot in ml.Floors.Where(m => m.Deck == "lower" && m.Room == "lowerdeck" && ((int)Math.Round(m.X) % 2 + 2) % 2 == 0).OrderBy(m => Math.Abs(m.X + 6) + Math.Abs(m.Z + 2)))
            {
                var sp = PiecePlanner.Plan(ml, foot, ShipStair0327(2), 3);     // climbing +z (away from a placer at -z)
                if (sp.Error != null || sp.Dir != 1) continue;
                var cells = new[] { -0.5, 0.5, 1.5 }.Select(dz => l.OnDeck(mainY, foot.X + 0.5, foot.Z + dz)).ToList();
                if (cells.Any(c => c < 0)) continue;
                var sta = new SlotStates(l);
                foreach (int c in cells) sta.Set(c, SlotState.Empty);
                var ga = md.Grid.Clone();
                sta.Apply(ga);
                if (PiecePlanner.GridClash(ml, sp, ga) != null) continue;
                var gs = ga.Clone();
                foreach (var b in sp.Solids) gs.Fill(b);
                bool walked = PiecePlanner.WalkFlight(gs, foot, sp.Dir, sp.Rise, out double top, out double back, 2);
                Check(walked && BeamRules.InTheWay(ml, l, sta, sp, ga, null) == null,
                    $"note 222: a steep stair across her on the lower deck ({foot.Id}, climbing to starboard) through a 3 × 1 well between two beams: no beam in its way, walked up ({top:0.00}) and down ({back:0.00})");
                across = true;
                break;
            }
            Check(across, "note 222: a stair across her between two beams fits");
        }

        static void Mending0327(ShipDefinition md)
        {
            // the lay job: planks blocks or boards (4 a block), building mode or not
            var dl = md.DamageParts;
            var st = new DamageState(dl.Count);
            var hl = md.Hull;
            int panel = Enumerable.Range(0, hl.Count).Where(n => hl.Slots[n].Sill < md.Waterline - 0.5 && hl.Slots[n].Side > 0 && hl.Slots[n].Band >= 2 && hl.Slots[n].Room != null)
                .OrderBy(n => Math.Abs(hl.Slots[n].Centre.X - 2)).First();
            st.Hurt[panel] = 1;
            var jobs = ShipRepairs.Needs(dl, st, panel, "game:plank-oak");
            var lay = jobs.FirstOrDefault(j => j.What == "lay");
            int blocks = Math.Max(1, hl.Slots[panel].Mat.Count);
            bool boards = lay != null && ShipRepairs.Plan(lay, new List<(string, int)> { ("game:plank-oak", 4 * blocks) }, out _, out _);
            bool fewBoards = lay != null && ShipRepairs.Plan(lay, new List<(string, int)> { ("game:plank-oak", 4 * blocks - 1) }, out _, out _);
            bool planks = lay != null && ShipRepairs.Plan(lay, new List<(string, int)> { ("game:planks-pine-ud", blocks) }, out _, out _);
            Check(lay != null && ShipRepairs.For(dl, st, panel, "game:plank-oak")?.What == "lay" && ShipRepairs.For(dl, st, panel, "game:planks-oak-ud")?.What == "lay" && boards && !fewBoards && planks &&
                  ShipRepairs.IsRepairItem("game:planks-oak-ud") && ShipRepairs.ItemKey(ShipRepairs.PlanksBlock) == "planksblock",
                $"a panel stove in is mended (laid again) by the repair tool too: {blocks} planks blocks or {4 * blocks} boards ({boards}; {4 * blocks - 1} boards not: {!fewBoards}); planks in hand count ({planks})");
            // the crosshair's pick of the opening: from inside her hold, from the water outside, and down from her deck at a bulwark's
            var g = md.Grid.Clone();
            var s = hl.Slots[panel];
            foreach (var b in s.SolidBoxes) g.Fill(b, false);
            // ((0.3.29) two layers: its ceiling out too, picked from inside, the planking from outside)
            int partner = s.PartnerIndex;
            if (partner >= 0) foreach (var b in hl.Slots[partner].SolidBoxes) g.Fill(b, false);
            var c = s.Centre;
            double hn = Math.Sqrt(s.Normal[0] * s.Normal[0] + s.Normal[2] * s.Normal[2]);
            double nx = s.Normal[0] / hn, nz = s.Normal[2] / hn;
            bool Pick(Vec3d o, double range, out double t)
            {
                var d = new Vec3d(c.X - o.X, c.Y - o.Y, c.Z - o.Z);
                double len = d.Length();
                d = new Vec3d(d.X / len, d.Y / len, d.Z / len);
                double wall = g.Raycast(o, d, range + 1);
                int n = hl.PickOpening(o, d, Math.Min(range, wall + 0.05), i => i == panel || i == partner, out t);
                return n == panel || n == partner && partner >= 0;
            }
            // (an eye in her hold: not in the deck over it)
            double eyeUp = new[] { 0.6, 0.3, 0.0, -0.3 }.First(u => !g.SolidAt(c.X - nx * 2.5, c.Y + u, c.Z - nz * 2.5) && !g.SolidAt(c.X - nx * 2.5, c.Y + u + 0.2, c.Z - nz * 2.5));
            bool inside = Pick(new Vec3d(c.X - nx * 2.5, c.Y + eyeUp, c.Z - nz * 2.5), 4.5, out double ti);
            bool outside = Pick(new Vec3d(c.X + nx * 2.5, c.Y + 0.3, c.Z + nz * 2.5), 4.5, out double to);
            // a panel over her main deck (a bulwark's): from her deck, looking out at it
            int high = Enumerable.Range(0, hl.Count).Where(n => hl.Slots[n].Sill > md.DeckY - 0.05 && hl.Slots[n].Room == null).OrderBy(n => hl.Slots[n].Sill).FirstOrDefault(-1);
            bool above = true;
            if (high >= 0)
            {
                panel = high;
                s = hl.Slots[high];
                g = md.Grid.Clone();
                foreach (var b in s.SolidBoxes) g.Fill(b, false);
                c = s.Centre;
                hn = Math.Sqrt(s.Normal[0] * s.Normal[0] + s.Normal[2] * s.Normal[2]);
                nx = s.Normal[0] / hn; nz = s.Normal[2] / hn;
                above = Pick(new Vec3d(c.X - nx * 2.0, c.Y + 0.4, c.Z - nz * 2.0), 4.5, out _);
            }
            Check(inside && outside && above, $"a hole is picked to be laid from inside her ({inside}, {ti:0.00} away), from the water outside ({outside}, {to:0.00}), and a bulwark's from her deck ({above})");
            Check(ShipRepairs.BoardsPerPlanks == 4, "boards: 4 to a planks block (vanilla's recipe)");
        }

        static void Laying0327()
        {
            var gun = new FixtureDef { Type = "gun", Code = "g", TrainLimits = new[] { -12f, 12f }, ElevationLimits = new[] { -5f, 12f } };
            // starting: the gun as it's laid, whatever the view is (looking down at it: no drop to -5)
            var lay = new GunLay(3, 2);
            var view0 = new GunLay(-20, -40);
            var off = GunLaying.AimOffset(lay, view0);
            var a0 = GunLaying.Relative(gun, view0, ref off);
            var a1 = GunLaying.Relative(gun, new GunLay(-15, -35), ref off);
            // past a limit and back: it comes off the limit at once
            var a2 = GunLaying.Relative(gun, new GunLay(-15, -10), ref off);
            var a3 = GunLaying.Relative(gun, new GunLay(-15, -12), ref off);
            Check(Math.Abs(a0.Train - 3) < 1e-4 && Math.Abs(a0.Elevation - 2) < 1e-4 && Math.Abs(a1.Train - 8) < 1e-4 && Math.Abs(a1.Elevation - 7) < 1e-4 &&
                  Math.Abs(a2.Elevation - 12) < 1e-4 && Math.Abs(a3.Elevation - 10) < 1e-4,
                $"gun laying is relative: started looking down at it, it stays laid ({a0}); the view moved 5° right and up lays it 5° ({a1}); up past its limit it stops there ({a2}) and comes back at once ({a3})");
        }

        static void StairBits0327(ShipDefinition md)
        {
            // note 215: a ship's stair's guards beside a walkable deck start over a man's height; note 200: balusters in the stringers
            var ml = md.Mounts;
            var foot = ml.Floors.Where(m => m.Deck == "quarter").OrderBy(m => Math.Abs(m.X + 7) + Math.Abs(m.Z - 0.5)).FirstOrDefault(m =>
                PiecePlanner.Plan(ml, m, ShipStair0327(2), 0).Error == null);
            if (foot != null)
            {
                var sp = PiecePlanner.Plan(ml, foot, ShipStair0327(2), 0);
                var guards = PiecePlanner.FlightGuards(ml, foot, sp.Dir, sp.Rise, 2);
                double lowest = guards.Count == 0 ? 99 : guards.Min(b => b[1]) - foot.Y;
                Check(guards.Count == 0 || lowest >= PiecePlanner.FlightHeadroom - 1e-6 || guards.Any(b => b[1] - foot.Y < 1.0),
                    $"note 215: beside a walkable deck a ship's stair's solid guards start over a man's height (the lowest {lowest:0.00} over its deck): whoever walks past it isn't stopped");
                Check(guards.All(b => b[1] - foot.Y >= PiecePlanner.FlightHeadroom - 1e-6 || !PiecePlanner.DeckBeside(md.Grid, new[] { (b[0] + b[3]) / 2, foot.Y, (b[2] + b[5]) / 2 })),
                    "note 215: no guard below a man's height where there's deck beside the stair");
            }
            var any = ml.Floors.First(m => m.Deck == "lower");
            var parts = ModularGeometry.FlightHandrails(any, 0, 3, 2);
            bool embedded = true;
            foreach (var p in parts)
            {
                var b = p.Box;
                if (b == null || b[4] - b[1] < 0.3 || b[3] - b[0] > 0.1) continue;      // the balusters (upright, thin)
                var (u, _) = PiecePlanner.StairUC(any, 0, b[0], b[2]);
                if (b[1] - any.Y > ModularGeometry.StringerTop(3, u, 2) - 0.01) embedded = false;
            }
            Check(embedded, "note 200: the stair's balusters reach down into its stringers");
        }

        static void Hooks0327(ShipDefinition md)
        {
            var ml = md.Mounts;
            var starts = (md.StartPieces ?? Array.Empty<StartPieceDef>()).Where(p => p.Mount?.StartsWith("h:") == true).ToList();
            var missing = starts.Where(p => ml.Get(p.Mount) == null).Select(p => p.Mount).ToList();
            Check(starts.Count >= 10 && missing.Count == 0 && starts.Count(p => p.Mount.StartsWith("h:c:")) >= 8,
                $"note 221: her lanterns hang on the mod's ceiling hooks ({starts.Count(p => p.Mount.StartsWith("h:c:"))} of {starts.Count}; missing {string.Join(", ", missing)})");
            // no deck cell fixed for a hook over it: the cells over the old hooks are cells now
            var l = md.Slots;
            bool cells = new[] { -5.5, 0.5 }.All(x => l.OnDeck(md.Decks.First(d => d.Code == "main").Y, x, 0.0) >= 0);
            Check(cells, "note 221: the main deck's cells over the lower deck's lanterns can be taken up (no fixed hook there)");
            // note 202: the mast hooks at the design's mast hook's height over their deck and its reach (where a ceiling hook
            // is already beside the mast, as under the forecastle and the poop, there's none of the mod's)
            int atDesign = 0;
            foreach (var mast in md.Masts)
            {
                var design = MountLayout.DesignMastHook(mast, ml.All);
                if (design == null) continue;
                var under = md.Decks.Where(dk => dk.Code != "top" && dk.Y < design.Y - 1.0 && mast.X >= dk.X0 && mast.X <= dk.X1).OrderByDescending(dk => dk.Y).First();
                double hgt = design.Y - under.Y;
                var mine = ml.All.Where(h => MountLayout.IsMastHook(h.Id) && h.Id.Contains(":" + mast.Code + ":")).ToList();
                atDesign += mine.Count(h => Math.Abs(h.Y - design.Y) < 1e-6);
                double dr = Math.Sqrt((design.X - mast.X) * (design.X - mast.X) + (design.Z - mast.Z) * (design.Z - mast.Z));
                Check(mine.Count >= 1 && mine.All(h => Math.Abs(h.Y - md.Decks.First(dk => dk.Code == h.Deck).Y - hgt) < 1e-6 &&
                                                        Math.Abs(Math.Sqrt((h.X - mast.X) * (h.X - mast.X) + (h.Z - mast.Z) * (h.Z - mast.Z)) - Math.Max(dr, mast.Radius + MountLayout.MastHookOut)) < 1e-3),
                    $"note 202: the {mast.Code}mast's hooks at its own hook's height over their deck ({hgt:0.###}) and its reach ({dr:0.###}): {string.Join(", ", mine.Select(h => h.Id + "@" + h.Y.ToString("0.###")))}");
            }
            Check(atDesign >= 2, $"note 202: the mainmast's other sides' hooks level with its own ({atDesign})");
        }

        static void Guns0327(ShipDefinition md)
        {
            var guns = md.Fixtures.Where(f => f.Type == "gun").ToList();
            var poop = guns.Where(f => f.Code.Contains("poop")).ToList();
            var portXs = poop.Where(f => f.Pos[2] < 0).Select(f => f.Pos[0]).OrderBy(x => x).ToList();
            // ((0.3.30, note 236) the poop's side ports gone altogether)
            if (portXs.Count == 0) Console.WriteLine("  --   note 203: the poop's side guns: none since 0.3.30 (note 236: \"remove there last side ports\")");
            else Check(portXs.Count == 3 && Math.Abs(portXs[1] - portXs[0] - 2) < 1e-3 && Math.Abs(portXs[2] - portXs[1] - 2) < 1e-3 && (md.GunPlaces ?? Array.Empty<GunPlaceDef>()).All(p => p.Id != "gun-port-poop-4"),
                $"note 203: the poop's guns three a side, 2 apart, each in a bay of its own ({string.Join(", ", portXs.Select(x => x.ToString("0.##")))}); the one by the transom gone");
            var bow = guns.Where(f => f.Code.Contains("forecastle-1")).ToList();
            foreach (var f in bow)
            {
                float mid = (f.TrainLimits[0] + f.TrainLimits[1]) / 2;
                var lid = md.MovingParts.FirstOrDefault(p => p.Code == f.Lid);
                // ((0.3.28, note 226) now as far as it's clear each way from square: still well toward the bow)
                Check(f.TrainLimits[0] < -5 && f.TrainLimits[1] > 14 && lid != null && Math.Abs(lid.AxisDir[2]) > 0.1,
                    $"notes 205, 206: {f.Code} trains {f.TrainLimits[0]:0.#}..{f.TrainLimits[1]:0.#}° (about her side's normal there, toward the bow), its lid hinged along her curving side ({(lid == null ? "-" : string.Join(", ", lid.AxisDir.Select(v => v.ToString("0.###"))))})");
            }
            var slide = md.MovingParts.Where(p => p.Kind == "slide" && guns.Any(g => g.Code == p.Code)).Select(p => p.Distance).Distinct().ToList();
            Check(slide.Count == 1 && slide[0] <= 0.8 + 1e-6, $"note 204: the guns run back {string.Join(", ", slide)} when fired and housed (1.1 before)");
        }

        static void Brow0327(ShipDefinition md)
        {
            var gate = md.Fixtures.FirstOrDefault(f => f.Type == "gate");
            if (gate == null) { Check(false, "note 219: her entry ports' gates"); return; }
            BrowGeometry.Port(gate, out var sill, out var inside, out int s);
            var end = new[] { sill[0], sill[1] - 1.0, sill[2] + s * 3.0 };
            var lad = BrowGeometry.Ladder(gate, end);
            Check(lad.Kind == "brow" && lad.Outside && lad.Gate == gate.Code && Math.Abs(lad.Top[1] - md.DeckY) < 0.01 && Math.Abs(lad.ExitTop[2]) < Math.Abs(lad.Top[2]) &&
                  Math.Abs(lad.Bottom[2] - end[2]) < 1e-9 && Math.Abs(lad.ExitBottom[2]) > Math.Abs(end[2]),
                $"note 219: a brow from {gate.Code}: from her deck at the port's sill ({string.Join(", ", lad.Top.Select(v => v.ToString("0.##")))}) to its end on the quay, walked as a way off her and on (a ladder of kind brow, through the gate)");
        }
    }
}
