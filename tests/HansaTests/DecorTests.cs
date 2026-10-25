using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>A vanilla block as the planner sees it, made up for the tests (no game to read one from).</summary>
        class FakePiece : PieceBlock
        {
            public string Family;
            public string[] States = Array.Empty<string>();
            public Dictionary<string, List<int[]>> CellsBy = new();
            public double[] Sel = { 0, 0, 0, 1, 1, 1 }, Col = { 0, 0, 0, 1, 1, 1 };

            public override string Variant(string state) => Array.IndexOf(States, state) >= 0 ? Family + "-" + state : null;
            public override List<int[]> Cells(string variant)
            {
                string side = variant?.Substring(variant.LastIndexOf('-') + 1) ?? "";
                return CellsBy.TryGetValue(side, out var c) ? c : new List<int[]> { new[] { 0, 0, 0 } };
            }
            public override double[][] Boxes(string variant, bool collision) => collision ? (Col == null ? Array.Empty<double[]>() : new[] { Col }) : new[] { Sel };
            public override string BedPart(string part, string side) => Family + "-" + part + "-" + side;
        }

        static readonly string[] Sides = { "north", "east", "south", "west" };

        /// <summary>Decorating: mount points, placing pieces (orientation, footprints), saving them, paint zones and their cost.</summary>
        static void Decor(ShipDefinition def, string root)
        {
            Console.WriteLine("decorating: mount points, pieces, paint");
            var l = def.Mounts;
            var g = def.Grid;
            foreach (var kv in l.Counts()) Console.WriteLine($"         {kv.Key}: {kv.Value}");
            Console.WriteLine($"         alignments: {string.Join(", ", l.Align.Select(a => $"{a.Key} ({a.Value.X:0.00}, {a.Value.Z:0.00})"))}");
            // for looking at them (python, scratch): HANSA_DECOR_DUMP=<file.json>
            string dump = Environment.GetEnvironmentVariable("HANSA_DECOR_DUMP");
            if (!string.IsNullOrEmpty(dump))
            {
                File.WriteAllText(dump, JsonConvert.SerializeObject(new
                {
                    mounts = l.All.Select(m => new { m.Id, kind = m.Kind.ToString(), m.Room, m.Deck, m.Box, m.Normal, m.In, m.X, m.Y, m.Z }),
                    clear = l.Clear.Select(c => new { c.Why, c.Box, c.Round, c.CX, c.CZ, c.R })
                }));
            }

            // ---- mount points: everywhere free, on every deck, in the rooms' walls, the hooks
            {
                int Floors(string room) => l.Floors.Count(m => m.Room == room);
                int Walls(string room) => l.Walls.Count(m => m.Room == room);
                // (0.3.12: the walled companion stair is its own room, a stairwell: nothing goes on its floor)
                var rooms = def.Rooms.Where(r => r.Enclosed && r.Code != "companion").Select(r => r.Code).ToList();
                bool everyRoom = rooms.All(r => Floors(r) > 0);
                bool openDecks = def.Decks.Where(d => d.Code != "top").All(d => l.Floors.Any(m => m.Deck == d.Code));
                Check(everyRoom && openDecks && l.Floors.Count() >= 200,
                    $"floor places on every deck and in every room: {string.Join(", ", rooms.Select(r => $"{r} {Floors(r)}"))}; open decks " +
                    string.Join(", ", def.Decks.Where(d => d.Code != "top").Select(d => $"{d.Code} {l.Floors.Count(m => m.Deck == d.Code && m.Room == d.Code + "-deck")}")) +
                    $" ({l.Floors.Count()} in all)");
                var walled = new[] { "greatcabin", "poopcabin", "lowerdeck", "forecastle" }.Where(r => rooms.Contains(r)).ToList();
                Check(walled.All(r => Walls(r) >= 4), $"wall spots in the cabins, the forecastle and on the lower deck: {string.Join(", ", walled.Select(r => $"{r} {Walls(r)}"))} ({l.Walls.Count()} in all)");
                int lanterns = def.Fixtures.Count(f => ShipMounts.IsLegacyHook(f)) + (def.Hooks?.Length ?? 0);
                // (0.3.16, note 161: and the ceiling hooks the mod finds between the beams, h:c:...)
                bool Found(MountPoint m) => MountLayout.IsCeilingHook(m.Id) || MountLayout.IsMastHook(m.Id);
                Check(l.Hooks.Count(m => !Found(m)) == lanterns && l.Hooks.Count(m => m.Shown) == (def.Fixture("lantern-stern") != null || def.Hooks?.Any(h => h.Shown) == true ? 1 : 0),
                    $"hooks: the design's and its lantern places ({l.Hooks.Count(m => !Found(m))}, and {l.Hooks.Count(m => MountLayout.IsCeilingHook(m.Id))} under the decks, {l.Hooks.Count(m => MountLayout.IsMastHook(m.Id))} on the masts), the stern lantern's shown empty");
                // (0.3.24, note 193) lantern hooks on the masts' sides over the open decks, clear of her and of the design's hooks
                var mastHooks = l.Hooks.Where(m => MountLayout.IsMastHook(m.Id)).ToList();
                Check(mastHooks.Count >= 6 && def.Masts.All(ms => mastHooks.Any(h => h.Id.StartsWith($"h:m:{ms.Code}:"))) &&
                      mastHooks.All(h => !def.Grid.Collides(h.Box[0] + 0.1, h.Box[1], h.Box[2] + 0.1, h.Box[3] - 0.1, h.Box[4] - 0.01, h.Box[5] - 0.1)) &&
                      mastHooks.All(h => !l.Hooks.Any(o => o != h && !MountLayout.IsMastHook(o.Id) && Math.Abs(o.X - h.X) < 0.6 && Math.Abs(o.Z - h.Z) < 0.6 && Math.Abs(o.Y - h.Y) < 1)),
                    $"0.3.24: lantern hooks on every mast's sides ({mastHooks.Count}: {string.Join(", ", mastHooks.Take(6).Select(h => h.Id))}...), clear of her and of the other hooks");

                // each floor place: solid floor under all of it, a block of room over it, not in anything kept clear
                bool floorsOk = l.Floors.All(m => !g.Collides(m.X + 0.01, m.Y + 0.01, m.Z + 0.01, m.X + 0.99, m.Y + 0.99, m.Z + 0.99) &&
                    g.SolidAt(m.X + 0.1, m.Y - 0.1, m.Z + 0.1) && g.SolidAt(m.X + 0.9, m.Y - 0.1, m.Z + 0.9) && g.SolidAt(m.X + 0.1, m.Y - 0.1, m.Z + 0.9) && g.SolidAt(m.X + 0.9, m.Y - 0.1, m.Z + 0.1) &&
                    l.ClearHit(m.Box[0], m.Box[1], m.Box[2], m.Box[3], m.Box[4], m.Box[5]) == null);
                Check(floorsOk, "every floor place stands on her deck with a block of room over it, clear of everything kept clear");
                // each wall spot: her wall right behind it, the space in front free
                // (a spot on a panel has the panel behind it, once it's up: the panels' own checks)
                var offWall = l.Walls.Where(m => m.Under == null).Where(m =>
                {
                    // her wall right behind the spot's face (within the half block a wall may be uneven), the space before it free
                    bool behind = Enumerable.Range(1, 6).Any(n => g.SolidAt(m.X - MountLayout.DX[m.In] * 0.1 * n, m.Y + 0.5, m.Z - MountLayout.DZ[m.In] * 0.1 * n));
                    return !behind || g.Collides(m.Box[0] + 0.02, m.Box[1] + 0.02, m.Box[2] + 0.02, m.Box[3] - 0.02, m.Box[4] - 0.02, m.Box[5] - 0.02) ||
                           Math.Abs(m.Normal[0] * MountLayout.DX[m.In] + m.Normal[2] * MountLayout.DZ[m.In]) < 0.9;
                }).Select(m => m.Id).ToList();
                Check(offWall.Count == 0, $"every wall spot is on her wall (the wall right behind it, square to it within 25°), the space before it free ({(offWall.Count == 0 ? "all" : "not: " + string.Join(", ", offWall.Take(8)))})");

                // what's kept clear: doorways and a block each side, ladders' heads and feet, masts, the capstan's sweep,
                // the guns' room, hatches, the helm's standing spot, stairs
                var bad = new List<string>();
                bool Near(MountPoint m, double x, double z, double r) => Math.Max(Math.Abs(m.CX - x) - 0.5, 0) < r && Math.Max(Math.Abs(m.CZ - z) - 0.5, 0) < r;
                foreach (var f in def.Fixtures)
                {
                    if (f.Type == "door" && f.Box != null)
                        foreach (var m in l.Floors.Where(m => Math.Abs(m.Y - f.Box[1]) < 0.3))
                            if (PiecePlanner.Overlap(m.Box, f.Normal == "z" ? new[] { f.Box[0], f.Box[1], f.Box[2] - 1, f.Box[3], f.Box[4], f.Box[5] + 1 }
                                                                             : new[] { f.Box[0] - 1, f.Box[1], f.Box[2], f.Box[3] + 1, f.Box[4], f.Box[5] }))
                                bad.Add($"{m.Id} in {f.Code}'s doorway");
                    if (f.Type == "anchor")
                        foreach (var m in l.Floors.Where(m => Math.Abs(m.Y - f.Pos[1]) < 0.3))
                        {
                            double dx = Math.Max(Math.Abs(m.CX - f.Pos[0]) - 0.5, 0), dz = Math.Max(Math.Abs(m.CZ - f.Pos[2]) - 0.5, 0);
                            if (dx * dx + dz * dz < 1.3 * 1.3) bad.Add($"{m.Id} in the capstan's sweep");
                        }
                    if (f.Type == "gun" && f.Seat != null)
                        foreach (var m in l.Floors.Where(m => Math.Abs(m.Y - f.Pos[1]) < 0.3))
                            if (Near(m, f.Seat[0], f.Seat[2], 0.4) || f.Box != null && PiecePlanner.Overlap(m.Box, f.Box)) bad.Add($"{m.Id} on {f.Code}");
                    if (f.Type == "hatch" && (f.Sel ?? f.Box) is double[] hb)
                        foreach (var m in l.Floors)
                            if (PiecePlanner.Overlap(m.Box, new[] { hb[0] - 0.5, hb[1] - 0.5, hb[2] - 0.5, hb[3] + 0.5, hb[4] + 1.5, hb[5] + 0.5 })) bad.Add($"{m.Id} by {f.Code}");
                    if (f.Type == "helm")
                        foreach (var m in l.Floors.Where(m => Math.Abs(m.Y - f.Pos[1]) < 0.3))
                            if (Near(m, f.Pos[0], f.Pos[2], 0.6)) bad.Add($"{m.Id} on the helm's spot");
                }
                foreach (var ld in def.Ladders)
                    foreach (var e in new[] { ld.ExitBottom, ld.ExitTop })
                    {
                        if (e == null || ld.Outside && e == ld.ExitBottom) continue;
                        foreach (var m in l.Floors.Where(m => Math.Abs(m.Y - e[1]) < 0.3))
                            if (Near(m, e[0], e[2], 0.5)) bad.Add($"{m.Id} at {ld.Code}'s end");
                    }
                foreach (var mast in def.Masts)
                    foreach (var m in l.Floors)
                    {
                        // (note 101: on the grid only the mast's own cell is kept clear: a place may stand right beside it)
                        double dx = Math.Max(Math.Abs(m.CX - mast.X) - 0.5, 0), dz = Math.Max(Math.Abs(m.CZ - mast.Z) - 0.5, 0);
                        if (m.Y > mast.Y0 && m.Y < mast.Y1 && dx * dx + dz * dz < (mast.Radius - 0.01) * (mast.Radius - 0.01)) bad.Add($"{m.Id} on the {mast.Code}mast");
                    }
                // a stair's treads: solid standing on air within a block and a quarter of a deck (found in her grid)
                foreach (var deck in def.Decks.Where(d => d.Code != "top"))
                {
                    int j0 = g.CellY(deck.Y + 0.01);
                    for (int i = 0; i < g.NX; i++)
                        for (int k = 0; k < g.NZ; k++)
                            for (int j = j0 + 1; j <= j0 + 4; j++)
                            {
                                if (!g.Solid(i, j, k) || g.Solid(i, j - 1, k) || !g.Solid(i, j0 - 1, k)) continue;
                                if (Enumerable.Range(1, 6).Any(h => g.Solid(i, j + h, k))) continue;
                                double x = g.OX + (i + 0.5) * g.V, z = g.OZ + (k + 0.5) * g.V;
                                foreach (var m in l.Floors.Where(m => m.Deck == deck.Code))
                                    if (Near(m, x, z, 0.5)) bad.Add($"{m.Id} by a tread at {x:0.00},{z:0.00}");
                            }
                }
                bad = bad.Distinct().ToList();
                Check(bad.Count == 0, $"nothing in the way of doorways, the capstan, the guns, hatches, the helm, ladders, masts, stairs ({(bad.Count == 0 ? "none" : string.Join("; ", bad.Take(8)))})");

                var again = MountLayout.Build(def);
                bool same = again.All.Count == l.All.Count && again.All.Zip(l.All).All(p => p.First.Id == p.Second.Id && p.First.Box.SequenceEqual(p.Second.Box));
                Check(same && l.All.Select(m => m.Id).Distinct().Count() == l.All.Count, $"worked out again, the same {l.All.Count} mount points with the same ids (every id once)");
            }

            // ---- placing: the way it faces, the cells it takes
            {
                var floor = l.Floors.Where(m => m.Deck == "lower" && l.FloorAt(m.Deck, m.I + 1, m.K) != null && l.FloorAt(m.Deck, m.I - 1, m.K) != null &&
                                                l.FloorAt(m.Deck, m.I, m.K + 1) != null && l.FloorAt(m.Deck, m.I, m.K - 1) != null).First();
                var chest = new FakePiece { Code = "game:chest-east", Family = "game:chest", Container = true, TurnMesh = true, Slots = 16, Sel = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 }, Col = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 } };
                bool ok = true;
                var quarters = new List<int>();
                for (int dir = 0; dir < 4; dir++)
                {
                    var p = PiecePlanner.Plan(l, floor, chest, dir);
                    quarters.Add(p.Parts[0].Quarter);
                    ok &= p.Error == null && p.Mounts.Count == 1 && p.Role == PieceRole.Storage && p.Box[1] == floor.Y && p.Box[0] >= floor.X && p.Box[3] <= floor.X + 1;
                }
                // a chest's front (+z unturned) faces the one who put it: toward +x is a quarter turn, toward -x three
                Check(ok && quarters.SequenceEqual(new[] { 1, 0, 3, 2 }), $"a chest on a floor place: one place, a storage, its front turned toward whoever put it (quarter turns for +x, +z, -x, -z: {string.Join(", ", quarters)})");

                var trunk = new FakePiece
                {
                    Code = "game:trunk-east", Family = "game:trunk", Orient = PieceOrient.Orientable, States = Sides, Container = true, TurnMesh = true, Slots = 36,
                    CellsBy = new()
                    {
                        ["north"] = new() { new[] { 0, 0, 0 }, new[] { 1, 0, 0 } }, ["east"] = new() { new[] { 0, 0, 0 }, new[] { 0, 0, 1 } },
                        ["south"] = new() { new[] { 0, 0, 0 }, new[] { -1, 0, 0 } }, ["west"] = new() { new[] { 0, 0, 0 }, new[] { 0, 0, -1 } },
                    }
                };
                var t1 = PiecePlanner.Plan(l, floor, trunk, 1);     // put from +z: looking north
                var t0 = PiecePlanner.Plan(l, floor, trunk, 0);     // put from +x: looking west
                Check(t1.Error == null && t1.Parts[0].Block == "game:trunk-north" && t1.Mounts.Count == 2 && t1.Mounts[1] == l.FloorAt("lower", floor.I + 1, floor.K).Id &&
                      t1.Parts[0].Quarter == 0 && t0.Parts[0].Block == "game:trunk-west" && t0.Mounts[1] == l.FloorAt("lower", floor.I, floor.K - 1).Id && t0.Parts[0].Quarter == 1 &&
                      Math.Abs(t1.Box[3] - t1.Box[0] - 2) < 1e-9,
                    "a trunk (vanilla's multiblock): the side the placer looks toward, two places across their view, its mesh turned to face them");

                var bed = new FakePiece { Code = "game:bed-wood-head-north", Family = "game:bed-wood", Orient = PieceOrient.Bed, SleepEfficiency = 0.70833f, Sel = new[] { 0, 0, 0, 1, 0.5625, 1.0 } };
                var b1 = PiecePlanner.Plan(l, floor, bed, 1);
                Check(b1.Error == null && b1.Role == PieceRole.Bed && b1.Parts[0].Block == "game:bed-wood-feet-south" && b1.Parts[1].Block == "game:bed-wood-head-south" &&
                      b1.Mounts[1] == l.FloorAt("lower", floor.I, floor.K - 1).Id && Math.Abs(b1.Seat[2] - floor.Z) < 1e-9 && b1.SeatYaw == 90 && b1.Efficiency > 0.7f,
                    "a bed: its feet where it's put, its head a block on away from the placer (vanilla's bed-<wood>-head/feet-<side toward them>), slept in between, the head the far way");
                var wallSpot = l.Walls.First(m => m.In == 1 && l.WallAt(m.Wall, m.I + 1, m.Row) != null);
                var painting = new FakePiece
                {
                    Code = "game:painting-trout-north", Family = "game:painting-trout", Orient = PieceOrient.Attachable, States = Sides, Col = null,
                    Sel = new[] { 0, 0, 0, 1, 1, 0.0625 },
                    CellsBy = new() { ["north"] = new() { new[] { 0, 0, 0 }, new[] { 1, 0, 0 } }, ["south"] = new() { new[] { 0, 0, 0 }, new[] { -1, 0, 0 } },
                                      ["east"] = new() { new[] { 0, 0, 0 }, new[] { 0, 0, 1 } }, ["west"] = new() { new[] { 0, 0, 0 }, new[] { 0, 0, -1 } } }
                };
                var pw = PiecePlanner.Plan(l, wallSpot, painting, 3);
                var pf = PiecePlanner.Plan(l, floor, painting, 1);
                Check(pw.Error == null && pw.Parts[0].Block == "game:painting-trout-north" && pw.Mounts.Count == 2 && pw.Mounts[1] == l.WallAt(wallSpot.Wall, wallSpot.I + 1, wallSpot.Row).Id &&
                      Math.Abs(pw.Box[2] - wallSpot.Z) < 1e-9 && pw.Solids.Count == 0 && pw.Tilt != null && pf.Error == "hansa:piece-needs-wall",
                    $"a 2 × 1 painting on a wall spot ({wallSpot.Id}): the side toward the wall, flush on its face, the next spot along the wall too, not solid; on a floor refused");
                var lantern = new FakePiece { Code = "game:lantern-small-up", Family = "game:lantern-small", Orient = PieceOrient.Omni, OmniWalls = true, Light = true, States = new[] { "up", "down", "north", "east", "south", "west" } };
                var hook = l.Hooks.First(m => m.Hang == "down");
                var lh = PiecePlanner.Plan(l, hook, lantern, 0);
                var lf = PiecePlanner.Plan(l, floor, lantern, 0);
                var lw = PiecePlanner.Plan(l, wallSpot, lantern, 0);
                Check(lh.Error == null && lh.Parts[0].Block == "game:lantern-small-down" && lh.Role == PieceRole.Light && lh.Solids.Count == 0 && lh.Hang == "down" &&
                      lf.Parts[0].Block == "game:lantern-small-up" && lw.Parts[0].Block == "game:lantern-small-south" && PiecePlanner.Plan(l, hook, chest, 0).Error == "hansa:piece-hook-light",
                    "a lantern (omni-attachable): hung from a hook (down, not solid), standing on a floor (up), on a wall its side away from the wall; a chest on a hook refused");
                var chair = new FakePiece { Code = "game:chair-plain", Family = "game:chair", Seat = true, Sel = new[] { 0.125, 0, 0.125, 0.875, 0.5, 0.875 } };
                var ch = PiecePlanner.Plan(l, floor, chair, 2);
                Check(ch.Role == PieceRole.Seat && Math.Abs(ch.Seat[1] - floor.Y - 0.5) < 1e-9 && ch.SeatYaw == 180 && ch.Parts[0].Quarter == 3,
                    "a chair: sat on at its seat's height, facing the way it faces (toward the one who put it)");
                var antlers = new FakePiece { Code = "game:antlermount-oak", Family = "game:antlermount", Sel = new[] { 0.25, 0.25, 0.0, 0.75, 0.75, 0.25 } };
                var aw = PiecePlanner.Plan(l, wallSpot, antlers, 3);
                Check(aw.Error == null && aw.Parts[0].Quarter == PiecePlanner.QuarterFor(wallSpot.In) && Math.Abs(aw.Box[2] - wallSpot.Z) < 1e-9 && aw.Box[5] < wallSpot.Z + 0.3 && PiecePlanner.Plan(l, wallSpot, chest, 3).Error == "hansa:piece-not-wall",
                    "a block with no variant of its own on a wall spot: its front turned into the room (its back against the wall); a chest on a wall refused");
                // the boxes turn with the mesh: a box against a block's south side (its front, +z), turned a quarter, is against its east side (+x)
                var tb = PiecePlanner.Turn(new[] { 0, 0, 0.75, 1, 1, 1 }, 1);
                Check(Math.Abs(tb[0] - 0.75) < 1e-9 && Math.Abs(tb[3] - 1) < 1e-9 && Math.Abs(tb[2]) < 1e-9 && Math.Abs(tb[5] - 1) < 1e-9,
                    "a box turned a quarter as Matrixf.RotateY turns a mesh (its south side to the east)");

                // ---- saved and loaded: the pieces, which places they take, their storage's contents
                var set = new ShipPieceSet();
                var blockChest = new Block { BlockId = 101, Code = new AssetLocation("game", "chest-east") };
                var stick = new Item(7) { Code = new AssetLocation("game", "stick") };
                var pc = ShipPiece.From(PiecePlanner.Plan(l, floor, chest, 1), set.NextId, new ItemStack(blockChest, 3));
                set.Add(pc);
                // (a trunk well away from the chest: two places of its own)
                var trunkAt = l.Floors.First(m => m.Deck == "lower" && Math.Abs(m.I - floor.I) >= 3 && l.FloorAt("lower", m.I, m.K - 1) != null);
                set.Add(ShipPiece.From(PiecePlanner.Plan(l, trunkAt, trunk, 0), set.NextId, new ItemStack(blockChest)));
                set.Add(ShipPiece.From(lh, set.NextId, new ItemStack(new Block { BlockId = 102, Code = new AssetLocation("game", "lantern-small-up") })));
                var tree = new TreeAttribute();
                set.ToTree(tree);
                var bytes = tree.ToBytes();
                var back = new TreeAttribute();
                back.FromBytes(bytes);
                var loaded = new ShipPieceSet();
                loaded.FromTree(back);
                bool sameSet = loaded.Count == set.Count && loaded.NextId == set.NextId && set.All.Zip(loaded.All).All(p =>
                    p.First.Id == p.Second.Id && p.First.Mount == p.Second.Mount && p.First.Mounts.SequenceEqual(p.Second.Mounts) && p.First.Dir == p.Second.Dir &&
                    p.First.Role == p.Second.Role && p.First.Parts.Count == p.Second.Parts.Count && p.First.Parts[0].Block == p.Second.Parts[0].Block &&
                    p.First.Parts[0].Origin.SequenceEqual(p.Second.Parts[0].Origin) && p.First.Parts[0].Quarter == p.Second.Parts[0].Quarter &&
                    p.First.Sel.Count == p.Second.Sel.Count && p.First.Box.SequenceEqual(p.Second.Box) && p.First.Solids.Count == p.Second.Solids.Count &&
                    p.First.Hang == p.Second.Hang && p.First.Stack.Id == p.Second.Stack.Id && p.Second.Stack.StackSize == 1);
                Check(sameSet && loaded.Taken(floor.Id) && loaded.At(hook.Id)?.Id == 3 && loaded.Blocking(PiecePlanner.Plan(l, floor, chest, 0)) != null && pc.Stack.StackSize == 1,
                    "pieces saved and loaded (a chest, a trunk, a lantern): ids, places, faces, blocks, boxes; one item each; a place taken stays taken");
                loaded.Remove(1);
                Check(!loaded.Taken(floor.Id) && loaded.Count == 2, "taken up, its place is free again");

                // ---- stacking (0.3.9): a piece with a top to stand on makes the place over it a floor place
                {
                    var up = l.Above(floor);
                    var full = new FakePiece { Code = "game:cabinet", Family = "game:cabinet", Container = true, Slots = 8 };
                    var lanternPc = new FakePiece { Code = "game:lantern-small-up", Family = "game:lantern-small", Orient = PieceOrient.Omni, Light = true, States = new[] { "up", "down" },
                                                    Sel = new[] { 0.3, 0, 0.3, 0.7, 0.5, 0.7 }, Col = new[] { 0.3, 0, 0.3, 0.7, 0.5, 0.7 } };
                    var s = new ShipPieceSet();
                    bool noneYet = up != null && !s.Supports(l, up);
                    var bottom = ShipPiece.From(PiecePlanner.Plan(l, floor, chest, 1), s.NextId, new ItemStack(blockChest));
                    s.Add(bottom);
                    bool onChest = s.Supports(l, up);
                    var top = PiecePlanner.Plan(l, up, full, 1);
                    bool topOk = top.Error == null && top.Mounts.SequenceEqual(new[] { up.Id }) && Math.Abs(top.Parts[0].Origin[1] - floor.Y - 1) < 1e-9 &&
                                 Math.Abs(top.Box[1] - floor.Y - 1) < 1e-9 && s.Blocking(top) == null;
                    var topPiece = ShipPiece.From(top, s.NextId, new ItemStack(blockChest));
                    s.Add(topPiece);
                    bool under = s.StandingOn(l, bottom)?.Id == topPiece.Id && s.StandingOn(l, topPiece) == null;
                    // the third: on the cabinet, where she has the room
                    var up2 = l.Above(up);
                    bool third = up2 == null || s.Supports(l, up2);
                    // saved and loaded: the stacked place's id, standing on the chest still
                    var st = new TreeAttribute();
                    s.ToTree(st);
                    var sb = new TreeAttribute();
                    sb.FromBytes(st.ToBytes());
                    var s2 = new ShipPieceSet();
                    s2.FromTree(sb);
                    bool kept = s2.At(up.Id)?.Id == topPiece.Id && s2.StandingOn(l, s2.Get(bottom.Id))?.Id == topPiece.Id;
                    // a lantern (no top to stand on) holds nothing; nothing stands on a free place
                    var s3 = new ShipPieceSet();
                    s3.Add(ShipPiece.From(PiecePlanner.Plan(l, floor, lanternPc, 1), s3.NextId, null));
                    bool notOnLantern = !s3.Supports(l, up);
                    // (0.3.12, note 124) on vanilla's cabinet (1 wide, 0.6875 deep, a block high): a second cabinet
                    var cab = new FakePiece { Code = "game:cabinet-oak", Family = "game:cabinet", Sel = new[] { 0, 0, 0, 1, 1, 0.6875 }, Col = new[] { 0, 0, 0, 1, 1, 0.6875 } };
                    var s4 = new ShipPieceSet();
                    s4.Add(ShipPiece.From(PiecePlanner.Plan(l, floor, cab, 1), s4.NextId, null));
                    var s5 = new ShipPieceSet();
                    s5.Add(ShipPiece.From(PiecePlanner.Plan(l, floor, cab, 0), s5.NextId, null));
                    var onCabinet = PiecePlanner.Plan(l, up, cab, 1);
                    bool cabinets = s4.Supports(l, up) && s5.Supports(l, up) && onCabinet.Error == null && s4.Blocking(onCabinet) == null;
                    Check(cabinets, "a cabinet (0.6875 deep) holds a second cabinet on its top, turned either way (note 124: its top wasn't a place to put things)");
                    s.Remove(topPiece.Id);
                    bool freed = s.StandingOn(l, bottom) == null;
                    bool ids = l.Stacked.All(m => m.Id == m.Under + ":" + m.Level || m.Id.EndsWith(":" + m.Level) && l.Get(m.Under)?.Level == m.Level - 1) &&
                               l.Stacked.All(m => !g.Collides(m.X + 0.01, m.Y + 0.01, m.Z + 0.01, m.X + 0.99, m.Y + 0.99, m.Z + 0.99) && Math.Abs(m.Y - l.Get(m.Under).Y - 1) < 1e-9);
                    Check(noneYet && onChest && topOk && under && third && kept && notOnLantern && freed && ids,
                        $"stacking: the place over {floor.Id} ({up?.Id}) holds only while a piece with a top stands under it (a chest: yes, a lantern: no); a cabinet " +
                        $"on the chest stands a block up (as vanilla stacks), the chest can't be taken from under it; saved and loaded; {l.Stacked.Count()} such places, each with room in her");
                }

                // ---- the ship's one block grid (0.3.9): with the design's grid origin every floor place and wall spot is on it
                {
                    string json = File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", def.Code + ".json"));
                    var gd = JsonConvert.DeserializeObject<ShipDefinition>(json);
                    bool theirs = gd.BlockGrid?.Origin?.Length >= 3;
                    if (!theirs) gd.BlockGrid = new BlockGridDef { Origin = new[] { 0.0, 0.125, 0.5 } };
                    gd.Init();
                    var gl = gd.Mounts;
                    double ox = gd.BlockGrid.Origin[0], oz = gd.BlockGrid.Origin[2];
                    bool Whole(double v) => Math.Abs(v - Math.Round(v)) < 1e-6;
                    var offFloor = gl.Floors.Concat(gl.Stacked).Where(m => !Whole(m.X - ox) || !Whole(m.Z - oz)).Select(m => m.Id).ToList();
                    var offWall = gl.Walls.Where(m => m.In == 1 || m.In == 3 ? !Whole(m.X - 0.5 - ox) : !Whole(m.Z - 0.5 - oz)).Select(m => m.Id).ToList();
                    var offDeck = gd.Decks.Where(d => d.Code != "top" && !Whole(d.Y - gd.BlockGrid.Origin[1])).Select(d => $"{d.Code} {d.Y}").ToList();
                    Console.WriteLine($"         on the ship's grid ({(theirs ? "the design's" : "a test origin")} [{string.Join(", ", gd.BlockGrid.Origin)}]): " +
                        $"{gl.Floors.Count()} floor places, {gl.Walls.Count()} wall spots, {gl.Stacked.Count()} stacked; decks off its y: {(offDeck.Count == 0 ? "none" : string.Join(", ", offDeck))}");
                    Check(gl.OnShipGrid && gl.Align.Values.Distinct().Count() == 1 && gl.Floors.Any() && gl.Walls.Any() && offFloor.Count == 0 && offWall.Count == 0 && (!theirs || offDeck.Count == 0),
                        $"with a grid origin: one alignment on every deck, every floor place and wall spot on the grid (off: {(offFloor.Count + offWall.Count == 0 ? "none" : string.Join(", ", offFloor.Concat(offWall).Take(6)))})" +
                        (theirs ? ", every deck's floor on a block border" : ""));
                    // nothing hung at an angle on the grid: every wall spot upright (a bulkhead, or a panel's face)
                    var tilted = gl.Walls.Where(m => Math.Abs(m.Normal[1]) > 1e-9 || Math.Abs(m.In == 0 || m.In == 2 ? m.Normal[2] : m.Normal[0]) > 1e-9).Select(m => m.Id).ToList();
                    // the panels: each at a room's outermost cell, its face on the cell's border, a wedge of her side behind it
                    // (part free, part her), clear of what's kept clear; its wall spot needs it
                    var gg = gd.Grid;
                    var badPanels = new List<string>();
                    foreach (var pm in gl.Panels)
                    {
                        int dirOut = (pm.In + 2) % 4;
                        var cell = gl.Floors.FirstOrDefault(f => f.Deck == pm.Deck && f.I == pm.I && f.K == pm.K);
                        bool outermost = cell != null && gl.FloorAt(pm.Deck, pm.I + MountLayout.DX[dirOut], pm.K + MountLayout.DZ[dirOut]) == null;
                        double line = pm.In == 0 || pm.In == 2 ? pm.X : pm.Z;
                        bool onBorder = cell != null && (pm.In == 0 || pm.In == 2 ? Math.Abs(line - (dirOut == 0 ? cell.X + 1 : cell.X)) < 1e-9 : Math.Abs(line - (dirOut == 1 ? cell.Z + 1 : cell.Z)) < 1e-9);
                        // (free just behind its face somewhere, her side within the block beyond: its depth; clear of what's kept clear)
                        bool wedge = pm.Depth > 0 && pm.Depth < MountLayout.MaxPanelDepth && gl.ClearHit(pm.Box[0], pm.Box[1], pm.Box[2], pm.Box[3], pm.Box[4], pm.Box[5]) == null;
                        bool spot = gl.Walls.Count(w => w.Under == pm.Id) <= 1 && (pm.Row == 0 ? pm.Under == null : pm.Under == null || gl.Get(pm.Under)?.Row == 0);
                        if (!(outermost && onBorder && wedge && spot && pm.Row < MountLayout.PanelRows)) badPanels.Add(pm.Id);
                    }
                    Console.WriteLine($"         panels: {gl.Panels.Count()} places ({string.Join(", ", gl.Panels.GroupBy(p => p.Room).Select(x => $"{x.Key} {x.Count()}"))}), " +
                        $"{gl.Walls.Count(w => w.Under != null)} wall spots on them, {gl.Walls.Count(w => w.Under == null)} on upright walls");
                    Check(tilted.Count == 0 && gl.Panels.Any() && badPanels.Count == 0,
                        $"on the grid nothing hangs at an angle (tilted: {(tilted.Count == 0 ? "none" : string.Join(", ", tilted.Take(5)))}); panel places at the rooms' outermost cells, " +
                        $"each face on the cell's border with a wedge of her side behind it, a block high, the upper on the lower (wrong: {(badPanels.Count == 0 ? "none" : string.Join(", ", badPanels.Take(5)))})");
                    // a planks block on one: a panel; then its face takes a painting; it isn't taken down from under that
                    var pp = gl.Panels.First(p => p.Row == 0 && gl.Walls.Any(w => w.Under == p.Id));
                    var planksB = new FakePiece { Code = "game:planks-oak-ud", Family = "game:planks-oak", Planks = true };
                    var plan = PiecePlanner.Plan(gl, pp, planksB, 0);
                    var refused = PiecePlanner.Plan(gl, pp, chest, 0);
                    var ps = new ShipPieceSet();
                    var spotOn = gl.Walls.First(w => w.Under == pp.Id);
                    bool spotShut = !ps.Supports(gl, spotOn);
                    var panelPc = ShipPiece.From(plan, ps.NextId, null);
                    ps.Add(panelPc);
                    int outAxis = pp.In == 0 || pp.In == 2 ? 0 : 2;
                    double faceLine = outAxis == 0 ? pp.X : pp.Z;
                    bool flush = Math.Abs((pp.In < 2 ? plan.Box[outAxis + 3] : plan.Box[outAxis]) - faceLine) < 1e-9 && plan.Role == PieceRole.Panel && plan.Solids.Count == 1 &&
                                 Math.Abs(plan.Solids[0][1] - pp.Y) < 1e-9 && Math.Abs(plan.Solids[0][4] - pp.Y - 1) < 1e-9;
                    var pic = new FakePiece { Code = "game:painting-trout-north", Family = "game:painting-trout", Orient = PieceOrient.Attachable, States = Sides, Col = null, Sel = new[] { 0, 0, 0, 1, 1, 0.0625 } };
                    var onPanel = PiecePlanner.Plan(gl, spotOn, pic, spotOn.In);
                    bool hangs = ps.Supports(gl, spotOn) && onPanel.Error == null && onPanel.Tilt != null && Math.Abs(onPanel.Tilt[1]) < 1e-9;
                    ps.Add(ShipPiece.From(onPanel, ps.NextId, null));
                    bool held = ps.StandingOn(gl, panelPc) != null;
                    var wallpaper = new FakePiece { Code = "game:wallpaper-brown", Family = "game:wallpaper", Overlay = true, Col = null };
                    var wpFloor = PiecePlanner.Plan(gl, cellOf(pp), wallpaper, 0);
                    MountPoint cellOf(MountPoint p) => gl.FloorAt(p.Deck, p.I, p.K);
                    Check(plan.Error == null && refused.Error == "hansa:panel-needs-planks" && spotShut && flush && hangs && held && wpFloor.Error == "hansa:piece-needs-wall",
                        $"a planks block on {pp.Id}: a panel, its face on the border, the wedge behind it filled a block high; only planks panel it; its wall spot ({spotOn.Id}) " +
                        "takes a painting (upright) only while the panel is up, and the panel isn't taken down from under it; wallpaper only on a wall");

                    // ---- partitions and doors (notes 78, 98): border places, a partition from planks, a door in a gap, a closed room
                    {
                        var bs = gl.Borders.ToList();
                        Console.WriteLine($"         borders with no place over them (her side, a beam, low headroom): " +
                            string.Join(", ", bs.Where(x => x.Row == 0 && gl.BorderAbove(x) == null).GroupBy(x => x.Room).Select(x => $"{x.Key} {x.Count()}")));
                        Console.WriteLine($"         borders: {bs.Count} places ({string.Join(", ", bs.GroupBy(x => x.Room).Select(x => $"{x.Key} {x.Count()}"))}), row 1 {bs.Count(x => x.Row == 1)}");
                        // (0.3.13, note 137: the places cut to what's free of her are PartitionsToHer's)
                        var badB = bs.Where(bm => bm.Clip == null).Where(bm =>
                        {
                            var (line, alongX) = MountLayout.BorderLine(bm);
                            var cell = gl.FloorAt(bm.Deck, bm.I, bm.K);
                            bool onLine = cell != null && (alongX ? Math.Abs(line - cell.Z) < 1e-9 || Math.Abs(line - cell.Z - 1) < 1e-9 : Math.Abs(line - cell.X) < 1e-9 || Math.Abs(line - cell.X - 1) < 1e-9);
                            bool under = bm.Row == 0 ? bm.Under == null : gl.Get(bm.Under)?.Row == 0;
                            return !onLine || !under || bm.Y + 1 > gd.RoomAt(bm.X, bm.Y + 0.5, bm.Z)?.Max[1] + 1e-6;
                        }).Select(x => x.Id).ToList();
                        int dupes = bs.GroupBy(x => (x.Deck, Math.Round(x.Box[0] + x.Box[3], 3), Math.Round(x.Box[2] + x.Box[5], 3), x.Row)).Count(x => x.Count() > 1);
                        // a partition from planks, only planks; a door on row 0 takes both rows, swings, isn't on a floor place
                        var b0 = bs.First(x => x.Row == 0 && gl.BorderAbove(x) != null && gd.RoomAt(x.X, x.Y + 0.5, x.Z)?.Code == "hold");
                        var part = PiecePlanner.Plan(gl, b0, planksB, 0);
                        var partChest = PiecePlanner.Plan(gl, b0, chest, 0);
                        var doorB = new FakePiece { Code = "game:door-solid-aged", Family = "game:door-solid", Door = true, Col = new[] { 0, 0, 0, 1, 2, 0.1875 } };
                        var doorPlan = PiecePlanner.Plan(gl, b0, doorB, 0);
                        var doorOnFloor = PiecePlanner.Plan(gl, gl.FloorAt(b0.Deck, b0.I, b0.K), doorB, 0);
                        var (bl, bx) = MountLayout.BorderLine(b0);
                        bool thin = part.Error == null && part.Role == PieceRole.Partition && Math.Abs((bx ? part.Box[5] - part.Box[2] : part.Box[3] - part.Box[0]) - MountLayout.PartitionThickness) < 1e-9 &&
                                    part.Sel.Count == 1 && part.Solids.Count == 1 && Math.Abs(part.Box[4] - part.Box[1] - 1) < 1e-9;
                        bool doorOk = doorPlan.Error == null && doorPlan.Role == PieceRole.Door && doorPlan.Mounts.Count == 2 && Math.Abs(doorPlan.Box[4] - doorPlan.Box[1] - 2) < 1e-9 &&
                                      doorPlan.Pivot != null && doorOnFloor.Error == "hansa:door-needs-border";
                        // pieces stand right up to a partition either side (its sheet is on the line)
                        var ps2 = new ShipPieceSet();
                        ps2.Add(ShipPiece.From(part, ps2.NextId, null));
                        var nextTo = PiecePlanner.Plan(gl, gl.FloorAt(b0.Deck, b0.I, b0.K), chest, 1);
                        bool upTo = ps2.Blocking(nextTo) == null;
                        var dp = ShipPiece.From(doorPlan, 1, null);
                        dp.Open = true;
                        var dt = new TreeAttribute();
                        dp.ToTree(dt);
                        bool openSaved = ShipPiece.FromTree(dt).Open;
                        // (0.3.12) walls put aboard are painted: a partition's two faces, a door's two (two high), saved; a bogus colour dropped
                        var pp0 = ShipPiece.From(part, 2, null);
                        pp0.Paint = "red";
                        var pt0 = new TreeAttribute();
                        pp0.ToTree(pt0);
                        var pp1 = ShipPiece.FromTree(pt0);
                        pt0.SetString("paint", "plaid");
                        bool paintOk = pp1.Paint == "red" && pp1.Paintable && Math.Abs(pp1.PaintArea - 2) < 1e-9 && Math.Abs(dp.PaintArea - 4) < 1e-9 &&
                                       ShipPiece.FromTree(pt0).Paint == null && !ShipPiece.From(nextTo, 3, null).Paintable;
                        Check(paintOk, $"a partition and a door put aboard are painted like her surfaces (2 and 4 m², the colour saved, an unknown one dropped); a chest isn't");
                        Check(bs.Count > 0 && badB.Count == 0 && dupes == 0 && thin && partChest.Error == "hansa:partition-needs-planks" && doorOk && upTo && openSaved,
                            $"border places on the grid lines of the rooms' floor cells (one per border, row 1 over row 0, under the band; wrong: {(badB.Count == 0 ? "none" : string.Join(", ", badB.Take(5)))}, twice: {dupes}); " +
                            $"planks on {b0.Id} a partition 1/8 thick, a block high (only planks); a door there both rows, hung on its end, not on a floor place; a chest stands right up to the partition; a door's open state saved");

                        // a closed room on the lower deck with the waist hatch open: two partition lines across her, two rows high
                        // (the hold: no fitting unseals it in the design, it's a closed room as it is)
                        var hatch = gd.Fixtures.FirstOrDefault(f => f.Type == "hatch" && f.Unseals == "lowerdeck");
                        var hold = gd.Rooms.FirstOrDefault(r => r.Code == "lowerdeck");
                        if (hatch != null && hold != null)
                        {
                            var leak = new[] { hatch.Sel ?? hatch.Box };
                            var holdDeck = gl.FloorOf(hold);
                            // two lines across her aft of the hatch, x = xa and x = xa + 2 (cells between): the first pair whose lines
                            // are whole (her fittings keep some cells clear: a mast, the pump, a ladder), looked for aft from the hatch
                            var hb = hatch.Sel ?? hatch.Box;
                            (bool Inside, bool Outside, bool Gap, int Count, int Carried) Try(int xa, int xb)
                            {
                                var pset = new ShipPieceSet();
                                int carried = 0;
                                foreach (int xl in new[] { xa, xb })
                                    foreach (var row in new[] { 0, 1 })
                                        foreach (var cell in gl.Floors.Where(f => f.Deck == holdDeck.Code && (Math.Abs(f.X + 1 - xl) < 1e-9 || Math.Abs(f.X - xl) < 1e-9)))
                                        {
                                            string side = Math.Abs(cell.X + 1 - xl) < 1e-9 ? "e" : "w";
                                            var bp = gl.Get($"b:{cell.Deck}:{cell.I}:{cell.K}:{side}:{row}");
                                            if (bp == null || pset.Taken(bp.Id)) continue;
                                            // (0.3.12: a row 0 partition with no place over it, her side or a beam there, closed up to them)
                                            if (row == 0 && gl.BorderAbove(bp) == null) carried++;
                                            // (and one whose next cell along the line her side cuts into)
                                            for (int end = -1; end <= 1; end += 2)
                                                if (gl.FloorAt(bp.Deck, bp.I, bp.K + end) is MountPoint nb &&
                                                    ShipCompartments.CutByHer(gl, bp, nb, PiecePlanner.Plan(gl, bp, planksB, 0).Solids[0], false)) carried++;
                                            pset.Add(ShipPiece.From(PiecePlanner.Plan(gl, bp, planksB, 0), pset.NextId, null));
                                        }
                                if (pset.Count == 0) return default;
                                var grid = gd.Grid.Clone();
                                foreach (var ppc in pset.All) foreach (var sb in ppc.Solids) grid.Fill(sb);
                                double my = holdDeck.Y + 0.1;
                                if (Environment.GetEnvironmentVariable("HANSA_SEAL_DEBUG") != null)
                                {
                                    ShipCompartments.Why = w => Console.WriteLine($"           {xa}/{xb}: {w}");
                                    ShipCompartments.Filled = fg =>
                                    {
                                        foreach (int xl in new[] { xa, xb })
                                        {
                                            var holes = new List<string>();
                                            for (double zz = fg.OZ + fg.V / 2; zz < fg.MaxZ; zz += fg.V)
                                                for (double yy = holdDeck.Y + fg.V / 2; yy < holdDeck.Y + 2; yy += fg.V)
                                                {
                                                    // free on both quarter layers either side of the line: a way through
                                                    if (!fg.SolidAt(xl - 0.125, yy, zz) && !fg.SolidAt(xl + 0.125, yy, zz))
                                                    {
                                                        // (inside the hull: something solid further out on both sides)
                                                        bool inHull = false, a = false, b = false;
                                                        for (double z2 = zz; z2 < fg.MaxZ; z2 += fg.V) if (fg.SolidAt(xl + 0.125, yy, z2)) { a = true; break; }
                                                        for (double z2 = zz; z2 > fg.OZ; z2 -= fg.V) if (fg.SolidAt(xl + 0.125, yy, z2)) { b = true; break; }
                                                        inHull = a && b;
                                                        if (inHull) holes.Add($"z{zz:0.00} y{yy - holdDeck.Y:0.00}");
                                                    }
                                                }
                                            Console.WriteLine($"           line x {xl}: {holes.Count} free voxels across it: {string.Join(" ", holes.Take(12))}");
                                        }
                                    };
                                }
                                bool inside = ShipCompartments.Sealed(gl, grid, pset.All, hold, leak, xa + 1, my, 0.5);
                                ShipCompartments.Why = null;
                                ShipCompartments.Filled = null;
                                bool outside = !ShipCompartments.Sealed(gl, grid, pset.All, hold, leak, xb + 1.5, my, 0.5);
                                var one = pset.All.FirstOrDefault(ppc => gl.Get(ppc.Mount)?.Row == 0 && Math.Abs(MountLayout.BorderLine(gl.Get(ppc.Mount)).Line - xb) < 1e-9);
                                if (one == null) return default;
                                var set2 = new ShipPieceSet();
                                foreach (var ppc in pset.All) if (ppc.Id != one.Id) set2.Add(ppc);
                                var grid2 = gd.Grid.Clone();
                                foreach (var ppc in set2.All) foreach (var sb in ppc.Solids) grid2.Fill(sb);
                                bool gap = !ShipCompartments.Sealed(gl, grid2, set2.All, hold, leak, xa + 1, my, 0.5);
                                return (inside, outside, gap, pset.Count, carried);
                            }
                            int found = int.MinValue, tried = 0, sealedPairs = 0, carriedPairs = 0, withCarried = 0;
                            (bool Inside, bool Outside, bool Gap, int Count, int Carried) res = default;
                            for (int xb = (int)Math.Floor(hb[0]) - 2; xb > (int)Math.Ceiling(hold.Min[0]) + 4; xb--)
                            {
                                tried++;
                                var r = Try(xb - 2, xb);
                                if (r.Carried > 0) withCarried++;
                                if (Environment.GetEnvironmentVariable("HANSA_SEAL_DEBUG") != null && !(r.Inside && r.Outside && r.Gap))
                                {
                                    var missing = new List<string>();
                                    foreach (int xl in new[] { xb - 2, xb })
                                        foreach (var cell in gl.Floors.Where(f => f.Deck == holdDeck.Code && Math.Abs(f.X - xl) < 1e-9))
                                            if (gl.Get($"b:{cell.Deck}:{cell.I - 1}:{cell.K}:e:0") == null && gl.Get($"b:{cell.Deck}:{cell.I}:{cell.K}:w:0") == null) missing.Add($"x{xl} k{cell.K}");
                                    var ks = gl.Floors.Where(f => f.Deck == holdDeck.Code && (Math.Abs(f.X - xb) < 1e-9 || Math.Abs(f.X - xb + 2) < 1e-9)).Select(f => f.K).ToList();
                                    Console.WriteLine($"           pair {xb - 2}/{xb}: inside {r.Inside} outside {r.Outside} gap {r.Gap}, {r.Count} partitions; cells k {(ks.Count > 0 ? ks.Min() : 0)}..{(ks.Count > 0 ? ks.Max() : 0)}; no border: {string.Join(", ", missing)}");
                                }
                                if (r.Inside && r.Outside && r.Gap)
                                {
                                    sealedPairs++;
                                    if (r.Carried > 0) carriedPairs++;
                                    if (found == int.MinValue) { found = xb; res = r; }
                                }
                            }
                            Console.WriteLine($"         partition pairs aft of the hatch: {sealedPairs} of {tried} close a room; {withCarried} have row 0 partitions with no place over them " +
                                $"(her side or a beam) or ending where her side cuts into the next cell's border, {carriedPairs} of those close");
                            Check(found != int.MinValue,
                                $"with the waist hatch open: two partition lines across the lower deck (x {found - 2} and {found}, two rows, {res.Count} partitions) make a closed room between them; " +
                                $"by the hatch it isn't; one partition taken away it isn't ({tried} pairs of lines tried aft of the hatch)");
                            Check(withCarried == 0 || carriedPairs > 0,
                                $"(0.3.12, note 123) a partition line whose top row can't be built at her side or under a beam, or whose last cell her side cuts into, still closes the room: closed up to her timbers " +
                                $"({carriedPairs} of {withCarried} such pairs close)");
                        }
                    }

                    // the rowing boat (and an older design) has none: the search per deck as before
                    Check(def.BlockGrid != null || !l.OnShipGrid, "without a grid origin each deck takes the alignment that gives it the most places, as before");
                }

                // ---- a lantern without its metal and glass gets vanilla's defaults (the user's note 57)
                {
                    var lb = new Vintagestory.GameContent.BlockLantern { BlockId = 103, Code = new AssetLocation("game", "lantern-small-up") };
                    lb.Textures = new Dictionary<string, Vintagestory.API.Client.CompositeTexture>
                    {
                        ["copper"] = new Vintagestory.API.Client.CompositeTexture(), ["brass"] = new Vintagestory.API.Client.CompositeTexture(), ["tinbronze"] = new Vintagestory.API.Client.CompositeTexture(), ["silver"] = new Vintagestory.API.Client.CompositeTexture()
                    };
                    var bare = new ItemStack(lb);
                    bool fixedBare = VanillaPieceBlock.WithDefaults(null, bare) && bare.Attributes.GetString("material") == "copper" &&
                                     bare.Attributes.GetString("lining") == "plain" && bare.Attributes.GetString("glass") == "quartz";
                    var brass = new ItemStack(lb);
                    brass.Attributes.SetString("material", "brass");
                    brass.Attributes.SetString("lining", "silver");
                    brass.Attributes.SetString("glass", "quartz");
                    bool keptBrass = !VanillaPieceBlock.WithDefaults(null, brass) && brass.Attributes.GetString("material") == "brass" && brass.Attributes.GetString("lining") == "silver";
                    var bronze = new ItemStack(lb);
                    bronze.Attributes.SetString("material", "bronze");
                    bronze.Attributes.SetString("lining", "mithril");
                    VanillaPieceBlock.WithDefaults(null, bronze);
                    bool bronzeOk = bronze.Attributes.GetString("material") == "tinbronze" && bronze.Attributes.GetString("lining") == "plain";
                    bool chestLeft = !VanillaPieceBlock.WithDefaults(null, new ItemStack(blockChest));
                    // the design's start lanterns: each with its metal, lining and glass (notes 57, 59)
                    var bareStart = (def.StartPieces ?? Array.Empty<StartPieceDef>()).Where(sp => sp.Block?.Contains("lantern") == true &&
                        !(sp.Attributes != null && sp.Attributes.ContainsKey("material") && sp.Attributes.ContainsKey("lining") && sp.Attributes.ContainsKey("glass"))).Select(sp => sp.Mount).ToList();
                    Console.WriteLine($"         start lanterns without their metal, lining or glass in the design: {(bareStart.Count == 0 ? "none" : string.Join(", ", bareStart))}");
                    Check(fixedBare && keptBrass && bronzeOk && chestLeft,
                        "a lantern without its metal, lining or glass gets vanilla's (copper, plain, quartz: never \"lantern-small-up-\" untextured), \"bronze\" is tin bronze, " +
                        "a lining it has no texture for plain; a lantern with its own keeps them; other blocks as they are");
                }

                // ---- her name board's style (the user's note 68): saved with her name, anything unknown back to the defaults
                {
                    var ns = new NameStyle { Text = "red", Back = "black", Size = 1, Italic = true };
                    var nt = new TreeAttribute();
                    ns.ToTree(nt);
                    var nb = new TreeAttribute();
                    nb.FromBytes(nt.ToBytes());
                    var back2 = NameStyle.FromTree(nb);
                    var bogus = new NameStyle { Text = "chartreuse", Back = "plaid", Size = 9 }.Clean();
                    var none = NameStyle.FromTree(new TreeAttribute());
                    Check(back2.SameAs(ns) && bogus.Text == ShipPaint.Gold && bogus.Back == null && bogus.Size == NameStyle.Sizes.Length - 1 &&
                          none.Text == ShipPaint.Gold && none.Back == null && !none.Italic && none.Size == 2 && NameStyle.Colours.Length == 12 &&
                          NameStyle.Rgb("white")[0] > 0.9 && NameStyle.Rgb(ShipPaint.Gold)[0] > 0.9,
                        "her name's style saved and loaded (red on black, medium, italic); unknown colours and sizes back to the defaults (gilt on the board as built, large)");
                }

                // ---- the heads (a "seat" fixture: sat on as a chair), the door to the companionway (a door like the others)
                {
                    var seatsF = def.Fixtures.Where(f => f.Type == "seat").ToList();
                    bool seatsOk = seatsF.All(f => f.Pos?.Length == 3 && f.Sel?.Length == 6 && !l.Floors.Any(m => PiecePlanner.Overlap(m.Box, f.Sel)));
                    var doors = def.Fixtures.Where(f => f.Type == "door").ToList();
                    bool doorsOk = doors.All(f => f.Box?.Length == 6 && (f.Normal == "x" || f.Normal == "z") && (f.Block != null || f.Hinge != null));
                    Console.WriteLine($"         seats: {(seatsF.Count == 0 ? "none in the design yet" : string.Join(", ", seatsF.Select(f => f.Code)))}; doors: {string.Join(", ", doors.Select(f => f.Code + (f.Unseals != null ? " (unseals " + f.Unseals + ")" : "")))}");
                    Check(seatsOk && doorsOk, "her seats (the heads) each with a place and a box, no floor place on them; every door with its opening, the way it faces and a block or hinge");
                }

                var inv = new InventoryGeneric(16, "hansa-piece-1", "1", null);
                inv[0].Itemstack = new ItemStack(stick, 12);
                inv[15].Itemstack = new ItemStack(blockChest, 1);
                var it = new TreeAttribute();
                inv.ToTreeAttributes(it);
                var itBack = new TreeAttribute();
                itBack.FromBytes(it.ToBytes());
                var inv2 = new InventoryGeneric(16, "hansa-piece-1", "1", null);
                inv2.FromTreeAttributes(itBack);
                Check(inv2[0].Itemstack?.StackSize == 12 && inv2[0].Itemstack.Id == 7 && inv2[15].Itemstack?.Id == 101 && inv2[15].Itemstack.Class == EnumItemClass.Block && inv2.Count(s => !s.Empty) == 2,
                    "a container piece's contents saved and loaded (by vanilla's inventory, as the hold's)");
            }

            // ---- an older ship's things: every chest, cargo, bed and lantern place 0.3.7 had has its spot, and a free place near it
            {
                // (0.3.7 as released: the generator moves things since)
                var released = def;
                string zip = Path.Combine(root, "release", "hansa-0.3.7.zip");
                if (def.Code == "carrack" && File.Exists(zip))
                {
                    using var za = System.IO.Compression.ZipFile.OpenRead(zip);
                    var entry = za.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("config/ships/carrack.json"));
                    if (entry != null)
                    {
                        using var sr = new StreamReader(entry.Open());
                        released = JsonConvert.DeserializeObject<ShipDefinition>(sr.ReadToEnd());
                    }
                }
                var legacy = released.Fixtures.Where(f => f.Type == "attach" && f.Kind is "chest" or "cargo" or "bed" or "lantern").ToList();
                var wrong = legacy.Where(f => !LegacyPlaces.TryGet("carrack", f.Code, out var p, out float yaw) ||
                    Math.Abs(p[0] - f.Pos[0]) + Math.Abs(p[1] - f.Pos[1]) + Math.Abs(p[2] - f.Pos[2]) > 1e-3 || Math.Abs(yaw - f.Yaw) > 1e-3).Select(f => f.Code).ToList();
                Check(legacy.Count == 0 || wrong.Count == 0, $"the 0.3.7 places kept for moving an older ship's things ({legacy.Count} in the design; wrong: {(wrong.Count == 0 ? "none" : string.Join(", ", wrong))})");
                // the design without its chest, cargo and bed places (the generator takes them out): a free place within 4 of each
                var bare = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", def.Code + ".json")));
                bare.Fixtures = bare.Fixtures.Where(f => !(f.Type == "attach" && f.Kind is "chest" or "cargo" or "bed")).ToArray();
                bare.Init();
                var lost = new List<string>();
                foreach (var f in legacy.Where(f => f.Kind != "lantern"))
                {
                    LegacyPlaces.TryGet("carrack", f.Code, out var p, out _);
                    if (!bare.Mounts.Floors.Any(m => Math.Abs(m.Y - p[1]) < 0.3 && Math.Sqrt((m.CX - p[0]) * (m.CX - p[0]) + (m.CZ - p[2]) * (m.CZ - p[2])) < 4)) lost.Add(f.Code);
                }
                Check(lost.Count == 0, $"without its chest, cargo and bed places her floor has a place within 4 blocks of each ({bare.Mounts.Floors.Count()} floor places then; none near: {(lost.Count == 0 ? "-" : string.Join(", ", lost))})");
                var hooks = legacy.Where(f => f.Kind == "lantern").Where(f => bare.Mounts.Get("h:" + f.Code) == null).Select(f => f.Code).ToList();
                Check(hooks.Count == 0, "each lantern place is a hook of the same code (an older ship's lantern goes back on it)");
            }

            // ---- paint: textures, cost, saved state
            {
                string tarred = "game:block/wood/planks/oak3++3~hansa:block/tar", deck = "game:block/wood/planks/oak2++0~hansa:block/caulk";
                Check(ShipPaint.Painted(tarred, "red") == "hansa:block/paint/oak3++3~hansa:block/dye/red" &&
                      ShipPaint.Painted(deck, "white") == "hansa:block/paint/oak2++3~hansa:block/dye/white++0~hansa:block/caulk" &&
                      ShipPaint.Painted("game:block/wood/debarked/oak", "blue") == "hansa:block/paint/timber++3~hansa:block/dye/blue" &&
                      ShipPaint.Painted(tarred, "gold") == ShipPaint.GoldTexture && ShipPaint.Painted(tarred, null) == tarred,
                    "a painted quad's texture: the oak's grain in grey (the same plank) times the dye, a deck's caulked seams kept over it; gilt: vanilla's gold plate; as built: its own");
                Check(ShipPaint.DyeItems(892.02) == 1785 && ShipPaint.DyeItems(1.26) == 3 && ShipPaint.GoldPlates(35.25) == 4 && ShipPaint.GoldPlates(1.26) == 1 &&
                      ShipPaint.DyeColour("game:dye-woad") == "woad" && ShipPaint.DyeColour("game:dye-brown") == null && ShipPaint.DyeColour("hansa:dye-red") == null &&
                      ShipPaint.Gildable(new PaintZoneDef { Kind = "carving" }) && !ShipPaint.Gildable(new PaintZoneDef { Kind = "panel" }),
                    "cost: 0.02 L of dye a m² (the lower deck's 892 m²: 17.85 L), a gold plate per 10 m² (at least one); vanilla's eleven dyes; only mouldings and carvings gilded");
                var st = new ShipPaintState();
                st.Zones["rail-caps"] = "red";
                st.Zones["stern-carvings"] = "gold";
                var tt = new TreeAttribute();
                st.ToTree(tt);
                var tb2 = new TreeAttribute();
                tb2.FromBytes(tt.ToBytes());
                tb2.SetString("bogus", "chartreuse");
                var st2 = new ShipPaintState();
                st2.FromTree(tb2);
                Check(st2.Zones.Count == 2 && st2.Of("rail-caps") == "red" && st2.Of("stern-carvings") == "gold" && st2.Of("bogus") == null, "her paint saved and loaded (an unknown colour dropped)");
            }

            // ---- paint zones on her real meshes (test zones until the generator writes hers): areas, the crosshair, the paint drawn
            PaintZonesOnMesh(def, root);
        }

        /// <summary>
        /// Paint zones on the carrack's own mesh file: the generator's if it has written them, else test zones made here (the
        /// great cabin's inside planking, her topsides outside, the faces of the great cabin's front from inside). Their areas,
        /// what the crosshair finds where, and a painted zone's quads in a damaged hull chunk drawn with the paint under the damage.
        /// </summary>
        static void PaintZonesOnMesh(ShipDefinition def, string root)
        {
            string meshPath = Path.Combine(root, "assets", "hansa", "config", "shipmeshes", def.Code + ".json");
            var file = JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(meshPath));
            var shapes = new Dictionary<string, Dictionary<string, double[]>>();
            foreach (var (role, loc) in new[] { ("exterior", def.Shapes.Exterior), ("interior", def.Shapes.Interior) })
            {
                var al = new AssetLocation(loc);
                shapes[role] = PaintPlan.ShapeBoxes(File.ReadAllText(Path.Combine(root, "assets", al.Domain, "shapes", al.Path + ".json")));
            }
            bool theirs = def.PaintZones?.Length > 0 && file.PaintQuads != null;
            var cabin = def.Rooms.First(r => r.Code == "greatcabin");
            if (!theirs)
            {
                // test zones: 0 the great cabin's inside planking (oak, inside group), 1 her topsides (tarred, outside, over the
                // waterline, amidships), 2 the great cabin front's boards seen from inside (voxel faces facing aft)
                def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", def.Code + ".json")));
                def.PaintZones = new[]
                {
                    new PaintZoneDef { Code = "test-cabin", Name = "Great cabin's sides", Kind = "panel", Default = "oak" },
                    new PaintZoneDef { Code = "test-topsides", Name = "Topsides", Kind = "panel", Default = "tarred" },
                    new PaintZoneDef { Code = "test-front", Name = "Great cabin's front", Kind = "moulding", Default = "oak" },
                };
                def.Init();
                file.PaintQuads = new Dictionary<string, int[]>();
                bool InCabin(float[] q) => Mid(q, 0) > cabin.Min[0] + 0.3 && Mid(q, 0) < cabin.Max[0] - 0.3 && Mid(q, 1) > cabin.Min[1] + 0.1 && Mid(q, 1) < cabin.Max[1] && Math.Abs(Mid(q, 2)) > 3;
                file.PaintQuads["interior"] = Runs(file.Groups["interior"], q => InCabin(q) && file.Textures[(int)q[0]].Contains("planks/oak") && !file.Textures[(int)q[0]].Contains("~") ? 0 : -1);
                file.PaintQuads["exterior"] = Runs(file.Groups["exterior"], q => file.Textures[(int)q[0]].Contains("~hansa:block/tar") && Mid(q, 1) > 0.5 && Mid(q, 1) < 2.2 && Math.Abs(Mid(q, 0)) < 6 ? 1 : -1);
                file.PaintFaces = new Dictionary<string, Dictionary<string, Dictionary<string, int>>> { ["interior"] = new(), ["exterior"] = new() };
                foreach (var (role, boxes) in shapes)
                    foreach (var kv in boxes)
                    {
                        var b = kv.Value;
                        // the front's aft face: a bulkhead at the cabin's forward end, seen from inside (west)
                        if (b[3] > cabin.Max[0] - 0.3 && b[0] < cabin.Max[0] + 0.3 && b[1] >= cabin.Min[1] - 0.01 && b[4] <= cabin.Max[1] + 0.3)
                            file.PaintFaces[role][kv.Key] = new Dictionary<string, int> { ["west"] = 2 };
                    }
            }
            var plan = PaintPlan.Build(def, file, shapes);
            Console.WriteLine($"         paint zones ({(theirs ? "the design's" : "test zones")}): " +
                string.Join(", ", plan.Zones.Select((z, i) => $"{z.Code} {plan.Area[i]:0.0} m² ({plan.QuadCount[i]} quads, {plan.FaceCount[i]} faces)")));
            Check(plan.Usable && plan.Area.All(a => a > 0), "each paint zone has quads or faces on her meshes, and an area (m²) to cost it by");
            if (theirs)
            {
                // the generator's own areas (docs/views/paint/zones.json): the same within a few percent
                string zj = Path.Combine(root, "docs", "views", "paint", "zones.json");
                // (only if it was written for this mesh: an older one measured an older design)
                if (File.Exists(zj) && File.GetLastWriteTimeUtc(zj) >= File.GetLastWriteTimeUtc(meshPath).AddMinutes(-10))
                {
                    var rows = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(zj))["zones"];
                    var off = new List<string>();
                    foreach (var r in rows)
                    {
                        int i = plan.ZoneIndex((string)r["code"]);
                        double want = (double)r["area"];
                        if (i < 0 || Math.Abs(plan.Area[i] - want) > Math.Max(0.5, want * 0.03)) off.Add($"{r["code"]} {(i < 0 ? -1 : plan.Area[i]):0.0}/{want:0.0}");
                    }
                    Check(off.Count == 0, $"the zones' areas as the generator measured them (docs/views/paint/zones.json; off: {(off.Count == 0 ? "none" : string.Join(", ", off))})");
                }
                else Console.WriteLine("         (docs/views/paint/zones.json is older than her mesh: its areas not compared)");
                // the crosshair on her zones, from a few places aboard and round her
                string Z(double[] o, double[] at)
                {
                    var d = new Vec3d(at[0] - o[0], at[1] - o[1], at[2] - o[2]);
                    d.Normalize();
                    var ov = new Vec3d(o[0], o[1], o[2]);
                    double wall = def.Grid.Raycast(ov, d, 30);
                    int z = plan.Pick(ov, d, PaintPlan.Reach(def.Grid, ov, d, 30, wall), out _);
                    return z < 0 ? "-" : plan.Zones[z].Code;
                }
                var gc = def.Rooms.First(r => r.Code == "greatcabin");
                var ld = def.Rooms.First(r => r.Code == "lowerdeck");
                double gx = -11, gy = gc.Min[1] + 1.6, lx = 6, ly = ld.Min[1] + 1.6;
                var seen = new (string Where, string Got, string[] Want)[]
                {
                    ("the great cabin's starboard side", Z(new[] { gx, gy, 0.5 }, new[] { gx, gy, 6.0 }), new[] { "great-cabin-walls", "cabin-trim" }),
                    ("the great cabin's ceiling", Z(new[] { gx, gy, 0.5 }, new[] { gx - 0.3, gc.Max[1] + 1, 0.7 }), new[] { "cabin-ceilings" }),
                    ("the lower deck's port side", Z(new[] { lx, ly, -0.5 }, new[] { lx, ly, -6.0 }), new[] { "lower-deck", "lower-deck-frames" }),     // (0.3.12: its frames their own zone)
                    // (clear of the main channels: 3.5 aft of the mainmast)
                    ("her topsides from abeam", Z(new[] { def.Masts[0].X - 3.5, def.DeckY + 0.6, 14 }, new[] { def.Masts[0].X - 3.5, def.DeckY + 0.6, 0 }), new[] { "topsides", "frieze" }),
                    ("her wales from abeam", Z(new[] { 0.0, 0.9, -14 }, new[] { 0.0, 0.9, 0 }), new[] { "wales" }),
                    ("the main deck", Z(new[] { 6.0, def.DeckY + 1.6, 2.8 }, new[] { 6.5, def.DeckY, 3.2 }), new[] { "-" }),
                };
                foreach (var v in seen) Console.WriteLine($"         {v.Where}: {v.Got}");
                Check(seen.All(v => v.Want.Contains(v.Got)), "the crosshair on her zones: the great cabin's side and ceiling, the lower deck's side, her topsides and wales from abeam; her deck no zone");
                return;
            }
            // the crosshair: from the middle of the great cabin at eye height, at her starboard side (the cabin's planking), at
            // the cabin's front; from outside abeam, at her topsides
            double cx = (cabin.Min[0] + cabin.Max[0]) / 2, ey = cabin.Min[1] + 1.6;
            int Pick(double[] o, double[] at, out double t)
            {
                var d = new Vec3d(at[0] - o[0], at[1] - o[1], at[2] - o[2]);
                d.Normalize();
                return plan.Pick(new Vec3d(o[0], o[1], o[2]), d, 20, out t);
            }
            int side = Pick(new[] { cx, ey, 0 }, new[] { cx, ey, 6 }, out double ts);
            int front = Pick(new[] { cabin.Max[0] - 3, ey, 2.0 }, new[] { cabin.Max[0] + 1, ey, 2.0 }, out double tf);
            int top = Pick(new[] { 0.0, 1.4, 12 }, new[] { 0.0, 1.4, 0 }, out double tt2);
            int none = Pick(new[] { cx, ey, 0 }, new[] { cx, cabin.Min[1] - 1, 0.5 }, out _);
            Check(side == 0 && front == 2 && top == 1 && none < 0 && ts > 3 && ts < 6 && tf > 2.5,
                $"the crosshair on a zone: the cabin's side from its middle ({side}, {ts:0.00}), its front from inside ({front}), her topsides from abeam ({top}); her deck no zone ({none})");
            Check(plan.Near(0, cx, ey, cabin.Max[2] - 0.6, 0.8) && !plan.Near(0, cx, ey, 0, 0.6), "the server's check of where a zone was painted: near its planking, not in the middle of the cabin");

            // a painted zone drawn: its quads on the paint's texture; in a damaged hull chunk, the paint under the damage
            if (def.DamageParts != null)
            {
                var hull = new HullDamageSkin(file, def.DamageParts);
                var zoneOf = plan.QuadZones["exterior"];
                var chunk = hull.Chunks.FirstOrDefault(c => c.Group == "exterior" && c.Quads.Any(q => zoneOf[q] == 1));
                if (chunk != null)
                {
                    var looks = new byte[def.DamageParts.Count];
                    int painted = chunk.Quads.First(q => zoneOf[q] == 1);
                    int panel = chunk.Panel[Array.IndexOf(chunk.Quads, painted)];
                    looks[panel] = HullLook.Cracked;
                    var quads = file.Groups["exterior"];
                    var built = hull.Build(chunk, looks, q => zoneOf[q] == 1 ? hull.TextureIndex(ShipPaint.Painted(hull.Textures[(int)quads[q][0]], "red")) : (int)quads[q][0]);
                    var names = built.Select(b => hull.Textures[(int)b[0]]).ToList();
                    bool redCracked = names.Any(n => n.StartsWith("hansa:block/paint/oak") && n.Contains("dye/red++0~hansa:block/dmg-cracked"));
                    bool othersOwn = chunk.Quads.Where(q => zoneOf[q] != 1).All(q => !names.Contains(null)) && names.Any(n => !n.Contains("dye/"));
                    Check(redCracked && othersOwn, $"a painted zone in a cracked hull panel: drawn as red-painted oak with the cracks over it ({names.Count(n => n.Contains("dye/red"))} of {names.Count} quads), the rest as they are");
                }
            }
        }

        static double Mid(float[] q, int a) => (q[1 + a] + q[9 + a] + q[17 + a] + q[25 + a]) / 4.0;

        /// <summary>A group's zone per quad as ranges [start, end, zone, ...] (the mesh file's paintQuads).</summary>
        static int[] Runs(float[][] quads, System.Func<float[], int> zoneOf)
        {
            var r = new List<int>();
            int start = 0, cur = -1;
            for (int i = 0; i <= quads.Length; i++)
            {
                int z = i < quads.Length ? zoneOf(quads[i]) : -1;
                if (z == cur) continue;
                if (cur >= 0) r.AddRange(new[] { start, i, cur });
                cur = z;
                start = i;
            }
            return r.ToArray();
        }
    }
}
