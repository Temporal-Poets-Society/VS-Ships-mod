using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Hansa
{
    /// <summary>A line of text above the hotbar: what a fixture does when you look at it, and the helm readout.</summary>
    public class ShipHud : HudElement
    {
        string last = "";
        bool composed;
        readonly ShipClientSystem sys;

        public ShipHud(ICoreClientAPI capi, ShipClientSystem sys) : base(capi) { this.sys = sys; }

        public override string ToggleKeyCombinationCode => null;
        public override bool Focusable => false;

        void Compose()
        {
            var font = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center).WithStroke(new double[] { 0, 0, 0, 0.85 }, 2);
            var bounds = ElementBounds.Fixed(EnumDialogArea.CenterBottom, 0, -125, 780, 96);
            SingleComposer = capi.Gui.CreateCompo("hansa-hud", bounds)
                .AddDynamicText("", font, ElementBounds.Fixed(0, 0, 780, 96), "text")
                .Compose();
            composed = true;
        }

        /// <param name="extra">climbing and boarding hints, shown when no fixture is looked at</param>
        /// <param name="busy">what you're doing (mending, pumping): shown before anything else</param>
        public void Update(EntityPlayer ep, ShipClientSystem.FixtureHit look, string extra = null, string busy = null)
        {
            string text = "";
            if (busy != null) text = busy;
            else if (ep.MountedOn is HelmSeat helm)
                text = helm.Ship.Def.RowSpeed > 0 ? BoatHelmText(helm.Ship, WindPanel.ShownFor(sys, helm)) : HelmText(helm.Ship, WindPanel.ShownFor(sys, helm));
            else if (ep.MountedOn is RowSeat row) text = OarsText(row.Ship, WindPanel.ShownFor(sys, row));
            else if (ep.MountedOn is GunSeat gun) text = gun.Ship.GunHudText(gun.Fixture, ep, false);
            else if (look.Ship != null) text = FixtureHint(look.Ship, look.Fixture);
            else if (extra != null) text = extra;
            else if (ep.MountedOn is BunkSeat) text = Lang.Get("hansa:hud-bunk");
            // aboard a ship that's foundering (not at her helm, which says it itself): a warning over whatever else
            if (ep.MountedOn is ShipSeat on && on is not HelmSeat && on.Ship.Stage == SinkStage.Foundering)
                text = Lang.Get("hansa:hud-foundering", on.Ship.WaterText()) + (text.Length > 0 ? "\n" + text : "");
            // (0.3.19) building mode on, aboard (not at the helm, the oars or a gun): its marker over the rest
            if (VanillaPieceBlock.BuildingMode(ep) && ep.MountedOn is ShipSeat bs && bs is not HelmSeat && bs is not RowSeat && bs is not GunSeat)
                text = Lang.Get("hansa:hud-building", DecoClient.BuildKeyName(capi)) + (text.Length > 0 ? "\n" + text : "");

            if (text == last) return;
            last = text;
            if (!composed) Compose();
            SingleComposer.GetDynamicText("text").SetNewText(text);
            if (text.Length == 0) TryClose();
            else if (!IsOpened()) TryOpen();
        }

        static string FixtureHint(EntityShip ship, FixtureDef f)
        {
            string name = FixtureName(ship, f);
            switch (f.Type)
            {
                case "gate" when ship.OpenFittings.Contains(f.Code):
                    // ((0.3.27, note 219) the brow: boards in hand at the open gate run it out, or take it in)
                    return Lang.Get("hansa:hint-gate-close") + " · " + Lang.Get(ship.Brows.ContainsKey(f.Code) ? "hansa:hint-gate-brow-in" : "hansa:hint-gate-brow");
                case "door":
                case "gate":
                    return Lang.Get("hansa:hint-" + f.Type + (ship.OpenFittings.Contains(f.Code) ? "-close" : "-open"));
                case "hatch":
                    bool open = ship.OpenFittings.Contains(f.Code);
                    if (f.Storage != null) return Lang.Get(open ? "hansa:hint-hatch-storage-close" : "hansa:hint-hatch-storage-open",
                        FixtureName(ship, ship.Def.Fixture(f.Storage) ?? f));
                    return Lang.Get(open ? "hansa:hint-hatch-close" : "hansa:hint-hatch-open", name);
                case "attach":
                    if (ship.CoveredBy(f) is FixtureDef trunk) return Lang.Get("hansa:hint-attached-cargo", FixtureName(ship, trunk));
                    // (an empty place with a hint of its own: the stern lantern's)
                    if (!ship.Attached.ContainsKey(f.Code))
                        return Lang.HasTranslation("hansa:hint-attach-" + f.Code, false, false) ? Lang.Get("hansa:hint-attach-" + f.Code)
                            : Lang.Get("hansa:hint-attach-" + f.Kind);
                    if (f.Kind == "plate")
                    {
                        // the ship's spawn point: is mine bound here?
                        bool mine = ship.SpawnBound.Contains(ship.Api is ICoreClientAPI capi ? capi.World.Player?.PlayerUID ?? "" : "");
                        return Lang.Get(mine ? "hansa:hint-attached-plate-bound" : "hansa:hint-attached-plate", name);
                    }
                    return Lang.Get("hansa:hint-attached-" + f.Kind, name);
                case "figurehead":
                    return Lang.Get("hansa:hint-figurehead", Lang.Get("hansa:figurehead-" + (ship.Figurehead ?? f.Options?[0] ?? "none")));
                case "plaque":
                {
                    // her crew list (ShipCrew)
                    string uid = (ship.Api as ICoreClientAPI)?.World.Player?.PlayerUID;
                    if (!ship.HasCrewList) return Lang.Get("hansa:hint-plaque-none");
                    return Lang.Get(ship.IsCaptain(uid) ? "hansa:hint-plaque-captain" : ship.IsCrew(uid) ? "hansa:hint-plaque-crew" : "hansa:hint-plaque", ship.Crew.Count);
                }
                case "nameboard":
                    return ship.ShipName != null ? Lang.Get("hansa:hint-nameboard-named", ship.ShipName) : Lang.Get("hansa:hint-nameboard");
                case "stove":
                    return Lang.Get("hansa:hint-stove", name, Lang.Get(ship.LitStoves.Contains(f.Code) ? "hansa:lit" : "hansa:unlit"));
                case "hearth":
                {
                    // vanilla's firepit: open it, Shift adds one, a firestarter or a lit torch held to it lights it
                    if (f != ship.HearthFixture) return Lang.Get("hansa:hint-hearth-cold", name);
                    var held = (ship.Api as ICoreClientAPI)?.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                    // (0.3.18) a hearth put aboard without a flue up to open air won't draw
                    if (!ship.HearthFlued) return Lang.Get("hansa:hint-hearth-noflue", name, ship.HearthStateText());
                    if (EntityShip.Igniter(held) > 0 && ship.HearthIgnitable) return Lang.Get("hansa:hint-hearth-light", name, ship.HearthStateText());
                    return Lang.Get("hansa:hint-hearth", name, ship.HearthStateText());
                }
                case "oven":
                {
                    // vanilla's clay oven: firewood, something to bake, an empty hand takes out, a torch or firestarter lights it
                    if (f != ship.OvenFixture) return name;
                    var held = (ship.Api as ICoreClientAPI)?.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                    string state = ship.Oven.Burning ? Lang.Get("hansa:oven-state-burning", (int)ship.Oven.Temperature)
                        : ship.Oven.Temperature > 25 ? Lang.Get("hansa:oven-state-hot", (int)ship.Oven.Temperature) : Lang.Get("hansa:oven-state-cold");
                    if (EntityShip.Igniter(held) > 0 && ship.OvenIgnitable) return Lang.Get("hansa:hint-oven-light", name, state);
                    return Lang.Get("hansa:hint-oven", name, state);
                }
                case "anchor":
                    return Lang.Get("hansa:hint-anchor", name, Lang.Get(ship.Anchored ? "hansa:anchor-down" : "hansa:anchor-up"));
                case "pinrail":
                {
                    // (0.3.13, note 131) a second place to work her sails: what she has set, and the clicks
                    if (!ship.MastUp) return Lang.Get("hansa:hint-pinrail-mast-down", name);
                    string set = Lang.Get("hansa:sails-" + (ship.Crawl ? "sweeps" : ship.SailSet switch { <= 0 => "furled", 1 => "half", _ => "full" }));
                    return Lang.Get("hansa:hint-pinrail", name, set);
                }
                case "oars":
                    return ship.GetSeat("row:" + f.Code)?.Passenger != null ? Lang.Get("hansa:hint-oars-taken") : Lang.Get("hansa:hint-oars", name);
                case "seat":
                    // the heads: sit down on it (unless someone is)
                    return ship.GetSeat("sit:" + f.Code)?.Passenger != null ? Lang.Get("hansa:piece-seat-taken") : Lang.Get("hansa:hint-seat", name);
                case "mast":
                    return Lang.Get(ship.OpenFittings.Contains(f.Code) ? "hansa:hint-mast-raise" : "hansa:hint-mast-lower", name);
                case "pump":
                    // the bilge pump: how much water she has in her
                    return Lang.Get("hansa:hint-pump", ship.WaterText());
                case "gun":
                    // a gun: stand to it (unless someone is at it); ready or not, how it's laid
                    return Lang.Get(ship.GunSeatOf(f)?.Passenger != null ? "hansa:hint-gun-taken" : "hansa:hint-gun", name,
                        ship.GunText(f) + " · " + GunLaying.Text(ship.GunLayNow(f), f)) +
                        // (0.3.21, step C) a gun place's gun can be taken up in building mode
                        (ship.GunPlaceOf(f) != null && (ship.Api as Vintagestory.API.Client.ICoreClientAPI)?.World.Player?.Entity is EntityPlayer hep && VanillaPieceBlock.BuildingMode(hep)
                            ? " · " + Lang.Get("hansa:hint-gun-takeup") : "");
                case "compass":
                {
                    // the binnacle: her heading, as the card shows it
                    double heading = HansaMod.HeadingDeg(ship.Pos.Yaw);
                    string[] points = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
                    return Lang.Get("hansa:hint-compass", heading, points[(int)Math.Round(heading / 22.5) % 16]);
                }
                default:
                    return Lang.Get("hansa:hint-" + f.Type, name);
            }
        }

        /// <summary>What a fixture is called: the vanilla item on an attachment point, else the fixture's own name.</summary>
        public static string FixtureName(EntityShip ship, FixtureDef f)
        {
            if (f.Type == "attach" && ship.Attached.TryGetValue(f.Code, out var stack)) return stack.GetName();
            // a piece put aboard (its storage dialog's title): the block's own name
            if (f.Type == "piece") return ship.PieceNameByCode(f.Code);
            // (0.3.18) a hearth or an oven put aboard (its stand-in fixture): the piece's name
            if (f.Code != null && f.Code.StartsWith("piece-") && f.Type is "hearth" or "oven") return ship.PieceNameByCode(f.Code);
            string code = f.Code;
            int dash = code.LastIndexOf('-');
            string key = dash > 0 && int.TryParse(code.Substring(dash + 1), out _) ? code.Substring(0, dash) : code;
            if (Lang.HasTranslation("hansa:fixture-" + key, false, false)) return Lang.Get("hansa:fixture-" + key);
            // no name of its own (a door, an empty place for a lantern): what kind of thing it is
            return Lang.Get("hansa:fixturekind-" + (f.Type == "attach" ? "attach-" + f.Kind : f.Type));
        }

        /// <summary>
        /// At the helm: the sails set, her heading, the helm, what's wrong (anchored, aground, why she's slow), warnings
        /// (the sails straining, water, damage) and the keys. (0.3.12, the user: no information twice at the wheel.) With
        /// the wind panel up, her speed and the wind are only on the panel; with it off they're here again, in the panel's
        /// own words (<see cref="WindPanel"/>).
        /// </summary>
        static string HelmText(EntityShip ship, bool panel)
        {
            // ((0.3.25) under the sweeps: "Sails: furled, under the sweeps: pulling ahead / backing astern / holding her")
            string sails = Lang.Get("hansa:sails-" + (ship.Crawl ? "sweeps" : ship.SailSet switch { <= 0 => "furled", 1 => "half", _ => "full" }));
            if (ship.Crawl) sails += " " + Lang.Get("hansa:sweeps-" + (ship.CrawlDrive > 0 ? "ahead" : ship.CrawlDrive < 0 ? "astern" : "holding"));
            double heading = HansaMod.HeadingDeg(ship.Pos.Yaw);
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            string compass = names[(int)Math.Round(heading / 45) % 8];
            string rudder = ship.Rudder < -0.15f ? "◄ " : ship.Rudder > 0.15f ? " ►" : "";
            string anchor = ship.Anchored ? " · " + Lang.Get("hansa:anchor-down") : "";
            string aground = ship.Aground ? " · " + Lang.Get("hansa:aground-short") : ship.Waiting ? " · " + Lang.Get("hansa:waiting-short")
                : "";
            // (why her set sails give her little way: taken aback, in irons, her rig hurt, the water in her)
            if (ship.WhySlow != null && Lang.HasTranslation(ship.WhySlow, false, false)) aground += " · " + Lang.Get(ship.WhySlow);
            string text = Lang.Get("hansa:hud-helm", sails, heading, compass, rudder, anchor, aground);
            if (!panel) text += "\n" + WindText(ship);
            // the sails straining in a gale (the panel says it in colour too)
            var w = ship.WindHere(ship.Pos.X, ship.Pos.Z);
            if (WindPanel.SailWarning(ship, w, out string warn) > 0) text += "\n" + Lang.Get("hansa:hud-warning", warn);
            // water in her (and a warning when she's foundering), her rig and rudder when they're hurt
            if (ship.Stage == SinkStage.Foundering) text += "\n" + Lang.Get("hansa:hud-foundering", ship.WaterText());
            else if (ship.Water >= 0.5 || ship.WaterRate > 0.002) text += "\n" + ship.WaterText();
            string hurt = HurtText(ship);
            if (hurt != null) text += (ship.Water >= 0.5 || ship.Stage == SinkStage.Foundering ? " · " : "\n") + hurt;
            return text + "\n" + Lang.Get(ship.Crawl ? "hansa:hud-helm-keys-sweeps" : ship.Def?.RowSpeed > 0 ? "hansa:hud-helm-keys-boat" : "hansa:hud-helm-keys");
        }

        /// <summary>Her sails, masts, rigging and rudder when they're hurt ("Main course torn · rudder damaged"), or null.</summary>
        static string HurtText(EntityShip ship)
        {
            var l = ship.DamageParts;
            if (l == null || ship.PartDamage == null || !ship.DamageOn) return null;
            var said = new System.Collections.Generic.List<string>();
            foreach (var p in l.Parts)
            {
                if (p.Kind is DamageKind.Hull or DamageKind.Deck) continue;
                var st = ship.StateOfPart(p.Index);
                if (st == PartState.Sound) continue;
                said.Add(Lang.Get("hansa:hud-part-state", ship.PartName(p.Index), Lang.Get("hansa:state-" + st.ToString().ToLowerInvariant())));
            }
            return said.Count == 0 ? null : string.Join(" · ", said);
        }

        /// <summary>At a boat's oars: how she goes (her speed only when the wind panel isn't up) and the keys.</summary>
        static string OarsText(EntityShip ship, bool panel)
        {
            string how = Lang.Get(ship.Rowing > 0 ? "hansa:oars-pulling" : ship.Rowing < 0 ? "hansa:oars-backing" : "hansa:oars-resting");
            string extra = ship.Anchored ? " · " + Lang.Get("hansa:grapnel-down") : ship.Aground ? " · " + Lang.Get("hansa:aground-short") : "";
            if (!panel) extra = " · " + WindPanel.Speed(Math.Abs(ship.Speed)) + extra;
            return Lang.Get("hansa:hud-oars", how, extra) + "\n" + Lang.Get("hansa:hud-oars-keys");
        }

        /// <summary>At a boat's tiller: her sail (or her mast lowered), heading; her way and the wind when the wind panel isn't up.</summary>
        static string BoatHelmText(EntityShip ship, bool panel)
        {
            string sail = ship.MastUp ? Lang.Get("hansa:sails-" + ship.SailSet switch { <= 0 => "furled", 1 => "half", _ => "full" }) : Lang.Get("hansa:sail-mast-down");
            double heading = HansaMod.HeadingDeg(ship.Pos.Yaw);
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            string compass = names[(int)Math.Round(heading / 45) % 8];
            string rudder = ship.Rudder < -0.15f ? "◄ " : ship.Rudder > 0.15f ? " ►" : "";
            string anchor = ship.Anchored ? " · " + Lang.Get("hansa:grapnel-down") : "";
            string aground = ship.Aground ? " · " + Lang.Get("hansa:aground-short") : ship.Waiting ? " · " + Lang.Get("hansa:waiting-short") : "";
            string text = Lang.Get("hansa:hud-boat-helm", sail, heading, compass, rudder, anchor, aground);
            if (!panel) text += "\n" + (ship.MastUp ? WindText(ship) : WindPanel.MotionText(ship));
            return text + "\n" + Lang.Get("hansa:hud-boat-helm-keys");
        }

        /// <summary>
        /// With the wind panel off: her motion, the wind and how she lies to it, in the panel's words on two lines
        /// ("Ship: 9.0 kn (4.6 m/s) ahead · Wind: force 8 gale, 36 kn (19 m/s) from the SW", "On her sails: … · Beam
        /// reach, port tack · Best she can make here: …").
        /// </summary>
        static string WindText(EntityShip ship)
        {
            var w = ship.WindHere(ship.Pos.X, ship.Pos.Z);
            string text = WindPanel.MotionText(ship, false) + " · " + WindPanel.WindLine(w);
            if (w.Strength < 0.02) return text;
            text += "\n" + WindPanel.OnSailsLine(ship, w);
            if (WindPanel.HelmLines(ship, w, " · ") is string helm) text += " · " + helm;
            return text;
        }
    }
}
