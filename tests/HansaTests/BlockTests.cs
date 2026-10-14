using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;
using Vintagestory.API.Datastructures;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// Working blocks aboard (0.3.14): the arithmetic between her block grid and her mini-dimension (ShipBlockMath),
        /// the record saved with her, and that every floor place on the carrack is exactly one cell of her grid.
        /// </summary>
        static void WorkingBlocks(ShipDefinition def)
        {
            Console.WriteLine("working blocks: grid cells, dimension positions, the record");
            bool round = true;
            foreach (int sub in new[] { 1, 2, 4095, 4096, 4097, 12345, 16777000 })
                foreach (var (i, j, k) in new[] { (0, 0, 0), (-20, -4, 3), (19, 9, -5), (100, 0, 100) })
                {
                    ShipBlockMath.DimPos(sub, i, j, k, out int x, out int y, out int z);
                    ShipBlockMath.CellOfDim(sub, x, y, z, out int i2, out int j2, out int k2);
                    ShipBlockMath.CellOfDim(sub, x, y + ShipBlockMath.DimSizeY, z, out int i3, out int j3, out int k3);
                    round &= ShipBlockMath.SubOf(x, z) == sub && (i2, j2, k2) == (i, j, k) && (i3, j3, k3) == (i, j, k) &&
                             ShipBlockMath.IsDimY(y) && ShipBlockMath.IsDimY(y + ShipBlockMath.DimSizeY);
                    ShipBlockMath.ToLocal(sub, 0, 0.125, 0.5, x + 0.25, y + 0.5, z + 0.75, out double lx, out double ly, out double lz);
                    ShipBlockMath.FromLocal(sub, 0, 0.125, 0.5, lx, ly, lz, true, out double bx, out double by, out double bz);
                    round &= Math.Abs(lx - (i + 0.25)) < 1e-9 && Math.Abs(ly - (j + 0.625)) < 1e-9 && Math.Abs(lz - (k + 1.25)) < 1e-9 &&
                             Math.Abs(bx - (x + 0.25)) < 1e-9 && Math.Abs(by - (y + 0.5 + ShipBlockMath.DimSizeY)) < 1e-9 && Math.Abs(bz - (z + 0.75)) < 1e-9;
                }
            Check(round, "a cell's block in her dimension and back, by plain and internal Y, any sub-dimension; ship-local points the same");
            Check(!ShipBlockMath.IsDimY(100) && !ShipBlockMath.IsDimY(2000) && !ShipBlockMath.IsDimY(32768 + 200) && !ShipBlockMath.IsDimY(65536 + 100) && ShipBlockMath.IsDimY(8192),
                "world heights (and the alt world's) are never taken for a dimension's");

            string[] codes = { "forge", "bellows", "anvil", "quern", "barrel", "crock", "bloomery*" };
            Check(ShipBlockMath.Matches(codes, "forge") && ShipBlockMath.Matches(codes, "anvil-iron") && ShipBlockMath.Matches(codes, "bellows-small-east") &&
                  ShipBlockMath.Matches(codes, "bloomerybase-north") && ShipBlockMath.Matches(codes, "bloomerychimney") && ShipBlockMath.Matches(codes, "quern-granite") &&
                  !ShipBlockMath.Matches(codes, "firepit-construct1") && !ShipBlockMath.Matches(codes, "chest-east") && !ShipBlockMath.Matches(codes, "anvilpart-iron") &&
                  !ShipBlockMath.Matches(codes, "forgedblade") && ShipBlockMath.Matches(new[] { "game:axle*" }, "axle-ud") && !ShipBlockMath.Matches(null, "forge"),
                "the working-block list matches by a code's first part (or a prefix with *): not the firepit, chests, anvil parts");

            // a ray into a block: the face it enters by (BlockFacing's order N E S W U D), from inside the face 'up'
            double t1 = ShipBlockMath.RayBox(-1, 0.5, 0.5, 1, 0, 0, 0, 0, 0, 1, 1, 1, out int f1);
            double t2 = ShipBlockMath.RayBox(0.5, 3, 0.5, 0, -1, 0, 0, 0, 0, 1, 0.5, 1, out int f2);
            double t3 = ShipBlockMath.RayBox(0.5, 0.5, 2, 0, 0, -1, 0, 0, 0, 1, 1, 1, out int f3);
            double t4 = ShipBlockMath.RayBox(0.5, 0.5, 2, 1, 0, 0, 0, 0, 0, 1, 1, 1, out _);
            Check(Math.Abs(t1 - 1) < 1e-9 && f1 == 3 && Math.Abs(t2 - 2.5) < 1e-9 && f2 == 4 && Math.Abs(t3 - 1) < 1e-9 && f3 == 2 && t4 < 0,
                $"a ray meets a block's box on the face it comes in by (west {f1}, up {f2}, south {f3}; a miss {t4})");

            // her record, saved and read back
            var rec = new ShipBlockRecord { Sub = 7 };
            rec.Cells.Add(new ShipBlockCell { Piece = 3, I = -9, J = -4, K = -1, Code = "game:forge" });
            rec.Cells.Add(new ShipBlockCell { Piece = 5, I = -10, J = -4, K = -1, Code = "game:bellows-small-east", Quarter = 3 });
            rec.Supports.Add(new[] { -9, -5, -1 });
            var tree = new TreeAttribute();
            rec.ToTree(tree);
            var back = new ShipBlockRecord();
            back.FromTree(tree);
            Check(back.Sub == 7 && back.Cells.Count == 2 && back.ByPiece(5)?.Code == "game:bellows-small-east" && back.ByPiece(5).Quarter == 3 &&
                  back.At(-9, -4, -1)?.Piece == 3 && back.IsSupport(-9, -5, -1) && back.Bounds(out var mn, out var mx) &&
                  mn.SequenceEqual(new[] { -10, -5, -1 }) && mx.SequenceEqual(new[] { -8, -3, 0 }),
                "her working-block record is saved and read back (cells, supports, the box they take)");

            // every floor place on her grid is exactly one cell (what a working block takes)
            var g = def.BlockGrid;
            var layout = def.Mounts ?? ShipMounts.For(def);
            if (g?.Origin == null || layout == null) { Check(false, "the carrack has a block grid and mount points"); return; }
            double fx = g.Frac(0), fy = g.Frac(1), fz = g.Frac(2);
            var cells = new HashSet<(int, int, int)>();
            int floors = 0, off = 0, dup = 0;
            foreach (var m in layout.All.Where(m => m.Kind == MountKind.Floor))
            {
                floors++;
                if (!ShipBlockMath.CellOf(fx, fy, fz, m.X, m.Y, m.Z, out int i, out int j, out int k)) { off++; continue; }
                if (!cells.Add((i, j, k))) dup++;
            }
            Check(floors > 100 && off == 0 && dup == 0, $"every one of her {floors} floor places is one cell of her grid ({off} off it, {dup} sharing one)");
        }
    }
}
