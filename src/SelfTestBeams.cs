using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.3.27, the user's stairs and beams; docs/slots.md §10) On a launched carrack-m: a length of a main deck beam under
    /// a well doesn't come out with the deck over it in; the three cells of a steep stair's well taken up; the stair put by
    /// hand on the lower deck under it: the beam's length comes out with it (out of her collision, the carlings drawn by the
    /// slots' rule), the stair walked up and down; the beam can't be laid back through the stair; the stair taken up, the
    /// beam laid back, the cells laid. A stair across her (to starboard) put between two beams with no beam taken out.
    /// (Note 207) a hit leaves scars. (Note 199) a stair turned with the wrench.
    /// </summary>
    public partial class SelfTest
    {
        void BeamsAboard(EntityShip ship)
        {
            var sl = ship.SlotLayout;
            var l = ship.Mounts;
            if (sl == null || sl.Beams.Count == 0) { Log("0.3.27: carrack-m has no beam lengths that come out: the stairs through beams aren't checked"); return; }
            var steep = sapi.World.GetBlock(new AssetLocation(ModularPieceDef.SteepStair));
            if (steep == null || steep.Id == 0) { Check(false, "0.3.27: the steep ship's stair block"); return; }
            double mainY = ship.Def.Decks.First(d => d.Code == "main").Y;
            bool done = false;
            // (amidships first, as the offline test: aft, by the stern, the tiller's sweep stands before a stair's foot)
            foreach (var segs in new[] { sl.Beams.Where(kv => kv.Key.StartsWith("main:")).SelectMany(kv => kv.Value).OrderBy(n => Math.Abs(sl.Slots[n].CX - 4) + Math.Abs(sl.Slots[n].CZ - 2.5)).ToList() })
            {
                foreach (int seg in segs)
                {
                    var sd = sl.Slots[seg];
                    double xc = sd.Box[3];
                    var foot = l.Floors.FirstOrDefault(m => m.Deck == "lower" && Math.Abs(m.X - xc) < 1e-6 && Math.Abs(m.Z + 0.5 - sd.CZ) < 1e-6 && !ship.Pieces.Taken(m.Id));
                    if (foot == null) continue;
                    var cells = new[] { xc - 0.5, xc + 0.5, xc + 1.5 }.Select(x => sl.OnDeck(mainY, x, sd.CZ)).ToList();
                    if (cells.Any(c => c < 0 || !ship.Slots.Filled(c) || ship.TakeProblem(c) != null)) continue;
                    // (the lower deck under it free: no piece in the stair's way)
                    var probe = PiecePlanner.Plan(l, foot, new VanillaPieceBlock(sapi.World, new ItemStack(steep), true), 2);
                    if (probe.Error != null || ship.Pieces.Blocking(probe) != null) continue;
                    // ((0.3.29) a railing or wall along the stair's run (the hold stair's well's on the lower deck): its voxel a
                    // quarter block into the cell, the stair walked off its middle: another place)
                    var lane = new[] { foot.X - 1.0, foot.Y + 0.05, sd.CZ - 0.55, foot.X + 3.5, mainY + 1.8, sd.CZ + 0.55 };
                    if (ship.Pieces.All.Any(p => p.Role != PieceRole.Stair && p.Solids != null && p.Solids.Any(b => PiecePlanner.Overlap(b, lane)))) continue;
                    int diff0 = ship.Slots.Differences;     // (what the steps before left changed)
                    string before = ship.BeamTakeProblem(seg);
                    foreach (int c in cells) ship.TakeCell(c, null);
                    var inBefore = sl.Beams.Values.SelectMany(v => v).Where(n => ship.Slots.Filled(n)).ToList();
                    string handErr = HandPlace(ship, foot, 0, steep, out var stair, beams: true);
                    var taken = inBefore.Where(n => !ship.Slots.Filled(n)).ToList();
                    bool beamOut = !ship.Slots.Filled(seg) && !ship.Grid.SolidAt(sd.CX, (sd.Box[1] + sd.Box[4]) / 2, sd.CZ);
                    bool walked = stair != null && PiecePlanner.WalkFlight(ship.Grid, foot, stair.Dir, stair.Rise, out double top, out double back, PiecePlanner.RunOf(stair));
                    Check(before == "ships:beam-deck-in" && handErr == null && beamOut && walked,
                        $"0.3.27: the {sd.Beam} beam's length {sd.Id} doesn't come out with the deck over it in ({before}); the well's three cells taken up and a steep stair put by hand through it ({handErr ?? "placed"}): the beam's length came out with it ({beamOut}), the stair walked up and down ({walked})");
                    string layThrough = ship.LayCell(seg, false);
                    string rem = stair != null ? ship.RemovePiece(stair, null) : "no stair";
                    string layBack = ship.LayCell(seg, false);
                    foreach (int n in taken) if (n != seg) layBack ??= ship.LayCell(n, false);
                    var layCells = cells.Select(c => ship.LayCell(c, false) ?? "ok").ToList();
                    bool solidAgain = ship.Grid.SolidAt(sd.CX, (sd.Box[1] + sd.Box[4]) / 2, sd.CZ);
                    Check(layThrough != null && rem == null && layBack == null && ship.Slots.Filled(seg) && solidAgain && ship.Slots.Differences == diff0,
                        $"0.3.27: the beam can't be laid back through the stair ({layThrough}); the stair taken up ({rem ?? "ok"}), the beam laid back ({taken.Count} taken out with the stair: {layBack ?? "ok"}; in her collision {solidAgain}), the cells laid ({string.Join(", ", layCells)}): as before ({ship.Slots.Differences} differences, {diff0} before)");
                    done = true;
                    break;
                }
                if (done) break;
            }
            if (!done) Check(false, "0.3.27: a main deck beam's length under three cells free for a steep stair from the lower deck");
            // a stair across her between two beams: nothing taken out
            bool across = false;
            foreach (var foot in l.Floors.Where(m => m.Deck == "lower" && m.Room == "lowerdeck" && ((int)Math.Round(m.X) % 2 + 2) % 2 == 0 && !ship.Pieces.Taken(m.Id)).OrderBy(m => Math.Abs(m.X + 6) + Math.Abs(m.Z + 2)))
            {
                var cells = new[] { -0.5, 0.5, 1.5 }.Select(dz => sl.OnDeck(mainY, foot.X + 0.5, foot.Z + dz)).ToList();
                if (cells.Any(c => c < 0 || !ship.Slots.Filled(c) || ship.TakeProblem(c) != null)) continue;
                var probe = PiecePlanner.Plan(l, foot, new VanillaPieceBlock(sapi.World, new ItemStack(steep), true), 3);
                if (probe.Error != null || ship.Pieces.Blocking(probe) != null) continue;
                foreach (int c in cells) ship.TakeCell(c, null);
                int outBefore = ship.Slots.Differences;
                string err = HandPlace(ship, foot, 1, steep, out var stair, beams: true);
                bool walked = stair != null && PiecePlanner.WalkFlight(ship.Grid, foot, stair.Dir, stair.Rise, out _, out _, PiecePlanner.RunOf(stair));
                bool none = ship.Slots.Differences == outBefore;
                // (note 199) turned with the wrench: the next way it fits
                string turned = stair != null ? ship.RotatePiece(stair, out var t2) : "no stair";
                var now = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Stair && p.Mount == foot.Id);
                if (now != null) ship.RemovePiece(now, null);
                foreach (int c in cells) ship.LayCell(c, false);
                if (err == null) Check(walked && none, $"0.3.27 (note 222): a steep stair across her ({foot.Id}, climbing to starboard) through a well between two beams: put by hand, no beam taken out ({none}), walked ({walked}); the wrench turns it ({turned ?? "turned"})");
                else continue;
                across = true;
                break;
            }
            if (!across) Check(false, "0.3.27 (note 222): a lower deck cell where a stair across her fits between two beams");
            // ((0.3.27, note 207) a shot's scars; (0.3.30, note 234: "nothing about holes from cannon") none any more: what a
            // shot hits is cracked or out (ShipDamage), an older save's scars become cracked slots as she loads)
            int s0 = ship.Scars.Count;
            var hit = new Vec3d(2.0, ship.Def.DeckY + 0.5, 3.0);
            ship.AddShotScars(hit, new Vec3d(0, 0, -1), new Vec3d(2.0, ship.Def.DeckY + 0.5, -3.0));
            Check(s0 == 0 && ship.Scars.Count == 0, $"0.3.30 (note 234): a ball through her leaves no scars ({ship.Scars.Count}), only slots cracked or out");
            ship.ClearAllScars();
        }
    }
}
