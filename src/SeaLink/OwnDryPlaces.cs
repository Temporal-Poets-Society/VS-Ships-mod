using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// Without Sea and without any <c>seacontracts:hulls</c> owner, nobody knows that a ship's hold is dry: the world has
    /// water there (she's an entity, not blocks), so vanilla would drown a player standing in her hold below the waterline
    /// and draw the camera there as under water (fog, tint, muffled sound). These two vanilla checks then ask Ships' own
    /// dry places (<see cref="ShipDrySpaces"/>, the same answer Sea gets from Ships when it is installed):
    /// <list type="bullet">
    /// <item>breathing (server): <c>EntityBehaviorBreathe.Check</c>: eyes in water inside a hull breathe; a solid block at
    /// the eyes still counts (vanilla's call stands);</item>
    /// <item>the camera (client): <c>ClientMain.EyesInWaterDepth</c> and <c>GetEyesInWaterColorShift</c>: no underwater
    /// look with the eyes inside a hull.</item>
    /// </list>
    /// The patches go on only when Ships starts without Sea's typed API; each answers only while
    /// <see cref="Server"/>/<see cref="Client"/> is set, i.e. while no contract owner holds Ships' dry places
    /// (<see cref="HansaMod"/> checks every couple of seconds). With Sea they are never applied: Sea does this itself.
    /// </summary>
    public static class OwnDryPlaces
    {
        /// <summary>The server's dry places while Ships answers for them itself; null otherwise.</summary>
        public static volatile ShipDrySpaces Server;
        /// <summary>The client's dry places while Ships answers for them itself; null otherwise.</summary>
        public static volatile ShipDrySpaces Client;
        /// <summary>Client: is the player in the world itself (dimension 0, where ships sail)?</summary>
        public static Func<bool> ClientInWorld;

        static System.Reflection.FieldInfo mainCamera, cameraEyePos;

        public static void PatchServer(Harmony h, Action<string, Exception> failed)
        {
            try
            {
                h.Patch(AccessTools.Method(typeof(EntityBehaviorBreathe), nameof(EntityBehaviorBreathe.Check)) ?? throw new Exception("EntityBehaviorBreathe.Check not found"),
                    postfix: new HarmonyMethod(typeof(OwnDryPlaces), nameof(BreathePostfix)));
            }
            catch (Exception e) { failed("breathing in her hold without Sea", e); }
        }

        public static void PatchClient(Harmony h, Action<string, Exception> failed)
        {
            try
            {
                var client = AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientMain") ?? throw new Exception("client class not found");
                var camera = AccessTools.TypeByName("Vintagestory.Client.NoObf.Camera") ?? throw new Exception("camera class not found");
                mainCamera = AccessTools.Field(client, "MainCamera") ?? throw new Exception("MainCamera not found");
                cameraEyePos = AccessTools.Field(camera, "CameraEyePos") ?? throw new Exception("CameraEyePos not found");
                h.Patch(AccessTools.Method(client, "EyesInWaterDepth") ?? throw new Exception("EyesInWaterDepth not found"),
                    postfix: new HarmonyMethod(typeof(OwnDryPlaces), nameof(EyesInWaterDepthPostfix)));
                h.Patch(AccessTools.Method(client, "GetEyesInWaterColorShift") ?? throw new Exception("GetEyesInWaterColorShift not found"),
                    postfix: new HarmonyMethod(typeof(OwnDryPlaces), nameof(EyesInWaterColorShiftPostfix)));
            }
            catch (Exception e) { failed("the camera in her hold without Sea", e); }
        }

        /// <summary>A creature whose eyes are in water inside a hull breathes (server); only a liquid at the eyes is overruled.</summary>
        static void BreathePostfix(EntityBehaviorBreathe __instance)
        {
            var dry = Server;
            if (dry == null || !dry.Any || __instance.HasAir) return;
            var e = __instance.entity;
            if (e?.World == null || e.World.Side != EnumAppSide.Server) return;
            if (e is EntityPlayer player)
            {
                var mode = e.World.PlayerByUid(player.PlayerUID)?.WorldData.CurrentGameMode;
                if (mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator) return;
            }
            var pos = e.Pos;
            if (pos.Dimension != 0) return;
            double eyeY = pos.Y + (e.Swimming ? e.Properties.SwimmingEyeHeight : e.Properties.EyeHeight);
            double eyeX = pos.X + (e.LocalEyePos?.X ?? 0), eyeZ = pos.Z + (e.LocalEyePos?.Z ?? 0);
            var block = e.World.BlockAccessor.GetBlock(new BlockPos((int)Math.Floor(eyeX), (int)Math.Floor(eyeY), (int)Math.Floor(eyeZ), pos.Dimension), BlockLayersAccess.MostSolid);
            if (block != null && block.Id != 0 && !block.IsLiquid()) return;
            if (dry.IsDry(eyeX, eyeY, eyeZ)) __instance.HasAir = true;
        }

        static Vec3d CameraEye(object client)
        {
            if (mainCamera == null || cameraEyePos == null) return null;
            var cam = mainCamera.GetValue(client);
            return cam == null ? null : cameraEyePos.GetValue(cam) as Vec3d;
        }

        static bool CameraDry(object client)
        {
            var dry = Client;
            if (dry == null || !dry.Any || ClientInWorld?.Invoke() != true) return false;
            var eye = CameraEye(client);
            return eye != null && dry.IsDry(eye.X, eye.Y, eye.Z);
        }

        static void EyesInWaterDepthPostfix(object __instance, ref float __result)
        {
            if (__result > 0 && CameraDry(__instance)) __result = 0;
        }

        static void EyesInWaterColorShiftPostfix(object __instance, ref int __result)
        {
            if (__result > 0 && CameraDry(__instance)) __result = 0;
        }
    }
}
