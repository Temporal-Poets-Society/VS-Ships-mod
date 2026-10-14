using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// (0.3.14) The self-test's working-blocks step: a carrack with a forge, bellows, anvil and bloomery (and its chimney) put
    /// on her floor places the way placing puts pieces (EntityShip.PlacePiece): real blocks in her dimension with supports
    /// under them; the forge lit and heating an ingot, the bellows blowing it, the hot ingot smithed on the anvil, the
    /// bloomery burning; climate, drops and positions taken to her; her moving carries the dimension; saved, let go of and
    /// made again from what was saved (as after a restart) with the block entities as they were; a piece taken up takes its
    /// block. And (information only) whether a mechanical-power network forms and turns in her dimension.
    /// </summary>
    public partial class SelfTest
    {
        void WorkingBlocksWorks(HansaMod mod, ShipDefinition def, BlockPos spawn, double wy, Action then)
        {
            var bsys = sapi.ModLoader.GetModSystem<ShipBlocksSystem>();
            if (!mod.Config.WorkingBlocks)
            {
                Log("working blocks: skipped (config WorkingBlocks is off)");
                then();
                return;
            }
            Block B(string c) => sapi.World.GetBlock(new AssetLocation("game", c));
            Item I(string c) => sapi.World.GetItem(new AssetLocation("game", c));
            var forge = B("forge");
            var bellows = B("bellows-small-east");
            var anvil = B("anvil-iron");
            var bloomery = B("bloomerybase-north");
            var chimney = B("bloomerychimney");
            var support = sapi.World.GetBlock(ShipBlocksSystem.SupportCode);
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", "carrack"));
            Check(bsys != null && forge != null && bellows != null && anvil != null && bloomery != null && chimney != null && support != null && type != null,
                $"working blocks: the system, vanilla's forge, bellows, anvil, bloomery and chimney, and the support block ({forge?.Code}, {bellows?.Code}, {anvil?.Code}, {bloomery?.Code}, {chimney?.Code}, {support?.Code})");
            Check(mod.Config.WorkingBlocks && ShipBlockMath.Matches(mod.Config.WorkingBlockCodes, "anvil-iron") && ShipBlockMath.Matches(mod.Config.WorkingBlockCodes, "bloomerychimney") &&
                  !ShipBlockMath.Matches(mod.Config.WorkingBlockCodes, "firepit-construct1") && !ShipBlockMath.Matches(mod.Config.WorkingBlockCodes, "chest-east"),
                "config: working blocks on; the anvil and the bloomery's chimney are on the list, the firepit and chests aren't");
            Check(BlockPatches.Applied.Contains("range") && BlockPatches.Applied.Any(a => a.StartsWith("climate:")) && BlockPatches.Applied.Contains("drops") &&
                  BlockPatches.Applied.Contains("sounds-server") && BlockPatches.Applied.Contains("particles-server"),
                "working blocks' server patches are on: " + string.Join(", ", BlockPatches.Applied));
            if (bsys == null || forge == null || bellows == null || anvil == null || type == null || support == null) { then(); return; }

            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X - 25.5, wy, spawn.Z + 0.5);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            var l = ship.Mounts;
            var frac = ship.GridFrac;
            Check(l != null && frac != null, $"the carrack has mount points and a block grid ({frac?[0]}, {frac?[1]}, {frac?[2]})");
            if (l == null || frac == null) { then(); return; }

            // a forge with the bellows to its west (blowing east into it) and the anvil to its east, and a bloomery two places
            // on (its chimney on the place above it); all on one deck
            MountPoint mf = null, mb = null, ma = null, mbl = null, mch = null;
            PiecePlan pf = null, pb = null, pa = null, pbl = null;
            foreach (var m in l.Floors.OrderBy(m => m.Y).ThenBy(m => m.X))
            {
                var b = l.FloorAt(m.Deck, m.I - 1, m.K);
                var a = l.FloorAt(m.Deck, m.I + 1, m.K);
                var bl = l.FloorAt(m.Deck, m.I + 3, m.K);
                var ch = bl == null ? null : l.Above(bl);
                if (b == null || a == null || bl == null || ch == null) continue;
                var f1 = PiecePlanner.Plan(l, m, new VanillaPieceBlock(sapi.World, new ItemStack(forge)), 1);
                var b1 = PiecePlanner.Plan(l, b, new VanillaPieceBlock(sapi.World, new ItemStack(B("bellows-small-north"))), 2);
                var a1 = PiecePlanner.Plan(l, a, new VanillaPieceBlock(sapi.World, new ItemStack(anvil)), 3);
                var bl1 = PiecePlanner.Plan(l, bl, new VanillaPieceBlock(sapi.World, new ItemStack(bloomery)), 3);
                if (ship.PlaceProblem(f1, false) != null || ship.PlaceProblem(b1, false) != null || ship.PlaceProblem(a1, false) != null || ship.PlaceProblem(bl1, false) != null) continue;
                (mf, mb, ma, mbl, mch, pf, pb, pa, pbl) = (m, b, a, bl, ch, f1, b1, a1, bl1);
                break;
            }
            Check(mf != null, $"four floor places in a row on one deck with room over the fourth (forge at {mf?.Id}, bellows {mb?.Id}, anvil {ma?.Id}, bloomery {mbl?.Id}, its chimney {mch?.Id})");
            if (mf == null) { then(); return; }

            string ef = ship.PlacePiece(pf, new ItemStack(forge), false, out var forgePiece);
            string eb = ship.PlacePiece(pb, new ItemStack(B("bellows-small-north")), false, out var bellowsPiece);
            string ea = ship.PlacePiece(pa, new ItemStack(anvil), false, out var anvilPiece);
            string ebl = ship.PlacePiece(pbl, new ItemStack(bloomery), false, out var bloomPiece);
            var pch = PiecePlanner.Plan(l, mch, new VanillaPieceBlock(sapi.World, new ItemStack(chimney)), 3);
            string ech = ship.PlacePiece(pch, new ItemStack(chimney), false, out var chimPiece);
            var rec = ship.BlockRecord;
            var sd = bsys.DimOf(ship);
            Check(ef == null && eb == null && ea == null && ebl == null && ech == null && rec.Cells.Count == 5 && sd != null && rec.Sub == sd.Sub && rec.Sub > 0,
                $"placing a forge, bellows, anvil, bloomery and chimney aboard puts them in her dimension ({ef}, {eb}, {ea}, {ebl}, {ech}; {rec.Cells.Count} cells, sub {rec.Sub})");
            if (sd == null || forgePiece == null || anvilPiece == null || bellowsPiece == null) { then(); return; }
            var ba = sapi.World.BlockAccessor;
            BlockPos At(ShipPiece p)
            {
                var c = rec.ByPiece(p.Id);
                if (c == null) return null;
                ShipBlockMath.DimPos(rec.Sub, c.I, c.J, c.K, out int x, out int y, out int z);
                return new BlockPos(x, y, z, Dimensions.MiniDimensions);
            }
            var fp = At(forgePiece);
            var bp = At(bellowsPiece);
            var ap = At(anvilPiece);
            var blp = bloomPiece != null ? At(bloomPiece) : null;
            var chp = chimPiece != null ? At(chimPiece) : null;
            Check(fp != null && ba.GetBlock(fp).Code.Path == "forge" && ba.GetBlock(bp).Code.Path == "bellows-small-east" && ba.GetBlock(ap).Code.Path == "anvil-iron" &&
                  ba.GetBlockEntity(fp) is BlockEntityForge && ba.GetBlockEntity(ap) is BlockEntityAnvil && bp.Copy().Add(BlockFacing.EAST).Equals(fp),
                $"the real blocks are there, with their block entities; the bellows faces the forge ({ba.GetBlock(fp)?.Code}, {ba.GetBlock(bp)?.Code}, {ba.GetBlock(ap)?.Code})");
            Check(blp != null && chp != null && chp.Equals(blp.UpCopy()) && ba.GetBlock(chp).Code.Path.Contains("bloomerychimney") && ba.GetBlockEntity(blp) is BlockEntityBloomery,
                $"the chimney stands on the bloomery in her dimension ({ba.GetBlock(blp ?? fp)?.Code} / {(chp != null ? ba.GetBlock(chp)?.Code : null)})");
            Check(ba.GetBlock(fp.DownCopy()).Id == support.Id && ba.GetBlock(ap.DownCopy()).Id == support.Id && blp != null && ba.GetBlock(blp.DownCopy()).Id == support.Id &&
                  rec.Supports.Count == 4 && !rec.IsSupport(rec.ByPiece(chimPiece.Id).I, rec.ByPiece(chimPiece.Id).J - 1, rec.ByPiece(chimPiece.Id).K),
                $"invisible supports under each (not under the chimney: the bloomery holds it): {rec.Supports.Count}");
            Check(forgePiece.Parts.Count == 0 && forgePiece.Solids.Count > 0 && forgePiece.Role == PieceRole.Deco && ship.IsWorkingPiece(forgePiece.Id) &&
                  ship.Grid.Collides(mf.X + 0.3, mf.Y + 0.3, mf.Z + 0.3, mf.X + 0.7, mf.Y + 0.7, mf.Z + 0.7),
                "the forge's piece isn't drawn (the block is) and stays solid for walking");
            Check(ShipBlocksSystem.IsShipPos(fp, false) && ShipBlocksSystem.ShipForSub(rec.Sub, false) == ship, "her dimension is registered for the patches");
            Delivery(bsys, ship, sd, mod, l, B("anvil-copper") ?? anvil);

            // positions taken to her
            var c0 = rec.ByPiece(forgePiece.Id);
            ShipBlocksSystem.ToWorld(fp.X + 0.5, fp.Y + 0.5, fp.Z + 0.5, false, out double wx, out double wy2, out double wz);
            ship.LocalToWorld(c0.I + frac[0] + 0.5, c0.J + frac[1] + 0.5, c0.K + frac[2] + 0.5, out double ex, out double ey, out double ez);
            Check(Math.Abs(wx - ex) + Math.Abs(wy2 - ey) + Math.Abs(wz - ez) < 1e-6 && Math.Abs(mf.X + 0.5 - (c0.I + frac[0] + 0.5)) < 0.01 && Math.Abs(mf.Y - (c0.J + frac[1])) < 0.01,
                $"a block in her dimension is where its floor place is aboard (world {wx:0.00}, {wy2:0.00}, {wz:0.00})");
            ShipBlocksSystem.ToWorld(fp.X + 0.5, fp.InternalY + 0.5, fp.Z + 0.5, false, out double ix, out double iy, out double iz);
            Check(Math.Abs(ix - wx) + Math.Abs(iy - wy2) + Math.Abs(iz - wz) < 1e-6, "its internal Y (sounds) maps to the same place");

            // climate, rain and drops (patches 3 and 4)
            var climHere = ba.GetClimateAt(new BlockPos((int)wx, (int)wy2, (int)wz, 0), EnumGetClimateMode.NowValues);
            var climDim = ba.GetClimateAt(fp, EnumGetClimateMode.NowValues);
            Check(climDim != null && climHere != null && Math.Abs(climDim.Temperature - climHere.Temperature) < 1.5f,
                $"the climate at the forge is the climate where she is ({climDim?.Temperature:0.0} °C there, {climHere?.Temperature:0.0} °C at her)");
            int rain = ba.GetRainMapHeightAt(fp);
            bool covered = ShipShelter.CoveredAt(ship, c0.I + frac[0] + 0.5, c0.J + frac[1] + 0.95, c0.K + frac[2] + 0.5);
            Check(covered ? rain > fp.Y : rain <= fp.Y, $"the rain map over it is hers ({(covered ? "under her decks" : "open sky")}: {rain} vs {fp.Y})");
            var dropped = sapi.World.SpawnItemEntity(new ItemStack(I("flint"), 3), new Vec3d(fp.X + 0.5, fp.InternalY + 0.5, fp.Z + 0.5));
            Check(dropped != null && dropped.Pos.Dimension == 0 && dropped.Pos.DistanceTo(new Vec3d(wx, wy2, wz)) < 1.5,
                $"an item dropped at a block of hers lands where she has it ({dropped?.Pos.XYZ}, dimension {dropped?.Pos.Dimension})");
            dropped?.Die(EnumDespawnReason.Removed);

            // the forge: charcoal, an iron ingot, lit; the calendar sped up as the spike did
            float speedWas = sapi.World.Calendar.CalendarSpeedMul;
            // (the self-test's server has nobody on it and its time stands still: moved on by hand, 30× real time)
            clockId = sapi.Event.RegisterGameTickListener(_ => sapi.World.Calendar.Add(0.05f), 200);
            var bef = (BlockEntityForge)ba.GetBlockEntity(fp);
            bef.Inventory[1].Itemstack = new ItemStack(I("charcoal"), 16);
            bef.Inventory[0].Itemstack = new ItemStack(I("ingot-iron"), 1);
            bef.TryIgnite();
            Check(bef.IsBurning, $"the forge aboard lights (fuel {bef.FuelLevel:0.00})");
            float t0 = bef.WorkItemStack.Collectible.GetTemperature(sapi.World, bef.WorkItemStack);

            // the bloomery: charcoal and ore, lit (its chimney is above it)
            var bbe = blp != null ? ba.GetBlockEntity(blp) as BlockEntityBloomery : null;
            var ore = I("nugget-hematite") ?? I("crushed-hematite");
            if (bbe != null && ore != null)
            {
                var inv = AccessTools.Field(typeof(BlockEntityBloomery), "bloomeryInv")?.GetValue(bbe) as InventoryBase;
                if (inv != null)
                {
                    inv[0].Itemstack = new ItemStack(I("charcoal"), 24);
                    inv[1].Itemstack = new ItemStack(ore, 24);
                }
                bool lit = bbe.TryIgnite();
                Check(lit && bbe.IsBurning, $"the bloomery aboard lights with its chimney over it ({ore.Code})");
            }
            else Check(false, $"the bloomery's block entity and an ore to smelt ({bbe != null}, {ore?.Code})");

            var oxyField = AccessTools.Field(typeof(BlockEntityForge), "extraOxygenRateParticles");
            double oxy0 = oxyField?.GetValue(bef) is float f0 ? f0 : -1;
            sapi.Event.RegisterCallback(_ => Safe(() =>
            {
                // the bellows, pumped (no player: as a mechanism would)
                // (Block.OnBlockInteractStart wants a player for the land claim check: the bellows' block entity, as the block calls it)
                bool pumped = (ba.GetBlockEntity(bp) as BlockEntityBellows)?.Interact(null) == true;
                sapi.Event.RegisterCallback(__ => Safe(() =>
                {
                    double oxy1 = oxyField?.GetValue(bef) is float f1 ? f1 : -1;
                    Check(pumped && oxy1 > oxy0, $"the bellows aboard blows into the forge (oxygen {oxy0:0.000} → {oxy1:0.000})");
                    WaitHot(bsys, ship, sd, bef, fp, ap, blp, t0, speedWas, then, 0);
                }, then), 1500);
            }, then), 1000);
        }

        /// <summary>
        /// (0.3.16) What reaching a client rests on, as far as it can be seen with nobody on: her record as a client reads it
        /// (through bytes), her chunks among the server's loaded chunks under the indices the explicit sending queues, the
        /// crosshair's allowance for her voxels round a working block, and an inert piece put with the switch off made
        /// working as when she loads (the user's 0.3.15 forge).
        /// </summary>
        void Delivery(ShipBlocksSystem bsys, EntityShip ship, ShipBlocksSystem.ShipDim sd, HansaMod mod, MountLayout l, Block block)
        {
            var rec = ship.BlockRecord;
            ShipBlockRecord ClientCopy()
            {
                var t = new Vintagestory.API.Datastructures.TreeAttribute();
                t.FromBytes(ship.WatchedAttributes.ToBytes());
                var r = new ShipBlockRecord();
                r.FromTree(t.GetTreeAttribute(EntityShip.BlocksKey));
                return r;
            }
            var cc = ClientCopy();
            Check(cc.Sub == rec.Sub && cc.Cells.Count == rec.Cells.Count && rec.Cells.All(c => cc.ByPiece(c.Piece) is ShipBlockCell x && x.I == c.I && x.J == c.J && x.K == c.K && x.Code == c.Code) && cc.Supports.Count == rec.Supports.Count,
                $"her record of working blocks reaches a client as it is (through bytes: sub {cc.Sub}, {cc.Cells.Count} cell(s), {cc.Supports.Count} support(s))");

            var idx = bsys.ChunkIndices(sd);
            var dimChunks = AccessTools.Field(typeof(Vintagestory.Common.BlockAccessorMovable), "chunks")?.GetValue(sd.Dim) as Dictionary<long, IWorldChunk>;
            Check(idx.Count > 0 && idx.All(i => bsys.LoadedChunk(i) != null) && dimChunks != null && idx.All(dimChunks.ContainsKey),
                $"her chunks are among the server's loaded chunks under the indices sent to players ({idx.Count}: {string.Join(", ", idx)}; the dimension's own {(dimChunks == null ? "?" : string.Join(", ", dimChunks.Keys))})");

            var frac = ship.GridFrac;
            var c0 = rec.Cells[0];
            double v = (ship.Grid?.V ?? 0.25) + 0.05;
            Check(ShipBlockMath.NearCell(frac[0], frac[1], frac[2], c0.I, c0.J, c0.K, c0.I + frac[0] + 0.5, c0.J + frac[1] + 1 + v * 0.8, c0.K + frac[2] + 0.5, v) &&
                  !ShipBlockMath.NearCell(frac[0], frac[1], frac[2], c0.I, c0.J, c0.K, c0.I + frac[0] + 0.5, c0.J + frac[1] + 1.5, c0.K + frac[2] + 0.5, v),
                $"the crosshair takes a hit on her voxels just over a working block (within {v:0.00}) as the block's own, not one half a block above it");

            // the user's 0.3.15 forge: put as an inert piece (switch off), made working as she loads (switch on)
            MountPoint mq = null;
            PiecePlan pq = null;
            foreach (var m in l.Floors)
            {
                if (ship.Pieces.Taken(m.Id)) continue;
                var plan = PiecePlanner.Plan(l, m, new VanillaPieceBlock(sapi.World, new ItemStack(block)), 1);
                if (ship.PlaceProblem(plan, false) != null) continue;
                (mq, pq) = (m, plan);
                break;
            }
            if (mq == null) { Check(false, "a free floor place for the migration check"); return; }
            bool was = mod.Config.WorkingBlocks;
            mod.Config.WorkingBlocks = false;
            string err = ship.PlacePiece(pq, new ItemStack(block), false, out var inert);
            mod.Config.WorkingBlocks = was;
            bool inertFirst = err == null && inert != null && !ship.IsWorkingPiece(inert.Id) && inert.Parts.Count == 1;
            bsys.TestMigrate(ship);
            var mc = inert != null ? ClientCopy().ByPiece(inert.Id) : null;
            var cell = inert != null ? ship.BlockRecord.ByPiece(inert.Id) : null;
            var at = cell != null ? new BlockPos(ShipBlockMath.BaseX(sd.Sub) + cell.I, ShipBlockMath.OriginOffset + cell.J, ShipBlockMath.BaseZ(sd.Sub) + cell.K, Dimensions.MiniDimensions) : null;
            Check(inertFirst && cell != null && mc != null && inert.Parts.Count == 0 && at != null && sapi.World.BlockAccessor.GetBlock(at).Id == block.Id &&
                  ship.WatchedAttributes.GetTreeAttribute("hansaPieces") != null && bsys.ChunkIndices(sd).All(i => bsys.LoadedChunk(i) != null),
                $"a {block.Code} put as an inert piece (switch off: {err}) works once she loads with the switch on: its block in her dimension, its piece not drawn, in the record a client gets ({inertFirst}, {cell != null}, {mc != null})");
            if (inert != null) ship.RemovePiece(inert, null);
        }

        long clockId;

        void Safe(Action a, Action then)
        {
            try { a(); }
            catch (Exception e) { Check(false, "working blocks: " + e); then(); }
        }

        void WaitHot(ShipBlocksSystem bsys, EntityShip ship, ShipBlocksSystem.ShipDim sd, BlockEntityForge bef, BlockPos fp, BlockPos ap, BlockPos blp, float t0, float speedWas, Action then, int waited)
        {
            var ws = bef.WorkItemStack;
            float temp = ws?.Collectible.GetTemperature(sapi.World, ws) ?? 0;
            if (temp < 900 && waited < 40)
            {
                sapi.Event.RegisterCallback(_ => Safe(() => WaitHot(bsys, ship, sd, bef, fp, ap, blp, t0, speedWas, then, waited + 1), then), 1000);
                return;
            }
            Check(temp > t0 + 300, $"the forge aboard heats the ingot ({t0:0} → {temp:0} °C in {waited + 2} s at 30× calendar)");
            var ba = sapi.World.BlockAccessor;
            var anvil = ba.GetBlockEntity(ap) as BlockEntityAnvil;
            Check(anvil != null, $"the anvil is still there when the ingot is hot ({ba.GetBlock(ap)?.Code}, {ba.GetBlockEntity(ap)?.GetType().Name}; under it {ba.GetBlock(ap.DownCopy())?.Code})");
            if (anvil == null) { then(); return; }
            var hot = ws.Clone();
            var workable = hot.Collectible.GetCollectibleInterface<IAnvilWorkable>();
            var wi = workable?.TryPlaceOn(hot, anvil);
            int hash0 = 0, hash1 = 0, vox0 = 0, vox1 = 0;
            if (wi != null)
            {
                AccessTools.Field(typeof(BlockEntityAnvil), "workItemStack").SetValue(anvil, wi);
                var recipes = wi.Collectible.GetCollectibleInterface<IAnvilWorkable>().GetMatchingRecipes(wi);
                var r = recipes.FirstOrDefault(x => x.Output.ResolvedItemstack?.Collectible.Code.Path.Contains("plate") == true) ?? recipes.First();
                anvil.SelectedRecipeId = r.RecipeId;
                (vox0, hash0) = Voxels(anvil);
                Vec3i target = null;
                for (int y = 5; y >= 1 && target == null; y--) for (int x = 0; x < 16 && target == null; x++) for (int z = 0; z < 16 && target == null; z++) if (anvil.Voxels[x, y, z] == 1) target = new Vec3i(x, y, z);
                if (target != null) anvil.OnHit(target);
                (vox1, hash1) = Voxels(anvil);
                anvil.MarkDirty();
            }
            Check(wi != null && hash1 != hash0 && vox1 > 0, $"the hot ingot goes on the anvil aboard, a recipe is chosen and a hammer blow works it ({wi?.Collectible.Code}, voxels {vox0} → {vox1})");

            // she moves: the dimension goes with her
            ship.Pos.X += 2.0;
            ship.Pos.Yaw += 0.3f;
            sapi.Event.RegisterCallback(_ => Safe(() =>
            {
                var cp = sd.Dim.CurrentPos;
                ShipBlocksSystem.ToWorld(fp.X + 0.5, fp.Y + 0.5, fp.Z + 0.5, false, out double wx, out double wy, out double wz);
                var c0 = ship.BlockRecord.ByPiece(ship.BlockRecord.Cells[0].Piece);
                var frac = ship.GridFrac;
                var fc = ship.BlockRecord.Cells.First(c => { ShipBlockMath.DimPos(ship.BlockRecord.Sub, c.I, c.J, c.K, out int x, out int y, out int z); return x == fp.X && y == fp.Y && z == fp.Z; });
                ship.LocalToWorld(fc.I + frac[0] + 0.5, fc.J + frac[1] + 0.5, fc.K + frac[2] + 0.5, out double ex, out double ey, out double ez);
                Check(Math.Abs(cp.X - ship.Pos.X) < 1e-6 && Math.Abs(cp.Yaw - ship.Pos.Yaw) < 1e-6 && Math.Abs(wx - ex) + Math.Abs(wy - ey) + Math.Abs(wz - ez) < 1e-6,
                    $"she moves and turns: her dimension follows ({cp.X:0.00} / {ship.Pos.X:0.00}, yaw {cp.Yaw:0.000}); the forge is still at its place aboard");
                Reload(bsys, ship, fp, ap, blp, hash1, speedWas, then);
            }, then), 300);
        }

        static (int, int) Voxels(BlockEntityAnvil anvil)
        {
            int c = 0, h = 17;
            for (int x = 0; x < 16; x++) for (int y = 0; y < 6; y++) for (int z = 0; z < 16; z++)
                {
                    if (anvil.Voxels[x, y, z] != 0) c++;
                    h = h * 31 + anvil.Voxels[x, y, z] * (x * 97 + y * 13 + z);
                }
            return (c, h);
        }

        void Reload(ShipBlocksSystem bsys, EntityShip ship, BlockPos fp, BlockPos ap, BlockPos blp, int anvilHash, float speedWas, Action then)
        {
            var ba = sapi.World.BlockAccessor;
            var bef = (BlockEntityForge)ba.GetBlockEntity(fp);
            float tempWas = bef.WorkItemStack.Collectible.GetTemperature(sapi.World, bef.WorkItemStack);
            int sub = ship.BlockRecord.Sub;
            // ((0.3.27) its ten game hours are ~40 s here, about as long as the forge takes: it may have burnt out by now)
            bool burnWas = blp != null && ba.GetBlockEntity(blp) is BlockEntityBloomery bb0 && bb0.IsBurning;
            bsys.TestReload(ship);
            var sd = bsys.DimOf(ship);
            var bef2 = ba.GetBlockEntity(fp) as BlockEntityForge;
            var an2 = ba.GetBlockEntity(ap) as BlockEntityAnvil;
            float temp2 = bef2?.WorkItemStack != null ? bef2.WorkItemStack.Collectible.GetTemperature(sapi.World, bef2.WorkItemStack) : -1;
            Check(sd != null && sd.Sub == sub && bef2 != null && bef2.IsBurning && Math.Abs(temp2 - tempWas) < 150 && an2 != null && an2.WorkItemStack != null && Voxels(an2).Item2 == anvilHash,
                $"saved, let go of and made again from her schematic (as after a restart): the forge still burns with its ingot ({tempWas:0} → {temp2:0} °C), the anvil's work piece as it was; same sub-dimension {sd?.Sub}");
            var bbe = blp != null ? ba.GetBlockEntity(blp) as BlockEntityBloomery : null;
            Check(bbe != null && bbe.IsBurning == burnWas && ba.GetBlock(blp.UpCopy()).Code.Path.Contains("bloomerychimney"),
                $"the bloomery under its chimney after the reload as before it ({(burnWas ? "burning" : "burnt out already")}; now {(bbe == null ? (blp != null ? ba.GetBlock(blp)?.Code?.ToString() : "no place") : bbe.IsBurning ? "burning" : "out")}; over it {(blp != null ? ba.GetBlock(blp.UpCopy())?.Code?.ToString() : "-")})");

            // taking up: the forge with something in it is refused; the bellows goes, its block and its support with it
            var forgePiece = ship.Pieces.Get(ship.BlockRecord.Cells.First(c => ba.GetBlock(new BlockPos(ShipBlockMath.BaseX(sub) + c.I, ShipBlockMath.OriginOffset + c.J, ShipBlockMath.BaseZ(sub) + c.K, 1)).Code.Path == "forge").Piece);
            string refused = ship.RemovePiece(forgePiece, null);
            var bellowsCell = ship.BlockRecord.Cells.First(c => c.Code.Contains("bellows"));
            var bpos = new BlockPos(ShipBlockMath.BaseX(sub) + bellowsCell.I, ShipBlockMath.OriginOffset + bellowsCell.J, ShipBlockMath.BaseZ(sub) + bellowsCell.K, 1);
            string removed = ship.RemovePiece(ship.Pieces.Get(bellowsCell.Piece), null);
            sapi.Event.RegisterCallback(_ => Safe(() =>
            {
                Check(refused == "hansa:piece-not-empty" && removed == null && ba.GetBlock(bpos).Id == 0 && ba.GetBlock(bpos.DownCopy()).Id == 0 &&
                      ship.BlockRecord.ByPiece(bellowsCell.Piece) == null && ship.Pieces.Get(bellowsCell.Piece) == null,
                    $"a working block with something in it isn't taken up ({refused}); the bellows is, its block and support gone ({removed})");
                Mechanisms(bsys, ship, speedWas, blp, then);
            }, then), 200);
        }

        /// <summary>Information only (not a check): does a mechanical network form and turn in her dimension (an axle chain on a creative rotor)?</summary>
        void Mechanisms(ShipBlocksSystem bsys, EntityShip ship, float speedWas, BlockPos blp, Action then)
        {
            var sd = bsys.DimOf(ship);
            var ba = sapi.World.BlockAccessor;
            int sub = sd.Sub;
            BlockPos P(int i) => new BlockPos(ShipBlockMath.BaseX(sub) + 100 + i, ShipBlockMath.OriginOffset, ShipBlockMath.BaseZ(sub) + 100, 1);
            var rotor = sapi.World.GetBlock(new AssetLocation("game", "creativerotor-east"));
            var axle = sapi.World.GetBlock(new AssetLocation("game", "woodenaxle-we"));
            var gear = sapi.World.GetBlock(new AssetLocation("game", "angledgears-s"));
            if (rotor == null || axle == null) { Log($"info mechanisms: rotor {rotor?.Code}, axle {axle?.Code} not found"); Finale(ship, speedWas, blp, then); return; }
            // (axles both sides of the rotor: whichever side it drives)
            sd.Dim.SetBlock(axle.Id, P(0));
            sd.Dim.SetBlock(axle.Id, P(1));
            sd.Dim.SetBlock(rotor.Id, P(2));
            sd.Dim.SetBlock(axle.Id, P(3));
            sd.Dim.SetBlock(axle.Id, P(4));
            sapi.Event.RegisterCallback(_ => Safe(() =>
            {
                string Net(BlockPos p)
                {
                    var be = ba.GetBlockEntity(p);
                    var beh = be?.Behaviors?.FirstOrDefault(b => b.GetType().Name.StartsWith("BEBehaviorMP"));
                    var net = beh?.GetType().GetProperty("Network")?.GetValue(beh);
                    if (net == null) return $"{ba.GetBlock(p)?.Code}: {(beh == null ? "no MP behaviour" : "no network")}";
                    var id = net.GetType().GetField("networkId")?.GetValue(net) ?? net.GetType().GetProperty("networkId")?.GetValue(net);
                    var speed = net.GetType().GetProperty("Speed")?.GetValue(net) ?? net.GetType().GetField("speed", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)?.GetValue(net);
                    return $"{ba.GetBlock(p)?.Code}: network {id} speed {speed}";
                }
                Log("info mechanisms in her dimension (3 s after placing): " + string.Join("; ", Enumerable.Range(0, 5).Select(i => Net(P(i)))));
                for (int i = 0; i < 5; i++) sd.Dim.SetBlock(0, P(i));
                Finale(ship, speedWas, blp, then);
            }, then), 3000);
        }

        void Finale(EntityShip ship, float speedWas, BlockPos blp, Action then)
        {
            // the bloomery's 10 game hours at 30×: ~40 s
            sapi.Event.RegisterCallback(_ => Safe(() =>
            {
                var ba = sapi.World.BlockAccessor;
                var bbe = blp != null ? ba.GetBlockEntity(blp) as BlockEntityBloomery : null;
                var inv = bbe != null ? AccessTools.Field(typeof(BlockEntityBloomery), "bloomeryInv")?.GetValue(bbe) as InventoryBase : null;
                var outStack = inv?[2]?.Itemstack;
                Check(bbe != null && !bbe.IsBurning && outStack != null && outStack.StackSize > 0, $"the bloomery aboard burns out and leaves a bloom ({outStack?.StackSize}x {outStack?.Collectible?.Code})");
                sapi.World.Calendar.CalendarSpeedMul = speedWas;
                sapi.Event.UnregisterGameTickListener(clockId);
                ship.Die(EnumDespawnReason.Removed);
                then();
            }, then), 44000);
        }
    }
}
