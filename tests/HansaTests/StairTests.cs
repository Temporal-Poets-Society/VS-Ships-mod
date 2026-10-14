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
        /// (0.3.23, notes 170, 172: "trying to place stairs here (where stairs were before) but cannot") A stair or ladder put
        /// back by hand, as the game does it: the player standing before its foot cell, the crosshair on that cell's floor
        /// (her collision: <see cref="AimPlace.GridHit"/>), the places next to the hit (<see cref="AimPlace.Candidates"/>) planned
        /// toward the player (<see cref="AimPlace.Toward"/>) as the client does; the packet's direction (the plan's Toward)
        /// planned again by the server. It must be the piece taken up: the same foot, the same way up, nothing in its way.
        /// The 0.3.22 packet (the plan's Dir, the way up) is shown turned round.
        /// </summary>
        static void HandPutBack(ShipDefinition md, MountLayout l, ShipPieceSet set, VoxelGrid grid, MountPoint foot, int up, PieceBlock block, string label)
        {
            var feet = PiecePlanner.StairPoint(foot, up, -1.3, 0);
            var eye = new Vec3d(feet[0], feet[1] + 1.62, feet[2]);
            var aim = PiecePlanner.StairPoint(foot, up, 0.5, 0);
            var d = new Vec3d(aim[0] - eye.X, aim[1] - eye.Y, aim[2] - eye.Z);
            d.Mul(1 / d.Length());
            string why = null;
            PiecePlan client = null, server = null, old = null;
            if (!AimPlace.GridHit(grid, eye, d, 6, out double t, out var normal)) why = "the crosshair meets nothing";
            else
            {
                var hit = eye.AddCopy(d.X * t, d.Y * t, d.Z * t);
                bool Free(MountPoint x) => !set.Taken(x.Id) && set.Supports(l, x) && x.Kind == MountKind.Floor && x.Level == 0;
                MountPoint chosen = null;
                foreach (var c in AimPlace.Candidates(l, set, hit, normal, null, Free))
                {
                    var pl = PiecePlanner.Plan(l, c, block, AimPlace.Toward(c, eye, hit));
                    if (chosen == null) { chosen = c; client = pl; }
                    if (pl.Error == null) { chosen = c; client = pl; break; }
                }
                if (client == null) why = "no place next to the hit";
                else
                {
                    // the server: TryPlacePiece(player, mount, packet.Dir) plans it again toward that way
                    server = PiecePlanner.Plan(l, chosen, block, client.Toward);
                    old = PiecePlanner.Plan(l, chosen, block, client.Dir);
                    var clash = server.Error == null ? PiecePlanner.GridClash(l, server, grid) : null;
                    var blocking = server.Error == null ? set.Blocking(server) : null;
                    why = client.Error ?? server.Error ??
                          (chosen != foot ? $"the crosshair's place is {chosen.Id}, not {foot.Id}" :
                           server.Dir != up ? $"planned climbing {MountLayout.DirCode[server.Dir]}, not {MountLayout.DirCode[up]}" :
                           blocking != null ? $"in the way of piece {blocking.Id} {blocking.Role}" :
                           clash != null ? $"its room blocked by her at [{string.Join(", ", clash.Select(v => v.ToString("0.##")))}]" : null);
                }
            }
            string oldSays = old == null ? "" : old.Error != null ? $"; the 0.3.22 packet (its way up) was refused: {old.Error}" : old.Dir != up ? $"; the 0.3.22 packet planned it climbing {MountLayout.DirCode[old.Dir]}" : "";
            Check(why == null, $"{md.Code}: {label} put back by hand (aimed at {foot.Id} from before it, the client's plan sent as its Toward){(why != null ? ": " + why : "")}{oldSays}");
        }

        /// <summary>
        /// (0.3.23, the user: "can we make stairs for 2 blocks also? ladder animation is kind of bad"; "for ships stairs better
        /// to have a special block") The ship's stairs on the carrack's lower deck up to the main deck: the long one (3 cells,
        /// 45°) and the steep one (2 cells, about 56°), their collision's steps, the wells and deck edges they need, walked
        /// up and down, their own handrails (solid beside the treads, nobody steps off sideways; drawn parallel to the
        /// nosings), no railing beside them, out of building mode refused, taken up and put back by hand.
        /// </summary>
        static void ShipStairs(ShipDefinition def)
        {
            Console.WriteLine("0.3.23: the ship's stairs (steep 2 long, long 3 long)");
            var l = def.Mounts;
            var g = def.Grid;
            var mainDeck = def.Decks.First(x => x.Code == "main");
            FakePiece Stair(int cells) => new FakePiece { Code = cells == 2 ? ModularPieceDef.SteepStair : ModularPieceDef.LongStair, Family = "x", Stairs = true, StairCells = cells, StairRails = true };
            // collision steps: risers at most half a block, the long one's a tread each
            var steepCols = PiecePlanner.FlightColumns(3, 2);
            var longCols = PiecePlanner.FlightColumns(3, 3);
            double prev = 0, maxRise = 0;
            foreach (var c in steepCols) { maxRise = Math.Max(maxRise, c.y - prev); prev = c.y; }
            Check(steepCols.Count == 8 && Math.Abs(steepCols[^1].y - 3) < 1e-9 && maxRise <= 0.5 + 1e-9 && longCols.Count == 12 &&
                  longCols.Select((c, i) => Math.Abs(c.y - 0.25 * (i + 1)) < 1e-9).All(x => x) && PiecePlanner.FlightTreads(3) == 12,
                $"its collision's steps: the steep one 8 columns a voxel long (tops {string.Join(" ", steepCols.Select(c => c.y.ToString("0.##")))}; risers ≤ {maxRise:0.##}), the long one a column per tread; 12 treads drawn each");
            foreach (int cells in new[] { 2, 3 })
            {
                string name = cells == 2 ? "steep" : "long";
                var block = Stair(cells);
                MountPoint foot = null;
                foreach (var m in l.Floors.Where(m => m.Deck == "lower" && m.Room == "lowerdeck").OrderBy(m => Math.Abs(m.X - 4) + Math.Abs(m.Z - 2.5)))
                {
                    bool room = l.FloorAt("lower", m.I - 1, m.K) != null && l.FloorAt("main", m.I + cells, m.K) != null && l.FloorAt("main", m.I - 1, m.K) != null;
                    for (int c = 1; c < cells; c++) room &= l.FloorAt("lower", m.I + c, m.K) != null;
                    if (!room) continue;
                    var tp = PiecePlanner.Plan(l, m, block, 2);
                    var tg = g.Clone();
                    tg.Fill(new[] { m.X - 1, m.Y + 1.5, m.Z, m.X + cells, mainDeck.Y + 0.05, m.Z + 1 }, false);
                    if (tp.Error == null && PiecePlanner.GridClash(l, tp, tg) == null) { foot = m; break; }
                }
                Check(foot != null, $"a lower deck cell with room for the {name} stair up to the main deck");
                if (foot == null) continue;
                var plan = PiecePlanner.Plan(l, foot, block, 2);
                double angle = Math.Atan2(plan.Rise, cells) * 180 / Math.PI;
                var others = Enumerable.Range(1, cells - 1).Select(c => l.FloorAt("lower", foot.I + c, foot.K)).ToList();
                int guards = plan.Solids.Count - PiecePlanner.FlightColumns(plan.Rise, cells).Count;
                Check(plan.Error == null && plan.Role == PieceRole.Stair && plan.Cells == cells && plan.Rails && plan.Dir == 0 && plan.Toward == 2 &&
                      Math.Abs(plan.Rise - (mainDeck.Y - foot.Y)) < 1e-9 && plan.Mounts.Contains(foot.Id) && others.All(o => !plan.Mounts.Contains(o.Id)) && guards > 0 &&
                      (cells == 2 ? angle > 52 && angle < 60 : angle > 40 && angle < 50),
                    $"the {name} ship's stair: {cells} cells climbing away from the placer (toward {plan.Toward}, up {plan.Dir}) to the main deck (rise {plan.Rise}, {angle:0}°), its other cells' places free, {guards} guard boxes beside its treads");
                // wells: its cells only; its cells and the cell before its foot; (the steep one) its cells and a quarter cut back
                VoxelGrid Well(double before)
                {
                    var gw = g.Clone();
                    gw.Fill(new[] { foot.X - before, foot.Y + 1.5, foot.Z, foot.X + cells, mainDeck.Y + 0.05, foot.Z + 1 }, false);
                    return gw;
                }
                bool Walks(VoxelGrid gw, PiecePlan p, out double top, out double back)
                {
                    var gs = gw.Clone();
                    foreach (var sb in p.Solids) gs.Fill(sb);
                    return PiecePlanner.WalkFlight(gs, foot, p.Dir, p.Rise, out top, out back, cells);
                }
                var exact = Well(0);
                var quarter = Well(0.25);
                var whole = Well(1);
                bool exactRoom = PiecePlanner.GridClash(l, plan, exact) == null, quarterRoom = PiecePlanner.GridClash(l, plan, quarter) == null, wholeRoom = PiecePlanner.GridClash(l, plan, whole) == null;
                bool exactWalk = Walks(exact, plan, out _, out _), quarterWalk = Walks(quarter, plan, out double tq, out double bq), wholeWalk = Walks(whole, plan, out double tw, out double bw);
                if (cells == 3)
                    Check(exactRoom && exactWalk && wholeWalk, $"the long stair through a 3 × 1 well over its cells: its room free ({exactRoom}), walked up and down ({exactWalk}); with the cell before its foot open too ({wholeWalk})");
                else
                    Check(!exactRoom && !exactWalk && quarterRoom && quarterWalk && wholeRoom && wholeWalk,
                        $"the steep stair through a deck: a 2 × 1 well over its cells alone is refused (room {exactRoom}) and not walked (head on the well's near edge: {exactWalk}); the deck cut back a quarter before its foot: room {quarterRoom}, walked up ({tq:0.00}) and down ({bq:0.00}); a 3 × 1 well with the cell before its foot: room {wholeRoom}, walked up ({tw:0.00}) and down ({bw:0.00})");
                // up to a deck's edge: the deck above may reach back over its head cell a quarter, not a half
                VoxelGrid Edge(double over)
                {
                    var ge = g.Clone();
                    ge.Fill(new[] { foot.X - 1, foot.Y + 1.5, foot.Z, foot.X + cells - over, mainDeck.Y + 0.05, foot.Z + 1 }, false);
                    ge.Fill(new[] { foot.X + cells - over, foot.Y + 1.5, foot.Z, foot.X + cells, mainDeck.Y - 0.25, foot.Z + 1 }, false);
                    ge.Fill(new[] { foot.X + cells - over, mainDeck.Y - 0.25, foot.Z, foot.X + cells + 0.01, mainDeck.Y, foot.Z + 1 });
                    return ge;
                }
                var edgeOk = Edge(0.25);
                var edgeFar = Edge(0.5);
                bool edgeWalk = Walks(edgeOk, plan, out double te, out double be);
                Check(PiecePlanner.GridClash(l, plan, edgeOk) == null && PiecePlanner.GridClash(l, plan, edgeFar) != null && edgeWalk,
                    $"the {name} stair up to a deck's edge (no well): the deck may reach back over its head cell 0.25, not 0.5; walked up ({te:0.00}) and down ({be:0.00})");
                // its own handrails: nobody steps off its side halfway up (walking sideways from its middle), both ways
                var gwalk = whole.Clone();
                foreach (var sb in plan.Solids) gwalk.Fill(sb);
                var fell = new List<string>();
                foreach (int side in new[] { -1, 1 })
                {
                    double u = cells / 2.0;
                    var colTop = PiecePlanner.FlightColumns(plan.Rise, cells).First(c => u >= c.u0 && u < c.u1).y;
                    var a = PiecePlanner.StairPoint(foot, plan.Dir, u, colTop, 0);
                    var b = PiecePlanner.StairPoint(foot, plan.Dir, u, colTop, side * 2);
                    var mv = new DeckMover(a[0], a[1] + 0.01, a[2]) { OnGround = true };
                    mv.Unstick(gwalk);
                    double low = mv.Y;
                    for (int k = 0; k < 40; k++) { mv.Step(gwalk, 0.05, b[0] - a[0], b[2] - a[2], 3.0, false, false); low = Math.Min(low, mv.Y); }
                    if (low < a[1] - 0.6) fell.Add($"side {side}: fell to {low - foot.Y:0.00}");
                }
                Check(fell.Count == 0, $"the {name} stair's own guards: walked sideways off its middle both ways, nobody falls{(fell.Count > 0 ? ": " + string.Join("; ", fell) : "")}");
                // no railing beside it; a railing beside an old (0.3.19) walked stair is still raked
                var set = new ShipPieceSet();
                var piece = ShipPiece.From(plan, 1, null);
                set.Add(piece);
                var side0 = l.Borders.FirstOrDefault(x => x.Deck == "lower" && x.Row == 0 && x.Clip == null && Math.Abs(x.Z - foot.Z) < 1e-6 && Math.Abs(x.X - (foot.X + 0.5)) < 1e-6);
                if (side0 != null)
                {
                    var old = ShipPiece.From(PiecePlanner.Plan(l, foot, new FakePiece { Code = "x", Family = "x", Stairs = true }, 2), 1, null);
                    var fence = new FakePiece { Code = "game:woodenfence-oak-ew-free", Family = "x", Fence = true };
                    var rail = ShipPiece.From(PiecePlanner.Plan(l, side0, fence, side0.In), 2, null);
                    var oldSet = new ShipPieceSet();
                    oldSet.Add(old);
                    oldSet.Add(rail);
                    PiecePlanner.ApplyRakes(l, oldSet.All);
                    Check(PiecePlanner.BesideRailed(l, set.All, side0) == piece && !old.Rails && PiecePlanner.BesideRailed(l, oldSet.All, side0) == null && rail.Rake != null,
                        $"no railing beside the {name} ship's stair ({side0.Id}: refused, railing-by-stair); beside an old walked stair (no handrails of its own) it's still raked");
                }
                // drawn: the handrails on its stringers' lines, parallel to the nosings 0.9 over them, stanchions upright, nothing through a tread, no two faces in one plane
                var parts = ModularGeometry.Flight(foot, plan.Dir, plan.Rise, cells, true);
                var treads = ModularGeometry.Flight(foot, plan.Dir, plan.Rise, cells, false).Where(p => p.Mat == PartMat.Planks).Select(p => p.AsBox()).ToList();
                var rails = ModularGeometry.FlightHandrails(foot, plan.Dir, plan.Rise, cells);
                var bad = new List<string>();
                int beams = 0, posts = 0;
                foreach (var p in rails)
                {
                    if (p.IsBeam && p.AsBox() == null)
                    {
                        beams++;
                        var (u0, _) = PiecePlanner.StairUC(foot, plan.Dir, p.P0[0], p.P0[2]);
                        var (u1, _) = PiecePlanner.StairUC(foot, plan.Dir, p.P1[0], p.P1[2]);
                        double o0 = p.P0[1] - foot.Y - PiecePlanner.FlightNosing(plan.Rise, u0, cells), o1 = p.P1[1] - foot.Y - PiecePlanner.FlightNosing(plan.Rise, u1, cells);
                        if (Math.Abs(o0 - PiecePlanner.RailHeight) > 1e-6 || Math.Abs(o1 - PiecePlanner.RailHeight) > 1e-6) bad.Add($"a handrail {o0:0.00}/{o1:0.00} over the nosings");
                        continue;
                    }
                    posts++;
                    var bx = p.AsBox();
                    foreach (var tb in treads) if (tb != null && PiecePlanner.Overlap(bx, tb, 0.001)) { bad.Add("a stanchion through a tread"); break; }
                }
                var boxes = parts.Select(x => x.AsBox()).Where(x => x != null).ToList();
                int coplanar = ModularGeometry.Coplanar(boxes).Count;
                // (0.3.24, note 189) balusters about every 0.3 along the run (7 a side on the steep one, 10 on the long one)
                int wantPosts = 2 * (Math.Max(2, (int)Math.Round((cells - 0.2) / ModularGeometry.FlightBalusterPitch)) + 1);
                Check(bad.Count == 0 && beams == 2 && posts == wantPosts && posts >= 14 && coplanar == 0,
                    $"the {name} stair drawn: {beams} handrails {PiecePlanner.RailHeight} over its nosings, {posts} balusters clear of the treads, no two faces in one plane ({coplanar}){(bad.Count > 0 ? ": " + string.Join("; ", bad.Distinct().Take(4)) : "")}");
                // out of building mode refused; put back by hand (aimed at its foot from before it) through a 3 × 1 well
                var offMode = new FakePiece { Code = block.Code, Family = "x", StairCells = cells, NeedsBuilding = true };
                Check(PiecePlanner.Plan(l, foot, offMode, 2).Error == "hansa:shipstair-build-mode", $"the {name} stair out of building mode: refused (shipstair-build-mode)");
                HandPutBack(def, l, new ShipPieceSet(), whole, foot, 0, block, $"the {name} ship's stair");
            }
            // ShipPiece saved and loaded: its length and handrails kept; one saved before 0.3.23 is the long one, raked as before
            {
                var foot = l.Floors.First(m => m.Deck == "lower" && m.Room == "lowerdeck");
                var p = ShipPiece.From(new PiecePlan { Mount = foot, Role = PieceRole.Stair, Rise = 3, Cells = 2, Rails = true }, 5, null);
                var t = new Vintagestory.API.Datastructures.TreeAttribute();
                p.ToTree(t);
                var back = ShipPiece.FromTree(t);
                var t0 = new Vintagestory.API.Datastructures.TreeAttribute();
                ShipPiece.From(new PiecePlan { Mount = foot, Role = PieceRole.Stair, Rise = 3 }, 6, null).ToTree(t0);
                var oldBack = ShipPiece.FromTree(t0);
                Check(back.Cells == 2 && back.Rails && PiecePlanner.RunOf(back) == 2 && oldBack.Cells == 0 && !oldBack.Rails && PiecePlanner.RunOf(oldBack) == 3,
                    "a ship's stair saved keeps its length and handrails; a walked stair saved before 0.3.23 is 3 long without them");
            }
            // the contract: a stair entry's length picks its block and its kind; an old entry without one as before
            {
                var e2 = new ModularPieceDef { Kind = "stair", Length = 2 };
                var e3 = new ModularPieceDef { Kind = "stair" };
                var eOld = new ModularPieceDef { Kind = "stair", Block = "game:plankstairs-oak-up-north-free" };
                Check(e2.BlockFor("stair") == ModularPieceDef.SteepStair && e3.BlockFor("stair") == ModularPieceDef.LongStair && eOld.BlockFor("stair") == ModularPieceDef.LongStair &&
                      ModularPieceDef.DefaultBlock("stair") == ModularPieceDef.LongStair,
                    "a stair entry is laid with the ship's stair of its length (2: steep, 3 or none: long; vanilla plank stairs named by an old entry: the long one)");
            }
            HatchFromBelow();
        }

        /// <summary>
        /// (0.3.23, note 171: "no guns in the cabins ... on upper floors") carrack-m's cabin guns moved up onto the open
        /// quarterdeck (their port, lid and wall raised by the deck's height): a lidded port there closes no room and lets no
        /// sea below (no downflood point of its own), while the cabin's own ports do.
        /// </summary>
        static void CastleGunPorts(string root)
        {
            var md = Newtonsoft.Json.JsonConvert.DeserializeObject<ShipDefinition>(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "assets", "hansa", "config", "ships", "carrack-m.json")));
            md.Init();
            var cabin = ShipCompartments.LiddedPorts(md).Where(f => ShipCompartments.PortRoom(md, f) != null).ToList();
            if (cabin.Count == 0) { Console.WriteLine("  --   carrack-m has no lidded ports in a room: the castle-deck port check needs one to move (the generator's castle guns are checked by -- gunplaces)"); return; }
            var q = md.Decks.First(x => x.Code == "quarter");
            double lift = q.Y - cabin[0].Pos[1];
            var before = new DamageLayout(md).Downflood.Count(p => cabin.Any(f => f.Code == p.OpenFixture));
            foreach (var f in cabin)
            {
                // (onto the open quarterdeck, forward of the poop: x -6)
                double dx = -6 - f.Pos[0];
                f.Pos[0] += dx;
                f.Pos[1] += lift;
                if (f.Port != null) { f.Port[0] += dx; f.Port[2] += dx; f.Port[1] += lift; f.Port[3] += lift; }
            }
            bool open = cabin.All(f => ShipCompartments.PortRoom(md, f) == null);
            var after = new DamageLayout(md).Downflood.Count(p => cabin.Any(f => f.Code == p.OpenFixture));
            Check(before > 0 && open && after == 0,
                $"0.3.23: carrack-m's {cabin.Count} cabin guns raised onto the open quarterdeck: their ports in no room ({open}), no downflood points of their own ({after}; {before} in the cabin)");
        }

        /// <summary>
        /// (0.3.23, note 169: "hatches cannot be opened from bottom; can climb through a closed hatch, then open and leave;
        /// without opening cannot leave") A shut hatch over a ladder's top: the climber's head stops under it (he can't climb
        /// through it, nor step onto the ladder from above through it); opened, he climbs out. From below the crosshair picks it
        /// (its planking first, as her collision) and it's seen (to open it), not behind her own deck.
        /// </summary>
        static void HatchFromBelow()
        {
            var g = new VoxelGrid(-3, -1, -3, 24, 28, 24, 0.25, new byte[(24 * 28 * 24 + 7) / 8]);
            // a lower deck at y 0 and the deck above at 3 (planking 2.75..3), an opening x 0..1, z 0..1 in it, shut by a hatch
            g.Fill(new double[] { -3, -0.25, -3, 3, 0, 3 });
            g.Fill(new double[] { -3, 2.75, -3, 3, 3, 3 });
            g.Fill(new double[] { 0, 2.75, 0, 1, 3, 1 }, false);
            var hatch = new ShipPiece { Id = 1, Role = PieceRole.Hatch, Kind = MountKind.Horizontal, Mounts = new[] { "cell:x" } };
            hatch.Solids.Add(new double[] { 0, 2.75, 0, 1, 3, 1 });
            hatch.Sel.Add(new double[] { 0, 2.875, 0, 1, 3.0625, 1 });
            hatch.Box = new double[] { 0, 2.875, 0, 1, 3, 1 };
            var set = new ShipPieceSet();
            set.Add(hatch);
            var shut = g.Clone();
            shut.Fill(hatch.Solids[0]);
            var ladder = new LadderDef { Code = "test", Kind = "ladder", Bottom = new double[] { 0.5, 0, 0.2 }, Top = new double[] { 0.5, 3, 0.6 }, ExitBottom = new double[] { 0.5, 0, -0.5 }, ExitTop = new double[] { 0.5, 3, 1.5 } };
            double Climb(bool open)
            {
                hatch.Open = open;
                var gg = open ? g : shut;
                var mv = new DeckMover(0.5, 0, -0.5) { OnGround = true };
                mv.TopClosed = l => !double.IsNaN(HatchCover.Under(set.All, l));
                mv.CoverUnder = l => HatchCover.Under(set.All, l);
                var lads = new[] { ladder };
                bool on = false;
                for (int n = 0; n < 200; n++)
                {
                    mv.Step(gg, 0.05, 0, 1, 3.0, false, false, 1, lads, 1);
                    if (mv.Climbing != null) on = true;
                    else if (on) break;
                }
                return mv.Climbing != null ? mv.Y + DeckMover.Height : -mv.Y;
            }
            double headShut = Climb(false), outOpen = Climb(true);
            Check(headShut > 0 && headShut <= 2.75 + 1e-6 && outOpen <= -2.9,
                $"a shut hatch over a ladder's top: the climber's head stops under it (at {headShut:0.00}, its underside 2.75); open, he climbs out onto the deck above ({-outOpen:0.00})");
            // from above: nobody steps onto the ladder down through it while it's shut
            hatch.Open = false;
            var top = new DeckMover(0.5, 3, 1.5) { OnGround = true, TopClosed = l => !double.IsNaN(HatchCover.Under(set.All, l)) };
            for (int n = 0; n < 12; n++) top.Step(shut, 0.05, 0, -1, 3.0, false, false, 1, new[] { ladder }, 0);
            Check(top.Climbing == null && top.Y > 2.9, $"from above, walking onto the shut hatch doesn't put him on the ladder under it (on it at {top.Y:0.00})");
            // the crosshair from below (the lower deck, looking up at it at 45°): the hatch, where her collision is met
            var eye = new Vec3d(-1.2, 1.62, 0.5);
            var d = new Vec3d(1.7, 1.13, 0);
            d.Mul(1 / d.Length());
            double wall = shut.Raycast(eye, d, 8);
            var picked = set.Pick(eye, d, 8, out double tp);
            bool oldWay = hatch.Sel.Any(s => ShipFittings.RayBox(eye, d, new Cuboidd(s[0], s[1], s[2], s[3], s[4], s[5])) is double ts && ts >= 0 && ts <= wall + 0.15);
            bool seen = ShipPieceSet.Seen(shut, eye, hatch.SeenBox), seenOld = ShipPieceSet.Seen(shut, eye, hatch.Box);
            Check(picked == hatch && tp <= wall + 0.15 && seen,
                $"from below the crosshair picks the shut hatch (at {tp:0.00}, her collision at {wall:0.00}) and it's seen to be opened ({seen}); before 0.3.23 by its leaves only: picked {oldWay}, seen {seenOld}");
        }
    }
}
