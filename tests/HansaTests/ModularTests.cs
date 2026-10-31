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
        /// (0.3.16, notes 154–161; docs/modular-pieces.md) The modular pieces: border places on open decks, horizontal places
        /// (shelves, wells' tops), wall spots on partitions, ceiling hooks between beams, windows and railings on borders, the
        /// 2 × 1 stair (its treads, walked up with the deck mover), a railing raked beside it, the support rules (a top
        /// partition hangs; a shelf holds what's on it), and the design's modular entries finding their places.
        /// </summary>
        static void Modular(ShipDefinition def, string root)
        {
            Console.WriteLine("modular pieces (0.3.16)");
            var l = def.Mounts;
            var g = def.Grid;

            // ---- border places on open decks (notes 155, 157)
            var open = l.Borders.Where(b => b.Room.EndsWith("-deck")).ToList();
            Check(open.Count(b => b.Deck == "main") > 20 && open.Count(b => b.Deck == "quarter") > 5 && open.All(b => b.Row <= 1),
                $"border places on the open decks, two rows ({open.Count(b => b.Deck == "main")} on the main deck, {open.Count(b => b.Deck == "quarter")} on the quarterdeck, {open.Count} in all)");

            // ---- a window and a railing on a border; partitions' rules
            var pane = new FakePiece { Code = "game:glasspane-leaded-oak-ew", Family = "game:glasspane-leaded-oak", Pane = true };
            var fence = new FakePiece { Code = "game:woodenfence-oak-ew-free", Family = "game:woodenfence-oak", Fence = true };
            var planks = new FakePiece { Code = "game:planks-oak-ud", Family = "game:planks-oak", Planks = true };
            var slab = new FakePiece { Code = "game:plankslab-oak-down-free", Family = "game:plankslab-oak", Slab = true };
            var stairs = new FakePiece { Code = "game:plankstairs-oak-up-north-free", Family = "game:plankstairs-oak", Stairs = true };
            // (0.3.19) a vanilla ladder: the companion ladder-stair (plank stairs are a walked stair)
            var ladder = new FakePiece { Code = "game:ladder-wood-oak-north", Family = "game:ladder-wood-oak", Ladder = true };
            var lantern = new FakePiece { Code = "game:lantern-small-up", Family = "game:lantern-small", Orient = PieceOrient.Omni, Light = true, States = new[] { "up", "down" } };
            var whole = l.Borders.First(b => b.Clip == null && b.Row == 0 && b.Room == "lowerdeck" && l.BorderAbove(b) != null);
            var pw = PiecePlanner.Plan(l, whole, pane, whole.In);
            var pr = PiecePlanner.Plan(l, whole, fence, whole.In);
            var pp = PiecePlanner.Plan(l, whole, planks, whole.In);
            Check(pw.Error == null && pw.Role == PieceRole.Window && pr.Error == null && pr.Role == PieceRole.Railing &&
                  pw.Solids.Count == 1 && pw.Solids[0].SequenceEqual(pp.Solids[0]) && pr.Box.SequenceEqual(pp.Box),
                "a pane on a border is a window, a fence a railing: the partition's thickness, picked and solid as one");
            var floor = l.Floors.First(m => m.Room == "lowerdeck");
            Check(PiecePlanner.Plan(l, floor, pane, 1).Error == "ships:piece-needs-border" && PiecePlanner.Plan(l, whole, slab, 1).Error != null,
                "a pane or a fence goes nowhere but a border; a slab nowhere but a horizontal place");

            // (note 160) a top partition with nothing under it; the bottom one taken from under it
            var set = new ShipPieceSet();
            var up = l.BorderAbove(whole);
            Check(set.Supports(l, up), "a partition's upper row needs nothing under it (it hangs from the beam over it)");
            var low = ShipPiece.From(PiecePlanner.Plan(l, whole, planks, whole.In), 1, null);
            var top = ShipPiece.From(PiecePlanner.Plan(l, up, planks, up.In), 2, null);
            set.Add(low); set.Add(top);
            Check(set.StandingOn(l, low) == null, "the bottom partition can be taken up with the top one on (note 160)");

            // ---- wall spots on a partition (note 159): both faces, a 2 × 1 painting across two partitions on a line
            var spots = l.Walls.Where(w => w.Under != null && l.Get(w.Under)?.Kind == MountKind.Border).ToList();
            var spot = spots.FirstOrDefault(w => w.Under == whole.Id);
            Check(spots.Count > 100 && spots.Count(w => w.Under == whole.Id) == 2, $"wall spots on placed partitions, both faces ({spots.Count}; {spots.Count(w => w.Under == whole.Id)} on {whole.Id})");
            if (spot != null)
            {
                var set2 = new ShipPieceSet();
                Check(!set2.Supports(l, spot), "a partition's wall spot is a place only while the partition stands");
                set2.Add(ShipPiece.From(PiecePlanner.Plan(l, whole, planks, whole.In), 1, null));
                Check(set2.Supports(l, spot), "with the partition up, its face takes a painting, a lantern, a shelf");
                var painting = new FakePiece
                {
                    Code = "game:painting-trout-north", Family = "game:painting-trout", Orient = PieceOrient.Attachable, States = Sides, Col = null,
                    Sel = new[] { 0, 0, 0, 1, 1, 0.0625 }
                };
                var pl = PiecePlanner.Plan(l, spot, painting, spot.In);
                var (line, alongX) = MountLayout.BorderLine(whole);
                double face = alongX ? spot.Z : spot.X;
                Check(pl.Error == null && Math.Abs(Math.Abs(face - line) - MountLayout.PartitionThickness / 2) < 1e-9 &&
                      pl.Box != null && (alongX ? Math.Min(Math.Abs(pl.Box[2] - face), Math.Abs(pl.Box[5] - face)) : Math.Min(Math.Abs(pl.Box[0] - face), Math.Abs(pl.Box[3] - face))) < 1e-6,
                    $"a painting hangs flush on the partition's face ({spot.Id}, {pl.Error})");
                Check(set2.StandingOn(l, set2.All.First()) == null, "nothing on it yet: the partition comes down");
            }

            // ---- ceiling hooks between the beams (note 161)
            var ceil = l.Hooks.Where(h => MountLayout.IsCeilingHook(h.Id)).ToList();
            bool underDeck = ceil.All(h => g.SolidAt(h.X, h.Y + 0.1, h.Z) && !g.SolidAt(h.X, h.Y - 0.1, h.Z));
            Check(ceil.Count(h => h.Room == "lowerdeck") > 20 && ceil.Count(h => h.Room == "hold") > 10 && underDeck,
                $"lantern hooks under the deck between the beams in every room ({ceil.Count(h => h.Room == "lowerdeck")} in the lower deck, {ceil.Count(h => h.Room == "hold")} in the hold, {ceil.Count} in all), each right under her planking");
            if (ceil.Count > 0)
            {
                var ph = PiecePlanner.Plan(l, ceil[0], lantern, 1);
                Check(ph.Error == null && ph.Hang == "down" && ph.Solids.Count == 0, "a lantern hangs on one (not solid)");
            }

            // ---- horizontal places (note 159): shelves in the rooms, a panel's plan; standing on it
            var shelves = l.Horizontals.ToList();
            Check(shelves.Count(h => h.Level <= 2) > 50, $"horizontal panel places: {shelves.Count(h => h.Level == 1)} a block up, {shelves.Count(h => h.Level == 2)} two, {shelves.Count(h => h.Level == 3)} wells' tops");
            var shelfPlace = shelves.FirstOrDefault(h => h.Level == 1 && l.FloorAt(h.Deck, h.I, h.K, 1) != null);
            if (shelfPlace != null)
            {
                var ps = PiecePlanner.Plan(l, shelfPlace, slab, 1);
                Check(ps.Error == null && ps.Role == PieceRole.Shelf && Math.Abs(ps.Box[4] - shelfPlace.Y) < 1e-9 && Math.Abs(ps.Box[1] - (shelfPlace.Y - MountLayout.ShelfThickness)) < 1e-9,
                    "a plank slab there is a panel 1/8 thick under its plane");
                var set3 = new ShipPieceSet();
                var over = l.FloorAt(shelfPlace.Deck, shelfPlace.I, shelfPlace.K, 1);
                Check(!set3.Supports(l, over), "the place a block up over an empty floor holds nothing");
                var shelf = ShipPiece.From(ps, 1, null);
                set3.Add(shelf);
                var chest = new FakePiece { Code = "game:chest-east", Family = "game:chest", Container = true, TurnMesh = true, Slots = 16, Sel = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 } };
                var onShelf = ShipPiece.From(PiecePlanner.Plan(l, over, chest, 1), 2, null);
                set3.Add(onShelf);
                Check(set3.Supports(l, over) && set3.StandingOn(l, shelf)?.Id == 2, "on the shelf a chest stands; the shelf doesn't come down from under it");
            }

            // ---- (0.3.18, note 162) the companion ladder-stair: steep, climbed as a ladder through a 2-cell well
            {
                MountPoint foot = null;
                foreach (var m in l.Floors.Where(m => m.Deck == "lower" && m.Room == "lowerdeck").OrderBy(m => Math.Abs(m.X - 4) + Math.Abs(m.Z - 2.5)))
                    if (l.FloorAt("lower", m.I + 1, m.K) != null && l.FloorAt("lower", m.I - 1, m.K) != null && l.FloorAt("main", m.I + 2, m.K) != null) { foot = m; break; }
                Check(foot != null, "a lower deck cell with room for a ladder-stair up to the main deck");
                if (foot != null)
                {
                    var plan = PiecePlanner.Plan(l, foot, ladder, 2);   // (the player at its -x side: it climbs +x)
                    var mainDeck = def.Decks.First(d => d.Code == "main");
                    double angle = Math.Atan2(plan.Rise, PiecePlanner.LadderRun) * 180 / Math.PI;
                    Check(plan.Error == null && plan.Role == PieceRole.Ladder && plan.Dir == 0 && Math.Abs(plan.Rise - (mainDeck.Y - foot.Y)) < 1e-9 &&
                          plan.Mounts.Contains(foot.Id) && !plan.Mounts.Contains(l.FloorAt("lower", foot.I + 1, foot.K).Id) && plan.Solids.Count == PiecePlanner.LadderSegments &&
                          angle > 55 && angle < 70,
                        $"a ladder makes a companion ladder climbing away from the placer to the deck above (rise {plan.Rise}, {angle:0}°); the cell under its high end stays a place (note 158)");
                    // her collision with a 2-cell well cut over its two cells only (not the cell before its foot)
                    var gw = g.Clone();
                    gw.Fill(new[] { foot.X, foot.Y + 1.5, foot.Z, foot.X + 2, mainDeck.Y + 0.05, foot.Z + 1 }, false);
                    bool roomFree = plan.Room.All(r => !gw.Collides(r[0] + 0.03, r[1] + 0.03, r[2] + 0.03, r[3] - 0.03, r[4] - 0.03, r[5] - 0.03));
                    bool roomShut = plan.Room.Any(r => g.Collides(r[0] + 0.03, r[1] + 0.03, r[2] + 0.03, r[3] - 0.03, r[4] - 0.03, r[5] - 0.03));
                    Check(roomFree && roomShut, $"its climber's room is free through a 2 × 1 well ({roomFree}) and not without one ({roomShut})");
                    var lad = PiecePlanner.StairLadder(foot, plan.Dir, plan.Rise, "test");
                    bool climbed = DeckMover.ClimbStair(gw, lad, out double topY, out double backY, out bool head);
                    Check(climbed && head, $"climbed up it as a ladder onto the main deck ({topY:0.00} of {plan.Rise}) and down again ({backY:0.00}); the head clear of the well's edge all the way ({head})");
                    // the walk up its treads doesn't happen: walking at it from its foot cell grabs it
                    var mover = new DeckMover(foot.X + 0.4, foot.Y, foot.Z + 0.5) { OnGround = true };
                    var gl = gw.Clone();
                    foreach (var sb in plan.Solids) gl.Fill(sb);
                    for (int n = 0; n < 10; n++) mover.Step(gl, 0.05, 1, 0, 3.0, false, false, 1, new[] { lad }, 1);
                    Check(mover.Climbing == lad, "walking at it from its foot cell, one is on the ladder (nobody walks up its treads)");
                    // a chest under its high end, in the head cell
                    var chest = new FakePiece { Code = "game:chest-east", Family = "game:chest", Container = true, TurnMesh = true, Slots = 16, Sel = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 } };
                    var setS = new ShipPieceSet();
                    setS.Add(ShipPiece.From(plan, 1, null));
                    var headCell = l.FloorAt("lower", foot.I + 1, foot.K);
                    var underPlan = PiecePlanner.Plan(l, headCell, chest, 2);
                    Check(underPlan.Error == null && setS.Blocking(underPlan) == null, "a chest fits under its high end (note 158)");
                    // taken up and put back (note 162)
                    setS.Remove(1);
                    var again = PiecePlanner.Plan(l, foot, ladder, 2);
                    Check(again.Error == null && setS.Blocking(again) == null && again.Box.SequenceEqual(plan.Box), "taken up, it goes back (note 162)");
                    // (0.3.19, the user: "ladders look better without rails") no railing beside it, none raked to it
                    var side = l.Borders.FirstOrDefault(b => b.Deck == "lower" && b.Row == 0 && b.Clip == null && Math.Abs(b.Z - foot.Z) < 1e-6 && Math.Abs(b.X - (foot.X + 1.5)) < 1e-6);
                    if (side != null)
                    {
                        setS.Add(ShipPiece.From(again, 1, null));
                        var rail = ShipPiece.From(PiecePlanner.Plan(l, side, fence, side.In), 2, null);
                        var withRail = setS.All.Concat(new[] { rail }).ToList();
                        Check(PiecePlanner.BesideLadder(l, setS.All, side)?.Id == 1 && PiecePlanner.ApplyRakes(l, withRail).Count == 0 && rail.Rake == null,
                            "a railing along its side is refused (none beside a ladder), and none is raked to it");
                        setS.Remove(1);
                    }
                    var far = l.Borders.First(b => b.Deck == "lower" && b.Row == 0 && Math.Abs(b.X - foot.X) > 5);
                    Check(PiecePlanner.BesideLadder(l, new[] { ShipPiece.From(again, 1, null) }, far) == null, "a railing elsewhere is fine");
                    // its drawn parts: no two faces in one plane
                    var lp = ModularGeometry.Ladder(foot, plan.Dir, plan.Rise).Select(x => x.AsBox()).Where(x => x != null).ToList();
                    Check(ModularGeometry.Coplanar(lp).Count == 0, $"its treads, shoes, hooks and handrails' stanchions: no two faces in one plane ({lp.Count} boxes)");
                    // (0.3.19, the user: "rails to ladder anyway need fixing") its own handrails: on each side over its stringer,
                    // upright stanchions and a rail parallel to the ladder 0.9 over its line, clear of the treads
                    LadderRailGeometry(foot, plan.Dir, plan.Rise);
                }
            }

            // ---- (0.3.19, the user: "two versions: stairs and ladders; stairs can take more space") the walked stair: 1 × 3,
            // about 45°, walked up and down by the deck mover (no climbing), through a 3 × 1 well or up to a deck's edge
            {
                MountPoint foot = null;
                foreach (var m in l.Floors.Where(m => m.Deck == "lower" && m.Room == "lowerdeck").OrderBy(m => Math.Abs(m.X - 4) + Math.Abs(m.Z - 2.5)))
                    if (l.FloorAt("lower", m.I + 1, m.K) != null && l.FloorAt("lower", m.I + 2, m.K) != null && l.FloorAt("lower", m.I - 1, m.K) != null &&
                        l.FloorAt("main", m.I + 3, m.K) != null)
                    {
                        // (clear of her below the deck above, with a 3 × 1 well cut over it: nothing low before its foot)
                        var tp = PiecePlanner.Plan(l, m, stairs, 2);
                        var tg = g.Clone();
                        tg.Fill(new[] { m.X, m.Y + 1.5, m.Z, m.X + 3, m.Y + 3.05, m.Z + 1 }, false);
                        if (tp.Error == null && PiecePlanner.GridClash(l, tp, tg) == null) { foot = m; break; }
                    }
                Check(foot != null, "a lower deck cell with room for a walked stair (3 long) up to the main deck");
                if (foot != null)
                {
                    var plan = PiecePlanner.Plan(l, foot, stairs, 2);   // (the player at its -x side: it climbs +x)
                    var mainDeck = def.Decks.First(d => d.Code == "main");
                    double angle = Math.Atan2(plan.Rise, PiecePlanner.FlightRun) * 180 / Math.PI;
                    var mid = l.FloorAt("lower", foot.I + 1, foot.K);
                    var head = l.FloorAt("lower", foot.I + 2, foot.K);
                    int n = PiecePlanner.FlightTreads(plan.Rise);
                    double riser = plan.Rise / n;
                    Check(plan.Error == null && plan.Role == PieceRole.Stair && plan.Dir == 0 && Math.Abs(plan.Rise - (mainDeck.Y - foot.Y)) < 1e-9 &&
                          plan.Mounts.Contains(foot.Id) && !plan.Mounts.Contains(mid.Id) && !plan.Mounts.Contains(head.Id) && plan.Solids.Count == n &&
                          angle > 40 && angle < 50 && riser <= PiecePlanner.FlightMaxRiser + 1e-9 && riser <= DeckMover.StepHeight,
                        $"plank stairs make a walked stair 3 long climbing away from the placer to the deck above (rise {plan.Rise}, {angle:0}°, {n} treads, risers {riser:0.00}); its middle and head cells stay places");
                    // her collision with a 3 × 1 well over its three cells
                    var gw = g.Clone();
                    gw.Fill(new[] { foot.X, foot.Y + 1.5, foot.Z, foot.X + 3, mainDeck.Y + 0.05, foot.Z + 1 }, false);
                    var clashWell = PiecePlanner.GridClash(l, plan, gw);
                    var clashShut = PiecePlanner.GridClash(l, plan, g);
                    Check(clashWell == null && clashShut != null, $"its room (a man's height over every tread) is free through a 3 × 1 well ({clashWell == null}) and not without one ({clashShut != null}): PiecePlanner.GridClash{(clashWell != null ? $" (her at [{string.Join(", ", clashWell.Select(v => v.ToString("0.###")))}], foot {foot.X} {foot.Y} {foot.Z})" : "")}");
                    var gl = gw.Clone();
                    foreach (var sb in plan.Solids) gl.Fill(sb);
                    bool walked = PiecePlanner.WalkFlight(gl, foot, plan.Dir, plan.Rise, out double topY, out double backY);
                    Check(walked, $"walked up it onto the main deck ({topY:0.00} of {plan.Rise}) and down again ({backY:0.00}) with the deck mover");
                    // nobody climbs it: it isn't a ladder
                    Check(PiecePlanner.ApplyRakes(l, new List<ShipPiece>()).Count == 0 && plan.Role != PieceRole.Ladder, "it's walked, not climbed (no ladder of its own)");
                    // up to a deck's edge: the deck above may reach back over its head cell by one tread, not more
                    double ov = PiecePlanner.FlightOverhang(plan.Rise);
                    VoxelGrid Edge(double over)
                    {
                        var ge = g.Clone();
                        ge.Fill(new[] { foot.X, foot.Y + 1.5, foot.Z, foot.X + 3 - over, mainDeck.Y + 0.05, foot.Z + 1 }, false);
                        ge.Fill(new[] { foot.X + 3 - over, foot.Y + 1.5, foot.Z, foot.X + 3, mainDeck.Y - 0.25, foot.Z + 1 }, false);
                        ge.Fill(new[] { foot.X + 3 - over, mainDeck.Y - 0.25, foot.Z, foot.X + 3.01, mainDeck.Y, foot.Z + 1 });
                        return ge;
                    }
                    var ok = Edge(ov);
                    var tooFar = Edge(ov + 0.25);
                    var gle = ok.Clone();
                    foreach (var sb in plan.Solids) gle.Fill(sb);
                    bool walkedEdge = PiecePlanner.WalkFlight(gle, foot, plan.Dir, plan.Rise, out double topE, out double backE);
                    Check(PiecePlanner.GridClash(l, plan, ok) == null && PiecePlanner.GridClash(l, plan, tooFar) != null && walkedEdge,
                        $"up to a deck's edge (no well): the deck may reach back over its head cell {ov:0.##} (its top tread flush with it), not {ov + 0.25:0.##}; walked up ({topE:0.00}) and down ({backE:0.00})");
                    // a chest under its head
                    var chest = new FakePiece { Code = "game:chest-east", Family = "game:chest", Container = true, TurnMesh = true, Slots = 16, Sel = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 } };
                    var setF = new ShipPieceSet();
                    setF.Add(ShipPiece.From(plan, 1, null));
                    var underPlan = PiecePlanner.Plan(l, head, chest, 2);
                    Check(underPlan.Error == null && setF.Blocking(underPlan) == null, "a chest fits under its high end");
                    // taken up and put back
                    setF.Remove(1);
                    var again = PiecePlanner.Plan(l, foot, stairs, 2);
                    Check(again.Error == null && setF.Blocking(again) == null && again.Box.SequenceEqual(plan.Box), "taken up, it goes back");
                    // a railing along its side raked along it; flat elsewhere
                    var side = l.Borders.FirstOrDefault(b => b.Deck == "lower" && b.Row == 0 && b.Clip == null && Math.Abs(b.Z - foot.Z) < 1e-6 && Math.Abs(b.X - (foot.X + 2.5)) < 1e-6);
                    if (side != null)
                    {
                        var rake = PiecePlanner.StairRake(foot, plan.Dir, plan.Rise, side);
                        var sel = new List<double[]>(); var sol = new List<double[]>();
                        PiecePlanner.RailingBoxes(side, rake, sel, sol, out var rbox);
                        double nos = plan.Rise / n;
                        Check(rake != null && Math.Abs(rake[1] - (plan.Rise + nos)) < 1e-9 && Math.Abs(rake[0] - (plan.Rise * 2 / 3 + nos)) < 1e-9 && sel.Count == 4 && rbox[4] > side.Y + plan.Rise + 0.9,
                            $"a railing along its head cell is raked along its nosings ({rake?[0]:0.00} → {rake?[1]:0.00}), its handrail {PiecePlanner.RailHeight} over them");
                    }
                    // (0.3.19, the user: "weirdly placed" rails) the raked railings along one side, as drawn: every part in the
                    // side's strip (on the border line, within the stair's run), the rails parallel to the nosings (the handrail
                    // 0.9 over them), the posts upright, nothing through a tread
                    RakedRailGeometry(l, plan, foot, fence);
                    var far = l.Borders.First(b => b.Deck == "lower" && b.Row == 0 && Math.Abs(b.X - foot.X) > 5);
                    Check(PiecePlanner.StairRake(foot, plan.Dir, plan.Rise, far) == null, "a railing elsewhere stays flat");
                    var fp = ModularGeometry.Flight(foot, plan.Dir, plan.Rise).Select(x => x.AsBox()).Where(x => x != null).ToList();
                    Check(ModularGeometry.Coplanar(fp).Count == 0 && fp.Count == n, $"its treads: no two faces in one plane ({fp.Count} boxes)");
                }
            }

            // ---- the design's modular entries find their places
            {
                var b0 = open.First(b => b.Row == 0);
                var (line, alongX) = MountLayout.BorderLine(b0);
                var e = new ModularPieceDef { Kind = "railing", Along = alongX ? "x" : "z", At = alongX ? new[] { b0.X + 0.05, b0.Y, line } : new[] { line, b0.Y, b0.Z - 0.05 } };
                var hp = shelves.First();
                var es = new ModularPieceDef { Kind = "panel", At = new[] { hp.X, hp.Y, hp.Z } };
                var ef = new ModularPieceDef { Kind = "stair", At = new[] { floor.X + 0.5, floor.Y, floor.Z + 0.5 }, Up = "-z" };
                var el = new ModularPieceDef { Kind = "ladder", At = new[] { floor.X + 0.5, floor.Y, floor.Z + 0.5 }, Up = "-z" };
                Check(l.ForModular(e) == b0 && l.ForModular(es) == hp && l.ForModular(ef) == floor && l.ForModular(el) == floor && MountLayout.DirOf("-z") == 3 &&
                      ModularPieceDef.Matches("panel", PieceRole.Shelf) && !ModularPieceDef.Matches("window", PieceRole.Partition) &&
                      ModularPieceDef.Matches("stair", PieceRole.Stair) && ModularPieceDef.Matches("ladder", PieceRole.Ladder) && !ModularPieceDef.Matches("stair", PieceRole.Ladder),
                    "a modular entry finds its place by where it is (a border's line and foot, a cell's middle, a plane)");
                Check(e.Key != es.Key && new ModularPieceDef { Kind = "door", Code = "cabin-door" }.Key == "cabin-door", "each entry has its own key (remembered once laid)");
            }
            // (0.3.19, the charter's double launch) the engine's stop packet for one completed use calls the stop twice (the
            // replayed step ending the use, then the packet's own): one launch per use, none within the cooldown
            {
                const string key = "test-launch";
                LaunchGuard.Reset(key);
                bool a = LaunchGuard.TryLaunch(key, 1000, 2600, out _), b = LaunchGuard.TryLaunch(key, 1000, 2600, out string whyB);
                bool c = LaunchGuard.TryLaunch(key, 2700, 4300, out string whyC), d = LaunchGuard.TryLaunch(key, 5000, 6700, out _);
                LaunchGuard.Reset(key);
                Check(a && !b && whyB == null && !c && whyC != null && d,
                    $"a charter's launch: once per use (the use's second stop refused quietly: {!b}), not again within {LaunchGuard.CooldownMs} ms ({!c}), again after ({d})");
            }
            // (0.3.18) step A: placing by aim, windows, corners, the hearth and its flue
            StepA(def);
            foreach (string code in new[] { "carrack-m" })
            {
                string path = Path.Combine(root, "assets", "ships", "config", "ships", code + ".json");
                if (!File.Exists(path)) { Console.WriteLine($"         ({code}.json not there yet: its modular entries aren't checked)"); continue; }
                var md = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
                md.Init();
                ModularDesign(md);
            }
        }

        /// <summary>
        /// (22 October 2026, caravel note 38) Plank partitions on a room's free border places on its outline (the lines of its
        /// box's ends and sides: an open front a player closes), one side's of each segment (not the twins), none in another
        /// piece's way.
        /// </summary>
        static List<ShipPiece> OutlinePartitions(ShipDefinition md, ShipPieceSet set, RoomDef r)
        {
            var l = md.Mounts;
            var planks = new FakePiece { Code = "game:planks-oak-ud", Family = "game:planks-oak", Planks = true };
            var list = new List<ShipPiece>();
            var taken = new ShipPieceSet();
            foreach (var p in set.All) taken.Add(p);
            foreach (var b in l.Borders.Where(b => b.Room == r.Code && !b.TwinSide && !taken.Taken(b.Id) && (b.Twin == null || !taken.Taken(b.Twin))))
            {
                var (line, alongX) = MountLayout.BorderLine(b);
                bool onOutline = alongX ? Math.Abs(line - r.Min[2]) < 1e-6 || Math.Abs(line - r.Max[2]) < 1e-6
                                        : Math.Abs(line - r.Min[0]) < 1e-6 || Math.Abs(line - r.Max[0]) < 1e-6;
                if (!onOutline) continue;
                var plan = PiecePlanner.Plan(l, b, planks, 0);
                if (plan.Error != null || taken.Blocking(plan) != null) continue;
                var pc = ShipPiece.From(plan, taken.NextId, null);
                taken.Add(pc);
                list.Add(pc);
            }
            return list;
        }

        /// <summary>
        /// (0.3.17) A modular design's own checks (instead of the carracks' door-fixture ones): its entries find their places
        /// and are all laid (none in another's way); its laid doors (each a door, swinging where its entry says); each stair
        /// walked up and down in her collision with the pieces in it (doors open); its wells; its rooms walled by pieces
        /// sealed with the doors shut, and a door opened opens one.
        /// </summary>
        static void ModularDesign(ShipDefinition md)
        {
            Console.WriteLine($"modular design {md.Code}");
            var l = md.Mounts;
            // ((21 October 2026) any ship's size: the caravel's collision is some 16 000 voxels, a carrack's over 20 000)
            Check(md.Grid != null && md.Grid.CountSolid() > 5000 && md.Fixture("helm") != null, $"{md.Code}: her collision decoded, a helm");
            ModularEntries(md);
            // (0.3.17) the castle decks' front edges: a railing place on every cell's front (the binnacle's and the helm's
            // margins kept it from the quarterdeck's front by them: note 155)
            foreach (var (deck, dir) in new[] { ("quarter", 0), ("forecastle", 2), ("poop", 0) })
            {
                var edge = l.Floors.Where(m => m.Deck == deck && l.FloorAt(deck, m.I + MountLayout.DX[dir], m.K) == null &&
                                               md.Decks.First(d => d.Code == deck) is DeckDef dd && (dir == 0 ? m.X + 1 >= dd.X1 - 0.5 : m.X <= dd.X0 + 0.5)).ToList();
                var missing = edge.Where(m => !l.Borders.Any(b => b.Deck == deck && b.Row == 0 && Math.Abs(b.Z - (m.Z + 0.5)) < 1e-6 && Math.Abs(b.X - (dir == 0 ? m.X + 1 : m.X)) < 1e-6)).ToList();
                foreach (var m in missing)
                {
                    double lx = dir == 0 ? m.X + 1 : m.X;
                    Console.WriteLine($"         {deck}'s front at z {m.Z + 0.5}: no railing place ({l.ClearHitCore(lx - 0.01, m.Y, m.Z, lx + 0.01, m.Y + 1, m.Z + 1) ?? "her collision"})");
                }
                Check(edge.Count == 0 || missing.Count <= 1, $"{md.Code}: a railing place on the {deck} deck's front, every cell ({edge.Count - missing.Count} of {edge.Count})");
                if (Environment.GetEnvironmentVariable("HANSA_FRONTS") != null)
                {
                    var dd2 = md.Decks.First(d => d.Code == deck);
                    double fx = dir == 0 ? Math.Floor(dd2.X1 - md.BlockGrid.Frac(0)) + md.BlockGrid.Frac(0) - 1 : Math.Ceiling(dd2.X0 - md.BlockGrid.Frac(0)) + md.BlockGrid.Frac(0);
                    for (double z = -5.5; z <= 5.5; z += 1)
                    {
                        double z0 = z;
                        var f = l.Floors.FirstOrDefault(m => m.Deck == deck && Math.Abs(m.X - fx) < 1e-6 && Math.Abs(m.Z - z0) < 1e-6);
                        double lx2 = dir == 0 ? fx + 1 : fx;
                        var bp = l.Borders.FirstOrDefault(b => b.Deck == deck && b.Row == 0 && Math.Abs(b.Z - (z0 + 0.5)) < 1e-6 && Math.Abs(b.X - lx2) < 1e-6);
                        var vox = new List<string>();
                        for (double vx = fx + 0.125; vx < fx + 1; vx += 0.25) for (double vy = dd2.Y + 0.125; vy < dd2.Y + 1; vy += 0.25) for (double vz = z0 + 0.125; vz < z0 + 1; vz += 0.25)
                            if (md.Grid.SolidAt(vx, vy, vz)) vox.Add($"({vx:0.###},{vy:0.###},{vz:0.###})");
                        if (vox.Count > 0 && vox.Count < 40) Console.WriteLine("             solid: " + string.Join(" ", vox));
                        Console.WriteLine($"           {deck} cell x {fx} z {z0}: front place {bp?.Id ?? "none"}{(bp?.Clip != null ? " (cut)" : "")}; core {l.ClearHitCore(fx, dd2.Y, z0, fx + 1, dd2.Y + 1, z0 + 1) ?? "-"}; her {md.Grid.Collides(fx + 0.02, dd2.Y + 0.02, z0 + 0.02, fx + 0.98, dd2.Y + 0.98, z0 + 0.98)}, floor {md.Grid.Collides(fx + 0.02, dd2.Y - 0.2, z0 + 0.02, fx + 0.98, dd2.Y - 0.02, z0 + 0.98)}; {(f != null ? "floor place" : l.ClearHit(fx, dd2.Y, z0, fx + 1, dd2.Y + 1, z0 + 1) ?? (md.Grid.Collides(fx + 0.02, dd2.Y + 0.02, z0 + 0.02, fx + 0.98, dd2.Y + 0.98, z0 + 0.98) ? "her" : "?"))}");
                    }
                }
            }
            // laid as a new ship has them
            var set = new ShipPieceSet();
            var byEntry = new Dictionary<ModularPieceDef, ShipPiece>();
            var blocked = new List<string>();
            var besideStairs = new List<string>();
            // (laid in the server's order: the ladder-stairs, the hearth and oven, their flue, the rest)
            // (0.3.19: an older "stair" entry is laid as a ladder: MountLayout.LaidKind)
            static int Order(ModularPieceDef d) => d?.Kind switch { "stair" or "ladder" => 0, "hearth" or "oven" => 1, "flue" => 2, _ => 3 };
            foreach (var e in (md.ModularPieces ?? Array.Empty<ModularPieceDef>()).OrderBy(Order))
            {
                var m = l.ForModular(e);
                if (m == null) continue;
                string kind = l.LaidKind(e);
                if (kind != e.Kind) Console.WriteLine($"         {e.Key}: a \"{e.Kind}\" entry laid as a {kind} (the 0.3.18 contract: say \"{kind}\")");
                var plan = PiecePlanner.Plan(l, m, ModularFake(e, kind), ModularToward(e, kind, m));
                if (plan.Error != null) continue;
                if (plan.Mounts.Any(set.Taken) || set.Blocking(plan) is ShipPiece other) { blocked.Add(e.Key); continue; }
                // (0.3.19) the server refuses a railing beside a ladder (ShipDecor.PlaceProblem)
                if (plan.Role == PieceRole.Railing && PiecePlanner.BesideLadder(l, set.All, m) is ShipPiece byLadder) { blocked.Add($"{e.Key} (a railing beside the ladder {byLadder.Mount})"); continue; }
                // (0.3.23) nor beside a ship's stair (its own handrails): the server skips it with a warning; an older design's
                // railing entries there wait on the generator's update (docs/modular-pieces.md §10)
                if (plan.Role == PieceRole.Railing && PiecePlanner.BesideRailed(l, set.All, m) is ShipPiece byStair) { besideStairs.Add($"{e.Key} (beside the stair {byStair.Mount})"); continue; }
                // (the server's own check against her collision as it stands then: her design and the pieces laid so far, not walls)
                if (plan.Role is PieceRole.Ladder or PieceRole.Stair or PieceRole.Hearth or PieceRole.Oven)
                {
                    var pg = md.Grid.Clone();
                    foreach (var q in set.All) if (!q.WallLike && q.Role != PieceRole.Door) foreach (var b in q.Solids) pg.Fill(b);
                    if (PiecePlanner.GridClash(l, plan, pg) is double[] c) { blocked.Add($"{e.Key} (her collision at [{string.Join(", ", c.Select(v => v.ToString("0.###")))}])"); continue; }
                }
                var p = ShipPiece.From(plan, set.NextId, null);
                set.Add(p);
                byEntry[e] = p;
            }
            foreach (var b in blocked.Take(10)) Console.WriteLine("         in another's way: " + b);
            if (besideStairs.Count > 0)
                Console.WriteLine($"  --   {md.Code}: {besideStairs.Count} railing entries beside her ship's stairs skipped (they have their own handrails since 0.3.23; the generator drops them): {string.Join(", ", besideStairs.Take(6))}");
            // (railings beside stairs raked, as the server does whenever her pieces change)
            int raked = PiecePlanner.ApplyRakes(l, set.All).Count;
            Console.WriteLine($"         {raked} railings raked beside her stairs");
            // ((26 October 2026) an entry on a start piece's top, the caravel's oven on its table, is laid by the game after the
            // start pieces, which this offline set doesn't lay)
            int onTops = md.ModularPieces?.Count(x => x.Kind != "hatch" && l.ForModular(x) == null && OnStartPieceTop(md, l, x)) ?? 0;
            Check(blocked.Count == 0 && byEntry.Count + besideStairs.Count + onTops == (md.ModularPieces?.Count(x => x.Kind != "hatch") ?? 0), $"{md.Code}: all {byEntry.Count} modular pieces laid ({blocked.Count} in another's way; {onTops} on a start piece's top, laid after it; the hatches over her deck cells: DesignSlots)");
            var doors = byEntry.Where(kv => kv.Key.Kind == "door").ToList();
            // ((21 October 2026) a design with door entries: the caravel has none, carrack-m's cabins have theirs)
            bool hasDoors = md.ModularPieces?.Any(x => x.Kind == "door") == true;
            if (hasDoors) Check(doors.Count > 0 && doors.All(kv => kv.Value.Role == PieceRole.Door && kv.Value.Mounts.Length == 2 &&
                                                     (kv.Key.Into == null || kv.Value.Dir == MountLayout.DirOf(kv.Key.Into))),
                $"{md.Code}: her {doors.Count} doors laid, two rows each, swinging where their entries say ({string.Join(", ", doors.Select(kv => kv.Key.Key + " " + MountLayout.DirCode[kv.Value.Dir]))})");
            // her collision with the pieces in it (as her grid is built: doors open for walking, shut for sealing)
            VoxelGrid With(bool doorsShut)
            {
                var g = md.Grid.Clone();
                foreach (var p in set.All)
                    if (p.Role != PieceRole.Door || doorsShut && !p.Open)
                        foreach (var b in p.Solids) g.Fill(b);
                return g;
            }
            // (0.3.18, note 162) each ladder-stair climbed up and down (her collision with the pieces but the stairs' own
            // boxes and the railings by them: the climber's room is what's checked), its well and landings free, and
            // taken up and put back
            var walk = md.Grid.Clone();
            foreach (var p in set.All)
                if (p.Role != PieceRole.Door && !p.WallLike) foreach (var b in p.Solids) walk.Fill(b);
            foreach (var kv in byEntry.Where(kv => kv.Value.Role == PieceRole.Ladder))
            {
                var st = kv.Value;
                var foot = l.Get(st.Mount);
                var lad = PiecePlanner.StairLadder(foot, st.Dir, st.Rise, kv.Key.Key);
                bool ok = DeckMover.ClimbStair(walk, lad, out double topY, out double backY, out bool head);
                if (!head)
                    for (int k = 0; k <= 24; k++)
                    {
                        double tt = k / 24.0;
                        double px = lad.Bottom[0] + (lad.Top[0] - lad.Bottom[0]) * tt, py = lad.Bottom[1] + (lad.Top[1] - lad.Bottom[1]) * tt, pz = lad.Bottom[2] + (lad.Top[2] - lad.Bottom[2]) * tt;
                        if (!walk.Collides(px - 0.18, py + PiecePlanner.ClimberLow(tt), pz - 0.18, px + 0.18, py + DeckMover.Height - 0.02, pz + 0.18)) continue;
                        var vox = new List<string>();
                        for (int ci = walk.CellX(px - 0.18); ci <= walk.CellX(px + 0.18); ci++) for (int cj = walk.CellY(py + PiecePlanner.ClimberLow(tt)); cj <= walk.CellY(py + DeckMover.Height - 0.02); cj++) for (int ck = walk.CellZ(pz - 0.18); ck <= walk.CellZ(pz + 0.18); ck++)
                            if (walk.SolidAt(walk.OX + (ci + 0.5) * walk.V, walk.OY + (cj + 0.5) * walk.V, walk.OZ + (ck + 0.5) * walk.V))
                                vox.Add($"({walk.OX + (ci + 0.5) * walk.V:0.###},{walk.OY + (cj + 0.5) * walk.V:0.###},{walk.OZ + (ck + 0.5) * walk.V:0.###})");
                        Console.WriteLine($"         t {tt:0.00}: the climber at ({px:0.00}, {py:0.00}, {pz:0.00}) meets her at {string.Join(" ", vox.Take(6))}");
                        break;
                    }
                Check(ok && head, $"{md.Code}: {kv.Key.Key} climbed up ({topY:0.00} of {st.Rise}) and down again ({backY:0.00}), the head clear of the well's edge ({head})");
                set.Remove(st.Id);
                var fake = new FakePiece { Code = ModularPieceDef.DefaultBlock("ladder"), Family = "x", Rise = kv.Key.Rise, Ladder = true };
                var again = PiecePlanner.Plan(l, foot, fake, (MountLayout.DirOf(kv.Key.Up) + 2) % 4);
                var blocking = again.Error == null ? set.Blocking(again) : null;
                // (the server's own check, PiecePlanner.GridClash on her PlaceGrid: her design and the pieces not walls)
                var clash = again.Error == null ? PiecePlanner.GridClash(l, again, walk) : null;
                bool roomFree = again.Error == null && clash == null && again.Room.All(r => !walk.Collides(r[0] + 0.03, r[1] + 0.03, r[2] + 0.03, r[3] - 0.03, r[4] - 0.03, r[5] - 0.03));
                if (!roomFree && again.Error == null)
                    foreach (var r in again.Room.Where(r => walk.Collides(r[0] + 0.03, r[1] + 0.03, r[2] + 0.03, r[3] - 0.03, r[4] - 0.03, r[5] - 0.03)).Take(3))
                    {
                        var vox = new List<string>();
                        var gg = walk;
                        for (int ci = gg.CellX(r[0] + 0.03); ci <= gg.CellX(r[3] - 0.03); ci++) for (int cj = gg.CellY(r[1] + 0.03); cj <= gg.CellY(r[4] - 0.03); cj++) for (int ck = gg.CellZ(r[2] + 0.03); ck <= gg.CellZ(r[5] - 0.03); ck++)
                        {
                            double vx = gg.OX + (ci + 0.5) * gg.V, vy = gg.OY + (cj + 0.5) * gg.V, vz = gg.OZ + (ck + 0.5) * gg.V;
                            if (gg.SolidAt(vx, vy, vz)) vox.Add($"({vx:0.###},{vy:0.###},{vz:0.###}){(md.Grid.SolidAt(vx, vy, vz) ? "" : " piece")}");
                        }
                        Console.WriteLine($"         room [{string.Join(", ", r.Select(v => v.ToString("0.##")))}] blocked at {string.Join(" ", vox.Take(6))}");
                    }
                Check(again.Error == null && blocking == null && roomFree,
                    $"{md.Code}: {kv.Key.Key} taken up and put back (note 162: {again.Error ?? (blocking != null ? "in the way of piece " + blocking.Id + " " + blocking.Role : roomFree ? "ok" : "its climber's room or landings blocked: the 2-cell well")})");
                // (0.3.23, note 172) and by hand: aimed at its foot cell from before it, as the client and the server do it
                HandPutBack(md, l, set, walk, foot, st.Dir, fake, kv.Key.Key);
                set.Add(st);
            }
            // (0.3.19, the coordinator: "stepping sideways off the companion ladder's foot falls 3 blocks into the hold") beside
            // each ladder's two cells no open drop: where the cell beside has no deck, the ladder's guard rail closes the side
            // (her collision with all her pieces); and walked sideways off its foot both ways, nobody falls more than a block
            {
                var all = With(false);
                var open = new List<string>();
                int guarded = 0;
                foreach (var kv in byEntry.Where(kv => kv.Value.Role == PieceRole.Ladder))
                {
                    var foot = l.Get(kv.Value.Mount);
                    int dir = kv.Value.Dir;
                    foreach (int side in new[] { -1, 1 })
                    {
                        for (int c = 0; c < 2; c++)
                        {
                            var n = PiecePlanner.StairPoint(foot, dir, c + 0.5, 0, side);
                            if (Environment.GetEnvironmentVariable("HANSA_DROPS") != null)
                            {
                                var col = new List<string>();
                                for (double vy = n[1] + 0.125; vy > n[1] - 4; vy -= 0.25) if (md.Grid.SolidAt(n[0], vy, n[2])) col.Add(vy.ToString("0.##"));
                                Console.WriteLine($"         {kv.Key.Key} cell {c} side {side}: beside at ({n[0]:0.##}, {n[1]:0.##}, {n[2]:0.##}) floor {PiecePlanner.DeckBeside(md.Grid, n)}; solid under it at y {string.Join(" ", col.Take(6))}");
                            }
                            if (PiecePlanner.DeckBeside(md.Grid, n)) continue;
                            var strip = PiecePlanner.StairBox(foot, dir, c + 0.1, 0.1, side * 0.5, c + 0.9, 0.9, side * 0.75);
                            if (all.Collides(strip[0], strip[1], strip[2], strip[3], strip[4], strip[5])) guarded++;
                            else open.Add($"{kv.Key.Key}: cell {c} side {side}");
                        }
                        var a = PiecePlanner.StairPoint(foot, dir, 0.25, 0, 0);
                        var b = PiecePlanner.StairPoint(foot, dir, 0.25, 0, side);
                        // (from beside the ladder at its foot: the walk-off; his box is clear of the ladder there)
                        var mv = new DeckMover(a[0], a[1], a[2]) { OnGround = true };
                        mv.Unstick(all);
                        double low = mv.Y;
                        for (int k = 0; k < 40; k++) { mv.Step(all, 0.05, b[0] - a[0], b[2] - a[2], 3.0, false, false); low = Math.Min(low, mv.Y); }
                        if (low < foot.Y - 1.0) open.Add($"{kv.Key.Key}: walked off its foot to side {side}, fell to {low - foot.Y:0.00}");
                    }
                }
                foreach (var o in open.Take(6)) Console.WriteLine("         open drop: " + o);
                Check(open.Count == 0, $"{md.Code}: no open drop beside her ladders' feet and treads ({guarded} sides over a drop guarded by the ladder's own rail)");
            }
            // (0.3.19) each walked stair walked up and down (her collision with all her pieces in it, doors open: a railing
            // across its head would stop him), its room free (GridClash, as the server checks), taken up and put back
            var withAll = With(false);
            foreach (var kv in byEntry.Where(kv => kv.Value.Role == PieceRole.Stair))
            {
                var st = kv.Value;
                var foot = l.Get(st.Mount);
                bool ok = PiecePlanner.WalkFlight(withAll, foot, st.Dir, st.Rise, out double topY, out double backY, PiecePlanner.RunOf(st));
                string off = "";
                for (int sgn = -1; !ok && sgn <= 1; sgn += 2)
                {
                    // ((0.3.29, note 233) a wall on the line beside it (the companion's beside the quarterdeck stair) is a
                    // quarter block into its cell: a man walks it a hand off its middle)
                    double dz = (st.Dir % 2 == 0 ? 0.1 : 0) * sgn, dx = (st.Dir % 2 == 1 ? 0.1 : 0) * sgn;
                    var sh = new MountPoint { Id = "shifted", Kind = MountKind.Floor, Deck = foot.Deck, X = foot.X + dx, Y = foot.Y, Z = foot.Z + dz, I = foot.I, K = foot.K, Box = foot.Box };
                    if (PiecePlanner.WalkFlight(withAll, sh, st.Dir, st.Rise, out topY, out backY, PiecePlanner.RunOf(st))) { ok = true; off = $", 0.1 off its middle ({(sgn > 0 ? "+" : "-")}{(st.Dir % 2 == 0 ? "z" : "x")})"; }
                }
                if (!ok)
                {
                    // where the walk up stopped, and what of her or her pieces is in front of him there
                    var a = PiecePlanner.FlightFoot(foot, st.Dir);
                    var mv = new DeckMover(a[0], a[1], a[2]) { OnGround = true };
                    mv.Unstick(withAll);
                    for (int s2 = 0; s2 < 200; s2++) mv.Step(withAll, 0.05, MountLayout.DX[st.Dir], MountLayout.DZ[st.Dir], 3.0, false, false);
                    double fx = mv.X + MountLayout.DX[st.Dir] * 0.4, fz = mv.Z + MountLayout.DZ[st.Dir] * 0.4;
                    var vox = new List<string>();
                    for (double vy = mv.Y + 0.125; vy < mv.Y + 1.85; vy += 0.25)
                        for (double d = -0.25; d <= 0.25; d += 0.25)
                        {
                            double vx = fx + (st.Dir % 2 == 1 ? d : 0), vz = fz + (st.Dir % 2 == 0 ? d : 0);
                            if (withAll.SolidAt(vx, vy, vz)) vox.Add($"({vx:0.##},{vy:0.##},{vz:0.##}){(md.Grid.SolidAt(vx, vy, vz) ? "" : " piece " + string.Join("/", set.All.Where(p => p.Solids.Any(b => vx > b[0] && vx < b[3] && vy > b[1] && vy < b[4] && vz > b[2] && vz < b[5])).Select(p => p.Role + " " + p.Mount)))}");
                        }
                    Console.WriteLine($"         {kv.Key.Key}: from ({a[0]:0.##}, {a[1]:0.##}, {a[2]:0.##}) the walk up stopped at ({mv.X:0.00}, {mv.Y:0.00}, {mv.Z:0.00}); in front: {string.Join(" ", vox.Take(8))}");
                }
                Check(ok, $"{md.Code}: {kv.Key.Key} walked up ({topY:0.00} of {st.Rise}) and down again ({backY:0.00}){off}");
                set.Remove(st.Id);
                var again = PiecePlanner.Plan(l, foot, ModularFake(kv.Key, "stair"), (MountLayout.DirOf(kv.Key.Up) + 2) % 4);
                var blocking = again.Error == null ? set.Blocking(again) : null;
                var clash = again.Error == null ? PiecePlanner.GridClash(l, again, walk) : null;
                Check(again.Error == null && blocking == null && clash == null,
                    $"{md.Code}: {kv.Key.Key} taken up and put back ({again.Error ?? (blocking != null ? "in the way of piece " + blocking.Id + " " + blocking.Role : clash != null ? $"its room blocked by her at [{string.Join(", ", clash.Select(v => v.ToString("0.##")))}]" : "ok")})");
                // (0.3.23, note 170) and by hand: aimed at its foot cell from before it, as the client and the server do it
                HandPutBack(md, l, set, walk, foot, st.Dir, ModularFake(kv.Key, "stair"), kv.Key.Key);
                set.Add(st);
            }
            // (0.3.19) no railing beside a ladder; the railings beside her walked stairs raked
            int byLadders = set.All.Count(r => r.Role == PieceRole.Railing && PiecePlanner.BesideLadder(l, set.All, l.Get(r.Mount)) != null);
            int rakedByStairs = set.All.Count(r => r.Role == PieceRole.Railing && r.Rake != null);
            Check(byLadders == 0, $"{md.Code}: no railing beside her ladders ({byLadders}); {rakedByStairs} raked beside her walked stairs");
            // her wells: each over one of her stairs; open onto a closed room or not
            foreach (var w in md.Wells ?? Array.Empty<WellDef>())
            {
                var b = w.Box;
                bool overStair = byEntry.Any(kv => kv.Value.IsStair && kv.Value.Box != null &&
                                                    Math.Min(b[3], kv.Value.Box[3]) - Math.Max(b[0], kv.Value.Box[0]) > 0.5 && Math.Min(b[5], kv.Value.Box[5]) - Math.Max(b[2], kv.Value.Box[2]) > 0.5);
                var below = md.Rooms.FirstOrDefault(r => (b[0] + b[3]) / 2 > r.Min[0] && (b[0] + b[3]) / 2 < r.Max[0] && Math.Abs(b[1] - r.Max[1]) < 0.6);
                var leaks = below == null ? null : ShipCompartments.WellLeaks(md, below, set.All);
                Check(overStair, $"{md.Code}: well {w.Code} over one of her stairs, in {below?.Code ?? "no room"}'s ceiling ({(leaks?.Count > 0 ? "open to the deck above" : "onto a closed room")})");
            }
            // her rooms walled by pieces: sealed with the doors shut, and a door opened opens one
            // ((22 October 2026, caravel note 38) a room a new ship has open, the caravel's steerage under the quarterdeck, its
            // front for the players to close: sealed once partitions stand on the free border places of its outline)
            var shut = With(true);
            var closing = new List<ShipPiece>(set.All);
            foreach (var r in md.Rooms.Where(r => r.Pieces))
            {
                var f = l.Floors.FirstOrDefault(m => m.Room == r.Code && !set.Taken(m.Id));
                if (f == null) { Check(false, $"{md.Code}: a free floor place in {r.Code}"); continue; }
                string why = null;
                ShipCompartments.Why = w2 => why = w2;
                bool sealedNow = ShipCompartments.Sealed(l, shut, set.All, r, ShipCompartments.WellLeaks(md, r, set.All), f.X + 0.5, f.Y + 0.6, f.Z + 0.5);
                string front = "";
                if (!sealedNow)
                {
                    var walls = OutlinePartitions(md, set, r);
                    var grid = shut.Clone();
                    foreach (var p in walls) foreach (var s in p.Solids) grid.Fill(s);
                    var all = set.All.Concat(walls).ToList();
                    sealedNow = walls.Count > 0 && ShipCompartments.Sealed(l, grid, all, r, ShipCompartments.WellLeaks(md, r, all), f.X + 0.5, f.Y + 0.6, f.Z + 0.5);
                    front = $" once its outline's {walls.Count} free border places have partitions (open as a new ship has her)";
                    closing.AddRange(walls);
                }
                ShipCompartments.Why = null;
                Check(sealedNow, $"{md.Code}: {r.Code} (walled by pieces) sealed with her doors shut{front}{(sealedNow ? "" : ": open " + why)}");
            }
            if (closing.Count > set.All.Count())
                foreach (var p in closing.Skip(set.All.Count())) foreach (var s in p.Solids) shut.Fill(s);
            // (0.3.22, step C) her lidded gun ports in her rooms (the great cabin's guns): a lid seals its port while shut,
            // cut in her collision or not; a synthetic port cut in a room's side, the same
            LiddedPortRooms(md, shut, closing, r => l.Floors.FirstOrDefault(m => m.Room == r.Code && !set.Taken(m.Id)));
            var portRoom = md.Rooms.FirstOrDefault(r => r.Pieces && ShipCompartments.LiddedPorts(md).Any(f => ShipCompartments.PortRoom(md, f) == r))
                           ?? md.Rooms.FirstOrDefault(r => r.Pieces);
            if (portRoom != null) LiddedPortSeal(md, shut, closing, portRoom, md.Code, m => !set.Taken(m.Id));
            int opens = 0;
            foreach (var kv in doors)
            {
                kv.Value.Open = true;
                var g = With(true);
                foreach (var r in md.Rooms.Where(r => r.Pieces))
                {
                    var f = l.Floors.FirstOrDefault(m => m.Room == r.Code && !set.Taken(m.Id));
                    if (f != null && !ShipCompartments.Sealed(l, g, set.All, r, ShipCompartments.WellLeaks(md, r, set.All), f.X + 0.5, f.Y + 0.6, f.Z + 0.5)) { opens++; break; }
                }
                kv.Value.Open = false;
            }
            if (hasDoors) Check(opens > 0, $"{md.Code}: a door opened opens a room ({opens} of {doors.Count} doors do)");
            // (0.3.18, note 165) her hearth, oven and flue as pieces: one of each, the hearth's smoke out through its flue
            var hearths = byEntry.Where(kv => kv.Key.Kind == "hearth").Select(kv => kv.Value).ToList();
            var ovens = byEntry.Where(kv => kv.Key.Kind == "oven").Select(kv => kv.Value).ToList();
            var flues = byEntry.Where(kv => kv.Key.Kind == "flue").Select(kv => kv.Value).ToList();
            if (hearths.Count + ovens.Count + flues.Count > 0)
            {
                Check(hearths.Count <= 1 && ovens.Count <= 1 && (hearths.Count == 0 || md.Fixtures.All(f => f.Type != "hearth")) && (ovens.Count == 0 || md.Fixtures.All(f => f.Type != "oven")),
                    $"{md.Code}: one hearth ({hearths.Count}) and one oven ({ovens.Count}) as pieces, no fixed ones beside them");
                Check(flues.All(f => set.Supports(l, l.Get(f.Mount))), $"{md.Code}: each of her {flues.Count} flue segments stands on the hearth or the segment under it");
                // (0.3.24, note 192: a hearth burns without a flue; a laid flue, if any, still reaches open air)
                foreach (var h in flues.Count > 0 ? hearths : new List<ShipPiece>())
                {
                    bool flued = HearthFlue.FlueTop(l, set, md.Grid, h, out var top);
                    Check(flued, $"{md.Code}: her hearth ({h.Mount}) draws: its smoke out to open air {(top != null ? $"at y {top[1]:0.00}" : "")}");
                }
            }
            else Console.WriteLine($"         ({md.Code} has no hearth, oven or flue pieces yet: their checks wait for the generator)");
            // (0.3.18, notes 164, 166) her pieces' drawn parts: no two faces in one plane
            CoplanarReport(md, set);
        }

        /// <summary>(0.3.19) A box's middle along a stair's run (u from its foot cell's near edge) and across it (c from its middle).</summary>
        static (double u, double c) StairUC(MountPoint foot, int up, double x, double z)
        {
            bool alongX = up == 0 || up == 2;
            int sgn = up < 2 ? 1 : -1;
            double start = alongX ? (sgn > 0 ? foot.X : foot.X + 1) : (sgn > 0 ? foot.Z : foot.Z + 1);
            double mid = (alongX ? foot.Z : foot.X) + 0.5;
            return alongX ? ((x - start) * sgn, z - mid) : ((z - start) * sgn, x - mid);
        }

        /// <summary>
        /// (0.3.19) A walked stair's raked railings along one side, as the renderer draws them (their ends shared, raked by
        /// ApplyRakes): each part in the side's strip, rails parallel to the nosings, the handrail RailHeight over them,
        /// posts upright from about the nosings, nothing through a tread.
        /// </summary>
        static void RakedRailGeometry(MountLayout l, PiecePlan plan, MountPoint foot, PieceBlock fence)
        {
            var set = new ShipPieceSet();
            set.Add(ShipPiece.From(plan, 1, null));
            int id = 2;
            for (int c = 0; c < PiecePlanner.FlightCells; c++)
            {
                var b = l.Borders.FirstOrDefault(x => x.Deck == foot.Deck && x.Row == 0 && x.Clip == null && Math.Abs(x.Z - foot.Z) < 1e-6 && Math.Abs(x.X - (foot.X + 0.5 + c)) < 1e-6);
                if (b != null) set.Add(ShipPiece.From(PiecePlanner.Plan(l, b, fence, b.In), id++, null));
            }
            PiecePlanner.ApplyRakes(l, set.All);
            var rails = set.All.Where(p => p.Role == PieceRole.Railing).ToList();
            if (rails.Count < 2) { Console.WriteLine("         (no border places along the test stair's side: its railings' geometry not checked)"); return; }
            var treads = PiecePlanner.FlightBoxes(foot, plan.Dir, plan.Rise);
            double N(double u) => PiecePlanner.FlightNosing(plan.Rise, u);
            var bad = new List<string>();
            int beams = 0, posts = 0;
            foreach (var r in rails)
            {
                var m = l.Get(r.Mount);
                var (line, alongX) = MountLayout.BorderLine(m);
                double a0 = (alongX ? m.X : m.Z) - 0.5, a1 = a0 + 1;
                var e0 = ModularGeometry.RailingEnd(l, set.All, r, alongX ? a0 : line, alongX ? line : a0, out double s0);
                var e1 = ModularGeometry.RailingEnd(l, set.All, r, alongX ? a1 : line, alongX ? line : a1, out double s1);
                double lineC = StairUC(foot, plan.Dir, alongX ? m.X : line, alongX ? line : m.Z).c;
                foreach (var part in ModularGeometry.Railing(m, r.Rake, r.Box, e0, s0, e1, s1))
                {
                    if (part.IsBeam && part.AsBox() == null)
                    {
                        beams++;
                        var (u0, c0) = StairUC(foot, plan.Dir, part.P0[0], part.P0[2]);
                        var (u1, c1) = StairUC(foot, plan.Dir, part.P1[0], part.P1[2]);
                        double o0 = part.P0[1] - foot.Y - N(u0), o1 = part.P1[1] - foot.Y - N(u1);
                        if (Math.Abs(c0 - lineC) > 1e-6 || Math.Abs(c1 - lineC) > 1e-6 || part.W / 2 > 0.07) bad.Add($"a rail off the side's line (c {c0:0.000}, line {lineC:0.000})");
                        if (Math.Abs(o0 - o1) > 1e-6) bad.Add($"a rail not parallel to the nosings ({o0:0.000} / {o1:0.000} over them)");
                        if (Math.Min(u0, u1) < -0.07 || Math.Max(u0, u1) > PiecePlanner.FlightRun + 0.07) bad.Add($"a rail past the stair's run ({u0:0.00}..{u1:0.00})");
                        if (o0 > 0.5 && Math.Abs(o0 + part.H / 2 - (PiecePlanner.RailHeight + 0.0)) > 0.06) bad.Add($"the handrail {o0 + part.H / 2:0.00} over the nosings, not {PiecePlanner.RailHeight}");
                        continue;
                    }
                    var bx = part.AsBox();
                    posts++;
                    var (u, cc) = StairUC(foot, plan.Dir, (bx[0] + bx[3]) / 2, (bx[2] + bx[5]) / 2);
                    double halfAcross = (alongX ? bx[5] - bx[2] : bx[3] - bx[0]) / 2, halfAlong = (alongX ? bx[3] - bx[0] : bx[5] - bx[2]) / 2;
                    if (Math.Abs(cc - lineC) + halfAcross > 0.07) bad.Add($"a post or baluster off the side's strip ({cc:0.000} ± {halfAcross:0.000})");
                    if (u - halfAlong < -0.07 || u + halfAlong > PiecePlanner.FlightRun + 0.07) bad.Add($"a post past the stair's run (u {u:0.00})");
                    double lo = bx[1] - foot.Y, hi = bx[4] - foot.Y;
                    if (lo < N(u + halfAlong) - 0.45 || hi > N(u + halfAlong) + PiecePlanner.RailHeight + 0.02) bad.Add($"a post at u {u:0.00} from {lo:0.00} to {hi:0.00}, the nosings at {N(u):0.00}");
                    foreach (var t in treads)
                        if (PiecePlanner.Overlap(bx, t, 0.001)) { bad.Add($"a post through a tread at u {u:0.00}"); break; }
                }
            }
            foreach (var b in bad.Distinct().Take(6)) Console.WriteLine("         " + b);
            Check(bad.Count == 0 && beams >= 2 * rails.Count && posts > rails.Count,
                $"its raked railings ({rails.Count} along one side): every part on the side's line within its run, the rails parallel to the nosings (the handrail {PiecePlanner.RailHeight} over them), the posts upright, none through a tread ({beams} rails, {posts} posts and balusters)");
        }

        /// <summary>(0.3.19) A ladder-stair's own handrails: on its stringers' lines, the rail parallel to the ladder 0.9 over its line, the stanchions upright on the stringers, clear of the treads.</summary>
        static void LadderRailGeometry(MountPoint foot, int up, double rise)
        {
            double cs = 0.5 - PiecePlanner.StairInset - 0.0625 / 2;
            var parts = ModularGeometry.LadderHandrails(foot, up, rise);
            var treads = ModularGeometry.Ladder(foot, up, rise).Where(p => p.Mat == PartMat.Planks).Select(p => p.AsBox()).ToList();
            var bad = new List<string>();
            int rails = 0, stanchions = 0;
            foreach (var p in parts)
            {
                if (p.IsBeam && p.AsBox() == null)
                {
                    rails++;
                    var (u0, c0) = StairUC(foot, up, p.P0[0], p.P0[2]);
                    var (u1, c1) = StairUC(foot, up, p.P1[0], p.P1[2]);
                    double o0 = p.P0[1] - foot.Y - PiecePlanner.LadderHeight(rise, u0), o1 = p.P1[1] - foot.Y - PiecePlanner.LadderHeight(rise, u1);
                    if (Math.Abs(Math.Abs(c0) - cs) > 1e-6 || Math.Abs(c1 - c0) > 1e-6) bad.Add($"a handrail off the stringer's line ({c0:0.000})");
                    if (Math.Abs(o0 - ModularGeometry.LadderRailHeight) > 1e-6 || Math.Abs(o1 - ModularGeometry.LadderRailHeight) > 1e-6) bad.Add($"a handrail {o0:0.00}/{o1:0.00} over the ladder's line");
                    continue;
                }
                stanchions++;
                var b = p.AsBox();
                var (u, c) = StairUC(foot, up, (b[0] + b[3]) / 2, (b[2] + b[5]) / 2);
                if (Math.Abs(Math.Abs(c) - cs) > 1e-6) bad.Add($"a stanchion off the stringer ({c:0.000})");
                double lo = b[1] - foot.Y - PiecePlanner.LadderHeight(rise, u);
                if (lo < 0.03 || lo > 0.08) bad.Add($"a stanchion's foot {lo:0.00} over the ladder's line (not on the stringer)");
                foreach (var t in treads)
                    if (t != null && PiecePlanner.Overlap(b, t, 0.001)) { bad.Add($"a stanchion through a tread at u {u:0.00}"); break; }
            }
            foreach (var x in bad.Distinct().Take(6)) Console.WriteLine("         " + x);
            Check(bad.Count == 0 && rails == 2 && stanchions == 6,
                $"its own handrails: {rails} rails on its stringers' lines, {ModularGeometry.LadderRailHeight} over the ladder and parallel to it, {stanchions} stanchions upright on the stringers, none through a tread");
        }

        /// <summary>(0.3.19) The block a modular entry is laid with, as the server reads it (in building mode), for the kind it's laid as.</summary>
        /// <summary>((25 October 2026, caravel note 105) An entry standing on a start piece's top, the galley's oven on its
        /// table: its place is the stacked one the game makes once it lays the start pieces, which it does first.</summary>
        static bool OnStartPieceTop(ShipDefinition md, MountLayout l, ModularPieceDef e) =>
            e.At?.Length >= 3 && md.StartPieces != null && md.StartPieces.Any(sp =>
                sp.Mount != null && sp.Mount.StartsWith("f:") && l.Get(sp.Mount) is MountPoint f &&
                e.At[0] > f.X && e.At[0] < f.X + 1 && e.At[2] > f.Z && e.At[2] < f.Z + 1 && Math.Abs(e.At[1] - (f.Y + 1)) < 0.05);

        static FakePiece ModularFake(ModularPieceDef e, string kind) => new FakePiece
        {
            Code = kind != e.Kind ? ModularPieceDef.DefaultBlock(kind) : e.Block ?? ModularPieceDef.DefaultBlock(kind), Family = "x", Rise = e.Rise, DoorInto = e.Into != null ? MountLayout.DirOf(e.Into) : -1,
            Planks = kind == "partition", Pane = kind == "window", Fence = kind == "railing", Stairs = kind == "stair", Ladder = kind == "ladder", Slab = kind == "panel", Door = kind == "door",
            Hearth = kind == "hearth", Oven = kind == "oven", Flue = kind == "flue",
            Sel = kind=="door" ? new double[]{0,0,0,1,2,.1875} : new double[]{0,0,0,1,1,1},
            // (0.3.23) a stair entry is a ship's stair of its length, with its own handrails
            // ((0.1.0-dev.6, note 36) length 1: the short one)
            StairCells = kind == "stair" && e.Length is PiecePlanner.ShortCells or PiecePlanner.SteepCells ? e.Length : PiecePlanner.FlightCells, StairRails = kind == "stair"
        };

        /// <summary>(0.3.19) The way a modular entry's piece is put toward (as ShipDecor.PlaceModular): a stair's or ladder's away from its "up", a hearth's front, a border's anchor.</summary>
        static int ModularToward(ModularPieceDef e, string kind, MountPoint m) =>
            kind is "stair" or "ladder" ? (MountLayout.DirOf(e.Up) + 2) % 4 : kind is "hearth" or "oven" ? MountLayout.DirOf(e.Facing) : m.In;

        /// <summary>(0.3.16) A design's modular entries: each finds its place and plans there as the kind it says (the ones that don't are listed).</summary>
        static void ModularEntries(ShipDefinition md)
        {
            var l = md.Mounts;
            var bad = new List<string>();
            int n = 0;
            foreach (var e in md.ModularPieces ?? Array.Empty<ModularPieceDef>())
            {
                // ((0.3.20) a hatch lies over deck cells, not on a place: DesignSlots checks it)
                if (e.Kind == "hatch") continue;
                n++;
                var m = l.ForModular(e);
                if (m == null && OnStartPieceTop(md, l, e))
                {
                    Console.WriteLine($"         {e.Key}: on a start piece's top (laid after it)");
                    continue;
                }
                if (m == null)
                {
                    // (a floor kind: why her cell there isn't a floor place)
                    string why = "";
                    if (e.Kind is "stair" or "ladder" or "hearth" or "oven" && e.At?.Length >= 3)
                    {
                        double cx = Math.Floor(e.At[0]), cz = Math.Floor(e.At[2] - 0.5) + 0.5, cy = e.At[1];
                        why = " (" + (l.ClearHit(cx, cy, cz, cx + 1, cy + 1, cz + 1) ?? (md.Grid.Collides(cx + 0.02, cy + 0.02, cz + 0.02, cx + 0.98, cy + 0.98, cz + 0.98) ? "her collision in the cell" : "?")) + ")";
                    }
                    bad.Add(e.Key + ": no place" + why);
                    continue;
                }
                string kind = l.LaidKind(e);
                var plan = PiecePlanner.Plan(l, m, ModularFake(e, kind), ModularToward(e, kind, m));
                if (plan.Error != null) bad.Add(e.Key + ": " + plan.Error);
            }
            foreach (var b in bad.Take(20)) Console.WriteLine("         " + b);
            Check(bad.Count == 0, $"{md.Code}: its {n} modular entries find their places ({bad.Count} don't)");
        }
    }
}
