using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>How a gun is laid: trained (degrees from square, + toward her bow) and elevated (degrees, + muzzle up).</summary>
    public struct GunLay
    {
        public float Train, Elevation;

        public GunLay(double train, double elevation)
        {
            Train = (float)train;
            Elevation = (float)elevation;
        }

        public override string ToString() => $"train {Train:0.0}°, elevation {Elevation:0.0}°";
    }

    /// <summary>
    /// Which mouse button does what by a gun (the client's ShipClientSystem.GunClick): hold right mouse to lay it where
    /// you look, left-click fires it (the user's choice in the eighth client test; it was the other way round till 0.3.7).
    /// </summary>
    public static class GunButtons
    {
        /// <summary>The press that starts laying a gun (held, it goes on laying it).</summary>
        public static bool Lays(EnumEntityAction action) => action == EnumEntityAction.InWorldRightMouseDown;

        /// <summary>The click that fires one.</summary>
        public static bool Fires(EnumEntityAction action) => action == EnumEntityAction.InWorldLeftMouseDown;

        /// <summary>Is the laying button still held (the in-world mouse buttons' state)?</summary>
        public static bool LayHeld(bool left, bool right) => right;

        /// <summary>
        /// (0.1.0-dev.7, the user on the caravel: "Trying to use telescope from Astra on deck and almost everywhere it tries
        /// to use cannon instead") The gun a right-click lays: the one this player stands to (atGun, its gun captain's place),
        /// unless what's in hand has a right-click of its own (ownUse: <see cref="HeldUse.OwnRightClick"/>); else the gun in
        /// the crosshair (inSight: the fixture the ray meets first, its own selection box), within reach of its breech. Nothing
        /// else: no gun "by its breech" with the crosshair on the sky, the sea or her deck (till 0.1.0-dev.6 the nearest
        /// within 2.5 blocks took the click, which on the caravel's waist is everywhere).
        /// </summary>
        public static FixtureDef LaysGun(FixtureDef atGun, FixtureDef inSight, bool inReach, bool ownUse)
        {
            if (atGun != null && !ownUse) return atGun;
            return inSight?.Type == "gun" && inReach ? inSight : null;
        }
    }

    /// <summary>
    /// Laying a gun (pure: the server, every client and the offline checks use it). The geometry is its moving parts'
    /// (the generator's gun.py, composed parent first by <see cref="PartMotion.Place"/>): the barrel elevates about its
    /// trunnions, the gun runs back inboard along the way it's laid, and the whole gun trains about the upright through
    /// its port (handspikes shift the trail, the muzzle stays in the port). Its limits and rates are the fixture's.
    /// </summary>
    public static class GunLaying
    {
        /// <summary>
        /// How far it trains (degrees, aft .. forward) and is elevated (down .. up): the fixture's, or the defaults; with the
        /// gate next to it standing open (gateOpen), the fixture's narrower train for that.
        /// </summary>
        public static void Limits(FixtureDef gun, out float trainMin, out float trainMax, out float elevMin, out float elevMax, bool gateOpen = false)
        {
            var t = gateOpen && gun?.TrainLimitsGateOpen?.Length >= 2 ? gun.TrainLimitsGateOpen : gun?.TrainLimits;
            var e = gun?.ElevationLimits;
            trainMin = t?.Length >= 2 ? Math.Min(t[0], t[1]) : -12;
            trainMax = t?.Length >= 2 ? Math.Max(t[0], t[1]) : 12;
            elevMin = e?.Length >= 2 ? Math.Min(e[0], e[1]) : -5;
            elevMax = e?.Length >= 2 ? Math.Max(e[0], e[1]) : 15;
        }

        /// <summary>
        /// (0.3.27, the user: "aiming is a bit inconvenient as the starting click to aim makes it point too low in the
        /// beginning") Laying is relative: when it starts, the gun stays as it's laid and the view then is its zero
        /// (<see cref="AimOffset"/>: the lay less the view's lay); moving the view lays it by as much. Past its limits the
        /// offset follows (as a dragged mouse at a screen's edge), so it comes back off a limit at once. The view's lay
        /// (unclamped, <see cref="FromView"/>) in, the lay to aim at out; the offset carried from call to call.
        /// </summary>
        public static GunLay Relative(FixtureDef gun, GunLay view, ref GunLay offset, bool gateOpen = false)
        {
            double train=view.Train+offset.Train;
            while(train>180) train-=360;
            while(train<=-180) train+=360;
            var raw = new GunLay(train, view.Elevation + offset.Elevation);
            var c = Clamp(gun, raw, gateOpen);
            offset.Train += c.Train - raw.Train;
            offset.Elevation += c.Elevation - raw.Elevation;
            return c;
        }

        /// <summary>(0.3.27) The offset that keeps a gun laid as it is for the view it's started from (<see cref="Relative"/>).</summary>
        public static GunLay AimOffset(GunLay lay, GunLay view) => new GunLay(lay.Train - view.Train, lay.Elevation - view.Elevation);

        /// <summary>The nearest lay it can be laid to.</summary>
        public static GunLay Clamp(FixtureDef gun, GunLay l, bool gateOpen = false)
        {
            Limits(gun, out float t0, out float t1, out float e0, out float e1, gateOpen);
            float t = Math.Clamp(float.IsNaN(l.Train) ? 0 : l.Train, t0, t1);
            float e = float.IsNaN(l.Elevation) ? 0 : l.Elevation;
            // ((25 October 2026) a swivel gun trained along her rail is depressed (or its tail lowered) only as far as the rail lets it)
            if (gun?.Swivel == true) return new GunLay(t, SwivelGun.ClampElevation(gun, t, e, e0, e1));
            return new GunLay(t, Math.Clamp(e, e0, e1));
        }

        /// <summary>Is this lay beyond what it can be laid to (more than a hair)?</summary>
        public static bool Beyond(FixtureDef gun, GunLay l, bool gateOpen = false)
        {
            var c = Clamp(gun, l, gateOpen);
            return Math.Abs(c.Train - l.Train) > 0.1 || Math.Abs(c.Elevation - l.Elevation) > 0.1;
        }

        /// <summary>The crew's rates (degrees a second): trained, elevated.</summary>
        public static void Rates(FixtureDef gun, out float train, out float elevation)
        {
            train = gun?.TrainRate > 0 ? gun.TrainRate : 10;
            elevation = gun?.ElevationRate > 0 ? gun.ElevationRate : 6;
        }

        /// <summary>dt seconds of laying it from now toward aim (within its limits), at its crew's rates times speed.</summary>
        public static GunLay Step(FixtureDef gun, GunLay now, GunLay aim, double dt, double speed = 1, bool gateOpen = false)
        {
            aim = Clamp(gun, aim, gateOpen);
            Rates(gun, out float rt, out float re);
            double mt = rt * dt * speed, me = re * dt * speed;
            return new GunLay(now.Train + Math.Clamp(aim.Train - now.Train, -mt, mt), now.Elevation + Math.Clamp(aim.Elevation - now.Elevation, -me, me));
        }

        // ------------------------------------------------------------------ where it is

        /// <summary>The level way it points laid square (unit, ship-local x and z).</summary>
        public static void OutOf(FixtureDef gun, out double ox, out double oz)
        {
            ox = gun?.Out?[0] ?? 0;
            oz = gun?.Out?.Length > 2 ? gun.Out[2] : 1;
            double l = Math.Sqrt(ox * ox + oz * oz);
            if (l < 1e-9) { ox = 0; oz = 1; l = 1; }
            ox /= l;
            oz /= l;
        }

        /// <summary>
        /// Which way the upright it trains about points (+1: up, -1: down): a turn by + (right hand rule) brings its muzzle
        /// toward the bow; up for a gun on her starboard side, down for one on her port side (gun.py's train_sign).
        /// </summary>
        public static double TrainSign(FixtureDef gun)
        {
            OutOf(gun, out _, out double oz);
            return oz >= 0 ? 1 : -1;
        }

        /// <summary>Where its breech is laid so (ship-local, on the deck under it): what "by the gun" is measured from.</summary>
        public static Vec3d Breech(FixtureDef gun, GunLay lay)
        {
            var p = gun?.Pos;
            if (p == null) return new Vec3d();
            OutOf(gun, out double ox, out double oz);
            return Place(gun, lay, 0, new Vec3d(p[0] - ox * 1.15, p[1], p[2] - oz * 1.15), false);
        }

        /// <summary>Is a ship-local spot within reach of its breech laid so (on its deck: under 1.5 up or down)?</summary>
        public static bool InReach(FixtureDef gun, GunLay lay, double x, double y, double z, double reach)
        {
            if (gun?.Pos == null) return false;
            var b = Breech(gun, lay);
            double floor = gun.Swivel && gun.Seat?.Length >= 3 ? gun.Seat[1] : b.Y;
            return Math.Abs(y - floor) < 1.5 && (x - b.X) * (x - b.X) + (z - b.Z) * (z - b.Z) <= reach * reach;
        }

        static Vec3d V(double[] a, Vec3d fallback) => a?.Length >= 3 ? new Vec3d(a[0], a[1], a[2]) : fallback;

        /// <summary>A point on the upright it trains about (ship-local).</summary>
        public static Vec3d PivotOf(FixtureDef gun) => V(gun?.Pivot, V(gun?.Pos, new Vec3d()));

        /// <summary>A point on the trunnions' line, which it elevates about (ship-local).</summary>
        public static Vec3d TrunnionOf(FixtureDef gun)
        {
            if (gun?.Trunnion?.Length >= 3) return V(gun.Trunnion, null);
            var p = V(gun?.Pos, new Vec3d());
            return new Vec3d(p.X, gun?.Muzzle?.Length >= 3 ? gun.Muzzle[1] : p.Y, p.Z);
        }

        /// <summary>A vector turned by angle (radians) about a unit axis (the right hand rule).</summary>
        static Vec3d Turn(Vec3d v, double kx, double ky, double kz, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a), d = (kx * v.X + ky * v.Y + kz * v.Z) * (1 - c);
            return new Vec3d(v.X * c + (ky * v.Z - kz * v.Y) * s + kx * d,
                             v.Y * c + (kz * v.X - kx * v.Z) * s + ky * d,
                             v.Z * c + (kx * v.Y - ky * v.X) * s + kz * d);
        }

        /// <summary>
        /// A point of the gun as it stands laid square and run out (ship-local) where it is laid and run back `back` blocks:
        /// the barrel's (barrel: true) turned up about the trunnions first; then everything slides inboard and turns about
        /// the upright through its port. The same as its moving parts composed.
        /// </summary>
        public static Vec3d Place(FixtureDef gun, GunLay lay, double back, Vec3d p, bool barrel)
        {
            OutOf(gun, out double ox, out double oz);
            var q = p.Clone();
            if (barrel)
            {
                // about the trunnions' line: out x up = (-oz, 0, ox)
                var t = TrunnionOf(gun);
                var r = Turn(new Vec3d(q.X - t.X, q.Y - t.Y, q.Z - t.Z), -oz, 0, ox, lay.Elevation * GameMath.DEG2RAD);
                q.Set(t.X + r.X, t.Y + r.Y, t.Z + r.Z);
            }
            q.X -= ox * back;
            q.Z -= oz * back;
            var pv = PivotOf(gun);
            var rr = Turn(new Vec3d(q.X - pv.X, q.Y - pv.Y, q.Z - pv.Z), 0, TrainSign(gun), 0, lay.Train * GameMath.DEG2RAD);
            return new Vec3d(pv.X + rr.X, pv.Y + rr.Y, pv.Z + rr.Z);
        }

        /// <summary>The way the barrel points laid so (unit, ship-local).</summary>
        public static Vec3d Dir(FixtureDef gun, GunLay lay)
        {
            OutOf(gun, out double ox, out double oz);
            var d = Turn(new Vec3d(ox, 0, oz), -oz, 0, ox, lay.Elevation * GameMath.DEG2RAD);
            return Turn(d, 0, TrainSign(gun), 0, lay.Train * GameMath.DEG2RAD);
        }

        /// <summary>Where the shot leaves the muzzle laid so (ship-local; run back `back` blocks: only for the checks), and its way.</summary>
        public static void MuzzleLocal(FixtureDef gun, GunLay lay, out Vec3d muzzle, out Vec3d dir, double back = 0)
        {
            var m = V(gun?.Muzzle, V(gun?.Pos, new Vec3d()));
            muzzle = Place(gun, lay, back, m, true);
            dir = Dir(gun, lay);
        }

        /// <summary>
        /// The same as she lies (yaw, and the pitch and roll she really has: the waves', her heel, list and trim), from her
        /// origin (world axes): where the shot leaves the muzzle, and its direction (unit).
        /// </summary>
        public static void Muzzle(FixtureDef gun, GunLay lay, double yaw, double pitch, double roll, out Vec3d muzzle, out Vec3d dir)
        {
            MuzzleLocal(gun, lay, out var ml, out var dl);
            ShipFrame.ToWorld(yaw, pitch, roll, ml.X, ml.Y, ml.Z, out double mx, out double my, out double mz);
            ShipFrame.ToWorld(yaw, pitch, roll, dl.X, dl.Y, dl.Z, out double dx, out double dy, out double dz);
            muzzle = new Vec3d(mx, my, mz);
            dir = new Vec3d(dx, dy, dz);
        }

        /// <summary>How far the gun captain stands clear behind the breech (blocks): the gun runs back this far before he has to step back with it.</summary>
        public const double SeatSlack = 0.2;

        /// <summary>
        /// Where the gun captain stands (feet, ship-local): behind the breech as the gun is trained, and stepping back as it
        /// runs back at him (`back` blocks: all but the room he had).
        /// </summary>
        public static Vec3d SeatLocal(FixtureDef gun, GunLay lay, double back)
        {
            OutOf(gun, out double ox, out double oz);
            var p = V(gun?.Pos, new Vec3d());
            var seat = V(gun?.Seat, new Vec3d(p.X - ox * 1.95, p.Y, p.Z - oz * 1.95));
            return Place(gun, lay, Math.Max(0, back - SeatSlack), seat, false);
        }

        // ------------------------------------------------------------------ laid where you look
        /// <summary>Handle the carriage and barrel where they are drawn, with train, elevation and run-back.</summary>
        public static double Pick(FixtureDef gun, GunLay lay, double back, Vec3d o, Vec3d d)
        {
            if (!NearRay(gun,back,o,d)) return -1;
            var cache=pickCache.GetValue(gun,_=>new CachedPick());
            if(cache.Pose==null || cache.Lay.Train!=lay.Train || cache.Lay.Elevation!=lay.Elevation || cache.Back!=back)
                {cache.Lay=lay;cache.Back=back;cache.Pose=new PickPose(gun,lay,back);}
            return cache.Pose.Pick(o,d);
        }
        sealed class CachedPick { public GunLay Lay;public double Back;public PickPose Pose; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FixtureDef,CachedPick> pickCache=new();

        /// <summary>Conservative rejection before constructing any matrices or corner arrays.</summary>
        public static bool NearRay(FixtureDef gun,double back,Vec3d o,Vec3d d)
        {
            if(gun?.Pos==null) return false;
            double x=gun.Pos[0]-o.X,y=gun.Pos[1]-o.Y,z=gun.Pos[2]-o.Z;
            double radius=3+Math.Abs(back);
            double along=Math.Max(0,x*d.X+y*d.Y+z*d.Z);
            return (x-along*d.X)*(x-along*d.X)+(y-along*d.Y)*(y-along*d.Y)+(z-along*d.Z)*(z-along*d.Z)<=radius*radius;
        }

        public sealed class PickPose
        {
            readonly (double[] Box,float[] Inverse)[] bodies;
            public readonly double[] Bounds;
            public PickPose(FixtureDef gun,GunLay lay,double back)
            {
                var list=new System.Collections.Generic.List<(double[],float[])>();
                var corners=new System.Collections.Generic.List<double[]>();
                foreach(var body in PickBodies(gun)) {
                    var m=Matrix(gun,lay,back,body.Barrel);var inv=new float[16];Mat4f.Invert(inv,m);
                    list.Add((body.Box,inv));
                    foreach(var p in PlacementMesh.Corners(body.Box)) corners.Add(PlacementMesh.Transform(m,p));
                }
                bodies=list.ToArray();Bounds=PlacementMesh.Bounds(corners);
            }
            public double Pick(Vec3d o,Vec3d d)
            {
            double best=double.MaxValue;
            foreach(var (box,inv) in bodies)
            {
                var a=PlacementMesh.Transform(inv,new[] {o.X,o.Y,o.Z});
                var end=PlacementMesh.Transform(inv,new[] {o.X+d.X,o.Y+d.Y,o.Z+d.Z});
                double t=ShipFittings.RayBox(new Vec3d(a[0],a[1],a[2]),new Vec3d(end[0]-a[0],end[1]-a[1],end[2]-a[2]),
                    new Cuboidd(box[0],box[1],box[2],box[3],box[4],box[5]));
                if(t>=0) best=Math.Min(best,t);
            }
            return best==double.MaxValue?-1:best;
            }
        }

        public static double[] SelectionBounds(FixtureDef gun,GunLay lay,double back) =>
            PlacementMesh.Bounds(PickBodies(gun).SelectMany(b=>PlacementMesh.Corners(b.Box).Select(p=>{
                var v=Place(gun,lay,back,new Vec3d(p[0],p[1],p[2]),b.Barrel);return new[] {v.X,v.Y,v.Z}; })));

        static System.Collections.Generic.IEnumerable<(double[] Box,bool Barrel)> PickBodies(FixtureDef gun)
        {
            if(gun.Swivel && gun.Sel!=null) { yield return (gun.Sel,true);yield break; }
            if((gun.Box??gun.Sel) is double[] carriage) yield return (carriage,false);
            var t=TrunnionOf(gun);OutOf(gun,out double ox,out double oz);
            double r=Math.Max(0.09,gun.Bore*1.15),tail=gun.Swivel?SwivelGun.TailReach:0.72;
            var muzzle=gun.Muzzle??new[] {t.X+ox,t.Y,t.Z+oz};
            yield return (new[] { Math.Min(t.X-ox*tail,muzzle[0])-r,t.Y-r,Math.Min(t.Z-oz*tail,muzzle[2])-r,
                Math.Max(t.X-ox*tail,muzzle[0])+r,t.Y+r,Math.Max(t.Z-oz*tail,muzzle[2])+r },true);
        }

        public static float[] Matrix(FixtureDef gun,GunLay lay,double back,bool barrel)
        {
            var m=new float[16];var o=Place(gun,lay,back,new Vec3d(),barrel);
            m[12]=(float)o.X;m[13]=(float)o.Y;m[14]=(float)o.Z;m[15]=1;
            for(int a=0;a<3;a++) {
                var p=Place(gun,lay,back,new Vec3d(a==0?1:0,a==1?1:0,a==2?1:0),barrel);
                m[4*a]=(float)(p.X-o.X);m[4*a+1]=(float)(p.Y-o.Y);m[4*a+2]=(float)(p.Z-o.Z);
            }
            return m;
        }

        /// <summary>
        /// The lay that points the barrel along a direction (ship-local, any length): its elevation over her deck, and its
        /// turn from square toward the bow. Not within its limits (<see cref="Clamp"/>).
        /// </summary>
        public static GunLay FromLocalDir(FixtureDef gun, double dx, double dy, double dz)
        {
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (l < 1e-9) return new GunLay(0, 0);
            double e = Math.Asin(Math.Clamp(dy / l, -1, 1));
            OutOf(gun, out double ox, out double oz);
            if (Math.Abs(dx) + Math.Abs(dz) < 1e-9) return new GunLay(0, e * GameMath.RAD2DEG);
            // the turn about +y that takes square (ox, oz) to the way it's looked (dx, dz), then about its own upright
            double phi = Math.Atan2(oz, ox) - Math.Atan2(dz, dx);
            while (phi > Math.PI) phi -= 2 * Math.PI;
            while (phi <= -Math.PI) phi += 2 * Math.PI;
            return new GunLay(phi * TrainSign(gun) * GameMath.RAD2DEG, e * GameMath.RAD2DEG);
        }

        /// <summary>Vanilla's view vector (EntityPos.GetViewVector, in doubles): a player's pitch is π looking level, less looking up.</summary>
        public static void ViewVector(double pitch, double yaw, out double dx, out double dy, out double dz)
        {
            double cp = Math.Cos(pitch);
            dx = -cp * Math.Sin(yaw);
            dy = Math.Sin(pitch);
            dz = -cp * Math.Cos(yaw);
        }

        /// <summary>
        /// The lay toward where someone looks (their view's yaw and pitch, as vanilla has a player's), aboard a ship with this
        /// yaw, pitch and roll: the view turned into her frame. Not within its limits.
        /// </summary>
        public static GunLay FromView(FixtureDef gun, double viewYaw, double viewPitch, double shipYaw, double pitch, double roll)
        {
            ViewVector(viewPitch, viewYaw, out double vx, out double vy, out double vz);
            ShipFrame.ToLocal(shipYaw, pitch, roll, vx, vy, vz, out double lx, out double ly, out double lz);
            return FromLocalDir(gun, lx, ly, lz);
        }

        /// <summary>The view's yaw and pitch (vanilla's, a player's) that looks along a world direction.</summary>
        public static void LookAlong(Vec3d dir, out float yaw, out float pitch)
        {
            double l = Math.Max(1e-9, dir.Length());
            yaw = (float)Math.Atan2(dir.X, dir.Z);
            if (yaw < 0) yaw += GameMath.TWOPI;
            pitch = (float)(Math.PI - Math.Asin(Math.Clamp(dir.Y / l, -1, 1)));
        }

        // ------------------------------------------------------------------ saying it, sending it

        /// <summary>
        /// (0.3.25, the stern chasers) Which side a gun pointing along her length (out of her stern or bow) turns its muzzle
        /// to when it's trained by a positive angle: +1 starboard, -1 port; 0 for a gun on her side (+ is toward her bow).
        /// </summary>
        public static int TrainAcross(FixtureDef gun)
        {
            if (gun?.Out == null || gun.Out.Length < 3 || Math.Abs(gun.Out[0]) <= Math.Abs(gun.Out[2])) return 0;
            OutOf(gun, out double ox, out _);
            // (the muzzle's way as it starts to turn: TrainSign × (up × out), its z = -TrainSign · ox)
            return -TrainSign(gun) * ox > 0 ? 1 : -1;
        }

        /// <summary>
        /// "trained 5° to the bow, elevation 3°" (the look-at box and the gun captain's HUD); a stern chaser's "trained 5° to
        /// starboard".
        /// </summary>
        public static string Text(GunLay lay, FixtureDef gun = null)
        {
            int t = (int)Math.Round(lay.Train), e = (int)Math.Round(lay.Elevation);
            int across = TrainAcross(gun);
            string train = t == 0 ? Lang.Get("hansa:gun-train-square")
                : across != 0 ? Lang.Get(t * across > 0 ? "hansa:gun-train-starboard" : "hansa:gun-train-port", Math.Abs(t))
                : Lang.Get(t > 0 ? "hansa:gun-train-bow" : "hansa:gun-train-stern", Math.Abs(t));
            return Lang.Get("hansa:gun-lay", train, e < 0 ? "−" + -e : e.ToString());
        }

        /// <summary>A ship's guns' lays, compactly (4 bytes a gun: hundredths of a degree), to save and send.</summary>
        public static byte[] Pack(GunLay[] lays)
        {
            var b = new byte[lays.Length * 4];
            for (int i = 0; i < lays.Length; i++)
            {
                short t = (short)Math.Clamp(Math.Round(lays[i].Train * 100), short.MinValue, short.MaxValue);
                short e = (short)Math.Clamp(Math.Round(lays[i].Elevation * 100), short.MinValue, short.MaxValue);
                b[i * 4] = (byte)t;
                b[i * 4 + 1] = (byte)(t >> 8);
                b[i * 4 + 2] = (byte)e;
                b[i * 4 + 3] = (byte)(e >> 8);
            }
            return b;
        }

        /// <summary>The lays back from <see cref="Pack"/> (as many as there are; the rest stay as they are).</summary>
        public static void Unpack(byte[] b, GunLay[] into)
        {
            if (b == null) return;
            for (int i = 0; i < into.Length && i * 4 + 3 < b.Length; i++)
            {
                short t = (short)(b[i * 4] | b[i * 4 + 1] << 8), e = (short)(b[i * 4 + 2] | b[i * 4 + 3] << 8);
                into[i] = new GunLay(t / 100.0, e / 100.0);
            }
        }
    }
}
