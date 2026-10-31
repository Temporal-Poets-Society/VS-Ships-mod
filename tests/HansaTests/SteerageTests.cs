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
        /// (22 October 2026, caravel chalk note 38: "cannot close of aft deck (open cabin) with partition walls up to upper
        /// deck") The steerage's open front on the x = -5 line, under the quarterdeck's front beam: its border places row by
        /// row with the steerage an open deck (as 0.1.0-dev.5 had it), then a room closed by pieces (enclosed, pieces): a third
        /// row cut under the beam and planking up to the room's top, drawn on up to them, and the steerage sealed once every
        /// place on the line has a partition.
        ///   dotnet run --project tests/HansaTests -- steerage (and with design caravel)
        /// </summary>
        static void CaravelSteerage(string root)
        {
            string path = Path.Combine(root, "assets", "ships", "config", "ships", "caravel.json");
            if (!File.Exists(path)) { Console.WriteLine("  --   no caravel"); return; }
            foreach (bool closed in new[] { false, true })
            {
                var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
                var room = def.Rooms?.FirstOrDefault(r => r.Code == "steerage");
                if (room == null) { Console.WriteLine("  --   caravel: no steerage room"); return; }
                room.Enclosed = closed;
                room.Pieces = closed;
                def.Init();
                var l = def.Mounts;
                double front = room.Max[0];
                var deck = l.FloorOf(room);
                var line = l.Borders.Where(b => b.Deck == deck?.Code && !b.TwinSide && b.In % 2 == 0 && Math.Abs(MountLayout.BorderLine(b).Line - front) < 1e-6)
                                    .OrderBy(b => b.Z).ThenBy(b => b.Row).ToList();
                // (one side's places: the twins on the other face share their segments; HANSA_STEERAGE=1 lists them)
                Console.WriteLine($"         caravel steerage (enclosed {room.Enclosed}, pieces {room.Pieces}, top {room.Max[1]}): {line.Count} places on x = {front}");
                if (Environment.GetEnvironmentVariable("HANSA_STEERAGE") != null)
                    foreach (var b in line)
                        Console.WriteLine($"    {b.Id} room {b.Room} x {b.Box[0]:0.000}..{b.Box[3]:0.000} z {b.Box[2]:0.000}..{b.Box[5]:0.000} y {b.Box[1]:0.000}..{b.Box[4]:0.000}{(b.Clip != null ? $" clip {b.Clip.Count}" : "")} under {b.Under ?? "-"}");
                if (!closed)
                {
                    // as an open deck (two rows): nothing reaches the quarterdeck's underside
                    Check(line.Where(b=>def.BorderPlaces?.Any(e=>"b:"+e.Code==b.Id)!=true).All(b => b.Box[4] <= deck.Y + OpenRowsTop + 1e-6),
                        $"caravel, steerage an open deck: the steerage's front places (x = {front}) stop {OpenRowsTop} over the main deck (an open deck's two rows)");
                    continue;
                }
                // a third row over every whole row 1 of the line (not by her side, where row 1 is cut already), partial under
                // the quarterdeck's beam and planking, up to the room's top or past it, and the steerage's
                var ones = line.Where(b => b.Row == 1 && b.Clip == null).ToList();
                var bad = ones.Where(b => line.FirstOrDefault(t => t.Row == 2 && t.Under == b.Id) is not MountPoint t || t.Clip == null ||
                                          t.Box[4] < room.Max[1] - 1e-6 || t.Room != room.Code).Select(b => b.Id).ToList();
                Check(ones.Count >= 3 && bad.Count == 0, $"caravel, steerage closed by pieces: a third row (partial, up to y {room.Max[1]}, the steerage's) over each " +
                                                         $"of the {ones.Count} whole row-1 places on x = {front}{(bad.Count == 0 ? "" : ": not over " + string.Join(", ", bad))}");
                // drawn out to her surfaces (DrawnClip), each third-row partition's top meets the quarterdeck's beam or planking
                // across the middle of its block, on one side of the line or the other (its ends: her side, the hatch's gap;
                // HANSA_STEERAGE=1 draws them)
                var surf = SurfacesOf(def, Path.Combine(root, "assets", "ships"));
                var unmet = new List<string>();
                foreach (var t in line.Where(b => b.Row == 2))
                {
                    if(def.BorderPlaces?.Any(e=>"b:"+e.Code==t.Id)==true) continue; // A narrow hull wedge meets the curved side before the deck; IntegrationNotes checks its actual geometry.
                    var drawn = l.DrawnClip(t, surf) ?? t.Clip;
                    if (Environment.GetEnvironmentVariable("HANSA_STEERAGE") != null)
                    {
                        DumpPlace(def, t, drawn);
                        Console.WriteLine($"      {t.Id} drawn: " + string.Join(" ", drawn.Select(c => "[" + string.Join(",", c.Select(v => v.ToString("0.####"))) + "]")));
                    }
                    // (at the middles of the drawing's fine cells: a sample on the seam of two of its runs is in neither, as at
                    // z -2.25 since her rib head and railing by the port side drew the run out there higher than the rest)
                    for (double al = t.Z - 0.25 - 0.5 / MountLayout.DrawCells; al <= t.Z + 0.25; al += 0.125)
                    {
                        var over = drawn.Where(c => al > c[0] && al < c[2]).ToList();
                        double tp = over.Count == 0 ? double.NaN : over.Max(c => c[3]);
                        if (double.IsNaN(tp) || tp < room.Max[1] - 1e-6 ||
                            !def.Grid.SolidAt(front - 0.125, tp + 0.01, al) && !def.Grid.SolidAt(front + 0.125, tp + 0.01, al))
                            unmet.Add($"{t.Id} at z {al:0.###} (top {tp:0.###})");
                    }
                }
                Check(line.Any(b => b.Row == 2) && unmet.Count == 0, $"caravel: the steerage front's third row drawn up to the quarterdeck's beam and planking" +
                                                                    $"{(unmet.Count == 0 ? "" : ": not at " + string.Join(", ", unmet.Take(6)))}");
                // close it: a partition on every place of the line
                var planks = new FakePiece { Code = "game:planks-oak-ud", Family = "game:planks-oak", Planks = true };
                var set = new ShipPieceSet();
                foreach (var b in line) set.Add(ShipPiece.From(PiecePlanner.Plan(l, b, planks, 0), set.NextId, null));
                var grid = def.Grid.Clone();
                foreach (var p in set.All) foreach (var s in p.Solids) grid.Fill(s);
                var leaks = ShipCompartments.WellLeaks(def, room, set.All);
                double sx = front - 3, sy = deck.Y + 0.1;
                bool shut = ShipCompartments.Sealed(l, grid, set.All, room, leaks, sx, sy, 0);
                // and open again with one row-0 place empty
                var one = set.All.First(p => l.Get(p.Mount) is MountPoint m && m.Row == 0 && m.Clip == null && Math.Abs(m.Z) < 0.5);
                var set2 = new ShipPieceSet();
                foreach (var p in set.All) if (p.Id != one.Id) set2.Add(p);
                var grid2 = def.Grid.Clone();
                foreach (var p in set2.All) foreach (var s in p.Solids) grid2.Fill(s);
                bool open = !ShipCompartments.Sealed(l, grid2, set2.All, room, leaks, sx, sy, 0);
                bool openBare = !ShipCompartments.Sealed(l, def.Grid, Array.Empty<ShipPiece>(), room, leaks, sx, sy, 0);
                Check(shut && open && openBare, $"caravel: the steerage is a closed room with its front's {set.Count} partitions up (sealed {shut}), " +
                                                $"open with none (open {openBare}) or one gone ({one.Mount}: open {open})");
            }
        }

        /// <summary>
        /// (22 October 2026, caravel note 49: "where stairs were i cannot build partition wall over") The hatch's cells on the
        /// main deck (empty in the design, a keep-clear "hatch" box over them) taken in: the steerage's front on x = -5 over
        /// them has its places (all three rows), usable only while the cells are laid, and the steerage is sealed with every
        /// place on the line taken, over the laid cells too.
        /// </summary>
        static void CaravelNote49(string root)
        {
            string path = Path.Combine(root, "assets", "ships", "config", "ships", "caravel.json");
            if (!File.Exists(path)) return;
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            var l = def.Mounts;
            var room = def.Rooms?.FirstOrDefault(r => r.Code == "steerage");
            var hatch = def.KeepClear?.FirstOrDefault(k => k.What == "hatch");
            if (room == null || hatch == null || def.Slots == null) { Console.WriteLine("  --   caravel: no steerage, hatch or deck cells (note 49)"); return; }
            double front = room.Max[0];
            var cells = Enumerable.Range(0, def.Slots.Count).Where(n => def.Slots.Slots[n] is DeckSlotDef c && c.IsCell && c.StartsEmpty &&
                                                                    c.Box[0] < hatch.Box[3] && c.Box[3] > hatch.Box[0] && c.Box[2] < hatch.Box[5] && c.Box[5] > hatch.Box[2]).ToList();
            var over = l.Borders.Where(b => b.Deck == "main" && !b.TwinSide && b.In % 2 == 0 && Math.Abs(MountLayout.BorderLine(b).Line - front) < 1e-6 &&
                                            b.Z > hatch.Box[2] && b.Z < hatch.Box[5]).OrderBy(b => b.Row).ToList();
            var shut = new SlotStates(def.Slots);
            var laid = new SlotStates(def.Slots);
            foreach (int n in cells) laid.Set(n, SlotState.Filled);
            // (24 October 2026, note 73: her hatch moved into the waist, on the centreline aft of the main mast: no hatch
            // under the steerage's front, nothing of note 49's left to check there)
            if (cells.Count == 0 || !(hatch.Box[0] < front - 1e-6 && hatch.Box[3] > front + 1e-6))
                Console.WriteLine($"  --   note 49: her hatch (x {hatch.Box[0]}..{hatch.Box[3]}) is clear of the steerage's front (x = {front})");
            else
            {
            bool rows = over.Select(b => b.Row).SequenceEqual(new[] { 0, 1, 2 }) && over.All(b => b.Room == room.Code);
            bool usable = over.Count > 0 && over.All(b => DeckCells.MountOpen(b, laid) && !DeckCells.MountOpen(b, shut));
            Check(cells.Count >= 2 && rows && usable, $"note 49: the hatch's {cells.Count} cells laid, the steerage's front (x = {front}) over them takes partitions " +
                $"({string.Join(" ", over.Select(b => b.Id))}: rows 0-2 {rows}, usable laid and not open {usable})");
            }
            // the whole front closed over the laid cells: sealed, with no carrying across a gap
            var line = l.Borders.Where(b => b.Deck == "main" && !b.TwinSide && b.In % 2 == 0 && Math.Abs(MountLayout.BorderLine(b).Line - front) < 1e-6).ToList();
            var planks = new FakePiece { Code = "game:planks-oak-ud", Family = "game:planks-oak", Planks = true };
            var set = new ShipPieceSet();
            foreach (var b in line) set.Add(ShipPiece.From(PiecePlanner.Plan(l, b, planks, 0), set.NextId, null));
            var delta = cells.ToDictionary(n => def.Slots.IdOf(n), n => (int)SlotState.Filled);
            var grid = DeckCells.Applied(def, delta);
            foreach (var p in set.All) foreach (var b in p.Solids) grid.Fill(b);
            var deck = l.FloorOf(room);
            bool sealedNow = ShipCompartments.Sealed(l, grid, set.All, room, ShipCompartments.WellLeaks(def, room, set.All), front - 3, deck.Y + 0.1, 0);
            Check(sealedNow, $"note 49: the steerage sealed with its front's {set.Count} partitions up, over the hatch's laid cells too");
        }

        /// <summary>
        /// (22 October 2026, caravel note 48: "partition penetrates deck beam below") Her partial border places' partitions as
        /// drawn (DrawnClip) don't go up through her timbers: up each drawn column from its foot, at either face, no surface
        /// of hers (a carling's or a beam's underside) is met short of its top: checked along the hatch's carlings (flush with the
        /// hold's lines z -0.5 and -1.5: the top row's partitions along them stood half in them), the rest listed.
        /// </summary>
        static void CaravelNote48(string root)
        {
            string path = Path.Combine(root, "assets", "ships", "config", "ships", "caravel.json");
            if (!File.Exists(path)) return;
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            var l = def.Mounts;
            var surf = SurfacesOf(def, Path.Combine(root, "assets", "ships"));
            if (surf == null) { Console.WriteLine("  --   caravel: no surfaces (note 48)"); return; }
            const double h = MountLayout.PartitionThickness / 2 * 0.9, step = 1.0 / 8;
            var through = new List<string>();
            int places = 0;
            foreach (var b in l.Borders.Where(b => b.Clip != null && !b.TwinSide))
            {
                var drawn = l.DrawnClip(b, surf) ?? b.Clip;
                var (line, alongX) = MountLayout.BorderLine(b);
                double along0 = (alongX ? b.X : b.Z) - 0.5;
                bool bad = false;
                places++;
                for (double al = along0 + 0.5 / MountLayout.DrawCells; al < along0 + 1 && !bad; al += step)
                {
                    var col = drawn.Where(c => al > c[0] && al < c[2]).ToList();
                    if (col.Count == 0) continue;
                    double foot = Math.Max(b.Y, col.Min(c => c[1])) + 0.01, tp = col.Max(c => c[3]);
                    foreach (double off in new[] { -h, h })
                    {
                        double o = line + off;
                        if (alongX ? def.Grid.SolidAt(al, foot, o) : def.Grid.SolidAt(o, foot, al)) continue;
                        double t = alongX ? surf.Ray(al, foot, o, 0, 1, 0, tp - foot) : surf.Ray(o, foot, al, 0, 1, 0, tp - foot);
                        // (a gap in the drawn column is its own: only from its lowest run up to its top)
                        if (t < tp - foot - 0.03 && !col.Any(c => c[3] > foot + t - 0.02 && c[3] < foot + t + 0.02) &&
                            col.Any(c => c[1] < foot + t && c[3] > foot + t + 0.03))
                        {
                            through.Add($"{b.Id} at {(alongX ? "x" : "z")} {al:0.###} y {foot + t:0.###} (top {tp:0.###})");
                            bad = true;
                            break;
                        }
                    }
                }
            }
            // (24 October 2026, note 73) the hatch from the design: on the centreline in the waist now, z -0.5..0.5; its carlings
            // along its sides' lines, over the hold's top row there
            var hb = def.KeepClear?.FirstOrDefault(k => k.What == "hatch")?.Box ?? new[] { -7.0, 0, -1.5, -4.0, 0, -0.5 };
            // (the hatch's own cells: just past its ends a column at a crossing beam's face is listed below, as elsewhere)
            var onLines = l.Borders.Where(b => b.Deck == "hold" && b.Row == 2 && !b.TwinSide && b.In % 2 == 1 && b.X > hb[0] && b.X < hb[3] &&
                                               (Math.Abs(MountLayout.BorderLine(b).Line - hb[2]) < 1e-6 || Math.Abs(MountLayout.BorderLine(b).Line - hb[5]) < 1e-6))
                                   .Select(b => b.Id + " ").ToList();
            var carling = through.Where(t => onLines.Any(t.StartsWith)).ToList();
            // (and meets it: the top row on the line along the hatch's side under it, drawn up to the carling's underside, 0.525)
            var along = l.Borders.Where(b => b.Deck == "hold" && b.Row == 2 && !b.TwinSide && b.In % 2 == 1 && Math.Abs(b.Z - hb[5]) < 1e-6 && b.X > hb[0] - 0.5 && b.X < hb[3]).ToList();
            var tops = along.Select(b => (b.Id, Top: (l.DrawnClip(b, surf) ?? b.Clip).Where(c => b.X > c[0] && b.X < c[2]).Select(c => c[3]).DefaultIfEmpty(double.NaN).Max())).ToList();
            bool meets = along.Count >= 2 && tops.All(t => t.Top > 0.52 && t.Top < 0.56);
            Check(carling.Count == 0 && meets, $"note 48: the hold's top row along the hatch's carlings stops at their undersides ({string.Join(", ", tops.Select(t => $"{t.Id} to {t.Top:0.###}"))})" +
                                               $"{(carling.Count == 0 ? "" : ": through at " + string.Join(", ", carling.Take(4)))}");
            // (the others, there before note 48's rule: a drawn column's fine cell at a crossing beam's face, her side's slope by the
            // tumblehome, the castle's knees and railing; listed, not checked; HANSA_N48_ALL=1 lists them all)
            Console.WriteLine($"         note 48: {through.Count} of her {places} partial places drawn up past a surface of hers elsewhere" +
                              $"{(through.Count == 0 ? "" : ": " + string.Join(", ", through.Take(Environment.GetEnvironmentVariable("HANSA_N48_ALL") != null ? 100 : 4)))}");
        }

        const double OpenRowsTop = MountLayout.OpenDeckRows;
    }
}
