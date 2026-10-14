using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Hansa
{
    /// <summary>
    /// Right-clicking with the surveyor's chalk: what was captured at the click (read-only, scrolls if it's long), a box
    /// for the comment (several lines; Ctrl + Enter saves too), Save and Cancel. The comment is <see cref="ChalkTextArea"/>
    /// (vanilla's text area with its caret kept with the text) in a clip, moved by its scrollbar as vanilla's text-input
    /// dialog does (GuiDialogTextInput), all in the dialog's own units; it scrolls to keep the caret in sight, and the
    /// mouse wheel over it scrolls it.
    /// </summary>
    public class GuiDialogChalkNote : GuiDialog
    {
        readonly int number;
        readonly string captured;
        readonly Action<string> onSave;
        bool saved;

        const double W = 620;
        const double CommentHeight = 140;
        /// <summary>The comment's whole height and how far it's scrolled (the dialog's units).</summary>
        double commentTotal = CommentHeight, commentScroll;
        ElementBounds commentClip;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogChalkNote(ICoreClientAPI capi, int number, string captured, Action<string> onSave) : base(capi)
        {
            this.number = number;
            this.captured = captured ?? "";
            this.onSave = onSave;
            Compose();
        }

        void Compose()
        {
            var font = CairoFont.WhiteDetailText();
            // the captured lines (rich text: it draws itself, so it scrolls in its clip), as tall as they wrap, up to 260
            // shown; the rest scrolls
            double total;
            try { total = capi.Gui.Text.GetMultilineTextHeight(font, captured, GuiElement.scaled(W - 4)) / Math.Max(0.1, RuntimeEnv.GUIScale) * 1.15 + 12; }
            catch (Exception) { total = 20 * (captured.Split('\n').Length + 2); }
            double shown = Math.Min(260, Math.Max(40, total));
            string vtml = captured.Replace("<", "&lt;").Replace(">", "&gt;");

            var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bg.BothSizing = ElementSizing.FitToChildren;
            double y = 34;
            var infoClip = ElementBounds.Fixed(0, y, W, shown);
            var infoText = ElementBounds.Fixed(0, 0, W - 4, total);
            var infoBar = ElementBounds.Fixed(W + 6, y, 20, shown);
            y += shown + 12;
            var label = ElementBounds.Fixed(0, y, W, 22);
            y += 26;
            var areaClip = commentClip = ElementBounds.Fixed(0, y, W, CommentHeight);
            var area = ElementBounds.Fixed(0, 0, W - 1, CommentHeight);
            var areaBar = ElementBounds.Fixed(W + 6, y, 20, CommentHeight);
            y += CommentHeight + 14;
            var cancel = ElementBounds.Fixed(0, y, 120, 30);
            var save = ElementBounds.Fixed(W + 26 - 170, y, 170, 30);

            SingleComposer = capi.Gui.CreateCompo("hansa-chalknote", dialog)
                .AddShadedDialogBG(bg)
                .AddDialogTitleBar(Lang.Get("hansa:chalk-title", number), () => TryClose())
                .BeginChildElements(bg)
                    .BeginClip(infoClip)
                        .AddRichtext(vtml, font, infoText, "info")
                    .EndClip()
                    .AddVerticalScrollbar(OnInfoScroll, infoBar, "infoscroll")
                    .AddStaticText(Lang.Get("hansa:chalk-comment"), CairoFont.WhiteSmallText(), label)
                    .BeginClip(areaClip)
                        .AddInteractiveElement(new ChalkTextArea(capi, area, OnCommentChanged, CairoFont.TextInput().WithFontSize(16)), "comment")
                    .EndClip()
                    .AddVerticalScrollbar(OnCommentScroll, areaBar, "commentscroll")
                    .AddSmallButton(Lang.Get("Cancel"), () => TryClose(), cancel)
                    .AddSmallButton(Lang.Get("hansa:chalk-save"), Save, save)
                .EndChildElements()
                .Compose();
            SingleComposer.GetScrollbar("commentscroll")?.SetHeights((float)CommentHeight, (float)CommentHeight);
            if (SingleComposer.GetElement("comment") is ChalkTextArea comment) comment.OnCursorMoved = (x, y) => KeepCaretInSight();
            // how tall the text really came out (it doesn't scroll if it fits)
            double real = total;
            try
            {
                var rt = SingleComposer.GetRichtext("info");
                if (rt != null && rt.TotalHeight > 0) real = rt.TotalHeight / Math.Max(0.1, RuntimeEnv.GUIScale) + 6;
            }
            catch (Exception) { }
            SingleComposer.GetScrollbar("infoscroll")?.SetHeights((float)shown, (float)Math.Max(shown, Math.Min(real, total)));
        }

        void OnInfoScroll(float value)
        {
            var text = SingleComposer.GetRichtext("info");
            if (text == null) return;
            text.Bounds.fixedY = -value;
            text.Bounds.CalcWorldBounds();
        }

        /// <summary>
        /// The text changed (before it's drawn again): the box as tall as its text, the scrollbar's range with it, and
        /// the text left where it was (the scrollbar keeps its handle's place, not the text's).
        /// </summary>
        void OnCommentChanged(string value)
        {
            if (SingleComposer?.GetElement("comment") is not ChalkTextArea area) return;
            commentTotal = Math.Max(CommentHeight, area.ContentHeight);
            area.Bounds.fixedHeight = commentTotal;
            var bar = SingleComposer.GetScrollbar("commentscroll");
            if (bar == null) return;
            double keep = commentScroll;
            bar.SetNewTotalHeight((float)commentTotal);
            ScrollCommentTo(keep);
        }

        /// <summary>Scrolls the comment so the caret's line is in sight (it moves after the text has changed).</summary>
        void KeepCaretInSight()
        {
            if (SingleComposer?.GetElement("comment") is not ChalkTextArea area) return;
            double scale = Math.Max(0.1, RuntimeEnv.GUIScale);
            double top = area.CaretTop / scale, bottom = top + area.LineHeight / scale + 6;
            double to = ChalkTextArea.ScrollToShow(commentScroll, top, bottom, CommentHeight, commentTotal);
            if (Math.Abs(to - commentScroll) > 0.01) ScrollCommentTo(to);
        }

        void ScrollCommentTo(double y)
        {
            var bar = SingleComposer?.GetScrollbar("commentscroll");
            double max = commentTotal - CommentHeight;
            y = max <= 0.5 ? 0 : Math.Clamp(y, 0, max);
            if (bar != null && max > 0.5) bar.CurrentYPosition = (float)y;
            OnCommentScroll((float)y);
        }

        void OnCommentScroll(float value)
        {
            commentScroll = value;
            var area = SingleComposer?.GetTextArea("comment");
            if (area == null) return;
            area.Bounds.fixedY = -value;
            area.Bounds.CalcWorldBounds();
        }

        /// <summary>The mouse wheel over the comment scrolls it (vanilla gives it to the first scrollbar, the captured text's).</summary>
        public override void OnMouseWheel(MouseWheelEventArgs args)
        {
            var bar = SingleComposer?.GetScrollbar("commentscroll");
            if (bar != null && commentClip != null && commentTotal > CommentHeight + 0.5 && commentClip.PointInside(capi.Input.MouseX, capi.Input.MouseY))
            {
                bar.OnMouseWheel(capi, args);
                if (args.IsHandled) return;
            }
            base.OnMouseWheel(args);
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            var area = SingleComposer.GetTextArea("comment");
            if (area != null) SingleComposer.FocusElement(area.TabIndex);
        }

        bool Save()
        {
            if (saved) return true;
            saved = true;
            string text = SingleComposer.GetTextArea("comment")?.GetText() ?? "";
            // closed first, so the screenshot taken after it doesn't show the dialog
            TryClose();
            onSave?.Invoke(text);
            return true;
        }

        public override void OnKeyDown(KeyEvent args)
        {
            // Enter makes a new line in the comment; Ctrl + Enter saves it
            if ((args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter) && args.CtrlPressed)
            {
                Save();
                args.Handled = true;
                return;
            }
            base.OnKeyDown(args);
        }
    }
}
