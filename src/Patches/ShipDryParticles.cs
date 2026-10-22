using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (22 October 2026, the caravel in game: "in hull underwater still can see some water air bubbles. Also when break
    /// blocks in hull under water then animation of particles floating are like under water") Particles in a ship's dry
    /// space behave as in air, though the world has water there (the ship is an entity, not blocks). Vanilla's particle
    /// physics asks the world's fluid block at each particle as it moves:
    /// <list type="bullet">
    /// <item>buoyancy (<c>ParticlePhysics.HandleBoyancy</c>): a light block's breaking bits (wood, under 1000 density) and
    /// its puff of cubes swim up in water; in her dry hold they fall as in air (not pushed by flowing water either);</item>
    /// <item>dying (<c>ParticleGeneric.TickNow</c>): the water specks die in air, smoke, sparks and motes in liquid. A speck
    /// in her hold dies as soon as it's there: Sea leaves out those it can see would be (its IsDryWithin, at spawning),
    /// and this catches the rest (those that drift in, or that her roll sweeps in faster than Sea judged); smoke and
    /// motes there live on.</item>
    /// </list>
    /// Client only (the particles are the client's); <see cref="Dry"/> is the client's dry spaces.
    /// </summary>
    public static class ShipDryParticles
    {
        /// <summary>The client's ships' dry spaces (asked from the particle thread); null: particles are left to vanilla.</summary>
        public static volatile ShipDrySpaces Dry;

        static Block air;

        /// <summary>Is a particle at this world place in a ship's dry space?</summary>
        public static bool DryAt(double x, double y, double z)
        {
            var dry = Dry;
            if (dry == null || !dry.Any || y >= BlockPos.DimensionBoundary) return false;
            try { return dry.IsDry(x, y, z); }
            catch (Exception) { return false; }
        }

        /// <summary>The particle physics' buoyancy and push of flowing water: none where the water is a ship's dry space.</summary>
        static bool BuoyancyPrefix(ParticlePhysics __instance, Vec3d pos)
        {
            var dry = Dry;
            if (dry == null || !dry.Any || pos == null) return true;
            var b = __instance.BlockAccess?.GetBlockRaw((int)pos.X, (int)pos.Y, (int)pos.Z, BlockLayersAccess.Fluid);
            return b == null || !b.IsLiquid() || !DryAt(pos.X, pos.Y, pos.Z);
        }

        /// <summary>
        /// For a particle's checks whether it's in liquid (ParticleGeneric.TickNow: to die in air or in liquid): the fluid
        /// block there, air where it's a ship's dry space (the particle's own place: it's judged where it is in her, not by
        /// the world's block grid).
        /// </summary>
        public static Block FluidAt(IBlockAccessor ba, int x, int y, int z, int layer, ParticleBase p)
        {
            var b = ba.GetBlockRaw(x, y, z, layer);
            if (b == null || !b.IsLiquid() || p?.Position == null) return b;
            if (!DryAt(p.Position.X, p.Position.Y + 0.15, p.Position.Z)) return b;
            return air ??= ba.GetBlock(0);
        }

        static readonly MethodInfo getBlockRaw = AccessTools.Method(typeof(IBlockAccessor), nameof(IBlockAccessor.GetBlockRaw),
            new[] { typeof(int), typeof(int), typeof(int), typeof(int) });

        /// <summary>Calls replaced by the last transpiler run (for the patch and the self-test).</summary>
        public static int LastReplaced;

        /// <summary>Every BlockAccess.GetBlockRaw(x, y, z, layer) in the method becomes <see cref="FluidAt"/>(…, this).</summary>
        static IEnumerable<CodeInstruction> TickTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int n = 0;
            var with = AccessTools.Method(typeof(ShipDryParticles), nameof(FluidAt));
            foreach (var ci in instructions)
            {
                if (ci.Calls(getBlockRaw))
                {
                    n++;
                    // (the stack: accessor, x, y, z, layer; the particle added, the same block out)
                    yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(ci.labels).WithBlocks(ci.blocks);
                    yield return new CodeInstruction(OpCodes.Call, with);
                    continue;
                }
                yield return ci;
            }
            LastReplaced = n;
        }

        /// <summary>The particle classes whose ticks check the fluid block they're in.</summary>
        public static Type ParticleType => AccessTools.TypeByName("Vintagestory.Client.NoObf.ParticleGeneric");

        /// <summary>Puts the patches on (client; the self-test with its own Harmony). Throws if one doesn't find what it patches.</summary>
        public static void Patch(Harmony h, Action<string, Exception> failed)
        {
            try
            {
                var m = AccessTools.Method(typeof(ParticlePhysics), nameof(ParticlePhysics.HandleBoyancy)) ?? throw new Exception("HandleBoyancy not found");
                h.Patch(m, prefix: new HarmonyMethod(typeof(ShipDryParticles), nameof(BuoyancyPrefix)));
            }
            catch (Exception e) { failed("particles fall as in air in a dry hold", e); }
            try
            {
                var t = ParticleType ?? throw new Exception("ParticleGeneric not found");
                var m = AccessTools.Method(t, "TickNow") ?? throw new Exception("ParticleGeneric.TickNow not found");
                int calls = 0;
                foreach (var ci in PatchProcessor.GetOriginalInstructions(m)) if (ci.Calls(getBlockRaw)) calls++;
                if (calls == 0) throw new Exception("no fluid check found in ParticleGeneric.TickNow");
                h.Patch(m, transpiler: new HarmonyMethod(typeof(ShipDryParticles), nameof(TickTranspiler)));
            }
            catch (Exception e) { failed("water specks die and smoke lives in a dry hold", e); }
        }
    }
}
