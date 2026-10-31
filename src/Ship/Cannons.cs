using System;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// How a design's guns shoot (ships/*.json "cannon"; the defaults suit the carrack's bronze demi-culverins). Speeds in
    /// blocks a second (a block is a metre), energy in kJ, masses in kg.
    /// </summary>
    public class CannonDef
    {
        /// <summary>
        /// The ball leaves the muzzle at this speed: about a fifth of a real gun's (a demi-culverin's ~300 m/s), so it's
        /// seen in flight. With real gravity: fired level from the carrack's waist (2.5 over the water) it meets the sea
        /// about 42 blocks out, at 10° of elevation 130, at 20° 220, at 45° over 300.
        /// </summary>
        public float MuzzleVelocity = 60;
        public float Gravity = 9.81f;
        /// <summary>Air drag: the speed falls as dv/dt = −k·v² (per block: a 9-lb iron ball's own, ½·ρ·Cd·A / m).</summary>
        public float AirDrag = 0.0006f;
        /// <summary>
        /// What it hits with: ½·m·v² × this (kJ). The game's ball is slow to be seen; this gives it the energy of a real one
        /// at ~270 m/s: 148 kJ at the muzzle for a 9-lb ball, enough to hole a carrack's side (a panel takes 80), go on across
        /// her and crack the far side.
        /// </summary>
        public float EnergyFactor = 20;
        /// <summary>A gun's shot when the gun doesn't say (kg; 4.1 is 9 lb, a demi-culverin's).</summary>
        public float ShotMass = 4.1f;
        /// <summary>
        /// The spot a hit is spread over (blocks; at most DamageDef.PierceRadius, so it's a shot and goes on through): about the
        /// ball's own size, so a hit holes the plank it strikes (one on the seam between two panels is shared by both).
        /// </summary>
        public float HitRadius = 0.12f;
        /// <summary>Seconds between two shots of one gun (for now: loading, running out and laying it come later).</summary>
        public float Cooldown = 8;
        /// <summary>
        /// Its report: mono, so the game places it and it fades with distance (vanilla's medium explosion, used till 0.3.7,
        /// is stereo: the game plays a stereo sound unplaced, as loud everywhere, and warns each time), heard out to this range
        /// (blocks), this loud. (0.3.25, the user: "it is reverberating and with delay") Our own short, sharp shot
        /// (tools/sounds/cannon_report.py: ReportVariants of them, Report + 1..; (0.3.27, the user: "muffled and does not sound
        /// like a cannon") cut from vanilla's recorded explosions: the crack at once, the large one's body, gone by 1.5-1.8 s), played by each
        /// client at the muzzle as the shot's packet comes (CannonClient), duller and quieter far off. Till 0.3.24 the server
        /// played vanilla's large explosion: 0.29 s of silence before it, then three seconds of rumble at nearly full loudness.
        /// </summary>
        public string Report = "ships:sounds/effect/cannon";
        public int ReportVariants = 3;
        public float ReportRange = 200, ReportVolume = 1f;
        /// <summary>
        /// Into the water: the speed it keeps going in, and the drag there (per block, as AirDrag). A real 9-lb iron ball's
        /// is about 0.42 a metre (3·Cd·ρ water / (8·ρ iron·r), Cd 0.47), and the blow on the surface takes much of its
        /// speed. So it keeps 0.6 of its speed going in (a third of its energy), loses three fifths of what's left in each
        /// block, and is spent within about two blocks (under Spent blocks/s it can't hurt planking any more: then it
        /// sinks). A hit at the waterline holes her as in the air, one through a little water below it cracks a plank and
        /// starts its seams, one that falls into the sea a few blocks short does nothing. (Until 0.3.7 it kept 0.9 of its
        /// speed and went on 3 to 4 blocks, 0.3 a block, spent at 6: the user, "does the cannon ball travel in water like
        /// in air? It should not.")
        /// </summary>
        public float WaterEntry = 0.6f, WaterDrag = 0.45f, Spent = 13;
        /// <summary>
        /// A ball meeting the water at a shallow angle (under RicochetAngle degrees, at more than RicochetSpeed) skips off
        /// it: its way along is RicochetKeep of what it was, and it rises at RicochetBounce of the speed it came down with.
        /// </summary>
        public float RicochetAngle = 6, RicochetSpeed = 20, RicochetKeep = 0.7f, RicochetBounce = 0.4f;
        /// <summary>A creature (or a player) struck takes this many hit points a kJ, and this much energy out of the ball (kJ).</summary>
        public float CreatureDamage = 0.5f, CreatureTakes = 25;
        /// <summary>Breaking blocks (config CannonsBreakBlocks): kJ per point of a block's resistance, at most this many blocks a ball, none harder than MaxResistance.</summary>
        public float BlockBreak = 15, MaxResistance = 4.5f;
        public int MaxBlocks = 3;
        /// <summary>How long a ball is followed (seconds), and how far out of the muzzle before the ship that fired it can be hit (blocks).</summary>
        public float MaxFlight = 12, SelfSafe = 6;
    }

    /// <summary>A ball in flight (world, or relative to anything: the maths doesn't care): where, how fast, in the water or not.</summary>
    public struct ShotState
    {
        public double X, Y, Z, VX, VY, VZ;
        public bool InWater;
        /// <summary>Seconds since it left the muzzle (or since the segment it's on started).</summary>
        public double Time;

        public double Speed => Math.Sqrt(VX * VX + VY * VY + VZ * VZ);
        public Vec3d Pos => new Vec3d(X, Y, Z);
        public Vec3d Vel => new Vec3d(VX, VY, VZ);
    }

    /// <summary>
    /// A cannon ball's flight, pure (the server's shot, every client's drawn ball, the offline tests): gravity and drag in
    /// fixed steps, so a ball stepped by the server in its ticks and by a client in its frames goes the same way; where a
    /// gun's muzzle is and which way it points as the ship lies; the energy it hits with.
    /// </summary>
    public static class Ballistics
    {
        /// <summary>The fixed step (seconds): every side steps a ball by this, whatever its own tick.</summary>
        public const double Step = 1.0 / 60;

        /// <summary>
        /// One step: the drag (air or water) takes its share of the speed (exactly, for a v² drag over the step), then
        /// gravity (in the water less its buoyancy: iron is 7.8 times as heavy), then it moves.
        /// </summary>
        public static void Advance(ref ShotState s, CannonDef d, double h = Step)
        {
            double v = s.Speed;
            double k = s.InWater ? d.WaterDrag : d.AirDrag;
            double f = 1 / (1 + k * v * h);
            s.VX *= f;
            s.VY *= f;
            s.VZ *= f;
            s.VY -= d.Gravity * (s.InWater ? 1 - 1 / 7.8 : 1) * h;
            s.X += s.VX * h;
            s.Y += s.VY * h;
            s.Z += s.VZ * h;
            s.Time += h;
        }

        /// <summary>
        /// Where a ball is t seconds after this state, stepped as every side steps it (whole steps, then part of the next
        /// in a straight line: for drawing between steps).
        /// </summary>
        public static ShotState At(ShotState s, CannonDef d, double t)
        {
            int n = (int)Math.Floor(t / Step);
            for (int i = 0; i < n; i++) Advance(ref s, d);
            double rest = t - n * Step;
            if (rest > 0)
            {
                s.X += s.VX * rest;
                s.Y += s.VY * rest;
                s.Z += s.VZ * rest;
                s.Time += rest;
            }
            return s;
        }

        /// <summary>The energy a ball of this mass hits with at this speed (kJ).</summary>
        public static double Energy(CannonDef d, double mass, double speed) => 0.5 * mass * speed * speed * d.EnergyFactor / 1000;

        /// <summary>The speed a ball of this mass has with this energy left (kJ).</summary>
        public static double SpeedFor(CannonDef d, double mass, double energy) =>
            energy <= 0 ? 0 : Math.Sqrt(2 * energy * 1000 / (Math.Max(0.01, mass) * Math.Max(0.01, d.EnergyFactor)));

        /// <summary>A gun's shot mass (its own, else the design's).</summary>
        public static double MassOf(FixtureDef gun, CannonDef d) => gun?.Shot > 0 ? gun.Shot : d.ShotMass;

        /// <summary>
        /// The gun's muzzle and the way it fires, in her level frame (ship-local), trained square and elevated this much
        /// (degrees): the barrel turned up about its trunnions (<see cref="GunLaying"/> for any lay).
        /// </summary>
        public static void MuzzleLocal(FixtureDef gun, double elevationDeg, out Vec3d muzzle, out Vec3d dir) =>
            GunLaying.MuzzleLocal(gun, new GunLay(0, elevationDeg), out muzzle, out dir);

        /// <summary>
        /// The same as she lies (yaw, and the pitch and roll she really has: the waves', her heel, list and trim), from her
        /// origin (world axes): where the shot leaves the muzzle, and its direction (unit).
        /// </summary>
        public static void Muzzle(FixtureDef gun, double elevationDeg, double yaw, double pitch, double roll, out Vec3d muzzle, out Vec3d dir) =>
            GunLaying.Muzzle(gun, new GunLay(0, elevationDeg), yaw, pitch, roll, out muzzle, out dir);

        /// <summary>How steeply it's coming down (degrees below the level; negative: going up).</summary>
        public static double Descent(in ShotState s) => Math.Atan2(-s.VY, Math.Sqrt(s.VX * s.VX + s.VZ * s.VZ)) * 180 / Math.PI;

        /// <summary>Does it skip off the water here (a shallow angle, fast enough)?</summary>
        public static bool Ricochets(CannonDef d, in ShotState s) => !s.InWater && s.VY < 0 && s.Speed > d.RicochetSpeed && Descent(s) < d.RicochetAngle;

        /// <summary>It skips: on at RicochetKeep of its way along, up at RicochetBounce of the speed it came down with.</summary>
        public static void Ricochet(CannonDef d, ref ShotState s)
        {
            s.VX *= d.RicochetKeep;
            s.VZ *= d.RicochetKeep;
            s.VY = Math.Abs(s.VY) * d.RicochetBounce;
        }

        /// <summary>It goes into the water: slowed by the blow, and the water's drag from now on.</summary>
        public static void EnterWater(CannonDef d, ref ShotState s)
        {
            s.VX *= d.WaterEntry;
            s.VY *= d.WaterEntry;
            s.VZ *= d.WaterEntry;
            s.InWater = true;
        }

        /// <summary>
        /// Where (0..1) along the segment from a to b a ball going down crosses a surface at height h (a at or over it, b
        /// under it); -1 if it doesn't.
        /// </summary>
        public static double Crossing(Vec3d a, Vec3d b, double h)
        {
            if (a.Y < h || b.Y >= h) return -1;
            double dy = a.Y - b.Y;
            return dy < 1e-12 ? 0 : (a.Y - h) / dy;
        }

        /// <summary>
        /// Where a ball fired from h over flat water (a level sea at 0) at this elevation meets it: how far along the level it
        /// has gone and how long it took (no ricochet). For the range table and the tests.
        /// </summary>
        public static double RangeOverWater(CannonDef d, double h, double elevationDeg, out double seconds)
        {
            double e = elevationDeg * Math.PI / 180;
            var s = new ShotState { Y = h, VX = d.MuzzleVelocity * Math.Cos(e), VY = d.MuzzleVelocity * Math.Sin(e) };
            seconds = 0;
            for (int i = 0; i < 200 * 60; i++)
            {
                var a = s.Pos;
                Advance(ref s, d);
                double t = Crossing(a, s.Pos, 0);
                if (t >= 0)
                {
                    seconds = s.Time - Step * (1 - t);
                    return a.X + (s.X - a.X) * t;
                }
            }
            seconds = s.Time;
            return s.X;
        }

        /// <summary>Where a ray (o + t·d, d of any length, t in its lengths) enters a box: t, or -1 if it misses; 0 from inside.</summary>
        public static double RayBox(Vec3d o, Vec3d d, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            double tmin = 0, tmax = double.MaxValue;
            double[] oo = { o.X, o.Y, o.Z }, dd = { d.X, d.Y, d.Z }, lo = { x0, y0, z0 }, hi = { x1, y1, z1 };
            for (int a = 0; a < 3; a++)
            {
                if (Math.Abs(dd[a]) < 1e-12)
                {
                    if (oo[a] < lo[a] || oo[a] > hi[a]) return -1;
                    continue;
                }
                double t1 = (lo[a] - oo[a]) / dd[a], t2 = (hi[a] - oo[a]) / dd[a];
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = Math.Max(tmin, t1);
                tmax = Math.Min(tmax, t2);
                if (tmin > tmax) return -1;
            }
            return tmin;
        }
    }
}
