using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.28, step D2) Deck cells and pieces take shots (docs/slots.md §11): carrack-m's plain deck cells are damage
        /// parts (no per-station deck parts), the crosshair names a cell; a shot down through a main deck cell: 40 kJ cracks
        /// it, ~110 kJ stoves it in and goes on to the lower deck; a cell under a laid gun or a piece stops at cracked; a
        /// partition is cracked, then shot away (the shot goes on); what mends them; the save (codes, piece hurt, old
        /// per-station deck hurt moved onto a cell).
        ///   dotnet run -c Debug -- notes0328damage
        /// </summary>
        static void Notes0328Damage(string root)
        {
            Console.WriteLine("0.3.28: deck cells and pieces take shots (step D2)");
            var (def, _) = LoadHullDesign(root);
            var l = def.Slots;
            var dl = def.DamageParts;
            var D = dl.D;
            Check(l != null && dl.CellSlotted, $"{def.Code} has deck cells as damage parts");
            if (l == null || !dl.CellSlotted) return;

            // ---- the parts: one a plain cell, none for a beam's length, no per-station deck parts; codes unique
            var cells = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].IsCell).ToList();
            int mapped = cells.Count(n => dl.CellPart(n) >= 0 && dl.Parts[dl.CellPart(n)].Cell == n);
            int beamsMapped = Enumerable.Range(0, l.Count).Count(n => !l.Slots[n].IsCell && dl.CellPart(n) >= 0);
            int oldDeck = dl.Parts.Count(p => p.Kind == DamageKind.Deck && p.Cell < 0);
            int dupCodes = dl.Parts.Length - dl.Parts.Select(p => p.Code).Distinct().Count();
            Check(mapped == cells.Count && beamsMapped == 0 && oldDeck == 0 && dupCodes == 0 && dl.Parts.Take(def.Hull?.Count ?? 0).Select((p, i) => p.Slot == i).All(b => b),
                  $"{cells.Count} plain deck cells are parts ({mapped} mapped, {beamsMapped} beam lengths), {oldDeck} per-station deck parts left, {dupCodes} codes twice; hull panels still first ({dl.Count} parts)");
            int badCode = cells.Count(n =>
            {
                var p = l.PartOf(n, true);
                return dl.Find(p) != dl.CellPart(n) || dl.Find(ShipPart.Decode(p.Encode())) != dl.CellPart(n) || p.CellI != l.CellI(l.Slots[n].CX) || p.CellK != l.CellK(l.Slots[n].CZ) || !p.CellUnder;
            });
            Check(badCode == 0, $"every cell's part code (its deck, i, k; its underside's too) finds its part ({badCode} not)");

            // ---- a main deck cell over a lower deck cell, nothing between (no beam), no gun over it
            var g = def.Grid;
            var gunCells = new HashSet<int>((def.GunPlaces ?? Array.Empty<GunPlaceDef>()).SelectMany(gp => GunPlaces.Cells(def, gp)));
            int top = -1, below = -1;
            foreach (int n in cells.Where(n => l.Slots[n].Deck == "main" && !l.Slots[n].StartsEmpty && !gunCells.Contains(n)).OrderBy(n => Math.Abs(l.Slots[n].CX - 4) + Math.Abs(l.Slots[n].CZ)))
            {
                var s = l.Slots[n];
                double t = g.RaycastCell(new Vec3d(s.CX + 0.1, s.Box[1] - 0.01, s.CZ + 0.1), new Vec3d(0, -1, 0), 6, out int ci, out int cj, out int ck, out _);
                if (t > 6) continue;
                int m = l.AtVoxel(g, ci, cj, ck);
                if (m < 0 || !l.Slots[m].IsCell || l.Slots[m].Deck != "lower" || l.Slots[m].StartsEmpty) continue;
                top = n; below = m;
                break;
            }
            Check(top >= 0, $"a main deck cell with a lower deck cell under it and nothing between: {(top >= 0 ? l.Slots[top].Id + " over " + l.Slots[below].Id : "none")}");
            if (top < 0) return;
            var ts = l.Slots[top];
            int P = dl.CellPart(top), Q = dl.CellPart(below);

            // the crosshair on it: the cell (its top from above, its underside from below)
            double th = g.RaycastCell(new Vec3d(ts.CX + 0.1, ts.Top + 1.5, ts.CZ + 0.1), new Vec3d(0, -1, 0), 4, out int hi, out int hj, out int hk, out int hax);
            var seen = def.PartMap.Classify(hi, hj, hk, hax, new Vec3d(0, -1, 0), new Vec3d(ts.CX + 0.1, ts.Top + 1.5 - th, ts.CZ + 0.1));
            double tu = g.RaycastCell(new Vec3d(ts.CX + 0.1, ts.Box[1] - 0.6, ts.CZ + 0.1), new Vec3d(0, 1, 0), 4, out int ui, out int uj, out int uk, out int uax);
            var seenU = def.PartMap.Classify(ui, uj, uk, uax, new Vec3d(0, 1, 0), new Vec3d(ts.CX + 0.1, ts.Box[1] - 0.6 + tu, ts.CZ + 0.1));
            Check(seen.Kind == ShipPartKind.Cell && !seen.CellUnder && dl.Find(seen) == P && seenU.Kind == ShipPartKind.Cell && seenU.CellUnder && dl.Find(seenU) == P,
                  $"the crosshair on it from above is the cell ({seen}), from below its underside ({seenU})");

            // ---- shots straight down at it (a plunging ball)
            var states = new SlotStates(l);
            DamageState Shot(double e, Func<int, bool> held, out DamageResult res)
            {
                var st = new DamageState(dl.Count);
                var hooks = PieceDamage.Hooks(dl, g, null, null, n => !states.Filled(n), held);
                res = ShipDamage.Apply(dl, st, g, new Vec3d(ts.CX + 0.1, ts.Top, ts.CZ + 0.1), new Vec3d(0, -1, 0), e, new CannonDef().HitRadius, DamageKinds.All, null, hooks);
                return st;
            }
            var s40 = Shot(40, null, out var r40);
            Check(HullPanelRules.StateFor(D, s40.Hurt[P]) == SlotState.Cracked && s40.Hurt[Q] == 0 && s40.Wear[P] == 0,
                  $"40 kJ cracks it (hurt {s40.Hurt[P]:0.00}: cracked, still in; its toughness {D.CellToughness} kJ), nothing under it hurt, no seam wear on a deck cell");
            var s110 = Shot(110, null, out var r110);
            Check(HullPanelRules.StateFor(D, s110.Hurt[P]) == SlotState.Empty && r110.Pierced && s110.Hurt[Q] > 0.05f,
                  $"110 kJ stoves it in (hurt {s110.Hurt[P]:0.00}: out) and goes on to the lower deck: {l.Slots[below].Id} hurt {s110.Hurt[Q]:0.00} " +
                  $"(hit: {string.Join(", ", r110.Hits.Select(h => dl.Parts[h.Part].Part + " +" + h.Added.ToString("0.00")))})");
            var sAgain = new DamageState(dl.Count);
            sAgain.Hurt[P] = 1;
            var rAgain = ShipDamage.Apply(dl, sAgain, g, new Vec3d(ts.CX + 0.1, ts.Top, ts.CZ + 0.1), new Vec3d(0, -1, 0), 40, new CannonDef().HitRadius, DamageKinds.All, null,
                PieceDamage.Hooks(dl, g, null, null, n => !states.Filled(n), null));
            Check(sAgain.Hurt[Q] > 0.2f && rAgain.Hits.All(h => h.Part != P), $"a cell stove in: the next shot goes through its opening to the lower deck ({sAgain.Hurt[Q]:0.00})");
            // a design opening (a well's cell: empty by design) takes nothing
            int well = cells.FirstOrDefault(n => l.Slots[n].StartsEmpty, -1);
            if (well >= 0)
            {
                var ws = l.Slots[well];
                var stW = new DamageState(dl.Count);
                var rW = ShipDamage.Apply(dl, stW, g, new Vec3d(ws.CX, ws.Top, ws.CZ), new Vec3d(0, -1, 0), 40, new CannonDef().HitRadius, DamageKinds.All, null,
                    PieceDamage.Hooks(dl, g, null, null, n => !states.Filled(n), null));
                Check(stW.Hurt[dl.CellPart(well)] == 0, $"a well's cell (an opening by design: {ws.Id}) takes nothing ({rW.Hits.Count} parts hit elsewhere)");
            }

            // ---- held: a cell something stands on stops at cracked (the rest goes on)
            var s200 = Shot(200, n => n == top, out var r200);
            Check(HullPanelRules.StateFor(D, s200.Hurt[P]) == SlotState.Cracked && s200.Hurt[P] >= PieceDamage.HeldCap(D) - 1e-4f && s200.Hurt[Q] > 0,
                  $"held (something on it): 200 kJ leaves it cracked (hurt {s200.Hurt[P]:0.00}), the rest goes on ({s200.Hurt[Q]:0.00} below)");
            var laid = (def.GunPlaces ?? Array.Empty<GunPlaceDef>()).FirstOrDefault(gp => gp.StartsLaid && GunPlaces.Cells(def, gp).Count > 0);
            int gunCell = laid != null ? GunPlaces.Cells(def, laid).First(n => l.Slots[n].IsCell) : -1;
            var chest = new ShipPiece { Id = 1, Role = PieceRole.Storage, Solids = new List<double[]> { new[] { ts.Box[0] + 0.1, ts.Top, ts.Box[2] + 0.1, ts.Box[3] - 0.1, ts.Top + 0.8, ts.Box[5] - 0.1 } } };
            bool gunHeld = gunCell >= 0 && PieceDamage.CellHeld(def, gunCell, Array.Empty<ShipPiece>(), gp => gp.StartsLaid) && !PieceDamage.CellHeld(def, gunCell, Array.Empty<ShipPiece>(), gp => false);
            bool chestHeld = PieceDamage.CellHeld(def, top, new[] { chest }, gp => gp.StartsLaid) && !PieceDamage.CellHeld(def, top, Array.Empty<ShipPiece>(), gp => gp.StartsLaid);
            Check(gunHeld && chestHeld, $"held cells: under a laid gun ({(gunCell >= 0 ? l.Slots[gunCell].Id : "none")}: held while it's laid, not while the place is empty), under a chest put on {ts.Id}");
            if (gunCell >= 0)
            {
                var gs = l.Slots[gunCell];
                var stG = new DamageState(dl.Count);
                ShipDamage.Apply(dl, stG, g, new Vec3d(gs.CX, gs.Top, gs.CZ), new Vec3d(0, -1, 0), 300, new CannonDef().HitRadius, DamageKinds.All, null,
                    PieceDamage.Hooks(dl, g, null, null, n => !states.Filled(n), n => PieceDamage.CellHeld(def, n, Array.Empty<ShipPiece>(), gp => gp.StartsLaid)));
                int gp0 = dl.CellPart(gunCell);
                Check(stG.Hurt[gp0] > D.Cracked && HullPanelRules.StateFor(D, stG.Hurt[gp0]) == SlotState.Cracked, $"300 kJ on the cell under the laid gun: cracked, not stove in ({stG.Hurt[gp0]:0.00})");
            }

            // ---- a partition put up on the main deck: cracked by a spent ball, shot away by the next, a strong one goes on
            var pg = g.Clone();
            int pi = g.CellX(ts.CX + 0.1);
            var pbox = new[] { g.OX + pi * g.V, ts.Top, ts.Box[2], g.OX + (pi + 1) * g.V, ts.Top + 1, ts.Box[5] };
            bool air = !g.Collides(pbox[0] + 0.01, pbox[1] + 0.01, pbox[2] + 0.01, pbox[3] - 0.01, pbox[4] - 0.01, pbox[5] - 0.01);
            pg.Fill(pbox);
            var pieces = new ShipPieceSet();
            pieces.Add(new ShipPiece { Id = 7, Role = PieceRole.Partition, Solids = new List<double[]> { pbox }, Sel = new List<double[]> { pbox }, Box = pbox });
            var pp = pieces.Get(7);
            DamageResult PShot(double e)
            {
                var st = new DamageState(dl.Count);
                var hooks = PieceDamage.Hooks(dl, pg, pieces, null, n => !states.Filled(n), null);
                return ShipDamage.Apply(dl, st, pg, new Vec3d(pbox[0], ts.Top + 0.5, ts.CZ), new Vec3d(1, 0, 0), e, new CannonDef().HitRadius, DamageKinds.All, null, hooks);
            }
            var rp1 = PShot(15);
            float h1 = pp.Hurt;
            var rp2 = PShot(15);
            float h2 = pp.Hurt;
            Check(air && rp1.PieceHits.Count == 1 && PieceDamage.StateOf(D, h1) == PartState.Cracked && rp2.PieceHits.Count == 1 && h2 >= D.Holed && rp1.Hits.Count == 0,
                  $"a partition ({D.PartitionToughness} kJ) in the waist: 15 kJ cracks it ({h1:0.00}), 15 more shoot it away ({h2:0.00}: gone)");
            pp.Hurt = 0;
            var rp3 = PShot(100);
            Check(pp.Hurt >= D.Holed && rp3.Pierced && (rp3.Exit != null || rp3.Hits.Count > 0 || rp3.Remaining == 0), $"100 kJ: shot away ({pp.Hurt:0.00}), the ball goes on through her ({rp3.Hits.Count} parts hit after, out: {rp3.Exit != null})");
            pp.Hurt = 0;
            var hold = PieceDamage.Hooks(dl, pg, pieces, null, null, null);
            hold.PieceCap = id => PieceDamage.HeldCap(D);
            ShipDamage.Apply(dl, new DamageState(dl.Count), pg, new Vec3d(pbox[0], ts.Top + 0.5, ts.CZ), new Vec3d(1, 0, 0), 100, new CannonDef().HitRadius, DamageKinds.All, null, hold);
            Check(PieceDamage.StateOf(D, pp.Hurt) == PartState.Cracked, $"a partition something hangs on stops at cracked ({pp.Hurt:0.00})");
            var door = new ShipPiece { Role = PieceRole.Door, Open = true, Solids = new List<double[]> { pbox } };
            var chestP = new ShipPiece { Role = PieceRole.Storage };
            Check(PieceDamage.Takes(door) && PieceDamage.Toughness(D, door) == D.DoorToughness && !PieceDamage.Takes(chestP) && PieceDamage.Toughness(D, new ShipPiece { Role = PieceRole.Window }) == D.WindowToughness,
                  "partitions, doors, windows and railings take shots; a chest is woodwork to the shot as before");

            // ---- what mends them
            var cr = new DamageState(dl.Count);
            cr.Hurt[P] = 0.5f;
            var jc = ShipRepairs.Needs(dl, cr, P);
            var so = new DamageState(dl.Count);
            so.Hurt[P] = 1;
            var jo = ShipRepairs.Needs(dl, so, P);
            var jp = PieceDamage.Needs(D, 0.5f);
            Check(jc.Count == 1 && jc[0].What == "planks" && jc[0].Kits[0].Items.Any(it => it.Code == ShipRepairs.Nails) && ShipRepairs.For(dl, cr, P, "game:plank-oak") != null &&
                  jo.Count == 1 && jo[0].What == "lay" && jo[0].Kits.Any(k => k.Items[0].Code == ShipRepairs.PlanksBlock && k.Items[0].Count == ts.Mat.Count) &&
                  jp.Count == 1 && jp[0].Kits[0].Items.Any(it => it.Code == ShipRepairs.Nails) && PieceDamage.For(D, 0.5f, "game:plank-pine") != null && PieceDamage.For(D, 0f, "game:plank-pine") == null,
                  $"a cracked cell: the repair tool's planks job (boards and nails); stove in: laid again ({jo.FirstOrDefault()?.What}, {ts.Mat.Count} planks block); a cracked partition: boards and nails");
            // the repair crosshair picks the cracked cell from above
            int pick = ShipRepairs.Pick(dl, cr, g, new Vec3d(ts.CX + 0.1, ts.Top + 1.6, ts.CZ + 0.1), new Vec3d(0, -1, 0), 5, "game:plank-oak", 0.6, out _);
            Check(pick == P, $"the repair crosshair with boards in hand picks the cracked cell ({(pick >= 0 ? dl.Parts[pick].Part.ToString() : "nothing")})");

            // ---- the save: a cell's hurt by its code, a piece's with it; an old save's per-station deck hurt onto a cell
            var codes = cr.Codes(dl);
            var back = new DamageState(dl.Count);
            back.Load(dl, codes, (float[])cr.Hurt.Clone(), (float[])cr.Wear.Clone());
            var tr = new TreeAttribute();
            new ShipPiece { Id = 3, Role = PieceRole.Partition, Hurt = 0.45f }.ToTree(tr);
            var pBack = ShipPiece.FromTree(tr);
            var t0 = new TreeAttribute();
            new ShipPiece { Id = 4, Role = PieceRole.Partition }.ToTree(t0);
            Check(Math.Abs(back.Hurt[P] - 0.5f) < 1e-6 && Math.Abs(pBack.Hurt - 0.45f) < 1e-6 && !t0.HasAttribute("hurt") && ShipPiece.FromTree(t0).Hurt == 0,
                  $"saved and loaded: the cell's hurt by its code ({back.Hurt[P]:0.00}), a partition's with it ({pBack.Hurt:0.00}; none saved for a sound one)");
            int mainIx = Array.FindIndex(def.Decks, d => d.Code == "main");
            int station = def.PartMap.StationAt(ts.CX);
            var oldCodes = new[] { ShipPart.Deck(mainIx, station, false).Encode(), ShipPart.Deck(mainIx, station + 1, false).Encode(), dl.Parts.First(p => p.Kind == DamageKind.Mast).Code };
            var (mc, mh, mw, moved) = CellMigration.Map(def, oldCodes, new[] { 0.9f, 0.2f, 0.5f }, new[] { 0.4f, 0f, 0f });
            var mig = new DamageState(dl.Count);
            mig.Load(dl, mc, mh, mw);
            int hitCell = Enumerable.Range(0, dl.Count).Where(i => dl.Parts[i].Cell >= 0 && mig.Hurt[i] > 0).DefaultIfEmpty(-1).First();
            int mast = dl.Find(oldCodes[2]);
            Check(moved == 1 && hitCell >= 0 && Math.Abs(mig.Hurt[hitCell] - D.Cracked) < 1e-6 && mig.Wear[hitCell] == 0 && Math.Abs(mig.Hurt[mast] - 0.5f) < 1e-6 &&
                  Math.Abs(l.Slots[dl.Parts[hitCell].Cell].CX - (def.Frames.X0 + (station + 0.5) * def.Frames.Space)) <= def.Frames.Space,
                  $"an old save (v 2): the main deck's holed frame space ({station}) cracks the cell nearest its middle ({(hitCell >= 0 ? l.Slots[dl.Parts[hitCell].Cell].Id : "none")}, never out on loading), a scraped one dropped, the mast's kept");
        }
    }
}
