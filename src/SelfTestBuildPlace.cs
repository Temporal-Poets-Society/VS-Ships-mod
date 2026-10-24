using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// (24 October 2026, caravel notes 66-79 on 0.1.0-dev.8) On the ship under test, with the game's own blocks: an oven on a
    /// planks block put on her deck (note 74), a trunk at her hold's after end where it's one cell wide (note 79), a bed on
    /// either side of her main deck by the mizzen (note 70), the mainmast's lantern taken down and hung back (note 66). Each
    /// put as the server puts a player's (laid another way where vanilla's doesn't fit) and taken up again.
    /// </summary>
    public partial class SelfTest
    {
        void BuildPlaceAboard(EntityShip ship)
        {
            var l = ship.Mounts;
            if (ship.Def?.Code != "caravel" || l == null) { Log($"24 October 2026: the caravel's building notes are checked on the caravel ({ship.Def?.Code} under test)"); return; }
            Block B(string c) => sapi.World.GetBlock(new AssetLocation("game", c)) is Block b && b.Id != 0 ? b : null;
            var put = new List<ShipPiece>();
            string Put(MountPoint m, Block b, int dir, out ShipPiece placed)
            {
                placed = null;
                if (m == null || b == null) return "no place or block";
                var stack = new ItemStack(b);
                var plan = PiecePlanner.Plan(l, m, new VanillaPieceBlock(sapi.World, stack), dir, p => ship.PlaceProblem(p, false) == null);
                string err = ship.PlacePiece(plan, stack, false, out placed);
                if (placed != null) put.Add(placed);
                return err;
            }
            try
            {
                // ---- note 74: an oven on top of planks put on her deck
                var floor = l.FloorAt("main", -11, -1);
                string under = Put(floor, B("planks-oak-ud"), 1, out var planks);
                string onTop = Put(l.Above(floor), B("clayoven-north"), 1, out var ovenP);
                string planksOut = planks == null ? "none" : ship.RemovePiece(planks, null);
                Check(under == null && onTop == null && ovenP?.Role == PieceRole.Oven && planksOut == "hansa:piece-under",
                    $"note 74: an oven on top of a planks block on her deck ({under ?? "planks put"}, {onTop ?? "oven put"}); the planks not taken from under it ({planksOut})");
                string ovenOut = ovenP == null ? "none" : ship.RemovePiece(ovenP, null);
                planksOut = planks == null ? "none" : ship.RemovePiece(planks, null);
                Check(ovenOut == null && planksOut == null, $"note 74: the oven taken up, then the planks ({ovenOut ?? "ok"}, {planksOut ?? "ok"})");
                put.Clear();

                // ---- note 79: a trunk where two vessels go, her hold's after end, put looking aft
                var trunkBlock = sapi.World.Blocks.FirstOrDefault(bl => bl is Vintagestory.GameContent.BlockGenericTypedContainerTrunk);
                var aft = l.FloorAt("hold", -8, -1);
                string trunk = Put(aft, trunkBlock, 0, out var trunkP);
                Check(trunk == null && trunkP?.Mounts.Length == 2 && trunkP.Mounts.Contains(aft.Id),
                    $"note 79: a trunk at her hold's after end, put looking aft ({trunk ?? string.Join(" + ", trunkP.Mounts)})");
                string trunkOut = trunkP == null ? "none" : ship.RemovePiece(trunkP, null);
                Check(trunkOut == null, $"note 79: the trunk taken up ({trunkOut ?? "ok"})");
                put.Clear();

                // ---- note 70: a bed on either side of her main deck by the mizzen, put from her middle looking out
                var beds = new List<string>();
                foreach (var (k, dir) in new[] { (-2, 1), (0, 3) })
                {
                    string bed = Put(l.FloorAt("main", -9, k), B("bed-wood-head-north"), dir, out var bedP);
                    beds.Add($"{(k < 0 ? "port" : "starboard")} {bed ?? string.Join(" + ", bedP.Mounts)}");
                    if (bedP != null) beds[^1] += ship.RemovePiece(bedP, null) == null ? ", taken up" : ", NOT taken up";
                }
                Check(beds.All(b => b.EndsWith(", taken up")), $"note 70: a bed by the mizzen on either side, put looking out to her side ({string.Join("; ", beds)})");
                put.Clear();

                // ---- note 66: the mainmast's lantern taken down and hung back
                var hook = l.Get("h:lantern-mainmast");
                var lantern = hook == null ? null : ship.Pieces.At(hook.Id);
                if (lantern == null) Log("note 66: no lantern on the mainmast's hook to take down");
                else
                {
                    var stack = lantern.Stack?.Clone();
                    string down = ship.RemovePiece(lantern, null);
                    var plan = stack == null ? null : PiecePlanner.Plan(l, hook, new VanillaPieceBlock(sapi.World, stack), 1);
                    string back = plan == null ? "no stack" : ship.PlacePiece(plan, stack, true, out _);
                    Check(down == null && back == null && ship.Pieces.At(hook.Id) != null, $"note 66: the mainmast's lantern taken down ({down ?? "ok"}) and hung back ({back ?? "ok"})");
                }
            }
            finally
            {
                foreach (var p in put) if (ship.Pieces.Get(p.Id) != null) ship.RemovePiece(p, null);
            }
        }
    }
}
