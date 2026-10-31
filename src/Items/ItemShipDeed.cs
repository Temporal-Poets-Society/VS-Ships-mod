using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Common.Entities;

namespace Hansa
{
    /// <summary>
    /// A ship's charter: hold right-click facing open water and the ship is launched side-on in front of you (stand-in
    /// until ships are built at a slipway). A boat (attribute "boat"): hold right-click on the water and she's put in
    /// there, her bow the way you look; Shift + right-click her, empty, picks her up again.
    /// - Held, not clicked, and never from aboard a ship or with a ship in the crosshair: till 0.3.5 any right-click with
    ///   it in hand that nothing aboard took launched one (a charter isn't used up in creative), and the user found
    ///   carracks they never meant to launch "reappearing" beside them.
    /// - Every launch is told in chat and written to the audit log, with her number.
    /// - (0.3.19, the user: "it often immediately spawns two on each other", "I release immediately and still get a double
    ///   spawn") The engine calls the stop twice on the server for one completed use (<see cref="OnHeldInteractStop"/>):
    ///   one launch per use now (<see cref="LaunchGuard"/>), a per-player cooldown, and never where another ship is or one
    ///   was just launched (<see cref="HansaMod.OverlapsShip"/>). Her meshes are loaded on the client while the charter is
    ///   held (<see cref="ShipClientSystem.PreloadDesign"/>), so she shows when she's launched, not seconds later.
    /// </summary>
    public class ItemShipDeed : Item
    {
        /// <summary>How long right-click is held to launch her (seconds).</summary>
        public const float LaunchSeconds = 1.5f;

        string ShipCode => Attributes?["ship"].AsString(HansaMod.DefaultShip) ?? HansaMod.DefaultShip;
        /// <summary>The design it launches.</summary>
        public string ShipCodeOf => ShipCode;
        /// <summary>A boat you carry and put in the water yourself (Hansa's ship's boat).</summary>
        public bool IsBoat => Attributes?["boat"].AsBool(false) == true;

        /// <summary>Why she can't be launched from here (a lang code), or null: not from aboard a ship, nor at one.</summary>
        static string Refusal(EntityAgent byEntity, EntitySelection entitySel)
        {
            if (byEntity.MountedOn is ShipSeat) return "ships:deed-aboard";
            if (entitySel?.Entity is EntityShip) return "ships:deed-at-ship";
            return null;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            // (0.3.19) a new press only: the game starts the use again (firstEvent false) while the button stays down after
            // one ended; not handled, nothing goes to the server
            if (!firstEvent || byEntity == null) return;
            if (Refusal(byEntity, entitySel) is string why)
            {
                if (api.Side == EnumAppSide.Server && (byEntity as EntityPlayer)?.Player is IServerPlayer sp) sp.SendIngameError("ships", Lang.GetL(sp.LanguageCode, why));
                return;
            }
            // (0.3.19, the launch takes long: the ship's meshes loaded while the charter is held, not after she's launched)
            if (api.Side == EnumAppSide.Client) api.ModLoader.GetModSystem<HansaMod>()?.Client?.PreloadDesign(IsBoat ? null : ShipCode);
            handling = EnumHandHandling.PreventDefault;
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            return secondsUsed < LaunchSeconds;
        }

        /// <summary>
        /// (0.3.19) A test's stand-in for the launch itself (the self-test has no player): called instead of launching when
        /// set, with what would launch; returns an error lang code or null.
        /// </summary>
        public static System.Func<EntityAgent, ItemSlot, string> LaunchForTest;

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            // (0.3.19) the server launches, once per use: the engine calls this twice for one use when the step it replays
            // from the client's stop packet ends the use (ServerSystemBlockSimulation: callOnUsing's stop, then the
            // packet's own) and both had the hold's full time: the charter's double launch (LaunchGuard)
            if (secondsUsed < LaunchSeconds || api.Side != EnumAppSide.Server || byEntity == null) return;
            if (!LaunchGuard.TryLaunch(api, byEntity, byEntity.Controls.UsingBeginMS, out string why))
            {
                if (why != null) api.World.Logger.Notification("[ships] {0}: a charter's launch ignored ({1})", (byEntity as EntityPlayer)?.Player?.PlayerName ?? byEntity.Code?.ToString(), why);
                return;
            }
            if (LaunchForTest != null) { LaunchForTest(byEntity, slot); return; }
            if (!(byEntity is EntityPlayer ep) || !(ep.Player is IServerPlayer player)) return;
            if (Refusal(byEntity, entitySel) is string refused)
            {
                player.SendIngameError("ships", Lang.GetL(player.LanguageCode, refused));
                return;
            }
            var mod = api.ModLoader.GetModSystem<HansaMod>();
            EntityShip ship;
            string err = IsBoat ? mod.TryLaunchBoatAt(player, ShipCode, blockSel?.Position, out ship) : mod.TrySpawnShip(player, ShipCode, out ship);
            if (err == null && ship != null && slot.Itemstack?.Attributes.GetString("shipName") is string name)
                ship.SetName(name, slot.Itemstack.Attributes.GetTreeAttribute("shipNameStyle") is ITreeAttribute style ? NameStyle.FromTree(style) : null);
            if (err != null)
            {
                player.SendIngameError("ships", Lang.GetL(player.LanguageCode, err));
                return;
            }
            if (ship != null)
            {
                // whoever launches her is her first captain (ShipCrew)
                ship.SetFirstCaptain(player);
                api.World.Logger.Audit("[ships] {0} launched {1} #{2} at {3}", player.PlayerName, ship.GetName(), ship.EntityId, ship.Pos.AsBlockPos);
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "ships:deed-launched", ship.GetName(), ship.EntityId), EnumChatType.Notification);
            }
            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get(IsBoat ? "ships:boat-help" : "ships:deed-help"));
            if (inSlot.Itemstack?.Attributes.GetString("shipName") is string name) dsc.AppendLine(Lang.Get("ships:boat-named", name));
        }
    }
}
