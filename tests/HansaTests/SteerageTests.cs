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
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", "caravel.json");
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
                    Check(line.All(b => b.Box[4] <= deck.Y + OpenRowsTop + 1e-6),
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
                // across the middle of its block, on one side of the line or the other (its ends: her side, the hatch's gap)
                var surf = SurfacesOf(def, Path.Combine(root, "assets", "hansa"));
                var unmet = new List<string>();
                foreach (var t in line.Where(b => b.Row == 2))
                {
                    var drawn = l.DrawnClip(t, surf) ?? t.Clip;
                    for (double al = t.Z - 0.25; al <= t.Z + 0.25 + 1e-6; al += 0.125)
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

        const double OpenRowsTop = MountLayout.OpenDeckRows;
    }
}
