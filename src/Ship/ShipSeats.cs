using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// A place on a ship a player can be attached to. Everyone aboard is "mounted" on one of these, so the engine
    /// moves them with the ship on every client and on the server, and teleports carry the ship and its crew.
    /// </summary>
    public abstract class ShipSeat : IMountableSeat
    {
        public const string ClassName = "hansa";

        public readonly EntityShip Ship;
        protected readonly EntityPos seatPos = new EntityPos();

        public SeatConfig Config { get; set; }
        public string SeatId { get; set; }
        public long PassengerEntityIdForInit { get; set; }
        public bool DoTeleportOnUnmount { get; set; } = true;
        public Entity Entity => Ship;
        public Entity Passenger { get; protected set; }
        public IMountable MountSupplier => Ship;
        public virtual bool CanControl => false;
        public virtual EnumMountAngleMode AngleMode => EnumMountAngleMode.PushYaw;
        public abstract AnimationMetaData SuggestedAnimation { get; }
        public virtual bool SkipIdleAnimation => true;
        public float FpHandPitchFollow => 1f;
        public virtual Vec3f LocalEyePos => null;
        public EntityControls Controls { get; } = new EntityControls();

        /// <summary>Feet position, ship-local.</summary>
        public abstract Vec3d LocalPos { get; }
        /// <summary>Facing relative to the ship, radians.</summary>
        public virtual float LocalYaw => 0;

        protected ShipSeat(EntityShip ship, string seatId)
        {
            Ship = ship;
            SeatId = seatId;
            Config = new SeatConfig { SeatId = seatId, Controllable = false };
            Controls.OnAction = OnControls;
        }

        public EntityPos SeatPosition
        {
            get
            {
                var l = LocalPos;
                Ship.LocalToWorld(l.X, l.Y, l.Z, out double x, out double y, out double z);
                seatPos.SetPos(x, y, z);
                seatPos.Yaw = Ship.Pos.Yaw + LocalYaw;
                seatPos.Pitch = 0;
                seatPos.Roll = 0;
                seatPos.Dimension = Ship.Pos.Dimension;
                return seatPos;
            }
        }

        public virtual Matrixf RenderTransform => null;

        protected bool IsLocalPassenger => Ship.Api is ICoreClientAPI capi && Passenger != null && Passenger == capi.World.Player?.Entity;

        /// <summary>The local player seen from outside (third person): their body tilts with the deck too.</summary>
        protected bool LocalThirdPerson => IsLocalPassenger && ((ICoreClientAPI)Ship.Api).World.Player.CameraMode != EnumCameraMode.FirstPerson;

        /// <summary>
        /// m = the ship's drawn pitch and roll as a turn in world axes about the passenger's feet (vanilla applies a
        /// seat's render transform there, before turning the body to its yaw), so passengers lean with the deck.
        /// </summary>
        protected Matrixf Tilted(Matrixf m) => ShipFrame.TiltMatrix(m, Ship.Pos.Yaw, Ship.Pitch, Ship.Roll);
        protected readonly Matrixf rt = new Matrixf();

        protected virtual void OnControls(EnumEntityAction action, bool on, ref EnumHandling handled) { }

        public virtual void MountableToTreeAttributes(TreeAttribute tree)
        {
            tree.SetString("className", ClassName);
            tree.SetLong("entityIdMount", Ship.EntityId);
            tree.SetString("seatId", SeatId);
        }

        public virtual bool CanMount(EntityAgent entityAgent) => Passenger == null || Passenger.EntityId == entityAgent.EntityId || !Passenger.Alive;
        public virtual bool CanUnmount(EntityAgent entityAgent) => true;

        public virtual void DidMount(EntityAgent entityAgent)
        {
            Passenger = entityAgent;
            if (entityAgent == (Ship.Api as ICoreClientAPI)?.World.Player?.Entity) CameraFix.SyncMountYaw(Ship.Api as ICoreClientAPI, SeatPosition.Yaw);
            Ship.OnSeatChanged(this, entityAgent, true);
        }

        public virtual void DidUnmount(EntityAgent entityAgent)
        {
            if (Passenger != null && Passenger.EntityId != entityAgent.EntityId) return;
            Passenger = null;
            entityAgent.PositionBeforeFalling.Set(entityAgent.Pos.X, entityAgent.Pos.Y, entityAgent.Pos.Z);
            Ship.OnSeatChanged(this, entityAgent, false);
        }

        protected AnimationMetaData PassengerAnim(string code)
        {
            var anims = Passenger?.Properties?.Client?.AnimationsByMetaCode;
            if (anims != null && anims.TryGetValue(code, out var meta)) return meta;
            return null;
        }

        /// <summary>Resolves a seat from the "mountedOn" tree a player carries (both sides, after load and on sync).</summary>
        public static IMountableSeat GetMountable(IWorldAccessor world, TreeAttribute tree)
        {
            long shipId = tree.GetLong("entityIdMount");
            if (world.GetEntityById(shipId) is EntityShip ship) return ship.SeatFromTree(tree);
            return null;
        }
    }

    /// <summary>Standing or walking freely on the ship.</summary>
    public class DeckSeat : ShipSeat
    {
        public const byte FlagMoving = 1, FlagSprint = 2, FlagSneak = 4, FlagOnGround = 8, FlagClimbing = 16;

        public readonly string PlayerUid;
        readonly Vec3d local = new Vec3d();
        readonly Vec3d interp = new Vec3d();
        public DeckMover Mover;          // only for the local player on the client
        public byte Flags = FlagOnGround;
        /// <summary>(server, review H1-2) When the player's last deck position came in.</summary>
        public long StateMs;
        /// <summary>
        /// (server, review H2-1) How far they may still move level, up and down (blocks: filled over time, spent by moves),
        /// when that was last worked out; their packets this second, and when their position was last passed on.
        /// </summary>
        public double BudgetLevel, BudgetUp, BudgetDown;
        public long BudgetMs, PacketWindowMs, RelayMs;
        public int PacketsInWindow;

        // remote interpolation (client)
        readonly Queue<(long ms, double x, double y, double z)> remote = new();
        (long ms, double x, double y, double z) remotePrev;
        bool hasRemotePrev;
        const long InterpDelayMs = 110;

        public DeckSeat(EntityShip ship, string playerUid, double x, double y, double z) : base(ship, "deck:" + playerUid)
        {
            PlayerUid = playerUid;
            local.Set(x, y, z);
        }

        public override Vec3d LocalPos
        {
            get
            {
                if (Mover != null) return local.Set(Mover.X, Mover.SmoothY, Mover.Z);
                if (Ship.Api.Side == EnumAppSide.Client && hasRemotePrev) return Interpolated();
                return local;
            }
        }

        public Vec3d StoredLocal => local;

        public void SetLocal(double x, double y, double z, byte flags)
        {
            local.Set(x, y, z);
            Flags = flags;
        }

        public void PushRemote(long nowMs, double x, double y, double z, byte flags)
        {
            Flags = flags;
            local.Set(x, y, z);
            if (!hasRemotePrev) { remotePrev = (nowMs, x, y, z); hasRemotePrev = true; }
            // (review H1-15: nothing comes while they stand still) starting off again: from where they stood, a send ago
            else if (remote.Count == 0 && nowMs - remotePrev.ms > 200) remotePrev = (nowMs - 67, remotePrev.x, remotePrev.y, remotePrev.z);
            remote.Enqueue((nowMs, x, y, z));
            while (remote.Count > 8) remote.Dequeue();
        }

        Vec3d Interpolated()
        {
            long t = Ship.World.ElapsedMilliseconds - InterpDelayMs;
            while (remote.Count > 0 && remote.Peek().ms <= t) remotePrev = remote.Dequeue();
            if (remote.Count == 0) return interp.Set(remotePrev.x, remotePrev.y, remotePrev.z);
            var next = remote.Peek();
            double f = next.ms == remotePrev.ms ? 1 : GameMath.Clamp((t - remotePrev.ms) / (double)(next.ms - remotePrev.ms), 0, 1);
            return interp.Set(remotePrev.x + (next.x - remotePrev.x) * f, remotePrev.y + (next.y - remotePrev.y) * f,
                remotePrev.z + (next.z - remotePrev.z) * f);
        }

        public override AnimationMetaData SuggestedAnimation
        {
            get
            {
                bool moving = (Flags & FlagMoving) != 0, sneak = (Flags & FlagSneak) != 0, sprint = (Flags & FlagSprint) != 0;
                if ((Flags & FlagClimbing) != 0) return PassengerAnim(moving ? "climbup" : "climbidle") ?? PassengerAnim("idle");
                if (moving) return PassengerAnim(sneak ? "sneakwalk" : sprint ? "sprint" : "walk") ?? PassengerAnim("walk");
                return PassengerAnim(sneak ? "sneakidle" : "idle") ?? PassengerAnim("idle");
            }
        }

        public override Matrixf RenderTransform
        {
            get
            {
                if (Ship.Api.Side != EnumAppSide.Client || Passenger == null) return null;
                if (IsLocalPassenger) return LocalThirdPerson ? Tilted(rt) : null;
                // Vanilla turns other players to face the mount's heading; turn them back to where they look.
                return Tilted(rt).RotateY(Passenger.Pos.Yaw - Ship.Pos.Yaw);
            }
        }

        public override void MountableToTreeAttributes(TreeAttribute tree)
        {
            base.MountableToTreeAttributes(tree);
            var l = LocalPos;
            tree.SetDouble("lx", l.X);
            tree.SetDouble("ly", l.Y);
            tree.SetDouble("lz", l.Z);
        }

        public override void DidMount(EntityAgent entityAgent)
        {
            if (Ship.Api is ICoreClientAPI capi && entityAgent == capi.World.Player?.Entity)
            {
                Mover = new DeckMover(local.X, local.Y, local.Z);
                Mover.Unstick(Ship.Grid);
                Mover.OnGround = DeckMover.HasFloor(Ship.Grid, Mover.X, Mover.Y, Mover.Z);
            }
            base.DidMount(entityAgent);
        }

        public override void DidUnmount(EntityAgent entityAgent)
        {
            if (Mover != null) local.Set(Mover.X, Mover.Y, Mover.Z);
            Mover = null;
            base.DidUnmount(entityAgent);
        }
    }

    /// <summary>Seat at a fixture: the helm (steers the ship) or a bunk (sleep).</summary>
    public abstract class FixtureSeat : ShipSeat
    {
        public readonly FixtureDef Fixture;
        readonly Vec3d pos;

        protected FixtureSeat(EntityShip ship, FixtureDef fixture, string prefix) : base(ship, prefix + fixture.Code)
        {
            Fixture = fixture;
            pos = fixture.PosVec;
            if (fixture.Kind == "bed") pos.Y += ShipFittings.BedHeight;   // on the mattress (beds put aboard, and built-in ones)
        }

        public override Vec3d LocalPos => pos;
        public override float LocalYaw => Fixture.Yaw * GameMath.DEG2RAD;

        /// <summary>Sneak gets up and puts you on the deck next to the fixture.</summary>
        protected override void OnControls(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (action == EnumEntityAction.Sneak && on && Ship.Api.Side == EnumAppSide.Server && Passenger is EntityAgent agent)
            {
                var ea = agent;
                Ship.Api.Event.EnqueueMainThreadTask(() => Ship.StandUp(ea, Fixture), "hansa-standup");
            }
        }
    }

    public class HelmSeat : FixtureSeat
    {
        public HelmSeat(EntityShip ship, FixtureDef fixture) : base(ship, fixture, "helm:") { }
        // at a wheel you stand; at a boat's tiller you sit on the stern sheets
        public override AnimationMetaData SuggestedAnimation => Fixture.Pose == "sit" ? PassengerAnim("sitboatidle") ?? RowSeat.SitAnim : PassengerAnim("idle");
        public override Vec3f LocalEyePos => Fixture.Pose == "sit" ? RowSeat.SitEyes : null;
        public override Matrixf RenderTransform =>
            Ship.Api.Side == EnumAppSide.Client && Passenger != null && (!IsLocalPassenger || LocalThirdPerson) ? Tilted(rt) : null;
    }

    /// <summary>
    /// A boat's rowing thwart: sit down (right-click it) and the oars come out. W pulls, S backs, A/D pull one side
    /// harder (that turns her when nobody has the tiller); Shift gets up and the oars are stowed again.
    /// </summary>
    public class RowSeat : FixtureSeat
    {
        public static readonly AnimationMetaData SitAnim = new AnimationMetaData { Code = "sitboatidle", Animation = "sitboatidle" }.Init();
        public static readonly Vec3f SitEyes = new Vec3f(0, 1.0f, 0);

        public RowSeat(EntityShip ship, FixtureDef fixture) : base(ship, fixture, "row:") { }
        public override AnimationMetaData SuggestedAnimation => PassengerAnim("sitboatidle") ?? SitAnim;
        public override Vec3f LocalEyePos => SitEyes;
        public override Matrixf RenderTransform =>
            Ship.Api.Side == EnumAppSide.Client && Passenger != null && (!IsLocalPassenger || LocalThirdPerson) ? Tilted(rt) : null;
    }

    /// <summary>
    /// The gun captain's place at a gun (config Cannons), an extra to laying a gun standing by it: Ctrl + right-click a gun
    /// to stand to it, on the deck just behind its breech, facing out along it. There, as by the gun, holding right mouse
    /// lays it toward where you look (<see cref="EntityShip.GunsTick"/>), a left-click fires it; Shift leaves it. You stay
    /// behind the breech as it's trained, and step back as it runs back at you. The seat's facing stays the gun's own
    /// (square out of her side): it doesn't turn the view as the gun is trained, which would lay it further.
    /// </summary>
    public class GunSeat : FixtureSeat
    {
        public GunSeat(EntityShip ship, FixtureDef gun) : base(ship, gun, "gun:") { }

        public override Vec3d LocalPos => Ship.GunSeatLocal(Fixture);
        public override AnimationMetaData SuggestedAnimation => PassengerAnim("idle");

        public override Matrixf RenderTransform
        {
            get
            {
                if (Ship.Api.Side != EnumAppSide.Client || Passenger == null) return null;
                if (IsLocalPassenger) return LocalThirdPerson ? Tilted(rt) : null;
                // vanilla turns other players to face the mount's heading: turn them back to where they look
                return Tilted(rt).RotateY(Passenger.Pos.Yaw - Ship.Pos.Yaw);
            }
        }

        public override void DidMount(EntityAgent entityAgent)
        {
            bool fresh = Passenger?.EntityId != entityAgent.EntityId;
            base.DidMount(entityAgent);
            // this client's own gun captain: looking along the gun as it's laid, so it isn't laid somewhere else at once
            if (fresh && Ship.Api is ICoreClientAPI capi && entityAgent == capi.World.Player?.Entity) Ship.LookAlongGun(Fixture);
        }
    }

    public class BunkSeat : FixtureSeat
    {
        static readonly AnimationMetaData SleepAnim = new AnimationMetaData { Code = "sleep", Animation = "lie" }.Init();
        long restListener;
        double hoursTotal;

        public BunkSeat(EntityShip ship, FixtureDef fixture) : base(ship, fixture, "bunk:") { }

        public override AnimationMetaData SuggestedAnimation => SleepAnim;
        public override EnumMountAngleMode AngleMode => EnumMountAngleMode.FixateYaw;
        public override Vec3f LocalEyePos => new Vec3f(0, 0.3f, 0);

        public override Matrixf RenderTransform
        {
            get
            {
                if (Ship.Api.Side != EnumAppSide.Client || Passenger == null || IsLocalPassenger) return null;
                return Tilted(rt).RotateY(LocalYaw);
            }
        }

        public override void DidMount(EntityAgent entityAgent)
        {
            bool fresh = Passenger?.EntityId != entityAgent.EntityId;
            base.DidMount(entityAgent);
            if (!fresh) return;
            if (entityAgent.Api.Side == EnumAppSide.Server)
            {
                hoursTotal = entityAgent.World.Calendar.TotalHours;
                if (restListener == 0) restListener = Ship.World.RegisterGameTickListener(Rest, 200);
            }
            entityAgent.Api.Event.EnqueueMainThreadTask(() =>
            {
                if (Passenger == entityAgent && entityAgent.GetBehavior("tiredness") is EntityBehaviorTiredness t) t.IsSleeping = true;
            }, "hansa-issleeping");
        }

        public override void DidUnmount(EntityAgent entityAgent)
        {
            if (entityAgent.GetBehavior("tiredness") is EntityBehaviorTiredness t) t.IsSleeping = false;
            if (restListener != 0)
            {
                Ship.World.UnregisterGameTickListener(restListener);
                restListener = 0;
            }
            bool realUnmount = DoTeleportOnUnmount;
            base.DidUnmount(entityAgent);
            // Woken by the game (e.g. a temporal storm): get up onto the deck instead of dropping through the hull
            if (realUnmount && entityAgent.Alive && entityAgent.Api.Side == EnumAppSide.Server)
            {
                entityAgent.Api.Event.EnqueueMainThreadTask(() => { if (entityAgent.MountedOn == null) Ship.StandUp(entityAgent, Fixture); }, "hansa-wake");
            }
        }

        void Rest(float dt)
        {
            if (!(Passenger is EntityAgent agent)) return;
            double hours = Ship.World.Calendar.TotalHours - hoursTotal;
            if (hours <= 0) return;
            float eff = Math.Max(0.2f, Fixture.Efficiency) - 1f / 12f;
            if (Ship.World.Config.GetString("temporalStormSleeping", "0") == "0" &&
                Ship.Api.ModLoader.GetModSystem<SystemTemporalStability>()?.StormStrength > 0)
            {
                Ship.StandUp(agent, Fixture);
                return;
            }
            if (agent.GetBehavior("tiredness") is EntityBehaviorTiredness t)
            {
                t.Tiredness = Math.Max(0, t.Tiredness - (float)hours / eff);
                if (t.Tiredness <= 0) Ship.StandUp(agent, Fixture);
            }
            hoursTotal = Ship.World.Calendar.TotalHours;
        }
    }

    /// <summary>
    /// The client smooths camera yaw toward the seat yaw using a private "last mount angle". Set it when we attach
    /// the local player so the view doesn't spin by the difference between the ship's heading and the last mount.
    /// </summary>
    static class CameraFix
    {
        static FieldInfo prevMountAngles;
        static bool failed;

        public static void SyncMountYaw(ICoreClientAPI capi, float yaw)
        {
            if (capi == null || failed) return;
            try
            {
                prevMountAngles ??= capi.World.GetType().GetField("prevMountAngles", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (prevMountAngles?.GetValue(capi.World) is Vec3f v) v.Y = yaw;
                else failed = true;
            }
            catch
            {
                failed = true;
            }
        }
    }
}
