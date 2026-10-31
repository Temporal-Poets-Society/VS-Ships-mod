using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.28) Chalk note 230 (carrack-m, 0.3.27): "i want to move companion way here. managed to place stairs but cannot
        /// enclose it with walls, roof and doors" â€” a steep stair down from the open waist through a well the player opened on
        /// the centreline beside the quarterdeck stair, enclosed by a companion hood: walls round the well (on the deck beside
        /// it: the twins of the places anchored on the well's own cells), a door at the stair's head swinging out onto the
        /// landing, a roof on the walls' tops; the stair walks under it. And the stern lower-deck stair that could be put where
        /// one steps off it onto her side's curve (refused now: "flight-no-landing"); every stair that's put walks.
        ///   dotnet run -c Debug -- notes0328hood
        /// </summary>
        static void Notes0328Hood(string root)
        {
            Console.WriteLine("0.3.28: note 230 (a companion hood over a stair well on the open waist), the stern lower-deck stair");
            var (md, _) = LoadHullDesign(root);
            Hood0328(md);
            SternStair0328(md);
        }

        static readonly FakePiece Planks0328 = new() { Code = "game:planks-oak-ud", Family = "planks", Planks = true };
        static readonly FakePiece Pane0328 = new() { Code = "game:glasspane-leaded-oak-ew", Family = "pane", Pane = true };
        static readonly FakePiece Door0328 = new() { Code = "game:door-solid-oak", Family = "door", Door = true };
        static readonly FakePiece Slab0328 = new() { Code = "game:plankslab-oak-down-free", Family = "slab", Slab = true };

        /// <summary>The server's checks of a planned piece that don't need the game (ShipDecor.PlaceProblem's): its places free and usable with her cells as they are, standing on what it needs, not into another piece, not over an opening, her collision.</summary>
        static string Problem0328(MountLayout ml, DeckSlotLayout l, SlotStates st, ShipPieceSet set, VoxelGrid grid, PiecePlan plan)
        {
            if (plan.Error != null) return plan.Error;
            foreach (string mid in plan.Mounts) if (set.Taken(mid)) return "ships:piece-taken";
            foreach (string mid in plan.Mounts) if (!DeckCells.MountOpen(ml.Get(mid), st)) return "ships:cell-empty-under";
            if (plan.Role is not (PieceRole.Stair or PieceRole.Ladder))
                foreach (string mid in plan.Mounts)
                {
                    var mm = ml.Get(mid);
                    if (mm?.Under != null && plan.Mounts.Contains(mm.Under)) continue;
                    if (!set.Supports(ml, mm)) return "ships:piece-no-support";
                }
            if (set.Blocking(plan) != null) return "ships:piece-taken";
            if (OpeningRules.OverOpening(l, n => !st.Filled(n), plan) is string open) return open;
            if (plan.Role == PieceRole.Railing && PiecePlanner.BesideRailed(ml, set.All, plan.Mount) != null) return "ships:railing-by-stair";
            if (plan.Role is PieceRole.Partition or PieceRole.Window or PieceRole.Railing or PieceRole.Shelf or PieceRole.Door) return null;
            if (PiecePlanner.GridClash(ml, plan, grid) is double[] clash)
                return plan.Role == PieceRole.Stair && StairLanding.At(plan, grid) ? "ships:flight-no-landing" : "ships:flight-no-well";
            return null;
        }

        /// <summary>The border place on a segment that's usable with her cells as they are (the first, or its twin across the line).</summary>
        static MountPoint UsableBorder0328(MountLayout ml, SlotStates st, bool alongX, double line, double along, double y, out MountPoint first)
        {
            first = ml.BorderAt(alongX, line, along, y);
            foreach (var m in new[] { first, ml.TwinOf(first) })
                if (m != null && DeckCells.MountOpen(m, st)) return m;
            return null;
        }

        static void Hood0328(ShipDefinition md)
        {
            var l = md.Slots;
            var ml = md.Mounts;
            double mainY = md.Decks.First(d => d.Code == "main").Y;
            var set = new ShipPieceSet();
            var st = new SlotStates(l);
            // her quarterdeck stair as the design lays it (beside the well, to starboard)
            var qe = md.ModularPieces.First(e => e.Code == "stair-quarterdeck");
            var qplan = PiecePlanner.Plan(ml, ml.ForModular(qe), ShipStair0327(2), (MountLayout.DirOf(qe.Up) + 2) % 4);
            Check(qplan.Error == null, $"the quarterdeck stair laid ({qplan.Error ?? "ok"})");
            if (qplan.Error != null) return;
            set.Add(ShipPiece.From(qplan, set.NextId, null));

            // ---- the user's well: the waist's centreline cells x -3..0 (the last under the quarterdeck's front), the beam at x -2 out
            var well = new[] { -2.5, -1.5, -0.5 }.Select(x => l.OnDeck(mainY, x, 0)).ToList();
            Check(well.All(c => c >= 0), $"the well's cells are deck cells ({string.Join(", ", well.Select(c => c < 0 ? "-" : l.Slots[c].Id))})");
            if (well.Any(c => c < 0)) return;
            foreach (int c in well) st.Set(c, SlotState.Empty);
            var foot = ml.Floors.First(m => m.Deck == "lower" && Math.Abs(m.X + 2) < 1e-6 && Math.Abs(m.Z + 0.5) < 1e-6);
            var sp = PiecePlanner.Plan(ml, foot, ShipStair0327(2), 2);      // climbing +x, its head at x 0
            var g0 = md.Grid.Clone();
            st.Apply(g0);
            var need = BeamRules.InTheWay(ml, l, st, sp, g0, null) ?? new List<int>();
            foreach (int n in need) st.Set(n, SlotState.Empty);
            var gw = md.Grid.Clone();
            st.Apply(gw);
            string stairErr = Problem0328(ml, l, st, set, gw, sp);
            Check(sp.Error == null && sp.Dir == 0 && stairErr == null,
                $"note 230: a steep stair from the lower deck ({foot.Id}) up through the well to the waist at x 0 ({stairErr ?? "ok"}; the beam {string.Join(", ", need.Select(n => l.Slots[n].Id))} out with it)");
            if (stairErr != null) return;
            var stair = ShipPiece.From(sp, set.NextId, null);
            set.Add(stair);

            // ---- the walls: both sides of the well (x -3..0), its aft end (x -3), two rows (a window in one); a door at its head (x 0)
            var walls = new List<(bool AlongX, double Line, double Along, string What)>();
            foreach (double x in new[] { -2.5, -1.5, -0.5 })
            {
                walls.Add((true, -0.5, x, "port side"));
                walls.Add((true, 0.5, x, "starboard side (the quarterdeck stair's)"));
            }
            walls.Add((false, -3.0, 0.0, "aft end"));
            int twins = 0, firsts = 0;
            var refusedBefore = new List<string>();
            var placed = new List<ShipPiece>();
            var whyNot = new List<string>();
            foreach (var (ax, line, along, what) in walls)
                for (int row = 0; row < 2; row++)
                {
                    var m = UsableBorder0328(ml, st, ax, line, along, mainY + row, out var first);
                    if (first != null && !DeckCells.MountOpen(first, st)) refusedBefore.Add(first.Id);
                    if (m == null) { whyNot.Add($"{what} at {along} row {row}: no usable place ({first?.Id ?? "none"})"); continue; }
                    if (m.TwinSide) twins++; else firsts++;
                    var plan = PiecePlanner.Plan(ml, m, row == 1 && what == "port side" && along == -1.5 ? Pane0328 : Planks0328, m.In);
                    string err = Problem0328(ml, l, st, set, gw, plan);
                    // its solid layer on the deck beside the well, never in it
                    bool outside = plan.Solids.All(s => !well.Any(c => PiecePlanner.Overlap(s, new[] { l.Slots[c].Box[0], mainY, l.Slots[c].Box[2], l.Slots[c].Box[3], mainY + 2, l.Slots[c].Box[5] })));
                    if (err != null || !outside || m.Clip != null) { whyNot.Add($"{m.Id}: {err ?? (m.Clip != null ? "cut" : "solid in the well")}"); continue; }
                    var pc = ShipPiece.From(plan, set.NextId, null);
                    set.Add(pc);
                    placed.Add(pc);
                }
            Check(whyNot.Count == 0 && placed.Count == walls.Count * 2,
                $"note 230: walls round the well on both rows, a window among them ({placed.Count} of {walls.Count * 2}: {firsts} on the deck's own places, {twins} on their twins across the line; refused before 0.3.28 as anchored on the well's cells: {refusedBefore.Count}){(whyNot.Count > 0 ? ": " + string.Join("; ", whyNot) : "")}");
            // the door at the stair's head, swinging out onto the landing (forward)
            var dm = UsableBorder0328(ml, st, false, 0.0, 0.0, mainY, out var dFirst);
            var dplan = dm == null ? null : PiecePlanner.Plan(ml, dm, Door0328, dm.In);
            string derr = dplan == null ? "no place" : Problem0328(ml, l, st, set, gw, dplan);
            // ((0.3.29) the design's own well there now: the forward cell's place is the line's own, not a twin)
            Check(derr == null && dplan.Dir == 0 && DeckCells.MountOpen(dm, st),
                $"note 230: a door at the stair's head ({dm?.Id ?? dFirst?.Id}: {derr ?? "ok"}), swinging out onto the landing ({(dplan == null ? "-" : MountLayout.DirCode[dplan.Dir])}), both rows whole");
            ShipPiece door = null;
            if (derr == null) { door = ShipPiece.From(dplan, set.NextId, null); set.Add(door); }

            // ---- the roof: a hood's board over each of the well's cells on the walls' tops
            var roofs = new List<string>();
            int covered = 0;
            foreach (int c in well)
            {
                var d = l.Slots[c];
                var h = ml.Horizontals.FirstOrDefault(x => MountLayout.IsHood(x.Id) && x.Deck == "main" && Math.Abs(x.X - d.CX) < 1e-6 && Math.Abs(x.Z - d.CZ) < 1e-6);
                // (the cell under the quarterdeck's front is roofed by her: her deck or its beam within a block over the walls)
                if (h == null && md.Grid.Collides(d.Box[0] + 0.02, mainY + 2.02, d.Box[2] + 0.02, d.Box[3] - 0.02, mainY + 3, d.Box[5] - 0.02)) { covered++; continue; }
                if (h == null) { roofs.Add($"{d.Id}: no place"); continue; }
                var plan = PiecePlanner.Plan(ml, h, Slab0328, 1);
                string err = Problem0328(ml, l, st, set, gw, plan);
                bool onTop = Math.Abs(h.Box[1] - (mainY + MountLayout.HoodHeight)) < 1e-6 && plan.Solids.All(s => s[1] >= mainY + 2 - 1e-6);
                if (err != null || !onTop) { roofs.Add($"{h.Id}: {err ?? "not on the walls' tops"}"); continue; }
                set.Add(ShipPiece.From(plan, set.NextId, null));
            }
            Check(roofs.Count == 0, $"note 230: the hood's roof over the well's cells, plank slabs on the walls' tops 2 over the deck ({well.Count - covered} put, {covered} under the quarterdeck's front){(roofs.Count > 0 ? ": " + string.Join("; ", roofs) : "")}");

            // ---- it walks: her collision with every piece (the door open), the stair up and down under the roof
            var gp = gw.Clone();
            foreach (var p in set.All) if (p != door) foreach (var s in p.Solids) gp.Fill(s);
            bool walked = PiecePlanner.WalkFlight(gp, foot, sp.Dir, sp.Rise, out double top, out double back, 2);
            var again = PiecePlanner.Plan(ml, foot, ShipStair0327(2), 2);
            set.Remove(stair.Id);
            var gq = gw.Clone();
            foreach (var p in set.All) if (p != door) foreach (var s in p.Solids) gq.Fill(s);
            string putAgain = Problem0328(ml, l, st, set, gq, again);
            set.Add(stair);
            Check(walked && putAgain == null,
                $"note 230: the stair walks up ({top:0.00}) and down ({back:0.00}) inside the hood, the door open; taken up it goes back in under the roof ({putAgain ?? "ok"})");
            // the quarterdeck stair beside it still walks (the wall's layer on its side: a man keeps to the stair's outer side)
            var qfoot = ml.ForModular(qe);
            bool qWalk = PiecePlanner.WalkFlight(gp, qfoot, qplan.Dir, qplan.Rise, out double qTop, out double qBack, 2);
            var shifted = new MountPoint { Id = "shifted", Kind = MountKind.Floor, Deck = qfoot.Deck, X = qfoot.X, Y = qfoot.Y, Z = qfoot.Z + 0.1, I = qfoot.I, K = qfoot.K, Box = qfoot.Box };
            bool qWalkOff = PiecePlanner.WalkFlight(gp, shifted, qplan.Dir, qplan.Rise, out double qTop2, out double qBack2, 2);
            Console.WriteLine($"  --   the quarterdeck stair beside the hood: walked along its middle {qWalk} ({qTop:0.00}, {qBack:0.00}), 0.1 to starboard of it {qWalkOff} ({qTop2:0.00}, {qBack2:0.00})");
            Check(qWalkOff, "note 230: the quarterdeck stair beside the hood still walks (keeping 0.1 off the hood's wall)");

            // ---- the crosshair: the places are next to what it hits
            bool Free(MountPoint x) => !set.Taken(x.Id) && DeckCells.MountOpen(x, st);
            // a wall piece in hand, the deck by the well's port edge; by its starboard edge (the quarterdeck stair's foot cell)
            set.Remove(placed[0].Id);
            var c1 = AimPlace.Candidates(ml, set, new Vec3d(-2.5, mainY, -0.6), new double[] { 0, 1, 0 }, null, x => Free(x) && x.Kind == MountKind.Border, true).FirstOrDefault();
            set.Add(placed[0]);
            set.Remove(placed[6].Id);
            var c2 = AimPlace.Candidates(ml, set, new Vec3d(-1.5, mainY, 0.6), new double[] { 0, 1, 0 }, null, x => Free(x) && x.Kind == MountKind.Border, true).FirstOrDefault();
            set.Add(placed[6]);
            Check(c1?.Id == placed[0].Mounts[0] && c2?.Id == placed[6].Mounts[0],
                $"note 230: the crosshair on the deck by the well's edge offers the wall there: port {c1?.Id ?? "none"} ({placed[0].Mounts[0]}), starboard {c2?.Id ?? "none"} ({placed[6].Mounts[0]}, a twin)");
            // a slab in hand, a wall's upper half from inside the hood: the roof over the well
            var upper = placed.First(p => ml.Get(p.Mounts[0]).Row == 1 && Math.Abs(ml.Get(p.Mounts[0]).Z + 0.5) < 1e-6 && Math.Abs(ml.Get(p.Mounts[0]).X + 0.5) < 1e-6);
            var roofMid = set.All.First(p => p.Role == PieceRole.Shelf && Math.Abs(ml.Get(p.Mounts[0]).X + 0.5) < 1e-6);
            set.Remove(roofMid.Id);
            var c3 = AimPlace.Candidates(ml, set, new Vec3d(-0.5, mainY + 1.3, -0.5), new double[] { 0, 0, 1 }, upper, x => Free(x) && x.Kind == MountKind.Horizontal).FirstOrDefault();
            // and from outside it, the wall's outer face: still the roof over the well (the side walled in)
            var c4 = AimPlace.Candidates(ml, set, new Vec3d(-0.5, mainY + 1.3, -0.5), new double[] { 0, 0, -1 }, upper, x => Free(x) && x.Kind == MountKind.Horizontal).FirstOrDefault();
            set.Add(roofMid);
            // a roof board's underside: the next one past its nearest edge
            var roofAft = set.All.First(p => p.Role == PieceRole.Shelf && Math.Abs(ml.Get(p.Mounts[0]).X + 1.5) < 1e-6);
            set.Remove(roofAft.Id);
            var c5 = AimPlace.Candidates(ml, set, new Vec3d(-0.9, mainY + 2, 0), new double[] { 0, -1, 0 }, roofMid, x => Free(x) && x.Kind == MountKind.Horizontal).FirstOrDefault();
            set.Add(roofAft);
            Check(c3?.Id == roofMid.Mounts[0] && c4?.Id == roofMid.Mounts[0] && c5?.Id == roofAft.Mounts[0],
                $"note 230: a slab in hand, the crosshair on a wall's upper half offers the hood's roof over the well (from inside: {c3?.Id ?? "none"}, from outside: {c4?.Id ?? "none"}; {roofMid.Mounts[0]}); on a roof board's underside the next one ({c5?.Id ?? "none"})");

            // ---- elsewhere as before: no twin is usable while the deck's own place is
            var st0 = new SlotStates(l);
            bool exclusive = ml.Borders.Where(b => b.TwinSide).All(t => !(DeckCells.MountOpen(t, st0) && DeckCells.MountOpen(ml.TwinOf(t), st0)));
            int hoods = ml.Horizontals.Count(h => MountLayout.IsHood(h.Id));
            bool noHoodInRooms = ml.Horizontals.Where(h => MountLayout.IsHood(h.Id)).All(h => md.RoomAt(h.X, h.Y - 1, h.Z)?.Enclosed != true);
            Check(exclusive && hoods > 50 && noHoodInRooms && ml.Borders.Count(b => b.TwinSide) > 100,
                $"0.3.28: twins ({ml.Borders.Count(b => b.TwinSide)}) usable only while the deck's own place isn't; hoods' places on the open decks only ({hoods})");
            // the waist's top row is whole along the centreline forward of the quarterdeck (her low bulwark there cut it in half)
            // ((0.3.29) not round the companion's design well: its places are its own, from her layout with the well laid)
            var waistTop = ml.Borders.Where(b => b.Deck == "main" && b.Row == 1 && !b.TwinSide && b.X > -1.5 && b.X < 10 && Math.Abs(b.Z) < 1.6 && md.RoomAt(b.X, b.Y + 0.5, b.Z) == null &&
                                                 !(b.SlotUnder >= 0 && l.Slots[b.SlotUnder].StartsEmpty)).ToList();
            // ((24 October 2026, caravel note 70) with her respawn spot no longer kept clear two more by the plate, their clip
            // the whole block (her quarter-block layer beside the line touches the next cell): whole as drawn)
            bool Whole(MountPoint b) => b.Clip == null || b.Clip.Count == 1 && b.Clip[0][2] - b.Clip[0][0] > 1 - 1e-6 && b.Clip[0][3] - b.Clip[0][1] > 1 - 1e-6;
            Check(waistTop.Count > 10 && waistTop.All(b => Whole(b) || md.Grid.Collides(b.Box[0] + 0.02, b.Box[1] + 0.02, b.Box[2] + 0.02, b.Box[3] - 0.02, b.Box[4] - 0.02, b.Box[5] - 0.02)),
                $"0.3.28: the waist's top-row places by the centreline are whole where nothing of hers is in them ({waistTop.Count(Whole)} of {waistTop.Count}{string.Concat(waistTop.Where(b => !Whole(b) && !md.Grid.Collides(b.Box[0] + 0.02, b.Box[1] + 0.02, b.Box[2] + 0.02, b.Box[3] - 0.02, b.Box[4] - 0.02, b.Box[5] - 0.02)).Select(b => "; cut: " + b.Id))})");
        }

        static void SternStair0328(ShipDefinition md)
        {
            var ml = md.Mounts;
            // the stern's lower-deck stair by the mizzen (foot x -12, climbing forward), whose foot stepped one off onto her side's
            // curve 0.5 high (0.3.27: put, not walked off): refused now (no room to step off, or no headroom), or it walks
            var bad = new List<string>();
            var said = new List<string>();
            foreach (string id in new[] { "f:lower:-12:-4", "f:lower:-12:2" })
            {
                var foot = ml.Get(id);
                if (foot == null) { said.Add(id + ": no place"); continue; }
                var (err, walked, why) = TryStair0328(md, foot, 0, 2);
                said.Add($"{id}: {err ?? "put, " + (walked ? "walks" : "doesn't walk") + " (" + why + ")"}");
                if (err == null && !walked) bad.Add(id);
            }
            Check(bad.Count == 0, $"the stern's lower-deck stair by the mizzen (foot x -12, climbing forward): refused with a reason or walked ({string.Join("; ", said)})");
            // and the landing check alone (her side's curve where one steps off: as wide as a man, 0.6)
            var f12 = ml.Get("f:lower:-12:-4");
            if (f12 != null)
            {
                var sp12 = PiecePlanner.Plan(ml, f12, ShipStair0327(2), 2);
                var foot12 = sp12.Room[sp12.Room.Count - 2];
                Check(Math.Abs(foot12[3] - foot12[0] - 2 * DeckMover.HalfWidth) < 1e-6 && Math.Abs(foot12[5] - foot12[2] - 2 * DeckMover.HalfWidth) < 1e-6,
                    $"a stair's landings are checked as wide and long as a man (0.6 × 0.6; were 0.5): {string.Join(", ", foot12.Select(v => v.ToString("0.##")))}");
            }
            // every steep or long stair that can be put on her lower and main decks (through a well opened for it) walks
            int tried = 0, put = 0;
            var stuck = new List<string>();
            foreach (var foot in ml.Floors.Where(m => m.Deck is "lower" or "main").ToList())
                foreach (int cells in new[] { 2, 3 })
                    for (int up = 0; up < 4; up++)
                    {
                        var (err, walked, why) = TryStair0328(md, foot, up, cells);
                        if (err == "skip") continue;
                        tried++;
                        if (err != null) continue;
                        put++;
                        if (!walked) stuck.Add($"{foot.Id} up {MountLayout.DirCode[up]} {cells} long ({why})");
                    }
            Check(stuck.Count == 0 && put > 100,
                $"every ship's stair that can be put on her lower and main decks walks up and down ({put} put of {tried} tried){(stuck.Count > 0 ? ": " + stuck.Count + " don't: " + string.Join("; ", stuck.Take(8)) : "")}");
        }

        /// <summary>A ship's stair on a floor place climbing up (cells 2 or 3), the cells it passes in the deck above opened (and the beams in its way out): the server's verdict, and whether it walks.</summary>
        static (string Err, bool Walked, string Why) TryStair0328(ShipDefinition md, MountPoint foot, int up, int cells)
        {
            var l = md.Slots;
            var ml = md.Mounts;
            var sp = PiecePlanner.Plan(ml, foot, ShipStair0327(cells), (up + 2) % 4);
            if (sp.Error != null) return ("skip", false, sp.Error);
            double above = foot.Y + sp.Rise;
            var st = new SlotStates(l);
            // (the deck above over P, F, (M,) H, where it's cells; a castle's front needs none)
            for (int c = -1; c < cells; c++)
            {
                int n = l.OnDeck(above, foot.X + 0.5 + MountLayout.DX[up] * c, foot.Z + 0.5 + MountLayout.DZ[up] * c);
                if (n >= 0) st.Set(n, SlotState.Empty);
            }
            var g = md.Grid.Clone();
            st.Apply(g);
            var need = BeamRules.InTheWay(ml, l, st, sp, g, null);
            if (need != null) { foreach (int n in need) st.Set(n, SlotState.Empty); g = md.Grid.Clone(); st.Apply(g); }
            // ((25 October 2026, caravel note 99) as the ship asks it: its foot and top on her deck as it is (the planner asks it of
            // her decks laid)
            if (PiecePlanner.LandingMissing(sp, g)) return ("ships:stair-no-landing", false, null);
            var clash = PiecePlanner.GridClash(ml, sp, g);
            if (clash != null) return (StairLanding.At(sp, g) ? "ships:flight-no-landing" : "ships:flight-no-well", false, null);
            var gs = g.Clone();
            foreach (var b in sp.Solids) gs.Fill(b);
            bool walked = PiecePlanner.WalkFlight(gs, foot, sp.Dir, sp.Rise, out double top, out double back, cells);
            return (null, walked, $"up {top:0.00}, back {back:0.00}");
        }
    }
}
