using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>Where a player was when they logged out aboard a ship (<see cref="ShipLogouts"/>).</summary>
    public class ShipLogout
    {
        public long ShipId;
        /// <summary>Their place aboard (ship-local feet), and the seat they were in.</summary>
        public double LX, LY, LZ;
        public string Seat;
        /// <summary>Where they were in the world then.</summary>
        public double X, Y, Z;
        public int Dim;
    }

    /// <summary>
    /// Server (0.3.11, the user's rule for multiplayer): someone who logs out aboard a ship comes back aboard her when they
    /// log in again, wherever she has sailed meanwhile (her last-seen place on the ships' map loads her chunks, then they're
    /// put back where they were aboard). Someone of her crew who wasn't aboard when she sailed stays where they were. If
    /// she's gone meanwhile (sunk, removed), they come back at their spawn (a ship's spawn bound to her has gone back to the
    /// one from before by then, ShipSpawns), not in the open sea where she was. Kept in the save game.
    /// </summary>
    public class ShipLogouts
    {
        const string SaveKey = "ships-logouts";
        /// <summary>How long to wait for her to load where she was last seen.</summary>
        const long WaitMs = 30000;

        readonly ICoreServerAPI sapi;
        readonly HansaMod mod;
        public Dictionary<string, ShipLogout> Records = new();
        readonly Dictionary<string, long> waiting = new();

        public ShipLogouts(ICoreServerAPI sapi, HansaMod mod)
        {
            this.sapi = sapi;
            this.mod = mod;
            sapi.Event.SaveGameLoaded += Load;
            sapi.Event.GameWorldSave += Save;
            sapi.Event.PlayerDisconnect += OnDisconnect;
            // (after ShipSpawns' own join, which gives back a spawn that ended while they were away)
            sapi.Event.PlayerNowPlaying += p => sapi.Event.RegisterCallback(_ => OnJoin(p), 250);
        }

        void Load()
        {
            try
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData(SaveKey);
                Records = data == null ? new() : JsonConvert.DeserializeObject<Dictionary<string, ShipLogout>>(Encoding.UTF8.GetString(data)) ?? new();
            }
            catch (Exception e)
            {
                sapi.Logger.Warning("[ships] Could not read who logged out aboard, starting afresh: {0}", e.Message);
                Records = new();
            }
        }

        void Save() => sapi.WorldManager.SaveGame.StoreData(SaveKey, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Records)));

        /// <summary>Logging out: aboard a ship (in any of her seats, a bunk too), noted; else whatever was noted goes.</summary>
        public void OnDisconnect(IServerPlayer player)
        {
            var ep = player?.Entity;
            if (ep == null) return;
            if (ep.MountedOn is ShipSeat seat && seat.Ship?.Def != null && !seat.Ship.GoneDown)
            {
                var l = seat.LocalPos;
                Records[player.PlayerUID] = new ShipLogout
                {
                    ShipId = seat.Ship.EntityId, LX = l.X, LY = l.Y, LZ = l.Z, Seat = seat.SeatId,
                    X = ep.Pos.X, Y = ep.Pos.Y, Z = ep.Pos.Z, Dim = ep.Pos.Dimension
                };
                // ((0.1.0-dev.10) the last aboard logging out: she's stopped now, not seconds later, so the save written
                // as they leave has her at anchor, Unmanned)
                if (mod.Config.StopWhenUnmanned && !seat.Ship.Manned(ep.EntityId)) seat.Ship.StopUnmanned(false);
            }
            else Records.Remove(player.PlayerUID);
        }

        void OnJoin(IServerPlayer player)
        {
            if (player?.Entity == null || !Records.TryGetValue(player.PlayerUID, out var r)) return;
            Records.Remove(player.PlayerUID);
            if (!mod.Config.RejoinShipOnLogin) return;
            var ep = player.Entity;
            if (ep.MountedOn is ShipSeat on && on.Ship.EntityId == r.ShipId) return;      // already put back (EntityShip's own)
            var ship = sapi.World.GetEntityById(r.ShipId) as EntityShip;
            double x, y, z;
            if (ship?.Def != null && ship.Alive && !ship.GoneDown)
            {
                ship.LocalToWorld(r.LX, r.LY, r.LZ, out x, out y, out z);
            }
            else
            {
                var seen = sapi.ModLoader.GetModSystem<HelmUiSystem>()?.LastSeen(r.ShipId);
                if (seen == null || ship != null && (!ship.Alive || ship.GoneDown))
                {
                    // she's gone: back at their spawn, not in the sea where she was
                    var spawn = player.GetSpawnPosition(false);
                    if (spawn != null) ep.TeleportToDouble(spawn.X, spawn.Y, spawn.Z);
                    player.SendMessage(Vintagestory.API.Config.GlobalConstants.GeneralChatGroup,
                        Vintagestory.API.Config.Lang.GetL(player.LanguageCode, "ships:rejoin-gone"), EnumChatType.Notification);
                    return;
                }
                x = seen.X; z = seen.Z;
                y = (seen.Y != 0 ? seen.Y : ep.Pos.Y) + r.LY + 0.2;
            }
            if (ep.Pos.Dimension != r.Dim || ep.Pos.SquareDistanceTo(x, y, z) > 24 * 24) ep.TeleportToDouble(x, y, z);
            // then aboard as soon as she's loaded there
            if (waiting.TryGetValue(player.PlayerUID, out long old)) sapi.Event.UnregisterGameTickListener(old);
            long started = sapi.World.ElapsedMilliseconds, id = 0;
            id = sapi.Event.RegisterGameTickListener(_ =>
            {
                if (!PutAboard(player, r, sapi.World.ElapsedMilliseconds - started)) return;
                sapi.Event.UnregisterGameTickListener(id);
                waiting.Remove(player.PlayerUID);
            }, 250);
            waiting[player.PlayerUID] = id;
        }

        /// <summary>One try at putting them back aboard; true when it's done with (aboard, or given up).</summary>
        bool PutAboard(IServerPlayer player, ShipLogout r, long waitedMs)
        {
            var ep = player.Entity;
            if (ep == null || player.ConnectionState != EnumClientState.Playing || !ep.Alive) return true;
            if (ep.MountedOn is ShipSeat on && on.Ship.EntityId == r.ShipId) return true;
            if (sapi.World.GetEntityById(r.ShipId) is not EntityShip ship || ship.Def == null) return waitedMs > WaitMs;
            if (ship.GoneDown || !ship.MayBoard(player)) return true;
            ship.LocalToWorld(r.LX, r.LY, r.LZ, out double x, out double y, out double z);
            if (ep.Pos.Dimension != ship.Pos.Dimension || ep.Pos.SquareDistanceTo(x, y, z) > 16 * 16)
            {
                ep.TeleportToDouble(x, y, z, () => ship.Board(ep, new Vec3d(r.LX, r.LY, r.LZ)));
                return true;
            }
            ship.Board(ep, new Vec3d(r.LX, r.LY, r.LZ));
            return true;
        }
    }
}
