using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// A block of a wreck (hansa:wreck): a 4 × 4 × 4 grid of 0.25 cells of her timber, sheathing or iron, kept by its
    /// block entity (<see cref="BlockEntityWreck"/>), which also draws it. Its collision and selection are its cells as
    /// boxes. Breaking it gives part of its raw materials (<see cref="WreckDrops"/>), never the block; it can't be chiselled
    /// (it isn't a microblock, and its JSON says canChisel false). Wood to the tools (an axe is quickest), metal where it's
    /// mostly copper or iron.
    /// </summary>
    public class BlockWreck : Block
    {
        public const string BlockCode = "hansa:wreck";

        BlockEntityWreck Be(IBlockAccessor ba, BlockPos pos) => pos == null ? null : ba?.GetBlockEntity(pos) as BlockEntityWreck;

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) => Be(blockAccessor, pos)?.Boxes ?? base.GetCollisionBoxes(blockAccessor, pos);
        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) => Be(blockAccessor, pos)?.Boxes ?? base.GetSelectionBoxes(blockAccessor, pos);
        public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) => Be(blockAccessor, pos)?.Boxes ?? base.GetParticleCollisionBoxes(blockAccessor, pos);

        public override EnumBlockMaterial GetBlockMaterial(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
        {
            var c = Be(blockAccessor, pos)?.Cells;
            if (c == null || c.Empty) return base.GetBlockMaterial(blockAccessor, pos, stack);
            return WreckMat.Metal(c.Main) ? EnumBlockMaterial.Metal : EnumBlockMaterial.Wood;
        }

        /// <summary>Its raw materials: planks (oak, pine), now and then firewood, sticks, resin, nails; copper plates and bits; iron bits and nails.</summary>
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var cells = Be(world.BlockAccessor, pos)?.Cells;
            var list = new List<ItemStack>();
            foreach (var (code, n) in WreckDrops.Roll(cells, world.Rand, dropQuantityMultiplier))
            {
                var loc = new AssetLocation(code);
                CollectibleObject c = world.GetItem(loc);
                c ??= world.GetBlock(loc);
                if (c == null || n <= 0) continue;
                list.Add(new ItemStack(c, n));
            }
            return list.ToArray();
        }

        /// <summary>Middle-click: a plank (the block itself isn't to be had).</summary>
        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            var plank = world.GetItem(new AssetLocation("game:plank-oak"));
            return plank != null ? new ItemStack(plank) : null;
        }

        public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
        {
            var c = Be(world.BlockAccessor, pos)?.Cells;
            return c == null || c.Empty ? Lang.Get("hansa:block-wreck") : Lang.Get("hansa:wreck-name-" + WreckMat.Texture(c.Main));
        }
    }

    /// <summary>
    /// A wreck block's cells (<see cref="WreckCells"/>) and whose wreck it is (her entity id: a wreck built again after a
    /// reload lays the same blocks over its own). Set when placed (from the placing stack's attributes, also in worldgen),
    /// saved and synced with the block. Clients draw it (<see cref="OnTesselation"/>): each cell's faces that no cell is
    /// against, in this block or a wreck block beside it, merged into rectangles, with the material's vanilla texture laid
    /// as on a vanilla cube.
    /// </summary>
    public class BlockEntityWreck : BlockEntity
    {
        public const string ClassName = "hansa.Wreck";

        public WreckCells Cells { get; private set; }
        public long WreckId { get; private set; }
        public Cuboidf[] Boxes { get; private set; }
        static bool drawFailed;

        public void Set(WreckCells cells, long wreckId)
        {
            Cells = cells;
            WreckId = wreckId;
            Boxes = cells?.Cuboids();
        }

        /// <summary>A stack that places a wreck block with these cells (its attributes are read when it's placed).</summary>
        public static ItemStack Stack(Block block, WreckCells cells, long wreckId)
        {
            var s = new ItemStack(block);
            cells.ToTree(s.Attributes);
            s.Attributes.SetLong("wreck", wreckId);
            return s;
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            if (byItemStack?.Attributes != null && WreckCells.FromTree(byItemStack.Attributes) is WreckCells c) Set(c, byItemStack.Attributes.GetLong("wreck"));
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Set(WreckCells.FromTree(tree), tree.GetLong("wreck"));
            if (Api?.Side == EnumAppSide.Client) MarkDirty(true);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            Cells?.ToTree(tree);
            tree.SetLong("wreck", WreckId);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            var cells = Cells;
            // (no cells at all: one put down by hand, solid as a full block: drawn as one)
            if (cells == null || Api is not ICoreClientAPI capi || drawFailed) return false;
            if (cells.Empty) return true;
            try
            {
                // what of the wreck blocks beside it is against its faces (other blocks are the chunk's own business:
                // a face on the block's edge against a solid block is culled by the game)
                var beside = new WreckCells[6];
                for (int f = 0; f < 6; f++)
                {
                    var n = BlockFacing.ALLFACES[f].Normali;
                    beside[f] = (Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(n.X, n.Y, n.Z)) as BlockEntityWreck)?.Cells;
                }
                var block = Block;
                var mesh = WreckMesh.Build(cells, (f, cx, cy, cz) => beside[f]?.Has(cx, cy, cz) == true,
                    m => capi.BlockTextureAtlas.GetPosition(block, WreckMat.Texture(m), true) ?? capi.BlockTextureAtlas.UnknownTexturePosition);
                mesher.AddMeshData(mesh);
                return true;
            }
            catch (Exception e)
            {
                drawFailed = true;
                capi.Logger.Error("[hansa] Drawing wreck blocks failed (they're drawn as plain cubes from now on): {0}", e);
                return false;
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            var c = Cells;
            if (c == null || c.Empty) return;
            var parts = c.Mats.Select((m, i) => (m, n: System.Numerics.BitOperations.PopCount(c.Masks[i]))).OrderByDescending(p => p.n)
                .Select(p => Lang.Get(WreckMat.Lang(p.m)) + " " + p.n);
            dsc.AppendLine(Lang.Get("hansa:wreck-info", string.Join(", ", parts), c.Count));
            dsc.AppendLine(Lang.Get(WreckMat.Metal(c.Main) ? "hansa:wreck-info-metal" : "hansa:wreck-info-wood"));
        }
    }
}
