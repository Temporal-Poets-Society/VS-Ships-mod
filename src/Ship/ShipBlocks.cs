using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// (0.3.14) Working blocks aboard, her side (the dimension itself, its saving, moving and sending: ShipBlocksSystem).
    /// The hooks ShipDecor calls: a piece put (made real if it's a working block on a floor place on her grid), a piece
    /// taken up (its block out of her dimension), her pieces as wreck loot or dropped (what's in the blocks too).
    /// </summary>
    public partial class EntityShip
    {
        public const string BlocksKey = "hansaBlocks";
        public const string BlocksSchematicKey = "hansaBlockSchem";

        ShipBlockRecord blockRecord;

        /// <summary>Her working blocks (read from WatchedAttributes, cached until <see cref="ReloadBlockRecord"/>).</summary>
        public ShipBlockRecord BlockRecord
        {
            get
            {
                if (blockRecord == null)
                {
                    var r = new ShipBlockRecord();
                    r.FromTree(WatchedAttributes.GetTreeAttribute(BlocksKey));
                    blockRecord = r;
                }
                return blockRecord;
            }
        }

        public void ReloadBlockRecord() => blockRecord = null;

        public void SaveBlockRecord()
        {
            var t = new TreeAttribute();
            BlockRecord.ToTree(t);
            WatchedAttributes[BlocksKey] = t;
            WatchedAttributes.MarkPathDirty(BlocksKey);
        }

        /// <summary>Is this piece a working block (drawn and used as the real block in her dimension)?</summary>
        public bool IsWorkingPiece(int pieceId) => BlockRecord.ByPiece(pieceId) != null;

        /// <summary>Her block grid's fraction (where block borders fall within a block, ship-local): null for a design without one.</summary>
        public double[] GridFrac => Def?.BlockGrid?.Origin?.Length >= 3 ? new[] { Def.BlockGrid.Frac(0), Def.BlockGrid.Frac(1), Def.BlockGrid.Frac(2) } : null;

        /// <summary>For ShipBlocksSystem: her pieces changed (their storages, seats, collision, sync).</summary>
        internal void BlocksChangedPieces() => Changed();

        ShipBlocksSystem BlocksSys => Api?.ModLoader.GetModSystem<ShipBlocksSystem>();

        /// <summary>ShipDecor: a piece was just put: a working block becomes real (server).</summary>
        void OnPiecePlaced(ShipPiece placed, ItemStack stack)
        {
            if (Api?.Side != EnumAppSide.Server || placed == null) return;
            BlocksSys?.TryMakeReal(this, placed, stack, true);
        }

        /// <summary>ShipDecor: a piece is about to be taken away: its real block goes (refused while something's in it). Returns an error lang code or null.</summary>
        string OnPieceRemoving(ShipPiece p, IServerPlayer player) =>
            Api?.Side == EnumAppSide.Server && IsWorkingPiece(p.Id) ? BlocksSys?.RemovingPiece(this, p, player) : null;

        /// <summary>ShipDecor: her pieces become wreck loot: what's in her working blocks too (take: out of her).</summary>
        void RealBlockLoot(List<ItemStack> into, bool take)
        {
            if (Api?.Side == EnumAppSide.Server && BlockRecord.Cells.Count > 0) BlocksSys?.Loot(this, into, take);
        }

        /// <summary>ShipDecor: removed for good: what's in her working blocks dropped where she is.</summary>
        void DropRealBlocks()
        {
            if (Api?.Side == EnumAppSide.Server && BlockRecord.Cells.Count > 0) BlocksSys?.DropAll(this);
        }
    }
}
