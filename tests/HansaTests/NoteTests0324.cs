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
        /// (0.3.24) The chalk notes 176–193 on carrack-m, offline: a stair on the port mirror of a starboard stair spot on the
        /// quarterdeck (an empty gun place keeps no room), a door picked through its own solid layer (177), a wall piece aimed
        /// near a floor cell's edge (178), railings' balusters at a fixed pitch (188).
        ///   dotnet run -c Debug -- notes0324
        /// </summary>
        static void Notes0324(string root)
        {
            Console.WriteLine("0.3.24: chalk notes 176-193 (carrack-m)");
            var md = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack-m.json")));
            md.Init();
            var l = md.Mounts;
            FakePiece Stair(int cells) => new FakePiece { Code = cells == 2 ? ModularPieceDef.SteepStair : ModularPieceDef.LongStair, Family = "x", Stairs = true, StairCells = cells, StairRails = true };

            // ---- 176/181: every stair spot on the main and castle decks has its mirror (z -> -z) where no laid gun's room is
            var asym = new List<string>();
            foreach (var deck in new[] { "main", "quarter", "poop", "forecastle" })
                foreach (var m in l.Floors.Where(f => f.Deck == deck))
                    for (int dir = 0; dir < 4; dir++)
                        foreach (int cells in new[] { 2, 3 })
                        {
                            var pl = PiecePlanner.Plan(l, m, Stair(cells), dir);
                            if (pl.Error != null || PiecePlanner.GridClash(l, pl, md.Grid) != null) continue;
                            // the mirror: the cell z0' = -(z0 + 1), climbing the mirrored way
                            var mm = l.Floors.FirstOrDefault(f => f.Deck == deck && Math.Abs(f.X - m.X) < 1e-6 && Math.Abs(f.Z + m.Z + 1) < 1e-6);
                            int mdir = dir is 1 or 3 ? (dir + 2) % 4 : dir;
                            var mp = mm == null ? null : PiecePlanner.Plan(l, mm, Stair(cells), mdir);
                            bool ok = mp != null && mp.Error == null && PiecePlanner.GridClash(l, mp, md.Grid) == null;
                            if (!ok) asym.Add($"{m.Id} {cells}-long toward {MountLayout.DirCode[dir]} ({(mm == null ? "no mirror place: " + (l.ClearHit(m.X + 0.01, m.Y + 0.01, -m.Z - 0.99, m.X + 0.99, m.Y + 1.5, -m.Z - 0.01) ?? "her") : mp.Error ?? "clash")})");
                        }
            Check(asym.Count == 0, $"176/181: every stair spot on the main and castle decks has its mirror on the other side{(asym.Count > 0 ? $" ({asym.Count} not: {string.Join("; ", asym.Take(4))})" : "")}");

            // ---- 177: a door seen slanting from its solid layer's side is picked (its own layer doesn't hide it)
            {
                var e = md.ModularPieces?.FirstOrDefault(x => x.Kind == "door");
                var m = e == null ? null : l.ForModular(e);
                if (m == null) Console.WriteLine("  --   177: no door entry (waits on the generator)");
                else
                {
                    var plan = PiecePlanner.Plan(l, m, ModularFake(e, "door"), ModularToward(e, "door", m));
                    var set = new ShipPieceSet();
                    var door = ShipPiece.From(plan, 1, null);
                    set.Add(door);
                    var grid = md.Grid.Clone();
                    foreach (var sb in door.Solids) grid.Fill(sb);
                    var (line, alongX) = MountLayout.BorderLine(m);
                    // its solid layer's side: where its solids lie off the line
                    var s0 = door.Solids[0];
                    double mid = alongX ? (s0[2] + s0[5]) / 2 : (s0[0] + s0[3]) / 2;
                    int side = mid < line ? -1 : 1;
                    double a = alongX ? m.X : m.Z;
                    int shown = 0, hidden = 0, before = 0;
                    foreach (double slant in new[] { 0.0, 0.5, 0.9, 1.3 })
                    {
                        // an eye 1.2 off the line on the layer's side, slanting along it, aimed at the leaf's middle row
                        double ex = alongX ? a + slant : line + side * 1.2, ez = alongX ? line + side * 1.2 : a + slant;
                        var eye = new Vec3d(ex, m.Y + 1.6, ez);
                        var at = alongX ? new Vec3d(a, m.Y + 1.0, line) : new Vec3d(line, m.Y + 1.0, a);
                        var d = new Vec3d(at.X - eye.X, at.Y - eye.Y, at.Z - eye.Z);
                        d.Mul(1 / d.Length());
                        double wall = grid.Raycast(eye, d, 6);
                        var picked = set.Pick(eye, d, 6, out double tp);
                        if (picked == null) continue;
                        if (tp > wall + 0.3) before++;
                        bool kept = !(tp > wall + 0.3) || ShipPieceSet.OwnHit(picked, eye, d, wall);
                        if (kept) shown++; else hidden++;
                    }
                    Check(shown == 4 && hidden == 0,
                        $"177: {e.Key} seen from its solid layer's side, square on and slanting: picked from every angle ({shown} of 4; its own layer was in front {before} times)");
                }
            }

            // ---- 178: a wall piece aimed near a floor cell's edge offers the border place there first
            {
                var f = l.Floors.FirstOrDefault(m => m.Deck == "lower" && l.Borders.Any(b => b.Row == 0 && Math.Abs(b.X - (m.X + 1)) < 1e-6 && b.Z > m.Z && b.Z < m.Z + 1));
                if (f == null) Check(false, "178: a lower-deck cell with a border place on its +x edge");
                else
                {
                    var edge = new Vec3d(f.X + 0.88, f.Y, f.Z + 0.5);
                    var middle = new Vec3d(f.X + 0.5, f.Y, f.Z + 0.5);
                    var up = new double[] { 0, 1, 0 };
                    var withWall = AimPlace.Candidates(l, null, edge, up, null, null, true);
                    var withChest = AimPlace.Candidates(l, null, edge, up, null, null, false);
                    var mid = AimPlace.Candidates(l, null, middle, up, null, null, true);
                    Check(withWall.FirstOrDefault()?.Kind == MountKind.Border && Math.Abs(withWall[0].X - (f.X + 1)) < 1e-6 && withChest.FirstOrDefault() == f && mid.FirstOrDefault() == f,
                        $"178: planks aimed 0.12 from {f.Id}'s edge offer the partition place there first ({withWall.FirstOrDefault()?.Id}); a chest there, or planks at its middle, the floor place");
                }
            }

            // ---- 197: the boats on the gallows rest on its beams (found in her mesh file)
            {
                var skinPath = Path.Combine(root, "assets", "hansa", md.Shapes.Skin.Replace("hansa:", "").Replace('/', Path.DirectorySeparatorChar));
                var skin = JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(skinPath));
                var boats = md.Fixtures.Where(f => f.Kind == "boat").ToList();
                var rests = boats.Select(f => ShipFittings.BoatRests(skin, f)).ToList();
                Check(boats.Count > 0 && rests.All(r => r.Count == 2 && r[1] - r[0] > 3),
                    $"197: each boat place rests on two gallows beams ({string.Join("; ", boats.Select((f, i) => $"{f.Code} at {string.Join(", ", rests[i].Select(v => v.ToString("0.00")))}"))})");
            }

            // ---- 188: railings' balusters at a fixed pitch, cut where they don't fit; thicker
            {
                var b = l.Borders.FirstOrDefault(m => m.Row == 0 && m.Clip == null);
                var fence = new FakePiece { Code = "game:woodenfence-oak-ew-free", Family = "x", Fence = true };
                var plan = PiecePlanner.Plan(l, b, fence, b.In);
                List<double[]> Bal(double[] box) => ModularGeometry.Railing(b, null, box).Select(p => p.AsBox()).Where(x => x != null)
                    .Where(x => Math.Abs((MountLayout.BorderLine(b).Item2 ? x[3] - x[0] : x[5] - x[2]) - 2 * ModularGeometry.BalusterHalf) < 1e-6).ToList();
                var full = Bal(plan.Box);
                // a cut one: half the segment (as a clipped place draws: its box's ends)
                var (line, alongX) = MountLayout.BorderLine(b);
                double a0 = (alongX ? b.X : b.Z) - 0.5;
                var cutBox = (double[])plan.Box.Clone();
                if (alongX) cutBox[3] = cutBox[0] + 0.5; else cutBox[5] = cutBox[2] + 0.5;
                var clipped = new MountPoint { Id = b.Id, Kind = b.Kind, Deck = b.Deck, X = b.X, Y = b.Y, Z = b.Z, In = b.In, Row = b.Row, Clip = new List<double[]> { cutBox } };
                var cut = ModularGeometry.Railing(clipped, null, cutBox).Select(p => p.AsBox()).Where(x => x != null)
                    .Where(x => Math.Abs((alongX ? x[3] - x[0] : x[5] - x[2]) - 2 * ModularGeometry.BalusterHalf) < 1e-6).ToList();
                double Pos(double[] x) => (alongX ? x[0] + x[3] : x[2] + x[5]) / 2 - a0;
                var fp = full.Select(Pos).OrderBy(v => v).ToList();
                var cp = cut.Select(Pos).OrderBy(v => v).ToList();
                bool pitch = fp.Zip(fp.Skip(1), (p, q) => q - p).All(dd => Math.Abs(dd - ModularGeometry.BalusterPitch) < 1e-6);
                Check(full.Count == 3 && pitch && cp.Count >= 1 && cp.All(v => fp.Any(q => Math.Abs(q - v) < 1e-6)) && ModularGeometry.BalusterHalf * 2 >= 0.07,
                    $"188: a railing's balusters 0.07 thick at a fixed {ModularGeometry.BalusterPitch} pitch ({fp.Count} a block at {string.Join(" ", fp.Select(v => v.ToString("0.0#")))}); half a segment keeps their places, the rest cut ({cp.Count} at {string.Join(" ", cp.Select(v => v.ToString("0.0#")))})");
            }
        }
    }
}
