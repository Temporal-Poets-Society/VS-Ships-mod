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

namespace Hansa
{
    /// <summary>
    /// A ship. The server sails it (sails, rudder, anchor, grounding); clients smooth its motion between position
    /// packets. Everyone aboard is mounted on one of its seats: a free deck seat per player, the helm and the bunks.
    ///
    /// It rides Sea's waves. Its height is the still-water level (<see cref="BaseY"/>) plus the heave, on the server
    /// and on every client, all worked out from the same waves and wave clock (<see cref="ShipMotion"/>). Pitch, roll
    /// and the lean in the wind are only drawn (clients): walking and collision stay level in the ship's own frame.
    /// </summary>
    public partial class EntityShip : Entity, IMountable
    {
        public ShipDefinition Def;
        public HansaMod Mod;

        // ---- sailing state (server-authoritative, synced in WatchedAttributes "hansa")
        public int SailSet;           // 0 furled, 1 half, 2 full ((0.3.25) the sweeps astern, -1 till 0.3.24, are Crawl now)
        /// <summary>
        /// (0.3.25) Under the sweeps (<see cref="Hansa.Crawl"/>): the sails furled, she creeps ahead or astern at a walk while
        /// W or S is held at the wheel and A/D turn her directly; synced.
        /// </summary>
        public bool Crawl;
        /// <summary>(0.3.25) Under the sweeps: which way they pull now (+1 ahead, -1 astern, 0 holding her); synced, for the HUD.</summary>
        public int CrawlDrive;
        /// <summary>(0.3.25, the self-test) The sweeps' keys as if held at the wheel (ahead -1..1, turn -1 A .. +1 D), or null: the helmsman's.</summary>
        public (int Ahead, float Turn)? CrawlTestInput;
        public float SailFraction;    // 0..1 how far the sails are set (visual + drive)
        /// <summary>Her drift sideways from the wind on her hull and rig (blocks/s, + to starboard), server.</summary>
        public float Leeway;
        /// <summary>Why her sails give her little way now (a lang key for the helm's HUD), or null; synced.</summary>
        public string WhySlow;
        public float Rudder;          // -1 port .. 1 starboard
        /// <summary>Client: the rudder as drawn (the wheel, the tiller): follows the synced value smoothly at the helm's own rate.</summary>
        public float RudderShown;
        public float Speed;           // blocks/s along the heading (negative = astern)
        public float YawRate;         // rad/s
        /// <summary>Sailing by the wind (config SailByWind); off, the sails are a throttle.</summary>
        public bool SailByWind = true;
        /// <summary>Her top speed now (the design's, the server's SpeedMultiplier, copper sheathing).</summary>
        public float MaxSpeedNow;
        /// <summary>The server's PointCloser (degrees closer to the wind than her design; synced).</summary>
        public float PointCloser;
        SailingDef sails, sailsOf;
        float sailsCloser;
        /// <summary>How she sails: her design's <see cref="ShipDefinition.Sailing"/> tuned by <see cref="PointCloser"/>.</summary>
        public SailingDef SailingNow
        {
            get
            {
                var d = Def?.Sailing;
                if (d == null) return null;
                if (sails == null || sailsOf != d || sailsCloser != PointCloser)
                {
                    sails = Sailing.Tuned(d, PointCloser);
                    sailsOf = d;
                    sailsCloser = PointCloser;
                }
                return sails;
            }
        }
        public bool Anchored = true;
        public bool Aground;
        /// <summary>A boat's oars: +1 pulling ahead, -1 backing, 0 at rest (synced, for the stroke drawn).</summary>
        public int Rowing;
        /// <summary>A boat's rowing thwarts.</summary>
        public readonly List<RowSeat> RowSeats = new();
        /// <summary>A boat's mast that folds (its fixture's code), or null.</summary>
        string mastFixture;
        /// <summary>Her mast is up (or doesn't fold): the sail can be set.</summary>
        public bool MastUp => mastFixture == null || !OpenFittings.Contains(mastFixture);

        readonly Dictionary<string, ShipSeat> seats = new();
        IMountableSeat[] seatArray = Array.Empty<IMountableSeat>();
        public HelmSeat Helm;
        public readonly Dictionary<string, InventoryGeneric> Storages = new();
        public readonly HashSet<string> LitStoves = new();

        // ---- fittings (synced in WatchedAttributes "hansaFit")
        /// <summary>This ship's collision: the design's voxels plus closed doors and gates, and the chests and beds aboard.</summary>
        public VoxelGrid Grid;
        /// <summary>Her collision without the partitions and shut doors put aboard: what a new piece is checked against (it may stand right up to a partition).</summary>
        public VoxelGrid PlaceGrid;
        /// <summary>Doors and gates standing open (all closed on a new ship).</summary>
        public readonly HashSet<string> OpenFittings = new();
        /// <summary>Vanilla lanterns, chests and beds put on attachment points, by fixture code.</summary>
        public readonly Dictionary<string, ItemStack> Attached = new();
        /// <summary>Copper plates nailed on so far (sheathed once it reaches Def.CladdingPlates).</summary>
        public int CladPlates;
        /// <summary>Her name, painted on the name board (null until someone names her).</summary>
        public string ShipName;
        /// <summary>The figurehead she carries (one of the figurehead fixture's options; null = the first).</summary>
        public string Figurehead;
        /// <summary>How her name is painted on the board: colours, size, italic (the user's note 68).</summary>
        public NameStyle NameStyle = new();
        public const int MaxNameLength = 24;
        /// <summary>Bumped whenever fittings change, so the renderer rebuilds what it draws.</summary>
        public int FitVersion;
        /// <summary>
        /// The players whose spawn is bound to her (at the plate by the helm), by uid; synced in WatchedAttributes
        /// "hansaSpawn". The bindings themselves are the server's (<see cref="ShipSpawns"/>).
        /// </summary>
        public readonly HashSet<string> SpawnBound = new();
        public bool Clad => Def != null && CladPlates >= Def.CladdingPlates;

        // server bookkeeping
        bool prevFwd, prevBack, prevJump, prevCtrl;
        float lastCrawlYaw;
        float unmannedFor, syncAccum, waterCheckAccum, passengerCheckAccum, weedAccum;
        readonly HashSet<long> hitsAhead = new(), hitsHere = new();
        double weedX = double.NaN, weedZ, weedYaw;
        double targetY = double.NaN;
        readonly Dictionary<string, PendingPassenger> pendingPassengers = new();

        // ---- riding the waves
        /// <summary>The still-water level the ship floats at (world y of its waterline). Pos.Y is this plus the heave.</summary>
        public double BaseY = double.NaN;
        /// <summary>
        /// The wave motion now. Server: the heave (real), pitch and roll 0. Client: what's drawn, scaled by the
        /// player's SeaMotion setting; Roll includes the lean in the wind (Heel).
        /// </summary>
        public double Heave, Pitch, Roll, Heel;
        readonly Sea.WavePlace waves = new();
        public Sea.SeaSystem SeaSys;
        double writtenY = double.NaN;
        double renderBaseY = double.NaN, fullHeave;
        long jumpMs;

        // client smoothing
        public double RenderYaw;
        public readonly Vec3d RenderPos = new Vec3d();
        readonly Vec3d targetPos = new Vec3d();
        double targetYaw;
        long targetMs;
        bool smoothInit;
        readonly Vec3d lastWritten = new Vec3d();

        class PendingPassenger
        {
            public string Uid;
            public string SeatId;
            public double X, Y, Z;
            public double Since;
        }

        public EntityShip()
        {
            SimulationRange = 256;
        }

        // The crosshair catches her on clients (IntersectsRay, and Patches' postfix on the game's selection ray trace adds
        // the ships its chunk walk misses). On the server it stays false: arrows (EntityProjectile) and creature AI take an
        // interactable entity for a target, as the box round her middle.
        public override bool IsInteractable => Api?.Side == EnumAppSide.Client;
        public override bool ApplyGravity => false;
        public override bool ShouldReceiveDamage(DamageSource damageSource, float damage) => false;
        public override double FrustumSphereRadius => 40;
        public override bool CanStepPitch => false;
        // Stay known to (and drawn on) clients even while the chunk under the ship hasn't reached them yet, e.g. for a
        // moment after the Cylinder World seam jump; otherwise the ship is dropped and re-sent (it "pops in").
        public override bool AllowOutsideLoadedRange => true;
        public override bool CanSwivel => false;

        // ------------------------------------------------------------------ setup

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            Mod = api.ModLoader.GetModSystem<HansaMod>();
            SeaSys = api.ModLoader.GetModSystem<Sea.SeaSystem>();
            string code = properties.Attributes?["ship"].AsString("carrack") ?? "carrack";
            Def = Mod.GetShip(code);
            if (Def == null)
            {
                api.Logger.Error("[hansa] Entity {0}: no ship definition '{1}'", Code, code);
                return;
            }

            SetCollisionBox(6, 4);
            SetSelectionBox(6, 4);
            // Clients: what the crosshair meets is her hull (IntersectsRay), not a box. Vanilla still measures the reach of
            // an attack by the selection box (the red "in reach" crosshair, the attack packet), and a 6 x 4 box round her
            // middle would light it up for anyone amidships: a speck far below her keel is never in reach.
            if (api.Side == EnumAppSide.Client) SelectionBox = new Cuboidf(-0.01f, -256f, -0.01f, 0.01f, -255.98f, 0.01f);
            Grid = Def.Grid.Clone();

            foreach (var f in Def.Fixtures)
            {
                switch (f.Type)
                {
                    case "helm":
                        Helm = new HelmSeat(this, f);
                        seats[Helm.SeatId] = Helm;
                        break;
                    case "bed":
                        var b = new BunkSeat(this, f);
                        seats[b.SeatId] = b;
                        break;
                    case "oars":
                        var rs = new RowSeat(this, f);
                        seats[rs.SeatId] = rs;
                        RowSeats.Add(rs);
                        break;
                    case "gun":
                        // the gun captain's place, behind the breech
                        var gs = new GunSeat(this, f);
                        seats[gs.SeatId] = gs;
                        break;
                    case "mast":
                        mastFixture ??= f.Code;
                        break;
                    case "attach" when f.Kind == "bed":
                        var ab = new BunkSeat(this, f);
                        seats[ab.SeatId] = ab;
                        break;
                    case "seat":
                        // a seat built into her (the heads): sat on as a chair put aboard is
                        var ss = new PieceSeat(this, f);
                        seats[ss.SeatId] = ss;
                        break;
                    case "storage":
                        var inv = new InventoryGeneric(f.Slots, "hansa-" + f.Code, EntityId.ToString(), api);
                        Storages[f.Code] = inv;
                        if (api.Side == EnumAppSide.Server)
                        {
                            string fcode = f.Code;
                            inv.SlotModified += _ => SaveInventory(fcode);
                        }
                        break;
                }
            }
            RebuildSeatArray();

            LoadState();
            // (0.3.20, step B) her deck cells, before her collision is first built
            InitSlots();
            // (0.3.21, step C) her gun places: which have their gun
            InitGunPlaces();
            LoadFittings();
            // her pieces (vanilla blocks put aboard) and her paint (ShipDecor)
            InitDecor();
            // ((0.3.27, note 207) where balls struck her)
            InitScars();
            // ((0.3.27, note 219) her brows run out to a quay
            InitBrows();
            LoadInventories();
            LoadSpawnBound();
            // her crew list and whether she's laid up (ShipCrew)
            LoadCrew();
            LoadLaidUp();
            LoadGunLays();
            InitDamage();
            // ((0.3.30, note 234) an older save's shot scars: cracked slots now)
            if (api.Side == EnumAppSide.Server) ScarsToCracks();
            // her firehearth: vanilla's firepit inventory and fire (ShipHearth); her clay oven likewise (ShipOven)
            InitHearth();
            InitOven();

            if (api.Side == EnumAppSide.Server)
            {
                LoadPendingPassengers();
                // a new ship's lanterns, an older ship's things from places her design no longer has (ShipDecor)
                MigrateDecor();
                RescueOrphanedCargo();
            }
            else
            {
                WatchedAttributes.RegisterModifiedListener("hansa", LoadState);
                WatchedAttributes.RegisterModifiedListener("hansaInv", LoadInventories);
                WatchedAttributes.RegisterModifiedListener("hansaFit", () => { LoadFittings(); LoadInventories(); });
                WatchedAttributes.RegisterModifiedListener("hansaSpawn", LoadSpawnBound);
                WatchedAttributes.RegisterModifiedListener(CrewKey, LoadCrew);
                WatchedAttributes.RegisterModifiedListener("hansaLaidUp", LoadLaidUp);
                WatchedAttributes.RegisterModifiedListener("hansaGuns", LoadGunLays);
                WatchedAttributes.RegisterModifiedListener("hansaHearth", LoadHearth);
                WatchedAttributes.RegisterModifiedListener("hansaOven", LoadOven);
                RenderPos.Set(Pos.X, Pos.Y, Pos.Z);
                RenderYaw = Pos.Yaw;
                renderBaseY = double.IsNaN(BaseY) ? Pos.Y : BaseY;
                targetPos.Set(RenderPos);
                targetYaw = RenderYaw;
                lastWritten.Set(RenderPos);
                smoothInit = true;
                Mod.Client?.OnShipLoaded(this);
            }
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            if (Api is ICoreClientAPI) Mod?.Client?.OnShipUnloaded(this);
            if (Api is ICoreServerAPI sapi)
            {
                foreach (var inv in Storages.Values.Cast<InventoryBase>().Append(HearthInv))
                {
                    if (inv == null) continue;
                    foreach (string uid in inv.openedByPlayerGUIds.ToArray())
                    {
                        sapi.World.PlayerByUid(uid)?.InventoryManager.CloseInventoryAndSync(inv);
                    }
                }
                if (despawn?.Reason == EnumDespawnReason.Removed || despawn?.Reason == EnumDespawnReason.Death)
                {
                    foreach (var s in seatArray.ToArray())
                    {
                        if (s.Passenger is EntityAgent a) a.TryUnmount();
                    }
                    foreach (var inv in Storages.Values) inv.DropAll(Pos.XYZ.Add(0, Def?.DeckY ?? 3, 0));
                    HearthInv?.DropAll(Pos.XYZ.Add(0, Def?.DeckY ?? 3, 0));
                    OvenInv?.DropAll(Pos.XYZ.Add(0, Def?.DeckY ?? 3, 0));
                    foreach (var stack in Attached.Values) World.SpawnItemEntity(stack, Pos.XYZ.Add(0, Def?.DeckY ?? 3, 0));
                    Attached.Clear();
                    DropPieces();
                }
            }
        }

