using System;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (21 October 2026) A design's places for pieces, deck by deck, as a map seen from above (bow to the right, port
        /// at the top): '#' a floor place, '+' a cell with a floor place one level up only, '.' nothing; then her wall spots
        /// by the way they face, her hooks, ladders and modular pieces.
        ///   dotnet run --project tests/HansaTests -- inspect [code]
        /// </summary>
        static void Inspect(string root, string code)
        {
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", code + ".json")));
            def.Init();
            var l = def.Mounts;
            Console.WriteLine($"{code}: {l.Floors.Count()} floor places, {l.Walls.Count()} wall spots, {l.All.Count(m => m.Kind == MountKind.Hook)} hooks, " +
                              $"{l.All.Count(m => m.Kind == MountKind.Border)} border, {l.All.Count(m => m.Kind == MountKind.Panel)} panel places");
            foreach (var deck in def.Decks)
            {
                var fl = l.Floors.Where(m => m.Deck == deck.Code).ToList();
                if (fl.Count == 0) { Console.WriteLine($"-- deck {deck.Code} (y {deck.Y}): no floor places"); continue; }
                Console.WriteLine($"-- deck {deck.Code} (y {deck.Y}): {fl.Count(m => m.Level == 0)} at level 0; rooms {string.Join(",", fl.Select(m => m.Room ?? "-").Distinct())}");
                int i0 = (int)Math.Floor(def.Grid.OX) - 1, i1 = (int)Math.Ceiling(def.Grid.MaxX) + 1;
                for (int k = -5; k <= 4; k++)
                {
                    var row = new char[i1 - i0];
                    for (int i = i0; i < i1; i++)
                    {
                        var here = fl.Where(m => Math.Floor(m.X + 1e-6) == i && Math.Floor(m.Z + 1e-6) == k).ToList();
                        row[i - i0] = here.Any(m => m.Level == 0) ? '#' : here.Any() ? '+' : '.';
                    }
                    Console.WriteLine($"  z {k,3} {new string(row)}");
                }
                Console.WriteLine($"        x from {i0} (each column one block)");
                // why the others aren't: U no deck under, A something in the block over it, K a keep-clear box, T a tread
                // (something standing on air) within a block
                var g = def.Grid;
                double ox = def.BlockGrid.Origin[0] - Math.Floor(def.BlockGrid.Origin[0]), oz = def.BlockGrid.Origin[2] - Math.Floor(def.BlockGrid.Origin[2]);
                int n = (int)Math.Round(1 / g.V), j0 = g.CellY(deck.Y + 1e-6);
                for (int k = -5; k <= 4; k++)
                {
                    var row = new char[i1 - i0];
                    for (int i = i0; i < i1; i++)
                    {
                        double x0 = ox + i, z0 = oz + k;
                        int ci = g.CellX(x0 + 1e-6), ck = g.CellZ(z0 + 1e-6);
                        bool under = true, above = false, tread = false;
                        for (int a = 0; a < n; a++) for (int c = 0; c < n; c++)
                        {
                            if (!g.Solid(ci + a, j0 - 1, ck + c)) under = false;
                            for (int h = 0; h < n; h++) if (g.Solid(ci + a, j0 + h, ck + c)) above = true;
                        }
                        for (int ii = ci - n; ii < ci + 2 * n && !tread; ii++)
                            for (int kk = ck - n; kk < ck + 2 * n && !tread; kk++)
                                for (int j = j0 + 1; j <= j0 + n + n / 4 && !tread; j++)
                                {
                                    if (!g.Solid(ii, j, kk) || g.Solid(ii, j - 1, kk) || !g.Solid(ii, j0 - 1, kk)) continue;
                                    bool fr = true;
                                    for (int h = 1; h <= n + n / 2 && fr; h++) if (g.Solid(ii, j + h, kk)) fr = false;
                                    if (fr) tread = true;
                                }
                        bool keep = (def.KeepClear ?? Array.Empty<KeepClearDef>()).Any(kc => kc.Box[0] < x0 + 1 && kc.Box[3] > x0 && kc.Box[1] < deck.Y + 1 && kc.Box[4] > deck.Y && kc.Box[2] < z0 + 1 && kc.Box[5] > z0);
                        row[i - i0] = !under ? 'U' : above ? 'A' : keep ? 'K' : tread ? 'T' : 'o';
                    }
                    Console.WriteLine($"  z {k,3} {new string(row)}");
                }
            }
            var walls = l.Walls.ToList();
            Console.WriteLine($"wall spots by facing (In): " + string.Join(", ", walls.GroupBy(w => w.In).Select(g => $"{g.Key}: {g.Count()}")));
            Console.WriteLine($"wall spots by kind of wall: " + string.Join(", ", walls.GroupBy(w => w.Wall?.Split(':')[0] ?? "-").Select(g => $"{g.Key}: {g.Count()}")));
            Console.WriteLine("partition ids (sample): " + string.Join(" ", l.All.Where(m => m.Id.StartsWith("b:")).Take(12).Select(m => m.Id)));
            // border places (partition places) per deck by the side of their anchor cell they stand on: e/w are lines
            // across her (a wall running athwartships), n/s along her
            foreach (var g2 in l.All.Where(m => m.Kind == MountKind.Border).GroupBy(m => m.Deck))
                Console.WriteLine($"  borders on {g2.Key}: " + string.Join(", ", g2.GroupBy(m => m.Id.Split(':')[4]).OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Count()}")));
            foreach (var h in l.All.Where(m => m.Kind == MountKind.Hook).Take(8)) Console.WriteLine($"  hook {h.Id} at {h.X:0.00} {h.Y:0.00} {h.Z:0.00} ({h.Hang})");
        }
    }
}
