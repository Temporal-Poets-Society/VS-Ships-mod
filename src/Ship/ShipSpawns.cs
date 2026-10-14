using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>Everyone's spawn bound to a ship, saved with the world (<see cref="ShipSpawns"/>).</summary>
    public class ShipSpawnRegistry
    {
        /// <summary>By player uid.</summary>
        public Dictionary<string, ShipSpawnBinding> Players = new();
        /// <summary>Where each ship's spawn spot was last seen (a world block), by her entity id.</summary>
        public Dictionary<long, ShipSpawnSpot> Ships = new();
    }

    public class ShipSpawnBinding
    {
        public long ShipId;
        /// <summary>The plate's fixture: its Spawn is where they come back.</summary>
        public string Plate;
        public string PlayerName;
        /// <summary>The spawn of their own they had before (a temporal gear's), given back when it ends; null: the world's.</summary>
        public PlayerSpawnPos Previous;
        /// <summary>The spawn position last given to vanilla (so one they set since, with a temporal gear, is noticed).</summary>
        public int SetX, SetY, SetZ;
        public bool HasSet;
        /// <summary>
        /// Null while it holds; else why it ended (<see cref="ShipSpawns.EndGone"/>...), while they were away: their old
        /// spawn comes back when they next join or respawn.
        /// </summary>
        public string Ended;
    }

    public class ShipSpawnSpot
    {
        public int X, Y, Z;
        public float Yaw;
    }

    /// <summary>
    /// Whose spawn it is, as far as <see cref="ShipSpawns"/> needs a player: vanilla's player (<see cref="PlayerHolder"/>),
    /// or the self-test's stand-in (its server has nobody on it, and IServerPlayer can't be made outside the game).
    /// </summary>
    public interface ISpawnHolder
    {
        string Uid { get; }
        string Name { get; }
        /// <summary>The real player (null for a stand-in): putting them aboard after a respawn.</summary>
        IServerPlayer Player { get; }
        void SetSpawn(PlayerSpawnPos pos);
        void ClearSpawn();
        /// <summary>Their own spawn position now (null: none); false if it can't be read.</summary>
        bool TryReadSpawn(out PlayerSpawnPos pos);
        void Tell(string langCode, params object[] args);
    }

    /// <summary>A real player's spawn: vanilla's IServerPlayer.</summary>
    public class PlayerHolder : ISpawnHolder
    {
        public PlayerHolder(IServerPlayer player) { Player = player; }
        public IServerPlayer Player { get; }
        public string Uid => Player.PlayerUID;
        public string Name => Player.PlayerName;
        public void SetSpawn(PlayerSpawnPos pos) => Player.SetSpawnPosition(pos);
        public void ClearSpawn() => Player.ClearSpawnPosition();

        public void Tell(string langCode, params object[] args)
        {
            Player.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(Player.LanguageCode, langCode, args), EnumChatType.Notification);
        }

        /// <summary>Vanilla keeps a player's own spawn position out of the API (ServerWorldPlayerData.SpawnPosition): by reflection.</summary>
        public bool TryReadSpawn(out PlayerSpawnPos pos)
        {
            pos = null;
            var wd = Player?.WorldData;
            if (wd == null) return false;
            try
            {
                var prop = wd.GetType().GetProperty("SpawnPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null || prop.PropertyType != typeof(PlayerSpawnPos)) return false;
                pos = prop.GetValue(wd) as PlayerSpawnPos;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Server: spawn points on ships (config ShipSpawn). With a metal plate up on the wall by her helm, anyone who
    /// right-clicks it with an empty hand has their spawn bound to her (again: unbound). Vanilla has one spawn position a
    /// player comes back to (a temporal gear sets it); while it's bound to a ship, it's kept at the spot by the plate as
    /// she sails (every couple of seconds, when she's moved a couple of blocks), so vanilla's respawn teleport loads the
    /// chunks where she is; right after it (mods' PlayerRespawn runs after the server's own handler) the player is put
    /// aboard there, once she has loaded too. When she's gone (removed, picked up, later sunk) or the plate is taken
    /// down, everyone bound to her gets the spawn they had before back: at once if they're online, else when they next
    /// join. Kept in the save game (not in each player's data: someone offline has to be found when she's gone).
    /// </summary>
    public class ShipSpawns
    {
        public const string EndGone = "gone", EndPlate = "plate", EndOff = "off", EndMissing = "missing", EndReplaced = "replaced", EndSunk = "sunk", EndCrew = "crew";
        const string SaveKey = "hansaShipSpawns";
        /// <summary>How far she moves before vanilla's spawn position is moved after her (blocks).</summary>
        public const double FollowDistance = 2.0;
        /// <summary>How long after a respawn to wait for her to load before giving up on her.</summary>
        public const long BoardWaitMs = 20000;

        readonly ICoreServerAPI sapi;
        readonly HansaMod mod;
        public ShipSpawnRegistry Registry = new();
        /// <summary>Someone by uid if they're online, else null (the self-test adds its stand-in).</summary>
        public System.Func<string, ISpawnHolder> FindPlayer;
        readonly Dictionary<string, long> boarding = new();

        public ShipSpawns(ICoreServerAPI sapi, HansaMod mod)
        {
            this.sapi = sapi;
            this.mod = mod;
            FindPlayer = uid => sapi.World.PlayerByUid(uid) is IServerPlayer p && p.ConnectionState == EnumClientState.Playing ? new PlayerHolder(p) : null;
            sapi.Event.SaveGameLoaded += Load;
            sapi.Event.GameWorldSave += Save;
            sapi.Event.PlayerJoin += p => OnJoin(new PlayerHolder(p));
            sapi.Event.PlayerRespawn += OnRespawn;
            sapi.Event.RegisterGameTickListener(_ => Tick(), 2000);
        }

        // ------------------------------------------------------------------ saving

        void Load()
        {
            try
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData(SaveKey);
                Registry = data == null ? new ShipSpawnRegistry() : JsonConvert.DeserializeObject<ShipSpawnRegistry>(Encoding.UTF8.GetString(data)) ?? new ShipSpawnRegistry();
            }
            catch (Exception e)
            {
                sapi.Logger.Warning("[hansa] Could not read the ship spawns, starting afresh: {0}", e.Message);
                Registry = new ShipSpawnRegistry();
            }
            Registry.Players ??= new();
            Registry.Ships ??= new();
            // switched off: everyone gets their own spawn back as they come in
            if (!mod.Config.ShipSpawn) foreach (var b in Registry.Players.Values) b.Ended ??= EndOff;
        }

        void Save() => sapi.WorldManager.SaveGame.StoreData(SaveKey, Serialize());

        public byte[] Serialize() => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Registry));

        // ------------------------------------------------------------------ binding

        /// <summary>The plate, right-clicked with an empty hand: bind the player's spawn to her, or unbind it.</summary>
        public void Toggle(IServerPlayer player, EntityShip ship, FixtureDef plate) => Toggle(new PlayerHolder(player), ship, plate);

        public void Toggle(ISpawnHolder who, EntityShip ship, FixtureDef plate)
        {
            if (!mod.Config.ShipSpawn) { who.Tell("hansa:spawn-off"); return; }
            if (plate?.Spawn == null || ship?.Def == null) return;
            Registry.Players.TryGetValue(who.Uid, out var b);
            if (b != null && b.Ended == null && b.ShipId == ship.EntityId)
            {
                End(who, b, null);
                who.Tell("hansa:spawn-unbound", ship.GetName());
                return;
            }
            if (b != null && b.Ended != null)
            {
                // ended while they were away and not given back yet
                Restore(who, b);
                b = null;
            }
            // bound to another ship: that one ends, the spawn of their own from before stays the one to give back
            var previous = b != null ? b.Previous : who.TryReadSpawn(out var now) ? Copy(now) : null;
            if (b != null) FindShip(b.ShipId)?.SetSpawnBound(who.Uid, null, false);
            b = new ShipSpawnBinding { ShipId = ship.EntityId, Plate = plate.Code, PlayerName = who.Name, Previous = previous };
            Registry.Players[who.Uid] = b;
            ship.SetSpawnBound(who.Uid, who.Name, true);
            Give(who, b, NoteShip(ship, true));
            who.Tell("hansa:spawn-bound", ship.GetName());
        }

        /// <summary>
        /// Every spawn bound to her ends (she's gone, or the plate was taken down): given back at once to those online,
        /// the others when they next join.
        /// </summary>
        public void EndShip(long shipId, string why)
        {
            foreach (var kv in Registry.Players.ToArray())
            {
                var b = kv.Value;
                if (b.ShipId != shipId || b.Ended != null) continue;
                var who = FindPlayer(kv.Key);
                if (who == null)
                {
                    b.Ended = why;
                    FindShip(shipId)?.SetSpawnBound(kv.Key, null, false);
                    continue;
                }
                End(who, b, why);
            }
            if (!Registry.Players.Values.Any(b => b.ShipId == shipId && b.Ended == null)) Registry.Ships.Remove(shipId);
        }

        /// <summary>
        /// One player's spawn bound to this ship ends (taken off her crew list, ShipCrew): their spawn from before back at
        /// once if they're online, else when they next join.
        /// </summary>
        public void EndPlayerOnShip(string uid, long shipId, string why)
        {
            if (uid == null || !Registry.Players.TryGetValue(uid, out var b) || b.ShipId != shipId || b.Ended != null) return;
            var who = FindPlayer(uid);
            if (who == null)
            {
                b.Ended = why;
                FindShip(shipId)?.SetSpawnBound(uid, null, false);
            }
            else End(who, b, why);
            if (!Registry.Players.Values.Any(x => x.ShipId == shipId && x.Ended == null)) Registry.Ships.Remove(shipId);
        }

        /// <summary>Their binding ends now: their own spawn back (unless they set another since), and why, in chat.</summary>
        void End(ISpawnHolder who, ShipSpawnBinding b, string why)
        {
            Restore(who, b);
            Registry.Players.Remove(who.Uid);
            FindShip(b.ShipId)?.SetSpawnBound(who.Uid, null, false);
            if (why != null) who.Tell("hansa:spawn-ended-" + why);
        }

        /// <summary>Give back the spawn they had before, if the one they have now is still hers.</summary>
        static void Restore(ISpawnHolder who, ShipSpawnBinding b)
        {
            if (b.HasSet && who.TryReadSpawn(out var now) && !IsOurs(now, b)) return;
            if (b.Previous != null) who.SetSpawn(Copy(b.Previous));
            else who.ClearSpawn();
        }

        static bool IsOurs(PlayerSpawnPos p, ShipSpawnBinding b) => p != null && p.x == b.SetX && p.y == b.SetY && p.z == b.SetZ;

        /// <summary>Since it was bound they've set a spawn of their own elsewhere (a temporal gear): the binding ends there.</summary>
        bool Replaced(ISpawnHolder who, ShipSpawnBinding b)
        {
            if (!b.HasSet || !who.TryReadSpawn(out var now) || IsOurs(now, b)) return false;
            Registry.Players.Remove(who.Uid);
            FindShip(b.ShipId)?.SetSpawnBound(who.Uid, null, false);
            who.Tell("hansa:spawn-ended-" + EndReplaced);
            return true;
        }

        static void Give(ISpawnHolder who, ShipSpawnBinding b, ShipSpawnSpot spot)
        {
            if (spot == null) return;
            who.SetSpawn(new PlayerSpawnPos(spot.X, spot.Y, spot.Z) { yaw = spot.Yaw, RemainingUses = -1 });
            b.SetX = spot.X;
            b.SetY = spot.Y;
            b.SetZ = spot.Z;
            b.HasSet = true;
        }

        // ------------------------------------------------------------------ following her

        /// <summary>
        /// Where her spawn spot is now, noted when she's moved more than <see cref="FollowDistance"/> since (or at once:
        /// force). Null if she has no plate spot.
        /// </summary>
        public ShipSpawnSpot NoteShip(EntityShip ship, bool force)
        {
            var plate = PlateOf(ship);
            if (plate?.Spawn == null) return null;
            double baseY = double.IsNaN(ship.BaseY) ? ship.Pos.Y : ship.BaseY;
            ShipFittings.SpawnBlock(plate.Spawn, ship.Pos.X, baseY, ship.Pos.Z, ship.Pos.Yaw, out int x, out int y, out int z);
            if (!force && Registry.Ships.TryGetValue(ship.EntityId, out var was))
            {
                double dx = x - was.X, dz = z - was.Z;
                if (dx * dx + dz * dz <= FollowDistance * FollowDistance && y == was.Y) return was;
            }
            var spot = new ShipSpawnSpot { X = x, Y = y, Z = z, Yaw = ship.Pos.Yaw };
            Registry.Ships[ship.EntityId] = spot;
            return spot;
        }

        /// <summary>Every 2 seconds: note where the ships with spawns bound to them are, and move vanilla's spawn after them.</summary>
        public void Tick()
        {
            if (!mod.Config.ShipSpawn || Registry.Players.Count == 0) return;
            foreach (long id in Registry.Players.Values.Where(b => b.Ended == null).Select(b => b.ShipId).Distinct().ToArray())
            {
                if (FindShip(id) is EntityShip ship && ship.Def != null) NoteShip(ship, false);
            }
            foreach (var kv in Registry.Players.ToArray())
            {
                var b = kv.Value;
                if (b.Ended != null || !Registry.Ships.TryGetValue(b.ShipId, out var spot)) continue;
                if (b.HasSet && spot.X == b.SetX && spot.Y == b.SetY && spot.Z == b.SetZ) continue;
                var who = FindPlayer(kv.Key);
                if (who == null || Replaced(who, b)) continue;
                Give(who, b, spot);
            }
        }

        // ------------------------------------------------------------------ joining and respawning

        public void OnJoin(ISpawnHolder who)
        {
            if (who == null || !Registry.Players.TryGetValue(who.Uid, out var b)) return;
            if (b.Ended == null && !mod.Config.ShipSpawn) b.Ended = EndOff;
            if (b.Ended != null)
            {
                // it ended while they were away
                string why = b.Ended;
                Restore(who, b);
                Registry.Players.Remove(who.Uid);
                who.Tell("hansa:spawn-ended-" + why);
                return;
            }
            // she may have sailed on while they were away (someone else at the helm)
            if (Registry.Ships.TryGetValue(b.ShipId, out var spot) && !(b.HasSet && spot.X == b.SetX && spot.Y == b.SetY && spot.Z == b.SetZ) &&
                !Replaced(who, b)) Give(who, b, spot);
        }

        /// <summary>
        /// Right after vanilla's respawn teleport has started (to her spot, which it loads): once they're alive there and
        /// she has loaded, put them aboard at the spot. If she doesn't turn up, their own spawn comes back and they go
        /// there.
        /// </summary>
        void OnRespawn(IServerPlayer player)
        {
            if (player == null || !Registry.Players.TryGetValue(player.PlayerUID, out var b)) return;
            var who = new PlayerHolder(player);
            if (b.Ended != null || !mod.Config.ShipSpawn)
            {
                OnJoin(who);
                return;
            }
            string uid = player.PlayerUID;
            if (boarding.TryGetValue(uid, out long old)) sapi.Event.UnregisterGameTickListener(old);
            long started = sapi.World.ElapsedMilliseconds, id = 0;
            id = sapi.Event.RegisterGameTickListener(_ =>
            {
                if (!PutAboard(who, b, sapi.World.ElapsedMilliseconds - started)) return;
                sapi.Event.UnregisterGameTickListener(id);
                boarding.Remove(uid);
            }, 250);
            boarding[uid] = id;
        }

        /// <summary>One try at putting a respawned player aboard; true when it's done with (aboard, or given up).</summary>
        bool PutAboard(PlayerHolder who, ShipSpawnBinding b, long waitedMs)
        {
            var player = who.Player;
            var ep = player.Entity;
            if (ep == null || player.ConnectionState != EnumClientState.Playing) return true;
            if (!Registry.Players.TryGetValue(who.Uid, out var now) || now != b || b.Ended != null) return true;
            // the respawn teleport revives them once the chunks there are loaded
            if (!ep.Alive) return waitedMs > 60000;
            var ship = FindShip(b.ShipId);
            var plate = ship?.Def?.Fixture(b.Plate);
            if (ship != null && plate?.Spawn != null)
            {
                if (ep.MountedOn is ShipSeat seat && seat.Ship == ship) return true;
                var s = plate.Spawn;
                var local = new Vec3d(s[0], s[1], s[2]);
                ship.LocalToWorld(s[0], s[1], s[2], out double wx, out double wy, out double wz);
                if (ep.Pos.Dimension != ship.Pos.Dimension || ep.Pos.SquareDistanceTo(wx, wy, wz) > 16 * 16)
                {
                    ep.TeleportToDouble(wx, wy, wz, () => ship.Board(ep, local));
                }
                else ship.Board(ep, local);
                return true;
            }
            if (waitedMs < BoardWaitMs) return false;
            // she didn't turn up where she was last seen (gone without our knowing): their own spawn back, and there
            sapi.Logger.Notification("[hansa] {0}'s ship #{1} didn't turn up at their spawn: giving them their own back", player.PlayerName, b.ShipId);
            End(who, b, EndMissing);
            var pos = player.GetSpawnPosition(false);
            if (pos != null) ep.TeleportToDouble(pos.X, pos.Y, pos.Z);
            return true;
        }

        // ------------------------------------------------------------------ helpers

        EntityShip FindShip(long id) => sapi.World.GetEntityById(id) is EntityShip s && s.Alive ? s : null;

        static FixtureDef PlateOf(EntityShip ship)
        {
            if (ship?.Def == null) return null;
            foreach (var f in ship.Def.Fixtures) if (f.Type == "attach" && f.Kind == "plate" && f.Spawn != null) return f;
            return null;
        }

        static PlayerSpawnPos Copy(PlayerSpawnPos p) => p == null ? null : new PlayerSpawnPos(p.x, p.y, p.z)
        {
            yaw = p.yaw, pitch = p.pitch, roll = p.roll, RemainingUses = p.RemainingUses
        };
    }
}
