using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// (24 October 2026, caravel notes 66-82 on 0.1.0-dev.8) On the ship under test, with the game's own blocks: an oven on a
    /// planks block put on her deck (note 74), a trunk at her hold's after end where it's one cell wide (note 79), a bed on
    /// either side of her main deck by the mizzen (note 70), the mainmast's lantern taken down and hung back, and one hung
    /// under her steerage's ceiling (note 66); no panel places (note 82); a shelf in her hold over a chest, a vessel on it
    /// (note 83). Each
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
                    // (and under a deck: the steerage's ceiling's hook)
                    var ceiling = l.Get("h:c:main:-11:-1");
                    var underDeck = stack == null || ceiling == null ? null : PiecePlanner.Plan(l, ceiling, new VanillaPieceBlock(sapi.World, stack), 1);
                    string hung = underDeck == null ? "no ceiling hook or stack" : ship.PlacePiece(underDeck, stack, true, out var hungP);
                    string off = hung == null ? ship.RemovePiece(ship.Pieces.At(ceiling.Id), null) : "not hung";
                    Check(hung == null && off == null, $"note 66: a lantern hung under the steerage's ceiling ({hung ?? "ok"}) and taken down ({off ?? "ok"})");
                }
                // ---- note 83: a shelf a block up in her hold, aimed at from a step away with a plank slab (building mode), over a
                // chest; a vessel on it; not taken up from under the vessel; then each taken up
                {
                    var cell = l.FloorAt("hold", -3, -2);
                    var slabStack = new ItemStack(B("plankslab-oak-down-free"));
                    var eye = new Vintagestory.API.MathTools.Vec3d(cell.X + 1.5, cell.Y + 1.62, cell.Z + 0.5);
                    var aim = new Vintagestory.API.MathTools.Vec3d(-1.0, -1.62, 0);
                    aim.Mul(1 / aim.Length());
                    MountPoint shelfAt = null;
                    if (AimPlace.GridHit(ship.Grid, eye, aim, 6, out double th, out var nh))
                        shelfAt = AimPlace.Candidates(l, ship.Pieces, eye.AddCopy(aim.X * th, aim.Y * th, aim.Z * th), nh, null,
                            x => x.Kind == MountKind.Horizontal && !ship.Pieces.Taken(x.Id) && ship.MountOpen(x)).FirstOrDefault();
                    string chestPut = Put(cell, B("chest-east"), 0, out var chestP);
                    var shelfPlan = shelfAt == null ? null : PiecePlanner.Plan(l, shelfAt, new VanillaPieceBlock(sapi.World, slabStack, true), 0);
                    string shelfPut = shelfPlan == null ? "nothing offered" : ship.PlacePiece(shelfPlan, slabStack, true, out var shelfP);
                    var vesselBlock = sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Code.Path.StartsWith("storagevessel-") && bl.Code.Path.EndsWith("-fired"));
                    string vesselPut = Put(l.FloorAt("hold", -3, -2, 1), vesselBlock, 0, out var vesselP);
                    var shelfPiece = shelfAt == null ? null : ship.Pieces.At(shelfAt.Id);
                    string fromUnder = shelfPiece == null ? "no shelf" : ship.RemovePiece(shelfPiece, null);
                    Check(shelfAt?.Id == "hb:hold:-3:-2:1" && chestPut == null && shelfPut == null && vesselPut == null && fromUnder == "hansa:piece-under",
                        $"note 83: a shelf a block up in her hold, aimed at from a step away ({shelfAt?.Id ?? "nothing"}), over a chest ({chestPut ?? "ok"}, {shelfPut ?? "ok"}), a vessel on it ({vesselPut ?? "ok"}), not taken up from under it ({fromUnder})");
                    string outs = string.Join(", ", new[] { vesselP, shelfPiece, chestP }.Select(p => p == null ? "none" : ship.RemovePiece(p, null) ?? "ok"));
                    Check(outs == "ok, ok, ok", $"note 83: the vessel, the shelf and the chest taken up ({outs})");
                    put.Clear();
                }
                // ---- note 82: no panel places on her (nothing put into a cell her hull or ribs are in)
                Check(!ship.Def.PanelPlaces && !l.Panels.Any(), $"note 82: no panel places on the caravel ({l.Panels.Count()})");
            }
            finally
            {
                foreach (var p in put) if (ship.Pieces.Get(p.Id) != null) ship.RemovePiece(p, null);
            }
        }
    }
}
