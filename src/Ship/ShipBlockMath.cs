using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Datastructures;

namespace Hansa
{
    /// <summary>
    /// (0.3.14, working blocks aboard) The arithmetic between a ship's block grid and her mini-dimension, pure (no game
    /// objects: the offline tests use it as it is).
    /// - A mini-dimension (the engine's BlockAccessorMovable) is a 16384 × 32768 × 16384 cube of block space in dimension 1;
    ///   sub-dimension n has its origin at (n % 4096 · 16384 + 8192, 8192, n / 4096 · 16384 + 8192).
    /// - A ship's cell (i, j, k) on her block grid (its corner ship-local at grid origin + (i, j, k)) is the dimension's block
    ///   at origin + (i, j, k). So the dimension's axes are her ship-local axes; drawn, the dimension is turned and moved as
    ///   her hull is (ShipBlocksClient), the grid's fraction (the carrack's 0, 0.125, 0.5) added.
    /// - Block positions in dimension 1 come as a BlockPos's Y (8192 + j, "plain") or its InternalY (32768 + 8192 + j):
    ///   sounds, particles and the animated block renderer take InternalY, most block renderers and particle code the plain Y.
    /// </summary>
    public static class ShipBlockMath
    {
        public const int OriginOffset = 8192, SubSize = 16384, ZMul = 4096, DimSizeY = 32768;
        /// <summary>Any plain Y from here up (and below DimSizeY) is a mini-dimension's: the normal world is never this high.</summary>
        public const int MinDimY = 4096;

        public static int BaseX(int sub) => sub % ZMul * SubSize + OriginOffset;
        public static int BaseZ(int sub) => sub / ZMul * SubSize + OriginOffset;
        public static int SubOf(int x, int z) => x < 0 || z < 0 ? -1 : z / SubSize * ZMul + x / SubSize;

        /// <summary>A cell's block in the dimension (plain Y).</summary>
        public static void DimPos(int sub, int i, int j, int k, out int x, out int y, out int z)
        {
            x = BaseX(sub) + i;
            y = OriginOffset + j;
            z = BaseZ(sub) + k;
        }

        /// <summary>A dimension block back to its cell (plain or internal Y).</summary>
        public static void CellOfDim(int sub, int x, int y, int z, out int i, out int j, out int k)
        {
            i = x - BaseX(sub);
            j = Plain(y) - OriginOffset;
            k = z - BaseZ(sub);
        }

        /// <summary>A Y in dimension 1 as a plain Y (an InternalY less the dimension's 32768).</summary>
        public static int Plain(int y) => y >= DimSizeY && y < 2 * DimSizeY ? y - DimSizeY : y;
        public static double Plain(double y) => y >= DimSizeY && y < 2 * DimSizeY ? y - DimSizeY : y;

        /// <summary>Is this Y (plain or internal) a mini-dimension's?</summary>
        public static bool IsDimY(double y)
        {
            double p = Plain(y);
            return p >= MinDimY && p < DimSizeY && !(y >= DimSizeY && y < DimSizeY + MinDimY);
        }

        /// <summary>
        /// The cell whose corner a piece's block origin (ship-local) is on, for a grid with this fraction (BlockGridDef.Frac):
        /// false if the origin isn't on the grid (an older design, a wall spot).
        /// </summary>
        public static bool CellOf(double fx, double fy, double fz, double x, double y, double z, out int i, out int j, out int k)
        {
            i = (int)Math.Round(x - fx);
            j = (int)Math.Round(y - fy);
            k = (int)Math.Round(z - fz);
            const double eps = 0.01;
            return Math.Abs(x - fx - i) < eps && Math.Abs(y - fy - j) < eps && Math.Abs(z - fz - k) < eps;
        }

