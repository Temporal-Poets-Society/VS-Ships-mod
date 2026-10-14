using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// (0.3.27, note 219: "how to combine side gate with a plank way to port pier or similar?") The brow: her gangplank,
    /// run out from an entry port to the quay. With her anchored and the entry port's gate open, boards in hand,
    /// right-click the gate: she runs her brow out from the port's sill to the nearest solid footing outside (a block with
    /// room over it, within <see cref="BrowReach"/> blocks and no steeper than about 40°). It's walked as a way off her
    /// and on again (a ladder of kind "brow": walk out along it onto the quay; from the quay walk up it onto her deck).
    /// The same click takes it in; it's taken in by itself when she weighs anchor, gets under way, the gate is shut or
    /// the quay under its end is gone. She carries it (no boards are used: they're only what says "the plank").
    /// Saved with her ("hansaBrows": gate code → its end, ship-local).
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>How far out from her side a brow reaches at most (blocks).</summary>
        public const double BrowReach = 5.0;
        /// <summary>How steep a brow may be (rise over run).</summary>
        public const double BrowSlope = 0.85;
        /// <summary>The brows run out: the entry port's gate code → the brow's end on the quay (ship-local feet).</summary>
        public readonly Dictionary<string, double[]> Brows = new();
        public int BrowVersion;
        float browCheckAccum;

        void InitBrows()
        {
            LoadBrows();
            if (Brows.Count > 0) SyncPieceRuntime();
            if (Api.Side == EnumAppSide.Client) WatchedAttributes.RegisterModifiedListener("hansaBrows", () => { LoadBrows(); SyncPieceRuntime(); });
        }

        void LoadBrows()
        {
            Brows.Clear();
            if (WatchedAttributes.GetTreeAttribute("hansaBrows") is ITreeAttribute t)
                foreach (var kv in t)
                    if (kv.Value is DoubleArrayAttribute da && da.value?.Length == 3 && Def?.Fixture(kv.Key) is FixtureDef g && g.Type == "gate") Brows[kv.Key] = da.value;
            BrowVersion++;
        }

        void SaveBrows()
        {
            var t = new TreeAttribute();
            foreach (var kv in Brows) t[kv.Key] = new DoubleArrayAttribute(kv.Value);
            WatchedAttributes["hansaBrows"] = t;
            WatchedAttributes.MarkPathDirty("hansaBrows");
            BrowVersion++;
            SyncPieceRuntime();
        }

        /// <summary>A brow's way (<see cref="BrowGeometry.Ladder"/>).</summary>
        public static LadderDef BrowLadder(FixtureDef gate, double[] end) => BrowGeometry.Ladder(gate, end);

        /// <summary>An entry port's sill, a spot on her deck inside it, and outward (<see cref="BrowGeometry.Port"/>).</summary>
        public static void BrowPort(FixtureDef gate, out double[] sill, out double[] inside, out int s) => BrowGeometry.Port(gate, out sill, out inside, out s);

        /// <summary>Server: where a brow from this gate would land (ship-local feet on the quay), or null: the nearest solid footing outward within reach.</summary>
        public double[] FindBrowLanding(FixtureDef gate)
        {
            if (gate?.Box == null || Api?.Side != EnumAppSide.Server) return null;
            BrowPort(gate, out var sill, out _, out int s);
            var ba = World.BlockAccessor;
            for (double d = 0.75; d <= BrowReach + 1e-6; d += 0.25)
            {
                var p = new[] { sill[0], sill[1], sill[2] + s * d };
                LocalToWorld(p[0], p[1], p[2], out double wx, out double wy, out double wz);
                double best = double.NaN;
                for (int k = 2; k >= -4; k--)
                {
                    var pos = new BlockPos((int)Math.Floor(wx), (int)Math.Floor(wy) + k, (int)Math.Floor(wz), Pos.Dimension);
                    double top = SolidTop(ba, pos);
                    if (double.IsNaN(top)) continue;
                    // room over it for a man
                    if (!double.IsNaN(SolidTop(ba, pos.UpCopy(1))) || !double.IsNaN(SolidTop(ba, pos.UpCopy(2)))) continue;
                    best = top;
                    break;
                }
                if (double.IsNaN(best)) continue;
                var local = WorldToLocal(wx, best, wz);
                double rise = Math.Abs(local.Y - sill[1]);
                if (rise > BrowSlope * d + 0.3 || rise > 3.0) continue;
                return new[] { local.X, local.Y, local.Z };
            }
            return null;
        }

        /// <summary>The top of a block one can stand on (its collision boxes' top, world y), or NaN: air, a liquid, a plant.</summary>
        static double SolidTop(IBlockAccessor ba, BlockPos pos)
        {
            var block = ba.GetBlock(pos, BlockLayersAccess.Solid);
            if (block == null || block.Id == 0 || block.IsLiquid()) return double.NaN;
            var boxes = block.GetCollisionBoxes(ba, pos);
            if (boxes == null || boxes.Length == 0) return double.NaN;
            double top = boxes.Max(b => b.Y2);
            return top < 0.4 ? double.NaN : pos.Y + top;
        }

        /// <summary>Server: boards in hand at an open entry port's gate: run the brow out, or take it in. An error lang code, or null.</summary>
        public string ToggleBrow(IServerPlayer player, FixtureDef gate)
        {
            if (gate?.Type != "gate") return "hansa:brow-no-port";
            if (Brows.ContainsKey(gate.Code))
            {
                TakeInBrow(gate.Code);
                return null;
            }
            if (GoneDown) return "hansa:gone-down";
            if (!Anchored || Math.Abs(Speed) > 0.3) return "hansa:brow-not-anchored";
            if (!OpenFittings.Contains(gate.Code)) return "hansa:brow-gate-shut";
            var end = FindBrowLanding(gate);
            if (end == null) return "hansa:brow-no-landing";
            Brows[gate.Code] = end;
            SaveBrows();
            BrowSound(gate);
            if (player != null) World.Logger.Audit("{0} ran out the brow of {1} #{2} at {3}.", player.PlayerName, GetName(), EntityId, gate.Code);
            return null;
        }

        /// <summary>Server: a brow taken in (by hand or by itself).</summary>
        public void TakeInBrow(string gate)
        {
            if (!Brows.Remove(gate)) return;
            SaveBrows();
            if (Def?.Fixture(gate) is FixtureDef g) BrowSound(g);
        }

        void BrowSound(FixtureDef gate)
        {
            LocalToWorld(gate.Pos[0], gate.Pos[1] + 0.5, gate.Pos[2], out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/block/planks"), x, y, z, null, true, 16);
        }

        /// <summary>Server, every second: a brow comes in when she's no longer anchored and still, its gate is shut, or the quay under its end is gone.</summary>
        void BrowTick(float dt)
        {
            if (Brows.Count == 0) return;
            browCheckAccum += dt;
            if (browCheckAccum < 1) return;
            browCheckAccum = 0;
            foreach (var kv in Brows.ToList())
            {
                bool keep = Anchored && Math.Abs(Speed) <= 0.3 && !GoneDown && OpenFittings.Contains(kv.Key);
                if (keep)
                {
                    var e = kv.Value;
                    LocalToWorld(e[0], e[1] - 0.05, e[2], out double wx, out double wy, out double wz);
                    var pos = new BlockPos((int)Math.Floor(wx), (int)Math.Floor(wy), (int)Math.Floor(wz), Pos.Dimension);
                    double top = SolidTop(World.BlockAccessor, pos);
                    keep = !double.IsNaN(top) && Math.Abs(top - (wy + 0.05)) < 0.6;
                }
                if (!keep) TakeInBrow(kv.Key);
            }
        }

        /// <summary>Boards in this player's hand (the brow's click).</summary>
        static bool ShipSlotsBoards(IServerPlayer player) => IsBoards(player?.InventoryManager?.ActiveHotbarSlot?.Itemstack);

        /// <summary>Her brows as ladders (her ladders' list: <see cref="Ladders"/>).</summary>
        IEnumerable<LadderDef> BrowLadders()
        {
            foreach (var kv in Brows)
                if (Def?.Fixture(kv.Key) is FixtureDef g && BrowLadder(g, kv.Value) is LadderDef l) yield return l;
        }
    }
}
