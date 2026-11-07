using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Hansa
{
    /// <summary>
    /// The wind panel (0.3.10, the user: "no indicator of wind speed and direction"): whenever you're aboard a ship, a
    /// small compass at the right edge of the screen with her outline pointing up (her bow), the true wind (a pale arrow,
    /// from where it comes toward her) and the apparent wind (an orange one, what her sails feel), north marked on the
    /// rim. Toggled with Ctrl+U or .ships wind (HansaClientConfig.WindPanel).
    /// (0.3.12, the user found it confusing: units mixed, lines unlabelled, the helm's line saying it all again, the orange
    /// arrow taken for the no-go angle.) Each line names what it measures. Ship speed keeps blocks per second in brackets;
    /// true and apparent wind use knots only. At the helm: "Beam reach, port tack", "Best she can make here: …". The
    /// card has a red no-go wedge (the bow directions she can't point: the true wind ± the design's Sailing.NoGo) and a
    /// legend under it (wind pale, on her sails orange, her course green, no-go red). And a warning in colour when the
    /// wind is too strong for the sail she has set, before it tears them (<see cref="SailWear.Warning"/>, the same load
    /// as the server's strain): orange "Gale: shorten sail!", red "Storm: furl…" and "about to tear". The helm's line
    /// (<see cref="ShipHud"/>) leaves out what the panel shows while it's up.
    /// </summary>
    public class WindPanel : HudElement
    {
        const double Card = 116, Legend = 16, Width = 300, TextH = 112, WarnH = 36;
        readonly ShipClientSystem sys;
        bool composed;
        string lastText = "", lastWarn = "";
        int lastWarnLevel = -1;
        // what the compass was last drawn with (degrees, rounded), to redraw only when it changes
        int drawnBow = int.MinValue, drawnTrue, drawnApp, drawnNorth, drawnCourse, drawnNoGo;
        double relTrue, relApp, relNorth, trueLen, appLen, relCourse, courseLen, noGo, noGoFrom, noGoTo;
        bool hasWind, hasWay;
        long lastUpdateMs;

        public WindPanel(ICoreClientAPI capi, ShipClientSystem sys) : base(capi) { this.sys = sys; }

        public override string ToggleKeyCombinationCode => null;
        public override bool Focusable => false;
        public override double DrawOrder => 0.2;

        void Compose()
        {
            var bounds = ElementBounds.Fixed(EnumDialogArea.RightMiddle, -12, -60, Width, Card + Legend + TextH + WarnH);
            var font = CairoFont.WhiteDetailText().WithFontSize(13).WithOrientation(EnumTextOrientation.Center).WithStroke(new double[] { 0, 0, 0, 0.8 }, 1.5);
            // the warning: orange while the strain builds, red when they'd go even reefed or are about to tear
            var warnOrange = CairoFont.WhiteDetailText().WithFontSize(14).WithWeight(FontWeight.Bold).WithOrientation(EnumTextOrientation.Center)
                .WithColor(new double[] { 1, 0.62, 0.15, 1 }).WithStroke(new double[] { 0, 0, 0, 0.9 }, 2);
            var warnRed = CairoFont.WhiteDetailText().WithFontSize(14).WithWeight(FontWeight.Bold).WithOrientation(EnumTextOrientation.Center)
                .WithColor(new double[] { 1, 0.25, 0.2, 1 }).WithStroke(new double[] { 0, 0, 0, 0.9 }, 2);
            double y = Card + Legend;
            SingleComposer = capi.Gui.CreateCompo("ships-windpanel", bounds)
                .AddDynamicCustomDraw(ElementBounds.Fixed(0, 0, Width, Card + Legend + TextH + WarnH), Draw, "card")
                .AddDynamicText("", font, ElementBounds.Fixed(4, y + 2, Width - 8, TextH - 2), "text")
                .AddDynamicText("", warnOrange, ElementBounds.Fixed(4, y + TextH, Width - 8, WarnH - 2), "warnA")
                .AddDynamicText("", warnRed, ElementBounds.Fixed(4, y + TextH, Width - 8, WarnH - 2), "warnR")
                .Compose();
            composed = true;
        }

        /// <summary>Is the panel up for this seat (switched on, aboard and awake, a wind there: Sea's, or the open sea contracts')?</summary>
        public static bool ShownFor(ShipClientSystem sys, ShipSeat seat) =>
            sys != null && sys.Comfort.WindPanel && seat != null && seat is not BunkSeat && seat.Ship.Def != null &&
            (seat.Ship.SeaSys != null || seat.Ship.Mod?.Contracts?.HasWind == true);

        /// <summary>Every frame from ShipClientSystem: aboard (and not asleep) and switched on, it shows.</summary>
        public void Update(Vintagestory.API.Common.EntityPlayer ep)
        {
            var seat = ep?.MountedOn as ShipSeat;
            if (!ShownFor(sys, seat))
            {
                if (IsOpened()) TryClose();
                return;
            }
            long now = capi.ElapsedMilliseconds;
            if (IsOpened() && now - lastUpdateMs < 200) return;
            lastUpdateMs = now;
            if (!composed) Compose();
            if (!IsOpened()) TryOpen();

            var ship = seat.Ship;
            double yaw = ship.Pos.Yaw;
            var w = ship.WindHere(ship.Pos.X, ship.Pos.Z);
            double strength = w.Strength;
            hasWind = strength >= 0.02;
            Sailing.Apparent(w.X, w.Z, yaw, ship.Speed, out double ax, out double az);
            double appStrength = Math.Sqrt(ax * ax + az * az);
            relTrue = HelmMath.Relative(w.X, w.Z, yaw);
            relApp = HelmMath.Relative(ax, az, yaw);
            relNorth = HelmMath.Relative(0, -1, yaw);
            // (0.3.24) east, south, west on the card too
            relPoints[0] = relNorth; relPoints[1] = HelmMath.Relative(1, 0, yaw); relPoints[2] = HelmMath.Relative(0, 1, yaw); relPoints[3] = HelmMath.Relative(-1, 0, yaw);
            // the arrows' lengths: a light air short, a gale the full radius
            trueLen = Math.Clamp(0.45 + strength / 0.6 * 0.55, 0.45, 1);
            appLen = Math.Clamp(0.35 + appStrength / 0.6 * 0.45, 0.35, 0.8);
            // the no-go wedge, only for a ship that sails by the wind: the design's no-go angle either side of the wind
            noGo = 0;
            if (ship.SailByWind && ship.MastUp && ship.Def.Sailing != null)
            {
                // (cairo's angles on the card: the same wedge as the map's, HelmMath.MapNoGo)
                HelmMath.CardNoGo(w.X, w.Z, yaw, ship.SailingNow.NoGo, out noGoFrom, out noGoTo);
                noGo = (noGoTo - noGoFrom) / 2;
            }

            // her course over ground (green): where she's really going, relative to her bow
            string motion = HelmMath.Motion(ship.Speed, ship.Leeway, out double over, out relCourse);
            hasWay = motion != "stopped";
            courseLen = Math.Clamp(0.3 + over / 7 * 0.6, 0.3, 0.9);

            int bow = (hasWay ? 1 : 0) + (int)Math.Round(courseLen * 10) * 2, t = Deg(relTrue), a = Deg(relApp), n = Deg(relNorth), c = Deg(relCourse), g = Deg(noGo);
            if (bow != drawnBow || t != drawnTrue || a != drawnApp || n != drawnNorth || c != drawnCourse || g != drawnNoGo)
            {
                drawnBow = bow; drawnTrue = t; drawnApp = a; drawnNorth = n; drawnCourse = c; drawnNoGo = g;
                SingleComposer.GetCustomDraw("card")?.Redraw();
            }

            string text = MotionText(ship) + "\n" + Text(ship, seat, w);
            if (text != lastText)
            {
                lastText = text;
                SingleComposer.GetDynamicText("text").SetNewText(text);
            }
            int level = SailWarning(ship, w, out string warn);
            warn ??= "";
            if (warn != lastWarn || level != lastWarnLevel)
            {
                lastWarn = warn;
                lastWarnLevel = level;
                SingleComposer.GetDynamicText("warnA").SetNewText(level == 1 ? warn : "");
                SingleComposer.GetDynamicText("warnR").SetNewText(level >= 2 ? warn : "");
            }
        }

        static int Deg(double rad) => (int)Math.Round(rad * 180 / Math.PI / 2) * 2;

        /// <summary>A speed (blocks a second) as the panel and the helm say the ship's own speed: "9.0 kn (4.6 b/s)" (0.3.24, the user: "b/s hides the fake speed better").</summary>
        public static string Speed(double ms, bool tenths = true) =>
            Lang.Get(tenths ? "ships:speed" : "ships:speed-whole", ms * HelmMath.KnotsPerMetreSecond, ms);

        /// <summary>"Wind: force 8 gale, 36 kn from the SW", or "Wind: calm".</summary>
        public static string WindLine(Sea.Wind w)
        {
            double strength = w.Strength;
            if (strength < 0.02) return Lang.Get("ships:windpanel-calm");
            return Lang.Get("ships:windpanel-true", Sea.Beaufort.Force(strength), Sea.Beaufort.Name(strength),
                HelmMath.WindSpeed(HelmMath.MetresPerSecond(strength)), Sea.Compass.Name(w.FromDegrees));
        }

        /// <summary>Where a wind going this way relative to her bow comes from: "right ahead", "on the port beam"...</summary>
        public static string Where(double rel)
        {
            double from = Math.Abs(Math.IEEERemainder(rel + Math.PI, 2 * Math.PI)) * 180 / Math.PI;
            int side = Math.IEEERemainder(rel + Math.PI, 2 * Math.PI) >= 0 ? 1 : -1;
            string sideName = Lang.Get(side > 0 ? "ships:side-starboard" : "ships:side-port");
            return from < 15 ? Lang.Get("ships:wind-ahead") : from < 60 ? Lang.Get("ships:wind-bow", sideName)
                : from < 120 ? Lang.Get("ships:wind-beam", sideName) : from < 165 ? Lang.Get("ships:wind-quarter", sideName)
                : Lang.Get("ships:wind-astern");
        }

        /// <summary>"On her sails: 38 kn, on the port beam" (the apparent wind).</summary>
        public static string OnSailsLine(EntityShip ship, Sea.Wind w)
        {
            Sailing.Apparent(w.X, w.Z, ship.Pos.Yaw, ship.Speed, out double ax, out double az);
            return Lang.Get("ships:windpanel-apparent", HelmMath.WindSpeed(HelmMath.MetresPerSecond(Math.Sqrt(ax * ax + az * az))),
                Where(HelmMath.Relative(ax, az, ship.Pos.Yaw)));
        }

        /// <summary>
        /// At the helm of a ship sailing by the wind: how she lies to it ("Beam reach, port tack", "In irons", "Taken
        /// aback"), and the best she can make here under the sail she has set (or, furled, under full sail); null otherwise.
        /// </summary>
        public static string HelmLines(EntityShip ship, Sea.Wind w, string join = "\n")
        {
            if (!ship.SailByWind || !ship.MastUp || w.Strength < 0.02 || ship.Def?.Sailing == null) return null;
            float sail = ship.SailSet >= 2 ? 1f : ship.SailSet == 1 ? 0.55f : 0f;
            string point = HelmMath.PointOfSail(w.X, w.Z, ship.Pos.Yaw, Math.Max(sail, ship.SailFraction), ship.Speed, ship.SailingNow, out int tack);
            string text = Lang.Get("ships:windpanel-pos-" + point);
            if (point != "inirons" && point != "aback" && point != "running")
                text = Lang.Get("ships:windpanel-tack", text, Lang.Get(tack > 0 ? "ships:side-starboard" : "ships:side-port"));
            if (point is "inirons" or "aback") return text + (sail > 0 ? join + Lang.Get("ships:windpanel-bearaway") : "");
            double best = Sailing.TargetSpeed(w.X, w.Z, ship.Pos.Yaw, sail > 0 ? sail : 1, ship.MaxSpeedNow, ship.SailingNow);
            return text + join + Lang.Get(sail > 0 ? "ships:windpanel-best" : "ships:windpanel-best-full", Speed(best));
        }

        static string Text(EntityShip ship, ShipSeat seat, Sea.Wind w)
        {
            string text = WindLine(w);
            if (w.Strength >= 0.02) text += "\n" + OnSailsLine(ship, w);
            if (seat is HelmSeat && HelmLines(ship, w) is string helm) text += "\n" + helm;
            // ((0.3.25) under the sweeps A/D turn her directly: the helm doesn't work the other way going astern)
            if (seat is HelmSeat && ship.Speed < -0.05f && !ship.Crawl) text += "\n" + Lang.Get("ships:windpanel-astern");
            return text;
        }

        /// <summary>
        /// The warning for her sails (the panel's, in colour, and the helm's line): 0 none; 1 the wind too strong for the
        /// sail she has set, the strain building ("Gale: shorten sail!"); 2 too strong even reefed ("Storm: furl…"); 3 a tear
        /// close, or tearing. The same load as the server's strain (<see cref="SailWear.Warning"/>), from the shared wind and
        /// her synced sail, way and strain; none where the server doesn't tear sails.
        /// </summary>
        public static int SailWarning(EntityShip ship, Sea.Wind w, out string text)
        {
            text = null;
            // (0.3.24) her limits as the server has them (SailStrainForce, SailTearForce)
            var d = ship.SailLimits;
            if (d == null || !ship.DamageOn || !ship.SailsTear || ship.GoneDown || ship.Def?.Sailing == null) return 0;
            Sailing.Apparent(w.X, w.Z, ship.Pos.Yaw, ship.Speed, out double ax, out double az);
            bool aback = Sailing.Aback(w.X, w.Z, ship.Pos.Yaw, ship.SailingNow);
            int level = SailWear.Warning(d, ship.SailFraction, Math.Sqrt(ax * ax + az * az), aback, ship.SailStrain);
            if (level == 0) return 0;
            text = Lang.Get(level == 3 ? (ship.SailStrain >= 1 ? "ships:windpanel-warn-tearing" : "ships:windpanel-warn-close")
                : level == 2 ? "ships:windpanel-warn-storm" : "ships:windpanel-warn-gale");
            return level;
        }

        /// <summary>
        /// Her motion, for the panel and the helm's line: "Ship: 2.3 kn (1.2 m/s) ahead", "… astern · sweeps astern",
        /// "Ship: drifting to port, …" when she goes more sideways than along her keel, "Ship: stopped"; and what drives her
        /// by hand (the sweeps, a boat's oars).
        /// </summary>
        public static string MotionText(EntityShip ship, bool sweeps = true)
        {
            string word = HelmMath.Motion(ship.Speed, ship.Leeway, out double over, out _);
            string text = word switch
            {
                "stopped" => Lang.Get("ships:motion-stopped"),
                "sideways" => Lang.Get("ships:motion-sideways", Lang.Get(ship.Leeway > 0 ? "ships:side-starboard" : "ships:side-port"), Speed(over)),
                _ => Lang.Get("ships:motion-" + word, Speed(over))
            };
            if (sweeps && ship.Crawl) text += " · " + Lang.Get("ships:motion-sweeps");
            if (ship.Rowing != 0) text += " · " + Lang.Get(ship.Rowing > 0 ? "ships:motion-oars-ahead" : "ships:motion-oars-astern");
            return text;
        }

        readonly double[] relPoints = new double[4];

        void Draw(Context ctx, ImageSurface surface, ElementBounds b)
        {
            double s = GuiElement.scaled(1);
            double w = b.InnerWidth, h = b.InnerHeight;
            // the panel's ground
            GuiElement.RoundRectangle(ctx, 0, 0, w, h, 6 * s);
            ctx.SetSourceRGBA(0.08, 0.07, 0.06, 0.55);
            ctx.Fill();

            double cx = w / 2, cy = Card * s / 2 + 2 * s, r = Card * s / 2 - 8 * s;
            // the card
            ctx.Arc(cx, cy, r, 0, 2 * Math.PI);
            ctx.SetSourceRGBA(0.95, 0.92, 0.85, 0.12);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0.95, 0.92, 0.85, 0.55);
            ctx.LineWidth = 1.2 * s;
            ctx.Stroke();
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8;
                double r0 = i % 4 == 0 ? r - 7 * s : r - 4 * s;
                ctx.MoveTo(cx + Math.Sin(a) * r0, cy - Math.Cos(a) * r0);
                ctx.LineTo(cx + Math.Sin(a) * r, cy - Math.Cos(a) * r);
            }
            ctx.Stroke();
            // the cardinal points on the rim (the card turns with her: her bow is always up); (0.3.24, the user: "the wind
            // dial should mark N, E, S, W") north red, the others dark
            ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(9 * s);
            string[] points = { "N", "E", "S", "W" };
            for (int k = 3; k >= 0; k--)
            {
                double pa = relPoints[k];
                double nx = cx + Math.Sin(pa) * (r + 0.5 * s), ny = cy - Math.Cos(pa) * (r + 0.5 * s);
                ctx.Arc(nx, ny, 6 * s, 0, 2 * Math.PI);
                if (k == 0) ctx.SetSourceRGBA(0.75, 0.15, 0.1, 0.95);
                else ctx.SetSourceRGBA(0.12, 0.11, 0.1, 0.9);
                ctx.Fill();
                ctx.SetSourceRGBA(1, 1, 1, 1);
                var ext = ctx.TextExtents(points[k]);
                ctx.MoveTo(nx - ext.Width / 2 - ext.XBearing, ny - ext.Height / 2 - ext.YBearing);
                ctx.ShowText(points[k]);
            }

            // (0.3.12) the no-go wedge, red: the bow directions she can't point, the design's no-go angle either side of
            // where the true wind comes from (she's in it when it covers her bow, straight up)
            if (hasWind && noGo > 0)
            {
                // (cairo's angles run clockwise from +x; the card's from straight up: noGoFrom/To are already cairo's)
                double a0 = noGoFrom, a1 = noGoTo;
                ctx.MoveTo(cx, cy);
                ctx.Arc(cx, cy, r, a0, a1);
                ctx.ClosePath();
                ctx.SetSourceRGBA(0.9, 0.12, 0.1, 0.22);
                ctx.Fill();
                ctx.NewPath();
                ctx.Arc(cx, cy, r - 1.5 * s, a0, a1);
                ctx.SetSourceRGBA(0.95, 0.15, 0.1, 0.95);
                ctx.LineWidth = 3 * s;
                ctx.Stroke();
            }

            // the legend under the card: a swatch and a word for each colour
            {
                string[] words = { Lang.Get("ships:windpanel-legend-true"), Lang.Get("ships:windpanel-legend-app"),
                    Lang.Get("ships:windpanel-legend-course"), Lang.Get("ships:windpanel-legend-nogo") };
                double[][] colours = { new[] { 0.85, 0.93, 1.0 }, new[] { 1.0, 0.6, 0.15 }, new[] { 0.3, 0.9, 0.35 }, new[] { 0.95, 0.15, 0.1 } };
                ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
                ctx.SetFontSize(10 * s);
                double total = 0;
                var widths = new double[words.Length];
                for (int i = 0; i < words.Length; i++) { widths[i] = ctx.TextExtents(words[i]).XAdvance; total += 12 * s + widths[i] + (i > 0 ? 8 * s : 0); }
                double lx = (w - total) / 2, ly = (Card + Legend / 2) * s;
                for (int i = 0; i < words.Length; i++)
                {
                    if (i > 0) lx += 8 * s;
                    ctx.Rectangle(lx, ly - 3 * s, 9 * s, 6 * s);
                    ctx.SetSourceRGBA(colours[i][0], colours[i][1], colours[i][2], 1);
                    ctx.Fill();
                    lx += 12 * s;
                    ctx.MoveTo(lx, ly + 3.5 * s);
                    ctx.SetSourceRGBA(0.95, 0.93, 0.88, 0.95);
                    ctx.ShowText(words[i]);
                    lx += widths[i];
                }
            }

            // her outline, bow up
            double L = r * 0.62, B = r * 0.2;
            ctx.MoveTo(cx, cy - L);
            ctx.CurveTo(cx + B * 1.1, cy - L * 0.55, cx + B, cy + L * 0.2, cx + B * 0.8, cy + L * 0.85);
            ctx.LineTo(cx - B * 0.8, cy + L * 0.85);
            ctx.CurveTo(cx - B, cy + L * 0.2, cx - B * 1.1, cy - L * 0.55, cx, cy - L);
            ctx.ClosePath();
            ctx.SetSourceRGBA(0.55, 0.38, 0.22, 0.9);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0.15, 0.1, 0.05, 1);
            ctx.LineWidth = 1 * s;
            ctx.Stroke();

            // her course over ground (green), out from her middle: up when she goes ahead, down astern, sideways adrift
            if (hasWay)
            {
                double dx = Math.Sin(relCourse), dy = -Math.Cos(relCourse);
                double tipX = cx + dx * r * courseLen, tipY = cy + dy * r * courseLen;
                double px = -dy, py = dx, hl = 9 * s, hw = 5 * s;
                for (int pass = 0; pass < 2; pass++)
                {
                    ctx.MoveTo(cx, cy);
                    ctx.LineTo(tipX - dx * hl * 0.8, tipY - dy * hl * 0.8);
                    ctx.LineWidth = pass == 0 ? 5 * s : 3 * s;
                    if (pass == 0) ctx.SetSourceRGBA(0, 0, 0, 0.6); else ctx.SetSourceRGBA(0.3, 0.9, 0.35, 1);
                    ctx.Stroke();
                }
                ctx.MoveTo(tipX, tipY);
                ctx.LineTo(tipX - dx * hl + px * hw, tipY - dy * hl + py * hw);
                ctx.LineTo(tipX - dx * hl - px * hw, tipY - dy * hl - py * hw);
                ctx.ClosePath();
                ctx.SetSourceRGBA(0.3, 0.9, 0.35, 1);
                ctx.FillPreserve();
                ctx.SetSourceRGBA(0, 0, 0, 0.6);
                ctx.LineWidth = 1 * s;
                ctx.Stroke();
            }
            if (!hasWind) return;
            // the arrows point where the air goes, their tails on the windward side
            Arrow(ctx, cx, cy, r, relTrue, trueLen, 3.2 * s, s, 0.85, 0.93, 1.0);
            Arrow(ctx, cx, cy, r, relApp, appLen, 2.2 * s, s, 1.0, 0.6, 0.15);
        }

        static void Arrow(Context ctx, double cx, double cy, double r, double rel, double len, double width, double s, double red, double green, double blue)
        {
            double dx = Math.Sin(rel), dy = -Math.Cos(rel);
            double tailX = cx - dx * r * len, tailY = cy - dy * r * len;
            double headX = cx - dx * r * 0.18, headY = cy - dy * r * 0.18;
            ctx.MoveTo(tailX, tailY);
            ctx.LineTo(headX - dx * 6 * s, headY - dy * 6 * s);
            ctx.SetSourceRGBA(0, 0, 0, 0.6);
            ctx.LineWidth = width + 2 * s;
            ctx.Stroke();
            ctx.MoveTo(tailX, tailY);
            ctx.LineTo(headX - dx * 6 * s, headY - dy * 6 * s);
            ctx.SetSourceRGBA(red, green, blue, 1);
            ctx.LineWidth = width;
            ctx.Stroke();
            // the head
            double px = -dy, py = dx, hl = 10 * s, hw = 5.5 * s;
            ctx.MoveTo(headX, headY);
            ctx.LineTo(headX - dx * hl + px * hw, headY - dy * hl + py * hw);
            ctx.LineTo(headX - dx * hl - px * hw, headY - dy * hl - py * hw);
            ctx.ClosePath();
            ctx.SetSourceRGBA(red, green, blue, 1);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0, 0, 0, 0.6);
            ctx.LineWidth = 1 * s;
            ctx.Stroke();
        }
    }
}
