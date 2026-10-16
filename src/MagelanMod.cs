using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    public class HansaConfig
    {
        /// <summary>Drop anchor and furl the sails when nobody is aboard (or everyone logged out).</summary>
        public bool StopWhenUnmanned = true;
        public float UnmannedStopSeconds = 5;
        /// <summary>A ship with a crew list lies laid up while none of her crew is online: anchored, furled, closed to
        /// others, and no water, damage or wear (ShipCrew).</summary>
        public bool LayUpWhenCrewOffline = true;
        /// <summary>A player who logged out aboard a ship comes back aboard her wherever she is now (ShipLogouts).</summary>
        public bool RejoinShipOnLogin = true;
        /// <summary>While anyone sleeps in a bunk aboard, the watch turns them out for land ahead, water rising, grounding,
        /// being hit, the wind shifting, the sea getting up, or another mod's alarm (ShipWatch).</summary>
        public bool ShipWatch = true;
        /// <summary>How far ahead the watch looks for land: this many seconds of her way (16 to 96 blocks).</summary>
        public float WatchAheadSeconds = 45;
        /// <summary>Multiplies every ship's top speed.</summary>
        public float SpeedMultiplier = 1;
        /// <summary>
        /// (0.3.25) Under the sweeps (at the wheel, sails furled, Ctrl): how fast she creeps ahead and astern holding W or S
        /// (blocks a second; a walk is about 1.5), how fast A/D turn her (degrees a second, even standing still), and how long
        /// she takes to pick up or lose that way (seconds).
        /// </summary>
        public float CrawlSpeed = Crawl.DefaultSpeed, CrawlAsternSpeed = Crawl.DefaultAsternSpeed, CrawlTurn = Crawl.DefaultTurn, CrawlRampSeconds = Crawl.DefaultRampSeconds;
        /// <summary>Ships sail by Sea's wind: the heading and the wind decide how fast they go, and they can't sail into
        /// it. Off, the sails are a throttle (full sail = top speed whatever the wind).</summary>
        public bool SailByWind = true;
        /// <summary>
        /// Degrees closer to the wind every ship points than her design (the carrack 67° off the true wind, the skiff 55°;
        /// negative: less close). Her speed on the wind comes with it. 0: historical; 7: the carrack as weatherly as the
        /// best square-riggers ever (clippers, today's sail-training ships, about 60°); past 10 it's beyond any square rig.
        /// Ships never point closer than 35°.
        /// </summary>
        public float PointCloser = 0;
        /// <summary>Cabins keep rain, hail and cold out, and stoves warm the room.</summary>
        public bool CozyCabins = true;
        /// <summary>No placing blocks inside a ship's hull (breaking the rock or soil a ship ran into is allowed).</summary>
        public bool ProtectHulls = true;
        /// <summary>A metal plate on the wall by a ship's helm binds your spawn to her (right-click it, empty-handed).</summary>
        public bool ShipSpawn = true;
        /// <summary>
        /// Ships take damage (running aground, running into each other, cannon balls), leak, take water, founder and
        /// sink; their seams and sails wear and their bottoms foul. Pumps and repairs with vanilla items.
        /// </summary>
        public bool Damage = true;
        /// <summary>A ship's guns are laid and fired (right-click one to stand to it, look to lay it, right-click to fire): cannon balls that hole ships, hurt creatures and players (PvP rules) and splash.</summary>
        public bool Cannons = true;
        /// <summary>Cannon balls break a few soft blocks in their way (not stone, ore or metal; not in a land claim the one who fired may not build in). Off: they stop at the first block.</summary>
        public bool CannonsBreakBlocks = false;
        /// <summary>How hard running aground and into each other hurts (a factor).</summary>
        public float GroundingDamage = 1;
        /// <summary>How fast water comes in through damage and worn seams (a factor).</summary>
        public float Leaks = 1;
        /// <summary>How fast seams, sails and an unsheathed bottom wear and foul with time (a factor; 0: no wear). The ones
        /// below multiply it.</summary>
        public float Wear = 1;
        /// <summary>How fast set sails chafe with time (a factor, times Wear; 0: none). Slow: a few % in many days at sea.</summary>
        public float SailWear = 1;
        /// <summary>How hard sails tear: in a gale with too much sail set or taken aback (times Wear), and from shot (a factor; 0: never).</summary>
        public float SailDamage = 1;
        /// <summary>
        /// (0.3.24, the user: "after a calm I want to go fast") The Beaufort force (its bottom; 8.5: halfway through force 8)
        /// from which full sail strains (the warning, the strain building); reefed sails stand nearly twice the wind. 0: the
        /// design's own (force 9).
        /// </summary>
        public float SailStrainForce = 9;
        /// <summary>(0.3.24) The force from which strained sails tear (after minutes of strain); below it they only strain. 0: the design's own (force 10).</summary>
        public float SailTearForce = 10;
        /// <summary>How fast the planking's seams work loose with time (a factor, times Wear; 0: never).</summary>
        public float SeamWear = 1;
        /// <summary>How fast an unsheathed bottom fouls with time (a factor, times Wear; 0: never).</summary>
        public float Fouling = 1;
        /// <summary>How much a blow hurts the hull, decks, rudder, masts and rigging, whatever it was (a factor, times
        /// GroundingDamage or CannonDamage; 0: they're never hurt).</summary>
        public float HullDamage = 1;
        /// <summary>How much a cannon ball hurts a ship (a factor on top of HullDamage and SailDamage; 0: shot does no harm).</summary>
        public float CannonDamage = 1;
        /// <summary>How fast the pumps get water out (a factor on 200 litres a stroke, a stroke a second, two at a brake).</summary>
        public float Pumps = 1;
        /// <summary>The wind on a ship's hull, castles and rig drives her even under bare poles (a factor; 0: only sails drive her).</summary>
        public float Windage = 1;
        /// <summary>
        /// A ship that sinks to the bottom becomes a wreck of world blocks (broken up, holed where she was), her cargo in
        /// vanilla chests and vessels in and beside her, her guns and anchor on the seabed. Off: she lies on the bottom as
        /// she is, frozen, with her cargo in her.
        /// </summary>
        public bool Wrecks = true;
        /// <summary>
        /// A ship's firehearth works as vanilla's firepit: fuel, lit with a firestarter or a lit torch, cooking and smelting
        /// (a pot's recipes too) by vanilla's numbers, flames, smoke out of its stovepipe, its light and crackle, and its
        /// warmth. Off: it doesn't burn and can't be lit (what's in it stays, and can be taken out).
        /// </summary>
        public bool Hearth = true;
        /// <summary>
        /// The ships' clay ovens (a fixture "oven": vanilla's clay oven rebuilt aboard, ShipOven): firewood, lit, baking
        /// bread and pies. False: they don't burn or take anything in (what's in them can still be taken out).
        /// </summary>
        public bool Oven = true;
        /// <summary>
        /// (0.3.14, working blocks aboard: ShipBlocks) A forge, bellows, anvil, quern, barrel, crock or bloomery put on a
        /// floor place aboard is the real vanilla block (in the ship's own mini-dimension, carried with her), working as
        /// ashore. Off (the default until it's been tried in the game): it's put as an inert piece as before, and none of
        /// it runs (no dimension, no patches on the server).
        /// </summary>
        public bool WorkingBlocks = false;
        /// <summary>
        /// Which blocks work aboard (WorkingBlocks): the first part of a block's code ("forge" for game:forge, "anvil" for
        /// game:anvil-iron), or a prefix ending in * ("bloomery*": the bloomery and its chimney). Anything else stays a piece.
        /// </summary>
        public string[] WorkingBlockCodes = { "forge", "bellows", "anvil", "quern", "barrel", "crock", "bloomery*" };
    }

    public partial class HansaMod : ModSystem
    {
        public const string ChannelName = "hansa";

        ICoreAPI api;
        ICoreServerAPI sapi;
        IServerNetworkChannel serverChannel;
        readonly Dictionary<string, ShipDefinition> ships = new();
        public readonly HashSet<EntityShip> ServerShips = new();
        public HansaConfig Config = new();
        public ShipClientSystem Client;
        /// <summary>Server: spawns bound to ships (the plate by the helm).</summary>
        public ShipSpawns Spawns;
        /// <summary>Server: who logged out aboard which ship, to put them back aboard her (ShipLogouts).</summary>
        public ShipLogouts Logouts;
        /// <summary>Server: the cannon balls in flight.</summary>
        public CannonShots Shots;
        /// <summary>Server: sunk ships made into wrecks.</summary>
        public ShipWrecks Wrecks;
        /// <summary>The inside of this side's ships' hulls, where Sea treats the water as dry (the hold, the lower deck).</summary>
        public readonly ShipDrySpaces Dry = new();
        Sea.SeaSystem sea;

        public override double ExecuteOrder() => 0.2;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            api.RegisterEntity("hansa.EntityShip", typeof(EntityShip));
            api.RegisterItemClass("hansa.ItemShipDeed", typeof(ItemShipDeed));
            api.RegisterEntity("hansa.EntityRowBoat", typeof(EntityRowBoat));
            api.RegisterItemClass("hansa.ItemRowBoat", typeof(ItemRowBoat));
            api.RegisterItemClass("hansa.ItemSurveyorsChalk", typeof(ItemSurveyorsChalk));
            api.RegisterBlockClass("hansa.BlockWreck", typeof(BlockWreck));
            // (0.3.23) the ship's stairs (placed aboard in building mode only)
            api.RegisterBlockClass("hansa.BlockShipStair", typeof(BlockShipStair));
            api.RegisterBlockEntityClass(BlockEntityWreck.ClassName, typeof(BlockEntityWreck));
            api.RegisterMountable(ShipSeat.ClassName, ShipSeat.GetMountable);
            api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<DeckStatePacket>()
                .RegisterMessageType<BoardPacket>()
                .RegisterMessageType<LeavePacket>()
                .RegisterMessageType<FixturePacket>()
                .RegisterMessageType<NamePacket>()
                .RegisterMessageType<RepairPacket>()
                .RegisterMessageType<ShotPacket>()
                .RegisterMessageType<GunLayPacket>()
                .RegisterMessageType<HearthPacket>()
                .RegisterMessageType<DecorPacket>()
                .RegisterMessageType<CrewPacket>();
            Patches.Apply(api, Mod.Logger);
            // below the waterline the hulls are dry: no water, fog or breath bar in the hold and on the lower deck
            sea = api.ModLoader.GetModSystem<Sea.SeaSystem>();
            sea?.AddDrySpaces(Dry);
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            ships.Clear();
            foreach (var kv in api.Assets.GetMany<ShipDefinition>(Mod.Logger, "config/ships/", "hansa"))
            {
                try
                {
                    kv.Value.Init();
                    ships[kv.Value.Code] = kv.Value;
                    Mod.Logger.Notification("Ship '{0}': {1} solid voxels, {2} fixtures, {3} rooms", kv.Value.Code,
                        kv.Value.Grid.CountSolid(), kv.Value.Fixtures.Length, kv.Value.Rooms.Length);
                }
                catch (Exception e)
                {
                    Mod.Logger.Error("Failed to load ship {0}: {1}", kv.Key, e);
                }
            }
            // (grid ships, docs/proposals/ship-design-architecture.md) designs built from the construction core, each on
            // its rig donor among the ships just loaded
            GridShips.LoadAll(api, ships, Mod.Logger);
            // each design's paint zones, read in the background (HansaDecor)
            StartPaintPlans(api);
        }

        public ShipDefinition GetShip(string code) => code != null && ships.TryGetValue(code, out var d) ? d : null;
        public IEnumerable<string> ShipCodes => ships.Keys;

        public override void Dispose()
        {
            sea?.RemoveDrySpaces(Dry);
            Wrecks?.Dispose();
            Patches.Remove(api);
        }

        // ------------------------------------------------------------------ server

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            try
            {
                Config = api.LoadModConfig<HansaConfig>("hansa.json") ?? new HansaConfig();
                api.StoreModConfig(Config, "hansa.json");
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Bad ModConfig/hansa.json, using defaults: {0}", e.Message);
            }
            Patches.Cozy = Config.CozyCabins;

            serverChannel = api.Network.GetChannel(ChannelName)
                .SetMessageHandler<DeckStatePacket>(OnDeckState)
                .SetMessageHandler<BoardPacket>(OnBoard)
                .SetMessageHandler<LeavePacket>(OnLeave)
                .SetMessageHandler<FixturePacket>(OnFixture)
                .SetMessageHandler<NamePacket>(OnName)
                .SetMessageHandler<RepairPacket>(OnRepair)
                .SetMessageHandler<GunLayPacket>(OnGunLay)
                .SetMessageHandler<HearthPacket>(OnHearth)
                .SetMessageHandler<DecorPacket>(OnDecor)
                .SetMessageHandler<CrewPacket>(OnCrew);

            api.Event.CanPlaceOrBreakBlock += OnCanPlaceOrBreak;
            api.Event.OnEntitySpawn += e => { if (e is EntityShip s) { ServerShips.Add(s); Dry.Set(ServerShips); } };
            api.Event.OnEntityLoaded += e => { if (e is EntityShip s) { ServerShips.Add(s); Dry.Set(ServerShips); } };
            api.Event.OnEntityDespawn += (e, d) =>
            {
                if (e is not EntityShip s) return;
                ServerShips.Remove(s);
                Dry.Set(ServerShips);
                // gone for good (removed, picked up, later sunk; not just her chunk unloading): spawns bound to her end
                if (d?.Reason is EnumDespawnReason.Death or EnumDespawnReason.Combusted or EnumDespawnReason.PickedUp or
                    EnumDespawnReason.Expire or EnumDespawnReason.Removed) Spawns?.EndShip(s.EntityId, ShipSpawns.EndGone);
            };
            Spawns = new ShipSpawns(api, this);
            Logouts = new ShipLogouts(api, this);
            Shots = new CannonShots(api, this);
            Wrecks = new ShipWrecks(api, this);
            api.Event.RegisterGameTickListener(dt => Shots.Tick(dt), 20);

            RegisterCommands(api);
            // decorating's test commands (HansaDecor)
            RegisterDecorCommands(api);
        }

        /// <summary>Server: tell the players within reach of a cannon ball (it's heard and seen far off) what it's doing.</summary>
        public void SendShot(ShotPacket p, Vec3d at)
        {
            foreach (IServerPlayer pl in sapi.World.AllOnlinePlayers)
            {
                if (pl.ConnectionState != EnumClientState.Playing || pl.Entity == null) continue;
                double dx = pl.Entity.Pos.X - at.X, dz = pl.Entity.Pos.Z - at.Z;
                if (dx * dx + dz * dz > 320 * 320) continue;
                serverChannel.SendPacket(p, pl);
            }
        }

        /// <summary>
        /// The sea's surface near a world point (world y): the top of the water column there (within a few blocks of y) and
        /// Sea's waves on it; NaN if there's no water there.
        /// </summary>
        public double SeaSurface(double x, double y, double z, int dim)
        {
            var bp = new BlockPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z), dim);
            double still = EntityShip.FindWaterSurface(sapi.World, bp, 2, 3);
            if (double.IsNaN(still)) return double.NaN;
            return still + (sea?.WaveHeightAt(x, z) ?? 0);
        }

        /// <summary>Sea's wind at a place as a velocity (blocks a second, level): what drifts a gun's smoke away.</summary>
        public Vec3d WindAt(double x, double z)
        {
            var w = sea != null ? sea.WindAt(x, z) : default;
            // (Sea's wind units: 1 is 25 m/s; smoke drifts at most of the wind's speed)
            return new Vec3d(w.X * 20, 0, w.Z * 20);
        }

        EntityShip ShipById(long id) => sapi.World.GetEntityById(id) as EntityShip;

        /// <summary>Not one of her crew (ShipCrew): told so (sent: the fixture was refused), true.</summary>
        public static bool CrewOnly(IServerPlayer player, EntityShip ship, bool refused = false)
        {
            if (!refused && ship.MayUse(player)) return false;
            player.SendIngameError("hansa-crew", Lang.GetL(player.LanguageCode, ship.LaidUp ? "hansa:laidup-closed" : "hansa:crew-only", ship.GetName()));
            return true;
        }

        /// <summary>A change to her crew list, made at the plaque by the great cabin's door (ShipCrew).</summary>
        void OnCrew(IServerPlayer player, CrewPacket p)
        {
            var ship = ShipById(p.ShipId);
            var ep = player.Entity;
            if (ship?.Def == null || ep == null || !ep.Alive || p.Action == null) return;
            if (ep.Pos.SquareDistanceTo(ship.Pos.X, ship.Pos.Y, ship.Pos.Z) > Math.Pow(ship.Def.Length / 2 + 8, 2)) return;
            string err = ship.ChangeCrew(player, p.Action, p.Name);
            if (err != null) player.SendIngameError("hansa-crew", Lang.GetL(player.LanguageCode, err, p.Name ?? ""));
        }

        void OnDeckState(IServerPlayer player, DeckStatePacket p)
        {
            var ep = player.Entity;
            if (!(ep?.MountedOn is DeckSeat seat) || seat.Ship.EntityId != p.ShipId) return;
            var g = seat.Ship.Def.Grid;
            double x = GameMath.Clamp(p.X, g.OX - 1, g.MaxX + 1), y = GameMath.Clamp(p.Y, g.OY - 1, g.MaxY + 2), z = GameMath.Clamp(p.Z, g.OZ - 1, g.MaxZ + 1);
            seat.SetLocal(x, y, z, p.Flags);

            var relay = new DeckStatePacket { ShipId = p.ShipId, X = (float)x, Y = (float)y, Z = (float)z, Flags = p.Flags, PlayerEntityId = ep.EntityId };
            var shipPos = seat.Ship.Pos;
            foreach (IServerPlayer other in sapi.World.AllOnlinePlayers)
            {
                if (other == player || other.ConnectionState != EnumClientState.Playing || other.Entity == null) continue;
                if (other.Entity.Pos.SquareDistanceTo(shipPos) > 160 * 160) continue;
                serverChannel.SendPacket(relay, other);
            }
        }

        void OnBoard(IServerPlayer player, BoardPacket p)
        {
            var ship = ShipById(p.ShipId);
            var ep = player.Entity;
            if (ship?.Def == null || ep == null || !ep.Alive || ship.GoneDown) return;
            if (ep.MountedOn != null && ep.MountedOn.Entity == ship) return;
            // laid up, she's closed to all but her crew (ShipCrew)
            if (!ship.MayBoard(player)) { player.SendIngameError("hansa-crew", Lang.GetL(player.LanguageCode, "hansa:laidup-closed")); return; }
            ship.LocalToWorld(p.X, p.Y, p.Z, out double wx, out double wy, out double wz);
            if (ep.Pos.SquareDistanceTo(wx, wy, wz) > 12 * 12) return;
            ship.Board(ep, new Vec3d(p.X, p.Y, p.Z));
        }

        void OnLeave(IServerPlayer player, LeavePacket p)
        {
            var ep = player.Entity;
            if (ep?.MountedOn is ShipSeat seat && seat.Ship.EntityId == p.ShipId)
            {
                ep.TryUnmount();
                ep.PositionBeforeFalling.Set(p.X, p.Y, p.Z);
            }
        }

        void OnFixture(IServerPlayer player, FixturePacket p)
        {
            if (p.Fixture == FixturePacket.BoardBoat && sapi.World.GetEntityById(p.ShipId) is EntityRowBoat inBoat)
            {
                // stepped, jumped or swum into her: sit down (vanilla's boats only take you by a click on a seat)
                var pe = player.Entity;
                if (pe == null || !pe.Alive || pe.MountedOn != null || pe.Pos.SquareDistanceTo(inBoat.Pos.X, inBoat.Pos.Y, inBoat.Pos.Z) > 7 * 7) return;
                var seat = inBoat.FreeSeat();
                if (seat != null) pe.TryMount(seat);
                return;
            }
            if (p.Fixture == FixturePacket.PickUp && sapi.World.GetEntityById(p.ShipId) is EntityRowBoat boat)
            {
                // the rowing boat from a ship's deck or rope ladder beside her (there Shift is the ladder's, and vanilla's
                // own pickup never sees the keys)
                PickUpRowBoat(player, boat);
                return;
            }
            var ship = ShipById(p.ShipId);
            var ep = player.Entity;
            if (ship?.Def == null || ep == null || !ep.Alive) return;
            if (p.Fixture == FixturePacket.Cladding)
            {
                if (CrewOnly(player, ship)) return;
                // from the water or a dock next to the hull
                var l = ship.WorldToLocal(ep.Pos.X, ep.Pos.Y, ep.Pos.Z);
                if (Math.Abs(l.X) > ship.Def.Length * 0.6 + 4 || Math.Abs(l.Z) > ship.Def.Beam * 0.5 + 6) return;
                ship.AddCladding(player);
                return;
            }
            if (p.Fixture == FixturePacket.PickUp)
            {
                // Shift + right-click a boat: from the water, a dock, or a ship's deck or ladder beside her
                if (ep.Pos.SquareDistanceTo(ship.Pos.X, ship.Pos.Y, ship.Pos.Z) > Math.Pow(ship.Def.Length / 2 + 7, 2)) return;
                string perr = ship.TryPickUp(player);
                if (perr != null) player.SendIngameError("hansa-pickup", Lang.GetL(player.LanguageCode, perr));
                return;
            }
            // (0.3.18, note 165) a hearth or an oven put aboard is used through its stand-in fixture (a piece's index)
            var f = p.Fixture >= 0 && p.Fixture < ship.Def.Fixtures.Length ? ship.Def.Fixtures[p.Fixture] : ship.FixtureByIndex(p.Fixture);
            if (f?.Pos == null || f.Type == "piece") return;
            ship.LocalToWorld(f.Pos[0], f.Pos[1], f.Pos[2], out double wx, out double wy, out double wz);
            if (ep.Pos.SquareDistanceTo(wx, wy, wz) > 8 * 8) return;
            // (not through her hull or decks: a chest in her from outside, 0.3.10)
            if (!ship.SeenBy(player, f.Sel ?? f.Box)) return;
            // someone not of her crew: doors, hatches and the heads, and only while she's not laid up (ShipCrew)
            if (!ship.MayUse(player) && (ship.LaidUp || !EntityShip.GuestMayUse(f))) { CrewOnly(player, ship, true); return; }
            ship.Interact(player, f, p.Sneak, p.Ctrl);
        }

        void PickUpRowBoat(IServerPlayer player, EntityRowBoat boat)
        {
            var ep = player.Entity;
            if (ep == null || !ep.Alive || !boat.Alive || ep.Pos.SquareDistanceTo(boat.Pos.X, boat.Pos.Y, boat.Pos.Z) > 9 * 9) return;
            if (!boat.IsEmptyForPickup())
            {
                player.SendIngameError("hansa-pickup", Lang.GetL(player.LanguageCode, "hansa:pickup-cargo"));
                return;
            }
            var item = sapi.World.GetItem(boat.Code);
            if (item == null) return;
            var stack = new ItemStack(item);
            if (!player.InventoryManager.TryGiveItemstack(stack, true)) sapi.World.SpawnItemEntity(stack, ep.Pos.XYZ.Add(0, 0.5, 0));
            sapi.World.Logger.Audit("{0} picked up a {1} at {2}.", player.PlayerName, boat.Code, boat.Pos);
            boat.Die(EnumDespawnReason.PickedUp);
        }

        void OnName(IServerPlayer player, NamePacket p)
        {
            var ship = ShipById(p.ShipId);
            var ep = player.Entity;
            if (ship?.Def == null || ep == null || !ep.Alive) return;
            // from aboard or close alongside
            if (ep.Pos.SquareDistanceTo(ship.Pos.X, ship.Pos.Y, ship.Pos.Z) > 40 * 40) return;
            if (CrewOnly(player, ship)) return;
            var style = p.TextColour == null ? null : new NameStyle { Text = p.TextColour, Back = string.IsNullOrEmpty(p.BackColour) ? null : p.BackColour, Size = p.Size, Italic = p.Italic };
            string err = ship.SetName(p.Name, style);
            if (err != null) player.SendIngameError("hansa-name", Lang.GetL(player.LanguageCode, err, EntityShip.MaxNameLength));
        }

        /// <summary>A part mended (held right-click with the items): the ship checks the reach and takes the items.</summary>
        void OnRepair(IServerPlayer player, RepairPacket p)
        {
            var ship = ShipById(p.ShipId);
            var ep = player.Entity;
            if (ship?.Def == null || ep == null || !ep.Alive) return;
            if (ep.Pos.SquareDistanceTo(ship.Pos.X, ship.Pos.Y, ship.Pos.Z) > Math.Pow(ship.Def.Length / 2 + 12, 2)) return;
            if (CrewOnly(player, ship)) return;
            string held = player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString();
            // ((0.3.28, step D2) a cracked piece put aboard)
            var sp = ShipPart.Decode(p.Part);
            if (sp.Kind == ShipPartKind.Piece) { ship.TryRepairPiece(player, sp.Index, held); return; }
            int part = ship.DamagePartFor(sp);
            ship.TryRepair(player, part, held);
        }

        /// <summary>Holding right mouse by a gun (lay it where they look) or letting go: the ship checks they're by it.</summary>
        void OnGunLay(IServerPlayer player, GunLayPacket p)
        {
            var ship = ShipById(p.ShipId);
            var ep = player.Entity;
            if (ship?.Def == null || ep == null || p.Gun < 0 || p.Gun >= ship.Def.Fixtures.Length) return;
            var gun = ship.Def.Fixtures[p.Gun];
            if (!p.On) { ship.StopLaying(gun, ep); return; }
            if (CrewOnly(player, ship)) return;
            string err = ship.StartLaying(gun, ep);
            if (err != null) player.SendIngameError("hansa-gun", Lang.GetL(player.LanguageCode, err));
            else if (p.HasAim) ship.LayerAim(gun, ep, new GunLay(p.Train, p.Elevation));
        }

        /// <summary>A firestarter or a lit torch held to a ship's hearth long enough: the ship checks they're by it and tries to light it.</summary>
        void OnHearth(IServerPlayer player, HearthPacket p)
        {
            var ship = ShipById(p.ShipId);
            var ep = player.Entity;
            if (ship?.Def == null || ep == null || !ep.Alive) return;
            var f = ship.FixtureByIndex(p.Fixture);
            if (f == null || f != ship.HearthFixture) return;
            if (CrewOnly(player, ship)) return;
            ship.LocalToWorld(f.Pos[0], f.Pos[1], f.Pos[2], out double wx, out double wy, out double wz);
            if (ep.Pos.SquareDistanceTo(wx, wy, wz) > 8 * 8) return;
            ship.HearthIgniteFrom(player, p.Seconds);
        }

        public void SendOpenStorage(IServerPlayer player, EntityShip ship, int fixture)
        {
            serverChannel.SendPacket(new FixturePacket { ShipId = ship.EntityId, Fixture = fixture }, player);
        }

        bool OnCanPlaceOrBreak(IServerPlayer byPlayer, BlockSelection blockSel, out string claimant)
        {
            claimant = null;
            if (!Config.ProtectHulls || blockSel?.Position == null || MayPlaceOrBreak(blockSel.Position)) return true;
            claimant = Lang.GetL(byPlayer.LanguageCode, "hansa:ship-in-the-way");
            return false;
        }

        /// <summary>
        /// ProtectHulls: nothing may be put inside a ship's hull. The game asks the same for placing (at the air, water or
        /// plant spot the block goes into) and for breaking; a solid block there is being broken, and breaking is fine:
        /// that's rock or soil a ship ran into, and digging it out frees her.
        /// </summary>
        public bool MayPlaceOrBreak(BlockPos bp)
        {
            var there = sapi.World.BlockAccessor.GetBlock(bp, BlockLayersAccess.Solid);
            if (there != null && there.Id != 0 && there.Replaceable < 6000 && there.CollisionBoxes?.Length > 0) return true;
            foreach (var ship in ServerShips)
            {
                if (ship.Def == null || ship.Pos.SquareDistanceTo(bp.X + 0.5, bp.Y + 0.5, bp.Z + 0.5) > 45 * 45) continue;
                if (BlockInsideShip(ship, bp)) return false;
            }
            return true;
        }

        static bool BlockInsideShip(EntityShip ship, BlockPos bp)
        {
            // sample the block's corners and centre in ship space
            var g = ship.Def.Grid;
            for (int i = 0; i < 9; i++)
            {
                double x = bp.X + (i == 8 ? 0.5 : (i & 1) * 0.9 + 0.05);
                double y = bp.Y + (i == 8 ? 0.5 : ((i >> 1) & 1) * 0.9 + 0.05);
                double z = bp.Z + (i == 8 ? 0.5 : ((i >> 2) & 1) * 0.9 + 0.05);
                var l = ship.WorldToLocal(x, y, z);
                if (g.SolidAt(l.X, l.Y, l.Z) || ship.Def.RoomAt(l.X, l.Y, l.Z) != null) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>Puts a new ship on open water in front of the player, side-on. Returns an error lang code or null.</summary>
        public string TrySpawnShip(IServerPlayer player, string code, out EntityShip spawned)
        {
            spawned = null;
            var def = GetShip(code);
            if (def == null) return "hansa:no-such-ship";
            var ep = player.Entity;
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", code));
            if (type == null) return "hansa:no-such-ship";

            double yaw = ep.Pos.Yaw;
            double dx = Math.Sin(yaw), dz = Math.Cos(yaw);
            double shipYaw = yaw + Math.PI / 2;  // side-on to the player
            for (double d = def.Beam / 2 + 4; d <= def.Beam / 2 + 40; d += 2)
            {
                double x = ep.Pos.X + dx * d, z = ep.Pos.Z + dz * d;
                var bp = new BlockPos((int)Math.Floor(x), (int)Math.Floor(ep.Pos.Y), (int)Math.Floor(z), ep.Pos.Dimension);
                double wy = EntityShip.FindWaterSurface(sapi.World, bp, 6, 20);
                if (double.IsNaN(wy)) continue;
                if (EntityShip.PoseBlocked(sapi.World, def, x, wy, z, shipYaw, ep.Pos.Dimension)) continue;
                if (!KeelInWater(def, x, wy, z, shipYaw, ep.Pos.Dimension)) continue;
                // (0.3.19) not on another ship, nor where one was just launched (she may not be among the ships yet)
                if (OverlapsShip(def, x, z, shipYaw, ep.Pos.Dimension)) continue;

                var entity = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
                entity.Pos.SetPos(x, wy, z);
                entity.Pos.Dimension = ep.Pos.Dimension;
                entity.Pos.Yaw = GameMath.Mod((float)shipYaw, GameMath.TWOPI);
                entity.PositionBeforeFalling.Set(x, wy, z);
                entity.Attributes.SetString("origin", "playerplaced");
                sapi.World.SpawnEntity(entity);
                NoteLaunch(def, x, z, shipYaw, ep.Pos.Dimension);
                spawned = entity;
                return null;
            }
            return "hansa:no-open-water";
        }

        /// <summary>(0.3.19) Ships launched in the last few seconds: where (x, z, yaw, dimension), her design, when (ms).</summary>
        readonly List<(ShipDefinition Def, double X, double Z, double Yaw, int Dim, long At)> recentLaunches = new();
        /// <summary>(0.3.19) How long a launch is remembered for <see cref="OverlapsShip"/> (ms): till she's surely among the ships.</summary>
        const long RecentLaunchMs = 10000;

        void NoteLaunch(ShipDefinition def, double x, double z, double yaw, int dim)
        {
            long now = sapi.World.ElapsedMilliseconds;
            recentLaunches.RemoveAll(l => now - l.At > RecentLaunchMs);
            recentLaunches.Add((def, x, z, yaw, dim, now));
        }

        /// <summary>
        /// (0.3.19, the charter's double launch) Would a ship of this design at (x, z) heading yaw overlap a ship afloat (her
        /// length by her beam, a block to spare) or one launched in the last few seconds?
        /// </summary>
        public bool OverlapsShip(ShipDefinition def, double x, double z, double yaw, int dim)
        {
            long now = sapi.World.ElapsedMilliseconds;
            foreach (var s in ServerShips)
                if (s.Alive && s.Def != null && s.Pos.Dimension == dim && FootprintsOverlap(def, x, z, yaw, s.Def, s.Pos.X, s.Pos.Z, s.Pos.Yaw)) return true;
            foreach (var l in recentLaunches)
                if (now - l.At <= RecentLaunchMs && l.Dim == dim && FootprintsOverlap(def, x, z, yaw, l.Def, l.X, l.Z, l.Yaw)) return true;
            return false;
        }

        /// <summary>(0.3.19) Two ships' footprints (length by beam about each one's hull middle, a block to spare) overlap: separating axes.</summary>
        public static bool FootprintsOverlap(ShipDefinition a, double ax, double az, double ayaw, ShipDefinition b, double bx, double bz, double byaw)
        {
            double ha = a.Length / 2 + 1, wa = a.Beam / 2 + 1, hb = b.Length / 2 + 1, wb = b.Beam / 2 + 1;
            // (about her hull's middle along her: her hull samples' mid x)
            double ca = HullMiddle(a), cb = HullMiddle(b);
            ax += ca * Math.Sin(ayaw); az += ca * Math.Cos(ayaw);
            bx += cb * Math.Sin(byaw); bz += cb * Math.Cos(byaw);
            if ((ax - bx) * (ax - bx) + (az - bz) * (az - bz) > (ha + wa + hb + wb) * (ha + wa + hb + wb)) return false;
            // each one's axes in the world: local +x (sin yaw, cos yaw), local +z (-cos yaw, sin yaw) (ShipFrame.ToWorld)
            var axes = new[] { (Math.Sin(ayaw), Math.Cos(ayaw)), (-Math.Cos(ayaw), Math.Sin(ayaw)), (Math.Sin(byaw), Math.Cos(byaw)), (-Math.Cos(byaw), Math.Sin(byaw)) };
            double dx = bx - ax, dz = bz - az;
            foreach (var (ux, uz) in axes)
            {
                double ra = ha * Math.Abs(ux * axes[0].Item1 + uz * axes[0].Item2) + wa * Math.Abs(ux * axes[1].Item1 + uz * axes[1].Item2);
                double rb = hb * Math.Abs(ux * axes[2].Item1 + uz * axes[2].Item2) + wb * Math.Abs(ux * axes[3].Item1 + uz * axes[3].Item2);
                if (Math.Abs(dx * ux + dz * uz) > ra + rb) return false;
            }
            return true;
        }

        /// <summary>
        /// Puts a boat on the water where the player aims (the water block under the crosshair), her bow pointing the
        /// way they look. Returns an error lang code or null.
        /// </summary>
        public string TryLaunchBoatAt(IServerPlayer player, string code, BlockPos at, out EntityShip spawned)
        {
            spawned = null;
            var def = GetShip(code);
            var type = def == null ? null : sapi.World.GetEntityType(new AssetLocation("hansa", "boat-" + code));
            if (def == null || type == null) return "hansa:no-such-ship";
            var ep = player.Entity;
            if (at == null) return "hansa:launch-needs-water";
            double wy = EntityShip.FindWaterSurface(sapi.World, at, 2, 4);
            if (double.IsNaN(wy)) return "hansa:launch-needs-water";
            double yaw = ep.Pos.Yaw;
            double x = at.X + 0.5, z = at.Z + 0.5;
            if (EntityShip.PoseBlocked(sapi.World, def, x, wy, z, yaw, ep.Pos.Dimension) || !KeelInWater(def, x, wy, z, yaw, ep.Pos.Dimension) ||
                OverlapsShip(def, x, z, yaw, ep.Pos.Dimension))
                return "hansa:launch-no-room";
            var entity = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            entity.Pos.SetPos(x, wy, z);
            entity.Pos.Dimension = ep.Pos.Dimension;
            entity.Pos.Yaw = GameMath.Mod((float)yaw, GameMath.TWOPI);
            entity.PositionBeforeFalling.Set(x, wy, z);
            entity.Attributes.SetString("origin", "playerplaced");
            sapi.World.SpawnEntity(entity);
            NoteLaunch(def, x, z, yaw, ep.Pos.Dimension);
            spawned = entity;
            return null;
        }

        /// <summary>
        /// A target for her guns: a carrack on the water abeam of a ship, heading the same way, this far off centre to
        /// centre (further if there's no room there), on this side (-1 port, +1 starboard, 0 the side the player looks to);
        /// with no ship, that far in front of the player, side-on. Returns an error lang code or null.
        /// </summary>
        public string TrySpawnTarget(IServerPlayer player, EntityShip from, double distance, int side, out EntityShip spawned)
        {
            spawned = null;
            const string code = "carrack";
            var def = GetShip(code);
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", code));
            if (def == null || type == null) return "hansa:no-such-ship";
            var ep = player.Entity;
            double yaw, cx, cz, y0, ux, uz;
            int dim = ep.Pos.Dimension;
            if (from?.Def != null)
            {
                yaw = from.Pos.Yaw;
                cx = from.Pos.X;
                cz = from.Pos.Z;
                y0 = from.FloatY;
                dim = from.Pos.Dimension;
                if (side == 0) side = from.WorldDirToLocal(Math.Sin(ep.Pos.Yaw), 0, Math.Cos(ep.Pos.Yaw)).Z < 0 ? -1 : 1;
                ShipFrame.ToWorld(yaw, 0, 0, side, out ux, out _, out uz);
            }
            else
            {
                yaw = ep.Pos.Yaw + Math.PI / 2;
                cx = ep.Pos.X;
                cz = ep.Pos.Z;
                y0 = ep.Pos.Y;
                ux = Math.Sin(ep.Pos.Yaw);
                uz = Math.Cos(ep.Pos.Yaw);
            }
            for (double d = Math.Max(def.Beam + 2, distance); d <= Math.Max(def.Beam + 2, distance) + 40; d += 2)
            {
                double x = cx + ux * d, z = cz + uz * d;
                var bp = new BlockPos((int)Math.Floor(x), (int)Math.Floor(y0), (int)Math.Floor(z), dim);
                double wy = EntityShip.FindWaterSurface(sapi.World, bp, 6, 20);
                if (double.IsNaN(wy) || EntityShip.PoseBlocked(sapi.World, def, x, wy, z, yaw, dim) || !KeelInWater(def, x, wy, z, yaw, dim)) continue;
                if (ServerShips.Any(s => s.Alive && s.Pos.SquareDistanceTo(x, wy, z) < Math.Pow(def.Beam + 2, 2))) continue;
                var entity = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
                entity.Pos.SetPos(x, wy, z);
                entity.Pos.Dimension = dim;
                entity.Pos.Yaw = GameMath.Mod((float)yaw, GameMath.TWOPI);
                entity.PositionBeforeFalling.Set(x, wy, z);
                entity.Attributes.SetString("origin", "playerplaced");
                sapi.World.SpawnEntity(entity);
                spawned = entity;
                return null;
            }
            return "hansa:no-open-water";
        }

        static double HullMiddle(ShipDefinition d)
        {
            if (d?.HullSamples == null || d.HullSamples.Length == 0) return 0;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var s in d.HullSamples) { lo = Math.Min(lo, s[0]); hi = Math.Max(hi, s[0]); }
            return (lo + hi) / 2;
        }

        bool KeelInWater(ShipDefinition def, double x, double y, double z, double yaw, int dim)
        {
            var ba = sapi.World.BlockAccessor;
            var bp = new BlockPos(dim);
            foreach (var s in def.HullSamples)
            {
                if (s[1] > def.KeelY + 0.5) continue;
                ShipFrame.ToWorld(yaw, s[0], s[1], s[2], out double wx, out double wy, out double wz);
                bp.Set((int)Math.Floor(x + wx), (int)Math.Floor(y + wy), (int)Math.Floor(z + wz));
                if (!(ba.GetBlock(bp, BlockLayersAccess.Fluid)?.IsLiquid() ?? false)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ commands

        void RegisterCommands(ICoreServerAPI api)
        {
            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands.Create("hansa")
                .WithDescription("Hansa ships")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .BeginSubCommand("spawn")
                    .WithDescription("Put a ship on the water in front of you (creative / admins)")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalWord("ship"))
                    .HandleWith(args =>
                    {
                        string code = args[0] as string ?? "carrack";
                        string err = TrySpawnShip((IServerPlayer)args.Caller.Player, code, out var ship);
                        return err != null ? TextCommandResult.Error(Lang.Get(err)) : TextCommandResult.Success($"{ship.GetName()} is on the water.");
                    })
                .EndSubCommand()
                .BeginSubCommand("remove")
                    .WithDescription("Remove the nearest ship (its cargo is dropped)")
                    .RequiresPrivilege(Privilege.gamemode)
                    .HandleWith(args =>
                    {
                        var ship = Nearest(args.Caller.Player.Entity.Pos.XYZ, 48);
                        if (ship == null) return TextCommandResult.Error("No ship within 48 blocks.");
                        ship.Die(EnumDespawnReason.Removed);
                        return TextCommandResult.Success("Ship removed.");
                    })
                .EndSubCommand()
                .BeginSubCommand("board")
                    .WithDescription("Climb aboard the nearest ship")
                    .HandleWith(args =>
                    {
                        var ep = args.Caller.Player.Entity;
                        var ship = Nearest(ep.Pos.XYZ, 48);
                        if (ship == null) return TextCommandResult.Error("No ship within 48 blocks.");
                        var b = ship.Def.BoardPoints[0];
                        ship.LocalToWorld(b[0], b[1], b[2], out double wx, out double wy, out double wz);
                        ep.TeleportToDouble(wx, wy, wz, () => ship.Board(ep, new Vec3d(b[0], b[1], b[2])));
                        return TextCommandResult.Success();
                    })
                .EndSubCommand()
                .BeginSubCommand("info")
                    .WithDescription("State of the nearest ship")
                    .HandleWith(args =>
                    {
                        var ship = Nearest(args.Caller.Player.Entity.Pos.XYZ, 64);
                        if (ship == null) return TextCommandResult.Error("No ship within 64 blocks.");
                        int crew = ship.Seats.Count(s => s.Passenger != null);
                        return TextCommandResult.Success(string.Format(
                            "{0} #{1} at {2:0.0},{3:0.0},{4:0.0} heading {5:0}°, {6:0.0} b/s, sails {7} ({8:0%}), rudder {9:0.00}, {10}{11}, crew {12}; " +
                            "afloat at y {13:0.00}, heaving {14:0.00} on waves of {15:0.0} blocks",
                            ship.GetName(), ship.EntityId, ship.Pos.X, ship.Pos.Y, ship.Pos.Z, HeadingDeg(ship.Pos.Yaw), ship.Speed,
                            ship.SailSet + (ship.Crawl ? " (under the sweeps)" : ""), ship.SailFraction, ship.Rudder, ship.Anchored ? "anchored" : "anchor up", ship.Aground ? ", aground" : "", crew,
                            ship.BaseY, ship.Heave, ship.Waves.SignificantHeight * (ship.SeaSys?.WaveFactorAt(ship.Pos.X, ship.Pos.Z) ?? 1)) +
                            "\n" + DamageSummary(ship));
                    })
                .EndSubCommand()
                .BeginSubCommand("damage")
                    .WithDescription("Damage the nearest ship (the one you're on): 'look [energy kJ]' the part you look at, 'part <name> [0..1]' " +
                                     "(hull-p-12-2, hull-s-12-2, deck-main-10, rudder, mast-main, sail-main-course, rigging-main), " +
                                     "'at <x> <y> <z> [energy] [radius]' a ship-local point, 'age <days>' her seams and bottom, 'list' what's hurt")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalAll("what"))
                    .HandleWith(args => DamageCommand((IServerPlayer)args.Caller.Player, args[0] as string))
                .EndSubCommand()
                .BeginSubCommand("repairall")
                    .WithDescription("Mend every part of the nearest ship, pump her dry and clean her bottom")
                    .RequiresPrivilege(Privilege.gamemode)
                    .HandleWith(args =>
                    {
                        var ship = ShipFor(args.Caller.Player.Entity);
                        if (ship == null) return TextCommandResult.Error("No ship within 64 blocks.");
                        ship.RepairAll();
                        return TextCommandResult.Success($"{ship.GetName()} is sound and dry.");
                    })
                .EndSubCommand()
                .BeginSubCommand("flood")
                    .WithDescription("Pour this much water (tonnes; negative takes it out) into the nearest ship's hold")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalWord("tonnes"))
                    .HandleWith(args =>
                    {
                        var ship = ShipFor(args.Caller.Player.Entity);
                        if (ship == null) return TextCommandResult.Error("No ship within 64 blocks.");
                        if (!double.TryParse(args[0] as string ?? "50", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t))
                            return TextCommandResult.Error("/hansa flood <tonnes>");
                        ship.Flood(t);
                        return TextCommandResult.Success(DamageSummary(ship));
                    })
                .EndSubCommand()
                .BeginSubCommand("fire")
                    .WithDescription("Fire the guns of the ship you're on (else the nearest): a gun's code (gun-port-1), port, starboard or all " +
                                     "(the default); [elevation] [train] in degrees lay them first, at once, as far as they go (elevation: negative is " +
                                     "down; train: positive toward the bow); else they fire as they're laid")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalWord("which"), parsers.OptionalWord("elevation"), parsers.OptionalWord("train"))
                    .HandleWith(args => FireCommand((IServerPlayer)args.Caller.Player, args[0] as string, args[1] as string, args[2] as string))
                .EndSubCommand()
                .BeginSubCommand("target")
                    .WithDescription("Put a carrack on the water abeam of the ship you're on (else the nearest), heading the same way, as a target: " +
                                     "[distance] centre to centre (default 60: her guns' level shot meets her waterline there) [port|starboard] (else the side you look to)")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalWord("distance"), parsers.OptionalWord("side"))
                    .HandleWith(args =>
                    {
                        var player = (IServerPlayer)args.Caller.Player;
                        double dist = double.TryParse(args[0] as string ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dd) ? dd : 60;
                        int side = (args[1] as string)?.ToLowerInvariant() switch { "port" => -1, "starboard" => 1, _ => 0 };
                        string err = TrySpawnTarget(player, ShipFor(player.Entity), dist, side, out var target);
                        return err != null ? TextCommandResult.Error(Lang.Get(err))
                            : TextCommandResult.Success($"{target.GetName()} #{target.EntityId} is on the water as a target, {dist:0} blocks off.");
                    })
                .EndSubCommand()
                .BeginSubCommand("wreck")
                    .WithDescription("Make the nearest sunk ship's wreck now; or 'here [seed]': lay a carrack's wreck (made up: holed, heeled, no cargo) on the seabed 24 blocks ahead of you")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalWord("here"), parsers.OptionalWord("seed"))
                    .HandleWith(args => WreckCommand((IServerPlayer)args.Caller.Player, args[0] as string, args[1] as string))
                .EndSubCommand();
        }

        /// <summary>/hansa wreck: the nearest sunk ship's wreck now; /hansa wreck here [seed]: a made-up carrack's wreck ahead (no ship needed).</summary>
        TextCommandResult WreckCommand(IServerPlayer player, string what, string seedArg)
        {
            if (Wrecks == null) return TextCommandResult.Error("No wrecks on this side.");
            if (what?.ToLowerInvariant() != "here")
            {
                var ship = ShipFor(player.Entity);
                if (ship == null) return TextCommandResult.Error("No ship within 64 blocks.");
                string why = Wrecks.Convert(ship);
                return why != null ? TextCommandResult.Error($"{ship.GetName()}: {why}" + (ship.Stage == SinkStage.Sunk ? "." : " (sink her first: /hansa flood 2000 in deep water)."))
                    : TextCommandResult.Success($"{ship.GetName()} #{ship.EntityId} is being made a wreck.");
            }
            var def = GetShip("carrack");
            var ep = player.Entity;
            if (def?.DamageParts == null || ep == null) return TextCommandResult.Error("No carrack design.");
            long seed = long.TryParse(seedArg ?? "", out long sv) ? sv : sapi.World.Rand.Next();
            var rand = new Random((int)(seed ^ seed >> 32));
            double yaw = ep.Pos.Yaw, x = ep.Pos.X + Math.Sin(yaw) * 24, z = ep.Pos.Z + Math.Cos(yaw) * 24;
            // the seabed there
            var site = new WorldWreckSite(sapi.World.BlockAccessor, 0, ep.Pos.Dimension);
            double floor = double.NaN;
            try
            {
                for (int y = (int)ep.Pos.Y + 8; y > 1 && double.IsNaN(floor); y--)
                    if (site.At((int)Math.Floor(x), y, (int)Math.Floor(z), out float top) == WreckGround.Ground) floor = y + top;
            }
            catch (WreckSiteUnloaded) { return TextCommandResult.Error("The sea there isn't loaded."); }
            if (double.IsNaN(floor)) return TextCommandResult.Error("No seabed found ahead.");
            // holed along one side below her waterline, a few cracks; heeled over to that side, down by the head
            var l = def.DamageParts;
            var hurt = new float[l.Count];
            int side = rand.Next(2) == 0 ? -1 : 1;
            var below = l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Part.Side == side && p.Center.Y < def.Waterline).ToList();
            for (int n = 0; n < 8 && below.Count > 0; n++) hurt[below[rand.Next(below.Count)].Index] = 0.72f + 0.28f * (float)rand.NextDouble();
            for (int n = 0; n < 12; n++) { var p = l.Parts[rand.Next(l.Count)]; if (p.Kind == DamageKind.Hull) hurt[p.Index] = Math.Max(hurt[p.Index], 0.4f); }
            var input = new WreckInput
            {
                Def = def, X = x, Y = floor - def.Grid.OY, Z = z, Yaw = yaw + Math.PI / 2,
                List = side * (8 + 20 * rand.NextDouble()) * GameMath.DEG2RAD, Trim = -(1 + 5 * rand.NextDouble()) * GameMath.DEG2RAD,
                Hurt = hurt, Clad = rand.NextDouble() < 0.3, Seed = seed,
            };
            long id = Wrecks.Place(input, null);
            return TextCommandResult.Success($"A carrack's wreck (#{id}, seed {seed}) is being laid on the seabed 24 blocks ahead, at {x:0},{floor:0},{z:0}.");
        }

        /// <summary>
        /// /hansa fire: a gun, a side's or all her guns, laid first at once if an elevation or a train is given (as far
        /// as each goes); what fired, how they were laid, and what didn't.
        /// </summary>
        TextCommandResult FireCommand(IServerPlayer player, string which, string elevation, string train) =>
            FireCommand(ShipFor(player.Entity), player?.Entity, player, which, elevation, train);

        /// <summary>/hansa fire on a ship (the command, and the self-test with no player).</summary>
        public TextCommandResult FireCommand(EntityShip ship, Entity firer, IServerPlayer player, string which, string elevation, string train)
        {
            if (ship?.Def == null) return TextCommandResult.Error("No ship within 64 blocks.");
            if (!Config.Cannons) return TextCommandResult.Error("Cannons are switched off (Cannons in hansa.json).");
            const string usage = "/hansa fire [gun|port|starboard|stern|all] [elevation, -45..45 degrees] [train, -45..45 degrees toward the bow (a stern chaser: toward starboard)]";
            double? Degrees(string s, out bool bad)
            {
                bad = false;
                if (s == null || s == "-" || s == "_") return null;
                bad = !double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || Math.Abs(v) > 45;
                return bad ? null : v;
            }
            double? elev = Degrees(elevation, out bool badE), trn = Degrees(train, out bool badT);
            if (badE || badT) return TextCommandResult.Error(usage);
            which = (which ?? "all").ToLowerInvariant();
            // (0.3.25) port and starboard: the broadsides (guns out of her sides); stern: the stern chasers (out of her transom)
            var guns = ship.Def.Fixtures.Where(f => f.Type == "gun" && ship.GunMounted(f) && (which == "all" || f.Code == which ||
                which == "port" && !ShipCompartments.AlongX(f) && (f.Out?[2] ?? 0) < 0 ||
                which == "starboard" && !ShipCompartments.AlongX(f) && (f.Out?[2] ?? 0) > 0 ||
                which == "stern" && ShipCompartments.AlongX(f) && f.Out[0] < 0 || which == "bow" && ShipCompartments.AlongX(f) && f.Out[0] > 0)).ToList();
            if (guns.Count == 0) return TextCommandResult.Error($"{ship.GetName()} has no gun '{which}' (her guns: {string.Join(", ", ship.Def.Fixtures.Where(f => f.Type == "gun").Select(f => f.Code))}).");
            int fired = 0;
            var not = new List<string>();
            var lays = new List<string>();
            foreach (var g in guns)
            {
                // laid first, at once (no crew to wait for), as far as it goes
                if (elev != null || trn != null)
                {
                    var now = ship.GunLayNow(g);
                    var want = new GunLay(trn ?? now.Train, elev ?? now.Elevation);
                    ship.SetGunLay(g, want);
                    var got = ship.GunLayNow(g);
                    lays.Add($"{g.Code} {GunLaying.Text(got, g)}" + (GunLaying.Beyond(g, want, ship.GunGateOpen(g)) ? " (as far as it goes)" : ""));
                }
                // (the command: a housed gun is run out at once)
                ship.OpenGunPort(g, true);
                string err = ship.FireGun(g, firer, player);
                if (err == null) fired++;
                else not.Add($"{g.Code}: {Lang.Get(err, Math.Ceiling(ship.GunReloading(g)))}");
            }
            return TextCommandResult.Success($"{ship.GetName()}: {fired} of {guns.Count} fired" + (lays.Count > 0 ? ", laid " + string.Join("; ", lays) : "") +
                (not.Count > 0 ? " (" + string.Join("; ", not) + ")" : "") + ".");
        }

        /// <summary>The ship you're aboard, else the nearest within 64 blocks.</summary>
        EntityShip ShipFor(Entity e) => e is EntityAgent a && a.MountedOn is ShipSeat seat ? seat.Ship : Nearest(e.Pos.XYZ, 64);

        /// <summary>Her water, how deep and how far over it puts her, her state, and how many parts are hurt.</summary>
        static string DamageSummary(EntityShip ship)
        {
            var l = ship.DamageParts;
            if (l == null || ship.PartDamage == null) return "no damage model";
            int hurt = 0;
            var worst = PartState.Sound;
            for (int i = 0; i < l.Count; i++)
            {
                var st = ship.StateOfPart(i);
                if (st == PartState.Sound) continue;
                hurt++;
                if (st > worst) worst = st;
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}: water {1:0.0} t of {2:0} (reserve {3:0}), {4:+0.0;-0.0} l/s, sinkage {5:0.00}, list {6:0.0}°, {7}{8}; {9} of {10} parts hurt (worst {11}), foul {12:0%}, sail drive {13:0%}",
                ship.Stage, ship.Water, l.Capacity, l.Reserve, ship.WaterRate * 1000, ship.Sinkage, ship.ListAngle * GameMath.RAD2DEG,
                ship.Pumpers > 0 ? ship.Pumpers + " at the pumps" : "no one at the pumps", ship.OnBottom ? ", on the bottom" : "",
                hurt, l.Count, worst, ship.Fouling, ship.SailDrive);
        }

        TextCommandResult DamageCommand(IServerPlayer player, string what)
        {
            var ep = player.Entity;
            var ship = ShipFor(ep);
            if (ship?.DamageParts == null) return TextCommandResult.Error("No ship within 64 blocks.");
            if (!Config.Damage) return TextCommandResult.Error("Damage is switched off (Damage in hansa.json).");
            var w = (what ?? "look").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            double Num(int i, double dflt) => w.Length > i && double.TryParse(w[i], System.Globalization.NumberStyles.Float, inv, out double v) ? v : dflt;
            DamageResult res = null;
            switch (w.Length > 0 ? w[0] : "look")
            {
                case "look":
                {
                    var eye = ep.Pos.XYZ.Add(ep.LocalEyePos);
                    var vd = EntityPos.GetViewVector(ep.Pos.Pitch, ep.Pos.Yaw);
                    var dir = new Vec3d(vd.X, vd.Y, vd.Z);
                    if (!ship.RayHit(eye, dir, 40, null, out var hit)) return TextCommandResult.Error("You aren't looking at her.");
                    var ld = ship.WorldDirToLocal(dir.X, dir.Y, dir.Z);
                    int part = ship.DamagePartFor(hit.Part);
                    // the planking: a shot there; anything else: that part, half wrecked
                    res = hit.Part.Kind is ShipPartKind.Hull or ShipPartKind.Deck
                        ? ship.Damage(hit.Local, ld, (float)Num(1, 120), 0.4f, "test")
                        : part >= 0 ? ship.DamagePart(part, (float)Num(1, 0.5), "test") : null;
                    break;
                }
                case "part":
                {
                    int part = w.Length > 1 ? ship.PartByName(w[1]) : -1;
                    if (part < 0) return TextCommandResult.Error("No such part (hull-p-12-2, hull-s-12-2, deck-main-10, rudder, mast-main, sail-main-course, rigging-main).");
                    res = ship.DamagePart(part, (float)Num(2, 0.8), "test");
                    break;
                }
                case "at":
                {
                    if (w.Length < 4) return TextCommandResult.Error("/hansa damage at <x> <y> <z> [energy] [radius]");
                    var p = new Vec3d(Num(1, 0), Num(2, 0), Num(3, 0));
                    // inward: toward her middle (up, from under her bottom)
                    var dir = p.Y < ship.Def.KeelY + 1 ? new Vec3d(0, 1, 0) : new Vec3d(0, 0, -Math.Sign(p.Z == 0 ? 1 : p.Z));
                    res = ship.Damage(p, dir, (float)Num(4, 120), (float)Num(5, 0.4), "test");
                    break;
                }
                case "age":
                    ship.Age(Num(1, 30));
                    return TextCommandResult.Success(DamageSummary(ship));
                case "list":
                {
                    var l = ship.DamageParts;
                    var lines = Enumerable.Range(0, l.Count).Where(i => ship.StateOfPart(i) != PartState.Sound)
                        .Select(i => $"{ship.PartName(i)}: {ship.StateOfPart(i)} ({ship.PartDamage.Hurt[i]:0.00}/{ship.PartDamage.Wear[i]:0.00})").Take(30);
                    return TextCommandResult.Success(DamageSummary(ship) + "\n" + string.Join("\n", lines));
                }
                default:
                    return TextCommandResult.Error("/hansa damage look|part|at|age|list");
            }
            if (res == null || res.Hits.Count == 0) return TextCommandResult.Success("Nothing was hurt. " + DamageSummary(ship));
            var worst = res.Hits.OrderByDescending(h => h.After).First();
            return TextCommandResult.Success($"Hurt {res.Hits.Count} parts{(res.Pierced ? ", went through" : "")}{(res.Exit != null ? " and out" : "")}: worst {worst.After} ({ship.PartName(worst.Part)}). " +
                DamageSummary(ship));
        }

        EntityShip Nearest(Vec3d pos, double range)
        {
            return ServerShips.Where(s => s.Alive && s.Pos.SquareDistanceTo(pos) < range * range)
                .OrderBy(s => s.Pos.SquareDistanceTo(pos)).FirstOrDefault();
        }

        /// <summary>Compass heading: 0 = north (-Z), 90 = east (+X).</summary>
        public static double HeadingDeg(double yaw)
        {
            double h = Math.Atan2(Math.Sin(yaw), -Math.Cos(yaw)) * GameMath.RAD2DEG;
            return (h + 360) % 360;
        }
    }
}
