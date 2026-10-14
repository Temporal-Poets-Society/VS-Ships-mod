using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Hansa;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace HansaTests
{
    /// <summary>Stands in for the client's API: does nothing, answers with defaults (and stand-ins for its parts).</summary>
    public class NothingApi : DispatchProxy
    {
        protected override object Invoke(MethodInfo m, object[] args)
        {
            var t = m.ReturnType;
            if (t == typeof(void)) return null;
            if (t.IsInterface && t.IsPublic) return Create(t, typeof(NothingApi));
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }

    static partial class Program
    {
        /// <summary>
        /// The chalk note's comment box (ChalkTextArea, the eighth client test: "cursor position breaks when changing line"):
        /// typed into with the game's own text area code and Cairo (its fonts measured as in the game), in a box narrow
        /// enough that it wraps. The caret follows each character typed across the wraps, Enter, typing after it, up and
        /// down, Home, Backspace joining lines; the lines are always what wrapping the text gives; the box's height and
        /// the scroll that keeps the caret in sight.
        /// </summary>
        static void ChalkText()
        {
            Console.WriteLine("the chalk note's comment box: the caret with the text");
            float keepScale = RuntimeEnv.GUIScale;
            try
            {
                string lib = Path.Combine(Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? "", "Vintagestory"), "Lib");
                try
                {
                    NativeLibrary.SetDllImportResolver(typeof(Cairo.Context).Assembly, (name, asm, path) =>
                        NativeLibrary.TryLoad(Path.Combine(lib, name.EndsWith(".dll") ? name : name + ".dll"), out var h) ? h : IntPtr.Zero);
                }
                catch (InvalidOperationException) { }   // set already
                var capi = (ICoreClientAPI)DispatchProxy.Create(typeof(ICoreClientAPI), typeof(NothingApi));
                if (!Lang.AvailableLanguages.ContainsKey("en"))
                {
                    var logger = (Vintagestory.API.Common.ILogger)DispatchProxy.Create(typeof(Vintagestory.API.Common.ILogger), typeof(NothingApi));
                    Lang.AvailableLanguages["en"] = new TranslationService("en", logger, null, EnumLinebreakBehavior.AfterWord);
                }
                if (Lang.CurrentLocale == null) typeof(Lang).GetProperty("CurrentLocale").GetSetMethod(true).Invoke(null, new object[] { "en" });
                RuntimeEnv.GUIScale = 1.5f;

                GuiElementTextArea Box(bool vanilla)
                {
                    var parent = ElementBounds.Fixed(0, 0, 300, 140);
                    var b = ElementBounds.Fixed(0, 0, 200, 140);
                    parent.WithChild(b);
                    parent.ParentBounds = ElementBounds.Empty;
                    var font = CairoFont.TextInput().WithFontSize(16);
                    GuiElementTextArea box = vanilla ? new GuiElementTextArea(capi, b, s => { }, font) : new ChalkTextArea(capi, b, s => { }, font);
                    b.CalcWorldBounds();
                    box.OnFocusGained();
                    return box;
                }
                void Type(GuiElementTextArea box, string s) { foreach (char c in s) box.OnKeyPress(capi, new KeyEvent { KeyChar = c }); }
                void Key(GuiElementTextArea box, GlKeys k) => box.OnKeyDown(capi, new KeyEvent { KeyCode = (int)k });
                // (Ctrl + a key asks OpenTK for the key's name: not here)
                void Top(GuiElementTextArea box) { for (int i = box.GetLines().Count; i > 0; i--) Key(box, GlKeys.Up); Key(box, GlKeys.Home); }
                void Bottom(GuiElementTextArea box) { for (int i = box.GetLines().Count; i > 0; i--) Key(box, GlKeys.Down); Key(box, GlKeys.End); }
                bool Wrapped(GuiElementTextArea box)
                {
                    var want = box.Lineize(box.GetText());
                    var have = box.GetLines();
                    if (want.Count != have.Count) return false;
                    for (int i = 0; i < want.Count; i++) if (want[i] != have[i]) return false;
                    return true;
                }

                var area = (ChalkTextArea)Box(false);
                Check(area.LineHeight > 10 && area.LineHeight < 40, $"the box's font is measured as in the game ({area.LineHeight:0.0} px a line at GUI scale 1.5)");
                if (area.LineHeight <= 0) return;

                // typing a sentence: the caret after each character, across the wraps
                string text = "The hold is dry but the pump wheezes when the ship rolls hard to port ";
                bool caretFollows = true;
                for (int i = 0; i < text.Length; i++)
                {
                    area.OnKeyPress(capi, new KeyEvent { KeyChar = text[i] });
                    caretFollows &= area.CaretPosWithoutLineBreaks == i + 1;
                }
                int wrapLines = area.GetLines().Count;
                var vanilla = Box(true);
                Type(vanilla, text);
                Check(area.GetText() == text && caretFollows && wrapLines >= 3 && Wrapped(area),
                    $"typing a sentence that wraps ({wrapLines} lines): the text is what was typed and the caret stays after each character " +
                    $"(vanilla's text area makes it \"{vanilla.GetText().Replace("\n", "\\n")}\")");

                // typing in the middle of the first line: the caret after it, the rest re-wrapped
                Top(area);
                Type(area, "Note: ");
                Check(area.GetText() == "Note: " + text && area.CaretPosWithoutLineBreaks == 6 && area.CaretPosLine == 0 && Wrapped(area),
                    $"typing at the start of the first line: the caret after what's typed ({area.CaretPosWithoutLineBreaks}), the rest wrapped on");

                // Enter at the end: a new line, the caret on it, typing goes there; no line in the list that isn't in the text
                Bottom(area);
                Key(area, GlKeys.Enter);
                Type(area, "second");
                string full = "Note: " + text + "\nsecond";
                Check(area.GetText() == full && area.CaretPosWithoutLineBreaks == full.Length && area.CaretPosLine == area.GetLines().Count - 1 &&
                      area.GetLines()[^1] == "second" && Wrapped(area),
                    $"Enter at the end of a wrapped line and typing: a new line with it ({area.GetLines().Count} lines, the caret at {area.CaretPosWithoutLineBreaks} of {full.Length})");
                // Enter in the middle of a wrapped line: the lines are re-wrapped (vanilla leaves a split line behind)
                var ve = Box(true);
                Type(ve, "aaaa bbbb cccc dddd eeee ffff gggg hhhh iiii jjjj");
                Key(ve, GlKeys.Home); Key(ve, GlKeys.Up); Key(ve, GlKeys.End);
                Key(ve, GlKeys.Enter);
                var ce = (ChalkTextArea)Box(false);
                Type(ce, "aaaa bbbb cccc dddd eeee ffff gggg hhhh iiii jjjj");
                Key(ce, GlKeys.Home); Key(ce, GlKeys.Up); Key(ce, GlKeys.End);
                int at = ce.CaretPosWithoutLineBreaks;
                Key(ce, GlKeys.Enter);
                Check(Wrapped(ce) && ce.CaretPosWithoutLineBreaks == at + 1 && ce.GetText()[at] == '\n',
                    $"Enter at the end of a line that wrapped: the lines are the text's ({ce.GetLines().Count}; vanilla's list has {ve.GetLines().Count}, " +
                    $"wrapping its text gives {ve.Lineize(ve.GetText()).Count}), the caret after the break");

                // up and down keep the caret's place across the line
                double x0 = area.CaretX;
                Key(area, GlKeys.Up);
                double x1 = area.CaretX;
                Key(area, GlKeys.Down);
                Check(area.CaretPosWithoutLineBreaks == full.Length && Math.Abs(x1 - x0) < area.LineHeight * 1.2,
                    $"up keeps the caret's place across the line ({x0:0} px, {x1:0} on the line above), down brings it back to the end");

                // Backspace at a line's start joins it to the line before; the caret at the join
                Key(area, GlKeys.Home);
                int lineStart = area.CaretPosWithoutLineBreaks;
                Key(area, GlKeys.BackSpace);
                Check(area.GetText() == "Note: " + text + "second" && area.CaretPosWithoutLineBreaks == lineStart - 1 && Wrapped(area),
                    $"Backspace at the start of a line joins it to the one before, the caret where they meet ({area.CaretPosWithoutLineBreaks})");
                Key(area, GlKeys.Delete);
                Check(area.GetText() == "Note: " + text + "econd" && area.CaretPosWithoutLineBreaks == lineStart - 1,
                    "Delete takes the character after the caret, which stays");

                // where the caret is drawn, the box's height and the scroll that keeps the caret in sight
                double scale = RuntimeEnv.GUIScale;
                Check(Math.Abs(area.CaretTop - area.CaretPosLine * area.LineHeight) < 1e-9 &&
                      Math.Abs(area.ContentHeight - (area.GetLines().Count * area.LineHeight / scale + 6)) < 0.5,
                    $"the caret's line and the box's height ({area.ContentHeight:0} for {area.GetLines().Count} lines, in the dialog's units at GUI scale {scale})");
                Check(ChalkTextArea.ScrollToShow(0, 200, 220, 140, 300) == 80 && ChalkTextArea.ScrollToShow(80, 30, 50, 140, 300) == 30 &&
                      ChalkTextArea.ScrollToShow(30, 60, 80, 140, 300) == 30 && ChalkTextArea.ScrollToShow(250, 400, 420, 140, 300) == 160 &&
                      ChalkTextArea.ScrollToShow(50, 0, 20, 140, 100) == 0,
                    "the comment scrolls as little as it takes to show the caret's line, within its range");
            }
            catch (Exception e) { Check(false, "the comment box's checks ran: " + e.Message); }
            finally { RuntimeEnv.GUIScale = keepScale; }
        }
    }
}
