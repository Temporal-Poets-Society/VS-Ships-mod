using System;
using System.Collections.Generic;
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
    /// Her guns (config Cannons; fixtures of type "gun"): the server lays them (the gun captain's seat behind the breech:
    /// the crew lays the gun where they look, <see cref="GunsTick"/>) and fires them (the shot's flight is
    /// <see cref="CannonShots"/>), keeps each gun's lay, saves it with her and sends it to clients (WatchedAttributes
    /// "hansaGuns", a few times a second while one is being laid). Clients draw them laid (the moving parts' sources
    /// "train:" and "elevate:" + the gun, eased toward the server's lay; the local gun captain's own gun laid at once
    /// from their view, <see cref="GunsFrame"/>) and running back ("fire:" + the gun, from when the shot's packet came),
    /// and say whether each is ready.
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>
        /// A gun's state on the server: when it last fired (it's ready when it has cooled down since), how it's laid and
        /// where its crew is laying it. Powder and shot are loaded while run in; firing consumes the charge and leaves
        /// the carriage run in with its port open for the next load.
        /// </summary>
        public class GunState
        {
            public long FiredMs = long.MinValue / 2;
            public int Shots;
            public byte Charge;
            public bool RunIn;
            /// <summary>How it's laid now, and where it's being laid (it follows at its crew's rates).</summary>
            public GunLay Lay, Aim;
        }

        readonly Dictionary<string, GunState> guns = new();

        /// <summary>Client: when each gun fired (World.ElapsedMilliseconds, when the shot's packet came), for its recoil and "reloading".</summary>
        public readonly Dictionary<string, long> GunFiredAt = new();

        public GunState Gun(string code)
        {
            if (!guns.TryGetValue(code, out var g))
            {
                var f = Def?.Fixture(code);
                var l = new GunLay(0, f?.Elevation ?? 0);
                guns[code] = g = new GunState { Lay = l, Aim = l };
            }
            return g;
        }

        // ------------------------------------------------------------------ laying them

        /// <summary>Her guns, in the order their lays are saved and sent.</summary>
        FixtureDef[] gunList;
        FixtureDef[] GunList => gunList ??= Def == null ? Array.Empty<FixtureDef>() : Array.FindAll(Def.Fixtures, f => f.Type == "gun");

        // client: the lays as the server last sent them, and as they're drawn
        readonly Dictionary<string, GunLay> gunLaySent = new(), gunLayShown = new();
        // server: sending the lays a few times a second while one's being laid
        float gunSyncAccum;
        bool gunLaysDirty;

        /// <summary>How a gun is laid now: the server's own; on clients as it's drawn.</summary>
        public GunLay GunLayNow(FixtureDef gun)
        {
            if (gun == null) return default;
            if (Api?.Side == EnumAppSide.Server) return Gun(gun.Code).Lay;
            if (gunLayShown.TryGetValue(gun.Code, out var shown)) return shown;
            return gunLaySent.TryGetValue(gun.Code, out var sent) ? sent : new GunLay(0, gun.Elevation);
        }

        public GunLay GunLayNow(string code) => GunLayNow(Def?.Fixture(code));

        readonly Dictionary<FixtureDef,(GunLay Lay,double Back,GunLaying.PickPose Pose)> gunPickPoses=new();
        GunLaying.PickPose GunPickPose(FixtureDef f)
        {
            var lay=GunLayNow(f);double back=GunBack(f);
            if(!gunPickPoses.TryGetValue(f,out var cached) || cached.Lay.Train!=lay.Train || cached.Lay.Elevation!=lay.Elevation || cached.Back!=back)
                gunPickPoses[f]=cached=(lay,back,new GunLaying.PickPose(f,lay,back));
            return cached.Pose;
        }
        public double GunPick(FixtureDef f,Vec3d o,Vec3d d) => GunLaying.NearRay(f,GunBack(f),o,d)?GunPickPose(f).Pick(o,d):-1;
        public double[] GunSelectionBounds(FixtureDef f) => GunPickPose(f).Bounds;
        double GunBack(FixtureDef f) => (Api.Side==EnumAppSide.Client?GunRecoil(f.Code):!GunOpen(f)||GunRunIn(f)?1:PartMotion.Recoil((World.ElapsedMilliseconds-Gun(f.Code).FiredMs)/1000.0))*GunRecoilDistance(f);

        /// <summary>
        /// The lay toward where someone at a gun looks (vanilla's view yaw and pitch: a player's, sent by their client): their
        /// view in her frame as she really lies (the server's true pose; on clients the same, unscaled by the player's
        /// SeaMotion). Not within the gun's limits.
        /// </summary>
        public GunLay GunAimFromView(FixtureDef gun, double viewYaw, double viewPitch)
        {
            RealPose(out double pitch, out double roll);
            return GunLaying.FromView(gun, viewYaw, viewPitch, Pos.Yaw, pitch, roll);
        }

        /// <summary>Server: lay a gun at once (a command, a test), within its limits; its crew holds it there.</summary>
        public void SetGunLay(FixtureDef gun, GunLay lay)
        {
            if (gun?.Type != "gun" || Api?.Side != EnumAppSide.Server) return;
            var st = Gun(gun.Code);
            st.Lay = st.Aim = GunLaying.Clamp(gun, lay, GunGateOpen(gun));
            SaveGunLays(true);
        }

        /// <summary>Server: have a gun's crew lay it toward this (within its limits): it follows at their rates (<see cref="GunsTick"/>).</summary>
        public void AimGun(FixtureDef gun, GunLay aim)
        {
            if (gun?.Type != "gun") return;
            Gun(gun.Code).Aim = GunLaying.Clamp(gun, aim, GunGateOpen(gun));
        }

        // ------------------------------------------------------------------ the port's lid: housed or run out

        /// <summary>How long a gun's crew takes to open its port and run it out, or to run it in and shut it (ms).</summary>
        public const int RunOutMs = 2000;

        /// <summary>
        /// A gun with a lidded port (FixtureDef.Lid) is housed (run in, the lid shut) or run out with its port open (its code
        /// among the open fittings, synced); one without a lid is always out.
        /// </summary>
        public bool GunOpen(FixtureDef gun) => gun?.Lid == null || OpenFittings.Contains(gun.Code);

        // server: when each gun's port was opened (it's run out RunOutMs later)
        readonly Dictionary<string, long> gunOpenedMs = new();

        /// <summary>Client: when this client saw each lidded gun's port open or shut (the gun runs out or in, the lid follows).</summary>
        public readonly Dictionary<string, (bool Open, long Ms)> GunPortSeen = new();

        /// <summary>Seconds till a gun is run out (0: it is, or it's housed). Server: its own clock; clients: from when they saw it open.</summary>
        public double GunRunningOut(FixtureDef gun)
        {
            if (gun==null || gun.Swivel || !GunOpen(gun) || GunRunIn(gun)) return 0;
            long since = Api?.Side == EnumAppSide.Server
                ? gunOpenedMs.TryGetValue(gun.Code, out long at) ? World.ElapsedMilliseconds - at : long.MaxValue / 2
                : GunPortSeen.TryGetValue(gun.Code, out var seen) && seen.Open ? World.ElapsedMilliseconds - seen.Ms : long.MaxValue / 2;
            return Math.Max(0, (RunOutMs - since) / 1000.0);
        }

        /// <summary>Server: open a housed gun's port and run it out (at once: for a test or a command).</summary>
        public void OpenGunPort(FixtureDef gun, bool instant = false)
        {
            if (gun==null || GunAbsent(gun) || GunOpen(gun) && !GunRunIn(gun)) return;
            Gun(gun.Code).RunIn=false;
            OpenFittings.Add(gun.Code);
            gunOpenedMs[gun.Code] = World.ElapsedMilliseconds - (instant ? RunOutMs : 0);
            FittingsChanged();
            SaveGunLays(true);
            LocalToWorld(gun.Pos[0], gun.Pos[1] + 0.8, gun.Pos[2], out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/block/door"), x, y, z, null, true, 16, 0.8f);
        }

        /// <summary>Server: run a gun in and shut its port (Shift + right-click it). Returns an error lang code, or null.</summary>
        public string HouseGun(FixtureDef gun)
        {
            if (gun?.Type != "gun" || gun.Lid == null) return "ships:gun-no-lid";
            if (!GunOpen(gun)) return null;
            if (gunLayers.ContainsKey(gun.Code)) gunLayers.Remove(gun.Code);
            OpenFittings.Remove(gun.Code);
            gunOpenedMs.Remove(gun.Code);
            FittingsChanged();
            LocalToWorld(gun.Pos[0], gun.Pos[1] + 0.8, gun.Pos[2], out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/block/door"), x, y, z, null, true, 16, 0.8f);
            return null;
        }

        /// <summary>
        /// Client: note lidded guns opened or shut (after the fittings came from the server): each runs out or in from now
        /// as drawn. The first time, as it is.
        /// </summary>
        public void NotePorts()
        {
            if (Def == null || Api?.Side != EnumAppSide.Client) return;
            long now = World.ElapsedMilliseconds;
            foreach (var f in GunList)
            {
                if (f.Swivel) continue;
                bool open = GunOpen(f) && !Gun(f.Code).RunIn;
                if (!GunPortSeen.TryGetValue(f.Code, out var s)) GunPortSeen[f.Code] = (open, long.MinValue / 4);
                else if (s.Open != open) GunPortSeen[f.Code] = (open, now);
            }
        }

        /// <summary>Client: how far a housed or housing gun is run in now (0 out .. 1 in), for its moving part.</summary>
        public float GunHoused(FixtureDef gun)
        {
            if (gun==null || gun.Swivel) return 0;
            bool open = GunOpen(gun) && !Gun(gun.Code).RunIn;
            double since = GunPortSeen.TryGetValue(gun.Code, out var s) && s.Open == open ? (World.ElapsedMilliseconds - s.Ms) / (double)RunOutMs : 1;
            double k = Math.Clamp(since, 0, 1);
            k = k * k * (3 - 2 * k);
            return (float)(open ? 1 - k : k);
        }

        /// <summary>Client: the lid's goal (0 shut .. 1 open): open while the gun is out or being run out, and while it's still being run in.</summary>
        public float GunLidGoal(FixtureDef gun)
        {
            if (gun?.Lid == null) return 1;
            if (OpenFittings.Contains(gun.Code)) return 1;
            return GunPortSeen.TryGetValue(gun.Code, out var s) && !s.Open && World.ElapsedMilliseconds - s.Ms < RunOutMs ? 1 : 0;
        }

        /// <summary>Is the entry port's gate next to this gun standing open (it trains less that way then: FixtureDef.TrainLimitsGateOpen)?</summary>
        public bool GunGateOpen(FixtureDef gun) => gun?.Gate != null && OpenFittings.Contains(gun.Gate);

        /// <summary>
        /// A gun trained (or being trained) into where this gate's leaf swings open, or null: the gate waits till it's laid
        /// clear (<see cref="ToggleFitting"/>).
        /// </summary>
        public FixtureDef GunInGateway(FixtureDef gate)
        {
            if (gate?.Type != "gate") return null;
            foreach (var f in GunList)
            {
                if (f.Gate != gate.Code || GunAbsent(f)) continue;
                var lay = GunLayNow(f);
                if (GunLaying.Beyond(f, lay, true) || Api?.Side == EnumAppSide.Server && GunLaying.Beyond(f, Gun(f.Code).Aim, true)) return f;
            }
            return null;
        }

        /// <summary>The gun captain's place at a gun (null: none).</summary>
        public GunSeat GunSeatOf(FixtureDef gun) => gun == null ? null : GetSeat("gun:" + gun.Code) as GunSeat;

        /// <summary>How far a gun runs back when fired (blocks: its moving part's).</summary>
        public double GunRecoilDistance(FixtureDef gun)
        {
            if (gun == null || Def == null) return 0;
            foreach (var p in Def.MovingParts) if (p.Code == gun.Code && p.Kind == "slide") return p.Distance;
            return 0;
        }

        /// <summary>Where the gun captain stands at a gun now (feet, ship-local): behind the breech as it's laid, and back with it as it runs back.</summary>
        public Vec3d GunSeatLocal(FixtureDef gun)
        {
            return GunLaying.SeatLocal(gun, GunLayNow(gun), GunBack(gun));
        }

        // server: who is laying each gun (holding right mouse by it), till when (their client says so again twice a second)
        readonly Dictionary<string, (EntityPlayer Who, long Until)> gunLayers = new();
        // (0.3.27) server: each layer's relative aim: the offset from their view (GunLaying.Relative), and the aim their
        // client sent (it's laid where their client lays it)
        readonly Dictionary<string, GunLay> gunLayOffset = new(), gunLayClient = new();

        /// <summary>Where a gun's breech is now (ship-local, on the deck under it): what "by the gun" is measured from.</summary>
        public Vec3d GunBreechLocal(FixtureDef gun) => GunLaying.Breech(gun, GunLayNow(gun));

        /// <summary>Is someone by this gun (within reach of its breech, on its deck), or at its gun captain's place?</summary>
        public bool ByGun(FixtureDef gun, Entity who, double reach)
        {
            if (gun == null || who == null || Def == null) return false;
            if ((who as EntityAgent)?.MountedOn is GunSeat gs && gs.Ship == this && gs.Fixture == gun) return true;
            var l = WorldToLocal(who.Pos.X, who.Pos.Y, who.Pos.Z);
            return GunLaying.InReach(gun, GunLayNow(gun), l.X, l.Y, l.Z, reach);
        }

        /// <summary>How far from a gun's breech a player may lay it (blocks): the client's 2.5, and a little for the lag.</summary>
        public const double LayReach = 3.5;

        /// <summary>
        /// Server: a player holds right mouse by a gun (or at its gun captain's place): it's laid toward where they look while
        /// they go on holding it (their client says so twice a second). Returns an error lang code, or null.
        /// </summary>
        public string StartLaying(FixtureDef gun, EntityPlayer who, bool begin = true)
        {
            if (gun?.Type != "gun" || who == null || !who.Alive) return "ships:gun-cannot";
            if (GunAbsent(gun)) return "ships:gunplace-no-gun";
            if (!Mod.Config.Cannons) return "ships:gun-off";
            if (GoneDown) return "ships:gone-down";
            if (!ByGun(gun, who, LayReach)) return "ships:gun-far";
            long now = World.ElapsedMilliseconds;
            if (gunLayers.TryGetValue(gun.Code, out var other) && other.Who != who && other.Who.Alive && other.Until > now) return "ships:gun-taken";
            // housed: its crew opens the port and runs it out first (it's laid once it's out: the client asks again
            // twice a second while the button is held)
            if (!GunOpen(gun) || GunRunIn(gun)) { if(begin) OpenGunPort(gun); return null; }
            if (GunRunningOut(gun) > 0) return null;
            // (0.3.27) a new layer: the gun stays as it's laid, their view now its zero (no drop to where they look)
            if (!(gunLayers.TryGetValue(gun.Code, out var was) && was.Who == who && was.Until > now))
            {
                gunLayOffset[gun.Code] = GunLaying.AimOffset(Gun(gun.Code).Lay, GunAimFromView(gun, who.Pos.Yaw, who.Pos.Pitch));
                gunLayClient.Remove(gun.Code);
            }
            gunLayers[gun.Code] = (who, now + 1200);
            return null;
        }

        /// <summary>(0.3.27) Server: where the client of the one laying a gun lays it (their relative aim), within its limits.</summary>
        public void LayerAim(FixtureDef gun, EntityPlayer who, GunLay aim)
        {
            if (gun == null || !gunLayers.TryGetValue(gun.Code, out var l) || l.Who != who) return;
            if (float.IsNaN(aim.Train) || float.IsNaN(aim.Elevation)) return;
            gunLayClient[gun.Code] = GunLaying.Clamp(gun, aim, GunGateOpen(gun));
        }

        /// <summary>(0.3.27) Server: the aim of the one laying a gun: their client's, else from their view relative to where they started.</summary>
        GunLay LayerAimNow(FixtureDef f, EntityPlayer gp)
        {
            if (gunLayClient.TryGetValue(f.Code, out var c)) return c;
            var off = gunLayOffset.TryGetValue(f.Code, out var o) ? o : default;
            var aim = GunLaying.Relative(f, GunAimFromView(f, gp.Pos.Yaw, gp.Pos.Pitch), ref off, GunGateOpen(f));
            gunLayOffset[f.Code] = off;
            return aim;
        }

        /// <summary>Server: they let go of right mouse: the gun stays laid as it is now.</summary>
        public void StopLaying(FixtureDef gun, EntityPlayer who)
        {
            if (gun == null || !gunLayers.TryGetValue(gun.Code, out var l) || l.Who != who) return;
            gunLayers.Remove(gun.Code);
            gunLayClient.Remove(gun.Code);
            gunLayOffset.Remove(gun.Code);
            var st = Gun(gun.Code);
            st.Aim = st.Lay;
        }

        /// <summary>Server: the player laying a gun now (null: nobody).</summary>
        public EntityPlayer GunLayer(FixtureDef gun) =>
            gun != null && gunLayers.TryGetValue(gun.Code, out var l) && l.Until > World.ElapsedMilliseconds && l.Who.Alive ? l.Who : null;

        /// <summary>
        /// Server, every tick: each gun someone is laying (holding right mouse by it) is laid toward where they look (their
        /// view as their client sends it, 15 times a second), within its limits, at its crew's rates; one let go of, or whose
        /// layer has gone off or out of reach, stays as it is. The lays go to clients a few times a second while one is
        /// moving, and once more when it stops.
        /// </summary>
        void GunsTick(float dt)
        {
            if (Def == null) return;
            gunSyncAccum += dt;
            bool moving = false;
            foreach (var f in GunList)
            {
                if (GunAbsent(f)) continue;
                var st = Gun(f.Code);
                if (gunLayers.TryGetValue(f.Code, out var layer))
                {
                    var gp = layer.Who;
                    if (!CannonsOn || GoneDown || !gp.Alive || layer.Until < World.ElapsedMilliseconds || !ByGun(f, gp, LayReach))
                    {
                        gunLayers.Remove(f.Code);
                        gunLayClient.Remove(f.Code);
                        gunLayOffset.Remove(f.Code);
                        st.Aim = st.Lay;
                    }
                    else
                    {
                        var aim = LayerAimNow(f, gp);
                        // (a view that moves less than a hair doesn't lay it anew)
                        if (Math.Abs(aim.Train - st.Aim.Train) > 0.02 || Math.Abs(aim.Elevation - st.Aim.Elevation) > 0.02) st.Aim = aim;
                    }
                }
                var next = GunLaying.Step(f, st.Lay, st.Aim, dt, 1, GunGateOpen(f));
                if (next.Train != st.Lay.Train || next.Elevation != st.Lay.Elevation)
                {
                    st.Lay = next;
                    moving = gunLaysDirty = true;
                }
            }
            // (saved as it goes; sent a few times a second, and when it stops)
            if (gunLaysDirty) SaveGunLays(gunSyncAccum >= 0.2f || !moving);
        }

        /// <summary>Server: the lays into her synced attributes (saved with her); sent to clients now if force.</summary>
        void SaveGunLays(bool force)
        {
            var list = GunList;
            if (list.Length == 0) return;
            var lays = new GunLay[list.Length];
            for (int i = 0; i < list.Length; i++) lays[i] = Gun(list[i].Code).Lay;
            WatchedAttributes.GetOrAddTreeAttribute("hansaGuns").SetBytes("lay", GunLaying.Pack(lays));
            var loads=WatchedAttributes.GetOrAddTreeAttribute("hansaGuns").GetOrAddTreeAttribute("loads");
            foreach(var f in list) loads.SetInt(f.Code,Gun(f.Code).Charge | (Gun(f.Code).RunIn?4:0));
            if (!force) return;
            WatchedAttributes.MarkPathDirty("hansaGuns");
            gunSyncAccum = 0;
            gunLaysDirty = false;
        }

        /// <summary>The saved lays (on loading her), and on clients each time the server sends them.</summary>
        void LoadGunLays()
        {
            var list = GunList;
            if (list.Length == 0) return;
            var lays = new GunLay[list.Length];
            for (int i = 0; i < list.Length; i++) lays[i] = new GunLay(0, list[i].Elevation);
            GunLaying.Unpack((WatchedAttributes.GetTreeAttribute("hansaGuns") as ITreeAttribute)?.GetBytes("lay"), lays);
            for (int i = 0; i < list.Length; i++)
            {
                var l = GunLaying.Clamp(list[i], lays[i], GunGateOpen(list[i]));
                int load=WatchedAttributes.GetTreeAttribute("hansaGuns")?.GetTreeAttribute("loads")?.GetInt(list[i].Code)??0;
                Gun(list[i].Code).Charge=(byte)(load&3);
                Gun(list[i].Code).RunIn=(load&4)!=0;
                if (Api?.Side == EnumAppSide.Server)
                {
                    var st = Gun(list[i].Code);
                    st.Lay = st.Aim = l;
                }
                else gunLaySent[list[i].Code] = l;
            }
            if(Api?.Side==EnumAppSide.Client) NotePorts();
        }

        /// <summary>Client: the gun of hers this player is laying now (holding right mouse by it), or null (ShipClientSystem sets it).</summary>
        public FixtureDef LayingHere;
        /// <summary>(0.3.27) Client: where this player lays the gun they're laying (their relative aim), sent to the server; its offset from their view.</summary>
        public GunLay LayingAim;
        GunLay layingOffset;
        FixtureDef layingOffsetOf;

        public void BeginLayingHere(FixtureDef gun)
        {
            LayingHere=gun;
            LayingAim=GunLayNow(gun);
            var me=(Api as ICoreClientAPI)?.World.Player?.Entity;
            if(me==null) return;
            layingOffset=GunLaying.AimOffset(LayingAim,GunAimFromView(gun,me.Pos.Yaw,me.Pos.Pitch));
            layingOffsetOf=gun;
        }

        /// <summary>
        /// Client, every frame: the guns as drawn ease toward the lays the server sent, at a little over their crews' rates,
        /// so everyone sees them turn. The gun this player is laying is laid here at once from their own view (as the server
        /// will lay it), and only drawn back toward the server's lay when it's further off than its lag explains.
        /// </summary>
        public void GunsFrame(float dt)
        {
            if (Def == null || GunList.Length == 0) return;
            var me = (Api as ICoreClientAPI)?.World.Player?.Entity;
            var mine = me != null && CannonsOn && !GoneDown && !GunRunIn(LayingHere) && GunOpen(LayingHere) && GunRunningOut(LayingHere) <= 0 ? LayingHere : null;
            foreach (var f in GunList)
            {
                var sent = gunLaySent.TryGetValue(f.Code, out var s) ? s : new GunLay(0, f.Elevation);
                var shown = gunLayShown.TryGetValue(f.Code, out var sh) ? sh : sent;
                if (f == mine)
                {
                    // ((0.3.27) relative: from where it's laid when they start, no jump to where they look)
                    var view = GunAimFromView(f, me.Pos.Yaw, me.Pos.Pitch);
                    if (layingOffsetOf != f) { layingOffset = GunLaying.AimOffset(shown, view); layingOffsetOf = f; }
                    LayingAim = GunLaying.Relative(f, view, ref layingOffset, GunGateOpen(f));
                    shown = GunLaying.Step(f, shown, LayingAim, dt, 1, GunGateOpen(f));
                    GunLaying.Rates(f, out float rt, out float re);
                    double k = 1 - Math.Exp(-dt * 3);
                    shown.Train += (float)(Beyond(sent.Train - shown.Train, rt * 0.4 + 0.5) * k);
                    shown.Elevation += (float)(Beyond(sent.Elevation - shown.Elevation, re * 0.4 + 0.5) * k);
                }
                else shown = GunLaying.Step(f, shown, sent, dt, 1.3, GunGateOpen(f));
                gunLayShown[f.Code] = shown;
            }
            if (mine == null) layingOffsetOf = null;
        }

        /// <summary>How much of a difference is more than its allowance (0 within it).</summary>
        static double Beyond(double diff, double allowance) => Math.Abs(diff) <= allowance ? 0 : diff - Math.Sign(diff) * allowance;

        /// <summary>Client: this player has just stood to a gun: they look along it as it's laid (as it's drawn).</summary>
        public void LookAlongGun(FixtureDef gun)
        {
            if (Api is not ICoreClientAPI capi || capi.World.Player?.Entity is not EntityPlayer ep) return;
            GunLaying.MuzzleLocal(gun, GunLayNow(gun), out _, out var d);
            GunLaying.LookAlong(LocalDirToWorld(d.X, d.Y, d.Z), out float yaw, out float pitch);
            pitch = GameMath.Clamp(pitch, 1.5857964f, 4.697389f);
            ep.Pos.Yaw = yaw;
            ep.Pos.Pitch = pitch;
            capi.Input.MouseYaw = yaw;
            capi.World.Player.CameraPitch = pitch;
        }

        /// <summary>Seconds till this gun can fire again (0: ready). Server: its own clock; clients: from the shot they saw.</summary>
        public double GunReloading(FixtureDef gun)
        {
            if (gun == null || Def == null) return 0;
            long fired = Api?.Side == EnumAppSide.Server ? Gun(gun.Code).FiredMs : GunFiredAt.TryGetValue(gun.Code, out long at) ? at : long.MinValue / 2;
            // ((25 October 2026) a swivel gun's own, quicker)
            double left = (gun.Reload > 0 ? gun.Reload : Def.Cannon.Cooldown) - (World.ElapsedMilliseconds - fired) / 1000.0;
            return Math.Max(0, left);
        }

        /// <summary>
        /// Her pitch and roll as she really lies now: the waves', her heel, list and trim (the server's own; clients work out
        /// the same, not scaled by the player's SeaMotion as she's drawn: <see cref="TruePitch"/>).
        /// </summary>
        public void RealPose(out double pitch, out double roll)
        {
            if (Api?.Side == EnumAppSide.Server) TruePose(out pitch, out roll);
            else { pitch = TruePitch; roll = TrueRoll; }
        }

        /// <summary>Client: her pitch and roll as she really lies (the waves', her trim, heel and list as drawn at full strength).</summary>
        public double TruePitch, TrueRoll;

        /// <summary>
        /// Where a gun's shot leaves its muzzle (world) and its direction (unit), laid so, as she lies now: her heading, and
        /// her real pitch and roll on the waves (the server keeps her level for walking; not for this).
        /// </summary>
        public void GunMuzzle(FixtureDef gun, GunLay lay, out Vec3d at, out Vec3d dir)
        {
            RealPose(out double pitch, out double roll);
            GunLaying.Muzzle(gun, lay, Pos.Yaw, pitch, roll, out var m, out dir);
            at = new Vec3d(Pos.X + m.X, Pos.InternalY + m.Y, Pos.Z + m.Z);
        }

        /// <summary>The same, trained square and elevated this much (degrees).</summary>
        public void GunMuzzle(FixtureDef gun, double elevationDeg, out Vec3d at, out Vec3d dir) => GunMuzzle(gun, new GunLay(0, elevationDeg), out at, out dir);

        /// <summary>
        /// Server: fire a gun along its lay (or, for a test, at this elevation and train in degrees instead, beyond its
        /// limits if need be). Returns an error lang code, or null when it fired: the shot is on its way
        /// (<see cref="CannonShots"/>), clients see it run back, flash and smoke.
        /// </summary>
        public string FireGun(FixtureDef gun, Entity firer, IServerPlayer player, double? elevation = null, double? train = null)
        {
            if (Api?.Side != EnumAppSide.Server || gun?.Type != "gun" || Def == null) return "ships:gun-cannot";
            if (GunAbsent(gun)) return "ships:gunplace-no-gun";
            if (!Mod.Config.Cannons) return "ships:gun-off";
            if (GoneDown) return "ships:gone-down";
            if (Mod.Shots == null) return "ships:gun-cannot";
            if (GunReloading(gun) > 0) return "ships:gun-reloading";
            if (!GunOpen(gun)) return "ships:gun-housed";
            if (GunRunIn(gun)) return "ships:gun-load-run-out";
            if (GunRunningOut(gun) > 0) return "ships:gun-running-out";
            var st = Gun(gun.Code);
            if(st.Charge!=GunLoading.Loaded) return st.Charge==0?"ships:gun-load-powder":"ships:gun-load-shot";
            st.FiredMs = World.ElapsedMilliseconds;
            st.Shots++;
            st.Charge=0;
            st.RunIn=!gun.Swivel;
            if (!gun.Swivel) gunOpenedMs[gun.Code] = World.ElapsedMilliseconds;
            SaveGunLays(true);
            GunMuzzle(gun, new GunLay(train ?? st.Lay.Train, elevation ?? st.Lay.Elevation), out var at, out var dir);
            // the shot flies off with her own way too
            var d = Def.Cannon;
            double v = d.MuzzleVelocity;
            var vel = new Vec3d(dir.X * v + Math.Sin(Pos.Yaw) * Speed, dir.Y * v, dir.Z * v + Math.Cos(Pos.Yaw) * Speed);
            Mod.Shots.Fire(this, gun, firer, player, at, vel, dir);
            return null;
        }

        /// <summary>
        /// Server: fire every gun on one side (-1 port, +1 starboard, 0 all of them) that's ready. Returns how many fired.
        /// (0.3.25) A broadside is the guns out of that side; the stern chasers (out of her transom) fire only with 0.
        /// </summary>
        public int FireBroadside(int side, Entity firer, IServerPlayer player, double? elevation = null, double? train = null)
        {
            int n = 0;
            foreach (var f in Def.Fixtures)
            {
                // ((25 October 2026) her great guns: a swivel gun is fired by the man at it)
                if (f.Type != "gun" || f.Swivel || GunAbsent(f) || side != 0 && (ShipCompartments.AlongX(f) || Math.Sign(f.Out?[2] ?? 0) != side)) continue;
                // (the command: housed guns are run out at once)
                OpenGunPort(f, true);
                if (FireGun(f, firer, player, elevation, train) == null) n++;
            }
            return n;
        }

        /// <summary>
        /// Server: where a shot's path (world: from origin along dir, range blocks) meets her as she really lies (her wave
        /// pitch and roll, heel, list and trim): her collision (a gun or a shut door included, a fixture's selection box not).
        /// </summary>
        public bool ShotHit(Vec3d origin, Vec3d dir, double range, out ShipRayHit hit, out double pitch, out double roll)
        {
            hit = default;
            RealPose(out pitch, out roll);
            if (Def == null || Grid == null) return false;
            return ShipParts.Ray(Def, Grid, Pos.X, Pos.InternalY, Pos.Z, Pos.Yaw, pitch, roll, origin, dir, range, f => false,
                f => OpenFittings.Contains(f.Code), f => Attached.TryGetValue(f.Code, out var st) ? st : null, out hit, GunAbsent);
        }

        /// <summary>Client: a gun of hers just fired (its shot's packet came): it runs back now.</summary>
        public void GunFired(FixtureDef gun)
        {
            if (gun != null) GunFiredAt[gun.Code] = World.ElapsedMilliseconds;
        }

        /// <summary>Client: how far a gun has run back now (0..1): the moving part's value for "fire:" + the gun.</summary>
        public float GunRecoil(string code)
        {
            float r = GunFiredAt.TryGetValue(code, out long at) ? (float)PartMotion.Recoil((World.ElapsedMilliseconds - at) / 1000.0) : 0;
            // housed, or being run in or out: back as far as the recoil takes it
            var f = Def?.Fixture(code);
            if(f!=null && GunRunIn(f) && GunFiredAt.TryGetValue(code,out long fired) && World.ElapsedMilliseconds-fired<RunOutMs)
                r=Math.Max(r,(float)Math.Clamp((World.ElapsedMilliseconds-fired)/180.0,0,1));
            return f!=null && !f.Swivel ? Math.Max(r, GunHoused(f)) : r;
        }

        /// <summary>The look-at box and the hint's line for a gun: ready, or reloading (seconds), or guns off.</summary>
        public string GunText(FixtureDef gun)
        {
            if (!CannonsOn) return Lang.Get("ships:gun-state-off");
            if (!GunOpen(gun)) return Lang.Get("ships:gun-state-housed");
            if (GunRunIn(gun)) return Lang.Get("ships:gun-state-run-in")+" · "+Lang.Get(Gun(gun.Code).Charge==GunLoading.Loaded?"ships:gun-load-full":Gun(gun.Code).Charge==0?"ships:gun-load-powder":"ships:gun-load-shot");
            double outIn = GunRunningOut(gun);
            if (outIn > 0) return Lang.Get("ships:gun-state-running-out", Math.Ceiling(outIn));
            double left = GunReloading(gun);
            return left > 0 ? Lang.Get("ships:gun-state-reloading", Math.Ceiling(left)) : Gun(gun.Code).Charge!=GunLoading.Loaded
                ? Lang.Get(Gun(gun.Code).Charge==0?"ships:gun-load-powder":"ships:gun-load-shot") : Lang.Get("ships:gun-state-ready");
        }

        /// <summary>
        /// The HUD while laying a gun (or at its gun captain's place): the gun, how it's laid ("trained 5° to the bow,
        /// elevation 3°", and when they look further than it goes), ready or reloading; the keys.
        /// </summary>
        public string GunHudText(FixtureDef gun, EntityAgent captain, bool laying)
        {
            var lay = GunLayNow(gun);
            string text = Lang.Get("ships:hud-gun", ShipHud.FixtureName(this, gun), GunLaying.Text(lay, gun), GunText(gun));
            // ((0.3.27) laying relatively: at a limit while the aim is on it)
            bool mineNow = Api?.Side == EnumAppSide.Client && LayingHere == gun;
            GunLaying.Limits(gun, out float lt0, out float lt1, out float le0, out float le1, GunGateOpen(gun));
            bool atLimit = mineNow
                ? Math.Abs(LayingAim.Train - lt0) < 0.05 || Math.Abs(LayingAim.Train - lt1) < 0.05 || Math.Abs(LayingAim.Elevation - le0) < 0.05 || Math.Abs(LayingAim.Elevation - le1) < 0.05
                : captain != null && GunLaying.Beyond(gun, GunAimFromView(gun, captain.Pos.Yaw, captain.Pos.Pitch), GunGateOpen(gun));
            if (laying && captain != null && CannonsOn && atLimit)
            {
                GunLaying.Limits(gun, out float t0, out float t1, out float e0, out float e1, GunGateOpen(gun));
                // ((0.3.25) a stern chaser trains to port and starboard)
                int across = GunLaying.TrainAcross(gun);
                text += "\n" + (across == 0 ? Lang.Get("ships:hud-gun-limit", Math.Round(-t0), Math.Round(t1), Math.Round(e0), Math.Round(e1))
                    : Lang.Get("ships:hud-gun-limit-across", Math.Round(across > 0 ? -t0 : t1), Math.Round(across > 0 ? t1 : -t0), Math.Round(e0), Math.Round(e1)));
            }
            return text + "\n" + Lang.Get(captain?.MountedOn is GunSeat ? "ships:hud-gun-keys-seat" : "ships:hud-gun-keys");
        }

        /// <summary>The server's Cannons switch, for clients (synced with her state).</summary>
        public bool CannonsOn = true;
    }
}
