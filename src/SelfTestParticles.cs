using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// (22 October 2026, the caravel in game: water bubbles in her hold, and a broken block's bits floating there as under
        /// water) The game's own particle physics and particles, with ShipDryParticles on (as the client has them; here with
        /// this server's dry spaces): in her hold, below the water level outside, a light block's bits get no lift and a water
        /// speck dies at once while smoke lives on; just outside her side at the same depth the bits are lifted, the speck
        /// lives and the smoke dies.
        /// </summary>
        void DryParticleChecks(HansaMod mod, EntityShip ship, ShipDefinition def)
        {
            var h = new Harmony("hansa-verify-particles");
            var before = ShipDryParticles.Dry;
            try
            {
                string failed = null;
                ShipDryParticles.Patch(h, (what, e) => failed = (failed == null ? "" : failed + "; ") + what + ": " + e.Message);
                Check(failed == null, "the particle patches for her dry hold find what they patch" + (failed != null ? ": " + failed : ""));
                if (failed != null) return;
                ShipDryParticles.Dry = mod.Dry;
                var type = ShipDryParticles.ParticleType;
                var phys = new ParticlePhysics(sapi.World.BlockAccessor);

                ship.LocalToWorld(4.0, -1.5, 0.0, out double ix, out double iy, out double iz);
                ship.LocalToWorld(4.0, -1.5, def.WaterClip.HalfWidth + 0.5, out double ox, out double oy, out double oz);
                bool Water(double x, double y, double z) => sapi.World.BlockAccessor.GetBlockRaw((int)x, (int)(y + 0.15), (int)z, BlockLayersAccess.Fluid).IsLiquid();
                Check(Water(ix, iy, iz) && Water(ox, oy, oz) && mod.Dry.IsDry(ix, iy, iz) && !mod.Dry.IsDry(ox, oy, oz),
                    "in her hold the world has water and it's dry; just outside her side it's water");

                float Lift(double x, double y, double z)
                {
                    var v = new Vec3f();
                    phys.HandleBoyancy(new Vec3d(x, y, z), v, true, 1f, 0.033f, 0.1f);
                    return v.Y;
                }
                float li = Lift(ix, iy, iz), lo = Lift(ox, oy, oz);
                Check(li == 0 && lo > 0, $"a wooden block's broken bits: no lift in her hold ({li:0.000}), lifted just outside her ({lo:0.000})");

                bool Lives(double x, double y, double z, string dieIn)
                {
                    var p = (ParticleBase)Activator.CreateInstance(type);
                    Traverse.Create(p).Property(dieIn).SetValue(true);
                    Traverse.Create(p).Property("TerrainCollision").SetValue(false);
                    // (its own light: a dedicated server has no light to look up)
                    Traverse.Create(p).Field("LightEmission").SetValue(int.MaxValue);
                    p.LifeLength = 2;
                    p.Alive = true;
                    p.Position.Set(x, y, z);
                    // (dying in liquid is looked at every third tick)
                    for (int t = 0; t < 4 && p.Alive; t++) p.TickNow(0.033f, 0.033f, null, phys);
                    return p.Alive;
                }
                bool si = Lives(ix, iy, iz, "DieInAir"), so = Lives(ox, oy, oz, "DieInAir");
                Check(!si && so, $"a water speck dies in her hold ({!si}) and lives just outside her ({so})");
                bool mi = Lives(ix, iy, iz, "DieInLiquid"), mo = Lives(ox, oy, oz, "DieInLiquid");
                Check(mi && !mo, $"smoke lives in her hold ({mi}) and dies in the water just outside her ({!mo})");
            }
            catch (Exception e) { Check(false, "the particles in her dry hold were checked: " + e); }
            finally
            {
                ShipDryParticles.Dry = before;
                h.UnpatchAll(h.Id);
            }
        }
    }
}
