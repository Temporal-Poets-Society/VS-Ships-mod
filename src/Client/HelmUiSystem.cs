using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// The helm and wind UI (0.3.10): the wind panel aboard (<see cref="WindPanel"/>, Ctrl+U or .ships wind), the
    /// masthead pennants (<see cref="PennantRenderer"/>, .ships pennants), and the ships on the map
    /// (<see cref="ShipMapLayer"/>). The server keeps where every ship was last seen and who has sailed in her (in the
    /// save game) and, while a player's map is open, tells them every few seconds where their own ships are and the
    /// loaded ones near them.
    /// </summary>
    public class HelmUiSystem : ModSystem, IRenderer
    {
        const string SaveKey = "ships-shipmap", HotKeyCode = "hansawindpanel";
        /// <summary>Loaded ships within this many blocks of a player are on their map (their own ones anywhere).</summary>
        const double NearRange = 2000;

        ICoreAPI api;
        ICoreClientAPI capi;
        ICoreServerAPI sapi;
        public HansaMod Hansa { get; private set; }
        public Sea.SeaSystem Sea { get; private set; }

        public override double ExecuteOrder() => 0.5;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            Hansa = api.ModLoader.GetModSystem<HansaMod>();
            Sea = api.ModLoader.GetModSystem<Sea.SeaSystem>();
            // (after HansaMod's own types, the same order on both sides)
            if (api is ICoreClientAPI c)
                c.Network.GetChannel(HansaMod.ChannelName).RegisterMessageType<ShipMapRequest>().RegisterMessageType<ShipMapPacket>();
            else if (api is ICoreServerAPI s)
                s.Network.GetChannel(HansaMod.ChannelName).RegisterMessageType<ShipMapRequest>().RegisterMessageType<ShipMapPacket>();
        }

        // ------------------------------------------------------------------ client

        WindPanel panel;
        PennantRenderer pennants;
        IClientNetworkChannel clientChannel;
        readonly Dictionary<long, ShipMapEntry> known = new();
        long mapShownMs = -100000, lastRequestMs = -100000;

        public double RenderOrder => 0.15;
        public int RenderRange => 9999;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            clientChannel = api.Network.GetChannel(HansaMod.ChannelName).SetMessageHandler<ShipMapPacket>(OnShipMap);
            panel = new WindPanel(api, Hansa.Client);
            pennants = new PennantRenderer(api, this);
            api.Event.RegisterRenderer(this, EnumRenderStage.Before, "ships-helmui");
            api.Event.RegisterRenderer(pennants, EnumRenderStage.Opaque, "ships-pennants");
            api.Event.RegisterGameTickListener(_ => RequestShips(), 1000);

            api.Input.RegisterHotKey(HotKeyCode, Lang.Get("ships:hotkey-windpanel"), GlKeys.U, HotkeyType.GUIOrOtherControls, ctrlPressed: true);
            api.Input.SetHotKeyHandler(HotKeyCode, _ => { SetWindPanel(!Hansa.Client.Comfort.WindPanel); return true; });

            var cmd = api.ChatCommands.Get("ships");
            cmd?.BeginSubCommand("wind")
                    .WithDescription(Lang.Get("ships:cmd-wind"))
                    .WithArgs(api.ChatCommands.Parsers.OptionalWordRange("state", "on", "off"))
                    .HandleWith(args =>
                    {
                        SetWindPanel(args[0] is string st ? st == "on" : !Hansa.Client.Comfort.WindPanel);
                        return TextCommandResult.Success();
                    })
                .EndSubCommand()
                .BeginSubCommand("pennants")
                    .WithDescription(Lang.Get("ships:cmd-pennants"))
                    .WithArgs(api.ChatCommands.Parsers.OptionalWordRange("state", "on", "off"))
                    .HandleWith(args =>
                    {
                        var cf = Hansa.Client.Comfort;
                        cf.Pennants = args[0] is string st ? st == "on" : !cf.Pennants;
                        api.StoreModConfig(cf, "ships-client.json");
                        return TextCommandResult.Success(Lang.Get(cf.Pennants ? "ships:pennants-on" : "ships:pennants-off"));
                    })
                .EndSubCommand();

            // the ships on the map (before the map's layers are made, at LevelFinalize)
            var wmm = api.ModLoader.GetModSystem<WorldMapManager>();
            wmm?.RegisterMapLayer<ShipMapLayer>(ShipMapLayer.Code, 1.0);
            if (wmm != null) DrawShipsUnderPlayers(wmm);
        }

        /// <summary>
        /// (0.3.12, the user: the player's arrow was hidden under the ships on the minimap.) The map draws its layers in
        /// the order they were made, which is the order they were registered (vanilla's registry is a dictionary filled
        /// in order: terrain, players, entities, waypoints, then the mods'); the position given only orders the tabs. So
        /// ours is moved in the registry to right after the terrain, before the players: the ships are drawn first and
        /// every marker over them. (Their depth is below the markers' too, see <see cref="ShipMapLayer"/>.)
        /// </summary>
        static void DrawShipsUnderPlayers(WorldMapManager wmm)
        {
            var reg = wmm.MapLayerRegistry;
            if (reg == null || !reg.TryGetValue(ShipMapLayer.Code, out var ours) || !reg.ContainsKey("players")) return;
            var order = new List<KeyValuePair<string, Type>>(reg);
            reg.Clear();
            foreach (var kv in order)
            {
                if (kv.Key == ShipMapLayer.Code) continue;
                if (kv.Key == "players") reg[ShipMapLayer.Code] = ours;
                reg[kv.Key] = kv.Value;
            }
        }

        void SetWindPanel(bool on)
        {
            var cf = Hansa.Client.Comfort;
            cf.WindPanel = on;
            capi.StoreModConfig(cf, "ships-client.json");
            capi.ShowChatMessage(Lang.Get(on ? "ships:windpanel-on" : "ships:windpanel-off"));
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (capi.IsGamePaused) return;
            panel?.Update(capi.World.Player?.Entity);
        }

        /// <summary>The ships layer is being drawn (a map is open with it on).</summary>
        public void MapShown()
        {
            if (capi == null) return;
            bool first = capi.ElapsedMilliseconds - mapShownMs > 5000;
            mapShownMs = capi.ElapsedMilliseconds;
            if (first) RequestShips();
        }

        void RequestShips()
        {
            long now = capi.ElapsedMilliseconds;
            if (now - mapShownMs > 1500 || now - lastRequestMs < 3000) return;
            lastRequestMs = now;
            clientChannel?.SendPacket(new ShipMapRequest { Open = true });
        }

        void OnShipMap(ShipMapPacket p)
        {
            known.Clear();
            foreach (var e in p.Ships ?? new List<ShipMapEntry>()) known[e.Id] = e;
        }

        public readonly struct MapShip
        {
            public readonly long Id;
            public readonly string Code, Name;
            public readonly double X, Z, Yaw, Length;
            public readonly float Speed;
            public readonly bool Own, Loaded;

            public MapShip(long id, string code, string name, double x, double z, double yaw, double length, float speed, bool own, bool loaded)
            {
                Id = id; Code = code; Name = name; X = x; Z = z; Yaw = yaw; Length = length; Speed = speed; Own = own; Loaded = loaded;
            }
        }

        /// <summary>The ships to draw: the ones this client has loaded where they are now, the others where the server says.</summary>
        public List<MapShip> MapShips()
        {
            var list = new List<MapShip>();
            var mine = capi?.World.Player?.PlayerUID;
            var here = new HashSet<long>();
            foreach (var s in Hansa?.Client?.Ships ?? new List<EntityShip>())
            {
                if (s?.Def == null || !s.Alive || s.GoneDown) continue;
                here.Add(s.EntityId);
                bool own = (known.TryGetValue(s.EntityId, out var e) && e.Own) || (mine != null && s.SpawnBound.Contains(mine));
                list.Add(new MapShip(s.EntityId, s.Def.Code, s.ShipName, s.Pos.X, s.Pos.Z, s.Pos.Yaw, s.Def.Length, s.Speed, own, true));
            }
            foreach (var e in known.Values)
            {
                if (here.Contains(e.Id)) continue;
                double len = Hansa?.GetShip(e.Code)?.Length ?? 30;
                list.Add(new MapShip(e.Id, e.Code, e.Name, e.X, e.Z, e.Yaw, len, e.Speed, e.Own, e.Loaded));
            }
            return list;
        }

        // ------------------------------------------------------------------ server

        readonly Dictionary<long, ShipMapRecord> records = new();
        readonly Dictionary<string, long> lastAnswerMs = new();
        IServerNetworkChannel serverChannel;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            serverChannel = api.Network.GetChannel(HansaMod.ChannelName).SetMessageHandler<ShipMapRequest>(OnRequest);
            api.Event.SaveGameLoaded += Load;
            api.Event.GameWorldSave += Save;
            api.Event.RegisterGameTickListener(_ => Survey(), 2000);
            api.Event.OnEntityDespawn += (entity, reason) =>
            {
                if (entity is EntityShip && reason != null && reason.Reason != EnumDespawnReason.Unload && reason.Reason != EnumDespawnReason.OutOfRange)
                    records.Remove(entity.EntityId);
            };
        }

        /// <summary>Server: where this ship was last seen (loaded or not), or null if she's gone (sunk, removed) or never seen.</summary>
        public ShipMapRecord LastSeen(long id)
        {
            Survey();
            return records.TryGetValue(id, out var r) ? r : null;
        }

        void Load()
        {
            try
            {
                var data = sapi.WorldManager.SaveGame.GetData(SaveKey);
                if (data == null) return;
                foreach (var r in SerializerUtil.Deserialize<List<ShipMapRecord>>(data) ?? new List<ShipMapRecord>())
                {
                    r.Crew ??= new HashSet<string>();
                    records[r.Id] = r;
                }
            }
            catch (Exception e) { Mod.Logger.Warning("Couldn't read the ships' map records: {0}", e.Message); }
        }

        void Save()
        {
            Survey();
            sapi.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(records.Values.ToList()));
        }

        /// <summary>Where each loaded ship is, and who is aboard her (she's theirs from then on).</summary>
        void Survey()
        {
            foreach (var s in Hansa?.ServerShips ?? new HashSet<EntityShip>())
            {
                if (s?.Def == null || !s.Alive) continue;
                if (s.GoneDown) { records.Remove(s.EntityId); continue; }
                if (!records.TryGetValue(s.EntityId, out var r)) records[s.EntityId] = r = new ShipMapRecord { Id = s.EntityId };
                r.Code = s.Def.Code; r.Name = s.ShipName; r.X = s.Pos.X; r.Y = s.Pos.Y; r.Z = s.Pos.Z; r.Yaw = s.Pos.Yaw;
                // with a crew list (ShipCrew) she's theirs on the map; else whoever has been aboard
                if (s.HasCrewList)
                {
                    r.Crew.Clear();
                    foreach (var c in s.Crew) r.Crew.Add(c.Uid);
                    continue;
                }
                foreach (var uid in s.SpawnBound) r.Crew.Add(uid);
                foreach (var seat in s.Seats ?? Array.Empty<IMountableSeat>())
                    if (seat?.Passenger is EntityPlayer ep && ep.PlayerUID != null) r.Crew.Add(ep.PlayerUID);
            }
        }

        void OnRequest(IServerPlayer player, ShipMapRequest req)
        {
            if (player?.Entity == null) return;
            long now = sapi.World.ElapsedMilliseconds;
            if (lastAnswerMs.TryGetValue(player.PlayerUID, out long last) && now - last < 1500) return;
            lastAnswerMs[player.PlayerUID] = now;
            var p = new ShipMapPacket();
            var pos = player.Entity.Pos;
            var loaded = new Dictionary<long, EntityShip>();
            foreach (var s in Hansa?.ServerShips ?? new HashSet<EntityShip>())
                if (s?.Def != null && s.Alive && !s.GoneDown) loaded[s.EntityId] = s;
            foreach (var s in loaded.Values)
            {
                bool own = records.TryGetValue(s.EntityId, out var r) && r.Crew.Contains(player.PlayerUID);
                double dx = s.Pos.X - pos.X, dz = s.Pos.Z - pos.Z;
                if (!own && dx * dx + dz * dz > NearRange * NearRange) continue;
                p.Ships.Add(new ShipMapEntry { Id = s.EntityId, Code = s.Def.Code, Name = s.ShipName, X = s.Pos.X, Z = s.Pos.Z, Yaw = s.Pos.Yaw, Own = own, Speed = s.Speed, Loaded = true });
            }
            foreach (var r in records.Values)
            {
                if (loaded.ContainsKey(r.Id) || !r.Crew.Contains(player.PlayerUID)) continue;
                p.Ships.Add(new ShipMapEntry { Id = r.Id, Code = r.Code, Name = r.Name, X = r.X, Z = r.Z, Yaw = r.Yaw, Own = true, Loaded = false });
            }
            serverChannel.SendPacket(p, player);
        }

        public override void Dispose()
        {
            if (capi != null)
            {
                capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
                if (pennants != null) capi.Event.UnregisterRenderer(pennants, EnumRenderStage.Opaque);
            }
            pennants?.Dispose();
            panel?.Dispose();
        }
    }
}
