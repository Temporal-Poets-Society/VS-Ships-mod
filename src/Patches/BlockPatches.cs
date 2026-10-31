using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace Hansa
{
    /// <summary>
    /// (0.3.14) The patches that make vanilla work with blocks in a ship's mini-dimension (ShipBlocksSystem). A block there
    /// sits at (sub-dimension origin + her cell) in dimension 1, far from where she is; each patch takes such a position to
    /// her (ShipBlocksSystem.ToWorld) or her frame:
    /// 1. the reach check (ServerPlayer.isInInteractionRangeOf for a block, client and server): to the block where she has it;
    /// 2. block entity packets sent with a plain Y (the barrel's, firepit's and quern's dialogs): the dimension put back in;
    /// 3. climate, wind and the rain map at such a block: hers where she is (rain: open sky over it aboard, or her decks);
    /// 4. sounds, particles and dropped items there: where she has it (drops into the inventory of the nearest player aboard);
    /// 5. block entity renderers that draw at the block's position themselves (the forge's coals and glowing work piece,
    ///    the anvil's work piece, the quern's stone, the bloomery's glow, animated blocks: the bellows): drawn with the
    ///    camera put into her frame for the call (its position in the dimension, the view turned with her), so the
    ///    renderer's own arithmetic lands it where she has it.
    /// Each is put on on its own; one that fails is logged and that part doesn't work.
    /// </summary>
    public static class BlockPatches
    {
        static ILogger log;

        static void Try(string what, Action patch)
        {
            try { patch(); }
            catch (Exception e) { log?.Warning("[ships] Could not patch ({0}) for working blocks aboard, that part won't work: {1}", what, e.Message); }
        }

        /// <summary>What's patched (for the self-test).</summary>
        public static readonly List<string> Applied = new();

        // ---------------------------------------------------------------- shared (client and server)

        public static void ApplyShared(Harmony h, ILogger logger)
        {
            log ??= logger;
            Try("reach to a block aboard", () =>
            {
                var sp = AccessTools.TypeByName("Vintagestory.Server.ServerPlayer") ?? throw new Exception("ServerPlayer not found");
                var m = AccessTools.Method(sp, "isInInteractionRangeOf", new[] { typeof(IPlayer), typeof(IBlockAccessor), typeof(BlockPos), typeof(float) })
                        ?? throw new Exception("isInInteractionRangeOf(block) not found");
                h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(RangePrefix)));
                Applied.Add("range");
            });
            Try("climate aboard", () =>
            {
                int n = 0;
                foreach (var t in new[] { typeof(BlockAccessorBase), AccessTools.TypeByName("Vintagestory.Common.BlockAccessorReadLockfree") })
                {
                    if (t == null) continue;
                    foreach (var m in AccessTools.GetDeclaredMethods(t))
                    {
                        var ps = m.GetParameters();
                        if (ps.Length == 0) continue;
                        if ((m.Name == "GetClimateAt" || m.Name == "GetWindSpeedAt") && ps[0].ParameterType == typeof(BlockPos) && ps[0].Name == "pos")
                        { h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(ClimatePrefix))); n++; }
                        else if (m.Name == "GetWindSpeedAt" && ps[0].ParameterType == typeof(Vec3d) && ps[0].Name == "pos")
                        { h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(WindVecPrefix))); n++; }
                        else if (m.Name == "GetRainMapHeightAt" && ps.Length == 1 && ps[0].ParameterType == typeof(BlockPos))
                        { h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(RainMapPrefix))); n++; }
                    }
                }
                if (n == 0) throw new Exception("no climate lookups found");
                Applied.Add("climate:" + n);
            });
        }

        static readonly AccessTools.FieldRef<BlockAccessorBase, IWorldAccessor> worldOf = TryRef();

        static AccessTools.FieldRef<BlockAccessorBase, IWorldAccessor> TryRef()
        {
            try { return AccessTools.FieldRefAccess<BlockAccessorBase, IWorldAccessor>("worldAccessor"); }
            catch { return null; }
        }

        static bool IsClient(object accessor) =>
            accessor is BlockAccessorBase b && worldOf != null ? worldOf(b)?.Side == EnumAppSide.Client : false;

        /// <summary>Patch 1: the reach to a block in her dimension, measured to it where she has it (her frame), with a little for the lag.</summary>
        static bool RangePrefix(IPlayer __0, IBlockAccessor __1, BlockPos __2, float __3, ref bool __result)
        {
            if (__2 == null || __2.dimension != Dimensions.MiniDimensions || __0?.Entity == null) return true;
            bool client = __0.Entity.Api?.Side == EnumAppSide.Client;
            int sub = ShipBlockMath.SubOf(__2.X, __2.Z);
            var ship = ShipBlocksSystem.ShipForSub(sub, client);
            var frac = ship?.GridFrac;
            if (frac == null) return true;
            // (review H1-11) her working blocks are her crew's, as her chests are (ShipCrew); decided by the server, which
            // knows her admins too (review H2-8)
            if (!client && !ship.MayUse(__0)) { __result = false; return false; }
            var ep = __0.Entity;
            var eye = ship.WorldToLocal(ep.Pos.X + ep.LocalEyePos.X, ep.Pos.Y + ep.LocalEyePos.Y, ep.Pos.Z + ep.LocalEyePos.Z);
            ShipBlockMath.ToLocal(sub, frac[0], frac[1], frac[2], __2.X, __2.Y, __2.Z, out double cx, out double cy, out double cz);
            Cuboidf[] boxes = null;
            try { boxes = __1?.GetBlock(__2)?.GetSelectionBoxes(__1, __2); } catch { }
            double best = double.MaxValue;
            if (boxes == null || boxes.Length == 0) boxes = new[] { new Cuboidf(0, 0, 0, 1, 1, 1) };
            foreach (var b in boxes)
            {
                double nx = Math.Clamp(eye.X, cx + b.X1, cx + b.X2), ny = Math.Clamp(eye.Y, cy + b.Y1, cy + b.Y2), nz = Math.Clamp(eye.Z, cz + b.Z1, cz + b.Z2);
                best = Math.Min(best, Math.Sqrt((nx - eye.X) * (nx - eye.X) + (ny - eye.Y) * (ny - eye.Y) + (nz - eye.Z) * (nz - eye.Z)));
            }
            // (the server's pose of her lags the client's a little, and she rolls on clients only)
            float slack = client ? 0.25f : 1.25f;
            __result = best <= __0.WorldData.PickingRange + __3 + slack;
            return false;
        }

        /// <summary>Patch 3: climate and wind at a block in her dimension: where she is.</summary>
        static void ClimatePrefix(object __instance, ref BlockPos pos)
        {
            if (pos == null || pos.dimension != Dimensions.MiniDimensions) return;
            if (ShipBlocksSystem.ToWorld(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, IsClient(__instance), out double x, out double y, out double z))
                pos = new BlockPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z), Dimensions.NormalWorld);
        }

        static void WindVecPrefix(object __instance, ref Vec3d pos)
        {
            if (pos == null || !ShipBlockMath.IsDimY(pos.Y)) return;
            if (ShipBlocksSystem.ToWorld(pos.X, pos.Y, pos.Z, IsClient(__instance), out double x, out double y, out double z)) pos = new Vec3d(x, y, z);
        }

        /// <summary>
        /// Patch 3: the rain map over a block in her dimension: rained on (the map at or below it) under the open sky, dry
        /// (the map above it) under her decks, as ShipShelter decides for people aboard.
        /// </summary>
        static bool RainMapPrefix(object __instance, BlockPos pos, ref int __result)
        {
            if (pos == null || pos.dimension != Dimensions.MiniDimensions) return true;
            if (!ShipBlocksSystem.ToWorld(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, IsClient(__instance), out _, out _, out _, out var ship, out double lx, out double ly, out double lz)) return true;
            bool covered = false;
            try { covered = ShipShelter.CoveredAt(ship, lx, ly + 0.45, lz); } catch { }
            __result = covered ? pos.Y + 2 : pos.Y;
            return false;
        }

        // ---------------------------------------------------------------- server

        public static void ApplyServer(Harmony h, ILogger logger)
        {
            log ??= logger;
            var sm = AccessTools.TypeByName("Vintagestory.Server.ServerMain");
            Try("drops aboard", () =>
            {
                var m = AccessTools.Method(sm, "SpawnItemEntity", new[] { typeof(ItemStack), typeof(Vec3d), typeof(Vec3d) }) ?? throw new Exception("SpawnItemEntity not found");
                h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(DropPrefix)));
                Applied.Add("drops");
            });
            Try("sounds aboard (server)", () =>
            {
                var m = AccessTools.Method(sm, "PlaySoundAtExceptPlayer") ?? throw new Exception("PlaySoundAtExceptPlayer not found");
                h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(SoundPrefixServer)));
                Applied.Add("sounds-server");
            });
            Try("particles aboard (server)", () =>
            {
                var m = AccessTools.Method(sm, "SpawnParticles", new[] { typeof(IParticlePropertiesProvider), typeof(IPlayer) }) ?? throw new Exception("SpawnParticles(provider) not found");
                h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(ParticlesPrefixServer)), finalizer: new HarmonyMethod(typeof(BlockPatches), nameof(ParticlesFinalizer)));
                Applied.Add("particles-server");
            });
        }

        /// <summary>
        /// Patch 4: an item dropped at a block in her dimension (a working block broken, its contents, what doesn't fit in a
        /// hand): into the inventory of the nearest player aboard her (within 8 blocks), else where she has the block.
        /// </summary>
        static bool DropPrefix(object __instance, ItemStack __0, ref Vec3d __1, ref Entity __result)
        {
            if (__0 == null || __1 == null || !ShipBlockMath.IsDimY(__1.Y)) return true;
            if (!ShipBlocksSystem.ToWorld(__1.X, __1.Y, __1.Z, false, out double x, out double y, out double z, out var ship, out _, out _, out _)) return true;
            var world = __instance as IWorldAccessor;
            IPlayer best = null;
            double bd = 8 * 8;
            if (world != null)
                foreach (var pl in world.AllOnlinePlayers)
                {
                    var e = pl.Entity;
                    if (e == null || !(e.MountedOn is ShipSeat seat && seat.Ship == ship)) continue;
                    double d = e.Pos.SquareDistanceTo(x, y, z);
                    if (d < bd) { bd = d; best = pl; }
                }
            if (best != null)
            {
                var copy = __0.Clone();
                if (best.InventoryManager.TryGiveItemstack(copy, true) && copy.StackSize <= 0) { __result = null; return false; }
                __0.StackSize = copy.StackSize > 0 ? copy.StackSize : __0.StackSize;
            }
            __1 = new Vec3d(x, y + 0.25, z);
            return true;
        }

        /// <summary>Patch 4: a sound at a block in her dimension: heard where she has it (server: sent to the players near that).</summary>
        static void SoundPrefixServer(ref double __1, ref double __2, ref double __3)
        {
            if (!ShipBlockMath.IsDimY(__2)) return;
            if (ShipBlocksSystem.ToWorld(__1, __2, __3, false, out double x, out double y, out double z)) { __1 = x; __2 = y; __3 = z; }
        }

        [ThreadStatic] static int shifting;

        /// <summary>Patch 4: particles at a block in her dimension: where she has it (the provider moved for the call and back).</summary>
        static void ParticlesPrefixServer(IParticlePropertiesProvider __0, out Vec3d __state) => __state = Shift(__0, false);
        static void ParticlesPrefixClient(IParticlePropertiesProvider __0, out Vec3d __state) => __state = Shift(__0, true);

        static Exception ParticlesFinalizer(Exception __exception, IParticlePropertiesProvider __0, Vec3d __state)
        {
            if (__state != null) { Move(__0, -__state.X, -__state.Y, -__state.Z); shifting--; }
            return __exception;
        }

        static Vec3d Shift(IParticlePropertiesProvider p, bool client)
        {
            if (p == null || shifting > 0) return null;
            Vec3d at;
            try { at = p.Pos; } catch { return null; }
            if (at == null || !ShipBlockMath.IsDimY(at.Y)) return null;
            if (!ShipBlocksSystem.ToWorld(at.X, at.Y, at.Z, client, out double x, out double y, out double z)) return null;
            var d = new Vec3d(x - at.X, y - at.Y, z - at.Z);
            if (!Move(p, d.X, d.Y, d.Z)) return null;
            shifting++;
            return d;
        }

        static bool Move(IParticlePropertiesProvider p, double dx, double dy, double dz)
        {
            switch (p)
            {
                case SimpleParticleProperties s when s.MinPos != null: s.MinPos.Add(dx, dy, dz); return true;
                case AdvancedParticleProperties a when a.basePos != null: a.basePos.Add(dx, dy, dz); return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- client

        public static void ApplyClient(Harmony h, ILogger logger)
        {
            log ??= logger;
            var cm = AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientMain");
            Try("block entity packets aboard", () =>
            {
                var m = AccessTools.Method(cm, "SendBlockEntityPacket", new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(byte[]) })
                        ?? throw new Exception("SendBlockEntityPacket not found");
                h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(BePacketPrefix)));
                Applied.Add("bepackets");
            });
            Try("sounds aboard (client)", () =>
            {
                var m = AccessTools.Method(cm, "PlaySoundAtInternal") ?? throw new Exception("PlaySoundAtInternal not found");
                h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(SoundPrefixClient)));
                Applied.Add("sounds-client");
            });
            Try("particles aboard (client)", () =>
            {
                int n = 0;
                var pm = AccessTools.TypeByName("Vintagestory.Client.NoObf.ParticleManager");
                var rp = AccessTools.TypeByName("Vintagestory.Client.NoObf.SystemRenderParticles");
                foreach (var m in new[] {
                    pm == null ? null : AccessTools.Method(pm, "SpawnParticles", new[] { typeof(IParticlePropertiesProvider) }),
                    pm == null ? null : AccessTools.Method(pm, "SpawnParticlesOffThread", new[] { typeof(IParticlePropertiesProvider) }),
                    rp == null ? null : AccessTools.Method(rp, "Spawn", new[] { typeof(IParticlePropertiesProvider) }) })
                {
                    if (m == null) continue;
                    h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(ParticlesPrefixClientGuarded)), finalizer: new HarmonyMethod(typeof(BlockPatches), nameof(ParticlesFinalizer)));
                    n++;
                }
                if (n == 0) throw new Exception("particle spawners not found");
                Applied.Add("particles-client:" + n);
            });
            Try("block entity renderers aboard", () =>
            {
                int n = 0;
                foreach (var (name, asm) in RendererTypes)
                {
                    var t = AccessTools.TypeByName(name);
                    if (t == null) continue;
                    var pf = AccessTools.Field(t, "pos");
                    var m = AccessTools.Method(t, "OnRenderFrame", new[] { typeof(float), typeof(EnumRenderStage) });
                    if (pf == null || m == null || m.IsAbstract || !(pf.FieldType == typeof(BlockPos) || pf.FieldType == typeof(Vec3d))) continue;
                    posFields[t] = pf;
                    h.Patch(m, prefix: new HarmonyMethod(typeof(BlockPatches), nameof(RenderPrefix)), finalizer: new HarmonyMethod(typeof(BlockPatches), nameof(RenderFinalizer)));
                    n++;
                }
                if (n == 0) throw new Exception("no renderers found");
                Applied.Add("renderers:" + n);
                RenderersPatched = n;
            });
        }

        /// <summary>
        /// Patch 4 (client): a particle provider still to be queued for the particle thread (an async one from the main
        /// thread) is moved when the thread spawns it (SystemRenderParticles.Spawn), not now.
        /// </summary>
        static void ParticlesPrefixClientGuarded(object __instance, IParticlePropertiesProvider __0, out Vec3d __state)
        {
            __state = null;
            if (__0 == null) return;
            if (__instance.GetType().Name == "ParticleManager" && __0.Async && Environment.CurrentManagedThreadId == RuntimeEnv.MainThreadId) return;
            __state = Shift(__0, true);
        }

        /// <summary>Patch 2: a block entity packet for a block in a ship's dimension sent with its plain Y (the barrel's, firepit's, quern's dialogs): with the dimension's.</summary>
        static void BePacketPrefix(int __0, ref int __1, int __2)
        {
            if (__1 >= ShipBlockMath.MinDimY && __1 < ShipBlockMath.DimSizeY && ShipBlocksSystem.ShipForSub(ShipBlockMath.SubOf(__0, __2), true) != null)
                __1 += ShipBlockMath.DimSizeY;
        }

        static void SoundPrefixClient(ref double __1, ref double __2, ref double __3)
        {
            if (!ShipBlockMath.IsDimY(__2)) return;
            if (ShipBlocksSystem.ToWorld(__1, __2, __3, true, out double x, out double y, out double z)) { __1 = x; __2 = y; __3 = z; }
        }

        /// <summary>Renderers that draw at their block's position themselves (field "pos"): the ones a working block can bring along.</summary>
        static readonly (string, string)[] RendererTypes =
        {
            ("Vintagestory.GameContent.ForgeContentsRenderer", "surv"),
            ("Vintagestory.GameContent.AnvilWorkItemRenderer", "surv"),
            ("Vintagestory.GameContent.QuernTopRenderer", "surv"),
            ("Vintagestory.GameContent.BloomeryContentsRenderer", "surv"),
            ("Vintagestory.GameContent.FirepitContentsRenderer", "surv"),
            ("Vintagestory.GameContent.PotInFirepitRenderer", "surv"),
            ("Vintagestory.GameContent.IngotMoldRenderer", "surv"),
            ("Vintagestory.GameContent.ToolMoldRenderer", "surv"),
            ("Vintagestory.GameContent.BlockEntitySignRenderer", "surv"),
            ("Vintagestory.GameContent.FruitpressContentsRenderer", "surv"),
            ("Vintagestory.GameContent.HelveHammerRenderer", "surv"),
            ("Vintagestory.API.Common.AnimatableRenderer", "api"),
        };

        static readonly Dictionary<Type, FieldInfo> posFields = new();
        public static int RenderersPatched;

        class RenderSwap
        {
            public Vec3d Cam;
            public double CamX, CamY, CamZ;
            public float[] Viewf, SavedViewf;
            public double[] View, SavedView;
        }

        static ICoreClientAPI Capi => ShipBlocksSystem.ClientInstance?.ClientApi;

        /// <summary>
        /// Patch 5: a renderer of a block in a ship's dimension draws with the camera in her frame for the call: the camera's
        /// position put into the dimension (as ship-local), the view turned with her. Its own (pos − camera) translation then
        /// lands the mesh where she has the block. In the shadow passes (which take the model-view matrix) it isn't drawn.
        /// </summary>
        static bool RenderPrefix(object __instance, EnumRenderStage stage, out object __state)
        {
            __state = null;
            try
            {
                var capi = Capi;
                if (capi == null || !posFields.TryGetValue(__instance.GetType(), out var f) && !TryBaseField(__instance.GetType(), out f)) return true;
                double px, py, pz;
                switch (f.GetValue(__instance))
                {
                    case BlockPos bp: if (bp.dimension != Dimensions.MiniDimensions) return true; px = bp.X; py = bp.Y; pz = bp.Z; break;
                    case Vec3d v: px = v.X; py = v.Y; pz = v.Z; break;
                    default: return true;
                }
                if (!ShipBlockMath.IsDimY(py)) return true;
                int sub = ShipBlockMath.SubOf((int)Math.Floor(px), (int)Math.Floor(pz));
                var ship = ShipBlocksSystem.ShipForSub(sub, true);
                var frac = ship?.GridFrac;
                if (frac == null || !ship.Alive) return false;      // (no ship to draw it in: not drawn)
                if (stage is EnumRenderStage.ShadowFar or EnumRenderStage.ShadowNear) return false;
                var ep = capi.World.Player?.Entity;
                if (ep == null) return true;
                var rot = ShipDimClient.Rotation(ship);
                var o = ShipDimClient.Origin(ship, ep);
                var cam = ep.CameraPos;
                // the camera ship-local: her turn undone (R is a rotation: its inverse its transpose)
                double rx = cam.X - o.X, ry = cam.Y - o.Y, rz = cam.Z - o.Z;
                double lx = rot[0] * rx + rot[1] * ry + rot[2] * rz;
                double ly = rot[4] * rx + rot[5] * ry + rot[6] * rz;
                double lz = rot[8] * rx + rot[9] * ry + rot[10] * rz;
                ShipBlockMath.FromLocal(sub, frac[0], frac[1], frac[2], lx, ly, lz, py >= ShipBlockMath.DimSizeY, out double fx, out double fy, out double fz);
                var s = new RenderSwap { Cam = cam, CamX = cam.X, CamY = cam.Y, CamZ = cam.Z };
                s.Viewf = capi.Render.CameraMatrixOriginf;
                s.View = capi.Render.CameraMatrixOrigin;
                if (s.Viewf != null) { s.SavedViewf = (float[])s.Viewf.Clone(); Mat4f.Mul(s.Viewf, s.SavedViewf, rot); }
                if (s.View != null)
                {
                    s.SavedView = (double[])s.View.Clone();
                    var rd = new double[16];
                    for (int i = 0; i < 16; i++) rd[i] = rot[i];
                    Mat4d.Mul(s.View, s.SavedView, rd);
                }
                cam.Set(fx, fy, fz);
                __state = s;
            }
            catch (Exception e)
            {
                if (!renderWarned) { renderWarned = true; log?.Warning("[ships] A working block's renderer couldn't be drawn aboard: {0}", e.Message); }
            }
            return true;
        }

        static bool renderWarned;

        static bool TryBaseField(Type t, out FieldInfo f)
        {
            for (var b = t.BaseType; b != null; b = b.BaseType)
                if (posFields.TryGetValue(b, out f)) { posFields[t] = f; return true; }
            f = null;
            return false;
        }

        static Exception RenderFinalizer(Exception __exception, object __state)
        {
            if (__state is RenderSwap s)
            {
                s.Cam.Set(s.CamX, s.CamY, s.CamZ);
                if (s.SavedViewf != null) Array.Copy(s.SavedViewf, s.Viewf, 16);
                if (s.SavedView != null) Array.Copy(s.SavedView, s.View, 16);
            }
            return __exception;
        }
    }
}
