using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// Ships' firehearths on this client, what vanilla's lit firepit block does by itself and a ship's hearth can't (it
    /// moves): its particles (the lit firepit's own flames, embers and smoke, cloned from the block's particle properties
    /// and spawned at the firepit as it's drawn, moving with her; a pot on it gets vanilla's ring of flames round it; an
    /// extinct one its faint smoke), smoke out of the stovepipe's mouth drifting off with Sea's wind, and vanilla's looped
    /// crackle (and a pot's bubbling) following the hearth. The positions are worked out each frame on the main thread
    /// (<see cref="Update"/>, as the renderer draws her) and handed to vanilla's particle thread.
    /// </summary>
    public class HearthClient : IDisposable
    {
        /// <summary>Hearths further off than this aren't heard or smoking here (vanilla's particle and sound ranges are about this).</summary>
        public const double Range = 48;
        /// <summary>The smoke under the hood lives this long (vanilla's firepit smoke 18 s would rise through her decks): it fades in the hood.</summary>
        public const float HoodSmokeLife = 3.5f;
        /// <summary>How much of Sea's wind the stovepipe's smoke drifts with (vanilla's smoke: 0.7 of the wind).</summary>
        public const float SmokeWind = 0.7f;

        readonly ICoreClientAPI capi;
        readonly ShipClientSystem sys;
        bool disposed;

        /// <summary>One hearth for the particle thread: where its firepit's top is, how it moves, the stovepipe's mouth, the wind there.</summary>
        class Spawn
        {
            /// <summary>(Smoke: the oven burning, its smoke out of the same stovepipe; no flames of the firepit's.)</summary>
            public bool Lit, Wide, Embers, Smoke, SmokeInside;
            public double X, Y, Z, CX, CY, CZ;
            public float VX, VY, VZ, WX, WZ;
        }

        class Sounds
        {
            public ILoadedSound Crackle, Cooking;
            public double LastX = double.NaN, LastY, LastZ;
            public float VX, VY, VZ;
        }

        volatile Spawn[] spawns = Array.Empty<Spawn>();
        readonly Dictionary<long, Sounds> sounds = new();
        // the lit firepit's particles (embers, smoke, flames), its ring for a pot, the extinct one's, the stovepipe's smoke:
        // made on the main thread, used only on the particle thread
        volatile Props props;

        class Props
        {
            public AdvancedParticleProperties[] Lit, Ring, Extinct;
            public AdvancedParticleProperties Chimney, MouthSmoke;
        }

        public HearthClient(ICoreClientAPI capi, ShipClientSystem sys)
        {
            this.capi = capi;
            this.sys = sys;
            capi.Event.RegisterAsyncParticleSpawner(OnParticles);
        }

        /// <summary>Vanilla's lit and extinct firepit particles, cloned (the block's own stay as they are), the hood's smoke shortened, the stovepipe's made.</summary>
        Props MakeProps()
        {
            var lit = capi.World.GetBlock(new AssetLocation("game", "firepit-lit"));
            var extinct = capi.World.GetBlock(new AssetLocation("game", "firepit-extinct"));
            if (lit?.ParticleProperties == null || lit.ParticleProperties.Length == 0) return null;
            var p = new Props();
            var src = lit.ParticleProperties;
            p.Lit = new AdvancedParticleProperties[src.Length];
            for (int i = 0; i < src.Length; i++) p.Lit[i] = Inside(src[i].Clone());
            // vanilla's ring (BlockFirepit.OnLoaded): with a pot on it, each of them along the four edges round it, a quarter as many
            var boxes = new[]
            {
                new Cuboidf(0.125f, 0, 0.125f, 0.3125f, 0.5f, 0.875f), new Cuboidf(0.7125f, 0, 0.125f, 0.875f, 0.5f, 0.875f),
                new Cuboidf(0.125f, 0, 0.125f, 0.875f, 0.5f, 0.3125f), new Cuboidf(0.125f, 0, 0.7125f, 0.875f, 0.5f, 0.875f)
            };
            p.Ring = new AdvancedParticleProperties[src.Length * 4];
            for (int i = 0; i < src.Length; i++)
                for (int j = 0; j < 4; j++)
                {
                    var r = Inside(src[i].Clone());
                    var b = boxes[j];
                    // (offsets from the block's middle: the spawn position given is its top middle, see OnParticles)
                    r.PosOffset[0].avg = b.MidX - 0.5f;
                    r.PosOffset[0].var = b.Width / 2;
                    r.PosOffset[1].avg = 0.1f - 0.5f;
                    r.PosOffset[1].var = 0.05f;
                    r.PosOffset[2].avg = b.MidZ - 0.5f;
                    r.PosOffset[2].var = b.Length / 2;
                    r.Quantity.avg /= 4;
                    r.Quantity.var /= 4;
                    p.Ring[i * 4 + j] = r;
                }
            if (extinct?.ParticleProperties != null)
            {
                p.Extinct = new AdvancedParticleProperties[extinct.ParticleProperties.Length];
                for (int i = 0; i < p.Extinct.Length; i++) p.Extinct[i] = Inside(extinct.ParticleProperties[i].Clone());
            }
            // the stovepipe's smoke: vanilla's smoke quads (the lit firepit's second), coming out of the pipe's mouth
            var smoke = src[Math.Min(1, src.Length - 1)].Clone();
            smoke.WindAffectednes = 0;
            smoke.PosOffset[0].avg = 0;
            smoke.PosOffset[0].var = 0.1f;
            smoke.PosOffset[1].avg = 0;
            smoke.PosOffset[1].var = 0.05f;
            smoke.PosOffset[2].avg = 0;
            smoke.PosOffset[2].var = 0.1f;
            p.Chimney = smoke;
            p.MouthSmoke = Inside(smoke.Clone());
            return p;
        }

        /// <summary>Under the hood, out of the wind: no wind on them (they move with her), and the smoke fades before it rises through her deck.</summary>
        static AdvancedParticleProperties Inside(AdvancedParticleProperties p)
        {
            p.WindAffectednes = 0;
            if (p.LifeLength.avg > HoodSmokeLife)
            {
                p.LifeLength.avg = HoodSmokeLife;
                p.LifeLength.var = Math.Min(p.LifeLength.var, HoodSmokeLife / 4);
            }
            return p;
        }

        /// <summary>
        /// Every frame (main thread), as the renderer draws the ships: where each hearth that burns or smoulders is (its
        /// firepit's top middle and the stovepipe's mouth, in the world as drawn), how fast it moves (from frame to frame:
        /// her way, her turning, her rolling), the wind at the stovepipe; its crackle and a pot's bubbling started, moved
        /// with it and stopped.
        /// </summary>
        public void Update(EntityPlayer ep, ShipRenderer renderer, float dt)
        {
            if (disposed || ep == null) return;
            props ??= MakeProps();
            var list = new List<Spawn>();
            var seen = new HashSet<long>();
            var cam = ep.CameraPos;
            foreach (var ship in sys.Ships)
            {
                var f = ship.HearthFixture;
                if (f == null || ship.HearthInv == null || ship.Def == null) continue;
                bool lit = ship.HearthOn && ship.Fire.IsBurning;
                bool embers = !lit && ship.Fire.BurnState == "extinct";
                var o = renderer.RenderOrigin(ship, ep);
                ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, f.Pos[0], f.Pos[1] + 0.5, f.Pos[2], out double tx, out double ty, out double tz);
                tx += o.X; ty += o.Y; tz += o.Z;
                double dx = tx - cam.X, dy = ty - cam.Y, dz = tz - cam.Z;
                if (dx * dx + dy * dy + dz * dz > Range * Range || !lit && !embers)
                {
                    StopSounds(ship.EntityId);
                    continue;
                }
                seen.Add(ship.EntityId);
                if (!sounds.TryGetValue(ship.EntityId, out var snd)) sounds[ship.EntityId] = snd = new Sounds();
                // how the firepit moves, as drawn (eased over a few frames)
                if (!double.IsNaN(snd.LastX) && dt > 1e-4)
                {
                    float k = (float)(1 - Math.Exp(-dt * 8));
                    snd.VX += ((float)((tx - snd.LastX) / dt) - snd.VX) * k;
                    snd.VY += ((float)((ty - snd.LastY) / dt) - snd.VY) * k;
                    snd.VZ += ((float)((tz - snd.LastZ) / dt) - snd.VZ) * k;
                    // (a jump: a teleport, the seam)
                    if (snd.VX * snd.VX + snd.VY * snd.VY + snd.VZ * snd.VZ > 30 * 30) snd.VX = snd.VY = snd.VZ = 0;
                }
                snd.LastX = tx; snd.LastY = ty; snd.LastZ = tz;
                var s = new Spawn { Lit = lit, Embers = embers, Wide = HearthModel(ship) == "wide", X = tx, Y = ty, Z = tz, VX = snd.VX, VY = snd.VY, VZ = snd.VZ };
                if (lit && f.Chimney != null)
                {
                    ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, f.Chimney[0], f.Chimney[1], f.Chimney[2], out double cx, out double cy, out double cz);
                    s.CX = cx + o.X; s.CY = cy + o.Y; s.CZ = cz + o.Z;
                    var w = sys.Hansa?.WindAt(s.CX, s.CZ) ?? new Vec3d();
                    s.WX = (float)w.X * SmokeWind;
                    s.WZ = (float)w.Z * SmokeWind;
                }
                list.Add(s);
                // vanilla's crackle (BEBehaviorFirepitAmbient) while it burns, and a pot's bubbling (PotInFirepitRenderer)
                var at = new Vec3f((float)tx, (float)(ty - 0.25), (float)tz);
                if (lit)
                {
                    if (snd.Crackle == null)
                    {
                        snd.Crackle = capi.World.LoadSound(new SoundParams
                        {
                            Location = new AssetLocation("sounds/environment/fireplace.ogg"), ShouldLoop = true, Position = at, DisposeOnFinish = false, Volume = 0.66f
                        });
                        if (snd.Crackle != null)
                        {
                            snd.Crackle.Start();
                            snd.Crackle.PlaybackPosition = snd.Crackle.SoundLengthSeconds * (float)capi.World.Rand.NextDouble();
                        }
                    }
                    snd.Crackle?.SetPosition(at);
                }
                else Stop(ref snd.Crackle);
                float bubbling = 0;
                var input = ship.HearthInv[1].Itemstack;
                if (input?.Collectible is Vintagestory.GameContent.BlockCookingContainer or Vintagestory.GameContent.BlockCookedContainer)
                    bubbling = Math.Clamp((ship.HearthInputTemp() - 50f) / 50f, 0f, 1f);
                if (bubbling > 0)
                {
                    if (snd.Cooking == null)
                    {
                        snd.Cooking = capi.World.LoadSound(new SoundParams
                        {
                            Location = new AssetLocation("sounds/effect/cooking.ogg"), ShouldLoop = true, Position = at, DisposeOnFinish = false,
                            Range = 10f, ReferenceDistance = 3f, Volume = bubbling
                        });
                        snd.Cooking?.Start();
                    }
                    snd.Cooking?.SetVolume(bubbling);
                    snd.Cooking?.SetPosition(at);
                }
                else Stop(ref snd.Cooking);
            }
            foreach (long id in new List<long>(sounds.Keys)) if (!seen.Contains(id)) StopSounds(id);
            Ovens(ep, renderer, list);
            spawns = list.ToArray();
        }

        readonly Dictionary<long, ILoadedSound> ovenSounds = new();

        /// <summary>
        /// The ships' clay ovens while they burn: vanilla's oven's crackle (BlockEntityOven.ToggleAmbientSounds, the fireplace
        /// loop) at the oven, and smoke out of the stovepipe it shares with the hearth (if the hearth isn't smoking it already).
        /// </summary>
        void Ovens(EntityPlayer ep, ShipRenderer renderer, List<Spawn> list)
        {
            var seen = new HashSet<long>();
            var cam = ep.CameraPos;
            foreach (var ship in sys.Ships)
            {
                var f = ship.OvenFixture;
                if (f == null || ship.OvenInv == null || ship.Def == null || !ship.Oven.Burning || !ship.OvenOn) continue;
                var o = renderer.RenderOrigin(ship, ep);
                ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, f.Pos[0], f.Pos[1] + 0.1, f.Pos[2], out double tx, out double ty, out double tz);
                tx += o.X; ty += o.Y; tz += o.Z;
                double dx = tx - cam.X, dy = ty - cam.Y, dz = tz - cam.Z;
                if (dx * dx + dy * dy + dz * dz > Range * Range) continue;
                seen.Add(ship.EntityId);
                var at = new Vec3f((float)tx, (float)ty, (float)tz);
                if (!ovenSounds.TryGetValue(ship.EntityId, out var snd) || snd == null)
                {
                    snd = capi.World.LoadSound(new SoundParams
                    {
                        Location = new AssetLocation("sounds/environment/fireplace.ogg"), ShouldLoop = true, Position = at, DisposeOnFinish = false, Volume = 0.66f
                    });
                    snd?.Start();
                    ovenSounds[ship.EntityId] = snd;
                }
                snd?.SetPosition(at);
                // Smoke at the mouth without a flue; a shared chimney only needs one smoke stream.
                if (f.Chimney == null || !(ship.HearthOn && ship.Fire.IsBurning))
                {
                    var outlet = FirepitTarget.SmokeOutlet(f);
                    ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, outlet[0], outlet[1], outlet[2], out double cx, out double cy, out double cz);
                    var s = new Spawn { Smoke = true, SmokeInside = f.Chimney == null, X = tx, Y = ty, Z = tz, CX = cx + o.X, CY = cy + o.Y, CZ = cz + o.Z };
                    var w = sys.Hansa?.WindAt(s.CX, s.CZ) ?? new Vec3d();
                    s.WX = s.SmokeInside ? 0 : (float)w.X * SmokeWind;
                    s.WZ = s.SmokeInside ? 0 : (float)w.Z * SmokeWind;
                    list.Add(s);
                }
            }
            foreach (long id in new List<long>(ovenSounds.Keys))
            {
                if (seen.Contains(id)) continue;
                var s = ovenSounds[id];
                Stop(ref s);
                ovenSounds.Remove(id);
            }
        }

        /// <summary>How the firepit is drawn with what's in it: "normal", "spit" (a joint on the spit) or "wide" (a pot), as vanilla picks it.</summary>
        public static string HearthModel(EntityShip ship)
        {
            var inv = ship.HearthInv;
            var content = inv?[1].Itemstack ?? inv?[2].Itemstack;
            if (content == null) return "normal";
            if (content.Collectible is Vintagestory.GameContent.IInFirepitRendererSupplier) return "wide";
            var props = Vintagestory.GameContent.BlockEntityFirepit.GetRenderProps(content);
            return props?.UseFirepitModel.ToString().ToLowerInvariant() ?? "normal";
        }

        static void Stop(ref ILoadedSound s)
        {
            if (s == null) return;
            s.Stop();
            s.Dispose();
            s = null;
        }

        void StopSounds(long ship)
        {
            if (!sounds.TryGetValue(ship, out var snd)) return;
            Stop(ref snd.Crackle);
            Stop(ref snd.Cooking);
            sounds.Remove(ship);
        }

        /// <summary>
        /// The particle thread (vanilla's async spawners, every 33 ms, as a firepit block's particle tick): each burning
        /// hearth's flames, embers and smoke (in a ring round a pot), with her motion; the stovepipe's smoke with the wind;
        /// an extinct one's faint smoke.
        /// </summary>
        bool OnParticles(float dt, IAsyncParticleManager manager)
        {
            if (disposed) return false;
            var p = props;
            var list = spawns;
            if (p == null || list.Length == 0) return true;
            foreach (var s in list)
            {
                var set = s.Lit ? (s.Wide ? p.Ring : p.Lit) : s.Embers ? p.Extinct : null;
                if (set != null)
                {
                    foreach (var pp in set)
                    {
                        pp.WindAffectednesAtPos = 0;
                        pp.basePos.Set(s.X, s.Y, s.Z);
                        pp.baseVelocity.Set(s.VX, s.VY, s.VZ);
                        manager.Spawn(pp);
                    }
                }
                if ((s.Lit || s.Smoke) && p.Chimney != null && s.CY != 0)
                {
                    var smoke = s.SmokeInside ? p.MouthSmoke : p.Chimney;
                    smoke.WindAffectednesAtPos = 0;
                    smoke.basePos.Set(s.CX, s.CY, s.CZ);
                    smoke.baseVelocity.Set(s.WX, 0, s.WZ);
                    manager.Spawn(smoke);
                }
            }
            return true;
        }

        public void Dispose()
        {
            disposed = true;
            spawns = Array.Empty<Spawn>();
            foreach (long id in new List<long>(sounds.Keys)) StopSounds(id);
            foreach (long id in new List<long>(ovenSounds.Keys)) { var s = ovenSounds[id]; Stop(ref s); }
            ovenSounds.Clear();
        }
    }
}