        // ------------------------------------------------------------------ frame

        // Ship-local <-> world, as the ship is drawn: with its pitch and roll on clients (so seats, lights and clicks
        // follow the tilted deck), level on the server. Walking itself is in the level ship-local frame.

        public void LocalToWorld(double lx, double ly, double lz, out double wx, out double wy, out double wz)
        {
            ShipFrame.ToWorld(Pos.Yaw, Pitch, Roll, lx, ly, lz, out wx, out wy, out wz);
            wx += Pos.X;
            wy += Pos.Y;
            wz += Pos.Z;
        }

        public Vec3d WorldToLocal(double wx, double wy, double wz)
        {
            ShipFrame.ToLocal(Pos.Yaw, Pitch, Roll, wx - Pos.X, wy - Pos.Y, wz - Pos.Z, out double lx, out double ly, out double lz);
            return new Vec3d(lx, ly, lz);
        }

        public Vec3d LocalDirToWorld(double lx, double ly, double lz)
        {
            ShipFrame.ToWorld(Pos.Yaw, Pitch, Roll, lx, ly, lz, out double x, out double y, out double z);
            return new Vec3d(x, y, z);
        }

        public Vec3d WorldDirToLocal(double dx, double dy, double dz)
        {
            ShipFrame.ToLocal(Pos.Yaw, Pitch, Roll, dx, dy, dz, out double lx, out double ly, out double lz);
            return new Vec3d(lx, ly, lz);
        }

        /// <summary>
        /// The motion the waves give the ship at this place and heading now, at full strength. Every side gets the
        /// same answer from the same inputs (the waves are Sea's shared ones, the wave clock is shared).
        /// </summary>
        public ShipAttitude WaveMotion(double x, double z, double yaw)
        {
            var sea = SeaSys;
            if (sea?.Spectrum == null || Def == null) return default;
            sea.SampleWaves(waves, x, z);
            Def.Waterplane(out double halfLength, out double halfBeam, out double centreX);
            double steady = ShipMotion.SailSteadiness(SailsDrawing, WindHere(x, z).Strength);
            // (the water in her slows her roll: MotionNow; the heave and pitch are the design's, alike everywhere)
            return ShipMotion.Waves(waves, sea.WaveFactorAt(x, z), waves.Time, x, z, yaw, Speed, halfLength, halfBeam, centreX, MotionNow, steady);
        }

        /// <summary>The waves at the ship as of the last <see cref="WaveMotion"/> (for reports and tests).</summary>
        public Sea.WavePlace Waves => waves;

        public RoomDef RoomAtLocal(Vec3d l) => Def?.RoomAt(l.X, l.Y + 0.5, l.Z);

        // ------------------------------------------------------------------ looking at her (the crosshair)

        /// <summary>
        /// Where a ray (world, as the game's selection ray: dir of any length, range along it) first meets her: her hull,
        /// decks and everything of her collision, or a fixture's selection box in front of it (an empty place only while
        /// holding what goes there, as with clicking). Tilted with her drawn pitch and roll on clients, like
        /// <see cref="WorldToLocal"/>; level on the server.
        /// </summary>
        public bool RayHit(Vec3d origin, Vec3d dir, double range, ItemStack held, out ShipRayHit hit, bool aboard = true)
        {
            hit = default;
            if (Def == null || Grid == null) return false;
            bool got = ShipParts.Ray(Def, Grid, Pos.X, Pos.InternalY, Pos.Z, Pos.Yaw, Pitch, Roll, origin, dir, range,
                f => !ShipMounts.IsLegacyHook(f) && (f.Type != "attach" || f.Shown || Attached.ContainsKey(f.Code) || CoveredBy(f) != null || ShipFittings.Accepts(f, held)),
                f => OpenFittings.Contains(f.Code), f => Attached.TryGetValue(f.Code, out var st) ? st : null, out hit, GunAbsent);
            // a piece put aboard in front of it (ShipDecor)
            got = PieceRay(origin, dir, range, got, ref hit);
            // a sail in front of that (ShipFlooding: to look at it and mend it)
            got = SailRay(origin, dir, range, got, ref hit);
            // with something in hand that mends a panel: through what's aboard and her inside planking to it (RepairPick)
            RepairPicked = -1;
            string code = held?.Collectible?.Code?.ToString();
            if (code != null && PartDamage != null && DamageOn)
            {
                int already = got ? DamagePartFor(hit.Part) : -1;
                // ((0.3.28) a cracked piece (a partition, a door...) with what mends it in hand: that piece)
                bool pieceMends = got && hit.Part.Kind == ShipPartKind.Piece && PieceRepairFor(hit.Part.Index, code) != null;
                if (!pieceMends && (already < 0 || ShipRepairs.For(DamageParts, PartDamage, already, code) == null))
                {
                    RayToLocal(origin, dir, out var o, out var d);
                    double len = Math.Max(1e-9, dir.Length());
                    int part = RepairPick(o, d, range * len, code, aboard, out double t);
                    if (part >= 0)
                    {
                        var face = got ? hit.Face : BlockFacing.FromVector(-dir.X, -dir.Y, -dir.Z);
                        hit = default;
                        hit.Distance = t;
                        hit.Local = o.AddCopy(d.X * t, d.Y * t, d.Z * t);
                        ShipFrame.ToWorld(Pos.Yaw, Pitch, Roll, hit.Local.X, hit.Local.Y, hit.Local.Z, out double wx, out double wy, out double wz);
                        hit.World = new Vec3d(Pos.X + wx, Pos.InternalY + wy, Pos.Z + wz);
                        hit.LocalNormal = new Vec3d(-d.X, -d.Y, -d.Z);
                        hit.Face = face;
                        hit.Part = DamageParts.Parts[part].Part;
                        RepairPicked = part;
                        got = true;
                    }
                }
            }
            return got;
        }

        /// <summary>
        /// The game's selection ray: her voxels in her own frame (vanilla tests one box round her middle). Sets where the
        /// ray meets her and the face, returns the real distance to it, and the part of her it meets as the selection box
        /// index (<see cref="ShipPart"/>).
        /// </summary>
        public override bool IntersectsRay(Ray ray, AABBIntersectionTest interesectionTester, out double intersectionDistance, ref int selectionBoxIndex)
        {
            intersectionDistance = 0;
            var player = (Api as ICoreClientAPI)?.World.Player;
            var held = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            bool aboard = player?.Entity?.MountedOn is ShipSeat seat && seat.Ship == this;
            if (ray == null || !RayHit(ray.origin, ray.dir, ray.Length, held, out var hit, aboard)) return false;
            interesectionTester.hitPosition.Set(hit.World);
            interesectionTester.hitOnBlockFace = hit.Face;
            intersectionDistance = hit.Distance;
            selectionBoxIndex = hit.Part.Encode();
            return true;
        }

        /// <summary>How far a world point is from her (the box round her collision, in her frame; 0 inside it).</summary>
        public double DistanceToHull(double x, double y, double z)
        {
            if (Grid == null) return Pos.DistanceTo(new Vec3d(x, y, z));
            var l = WorldToLocal(x, y, z);
            double dx = Math.Max(0, Math.Max(Grid.OX - l.X, l.X - Grid.MaxX));
            double dy = Math.Max(0, Math.Max(Grid.OY - l.Y, l.Y - Grid.MaxY));
            double dz = Math.Max(0, Math.Max(Grid.OZ - l.Z, l.Z - Grid.MaxZ));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Vanilla's clicks on her do nothing: hers go through Hansa's own packets (ShipClientSystem).</summary>
        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode) { }

        /// <summary>The look-at box: what she is (her name is its title) and the part of her you look at.</summary>
        public override string GetInfoText()
        {
            var sb = new System.Text.StringBuilder();
            if (Def != null && Api is ICoreClientAPI capi)
            {
                if (ShipName != null) sb.AppendLine(Def.Name);
                // her crew offline: laid up (ShipCrew)
                if (LaidUp) sb.AppendLine(Lang.Get("hansa:info-laidup"));
                var sel = capi.World.Player?.CurrentEntitySelection;
                if (sel?.Entity == this)
                {
                    var part = ShipPart.Decode(sel.SelectionBoxIndex);
                    string text = PartText(part);
                    if (!string.IsNullOrEmpty(text)) sb.AppendLine(text);
                    // the part's state, its leak, what mends it (the damage model)
                    DamageInfo(part, sb);
                    // a gun: ready, or reloading; how it's laid
                    if (part.Kind == ShipPartKind.Fixture && part.Index < Def.Fixtures.Length && Def.Fixtures[part.Index].Type == "gun" && GunMounted(Def.Fixtures[part.Index]))
                    {
                        var gf = Def.Fixtures[part.Index];
                        string gun = GunText(gf) + " · " + GunLaying.Text(GunLayNow(gf), gf);
                        sb.AppendLine(gun.Length > 0 ? char.ToUpperInvariant(gun[0]) + gun.Substring(1) : gun);
                    }
                    // the firehearth: burning or cold, what's cooking
                    if (part.Kind == ShipPartKind.Fixture && part.Index < Def.Fixtures.Length && Def.Fixtures[part.Index] == HearthFixture) HearthInfo(sb);
                    // the clay oven: its heat, what's in it
                    if (part.Kind == ShipPartKind.Fixture && part.Index < Def.Fixtures.Length && Def.Fixtures[part.Index] == OvenFixture) OvenInfo(sb);
                    if (part.Kind == ShipPartKind.Fixture && part.Index < Def.Fixtures.Length && Def.Fixtures[part.Index].Kind == "plate" &&
                        Attached.ContainsKey(Def.Fixtures[part.Index].Code))
                    {
                        int bound = SpawnBound.Count;
                        bool mine = capi.World.Player != null && SpawnBound.Contains(capi.World.Player.PlayerUID);
                        sb.AppendLine(mine ? Lang.Get("hansa:plate-info-yours", bound) : Lang.Get("hansa:plate-info", bound));
                    }
                }
            }
            sb.Append(base.GetInfoText());
            return sb.ToString().TrimEnd();
        }

