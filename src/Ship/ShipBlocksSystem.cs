using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// (0.3.14) Working blocks aboard (config WorkingBlocks): a forge, bellows, anvil, quern, barrel, crock or bloomery put on
    /// a floor place on her block grid is the real vanilla block, in a mini-dimension of her own that's carried with her.
    /// - Server: one dimension per ship (made when first needed; its sub-dimension id kept in her record), moved with her every
    ///   tick; its blocks (and their block entities' state) saved as a vanilla BlockSchematic in her Attributes every 20 s, on
    ///   a world save and as she unloads, pasted into a fresh dimension when she loads (the engine doesn't keep dimensions over
    ///   a restart). Its chunks are sent to players as they come within view (the engine only sends chunks that change).
    ///   Invisible solid support blocks (hansa:shipsupport) stand under them (her decks aren't blocks).
    /// - The piece stays her record of it (its places, boxes and collision for walking, the item given back); it isn't drawn.
    /// - Going down: what's in them goes into her wreck's loot with the blocks' items; removed for good: dropped where she is.
    /// - Clients draw the dimension in her drawn pose (ShipDimClient), pick its blocks (ShipBlocksClient.Pick) and hand vanilla
    ///   the block selection; the patches (BlockPatches) make vanilla's range check, block entity packets, climate, sounds,
    ///   particles, drops and block entity renderers work with blocks there.
    /// Mechanical power isn't on the list yet (the list is config WorkingBlockCodes).
    /// </summary>
    public class ShipBlocksSystem : ModSystem
    {
        public const string HarmonyId = "hansa-blocks";
        public static readonly AssetLocation SupportCode = new("hansa", "shipsupport");

        static Harmony harmony;
        static int users;
        static bool sharedPatched, serverPatched, clientPatched;

        ICoreAPI api;
        ICoreServerAPI sapi;
        ICoreClientAPI capi;
        HansaMod mod;

        /// <summary>The ship carrying each sub-dimension, per side (the patches look positions up here; any thread).</summary>
        static readonly ConcurrentDictionary<int, EntityShip> serverSubs = new(), clientSubs = new();

        public static ShipBlocksSystem ServerInstance, ClientInstance;
        public ICoreClientAPI ClientApi => capi;
        public ICoreServerAPI ServerApi => sapi;

        /// <summary>Server: a ship's dimension and its bookkeeping.</summary>
        public class ShipDim
        {
            public EntityShip Ship;
            public IMiniDimension Dim;
            public int Sub;
            public readonly HashSet<string> SentTo = new();
            public double SavedAt;
            /// <summary>(0.3.16) Per player: how many of her working blocks their client says it has (BlocksAckPacket), when her chunks were last sent to them and how often.</summary>
            public readonly Dictionary<string, BlocksAckPacket> Acked = new();
            public readonly Dictionary<string, double> LastSent = new();
            public readonly Dictionary<string, int> Sends = new();
        }

        public const string ChannelName = "hansablocks";

        readonly Dictionary<long, ShipDim> dims = new();

        public override double ExecuteOrder() => 0.25;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            mod = api.ModLoader.GetModSystem<HansaMod>();
            users++;
            harmony ??= new Harmony(HarmonyId);
            // (0.3.16) a client says how many of a ship's working blocks it has: her chunks are sent again until it has them all
            api.Network.RegisterChannel(ChannelName).RegisterMessageType<BlocksAckPacket>();
        }

        void PatchShared(ILogger logger)
        {
            if (sharedPatched) return;
            sharedPatched = true;
            BlockPatches.ApplyShared(harmony, logger);
        }

        public override void Dispose()
        {
            if (sapi != null) { ServerInstance = null; serverSubs.Clear(); }
            if (capi != null) { ClientInstance = null; clientSubs.Clear(); }
            if (--users <= 0 && harmony != null)
            {
                harmony.UnpatchAll(HarmonyId);
                harmony = null;
                sharedPatched = serverPatched = clientPatched = false;
            }
        }

        /// <summary>The ship carrying a sub-dimension: the given side's if loaded there, else the other's (single player runs both).</summary>
        public static EntityShip ShipForSub(int sub, bool client)
        {
            if (sub <= 0) return null;
            EntityShip s;
            if ((client ? clientSubs : serverSubs).TryGetValue(sub, out s) && s != null) return s;
            return (client ? serverSubs : clientSubs).TryGetValue(sub, out s) ? s : null;
        }

        /// <summary>
        /// A position in a ship's dimension (plain or internal Y) as a world position (her pose on that side): false if it
        /// isn't one. The ship and the ship-local point come too.
        /// </summary>
        public static bool ToWorld(double x, double y, double z, bool client, out double wx, out double wy, out double wz, out EntityShip ship, out double lx, out double ly, out double lz)
        {
            wx = wy = wz = lx = ly = lz = 0;
            ship = null;
            if (!ShipBlockMath.IsDimY(y)) return false;
            int sub = ShipBlockMath.SubOf((int)Math.Floor(x), (int)Math.Floor(z));
            ship = ShipForSub(sub, client);
            var frac = ship?.GridFrac;
            if (frac == null) { ship = null; return false; }
            ShipBlockMath.ToLocal(sub, frac[0], frac[1], frac[2], x, y, z, out lx, out ly, out lz);
            ship.LocalToWorld(lx, ly, lz, out wx, out wy, out wz);
            return true;
        }

        public static bool ToWorld(double x, double y, double z, bool client, out double wx, out double wy, out double wz) =>
            ToWorld(x, y, z, client, out wx, out wy, out wz, out _, out _, out _, out _);

        public static bool IsShipPos(BlockPos pos, bool client) =>
            pos != null && pos.dimension == Dimensions.MiniDimensions && ShipForSub(ShipBlockMath.SubOf(pos.X, pos.Z), client) != null;

        // ------------------------------------------------------------------ server

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            ServerInstance = this;
            api.Network.GetChannel(ChannelName).SetMessageHandler<BlocksAckPacket>(OnAck);
            api.ChatCommands.GetOrCreate("hansa")
                .BeginSubCommand("blocks")
                    .WithDescription("Working blocks aboard (diagnostics): the ship you're on (else the nearest): her record, her dimension, the blocks at her cells, her chunks and which players they were sent to")
                    .HandleWith(args => TextCommandResult.Success(ServerReport(args.Caller.Player as IServerPlayer)))
                .EndSubCommand();
            // (config WorkingBlocks off, the default for now: none of it runs, nothing is patched)
            if (!Enabled) { api.Logger.Notification("[hansa] Working blocks aboard are off (config WorkingBlocks)"); return; }
            PatchShared(api.Logger);
            if (!serverPatched) { serverPatched = true; BlockPatches.ApplyServer(harmony, api.Logger); }
            api.Event.OnEntitySpawn += e => { if (e is EntityShip s) ShipUp(s); };
            api.Event.OnEntityLoaded += e => { if (e is EntityShip s) ShipUp(s); };
            api.Event.OnEntityDespawn += (e, d) => { if (e is EntityShip s) ShipDown(s, d); };
            api.Event.GameWorldSave += () => { foreach (var d in dims.Values.ToArray()) Capture(d); };
            api.Event.DidBreakBlock += OnDidBreak;
            api.Event.CanPlaceOrBreakBlock += OnCanPlaceOrBreak;
            api.Event.RegisterGameTickListener(_ => MoveAll(), 50);
            api.Event.RegisterGameTickListener(_ => SecondTick(), 1000);
        }

        bool Enabled => mod?.Config?.WorkingBlocks != false;

        public ShipDim DimOf(EntityShip ship) => ship != null && dims.TryGetValue(ship.EntityId, out var d) ? d : null;

        void ShipUp(EntityShip ship)
        {
            if (ship.Def == null || ship.Api?.Side != EnumAppSide.Server || dims.ContainsKey(ship.EntityId)) return;
            try
            {
                ship.ReloadBlockRecord();
                var rec = ship.BlockRecord;
                if (rec.Cells.Count > 0 || rec.Supports.Count > 0) Restore(ship);
                // an older ship's inert pieces of working blocks (put before 0.3.14, or with the switch off) start working
                if (Enabled) MigratePieces(ship);
            }
            catch (Exception e) { sapi.Logger.Error("[hansa] Working blocks of {0} #{1} could not be set up: {2}", ship.GetName(), ship.EntityId, e); }
        }

        void ShipDown(EntityShip ship, EntityDespawnData d)
        {
            if (!dims.TryGetValue(ship.EntityId, out var sd)) return;
            bool gone = d?.Reason is EnumDespawnReason.Death or EnumDespawnReason.Combusted or EnumDespawnReason.PickedUp or EnumDespawnReason.Expire or EnumDespawnReason.Removed;
            // gone for good: what's in her working blocks dropped where she is (her pieces' items drop with her pieces);
            // unloading: saved with her
            if (gone) { try { DropAll(ship); } catch (Exception e) { sapi.Logger.Warning("[hansa] Dropping the contents of #{0}'s working blocks: {1}", ship.EntityId, e.Message); } }
            else Capture(sd);
            Release(sd);
        }

        /// <summary>Her dimension: made (or remade after a restart, with the same sub-dimension id when that's free) and registered.</summary>
        public ShipDim EnsureDim(EntityShip ship)
        {
            if (dims.TryGetValue(ship.EntityId, out var have)) return have;
            var rec = ship.BlockRecord;
            var dim = sapi.World.BlockAccessor.CreateMiniDimension(new Vec3d(ship.Pos.X, ship.Pos.Y, ship.Pos.Z));
            int sub = rec.Sub;
            if (sub > 0 && sapi.Server.GetMiniDimension(sub) == null) sapi.Server.SetMiniDimension(dim, sub);
            else sub = sapi.Server.LoadMiniDimension(dim);
            dim.SetSubDimensionId(sub);
            var sd = new ShipDim { Ship = ship, Dim = dim, Sub = sub, SavedAt = sapi.World.ElapsedMilliseconds / 1000.0 };
            dims[ship.EntityId] = sd;
            serverSubs[sub] = ship;
            if (rec.Sub != sub)
            {
                rec.Sub = sub;
                ship.SaveBlockRecord();
            }
            Move(sd);
            return sd;
        }

        /// <summary>Her working blocks back in her dimension from her saved schematic; false if it couldn't be read (review H3-4).</summary>
        bool Restore(EntityShip ship)
        {
            var sd = EnsureDim(ship);
            var rec = ship.BlockRecord;
            string json = ship.Attributes.GetString(EntityShip.BlocksSchematicKey);
            var origin = ship.Attributes["hansaBlockSchemAt"] as IntArrayAttribute;
            if (json == null || origin?.value == null || origin.value.Length < 3)
            {
                sapi.Logger.Warning("[hansa] {0} #{1} has working blocks but no saved schematic: placing them anew", ship.GetName(), ship.EntityId);
                foreach (var s in rec.Supports) SetSupport(sd, s[0], s[1], s[2]);
                foreach (var c in rec.Cells.OrderBy(c => c.J)) SetCellBlock(sd, c, null);
                return true;
            }
            string err = null;
            var sch = BlockSchematic.LoadFromString(json, ref err);
            if (sch == null) { sapi.Logger.Error("[hansa] {0} #{1}: working blocks' schematic unreadable: {2}", ship.GetName(), ship.EntityId, err); return false; }
            ShipBlockMath.DimPos(sd.Sub, origin.value[0], origin.value[1], origin.value[2], out int x, out int y, out int z);
            sch.PasteToMiniDimension(sapi, sapi.World.BlockAccessor, sd.Dim, new BlockPos(x, y, z, Dimensions.MiniDimensions), false);
            sapi.Logger.Notification("[hansa] {0} #{1}: {2} working block(s) back in her dimension {3}", ship.GetName(), ship.EntityId, rec.Cells.Count, sd.Sub);
            return true;
        }

        /// <summary>Her dimension's blocks into her Attributes (a vanilla schematic, with the block entities' state).</summary>
        public void Capture(ShipDim sd)
        {
            var ship = sd.Ship;
            var rec = ship.BlockRecord;
            sd.SavedAt = sapi.World.ElapsedMilliseconds / 1000.0;
            if (!rec.Bounds(out var min, out var max))
            {
                ship.Attributes.RemoveAttribute(EntityShip.BlocksSchematicKey);
                ship.Attributes.RemoveAttribute("hansaBlockSchemAt");
                return;
            }
            try
            {
                ShipBlockMath.DimPos(sd.Sub, min[0], min[1], min[2], out int x0, out int y0, out int z0);
                ShipBlockMath.DimPos(sd.Sub, max[0], max[1], max[2], out int x1, out int y1, out int z1);
                var sch = new BlockSchematic(sapi.World, sapi.World.BlockAccessor, new BlockPos(x0, y0, z0, Dimensions.MiniDimensions), new BlockPos(x1, y1, z1, Dimensions.MiniDimensions), false);
                ship.Attributes.SetString(EntityShip.BlocksSchematicKey, sch.ToJson());
                ship.Attributes["hansaBlockSchemAt"] = new IntArrayAttribute(min);
                // (her chunk is saved with her: make sure it is)
                sapi.WorldManager.GetChunk(ship.Pos.AsBlockPos)?.MarkModified();
            }
            catch (Exception e) { sapi.Logger.Error("[hansa] Could not save the working blocks of {0} #{1}: {2}", ship.GetName(), ship.EntityId, e.Message); }
        }

        /// <summary>Her dimension emptied and let go of (she unloaded or went for good).</summary>
        void Release(ShipDim sd)
        {
            dims.Remove(sd.Ship.EntityId);
            serverSubs.TryRemove(sd.Sub, out _);
            try
            {
                // (emptied with the block entities' own unloading, not broken: nothing drops)
                sd.Dim.ClearChunks();
                sd.Dim.UnloadUnusedServerChunks();
            }
            catch (Exception e) { sapi.Logger.Warning("[hansa] Clearing dimension {0}: {1}", sd.Sub, e.Message); }
            Unregister(sd.Sub);
        }

        static FieldInfo loadedDims;

        /// <summary>The engine has no way to unregister a mini-dimension: out of ServerMain.LoadedMiniDimensions by reflection.</summary>
        void Unregister(int sub)
        {
            try
            {
                loadedDims ??= AccessTools.Field(AccessTools.TypeByName("Vintagestory.Server.ServerMain"), "LoadedMiniDimensions");
                if (loadedDims?.GetValue(sapi.World) is ConcurrentDictionary<int, IMiniDimension> d) d.TryRemove(sub, out _);
            }
            catch (Exception e) { sapi.Logger.Warning("[hansa] Could not unregister dimension {0}: {1}", sub, e.Message); }
        }

        void MoveAll()
        {
            foreach (var sd in dims.Values) Move(sd);
        }

        /// <summary>The dimension where she is (the engine sends its chunks to players within view of it).</summary>
        static void Move(ShipDim sd)
        {
            var cp = sd.Dim.CurrentPos;
            var p = sd.Ship.Pos;
            cp.X = p.X; cp.Y = p.Y; cp.Z = p.Z;
            cp.Yaw = p.Yaw;
        }

        void SecondTick()
        {
            double now = sapi.World.ElapsedMilliseconds / 1000.0;
            foreach (var sd in dims.Values.ToArray())
            {
                if (!sd.Ship.Alive) continue;
                try
                {
                    Deliver(sd);
                    Reconcile(sd);
                    if (now - sd.SavedAt > 20) Capture(sd);
                }
                catch (Exception e) { sapi.Logger.Warning("[hansa] Working blocks of #{0}: {1}", sd.Ship.EntityId, e.Message); }
            }
        }

        /// <summary>
        /// The engine sends a dimension's chunks only when they change: to a player who comes within view (or joins), all of
        /// her dimension's chunks again: marked dirty (the engine's way: everyone in view gets them) and put in that player's
        /// connection's chunks to send (0.3.16: as the engine does for its own, so they go even if the dirty marking misses
        /// them). Sent again every few seconds until the player's client says it has all her working blocks (BlocksAckPacket).
        /// </summary>
        void Deliver(ShipDim sd)
        {
            double now = sapi.World.ElapsedMilliseconds / 1000.0;
            int total = sd.Ship.BlockRecord.Cells.Count;
            var inView = new HashSet<string>();
            foreach (IServerPlayer pl in sapi.World.AllOnlinePlayers)
            {
                if (pl.ConnectionState != EnumClientState.Playing || pl.Entity == null) continue;
                float vd = (pl.WorldData?.LastApprovedViewDistance ?? 128) + 16;
                double dx = pl.Entity.Pos.X - sd.Ship.Pos.X, dz = pl.Entity.Pos.Z - sd.Ship.Pos.Z;
                if (dx * dx + dz * dz > vd * vd) continue;
                string uid = pl.PlayerUID;
                inView.Add(uid);
                bool newcomer = !sd.SentTo.Contains(uid);
                sd.Acked.TryGetValue(uid, out var ack);
                bool complete = total == 0 || ack != null && ack.Sub == sd.Sub && ack.Resolved >= total && ack.Total == total;
                sd.LastSent.TryGetValue(uid, out double last);
                sd.Sends.TryGetValue(uid, out int sends);
                double wait = sends < 10 ? 3 : 30;
                // (review H1-27) a client that never has them all (a block it can't make) isn't sent them for ever: said once
                if (!newcomer && !complete && sends >= MaxSends)
                {
                    if (sends == MaxSends)
                    {
                        sd.Sends[uid] = sends + 1;
                        sapi.Logger.Warning("[hansa] working blocks: {0} #{1}'s dimension {2}: {3}'s client still hasn't all of them after {4} sends; not sent again until they come within view anew",
                            sd.Ship.GetName(), sd.Ship.EntityId, sd.Sub, pl.PlayerName, sends);
                    }
                    continue;
                }
                if (!newcomer && (complete || now - last < wait)) continue;
                string why = newcomer ? "came within view" : ack == null ? $"their client hasn't answered (send {sends + 1})"
                    : $"their client has {ack.Resolved}/{ack.Total} working block(s), {ack.Chunks} chunk(s), dimension {(ack.Registered ? "registered" : "NOT registered")} (send {sends + 1})";
                SendChunks(sd, pl, why, sends);
                sd.LastSent[uid] = now;
                sd.Sends[uid] = sends + 1;
            }
            foreach (string uid in sd.SentTo.Where(u => !inView.Contains(u)).ToArray()) { sd.Acked.Remove(uid); sd.LastSent.Remove(uid); sd.Sends.Remove(uid); }
            sd.SentTo.Clear();
            sd.SentTo.UnionWith(inView);
        }

        const int MaxSends = 20;

        void SendChunks(ShipDim sd, IServerPlayer pl, string why, int sends)
        {
            // (review H1-27: marked dirty, they go to everyone in view; sent again, they go to this player only)
            if (sends == 0) MarkAllDirty(sd);
            int forced = ForceSend(sd, pl, out string err);
            if (sends < 3 || sends % 10 == 0 || err != null)
                sapi.Logger.Notification("[hansa] working blocks: {0} #{1}'s dimension {2} sent to {3} ({4}): {5} chunk(s) queued{6}", sd.Ship.GetName(), sd.Ship.EntityId, sd.Sub,
                    pl.PlayerName, why, forced, err != null ? "; " + err : "");
        }

        void MarkAllDirty(ShipDim sd)
        {
            if (sd.Dim is not BlockAccessorMovable bam) return;
            var done = new HashSet<(int, int, int)>();
            var rec = sd.Ship.BlockRecord;
            foreach (var c in rec.Cells.Select(c => new[] { c.I, c.J, c.K }).Concat(rec.Supports))
            {
                ShipBlockMath.DimPos(sd.Sub, c[0], c[1], c[2], out int x, out int y, out int z);
                if (done.Add((x >> 5, y >> 5, z >> 5))) bam.MarkChunkDirty(x, y, z);
            }
        }

        /// <summary>Her chunks' indices (as the engine keys them: chunk Y with the dimension's 1024).</summary>
        public List<long> ChunkIndices(ShipDim sd)
        {
            var rec = sd.Ship.BlockRecord;
            var set = new HashSet<long>();
            foreach (var c in rec.Cells.Select(c => new[] { c.I, c.J, c.K }).Concat(rec.Supports))
            {
                ShipBlockMath.DimPos(sd.Sub, c[0], c[1], c[2], out int x, out int y, out int z);
                set.Add(sapi.WorldManager.ChunkIndex3D(x / 32, (y + ShipBlockMath.DimSizeY) / 32, z / 32));
            }
            return set.ToList();
        }

        static FieldInfo playerClient, forceSendChunks, chunkSent;
        static MethodInfo getLoadedChunk, markToPack;

        /// <summary>The engine's connection of a player (ServerPlayer.client, internal).</summary>
        static object ClientOf(IServerPlayer pl)
        {
            playerClient ??= AccessTools.Field(AccessTools.TypeByName("Vintagestory.Server.ServerPlayer"), "client");
            return pl == null ? null : playerClient?.GetValue(pl);
        }

        /// <summary>A loaded server chunk by index (ServerMain.GetLoadedChunk), or null.</summary>
        public object LoadedChunk(long index)
        {
            getLoadedChunk ??= AccessTools.Method(AccessTools.TypeByName("Vintagestory.Server.ServerMain"), "GetLoadedChunk", new[] { typeof(long) });
            return getLoadedChunk?.Invoke(sapi.World, new object[] { index });
        }

        /// <summary>Her chunks into a player's connection's chunks to send (ConnectedClient.forceSendChunks), packed fresh: how many.</summary>
        int ForceSend(ShipDim sd, IServerPlayer pl, out string err)
        {
            err = null;
            try
            {
                var client = ClientOf(pl);
                if (client == null) { err = "no connection for the player"; return 0; }
                forceSendChunks ??= AccessTools.Field(client.GetType(), "forceSendChunks");
                if (forceSendChunks?.GetValue(client) is not HashSet<long> set) { err = "ConnectedClient.forceSendChunks not found"; return 0; }
                int n = 0, missing = 0;
                foreach (long index in ChunkIndices(sd))
                {
                    var chunk = LoadedChunk(index);
                    if (chunk == null) { missing++; continue; }
                    markToPack ??= AccessTools.Method(chunk.GetType(), "MarkToPack");
                    markToPack?.Invoke(chunk, null);
                    set.Add(index);
                    n++;
                }
                if (missing > 0) err = $"{missing} of her chunk(s) aren't among the server's loaded chunks";
                return n;
            }
            catch (Exception e) { err = "explicit send failed: " + e.Message; return 0; }
        }

        static HashSet<long> ClientSet(IServerPlayer pl, ref FieldInfo field, string name)
        {
            try
            {
                var client = ClientOf(pl);
                if (client == null) return null;
                field ??= AccessTools.Field(client.GetType(), name);
                return field?.GetValue(client) as HashSet<long>;
            }
            catch { return null; }
        }

        void OnAck(IServerPlayer pl, BlocksAckPacket p)
        {
            var sd = dims.Values.FirstOrDefault(d => d.Ship.EntityId == p.ShipId);
            if (sd == null || pl == null) return;
            bool first = !sd.Acked.TryGetValue(pl.PlayerUID, out var was);
            sd.Acked[pl.PlayerUID] = p;
            if (first || was.Resolved != p.Resolved || was.Total != p.Total || was.Registered != p.Registered)
                sapi.Logger.Notification("[hansa] working blocks: {0}'s client has {1}/{2} of {3} #{4}'s working blocks ({5} chunk(s), dimension {6} {7})", pl.PlayerName, p.Resolved, p.Total,
                    sd.Ship.GetName(), sd.Ship.EntityId, p.Chunks, p.Sub, p.Registered ? "registered" : "NOT registered");
        }

        /// <summary>/hansa blocks: the ship the player's on (else the nearest within 100 blocks).</summary>
        string ServerReport(IServerPlayer pl)
        {
            if (!Enabled) return "Working blocks aboard are off on this server (config WorkingBlocks in ModConfig/hansa.json)";
            EntityShip ship = pl?.Entity?.MountedOn is ShipSeat seat ? seat.Ship : null;
            if (ship == null && pl?.Entity != null)
                ship = mod?.ServerShips.Where(s => s.Alive && s.Pos.DistanceTo(pl.Entity.Pos.XYZ) < 100).OrderBy(s => s.Pos.SquareDistanceTo(pl.Entity.Pos.XYZ)).FirstOrDefault();
            if (ship == null) return "No ship near.";
            var sb = new System.Text.StringBuilder();
            var rec = ship.BlockRecord;
            var sd = DimOf(ship);
            sb.AppendLine($"{ship.GetName()} #{ship.EntityId}: sub {rec.Sub}, {rec.Cells.Count} working block(s), {rec.Supports.Count} support(s); dimension " +
                (sd == null ? "NONE on the server" : $"{sd.Sub}, registered with the engine: {ReferenceEquals(sapi.Server.GetMiniDimension(sd.Sub), sd.Dim)}, at {sd.Dim.CurrentPos.X:0.0}, {sd.Dim.CurrentPos.Z:0.0}"));
            var ba = sapi.World.BlockAccessor;
            foreach (var c in rec.Cells)
            {
                var p = ship.Pieces.Get(c.Piece);
                var b = rec.Sub > 0 ? ba.GetBlock(CellPos(rec.Sub, c.I, c.J, c.K)) : null;
                var be = rec.Sub > 0 ? ba.GetBlockEntity(CellPos(rec.Sub, c.I, c.J, c.K)) : null;
                string piece = p == null ? "gone" : $"{p.Role}, {(p.Parts.Count > 0 ? "DRAWN" : "not drawn")}";
                sb.AppendLine($"  piece {c.Piece} cell {c.I},{c.J},{c.K} {c.Code}: block {(b == null || b.Id == 0 ? "AIR" : b.Code.ToString())}{(be != null ? " (" + be.GetType().Name + ")" : "")}; piece {piece}");
            }
            var frac = ship.GridFrac;
            foreach (var p in ship.Pieces.All)
            {
                if (rec.ByPiece(p.Id) != null || p.Stack?.Block is not Block wb || !IsWorking(wb)) continue;
                bool offGrid = p.Kind == MountKind.Floor && p.Parts.Count == 1 && frac != null &&
                               !ShipBlockMath.CellOf(frac[0], frac[1], frac[2], p.Parts[0].Origin[0], p.Parts[0].Origin[1], p.Parts[0].Origin[2], out _, out _, out _);
                sb.AppendLine($"  piece {p.Id} {wb.Code}: NOT a working block ({p.Kind}, {p.Parts.Count} part(s), {p.Role}{(offGrid ? ", off her block grid" : "")})");
            }
            if (sd != null)
            {
                var idx = ChunkIndices(sd);
                sb.AppendLine($"her chunks: {idx.Count}, loaded on the server: {idx.Count(i => LoadedChunk(i) != null)}");
                foreach (IServerPlayer op in sapi.World.AllOnlinePlayers)
                {
                    sd.Acked.TryGetValue(op.PlayerUID, out var a);
                    sd.Sends.TryGetValue(op.PlayerUID, out int n);
                    var sent = ClientSet(op, ref chunkSent, "ChunkSent");
                    var queued = ClientSet(op, ref forceSendChunks, "forceSendChunks");
                    string says = a == null ? "nothing yet" : $"{a.Resolved}/{a.Total} blocks, {a.Chunks} chunk(s), dimension {(a.Registered ? "registered" : "NOT registered")}";
                    sb.AppendLine($"  {op.PlayerName} ({op.ConnectionState}): in view {sd.SentTo.Contains(op.PlayerUID)}, sent {n} time(s), engine sent {(sent == null ? "?" : idx.Count(sent.Contains).ToString())}/{idx.Count} chunk(s), " +
                                  $"{(queued == null ? "?" : idx.Count(queued.Contains).ToString())} still queued; client says {says}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        static BlockPos CellPos(int sub, int i, int j, int k)
        {
            ShipBlockMath.DimPos(sub, i, j, k, out int x, out int y, out int z);
            return new BlockPos(x, y, z, Dimensions.MiniDimensions);
        }

        /// <summary>Blocks whose piece is gone (taken up, made loot) out of her dimension, with the supports nothing needs any more.</summary>
        void Reconcile(ShipDim sd)
        {
            var ship = sd.Ship;
            var rec = ship.BlockRecord;
            bool changed = false;
            foreach (var c in rec.Cells.ToArray())
            {
                if (ship.Pieces.Get(c.Piece) != null) continue;
                SetCell(sd, c.I, c.J, c.K, 0);
                rec.Cells.Remove(c);
                changed = true;
            }
            changed |= PruneSupports(sd);
            if (changed) { ship.SaveBlockRecord(); Capture(sd); }
        }

        bool PruneSupports(ShipDim sd)
        {
            var rec = sd.Ship.BlockRecord;
            bool changed = false;
            foreach (var s in rec.Supports.ToArray())
            {
                if (rec.At(s[0], s[1] + 1, s[2]) != null) continue;
                SetCell(sd, s[0], s[1], s[2], 0);
                rec.Supports.Remove(s);
                changed = true;
            }
            return changed;
        }

        BlockPos CellPos(ShipDim sd, int i, int j, int k)
        {
            ShipBlockMath.DimPos(sd.Sub, i, j, k, out int x, out int y, out int z);
            return new BlockPos(x, y, z, Dimensions.MiniDimensions);
        }

        void SetCell(ShipDim sd, int i, int j, int k, int blockId, ItemStack stack = null) => sd.Dim.SetBlock(blockId, CellPos(sd, i, j, k), stack);

        void SetSupport(ShipDim sd, int i, int j, int k)
        {
            var b = sapi.World.GetBlock(SupportCode);
            if (b != null) SetCell(sd, i, j, k, b.Id);
        }

        /// <summary>A working block's real block in the dimension (its variant, its mesh turned as the piece's was).</summary>
        Block SetCellBlock(ShipDim sd, ShipBlockCell c, ItemStack stack)
        {
            var block = sapi.World.GetBlock(new AssetLocation(c.Code));
            if (block == null || block.Id == 0) return null;
            var pos = CellPos(sd, c.I, c.J, c.K);
            ItemStack one = null;
            if (stack != null) { one = stack.Clone(); one.StackSize = 1; }
            sd.Dim.SetBlock(block.Id, pos, one);
            if (c.Quarter != 0) TurnMesh(sapi.World.BlockAccessor.GetBlockEntity(pos), c.Quarter * GameMath.PIHALF);
            return block;
        }

        /// <summary>Patch 6 (placement facing): a block entity that turns its mesh to its placer (an anvil's MeshAngle) is turned as the piece was.</summary>
        static void TurnMesh(BlockEntity be, float angle)
        {
            if (be == null) return;
            var t = be.GetType();
            var f = AccessTools.Field(t, "MeshAngle") ?? AccessTools.Field(t, "MeshAngleRad");
            if (f != null && f.FieldType == typeof(float)) { f.SetValue(be, angle); be.MarkDirty(true); return; }
            var p = AccessTools.Property(t, "MeshAngle") ?? AccessTools.Property(t, "MeshAngleRad");
            if (p != null && p.CanWrite && p.PropertyType == typeof(float)) { p.SetValue(be, angle); be.MarkDirty(true); }
        }

        /// <summary>Is this block (its code) one that works aboard (config)?</summary>
        public bool IsWorking(Block b) => b?.Code != null && b.Id != 0 && ShipBlockMath.Matches(mod?.Config?.WorkingBlockCodes, b.Code.Path) &&
                                          !VanillaPieceBlock.IsDoor(b) && b.Code.Domain == "game";

        /// <summary>
        /// A piece just put (or an older inert one): a working block on a floor place on her grid becomes the real block in
        /// her dimension, the piece only her record of it (not drawn: its parts cleared; a plain piece in role). False if it
        /// stays a piece (not a working block, not on her grid, switch off, the cell taken).
        /// </summary>
        public bool TryMakeReal(EntityShip ship, ShipPiece p, ItemStack stack, bool checkSwitch)
        {
            if (sapi == null || ship?.Def == null || p == null || !Enabled) return false;
            var block = stack?.Block ?? p.Stack?.Block;
            if (!IsWorking(block) || p.Kind != MountKind.Floor || p.Parts.Count != 1 || ship.GoneDown) return false;
            var frac = ship.GridFrac;
            if (frac == null) return false;
            var part = p.Parts[0];
            if (!ShipBlockMath.CellOf(frac[0], frac[1], frac[2], part.Origin[0], part.Origin[1], part.Origin[2], out int i, out int j, out int k)) return false;
            var rec = ship.BlockRecord;
            if (rec.ByPiece(p.Id) != null) return true;
            // (an older piece that was a storage with something in it stays as it is: its things aren't lost)
            if (p.Role == PieceRole.Storage && ship.Storages.TryGetValue(p.StorageCode, out var inv) && !inv.Empty) return false;
            if (rec.At(i, j, k) != null) return false;
            var sd = EnsureDim(ship);
            var cell = new ShipBlockCell { Piece = p.Id, I = i, J = j, K = k, Code = part.Block, Quarter = part.Quarter };
            // (a support block where it stands is replaced by it)
            rec.Supports.RemoveAll(s => s[0] == i && s[1] == j && s[2] == k);
            // something solid under it first (her decks aren't blocks; an anvil or a chimney falls the moment it's put
            // on nothing): a support, unless a working block stands there
            bool newSupport = rec.At(i, j - 1, k) == null && !rec.IsSupport(i, j - 1, k);
            if (newSupport) SetSupport(sd, i, j - 1, k);
            if (SetCellBlock(sd, cell, stack ?? p.Stack) == null)
            {
                if (newSupport) SetCell(sd, i, j - 1, k, 0);
                return false;
            }
            rec.Cells.Add(cell);
            if (newSupport) rec.Supports.Add(new[] { i, j - 1, k });
            ship.SaveBlockRecord();
            // the piece: not drawn (the block is), a plain piece (no storage, seat or light of its own)
            p.Parts.Clear();
            p.Role = PieceRole.Deco;
            ship.BlocksChangedPieces();
            Capture(sd);
            sapi.Logger.Audit("[hansa] {0} on {1} #{2} works aboard: {3} at cell {4},{5},{6} of dimension {7}", p.Stack?.Collectible?.Code, ship.GetName(), ship.EntityId, cell.Code, i, j, k, sd.Sub);
            return true;
        }

        /// <summary>An older ship's inert pieces of working blocks become real (the user's forges and anvils put in 0.3.13).</summary>
        void MigratePieces(EntityShip ship)
        {
            int n = 0;
            foreach (var p in ship.Pieces.All.ToArray())
            {
                if (ship.IsWorkingPiece(p.Id) || p.Stack?.Block == null || !IsWorking(p.Stack.Block)) continue;
                if (TryMakeReal(ship, p, p.Stack, false)) n++;
            }
            if (n > 0) sapi.Logger.Notification("[hansa] {0} #{1}: {2} piece(s) put before working blocks now work aboard", ship.GetName(), ship.EntityId, n);
        }

        /// <summary>What's in a working block's block entity (its inventory), or none.</summary>
        static IEnumerable<ItemSlot> Contents(BlockEntity be)
        {
            if (be is IBlockEntityContainer bec && bec.Inventory != null) foreach (var s in bec.Inventory) if (s != null && !s.Empty) yield return s;
            if (be is BlockEntityAnvil anvil && anvil.WorkItemStack != null) yield return new DummySlot(anvil.WorkItemStack);
        }

        /// <summary>A working piece taken up (Hansa's take-up or a command): refused while something's in it; its block goes right after.</summary>
        public string RemovingPiece(EntityShip ship, ShipPiece p, IServerPlayer player)
        {
            var sd = DimOf(ship);
            var c = ship.BlockRecord.ByPiece(p.Id);
            // (review H2-5) the switch turned off since she got it: its block is still in her saved schematic, made again to
            // look in it (as for her loot), and let go of again; refused while something's in it, as with the switch on
            // (review H3-4) not known to be empty (her schematic unreadable, or looking failed): refused all the same; the
            // dimension made for the look let go of whatever happens
            if (sd == null && c != null && sapi != null)
            {
                bool empty = false;
                try
                {
                    if (Restore(ship) && DimOf(ship) is ShipDim borrowed)
                        empty = !Contents(sapi.World.BlockAccessor.GetBlockEntity(CellPos(borrowed, c.I, c.J, c.K))).Any();
                }
                catch (Exception e) { sapi.Logger.Warning("[hansa] Working block of #{0} looked into: {1}", ship.EntityId, e.Message); }
                finally
                {
                    if (DimOf(ship) is ShipDim made) Release(made);
                }
                return empty ? null : "hansa:piece-not-empty";
            }
            if (sd != null && c != null)
            {
                var be = sapi.World.BlockAccessor.GetBlockEntity(CellPos(sd, c.I, c.J, c.K));
                if (Contents(be).Any()) return "hansa:piece-not-empty";
                sapi.Event.RegisterCallback(_ => { if (dims.ContainsKey(ship.EntityId)) Reconcile(sd); }, 1);
            }
            return null;
        }

        /// <summary>Her working blocks broken up for her wreck's loot: what's in them (the block items are her pieces' and go with them).</summary>
        public void Loot(EntityShip ship, List<ItemStack> into, bool take)
        {
            var sd = DimOf(ship);
            // (review H1-12) the switch turned off since she got them: they're still in her saved schematic, made again for
            // this so what's in them isn't lost with her, and let go of again after
            bool borrowed = false;
            if (sd == null && sapi != null && ship.BlockRecord.Cells.Count > 0)
            {
                try { Restore(ship); }
                catch (Exception e) { sapi.Logger.Warning("[hansa] Working blocks of #{0} for her loot: {1}", ship.EntityId, e.Message); }
                sd = DimOf(ship);
                borrowed = sd != null;
            }
            if (sd == null) return;
            // (review H3-4: a dimension made for this let go of whatever happens)
            try
            {
                foreach (var c in ship.BlockRecord.Cells)
                {
                    var be = sapi.World.BlockAccessor.GetBlockEntity(CellPos(sd, c.I, c.J, c.K));
                    foreach (var slot in Contents(be).ToArray())
                    {
                        into.Add(slot.Itemstack.Clone());
                        if (take && slot is not DummySlot) { slot.Itemstack = null; slot.MarkDirty(); }
                    }
                    if (take && be is BlockEntityAnvil anvil && anvil.WorkItemStack != null) ClearAnvil(anvil);
                }
                if (take) ClearAll(sd);
            }
            finally
            {
                if (borrowed) Release(sd);
            }
        }

        /// <summary>Her working blocks' contents dropped where she is (she's removed for good); the dimension emptied.</summary>
        public void DropAll(EntityShip ship)
        {
            var at = ship.Pos.XYZ.Add(0, ship.Def?.DeckY ?? 3, 0);
            var list = new List<ItemStack>();
            Loot(ship, list, true);
            foreach (var s in list) sapi.World.SpawnItemEntity(s, at);
        }

        static void ClearAnvil(BlockEntityAnvil anvil)
        {
            var f = AccessTools.Field(typeof(BlockEntityAnvil), "workItemStack");
            f?.SetValue(anvil, null);
        }

        void ClearAll(ShipDim sd)
        {
            var rec = sd.Ship.BlockRecord;
            foreach (var c in rec.Cells) SetCell(sd, c.I, c.J, c.K, 0);
            foreach (var s in rec.Supports) SetCell(sd, s[0], s[1], s[2], 0);
            rec.Cells.Clear();
            rec.Supports.Clear();
            sd.Ship.SaveBlockRecord();
            Capture(sd);
        }

        /// <summary>A working block broken the vanilla way (mined aboard): its piece goes too (vanilla dropped the item and what was in it).</summary>
        void OnDidBreak(IServerPlayer byPlayer, int oldBlockId, BlockSelection blockSel)
        {
            var pos = blockSel?.Position;
            if (pos == null || pos.dimension != Dimensions.MiniDimensions) return;
            int sub = ShipBlockMath.SubOf(pos.X, pos.Z);
            var sd = dims.Values.FirstOrDefault(d => d.Sub == sub);
            if (sd == null) return;
            ShipBlockMath.CellOfDim(sub, pos.X, pos.Y, pos.Z, out int i, out int j, out int k);
            var rec = sd.Ship.BlockRecord;
            var c = rec.At(i, j, k);
            if (c == null) return;
            rec.Cells.Remove(c);
            if (sd.Ship.Pieces.Remove(c.Piece)) sd.Ship.BlocksChangedPieces();
            PruneSupports(sd);
            sd.Ship.SaveBlockRecord();
            Capture(sd);
        }

        /// <summary>
        /// In a ship's dimension nothing is placed the vanilla way (only Hansa's placing puts blocks there), and only a
        /// working block is broken (not her supports, nor one something stands on).
        /// </summary>
        bool OnCanPlaceOrBreak(IServerPlayer byPlayer, BlockSelection blockSel, out string claimant)
        {
            claimant = null;
            var pos = blockSel?.Position;
            if (pos == null || pos.dimension != Dimensions.MiniDimensions) return true;
            int sub = ShipBlockMath.SubOf(pos.X, pos.Z);
            var sd = dims.Values.FirstOrDefault(d => d.Sub == sub);
            if (sd == null) return true;
            ShipBlockMath.CellOfDim(sub, pos.X, pos.Y, pos.Z, out int i, out int j, out int k);
            var c = sd.Ship.BlockRecord.At(i, j, k);
            var p = c != null ? sd.Ship.Pieces.Get(c.Piece) : null;
            // (review H1-11) only her crew break her working blocks up (and take what's in them)
            if (p != null && byPlayer != null && !sd.Ship.MayUse(byPlayer))
            {
                claimant = Lang.GetL(byPlayer.LanguageCode, sd.Ship.LaidUp ? "hansa:laidup-closed" : "hansa:crew-only", sd.Ship.GetName());
                return false;
            }
            if (p != null && sd.Ship.Pieces.StandingOn(sd.Ship.Mounts, p) == null && !sd.Ship.GoneDown) return true;
            claimant = Lang.GetL(byPlayer?.LanguageCode ?? "en", p != null ? "hansa:piece-under" : "hansa:ship-in-the-way");
            return false;
        }

        /// <summary>The self-test: her dimension saved, let go of and made again from what was saved (as after a restart).</summary>
        public void TestReload(EntityShip ship)
        {
            if (!dims.TryGetValue(ship.EntityId, out var sd)) return;
            Capture(sd);
            Release(sd);
            ship.ReloadBlockRecord();
            ShipUp(ship);
        }

        /// <summary>The self-test (0.3.16): an older ship's inert pieces made working as when she loads.</summary>
        public void TestMigrate(EntityShip ship) => MigratePieces(ship);

        // ------------------------------------------------------------------ client

        IClientNetworkChannel clientChannel;
        /// <summary>Client: the ships with a record of working blocks, and what was last told the server of each.</summary>
        readonly Dictionary<long, EntityShip> clientShips = new();
        readonly Dictionary<long, (int Resolved, int Total, int Chunks, bool Registered, double At)> told = new();

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            ClientInstance = this;
            // (the client can't know the server's switch: its patches do nothing until a ship has working blocks)
            PatchShared(api.Logger);
            if (!clientPatched) { clientPatched = true; BlockPatches.ApplyClient(harmony, api.Logger); }
            clientChannel = api.Network.GetChannel(ChannelName);
            api.Event.OnEntitySpawn += e => { if (e is EntityShip s) ClientShipUp(s); };
            api.Event.OnEntityLoaded += e => { if (e is EntityShip s) ClientShipUp(s); };
            api.Event.OnEntityDespawn += (e, d) => { if (e is EntityShip s) ClientShipDown(s); };
            api.Event.RegisterGameTickListener(_ => ClientTick(), 1000);
            // (after every mod's StartClientSide: .hansa is ShipClientSystem's)
            api.Event.LevelFinalize += () =>
            {
                try
                {
                    api.ChatCommands.GetOrCreate("hansa")
                        .BeginSubCommand("blocks")
                            .WithDescription("Working blocks aboard (diagnostics): what this client has of the working blocks of the ship in the crosshair (else the nearest)")
                            .HandleWith(_ => TextCommandResult.Success(ClientReport()))
                        .EndSubCommand();
                }
                catch (Exception e) { api.Logger.Warning("[hansa] Could not add .hansa blocks: {0}", e.Message); }
            };
        }

        readonly HashSet<long> listening = new();

        void ClientShipUp(EntityShip ship)
        {
            if (ship.Api?.Side != EnumAppSide.Client) return;
            if (listening.Add(ship.EntityId))
                ship.WatchedAttributes.RegisterModifiedListener(EntityShip.BlocksKey, () =>
                {
                    ship.ReloadBlockRecord();
                    LogRecord(ship, "changed");
                    ClientRegister(ship);
                });
            ship.ReloadBlockRecord();
            if (ship.BlockRecord.Cells.Count > 0 || ship.BlockRecord.Sub > 0) LogRecord(ship, "came with her");
            ClientRegister(ship);
        }

        void LogRecord(EntityShip ship, string how)
        {
            var rec = ship.BlockRecord;
            capi.Logger.Notification("[hansa] working blocks: {0} #{1}'s record {2}: sub {3}, {4} working block(s) ({5}), {6} support(s)", ship.GetName(), ship.EntityId, how, rec.Sub,
                rec.Cells.Count, string.Join(", ", rec.Cells.Select(c => $"piece {c.Piece} {c.Code} at {c.I},{c.J},{c.K}")), rec.Supports.Count);
        }

        void ClientShipDown(EntityShip ship)
        {
            listening.Remove(ship.EntityId);
            clientShips.Remove(ship.EntityId);
            told.Remove(ship.EntityId);
            int sub = ship.BlockRecord.Sub;
            if (sub > 0 && clientSubs.TryGetValue(sub, out var s) && s == ship)
            {
                clientSubs.TryRemove(sub, out _);
                if (capi.World.MiniDimensions.TryGetValue(sub, out var d) && d is ShipDimClient sdc) sdc.Ship = null;
            }
        }

        void ClientRegister(EntityShip ship)
        {
            int sub = ship.BlockRecord.Sub;
            if (sub <= 0) return;
            clientShips[ship.EntityId] = ship;
            clientSubs[sub] = ship;
            try { ShipDimClient.Register(capi, ship, sub); }
            catch (Exception e) { capi.Logger.Warning("[hansa] working blocks: could not set up dimension {0} of {1} #{2} on this client: {3}", sub, ship.GetName(), ship.EntityId, e); }
        }

        /// <summary>
        /// Client, each second: for each ship with working blocks, how many of them this client has (their chunks come from
        /// the server); told the server when it changes, and every 3 s while some are missing (it sends her chunks again).
        /// </summary>
        void ClientTick()
        {
            if (clientShips.Count == 0) return;
            double now = capi.World.ElapsedMilliseconds / 1000.0;
            foreach (var ship in clientShips.Values.ToArray())
            {
                try
                {
                    var rec = ship.BlockRecord;
                    if (!ship.Alive || rec.Sub <= 0) continue;
                    int total = rec.Cells.Count, resolved = ShipBlocksClient.Resolved(capi, ship), chunks = ShipBlocksClient.ChunksHere(capi, rec);
                    bool registered = capi.World.MiniDimensions.TryGetValue(rec.Sub, out var d) && d is ShipDimClient;
                    told.TryGetValue(ship.EntityId, out var was);
                    bool changed = was.At == 0 || was.Resolved != resolved || was.Total != total || was.Chunks != chunks || was.Registered != registered;
                    if (changed)
                        capi.Logger.Notification("[hansa] working blocks: this client has {0}/{1} of {2} #{3}'s working blocks ({4} of {5} chunk(s); dimension {6} {7})", resolved, total, ship.GetName(),
                            ship.EntityId, chunks, ShipBlocksClient.ChunkKeys(rec).Count, rec.Sub, registered ? "registered" : "NOT registered");
                    if (!changed && (resolved >= total || now - was.At < 3)) continue;
                    if (clientChannel?.Connected == true)
                        clientChannel.SendPacket(new BlocksAckPacket { ShipId = ship.EntityId, Sub = rec.Sub, Resolved = resolved, Total = total, Chunks = chunks, Registered = registered });
                    told[ship.EntityId] = (resolved, total, chunks, registered, now);
                }
                catch (Exception e) { capi.Logger.Warning("[hansa] working blocks: checking #{0}: {1}", ship.EntityId, e.Message); }
            }
        }

        /// <summary>.hansa blocks: the ship in the crosshair, else the nearest (within 100 blocks).</summary>
        string ClientReport()
        {
            var ep = capi.World.Player?.Entity;
            var ship = capi.World.Player?.CurrentEntitySelection?.Entity as EntityShip;
            if (ship == null && capi.World.Player?.CurrentBlockSelection?.Position is BlockPos bp && bp.dimension == Dimensions.MiniDimensions)
                ship = ShipForSub(ShipBlockMath.SubOf(bp.X, bp.Z), true);
            if (ship == null && ep != null)
                ship = (ShipClientSystem.Instance?.Ships ?? clientShips.Values.ToList()).Where(s => s.Alive && s.Pos.DistanceTo(ep.Pos.XYZ) < 100)
                    .OrderBy(s => s.Pos.SquareDistanceTo(ep.Pos.XYZ)).FirstOrDefault();
            if (ship == null) return "No ship near.";
            string text = ShipBlocksClient.Report(capi, ship);
            capi.Logger.Notification("[hansa] working blocks (.hansa blocks):\n" + text);
            return text;
        }

        /// <summary>Client: the block in the crosshair is one of a ship's working blocks (clicks are vanilla's then).</summary>
        public static bool ClientOwnsClick(ICoreClientAPI capi) => IsShipPos(capi?.World?.Player?.CurrentBlockSelection?.Position, true);
    }

    /// <summary>(0.3.16) Client → server: how many of a ship's working blocks this client has (her chunks are sent again until all).</summary>
    [ProtoBuf.ProtoContract]
    public class BlocksAckPacket
    {
        [ProtoBuf.ProtoMember(1)] public long ShipId;
        [ProtoBuf.ProtoMember(2)] public int Sub;
        [ProtoBuf.ProtoMember(3)] public int Resolved;
        [ProtoBuf.ProtoMember(4)] public int Total;
        [ProtoBuf.ProtoMember(5)] public int Chunks;
        [ProtoBuf.ProtoMember(6)] public bool Registered;
    }
}
