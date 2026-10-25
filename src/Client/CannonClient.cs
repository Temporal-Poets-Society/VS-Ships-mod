using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// Client side of the guns: the cannon balls in flight, flown here between the server's packets in the same fixed
    /// steps as the server flies them (<see cref="Ballistics"/>), and drawn by <see cref="ShipRenderer"/> (the design's
    /// "shot" mesh: a cast iron ball); a gun running back when it fires (its ship's GunFiredAt, read by the moving part's
    /// "fire:" source); the flash of light at the muzzle. The smoke, the splashes and the splinters are vanilla particles
    /// the server spawns. Nothing here has been seen in the game yet; anything that goes wrong is logged once and
    /// cannon balls aren't drawn for the rest of the session.
    /// </summary>
    public class CannonClient
    {
        readonly ICoreClientAPI capi;
        public readonly Dictionary<int, Flying> Balls = new();
        readonly List<Flash> flashes = new();
        bool failed;

        /// <summary>A ball in flight: the state it was last known in (a packet), and where it has got to since, stepped.</summary>
        public class Flying
        {
            public CannonDef D;
            public ShotState Now;
            /// <summary>(25 October 2026) How big it's drawn: its gun's bore to a great gun's (a swivel gun's small ball), 1 for those.</summary>
            public float Scale = 1;
            /// <summary>World.ElapsedMilliseconds when Now.Time was 0 (the packet came).</summary>
            public long StartMs;

            /// <summary>Where it's drawn this frame: stepped as the server steps it, and part of the next step in a line.</summary>
            public Vec3d Drawn(long nowMs)
            {
                double t = (nowMs - StartMs) / 1000.0;
                while (Now.Time + Ballistics.Step <= t) Ballistics.Advance(ref Now, D);
                double rest = Math.Max(0, t - Now.Time);
                return new Vec3d(Now.X + Now.VX * rest, Now.Y + Now.VY * rest, Now.Z + Now.VZ * rest);
            }
        }

        class Flash : IPointLight
        {
            public Vec3f Color { get; set; } = new Vec3f();
            public Vec3d Pos { get; } = new Vec3d();
            public long Born;
        }

        public CannonClient(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>A shot's packet: fired (the gun runs back, a flash, the ball starts), going on from somewhere new, or stopped.</summary>
        public void OnShot(ShotPacket p)
        {
            try
            {
                long now = capi.World.ElapsedMilliseconds;
                var state = new ShotState { X = p.X, Y = p.Y, Z = p.Z, VX = p.VX, VY = p.VY, VZ = p.VZ, InWater = p.InWater };
                switch (p.Kind)
                {
                    case ShotPacket.Fired:
                    {
                        var ship = capi.World.GetEntityById(p.ShipId) as EntityShip;
                        var gun = ship?.Def != null && p.Gun >= 0 && p.Gun < ship.Def.Fixtures.Length ? ship.Def.Fixtures[p.Gun] : null;
                        if (gun?.Type == "gun") ship.GunFired(gun);
                        // ((25 October 2026) a swivel gun's: a small ball, a smaller report)
                        float scale = gun?.Swivel == true ? Math.Clamp(gun.Bore / 0.14f, 0.2f, 1) : 1;
                        Balls[p.Id] = new Flying { D = ship?.Def?.Cannon ?? new CannonDef(), Now = state, StartMs = now, Scale = scale };
                        AddFlash(p.X, p.Y, p.Z, now);
                        Report(p.X, p.Y, p.Z, ship?.Def?.Cannon ?? new CannonDef(), gun?.Swivel == true ? 0.45f : 1);
                        break;
                    }
                    case ShotPacket.Going:
                        if (Balls.TryGetValue(p.Id, out var f))
                        {
                            f.Now = state;
                            f.StartMs = now;
                        }
                        else Balls[p.Id] = new Flying { D = new CannonDef(), Now = state, StartMs = now };
                        break;
                    default:
                        Balls.Remove(p.Id);
                        break;
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        readonly Random rand = new();

        /// <summary>
        /// (0.3.25, the user: "fix also cannon fire sound. it is reverberating and with delay") The gun's report, here at once
        /// as the shot's packet comes (it was the server's sound: vanilla's large explosion, 0.29 s of silence before it and
        /// three seconds of rumble): one of our short shots (CannonDef.Report + 1..ReportVariants), at the muzzle, its pitch a
        /// little different each time, no reverb; further off quieter (the engine's distance), duller and a little lower.
        /// </summary>
        void Report(double x, double y, double z, CannonDef d, float loud = 1)
        {
            var me = capi.World.Player?.Entity;
            double dist = me == null ? 0 : me.Pos.DistanceTo(new Vec3d(x, y, z));
            if (dist > d.ReportRange) return;
            int n = 1 + rand.Next(Math.Max(1, d.ReportVariants));
            double far = Math.Clamp(dist / d.ReportRange, 0, 1);
            var loc = new AssetLocation(d.Report + (d.ReportVariants > 0 ? n.ToString() : "")).WithPathAppendixOnce(".ogg");
            var sound = capi.World.LoadSound(new SoundParams
            {
                Location = loc,
                Position = new Vec3f((float)x, (float)y, (float)z),
                RelativePosition = false,
                DisposeOnFinish = true,
                Range = d.ReportRange,
                ReferenceDistance = 12,
                Volume = d.ReportVolume * loud,
                Pitch = (float)(0.92 + rand.NextDouble() * 0.16 - 0.1 * far),
                // ((0.3.27, the user: "muffled") mild: clear near by, a little dull only far off)
                LowPassFilter = (float)(1 - 0.6 * far * far),
                ReverbDecayTime = 0,
                SoundType = EnumSoundType.Sound,
            });
            sound?.Start();
        }

        /// <summary>Every frame: balls that should have come down long ago are dropped; flashes fade and go.</summary>
        public void Update()
        {
            if (failed) return;
            try
            {
                long now = capi.World.ElapsedMilliseconds;
                List<int> old = null;
                foreach (var kv in Balls)
                    if ((now - kv.Value.StartMs) / 1000.0 > kv.Value.D.MaxFlight + 1) (old ??= new List<int>()).Add(kv.Key);
                if (old != null) foreach (int id in old) Balls.Remove(id);
                for (int i = flashes.Count - 1; i >= 0; i--)
                {
                    var fl = flashes[i];
                    double age = (now - fl.Born) / 1000.0;
                    if (age > FlashSeconds)
                    {
                        capi.Render.RemovePointLight(fl);
                        flashes.RemoveAt(i);
                        continue;
                    }
                    float k = (float)(1 - age / FlashSeconds);
                    // (blue, green, red) × strength
                    fl.Color.Set(6 * k, 14 * k, 22 * k);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        const double FlashSeconds = 0.14;

        void AddFlash(double x, double y, double z, long now)
        {
            var fl = new Flash { Born = now };
            fl.Pos.Set(x, y, z);
            fl.Color.Set(6, 14, 22);
            flashes.Add(fl);
            capi.Render.AddPointLight(fl);
        }

        /// <summary>Where each ball is drawn now, and how big (none once something failed).</summary>
        public IEnumerable<(Vec3d At, float Scale)> Drawn()
        {
            if (failed || Balls.Count == 0) yield break;
            long now = capi.World.ElapsedMilliseconds;
            foreach (var f in Balls.Values) yield return (f.Drawn(now), f.Scale);
        }

        void Fail(Exception e)
        {
            if (failed) return;
            failed = true;
            capi.Logger.Error("[hansa] Cannon balls failed on this client; they're not drawn until the game is restarted: {0}", e);
            foreach (var fl in flashes) capi.Render.RemovePointLight(fl);
            flashes.Clear();
            Balls.Clear();
        }

        public void Dispose()
        {
            foreach (var fl in flashes) capi.Render.RemovePointLight(fl);
            flashes.Clear();
            Balls.Clear();
        }
    }
}
