using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (24 October 2026, caravel notes 57-82 on 0.1.0-dev.8) Putting things aboard the caravel and taking them up: her decks
        /// alike port and starboard, a bed, a trunk, a chest, a vessel, an oven wherever their cells are free, the oven on a
        /// block, a partition drawn alike from either side, a panel picked by any face, the axe's ray past a piece.
        ///   dotnet run --project tests/HansaTests -- buildplace        (HANSA_BP_MAP=1: each deck's places as a map, and why not)
        /// </summary>
        static void CaravelBuildPlace(string root)
        {
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", "caravel.json");
            if (!File.Exists(path)) return;
            Console.WriteLine("caravel: building aboard (notes 57-82)");
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            var l = def.Mounts;
            var g = def.Grid;
            if (Environment.GetEnvironmentVariable("HANSA_BP_MAP") != null) FloorMap(def);

            var block = new FakePiece { Code = "game:planks-oak-ud", Family = "game:planks-oak", Planks = true };
            var chest = new FakePiece { Code = "game:chest-east", Family = "game:chest", Container = true, TurnMesh = true, Slots = 16, Sel = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 }, Col = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 } };
            var vessel = new FakePiece { Code = "game:storagevessel-burned", Family = "game:storagevessel", Container = true, TurnMesh = true, Slots = 12, Sel = new[] { 0.125, 0, 0.125, 0.875, 1, 0.875 }, Col = new[] { 0.125, 0, 0.125, 0.875, 1, 0.875 } };
            var trunk = new FakePiece
            {
                Code = "game:trunk-east", Family = "game:trunk", Orient = PieceOrient.Orientable, States = Sides, Container = true, TurnMesh = true, Slots = 36,
                Sel = new[] { 0, 0, 0, 1, 0.75, 1.0 }, Col = new[] { 0, 0, 0, 1, 0.75, 1.0 },
                CellsBy = new()
                {
                    ["north"] = new() { new[] { 0, 0, 0 }, new[] { 1, 0, 0 } }, ["east"] = new() { new[] { 0, 0, 0 }, new[] { 0, 0, 1 } },
                    ["south"] = new() { new[] { 0, 0, 0 }, new[] { -1, 0, 0 } }, ["west"] = new() { new[] { 0, 0, 0 }, new[] { 0, 0, -1 } },
                }
            };
            var bed = new FakePiece { Code = "game:bed-wood-head-north", Family = "game:bed-wood", Orient = PieceOrient.Bed, SleepEfficiency = 0.70833f, Sel = new[] { 0, 0, 0, 1, 0.5625, 1.0 }, Col = new[] { 0, 0, 0, 1, 0.5625, 1.0 } };
            var oven = new FakePiece { Code = "game:clayoven-north", Family = "game:clayoven", Oven = true, Orient = PieceOrient.Orientable, States = Sides };
            bool Fits(PiecePlan p) => p.Error == null && PiecePlanner.GridClash(l, p, g) == null;
            PiecePlan Put(MountPoint m, PieceBlock b, int dir) => PiecePlanner.Plan(l, m, b, dir, Fits);

            // ---- note 70: every deck alike port and starboard (her centre line z 0: a cell k and -k - 2 on the grid's z
            // offset 0.5), and every piece that fits on one side fits on the other
            {
                int Mirror(int k) => -k - 2;
                int MirrorDir(int dir) => dir % 2 == 1 ? (dir + 2) % 4 : dir;
                var lone = l.Floors.Where(m => l.FloorAt(m.Deck, m.I, Mirror(m.K)) == null).Select(m => m.Id).ToList();
                Check(lone.Count == 0, $"note 70: every deck's floor places alike port and starboard ({(lone.Count == 0 ? $"{l.Floors.Count()} places" : "alone: " + string.Join(", ", lone.Take(8)))})");
                var differ = new List<string>();
                var kinds = new (string Name, PieceBlock B)[] { ("block", block), ("chest", chest), ("vessel", vessel), ("trunk", trunk), ("bed", bed), ("oven", oven) };
                foreach (var m in l.Floors)
                {
                    var mm = l.FloorAt(m.Deck, m.I, Mirror(m.K));
                    if (mm == null) continue;
                    foreach (var (name, b) in kinds)
                        for (int dir = 0; dir < 4; dir++)
                            if (Fits(Put(m, b, dir)) != Fits(Put(mm, b, MirrorDir(dir)))) differ.Add($"{name} on {m.Id} toward {dir}");
                }
                Check(differ.Count == 0, $"note 70: a block, a chest, a vessel, a trunk, a bed and an oven fit alike on either side of every deck ({(differ.Count == 0 ? "all" : string.Join("; ", differ.Distinct().Take(6)))})");
                // the user's spot: the main deck aft by the mizzen, both sides and round the mast
                var spot = new[] { (-10, -2), (-9, -2), (-8, -2), (-10, -1), (-8, -1), (-10, 0), (-9, 0), (-8, 0) };
                var missing = spot.Where(c => l.FloorAt("main", c.Item1, c.Item2) == null).ToList();
                Check(missing.Count == 0, $"note 70: the main deck round the mizzen takes pieces on both sides and round the mast ({(missing.Count == 0 ? "8 places" : "none at " + string.Join(", ", missing))})");
                // a bed and a trunk on every floor place with a free neighbour, aimed from any way (laid another way where vanilla's doesn't fit)
                var noBed = new List<string>();
                foreach (var m in l.Floors.Where(m => Enumerable.Range(0, 4).Any(d => l.FloorAt(m.Deck, m.I + MountLayout.DX[d], m.K + MountLayout.DZ[d]) != null)))
                    for (int dir = 0; dir < 4; dir++)
                    {
                        if (!Fits(Put(m, bed, dir))) noBed.Add($"bed on {m.Id} toward {dir}");
                        if (!Fits(Put(m, trunk, dir))) noBed.Add($"trunk on {m.Id} toward {dir}");
                    }
                Check(noBed.Count == 0, $"notes 70, 79: a bed and a trunk on every floor place with a free one beside it, from any way ({(noBed.Count == 0 ? "all" : string.Join("; ", noBed.Take(6)))})");
            }

            // ---- note 79: the trunk where two vessels fit, at the after end of her hold (one cell wide there), put looking aft
            {
                var at = l.FloorAt("hold", -8, -1);
                var p = at == null ? null : Put(at, trunk, 0);
                bool vessels = at != null && Fits(Put(at, vessel, 0)) && l.FloorAt("hold", -9, -1) is MountPoint v2 && Fits(Put(v2, vessel, 0));
                Check(vessels && p != null && Fits(p) && p.Mounts.Contains(at.Id) && p.Mounts.Count == 2 && p.Toward == 0,
                    $"note 79: a trunk goes in her hold's after end where two vessels go ({(p == null ? "no place" : p.Error ?? string.Join(" + ", p.Mounts) + $", {p.Parts[0].Block}")})");
            }

            // ---- note 74: the oven on a block put on her deck, as on the deck itself
            {
                var floor = l.FloorAt("main", -11, -1);
                var up = l.Above(floor);
                var set = new ShipPieceSet();
                if (floor != null) set.Add(ShipPiece.From(Put(floor, block, 1), set.NextId, null));
                var p = up == null ? null : Put(up, oven, 1);
                Check(p != null && Fits(p) && p.Role == PieceRole.Oven && set.Supports(l, up) && !p.Mounts.Contains(floor.Id),
                    $"note 74: an oven on top of a planks block on the steerage's deck ({(p == null ? "no place over it" : p.Error ?? string.Join(", ", p.Mounts))})");
                var hearth = new FakePiece { Code = "game:firepit", Hearth = true };
                Check(up != null && Put(up, hearth, 1).Error == "hansa:hearth-needs-floor", "note 74: the hearth still on her deck only (its flue rises from there)");
            }

            // ---- note 72: a partition on the quarterdeck's front is drawn up to the deck's top from either side of the line
            {
                var surf = SurfacesOf(def, Path.Combine(root, "assets", "hansa"));
                var waist = l.Get("b:main:-5:-3:w:2");
                var steerage = l.TwinOf(waist);
                double Top(MountPoint m) => (l.DrawnClip(m, surf) ?? m.Clip ?? new List<double[]> { new[] { 0, m.Y, 0, m.Y + 1 } }).Where(c => c[0] < -1.6 && c[2] > -2.2).Select(c => c[3]).DefaultIfEmpty(double.NaN).Max();
                Check(surf != null && waist != null && steerage != null && Math.Abs(Top(waist) - 4.125) < 1e-6 && Math.Abs(Top(steerage) - 4.125) < 1e-6,
                    $"note 72: the top partition under the quarterdeck's front drawn to its top from either side ({(waist == null || steerage == null ? "no places" : $"{waist.Id} to {Top(waist):0.###}, {steerage.Id} to {Top(steerage):0.###}")})");
                var unlike = new List<string>();
                if (surf != null)
                    foreach (var b in l.Borders.Where(b => !b.TwinSide && l.TwinOf(b) != null))
                    {
                        // (null: the whole block)
                        string Key(MountPoint m) => string.Join(";", (l.DrawnClip(m, surf) ?? m.Clip ?? new List<double[]> { Whole(m) }).OrderBy(c => c[0]).ThenBy(c => c[1]).Select(c => string.Join(",", c.Select(v => v.ToString("0.###")))));
                        if (Key(b) != Key(l.TwinOf(b))) unlike.Add(b.Id + (Environment.GetEnvironmentVariable("HANSA_BP_MAP") != null ? $" [{Key(b)}] vs [{Key(l.TwinOf(b))}] clip {b.Clip?.Count} / {l.TwinOf(b).Clip?.Count}" : ""));
                    }
                Check(unlike.Count == 0, $"note 72: every partition place and its twin across the line drawn alike ({(unlike.Count == 0 ? "all" : string.Join(", ", unlike.Take(6)))})");
            }

            // ---- note 82: a panel at her side in the hold is picked by its end too (the user aimed at it there and couldn't take it up)
            {
                var pm = l.Get("p:hold:-3:-2:n:0");
                var set = new ShipPieceSet();
                if (pm != null) set.Add(ShipPiece.From(PiecePlanner.Plan(l, pm, block, 1), set.NextId, null));
                var eye = new Vec3d(-0.993, -0.177, -1.244);
                var d = new Vec3d(-0.6899, -0.6037, -0.3994);
                var got = set.Pick(eye, d, 5, out double t);
                Check(pm != null && got?.Role == PieceRole.Panel && Math.Abs(t - 1.46) < 0.05, $"note 82: the panel by her side picked where the user aimed at it, its end ({(got == null ? "nothing" : $"{got.Role} at {t:0.##}")})");
            }

            // ---- note 57: the partition under the main deck's beam in her hold, aimed at with an axe: her own planking is behind it
            {
                var bm = l.Get("b:hold:-7:-2:w:2");
                var set = new ShipPieceSet();
                if (bm != null) set.Add(ShipPiece.From(PiecePlanner.Plan(l, bm, block, 1), set.NextId, null));
                var eye = new Vec3d(-5.751, -0.174, -0.309);
                var d = new Vec3d(-0.8352, 0.3157, -0.4504);
                var got = set.Pick(eye, d, 5, out double t);
                double behind = g.RaycastCell(eye, d, 6, out _, out _, out _, out _);
                Check(got?.Role == PieceRole.Partition && Math.Abs(t - 1.49) < 0.05 && behind > t + 0.05,
                    $"note 57: the hold's top partition is nearer than her woodwork the axe's ray meets (partition at {t:0.##}, her planking at {behind:0.##})");
            }
        }

        static double[] Whole(MountPoint m)
        {
            var (_, alongX) = MountLayout.BorderLine(m);
            double a0 = (alongX ? m.X : m.Z) - 0.5;
            return new[] { a0, m.Y, a0 + 1, m.Y + 1 };
        }

        /// <summary>Each deck's cells: '#' a floor place, '.' no deck under, 'A' something of hers over it, else what keeps it clear (first letter).</summary>
        static void FloorMap(ShipDefinition def)
        {
            var l = def.Mounts;
            var g = def.Grid;
            int n = (int)Math.Round(1 / g.V);
            foreach (var deck in def.Decks.Where(d => d.Code != "top"))
            {
                Console.WriteLine($"  -- {deck.Code} (y {deck.Y}), x from -15");
                int j0 = g.CellY(deck.Y + 1e-6);
                for (int k = -4; k <= 3; k++)
                {
                    var sb = new System.Text.StringBuilder($"  k {k,3} ");
                    var why = new List<string>();
                    for (int i = -15; i <= 12; i++)
                    {
                        double x0 = i, z0 = def.BlockGrid.Frac(2) + k;
                        if (l.FloorAt(deck.Code, i, k) != null) { sb.Append('#'); continue; }
                        int ci = g.CellX(x0 + 1e-6), ck = g.CellZ(z0 + 1e-6);
                        bool under = true, above = false;
                        for (int a = 0; a < n; a++) for (int c = 0; c < n; c++)
                        {
                            if (!g.Solid(ci + a, j0 - 1, ck + c)) under = false;
                            for (int h = 0; h < n; h++) if (g.Solid(ci + a, j0 + h, ck + c)) above = true;
                        }
                        string clear = l.ClearHit(x0, deck.Y, z0, x0 + 1, deck.Y + 1, z0 + 1);
                        sb.Append(!under ? '.' : above ? 'A' : clear != null ? char.ToLower(clear[0]) : 'T');
                        if (under && !above && clear != null) why.Add($"{i}:{clear}");
                    }
                    Console.WriteLine(sb + "  " + string.Join(" ", why));
                }
            }
        }
    }
}
