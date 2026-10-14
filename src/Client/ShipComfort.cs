using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Hansa
{
    /// <summary>ModConfig/hansa-client.json: how much of the sea each player feels (their own setting, their own screen).</summary>
    public class HansaClientConfig
    {
        /// <summary>
        /// 0..1: how much the ships you see move with the waves and lean in the wind (1 = all of it, 0 = a steady
        /// ship on a moving sea). Only on your screen: everyone else still sees the real motion.
        /// </summary>
        public float SeaMotion = 1;
        /// <summary>Keep the horizon level: your view doesn't tip with the deck (the ship still moves under you).</summary>
        public bool SteadyHorizon = false;
        /// <summary>Ships cast shadows on your screen (needs the game's own shadows on in the graphics settings).</summary>
        public bool ShipShadows = true;
        /// <summary>
        /// The wind panel aboard a ship (0.3.10): the true and apparent wind on a little compass round her, the force, and
        /// at the helm the point of sail. Toggled with Ctrl+U (rebindable: "Hansa: wind panel") or .hansa wind.
        /// </summary>
        public bool WindPanel = true;
        /// <summary>Masthead pennants streaming in the wind the ship feels (0.3.10).</summary>
        public bool Pennants = true;
    }

    /// <summary>.hansa: a slider for how much of the sea's motion you see, and the steady-horizon switch.</summary>
    public class GuiDialogSeaComfort : GuiDialog
    {
        readonly ShipClientSystem sys;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogSeaComfort(ICoreClientAPI capi, ShipClientSystem sys) : base(capi)
        {
            this.sys = sys;
            Compose();
        }

        void Compose()
        {
            var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bg.BothSizing = ElementSizing.FitToChildren;
            var motionLabel = ElementBounds.Fixed(0, 40, 340, 25);
            var slider = ElementBounds.Fixed(0, 68, 340, 20);
            var steady = ElementBounds.Fixed(0, 108, 30, 30);
            var steadyLabel = ElementBounds.Fixed(42, 112, 300, 25);
            var hint = ElementBounds.Fixed(0, 150, 340, 70);

            SingleComposer = capi.Gui.CreateCompo("hansa-seacomfort", dialog)
                .AddShadedDialogBG(bg)
                .AddDialogTitleBar(Lang.Get("hansa:comfort-title"), () => TryClose())
                .BeginChildElements(bg)
                    .AddStaticText(Lang.Get("hansa:comfort-seamotion"), CairoFont.WhiteSmallText(), motionLabel)
                    .AddSlider(OnSlider, slider, "seamotion")
                    .AddSwitch(OnSwitch, steady, "steady")
                    .AddStaticText(Lang.Get("hansa:comfort-steadyhorizon"), CairoFont.WhiteSmallText(), steadyLabel)
                    .AddStaticText(Lang.Get("hansa:comfort-hint"), CairoFont.WhiteDetailText(), hint)
                .EndChildElements()
                .Compose();
            SingleComposer.GetSlider("seamotion").SetValues((int)Math.Round(sys.Comfort.SeaMotion * 100), 0, 100, 5, "%");
            SingleComposer.GetSwitch("steady").On = sys.Comfort.SteadyHorizon;
        }

        bool OnSlider(int value)
        {
            sys.SetComfort(value / 100f, sys.Comfort.SteadyHorizon);
            return true;
        }

        void OnSwitch(bool on) => sys.SetComfort(sys.Comfort.SeaMotion, on);
    }
}