        /// <summary>(0.3.16) Is a ship-local point in cell (i, j, k) of a grid with this fraction, or within margin of it?</summary>
        public static bool NearCell(double fx, double fy, double fz, int i, int j, int k, double x, double y, double z, double margin)
        {
            double x0 = i + fx, y0 = j + fy, z0 = k + fz;
            return x >= x0 - margin && x <= x0 + 1 + margin && y >= y0 - margin && y <= y0 + 1 + margin && z >= z0 - margin && z <= z0 + 1 + margin;
        }

        /// <summary>A point in a sub-dimension (plain or internal Y) as a ship-local point of the ship it's carried by.</summary>
        public static void ToLocal(int sub, double fx, double fy, double fz, double x, double y, double z, out double lx, out double ly, out double lz)
        {
            lx = x - BaseX(sub) + fx;
            ly = Plain(y) - OriginOffset + fy;
            lz = z - BaseZ(sub) + fz;
        }

        /// <summary>A ship-local point as a point in her sub-dimension (plain Y, or internal with internalY).</summary>
        public static void FromLocal(int sub, double fx, double fy, double fz, double lx, double ly, double lz, bool internalY, out double x, out double y, out double z)
        {
            x = lx - fx + BaseX(sub);
            y = ly - fy + OriginOffset + (internalY ? DimSizeY : 0);
            z = lz - fz + BaseZ(sub);
        }

        /// <summary>
        /// Does a block code's path ("anvil-iron", "bloomerybase-north") match the working-block list: its first part
        /// ("anvil") exactly, or an entry ending in * as a prefix of the path ("bloomery*").
        /// </summary>
        public static bool Matches(string[] codes, string path)
        {
            if (codes == null || string.IsNullOrEmpty(path)) return false;
            int dash = path.IndexOf('-');
            string first = dash < 0 ? path : path.Substring(0, dash);
            foreach (string c0 in codes)
            {
                if (string.IsNullOrWhiteSpace(c0)) continue;
                string c = c0.Trim();
                int colon = c.IndexOf(':');
                if (colon >= 0) c = c.Substring(colon + 1);
                if (c.EndsWith("*") ? path.StartsWith(c.Substring(0, c.Length - 1), StringComparison.Ordinal) : first == c) return true;
            }
            return false;
        }

        /// <summary>The ray (block-local, d not necessarily normalised) against a box (0..1 block-local): the entry t, or -1; the face it enters by (0..5: N E S W U D as BlockFacing).</summary>
        public static double RayBox(double ox, double oy, double oz, double dx, double dy, double dz, double x0, double y0, double z0, double x1, double y1, double z1, out int face)
        {
            face = -1;
            double tmin = double.NegativeInfinity, tmax = double.PositiveInfinity;
            int fmin = -1;
            if (!Slab(ox, dx, x0, x1, ref tmin, ref tmax, ref fmin, 3, 1)) return -1;   // -x: west(3), +x: east(1)
            if (!Slab(oy, dy, y0, y1, ref tmin, ref tmax, ref fmin, 5, 4)) return -1;   // -y: down(5), +y: up(4)
            if (!Slab(oz, dz, z0, z1, ref tmin, ref tmax, ref fmin, 0, 2)) return -1;   // -z: north(0), +z: south(2)
            if (tmax < 0 || tmin > tmax) return -1;
            if (tmin < 0) { face = fmin < 0 ? 4 : fmin; return 0; }
            face = fmin;
            return tmin;
        }

        static bool Slab(double o, double d, double a0, double a1, ref double tmin, ref double tmax, ref int fmin, int lowFace, int highFace)
        {
            if (Math.Abs(d) < 1e-12) return o >= a0 && o <= a1;
            double t0 = (a0 - o) / d, t1 = (a1 - o) / d;
            int f0 = lowFace, f1 = highFace;
            if (t0 > t1) { (t0, t1) = (t1, t0); (f0, f1) = (f1, f0); }
            if (t0 > tmin) { tmin = t0; fmin = f0; }
            if (t1 < tmax) tmax = t1;
            return true;
        }
    }
    /// <summary>A working block aboard: the piece it is, its cell on her block grid, the block (variant) in her dimension.</summary>
    public class ShipBlockCell
    {
        public int Piece, I, J, K;
        public string Code;
        /// <summary>A mesh turned by quarter turns (a block with no variant for the way it faces: its block entity's MeshAngle).</summary>
        public int Quarter;
    }

