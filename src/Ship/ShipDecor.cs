using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>What a held item does to a paint zone: paint it a dye's colour, gild it, scrape it back.</summary>
    public enum PaintToolKind { None, Dye, Gold, Knife }

    /// <summary>
    /// Decorating a ship (the user's decisions of 2026-10-02): vanilla blocks put on her mount points (<see cref="MountLayout"/>)
    /// as pieces (<see cref="ShipPiece"/>), and her paint (<see cref="ShipPaint"/>).
    /// - A piece is put with right-click (the block in hand, which it takes in survival), facing whoever put it; taken up by
    ///   holding left mouse on it (the item back into the inventory; not a container with something in it, nor with the
    ///   inventory full). What it does: a container is a storage of vanilla's size, opened with right-click and saved with
    ///   her (its contents in "hansaInv" as the hold's are); a bed is slept in as her bunks are; a chair, stool or bench is
    ///   sat on; a light gives its light; anything else is drawn. Pieces are solid (their collision boxes) for walking.
    /// - Saved in WatchedAttributes "hansaPieces" (sent to every client), the paint in "hansaPaint".
    /// - A new ship gets the design's start pieces (lanterns on their hooks); an older one has its lanterns moved onto the
    ///   hooks as pieces, and what was on a chest, cargo or bed place her design no longer has becomes a piece where it stood
    ///   (a container with what's in it). What can't be placed goes into the hold as before.
    /// - No access rights yet: anyone aboard may place, take up and paint.
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>Storage dialogs' packet ids for pieces: (PiecePacketBase + piece id) &lt;&lt; 11, as a fixture's are (index + 1) &lt;&lt; 11.</summary>
        public const int PiecePacketBase = 0x10000;
        /// <summary>How long left mouse is held to take a piece up (s).</summary>
        public const float PickupSeconds = 0.6f;

        public readonly ShipPieceSet Pieces = new();
        public readonly ShipPaintState Paint = new();
        /// <summary>Bumped whenever her paint changes (the renderer builds the painted zones again).</summary>
        public int PaintVersion;
        readonly Dictionary<int, FixtureDef> pieceFixtures = new();

        public MountLayout Mounts => Def?.Mounts;

        /// <summary>(0.3.18) Her ladders: the design's and her ladder-stairs' (pieces), as the deck mover climbs them.</summary>
        public LadderDef[] Ladders => pieceLadders ?? Def?.Ladders ?? Array.Empty<LadderDef>();
        LadderDef[] pieceLadders;

        /// <summary>(0.1.0-dev.6, note 36) Her short ship's stairs' flights, where the deck mover takes a ship's stair's step (null: none).</summary>
        public double[][] SteepFlights { get; private set; }

        // ------------------------------------------------------------------ loading and saving

        /// <summary>Right after her fittings: her pieces (with their storages and seats) and her paint; clients follow both.</summary>
        void InitDecor()
        {
            LoadPieces();
            LoadPaint();
            if (Api.Side == EnumAppSide.Client)
            {
                WatchedAttributes.RegisterModifiedListener("hansaPieces", () => { LoadPieces(); LoadInventories(); });
                WatchedAttributes.RegisterModifiedListener("hansaPaint", LoadPaint);
            }
        }

        void LoadPieces()
        {
            if (Def == null) return;
            Pieces.FromTree(WatchedAttributes.GetTreeAttribute("hansaPieces"));
            bool stairs = false;
            foreach (var p in Pieces.All.ToArray())
            {
                if (p.Stack != null && !p.Stack.ResolveBlockOrItem(World)) p.Stack = null;
                // (0.3.8's start lanterns had no metal or glass: vanilla's defaults, as when they're put)
                VanillaPieceBlock.WithDefaults(World, p.Stack);
                // (0.3.18, note 162) a stair from before is a ladder-stair now: its boxes (the same on every side)
                stairs |= PiecePlanner.RefreshStair(Mounts, p);
                // ((0.3.27, notes 202, 221) a lantern on a hook the design no longer has (its ceiling hooks are the mod's now,
                // its mast hooks moved): onto the nearest free hook)
                if (Api.Side == EnumAppSide.Server) stairs |= RehookLantern(p);
            }
            if (stairs && Api.Side == EnumAppSide.Server)
            {
                UpdateRakes();
                SavePieces();
            }
            SyncPieceRuntime();
            RebuildGrid(OnlyOpeningsChanged(WatchedAttributes.GetTreeAttribute("hansaPieces"), ref lastPiecesTree));
        }

        /// <summary>
        /// (0.3.27, notes 202, 221) A piece hung on a hook that isn't hers any more (the design's ceiling hooks over her
        /// rooms are gone: the mod's own ceiling hooks are there; a mast hook moved up to the design's hook's height): hung on
        /// the nearest free hook within 1.2 of where it hung, as put there; else it stays where it is. True if moved.
        /// </summary>
        bool RehookLantern(ShipPiece p)
        {
            if (p?.Kind != MountKind.Hook || p.Stack?.Block == null || Mounts == null || p.Box == null) return false;
            var at = Mounts.Get(p.Mount);
            // ((0.1.0-dev.9, caravel note 78) a figurehead whose place moved with her bow (her model changes it): set on it again,
            // where it is now; its place gone, it stays where it was)
            if (p.Hang == PiecePlanner.FigureheadHang)
            {
                if (at == null || p.Parts.Count == 0) return false;
                var again = PiecePlanner.Plan(Mounts, at, new VanillaPieceBlock(World, p.Stack, false), p.Dir);
                if (again.Error != null || again.Parts.Count == 0) return false;
                var o0 = p.Parts[0].Origin;
                var o1 = again.Parts[0].Origin;
                if (Math.Abs(o0[0] - o1[0]) + Math.Abs(o0[1] - o1[1]) + Math.Abs(o0[2] - o1[2]) < 1e-3 && p.Parts[0].Quarter == again.Parts[0].Quarter) return false;
                var fp = ShipPiece.From(again, p.Id, p.Stack);
                fp.Paint = p.Paint;
                Pieces.Remove(p.Id);
                Pieces.Add(fp);
                return true;
            }
            bool moved = at != null && MountLayout.IsMastHook(at.Id) && Math.Abs(at.Y - p.Box[4]) > 0.1 && p.Hang != "up";
            if (at != null && !moved) return false;
            double hx = (p.Box[0] + p.Box[3]) / 2, hy = p.Hang == "up" ? p.Box[1] : p.Box[4], hz = (p.Box[2] + p.Box[5]) / 2;
            MountPoint best = moved ? at : null;
            double bd = 1.2;
            if (best == null)
                foreach (var m in Mounts.All)
                {
                    if (m.Kind != MountKind.Hook || m.Hang != p.Hang && !(m.Hang == null && p.Hang == "down") || Pieces.All.Any(o => o.Id != p.Id && o.Mounts.Contains(m.Id))) continue;
                    double dd = Math.Sqrt((m.X - hx) * (m.X - hx) + (m.Y - hy) * (m.Y - hy) + (m.Z - hz) * (m.Z - hz));
                    if (dd < bd) { bd = dd; best = m; }
                }
            if (best == null) return false;
            var plan = PiecePlanner.Plan(Mounts, best, new VanillaPieceBlock(World, p.Stack, false), p.Dir);
            if (plan.Error != null) return false;
            var np = ShipPiece.From(plan, p.Id, p.Stack);
            np.Paint = p.Paint;
            Pieces.Remove(p.Id);
            Pieces.Add(np);
            return true;
        }

        void SavePieces()
        {
            var t = new TreeAttribute();
            Pieces.ToTree(t);
            WatchedAttributes["hansaPieces"] = t;
            WatchedAttributes.MarkPathDirty("hansaPieces");
        }

        void LoadPaint()
        {
            Paint.FromTree(WatchedAttributes.GetTreeAttribute("hansaPaint"));
            PaintVersion++;
        }

        void SavePaint()
        {
            var t = new TreeAttribute();
            Paint.ToTree(t);
            WatchedAttributes["hansaPaint"] = t;
            WatchedAttributes.MarkPathDirty("hansaPaint");
            PaintVersion++;
        }

        /// <summary>A storage for each container piece (sized like it), a bunk or a seat for each bed and chair; gone ones gone.</summary>
        void SyncPieceRuntime()
        {
            var alive = new HashSet<int>(Pieces.ById.Keys);
            foreach (int id in pieceFixtures.Keys.ToArray())
            {
                if (alive.Contains(id)) continue;
                pieceFixtures.Remove(id);
                RemovePieceSeat("bunk:piece-" + id);
                RemovePieceSeat("sit:piece-" + id);
                if (Storages.Remove("piece-" + id) && Api.Side == EnumAppSide.Server)
                    WatchedAttributes.GetTreeAttribute("hansaInv")?.RemoveAttribute("piece-" + id);
            }
            foreach (var p in Pieces.All)
            {
                pieceFixtures.TryGetValue(p.Id, out var was);
                var f = pieceFixtures[p.Id] = PieceFixture(p);
                // (review H1-6) turned: its bunk or seat goes with it (RotatePiece refuses while someone's in it)
                if (was != null && was != f)
                {
                    RemovePieceSeat("bunk:" + f.Code);
                    RemovePieceSeat("sit:" + f.Code);
                }
                if (p.Role == PieceRole.Storage)
                {
                    int slots = Math.Clamp(p.Slots > 0 ? p.Slots : 16, 1, 64);
                    if (!(Storages.TryGetValue(p.StorageCode, out var existing) && existing.Count == slots))
                    {
                        var inv = new InventoryGeneric(slots, "hansa-" + p.StorageCode, EntityId.ToString(), Api);
                        if (Api.Side == EnumAppSide.Server)
                        {
                            string code = p.StorageCode;
                            inv.SlotModified += _ => SaveInventory(code);
                        }
                        Storages[p.StorageCode] = inv;
                    }
                }
                if (p.Role == PieceRole.Bed && GetSeat("bunk:" + f.Code) == null)
                {
                    var b = new BunkSeat(this, f);
                    seats[b.SeatId] = b;
                }
                if (p.Role == PieceRole.Seat && GetSeat("sit:" + f.Code) == null)
                {
                    var s = new PieceSeat(this, f);
                    seats[s.SeatId] = s;
                }
            }
            RebuildSeatArray();
            // (0.3.18) her ladder-stairs climbed as her ladders are
            var ladders = new List<LadderDef>(Def?.Ladders ?? Array.Empty<LadderDef>());
            foreach (var p in Pieces.All)
                if (p.Role == PieceRole.Ladder && PiecePlanner.StairLadder(Mounts?.Get(p.Mount), p.Dir, p.Rise, "piece-" + p.Id) is LadderDef sl) ladders.Add(sl);
            // ((0.3.27, note 219) her brows run out to a quay: walked as ladders of their own kind)
            ladders.AddRange(BrowLadders());
            pieceLadders = ladders.ToArray();
            // ((0.1.0-dev.6, note 36) her short ship's stairs: walked with a ship's stair's step)
            var steep = new List<double[]>();
            foreach (var p in Pieces.All)
                if (p.Role == PieceRole.Stair && PiecePlanner.SteepFlightBox(Mounts?.Get(p.Mount), p.Dir, p.Rise > 0 ? p.Rise : 3, PiecePlanner.RunOf(p)) is double[] sb) steep.Add(sb);
            SteepFlights = steep.Count > 0 ? steep.ToArray() : null;
            SyncPieceFixtures();
        }

        void RemovePieceSeat(string id)
        {
            if (!seats.TryGetValue(id, out var s)) return;
            if (Api.Side == EnumAppSide.Server && s.Passenger is EntityAgent a) a.TryUnmount();
            seats.Remove(id);
        }

        /// <summary>
        /// A piece as a fixture (for what works by fixtures: a bunk or a seat, the storage dialog, standing up beside it): its
        /// storage's code, where it is (the sleeper's or sitter's place, else its middle), its box, which way it faces.
        /// </summary>
        public FixtureDef PieceFixture(ShipPiece p)
        {
            pieceFixtures.TryGetValue(p.Id, out var f);
            if (f != null && f.Code == p.StorageCode && ReferenceEquals(f.Sel, p.Box)) return f;
            var c = p.Center;
            var nf = new FixtureDef
            {
                Type = "piece", Code = p.StorageCode, Kind = p.Role == PieceRole.Bed ? "bed" : p.Role == PieceRole.Seat ? "seat" : p.Role == PieceRole.Storage ? "chest" : "piece",
                Pos = p.Seat != null ? (double[])p.Seat.Clone() : new[] { c[0], p.Box?[1] ?? c[1], c[2] },
                Yaw = p.Role is PieceRole.Bed or PieceRole.Seat ? p.SeatYaw : ShipMounts.YawOfDir(p.Dir),
                Sel = p.Box, Efficiency = p.Efficiency > 0 ? p.Efficiency : 0.7f, Index = PiecePacketBase + p.Id - 1
            };
            // (review H1-6) the cached one while the piece stands as it did (read again from a sync: what its seat holds),
            // a new one once it's turned (its place, facing and box)
            if (f != null && f.Code == nf.Code && f.Yaw == nf.Yaw && SameValues(f.Pos, nf.Pos) && SameValues(f.Sel, nf.Sel)) return f;
            return nf;
        }

        static bool SameValues(double[] a, double[] b) => a == b || a != null && b != null && a.AsSpan().SequenceEqual(b);

        /// <summary>A fixture by the index a packet gives (the design's, or a piece's stand-in), or null.</summary>
        public FixtureDef FixtureByIndex(int index)
        {
            if (Def == null) return null;
            if (index >= 0 && index < Def.Fixtures.Length) return Def.Fixtures[index];
            int id = index - PiecePacketBase + 1;
            return Pieces.Get(id) is ShipPiece p ? PieceFixture(p) : null;
        }

        /// <summary>
        /// Her pieces' collision boxes into her collision (after the design's and the fittings'): the walls (partitions,
        /// shut doors) or everything else. An open door fills nothing (its leaf stands along the cell's side).
        /// </summary>
        void FillPieceSolids(VoxelGrid grid, bool walls)
        {
            foreach (var p in Pieces.All)
            {
                // (0.3.16) windows, railings, stairs and shelves too: pieces go right up to them
                bool wall = p.WallLike;
                // (0.3.20) an open hatch fills nothing either (its leaves stand up beside the opening)
                if (wall != walls || p.Role is PieceRole.Door or PieceRole.Hatch && p.Open) continue;
                foreach (var s in p.Solids) grid.Fill(s);
            }
        }

        /// <summary>A storage dialog's packet for a piece's storage: true if it was one.</summary>
        bool PieceInventoryPacket(IServerPlayer player, int fi, int inner, byte[] data)
        {
            if (fi < PiecePacketBase - 1) return false;
            var p = Pieces.Get(fi - PiecePacketBase + 1);
            // (0.3.18) a hearth put aboard: its dialog is the hearth's (vanilla's firepit inventory)
            if (p != null && p.Role == PieceRole.Hearth && HearthFixture != null && HearthFixture == PlacedHearth && HearthInv != null)
            {
                if (inner < 1000 && HearthInv.HasOpened(player)) HearthInv.InvNetworkUtil.HandleClientPacket(player, inner, data);
                return true;
            }
            if (p != null && Storages.TryGetValue(p.StorageCode, out var inv) && inner < 1000 && inv.HasOpened(player))
                inv.InvNetworkUtil.HandleClientPacket(player, inner, data);
            return true;
        }

        void Changed()
        {
            // (0.3.16) a railing beside a stair follows it (raked), worked out whenever her pieces change
            if (Api?.Side == EnumAppSide.Server) UpdateRakes();
            SyncPieceRuntime();
            SavePieces();
            RebuildGrid();
        }

        // ------------------------------------------------------------------ the crosshair on a piece

        /// <summary>A ray (world, as the selection ray) in her frame: origin and normalised direction.</summary>
        public void RayToLocal(Vec3d origin, Vec3d dir, out Vec3d o, out Vec3d d)
        {
            double len = Math.Max(1e-9, dir.Length());
            ShipFrame.ToLocal(Pos.Yaw, Pitch, Roll, origin.X - Pos.X, origin.Y - Pos.InternalY, origin.Z - Pos.Z, out double ox, out double oy, out double oz);
            ShipFrame.ToLocal(Pos.Yaw, Pitch, Roll, dir.X / len, dir.Y / len, dir.Z / len, out double dx, out double dy, out double dz);
            o = new Vec3d(ox, oy, oz);
            d = new Vec3d(dx, dy, dz);
        }

        /// <summary>A piece in front of what her ray met (or nothing met): the hit is the piece.</summary>
        bool PieceRay(Vec3d origin, Vec3d dir, double range, bool got, ref ShipRayHit hit)
        {
            if (Pieces.Count == 0) return got;
            RayToLocal(origin, dir, out var o, out var d);
            var p = Pieces.Pick(o, d, range, out double t);
            if (p == null) return got;
            // (0.3.24, note 177) its own solid layer (a door's, seen slanting from that side) doesn't hide it
            if (got && t > hit.Distance + 0.02 && !(hit.Fixture == null && ShipPieceSet.OwnHit(p, o, d, hit.Distance))) return got;
            hit.Distance = t;
            hit.Fixture = null;
            hit.Part = new ShipPart { Kind = ShipPartKind.Piece, Index = p.Id };
            hit.Local = o.AddCopy(d.X * t, d.Y * t, d.Z * t);
            ShipFrame.ToWorld(Pos.Yaw, Pitch, Roll, hit.Local.X, hit.Local.Y, hit.Local.Z, out double wx, out double wy, out double wz);
            hit.World = new Vec3d(Pos.X + wx, Pos.InternalY + wy, Pos.Z + wz);
            hit.LocalNormal = new Vec3d(-d.X, -d.Y, -d.Z);
            hit.Face = BlockFacing.FromVector(-dir.X, -dir.Y, -dir.Z);
            return true;
        }

        /// <summary>What a piece is called in the look-at box: the block's name (and what's in a container).</summary>
        public string PieceText(int id)
        {
            var p = Pieces.Get(id);
            if (p == null) return null;
            string name = PieceName(p);
            if (p.Role == PieceRole.Storage && Storages.TryGetValue(p.StorageCode, out var inv))
            {
                int used = inv.Count(s => !s.Empty);
                return Lang.Get("hansa:piece-info-storage", name, used, inv.Count);
            }
            // (0.3.18) a hearth or an oven put aboard: how it stands, as the design's
            if (p.Role == PieceRole.Hearth && HearthFixture != null && HearthFixture == PlacedHearth)
            {
                var sb = new System.Text.StringBuilder(name).AppendLine();
                if (!HearthFlued) sb.AppendLine(Lang.Get("hansa:hearth-no-flue"));
                HearthInfo(sb);
                return sb.ToString().TrimEnd();
            }
            if (p.Role == PieceRole.Oven && OvenFixture != null && OvenFixture == PlacedOven)
            {
                var sb = new System.Text.StringBuilder(name).AppendLine();
                OvenInfo(sb);
                return sb.ToString().TrimEnd();
            }
            return name;
        }

        public string PieceName(ShipPiece p) =>
            p?.Role == PieceRole.Panel ? Lang.Get("hansa:piece-panel", p.Stack?.GetName() ?? "") :
            p?.Role == PieceRole.Partition ? Lang.Get("hansa:piece-partition", p.Stack?.GetName() ?? "") :
            // (0.3.16) what the modular pieces are called: "Window (Leaded glass pane)", "Railing (Oak fence)"...
            p?.Role == PieceRole.Hatch ? Lang.Get(VanillaPieceBlock.IsGlazedTrapdoor(p.Stack?.Collectible?.Code?.ToString()) ? "hansa:piece-hatch-glazed" : "hansa:piece-hatch", p.Stack?.GetName() ?? "") :
            p?.Role is PieceRole.Window or PieceRole.Railing or PieceRole.Ladder or PieceRole.Stair or PieceRole.Shelf or PieceRole.Hearth or PieceRole.Oven or PieceRole.Flue ? Lang.Get("hansa:piece-" + p.Role.ToString().ToLowerInvariant(), p.Stack?.GetName() ?? "") :
            p?.Stack?.GetName() ?? Lang.Get("hansa:piece");

        /// <summary>A piece's name by its storage's (and stand-in fixture's) code, "piece-12".</summary>
        public string PieceNameByCode(string code) =>
            PieceName(code != null && code.StartsWith("piece-") && int.TryParse(code.Substring(6), out int id) ? Pieces.Get(id) : null);

        // ------------------------------------------------------------------ placing (server)

        public static bool Creative(IPlayer player) => player?.WorldData?.CurrentGameMode == EnumGameMode.Creative;

        /// <summary>How far a player may be from a mount point or piece to use it: the picking range and a little for the lag.</summary>
        bool InReach(IServerPlayer player, double lx, double ly, double lz, double box = 0.9)
        {
            var ep = player.Entity;
            if (ep == null) return false;
            LocalToWorld(lx, ly, lz, out double wx, out double wy, out double wz);
            double range = (player.WorldData?.PickingRange ?? 4.5f) + 1.5 + box;
            return ep.Pos.SquareDistanceTo(wx, wy - ep.LocalEyePos.Y, wz) <= range * range ||
                   ep.Pos.XYZ.Add(ep.LocalEyePos).SquareDistanceTo(wx, wy, wz) <= range * range;
        }

        /// <summary>
        /// Put the block in a player's hand on a mount point, facing dir (toward them: what their crosshair showed). Returns an
        /// error lang code or null.
        /// </summary>
        public string TryPlacePiece(IServerPlayer player, string mountId, int dir)
        {
            if (Def == null || Mounts == null) return "hansa:piece-nowhere";
            if (GoneDown) return "hansa:gone-down";
            var slot = player.InventoryManager.ActiveHotbarSlot;
            var stack = slot?.Itemstack;
            if (stack?.Block == null || stack.Block.Id == 0) return "hansa:piece-needs-block";
            // (0.3.21, step C) a gun goes on a gun place, not on a floor place
            if (IsGunBlock(stack.Collectible)) return "hansa:gunplace-gun-not-piece";
            var m = Mounts.Get(mountId);
            if (m == null) return "hansa:piece-nowhere";
            // (after 0.1.0-dev.6) building mode puts ship structure only; decoration goes aboard with it off
            // ((24 October 2026, caravel note 66) but a lantern on a hook: it's taken down in building mode too)
            if (VanillaPieceBlock.BuildingRefusal(stack.Block, VanillaPieceBlock.BuildingMode(player.Entity)) is string berr &&
                !(m.Kind == MountKind.Hook && new VanillaPieceBlock(World, stack).Light)) return berr;
            if (!InReach(player, m.CX, m.CY, m.CZ)) return "hansa:piece-too-far";
            if (m.Kind != MountKind.Hook && !SeenBy(player, m.Box)) return "hansa:piece-too-far";
            // (0.3.18) building mode (0.3.19: the player's toggle, not a saw in the off hand)
            // ((24 October 2026, notes 70, 79) as the client planned it: another way over the place where vanilla's doesn't fit)
            var plan = PiecePlanner.Plan(Mounts, m, new VanillaPieceBlock(World, stack, VanillaPieceBlock.BuildingMode(player.Entity)), dir, pl => PlaceProblem(pl, false) == null);
            if (plan.Error != null) return plan.Error;
            string err = PlaceTakingBeams(plan, stack, player, out var placed);
            if (err != null) return err;
            if (!Creative(player))
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }
            PieceSound(placed, stack.Block.Sounds?.Place.Location);
            World.Logger.Audit("{0} put a {1} on {2} #{3} at {4}.", player.PlayerName, stack.Collectible.Code, GetName(), EntityId, mountId);
            return null;
        }

        /// <summary>
        /// (0.3.27, the user's stairs and beams) Put a piece as planned; a stair through a well: the beam segments in its way
        /// come out first (their planks to the player: survival, all or nothing; no player: just out), and go back in if it
        /// can't be put after all. Returns an error lang code or null.
        /// </summary>
        public string PlaceTakingBeams(PiecePlan plan, ItemStack stack, IServerPlayer player, out ShipPiece placed)
        {
            var beams = PlaceProblem(plan, true) == null ? BeamsInTheWay(plan) : null;
            if (beams != null)
            {
                string berr = TakeBeamsFor(beams, player);
                if (berr != null) { placed = null; return berr; }
            }
            string err = PlacePiece(plan, stack, true, out placed);
            if (err != null && beams != null)
            {
                foreach (int n in beams) Slots.Set(n, SlotState.Filled);
                SlotsChanged();
            }
            return err;
        }

        /// <summary>Put a piece as planned (no player's hand: the commands, a new ship's lanterns, an older ship's things). Returns an error lang code or null.</summary>
        public string PlacePiece(PiecePlan plan, ItemStack stack, bool checkPeople, out ShipPiece placed)
        {
            placed = null;
            string err = PlaceProblem(plan, checkPeople);
            if (err != null) return err;
            placed = ShipPiece.From(plan, Pieces.NextId, stack);
            // (a lantern without its metal and glass gets vanilla's)
            VanillaPieceBlock.WithDefaults(World, placed.Stack);
            Pieces.Add(placed);
            Changed();
            // (0.3.14) a working block (a forge, an anvil...) on a floor place: the real block in her dimension (ShipBlocks)
            OnPiecePlaced(placed, stack);
            return null;
        }

        /// <summary>
        /// Why a planned piece can't go there now (a lang code), or null: a place it takes is taken, it runs into another
        /// piece, into her (a shut door, something attached), or into someone standing there. The client asks the same for
        /// what its crosshair shows.
        /// </summary>
        PiecePlan clashPlan;
        int clashVersion = -1;
        string clashSays;

        /// <summary>Why her collision refuses a piece's plan (its boxes, its room), or null: what PlaceProblem asks of her structure.</summary>
        string StructureProblem(PiecePlan plan)
        {
            if (PiecePlanner.GridClash(Mounts, plan, PlaceGrid ?? Grid) is not double[] clash) return null;
            // ((0.3.27, the user's stairs and beams) a beam in its way that can come out with it (the deck either side
            // open): fine, it comes out when the stair is put (TryPlacePiece); one that can't: say what to do
            if (BeamsInTheWay(plan) != null) return null;
            // ((22 October 2026, caravel note 52) a beam only if one is in its way with the deck's cells over it up)
            if (BeamRules.ClashSays(Mounts, SlotLayout, Slots, plan, PlaceGrid ?? Grid, BeamAt) is string beam) return beam;
            // ((0.3.28) where one steps off it at its foot or onto the deck at its top: her side's curve, a fitting)
            if (plan.Role == PieceRole.Stair && StairLanding.At(plan, PlaceGrid ?? Grid)) return "hansa:flight-no-landing";
            return plan.Role == PieceRole.Ladder ? "hansa:stair-no-well" : plan.Role == PieceRole.Stair ? "hansa:flight-no-well" : plan.Role == PieceRole.Hearth ? "hansa:hearth-no-room" : "hansa:piece-blocked";
        }

        public string PlaceProblem(PiecePlan plan, bool checkPeople)
        {
            if (plan == null || plan.Error != null) return plan?.Error ?? "hansa:piece-nowhere";
            foreach (string mid in plan.Mounts) if (Pieces.Taken(mid)) return "hansa:piece-taken";
            // (0.3.20, step B) its places with her deck cells as they are (over a cell taken up: none)
            if (Slots != null && plan.Role != PieceRole.Hatch)
                foreach (string mid in plan.Mounts)
                    if (Mounts?.Get(mid) is MountPoint mp && !MountOpen(mp)) return "hansa:cell-empty-under";
            // on top of a piece: every place it takes stands on one with a top to stand on
            // (a door's upper half stands on its own lower half)
            // (0.3.16: a stair takes the places over its cells, standing on the deck)
            foreach (string mid in plan.Role is PieceRole.Ladder or PieceRole.Stair ? Enumerable.Empty<string>() : plan.Mounts)
            {
                var mm = Mounts?.Get(mid);
                if (mm?.Under != null && plan.Mounts.Contains(mm.Under)) continue;
                if (!Pieces.Supports(Mounts, mm)) return "hansa:piece-no-support";
            }
            if (Pieces.Blocking(plan) != null) return "hansa:piece-taken";
            // (0.3.24, notes 176, 181) not in the working room of a gun mounted on a place that started empty (its room
            // isn't kept in her layout: the player chooses the gun or the stair there)
            if (Def?.GunPlaces?.Length > 0 && MountLayout.FreeGunRoomHit(Def, plan.Solids.Concat(plan.Sel), GunMounted, plan.Role == PieceRole.Railing) != null) return "hansa:piece-gun-room";
            // (0.3.20, step B) nothing stands over an opening in her deck (a cell taken up); a hatch goes over one
            if (OverOpening(plan) is string open) return open;
            if (plan.Role == PieceRole.Hatch)
                return checkPeople && plan.Solids.Any(s => BoxOccupied(new[] { s[0], s[1], s[2], s[3], s[4] + 0.05, s[5] })) ? "hansa:piece-someone" : null;
            // (0.3.19, the user: "ladders look better without rails") no railing beside a ladder-stair
            // ((0.3.23) nor beside a ship's stair: it has its own handrails)
            if (plan.Role == PieceRole.Railing && PiecePlanner.BesideRailed(Mounts, Pieces.All, plan.Mount) is ShipPiece railed) return railed.Role == PieceRole.Ladder ? "hansa:railing-by-ladder" : "hansa:railing-by-stair";
            // (0.3.18, note 165) one hearth and one oven: the design's, or one put aboard
            if (plan.Role == PieceRole.Hearth && HearthFixture != null) return "hansa:hearth-have-one";
            if (plan.Role == PieceRole.Oven && OvenFixture != null) return "hansa:oven-have-one";
            // (a panel fills the wedge between the border and her side: her side is meant to be in its box; a partition's
            // and a door's layer was found free of her with its place; a flue's place was found free of her but for the deck
            // it goes through)
            if (plan.Role is PieceRole.Panel or PieceRole.Partition or PieceRole.Window or PieceRole.Railing or PieceRole.Flue) return null;
            if (plan.Role == PieceRole.Shelf) return checkPeople && plan.Solids.Any(BoxOccupied) ? "hansa:piece-someone" : null;
            if (plan.Role == PieceRole.Door) return checkPeople && plan.Solids.Any(BoxOccupied) ? "hansa:piece-someone" : null;
            // (on floor places, a piece's boxes only within its places' blocks: those the layout found free of her, a floor
            // cell's whole block; a lip over the edge, a cabinet's top board, touching her leaning side is no clash: note 72)
            // (0.3.18, note 162) a ladder-stair: the climber's room up it through the well and its landings (its own boxes
            // rest on her coaming at the top: PiecePlanner.GridClash, the offline tests' check too)
            // ((after 0.1.0-dev.6, Codex's review) what her collision says of it, worked out once for the plan as long as
            // her collision is as it was (FitVersion): the client asks every frame for the piece in the crosshair)
            if (!ReferenceEquals(plan, clashPlan) || clashVersion != FitVersion)
            {
                clashSays = StructureProblem(plan);
                clashPlan = plan;
                clashVersion = FitVersion;
            }
            if (clashSays != null) return clashSays;
            if (checkPeople)
            {
                double[] cells = plan.Kind == MountKind.Floor ? PiecePlanner.PlaceBounds(Mounts, plan) : null;
                foreach (var s0 in plan.Solids.Concat(plan.Room))
                    if (PiecePlanner.ClipTo(s0, cells) is double[] s && BoxOccupied(s)) return "hansa:piece-someone";
            }
            return null;
        }

        /// <summary>
        /// Server: can this player see the box (ship-local) from their eye, not through her hull, decks or bulkheads (her
        /// design's collision)? A use, a take-down or a placing through her side is refused (0.3.10).
        /// </summary>
        public bool SeenBy(IServerPlayer player, double[] box)
        {
            var ep = player?.Entity;
            if (ep == null || box == null || Def?.Grid == null) return true;
            var eye = WorldToLocal(ep.Pos.X + ep.LocalEyePos.X, ep.Pos.Y + ep.LocalEyePos.Y, ep.Pos.Z + ep.LocalEyePos.Z);
            // (0.3.20: through an opening in her deck, her cells as they are)
            return ShipPieceSet.Seen(DesignGrid, eye, box);
        }

        /// <summary>The block's own sound at a piece (its place, hit or break sound; planks if it has none).</summary>
        void PieceSound(ShipPiece p, AssetLocation sound)
        {
            if (p == null) return;
            var c = p.Center;
            LocalToWorld(c[0], c[1], c[2], out double x, out double y, out double z);
            World.PlaySoundAt(sound ?? new AssetLocation("sounds/block/planks"), x, y, z, null, true, 16);
        }

        /// <summary>Take a piece up (held left mouse on it): its item into the player's inventory. Returns an error lang code or null.</summary>
        public string TryRemovePiece(IServerPlayer player, int id)
        {
            var p = Pieces.Get(id);
            if (p == null) return null;
            if (GoneDown) return "hansa:gone-down";
            var c = p.Center;
            if (!InReach(player, c[0], c[1], c[2])) return "hansa:piece-too-far";
            if (!SeenBy(player, p.SeenBox)) return "hansa:piece-too-far";
            string err = RemovePiece(p, player);
            if (err == null)
            {
                PieceSound(p, p.Stack?.Block?.Sounds?.Break.Location);
                World.Logger.Audit("{0} took up a {1} from {2} #{3}.", player.PlayerName, p.Stack?.Collectible?.Code, GetName(), EntityId);
            }
            return err;
        }

        /// <summary>Take a piece away: not a container with something in it, nor a bed or seat someone's in; its item to the player (survival; refused if it doesn't fit). Null player: just gone (the commands).</summary>
        public string RemovePiece(ShipPiece p, IServerPlayer player)
        {
            if (p.Role == PieceRole.Storage && Storages.TryGetValue(p.StorageCode, out var inv) && !inv.Empty) return "hansa:piece-not-empty";
            if (GetSeat("bunk:" + p.StorageCode)?.Passenger != null) return "hansa:bunk-taken";
            if (GetSeat("sit:" + p.StorageCode)?.Passenger != null) return "hansa:piece-seat-taken";
            // (not from under another piece: that one first)
            if (Pieces.StandingOn(Mounts, p) != null) return p.Role == PieceRole.Hearth ? "hansa:hearth-flue-first" : "hansa:piece-under";
            // (0.3.18, note 165) the hearth and the oven empty and cold first; a flue not while the fire draws through it
            if (PieceFireProblem(p) is string fireErr) return fireErr;
            // (0.3.14) a working block: its real block out of her dimension (not while something's in it: ShipBlocks)
            string realErr = OnPieceRemoving(p, player);
            if (realErr != null) return realErr;
            if (player != null && !Creative(player) && p.Stack != null)
            {
                // (all or nothing: never dropped, it'd go into the sea)
                if (!player.InventoryManager.TryGiveItemstack(ReturnedStack(p), true)) return "hansa:piece-inventory-full";
            }
            Pieces.Remove(p.Id);
            Changed();
            return null;
        }

        /// <summary>
        /// (0.3.23) What taking a piece up gives back: its block; a stair laid or put with vanilla plank stairs before 0.3.23
        /// gives the ship's stair it is (plank stairs make no stair now), a ladder-stair saved with plank stairs (0.3.16-0.3.18)
        /// a ladder.
        /// </summary>
        ItemStack ReturnedStack(ShipPiece p)
        {
            var b = p.Stack?.Block;
            string code = p.Role == PieceRole.Stair && b != null && !VanillaPieceBlock.IsShipStair(b) ? ModularPieceDef.StairBlock((int)PiecePlanner.RunOf(p)) :
                          p.Role == PieceRole.Ladder && b != null && !VanillaPieceBlock.IsLadder(b) ? ModularPieceDef.DefaultBlock("ladder") : null;
            var nb = code == null ? null : World.GetBlock(new AssetLocation(code));
            return nb != null && nb.Id != 0 ? new ItemStack(nb) : p.Stack.Clone();
        }

        /// <summary>Right-click on a piece: open a container, sleep in a bed, sit on a chair.</summary>
        public void UsePiece(IServerPlayer player, int id)
        {
            var p = Pieces.Get(id);
            if (p == null) return;
            var c = p.Center;
            if (!InReach(player, c[0], c[1], c[2])) return;
            // ((0.3.23, note 169) a shut hatch from below too: its planking is in her design's collision)
            if (!SeenBy(player, p.SeenBox)) return;
            var f = PieceFixture(p);
            switch (p.Role)
            {
                case PieceRole.Storage:
                    if (!Storages.TryGetValue(p.StorageCode, out var inv)) return;
                    player.InventoryManager.OpenInventory(inv);
                    Mod.SendOpenStorage(player, this, f.Index);
                    PieceSound(p, new AssetLocation("sounds/block/chestopen"));
                    break;
                case PieceRole.Bed when !GoneDown:
                    TrySleep(player, f);
                    break;
                case PieceRole.Seat when !GoneDown:
                    if (GetSeat("sit:" + f.Code) is not PieceSeat seat) return;
                    if (seat.Passenger != null && seat.Passenger != player.Entity) { Notify(player, "hansa:piece-seat-taken"); return; }
                    player.Entity.TryMount(seat);
                    break;
                case PieceRole.Door:
                    ToggleDoorPiece(player, p);
                    break;
                // (0.3.20, step B) a hatch over an opening in her deck
                case PieceRole.Hatch:
                    ToggleHatch(player, p);
                    break;
                // (0.3.18) a hearth or an oven put aboard: as the design's (its stand-in fixture)
                case PieceRole.Hearth when f == HearthFixture:
                case PieceRole.Oven when f == OvenFixture:
                    Interact(player, f);
                    break;
                default:
                    // (a forge, an anvil, a quern: its workings are its block entity's, which pieces haven't got)
                    if (p.Stack?.Block is Block wb && VanillaPieceBlock.IsWorkstation(wb)) player.SendIngameError("hansa-ashore", Lang.GetL(player.LanguageCode, "hansa:piece-ashore-only", p.Stack.GetName()));
                    break;
            }
        }

        /// <summary>A door put aboard opened or shut (its block's own sounds); it won't shut on someone in the doorway.</summary>
        public bool ToggleDoorPiece(IServerPlayer player, ShipPiece p)
        {
            if (p?.Role != PieceRole.Door) return false;
            if (p.Open && p.Solids.Any(BoxOccupied))
            {
                if (player != null) Notify(player, "hansa:doorway-blocked");
                return false;
            }
            p.Open = !p.Open;
            var attr = p.Stack?.Block?.Attributes;
            string sound = (p.Open ? attr?["openSound"].AsString() : attr?["closeSound"].AsString()) ?? "sounds/block/door";
            PieceSound(p, new AssetLocation(sound));
            Changed();
            return true;
        }

        // ------------------------------------------------------------------ painting (server)

        /// <summary>What an item does to paint: a dye in a liquid container (its colour, how many dye items), gold plates, a knife.</summary>
        public static PaintToolKind PaintTool(ItemStack stack, out string colour, out int amount)
        {
            colour = null;
            amount = 0;
            var coll = stack?.Collectible;
            if (coll == null) return PaintToolKind.None;
            if (coll is ILiquidSource src && src.GetContent(stack) is ItemStack content && ShipPaint.DyeColour(content.Collectible?.Code?.ToString()) is string c)
            {
                colour = c;
                amount = content.StackSize;
                return PaintToolKind.Dye;
            }
            if (coll.Code?.ToString() == ShipPaint.GoldPlate)
            {
                amount = stack.StackSize;
                return PaintToolKind.Gold;
            }
            if (coll.Tool == EnumTool.Knife) return PaintToolKind.Knife;
            return PaintToolKind.None;
        }

        /// <summary>
        /// Paint (gild, scrape) the zone a player's crosshair was on (at: ship-local, where), with what's in their hand. Returns
        /// an error lang code (args in errArgs) or null; done: what was done, to say.
        /// </summary>
        public string TryPaint(IServerPlayer player, string zoneCode, Vec3d at, out object[] errArgs)
        {
            errArgs = Array.Empty<object>();
            var plan = Mod.PaintPlanFor(Def);
            if (plan == null) return "hansa:paint-notready";
            int zi = plan.ZoneIndex(zoneCode);
            // (0.3.17) a zone with nothing of hers in it on this design isn't one
            if (zi < 0 || plan.Area[zi] <= 0) return "hansa:paint-where";
            if (GoneDown) return "hansa:gone-down";
            if (at == null || !InReach(player, at.X, at.Y, at.Z, 0.3) || !plan.Near(zi, at.X, at.Y, at.Z, 0.6)) return "hansa:paint-where";
            string err = PaintWith(player.InventoryManager.ActiveHotbarSlot, player.Entity, Creative(player), plan.Zones[zi].Code, at, out errArgs, out var kind, out string colour);
            if (err == null)
                World.Logger.Audit("{0} {1} the {2} of {3} #{4}.", player.PlayerName, kind == PaintToolKind.Knife ? "scraped" : kind == PaintToolKind.Gold ? "gilded" : "painted " + colour,
                    zoneCode, GetName(), EntityId);
            return err;
        }

        /// <summary>
        /// Paint (gild, scrape) a zone with what's in a slot, taking what it costs from it (not in creative): the rules of
        /// <see cref="ShipPaint"/>. at: where (ship-local, for the sound; null: none). Returns an error lang code (args in
        /// errArgs) or null. The self-test calls it with a slot of its own.
        /// </summary>
        public string PaintWith(ItemSlot slot, Entity byEntity, bool creative, string zoneCode, Vec3d at, out object[] errArgs, out PaintToolKind kind, out string colour)
        {
            errArgs = Array.Empty<object>();
            kind = PaintToolKind.None;
            colour = null;
            var plan = Mod.PaintPlanFor(Def);
            if (plan == null) return "hansa:paint-notready";
            int zi = plan.ZoneIndex(zoneCode);
            if (zi < 0) return "hansa:paint-where";
            var zone = plan.Zones[zi];
            string err = ApplyPaintTool(slot, byEntity, creative, Paint.Of(zone.Code), plan.Area[zi], ShipPaint.Gildable(zone), at, out errArgs, out kind, out colour, out string now);
            if (err != null) return err;
            if (now == null) Paint.Zones.Remove(zone.Code);
            else Paint.Zones[zone.Code] = now;
            SavePaint();
            return null;
        }

        /// <summary>
        /// What a paint tool in a slot does to a surface painted <paramref name="was"/> (null: as built) of this area (m²):
        /// a dye paints it its colour (DyeItems of it taken from the container), gold plates gild it (if it's gildable), a
        /// knife scrapes it back (a point off the knife); nothing is used up in creative. The new paint in <paramref name="now"/>
        /// (null: as built). Returns an error lang code (args in errArgs) or null. A zone of hers and a wall put aboard alike.
        /// </summary>
        string ApplyPaintTool(ItemSlot slot, Entity byEntity, bool creative, string was, double area, bool gildable, Vec3d at,
            out object[] errArgs, out PaintToolKind kind, out string colour, out string now)
        {
            errArgs = Array.Empty<object>();
            now = was;
            var stack = slot?.Itemstack;
            kind = PaintTool(stack, out colour, out int have);
            switch (kind)
            {
                case PaintToolKind.Dye:
                {
                    if (was == colour) return "hansa:paint-same";
                    int need = ShipPaint.DyeItems(area);
                    if (!creative)
                    {
                        if (have < need)
                        {
                            errArgs = new object[] { need / (double)ShipPaint.DyeItemsPerLitre, have / (double)ShipPaint.DyeItemsPerLitre };
                            return "hansa:paint-dye-short";
                        }
                        var src = (ILiquidSource)stack.Collectible;
                        var taken = src.TryTakeContent(stack, need);
                        if (taken == null || taken.StackSize < need) return "hansa:paint-dye-short";
                        slot.MarkDirty();
                    }
                    now = colour;
                    if (at != null) PaintSound(at, "sounds/effect/water-pour");
                    return null;
                }
                case PaintToolKind.Gold:
                {
                    if (!gildable) return "hansa:paint-gild-kind";
                    if (was == ShipPaint.Gold) return "hansa:paint-same";
                    int need = ShipPaint.GoldPlates(area);
                    if (!creative)
                    {
                        if (have < need) { errArgs = new object[] { need, have }; return "hansa:paint-gold-short"; }
                        slot.TakeOut(need);
                        slot.MarkDirty();
                    }
                    now = ShipPaint.Gold;
                    if (at != null) PaintSound(at, "sounds/block/heavymetal-hit");
                    return null;
                }
                case PaintToolKind.Knife:
                    if (was == null) return "hansa:paint-bare";
                    now = null;
                    if (!creative && byEntity != null) stack.Collectible.DamageItem(World, byEntity, slot, 1);
                    if (at != null) PaintSound(at, "sounds/effect/woodgrind");
                    return null;
                default:
                    return "hansa:paint-needs-tool";
            }
        }

        /// <summary>
        /// (0.3.12, the user: placed walls can't be painted.) Paint, or scrape, a wall put aboard (a partition, a panel, a
        /// door: <see cref="ShipPiece.Paintable"/>) with what's in the player's hand, as her own surfaces are painted: its
        /// faces' area in dye (<see cref="ShipPiece.PaintArea"/>), a knife scrapes it back; it isn't gilded. Returns an error
        /// lang code (args in errArgs) or null.
        /// </summary>
        public string TryPaintPiece(IServerPlayer player, int id, out object[] errArgs)
        {
            errArgs = Array.Empty<object>();
            var p = Pieces.Get(id);
            if (p == null || !p.Paintable) return "hansa:paint-where";
            if (GoneDown) return "hansa:gone-down";
            var c = p.Center;
            if (!InReach(player, c[0], c[1], c[2]) || !SeenBy(player, p.Box)) return "hansa:paint-where";
            string err = PaintPieceWith(player.InventoryManager.ActiveHotbarSlot, player.Entity, Creative(player), p, out errArgs, out var kind, out string colour);
            if (err == null)
                World.Logger.Audit("{0} {1} a {2} on {3} #{4}.", player.PlayerName, kind == PaintToolKind.Knife ? "scraped" : "painted " + colour,
                    p.Stack?.Collectible?.Code, GetName(), EntityId);
            return err;
        }

        /// <summary>Paint (scrape) a wall put aboard with what's in a slot (the self-test calls it with a slot of its own).</summary>
        public string PaintPieceWith(ItemSlot slot, Entity byEntity, bool creative, ShipPiece p, out object[] errArgs, out PaintToolKind kind, out string colour)
        {
            errArgs = Array.Empty<object>();
            kind = PaintToolKind.None;
            colour = null;
            if (p == null || !p.Paintable) return "hansa:paint-where";
            var c = p.Center;
            string err = ApplyPaintTool(slot, byEntity, creative, p.Paint, p.PaintArea, false, new Vec3d(c[0], c[1], c[2]), out errArgs, out kind, out colour, out string now);
            if (err != null) return err;
            p.Paint = now;
            Changed();
            return null;
        }

        void PaintSound(Vec3d at, string sound)
        {
            LocalToWorld(at.X, at.Y, at.Z, out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation(sound), x, y, z, null, true, 16);
        }

        /// <summary>Set a zone's paint (or null: as built) without anyone's dye (the commands).</summary>
        public void SetPaint(string zone, string paint)
        {
            if (paint == null) Paint.Zones.Remove(zone);
            else Paint.Zones[zone] = paint;
            SavePaint();
        }

        // ------------------------------------------------------------------ a new ship's pieces, an older ship's things (server)

        /// <summary>
        /// Before what her design no longer has goes into the hold: a new ship gets the design's start pieces; an older one's
        /// lanterns go onto the hooks as pieces, and what's on a chest, cargo or bed place her design no longer has becomes a
        /// piece where it stood (a container with what was in it).
        /// </summary>
        void MigrateDecor()
        {
            if (Def == null || Mounts == null) return;
            bool hadPieces = WatchedAttributes.GetTreeAttribute("hansaPieces") != null;
            bool newShip = !hadPieces && WatchedAttributes.GetTreeAttribute("hansaFit") == null;
            bool changed = false;
            if (newShip) changed |= PlaceStartPieces();
            else changed |= RescueOrphanedPieces();
            // (0.3.19) railings beside a ladder-stair (raked to it till 0.3.18) taken off, once
            changed |= RemoveLadderRailings();
            // (0.3.16, notes 154-158) the design's modular pieces (cabin fronts, windows, doors, railings, stairs, panels):
            // on a new ship all; on an older one each not laid on her yet, where its places are free
            changed |= PlaceModularPieces() > 0;

            // her lanterns on what are hooks now
            foreach (var f in Def.Fixtures)
            {
                if (!ShipMounts.IsLegacyHook(f) || !Attached.TryGetValue(f.Code, out var lantern)) continue;
                if (PlaceOnHook("h:" + f.Code, f.Pos, lantern))
                {
                    Attached.Remove(f.Code);
                    FittingsChanged();
                    changed = true;
                }
            }

            // what's on a place her design no longer has
            var attached = WatchedAttributes.GetTreeAttribute("hansaFit")?.GetTreeAttribute("attach");
            if (attached != null)
            {
                foreach (var kv in attached.ToArray())
                {
                    if (Def.Fixture(kv.Key) is FixtureDef still && still.Type == "attach") continue;
                    var stack = attached.GetItemstack(kv.Key);
                    if (stack == null || !stack.ResolveBlockOrItem(World) || stack.Block == null) continue;
                    if (!LegacyPlaces.TryGet(Def.Code, kv.Key, out var pos, out float yaw)) continue;
                    bool done = false;
                    if (stack.Block is BlockLantern || stack.Block.GetLightHsv(World.BlockAccessor, null, stack)?[2] > 0 && kv.Key.StartsWith("lantern"))
                        done = PlaceOnHook("h:" + kv.Key, pos, stack);
                    else
                        done = PlaceNear(pos, yaw, stack, kv.Key);
                    if (!done) continue;
                    attached.RemoveAttribute(kv.Key);
                    WatchedAttributes.MarkPathDirty("hansaFit");
                    changed = true;
                    Api.Logger.Notification("[hansa] {0} #{1}: the {2} on the old place {3} is a placed piece now", Code, EntityId, stack.Collectible.Code, kv.Key);
                }
            }
            if (changed || !hadPieces) SavePieces();
        }

        /// <summary>
        /// Pieces on places her design no longer has (a new hull; on the block grid the tilted wall spots are gone, 0.3.9):
        /// a piece that stood on a floor goes onto the nearest free floor place within 4 blocks (and a block up or down) where
        /// it fits, facing as it did if it can; what can't (a picture from a gone wall spot, a panel, no room) goes into
        /// the hold, or onto her deck if the hold is full: never lost. A container keeps what's in it, or that goes too.
        /// </summary>
        bool RescueOrphanedPieces()
        {
            // ((0.3.20) a hatch's places are her deck cells, not mount points)
            var orphans = Pieces.All.Where(p => p.Role != PieceRole.Hatch && (p.Mounts.Length == 0 || p.Mounts.Any(id => Mounts.Get(id) == null)))
                .OrderBy(p => p.Box?[1] ?? 0).ToList();
            if (orphans.Count == 0) return false;
            foreach (var p in orphans)
            {
                // (what was in it, out of its storage before that goes)
                var contents = new List<ItemStack>();
                if (Storages.TryGetValue(p.StorageCode, out var inv))
                    foreach (var s in inv)
                        if (!s.Empty) { contents.Add(s.Itemstack); s.Itemstack = null; }
                Pieces.Remove(p.Id);
                ShipPiece placed = null;
                if (p.Stack?.Block != null && p.Kind == MountKind.Floor && p.Box != null && p.Role != PieceRole.Panel)
                {
                    var c = p.Center;
                    var info = new VanillaPieceBlock(World, p.Stack);
                    var places = Mounts.Floors.Where(m => Math.Abs(m.Y - p.Box[1]) < 1.01 && !Pieces.Taken(m.Id))
                        .Select(m => (m, d: Math.Sqrt((m.CX - c[0]) * (m.CX - c[0]) + (m.CZ - c[2]) * (m.CZ - c[2])) + Math.Abs(m.Y - p.Box[1])))
                        .Where(x => x.d < 4).OrderBy(x => x.d).Select(x => x.m);
                    foreach (var m in places)
                    {
                        foreach (int dir in new[] { p.Dir, (p.Dir + 1) % 4, (p.Dir + 3) % 4, (p.Dir + 2) % 4 })
                        {
                            var plan = PiecePlanner.Plan(Mounts, m, info, dir);
                            if (plan.Error == null && PlacePiece(plan, p.Stack, false, out placed) == null) break;
                            placed = null;
                        }
                        if (placed != null) break;
                    }
                }
                if (placed != null && placed.Role == PieceRole.Storage && Storages.TryGetValue(placed.StorageCode, out var into))
                {
                    int n = 0;
                    foreach (var s in contents.ToArray())
                    {
                        while (n < into.Count && !into[n].Empty) n++;
                        if (n >= into.Count) break;
                        into[n].Itemstack = s;
                        contents.Remove(s);
                    }
                    SaveInventory(placed.StorageCode);
                }
                if (placed == null && p.Stack != null) StowInHold(p.Stack);
                foreach (var s in contents) StowInHold(s);
                Api.Logger.Notification("[hansa] {0} #{1}: the {2} on {3}, a place her design no longer has, {4}", Code, EntityId, p.Stack?.Collectible?.Code,
                    string.Join("+", p.Mounts), placed != null ? "stands on " + string.Join("+", placed.Mounts) + " now" : "went into the hold");
            }
            Changed();
            if (Storages.ContainsKey("hold")) SaveInventory("hold");
            return true;
        }

        /// <summary>
        /// (0.3.19, the user: "railings beside ladder-stairs are weirdly placed and look wrong; ladders look better without
        /// rails") Once per ship: the railings standing beside a ladder-stair (raked to it till 0.3.18; the design laid them on
        /// carrack-m) are taken off and their fences put in her hold (on her deck if it's full). True if any were.
        /// </summary>
        bool RemoveLadderRailings()
        {
            if (Mounts == null || WatchedAttributes.GetBool("hansaLadderRails")) return false;
            WatchedAttributes.SetBool("hansaLadderRails", true);
            var off = Pieces.All.Where(r => r.Role == PieceRole.Railing && PiecePlanner.BesideLadder(Mounts, Pieces.All, Mounts.Get(r.Mount)) != null).ToList();
            if (off.Count == 0) return false;
            foreach (var r in off)
            {
                Pieces.Remove(r.Id);
                if (r.Stack != null) StowInHold(r.Stack);
            }
            Api.Logger.Notification("[hansa] {0} #{1}: {2} railings beside her ladder-stairs taken off (0.3.19), their fences in her hold", Code, EntityId, off.Count);
            Changed();
            if (Storages.ContainsKey("hold")) SaveInventory("hold");
            return true;
        }

        /// <summary>The design's start pieces (lanterns on their hooks) on a new ship.</summary>
        public bool PlaceStartPieces()
        {
            bool any = false;
            foreach (var sp in Def.StartPieces ?? Array.Empty<StartPieceDef>())
            {
                var block = sp.Block == null ? null : World.GetBlock(new AssetLocation(sp.Block));
                var m = Mounts.Get(sp.Mount);
                if (block == null || block.Id == 0 || m == null) continue;
                var stack = new ItemStack(block);
                if (sp.Attributes != null)
                    foreach (var kv in sp.Attributes) stack.Attributes.SetString(kv.Key, kv.Value);
                var plan = PiecePlanner.Plan(Mounts, m, new VanillaPieceBlock(World, stack), ShipMounts.DirOfYaw(sp.Yaw));
                string err = PlacePiece(plan, stack, false, out _);
                if (err == null) any = true;
                else Api.Logger.Warning("[hansa] {0}: start piece {1} on {2}: {3}", Def.Code, sp.Block, sp.Mount, err);
            }
            return any;
        }

        /// <summary>
        /// (0.3.16, notes 154-158; docs/modular-pieces.md) The design's modular pieces not laid on her yet: each on its place
        /// if that's free, remembered as laid either way ("hansaLaid"), so what the player takes up never comes back.
        /// Returns how many were put.
        /// </summary>
        public int PlaceModularPieces()
        {
            var defs = Def?.ModularPieces;
            if (defs == null || defs.Length == 0 || Mounts == null) return 0;
            var laid = WatchedAttributes.GetTreeAttribute("hansaLaid") ?? new TreeAttribute();
            int n = 0;
            // (0.3.18) the ladder-stairs first (a railing round a well laid before its stair would stand in the well and
            // refuse it: "stair-no-well", seen on the server), then the hearth and oven, then their flue, then the rest
            static int Order(ModularPieceDef d) => d?.Kind switch { "stair" or "ladder" => 0, "hearth" or "oven" => 1, "flue" => 2, _ => 3 };
            foreach (var e in defs.OrderBy(Order))
            {
                if (e == null) continue;
                string key = e.Key;
                if (laid.HasAttribute(key)) continue;
                laid.SetBool(key, true);
                string err = PlaceModular(e, out _);
                if (err == null) n++;
                else Api.Logger.Warning("[hansa] {0}: modular {1} at {2}: {3}", Def.Code, e.Kind, key, err);
            }
            WatchedAttributes["hansaLaid"] = laid;
            WatchedAttributes.MarkPathDirty("hansaLaid");
            return n;
        }

        /// <summary>(0.3.16) One of the design's modular pieces put on its place (its block, paint, a door open). Returns an error (a lang code or a reason) or null.</summary>
        public string PlaceModular(ModularPieceDef e, out ShipPiece placed)
        {
            placed = null;
            // (0.3.20, step B) a hatch over an opening of her deck cells (docs/slots.md §5)
            if (e.Kind == "hatch") return PlaceModularHatch(e, out placed);
            var m = Mounts?.ForModular(e);
            if (m == null) return "no such place";
            // (0.3.19) an older "stair" entry (a ladder-stair's place) is laid as a ladder (MountLayout.LaidKind)
            string kind = Mounts.LaidKind(e);
            string code = kind != e.Kind && kind == "ladder" && !(e.Block ?? "").Contains(":ladder-") ? ModularPieceDef.DefaultBlock(kind) : e.BlockFor(kind);
            var block = World.GetBlock(new AssetLocation(code));
            if (block == null || block.Id == 0) return "no such block " + code;
            var stack = new ItemStack(block);
            var info = new VanillaPieceBlock(World, stack) { Rise = e.Rise, DoorInto = e.Into != null ? MountLayout.DirOf(e.Into) : -1 };
            // (0.3.18) a hearth's front, an oven's mouth: toward where the cook stands
            int toward = kind is "stair" or "ladder" ? (MountLayout.DirOf(e.Up) + 2) % 4 : kind is "hearth" or "oven" ? MountLayout.DirOf(e.Facing) : m.In;
            var plan = PiecePlanner.Plan(Mounts, m, info, toward);
            if (plan.Error == null && !ModularPieceDef.Matches(kind, plan.Role)) return "the block makes a " + plan.Role + ", not a " + kind;
            string err = PlacePiece(plan, stack, false, out placed);
            if (err != null) return err;
            bool again = false;
            if (e.Paint != null && ShipPaint.Known(e.Paint) && placed.Paintable) { placed.Paint = e.Paint; again = true; }
            if (e.Open && placed.Role == PieceRole.Door) { placed.Open = true; again = true; }
            if (again) Changed();
            return null;
        }

        /// <summary>
        /// (0.3.16, notes 154, 157) Each railing on the border along a placed stair's side (its deck, row 0) follows the
        /// stair: its rake (the treads' height at its two ends) and its boxes up to its handrail over them; flat again when
        /// the stair's gone.
        /// </summary>
        void UpdateRakes()
        {
            if (Mounts != null) PiecePlanner.ApplyRakes(Mounts, Pieces.All);
        }

        /// <summary>A lantern onto the hook of that id, else the nearest hook within 3 blocks of where it was.</summary>
        bool PlaceOnHook(string hookId, double[] pos, ItemStack stack)
        {
            var hooks = new List<MountPoint>();
            if (Mounts.Get(hookId) is MountPoint h) hooks.Add(h);
            if (pos != null)
                hooks.AddRange(Mounts.Hooks.Where(x => x.Id != hookId && Dist(x, pos) < 3).OrderBy(x => Dist(x, pos)));
            foreach (var m in hooks)
            {
                if (Pieces.Taken(m.Id)) continue;
                var plan = PiecePlanner.Plan(Mounts, m, new VanillaPieceBlock(World, stack), 1);
                if (PlacePiece(plan, stack, false, out _) == null) return true;
            }
            return false;
        }

        static double Dist(MountPoint m, double[] p) => Math.Sqrt((m.X - p[0]) * (m.X - p[0]) + (m.Y - p[1]) * (m.Y - p[1]) + (m.Z - p[2]) * (m.Z - p[2]));

        /// <summary>
        /// A container or bed from an old place: on the nearest free floor place of that deck within 4 blocks, its front the way
        /// it faced (else any way that fits); a container takes what was in the old place's storage with it.
        /// </summary>
        bool PlaceNear(double[] pos, float yaw, ItemStack stack, string oldCode)
        {
            var info = new VanillaPieceBlock(World, stack);
            int faced = ShipMounts.DirOfYaw(yaw);
            var places = Mounts.Floors.Where(m => Math.Abs(m.Y - pos[1]) < 0.3)
                .Select(m => (m, d: Math.Sqrt((m.CX - pos[0]) * (m.CX - pos[0]) + (m.CZ - pos[2]) * (m.CZ - pos[2]))))
                .Where(x => x.d < 4).OrderBy(x => x.d).Select(x => x.m);
            foreach (var m in places)
            {
                if (Pieces.Taken(m.Id)) continue;
                foreach (int dir in new[] { faced, (faced + 1) % 4, (faced + 3) % 4, (faced + 2) % 4 })
                {
                    var plan = PiecePlanner.Plan(Mounts, m, info, dir);
                    if (plan.Error != null || PlacePiece(plan, stack, false, out var placed) != null) continue;
                    // what was in the old place's storage goes with it
                    var invTree = WatchedAttributes.GetTreeAttribute("hansaInv");
                    if (placed.Role == PieceRole.Storage && invTree?.GetTreeAttribute(oldCode) is ITreeAttribute old &&
                        Storages.TryGetValue(placed.StorageCode, out var inv))
                    {
                        var from = new InventoryGeneric(Math.Max(1, old.GetInt("qslots", 64)), "hansa-old-" + oldCode, EntityId.ToString(), Api);
                        from.FromTreeAttributes(old);
                        from.AfterBlocksLoaded(World);
                        int n = 0;
                        foreach (var s in from)
                        {
                            if (s.Empty) continue;
                            while (n < inv.Count && !inv[n].Empty) n++;
                            if (n >= inv.Count) break;
                            inv[n].Itemstack = s.Itemstack;
                            s.Itemstack = null;
                        }
                        // (what didn't fit stays in the old storage: it goes into the hold, RescueOrphanedCargo)
                        if (from.Empty) invTree.RemoveAttribute(oldCode);
                        else
                        {
                            var rest = new TreeAttribute();
                            from.ToTreeAttributes(rest);
                            invTree[oldCode] = rest;
                        }
                        SaveInventory(placed.StorageCode);
                    }
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ gone: her wreck, her removal

        /// <summary>Her pieces as wreck loot: each container with what's in it (where it stood), the rest loose. take: out of her.</summary>
        public void PieceLoot(List<WreckLoot> list, WreckLoot loose, bool take)
        {
            // (0.3.14) what's in her working blocks (a forge's coal and ingot, a barrel's contents) goes with the rest (ShipBlocks)
            RealBlockLoot(loose.Items, take);
            foreach (var p in Pieces.All.ToArray())
            {
                if (p.Stack == null) continue;
                if (p.Role == PieceRole.Storage)
                {
                    var l = new WreckLoot { Fixture = p.StorageCode, Container = p.Stack, Place = new[] { p.Center[0], p.Box?[1] ?? p.Center[1], p.Center[2], ShipMounts.YawOfDir(p.Dir) } };
                    if (Storages.TryGetValue(p.StorageCode, out var inv))
                        foreach (var slot in inv)
                        {
                            if (slot?.Itemstack == null || slot.StackSize <= 0) continue;
                            l.Items.Add(slot.Itemstack);
                            if (take) slot.Itemstack = null;
                        }
                    list.Add(l);
                }
                else loose.Items.Add(p.Stack);
            }
            if (take)
            {
                Pieces.Clear();
                Changed();
            }
        }

        /// <summary>Removed for good (a command, or she died without a wreck): her pieces' items dropped where she is.</summary>
        void DropPieces()
        {
            // (0.3.14) what's in her working blocks, dropped too (ShipBlocks)
            DropRealBlocks();
            foreach (var p in Pieces.All)
                if (p.Stack != null) World.SpawnItemEntity(p.Stack, Pos.XYZ.Add(0, Def?.DeckY ?? 3, 0));
            Pieces.Clear();
        }
    }

    /// <summary>
    /// A chair, stool or bench put aboard, or a seat built into her (a "seat" fixture: the heads): sit on it (right-click);
    /// Shift, or a step (a movement key or jump, after a moment sitting), gets up beside it.
    /// </summary>
    public class PieceSeat : FixtureSeat
    {
        /// <summary>A movement key held as one sits down doesn't get them up again at once (ms).</summary>
        const long SettleMs = 600;
        long mountedAt;

        public PieceSeat(EntityShip ship, FixtureDef fixture) : base(ship, fixture, "sit:") { }
        /// <summary>
        /// Vanilla's sitting on a block's edge (the "sitflooredge" meta, animation "sitidle"): the legs hang over the front.
        /// (0.3.13, note 139) As the player's own meta has it (blended over the body as an average, the torso and legs
        /// weighted), for a creature without that meta.
        /// </summary>
        public static readonly AnimationMetaData EdgeSitAnim = EdgeSit();

        static AnimationMetaData EdgeSit()
        {
            var m = new AnimationMetaData { Code = "sitflooredge", Animation = "sitidle", BlendMode = EnumAnimationBlendMode.Average, EaseInSpeed = 2, ClientSide = true };
            m.ElementWeight = new System.Collections.Generic.Dictionary<string, float>();
            m.ElementBlendMode = new System.Collections.Generic.Dictionary<string, EnumAnimationBlendMode>();
            foreach (var el in new[] { "LowerTorso", "UpperTorso", "UpperFootR", "UpperFootL", "LowerFootR", "LowerFootL" })
            {
                m.ElementWeight[el] = 80;
                m.ElementBlendMode[el] = EnumAnimationBlendMode.Average;
            }
            return m.Init();
        }

        /// <summary>
        /// (0.3.13, note 139: sitting on the heads showed the player standing) The sitter's own edge-sitting meta, taken
        /// as they sit down. The game starts a seat's animation before the seat knows who sits on it, so it got the bare
        /// fallback, and with the same code the player's own was never put in its place; then a moment later the walking
        /// seat's idle could win over it.
        /// </summary>
        AnimationMetaData sitMeta;

        // (a chair or the heads is sat on as on a block's edge, the legs over its front (note 91), not as in a boat: the
        // boat's pose has the legs out along the bottom, into the heads' box)
        public override AnimationMetaData SuggestedAnimation => sitMeta ?? PassengerAnim("sitflooredge") ?? EdgeSitAnim;
        public override Vec3f LocalEyePos => RowSeat.SitEyes;
        public override Matrixf RenderTransform =>
            Ship.Api.Side == EnumAppSide.Client && Passenger != null && (!IsLocalPassenger || LocalThirdPerson) ? Tilted(rt) : null;

        public override void DidMount(EntityAgent entityAgent)
        {
            if (Passenger?.EntityId != entityAgent.EntityId) mountedAt = Ship.World.ElapsedMilliseconds;
            base.DidMount(entityAgent);
            // (0.3.13, note 139) sit down in the sitter's own pose: what the walking seat had going (idle, walking,
            // sneaking) stopped, the edge sit started over whatever the game began before it knew who it was
            var anims = entityAgent.Properties?.Client?.AnimationsByMetaCode;
            sitMeta = anims != null && anims.TryGetValue("sitflooredge", out var meta) ? meta : EdgeSitAnim;
            var am = entityAgent.AnimManager;
            if (am == null) return;
            foreach (string code in new[] { "idle", "walk", "sneakidle", "sneakwalk", "sprint", "climbup", "climbidle", "sitflooredge" })
                am.StopAnimation(code);
            am.StartAnimation(sitMeta);
        }

        public override void DidUnmount(EntityAgent entityAgent)
        {
            if (Passenger != null && Passenger.EntityId == entityAgent.EntityId)
            {
                entityAgent.AnimManager?.StopAnimation("sitflooredge");
                sitMeta = null;
            }
            base.DidUnmount(entityAgent);
        }

        protected override void OnControls(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            base.OnControls(action, on, ref handled);
            if (!on || action is not (EnumEntityAction.Forward or EnumEntityAction.Backward or EnumEntityAction.Left or EnumEntityAction.Right or EnumEntityAction.Jump)) return;
            if (Ship.Api.Side != EnumAppSide.Server || Passenger is not EntityAgent agent || Ship.World.ElapsedMilliseconds - mountedAt < SettleMs) return;
            Ship.Api.Event.EnqueueMainThreadTask(() => { if (agent.MountedOn == this) Ship.StandUp(agent, Fixture); }, "hansa-standup");
        }
    }
}
