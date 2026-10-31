using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// (24 October 2026, caravel notes 66-82 on 0.1.0-dev.8) On the ship under test, with the game's own blocks: an oven on a
    /// planks block put on her deck (note 74), a trunk at her hold's after end where it's one cell wide (note 79), a bed on
    /// either side of her main deck by the mizzen (note 70), the mainmast's lantern taken down and hung back, and one hung
    /// under her steerage's ceiling (note 66); no panel places (note 82); a shelf in her hold over a chest, a vessel on it
    /// (note 83). Each
    /// put as the server puts a player's (laid another way where vanilla's doesn't fit) and taken up again.
    /// </summary>
    public partial class SelfTest
    {
        void BuildPlaceAboard(EntityShip ship)
        {
            var l = ship.Mounts;
            if (ship.Def?.Code != "caravel" || l == null) { Log($"24 October 2026: the caravel's building notes are checked on the caravel ({ship.Def?.Code} under test)"); return; }
            RuntimePlacementAboard(ship);
            Block B(string c) => sapi.World.GetBlock(new AssetLocation("game", c)) is Block b && b.Id != 0 ? b : null;
            var put = new List<ShipPiece>();
            string Put(MountPoint m, Block b, int dir, out ShipPiece placed)
            {
                placed = null;
                if (m == null || b == null) return "no place or block";
                var stack = new ItemStack(b);
                var plan = PiecePlanner.Plan(l, m, new VanillaPieceBlock(sapi.World, stack), dir, p => ship.PlaceProblem(p, false) == null);
                string err = ship.PlacePiece(plan, stack, false, out placed);
                if (placed != null) put.Add(placed);
                return err;
            }
            try
            {
                // ---- note 74: an oven on top of planks put on her deck
                var defaultOven=ship.Pieces.All.FirstOrDefault(p=>p.Role==PieceRole.Oven);
                var ovenStack=defaultOven?.Stack?.Clone();var ovenMount=defaultOven==null?null:l.Get(defaultOven.Mount);
                if(defaultOven!=null) Check(ship.RemovePiece(defaultOven,null)==null,"the default oven can be taken up before testing a different supporting block");
                var floor = l.FloorAt("main", -13, -1);   // (her steerage, 2 aft since note 73)
                string under = Put(floor, B("planks-oak-ud"), 1, out var planks);
                string onTop = Put(l.Above(floor), B("clayoven-north"), 1, out var ovenP);
                string planksOut = planks == null ? "none" : ship.RemovePiece(planks, null);
                Check(under == null && onTop == null && ovenP?.Role == PieceRole.Oven && planksOut == "hansa:piece-under",
                    $"note 74: an oven on top of a planks block on her deck ({under ?? "planks put"}, {onTop ?? "oven put"}); the planks not taken from under it ({planksOut})");
                string ovenOut = ovenP == null ? "none" : ship.RemovePiece(ovenP, null);
                planksOut = planks == null ? "none" : ship.RemovePiece(planks, null);
                Check(ovenOut == null && planksOut == null, $"note 74: the oven taken up, then the planks ({ovenOut ?? "ok"}, {planksOut ?? "ok"})");
                put.Clear();
                if(ovenStack!=null) {
                    var restore=PiecePlanner.Plan(l,ovenMount,new VanillaPieceBlock(sapi.World,ovenStack),0);
                    Check(ship.PlacePiece(restore,ovenStack,false,out _) == null,"the default oven goes back on its table");
                }

                // ---- note 79: a trunk where two vessels go, her hold's after end, put looking aft
                var trunkBlock = sapi.World.Blocks.FirstOrDefault(bl => bl is Vintagestory.GameContent.BlockGenericTypedContainerTrunk);
                var aft = l.FloorAt("hold", -10, -1);
                string trunk = Put(aft, trunkBlock, 0, out var trunkP);
                Check(trunk == null && trunkP?.Mounts.Length == 2 && trunkP.Mounts.Contains(aft.Id),
                    $"note 79: a trunk at her hold's after end, put looking aft ({trunk ?? string.Join(" + ", trunkP.Mounts)})");
                string trunkOut = trunkP == null ? "none" : ship.RemovePiece(trunkP, null);
                Check(trunkOut == null, $"note 79: the trunk taken up ({trunkOut ?? "ok"})");
                put.Clear();

                // ---- note 70: a bed on either side of her main deck by the mizzen, put from her middle looking out
                var beds = new List<string>();
                foreach (var (k, dir) in new[] { (-2, 1), (0, 3) })
                {
                    string bed = Put(l.FloorAt("main", -11, k), B("bed-wood-head-north"), dir, out var bedP);
                    beds.Add($"{(k < 0 ? "port" : "starboard")} {bed ?? string.Join(" + ", bedP.Mounts)}");
                    if (bedP != null) beds[^1] += ship.RemovePiece(bedP, null) == null ? ", taken up" : ", NOT taken up";
                }
                Check(beds.All(b => b.EndsWith(", taken up")), $"note 70: a bed by the mizzen on either side, put looking out to her side ({string.Join("; ", beds)})");
                put.Clear();

                // ---- note 66: the mainmast's lantern taken down and hung back
                var hook = l.Get("h:lantern-mainmast");
                var lantern = hook == null ? null : ship.Pieces.At(hook.Id);
                if (lantern == null) Log("note 66: no lantern on the mainmast's hook to take down");
                else
                {
                    var stack = lantern.Stack?.Clone();
                    string down = ship.RemovePiece(lantern, null);
                    var plan = stack == null ? null : PiecePlanner.Plan(l, hook, new VanillaPieceBlock(sapi.World, stack), 1);
                    string back = plan == null ? "no stack" : ship.PlacePiece(plan, stack, true, out _);
                    Check(down == null && back == null && ship.Pieces.At(hook.Id) != null, $"note 66: the mainmast's lantern taken down ({down ?? "ok"}) and hung back ({back ?? "ok"})");
                    // (and under a deck: the steerage's ceiling's hook)
                    var ceiling = l.Hooks.Where(h=>MountLayout.IsCeilingHook(h.Id) && h.Deck=="main")
                        .OrderBy(h=>Math.Abs(h.X+12.5)+Math.Abs(h.Z-1))
                        .FirstOrDefault(h=>ship.PlaceProblem(PiecePlanner.Plan(l,h,new VanillaPieceBlock(sapi.World,stack),1),false)==null);
                    var underDeck = stack == null || ceiling == null ? null : PiecePlanner.Plan(l, ceiling, new VanillaPieceBlock(sapi.World, stack), 1);
                    string hung = underDeck == null ? "no ceiling hook or stack" : ship.PlacePiece(underDeck, stack, true, out var hungP);
                    string off = hung == null ? ship.RemovePiece(ship.Pieces.At(ceiling.Id), null) : "not hung";
                    Check(hung == null && off == null, $"note 66: a lantern hung under the steerage's ceiling ({hung ?? "ok"}) and taken down ({off ?? "ok"})");
                }
                // ---- note 83: a shelf a block up in her hold, aimed at from a step away with a plank slab (building mode), over a
                // chest; a vessel on it; not taken up from under the vessel; then each taken up
                {
                    var cell = l.FloorAt("hold", -3, -2);
                    var slabStack = new ItemStack(B("plankslab-oak-down-free"));
                    var eye = new Vintagestory.API.MathTools.Vec3d(cell.X + 1.5, cell.Y + 1.62, cell.Z + 0.5);
                    var aim = new Vintagestory.API.MathTools.Vec3d(-1.0, -1.62, 0);
                    aim.Mul(1 / aim.Length());
                    MountPoint shelfAt = null;
                    if (AimPlace.GridHit(ship.Grid, eye, aim, 6, out double th, out var nh))
                        shelfAt = AimPlace.Candidates(l, ship.Pieces, eye.AddCopy(aim.X * th, aim.Y * th, aim.Z * th), nh, null,
                            x => x.Kind == MountKind.Horizontal && !ship.Pieces.Taken(x.Id) && ship.MountOpen(x)).FirstOrDefault();
                    string chestPut = Put(cell, B("chest-east"), 0, out var chestP);
                    var shelfPlan = shelfAt == null ? null : PiecePlanner.Plan(l, shelfAt, new VanillaPieceBlock(sapi.World, slabStack, true), 0);
                    string shelfPut = shelfPlan == null ? "nothing offered" : ship.PlacePiece(shelfPlan, slabStack, true, out var shelfP);
                    var vesselBlock = sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Code.Path.StartsWith("storagevessel-") && bl.Code.Path.EndsWith("-fired"));
                    string vesselPut = Put(l.FloorAt("hold", -3, -2, 1), vesselBlock, 0, out var vesselP);
                    var shelfPiece = shelfAt == null ? null : ship.Pieces.At(shelfAt.Id);
                    string fromUnder = shelfPiece == null ? "no shelf" : ship.RemovePiece(shelfPiece, null);
                    Check(shelfAt?.Id == "hb:hold:-3:-2:1" && chestPut == null && shelfPut == null && vesselPut == null && fromUnder == "hansa:piece-under",
                        $"note 83: a shelf a block up in her hold, aimed at from a step away ({shelfAt?.Id ?? "nothing"}), over a chest ({chestPut ?? "ok"}, {shelfPut ?? "ok"}), a vessel on it ({vesselPut ?? "ok"}), not taken up from under it ({fromUnder})");
                    string outs = string.Join(", ", new[] { vesselP, shelfPiece, chestP }.Select(p => p == null ? "none" : ship.RemovePiece(p, null) ?? "ok"));
                    Check(outs == "ok, ok, ok", $"note 83: the vessel, the shelf and the chest taken up ({outs})");
                    put.Clear();
                }
                // ---- (25 October 2026, dev.9: "no it does not work and cannot be placed") the shelf as the client's crosshair finds
                // it (PlaceAim, DecoClient's own code), with the game's plank slabs: up, down, on edge, other woods; aimed at the
                // floor cell from the cell beside it and at her side from over an outermost cell, standing as a player does
                {
                    var slabs = new[] { "plankslab-oak-down-free", "plankslab-oak-up-free", "plankslab-oak-north-free", "plankslab-pine-down-free", "plankslab-aged-east-free" }
                        .Select(c => B(c)).Where(b => b != null).ToList();
                    int looks = 0, blocked = 0;
                    var missed = new List<string>();
                    foreach (var slabB in slabs)
                    {
                        var h = HeldForPlacing.Of(slabB, true);
                        var stack = new ItemStack(slabB);
                        var free = PlaceAim.Free(ship, h);
                        foreach (var f in l.Floors.Where(f => l.HorizontalAt(f.Deck, f.I, f.K, 1) != null && l.FloorAt(f.Deck, f.I, f.K, 1) != null && !ship.Pieces.Taken(f.Id)))
                        {
                            var want = l.HorizontalAt(f.Deck, f.I, f.K, 1);
                            var aims = new List<(Vintagestory.API.MathTools.Vec3d Eye, Vintagestory.API.MathTools.Vec3d At, string What)>();
                            for (int dir = 0; dir < 4; dir++)
                                if (l.FloorAt(f.Deck, f.I + MountLayout.DX[dir], f.K + MountLayout.DZ[dir]) is MountPoint by && !ship.Pieces.Taken(by.Id))
                                    aims.Add((new Vintagestory.API.MathTools.Vec3d(by.X + 0.5, by.Y + 1.62, by.Z + 0.5), new Vintagestory.API.MathTools.Vec3d(f.X + 0.5, f.Y, f.Z + 0.5), "floor from " + by.Id));
                            foreach (int dir in new[] { 1, 3 })
                                if (l.FloorAt(f.Deck, f.I, f.K + MountLayout.DZ[dir]) == null)
                                    aims.Add((new Vintagestory.API.MathTools.Vec3d(f.X + 0.5, f.Y + 1.62, f.Z + 0.5), new Vintagestory.API.MathTools.Vec3d(f.X + 0.5, f.Y + 1.0, (dir == 1 ? f.Z + 1 : f.Z) + MountLayout.DZ[dir] * 0.7), "her side"));
                            foreach (var (eye, at, what) in aims)
                            {
                                var d = new Vintagestory.API.MathTools.Vec3d(at.X - eye.X, at.Y - eye.Y, at.Z - eye.Z);
                                d.Mul(1 / d.Length());
                                var piece = ship.Pieces.Pick(eye, d, 5, out double tp);
                                var got = PlaceAim.Choose(ship, h, eye, d, 5, piece, tp, free,
                                    (c, hit) => PiecePlanner.Plan(l, c, new VanillaPieceBlock(sapi.World, stack, true), c.Kind is MountKind.Wall or MountKind.Panel ? c.In : AimPlace.Toward(c, eye, hit), p => ship.PlaceProblem(p, false) == null),
                                    out var plan, out double t, out _);
                                if (got == null && what == "her side") continue;     // (her side out of reach of that look: the floor's are checked)
                                looks++;
                                // (at a corner of her side the next cell's shelf is as good)
                                string why = got == null ? "nothing" : got != want && (what != "her side" || got.Kind != MountKind.Horizontal) ? got.Id : plan?.Error ?? ship.PlaceProblem(plan, true);
                                // ((27 October 2026, dev.11) a shelf a piece aboard is in the way of (the galley's tables): occupied, not a miss)
                                if (why == "hansa:piece-taken" && plan != null && ship.Pieces.Blocking(plan) != null) { looks--; continue; }
                                // A tiller, rigging or other visible fitting is an obstruction even without walking voxels.
                                // The transom shelf crosses the moving tiller: it must now be refused.
                                if(why=="hansa:piece-blocked" && ship.VisibleObstructed(plan)) { blocked++; continue; }
                                if (why != null) missed.Add($"{slabB.Code.Path} on {f.Id} ({what}): {why}");
                            }
                        }
                    }
                    Check(slabs.Count >= 4 && looks > 100 && missed.Count == 0,
                        $"dev.9: the shelf as the client's crosshair finds it, with the game's plank slabs ({string.Join(", ", slabs.Select(b => b.Code.Path))}): {looks} looks, {blocked} refused at visible fittings{(missed.Count == 0 ? ", every one" : "; not: " + string.Join("; ", missed.Take(6)))}");
                }
                // ---- (25 October 2026, dev.9) note 89: a door hung at either end; note 90: a window on a place her side cuts; note
                // 99: the short stair up aft from her hold's cell under the hatchway: its top on the hatch, not "no landing"
                {
                    var doorB = B("door-sleek-windowed-oak") ?? sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && VanillaPieceBlock.IsDoor(bl));
                    var dm = l.Borders.FirstOrDefault(b => b.Deck == "hold" && b.Row == 0 && b.Clip == null && !b.TwinSide && l.BorderAbove(b) is MountPoint up && up.Clip == null &&
                                                            !ship.Pieces.Taken(b.Id) && !ship.Pieces.Taken(up.Id) && ship.MountOpen(b));
                    var ends = new List<string>();
                    foreach (int hinge in new[] { 0, PiecePlanner.HingeHigh })
                    {
                        if (dm == null || doorB == null) { ends.Add("no doorway or door"); break; }
                        var stack = new ItemStack(doorB);
                        var plan = PiecePlanner.Plan(l, dm, new VanillaPieceBlock(sapi.World, stack, true), dm.In | hinge);
                        string err = ship.PlacePiece(plan, stack, false, out var dp);
                        ends.Add(err ?? (dp.HingeHighEnd == (hinge != 0) ? "ok" : "wrong end"));
                        if (dp != null) ship.RemovePiece(dp, null);
                    }
                    Check(ends.All(e => e == "ok"), $"note 89: a door in her hold hung at its doorway's lower end, then its upper ({dm?.Id}: {string.Join(", ", ends)})");
                    var paneB = B("glasspane-leaded-oak-ew") ?? sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && VanillaPieceBlock.IsPane(bl));
                    var wm = l.Borders.FirstOrDefault(b => b.Clip != null && !b.TwinSide && PiecePlanner.ClipArea(b) >= 0.5 && !ship.Pieces.Taken(b.Id) && ship.MountOpen(b) && b.Deck == "hold");
                    string win = "no place or pane";
                    if (wm != null && paneB != null)
                    {
                        var stack = new ItemStack(paneB);
                        var plan = PiecePlanner.Plan(l, wm, new VanillaPieceBlock(sapi.World, stack, true), wm.In);
                        win = ship.PlacePiece(plan, stack, true, out var wp) ?? (wp.Role == PieceRole.Window ? "ok" : wp.Role.ToString());
                        if (wp != null) ship.RemovePiece(wp, null);
                    }
                    Check(win == "ok", $"note 90: a window on a place her side cuts ({wm?.Id}: {win})");
                    // ((26 October 2026, dev.11) a hold cell whose short stair's top lands on her main deck's hatchway, found in her
                    // layout as it is: the note's cell is her mainmast's now)
                    var shortB = sapi.World.GetBlock(new AssetLocation(ModularPieceDef.StairBlock(1)));
                    double mainY = ship.Def.Decks.First(dk => dk.Code == "main").Y;
                    var sl = ship.SlotLayout;
                    var stairPlan = shortB == null || sl == null ? null : l.Floors.Where(m => m.Deck == "hold" && !ship.Pieces.Taken(m.Id))
                        .SelectMany(m => new[] { 0, 2 }.Select(t => PiecePlanner.Plan(l, m, new VanillaPieceBlock(sapi.World, new ItemStack(shortB), true), t)))
                        .FirstOrDefault(pl => pl.Error == null && PiecePlanner.FlightTop(pl.Mount, pl.Dir, pl.Rise, pl.Cells) is var off &&
                                              sl.OnDeck(mainY, off[0], off[2]) is int cn && cn >= 0 && sl.Slots[cn].StartsEmpty && ship.Pieces.Blocking(pl) == null);
                    var foot = stairPlan?.Mount;
                    // (open, its top would step into the hatchway: refused for that; laid (or shut by its hatch), not)
                    int hw = stairPlan == null ? -1 : sl.OnDeck(mainY, PiecePlanner.FlightTop(foot, stairPlan.Dir, stairPlan.Rise, stairPlan.Cells)[0], PiecePlanner.FlightTop(foot, stairPlan.Dir, stairPlan.Rise, stairPlan.Cells)[2]);
                    bool wasOpen = hw >= 0 && !ship.Slots.Filled(hw);
                    string open = stairPlan == null ? "no foot or stair" : ship.PlaceProblem(stairPlan, false) ?? "fits";
                    string laid = hw < 0 ? "no cell" : wasOpen ? ship.LayCell(hw, false) : null;
                    string then = stairPlan == null ? "no foot or stair" : ship.PlaceProblem(stairPlan, false) ?? "fits";
                    if (wasOpen && laid == null) ship.TakeCell(hw, null);
                    Check(stairPlan?.Error == null && (!wasOpen || open == "hansa:stair-no-landing") && laid == null && then != "hansa:stair-no-landing",
                        $"note 99: the short stair from {foot?.Id} up {(stairPlan == null ? "-" : MountLayout.DirCode[stairPlan.Dir])}, its top on the hatchway {(hw >= 0 ? sl.Slots[hw].Id : "-")} (planned: {stairPlan?.Error ?? "ok"}): with the hatchway past its top open {open}; laid ({laid ?? "ok"}), not refused for its landing ({then})");
                }
                // ---- note 82: no panel places on her (nothing put into a cell her hull or ribs are in)
                Check(!ship.Def.PanelPlaces && !l.Panels.Any(), $"note 82: no panel places on the caravel ({l.Panels.Count()})");
            }
            finally
            {
                foreach (var p in put) if (ship.Pieces.Get(p.Id) != null) ship.RemovePiece(p, null);
            }
        }
    }
}