        /// <summary>What a part of her is called: "Hull, port side, forward, below the waterline", "Main deck", a fixture's name.</summary>
        public string PartText(ShipPart part)
        {
            var map = Def?.PartMap;
            if (map == null) return null;
            string text = null;
            switch (part.Kind)
            {
                case ShipPartKind.Hull:
                {
                    // ((0.3.26) a hull panel's height from its middle, not a whole block's band)
                    string height = Def.Hull?.Get($"hull:{(part.Side > 0 ? "s" : "p")}:{part.Station}:{part.Band}") is HullSlotDef hs
                        ? map.HeightZoneAt(part.Station, hs.Centre.Y) : map.HeightZone(part.Station, part.Band);
                    text = Lang.Get(part.InnerLayer ? "hansa:part-ceiling" : "hansa:part-hull", Lang.Get(part.Side > 0 ? "hansa:part-side-starboard" : "hansa:part-side-port"),
                        Lang.Get("hansa:part-at-" + map.LengthZone(part.Station)), Lang.Get("hansa:part-height-" + height),
                        part.Station + 1, part.Station + 2);
                    break;
                }
                case ShipPartKind.Deck when part.Index < Def.Decks.Length:
                {
                    string deck = Lang.Get("hansa:deck-" + Def.Decks[part.Index].Code);
                    text = Lang.Get(part.Band != 0 ? "hansa:part-deck-under" : "hansa:part-deck", deck);
                    // in a cabin (the rooms below the main deck are the decks' own)
                    if (map.RoomOf(part) is RoomDef room && room.Min[1] >= Def.DeckY - 0.01)
                        text = Lang.Get("hansa:part-in", text, Lang.Get("hansa:room-" + room.Code));
                    break;
                }
                case ShipPartKind.Mast when part.Index < Def.Masts.Length:
                    text = Lang.Get("hansa:mast-" + Def.Masts[part.Index].Code);
                    break;
                case ShipPartKind.Structure:
                    text = map.RoomOf(part) is RoomDef r
                        ? Lang.Get("hansa:part-structure", Lang.Get("hansa:room-" + r.Code))
                        : Lang.Get("hansa:part-structure", Lang.Get("hansa:part-at-" + map.LengthZone(part.Station)));
                    break;
                case ShipPartKind.Fixture when part.Index < Def.Fixtures.Length:
                    text = ShipHud.FixtureName(this, Def.Fixtures[part.Index]);
                    break;
                case ShipPartKind.Piece:
                    text = PieceText(part.Index);
                    break;
                case ShipPartKind.Cell:
                    // ((0.3.28, step D2) a deck cell's planking: its deck, where along her)
                    text = CellText(part);
                    break;
            }
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        // ------------------------------------------------------------------ IMountable

        public IMountableSeat[] Seats => seatArray;
        public EntityPos Position => Pos;
        public double StepPitch => 0;
        public Entity Controller => null;
        public Entity OnEntity => this;
        // Null on purpose: the engine copies "mount controls" from every position packet into these, which would
        // fight the helmsman's own keys. The server reads the helm seat's controls directly.
        public EntityControls ControllingControls => null;
        public bool AnyMounted() => seatArray.Any(s => s.Passenger != null);

        void RebuildSeatArray() => seatArray = seats.Values.Cast<IMountableSeat>().ToArray();

        public ShipSeat GetSeat(string seatId) => seatId != null && seats.TryGetValue(seatId, out var s) ? s : null;

        public DeckSeat GetOrCreateDeckSeat(string playerUid, double x, double y, double z)
        {
            string id = "deck:" + playerUid;
            if (seats.TryGetValue(id, out var existing) && existing is DeckSeat ds)
            {
                if (ds.Passenger == null) ds.SetLocal(x, y, z, ds.Flags);
                return ds;
            }
            ds = new DeckSeat(this, playerUid, x, y, z);
            seats[id] = ds;
            RebuildSeatArray();
            return ds;
        }

        public IMountableSeat SeatFromTree(TreeAttribute tree)
        {
            string id = tree.GetString("seatId");
            var s = GetSeat(id);
            if (s != null) return s;
            if (id != null && id.StartsWith("deck:"))
            {
                return GetOrCreateDeckSeat(id.Substring(5), tree.GetDouble("lx"), tree.GetDouble("ly", Def?.DeckY ?? 3.5), tree.GetDouble("lz"));
            }
            return null;
        }

        public void OnSeatChanged(ShipSeat seat, EntityAgent agent, bool mounted)
        {
            if (!mounted && seat is DeckSeat && seat.Passenger == null)
            {
                seats.Remove(seat.SeatId);
                RebuildSeatArray();
            }
            if (Api.Side == EnumAppSide.Server)
            {
                // leaving the helm under sail: it's lashed (ShipWatch)
                if (!mounted && seat is HelmSeat) HelmLeft(agent);
                if (agent is EntityPlayer ep)
                {
                    if (mounted) RecordPassenger(ep.PlayerUID, seat);
                    else if (seat.DoTeleportOnUnmount) ForgetPassenger(ep.PlayerUID);
                }
            }
        }

        // ------------------------------------------------------------------ passengers across save / relog

        void RecordPassenger(string uid, ShipSeat seat)
        {
            var tree = Attributes.GetOrAddTreeAttribute("hansaCrew");
            var t = tree.GetOrAddTreeAttribute(uid);
            t.SetString("seat", seat.SeatId);
            var l = seat.LocalPos;
            t.SetDouble("x", l.X);
            t.SetDouble("y", l.Y);
            t.SetDouble("z", l.Z);
            pendingPassengers.Remove(uid);
        }

        void ForgetPassenger(string uid)
        {
            Attributes.GetTreeAttribute("hansaCrew")?.RemoveAttribute(uid);
        }

        void UpdateCrewPositions()
        {
            var tree = Attributes.GetTreeAttribute("hansaCrew");
            if (tree == null) return;
            foreach (var s in seatArray)
            {
                if (s is ShipSeat ss && s.Passenger is EntityPlayer ep && tree.GetTreeAttribute(ep.PlayerUID) is ITreeAttribute t)
                {
                    t.SetString("seat", ss.SeatId);
                    var l = ss.LocalPos;
                    t.SetDouble("x", l.X);
                    t.SetDouble("y", l.Y);
                    t.SetDouble("z", l.Z);
                }
            }
        }

        void LoadPendingPassengers()
        {
            var tree = Attributes.GetTreeAttribute("hansaCrew");
            if (tree == null) return;
            foreach (var kv in tree)
            {
                if (kv.Value is not ITreeAttribute t) continue;
                pendingPassengers[kv.Key] = new PendingPassenger
                {
                    Uid = kv.Key, SeatId = t.GetString("seat"), X = t.GetDouble("x"), Y = t.GetDouble("y"), Z = t.GetDouble("z"),
                    Since = World.Calendar.TotalHours
                };
            }
        }

        /// <summary>
        /// Players still attached to an older copy of this ship (the entity was unloaded and loaded again) are moved
        /// onto this copy's matching seat.
        /// </summary>
        void AdoptStalePassengers()
        {
            foreach (IServerPlayer plr in ((ICoreServerAPI)Api).World.AllOnlinePlayers)
            {
                if (plr.Entity?.MountedOn is ShipSeat old && old.Ship != this && old.Ship.EntityId == EntityId && plr.Entity.Alive)
                {
                    var tree = new TreeAttribute();
                    old.MountableToTreeAttributes(tree);
                    if (SeatFromTree(tree) is IMountableSeat seat) plr.Entity.TryMount(seat);
                }
            }
        }

        /// <summary>Players whose ship wasn't loaded yet when they logged in get put back aboard.</summary>
        void CheckPendingPassengers()
        {
            AdoptStalePassengers();
            if (pendingPassengers.Count == 0) return;
            var sapi = (ICoreServerAPI)Api;
            foreach (var p in pendingPassengers.Values.ToArray())
            {
                var plr = sapi.World.PlayerByUid(p.Uid) as IServerPlayer;
                if (plr?.Entity == null || plr.ConnectionState != EnumClientState.Playing) continue;
                var ep = plr.Entity;
                if (ep.MountedOn != null)
                {
                    if (ep.MountedOn.Entity?.EntityId == EntityId) pendingPassengers.Remove(p.Uid);
                    continue;
                }
                pendingPassengers.Remove(p.Uid);
                if (!ep.Alive) continue;
                var seat = (p.SeatId?.StartsWith("bunk:") == true ? null : GetSeat(p.SeatId)) ?? GetOrCreateDeckSeat(p.Uid, p.X, p.Y, p.Z);
                if (seat is FixtureSeat fs && fs.Passenger != null) seat = GetOrCreateDeckSeat(p.Uid, p.X, p.Y, p.Z);
                var sp = seat.SeatPosition;
                if (ep.Pos.DistanceTo(sp.XYZ) > 64)
                {
                    ep.TeleportToDouble(sp.X, sp.Y, sp.Z, () => ep.TryMount(seat));
                }
                else ep.TryMount(seat);
            }
        }

        // ------------------------------------------------------------------ boarding and standing up (server)

        /// <summary>Is there room aboard for this player (a boat takes so many)?</summary>
        public bool HasRoomFor(Entity ep)
        {
            if (Def == null || Def.MaxAboard <= 0) return true;
            int aboard = 0;
            foreach (var s in seatArray)
            {
                if (s.Passenger == null) continue;
                if (s.Passenger.EntityId == ep.EntityId) return true;
                aboard++;
            }
            return aboard < Def.MaxAboard;
        }

        /// <summary>
        /// Pick the boat up (Shift + right-click, nobody aboard, no cargo in her): it goes into the player's inventory as
        /// the item she was put in the water from. Returns an error lang code or null.
        /// </summary>
        public string TryPickUp(IServerPlayer player)
        {
            if (Def?.PickUpItem == null) return "hansa:pickup-cannot";
            if (AnyMounted()) return "hansa:pickup-aboard";
            if (Attached.Count > 0 || Pieces.Count > 0) return "hansa:pickup-cargo";
            foreach (var inv in Storages.Values) if (!inv.Empty) return "hansa:pickup-cargo";
            if (HearthInv != null && !HearthInv.Empty) return "hansa:pickup-cargo";
            if (OvenInv != null && !OvenInv.Empty) return "hansa:pickup-cargo";
            var item = World.GetItem(new AssetLocation(Def.PickUpItem));
            if (item == null) return "hansa:pickup-cannot";
            var stack = new ItemStack(item);
            if (ShipName != null)
            {
                stack.Attributes.SetString("shipName", ShipName);
                var style = new TreeAttribute();
                NameStyle.ToTree(style);
                stack.Attributes["shipNameStyle"] = style;
            }
            if (!player.InventoryManager.TryGiveItemstack(stack, true)) World.SpawnItemEntity(stack, player.Entity.Pos.XYZ.Add(0, 0.5, 0));
            World.Logger.Audit("{0} picked up a {1} at {2}.", player.PlayerName, Code, Pos);
            Die(EnumDespawnReason.PickedUp);
            return null;
        }

        public bool Board(EntityPlayer ep, Vec3d local)
        {
            if (Def == null || !ep.Alive || !HasRoomFor(ep) || GoneDown) return false;
            var mover = new DeckMover(local.X, local.Y, local.Z);
            mover.Unstick(Grid);
            var seat = GetOrCreateDeckSeat(ep.PlayerUID, mover.X, mover.Y, mover.Z);
            return ep.TryMount(seat);
        }

        public void StandUp(EntityAgent agent, FixtureDef near)
        {
            if (!(agent is EntityPlayer ep) || !ep.Alive) return;
            // from a gun: where the gun captain stood, behind the breech, if there's room
            // (from the heads: the deck spot in front of the seat, if there's room)
            var spot = near.Type == "gun" && StandsFree(GunSeatLocal(near)) is Vec3d at ? at
                : near.Feet?.Length == 3 && StandsFree(new Vec3d(near.Feet[0], near.Feet[1], near.Feet[2])) is Vec3d feet ? feet
                : FindStandSpot(near.PosVec, near.SelBox);
            var seat = GetOrCreateDeckSeat(ep.PlayerUID, spot.X, spot.Y, spot.Z);
            seat.SetLocal(spot.X, spot.Y, spot.Z, DeckSeat.FlagOnGround);
            ep.TryMount(seat);
        }

        /// <summary>This spot (feet, ship-local) if someone can stand there (room, and a floor under it), else null.</summary>
        public Vec3d StandsFree(Vec3d p)
        {
            var g = Grid;
            if (g == null || p == null) return null;
            if (g.Collides(p.X - DeckMover.HalfWidth, p.Y + 0.01, p.Z - DeckMover.HalfWidth, p.X + DeckMover.HalfWidth, p.Y + DeckMover.Height, p.Z + DeckMover.HalfWidth)) return null;
            return DeckMover.HasFloor(g, p.X, p.Y, p.Z) ? p : null;
        }

        /// <summary>A free spot with floor near a fixture, searched in rings around it.</summary>
        public Vec3d FindStandSpot(Vec3d near, Cuboidd avoid)
        {
            var g = Grid;
            double baseY = avoid != null ? avoid.Y1 : near.Y;
            for (double r = 0.75; r <= 3.01; r += 0.25)
            {
                for (int a = 0; a < 16; a++)
                {
                    double ang = a * Math.PI / 8;
                    double x = near.X + Math.Cos(ang) * r, z = near.Z + Math.Sin(ang) * r;
                    if (avoid != null && x + DeckMover.HalfWidth > avoid.X1 && x - DeckMover.HalfWidth < avoid.X2 &&
                        z + DeckMover.HalfWidth > avoid.Z1 && z - DeckMover.HalfWidth < avoid.Z2) continue;
                    for (double dy = 0; dy <= 1.01; dy += 0.25)
                    {
                        foreach (double y in new[] { baseY + dy, baseY - dy })
                        {
                            if (g.Collides(x - DeckMover.HalfWidth, y, z - DeckMover.HalfWidth, x + DeckMover.HalfWidth, y + DeckMover.Height, z + DeckMover.HalfWidth)) continue;
                            if (!DeckMover.HasFloor(g, x, y, z)) continue;
                            return new Vec3d(x, y, z);
                        }
                    }
                }
            }
            var b = Def.BoardPoints.Length > 0 ? Def.BoardPoints[0] : new double[] { 0, Def.DeckY, 0 };
            return new Vec3d(b[0], b[1], b[2]);
        }

        // ------------------------------------------------------------------ fixtures (server)

        public void Interact(IServerPlayer player, FixtureDef f, bool sneak = false, bool ctrl = false)
        {
            var ep = player.Entity;
            // gone down: a diver can still open what's stowed in her (the hearth too), nothing else
            if (GoneDown && !(f.Type == "storage" || f.Type == "hearth" || f.Type == "attach" && (f.Kind == "chest" || f.Kind == "cargo") && !sneak))
            {
                Notify(player, "hansa:gone-down");
                return;
            }
            switch (f.Type)
            {
                case "gate" when ShipSlotsBoards(player):
                {
                    // ((0.3.27, note 219) boards in hand at an entry port: run her brow out to the quay, or take it in)
                    string berr = ToggleBrow(player, f);
                    if (berr != null) Notify(player, berr);
                    break;
                }
                case "door":
                case "gate":
                    ToggleFitting(player, f);
                    break;
                case "pump":
                    // a stroke of the brake (the client sends one a second while right-click is held)
                    PumpStroke(player, f);
                    break;
                case "gun":
                {
                    // (0.3.21, step C) a gun place without its gun: nothing to work
                    if (GunAbsent(f)) { Notify(player, "hansa:gunplace-no-gun"); return; }
                    // fire it along its lay (it's laid by holding right mouse by it: ShipGuns; the client sends this on a left-click); Ctrl: stand to it instead,
                    // at the gun captain's place behind its breech. (Loading comes later: for now it's ready again when
                    // it has cooled down.)
                    var gs = GunSeatOf(f);
                    // Shift: run it in and shut its port
                    if (sneak && !ctrl && f.Lid != null)
                    {
                        string herr = HouseGun(f);
                        if (herr != null) player.SendIngameError("hansa-gun", Lang.GetL(player.LanguageCode, herr));
                        break;
                    }
                    if (ctrl && gs != null && ep.MountedOn != gs)
                    {
                        if (gs.Passenger != null && gs.Passenger != ep && gs.Passenger.Alive) { Notify(player, "hansa:gun-seat-taken"); return; }
                        if (!Mod.Config.Cannons) { player.SendIngameError("hansa-gun", Lang.GetL(player.LanguageCode, "hansa:gun-off")); return; }
                        ep.TryMount(gs);
                        break;
                    }
                    string err = FireGun(f, ep, player);
                    if (err == "hansa:gun-reloading") player.SendIngameError("hansa-gun", Lang.GetL(player.LanguageCode, err, Math.Ceiling(GunReloading(f))));
                    else if (err != null) player.SendIngameError("hansa-gun", Lang.GetL(player.LanguageCode, err));
                    break;
                }
                case "hatch":
                    // a hatch onto a storage still holding something (the hold of an older ship, one big storage before
                    // it had places for chests): using it opens the hatch and the storage, Shift shuts it
                    if (f.Storage != null && !sneak && Storages.TryGetValue(f.Storage, out var held) && !held.Empty &&
                        Def.Fixture(f.Storage) is FixtureDef sf)
                    {
                        if (!OpenFittings.Contains(f.Code)) ToggleFitting(player, f);
                        player.InventoryManager.OpenInventory(held);
                        Mod.SendOpenStorage(player, this, sf.Index);
                    }
                    else ToggleFitting(player, f);
                    break;
                case "figurehead":
                    NextFigurehead(f);
                    LocalToWorld(f.Pos[0], f.Pos[1], f.Pos[2], out double fx, out double fy, out double fz);
                    World.PlaySoundAt(new AssetLocation("sounds/block/planks"), fx, fy, fz, null, true, 16);
                    break;
                case "attach":
                    InteractAttach(player, f, sneak);
                    break;
                case "helm":
                    if (Helm.Passenger != null && Helm.Passenger != ep) { Notify(player, "hansa:helm-taken"); return; }
                    if (!HasRoomFor(ep)) { Notify(player, "hansa:boat-full"); return; }
                    ep.TryMount(Helm);
                    break;
                case "oars":
                    // sit down at the oars: they come out while you're there
                    if (GetSeat("row:" + f.Code) is not RowSeat row) return;
                    if (row.Passenger != null && row.Passenger != ep) { Notify(player, "hansa:oars-taken"); return; }
                    if (!HasRoomFor(ep)) { Notify(player, "hansa:boat-full"); return; }
                    ep.TryMount(row);
                    break;
                case "mast":
                    // raise or lower the mast; lowered, her sail comes in
                    ToggleFitting(player, f);
                    if (!MastUp && SailSet != 0) { SailSet = 0; SaveState(true); }
                    break;
                case "bed":
                    TrySleep(player, f);
                    break;
                case "seat":
                    // the heads: sit down (Shift, or a step away, gets up)
                    if (GoneDown || GetSeat("sit:" + f.Code) is not PieceSeat sit) return;
                    if (sit.Passenger != null && sit.Passenger != ep) { Notify(player, "hansa:piece-seat-taken"); return; }
                    ep.TryMount(sit);
                    break;
                case "storage":
                    if (!Storages.TryGetValue(f.Code, out var inv)) return;
                    player.InventoryManager.OpenInventory(inv);
                    Mod.SendOpenStorage(player, this, f.Index);
                    break;
                case "stove":
                    if (!LitStoves.Remove(f.Code)) LitStoves.Add(f.Code);
                    SaveState(true);
                    break;
                case "hearth":
                    // vanilla's firepit: open it, or put fuel or food in with Shift, a pot in, serve a bowl (ShipHearth)
                    if (f == HearthFixture) UseHearth(player, f, sneak);
                    break;
                case "oven":
                {
                    // vanilla's clay oven: firewood in, something to bake in, take out, light it (ShipOven)
                    string oerr = UseOven(player, f);
                    if (oerr != null) player.SendIngameError("hansa-oven", Lang.GetL(player.LanguageCode, oerr));
                    break;
                }
                case "anchor":
                    Anchored = !Anchored;
                    Notify(player, Anchored ? "hansa:anchor-dropped" : "hansa:anchor-weighed");
                    SaveState(true);
                    break;
                case "pinrail":
                {
                    // (0.3.13, note 131) the pin rails by the main shrouds: the crew belays or casts off the sheets and
                    // halyards there, as W/S at the helm (right-click more sail, Shift less)
                    string serr = WorkSails(!sneak);
                    if (serr != null) player.SendIngameError("hansa-sails", Lang.GetL(player.LanguageCode, serr));
                    break;
                }
            }
        }

        /// <summary>
        /// (0.3.13, note 131) Sail set or taken in from a pin rail, a step at a time as with W and S at the helm: furled →
        /// half → full, and back (from the sweeps, more sail is half sail). The fixture packet has already checked that
        /// it's her crew. Null, or why not (laid up, her mast lowered, no more to set or take in).
        /// </summary>
        /// <summary>
        /// (0.3.25) Server: put her under the sweeps (<see cref="Hansa.Crawl"/>: only with her sails furled; the anchor is
        /// weighed), or take her off them. Not for a boat with oars, a ship laid up or gone down. False: refused.
        /// </summary>
        public bool SetCrawl(bool on)
        {
            if (on && (Def == null || Def.RowSpeed > 0 || LaidUp || GoneDown || SailSet > 0)) return false;
            if (on == Crawl) return true;
            Crawl = on;
            if (on) Anchored = false;
            else CrawlDrive = 0;
            SaveState(true);
            return true;
        }

        public string WorkSails(bool more)
        {
            if (LaidUp) return "hansa:laidup-closed";
            if (!MastUp) return "hansa:pinrail-mast-down";
            int to = PinRailStep(SailSet, more);
            if (to == SailSet) return more ? "hansa:pinrail-full" : "hansa:pinrail-furled";
            SailSet = to;
            SaveState(true);
            LocalToWorld(0, Def.DeckY + 1, 0, out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/block/cloth"), x, y, z, null, true, 24);
            return null;
        }

        /// <summary>The sail a pin rail's step leaves her with: one more (0 to 2; from the sweeps, half), or one less (down to furled).</summary>
        public static int PinRailStep(int sailSet, bool more) =>
            more ? Math.Min(2, Math.Max(0, sailSet) + 1) : sailSet <= 0 ? sailSet : sailSet - 1;

        void TrySleep(IServerPlayer player, FixtureDef f)
        {
            var ep = player.Entity;
            if (GetSeat("bunk:" + f.Code) is not BunkSeat bunk) return;
            if (bunk.Passenger != null && bunk.Passenger != ep) { Notify(player, "hansa:bunk-taken"); return; }
            if (ep.GetBehavior("tiredness") is Vintagestory.GameContent.EntityBehaviorTiredness t && t.Tiredness <= 8)
            {
                player.SendIngameError("not-tired-enough", Lang.GetL(player.LanguageCode, "not-tired-enough"));
                return;
            }
            if (World.Config.GetString("temporalStormSleeping", "0") == "0" &&
                Api.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>()?.StormStrength > 0)
            {
                player.SendIngameError("cantsleep-tempstorm", Lang.GetL(player.LanguageCode, "cantsleep-tempstorm"));
                return;
            }
            ep.TryMount(bunk);
        }

        // ------------------------------------------------------------------ fittings (server)

        /// <summary>
        /// Is this room closed right now: a cabin or the lower deck, with nothing standing open that lets the weather in
        /// (the waist hatch opens the lower deck).
        /// </summary>
        public bool RoomEnclosed(RoomDef r)
        {
            if (r == null || !r.Enclosed) return false;
            foreach (string code in OpenFittings)
            {
                if (Def.Fixture(code)?.Unseals == r.Code) return false;
            }
            // (0.3.20, step B) an open deck cell in its ceiling or floor onto the open deck
            if (Slots != null && CellLeaks(r).Count > 0) return false;
            // (0.3.26, step D1) a hull panel of its side out
            if (HullPanels != null && HullLeaks(r).Count > 0) return false;
            // (0.3.22, step C) a lidded gun port of the room standing open (the gun run out)
            foreach (var f in ShipCompartments.LiddedPorts(Def))
                if (PortOpen(f) && ShipCompartments.PortRoom(Def, f) == r) return false;
            return true;
        }

        /// <summary>
        /// (0.3.22, step C) Is this lidded gun's port open now (its gun run out)? Shut while it's housed, and always on an empty
        /// gun place (docs/slots.md §7: the lid is her side's). A shut lid seals its port (cut in her collision or not) for
        /// her rooms (<see cref="RoomEnclosed"/>, <see cref="SealedAt"/>), an open one lets the weather in, and the sea once
        /// it's under (ShipDamage's downflood points).
        /// </summary>
        public bool PortOpen(FixtureDef f) => f?.Type == "gun" && f.Lid != null && !GunAbsent(f) && OpenFittings.Contains(f.Code);

        readonly Dictionary<(string, int, int, int), bool> sealedCache = new();

        /// <summary>
        /// Is this place (ship-local) in a closed room right now: a design room with nothing open that unseals it
        /// (<see cref="RoomEnclosed"/>), or, within one that's open, a space closed off by partitions and shut doors put
        /// aboard (<see cref="ShipCompartments"/>; worked out again when her pieces or fittings change, by block).
        /// </summary>
        public bool SealedAt(double x, double y, double z)
        {
            var r = Def?.RoomAt(x, y, z);
            if (r == null || !r.Enclosed) return false;
            // (0.3.16, note 154) a room whose walls are (partly) placed pieces is sealed only when they close it
            if (!r.Pieces && RoomEnclosed(r)) return true;
            if (Grid == null || Mounts == null) return false;
            if (!r.Pieces && !Pieces.All.Any(p => p.OnBorder || p.Role is PieceRole.Shelf or PieceRole.Hatch) && (HullPanels == null || HullLeaks(r).Count == 0)) return false;
            var key = (r.Code, (int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
            if (sealedCache.TryGetValue(key, out bool sealedNow)) return sealedNow;
            var leaks = OpenFittings.Select(c => Def.Fixture(c)).Where(f => f?.Unseals == r.Code).Select(f => f.Sel ?? f.Box).ToList();
            // (0.3.16) a well in its ceiling open to the deck above, not panelled over
            leaks.AddRange(ShipCompartments.WellLeaks(Def, r, Pieces.All));
            // (0.3.20, step B) and her open deck cells over or under it
            leaks.AddRange(CellLeaks(r));
            // (0.3.26, step D1) and her hull panels of its side that are out
            leaks.AddRange(HullLeaks(r));
            sealedNow = ShipCompartments.Sealed(Mounts, Grid, Pieces.All, r, leaks, x, y, z, PortOpen);
            if (sealedCache.Count > 512) sealedCache.Clear();
            sealedCache[key] = sealedNow;
            return sealedNow;
        }

        /// <summary>Open or close a door, gate or hatch; it won't close on someone standing in it.</summary>
        public bool ToggleFitting(IServerPlayer player, FixtureDef f)
        {
            bool open = OpenFittings.Contains(f.Code);
            if (open && f.Box != null && BoxOccupied(f.Box))
            {
                if (player != null) Notify(player, "hansa:doorway-blocked");
                return false;
            }
            // an entry port's gate swings in where the gun next to it would be, trained that way (ShipGuns)
            if (!open && GunInGateway(f) != null)
            {
                if (player != null) Notify(player, "hansa:gate-gun");
                return false;
            }
            if (open) OpenFittings.Remove(f.Code);
            else OpenFittings.Add(f.Code);
            // ((0.3.27) the gate shut: her brow there comes in)
            if (open && f.Type == "gate" && Brows.ContainsKey(f.Code)) TakeInBrow(f.Code);
            FittingsChanged();
            LocalToWorld(f.Pos[0], f.Pos[1] + 1, f.Pos[2], out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/block/door"), x, y, z, null, true, 16);
            return true;
        }

        void InteractAttach(IServerPlayer player, FixtureDef f, bool sneak)
        {
            if (CoveredBy(f) is FixtureDef trunk) f = trunk;     // a spot a trunk on the next one lies across
            var slot = player.InventoryManager.ActiveHotbarSlot;
            if (!Attached.ContainsKey(f.Code))
            {
                if (slot?.Itemstack == null || !ShipFittings.Accepts(f, slot.Itemstack))
                {
                    Notify(player, "hansa:attach-needs-" + f.Kind);
                    return;
                }
                string err = TryAttach(f, slot.Itemstack);
                if (err != null) { Notify(player, err); return; }
                slot.TakeOut(1);
                slot.MarkDirty();
                LocalToWorld(f.Pos[0], f.Pos[1], f.Pos[2], out double x, out double y, out double z);
                World.PlaySoundAt(new AssetLocation("sounds/block/planks"), x, y, z, null, true, 16);
                return;
            }
            switch (f.Kind)
            {
                case "lantern":
                case "boat":
                    TakeBack(player, f);      // like vanilla: right-click picks a lantern up; the boat comes off its chocks
                    break;
                case "chest":
                case "cargo":
                    if (sneak) TakeBack(player, f);
                    else if (Storages.TryGetValue(f.Code, out var inv))
                    {
                        player.InventoryManager.OpenInventory(inv);
                        Mod.SendOpenStorage(player, this, f.Index);
                    }
                    break;
                case "bed":
                    if (sneak) TakeBack(player, f);
                    else TrySleep(player, f);
                    break;
                case "plate":
                    // right-click with an empty hand: bind your spawn to her, or unbind it; Shift: take the plate back
                    // (every spawn bound to her ends)
                    if (sneak) TakeBack(player, f);
                    else if (slot?.Itemstack != null) Notify(player, "hansa:plate-empty-hand");
                    else Mod.Spawns?.Toggle(player, this, f);
                    break;
            }
        }

        /// <summary>Put a copy of one of these items on the attachment point; returns an error lang code or null.</summary>
        public string TryAttach(FixtureDef f, ItemStack from)
        {
            if (Attached.ContainsKey(f.Code) || CoveredBy(f) != null) return "hansa:attach-taken";
            if (!ShipFittings.Accepts(f, from)) return "hansa:attach-needs-" + f.Kind;
            if (ShipFittings.IsTrunk(from))
            {
                // a trunk lies across this spot and the next: that one must be free too
                var pair = f.Pair != null ? Def.Fixture(f.Pair) : null;
                if (pair == null || Attached.ContainsKey(pair.Code) || CoveredBy(pair) != null) return "hansa:attach-trunk-room";
            }
            if (ShipFittings.AttachBox(f, from, Def) is double[] box && BoxOccupied(box)) return "hansa:attach-blocked";
            var one = from.Clone();
            one.StackSize = 1;
            Attached[f.Code] = one;
            FittingsChanged();
            return null;
        }

        /// <summary>Take an attached item back (a chest has to be empty, a bed unoccupied).</summary>
        public bool TakeBack(IServerPlayer player, FixtureDef f)
        {
            if (!Attached.TryGetValue(f.Code, out var stack)) return false;
            if ((f.Kind == "chest" || f.Kind == "cargo") && Storages.TryGetValue(f.Code, out var inv) && !inv.Empty)
            {
                if (player != null) Notify(player, "hansa:chest-not-empty");
                return false;
            }
            if (f.Kind == "bed" && GetSeat("bunk:" + f.Code)?.Passenger != null)
            {
                if (player != null) Notify(player, "hansa:bunk-taken");
                return false;
            }
            Attached.Remove(f.Code);
            FittingsChanged();
            if (player != null && !player.InventoryManager.TryGiveItemstack(stack, true))
            {
                World.SpawnItemEntity(stack, player.Entity.Pos.XYZ.Add(0, 0.5, 0));
            }
            // without the plate she's no one's spawn any more
            if (f.Kind == "plate" && Api.Side == EnumAppSide.Server) Mod.Spawns?.EndShip(EntityId, ShipSpawns.EndPlate);
            return true;
        }

        /// <summary>Server: note a player's spawn bound to her or not (for the plate's look-at text on clients).</summary>
        public void SetSpawnBound(string uid, string name, bool bound)
        {
            var t = WatchedAttributes.GetOrAddTreeAttribute("hansaSpawn");
            if (bound) t.SetString(uid, name ?? uid);
            else t.RemoveAttribute(uid);
            WatchedAttributes.MarkPathDirty("hansaSpawn");
            LoadSpawnBound();
        }

        void LoadSpawnBound()
        {
            SpawnBound.Clear();
            if (WatchedAttributes.GetTreeAttribute("hansaSpawn") is ITreeAttribute t)
                foreach (var kv in t) SpawnBound.Add(kv.Key);
        }

        /// <summary>The cargo spot whose trunk lies across this one (its pair), or null.</summary>
        public FixtureDef CoveredBy(FixtureDef f)
        {
            if (f.Kind != "cargo" || f.Pair == null || Attached.ContainsKey(f.Code)) return null;
            return Attached.TryGetValue(f.Pair, out var s) && ShipFittings.IsTrunk(s) ? Def.Fixture(f.Pair) : null;
        }

        /// <summary>The mesh group of the figurehead she carries ("" for none).</summary>
        public string FigureheadGroup(FixtureDef f)
        {
            if (f.Options == null || f.Groups == null || f.Options.Length == 0) return null;
            int i = Math.Max(0, Array.IndexOf(f.Options, Figurehead ?? f.Options[0]));
            return i < f.Groups.Length ? f.Groups[i] : null;
        }

        /// <summary>Carve her the next figurehead there is.</summary>
        public void NextFigurehead(FixtureDef f)
        {
            if (f.Options == null || f.Options.Length == 0) return;
            int i = Array.IndexOf(f.Options, Figurehead ?? f.Options[0]);
            Figurehead = f.Options[(i + 1) % f.Options.Length];
            FittingsChanged();
        }

        /// <summary>Paint her name on the board (blank rubs it off), in a style (null: as it was). Returns an error lang code or null.</summary>
        public string SetName(string name, NameStyle style = null)
        {
            name = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (name.Length > MaxNameLength) return "hansa:name-too-long";
            ShipName = name.Length == 0 ? null : name;
            if (style != null) NameStyle = style.Clean();
            FittingsChanged();
            return null;
        }

        /// <summary>Nail copper plates from the player's hand onto the bottom. Returns the plates used.</summary>
        public int AddCladding(IServerPlayer player)
        {
            var slot = player?.InventoryManager.ActiveHotbarSlot;
            if (slot?.Itemstack == null || Clad) return 0;
            if (slot.Itemstack.Collectible.Code.ToString() != "game:metalplate-copper")
            {
                Notify(player, "hansa:cladding-needs-copper");
                return 0;
            }
            int take = AddCladdingPlates(slot.Itemstack.StackSize);
            slot.TakeOut(take);
            slot.MarkDirty();
            player.SendMessage(GlobalConstants.InfoLogChatGroup, Clad
                ? Lang.GetL(player.LanguageCode, "hansa:cladding-done")
                : Lang.GetL(player.LanguageCode, "hansa:cladding-progress", CladPlates, Def.CladdingPlates), EnumChatType.Notification);
            return take;
        }

        public int AddCladdingPlates(int plates)
        {
            int take = Math.Max(0, Math.Min(Def.CladdingPlates - CladPlates, plates));
            if (take == 0) return 0;
            CladPlates += take;
            // her bottom is scraped clean for the copper (and stays clean under it)
            if (Clad && Fouling > 0) { Fouling = 0; SaveState(true); }
            FittingsChanged();
            return take;
        }

        /// <summary>Is anyone aboard standing in this box (ship-local)?</summary>
        public bool BoxOccupied(double[] box)
        {
            foreach (var s in seatArray)
            {
                if (s.Passenger == null || s is not ShipSeat ss) continue;
                var l = ss.LocalPos;
                if (l.X + DeckMover.HalfWidth > box[0] && l.X - DeckMover.HalfWidth < box[3] &&
                    l.Y + DeckMover.Height > box[1] && l.Y < box[4] &&
                    l.Z + DeckMover.HalfWidth > box[2] && l.Z - DeckMover.HalfWidth < box[5]) return true;
            }
            return false;
        }

        /// <summary>Ladders whose top can't be passed: the entry port gate is shut.</summary>
        public bool LadderTopClosed(LadderDef l) => l.Gate != null && (l.Gate == "opening" ? !OpeningAt(l.Top) : !OpenFittings.Contains(l.Gate)) ||
                                                    !double.IsNaN(LadderCover(l));

        /// <summary>
        /// (0.3.23, note 169: "can climb through a closed hatch, then open it and leave") The underside of a shut hatch piece
        /// over a ladder's top (its way up comes out under the hatch's cells), or NaN: the climber's head stops under it
        /// (<see cref="DeckMover.CoverUnder"/>) and nobody steps onto the ladder from above through it.
        /// </summary>
        public double LadderCover(LadderDef l) => HatchCover.Under(Pieces.All, l);
        public bool LadderThroughClosed(LadderDef l) => l.Through != null && (l.Through == "opening" ? !OpeningAt(new[] { l.Top[0], l.ThroughY, l.Top[2] }) : !OpenFittings.Contains(l.Through));

        void FittingsChanged()
        {
            SyncChestStorages();
            SaveFittings();
            RebuildGrid();
        }

        void RebuildGrid()
        {
            if (Def == null || Grid == null) return;
            ShipFittings.ApplySolids(Def, Grid, f => OpenFittings.Contains(f.Code), f => Attached.TryGetValue(f.Code, out var st) ? st : null, GunAbsent);
            // (0.3.20, step B) her deck cells as they are: one taken up is an opening
            ApplySlots(Grid);
            // the pieces put aboard (ShipDecor): the partitions and shut doors last, after PlaceGrid is taken
            FillPieceSolids(Grid, walls: false);
            if (PlaceGrid == null) PlaceGrid = Grid.Clone(); else PlaceGrid.CopyFrom(Grid);
            FillPieceSolids(Grid, walls: true);
            FitVersion++;
            sealedCache.Clear();
        }

        /// <summary>A storage for every chest, trunk, vessel and crate aboard (sized like it), none for spots left empty.</summary>
        void SyncChestStorages()
        {
            foreach (var f in Def.Fixtures)
            {
                if (f.Type != "attach" || (f.Kind != "chest" && f.Kind != "cargo")) continue;
                if (Attached.TryGetValue(f.Code, out var stack))
                {
                    int slots = ShipFittings.ContainerSlots(stack);
                    if (Storages.TryGetValue(f.Code, out var existing) && existing.Count == slots) continue;
                    var inv = new InventoryGeneric(slots, "hansa-" + f.Code, EntityId.ToString(), Api);
                    if (Api.Side == EnumAppSide.Server)
                    {
                        string fcode = f.Code;
                        inv.SlotModified += _ => SaveInventory(fcode);
                    }
                    Storages[f.Code] = inv;
                }
                else if (Storages.Remove(f.Code) && Api.Side == EnumAppSide.Server)
                {
                    WatchedAttributes.GetTreeAttribute("hansaInv")?.RemoveAttribute(f.Code);
                }
            }
        }

        void SaveFittings()
        {
            var t = WatchedAttributes.GetOrAddTreeAttribute("hansaFit");
            t.SetString("open", string.Join(",", OpenFittings));
            t.SetInt("clad", CladPlates);
            if (ShipName != null) t.SetString("name", ShipName); else t.RemoveAttribute("name");
            NameStyle.ToTree(t);
            if (Figurehead != null) t.SetString("figurehead", Figurehead); else t.RemoveAttribute("figurehead");
            var at = new TreeAttribute();
            foreach (var kv in Attached) at.SetItemstack(kv.Key, kv.Value);
            t["attach"] = at;
            WatchedAttributes.MarkPathDirty("hansaFit");
        }

        void LoadFittings()
        {
            if (Def == null) return;
            var t = WatchedAttributes.GetTreeAttribute("hansaFit");
            OpenFittings.Clear();
            Attached.Clear();
            CladPlates = 0;
            ShipName = null;
            Figurehead = null;
            NameStyle = NameStyle.FromTree(t);
            if (t == null)
            {
                // a new ship: what starts open (a boat's mast starts lowered)
                foreach (var f in Def.Fixtures) if (f.StartOpen && f.Opens) OpenFittings.Add(f.Code);
            }
            if (t != null)
            {
                foreach (string s in (t.GetString("open") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) OpenFittings.Add(s);
                CladPlates = t.GetInt("clad");
                ShipName = t.GetString("name");
                Figurehead = t.GetString("figurehead");
                if (t.GetTreeAttribute("attach") is ITreeAttribute at)
                {
                    foreach (var kv in at)
                    {
                        var stack = at.GetItemstack(kv.Key);
                        if (stack == null || !stack.ResolveBlockOrItem(World)) continue;
                        if (Def.Fixture(kv.Key) is FixtureDef f && f.Type == "attach") Attached[kv.Key] = stack;
                    }
                }
            }
            SyncChestStorages();
            RebuildGrid();
            // (clients: a lidded gun's port opened or shut runs it out or in from now)
            NotePorts();
        }

        void Notify(IServerPlayer player, string langCode)
        {
            player.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(player.LanguageCode, langCode), EnumChatType.Notification);
        }

        public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data)
        {
            // storage dialogs: packet id = (fixture index + 1) << 11 | inventory packet
            int fi = (packetid >> 11) - 1;
            int inner = packetid & 2047;
            // a piece's storage (ShipDecor)
            if (PieceInventoryPacket(player, fi, inner, data)) return;
            if (fi >= 0 && fi < Def.Fixtures.Length && Storages.TryGetValue(Def.Fixtures[fi].Code, out var inv))
            {
                if (inner < 1000 && inv.HasOpened(player)) inv.InvNetworkUtil.HandleClientPacket(player, inner, data);
                return;
            }
            // the hearth's dialog (vanilla's firepit inventory)
            if (fi >= 0 && fi < Def.Fixtures.Length && Def.Fixtures[fi] == HearthFixture && HearthInv != null)
            {
                if (inner < 1000 && HearthInv.HasOpened(player)) HearthInv.InvNetworkUtil.HandleClientPacket(player, inner, data);
                return;
            }
            base.OnReceivedClientPacket(player, packetid, data);
        }

        // ------------------------------------------------------------------ persistence

        void SaveInventory(string code)
        {
            var tree = WatchedAttributes.GetOrAddTreeAttribute("hansaInv");
            bool known = tree.HasAttribute(code);
            var t = new TreeAttribute();
            Storages[code].ToTreeAttributes(t);
            tree[code] = t;
            // Only this storage goes out again (the hold is big); clients' listener on "hansaInv" still fires. A partial
            // update for a path a client doesn't have yet would land as a top-level key named "hansaInv/<code>", so a
            // storage saved for the first time sends the whole tree.
            WatchedAttributes.MarkPathDirty(known ? "hansaInv/" + code : "hansaInv");
        }

        /// <summary>
        /// Chests an older version of the design had (and whatever was in them) are gone from the design: move their
        /// contents into the hold, or onto the deck if it's full, so nothing is lost. Likewise what was put on a place
        /// the design no longer has (since 0.3.6 the hold's ladder comes down where two pairs of cargo places were): the
        /// chest, vessel, crate or trunk itself goes into the hold.
        /// </summary>
        void RescueOrphanedCargo()
        {
            Storages.TryGetValue("hold", out var hold);
            void Stow(ItemStack stack) => StowInHold(stack);

            var orphaned = new List<string>();
            var tree = WatchedAttributes.GetTreeAttribute("hansaInv");
            if (tree != null) foreach (var kv in tree) if (!Storages.ContainsKey(kv.Key)) orphaned.Add(kv.Key);
            RescueOrphanedCargoRest(hold, orphaned, tree, Stow);
        }

        /// <summary>Into the hold (stacked onto what's like it), or onto her deck where it's full or she has none: nothing is lost. The hold is saved by the caller.</summary>
        public void StowInHold(ItemStack stack)
        {
            Storages.TryGetValue("hold", out var hold);
            {
                if (hold != null)
                {
                    foreach (var target in hold)
                    {
                        if (stack == null || stack.StackSize <= 0) break;
                        if (target.Empty) { target.Itemstack = stack; stack = null; target.MarkDirty(); break; }
                        int moved = target.Itemstack.Equals(World, stack, GlobalConstants.IgnoredStackAttributes)
                            ? Math.Min(stack.StackSize, target.Itemstack.Collectible.MaxStackSize - target.Itemstack.StackSize) : 0;
                        if (moved > 0)
                        {
                            target.Itemstack.StackSize += moved;
                            stack.StackSize -= moved;
                            target.MarkDirty();
                        }
                    }
                }
                if (stack != null && stack.StackSize > 0) World.SpawnItemEntity(stack, Pos.XYZ.Add(0, (Def?.DeckY ?? 3) + 1, 0));
            }
        }

        void RescueOrphanedCargoRest(InventoryBase hold, List<string> orphaned, ITreeAttribute tree, Action<ItemStack> Stow)
        {
            foreach (string code in orphaned)
            {
                if (tree.GetTreeAttribute(code) is ITreeAttribute t)
                {
                    var old = new InventoryGeneric(Math.Max(1, t.GetInt("qslots", 64)), "hansa-old-" + code, EntityId.ToString(), Api);
                    old.FromTreeAttributes(t);
                    old.AfterBlocksLoaded(World);
                    foreach (var slot in old)
                    {
                        if (!slot.Empty) Stow(slot.Itemstack);
                    }
                    Api.Logger.Notification("[hansa] Moved the contents of the old {0} into the hold", code);
                }
                tree.RemoveAttribute(code);
            }

            var gone = new List<string>();
            var attached = WatchedAttributes.GetTreeAttribute("hansaFit")?.GetTreeAttribute("attach");
            if (attached != null && Def != null)
                foreach (var kv in attached) if (!(Def.Fixture(kv.Key) is FixtureDef f && f.Type == "attach")) gone.Add(kv.Key);
            foreach (string code in gone)
            {
                var stack = attached.GetItemstack(code);
                attached.RemoveAttribute(code);
                if (stack == null || !stack.ResolveBlockOrItem(World)) continue;
                Api.Logger.Notification("[hansa] Moved the {0} from the old place {1} into the hold", stack.Collectible.Code, code);
                Stow(stack);
            }

            if (orphaned.Count > 0 || gone.Count > 0)
            {
                if (hold != null) SaveInventory("hold");
                if (orphaned.Count > 0) WatchedAttributes.MarkPathDirty("hansaInv");
                if (gone.Count > 0) WatchedAttributes.MarkPathDirty("hansaFit");
            }
        }

        void LoadInventories()
        {
            var tree = WatchedAttributes.GetTreeAttribute("hansaInv");
            if (tree == null) return;
            foreach (var kv in Storages)
            {
                if (tree.GetTreeAttribute(kv.Key) is ITreeAttribute t)
                {
                    kv.Value.FromTreeAttributes(t);
                    kv.Value.AfterBlocksLoaded(World);
                }
            }
        }

        public void SaveState(bool force)
        {
            var t = WatchedAttributes.GetOrAddTreeAttribute("hansa");
            t.SetInt("sailSet", SailSet);
            t.SetBool("crawl", Crawl);
            t.SetInt("crawlDrive", CrawlDrive);
            t.SetFloat("sail", SailFraction);
            t.SetFloat("leeway", Leeway);
            if (WhySlow != null) t.SetString("whySlow", WhySlow); else t.RemoveAttribute("whySlow");
            t.SetFloat("rudder", Rudder);
            t.SetFloat("speed", Speed);
            t.SetFloat("yawRate", YawRate);
            t.SetBool("anchored", Anchored);
            t.SetBool("aground", Aground);
            t.SetInt("rowing", Rowing);
            t.SetBool("waiting", Waiting);
            t.SetBool("byWind", SailByWind);
            t.SetFloat("maxSpeed", MaxSpeedNow);
            t.SetFloat("pointCloser", PointCloser);
            t.SetString("stoves", string.Join(",", LitStoves));
            t.SetBool("guns", CannonsOn);
            t.SetBool("hearth", HearthOn);
            t.SetBool("oven", OvenOn);
            if (!double.IsNaN(targetY)) t.SetDouble("targetY", targetY);
            if (!double.IsNaN(BaseY)) t.SetDouble("baseY", BaseY);
            SaveFloodState(t);
            if (force || syncAccum >= 0.2f)
            {
                WatchedAttributes.MarkPathDirty("hansa");
                syncAccum = 0;
            }
        }

        /// <summary>
        /// Why her set sails give her little way (a lang key for the helm's HUD), or null: taken aback (the wind right
        /// ahead: sternway), in irons (closer to it than she can sail), her rig too hurt to draw, or the water in her.
        /// </summary>
        string SlowReason(float target, float maxSpeed)
        {
            if (SailSet <= 0 || Anchored || OnBottom || !SailByWind || Def == null) return null;
            var w = WindHere(Pos.X, Pos.Z);
            double angle = Sailing.WindAngle(w.X, w.Z, Pos.Yaw, out _);
            if (angle < SailingNow.NoGo) return angle < SailingNow.NoGo / 2 ? "hansa:why-aback" : "hansa:why-irons";
            if (SailDrive < 0.35) return "hansa:why-rig";
            if (SpeedFactorNow < 0.5) return "hansa:why-water";
            return null;
        }

        void LoadState()
        {
            var t = WatchedAttributes.GetTreeAttribute("hansa");
            if (t == null)
            {
                foreach (var f in Def.Fixtures) if (f.Type == "stove") LitStoves.Add(f.Code);
                return;
            }
            SailSet = t.GetInt("sailSet");
            Crawl = t.GetBool("crawl");
            CrawlDrive = t.GetInt("crawlDrive");
            // ((0.3.25) a ship saved under the sweeps astern (-1, till 0.3.24) is under the sweeps now)
            if (SailSet < 0) { SailSet = 0; Crawl = true; }
            SailFraction = t.GetFloat("sail");
            Leeway = t.GetFloat("leeway");
            WhySlow = t.GetString("whySlow");
            Rudder = t.GetFloat("rudder");
            Speed = t.GetFloat("speed");
            YawRate = t.GetFloat("yawRate");
            Anchored = t.GetBool("anchored", true);
            Aground = t.GetBool("aground");
            Rowing = t.GetInt("rowing");
            Waiting = t.GetBool("waiting");
            SailByWind = t.GetBool("byWind", true);
            MaxSpeedNow = t.GetFloat("maxSpeed", Def.MaxSpeed);
            PointCloser = t.GetFloat("pointCloser");
            LitStoves.Clear();
            foreach (string s in (t.GetString("stoves") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) LitStoves.Add(s);
            CannonsOn = t.GetBool("guns", true);
            HearthOn = t.GetBool("hearth", true);
            OvenOn = t.GetBool("oven", true);
            if (t.HasAttribute("targetY")) targetY = t.GetDouble("targetY");
            if (t.HasAttribute("baseY")) BaseY = t.GetDouble("baseY");
            LoadFloodState(t);
        }

        // ------------------------------------------------------------------ server sailing

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (Def == null) return;
            if (Api.Side == EnumAppSide.Server) ServerTick(dt);
        }

        public bool Manned()
        {
            foreach (IServerPlayer sp in ((ICoreServerAPI)Api).World.AllOnlinePlayers)
            {
                if (sp.ConnectionState == EnumClientState.Playing && sp.Entity is EntityPlayer ep && ep.Alive && ep.MountedOn?.Entity?.EntityId == EntityId) return true;
            }
            return false;
        }

        void ServerTick(float dt)
        {
            dt = Math.Min(dt, 0.2f);
            syncAccum += dt;
            var cfg = Mod.Config;

            // something else moved the ship up or down (a teleport): that's its still-water level now
            if (double.IsNaN(BaseY) || !double.IsNaN(writtenY) && Math.Abs(Pos.Y - writtenY) > 1e-4)
            {
                BaseY = Pos.Y + Sinkage;
                targetY = double.NaN;
                waterCheckAccum = 99;
            }

            passengerCheckAccum += dt;
            if (passengerCheckAccum > 1)
            {
                passengerCheckAccum = 0;
                if (!GoneDown) CheckPendingPassengers();
                UpdateCrewPositions();
            }

            // going down, or on the bottom: no sailing, no waves (ShipFlooding)
            if (GoneDown)
            {
                if (!double.IsNaN(BaseY)) SinkTick(dt);
                SaveState(false);
                return;
            }

            // laid up while none of her crew is online (ShipCrew): anchored, furled, no water, damage or wear
            LaidUpTick(dt);
            // the watch over anyone asleep aboard (ShipWatch)
            WatchTick(dt);
            // ((0.3.27, note 219) a brow comes in when she leaves)
            BrowTick(dt);
            bool manned = Manned();
            unmannedFor = manned ? 0 : unmannedFor + dt;
            if (cfg.StopWhenUnmanned && unmannedFor > cfg.UnmannedStopSeconds && (!Anchored || SailSet != 0))
            {
                Anchored = true;
                SailSet = 0;
                SaveState(true);
            }

            // helm input
            var c = Helm?.Passenger != null ? Helm.Controls : null;
            float rudderIn = 0;
            // ((0.3.25) the sweeps: Ctrl (the sprint key) at the wheel puts her under them with her sails furled, or takes
            // her off them; under them W/S held creep her ahead/astern and A/D turn her; S with the sails furled mans them too,
            // as the sweeps astern did till 0.3.24. A boat has her oars instead)
            bool mayCrawl = Def.RowSpeed <= 0;
            int crawlAhead = 0;
            float crawlTurn = 0;
            if (c != null)
            {
                bool ctrl = c.Sprint || c.CtrlKey;
                if (ctrl && !prevCtrl && mayCrawl && !SetCrawl(!Crawl) && Helm.Passenger is EntityPlayer hp && hp.Player is IServerPlayer hsp)
                    Notify(hsp, "hansa:sweeps-furl-first");
                if (Crawl)
                {
                    crawlAhead = (c.Forward ? 1 : 0) - (c.Backward ? 1 : 0);
                    crawlTurn = HelmMath.RudderInput(c.Left, c.Right);
                }
                else
                {
                    if (c.Forward && !prevFwd) { SailSet = Math.Min(2, SailSet + 1); SaveState(true); }
                    if (c.Backward && !prevBack)
                    {
                        if (SailSet > 0) { SailSet--; SaveState(true); }
                        else if (mayCrawl) { SetCrawl(true); crawlAhead = -1; }
                    }
                }
                if (c.Jump && !prevJump) { Anchored = !Anchored; SaveState(true); }
                rudderIn = HelmMath.RudderInput(c.Left, c.Right);
                prevFwd = c.Forward; prevBack = c.Backward; prevJump = c.Jump; prevCtrl = ctrl;
            }
            else prevFwd = prevBack = prevJump = prevCtrl = false;
            if (CrawlTestInput is (int ta, float tt))
            {
                crawlAhead = ta;
                crawlTurn = tt;
                rudderIn = tt;
            }
            if (SailSet < 0) SailSet = 0;
            if (!MastUp && SailSet > 0) { SailSet = 0; SaveState(true); }
            // (under the sweeps only furled, at anchor never, afloat; setting sail or letting go the anchor ends it)
            if (Crawl && !Hansa.Crawl.Holds(SailSet, Anchored, GoneDown, Def.RowSpeed)) { Crawl = false; SaveState(true); }
            if (!Crawl) crawlAhead = 0;
            if (crawlAhead != CrawlDrive) { CrawlDrive = crawlAhead; SaveState(true); }

            // the oars: everyone at a rowing thwart pulls (W) or backs (S); A/D pull one side harder, which turns her when
            // nobody has the tiller. Rowing off weighs the grapnel.
            int pulling = 0, rowers = 0;
            float rowTurn = 0;
            foreach (var rs in RowSeats)
            {
                if (rs.Passenger == null) continue;
                rowers++;
                var rc = rs.Controls;
                pulling += (rc.Forward ? 1 : 0) - (rc.Backward ? 1 : 0);
                rowTurn += HelmMath.RudderInput(rc.Left, rc.Right);
            }
            if (pulling != 0 && Anchored) { Anchored = false; SaveState(true); }
            if (c == null && rowers > 0) rudderIn = GameMath.Clamp(rowTurn, -1, 1);
            if (Math.Sign(pulling) != Rowing) { Rowing = Math.Sign(pulling); SaveState(true); }

            float rudderRate = Def.RudderSpeed / 45f;
            Rudder += GameMath.Clamp(rudderIn - Rudder, -rudderRate * dt, rudderRate * dt);

            float sailGoal = SailSet >= 2 ? 1f : SailSet == 1 ? 0.55f : 0f;
            SailFraction += GameMath.Clamp(sailGoal - SailFraction, -0.35f * dt, 0.25f * dt);

            float maxSpeed = Def.MaxSpeed * cfg.SpeedMultiplier * (Clad ? 1 + Def.CladdingSpeedBonus : 1) * (float)SpeedFactorNow;
            if (maxSpeed != MaxSpeedNow || cfg.SailByWind != SailByWind || cfg.Cannons != CannonsOn || cfg.PointCloser != PointCloser)
            {
                MaxSpeedNow = maxSpeed;
                PointCloser = cfg.PointCloser;
                SailByWind = cfg.SailByWind;
                CannonsOn = cfg.Cannons;
                SaveState(true);
            }
            // under sail the wind decides how fast she can go (the helmsman sets sail and steers, the crew trims)
            bool inIrons = false;
            float sailTarget = maxSpeed * (float)SailsDrawing;
            double leeway = 0;
            if (SailByWind)
            {
                var w = WindHere(Pos.X, Pos.Z);
                // (torn sails, a sprung mast or parted rigging drive her less: SailDrive)
                sailTarget = (float)Sailing.TargetSpeed(w.X, w.Z, Pos.Yaw, SailsDrawing, maxSpeed, SailingNow);
                inIrons = SailFraction > 0.05f && Sailing.WindAngle(w.X, w.Z, Pos.Yaw, out _) < SailingNow.NoGo;
                // the wind on her hull and rig (0.3.10): she drifts downwind under bare poles, makes leeway beam-on
                // (not on the bottom or at anchor: then she lies to it)
                if (!Anchored && !OnBottom && !Crawl && Def.RowSpeed <= 0)
                {
                    Sailing.Windage(w.X, w.Z, Pos.Yaw, SailsDrawing, SailingNow, cfg.Windage, out double wAhead, out leeway);
                    double slow = SpeedFactorNow;
                    sailTarget += (float)(wAhead * slow);
                    leeway *= slow;
                }
            }
            Leeway += (float)((leeway - Leeway) * Math.Min(1, dt / 3));
            float target = Anchored || OnBottom ? 0 : Crawl ? Hansa.Crawl.Target(crawlAhead, cfg.CrawlSpeed, cfg.CrawlAsternSpeed) : sailTarget;
            WhySlow = Crawl ? null : SlowReason(target, maxSpeed);
            float rowTarget = Oars.Target(pulling, Def.RowSpeed * cfg.SpeedMultiplier);
            if (!Anchored && rowTarget != 0) target = Oars.Combine(target, rowTarget, maxSpeed);
            if (SailSet == 0 && !Anchored && Math.Abs(Speed) < 0.05f && Math.Abs(target) < 0.05f && rowTarget == 0) target = 0;
            float decel = Def.Deceleration * (Anchored ? 3 : 1);
            if (Crawl) Speed = Hansa.Crawl.StepSpeed(Speed, target, Math.Max(cfg.CrawlSpeed, cfg.CrawlAsternSpeed), cfg.CrawlRampSeconds, decel, dt);
            else Speed += GameMath.Clamp(target - Speed, -decel * dt, Def.Acceleration * dt);

            float steer = (float)Sailing.Steer(Speed, maxSpeed, inIrons, SailingNow);
            // pulling one side harder turns a boat even with no way on
            if (c == null && rowers > 0 && rowTurn != 0) steer = Math.Max(steer, 0.7f);
            // (a damaged rudder answers less, water in her makes her less handy)
            // (HelmMath.YawRate: the helm to starboard swings her bow to starboard with way on; the offline tests pin it)
            YawRate = (float)HelmMath.YawRate(Rudder, Def.TurnRate, steer * (Anchored ? 0.3f : 1f) * RudderAnswers * ShipFlood.Sluggish(DamageParts, DamageOn ? Water : 0));
            // ((0.3.25) under the sweeps A/D turn her directly about her middle, with way on or none)
            if (Crawl) YawRate = Hansa.Crawl.StepYaw(lastCrawlYaw, Hansa.Crawl.YawTarget(crawlTurn, cfg.CrawlTurn), cfg.CrawlTurn, dt);
            lastCrawlYaw = Crawl ? YawRate : 0;

            double newYaw = Pos.Yaw + YawRate * dt;
            double fx = Math.Sin(newYaw), fz = Math.Cos(newYaw);
            // (her leeway: to starboard of her heading is (-cos yaw, sin yaw) in the world)
            double nx = Pos.X + fx * Speed * dt - Math.Cos(newYaw) * Leeway * dt, nz = Pos.Z + fz * Speed * dt + Math.Sin(newYaw) * Leeway * dt;

            if (Math.Abs(Speed) > 1e-4 || Math.Abs(YawRate) > 1e-5)
            {
                var pose = PoseToward(nx, nz, newYaw);
                if (pose == PoseState.Unloaded)
                {
                    // the sea ahead isn't loaded yet (just after a teleport or the seam jump): wait for it, keep way on
                    RequestChunksAround(nx, nz);
                    Waiting = true;
                }
                else if (pose == PoseState.Blocked && blockedByShip)
                {
                    // against another ship or a boat: she stops there (no "aground"), and both take the blow
                    Waiting = false;
                    Rammed(shipContact, shipContactAt, nx, nz, newYaw, dt);
                    Speed = Math.Sign(Speed) * -0.05f;
                    YawRate = 0;
                    SaveState(true);
                }
                else if (pose == PoseState.Blocked)
                {
                    Waiting = false;
                    if (!Aground)
                    {
                        Aground = true;
                        // she bumps off and drives on again with the sails set: say it once, not at every bump
                        if (World.ElapsedMilliseconds - lastAgroundNoticeMs > 20000)
                        {
                            lastAgroundNoticeMs = World.ElapsedMilliseconds;
                            foreach (var s in seatArray)
                                if (s.Passenger is EntityPlayer ep && ep.Player is IServerPlayer sp) Notify(sp, "hansa:aground");
                        }
                    }
                    // where she touched takes the blow (ShipFlooding)
                    Grounded(nx, nz, newYaw, dt);
                    Speed = Math.Sign(Speed) * -0.1f;
                    YawRate = 0;
                    SaveState(true);
                }
                else
                {
                    Waiting = false;
                    if (Aground) { Aground = false; SaveState(true); }
                    Pos.X = nx;
                    Pos.Z = nz;
                    Pos.Yaw = GameMath.Mod((float)newYaw, GameMath.TWOPI);
                }
            }

            if (Waiting != wasWaiting) { wasWaiting = Waiting; SaveState(true); }

            // follow the water surface, and ride the waves on it
            waterCheckAccum += dt;
            if (waterCheckAccum > 2)
            {
                waterCheckAccum = 0;
                double wy = WaterSurfaceY(Pos.X, BaseY, Pos.Z);
                if (!double.IsNaN(wy)) targetY = wy;
            }
            if (!double.IsNaN(targetY) && Math.Abs(targetY - BaseY) > 0.01)
            {
                BaseY += GameMath.Clamp(targetY - BaseY, -0.5 * dt, 0.5 * dt);
            }
            seaAttitude = WaveMotion(Pos.X, Pos.Z, Pos.Yaw);
            Heave = seaAttitude.Heave;
            // her lean in the wind, following it as slowly as clients draw it
            {
                var hw = WindHere(Pos.X, Pos.Z);
                double heel = ShipMotion.Heel(hw.X, hw.Z, Pos.Yaw, Speed, SailsDrawing, Def.Motion, SailingNow);
                ServerHeel += (heel - ServerHeel) * (1 - Math.Exp(-dt / Math.Max(0.1, Def.Motion.HeelResponse)));
            }
            ClearPlantsInHull(dt);
            // leaks, pumps, wear, foundering (ShipFlooding); water in her floats her deeper (Sinkage)
            if (!LaidUp) FloodTick(dt);
            else { PumpedRate = 0; WaterRate = 0; }
            // her guns laid where their gun captains look (ShipGuns)
            GunsTick(dt);
            // her firehearth's fire: vanilla's firepit burn tick (ShipHearth); her oven's (ShipOven)
            HearthTick(dt);
            OvenTick(dt);
            if (OnBottom && !Aground) { Aground = true; SaveState(true); }
            if (!GoneDown)
            {
                Pos.Y = BaseY - Sinkage + Heave;
                writtenY = Pos.Y;
            }

            SaveState(false);
        }

        public enum PoseState { Clear, Blocked, Unloaded }

        /// <summary>Waiting for chunks ahead to load (not aground).</summary>
        public bool Waiting;
        long lastChunkRequestMs;
        bool wasWaiting;
        long lastAgroundNoticeMs = -100000;

        /// <summary>
        /// Hull sample points hitting solid blocks (Blocked) or reaching into chunks that aren't loaded yet (Unloaded).
        /// hits: if given, every block hit goes in it (not just the first).
        /// </summary>
        public static PoseState CheckPose(IWorldAccessor world, ShipDefinition def, double x, double y, double z, double yaw, int dim,
            HashSet<long> hits = null, List<(int sample, long block)> sampleHits = null)
        {
            var ba = world.BlockAccessor;
            var bp = new BlockPos(dim);
            bool unloaded = false, blocked = false;
            for (int n = 0; n < def.HullSamples.Length; n++)
            {
                var s = def.HullSamples[n];
                ShipFrame.ToWorld(yaw, s[0], s[1], s[2], out double wx, out double wy, out double wz);
                bp.Set((int)Math.Floor(x + wx), (int)Math.Floor(y + wy), (int)Math.Floor(z + wz));
                if (ba.GetChunkAtBlockPos(bp) == null) { unloaded = true; continue; }
                var block = ba.GetBlock(bp, BlockLayersAccess.Solid);
                if (block != null && block.Id != 0 && block.CollisionBoxes != null && block.CollisionBoxes.Length > 0)
                {
                    if (hits == null) return PoseState.Blocked;
                    blocked = true;
                    long key = ((long)bp.X << 38) ^ ((long)bp.Y << 26) ^ (bp.Z & 0x3FFFFFF);
                    hits.Add(key);
                    sampleHits?.Add((n, key));
                }
            }
            return blocked ? PoseState.Blocked : unloaded ? PoseState.Unloaded : PoseState.Clear;
        }

        /// <summary>
        /// Whether she can go from where she lies to this pose (at her still-water level): Blocked if a hull sample would
        /// be in a solid block. If she's already lying against something (the water fell under her, she was turned into
        /// a bank, someone built there), she may still move as long as she touches nothing she doesn't touch already, so
        /// she can be worked off with the sweeps or the helm, or dug free.
        /// </summary>
        public PoseState PoseToward(double nx, double nz, double newYaw)
        {
            hitsAhead.Clear();
            contactsAhead.Clear();
            blockedByShip = false;
            // (at the level she floats at now: water in her puts her deeper)
            double floatY = FloatY;
            var pose = CheckPose(World, Def, nx, floatY, nz, newYaw, Pos.Dimension, hitsAhead, contactsAhead);
            if (pose != PoseState.Blocked)
            {
                // ships and boats don't pass through each other (unless they're already tangled: then they may part)
                if (HitsShip(nx, nz, newYaw, out shipContact, out shipContactAt) && !HitsShip(Pos.X, Pos.Z, Pos.Yaw))
                {
                    blockedByShip = true;
                    return PoseState.Blocked;
                }
                return pose;
            }
            hitsHere.Clear();
            if (CheckPose(World, Def, Pos.X, floatY, Pos.Z, Pos.Yaw, Pos.Dimension, hitsHere) == PoseState.Blocked && hitsHere.IsSupersetOf(hitsAhead))
                return PoseState.Clear;
            // only the places she didn't touch already take a blow
            contactsAhead.RemoveAll(c => hitsHere.Contains(c.block));
            return PoseState.Blocked;
        }

        bool blockedByShip;

        /// <summary>
        /// Is this ship-local point in her hull: inside its planking below the main deck (the dry mask: the hold and the
        /// lower deck count, not only the planks), or in her collision anywhere? What another hull or a swimmer can't be in.
        /// </summary>
        public bool HullAt(double x, double y, double z) => Def.DryMask?.SolidAt(x, y, z) == true || Grid.SolidAt(x, y, z);

        /// <summary>
        /// Would she, at this pose (at her still-water level), touch another ship: one of her hull samples inside its
        /// hull, or one of its samples inside hers?
        /// </summary>
        public bool HitsShip(double x, double z, double yaw) => HitsShip(x, z, yaw, out _, out _);

        /// <summary>The same, and which ship and where they meet (world).</summary>
        public bool HitsShip(double x, double z, double yaw, out EntityShip hit, out Vec3d at)
        {
            hit = null;
            at = null;
            var others = Mod?.ServerShips;
            if (others == null) return false;
            double baseY = FloatY;     // (a ship not yet ticked floats where she was put)
            foreach (var other in others)
            {
                if (other == this || other.Def == null || !other.Alive || other.Pos.Dimension != Pos.Dimension) continue;
                double reach = (Def.Length + other.Def.Length) / 2 + 3;
                double dx = other.Pos.X - x, dz = other.Pos.Z - z;
                if (dx * dx + dz * dz > reach * reach || Math.Abs(other.Pos.Y - Pos.Y) > 30) continue;
                foreach (var s in Def.HullSamples)
                {
                    ShipFrame.ToWorld(yaw, s[0], s[1], s[2], out double wx, out double wy, out double wz);
                    var l = other.WorldToLocal(x + wx, baseY + wy, z + wz);
                    if (other.HullAt(l.X, l.Y, l.Z)) { hit = other; at = new Vec3d(x + wx, baseY + wy, z + wz); return true; }
                }
                foreach (var s in other.Def.HullSamples)
                {
                    other.LocalToWorld(s[0], s[1], s[2], out double wx, out double wy, out double wz);
                    ShipFrame.ToLocal(yaw, wx - x, wy - baseY, wz - z, out double lx, out double ly, out double lz);
                    if (HullAt(lx, ly, lz)) { hit = other; at = new Vec3d(wx, wy, wz); return true; }
                }
            }
            return false;
        }

        /// <summary>
        /// Plants inside the hull go: seaweed, sea grass, corals, water lilies (blocks you walk through) the ship sails
        /// into would show through her bottom in the hold, the water being cut out there but not the plants. She pushes
        /// through them as a real ship would. Only the solid block layer is cleared; the water stays.
        /// </summary>
        void ClearPlantsInHull(float dt)
        {
            weedAccum += dt;
            if (weedAccum < 0.5f || Def.DryMask == null) return;
            weedAccum = 0;
            // only when she has moved (or turned) since the last sweep
            if (!double.IsNaN(weedX) && Math.Abs(Pos.X - weedX) + Math.Abs(Pos.Z - weedZ) < 0.3 &&
                Math.Abs(GameMath.AngleRadDistance((float)weedYaw, Pos.Yaw)) < 0.02) return;
            weedX = Pos.X; weedZ = Pos.Z; weedYaw = Pos.Yaw;
            SweepPlants();
        }

        /// <summary>Clear the plants inside the hull where she is now; returns how many went.</summary>
        public int SweepPlants()
        {
            int cleared = 0;
            var g = Def.DryMask;
            if (g == null) return 0;
            var ba = World.BlockAccessor;
            var bp = new BlockPos(Pos.Dimension);
            // the hull's extent in the world, below the main deck
            double r = Math.Max(Math.Max(Math.Abs(g.OX), Math.Abs(g.MaxX)), Math.Max(Math.Abs(g.OZ), Math.Abs(g.MaxZ))) + 1;
            int y0 = (int)Math.Floor(Pos.Y + g.OY), y1 = (int)Math.Floor(Pos.Y + Def.DeckY);
            int x0 = (int)Math.Floor(Pos.X - r), x1 = (int)Math.Floor(Pos.X + r), z0 = (int)Math.Floor(Pos.Z - r), z1 = (int)Math.Floor(Pos.Z + r);
            for (int bx = x0; bx <= x1; bx++)
                for (int bz = z0; bz <= z1; bz++)
                {
                    // well outside the hull's plan: skip the column
                    var c = WorldToLocal(bx + 0.5, Pos.Y, bz + 0.5);
                    if (c.X < g.OX - 1 || c.X > g.MaxX + 1 || c.Z < g.OZ - 1 || c.Z > g.MaxZ + 1) continue;
                    for (int by = y0; by <= y1; by++)
                    {
                        bp.Set(bx, by, bz);
                        var block = ba.GetBlock(bp, BlockLayersAccess.Solid);
                        if (block == null || block.Id == 0 || block.CollisionBoxes?.Length > 0) continue;
                        if (block.BlockMaterial != EnumBlockMaterial.Plant && !(ba.GetBlock(bp, BlockLayersAccess.Fluid)?.IsLiquid() ?? false)) continue;
                        if (!BlockInHull(g, bx, by, bz)) continue;
                        ba.SetBlock(0, bp, BlockLayersAccess.Solid);
                        cleared++;
                    }
                }
            return cleared;
        }

        /// <summary>Does any corner or the centre of this world block lie inside the hull (the design's inside mask)?</summary>
        bool BlockInHull(VoxelGrid inside, int bx, int by, int bz)
        {
            for (int i = 0; i < 9; i++)
            {
                double x = bx + (i == 8 ? 0.5 : (i & 1) * 0.9 + 0.05);
                double y = by + (i == 8 ? 0.5 : ((i >> 1) & 1) * 0.9 + 0.05);
                double z = bz + (i == 8 ? 0.5 : ((i >> 2) & 1) * 0.9 + 0.05);
                var l = WorldToLocal(x, y, z);
                if (inside.SolidAt(l.X, l.Y, l.Z)) return true;
            }
            return false;
        }

        public static bool PoseBlocked(IWorldAccessor world, ShipDefinition def, double x, double y, double z, double yaw, int dim)
            => CheckPose(world, def, x, y, z, yaw, dim) != PoseState.Clear;

        void RequestChunksAround(double x, double z)
        {
            if (!(Api is ICoreServerAPI sapi) || World.ElapsedMilliseconds - lastChunkRequestMs < 1000) return;
            lastChunkRequestMs = World.ElapsedMilliseconds;
            int cx = (int)Math.Floor(x / GlobalConstants.ChunkSize), cz = (int)Math.Floor(z / GlobalConstants.ChunkSize);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    sapi.WorldManager.LoadChunkColumnPriority(cx + dx, cz + dz);
        }

        /// <summary>Top of the water column near y, as the height of the visible surface; NaN if there's no water.</summary>
        public double WaterSurfaceY(double x, double y, double z)
        {
            var ba = World.BlockAccessor;
            var bp = new BlockPos((int)Math.Floor(x), (int)Math.Floor(y) + 3, (int)Math.Floor(z), Pos.Dimension);
            for (int i = 0; i < 12; i++, bp.Y--)
            {
                if (ba.GetChunkAtBlockPos(bp) == null) return double.NaN;
                var fluid = ba.GetBlock(bp, BlockLayersAccess.Fluid);
                if (fluid != null && fluid.IsLiquid()) return bp.Y + 0.875;
            }
            return double.NaN;
        }

        public static double FindWaterSurface(IWorldAccessor world, BlockPos around, int searchUp = 4, int searchDown = 12)
        {
            var ba = world.BlockAccessor;
            var bp = around.Copy();
            bp.Y += searchUp;
            for (int i = 0; i < searchUp + searchDown; i++, bp.Y--)
            {
                var fluid = ba.GetBlock(bp, BlockLayersAccess.Fluid);
                if (fluid != null && fluid.IsLiquid()) return bp.Y + 0.875;
            }
            return double.NaN;
        }

        // ------------------------------------------------------------------ client smoothing

        public override void OnReceivedServerPos(bool isTeleport)
        {
            base.OnReceivedServerPos(isTeleport);
            if (Api.Side != EnumAppSide.Client) return;
            targetPos.Set(Pos.X, Pos.Y, Pos.Z);
            targetYaw = Pos.Yaw;
            targetMs = World.ElapsedMilliseconds;
            if (!smoothInit || isTeleport || RenderPos.SquareDistanceTo(targetPos) > 20 * 20)
            {
                RenderPos.Set(targetPos);
                RenderYaw = targetYaw;
                renderBaseY = targetPos.Y - fullHeave;
                jumpMs = World.ElapsedMilliseconds;
                smoothInit = true;
            }
            Pos.SetPos(RenderPos);
            Pos.Yaw = (float)RenderYaw;
            lastWritten.Set(Pos.X, Pos.Y, Pos.Z);
        }

        /// <summary>Called every frame on the client before player physics: dead-reckon and blend toward the server pose.</summary>
        public void ClientFrame(float dt)
        {
            if (!smoothInit) return;
            dt = Math.Min(dt, 0.1f);

            // Something other than a position packet moved us (e.g. the engine moves the mount when a passenger is
            // teleported, which is how Cylinder World's seam jump reaches the client first): take it as the truth.
            bool jumped = false;
            if (Math.Abs(Pos.X - lastWritten.X) + Math.Abs(Pos.Y - lastWritten.Y) + Math.Abs(Pos.Z - lastWritten.Z) > 1e-6)
            {
                targetPos.Set(Pos.X, Pos.Y, Pos.Z);
                targetMs = World.ElapsedMilliseconds;
                if (RenderPos.SquareDistanceTo(targetPos) > 8 * 8)
                {
                    RenderPos.Set(targetPos);
                    renderBaseY = targetPos.Y - fullHeave;
                    jumpMs = World.ElapsedMilliseconds;
                    jumped = true;
                }
            }
            RenderYaw += YawRate * dt;
            RenderPos.X += Math.Sin(RenderYaw) * Speed * dt - Math.Cos(RenderYaw) * Leeway * dt;
            RenderPos.Z += Math.Cos(RenderYaw) * Speed * dt + Math.Sin(RenderYaw) * Leeway * dt;

            double age = Math.Min(0.5, (World.ElapsedMilliseconds - targetMs) / 1000.0);
            double tyaw = targetYaw + YawRate * age;
            double tx = targetPos.X + Math.Sin(tyaw) * Speed * age;
            double tz = targetPos.Z + Math.Cos(tyaw) * Speed * age;
            double k = 1 - Math.Exp(-dt * 3);
            RenderPos.X += (tx - RenderPos.X) * k;
            RenderPos.Z += (tz - RenderPos.Z) * k;
            RenderYaw += GameMath.AngleRadDistance((float)RenderYaw, (float)tyaw) * k;

            // height: the still-water level the server floats her at, plus the waves (worked out here from the same
            // waves and clock as the server's, so no lag behind the drawn water)
            if (double.IsNaN(renderBaseY)) renderBaseY = double.IsNaN(BaseY) ? targetPos.Y : BaseY;
            if (!double.IsNaN(BaseY))
            {
                // right after a teleport the synced level may still be the old one: wait a moment for the new one
                double gap = BaseY - renderBaseY;
                if (Math.Abs(gap) < 2 || World.ElapsedMilliseconds - jumpMs > 1500) renderBaseY += gap * k;
            }
            RideWaves(dt);
            RenderPos.Y = renderBaseY - sinkShown + Heave;

            // the rudder arrives a few times a second: the wheel and the tiller follow it at the helm's own rate (a
            // little quicker, so they never fall behind), without steps
            if (Def != null)
            {
                float rate = Def.RudderSpeed / 45f * 1.3f;
                RudderShown += GameMath.Clamp(Rudder - RudderShown, -rate * dt, rate * dt);
            }

            Pos.SetPos(RenderPos);
            Pos.Yaw = (float)RenderYaw;
            lastWritten.Set(Pos.X, Pos.Y, Pos.Z);
            // her guns as drawn, and the gun this player is captain of laid from their view (ShipGuns)
            GunsFrame(dt);
            if (jumped) Mod.Client?.OnShipJumped(this);
        }

        /// <summary>Client: this frame's drawn heave, pitch and roll (with the lean in the wind), scaled by SeaMotion.</summary>
        void RideWaves(float dt)
        {
            EaseFlood(dt);
            // on the bottom the waves don't reach her; going down they let go of her
            var mo = Stage == SinkStage.Sunk ? default : WaveMotion(RenderPos.X, RenderPos.Z, RenderYaw);
            if (Stage == SinkStage.Sinking) mo = mo.Scaled(Math.Max(0, 1 - sinkShown / 6));
            fullHeave = mo.Heave;
            double heel = 0;
            if (Def != null)
            {
                var w = WindHere(RenderPos.X, RenderPos.Z);
                heel = ShipMotion.Heel(w.X, w.Z, RenderYaw, Speed, SailsDrawing, Def.Motion, SailingNow);
                // the crew trims to the wind the sails feel; the yards come round over a few seconds
                Sailing.Apparent(w.X, w.Z, RenderYaw, Speed, out double ax, out double az);
                Sailing.Trim(Sailing.WindAngle(ax, az, RenderYaw, out int side), side, SailingNow, out double brace, out double sheet);
                double kt = 1 - Math.Exp(-dt * 0.6);
                Brace += (brace - Brace) * kt;
                Sheet += (sheet - Sheet) * kt;
            }
            // her lean follows the wind in her sails slowly (a heavy hull, sails steadying her, the crew easing the sheets
            // in a gust: MotionDef.HeelResponse), as on the server (ServerHeel)
            heelNow += (heel - heelNow) * (1 - Math.Exp(-dt / Math.Max(0.1, Def?.Motion.HeelResponse ?? 4)));
            double k = Mod.Client?.SeaMotion ?? 1;
            Heave = mo.Heave * k;
            // (her trim and list from the water in her, with the lean in the wind)
            Pitch = mo.Pitch * k + trimShown * k;
            Heel = heelNow * k;
            Roll = mo.Roll * k + Heel + listShown * k;
            // as she really lies, whatever this player's SeaMotion (her guns are laid in it, as on the server)
            TruePitch = mo.Pitch + trimShown;
            TrueRoll = mo.Roll + heelNow + listShown;
        }

        double heelNow;
        /// <summary>Server: her lean in the wind (radians, starboard down +), eased as clients ease theirs (<see cref="MotionDef.HeelResponse"/>).</summary>
        public double ServerHeel;

        /// <summary>
        /// Client: how the sails are trimmed now (radians about the ship's up axis, + turns the starboard end forward
        /// and the after end to starboard): the square yards' brace and a fore-and-aft sail's sheet (see
        /// <see cref="Sailing.Trim"/>). For drawing the yards round.
        /// </summary>
        public double Brace, Sheet;

        /// <summary>Sea's wind at a place (the same on every side); calm if Sea isn't there.</summary>
        public Sea.Wind WindHere(double x, double z) => SeaSys != null ? SeaSys.WindAt(x, z) : default;

        public override string GetName() => ShipName ?? Def?.Name ?? base.GetName();
    }
}
