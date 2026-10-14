using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Hansa
{
    /// <summary>
    /// Right-clicking the name board: a line to write her name in, and how it's painted (the user's note 68): the letters'
    /// colour (gilt or a dye), the board's ground (as built or a colour), the size, italic. Painted on the transom when you
    /// confirm (the server keeps it with her name; every client draws it).
    /// </summary>
    public class GuiDialogShipName : GuiDialog
    {
        readonly EntityShip ship;
        readonly Action<string, NameStyle> onName;
        readonly NameStyle style;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogShipName(ICoreClientAPI capi, EntityShip ship, Action<string, NameStyle> onName) : base(capi)
        {
            this.ship = ship;
            this.onName = onName;
            var s = ship.NameStyle ?? new NameStyle();
            style = new NameStyle { Text = s.Text, Back = s.Back, Size = s.Size, Italic = s.Italic };
            Compose();
        }

        static string ColourName(string c) => c == ShipPaint.Gold ? Lang.Get("hansa:name-gilt") : DecoClient.DyeName(c);

        void Compose()
        {
            var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bg.BothSizing = ElementSizing.FitToChildren;
            var label = ElementBounds.Fixed(0, 40, 340, 25);
            var input = ElementBounds.Fixed(0, 68, 340, 30);
            var textLabel = ElementBounds.Fixed(0, 114, 150, 25);
            var textDrop = ElementBounds.Fixed(160, 110, 180, 28);
            var backLabel = ElementBounds.Fixed(0, 150, 150, 25);
            var backDrop = ElementBounds.Fixed(160, 146, 180, 28);
            var sizeLabel = ElementBounds.Fixed(0, 186, 150, 25);
            var sizeDrop = ElementBounds.Fixed(160, 182, 180, 28);
            var italicLabel = ElementBounds.Fixed(0, 222, 150, 25);
            var italicSwitch = ElementBounds.Fixed(160, 218, 30, 30);
            var cancel = ElementBounds.Fixed(0, 268, 120, 30);
            var ok = ElementBounds.Fixed(220, 268, 120, 30);

            var colours = NameStyle.Colours;
            string[] backs = new[] { "" }.Concat(colours).ToArray();
            string[] backNames = new[] { Lang.Get("hansa:name-back-none") }.Concat(colours.Select(ColourName)).ToArray();
            string[] sizes = Enumerable.Range(0, NameStyle.Sizes.Length).Select(i => i.ToString()).ToArray();
            string[] sizeNames = sizes.Select(i => Lang.Get("hansa:name-size-" + i)).ToArray();
            int ti = Math.Max(0, Array.IndexOf(colours, style.Text)), bi = Math.Max(0, Array.IndexOf(backs, style.Back ?? ""));

            SingleComposer = capi.Gui.CreateCompo("hansa-shipname", dialog)
                .AddShadedDialogBG(bg)
                .AddDialogTitleBar(Lang.Get("hansa:name-title"), () => TryClose())
                .BeginChildElements(bg)
                    .AddStaticText(Lang.Get("hansa:name-label", EntityShip.MaxNameLength), CairoFont.WhiteSmallText(), label)
                    .AddTextInput(input, null, CairoFont.TextInput(), "name")
                    .AddStaticText(Lang.Get("hansa:name-text-colour"), CairoFont.WhiteSmallText(), textLabel)
                    .AddDropDown(colours, colours.Select(ColourName).ToArray(), ti, (code, sel) => style.Text = code, textDrop, "text")
                    .AddStaticText(Lang.Get("hansa:name-back-colour"), CairoFont.WhiteSmallText(), backLabel)
                    .AddDropDown(backs, backNames, bi, (code, sel) => style.Back = string.IsNullOrEmpty(code) ? null : code, backDrop, "back")
                    .AddStaticText(Lang.Get("hansa:name-size"), CairoFont.WhiteSmallText(), sizeLabel)
                    .AddDropDown(sizes, sizeNames, Math.Clamp(style.Size, 0, sizes.Length - 1), (code, sel) => style.Size = int.Parse(code), sizeDrop, "size")
                    .AddStaticText(Lang.Get("hansa:name-italic"), CairoFont.WhiteSmallText(), italicLabel)
                    .AddSwitch(on => style.Italic = on, italicSwitch, "italic")
                    .AddSmallButton(Lang.Get("Cancel"), () => TryClose(), cancel)
                    .AddSmallButton(Lang.Get("hansa:name-paint"), Confirm, ok)
                .EndChildElements()
                .Compose();
            var field = SingleComposer.GetTextInput("name");
            field.SetMaxLength(EntityShip.MaxNameLength);
            field.SetValue(ship.ShipName ?? "");
            SingleComposer.GetSwitch("italic").SetValue(style.Italic);
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            SingleComposer.FocusElement(SingleComposer.GetTextInput("name").TabIndex);
        }

        bool Confirm()
        {
            onName(SingleComposer.GetTextInput("name").GetText(), style.Clean());
            TryClose();
            return true;
        }

        public override void OnKeyDown(KeyEvent args)
        {
            if (args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter)
            {
                Confirm();
                args.Handled = true;
                return;
            }
            base.OnKeyDown(args);
        }
    }
}
