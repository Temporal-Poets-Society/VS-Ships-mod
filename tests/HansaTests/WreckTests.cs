using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// A made-up seabed for the wreck's builder: ground (sand) below a level, or below a height a function gives per
        /// column; a rock standing up through it here and there; plants (soft) and blocks of hers already laid (ours).
        /// </summary>
        class TestSite : IWreckSite
        {
            public Func<int, int, double> Floor = (x, z) => double.NegativeInfinity;
            public readonly HashSet<long> Rocks = new(), Plants = new(), Ours = new();
            public int Asked;

            public WreckGround At(int x, int y, int z, out float top)
            {
                Asked++;
                top = 1;
                long k = WreckPlan.Key(x, y, z);
                if (Rocks.Contains(k)) return WreckGround.Ground;
                if (Ours.Contains(k)) return WreckGround.Ours;
                if (Plants.Contains(k)) return WreckGround.Soft;
                return y + 1 <= Floor(x, z) ? WreckGround.Ground : WreckGround.Free;
            }
        }

        /// <summary>A plan's cells back in her own frame (ship-local middles), with their material.</summary>
        static List<(double X, double Y, double Z, byte Mat)> LocalCells(WreckPlan plan)
        {
            var w = plan.Input;
            var list = new List<(double, double, double, byte)>();
            foreach (var kv in plan.Blocks)
            {
                WreckPlan.Unkey(kv.Key, out int bx, out int by, out int bz);
                var cells = kv.Value.ToCells();
                for (int b = 0; b < 64; b++)
                {
                    if (cells[b] == WreckMat.None) continue;
                    WreckCells.Cell(b, out int cx, out int cy, out int cz);
                    ShipFrame.ToLocal(w.Yaw, w.Trim, w.List, bx + (cx + 0.5) * 0.25 - w.X, by + (cy + 0.5) * 0.25 - (w.Y - w.Settle), bz + (cz + 0.5) * 0.25 - w.Z,
                        out double lx, out double ly, out double lz);
                    list.Add((lx, ly, lz, cells[b]));
                }
            }
            return list;
        }

        /// <summary>
        /// The wreck (Hansa 0.3.5, task 5): her hull laid in the world's cells at any yaw (her cells kept), the wreck
        /// block's cells (mask round trip, its boxes, its faces and their texture), what breaking one gives, the same wreck
        /// from the same seed, and broken up on a seabed: holes, decay, a fallen castle, the masts as timbers, the anchor,
        /// her guns and containers and extra chests in their places, sand against her, nothing in the ground.
        /// </summary>
        static void Wrecks(ShipDefinition def)
        {
            Console.WriteLine("wrecks: her hull as world blocks, broken up on the seabed; the wreck block; what breaking one gives");
            var l = def.DamageParts;

            // ---- laid in the world's cells: as many as she has, whatever her yaw (open sea, nothing broken)
            var open = new TestSite();
            WreckInput Plain(double yaw, double list = 0, double trim = 0) => new()
            {
                Def = def, X = 1000, Y = 60, Z = 2000, Yaw = yaw, List = list, Trim = trim, Seed = 1, Settle = 0,
                Decay = false, Holes = false, Masts = false, Guns = false, Anchor = false,
            };
            foreach (double yaw in new[] { 0, Math.PI / 2, Math.PI, 0.52, 0.79, 2.4, 4.0 })
            {
                var p = WreckBuilder.Build(Plain(yaw), open);
                double err = (p.CellsPlaced - p.CellsDesign) / (double)p.CellsDesign;
                bool square = Math.Abs(Math.Sin(2 * yaw)) < 1e-9;
                Check(square ? p.CellsPlaced == p.CellsDesign : Math.Abs(err) < 0.03,
                    $"turned to {yaw * 180 / Math.PI:0}°, her {p.CellsDesign} cells laid in {p.Blocks.Count} world blocks as {p.CellsPlaced} cells ({err * 100:+0.00;-0.00}%{(square ? ", exact" : "")})");
            }
            {
                var p = WreckBuilder.Build(Plain(0.79, 0.35, -0.1), open);
                double err = (p.CellsPlaced - p.CellsDesign) / (double)p.CellsDesign;
                Check(Math.Abs(err) < 0.04, $"heeled 20° and 6° by the head too: {p.CellsPlaced} cells for her {p.CellsDesign} ({err * 100:+0.00;-0.00}%)");
                // every world cell is one of hers: back in her frame its middle is in a solid cell of her design
                var g = def.Grid;
                int wrong = LocalCells(p).Count(c => !g.SolidAt(c.X, c.Y, c.Z));
                Check(wrong == 0, $"every cell laid is in her design where it is in her frame ({wrong} not)");
            }

            // ---- the wreck block's cells
            var rnd = new Random(5);
            bool roundTrip = true, boxesExact = true, treeTrip = true;
            for (int t = 0; t < 300; t++)
            {
                var cells = new byte[64];
                int fill = rnd.Next(65);
                for (int b = 0; b < 64; b++) cells[b] = rnd.Next(64) < fill ? (byte)rnd.Next(1, WreckMat.Count) : WreckMat.None;
                var c = WreckCells.FromCells(cells);
                roundTrip &= c.ToCells().SequenceEqual(cells) && c.Count == cells.Count(x => x != 0);
                var tree = new TreeAttribute();
                c.ToTree(tree);
                var back = WreckCells.FromTree(tree);
                treeTrip &= back != null && back.ToCells().SequenceEqual(cells);
                // its boxes cover exactly its cells, without overlapping
                ulong covered = 0;
                int volume = 0;
                foreach (var bx in WreckCells.Boxes(c.All))
                    for (int y = bx[1]; y < bx[4]; y++)
                        for (int z = bx[2]; z < bx[5]; z++)
                            for (int x = bx[0]; x < bx[3]; x++) { covered |= 1UL << WreckCells.Bit(x, y, z); volume++; }
                boxesExact &= covered == c.All && volume == c.Count;
            }
            Check(roundTrip && treeTrip, "a block's cells round-trip: 64 materials → layers (a material and its 64-bit mask) → back, and through its saved attributes");
            Check(boxesExact, "its collision boxes cover exactly its cells, none overlapping");
            {
                var full = WreckCells.FromCells(Enumerable.Repeat(WreckMat.Oak, 64).ToArray());
                var fq = full.Quads();
                var one = new byte[64];
                one[WreckCells.Bit(1, 2, 3)] = WreckMat.Iron;
                var oq = WreckCells.FromCells(one).Quads();
                var half = new byte[64];
                for (int b = 0; b < 64; b++) { WreckCells.Cell(b, out int x, out _, out _); half[b] = x < 2 ? WreckMat.Tarred : WreckMat.Oak; }
                var hq = WreckCells.FromCells(half).Quads();
                Check(fq.Count == 6 && fq.All(q => Area(q) == 1) && oq.Count == 6 && oq.All(q => Math.Abs(Area(q) - 1 / 16f) < 1e-6) &&
                      Math.Abs(hq.Sum(q => Area(q)) - 6) < 1e-6 && full.Cuboids().Length == 1,
                    $"a full block draws as its 6 faces (merged), a single cell as 6 small ones, two materials side by side with no face between them ({hq.Count} faces)");
                // against a full wreck block beside it, that side isn't drawn
                var covered = full.Quads((f, x, y, z) => f == 1 && full.Has(x, y, z));
                Check(covered.Count == 5 && covered.All(q => q.Face != 1), "a side against a wreck block's cells isn't drawn");
                // the texture: each face's corners get what a vanilla cube's face shows there (seamless across faces)
                double worst = 0;
                bool fullFace = true;
                foreach (var q in fq.Concat(oq).Concat(hq))
                {
                    q.Box(out float cx, out float cy, out float cz, out float sx, out float sy, out float sz);
                    q.Uv(out float ou, out float ov, out float su, out float sv);
                    for (int j = 0; j < 4; j++)
                    {
                        WreckFaces.Corner(q.Face, j, cx, cy, cz, sx, sy, sz, out float x, out float y, out float z);
                        WreckFaces.At(q.Face, x, y, z, out float u, out float v);
                        float gu = ou + su * CubeMeshUtil.CubeUvCoords[q.Face * 8 + j * 2], gv = ov + sv * CubeMeshUtil.CubeUvCoords[q.Face * 8 + j * 2 + 1];
                        worst = Math.Max(worst, Math.Abs(gu - u) + Math.Abs(gv - v));
                        // and on the face's own plane
                        float plane = WreckCells.FaceSign[q.Face] > 0 ? new[] { q.X1, q.Y1, q.Z1 }[WreckCells.FaceAxis[q.Face]] : new[] { q.X0, q.Y0, q.Z0 }[WreckCells.FaceAxis[q.Face]];
                        worst = Math.Max(worst, Math.Abs(new[] { x, y, z }[WreckCells.FaceAxis[q.Face]] - plane));
                    }
                    if (Area(q) == 1) fullFace &= Math.Abs(Math.Abs(su) - 1) < 1e-6 && Math.Abs(Math.Abs(sv) - 1) < 1e-6;
                }
                Check(worst < 1e-5 && fullFace, $"each face's corners are on it and show the texture where a vanilla cube's face would (off by {worst:e1}); a whole face shows all of it");
                // the mesh the client hands the game (vanilla's AddFace): a face each four corners where the faces are, each
                // with its own material's piece of the atlas and its normal
                var cells = new byte[64];
                var r = new Random(11);
                for (int b = 0; b < 64; b++) cells[b] = r.Next(3) == 0 ? WreckMat.None : (byte)r.Next(1, WreckMat.Count);
                var wc = WreckCells.FromCells(cells);
                var atlas = new Dictionary<byte, TextureAtlasPosition>();
                for (byte m = 1; m < WreckMat.Count; m++) atlas[m] = new TextureAtlasPosition { x1 = 0.1f * m, y1 = 0.05f * m, x2 = 0.1f * m + 0.03f, y2 = 0.05f * m + 0.02f, atlasTextureId = 7 };
                var mesh = WreckMesh.Build(wc, null, m => atlas[m]);
                var qs = wc.Quads();
                double off = 0;
                bool inPiece = true, normals = true;
                for (int n = 0; n < qs.Count; n++)
                {
                    var q = qs[n];
                    var tp = atlas[q.Mat];
                    q.Box(out float cx, out float cy, out float cz, out float sx, out float sy, out float sz);
                    for (int j = 0; j < 4; j++)
                    {
                        int v = n * 4 + j;
                        WreckFaces.Corner(q.Face, j, cx, cy, cz, sx, sy, sz, out float x, out float y, out float z);
                        WreckFaces.At(q.Face, x, y, z, out float u, out float vv);
                        off = Math.Max(off, Math.Abs(mesh.xyz[v * 3] - x) + Math.Abs(mesh.xyz[v * 3 + 1] - y) + Math.Abs(mesh.xyz[v * 3 + 2] - z));
                        off = Math.Max(off, Math.Abs(mesh.Uv[v * 2] - (tp.x1 + u * (tp.x2 - tp.x1))) + Math.Abs(mesh.Uv[v * 2 + 1] - (tp.y1 + vv * (tp.y2 - tp.y1))));
                        inPiece &= mesh.Uv[v * 2] >= tp.x1 - 1e-6 && mesh.Uv[v * 2] <= tp.x2 + 1e-6 && mesh.Uv[v * 2 + 1] >= tp.y1 - 1e-6 && mesh.Uv[v * 2 + 1] <= tp.y2 + 1e-6;
                        normals &= (mesh.Flags[v] & BlockFacing.AllVertexFlagsNormals[q.Face]) == BlockFacing.AllVertexFlagsNormals[q.Face];
                    }
                }
                Check(mesh.VerticesCount == qs.Count * 4 && mesh.IndicesCount == qs.Count * 6 && mesh.XyzFacesCount == qs.Count && off < 1e-5 && inPiece && normals,
                    $"its mesh as the client makes it (vanilla's AddFace): {qs.Count} faces, each corner where it should be with its material's piece of the atlas, and its normal (off by {off:e1})");
            }

            // ---- what breaking one gives
            {
                var oak = WreckCells.FromCells(Enumerable.Repeat(WreckMat.Oak, 64).ToArray());
                var halfOak = WreckCells.FromCells(Enumerable.Range(0, 64).Select(b => b < 32 ? WreckMat.Oak : WreckMat.None).ToArray());
                var copper = WreckCells.FromCells(Enumerable.Range(0, 64).Select(b => b < 16 ? WreckMat.Copper : WreckMat.None).ToArray());
                var iron = WreckCells.FromCells(Enumerable.Repeat(WreckMat.Iron, 64).ToArray());
                var codes = WreckDrops.Table.Values.SelectMany(d => d).Select(d => d.Code).ToHashSet();
                (double Mean, int Min, int Max) Stat(WreckCells c, string code, int n = 20000)
                {
                    var r = new Random(9);
                    long sum = 0;
                    int mn = int.MaxValue, mx = 0;
                    for (int i = 0; i < n; i++)
                    {
                        int got = WreckDrops.Roll(c, r).Where(d => d.Code == code).Sum(d => d.Count);
                        sum += got; mn = Math.Min(mn, got); mx = Math.Max(mx, got);
                    }
                    return ((double)sum / n, mn, mx);
                }
                var planks = Stat(oak, "game:plank-oak");
                var halfPlanks = Stat(halfOak, "game:plank-oak");
                var fire = Stat(oak, "game:firewood");
                var plates = Stat(copper, "game:metalplate-copper");
                var bits = Stat(copper, "game:metalbit-copper");
                var nails = Stat(iron, "game:metalnailsandstrips-iron");
                Check(Math.Abs(planks.Mean - 3) < 0.05 && planks.Min == 2 && planks.Max == 4 && Math.Abs(halfPlanks.Mean - 1.5) < 0.05 && halfPlanks.Min >= 1,
                    $"a full block of weathered oak gives 2..4 oak planks, {planks.Mean:0.00} on average; half a block {halfPlanks.Mean:0.00} ({halfPlanks.Min}..{halfPlanks.Max})");
                Check(Math.Abs(fire.Mean - 0.15) < 0.02 && Math.Abs(plates.Mean - 0.0875) < 0.02 && Math.Abs(bits.Mean - 0.5) < 0.05 && Math.Abs(nails.Mean - 2) < 0.05,
                    $"now and then firewood ({fire.Mean:0.00} a block); a face of copper sheathing (16 cells) {plates.Mean:0.00} plates and {bits.Mean:0.00} bits; a block of iron {nails.Mean:0.00} nails and strips");
                bool never = true;
                var r2 = new Random(3);
                for (int i = 0; i < 2000; i++)
                {
                    var cells = new byte[64];
                    for (int b = 0; b < 64; b++) cells[b] = (byte)r2.Next(WreckMat.Count);
                    never &= WreckDrops.Roll(WreckCells.FromCells(cells), r2).All(d => codes.Contains(d.Code) && d.Count > 0);
                }
                Check(never && codes.All(c => c.StartsWith("game:")) && !codes.Contains("ships:wreck"), "it gives only vanilla raw materials from the table, never the block (or a chiselled one)");
            }

            // ---- broken up on a seabed: the same wreck from the same seed
            // she lies on sand, her keel a little into it, heeled to port and down by the head, with holes along her port side
            double floorY = 40 + def.Grid.OY + 0.6;
            var sand = new TestSite { Floor = (x, z) => Math.Floor(floorY) + (((x * 7 + z * 13) & 7) == 0 ? 1 : 0) };
            var hurt = new float[l.Count];
            var holed = l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Part.Side > 0 && p.Center.Y < def.Waterline && Math.Abs(p.Center.X) < 8).Take(5).ToList();
            foreach (var p in holed) hurt[p.Index] = 0.95f;
            var cargo = def.Fixtures.Where(f => f.Kind == "cargo").Select(f => f.Code).Take(4).Append("chest-lowerdeck-1").ToArray();
            WreckInput Lying(long seed) => new()
            {
                Def = def, X = 500.37, Y = 40, Z = -300.81, Yaw = 1.1, List = -0.35, Trim = -0.1, Hurt = hurt, Clad = true,
                Seed = seed, WreckId = 77, Containers = cargo, ExtraChests = 2,
            };
            var plan = WreckBuilder.Build(Lying(42), sand);
            var again = WreckBuilder.Build(Lying(42), sand);
            var other = WreckBuilder.Build(Lying(43), sand);
            Check(plan.Fingerprint() == again.Fingerprint() && plan.Fingerprint() != other.Fingerprint() && plan.Blocks.Count > 500,
                $"the same seed makes the same wreck ({plan.Blocks.Count} blocks, {plan.CellsPlaced} cells), another seed another ({other.Blocks.Count} blocks)");
            // built again over the blocks it laid (a reload in the middle): they're hers, and it's the same wreck
            var mine = new TestSite { Floor = sand.Floor };
            foreach (long k in plan.Blocks.Keys) mine.Ours.Add(k);
            Check(WreckBuilder.Build(Lying(42), mine).Fingerprint() == plan.Fingerprint(), "built again over its own blocks (a reload in the middle), it's the same wreck");

            // nothing in the ground: no cell under the seabed's surface
            int under = 0;
            foreach (var kv in plan.Blocks)
            {
                WreckPlan.Unkey(kv.Key, out int bx, out int by, out int bz);
                double s = sand.Floor(bx, bz);
                var cells = kv.Value.ToCells();
                for (int b = 0; b < 64; b++)
                {
                    if (cells[b] == WreckMat.None) continue;
                    WreckCells.Cell(b, out _, out int cy, out _);
                    if (by + (cy + 0.5) * 0.25 < s) under++;
                }
            }
            Check(under == 0 && plan.CellsBuried + plan.CellsBlocked > 200,
                $"she's settled into the sand: {plan.CellsBuried + plan.CellsBlocked} of her cells under the seabed's surface left out, none laid in it");

            // her hull mostly there below her main deck, her upper works and her woodwork more broken, a castle fallen in
            var local = LocalCells(plan);
            var g2 = def.Grid;
            double Kept(Func<double, double, double, bool> where)
            {
                int design = 0, kept = local.Count(c => where(c.X, c.Y, c.Z));
                for (int i = 0; i < g2.NX; i++)
                    for (int j = 0; j < g2.NY; j++)
                        for (int k = 0; k < g2.NZ; k++)
                            if (g2.Solid(i, j, k) && where(g2.OX + (i + 0.5) * g2.V, g2.OY + (j + 0.5) * g2.V, g2.OZ + (k + 0.5) * g2.V)) design++;
                return design == 0 ? 0 : (double)kept / design;
            }
            double low = Kept((x, y, z) => y > def.Waterline && y < def.DeckY - 0.3 && Math.Abs(x) < 10);
            double upper = Kept((x, y, z) => y > def.DeckY + 0.5 && y < def.DeckY + 2 && Math.Abs(x) < 10);
            var quarter = def.Decks.First(d => d.Code == "quarter");
            var fore = def.Decks.First(d => d.Code == "forecastle");
            double sternCastle = Kept((x, y, z) => x < quarter.X1 - 1 && y > quarter.Y - 0.2 && y < quarter.Y + 2.5);
            double foreCastle = Kept((x, y, z) => x > fore.X0 + 1 && x < fore.X0 + 6 && y > fore.Y - 0.2 && y < fore.Y + 1.5);
            Check(low > 0.8 && upper < low - 0.08 && Math.Min(sternCastle, foreCastle) < 0.3 && Math.Max(sternCastle, foreCastle) > 0.4,
                $"broken up: {low:0%} of her sides above the waterline amidships left, {upper:0%} of her bulwarks; one castle fallen in (stern {sternCastle:0%}, forecastle {foreCastle:0%} left)");
            // holed where she was: little left round a holed panel's middle (her design there, mostly)
            {
                double near = 0.45;
                int keptNear = 0, designNear = 0;
                foreach (var p in holed)
                {
                    keptNear += local.Count(c => Math.Abs(c.X - p.Center.X) < near && Math.Abs(c.Y - p.Center.Y) < near && Math.Abs(c.Z - p.Center.Z) < near + 0.3);
                    for (int i = g2.CellX(p.Center.X - near); i <= g2.CellX(p.Center.X + near); i++)
                        for (int j = g2.CellY(p.Center.Y - near); j <= g2.CellY(p.Center.Y + near); j++)
                            for (int k = g2.CellZ(p.Center.Z - near - 0.3); k <= g2.CellZ(p.Center.Z + near + 0.3); k++)
                                if (g2.Solid(i, j, k)) designNear++;
                }
                Check(designNear > 0 && keptNear < designNear * 0.2 && plan.CellsHoled > 100,
                    $"holed where she was: round her {holed.Count} holed panels' middles {keptNear} of {designNear} cells left ({plan.CellsHoled} cells holed in all)");
            }
            // materials: tarred and copper outside, oak inside, pine stumps
            {
                var mats = local.GroupBy(c => c.Mat).ToDictionary(gr => gr.Key, gr => gr.Count());
                bool copperLow = local.Where(c => c.Mat == WreckMat.Copper).All(c => c.Y < def.Waterline + 0.75);
                Check(mats.GetValueOrDefault(WreckMat.Tarred) > 1000 && mats.GetValueOrDefault(WreckMat.Copper) > 500 && mats.GetValueOrDefault(WreckMat.Oak) > 5000 && copperLow &&
                      local.Any(c => c.Mat == WreckMat.Pine && Math.Abs(c.X - def.Masts[0].X) < 1 && c.Y < def.DeckY + 1.6),
                    $"her outside tarred ({mats.GetValueOrDefault(WreckMat.Tarred)} cells) and copper below her waterline ({mats.GetValueOrDefault(WreckMat.Copper)}), weathered oak inside ({mats.GetValueOrDefault(WreckMat.Oak)}), the mainmast's stump in pine");
            }
            // the masts fallen beside her (to port, the side she lies over to), the anchor by her bow
            {
                var pine = local.Where(c => c.Mat == WreckMat.Pine && c.Y < def.DeckY).ToList();
                int outside = local.Count(c => c.Mat == WreckMat.Pine && c.Z < -def.Beam / 2 - 0.5);
                var anchor = local.Where(c => c.Mat == WreckMat.Iron).ToList();
                Check(plan.CellsTimber > 300 && outside > 300 && local.Count(c => c.Mat == WreckMat.Pine && c.Z > def.Beam / 2 + 0.5) == 0,
                    $"her masts fallen over her port side as timbers on the seabed ({plan.CellsTimber} cells, {outside} of them clear of her side)");
                Check(plan.CellsAnchor > 20 && anchor.Count > 20 && anchor.All(c => c.X > def.Length * 0.2),
                    $"her anchor in iron on the seabed by her bow ({anchor.Count} cells, from x {anchor.DefaultIfEmpty().Min(c => c.X):0.0})");
            }
            // ground stays ground, plants give way
            {
                // rocks standing up from the seabed where her lowest blocks are, in some columns; plants in others
                var rocky = new TestSite { Floor = sand.Floor };
                var columns = plan.Order().GroupBy(k => { WreckPlan.Unkey(k, out int x, out _, out int z); return WreckPlan.Key(x, 0, z); }).ToList();
                var pick = columns.Where((c, i) => i % 7 == 0).Select(c => c.First()).Take(25).ToList();
                foreach (long k in pick) rocky.Rocks.Add(k);
                // (columns whose top cell she has as a block: one decayed away leaves nothing to give way to)
                var weeds = columns.Where((c, i) => i % 7 == 3).Select(c => c.Last()).Where(k => plan.Blocks.ContainsKey(k)).Take(25).ToList();
                foreach (long k in weeds) rocky.Plants.Add(k);
                var rp = WreckBuilder.Build(Lying(42), rocky);
                // (most: with rocks in her way her decay draws differ, a plant's column may lose its top cell to decay)
                Check(pick.All(k => !rp.Blocks.ContainsKey(k)) && weeds.Count(k => rp.Blocks.ContainsKey(k)) >= weeds.Count * 0.9 && rp.CellsBlocked > plan.CellsBlocked,
                    $"a rock in her way stays (none of her cells there: {rp.CellsBlocked - plan.CellsBlocked} more left out), plants give way to her" +
                    $" (rocks with her cells: {pick.Count(k => rp.Blocks.ContainsKey(k))} of {pick.Count}, plants given way: {weeds.Count(k => rp.Blocks.ContainsKey(k))} of {weeds.Count})");
            }
            // her guns, containers and extra chests: each in a block of its own, on something, none in the ground
            {
                bool Clear(WreckSpot s) => s != null && !plan.Blocks.ContainsKey(WreckPlan.Key(s.X, s.Y, s.Z)) && s.Y + 0.5 > sand.Floor(s.X, s.Z);
                var spots = plan.Guns.Concat(plan.Containers).Concat(plan.Extra).ToList();
                var keys = spots.Where(s => s != null).Select(s => WreckPlan.Key(s.X, s.Y, s.Z)).ToList();
                // (0.3.8: ten guns, five a side; six before)
                Check(plan.Guns.Count >= 6 && plan.Guns.All(Clear) && plan.Guns.All(s => s.Side is "north" or "east" or "south" or "west"),
                    $"her {plan.Guns.Count} guns: {plan.Guns.Count(s => s.InWreck)} where they stood, {plan.Guns.Count(s => !s.InWreck)} tumbled onto the seabed beside her, each in a free block");
                Check(plan.Containers.Count == cargo.Length && plan.Containers.All(Clear) && plan.Extra.Count == 2 && plan.Extra.All(s => Clear(s) && !s.InWreck) &&
                      keys.Distinct().Count() == keys.Count,
                    $"her containers: {plan.Containers.Count(s => s?.InWreck == true)} where they stood in her, {plan.Containers.Count(s => s?.InWreck == false)} beside her; two extra chests beside her; each in a block of its own");
                Check(plan.Sediment.Count > 0 && plan.Sediment.All(s => s.Y + 1 <= sand.Floor(s.X, s.Z) + 1.001 && !plan.Blocks.ContainsKey(WreckPlan.Key(s.X, s.Y, s.Z)) &&
                      plan.Blocks.Keys.Any(k => { WreckPlan.Unkey(k, out int x, out int y, out int z); return y == s.Y && Math.Abs(x - s.X) + Math.Abs(z - s.Z) == 1; }) &&
                      !Between(plan, s.X + 0.5, s.Y + 0.5, s.Z + 0.5)),
                    $"sand heaped against her lowest blocks in {plan.Sediment.Count} places on the seabed, outside her (not between her sides)");
            }
            Console.WriteLine($"  the wreck: {plan.Blocks.Count} blocks, {plan.CellsPlaced} cells (her design {plan.CellsDesign}: {plan.CellsDecayed} decayed, {plan.CellsHoled} holed, " +
                $"{plan.CellsBuried} buried, {plan.CellsBlocked} in rock), timbers {plan.CellsTimber}, anchor {plan.CellsAnchor}; site asked {sand.Asked} times");
            // what breaking all of her would give, on average
            {
                var total = new Dictionary<string, double>();
                foreach (var c in plan.Blocks.Values)
                    for (int i = 0; i < c.Mats.Length; i++)
                        foreach (var d in WreckDrops.Table[c.Mats[i]])
                            total[d.Code] = total.GetValueOrDefault(d.Code) + WreckDrops.Expected(c.Mats[i], d.Code, System.Numerics.BitOperations.PopCount(c.Masks[i]) / 64.0);
                Console.WriteLine("  broken up, all of her gives about: " + string.Join(", ", total.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value:0} {kv.Key.Replace("game:", "")}")));
            }
        }

        /// <summary>Is a world point between her sides (in her hold, under her decks), as the plan lays her?</summary>
        static bool Between(WreckPlan plan, double x, double y, double z)
        {
            var w = plan.Input;
            var g = w.Def.Grid;
            ShipFrame.ToLocal(w.Yaw, w.Trim, w.List, x - w.X, y - (w.Y - w.Settle), z - w.Z, out double lx, out double ly, out double lz);
            int i = g.CellX(lx), j = g.CellY(ly), k = g.CellZ(lz);
            bool port = false, starboard = false;
            for (int n = 0; n < g.NZ; n++)
                if (g.Solid(i, j, n)) { port |= n <= k; starboard |= n >= k; }
            return port && starboard;
        }

        static float Area(WreckQuad q)
        {
            float dx = q.X1 - q.X0, dy = q.Y1 - q.Y0, dz = q.Z1 - q.Z0;
            return WreckCells.FaceAxis[q.Face] switch { 0 => dy * dz, 1 => dx * dz, _ => dx * dy };
        }

        /// <summary>
        /// A wreck as the client draws it, for python tools/shipgen/texview.py wreck: every face of every block (its corners
        /// as AddFace makes them, the texture's place, the vanilla texture), the seabed round her, her guns, chests and sand
        /// as boxes. Written to the temp folder (ships-wreck-views/carrack-wreck.json).
        /// </summary>
        static void WreckViews(ShipDefinition def)
        {
            var l = def.DamageParts;
            double floorY = 40 + def.Grid.OY + 0.55;
            var site = new TestSite { Floor = (x, z) => Math.Floor(floorY) };
            var hurt = new float[l.Count];
            foreach (var p in l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Part.Side < 0 && p.Center.Y < def.Waterline + 1.5 && Math.Abs(p.Center.X - 2) < 9).Where((p, i) => i % 3 == 0))
                hurt[p.Index] = 0.85f;
            var w = new WreckInput
            {
                Def = def, X = 0.3, Y = 40, Z = 0.6, Yaw = Math.PI / 2 + 0.35, List = 0.33, Trim = -0.08, Hurt = hurt, Seed = 7, WreckId = 1,
                Containers = def.Fixtures.Where(f => f.Kind == "cargo").Select(f => f.Code).Take(6).ToArray(), ExtraChests = 2,
            };
            var plan = WreckBuilder.Build(w, site);
            var quads = new List<object>();
            var textures = new List<string>();
            int Tex(string code)
            {
                int i = textures.IndexOf(code);
                if (i < 0) { i = textures.Count; textures.Add(code); }
                return i;
            }
            foreach (var kv in plan.Blocks)
            {
                WreckPlan.Unkey(kv.Key, out int bx, out int by, out int bz);
                var cells = kv.Value;
                var beside = new WreckCells[6];
                for (int f = 0; f < 6; f++)
                {
                    int[] n = { bx, by, bz };
                    n[WreckCells.FaceAxis[f]] += WreckCells.FaceSign[f];
                    beside[f] = plan.Blocks.GetValueOrDefault(WreckPlan.Key(n[0], n[1], n[2]));
                }
                foreach (var q in cells.Quads((f, x, y, z) => beside[f]?.Has(x, y, z) == true))
                {
                    q.Box(out float cx, out float cy, out float cz, out float sx, out float sy, out float sz);
                    q.Uv(out float ou, out float ov, out float su, out float sv);
                    var c = new List<float> { Tex(WreckMat.GameTexture(q.Mat)) };
                    for (int j = 0; j < 4; j++)
                    {
                        WreckFaces.Corner(q.Face, j, cx, cy, cz, sx, sy, sz, out float x, out float y, out float z);
                        float u = ou + su * CubeMeshUtil.CubeUvCoords[q.Face * 8 + j * 2], v = ov + sv * CubeMeshUtil.CubeUvCoords[q.Face * 8 + j * 2 + 1];
                        c.AddRange(new[] { bx + x, by + y, bz + z, u * 16, v * 16 });
                    }
                    quads.Add(c);
                }
            }
            var boxes = new List<object>();
            foreach (var s in plan.Guns)
            {
                bool alongZ = s.Side is "north" or "south";
                boxes.Add(new { what = "gun", lo = new[] { s.X + (alongZ ? 0.3 : -1.1), s.Y, s.Z + (alongZ ? -1.1 : 0.3) }, hi = new[] { s.X + (alongZ ? 0.7 : 1.9), s.Y + 0.55, s.Z + (alongZ ? 1.9 : 0.7) }, tex = "game:block/metal/plate/tinbronze" });
            }
            foreach (var s in plan.Containers.Concat(plan.Extra).Where(s => s != null))
                boxes.Add(new { what = "chest", lo = new[] { s.X + 0.06, s.Y + 0.0, s.Z + 0.06 }, hi = new[] { s.X + 0.94, s.Y + 0.875, s.Z + 0.94 }, tex = "game:block/wood/chest/sides" });
            foreach (var s in plan.Sediment)
                boxes.Add(new { what = "sand", lo = new[] { s.X + 0.0, s.Y, s.Z }, hi = new[] { s.X + 1.0, s.Y + 1, s.Z + 1 }, tex = "game:block/stone/sand/granite" });
            string dir = Path.Combine(Path.GetTempPath(), "ships-wreck-views");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{def.Code}-wreck.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new
            {
                textures, quads, boxes, seabed = Math.Floor(floorY), origin = new[] { w.X, w.Y, w.Z }, yaw = w.Yaw,
                min = new[] { plan.MinX, plan.MinY, plan.MinZ }, max = new[] { plan.MaxX, plan.MaxY, plan.MaxZ },
            }));
            Console.WriteLine(path);
            Console.WriteLine($"  {plan.Blocks.Count} blocks, {quads.Count} faces; guns {string.Join(", ", plan.Guns)}; containers {plan.Containers.Count(s => s?.InWreck == true)} in her, {plan.Containers.Count(s => s?.InWreck == false)} beside");
        }
    }
}
