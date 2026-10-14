using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Hansa
{
    /// <summary>
    /// The chalk note's comment box: vanilla's text area (GuiElementTextArea) with its caret kept with the text.
    /// <list type="bullet">
    /// <item>Typing a character that wraps the line (the word moves down): vanilla re-wraps the whole text, then puts the
    /// caret back by its old line and place in it, clamped to the new, shorter line, and adds one: it lands a few
    /// characters back on the next line and what's typed next goes into the middle of the word. Here it goes right
    /// after the character typed, counted through the whole text.</item>
    /// <item>Enter splits the line in its list of lines without wrapping again: Enter at the end of a wrapped line leaves an
    /// empty line in the list that isn't in the text, drawn until something wraps it away. Here the lines are always
    /// what wrapping the text gives, after any edit, and the caret stays where the edit left it in the text.</item>
    /// <item>Up and down keep the caret's place across the line (vanilla keeps its count of characters).</item>
    /// <item>Its height is the text's, in the dialog's own (unscaled) units: vanilla's grows by the text's height in screen
    /// pixels, too tall when the GUI is scaled, and counted each line break twice.</item>
    /// </list>
    /// </summary>
    public class ChalkTextArea : GuiElementTextArea
    {
        public ChalkTextArea(ICoreClientAPI capi, ElementBounds bounds, Action<string> onTextChanged, CairoFont font)
            : base(capi, bounds, onTextChanged, font)
        {
            Autoheight = false;
        }

        /// <summary>A line's height on screen (pixels): where vanilla draws the lines and the caret.</summary>
        public double LineHeight => Font.GetFontExtents().Height;

        /// <summary>The caret's top on screen, from the box's top (pixels).</summary>
        public double CaretTop => CaretPosLine * LineHeight;

        /// <summary>The text's height in the dialog's (unscaled) units.</summary>
        public double ContentHeight => (lines.Count * LineHeight + 2 * Bounds.absPaddingY) / Math.Max(0.1, RuntimeEnv.GUIScale) + 6;

        /// <summary>The caret's distance from the line's start (pixels).</summary>
        public double CaretX
        {
            get
            {
                string line = lines[CaretPosLine];
                int n = Math.Clamp(CaretPosInLine, 0, line.Length);
                return n == 0 ? 0 : Font.GetTextExtents(line.Substring(0, n)).XAdvance;
            }
        }

        /// <summary>Where across the line up and down keep the caret, from where the first of them started (pixels).</summary>
        double? goalX;

        public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
        {
            if (!HasFocus) { base.OnKeyPress(api, args); return; }
            goalX = null;
            string before = GetText();
            int at = CaretPosWithoutLineBreaks;
            base.OnKeyPress(api, args);
            string after = GetText();
            if (after == before) return;
            Settle(CaretAfterTyping(before, after, at, args.KeyChar));
        }

        public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
        {
            if (!HasFocus) { base.OnKeyDown(api, args); return; }
            string before = GetText();
            int line = CaretPosLine;
            bool vertical = (args.KeyCode == (int)GlKeys.Up || args.KeyCode == (int)GlKeys.Down) && !args.ShiftPressed && !args.CtrlPressed;
            double x = vertical ? goalX ?? CaretX : 0;
            base.OnKeyDown(api, args);
            if (GetText() != before) { goalX = null; Settle(CaretPosWithoutLineBreaks); }
            else if (vertical && CaretPosLine != line) { goalX = x; SetCaretPos(NearestTo(CaretPosLine, x), CaretPosLine); }
            else if (!vertical && args.KeyCode != (int)GlKeys.LShift && args.KeyCode != (int)GlKeys.RShift) goalX = null;
        }

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            goalX = null;
            base.OnMouseDownOnElement(api, args);
        }

        /// <summary>The place in a line nearest to x across it (pixels): before a character when x is nearer its left side.</summary>
        int NearestTo(int line, double x)
        {
            string s = lines[line].TrimEnd('\r', '\n');
            int best = 0;
            double bestD = Math.Abs(x);
            for (int i = 1; i <= s.Length; i++)
            {
                double d = Math.Abs(Font.GetTextExtents(s.Substring(0, i)).XAdvance - x);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>The lines as wrapping the whole text gives them, and the caret at this place in the text.</summary>
        public void Settle(int caret)
        {
            var wrapped = Lineize(GetText());
            if (!SameLines(wrapped, lines)) LoadValue(wrapped);
            CaretPosWithoutLineBreaks = Math.Clamp(caret, 0, GetText().Length);
        }

        /// <summary>
        /// Where the caret goes after a key typed a character: right after it, where it went in (at, counted through the
        /// whole text) if that's what happened; otherwise (a selection replaced) after the part that changed.
        /// </summary>
        public static int CaretAfterTyping(string before, string after, int at, char typed)
        {
            if (at >= 0 && at <= before.Length && after.Length == before.Length + 1 && after[at] == typed &&
                string.CompareOrdinal(after, 0, before, 0, at) == 0 && string.CompareOrdinal(after, at + 1, before, at, before.Length - at) == 0)
                return at + 1;
            int p = 0;
            while (p < before.Length && p < after.Length && before[p] == after[p]) p++;
            int s = 0;
            while (s < before.Length - p && s < after.Length - p && before[before.Length - 1 - s] == after[after.Length - 1 - s]) s++;
            return after.Length - s;
        }

        /// <summary>
        /// How far to scroll a box showing <paramref name="visible"/> of <paramref name="total"/> so that top..bottom is
        /// in sight, moving as little as can be from <paramref name="scroll"/> (all in one unit).
        /// </summary>
        public static double ScrollToShow(double scroll, double top, double bottom, double visible, double total)
        {
            double max = Math.Max(0, total - visible);
            if (top < scroll) scroll = top;
            else if (bottom > scroll + visible) scroll = bottom - visible;
            return Math.Clamp(scroll, 0, max);
        }

        static bool SameLines(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
