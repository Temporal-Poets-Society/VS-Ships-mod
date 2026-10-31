using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Hansa
{
    /// <summary>
    /// The firehearth's dialog: vanilla's firepit dialog (GuiDialogBlockEntityFirepit, read from the 1.22.7 game) rebuilt
    /// for a ship, the same layout and look: the fuel slot with the flame that burns down and the fire's temperature, the
    /// input with its temperature, the arrow that fills as it cooks, the output, and with a pot in the input its four
    /// cooking slots and what it will make. It shows the values vanilla's firepit gives its dialog
    /// (<see cref="EntityShip.HearthDialogValues"/>, from what the server sent), sends its slot clicks to the ship (like the
    /// storages' dialogs), and closes when you walk away from the hearth.
    /// </summary>
    public class HearthDialog : GuiDialogGeneric
    {
        readonly EntityShip ship;
        readonly FixtureDef fixture;
        readonly InventoryBase inv;
        readonly int packetIdOffset;
        bool haveCookingContainer;
        string currentOutputText;
        ElementBounds cookingSlotsSlotBounds;
        long lastRedrawMs;
        EnumPosFlag screenPos;
        int seenVersion;

        public override double DrawOrder => 0.2;
        public override bool PrefersUngrabbedMouse => false;

        public HearthDialog(EntityShip ship, FixtureDef fixture, ICoreClientAPI capi) : base(Title(ship, fixture), capi)
        {
            this.ship = ship;
            this.fixture = fixture;
            inv = ship.HearthInv;
            packetIdOffset = (fixture.Index + 1) << 11;
            Attributes = new TreeAttribute();
            ship.HearthDialogValues(Attributes);
            seenVersion = ship.HearthVersion;
        }

        static string Title(EntityShip ship, FixtureDef f)
        {
            string name = ShipHud.FixtureName(ship, f);
            return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1) : name;
        }

        public InventoryBase Inventory => inv;

        void OnInventorySlotModified(int slotid)
        {
            // (as vanilla: what's in it changed here, so its values and the layout are looked at again)
            capi.Event.EnqueueMainThreadTask(() =>
            {
                ship.HearthDialogValues(Attributes);
                SetupDialog();
                OnAttributesModified();
            }, "ships-hearthdlg");
        }

        void SetupDialog()
        {
            var hovered = capi.World.Player.InventoryManager.CurrentHoveredSlot;
            if (hovered != null && hovered.Inventory?.InventoryID != inv?.InventoryID) hovered = null;

            string outputText = Attributes.GetString("outputText", "");
            bool haveContainer = Attributes.GetInt("haveCookingContainer") > 0;
            GuiElementDynamicText text;
            if (haveCookingContainer == haveContainer && SingleComposer != null)
            {
                // the same layout: only the text of what it will make, and the flame and arrow drawn again
                text = SingleComposer.GetDynamicText("outputText");
                text.Font.WithFontSize(14);
                text.SetNewText(outputText, true);
                SingleComposer.GetCustomDraw("symbolDrawer").Redraw();
                currentOutputText = outputText;
                FitOutputText(text);
                return;
            }
            haveCookingContainer = haveContainer;
            currentOutputText = outputText;
            int cookingSlots = Attributes.GetInt("quantityCookingSlots");

            var stoveBounds = ElementBounds.Fixed(0, 0, 210, 250);
            cookingSlotsSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 30 + 45, 4, cookingSlots / 4);
            cookingSlotsSlotBounds.fixedHeight += 10;
            double top = cookingSlotsSlotBounds.fixedHeight + cookingSlotsSlotBounds.fixedY;
            var inputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, top, 1, 1);
            var fuelSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 110 + top, 1, 1);
            var outputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 153, top, 1, 1);
            var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(stoveBounds);
            var dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithFixedAlignmentOffset(IsRight(screenPos) ? -GuiStyle.DialogToScreenPadding : GuiStyle.DialogToScreenPadding, 0)
                .WithAlignment(IsRight(screenPos) ? EnumDialogArea.RightMiddle : EnumDialogArea.LeftMiddle);
            if (!capi.Settings.Bool["immersiveMouseMode"])
            {
                dialogBounds.fixedOffsetY += (stoveBounds.fixedHeight + 65 + (haveCookingContainer ? 25 : 0)) * YOffsetMul(screenPos);
                dialogBounds.fixedOffsetX += (stoveBounds.fixedWidth + 10) * XOffsetMul(screenPos);
            }
            int[] cookingSlotIds = new int[cookingSlots];
            for (int i = 0; i < cookingSlots; i++) cookingSlotIds[i] = 3 + i;

            SingleComposer = capi.Gui.CreateCompo("ships-hearth-" + ship.EntityId, dialogBounds)
                .AddShadedDialogBG(bgBounds, true, 5, 0.75f)
                .AddDialogTitleBar(DialogTitle, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddDynamicCustomDraw(stoveBounds, OnBgDraw, "symbolDrawer")
                    .AddDynamicText("", CairoFont.WhiteDetailText(), ElementBounds.Fixed(0, 30, 210, 45), "outputText")
                    .AddIf(haveCookingContainer)
                        .AddItemSlotGrid(inv, SendInvPacket, 4, cookingSlotIds, cookingSlotsSlotBounds, "ingredientSlots")
                    .EndIf()
                    .AddItemSlotGrid(inv, SendInvPacket, 1, new[] { 0 }, fuelSlotBounds, "fuelslot")
                    .AddDynamicText("", CairoFont.WhiteDetailText(), fuelSlotBounds.RightCopy(17, 16).WithFixedSize(60, 30), "fueltemp")
                    .AddItemSlotGrid(inv, SendInvPacket, 1, new[] { 1 }, inputSlotBounds, "oreslot")
                    .AddDynamicText("", CairoFont.WhiteDetailText(), inputSlotBounds.RightCopy(23, 16).WithFixedSize(60, 30), "oretemp")
                    .AddItemSlotGrid(inv, SendInvPacket, 1, new[] { 2 }, outputSlotBounds, "outputslot")
                .EndChildElements()
                .Compose();
            lastRedrawMs = capi.ElapsedMilliseconds;
            if (hovered != null) SingleComposer.OnMouseMove(new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));
            text = SingleComposer.GetDynamicText("outputText");
            text.SetNewText(currentOutputText, true);
            FitOutputText(text);
        }

        /// <summary>A long "will make…" text: up a little and smaller, as vanilla's.</summary>
        static void FitOutputText(GuiElementDynamicText text)
        {
            text.Bounds.fixedOffsetY = 0;
            if (text.QuantityTextLines > 2)
            {
                text.Bounds.fixedOffsetY = -text.Font.GetFontExtents().Height / RuntimeEnv.GUIScale * 0.65;
                text.Font.WithFontSize(12);
                text.RecomposeText();
            }
            text.Bounds.CalcWorldBounds();
        }

        /// <summary>The fire's and the input's temperatures (vanilla's "Cold" at the air's), the flame and arrow drawn again now and then.</summary>
        void OnAttributesModified()
        {
            if (!IsOpened() || SingleComposer == null) return;
            float fireTemp = Attributes.GetFloat("furnaceTemperature");
            float inputTemp = Attributes.GetFloat("oreTemperature");
            string fire = fireTemp.ToString("#"), input = inputTemp.ToString("#");
            fire += fire.Length > 0 ? "°C" : "";
            input += input.Length > 0 ? "°C" : "";
            if (fireTemp > 0 && fireTemp <= 20) fire = Lang.Get("Cold");
            if (inputTemp > 0 && inputTemp <= 20) input = Lang.Get("Cold");
            SingleComposer.GetDynamicText("fueltemp")?.SetNewText(fire);
            SingleComposer.GetDynamicText("oretemp")?.SetNewText(input);
            if (capi.ElapsedMilliseconds - lastRedrawMs > 500)
            {
                SingleComposer.GetCustomDraw("symbolDrawer")?.Redraw();
                lastRedrawMs = capi.ElapsedMilliseconds;
            }
        }

        /// <summary>Vanilla's: the flame filled as far as the fuel has left to burn, the arrow as far as the input has cooked.</summary>
        void OnBgDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            double top = cookingSlotsSlotBounds.fixedHeight + cookingSlotsSlotBounds.fixedY;

            ctx.Save();
            var m = ctx.Matrix;
            m.Translate(GuiElement.scaled(5), GuiElement.scaled(53 + top));
            m.Scale(GuiElement.scaled(0.25), GuiElement.scaled(0.25));
            ctx.Matrix = m;
            capi.Gui.Icons.DrawFlame(ctx, 3, true, true);
            float maxBurn = Attributes.GetFloat("maxFuelBurnTime");
            double burnt = 210 - 210 * (maxBurn > 0 ? Attributes.GetFloat("fuelBurnTime") / maxBurn : 0);
            ctx.Rectangle(0, burnt, 200, 210 - burnt);
            ctx.Clip();
            var gradient = new LinearGradient(0, GuiElement.scaled(250), 0, 0);
            gradient.AddColorStop(0, new Color(1, 1, 0, 1));
            gradient.AddColorStop(1, new Color(1, 0, 0, 1));
            ctx.SetSource(gradient);
            capi.Gui.Icons.DrawFlame(ctx, 0, false, false);
            gradient.Dispose();
            ctx.Restore();

            ctx.Save();
            m = ctx.Matrix;
            m.Translate(GuiElement.scaled(63), GuiElement.scaled(top + 2));
            m.Scale(GuiElement.scaled(0.6), GuiElement.scaled(0.6));
            ctx.Matrix = m;
            capi.Gui.Icons.DrawArrowRight(ctx, 2, true, true);
            float maxCook = Attributes.GetFloat("maxOreCookingTime", 1);
            double cooked = maxCook > 0 ? Math.Clamp(Attributes.GetFloat("oreCookingTime") / maxCook, 0, 1) : 0;
            ctx.Rectangle(5, 0, 125 * cooked, 100);
            ctx.Clip();
            gradient = new LinearGradient(0, 0, 200, 0);
            gradient.AddColorStop(0, new Color(0, 0.4, 0, 1));
            gradient.AddColorStop(1, new Color(0.2, 0.6, 0.2, 1));
            ctx.SetSource(gradient);
            capi.Gui.Icons.DrawArrowRight(ctx, 0, false, false);
            gradient.Dispose();
            ctx.Restore();
        }

        void SendInvPacket(object p) => capi.Network.SendEntityPacketWithOffset(ship.EntityId, packetIdOffset, p);

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            capi.World.Player.InventoryManager.OpenInventory(inv);
            inv.SlotModified += OnInventorySlotModified;
            screenPos = GetFreePos("smallblockgui");
            OccupyPos("smallblockgui", screenPos);
            SetupDialog();
            OnAttributesModified();
            ShipStorageDialog.SetOpen(inv, true);
        }

        public override void OnGuiClosed()
        {
            inv.SlotModified -= OnInventorySlotModified;
            foreach (string grid in new[] { "fuelslot", "oreslot", "outputslot", "ingredientSlots" })
                SingleComposer?.GetSlotGrid(grid)?.OnGuiClosed(capi);
            capi.World.Player.InventoryManager.CloseInventoryAndSync(inv);
            FreePos("smallblockgui", screenPos);
            ShipStorageDialog.SetOpen(inv, false);
            base.OnGuiClosed();
        }

        public override void OnFinalizeFrame(float dt)
        {
            base.OnFinalizeFrame(dt);
            if (ShipStorageDialog.TooFar(capi, ship, fixture) || ship.HearthInv != inv)
            {
                capi.Event.EnqueueMainThreadTask(() => TryClose(), "ships-closehearth");
                return;
            }
            // the server sent the hearth again (twice a second while it burns): its values, and the layout if a pot came or went
            if (ship.HearthVersion != seenVersion)
            {
                seenVersion = ship.HearthVersion;
                ship.HearthDialogValues(Attributes);
                SetupDialog();
                OnAttributesModified();
            }
        }
    }
}
