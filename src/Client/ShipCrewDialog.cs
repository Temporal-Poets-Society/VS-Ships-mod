using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// Right-clicking the bronze plaque by the great cabin's door (0.3.11): her crew list, each name a captain or crew.
    /// Anyone may read it. A captain writes names on (online, or a player the server has seen), takes them off, and makes
    /// others captains or not; anyone on it can take their own name off. While no one has signed it, the first to do so is
    /// her captain. The server decides (ShipCrew.ChangeCrew); the list comes back synced and the dialog redraws.
    /// </summary>
    public class GuiDialogShipCrew : GuiDialog
    {
        readonly EntityShip ship;
        readonly Action<string, string> send;

        public override string ToggleKeyCombinationCode => null;
        public EntityShip Ship => ship;

        public GuiDialogShipCrew(ICoreClientAPI capi, EntityShip ship, Action<string, string> send) : base(capi)
        {
            this.ship = ship;
            this.send = send;
            Compose();
        }

        bool ViewerCaptain()
        {
            var me = capi.World.Player;
            return ship.IsCaptain(me.PlayerUID) || me.Privileges?.Contains(Privilege.controlserver) == true;
        }

        /// <summary>Drawn again whenever her list changes (EntityShip.LoadCrew).</summary>
        public void Refresh()
        {
            if (!IsOpened()) return;
            string typed = SingleComposer?.GetTextInput("name")?.GetText() ?? "";
            Compose();
            SingleComposer.GetTextInput("name")?.SetValue(typed);
        }

        void Compose()
        {
            const int W = 420, Row = 30;
            var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bg.BothSizing = ElementSizing.FitToChildren;
            string me = capi.World.Player.PlayerUID;
            bool captain = ViewerCaptain();

            var c = capi.Gui.CreateCompo("ships-crew", dialog)
                .AddShadedDialogBG(bg)
                .AddDialogTitleBar(Lang.Get("ships:crew-title", ship.GetName()), () => TryClose())
                .BeginChildElements(bg);
            int y = 40;
            if (!ship.HasCrewList)
            {
                c.AddStaticText(Lang.Get("ships:crew-none"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, y, W, 50));
                y += 56;
                c.AddSmallButton(Lang.Get("ships:crew-claim"), () => { send("claim", null); return true; }, ElementBounds.Fixed(0, y, 200, 28));
                y += 40;
            }
            else
            {
                c.AddStaticText(Lang.Get(ship.LaidUp ? "ships:crew-laidup" : "ships:crew-incommission"), CairoFont.WhiteDetailText(), ElementBounds.Fixed(0, y, W, 22));
                y += 28;
                int i = 0;
                foreach (var m in ship.Crew.ToArray())
                {
                    string name = m.Name;
                    var font = m.Uid == me ? CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold) : CairoFont.WhiteSmallText();
                    c.AddStaticText(name, font, ElementBounds.Fixed(0, y + 4, 170, 24), "n" + i);
                    c.AddStaticText(Lang.Get(m.Captain ? "ships:crew-role-captain" : "ships:crew-role-crew"), CairoFont.WhiteDetailText(), ElementBounds.Fixed(175, y + 6, 70, 22), "r" + i);
                    if (captain)
                    {
                        c.AddSmallButton(Lang.Get(m.Captain ? "ships:crew-make-crew" : "ships:crew-make-captain"),
                            () => { send(m.Captain ? "crew" : "captain", name); return true; }, ElementBounds.Fixed(250, y, 95, 26), EnumButtonStyle.Small, "p" + i);
                        if (m.Uid != me)
                            c.AddSmallButton(Lang.Get("ships:crew-remove"), () => { send("remove", name); return true; }, ElementBounds.Fixed(350, y, 70, 26), EnumButtonStyle.Small, "x" + i);
                    }
                    if (m.Uid == me)
                        c.AddSmallButton(Lang.Get("ships:crew-leave"), () => { send("leave", null); return true; }, ElementBounds.Fixed(350, y, 70, 26), EnumButtonStyle.Small, "l" + i);
                    y += Row;
                    i++;
                }
                if (captain)
                {
                    y += 10;
                    c.AddStaticText(Lang.Get("ships:crew-add-label"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, y, W, 22));
                    y += 24;
                    c.AddTextInput(ElementBounds.Fixed(0, y, 300, 30), null, CairoFont.TextInput(), "name");
                    c.AddSmallButton(Lang.Get("ships:crew-add"), Add, ElementBounds.Fixed(310, y, 110, 30));
                    y += 40;
                }
                else if (!ship.IsCrew(me))
                {
                    c.AddStaticText(Lang.Get("ships:crew-ask"), CairoFont.WhiteDetailText(), ElementBounds.Fixed(0, y + 6, W, 40));
                    y += 46;
                }
            }
            c.AddSmallButton(Lang.Get("Close"), () => TryClose(), ElementBounds.Fixed(W - 110, y + 6, 110, 28));
            SingleComposer = c.EndChildElements().Compose();
            SingleComposer.GetTextInput("name")?.SetMaxLength(40);
        }

        bool Add()
        {
            var field = SingleComposer.GetTextInput("name");
            string name = field?.GetText()?.Trim();
            if (string.IsNullOrEmpty(name)) return true;
            send("add", name);
            field.SetValue("");
            return true;
        }

        public override void OnKeyDown(KeyEvent args)
        {
            if ((args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter) && SingleComposer.GetTextInput("name") != null)
            {
                Add();
                args.Handled = true;
                return;
            }
            base.OnKeyDown(args);
        }
    }
}
