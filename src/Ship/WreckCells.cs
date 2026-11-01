using System;
using System.Collections.Generic;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>What a wreck block's cells are made of (a byte; 0 is none).</summary>
    public static class WreckMat
    {
        public const byte None = 0, Oak = 1, Tarred = 2, Pine = 3, Copper = 4, Iron = 5, Brass = 6, Lead = 7;
        public const int Count = 8;

        /// <summary>The texture code in the wreck block's JSON (blocktypes/wreck.json) for a material.</summary>
        public static string Texture(byte m) => m switch
        {
            Tarred => "tarred",
            Pine => "pine",
            Copper => "copper",
            Iron => "iron",
            Brass => "brass",
            Lead => "lead",
            _ => "oak"
        };

        /// <summary>The vanilla texture each is drawn with (the block's JSON says the same; texview.py draws the views with these).</summary>
        public static string GameTexture(byte m) => m switch
        {
            Tarred => "game:block/wood/planks/aged/veryaged1",
            Pine => "game:block/wood/debarked/aged",
            Copper => "game:block/metal/tarnished/tarnished-copper",
            Iron => "game:block/metal/tarnished/rusty-iron",
            Brass => "game:block/metal/tarnished/brass",
            Lead => "game:block/metal/tarnished/tarnished-lead",
            _ => "game:block/wood/planks/aged/aged1"
        };

        /// <summary>Lang code of its name ("ships:wreckmat-tarred").</summary>
        public static string Lang(byte m) => "ships:wreckmat-" + Texture(m);

        public static bool Metal(byte m) => m == Copper || m == Iron || m == Brass || m == Lead;
    }

    /// <summary>
    /// One wreck block's contents: a 4 × 4 × 4 grid of 0.25 cells (her collision grid's size), each empty or of one
    /// material. Kept as layers: a material and the 64-bit mask of its cells, so a block of one material is one ulong.
    /// Bit (cy · 4 + cz) · 4 + cx is the cell at (cx, cy, cz) from the block's corner. Immutable (the client's tesselation
    /// thread reads a neighbour's while the main thread may replace it).
    /// </summary>
    public sealed class WreckCells
    {
        public const int N = 4;
        public const float Size = 0.25f;

        public readonly byte[] Mats;
        public readonly ulong[] Masks;
        /// <summary>Every cell that's there, whatever it's of.</summary>
        public readonly ulong All;

        public WreckCells(byte[] mats, ulong[] masks)
        {
            Mats = mats ?? Array.Empty<byte>();
            Masks = masks ?? Array.Empty<ulong>();
            ulong all = 0;
            for (int i = 0; i < Math.Min(Mats.Length, Masks.Length); i++) if (Mats[i] != WreckMat.None) all |= Masks[i];
            All = all;
        }

        public static int Bit(int cx, int cy, int cz) => (cy * N + cz) * N + cx;

        public static void Cell(int bit, out int cx, out int cy, out int cz)
        {
            cx = bit & 3;
            cz = bit >> 2 & 3;
            cy = bit >> 4;
        }

        public int Count => BitOperations.PopCount(All);
        public bool Empty => All == 0;
        public bool Has(int cx, int cy, int cz) => (All >> Bit(cx, cy, cz) & 1) != 0;

        /// <summary>From 64 bytes (bit order), a material each (0 empty).</summary>
        public static WreckCells FromCells(byte[] cells)
        {
            var masks = new ulong[WreckMat.Count];
            for (int b = 0; b < 64; b++) if (cells[b] != WreckMat.None && cells[b] < WreckMat.Count) masks[cells[b]] |= 1UL << b;
            var mats = new List<byte>();
            var ms = new List<ulong>();
            for (byte m = 1; m < WreckMat.Count; m++)
            {
                if (masks[m] == 0) continue;
                mats.Add(m);
                ms.Add(masks[m]);
            }
            return new WreckCells(mats.ToArray(), ms.ToArray());
        }

        public byte[] ToCells()
        {
            var c = new byte[64];
            for (int i = 0; i < Mats.Length && i < Masks.Length; i++)
                for (int b = 0; b < 64; b++)
                    if ((Masks[i] >> b & 1) != 0) c[b] = Mats[i];
            return c;
        }

        public byte MatAt(int bit)
        {
            for (int i = 0; i < Mats.Length && i < Masks.Length; i++) if ((Masks[i] >> bit & 1) != 0) return Mats[i];
            return WreckMat.None;
        }

        public int CountOf(byte mat)
        {
            int n = 0;
            for (int i = 0; i < Mats.Length && i < Masks.Length; i++) if (Mats[i] == mat) n += BitOperations.PopCount(Masks[i]);
            return n;
        }

        /// <summary>The material with the most cells (oak if none).</summary>
        public byte Main
        {
            get
            {
                byte best = WreckMat.Oak;
                int most = -1;
                for (int i = 0; i < Mats.Length && i < Masks.Length; i++)
                {
                    int n = BitOperations.PopCount(Masks[i]);
                    if (n > most) { most = n; best = Mats[i]; }
                }
                return best;
            }
        }

        // ------------------------------------------------------------------ saved with the block entity

        public void ToTree(ITreeAttribute t)
        {
            t.SetBytes("mats", (byte[])Mats.Clone());
            var l = new long[Masks.Length];
            for (int i = 0; i < l.Length; i++) l[i] = unchecked((long)Masks[i]);
            t["masks"] = new LongArrayAttribute(l);
        }

        /// <summary>What <see cref="ToTree"/> wrote, or null if there's nothing there.</summary>
        public static WreckCells FromTree(ITreeAttribute t)
        {
            var mats = t?.GetBytes("mats");
            var l = (t?["masks"] as LongArrayAttribute)?.value;
            if (mats == null || l == null || mats.Length != l.Length) return null;
            var m = new ulong[l.Length];
            for (int i = 0; i < l.Length; i++) m[i] = unchecked((ulong)l[i]);
            return new WreckCells(mats, m);
        }

        // ------------------------------------------------------------------ boxes (collision, selection)

        /// <summary>
        /// Its cells merged into boxes (in cells: x0 y0 z0 x1 y1 z1, ends exclusive): runs along x, then widened along z,
        /// then up along y, where every cell is there and not yet in a box. They cover exactly its cells, without overlap.
        /// </summary>
        public static List<int[]> Boxes(ulong mask)
        {
            var boxes = new List<int[]>();
            ulong left = mask;
            while (left != 0)
            {
                int b = BitOperations.TrailingZeroCount(left);
                Cell(b, out int x0, out int y0, out int z0);
                int x1 = x0 + 1;
                while (x1 < N && (left >> Bit(x1, y0, z0) & 1) != 0) x1++;
                int z1 = z0 + 1;
                while (z1 < N && Row(left, x0, x1, y0, z1)) z1++;
                int y1 = y0 + 1;
                while (y1 < N && Slab(left, x0, x1, y1, z0, z1)) y1++;
                for (int y = y0; y < y1; y++)
                    for (int z = z0; z < z1; z++)
                        for (int x = x0; x < x1; x++) left &= ~(1UL << Bit(x, y, z));
                boxes.Add(new[] { x0, y0, z0, x1, y1, z1 });
            }
            return boxes;
        }

        static bool Row(ulong m, int x0, int x1, int y, int z)
        {
            for (int x = x0; x < x1; x++) if ((m >> Bit(x, y, z) & 1) == 0) return false;
            return true;
        }

        static bool Slab(ulong m, int x0, int x1, int y, int z0, int z1)
        {
            for (int z = z0; z < z1; z++) if (!Row(m, x0, x1, y, z)) return false;
            return true;
        }

        /// <summary>Its boxes as the game takes them (block-local, 0..1).</summary>
        public Cuboidf[] Cuboids()
        {
            var bs = Boxes(All);
            var c = new Cuboidf[bs.Count];
            for (int i = 0; i < c.Length; i++)
            {
                var b = bs[i];
                c[i] = new Cuboidf(b[0] * Size, b[1] * Size, b[2] * Size, b[3] * Size, b[4] * Size, b[5] * Size);
            }
            return c;
        }

        // ------------------------------------------------------------------ faces (what the client draws)

        /// <summary>
        /// The faces to draw: each cell's side that no cell is against (in this block, or, past its edge, in the
        /// neighbour: covered(face, cx, cy, cz) is asked with the cell's place in that neighbour), merged into rectangles
        /// of one material on each slice. Faces in BlockFacing order (north -z, east +x, south +z, west -x, up, down).
        /// </summary>
        public List<WreckQuad> Quads(Func<int, int, int, int, bool> covered = null)
        {
            var quads = new List<WreckQuad>();
            var cells = ToCells();
            var grid = new byte[N * N];
            var c = new int[3];
            for (int f = 0; f < 6; f++)
            {
                int axis = FaceAxis[f], dir = FaceSign[f];
                // the two axes across the face: p, q
                int pa = axis == 0 ? 2 : 0, qa = axis == 1 ? 2 : 1;
                for (int t = 0; t < N; t++)
                {
                    bool any = false;
                    for (int p = 0; p < N; p++)
                        for (int q = 0; q < N; q++)
                        {
                            c[axis] = t;
                            c[pa] = p;
                            c[qa] = q;
                            byte m = cells[Bit(c[0], c[1], c[2])];
                            grid[p * N + q] = 0;
                            if (m == WreckMat.None) continue;
                            c[axis] = t + dir;
                            bool hidden;
                            if (c[axis] >= 0 && c[axis] < N) hidden = cells[Bit(c[0], c[1], c[2])] != WreckMat.None;
                            else
                            {
                                c[axis] = (c[axis] + N) % N;
                                hidden = covered != null && covered(f, c[0], c[1], c[2]);
                            }
                            if (hidden) continue;
                            grid[p * N + q] = m;
                            any = true;
                        }
                    if (!any) continue;
                    // merge: runs along p, widened along q
                    for (int q0 = 0; q0 < N; q0++)
                        for (int p0 = 0; p0 < N; p0++)
                        {
                            byte m = grid[p0 * N + q0];
                            if (m == WreckMat.None) continue;
                            int p1 = p0 + 1;
                            while (p1 < N && grid[p1 * N + q0] == m) p1++;
                            int q1 = q0 + 1;
                            while (q1 < N)
                            {
                                bool all = true;
                                for (int p = p0; p < p1; p++) if (grid[p * N + q1] != m) { all = false; break; }
                                if (!all) break;
                                q1++;
                            }
                            for (int q = q0; q < q1; q++)
                                for (int p = p0; p < p1; p++) grid[p * N + q] = 0;
                            var lo = new float[3];
                            var hi = new float[3];
                            float plane = (dir > 0 ? t + 1 : t) * Size;
                            lo[axis] = hi[axis] = plane;
                            lo[pa] = p0 * Size;
                            hi[pa] = p1 * Size;
                            lo[qa] = q0 * Size;
                            hi[qa] = q1 * Size;
                            quads.Add(new WreckQuad { Face = (byte)f, Mat = m, X0 = lo[0], Y0 = lo[1], Z0 = lo[2], X1 = hi[0], Y1 = hi[1], Z1 = hi[2] });
                        }
                }
            }
            return quads;
        }

        /// <summary>BlockFacing order: the axis a face looks along (0 x, 1 y, 2 z) and which way.</summary>
        public static readonly int[] FaceAxis = { 2, 0, 2, 0, 1, 1 }, FaceSign = { -1, 1, 1, -1, 1, -1 };
    }

    /// <summary>A face to draw: its rectangle (block-local; flat along its face's axis), the way it faces, its material.</summary>
    public struct WreckQuad
    {
        public byte Face, Mat;
        public float X0, Y0, Z0, X1, Y1, Z1;

        /// <summary>
        /// The box vanilla's ModelCubeUtilExt.AddFace draws this face of: its middle and size (a cell deep behind the face).
        /// </summary>
        public void Box(out float cx, out float cy, out float cz, out float sx, out float sy, out float sz)
        {
            float[] lo = { X0, Y0, Z0 }, hi = { X1, Y1, Z1 };
            int a = WreckCells.FaceAxis[Face];
            if (WreckCells.FaceSign[Face] > 0) lo[a] -= WreckCells.Size;
            else hi[a] += WreckCells.Size;
            cx = (lo[0] + hi[0]) / 2; cy = (lo[1] + hi[1]) / 2; cz = (lo[2] + hi[2]) / 2;
            sx = hi[0] - lo[0]; sy = hi[1] - lo[1]; sz = hi[2] - lo[2];
        }

        /// <summary>
        /// The texture's place for AddFace (0..1 of the texture: its origin and size, to be scaled into the atlas), so the
        /// texture lies on the block as on a vanilla cube (<see cref="WreckFaces"/>): seamless across neighbouring faces.
        /// </summary>
        public void Uv(out float ou, out float ov, out float su, out float sv) => WreckFaces.Uv(this, out ou, out ov, out su, out sv);
    }

    /// <summary>
    /// How vanilla lays a texture on a cube's faces (CubeMeshUtil's vertices and their uv corners), as a map from a point
    /// on a face (block-local) to the texture (0..1). A small face gets the part of the texture its place on the block
    /// would show, so a wreck block's faces join up like one block's.
    /// </summary>
    public static class WreckFaces
    {
        // per face: the axis u runs along, u at that axis' 0, and its slope; the same for v
        static readonly int[] uAxis = new int[6], vAxis = new int[6];
        static readonly float[] u0 = new float[6], uSlope = new float[6], v0 = new float[6], vSlope = new float[6];

        static WreckFaces()
        {
            for (int f = 0; f < 6; f++)
            {
                var c = new int[4][];
                var a = new int[4][];
                for (int j = 0; j < 4; j++)
                {
                    c[j] = new int[3];
                    for (int k = 0; k < 3; k++) c[j][k] = (CubeMeshUtil.CubeVertices[f * 12 + j * 3 + k] + 1) / 2;
                    a[j] = new[] { CubeMeshUtil.CubeUvCoords[f * 8 + j * 2], CubeMeshUtil.CubeUvCoords[f * 8 + j * 2 + 1] };
                }
                for (int uv = 0; uv < 2; uv++)
                {
                    // two corners that differ in this texture coordinate only: the block axis they differ along is its axis
                    for (int j = 0; j < 4; j++)
                        for (int k = 0; k < 4; k++)
                        {
                            if (a[j][uv] == a[k][uv] || a[j][1 - uv] != a[k][1 - uv]) continue;
                            int ax = -1;
                            for (int d = 0; d < 3; d++) if (c[j][d] != c[k][d]) ax = d;
                            if (ax < 0) continue;
                            float slope = (float)(a[k][uv] - a[j][uv]) / (c[k][ax] - c[j][ax]);
                            float at0 = a[j][uv] - slope * c[j][ax];
                            if (uv == 0) { uAxis[f] = ax; uSlope[f] = slope; u0[f] = at0; }
                            else { vAxis[f] = ax; vSlope[f] = slope; v0[f] = at0; }
                        }
                }
            }
        }

        /// <summary>Where a point on a face shows the texture (0..1, as a vanilla cube's face does).</summary>
        public static void At(int face, float x, float y, float z, out float u, out float v)
        {
            float[] p = { x, y, z };
            u = u0[face] + uSlope[face] * p[uAxis[face]];
            v = v0[face] + vSlope[face] * p[vAxis[face]];
        }

        /// <summary>The corners AddFace makes for a face of this box (vertex j: CubeVertices), and the uv each gets (CubeUvCoords).</summary>
        public static void Corner(int face, int j, float cx, float cy, float cz, float sx, float sy, float sz, out float x, out float y, out float z)
        {
            int n = face * 12 + j * 3;
            x = cx + sx * CubeMeshUtil.CubeVertices[n] / 2f;
            y = cy + sy * CubeMeshUtil.CubeVertices[n + 1] / 2f;
            z = cz + sz * CubeMeshUtil.CubeVertices[n + 2] / 2f;
        }

        public static void Uv(WreckQuad q, out float ou, out float ov, out float su, out float sv)
        {
            q.Box(out float cx, out float cy, out float cz, out float sx, out float sy, out float sz);
            ou = ov = su = sv = 0;
            float bu = 0, bv = 0;
            int got = 0;
            for (int j = 0; j < 4; j++)
            {
                int a = CubeMeshUtil.CubeUvCoords[q.Face * 8 + j * 2], b = CubeMeshUtil.CubeUvCoords[q.Face * 8 + j * 2 + 1];
                Corner(q.Face, j, cx, cy, cz, sx, sy, sz, out float x, out float y, out float z);
                At(q.Face, x, y, z, out float u, out float v);
                if (a == 0 && b == 0) { ou = u; ov = v; got |= 1; }
                if (a == 1 && b == 0) { bu = u; got |= 2; }
                if (a == 0 && b == 1) { bv = v; got |= 4; }
            }
            su = bu - ou;
            sv = bv - ov;
        }
    }

    /// <summary>
    /// A wreck block's mesh as the game takes it from a block entity (vanilla's ModelCubeUtilExt.AddFace per face, as the
    /// shape tesselator and microblocks make theirs): its faces (<see cref="WreckCells.Quads"/>), each with its material's
    /// texture from the block atlas laid as on a vanilla cube, shaded by its normal; the chunk tesselator lights it and
    /// leaves out a face on the block's edge against a solid block.
    /// </summary>
    public static class WreckMesh
    {
        public static MeshData Build(WreckCells cells, Func<int, int, int, int, bool> covered, Func<byte, TextureAtlasPosition> texture)
        {
            var quads = cells.Quads(covered);
            var mesh = new MeshData(Math.Max(4, quads.Count * 4), Math.Max(6, quads.Count * 6), false, true, true, true).WithXyzFaces().WithRenderpasses().WithColorMaps();
            var tex = new TextureAtlasPosition[WreckMat.Count];
            var flags = new int[4];
            foreach (var q in quads)
            {
                var tp = tex[q.Mat] ??= texture(q.Mat);
                q.Box(out float cx, out float cy, out float cz, out float sx, out float sy, out float sz);
                q.Uv(out float ou, out float ov, out float su, out float sv);
                float w = tp.x2 - tp.x1, h = tp.y2 - tp.y1;
                Array.Fill(flags, BlockFacing.AllVertexFlagsNormals[q.Face]);
                ModelCubeUtilExt.AddFace(mesh, BlockFacing.ALLFACES[q.Face], new Vec3f(cx, cy, cz), new Vec3f(sx, sy, sz),
                    new Vec2f(tp.x1 + ou * w, tp.y1 + ov * h), new Vec2f(su * w, sv * h), tp.atlasTextureId, ColorUtil.WhiteArgb,
                    ModelCubeUtilExt.EnumShadeMode.On, flags);
            }
            return mesh;
        }
    }

    /// <summary>One line of the drop table: an item, how many a full block (64 cells) of the material gives, and how sure.</summary>
    public class WreckDrop
    {
        public string Code;
        /// <summary>A full block's worth on average, give or take Spread (evenly); scaled by its cells.</summary>
        public float PerBlock, Spread;
        /// <summary>Below 1: it drops at all only this often (for a full block; fewer cells, less often), then 1..PerBlock.</summary>
        public float Chance = 1;

        public WreckDrop(string code, float perBlock, float spread, float chance = 1)
        {
            Code = code;
            PerBlock = perBlock;
            Spread = spread;
            Chance = chance;
        }
    }

    /// <summary>
    /// What breaking a wreck block gives: part of its raw materials, by its cells of each material, never the block itself.
    /// The numbers (also in the README):
    /// - weathered oak (her inside, decks, woodwork): 3 ± 1 oak planks a full block; 15% a firewood, 20% 1–2 sticks
    /// - tarred oak (her planking outside): 2.5 ± 1 oak planks; 10% a firewood, 15% a stick, 12% a resin, 8% nails and strips
    /// - pine (her masts): 3 ± 1 pine planks; 15% a firewood, 20% 1–2 sticks
    /// - copper sheathing: 0.35 ± 0.15 copper plates and 2 ± 1 copper bits a full block (a face of sheathing is 16 cells: the
    ///   carrack's 48 plates come back as about 25 plates and 150 bits)
    /// - iron (the anchor): 2 ± 1 iron nails and strips and 5 ± 2 iron bits
    /// A block of n cells gives n/64 of that; a fraction left over is the chance of one more.
    /// </summary>
    public static class WreckDrops
    {
        public static readonly Dictionary<byte, WreckDrop[]> Table = new()
        {
            [WreckMat.Oak] = new[]
            {
                new WreckDrop("game:plank-oak", 3, 1),
                new WreckDrop("game:firewood", 1, 0, 0.15f),
                new WreckDrop("game:stick", 1.5f, 0.5f, 0.2f),
            },
            [WreckMat.Tarred] = new[]
            {
                new WreckDrop("game:plank-oak", 2.5f, 1),
                new WreckDrop("game:firewood", 1, 0, 0.1f),
                new WreckDrop("game:stick", 1, 0, 0.15f),
                new WreckDrop("game:resin", 1, 0, 0.12f),
                new WreckDrop("game:metalnailsandstrips-iron", 1, 0, 0.08f),
            },
            [WreckMat.Pine] = new[]
            {
                new WreckDrop("game:plank-pine", 3, 1),
                new WreckDrop("game:firewood", 1, 0, 0.15f),
                new WreckDrop("game:stick", 1.5f, 0.5f, 0.2f),
            },
            [WreckMat.Copper] = new[]
            {
                new WreckDrop("game:metalplate-copper", 0.35f, 0.15f),
                new WreckDrop("game:metalbit-copper", 2, 1),
            },
            // ((1 November 2026) a brass- or lead-sheathed bottom: as copper)
            [WreckMat.Brass] = new[]
            {
                new WreckDrop("game:metalplate-brass", 0.35f, 0.15f),
                new WreckDrop("game:metalbit-brass", 2, 1),
            },
            [WreckMat.Lead] = new[]
            {
                new WreckDrop("game:metalplate-lead", 0.35f, 0.15f),
                new WreckDrop("game:metalbit-lead", 2, 1),
            },
            [WreckMat.Iron] = new[]
            {
                new WreckDrop("game:metalnailsandstrips-iron", 2, 1),
                new WreckDrop("game:metalbit-iron", 5, 2),
            },
        };

        /// <summary>What these cells give (item code, how many), rolled with this random; mult: the game's drop multiplier.</summary>
        public static List<(string Code, int Count)> Roll(WreckCells cells, Random rand, float mult = 1)
        {
            var got = new Dictionary<string, int>();
            var order = new List<string>();
            if (cells != null)
            {
                for (int i = 0; i < cells.Mats.Length && i < cells.Masks.Length; i++)
                {
                    if (!Table.TryGetValue(cells.Mats[i], out var drops)) continue;
                    double share = BitOperations.PopCount(cells.Masks[i]) / 64.0;
                    foreach (var d in drops)
                    {
                        int n = Amount(d, share, mult, rand);
                        if (n <= 0) continue;
                        if (!got.ContainsKey(d.Code)) order.Add(d.Code);
                        got[d.Code] = got.GetValueOrDefault(d.Code) + n;
                    }
                }
            }
            var list = new List<(string, int)>();
            foreach (string c in order) list.Add((c, got[c]));
            return list;
        }

        /// <summary>How many of one line a share of a block (cells / 64) gives.</summary>
        public static int Amount(WreckDrop d, double share, float mult, Random rand)
        {
            if (share <= 0) return 0;
            if (d.Chance < 1)
            {
                if (rand.NextDouble() >= d.Chance * share * mult) return 0;
                double n1 = d.PerBlock + d.Spread * (2 * rand.NextDouble() - 1);
                return Math.Max(1, (int)Math.Round(n1));
            }
            double n = share * mult * (d.PerBlock + d.Spread * (2 * rand.NextDouble() - 1));
            int whole = (int)Math.Floor(n);
            return whole + (rand.NextDouble() < n - whole ? 1 : 0);
        }

        /// <summary>What a full block of a material gives on average (for the tests and the README).</summary>
        public static double Expected(byte mat, string code, double share = 1)
        {
            if (!Table.TryGetValue(mat, out var drops)) return 0;
            double e = 0;
            foreach (var d in drops)
            {
                if (d.Code != code) continue;
                e += d.Chance < 1 ? Math.Min(1, d.Chance * share) * Math.Max(1, d.PerBlock) : share * d.PerBlock;
            }
            return e;
        }
    }
}