    /// <summary>
    /// (0.3.14) What of her is real blocks in her mini-dimension: its sub-dimension id, the working blocks (each a piece's
    /// stand-in: the piece keeps her bookkeeping: its places, boxes, collision, the item it gives back; it isn't drawn, the
    /// block is), and the invisible support blocks under them. Saved in WatchedAttributes "hansaBlocks" (clients follow
    /// it: which pieces are real, where, and which sub-dimension to draw); the blocks themselves (with their block
    /// entities' state) as a vanilla BlockSchematic in her server-only Attributes "hansaBlockSchem".
    /// </summary>
    public class ShipBlockRecord
    {
        public int Sub = -1;
        public readonly List<ShipBlockCell> Cells = new();
        public readonly List<int[]> Supports = new();

        public ShipBlockCell ByPiece(int id) => Cells.FirstOrDefault(c => c.Piece == id);
        public ShipBlockCell At(int i, int j, int k) => Cells.FirstOrDefault(c => c.I == i && c.J == j && c.K == k);
        public bool IsSupport(int i, int j, int k) => Supports.Any(s => s[0] == i && s[1] == j && s[2] == k);

        /// <summary>The cells in use (blocks and supports) as a box: min (inclusive) and max (exclusive) per axis; false if none.</summary>
        public bool Bounds(out int[] min, out int[] max)
        {
            min = new[] { int.MaxValue, int.MaxValue, int.MaxValue };
            max = new[] { int.MinValue, int.MinValue, int.MinValue };
            bool any = false;
            foreach (var c in Cells) { Grow(min, max, c.I, c.J, c.K); any = true; }
            foreach (var s in Supports) { Grow(min, max, s[0], s[1], s[2]); any = true; }
            return any;
        }

        static void Grow(int[] min, int[] max, int i, int j, int k)
        {
            min[0] = Math.Min(min[0], i); min[1] = Math.Min(min[1], j); min[2] = Math.Min(min[2], k);
            max[0] = Math.Max(max[0], i + 1); max[1] = Math.Max(max[1], j + 1); max[2] = Math.Max(max[2], k + 1);
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public void ToTree(ITreeAttribute t)
        {
            t.SetInt("sub", Sub);
            t.SetString("cells", string.Join(";", Cells.Select(c => string.Join("|", c.Piece.ToString(Inv), c.I.ToString(Inv), c.J.ToString(Inv), c.K.ToString(Inv), c.Code, c.Quarter.ToString(Inv)))));
            t.SetString("supports", string.Join(";", Supports.Select(s => string.Join("|", s[0].ToString(Inv), s[1].ToString(Inv), s[2].ToString(Inv)))));
        }

        public void FromTree(ITreeAttribute t)
        {
            Cells.Clear();
            Supports.Clear();
            Sub = t?.GetInt("sub", -1) ?? -1;
            if (t == null) return;
            foreach (string part in (t.GetString("cells") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = part.Split('|');
                if (s.Length < 5) continue;
                try
                {
                    Cells.Add(new ShipBlockCell
                    {
                        Piece = int.Parse(s[0], Inv), I = int.Parse(s[1], Inv), J = int.Parse(s[2], Inv), K = int.Parse(s[3], Inv), Code = s[4],
                        Quarter = s.Length > 5 ? int.Parse(s[5], Inv) : 0
                    });
                }
                catch (FormatException) { }
            }
            foreach (string part in (t.GetString("supports") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = part.Split('|');
                if (s.Length < 3) continue;
                try { Supports.Add(new[] { int.Parse(s[0], Inv), int.Parse(s[1], Inv), int.Parse(s[2], Inv) }); }
                catch (FormatException) { }
            }
        }
    }

}
