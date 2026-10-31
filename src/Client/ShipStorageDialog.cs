using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// The vanilla creature-inventory dialog, but it closes by distance to the chest instead of to the ship's
    /// centre (a ship is far bigger than the pick range).
    /// </summary>
    public class ShipStorageDialog : GuiDialogCreatureContents
    {
        static readonly HashSet<string> open = new();
        readonly EntityShip ship;
        readonly FixtureDef fixture;
        readonly InventoryGeneric inventory;

        public ShipStorageDialog(InventoryGeneric inv, EntityShip ship, ICoreClientAPI capi, FixtureDef fixture)
            : base(inv, ship, capi, "ships-storage-" + fixture.Code, ShipHud.FixtureName(ship, fixture))
        {
            this.ship = ship;
            this.fixture = fixture;
            inventory = inv;
            packetIdOffset = (fixture.Index + 1) << 11;
        }

        public static bool IsOpen(InventoryBase inv) => open.Contains(inv.InventoryID);
        internal static void SetOpen(InventoryBase inv, bool isOpen)
        {
            if (isOpen) open.Add(inv.InventoryID); else open.Remove(inv.InventoryID);
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            SetOpen(inventory, true);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            SetOpen(inventory, false);
        }

        public override void OnFinalizeFrame(float dt)
        {
            foreach (var comp in Composers) comp.Value.PostRender(dt);
            if (TooFar(capi, ship, fixture)) capi.Event.EnqueueMainThreadTask(() => TryClose(), "ships-closestorage");
        }

        /// <summary>The player walked away from the storage (or the ship is gone).</summary>
        internal static bool TooFar(ICoreClientAPI capi, EntityShip ship, FixtureDef fixture)
        {
            var ep = capi.World.Player.Entity;
            ship.LocalToWorld(fixture.Pos[0], fixture.Pos[1], fixture.Pos[2], out double x, out double y, out double z);
            return !ship.Alive || ep.Pos.SquareDistanceTo(x, y, z) > 6 * 6;
        }
    }

    /// <summary>
    /// A big storage (the hold, well over a thousand slots): its slots in a grid that scrolls, like the creative
    /// inventory's. Like the small one it closes when you walk away from the storage.
    /// </summary>
    public class ShipHoldDialog : GuiDialog
    {
        /// <summary>Storages up to this many slots use the small dialog.</summary>
        public const int SmallStorage = 48;
        const int Cols = 12, VisibleRows = 9;

        readonly InventoryGeneric inv;
        readonly EntityShip ship;
        readonly FixtureDef fixture;
        readonly int packetIdOffset;
        ElementBounds gridBounds;

        public override string ToggleKeyCombinationCode => null;
        public override bool UnregisterOnClose => true;
        public override bool PrefersUngrabbedMouse => false;

        public ShipHoldDialog(InventoryGeneric inv, EntityShip ship, ICoreClientAPI capi, FixtureDef fixture) : base(capi)
        {
            this.inv = inv;
            this.ship = ship;
            this.fixture = fixture;
            packetIdOffset = (fixture.Index + 1) << 11;
            Compose();
        }

        void Compose()
        {
            double pad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double dpad = GuiStyle.ElementToDialogPadding;
            int rows = (int)Math.Ceiling(inv.Count / (float)Cols);
            var visible = ElementStdBounds.SlotGrid(EnumDialogArea.None, pad, pad, Cols, VisibleRows).FixedGrow(2 * pad, 2 * pad);
            gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, pad, pad, Cols, rows);
            var clip = visible.ForkBoundingParent();
            var inset = clip.ForkBoundingParent(6, 3, 0, 3);
            var dialog = inset.ForkBoundingParent(dpad, dpad + 30, dpad + 20, dpad).WithAlignment(EnumDialogArea.CenterMiddle);
            var scrollbar = ElementStdBounds.VerticalScrollbar(inset).WithParent(dialog);
            string title = ShipHud.FixtureName(ship, fixture) + " — " + Lang.Get("ships:storage-slots", inv.Count);

            SingleComposer = capi.Gui.CreateCompo("ships-hold-" + ship.EntityId + "-" + fixture.Code, dialog)
                .AddShadedDialogBG(ElementBounds.Fill)
                .AddDialogTitleBar(title, () => TryClose())
                .AddInset(inset, 3)
                .BeginClip(clip)
                    .AddItemSlotGrid(inv, SendPacket, Cols, gridBounds, "slotgrid")
                .EndClip()
                .AddVerticalScrollbar(OnScroll, scrollbar, "scrollbar")
                .Compose();
            SingleComposer.GetScrollbar("scrollbar").SetHeights((float)visible.fixedHeight, (float)(gridBounds.fixedHeight + pad));
        }

        void OnScroll(float value)
        {
            gridBounds.fixedY = GuiElementItemSlotGridBase.unscaledSlotPadding - value;
            gridBounds.CalcWorldBounds();
        }

        void SendPacket(object p) => capi.Network.SendEntityPacketWithOffset(ship.EntityId, packetIdOffset, p);

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            ShipStorageDialog.SetOpen(inv, true);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            capi.World.Player.InventoryManager.CloseInventoryAndSync(inv);
            SingleComposer.GetSlotGrid("slotgrid")?.OnGuiClosed(capi);
            ShipStorageDialog.SetOpen(inv, false);
        }

        public override void OnFinalizeFrame(float dt)
        {
            base.OnFinalizeFrame(dt);
            if (ShipStorageDialog.TooFar(capi, ship, fixture)) capi.Event.EnqueueMainThreadTask(() => TryClose(), "ships-closestorage");
        }
    }
}
