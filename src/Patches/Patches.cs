using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>Is someone inside an enclosed ship cabin, and how warm is it there.</summary>
    public static class ShipShelter
    {
        /// <summary>
        /// The local client player is in one of her rooms (a cabin, the lower deck, the hold) with her structure overhead:
        /// no rain, hail or snow drawn, the rain muffled (read by the weather particle thread). (0.3.13, the user: "rain is
        /// in the lower deck": it was set while the room was sealed, so an open waist hatch or companion door let the rain
        /// fall all over the lower deck; now it's what's overhead, <see cref="Covered"/>.)
        /// </summary>
        public static volatile bool LocalSheltered;
        /// <summary>
        /// (0.3.13) In one of her rooms with open sky overhead (under the open waist hatch): the weather is drawn only
        /// through the opening, this world box (x0, z0, x1, z1) round the player's head; null otherwise.
        /// </summary>
        public static volatile double[] LocalOpening;
        /// <summary>(0.3.13) The world height of the local player's head while <see cref="LocalOpening"/> is set.</summary>
        public static double LocalHeadY;

        public static bool CoveredAt(EntityShip ship, double x, double y, double z) =>
            ship?.Grid != null && ShipCover.Above(ship.Grid, x, y, z, ship.WorldDirToLocal(0, 1, 0)) < double.MaxValue;

        /// <summary>A creature's head over its feet (a player's eyes), for <see cref="Covered"/>.</summary>
        static double HeadHeight(Entity e) => Math.Clamp(e?.Properties?.EyeHeight ?? 1.6, 0.4, 2.0);

        /// <summary>
        /// (0.3.13) Someone aboard with something of hers overhead: rain doesn't wet them, hail doesn't hurt them, and in
        /// her rooms no rain falls and it's muffled. Under the open waist hatch the rain comes in; anywhere else below decks
        /// never (warmth is still the sealed room's: <see cref="IsSheltered"/>).
        /// </summary>
        public static bool Covered(Entity e)
        {
            if (e is not EntityAgent { MountedOn: ShipSeat seat } || seat.Ship?.Def == null) return false;
            var l = seat.LocalPos;
            return CoveredAt(seat.Ship, l.X, l.Y + HeadHeight(e), l.Z);
        }

        /// <summary>In one of her rooms (a cabin, the lower deck, the hold), by the head.</summary>
        public static bool InRoom(Entity e) =>
            e is EntityAgent { MountedOn: ShipSeat seat } && seat.Ship?.Def != null &&
            seat.Ship.Def.RoomAt(seat.LocalPos.X, seat.LocalPos.Y + HeadHeight(e), seat.LocalPos.Z) != null;

        /// <summary>
        /// (0.3.13) The open sky over someone in one of her rooms (under the open hatch): the world box (x0, z0, x1, z1) of
        /// the spots round their head, half a block apart out to 1.5, with nothing of hers above; null if there's none.
        /// </summary>
        public static double[] OpeningOver(Entity e)
        {
            if (e is not EntityAgent { MountedOn: ShipSeat seat } || seat.Ship?.Grid == null) return null;
            var ship = seat.Ship;
            var l = seat.LocalPos;
            double hy = l.Y + HeadHeight(e);
            var up = ship.WorldDirToLocal(0, 1, 0);
            double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
            for (int a = -3; a <= 3; a++)
                for (int b = -3; b <= 3; b++)
                {
                    double lx = l.X + a * 0.5, lz = l.Z + b * 0.5;
                    if (ShipCover.Above(ship.Grid, lx, hy, lz, up) < double.MaxValue) continue;
                    ship.LocalToWorld(lx, hy, lz, out double wx, out _, out double wz);
                    x0 = Math.Min(x0, wx); z0 = Math.Min(z0, wz); x1 = Math.Max(x1, wx); z1 = Math.Max(z1, wz);
                }
            if (x0 > x1) return null;
            return new[] { x0 - 0.25, z0 - 0.25, x1 + 0.25, z1 + 0.25 };
        }

        /// <summary>The world height of someone's head (for <see cref="LocalHeadY"/>).</summary>
        public static double HeadWorldY(Entity e) => e.Pos.Y + HeadHeight(e);

        public static RoomDef Room(Entity e)
        {
            if (e is EntityAgent ea && ea.MountedOn is ShipSeat seat && seat.Ship.Def != null)
            {
                return seat.Ship.RoomAtLocal(seat.LocalPos);
            }
            return null;
        }

        /// <summary>In a closed room aboard (a cabin, or the lower deck while the waist hatch is shut).</summary>
        public static bool IsSheltered(Entity e)
        {
            var r = Room(e);
            if (r == null || e is not EntityAgent ea || ea.MountedOn is not ShipSeat seat) return false;
            // (a design room, or a space partitions and shut doors close off in one that's open: notes 78, 98)
            var l = seat.LocalPos;
            return seat.Ship.SealedAt(l.X, l.Y + 0.5, l.Z);
        }

        /// <summary>Heat from lit stoves in the same cabin, using vanilla's distance falloff.</summary>
        public static float StoveHeat(Entity e, bool enclosed)
        {
            if (!(e is EntityAgent ea) || !(ea.MountedOn is ShipSeat seat) || seat.Ship.Def == null) return 0;
            var room = seat.Ship.RoomAtLocal(seat.LocalPos);
            if (room == null) return 0;
            var l = seat.LocalPos;
            float heat = 0;
            foreach (var f in seat.Ship.Def.Fixtures)
            {
                if (f.Type != "stove" || f.Room != room.Code || !seat.Ship.LitStoves.Contains(f.Code)) continue;
                double dx = f.Pos[0] - l.X, dy = f.Pos[1] - (l.Y + 0.9), dz = f.Pos[2] - l.Z;
                double distSq = dx * dx + dy * dy + dz * dz;
                double power = enclosed ? 0.875 : 1.25;
                heat += f.Heat * Math.Min(1f, 9f / (8f + (float)Math.Pow(distSq, power)));
            }
            return heat;
        }

        /// <summary>
        /// Heat from the ship's firehearth while it burns (or smoulders), as from vanilla's firepit: its strength (10, or
        /// 0.25 for embers) by vanilla's falloff from its block's nearer edge, for whoever is in its room while that's a
        /// closed room, or else within vanilla's 3 blocks of it (through the bulkhead, the deck above).
        /// </summary>
        public static float HearthHeat(Entity e, bool enclosed)
        {
            if (e is not EntityAgent { MountedOn: ShipSeat seat } || seat.Ship?.HearthFixture is not FixtureDef f || !seat.Ship.HearthOn) return 0;
            var ship = seat.Ship;
            float strength = ship.Fire.HeatStrength;
            if (strength <= 0) return 0;
            var l = seat.LocalPos;
            double px = l.X, py = l.Y + 0.9, pz = l.Z;
            bool near = enclosed
                ? ship.RoomAtLocal(l)?.Code == f.Room
                : Math.Abs(px - f.Pos[0]) <= 3.5 && Math.Abs(l.Y - f.Pos[1]) <= 3.5 && Math.Abs(pz - f.Pos[2]) <= 3.5;
            if (!near) return 0;
            // (the firepit's block: its bottom centre at Pos)
            double dsq = HearthFire.DistanceSqToNearerEdge(f.Pos[0] - 0.5, f.Pos[1], f.Pos[2] - 0.5, px, py, pz);
            return HearthFire.Warmth(strength, dsq, enclosed);
        }

        /// <summary>Replaces the rain-exposure height lookup inside body temperature: under her decks it's dry (0.3.13: by what's overhead, not the room's sealing).</summary>
        public static int RainMapHeight(IBlockAccessor ba, BlockPos pos, EntityBehaviorBodyTemperature behavior)
        {
            if (Patches.Cozy && Covered(behavior.entity)) return int.MaxValue / 2;
            return ba.GetRainMapHeightAt(pos);
        }
    }

    public static class Patches
    {
        const string HarmonyId = "hansa";
        static Harmony harmony;
        static int users;
        static ILogger log;
        public static bool Cozy = true;

        public static void Apply(ICoreAPI api, ILogger logger)
        {
            users++;
            if (harmony != null) return;
            log = logger;
            harmony = new Harmony(HarmonyId);

            Try("deck walking", () => harmony.Patch(AccessTools.Method(typeof(EntityBehaviorPlayerPhysics), "SimPhysics"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(SimPhysicsPrefix))));
            // ((22 October 2026) things dropped aboard lie on her decks: DeckItems moves them, vanilla's physics leaves them be)
            Try("things dropped aboard", () =>
            {
                harmony.Patch(AccessTools.Method(typeof(EntityBehaviorPassivePhysics), nameof(EntityBehaviorPassivePhysics.OnPhysicsTick)),
                    prefix: new HarmonyMethod(typeof(Patches), nameof(DeckItemPhysicsPrefix)));
                harmony.Patch(AccessTools.Method(typeof(EntityBehaviorPassivePhysics), nameof(EntityBehaviorPassivePhysics.HandleRemotePhysics)),
                    prefix: new HarmonyMethod(typeof(Patches), nameof(DeckItemRemotePrefix)));
                DeckItemsPatched = true;
            });
            Try("wait for ship before mounting", () => harmony.Patch(AccessTools.Method(typeof(EntityAgent), "updateMountedState"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(UpdateMountedStatePrefix))));
            Try("sleep screen in bunks", () => harmony.Patch(AccessTools.Method(typeof(ModSleeping), "FastTick"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(FastTickPrefix)),
                postfix: new HarmonyMethod(typeof(Patches), nameof(FastTickPostfix))));
            Try("cabin warmth", () => harmony.Patch(AccessTools.Method(typeof(EntityBehaviorBodyTemperature), "getNearHeatSourceStrength"),
                postfix: new HarmonyMethod(typeof(Patches), nameof(HeatPostfix))));
            Try("dry cabins", () => harmony.Patch(AccessTools.Method(typeof(EntityBehaviorBodyTemperature), "updateBodyTemperature"),
                transpiler: new HarmonyMethod(typeof(Patches), nameof(RainExposureTranspiler))));
            Try("no rain inside", () => harmony.Patch(AccessTools.Method(typeof(WeatherSimulationParticles), "asyncParticleSpawn"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(ParticlesPrefix))));
            // (0.3.13) under the open hatch in one of her rooms: rain, hail, snow and storm dust only through the opening
            foreach (var name in new[] { "SpawnRainParticles", "SpawnHailParticles", "SpawnSnowParticles", "SpawnDustParticles" })
                Try("weather through her hatches (" + name + ")", () => harmony.Patch(AccessTools.Method(typeof(WeatherSimulationParticles), name) ?? throw new Exception(name + " not found"),
                    prefix: new HarmonyMethod(typeof(Patches), nameof(WeatherThroughOpeningPrefix))));
            Try("muffled rain", () => harmony.Patch(AccessTools.Method(typeof(WeatherSimulationSound), "updateSounds"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(SoundPrefix))));
            Try("no hail below decks", () => harmony.Patch(AccessTools.Method(typeof(EntityBehaviorHealth), "ApplyHailDamage"),
                prefix: new HarmonyMethod(typeof(Patches), nameof(HailPrefix))));
            // waves inside hulls: the Sea mod owns the water shaders (ShipRenderer is its clip source)
            Try("reach of a ship's hull (anti-abuse)", () =>
            {
                var sp = AccessTools.TypeByName("Vintagestory.Server.ServerPlayer") ?? throw new Exception("server player class not found");
                var m = AccessTools.Method(sp, "isInInteractionRangeOf", new[] { typeof(IPlayer), typeof(Entity), typeof(float) })
                        ?? throw new Exception("isInInteractionRangeOf not found");
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Patches), nameof(InteractionRangePrefix)));
                ReachPatched = true;
            });
        }

        static bool clientPatched;
        /// <summary>The server's reach check knows ships (for the self-test).</summary>
        public static bool ReachPatched;

        /// <summary>Client-only patches (the game's camera lives in the client).</summary>
        public static void ApplyClient(ILogger logger)
        {
            if (harmony == null || clientPatched) return;
            clientPatched = true;
            log ??= logger;
            PatchClient(harmony, (what, e) => log?.Warning("Could not patch ({0}), that feature is off: {1}", what, e.Message));
        }

        /// <summary>
        /// The self-test (a dedicated server, no client): would the client's patches go on? They're put on under another
        /// Harmony id, whilePatched runs (the server's own selection ray goes through them), and they're taken off again.
        /// Returns what failed, or null.
        /// </summary>
        public static string VerifyClient(Action whilePatched = null)
        {
            var h = new Harmony(HarmonyId + "-verify");
            var failed = new List<string>();
            try
            {
                PatchClient(h, (what, e) => failed.Add(what + ": " + e.Message));
                whilePatched?.Invoke();
            }
            catch (Exception e) { failed.Add("running with them: " + e.Message); }
            finally { h.UnpatchAll(h.Id); }
            return failed.Count == 0 ? null : string.Join("; ", failed);
        }

        static void PatchClient(Harmony h, Action<string, Exception> failed)
        {
            void Try(string what, Action patch)
            {
                try { patch(); }
                catch (Exception e) { failed(what, e); }
            }
            Try("the view tipping with the deck", () =>
            {
                var camera = AccessTools.TypeByName("Vintagestory.Client.NoObf.Camera") ?? throw new Exception("camera class not found");
                cameraMatrix = AccessTools.Field(camera, "CameraMatrix");
                cameraMatrixOrigin = AccessTools.Field(camera, "CameraMatrixOrigin");
                cameraMatrixOriginf = AccessTools.Field(camera, "CameraMatrixOriginf");
                if (cameraMatrix == null || cameraMatrixOrigin == null || cameraMatrixOriginf == null) throw new Exception("camera matrices not found");
                h.Patch(AccessTools.Method(camera, "Update"), postfix: new HarmonyMethod(typeof(Patches), nameof(CameraUpdatePostfix)));
            });
            Try("the crosshair on ships", () =>
            {
                var game = AccessTools.TypeByName("Vintagestory.Common.GameMain") ?? throw new Exception("GameMain not found");
                var m = AccessTools.Method(game, "RayTraceForSelection", new[] { typeof(IWorldIntersectionSupplier), typeof(Ray),
                    typeof(BlockSelection).MakeByRefType(), typeof(EntitySelection).MakeByRefType(), typeof(BlockFilter), typeof(EntityFilter) })
                    ?? throw new Exception("RayTraceForSelection not found");
                h.Patch(m, prefix: new HarmonyMethod(typeof(Patches), nameof(RayTracePrefix)), postfix: new HarmonyMethod(typeof(Patches), nameof(RayTracePostfix)));
            });
            Try("the look-at box on ships", () =>
            {
                var hud = AccessTools.TypeByName("Vintagestory.Client.NoObf.HudElementBlockAndEntityInfo") ?? throw new Exception("block info hud not found");
                composeInfoHud = AccessTools.Method(hud, "ComposeBlockInfoHud") ?? throw new Exception("ComposeBlockInfoHud not found");
                h.Patch(AccessTools.Method(hud, "EntityInView") ?? throw new Exception("EntityInView not found"),
                    postfix: new HarmonyMethod(typeof(Patches), nameof(EntityInViewPostfix)));
            });
        }

        public static void Remove(ICoreAPI api)
        {
            if (--users > 0 || harmony == null) return;
            harmony.UnpatchAll(HarmonyId);
            harmony = null;
            clientPatched = false;
        }

        static void Try(string what, Action patch)
        {
            try { patch(); }
            catch (Exception e) { log?.Warning("Could not patch ({0}), that feature is off: {1}", what, e.Message); }
        }

        // ---------------------------------------------------------------- deck walking

        static void SimPhysicsPrefix(EntityBehaviorPlayerPhysics __instance, float dt)
        {
            if (__instance.entity is EntityPlayer ep && ep.MountedOn is DeckSeat && ep.Api.Side == EnumAppSide.Client)
            {
                ShipClientSystem.Instance?.StepLocal(ep, dt);
            }
        }

        /// <summary>Both of the patches for things dropped aboard took (for the self-test).</summary>
        public static bool DeckItemsPatched;

        /// <summary>Server: an item lying aboard is moved by DeckItems, not by vanilla's physics (which would drop it through her).</summary>
        static bool DeckItemPhysicsPrefix(EntityBehaviorPassivePhysics __instance) =>
            !(__instance.Entity?.World?.Side == EnumAppSide.Server && DeckItems.HeldOnServer(__instance.Entity));

        /// <summary>Client: an item the server says is aboard is drawn on her deck (DeckItems.ClientFrame), not run through vanilla's physics.</summary>
        static bool DeckItemRemotePrefix(EntityBehaviorPassivePhysics __instance) => !DeckItems.ClientRemote(__instance.Entity);

        /// <summary>On clients the ship entity may arrive after a player's "mounted on" info; don't drop it, wait.</summary>
        static bool UpdateMountedStatePrefix(EntityAgent __instance)
        {
            if (__instance.World?.Side != EnumAppSide.Client) return true;
            if (__instance.WatchedAttributes["mountedOn"] is TreeAttribute tree && tree.GetString("className") == ShipSeat.ClassName &&
                !(__instance.World.GetEntityById(tree.GetLong("entityIdMount")) is EntityShip))
            {
                return false;  // ShipClientSystem.OnShipLoaded mounts them when the ship shows up
            }
            return true;
        }

        // ---------------------------------------------------------------- the view on a rolling deck

        static FieldInfo cameraMatrix, cameraMatrixOrigin, cameraMatrixOriginf;

        /// <summary>
        /// Roll the player's view about its own axis (<see cref="ShipCamera.Roll"/>): the matrix everything is drawn
        /// with, and the one the frustum is culled with, so nothing at the screen's corners goes missing.
        /// </summary>
        static void CameraUpdatePostfix(object __instance)
        {
            double roll = ShipCamera.Roll;
            if (roll == 0 || __instance.GetType().Name != "PlayerCamera") return;
            ShipCamera.ApplyRoll(cameraMatrix.GetValue(__instance) as double[], roll);
            if (cameraMatrixOrigin.GetValue(__instance) is double[] origin && cameraMatrixOriginf.GetValue(__instance) is float[] originf)
            {
                ShipCamera.ApplyRoll(origin, roll);
                for (int i = 0; i < 16; i++) originf[i] = (float)origin[i];
            }
        }

        // ---------------------------------------------------------------- the crosshair on ships

        /// <summary>
        /// The client's selection ray (GameMain.RayTraceForSelection): vanilla takes its entities from the chunks within
        /// reach of the eye, those whose centre is in reach, and a ship's centre is often 10-15 blocks away. And the
        /// distance it compares entities by is the square of the distance to their centres. So ships are left out of its
        /// own pass here and tested after it (<see cref="RayTracePostfix"/>), where the nearest hit point wins among the
        /// block, the other entity and the ships. Only for the client's world itself (not a mini-dimension's supplier).
        /// Arguments by position: supplier, ray, block selection, entity selection, block filter, entity filter.
        /// </summary>
        static void RayTracePrefix(object __instance, object __0, ref EntityFilter __5, out EntityFilter __state)
        {
            __state = null;
            if (__instance is not IClientWorldAccessor || !ReferenceEquals(__0, __instance) || ShipClientSystem.Instance == null) return;
            var original = __5;
            __state = original ?? (e => true);
            __5 = e => e is not EntityShip && (original == null || original(e));
        }

        static void RayTracePostfix(Ray __1, ref BlockSelection __2, ref EntitySelection __3, EntityFilter __state)
        {
            if (__state == null || __1 == null) return;
            ShipClientSystem.Instance?.PickShips(__1, ref __2, ref __3, __state);
            // (0.3.14) one of her working blocks there: the selection is that block, in her dimension (ShipBlocksClient)
            ShipBlocksClient.Pick(ShipBlocksSystem.ClientInstance?.ClientApi, __1, ref __2, ref __3);
        }

        static MethodInfo composeInfoHud;
        static long hudShip;
        static int hudPart;

        /// <summary>
        /// The look-at box (HudElementBlockAndEntityInfo) is rebuilt by vanilla only when you look at another entity:
        /// looking at another part of the same ship rebuilds it too (it redraws only if the text changed).
        /// </summary>
        static void EntityInViewPostfix(object __instance)
        {
            var sel = ShipClientSystem.Instance?.LookSelection;
            if (sel?.Entity is not EntityShip ship) { hudShip = 0; return; }
            bool same = ship.EntityId == hudShip;
            if (same && sel.SelectionBoxIndex == hudPart) return;
            hudShip = ship.EntityId;
            hudPart = sel.SelectionBoxIndex;
            if (same) composeInfoHud?.Invoke(__instance, null);     // another entity: vanilla has just rebuilt it
        }

        /// <summary>
        /// Anti-abuse (server, ServerPlayer.isInInteractionRangeOf): vanilla measures a player's reach to an entity to its
        /// selection box, 6 x 4 round a ship's middle, so using an item while the crosshair is on her far end (eating on
        /// deck) would be refused. For a ship it's the reach to her hull. Arguments: player, entity, slack.
        /// </summary>
        static bool InteractionRangePrefix(IPlayer __0, Entity __1, float __2, ref bool __result)
        {
            if (__1 is not EntityShip ship || ship.Def == null || __0?.Entity == null) return true;
            var ep = __0.Entity;
            __result = ep.Pos.Dimension == ship.Pos.Dimension &&
                       ship.DistanceToHull(ep.Pos.X + ep.LocalEyePos.X, ep.Pos.Y + ep.LocalEyePos.Y, ep.Pos.Z + ep.LocalEyePos.Z) <= __0.WorldData.PickingRange + __2 + 0.5;
            return false;
        }

        // ---------------------------------------------------------------- sleeping

        static readonly AccessTools.FieldRef<ModSleeping, float> sleepLevel = TryFieldRef<ModSleeping, float>("sleepLevel");
        static readonly AccessTools.FieldRef<ModSleeping, ICoreClientAPI> sleepCapi = TryFieldRef<ModSleeping, ICoreClientAPI>("capi");

        static AccessTools.FieldRef<T, F> TryFieldRef<T, F>(string name) where T : class
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch { return null; }
        }

        static void FastTickPrefix(ModSleeping __instance, out float __state)
        {
            __state = sleepLevel != null ? sleepLevel(__instance) : 0;
        }

        /// <summary>Vanilla only shows the closing-eyes screen for players in a bed block.</summary>
        static void FastTickPostfix(ModSleeping __instance, float dt, float __state)
        {
            if (sleepLevel == null || sleepCapi == null) return;
            var capi = sleepCapi(__instance);
            if (capi?.World?.Player?.Entity?.MountedOn is not BunkSeat) return;
            sleepLevel(__instance) = GameMath.Clamp(__state + dt * (__instance.AllSleeping ? 0.1f : -0.35f), 0, 0.99f);
        }

        // ---------------------------------------------------------------- warmth and dryness

        static void HeatPostfix(EntityBehaviorBodyTemperature __instance, ref float __result, ref bool ___inEnclosedRoom)
        {
            if (Cozy && ShipShelter.Room(__instance.entity) != null)
            {
                if (ShipShelter.IsSheltered(__instance.entity)) ___inEnclosedRoom = true;
                __result += ShipShelter.StoveHeat(__instance.entity, ___inEnclosedRoom);
            }
            // the firehearth warms like vanilla's firepit, cabins or not (config Hearth)
            __result += ShipShelter.HearthHeat(__instance.entity, ___inEnclosedRoom);
        }

        static IEnumerable<CodeInstruction> RainExposureTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var target = AccessTools.Method(typeof(IBlockAccessor), nameof(IBlockAccessor.GetRainMapHeightAt), new[] { typeof(BlockPos) });
            var replacement = AccessTools.Method(typeof(ShipShelter), nameof(ShipShelter.RainMapHeight));
            bool done = false;
            foreach (var ci in instructions)
            {
                if (!done && ci.Calls(target))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    done = true;
                    continue;
                }
                yield return ci;
            }
            if (!done) log?.Warning("Rain exposure call not found in body temperature; cabins won't keep you dry");
        }

        static bool ParticlesPrefix(ref bool __result)
        {
            if (!ShipShelter.LocalSheltered) return true;
            __result = true;
            return false;
        }

        /// <summary>
        /// (0.3.13) Under the open hatch in one of her rooms: vanilla's weather particles (each a box tens of blocks
        /// wide) go through <see cref="Hansa.ThroughOpening"/>, which puts them over the opening only.
        /// </summary>
        static void WeatherThroughOpeningPrefix(ref IAsyncParticleManager manager)
        {
            var box = ShipShelter.LocalOpening;
            if (box == null || manager == null || manager is ThroughOpening) return;
            manager = new ThroughOpening(manager, box, ShipShelter.LocalHeadY);
        }

        static bool wasMuffled;

        static void SoundPrefix(ref bool ___searchComplete)
        {
            if (ShipShelter.LocalSheltered)
            {
                WeatherSimulationSound.roomVolumePitchLoss = 0.8f;
                ___searchComplete = false;
                wasMuffled = true;
            }
            else if (wasMuffled)
            {
                ___searchComplete = true;
                wasMuffled = false;
            }
        }

        static bool HailPrefix(EntityBehaviorHealth __instance) => !(Cozy && ShipShelter.Covered(__instance.entity));
    }
}
