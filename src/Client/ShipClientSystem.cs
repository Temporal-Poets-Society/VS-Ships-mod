using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// Client side of ships: smooth ship motion, walking the deck (stepped in lockstep with player physics),
    /// boarding and leaving, using fixtures, the helm HUD, what the weather patches need to know, and the view on a
    /// deck that rides the waves (with the player's comfort settings, .hansa).
    /// </summary>
    public class ShipClientSystem : ModSystem, IRenderer
    {
        public static ShipClientSystem Instance;

        ICoreClientAPI capi;
        HansaMod mod;
        public HansaMod Hansa => mod;
        IClientNetworkChannel channel;
        public readonly List<EntityShip> Ships = new();
        /// <summary>Hansa's rowing boats this client knows (for holding the waves down under them).</summary>
        public readonly List<EntityRowBoat> RowBoats = new();
        /// <summary>The cannon balls in flight and the guns' flashes.</summary>
        public CannonClient Cannons;
        /// <summary>The surveyor's chalk: notes for reporting, their marks, .hansa notes.</summary>
        public SurveyorsChalk Chalk;
        /// <summary>The ships' firehearths: their flames and smoke, the stovepipes' smoke, their crackle.</summary>
        public HearthClient Hearths;
        /// <summary>Decorating: what's in the crosshair of it, its outline, its clicks (pieces, mount points, paint).</summary>
        public DecoClient Deco;
        /// <summary>The bilge pumps' water gushing out of their spouts while they bring water up.</summary>
        public PumpClient Pumps;
        ShipRenderer renderer;
        public ShipRenderer Renderer => renderer;
        ShipHud hud;

        float sendAccum;
        (long ship, float x, float y, float z, byte flags) lastSentDeck;
        long lastSentDeckMs;
        long boardCooldownUntil;
        Vec3d boardedFrom;
        long boardedShip;
        int boardAttempt;
        FixtureHit lookedAt;
        // the gun last clicked (the game repeats a held click: one shot a click)
        (long Ship, int Gun, long Ms) lastGunClick;
        (double x, double z) lastWishLocal;
        // boarding a rope ladder from the water: which one, until the mount arrives
        long pendingLadderShip;
        string pendingLadder;
        long pendingLadderUntil;

        public double RenderOrder => 0.1;
        public int RenderRange => 9999;

        const string ComfortFile = "hansa-client.json";
        /// <summary>This player's comfort settings (ModConfig/hansa-client.json).</summary>
        public HansaClientConfig Comfort = new();
        /// <summary>How much of the sea's motion ships are drawn with here (0..1).</summary>
        public float SeaMotion => GameMath.Clamp(Comfort.SeaMotion, 0, 1);
        GuiDialogSeaComfort comfortDialog;
        // how much the deck's tilt has turned the view pitch so far (pushed as changes)
        double pushedLook;

        public struct FixtureHit
        {
            public EntityShip Ship;
            public FixtureDef Fixture;
            public double Dist;
        }

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            Instance = this;
            mod = api.ModLoader.GetModSystem<HansaMod>();
            mod.Client = this;
            // (22 October 2026) particles in her dry hold behave as in air (ShipDryParticles)
            ShipDryParticles.Dry = mod.Dry;

            Cannons = new CannonClient(api);
            channel = api.Network.GetChannel(HansaMod.ChannelName)
                .SetMessageHandler<DeckStatePacket>(OnRemoteDeckState)
                .SetMessageHandler<FixturePacket>(OnOpenStorage)
                .SetMessageHandler<ShotPacket>(Cannons.OnShot);

            api.Event.RegisterRenderer(this, EnumRenderStage.Before, "hansa-ships");
            renderer = new ShipRenderer(api, this);
            api.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "hansa-ships");
            // her shadow, into the sun's shadow maps (the game runs these stages only with shadows on)
            api.Event.RegisterRenderer(renderer, EnumRenderStage.ShadowFar, "hansa-ships-shadow");
            api.Event.RegisterRenderer(renderer, EnumRenderStage.ShadowNear, "hansa-ships-shadow");
            api.Input.InWorldAction += OnInWorldAction;
            hud = new ShipHud(api, this);
            Chalk = new SurveyorsChalk(api, this);
            Hearths = new HearthClient(api, this);
            // decorating: pieces put aboard, mount points, paint (DecoClient)
            Deco = new DecoClient(api, this, channel);
            Pumps = new PumpClient(api, this);

            // Sea owns the water shaders; it asks the renderer where the hulls are and keeps the waves out
            sea = api.ModLoader.GetModSystem<Sea.SeaSystem>();
            sea?.AddWaterClipSource(renderer);

            try { Comfort = api.LoadModConfig<HansaClientConfig>(ComfortFile) ?? new HansaClientConfig(); }
            catch (Exception e) { Mod.Logger.Error("Bad ModConfig/{0}, using defaults: {1}", ComfortFile, e.Message); Comfort = new HansaClientConfig(); }
            api.StoreModConfig(Comfort, ComfortFile);
            Patches.ApplyClient(Mod.Logger);
            RegisterCommands(api);
        }

        /// <summary>
        /// (0.3.19, the user: "the carrack doesn't spawn after 1.5 s but much longer") A design's meshes built and its mesh
        /// file (megabytes of JSON) read in the background before any ship of it is here: a charter held, or its use begun.
        /// </summary>
        public void PreloadDesign(string code)
        {
            if (code == null || renderer == null) return;
            var def = mod.GetShip(code);
            if (def != null) renderer.Preload(def);
        }

        /// <summary>Change the comfort settings (the dialog, .hansa) and remember them.</summary>
        public void SetComfort(float seaMotion, bool steadyHorizon)
        {
            Comfort.SeaMotion = GameMath.Clamp(seaMotion, 0, 1);
            Comfort.SteadyHorizon = steadyHorizon;
            capi.StoreModConfig(Comfort, ComfortFile);
        }

        void RegisterCommands(ICoreClientAPI api)
        {
            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands.Create("hansa")
                .WithDescription(Lang.Get("hansa:cmd-comfort"))
                .HandleWith(_ =>
                {
                    // built fresh each time, so it shows the settings as they are now
                    if (comfortDialog?.IsOpened() != true)
                    {
                        comfortDialog?.Dispose();
                        comfortDialog = new GuiDialogSeaComfort(capi, this);
                        comfortDialog.TryOpen();
                    }
                    return TextCommandResult.Success();
                })
                .BeginSubCommand("seamotion")
                    .WithDescription(Lang.Get("hansa:cmd-seamotion"))
                    .WithArgs(parsers.OptionalWord("amount"))
                    .HandleWith(args =>
                    {
                        if (args[0] is string a)
                        {
                            a = a.Trim().TrimEnd('%');
                            if (!double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                                return TextCommandResult.Error(Lang.Get("hansa:cmd-seamotion"));
                            if (v > 1) v /= 100;
                            SetComfort((float)v, Comfort.SteadyHorizon);
                        }
                        return TextCommandResult.Success(ComfortText());
                    })
                .EndSubCommand()
                .BeginSubCommand("steadyhorizon")
                    .WithDescription(Lang.Get("hansa:cmd-steadyhorizon"))
                    .WithArgs(parsers.OptionalWordRange("state", "on", "off"))
                    .HandleWith(args =>
                    {
                        bool on = args[0] is string st ? st == "on" : !Comfort.SteadyHorizon;
                        SetComfort(Comfort.SeaMotion, on);
                        return TextCommandResult.Success(ComfortText());
                    })
                .EndSubCommand()
                .BeginSubCommand("shadows")
                    .WithDescription(Lang.Get("hansa:cmd-shadows"))
                    .WithArgs(parsers.OptionalWordRange("state", "on", "off"))
                    .HandleWith(args =>
                    {
                        Comfort.ShipShadows = args[0] is string st ? st == "on" : !Comfort.ShipShadows;
                        capi.StoreModConfig(Comfort, ComfortFile);
                        return TextCommandResult.Success(Lang.Get("hansa:shadows-now",
                            Lang.Get(Comfort.ShipShadows ? "hansa:comfort-steady-on" : "hansa:comfort-steady-off")));
                    })
                .EndSubCommand()
                .BeginSubCommand("build")
                    .WithDescription(Lang.Get("hansa:cmd-build"))
                    .WithArgs(parsers.OptionalWordRange("state", "on", "off"))
                    .HandleWith(args =>
                    {
                        // (0.3.19) building mode: the server sets it (and says so in chat)
                        Deco?.SendBuild(args[0] is string st ? st == "on" : null);
                        return TextCommandResult.Success();
                    })
                .EndSubCommand()
                .BeginSubCommand("notes")
                    .WithDescription(Lang.Get("hansa:cmd-notes"))
                    .WithArgs(parsers.OptionalWord("what"), parsers.OptionalWord("number"))
                    .HandleWith(args => Chalk?.Command(args[0] as string, args[1] as string) ?? TextCommandResult.Error("No chalk."))
                .EndSubCommand()
                .BeginSubCommand("damagecheck")
                    .WithDescription(Lang.Get("hansa:cmd-damagecheck"))
                    .HandleWith(cmd =>
                    {
                        // the ship in the crosshair, else the nearest: what drawing her damage has (0.3.7)
                        var ep = capi.World.Player?.Entity;
                        if (ep == null || renderer == null) return TextCommandResult.Error(Lang.Get("hansa:damagecheck-none"));
                        var ship = ShipLookedAt(ep, capi.World.Player.WorldData.PickingRange, out _) ?? NearestShip(ep.Pos.XYZ);
                        if (ship == null) return TextCommandResult.Error(Lang.Get("hansa:damagecheck-none"));
                        try { return TextCommandResult.Success(renderer.DamageReport(ship)); }
                        catch (Exception e) { return TextCommandResult.Error("damagecheck failed: " + e.Message); }
                    })
                .EndSubCommand();
        }

        string ComfortText() => Lang.Get("hansa:comfort-now", (int)Math.Round(SeaMotion * 100),
            Lang.Get(Comfort.SteadyHorizon ? "hansa:comfort-steady-on" : "hansa:comfort-steady-off"));

        Sea.SeaSystem sea;

        public override void Dispose()
        {
            if (capi != null)
            {
                capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
                if (renderer != null)
                {
                    capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
                    capi.Event.UnregisterRenderer(renderer, EnumRenderStage.ShadowFar);
                    capi.Event.UnregisterRenderer(renderer, EnumRenderStage.ShadowNear);
                }
                capi.Input.InWorldAction -= OnInWorldAction;
            }
            if (renderer != null) sea?.RemoveWaterClipSource(renderer);
            if (ShipDryParticles.Dry == mod?.Dry) ShipDryParticles.Dry = null;
            renderer?.Dispose();
            Cannons?.Dispose();
            Chalk?.Dispose();
            Hearths?.Dispose();
            Deco?.Dispose();
            Pumps?.Dispose();
            hud?.Dispose();
            comfortDialog?.Dispose();
            nameDialog?.Dispose();
            if (Instance == this) Instance = null;
            ShipShelter.LocalSheltered = false;
            ShipShelter.LocalOpening = null;
            ShipCamera.Roll = 0;
        }

        public void OnShipLoaded(EntityShip ship)
        {
            if (!Ships.Contains(ship)) Ships.Add(ship);
            mod.Dry.Set(Ships);
            renderer?.AddLights(ship);
            foreach (var plr in capi.World.AllOnlinePlayers)
            {
                if (plr.Entity is not EntityPlayer ep) continue;

                // still attached to an older copy of this ship (it was sent to us again): move onto this copy
                if (ep.MountedOn is ShipSeat old && old.Ship != ship && old.Ship.EntityId == ship.EntityId)
                {
                    var t = new Vintagestory.API.Datastructures.TreeAttribute();
                    old.MountableToTreeAttributes(t);
                    if (ship.SeatFromTree(t) is ShipSeat fresh)
                    {
                        if (old is DeckSeat od && fresh is DeckSeat fd) { var l = od.LocalPos; fd.SetLocal(l.X, l.Y, l.Z, od.Flags); }
                        ep.TryMount(fresh);
                    }
                    continue;
                }

                // players whose "mountedOn" pointed at this ship before it arrived
                if (ep.MountedOn != null) continue;
                if (ep.WatchedAttributes["mountedOn"] is Vintagestory.API.Datastructures.TreeAttribute tree &&
                    tree.GetString("className") == ShipSeat.ClassName && tree.GetLong("entityIdMount") == ship.EntityId &&
                    ship.SeatFromTree(tree) is IMountableSeat seat)
                {
                    ep.TryMount(seat);
                }
            }
        }

        static readonly HarmonyLib.AccessTools.FieldRef<Vintagestory.GameContent.EntityBehaviorControlledPhysics, Vec3d> physPrevPos =
            TryPrevPosRef();

        static HarmonyLib.AccessTools.FieldRef<Vintagestory.GameContent.EntityBehaviorControlledPhysics, Vec3d> TryPrevPosRef()
        {
            try { return HarmonyLib.AccessTools.FieldRefAccess<Vintagestory.GameContent.EntityBehaviorControlledPhysics, Vec3d>("prevPos"); }
            catch { return null; }
        }

        /// <summary>
        /// The ship was teleported (seam jump, /tp). The engine first puts passengers at the ship's origin; put the local
        /// player straight at their seat, and reset the physics' previous position so the camera doesn't sweep
        /// between the old and new place for a frame.
        /// </summary>
        public void OnShipJumped(EntityShip ship)
        {
            var ep = capi.World.Player?.Entity;
            if (!(ep?.MountedOn is ShipSeat seat) || seat.Ship != ship) return;
            var sp = seat.SeatPosition;
            ep.Pos.SetPos(sp.X, sp.Y, sp.Z);
            ep.CameraPos.Set(sp.X, sp.Y, sp.Z);
            var phys = ep.GetBehavior<Vintagestory.GameContent.EntityBehaviorPlayerPhysics>();
            if (phys != null && physPrevPos != null) physPrevPos(phys).Set(sp.X, sp.Y, sp.Z);
        }

        public void OnShipUnloaded(EntityShip ship)
        {
            Ships.Remove(ship);
            mod.Dry.Set(Ships);
            renderer?.RemoveShip(ship);
        }

        // ------------------------------------------------------------------ per frame

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (capi.IsGamePaused) return;
            foreach (var ship in Ships) ship.ClientFrame(dt);
            Cannons?.Update();

            var ep = capi.World.Player?.Entity;
            if (ep == null) return;
            NoteLeftRowBoat(ep);
            UpdateView(ep, dt);

            if (ep.MountedOn is DeckSeat ds && ds.Ship.Def != null)
            {
                sendAccum += dt;
                if (sendAccum >= 1 / 15f)
                {
                    sendAccum = 0;
                    // (review H4-1) where the walker's feet are (its collision height, which the server checks against her), not the
                    // height the camera glides at over steps: others' clients glide it themselves (DeckSeat.PushRemote)
                    var m = ds.Mover;
                    var l = m != null ? new Vec3d(m.X, m.Y, m.Z) : ds.LocalPos;
                    // (review H1-15) only when we moved or our stance changed, and once a second standing still
                    var s = (ship: ds.Ship.EntityId, x: (float)l.X, y: (float)l.Y, z: (float)l.Z, flags: ds.Flags);
                    long now = capi.World.ElapsedMilliseconds;
                    var o = lastSentDeck;
                    if (s.ship != o.ship || s.flags != o.flags || Math.Abs(s.x - o.x) > 0.01f || Math.Abs(s.y - o.y) > 0.01f ||
                        Math.Abs(s.z - o.z) > 0.01f || now - lastSentDeckMs >= 1000)
                    {
                        lastSentDeck = s;
                        lastSentDeckMs = now;
                        channel.SendPacket(new DeckStatePacket { ShipId = s.ship, X = s.x, Y = s.y, Z = s.z, Flags = s.flags });
                    }
                }
            }
            else if (ep.MountedOn == null)
            {
                TryAutoBoard(ep);
                if (ep.MountedOn == null) TryRowBoatBoard(ep);
                if (ep.MountedOn == null) TryBoatBoard(ep, ep.Controls, false);
                if (ep.MountedOn == null) TryLadderBoard(ep, ep.Controls, null);
                if (ep.MountedOn == null) KeepOutOfHulls(ep);
            }
            // in a boat alongside a ship: walk into her rope ladder to climb it
            if (ep.MountedOn is DeckSeat inBoat && inBoat.Ship.Def?.BoardFromWater == true && inBoat.Mover?.Climbing == null)
            {
                TryLadderBoard(ep, inBoat.Controls, inBoat.Ship);
            }
            // things lying on her decks, where she's drawn now (once the player is where they'll be this frame)
            DeckItems.ClientFrame(capi, renderer, ep);
            DeckCreatures.ClientFrame(capi, renderer, ep);

            // (0.3.13) the weather by what's overhead in her rooms, not by the room being sealed: none under her decks, and
            // under the open hatch only through it; on her open deck vanilla's
            bool inRoom = mod.Config.CozyCabins && ShipShelter.InRoom(ep);
            bool covered = inRoom && ShipShelter.Covered(ep);
            ShipShelter.LocalSheltered = covered;
            if (inRoom && !covered) ShipShelter.LocalHeadY = ShipShelter.HeadWorldY(ep);
            ShipShelter.LocalOpening = inRoom && !covered ? ShipShelter.OpeningOver(ep) : null;
            lookedAt = FindLookedAtFixture(ep);
            // a piece, a free mount point or a paint zone in front of any fixture (DecoClient)
            // ((25 October 2026, notes 97, 98) and in front of a block of the world: aimed at a pier beside her, it's the pier's)
            Deco?.Update(ep, Math.Min(lookedAt.Ship != null ? lookedAt.Dist : double.MaxValue, WorldBlockDistance(ep)));
            Deco?.UpdateTaking(ep);
            // (a press ends when the button is let go)
            if (!capi.Input.InWorldMouseButton.Right) rightSwallowed = false;
            // laying a gun (holding right mouse by it)
            UpdateLaying(ep);
            // (0.3.19) a charter in hand: her meshes loaded now, so she shows as soon as she's launched
            if (capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is ItemShipDeed deed && !deed.IsBoat) PreloadDesign(deed.ShipCodeOf);
            string hint = null;
            if (ep.MountedOn is DeckSeat cs && cs.Mover != null)
            {
                if (cs.Mover.Climbing != null) hint = Lang.Get(cs.Mover.Climbing.KindOrDefault switch
                {
                    "rope" => "hansa:hud-climbing-side",
                    "ladder" => "hansa:hud-climbing-ladder",
                    "stair" => "hansa:hud-climbing-stair",
                    _ => "hansa:hud-climbing"
                });
                else hint = LadderHint(cs);
            }
            else if (ep.MountedOn == null && lookedAt.Ship == null)
            {
                hint = HullHint(ep);
            }
            if (hint == null && ep.MountedOn is ShipSeat && capi.World.Player.CurrentEntitySelection?.Entity is EntityRowBoat)
            {
                // from a ship's deck or ladder, looking at a rowing boat alongside
                hint = Lang.Get("hansa:hint-rowboat-pickup");
            }
            if (hint == null && lookedAt.Ship == null && ep.MountedOn is ShipSeat on)
            {
                // from a ship's deck or ladder, looking at a boat alongside
                var b = ShipLookedAt(ep, capi.World.Player.WorldData.PickingRange, out _);
                if (b != null && b != on.Ship && b.Def.PickUpItem != null) hint = Lang.Get("hansa:hint-boat-pickup");
            }
            // (with the surveyor's chalk in hand a click is the chalk's: its line over a fixture's)
            hud.Update(ep, Deco?.Hint != null ? default : lookedAt, Deco?.Hint ?? hint, UpdateWork() ?? Deco?.Busy ?? (laying.Ship != null ? laying.Ship.GunHudText(laying.Gun, ep, true) : null) ?? (Deco != null ? Deco.ChalkLine(Chalk?.HudText(ep)) : Chalk?.HudText(ep)));
        }

        /// <summary>
        /// On a deck that pitches and rolls, the view tips with it (unless the player keeps the horizon steady): the
        /// tilt along the view goes into the view pitch as changes, the tilt across it rolls the view. Both ease back
        /// to level off the ship.
        /// </summary>
        void UpdateView(EntityPlayer ep, float dt)
        {
            bool aboard = !Comfort.SteadyHorizon && ep.MountedOn is ShipSeat seat && seat is not BunkSeat && seat.Ship.Def != null;
            double look, roll;
            if (aboard)
            {
                var ship = ((ShipSeat)ep.MountedOn).Ship;
                ShipCamera.ViewTilt(ship.Pitch, ship.Roll, ep.Pos.Yaw - ship.Pos.Yaw, out look, out roll);
            }
            else
            {
                double k = Math.Exp(-dt * 4);
                look = Math.Abs(pushedLook) < 1e-4 ? 0 : pushedLook * k;
                roll = Math.Abs(ShipCamera.Roll) < 1e-4 ? 0 : ShipCamera.Roll * k;
            }
            double d = look - pushedLook;
            if (d != 0)
            {
                // vanilla pitch: π is level, less looks up
                ep.Pos.Pitch = GameMath.Clamp(ep.Pos.Pitch - (float)d, 1.5857964f, 4.697389f);
                capi.World.Player.CameraPitch = ep.Pos.Pitch;
            }
            pushedLook = look;
            ShipCamera.Roll = roll;
        }

        /// <summary>Called from the player physics step (60 Hz), right before the engine reads the seat position.</summary>
        public void StepLocal(EntityPlayer ep, float dt)
        {
            if (!(ep.MountedOn is DeckSeat ds) || ds.Mover == null || ds.Ship.Def == null) return;
            var ship = ds.Ship;
            var c = ds.Controls;

            double fwd = (c.Forward ? 1 : 0) - (c.Backward ? 1 : 0);
            double left = (c.Left ? 1 : 0) - (c.Right ? 1 : 0);
            double yaw = capi.Input.MouseYaw;
            double wx = fwd * Math.Sin(yaw) + left * Math.Cos(yaw);
            double wz = fwd * Math.Cos(yaw) - left * Math.Sin(yaw);
            double len = Math.Sqrt(wx * wx + wz * wz);
            if (len > 1e-6) { wx /= len; wz /= len; }
            var ld = ship.WorldDirToLocal(wx, 0, wz);

            double walk = 3.5 * GameMath.Clamp(ep.Stats.GetBlended("walkspeed"), 0, 5);
            double speed = walk * (c.Sneak ? GlobalConstants.SneakSpeedMultiplier : c.Sprint ? GlobalConstants.SprintSpeedMultiplier : 1);
            float jumpMul = ep.Stats.GetBlended("jumpHeightMul");

            var m = ds.Mover;
            m.TopClosed = ship.LadderTopClosed;
            m.ThroughClosed = ship.LadderThroughClosed;
            m.CoverUnder = ship.LadderCover;
            m.SteepFlights = ship.SteepFlights;
            if (pendingLadder != null && capi.World.ElapsedMilliseconds > pendingLadderUntil) pendingLadder = null;
            if (pendingLadder != null)
            {
                // just boarded from the water onto a rope ladder: start at its bottom
                var l = ship.EntityId == pendingLadderShip ? Array.Find(ship.Ladders, x => x.Code == pendingLadder) : null;
                if (l != null && m.Climbing == null) m.StartClimbAtBottom(l);
                pendingLadder = null;
            }
            int climb = c.Forward || c.Jump ? 1 : c.Backward || c.Sneak ? -1 : 0;
            var result = m.Step(ship.Grid, dt, ld.X, ld.Z, len > 1e-6 ? speed : 0, c.Jump, c.Sneak, jumpMul, ship.Ladders, climb);
            byte flags = 0;
            if (m.Moving && (len > 1e-6 || m.Climbing != null)) flags |= DeckSeat.FlagMoving;
            if (c.Sprint) flags |= DeckSeat.FlagSprint;
            if (c.Sneak) flags |= DeckSeat.FlagSneak;
            if (m.OnGround) flags |= DeckSeat.FlagOnGround;
            // ((0.3.27, note 219) down or up a brow one walks)
            if (m.Climbing != null && m.Climbing.Kind != "brow") flags |= DeckSeat.FlagClimbing;
            ds.Flags = flags;
            lastWishLocal = len > 1e-6 ? (ld.X, ld.Z) : (0, 0);

            if (result == DeckMover.Result.Overboard) LeaveShip(ep, ds);
            else if (result == DeckMover.Result.LeftShip)
            {
                LeaveShip(ep, ds, keepShipSpeed: false);
                // down a rope ladder into a boat waiting at its foot
                TryBoatBoard(ep, ep.Controls, true);
            }
        }

        /// <summary>What to tell someone standing near something they can climb.</summary>
        string LadderHint(DeckSeat ds)
        {
            var m = ds.Mover;
            var ahead = m.LadderAhead(ds.Ship.Ladders, lastWishLocal.x, lastWishLocal.z, out double startT);
            if (ahead != null) return LadderName(ahead, startT >= 1);
            foreach (var l in ds.Ship.Ladders)
            {
                double ex = l.ExitTop[0] - m.X, ez = l.ExitTop[2] - m.Z;
                bool nearTop = ex * ex + ez * ez < 1.2 && Math.Abs(m.Y - l.ExitTop[1]) < 0.4;
                if (l.Outside)
                {
                    if (nearTop) return Lang.Get(l.Kind == "brow" ? "hansa:hint-brow" : ds.Ship.LadderTopClosed(l) ? "hansa:hint-sideladder-gate" : "hansa:hint-sideladder");
                    continue;
                }
                double dx = l.Bottom[0] - m.X, dz = l.Bottom[2] - m.Z;
                bool nearBottom = dx * dx + dz * dz < 1.6 * 1.6 && Math.Abs(m.Y - l.ExitBottom[1]) < 0.6;
                if (nearTop || nearBottom) return LadderName(l, nearTop);
            }
            return null;
        }

        static string LadderName(LadderDef l, bool atTop) => l.KindOrDefault switch
        {
            "rope" => Lang.Get("hansa:hint-sideladder"),
            "brow" => Lang.Get("hansa:hint-brow"),
            "ladder" => Lang.Get(atTop ? "hansa:hint-companion-down" : "hansa:hint-companion-up"),
            // (0.3.18, note 162) a ladder-stair put aboard
            "stair" => Lang.Get(atTop ? "hansa:hint-stair-down" : "hansa:hint-stair-up"),
            _ => Lang.Get("hansa:hint-ratlines")
        };

        /// <summary>Off the ship, looking at the hull: what a right-click would do.</summary>
        string HullHint(EntityPlayer ep)
        {
            double range = capi.World.Player.WorldData.PickingRange;
            // ((25 October 2026, notes 97, 98) not past a block of the world in front of her)
            if (ShipWallDistance(ep, range) >= Math.Min(range, WorldBlockDistance(ep))) return null;
            var ship = ShipLookedAt(ep, range, out _) ?? NearestShip(ep.Pos.XYZ);
            if (ship?.Def == null || ship.GoneDown) return null;
            if (ship.Def.BoardFromWater)
            {
                return Lang.Get("hansa:hint-board-boat") + (ship.Def.PickUpItem != null ? " · " + Lang.Get("hansa:hint-boat-pickup") : "");
            }
            var held = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            // ((0.1.0-dev.9, note 101) copper plates, or copper sheets: two a plate)
            if (ShipFittings.CladdingItemsPerPlate(held) > 0)
            {
                return ship.Clad ? Lang.Get("hansa:hint-clad-done") : Lang.Get("hansa:hint-clad", ship.CladPlates, ship.Def.CladdingPlates);
            }
            return NearestOutsideLadder(ship, ep.Pos.XYZ, 5) != null ? Lang.Get("hansa:hint-board-ladder") : Lang.Get("hansa:hint-board-where");
        }

        /// <summary>A rope ladder down the side whose bottom is within reach of this world position.</summary>
        long ladderHintAt = -100000;

        /// <summary>Off her: the outside rope ladder the crosshair is on (within a block of it, standing by its foot), or none.</summary>
        (EntityShip, LadderDef)? LadderInSight(EntityPlayer ep, double range)
        {
            double wall = ShipWallDistance(ep, range);
            // ((25 October 2026, note 97: "cannot place block here as snaps aggressively to ship rope ladder") a block of the
            // world in front of her (a pier being built beside her) is what's aimed at: no ladder behind it
            if (wall >= range || wall >= WorldBlockDistance(ep)) return null;
            var eye = EyePos(ep);
            var vd = EntityPos.GetViewVector(ep.Pos.Pitch, ep.Pos.Yaw);
            var ship = ShipLookedAt(ep, range, out _) ?? NearestShip(ep.Pos.XYZ);
            if (ship?.Def == null || ship.Def.BoardFromWater) return null;
            var aim = ship.WorldToLocal(eye.X + vd.X * wall, eye.Y + vd.Y * wall, eye.Z + vd.Z * wall);
            var me = ship.WorldToLocal(ep.Pos.X, ep.Pos.Y, ep.Pos.Z);
            var l = Boarding.OutsideClick(ship.Ladders, me, aim, true, out _);
            return l == null ? null : (ship, l);
        }

        static LadderDef NearestOutsideLadder(EntityShip ship, Vec3d pos, double reach)
        {
            var l0 = ship.WorldToLocal(pos.X, pos.Y, pos.Z);
            LadderDef best = null;
            double bd = reach * reach;
            foreach (var l in ship.Ladders)
            {
                if (!l.Outside) continue;
                double dx = l.Bottom[0] - l0.X, dz = l.Bottom[2] - l0.Z, dy = l.Bottom[1] - l0.Y;
                double d = dx * dx + dz * dz;
                if (d < bd && dy > -2.5 && dy < 2.0) { bd = d; best = l; }
            }
            return best;
        }

        /// <summary>
        /// Swimming (or in a boat) at the foot of a rope ladder and pushing toward the ship: climb on. c: the keys (a
        /// seat's while in one of our boats); skip: the boat you're in.
        /// </summary>
        void TryLadderBoard(EntityPlayer ep, EntityControls c, EntityShip skip)
        {
            if (capi.World.ElapsedMilliseconds < boardCooldownUntil || !ep.Alive) return;
            if (!c.Forward && !c.Jump) return;
            // ((25 October 2026, note 98: "cannot normally walk on ship as snap to ship is aggressive") in the water or in a boat
            // only: walking on a pier or a dock by the ladder's foot isn't climbing it (from there right-click it, or step aboard)
            if (skip == null && !ep.Swimming && !ep.FeetInLiquid) return;
            foreach (var ship in Ships)
            {
                if (ship == skip || ship.Def == null || ship.GoneDown || ship.Pos.SquareDistanceTo(ep.Pos.X, ep.Pos.Y, ep.Pos.Z) > 45 * 45) continue;
                var l = NearestOutsideLadder(ship, ep.Pos.XYZ, 1.4);
                if (l == null) continue;
                // facing the hull (toward the top of the ladder)
                var local = ship.WorldToLocal(ep.Pos.X, ep.Pos.Y, ep.Pos.Z);
                var look = ship.WorldDirToLocal(Math.Sin(ep.Pos.Yaw), 0, Math.Cos(ep.Pos.Yaw));
                double tx = l.Top[0] - local.X, tz = l.Top[2] - local.Z, tl = Math.Sqrt(tx * tx + tz * tz);
                if (tl > 1e-6 && (look.X * tx + look.Z * tz) / tl < 0.2) continue;
                BoardLadder(ep, ship, l);
                return;
            }
        }

        void BoardLadder(EntityPlayer ep, EntityShip ship, LadderDef l)
        {
            pendingLadder = l.Code;
            pendingLadderShip = ship.EntityId;
            pendingLadderUntil = capi.World.ElapsedMilliseconds + 3000;
            Board(ep, ship, l.Bottom[0], l.Bottom[1], l.Bottom[2]);
        }

        void LeaveShip(EntityPlayer ep, DeckSeat ds, bool keepShipSpeed = true)
        {
            var ship = ds.Ship;
            var m = ds.Mover;
            ship.LocalToWorld(m.X, m.Y, m.Z, out double x, out double y, out double z);
            var v = ship.LocalDirToWorld(m.VX, m.VY, m.VZ);
            if (keepShipSpeed)
            {
                v.X += Math.Sin(ship.Pos.Yaw) * ship.Speed;
                v.Z += Math.Cos(ship.Pos.Yaw) * ship.Speed;
            }

            ep.TryUnmount();
            ep.Pos.SetPos(x, y, z);
            ep.Pos.Motion.Set(v.X / 60, v.Y / 60, v.Z / 60);
            ep.PositionBeforeFalling.Set(x, y, z);
            boardCooldownUntil = capi.World.ElapsedMilliseconds + 1200;
            channel.SendPacket(new LeavePacket { ShipId = ship.EntityId, X = x, Y = y, Z = z });
        }

        /// <summary>
        /// A ship's hull is an entity, not blocks, so the game lets a swimmer through it at the waterline. Someone not
        /// aboard who has got into a ship's hull (below her main deck; a boat you climb into instead) is put back outside
        /// it, the shortest way out, and their way into it stopped.
        /// </summary>
        void KeepOutOfHulls(EntityPlayer ep)
        {
            foreach (var ship in Ships)
            {
                // (a ship gone down is open to the sea: divers go in to her hold)
                if (ship.Def?.DryMask == null || ship.Def.BoardFromWater || ship.GoneDown) continue;
                if (ship.Pos.SquareDistanceTo(ep.Pos.X, ep.Pos.Y, ep.Pos.Z) > 45 * 45) continue;
                var l = ship.WorldToLocal(ep.Pos.X, ep.Pos.Y, ep.Pos.Z);
                if (l.Y > ship.Def.DeckY || l.Y + DeckMover.Height < ship.Def.KeelY) continue;
                if (!InHull(ship, l.X, l.Y, l.Z)) continue;
                // the shortest way out: along the ship's width (either side) or its length (either end)
                double best = double.MaxValue, bx = 0, bz = 0;
                foreach (var (dx, dz) in new[] { (0.0, Math.Sign(l.Z) == 0 ? 1.0 : Math.Sign(l.Z)), (0.0, -(Math.Sign(l.Z) == 0 ? 1.0 : Math.Sign(l.Z))), (1.0, 0.0), (-1.0, 0.0) })
                {
                    for (double d = 0.1; d < Math.Min(best, 20); d += 0.1)
                    {
                        if (InHull(ship, l.X + dx * d, l.Y, l.Z + dz * d)) continue;
                        if (d < best) { best = d; bx = dx * (d + 0.05); bz = dz * (d + 0.05); }
                        break;
                    }
                }
                if (best == double.MaxValue) continue;
                var w = ship.LocalDirToWorld(bx, 0, bz);
                ep.Pos.X += w.X;
                ep.Pos.Z += w.Z;
                // no way on into the hull
                double len = Math.Sqrt(w.X * w.X + w.Z * w.Z);
                if (len > 1e-9)
                {
                    double nx = w.X / len, nz = w.Z / len, into = ep.Pos.Motion.X * nx + ep.Pos.Motion.Z * nz;
                    if (into < 0) { ep.Pos.Motion.X -= into * nx; ep.Pos.Motion.Z -= into * nz; }
                }
            }
        }

        long rowBoardAt, leftBoatId;

        /// <summary>
        /// Stepping, jumping or swimming into a rowing boat seats you in her (vanilla's boats have nothing to stand on:
        /// you'd fall through her). Not again straight after getting out of her: first get clear of her.
        /// </summary>
        void TryRowBoatBoard(EntityPlayer ep)
        {
            if (!ep.Alive || capi.World.ElapsedMilliseconds < rowBoardAt || ep.Controls.Sneak) return;
            foreach (var boat in RowBoats)
            {
                if (!boat.Alive || boat.Pos.Dimension != ep.Pos.Dimension || boat.Pos.SquareDistanceTo(ep.Pos.X, ep.Pos.Y, ep.Pos.Z) > 10 * 10) continue;
                bool inside = boat.Holds(ep.Pos.X, ep.Pos.Y, ep.Pos.Z, ep.Swimming ? 0.6 : 0.0);
                if (boat.EntityId == leftBoatId)
                {
                    if (!boat.Holds(ep.Pos.X, ep.Pos.Y, ep.Pos.Z, 1.2)) leftBoatId = 0;
                    continue;
                }
                if (!inside || boat.FreeSeat() == null) continue;
                channel.SendPacket(new FixturePacket { ShipId = boat.EntityId, Fixture = FixturePacket.BoardBoat });
                rowBoardAt = capi.World.ElapsedMilliseconds + 700;
                return;
            }
        }

        /// <summary>Just got out of a rowing boat: don't sit straight back down in her.</summary>
        void NoteLeftRowBoat(EntityPlayer ep)
        {
            if (lastMount is Vintagestory.GameContent.EntityBoatSeat bs && bs.Entity is EntityRowBoat rb && ep.MountedOn != lastMount)
            {
                leftBoatId = rb.EntityId;
                rowBoardAt = capi.World.ElapsedMilliseconds + 1500;
            }
            lastMount = ep.MountedOn;
        }

        IMountableSeat lastMount;

        // (review H1-22: made once, not each call each frame)
        static readonly double[] InHullHeights = { 0.1, 0.6, 1.1, 1.6 };
        const double InHullR = DeckMover.HalfWidth - 0.05;
        static readonly (double, double)[] InHullOffsets = { (0.0, 0.0), (InHullR, 0.0), (-InHullR, 0.0), (0.0, InHullR), (0.0, -InHullR) };

        /// <summary>A player's body (feet at y, ship-local) in the hull: a few points of it inside her planking.</summary>
        static bool InHull(EntityShip ship, double x, double y, double z)
        {
            foreach (double h in InHullHeights)
                foreach (var (ox, oz) in InHullOffsets)
                    if (ship.HullAt(x + ox, y + h, z + oz) && y + h < ship.Def.DeckY) return true;
            return false;
        }

        void TryAutoBoard(EntityPlayer ep)
        {
            if (capi.World.ElapsedMilliseconds < boardCooldownUntil || !ep.Alive || ep.Swimming) return;
            foreach (var ship in Ships)
            {
                if (ship.Def == null || ship.GoneDown || ship.Pos.SquareDistanceTo(ep.Pos.X, ep.Pos.Y, ep.Pos.Z) > 45 * 45) continue;
                var g = ship.Grid;
                var l = ship.WorldToLocal(ep.Pos.X, ep.Pos.Y, ep.Pos.Z);
                if (l.X < g.OX || l.X > g.MaxX || l.Z < g.OZ || l.Z > g.MaxZ || l.Y < g.OY || l.Y > g.MaxY + 1) continue;

                bool standing = DeckMover.HasFloor(g, l.X, l.Y, l.Z) && ep.Pos.Motion.Y <= 0.001;
                bool inside = g.Collides(l.X - DeckMover.HalfWidth, l.Y, l.Z - DeckMover.HalfWidth, l.X + DeckMover.HalfWidth, l.Y + DeckMover.Height, l.Z + DeckMover.HalfWidth);
                if (!standing && !inside) continue;

                var m = new DeckMover(l.X, l.Y, l.Z);
                m.Unstick(g);
                if (!DeckMover.HasFloor(g, m.X, m.Y, m.Z) || m.Y - l.Y > 1.3) continue;
                Board(ep, ship, m.X, m.Y, m.Z);
                return;
            }
        }

        /// <summary>
        /// Into a boat (low enough to climb into from the water: Def.BoardFromWater): swimming into her, or pushing toward
        /// her right beside her, or landing in her from above, puts you on her floor at the nearest place. force: coming
        /// down a ship's rope ladder, get into a boat at its foot.
        /// </summary>
        bool TryBoatBoard(EntityPlayer ep, EntityControls c, bool force)
        {
            if (!ep.Alive || !force && capi.World.ElapsedMilliseconds < boardCooldownUntil) return false;
            foreach (var ship in Ships)
            {
                if (ship.Def == null || !ship.Def.BoardFromWater || ship.Def.DryMask == null || ship.GoneDown) continue;
                if (ship.Pos.SquareDistanceTo(ep.Pos.X, ep.Pos.Y, ep.Pos.Z) > 12 * 12) continue;
                var l = ship.WorldToLocal(ep.Pos.X, ep.Pos.Y, ep.Pos.Z);
                if (l.Y < ship.Def.KeelY - 2.0 || l.Y > ship.Def.DeckY + 2.5) continue;
                bool inHer = NearHull(ship, l.X, l.Z, 0);
                bool near = !inHer && NearHull(ship, l.X, l.Z, force ? 1.5 : 0.8);
                if (!inHer && !(near && (force || c.Forward || c.Jump))) continue;
                if (!ship.HasRoomFor(ep))
                {
                    capi.TriggerIngameError(this, "hansa-full", Lang.Get("hansa:boat-full"));
                    boardCooldownUntil = capi.World.ElapsedMilliseconds + 2000;
                    continue;
                }
                var b = NearestBoardPoint(ship, l);
                Board(ep, ship, b[0], b[1], b[2]);
                return true;
            }
            return false;
        }

        /// <summary>Is ship-local (x, z) within reach of a hull's inside (its dry mask, just under the gunwale)?</summary>
        static bool NearHull(EntityShip ship, double x, double z, double reach)
        {
            var g = ship.Def.DryMask;
            double y = Math.Min(ship.Def.DeckY - 0.4, ship.Def.Waterline + 0.1);
            if (g.SolidAt(x, y, z)) return true;
            for (int k = 0; k < 16 && reach > 0; k++)
            {
                double a = k * Math.PI / 8;
                for (double r = 0.25; r <= reach + 1e-9; r += 0.25)
                    if (g.SolidAt(x + Math.Cos(a) * r, y, z + Math.Sin(a) * r)) return true;
            }
            return false;
        }

        static double[] NearestBoardPoint(EntityShip ship, Vec3d l)
        {
            double[] best = new double[] { 0, ship.Def.DeckY, 0 };
            double bd = double.MaxValue;
            foreach (var b in ship.Def.BoardPoints)
            {
                double d = (b[0] - l.X) * (b[0] - l.X) + (b[2] - l.Z) * (b[2] - l.Z);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        /// <summary>The ship whose hull the view ray meets first, and how far along it; null if none in reach.</summary>
        EntityShip ShipLookedAt(EntityPlayer ep, double range, out double dist)
        {
            var eye = EyePos(ep);
            var dir = EntityPos.GetViewVector(ep.Pos.Pitch, ep.Pos.Yaw);
            EntityShip best = null;
            dist = double.MaxValue;
            foreach (var ship in Ships)
            {
                if (ship.Def == null || ship.Pos.SquareDistanceTo(eye.X, eye.Y, eye.Z) > 50 * 50) continue;
                var lo = ship.WorldToLocal(eye.X, eye.Y, eye.Z);
                var ld = ship.WorldDirToLocal(dir.X, dir.Y, dir.Z);
                double t = ship.Grid.Raycast(lo, ld, range);
                if (t < range && t < dist) { dist = t; best = ship; }
            }
            return best;
        }

        public void Board(EntityPlayer ep, EntityShip ship, double x, double y, double z)
        {
            // (not a ship that has gone down: the server refuses it)
            if (ship.GoneDown) return;
            if (!ship.HasRoomFor(ep))
            {
                capi.TriggerIngameError(this, "hansa-full", Lang.Get("hansa:boat-full"));
                return;
            }
            var seat = ship.GetOrCreateDeckSeat(ep.PlayerUID, x, y, z);
            // (review H3-3: where they were, should the server refuse it)
            boardedFrom = ep.Pos.XYZ;
            boardedShip = ship.EntityId;
            // (review H4-4: which try a refusal answers)
            boardAttempt++;
            ep.TryMount(seat);   // predicted; the server confirms through the "mountedOn" attribute, or refuses (OnRemoteDeckState)
            channel.SendPacket(new BoardPacket { ShipId = ship.EntityId, X = (float)x, Y = (float)y, Z = (float)z, Attempt = boardAttempt });
            boardCooldownUntil = capi.World.ElapsedMilliseconds + 500;
        }

        // ------------------------------------------------------------------ looking and clicking

        /// <summary>What the crosshair is on (an entity), for the look-at box patch.</summary>
        public EntitySelection LookSelection => capi?.World?.Player?.CurrentEntitySelection;

        /// <summary>The fixture in the crosshair this frame (what a right-click would use), or none.</summary>
        public FixtureHit LookedAt => lookedAt;

        AABBIntersectionTest shipTester;

        /// <summary>
        /// The crosshair on ships, right after the game's own selection ray (Patches.RayTracePostfix, which left the
        /// ships out of it): the ship whose hull, deck or fixture the ray meets first (EntityShip.IntersectsRay: where,
        /// the face, the part of her) becomes the selection if it's nearer than the block or the other entity the game
        /// found. filter: the game's entity filter.
        /// </summary>
        public void PickShips(Ray ray, ref BlockSelection blockSel, ref EntitySelection entitySel, EntityFilter filter)
        {
            double range = ray.Length;
            if (range <= 0 || Ships.Count == 0) return;
            shipTester ??= new AABBIntersectionTest(capi.World as IWorldIntersectionSupplier);
            EntityShip best = null;
            double bestDist = double.MaxValue;
            int bestIndex = 0;
            var bestHit = new Vec3d();
            BlockFacing bestFace = BlockFacing.UP;
            foreach (var ship in Ships)
            {
                if (ship.Def == null || !ship.Alive || !filter(ship)) continue;
                double reach = range + ship.Def.Length;
                if (ship.Pos.SquareDistanceTo(ray.origin.X, ray.origin.Y, ray.origin.Z) > reach * reach) continue;
                int index = 0;
                if (!ship.IntersectsRay(ray, shipTester, out double d, ref index) || d >= bestDist) continue;
                best = ship;
                bestDist = d;
                bestIndex = index;
                bestHit.Set(shipTester.hitPosition);
                bestFace = shipTester.hitOnBlockFace;
            }
            if (best == null) return;
            // the nearest hit point wins, as the game compares a block with an entity
            double shipSq = bestDist * bestDist;
            if (blockSel?.Position != null &&
                ray.origin.SquareDistanceTo(new Vec3d(blockSel.Position.X, blockSel.Position.InternalY, blockSel.Position.Z).Add(blockSel.HitPosition)) < shipSq) return;
            if (entitySel?.Entity is Entity other &&
                ray.origin.SquareDistanceTo(new Vec3d(other.Pos.X, other.Pos.Y, other.Pos.Z).Add(entitySel.HitPosition)) < shipSq) return;
            blockSel = null;
            entitySel = new EntitySelection
            {
                Entity = best,
                Position = best.Pos.XYZ,
                Face = bestFace,
                HitPosition = bestHit.SubCopy(best.Pos.X, best.Pos.Y, best.Pos.Z),
                SelectionBoxIndex = bestIndex
            };
        }

        Vec3d EyePos(EntityPlayer ep) => ep.CameraPos.AddCopy(ep.LocalEyePos);

        FixtureHit FindLookedAtFixture(EntityPlayer ep)
        {
            var best = new FixtureHit { Dist = double.MaxValue };
            double range = capi.World.Player.WorldData.PickingRange;
            var eye = EyePos(ep);
            var dir = EntityPos.GetViewVector(ep.Pos.Pitch, ep.Pos.Yaw);
            var held = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            foreach (var ship in Ships)
            {
                if (ship.Def == null || ship.Pos.SquareDistanceTo(eye.X, eye.Y, eye.Z) > 50 * 50) continue;
                var lo = ship.WorldToLocal(eye.X, eye.Y, eye.Z);
                var ld = ship.WorldDirToLocal(dir.X, dir.Y, dir.Z);
                // an empty spot only shows up when you hold something that goes there (a spot a trunk lies across is taken)
                var hit = ShipFittings.Pick(ship.Def, ship.Grid, lo, ld, range,
                    // (an older design's lantern places are hooks now: DecoClient)
                    f => !ShipMounts.IsLegacyHook(f) && (f.Type != "attach" || f.Shown || ship.Attached.ContainsKey(f.Code) || ship.CoveredBy(f) != null || ShipFittings.Accepts(f, held)),
                    f => ship.OpenFittings.Contains(f.Code), f => ship.Attached.TryGetValue(f.Code, out var st) ? st : null, out double t, ship.PlacedFixtures, ship.GunAbsent, ship.GunPick);
                if (hit != null && t < best.Dist) best = new FixtureHit { Ship = ship, Fixture = hit, Dist = t };
            }
            // ((25 October 2026, notes 97, 98) not one behind a block of the world in front of her)
            if (best.Ship != null && best.Dist > WorldBlockDistance(ep)) return default;
            return best.Ship == null ? default : best;
        }

        /// <summary>
        /// (25 October 2026, notes 97, 98: building a pier beside her, "snaps aggressively to ship rope ladder") How far the
        /// block of the world the game's crosshair is on is (in the player's own dimension; MaxValue: none). The game picks
        /// her (an entity) or a block, the nearer: a block picked is in front of her, and her own aim yields to it.
        /// </summary>
        double WorldBlockDistance(EntityPlayer ep)
        {
            var bs = capi.World.Player?.CurrentBlockSelection;
            if (bs?.Position == null || bs.Position.dimension != ep.Pos.Dimension) return double.MaxValue;
            return EyePos(ep).DistanceTo(bs.Position.ToVec3d().Add(bs.HitPosition));
        }

        /// <summary>Distance to the closest solid ship voxel along the view ray, or MaxValue.</summary>
        double ShipWallDistance(EntityPlayer ep, double range)
        {
            var eye = EyePos(ep);
            var dir = EntityPos.GetViewVector(ep.Pos.Pitch, ep.Pos.Yaw);
            double best = double.MaxValue;
            foreach (var ship in Ships)
            {
                if (ship.Def == null || ship.Pos.SquareDistanceTo(eye.X, eye.Y, eye.Z) > 50 * 50) continue;
                var lo = ship.WorldToLocal(eye.X, eye.Y, eye.Z);
                var ld = ship.WorldDirToLocal(dir.X, dir.Y, dir.Z);
                best = Math.Min(best, ship.Grid.Raycast(lo, ld, range));
            }
            return best;
        }

        void OnInWorldAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (!on || (action != EnumEntityAction.InWorldRightMouseDown && action != EnumEntityAction.InWorldLeftMouseDown)) return;
            var ep = capi.World.Player?.Entity;
            if (ep == null) return;
            double range = capi.World.Player.WorldData.PickingRange;

            // the surveyor's chalk in hand: every click is its (a note, a mark read or rubbed out), whatever is in the crosshair
            if (Chalk != null && Chalk.OnClick(ep, action))
            {
                handled = EnumHandling.PreventDefault;
                return;
            }

            // (0.3.14) one of her working blocks in the crosshair (a forge, an anvil): its clicks are vanilla's (ShipBlocks)
            // (but Shift + right-click with a block on one with a free place on top puts that there: DecoClient)
            if (ShipBlocksSystem.ClientOwnsClick(capi) && Deco?.Look.Kind != DecoClient.TargetKind.Mount) return;

            // decorating: a piece in the crosshair (right-click uses it, held left takes it up), a free mount point with a
            // block in hand (right-click puts it there), a paint zone with dye, gold plates or a knife (DecoClient)
            if (Deco != null && Deco.OnClick(ep, action))
            {
                handled = EnumHandling.PreventDefault;
                return;
            }

            // the guns: hold right mouse on one (in reach, or at its gun captain's place) to lay it where you look;
            // left-click fires the one you're laying (or laid a moment ago and are still by), or the one in the crosshair:
            // the click is the gun's, it doesn't swing at her or break anything
            if (GunClick(ep, action))
            {
                handled = EnumHandling.PreventDefault;
                return;
            }

            // Left-clicking a ship: vanilla would swing at her like at a creature and send the server an attack it drops.
            // Nothing of hers takes a left-click yet.
            if (action == EnumEntityAction.InWorldLeftMouseDown && capi.World.Player.CurrentEntitySelection?.Entity is EntityShip)
            {
                handled = EnumHandling.PreventDefault;
                return;
            }

            if (action == EnumEntityAction.InWorldRightMouseDown)
            {
                // mending or pumping already (the game repeats the click while it's held)
                if (work != null)
                {
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                if (rightSwallowed)
                {
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                var hit = FindLookedAtFixture(ep);
                if(hit.Ship!=null && hit.Fixture.Type=="hitch" && capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is Vintagestory.GameContent.ItemRope) return;
                // a pump: its brake is worked while right-click is held, a stroke a second
                if (hit.Ship != null && hit.Fixture.Type == "pump" && !hit.Ship.GoneDown)
                {
                    work = new Work { Ship = hit.Ship, Pump = hit.Fixture, Started = capi.World.ElapsedMilliseconds, LastStroke = capi.World.ElapsedMilliseconds };
                    channel.SendPacket(new FixturePacket { ShipId = hit.Ship.EntityId, Fixture = hit.Fixture.Index });
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                // the firehearth with a firestarter or a lit torch in hand, fuel in it and not burning: held to it to light it
                if (hit.Ship != null && hit.Fixture == hit.Ship.HearthFixture && hit.Ship.HearthIgnitable && TryStartLighting(ep, hit.Ship, hit.Fixture))
                {
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                // from the water or a dock, aiming at a rope ladder itself: climb it, whatever's in hand (the user, 0.3.10);
                // mending wins only on her plain side
                if (ep.MountedOn == null && hit.Ship == null && LadderInSight(ep, range) is (EntityShip lship, LadderDef lad))
                {
                    BoardLadder(ep, lship, lad);
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                // mending: the crosshair on a part of her that what's in hand mends (held right-click for a few seconds)
                if (TryStartRepair())
                {
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                bool sneak = ep.Controls.Sneak || ep.Controls.ShiftKey || (ep.MountedOn is ShipSeat seat && seat.Controls.Sneak);
                // Shift (or Ctrl: on a rope ladder Shift climbs down) + right-click a boat to pick it up (nobody and
                // nothing in her), from the water, a dock, or a ship alongside; but Shift + right-click a chest or vessel
                // in her takes that back
                bool ctrl = ep.Controls.CtrlKey || (ep.MountedOn is ShipSeat cseat && cseat.Controls.CtrlKey);
                if ((sneak || ctrl) && ShipLookedAt(ep, range, out double tb) is EntityShip boat && boat.Def.PickUpItem != null &&
                    (hit.Ship == null || hit.Dist > tb + 0.3 || hit.Ship == boat && !(hit.Fixture.Type == "attach" && boat.Attached.ContainsKey(hit.Fixture.Code))))
                {
                    channel.SendPacket(new FixturePacket { ShipId = boat.EntityId, Fixture = FixturePacket.PickUp });
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                // the rowing boat (vanilla's boat): from a ship's deck or rope ladder the keys are the ship's, so vanilla's
                // Shift + right-click pickup never fires there; Ctrl + right-click works anywhere
                // (only on a ship's deck or ladder and with an empty hand: in the water or on a dock vanilla picks her up
                // with Shift, and Ctrl + right-click with a chest or vessel in hand puts it in her)
                bool emptyHand = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Empty != false;
                if (ep.MountedOn is ShipSeat && (sneak || ctrl) && emptyHand &&
                    capi.World.Player.CurrentEntitySelection?.Entity is EntityRowBoat rb && (hit.Ship == null || hit.Dist > ep.Pos.DistanceTo(rb.Pos.XYZ) + 0.3))
                {
                    if (rb.IsEmptyForPickup()) channel.SendPacket(new FixturePacket { ShipId = rb.EntityId, Fixture = FixturePacket.PickUp });
                    else capi.TriggerIngameError(this, "hansa-pickup", Lang.Get("hansa:pickup-cargo"));
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                if (hit.Ship != null && hit.Fixture.Type == "plaque")
                {
                    // her crew list (ShipCrew): read by anyone, changed by her captains; the server decides
                    OpenCrewDialog(hit.Ship);
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                if (hit.Ship != null && hit.Fixture.Type == "nameboard")
                {
                    // naming her: the dialog is ours, the name goes to the server
                    OpenNameDialog(hit.Ship);
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                if (hit.Ship != null)
                {
                    channel.SendPacket(new FixturePacket { ShipId = hit.Ship.EntityId, Fixture = hit.Fixture.Index, Sneak = sneak });
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                // off the ship (swimming, on a dock): right-click the hull to sheathe it in copper, or to get onto a rope
                // ladder close by. From the water the rope ladders are the only way up; from a dock you jump aboard.
                if (ep.MountedOn == null)
                {
                    double wall = ShipWallDistance(ep, range);
                    // ((25 October 2026, note 97) not behind a block of the world in front of her)
                    if (wall < range && wall < WorldBlockDistance(ep))
                    {
                        var ship = ShipLookedAt(ep, range, out _) ?? NearestShip(ep.Pos.XYZ);
                        if (ship?.Def != null && ship.Def.BoardFromWater)
                        {
                            // climb into a boat: right-click her from the water or a dock beside her
                            var l = ship.WorldToLocal(ep.Pos.X, ep.Pos.Y, ep.Pos.Z);
                            if (NearHull(ship, l.X, l.Z, 2.0))
                            {
                                var b = NearestBoardPoint(ship, l);
                                Board(ep, ship, b[0], b[1], b[2]);
                            }
                            handled = EnumHandling.PreventDefault;
                            return;
                        }
                        var held = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
                        if (ship?.Def != null && ShipFittings.CladdingItemsPerPlate(held) > 0)
                        {
                            channel.SendPacket(new FixturePacket { ShipId = ship.EntityId, Fixture = FixturePacket.Cladding });
                            handled = EnumHandling.PreventDefault;
                            return;
                        }
                        // (0.3.10, the user: right-clicking her side from the water always boarded or said "board by
                        // ladder": mending her from the water never started.) Anything in hand: the click is the item's
                        // (mending her: TryStartRepair above, held right-click), never boarding. Empty-handed: onto a rope
                        // ladder only aiming at or near it; else a hint, at most every 20 s
                        if (ship?.Def != null)
                        {
                            var eye = EyePos(ep);
                            var vd = EntityPos.GetViewVector(ep.Pos.Pitch, ep.Pos.Yaw);
                            var aim = ship.WorldToLocal(eye.X + vd.X * wall, eye.Y + vd.Y * wall, eye.Z + vd.Z * wall);
                            var me = ship.WorldToLocal(ep.Pos.X, ep.Pos.Y, ep.Pos.Z);
                            var ladder = Boarding.OutsideClick(ship.Ladders, me, aim, held != null, out bool hint);
                            if (ladder != null)
                            {
                                BoardLadder(ep, ship, ladder);
                                handled = EnumHandling.PreventDefault;
                                return;
                            }
                            if (hint && capi.World.ElapsedMilliseconds - ladderHintAt > 20000)
                            {
                                ladderHintAt = capi.World.ElapsedMilliseconds;
                                capi.ShowChatMessage(Lang.Get("hansa:board-by-ladder"));
                            }
                            if (held == null) handled = EnumHandling.PreventDefault;
                            return;
                        }
                    }
                }
            }

            // don't let clicks reach blocks behind the hull (e.g. the seabed through the deck)
            var bs = capi.World.Player.CurrentBlockSelection;
            if (bs != null)
            {
                double wall = ShipWallDistance(ep, range);
                if (wall < range && wall < EyePos(ep).DistanceTo(bs.Position.ToVec3d().Add(bs.HitPosition)) - 0.05)
                {
                    handled = EnumHandling.PreventDefault;
                }
            }
        }

        // ------------------------------------------------------------------ the guns: laying and firing them

        /// <summary>The gun this player is laying (holding right mouse by it), and when the server was last told.</summary>
        (EntityShip Ship, FixtureDef Gun) laying;
        long layingSent;
        /// <summary>(0.3.27) Left mouse was down the last frame while laying (a press fires it).</summary>
        bool layLeftWas;
        /// <summary>The gun this player laid last, and till when a left-click fires it without it in the crosshair.</summary>
        (EntityShip Ship, FixtureDef Gun) lastLaid;
        long lastLaidUntil;
        /// <summary>How near a gun's breech this player lays it from (blocks; the server allows a little more).</summary>
        const double LayReach = 2.5;

        /// <summary>
        /// A click by the guns (<see cref="GunButtons"/>): right mouse down on one (it in the crosshair within reach of its
        /// breech, or at its gun captain's place with nothing in hand that has a right-click of its own) starts laying it,
        /// Ctrl + right-click on one stands to it; a left-click fires the one being laid (or laid a moment ago, still by it),
        /// the one in the crosshair within reach, or the one whose gun captain this player is. True: the click was the guns'
        /// (nothing else gets it: no swing at her, no block broken, no item used).
        /// </summary>
        bool GunClick(EntityPlayer ep, EnumEntityAction action)
        {
            var seat = ep.MountedOn as GunSeat;
            var look = FindLookedAtFixture(ep);
            (EntityShip Ship, FixtureDef Gun) inSight = look.Ship != null && look.Fixture.Type == "gun" ? (look.Ship, look.Fixture) : default;
            if (GunButtons.Lays(action))
            {
                if (laying.Ship != null) return true;             // (the game repeats it while it's held)
                if (work != null || rightSwallowed) return false; // pumping, mending, lighting the hearth: theirs
                bool ctrl = ep.Controls.CtrlKey || ep.MountedOn is ShipSeat ss && ss.Controls.CtrlKey;
                if (ctrl && seat == null && inSight.Ship != null)
                {
                    // Ctrl + right-click: stand to it (the gun captain's place)
                    channel.SendPacket(new FixturePacket { ShipId = inSight.Ship.EntityId, Fixture = inSight.Gun.Index, Ctrl = true });
                    return true;
                }
                bool sneak = ep.Controls.Sneak || ep.Controls.ShiftKey || ep.MountedOn is ShipSeat sk && sk.Controls.Sneak;
                var loadTarget=inSight.Ship!=null?inSight:seat!=null?(seat.Ship,seat.Fixture):default;
                if(loadTarget.Item1!=null && GunLoading.IsLoad(capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString()))
                {
                    if(capi.World.ElapsedMilliseconds-layingSent>400) {
                        layingSent=capi.World.ElapsedMilliseconds;
                        channel.SendPacket(new GunLayPacket { ShipId=loadTarget.Item1.EntityId,Gun=loadTarget.Item2.Index,Load=true });
                    }
                    return true;
                }
                // (0.3.21, step C) building mode, empty hand, Shift + right-click a gun place's gun: take it up
                // (run out: the first such click runs it in and shuts its port, as out of building mode)
                if (sneak && seat == null && inSight.Ship != null && VanillaPieceBlock.BuildingMode(ep) && capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack == null &&
                    inSight.Ship.GunPlaceOf(inSight.Gun) is GunPlaceDef gp && !(inSight.Gun.Lid != null && inSight.Ship.GunOpen(inSight.Gun)))
                {
                    string why = inSight.Ship.TakeGunProblem(gp);
                    if (why != null) capi.TriggerIngameError(this, "hansa-gun", Lang.Get(why));
                    else channel.SendPacket(new DecorPacket { ShipId = inSight.Ship.EntityId, Action = DecorPacket.GunTake, Mount = gp.Id });
                    return true;
                }
                if (sneak && seat == null && inSight.Ship != null && inSight.Gun.Lid != null)
                {
                    // Shift + right-click: run it in and shut its port
                    if (inSight.Ship.GunOpen(inSight.Gun)) channel.SendPacket(new FixturePacket { ShipId = inSight.Ship.EntityId, Fixture = inSight.Gun.Index, Sneak = true });
                    return true;
                }
                // (0.1.0-dev.7, the user: "Trying to use telescope from Astra on deck and almost everywhere it tries to use
                // cannon instead") the one stood to (unless what's in hand has a right-click of its own), else the one in the
                // crosshair within reach; never the nearest by its breech with the crosshair elsewhere (GunButtons.LaysGun)
                bool ownUse = HeldUse.OwnRightClick(capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible);
                bool inReach = inSight.Ship != null && inSight.Ship.ByGun(inSight.Gun, ep, LayReach);
                var lays = GunButtons.LaysGun(seat?.Fixture, inSight.Gun, inReach, ownUse);
                if (lays == null)
                {
                    // aimed at one from further off: the click is still that gun's (passed on, the fixture's click fires it)
                    if (inSight.Ship == null) return false;
                    capi.TriggerIngameError(this, "hansa-gun", Lang.Get("hansa:gun-far"));
                    return true;
                }
                (EntityShip Ship, FixtureDef Gun) gun = lays == seat?.Fixture ? (seat.Ship, seat.Fixture) : inSight;
                if (gun.Ship == null || gun.Ship.GoneDown) return false;
                if (!gun.Ship.CannonsOn)
                {
                    capi.TriggerIngameError(this, "hansa-gun", Lang.Get("hansa:gun-off"));
                    return true;
                }
                laying = gun;
                gun.Ship.LookAlongGun(gun.Gun);
                gun.Ship.BeginLayingHere(gun.Gun);
                gun.Ship.LayingHere = gun.Gun;
                layingSent = capi.World.ElapsedMilliseconds;
                channel.SendPacket(new GunLayPacket { ShipId = gun.Ship.EntityId, Gun = gun.Gun.Index, On = true, Begin = true });
                return true;
            }
            if (!GunButtons.Fires(action)) return false;
            // (the one laid a moment ago only with nothing else of a ship's in the crosshair: a door by it still opens)
            // ((0.1.0-dev.7) the one in the crosshair only within reach of its breech, as laying it)
            bool fireReach = inSight.Ship != null && inSight.Ship.ByGun(inSight.Gun, ep, LayReach);
            (EntityShip Ship, FixtureDef Gun) fire = laying.Ship != null ? laying : seat != null ? (seat.Ship, seat.Fixture) : fireReach ? inSight :
                look.Ship == null && lastLaid.Ship != null && capi.World.ElapsedMilliseconds < lastLaidUntil && lastLaid.Ship.ByGun(lastLaid.Gun, ep, LayReach + 0.5)
                    ? lastLaid : default;
            if (fire.Ship == null && inSight.Ship != null)
            {
                capi.TriggerIngameError(this, "hansa-gun", Lang.Get("hansa:gun-far"));
                return true;
            }
            if (fire.Ship == null) return false;
            FireGun(fire.Ship, fire.Gun);
            return true;
        }

        /// <summary>
        /// Every frame: while right mouse is held the gun goes on being laid (the server told again twice a second); let go,
        /// moved off or out of reach, it stays as it is (the server told), and a left-click still fires it for a few seconds.
        /// </summary>
        void UpdateLaying(EntityPlayer ep)
        {
            if (laying.Ship == null) return;
            long now = capi.World.ElapsedMilliseconds;
            bool held = GunButtons.LayHeld(capi.Input.InWorldMouseButton.Left, capi.Input.InWorldMouseButton.Right);
            bool by = laying.Ship.ByGun(laying.Gun, ep, LayReach + 0.5) && laying.Ship.CannonsOn && !laying.Ship.GoneDown && Ships.Contains(laying.Ship);
            if (held && by)
            {
                // ((0.3.27) the relative aim this client lays it to, ten times a second)
                if (now - layingSent >= 100)
                {
                    layingSent = now;
                    bool aimed = laying.Ship.LayingHere == laying.Gun && laying.Ship.GunOpen(laying.Gun) && laying.Ship.GunRunningOut(laying.Gun) <= 0;
                    channel.SendPacket(new GunLayPacket { ShipId = laying.Ship.EntityId, Gun = laying.Gun.Index, On = true, HasAim = aimed,
                        Train = laying.Ship.LayingAim.Train, Elevation = laying.Ship.LayingAim.Elevation });
                }
                // ((0.3.27, the user: "cannons should allow aiming and fire while aiming") left mouse pressed while laying fires
                // it (the game may not pass a left click on while the right button is held: watched here as well)
                bool left = capi.Input.InWorldMouseButton.Left;
                if (left && !layLeftWas) FireGun(laying.Ship, laying.Gun);
                layLeftWas = left;
                return;
            }
            layLeftWas = false;
            channel.SendPacket(new GunLayPacket { ShipId = laying.Ship.EntityId, Gun = laying.Gun.Index, On = false });
            laying.Ship.LayingHere = null;
            lastLaid = laying;
            lastLaidUntil = now + 4000;
            laying = default;
        }

        /// <summary>
        /// Fire a gun (the server decides). Once a click: not again while left-click is held (the game repeats it every
        /// 0.25 s); and no shot asked for while this client knows it's still reloading.
        /// </summary>
        void FireGun(EntityShip ship, FixtureDef gun)
        {
            long now = capi.World.ElapsedMilliseconds;
            bool repeat = lastGunClick.Ship == ship.EntityId && lastGunClick.Gun == gun.Index && now - lastGunClick.Ms < 600;
            lastGunClick = (ship.EntityId, gun.Index, now);
            if (repeat) return;
            // a housed gun: its port has to be opened (and it run out) first
            if (!ship.GunOpen(gun)) { capi.TriggerIngameError(this, "hansa-gun", Lang.Get("hansa:gun-housed")); return; }
            double outIn = ship.GunRunningOut(gun);
            if (outIn > 0) { capi.TriggerIngameError(this, "hansa-gun", Lang.Get("hansa:gun-running-out", Math.Ceiling(outIn))); return; }
            double left = ship.GunReloading(gun);
            if (left > 0) capi.TriggerIngameError(this, "hansa-gun", Lang.Get("hansa:gun-reloading", Math.Ceiling(left)));
            else channel.SendPacket(new FixturePacket { ShipId = ship.EntityId, Fixture = gun.Index });
        }

        /// <summary>The gun this player is laying now (HUD), or none.</summary>
        public (EntityShip Ship, FixtureDef Gun) Laying => laying;

        // ------------------------------------------------------------------ held work: mending a part, working a pump

        class Work
        {
            public EntityShip Ship;
            /// <summary>Mending: the part the crosshair is on (its selection box index). Pumping: the pump.</summary>
            public int Part;
            public FixtureDef Pump;
            /// <summary>Lighting the hearth: it, what's held to it (<see cref="EntityShip.Igniter"/>), the firestarter's animation, its sound played.</summary>
            public FixtureDef Hearth;
            public int Igniter;
            public string Animation;
            public bool Sounded;
            public long Started, LastStroke;
            public float Seconds;
            public string What;
        }

        Work work;
        /// <summary>Right-click still held after lighting the hearth: its repeats are dropped until it's let go.</summary>
        bool rightSwallowed;

        /// <summary>
        /// A firestarter or a lit torch held to the hearth, as vanilla holds one to a firepit: the seconds counted here (the
        /// torch's sound at once, the hand drill's after half a second, the firestarter's animation), and after 3 s the
        /// server is asked to light it; a firestarter that didn't catch tries again while it's held.
        /// </summary>
        bool TryStartLighting(EntityPlayer ep, EntityShip ship, FixtureDef hearth)
        {
            var held = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            int kind = EntityShip.Igniter(held);
            if (kind == 0) return false;
            long now = capi.World.ElapsedMilliseconds;
            work = new Work
            {
                Ship = ship, Hearth = hearth, Igniter = kind, Started = now, LastStroke = now, Seconds = HearthFire.IgniteSeconds + 0.2f,
                What = ShipHud.FixtureName(ship, hearth)
            };
            channel.SendPacket(new HearthPacket { ShipId = ship.EntityId, Fixture = hearth.Index, Start = true });
            if (kind == 2) capi.World.PlaySoundAt(new AssetLocation("sounds/torch-ignite"), ep, null, false, 16);
            else
            {
                work.Animation = held.Collectible.Attributes?["igniteAnimation"].AsString(null);
                if (work.Animation != null) ep.AnimManager?.StartAnimation(work.Animation);
            }
            return true;
        }

        /// <summary>Every frame while lighting the hearth: on while it's held to it and could still be lit. Returns what the HUD says, or null.</summary>
        string UpdateLighting(long now, bool held)
        {
            var ep = capi.World.Player.Entity;
            var stack = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (!held || lookedAt.Ship != work.Ship || lookedAt.Fixture != work.Hearth || !work.Ship.HearthIgnitable || EntityShip.Igniter(stack) != work.Igniter)
            {
                if (work.Animation != null) ep?.AnimManager?.StopAnimation(work.Animation);
                work = null;
                // lit (or given up) with the button still held: the game's repeated clicks don't open it till it's let go
                rightSwallowed = held;
                return null;
            }
            if (work.Igniter == 1 && !work.Sounded && now - work.Started > 500)
            {
                work.Sounded = true;
                capi.World.PlaySoundAt(new AssetLocation("sounds/player/handdrill"), ep, null, false, 16);
            }
            double f = (now - work.Started) / 1000.0 / work.Seconds;
            if (f >= 1)
            {
                channel.SendPacket(new HearthPacket { ShipId = work.Ship.EntityId, Fixture = work.Hearth.Index, Seconds = (float)((now - work.Started) / 1000.0) });
                work.Started = now;
                work.Sounded = false;
                f = 0;
            }
            return Lang.Get("hansa:hud-lighting", work.What, (int)(f * 100));
        }

        /// <summary>Right-click on a part of a ship with what mends it in hand: start mending it (the server checks and takes the items at the end).</summary>
        bool TryStartRepair()
        {
            var sel = capi.World.Player.CurrentEntitySelection;
            if (sel?.Entity is not EntityShip ship || ship.PartDamage == null || ship.GoneDown || !ship.DamageOn) return false;
            string held = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString();
            if (held == null) return false;
            // ((0.3.28, step D2) a cracked partition, door, window or railing: mended as the planking is, held right-click)
            var sp = ShipPart.Decode(sel.SelectionBoxIndex);
            if (sp.Kind == ShipPartKind.Piece)
            {
                if (ship.PieceRepairFor(sp.Index, held) == null) return false;
                work = new Work
                {
                    Ship = ship, Part = sel.SelectionBoxIndex, Started = capi.World.ElapsedMilliseconds, Seconds = Math.Max(0.5f, ship.DamageParts.D.RepairSeconds),
                    What = ship.PieceName(ship.Pieces.Get(sp.Index))
                };
                capi.World.PlaySoundAt(new AssetLocation("sounds/block/planks"), capi.World.Player.Entity, null, true, 8);
                return true;
            }
            int part = ship.DamagePartFor(sp);
            var job = part >= 0 ? ShipRepairs.For(ship.DamageParts, ship.PartDamage, part, held) : null;
            if (job == null) return false;
            if (!job.AtSea)
            {
                capi.TriggerIngameError(this, "hansa-repair", Lang.Get("hansa:repair-port", ship.PartName(job.Part)));
                return true;
            }
            work = new Work
            {
                Ship = ship, Part = sel.SelectionBoxIndex, Started = capi.World.ElapsedMilliseconds, Seconds = Math.Max(0.5f, ship.DamageParts.D.RepairSeconds),
                What = ship.PartName(job.Part)
            };
            capi.World.PlaySoundAt(new AssetLocation("sounds/block/planks"), capi.World.Player.Entity, null, true, 8);
            return true;
        }

        /// <summary>
        /// Every frame: the work in hand goes on while right-click is held on what it's for (a pump: a stroke a second to
        /// the server; mending: done after its seconds, then the server mends it). Returns what the HUD says, or null.
        /// </summary>
        string UpdateWork()
        {
            if (work == null) return null;
            long now = capi.World.ElapsedMilliseconds;
            bool held = capi.Input.InWorldMouseButton.Right;
            if (work.Hearth != null) return UpdateLighting(now, held);
            if (work.Pump != null)
            {
                if (!held || lookedAt.Ship != work.Ship || lookedAt.Fixture != work.Pump || work.Ship.GoneDown) { work = null; return null; }
                float stroke = 1000 * Math.Max(0.2f, work.Ship.DamageParts?.D.PumpStroke ?? 1);
                if (now - work.LastStroke >= stroke)
                {
                    work.LastStroke = now;
                    channel.SendPacket(new FixturePacket { ShipId = work.Ship.EntityId, Fixture = work.Pump.Index });
                }
                return Lang.Get("hansa:hud-pumping", work.Ship.WaterText());
            }
            var sel = capi.World.Player.CurrentEntitySelection;
            if (!held || sel?.Entity != work.Ship || sel.SelectionBoxIndex != work.Part || work.Ship.GoneDown) { work = null; return null; }
            double f = (now - work.Started) / 1000.0 / work.Seconds;
            if (f >= 1)
            {
                channel.SendPacket(new RepairPacket { ShipId = work.Ship.EntityId, Part = work.Part });
                work = null;
                return null;
            }
            return Lang.Get("hansa:hud-mending", work.What, (int)(f * 100));
        }

        GuiDialogShipCrew crewDialog;

        void OpenCrewDialog(EntityShip ship)
        {
            if (crewDialog?.IsOpened() == true) return;
            crewDialog?.Dispose();
            crewDialog = new GuiDialogShipCrew(capi, ship, (action, name) => channel.SendPacket(new CrewPacket { ShipId = ship.EntityId, Action = action, Name = name }));
            crewDialog.TryOpen();
        }

        /// <summary>Her crew list or laid-up state came in: the plaque's dialog, if it's open on her, is drawn again.</summary>
        public void OnCrewChanged(EntityShip ship)
        {
            if (crewDialog?.Ship == ship) crewDialog.Refresh();
        }

        GuiDialogShipName nameDialog;

        void OpenNameDialog(EntityShip ship)
        {
            if (nameDialog?.IsOpened() == true) return;
            nameDialog?.Dispose();
            nameDialog = new GuiDialogShipName(capi, ship, (name, style) => channel.SendPacket(new NamePacket
            {
                ShipId = ship.EntityId, Name = name, TextColour = style.Text, BackColour = style.Back ?? "", Size = style.Size, Italic = style.Italic
            }));
            nameDialog.TryOpen();
        }

        EntityShip NearestShip(Vec3d pos)
        {
            EntityShip best = null;
            double bd = double.MaxValue;
            foreach (var s in Ships)
            {
                double d = s.Pos.SquareDistanceTo(pos.X, pos.Y, pos.Z);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        // ------------------------------------------------------------------ network

        void OnRemoteDeckState(DeckStatePacket p)
        {
            var e = capi.World.GetEntityById(p.PlayerEntityId) as EntityAgent;
            if (e == null) return;
            if (e == capi.World.Player.Entity)
            {
                // (review H3-3) the server refused our boarding: off the seat we took, back where we were, not again at once
                if (p.Refused)
                {
                    // (review H4-4) only our latest try: a late answer to an earlier one doesn't undo a boarding since
                    if (p.Attempt == boardAttempt && e.MountedOn is DeckSeat taken && taken.Ship.EntityId == p.ShipId)
                    {
                        e.TryUnmount();
                        if (boardedShip == p.ShipId && boardedFrom != null)
                        {
                            e.Pos.SetPos(boardedFrom.X, boardedFrom.Y, boardedFrom.Z);
                            e.Pos.Motion.Set(0, 0, 0);
                            e.PositionBeforeFalling.Set(boardedFrom.X, boardedFrom.Y, boardedFrom.Z);
                        }
                        pendingLadder = null;
                        boardCooldownUntil = capi.World.ElapsedMilliseconds + 1500;
                    }
                    return;
                }
                // (review H1-2) the server refused a move of ours: back to where it has us
                if (e.MountedOn is DeckSeat own && own.Ship.EntityId == p.ShipId && own.Mover is DeckMover m)
                {
                    m.X = p.X; m.Y = p.Y; m.Z = p.Z;
                    m.VX = m.VY = m.VZ = 0;
                    m.SmoothOffset = 0;
                    m.Climbing = null;
                    m.Unstick(own.Ship.Grid);
                    m.OnGround = DeckMover.HasFloor(own.Ship.Grid, m.X, m.Y, m.Z);
                    lastSentDeck = default;
                }
                return;
            }
            if (e.MountedOn is DeckSeat ds && ds.Ship.EntityId == p.ShipId)
            {
                ds.PushRemote(capi.World.ElapsedMilliseconds, p.X, p.Y, p.Z, p.Flags);
            }
        }

        void OnOpenStorage(FixturePacket p)
        {
            if (!(capi.World.GetEntityById(p.ShipId) is EntityShip ship) || ship.Def == null) return;
            // (a fixture of the design, or a piece's storage: ShipDecor)
            var f = ship.FixtureByIndex(p.Fixture);
            if (f == null) return;
            if (f == ship.HearthFixture && ship.HearthInv != null)
            {
                // the firehearth: vanilla's firepit dialog, rebuilt for her
                if (capi.World.Player.InventoryManager.HasInventory(ship.HearthInv) && ShipStorageDialog.IsOpen(ship.HearthInv)) return;
                capi.World.Player.InventoryManager.OpenInventory(ship.HearthInv);
                new HearthDialog(ship, f, capi).TryOpen();
                return;
            }
            if (!ship.Storages.TryGetValue(f.Code, out var inv)) return;
            if (capi.World.Player.InventoryManager.HasInventory(inv) && ShipStorageDialog.IsOpen(inv)) return;
            capi.World.Player.InventoryManager.OpenInventory(inv);
            // a chest's worth fits the small dialog; the hold gets a big one that scrolls
            GuiDialog dlg = inv.Count > ShipHoldDialog.SmallStorage ? new ShipHoldDialog(inv, ship, capi, f) : new ShipStorageDialog(inv, ship, capi, f);
            dlg.TryOpen();
        }
    }
}
