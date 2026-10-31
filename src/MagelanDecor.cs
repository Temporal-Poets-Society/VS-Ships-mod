using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>Decorating: the packets, each design's paint plan (read in the background), the test commands.</summary>
    public partial class HansaMod
    {
        readonly Dictionary<string, Task<PaintPlan>> paintPlans = new();

        /// <summary>
        /// Each design with paint zones: its paint plan from its mesh file and shapes (the mesh file is a few MB of JSON: read in
        /// the background, as the renderer reads it). Both sides, from the same files: the cost the hint says is what's taken.
        /// </summary>
        void StartPaintPlans(ICoreAPI api)
        {
            paintPlans.Clear();
            foreach (var def in ships.Values)
            {
                if (def.PaintZones == null || def.PaintZones.Length == 0) continue;
                // (a grid ship's mesh is built in memory, GridShips; a generated design's is read from its file)
                var built = def.SkinFile;
                byte[] data = null;
                if (built == null)
                {
                    if (def.Shapes?.Skin == null) continue;
                    var skin = api.Assets.TryGet(AssetLocation.Create(def.Shapes.Skin, "ships"));
                    if (skin == null) continue;
                    data = skin.Data;
                }
                var shapes = new Dictionary<string, string>();
                foreach (var (role, loc) in new[] { ("exterior", def.Shapes?.Exterior), ("interior", def.Shapes?.Interior) })
                {
                    if (loc == null) continue;
                    var a = api.Assets.TryGet(AssetLocation.Create(loc, "ships").WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
                    if (a != null) shapes[role] = a.ToText();
                }
                var logger = Mod.Logger;
                var d = def;
                paintPlans[def.Code] = Task.Run(() =>
                {
                    try
                    {
                        var file = built ?? JsonConvert.DeserializeObject<ShipSkinFile>(System.Text.Encoding.UTF8.GetString(data));
                        var boxes = shapes.ToDictionary(kv => kv.Key, kv => PaintPlan.ShapeBoxes(kv.Value));
                        var plan = PaintPlan.Build(d, file, boxes);
                        logger.Notification("Ship '{0}': {1} paint zones, {2} of them on her meshes", d.Code, plan.Zones.Length,
                            plan.Area.Count(a => a > 0));
                        return plan;
                    }
                    catch (Exception e)
                    {
                        logger.Error("Ship '{0}': couldn't read her paint zones, she can't be painted: {1}", d.Code, e);
                        return null;
                    }
                });
            }
        }

        /// <summary>A design's paint plan, or null (none, or still being read).</summary>
        public PaintPlan PaintPlanFor(ShipDefinition def)
        {
            if (def?.Code == null || !paintPlans.TryGetValue(def.Code, out var t) || !t.IsCompletedSuccessfully) return null;
            return t.Result;
        }

        /// <summary>Whether a design has paint zones at all (its plan may still be loading).</summary>
        public bool HasPaint(ShipDefinition def) => def?.Code != null && paintPlans.ContainsKey(def.Code);

        void OnDecor(IServerPlayer player, DecorPacket p)
        {
            // (0.3.19) building mode: the player's own, set here (server-authoritative), read on both sides from the entity
            if (p.Action == DecorPacket.Build)
            {
                if (player.Entity == null) return;
                bool on = p.Dir == 2 ? !VanillaPieceBlock.BuildingMode(player.Entity) : p.Dir == 1;
                VanillaPieceBlock.SetBuilding(player.Entity, on);
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, on ? "ships:build-now-on" : "ships:build-now-off"), EnumChatType.Notification);
                return;
            }
            var ship = ShipById(p.ShipId);
            var ep = player.Entity;
            if (ship?.Def == null || ep == null || !ep.Alive) return;
            // (review H1-4) someone not of her crew may still open her doors and hatches put aboard and sit on her seats, as
            // the design's (OnFixture), while she's not laid up; nothing else
            bool guestUse = p.Action == DecorPacket.Use && !ship.LaidUp && EntityShip.GuestMayUse(ship.Pieces.Get(p.Piece));
            if (!guestUse && CrewOnly(player, ship)) return;
            string err = null;
            object[] args = Array.Empty<object>();
            switch (p.Action)
            {
                case DecorPacket.Place:
                    // ((25 October 2026, note 89) the way toward the player, and a door's hinge end: PiecePlanner.HingeHigh)
                    err = ship.TryPlacePiece(player, p.Mount, ((p.Dir % 8) + 8) % 8);
                    break;
                case DecorPacket.Remove:
                    err = ship.TryRemovePiece(player, p.Piece);
                    break;
                case DecorPacket.Use:
                    ship.UsePiece(player, p.Piece);
                    break;
                case DecorPacket.CellTake:
                    err = ship.TryTakeCell(player, p.Mount);
                    break;
                case DecorPacket.CellLay:
                    err = ship.TryLayCell(player, p.Mount);
                    break;
                case DecorPacket.CellHatch:
                    err = ship.TryPlaceHatch(player, p.Mount);
                    break;
                case DecorPacket.GunMount:
                    err = ship.TryMountGun(player, p.Mount);
                    break;
                case DecorPacket.HullTake:
                    err = ship.TryTakeHull(player, p.Mount);
                    break;
                case DecorPacket.HullLay:
                    err = ship.TryLayHull(player, p.Mount);
                    break;
                case DecorPacket.GunTake:
                    err = ship.TryTakeGun(player, p.Mount);
                    break;
                case DecorPacket.Rotate:
                    err = ship.TryRotatePiece(player, p.Piece);
                    break;
                case DecorPacket.Paint:
                    // (0.3.12: a wall put aboard, by its id, when no zone is given)
                    err = p.Zone == null && p.Piece > 0 ? ship.TryPaintPiece(player, p.Piece, out args) : ship.TryPaint(player, p.Zone, new Vec3d(p.X, p.Y, p.Z), out args);
                    break;
            }
            if (err != null) player.SendIngameError("ships-decor", Lang.GetL(player.LanguageCode, err, args));
        }

        // ------------------------------------------------------------------ commands

        void RegisterDecorCommands(ICoreServerAPI api)
        {
            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands.GetOrCreate("ships")
                .BeginSubCommand("pieces")
                    .WithDescription("The pieces (vanilla blocks put aboard) on the ship you're on (else the nearest): 'list' (default), 'clear' takes them all away (their items and what's in them dropped by you)")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalWord("what"))
                    .HandleWith(args => PiecesCommand((IServerPlayer)args.Caller.Player, args[0] as string))
                .EndSubCommand()
                .BeginSubCommand("place")
                    .WithDescription("Put a vanilla block on a mount point of the ship you're on (else the nearest), no item needed: <block code> <mount id> [facing: bow|starboard|stern|port, default starboard]. /ships mounts lists the ids.")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.Word("block"), parsers.Word("mount"), parsers.OptionalWord("facing"))
                    .HandleWith(args => PlaceCommand((IServerPlayer)args.Caller.Player, args[0] as string, args[1] as string, args[2] as string))
                .EndSubCommand()
                .BeginSubCommand("paint")
                    .WithDescription("Paint a zone of the ship you're on (else the nearest), no dye needed: <zone code|all> <blue|red|yellow|purple|woad|green|orange|black|gray|pink|white|gold|none>")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.Word("zone"), parsers.Word("colour"))
                    .HandleWith(args => PaintCommand((IServerPlayer)args.Caller.Player, args[0] as string, args[1] as string))
                .EndSubCommand()
                .BeginSubCommand("zones")
                    .WithDescription("The paint zones of the ship you're on (else the nearest): each one's area, what painting and gilding it costs, how it's painted now")
                    .HandleWith(args => ZonesCommand((IServerPlayer)args.Caller.Player))
                .EndSubCommand()
                .BeginSubCommand("gunplaces")
                    .WithDescription("(0.3.21) The gun places of the ship you're on (else the nearest) and their guns; 'mount <id|all>' / 'take <id|all>' a gun (no item), 'reset' as designed")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalWord("what"), parsers.OptionalWord("id"))
                    .HandleWith(args => GunPlacesCommand((IServerPlayer)args.Caller.Player, args[0] as string, args[1] as string))
                .EndSubCommand()
                .BeginSubCommand("slots")
                    .WithDescription("(0.3.20) The deck cells of the ship you're on (else the nearest): how many, how many out; 'take <id>' / 'lay <id>' a cell (no tools), 'reset' all as designed")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalWord("what"), parsers.OptionalWord("id"))
                    .HandleWith(args => SlotsCommand((IServerPlayer)args.Caller.Player, args[0] as string, args[1] as string))
                .EndSubCommand()
                .BeginSubCommand("hull")
                    .WithDescription("(0.3.26) The hull panels of the ship you're on (else the nearest): how many, which are out or cracked; 'take <id>' / 'lay <id>' / 'crack <id>' a panel (no tools), 'reset' all in and sound")
                    .RequiresPrivilege(Privilege.gamemode)
                    .WithArgs(parsers.OptionalWord("what"), parsers.OptionalWord("id"))
                    .HandleWith(args => HullCommand((IServerPlayer)args.Caller.Player, args[0] as string, args[1] as string))
                .EndSubCommand()
                .BeginSubCommand("mounts")
                    .WithDescription("Where pieces may go on the ship you're on (else the nearest): how many places in each room and deck, free and taken; 'ids [filter]' lists them")
                    .WithArgs(parsers.OptionalWord("what"), parsers.OptionalWord("filter"))
                    .HandleWith(args => MountsCommand((IServerPlayer)args.Caller.Player, args[0] as string, args[1] as string))
                .EndSubCommand();
        }

        /// <summary>(0.3.20, step B) <c>/ships slots [take|lay|reset] [id]</c>: her deck cells.</summary>
        TextCommandResult GunPlacesCommand(IServerPlayer player, string what, string id)
        {
            var ship = ShipFor(player.Entity);
            if (ship?.Def == null) return TextCommandResult.Error("No ship within 64 blocks.");
            if (ship.Def.GunPlaces == null || ship.Def.GunPlaces.Length == 0 || what == null || what == "list") return TextCommandResult.Success(ship.GunPlacesText());
            var places = id == "all" || what == "reset" ? ship.Def.GunPlaces : new[] { ship.GunPlace(id) };
            if (places.Length == 1 && places[0] == null) return TextCommandResult.Error($"No gun place '{id}'. {ship.GunPlacesText()}");
            var errs = new List<string>();
            foreach (var gp in places)
            {
                bool want = what == "reset" ? gp.StartsLaid : what == "mount";
                if (ship.GunPlaceLaid(gp) == want) continue;
                string err = want ? ship.MountGun(gp, true) : ship.TakeGun(gp);
                if (err != null) errs.Add(gp.Id + ": " + Lang.GetL(player.LanguageCode, err));
            }
            return errs.Count == 0 ? TextCommandResult.Success(ship.GunPlacesText()) : TextCommandResult.Error(string.Join("; ", errs) + ". " + ship.GunPlacesText());
        }

        TextCommandResult SlotsCommand(IServerPlayer player, string what, string id)
        {
            var ship = ShipFor(player.Entity);
            if (ship?.Def == null) return TextCommandResult.Error("No ship within 64 blocks.");
            if (ship.Slots == null) return TextCommandResult.Success(ship.SlotsText());
            switch (what ?? "list")
            {
                case "take":
                case "lay":
                {
                    int n = ship.SlotLayout.IndexOf(id);
                    if (n < 0) return TextCommandResult.Error($"No deck cell '{id}' (ids: <deck>:<i>:<k>, e.g. {ship.SlotLayout.Slots[0].Id}).");
                    string err = what == "take" ? ship.TakeCell(n, null) : ship.LayCell(n, true);
                    return err == null ? TextCommandResult.Success($"{id} {(what == "take" ? "taken up" : "laid")}. {ship.SlotsText()}") : TextCommandResult.Error(Lang.GetL(player.LanguageCode, err));
                }
                case "reset":
                {
                    int bad = 0;
                    for (int n = 0; n < ship.SlotLayout.Count; n++)
                    {
                        bool want = !ship.SlotLayout.Slots[n].StartsEmpty;
                        if (ship.Slots.Filled(n) == want) continue;
                        if ((want ? ship.LayCell(n, true) : ship.TakeCell(n, null)) != null) bad++;
                    }
                    return TextCommandResult.Success($"Her deck cells as designed{(bad > 0 ? $" ({bad} couldn't be: something on or in them)" : "")}. {ship.SlotsText()}");
                }
            }
            return TextCommandResult.Success(ship.SlotsText());
        }

        /// <summary>(0.3.26, step D1) <c>/ships hull [take|lay|crack|reset] [id]</c>: her hull panels.</summary>
        TextCommandResult HullCommand(IServerPlayer player, string what, string id)
        {
            var ship = ShipFor(player.Entity);
            if (ship?.Def == null) return TextCommandResult.Error("No ship within 64 blocks.");
            if (ship.HullPanels == null) return TextCommandResult.Success(ship.HullText());
            var l = ship.HullLayout;
            switch (what ?? "list")
            {
                case "take":
                case "lay":
                case "crack":
                {
                    int n = l.IndexOf(id);
                    if (n < 0) return TextCommandResult.Error($"No hull panel '{id}' (ids: hull:<p|s>:<station>:<band>, e.g. {l.Slots[0].Id}).");
                    string err = null;
                    if (what == "take") err = ship.TakeHull(n, null);
                    else if (what == "lay") err = ship.LayHull(n, true);
                    else ship.SetHullState(n, SlotState.Cracked);
                    return err == null ? TextCommandResult.Success($"{id} {(what == "take" ? "taken out" : what == "lay" ? "laid" : "cracked")}. {ship.HullText()}") : TextCommandResult.Error(Lang.GetL(player.LanguageCode, err));
                }
                case "reset":
                {
                    for (int n = 0; n < l.Count; n++)
                        if (ship.HullPanels[n] != SlotState.Filled) ship.SetHullState(n, SlotState.Filled);
                    return TextCommandResult.Success($"Her hull panels all in and sound. {ship.HullText()}");
                }
            }
            return TextCommandResult.Success(ship.HullText());
        }

        TextCommandResult PiecesCommand(IServerPlayer player, string what)
        {
            var ship = ShipFor(player.Entity);
            if (ship?.Def == null) return TextCommandResult.Error("No ship within 64 blocks.");
            if ((what ?? "list") == "clear")
            {
                int n = 0;
                // (the highest first: a piece standing on another goes before it)
                foreach (var p in ship.Pieces.All.OrderByDescending(x => x.Box?[1] ?? 0).ToArray())
                {
                    if (ship.Storages.TryGetValue(p.StorageCode, out var inv))
                        foreach (var s in inv)
                            if (!s.Empty) { sapi.World.SpawnItemEntity(s.Itemstack, player.Entity.Pos.XYZ.Add(0, 0.5, 0)); s.Itemstack = null; }
                    if (p.Stack != null) sapi.World.SpawnItemEntity(p.Stack.Clone(), player.Entity.Pos.XYZ.Add(0, 0.5, 0));
                    foreach (var seat in ship.Seats)
                        if (seat is FixtureSeat fs && fs.Fixture?.Code == p.StorageCode && seat.Passenger is EntityAgent a) a.TryUnmount();
                    if (ship.RemovePiece(p, null) == null) n++;
                }
                return TextCommandResult.Success($"{ship.GetName()}: {n} pieces taken away, their items dropped by you.");
            }
            if (ship.Pieces.Count == 0) return TextCommandResult.Success($"{ship.GetName()} has no pieces aboard.");
            var lines = ship.Pieces.All.Select(p =>
            {
                string inv = ship.Storages.TryGetValue(p.StorageCode, out var i) ? $", {i.Count(s => !s.Empty)}/{i.Count} slots used" : "";
                return $"#{p.Id} {p.Stack?.Collectible?.Code} ({p.Role.ToString().ToLowerInvariant()}) on {string.Join("+", p.Mounts)} facing {MountLayout.DirCode[p.Dir]}{inv}";
            });
            return TextCommandResult.Success($"{ship.GetName()}: {ship.Pieces.Count} pieces\n" + string.Join("\n", lines));
        }

        static int DirArg(string s) => (s ?? "starboard").ToLowerInvariant() switch
        {
            "bow" or "east" or "0" or "+x" => 0,
            "starboard" or "south" or "1" or "+z" => 1,
            "stern" or "west" or "2" or "-x" => 2,
            "port" or "north" or "3" or "-z" => 3,
            _ => -1
        };

        TextCommandResult PlaceCommand(IServerPlayer player, string blockCode, string mountId, string facing)
        {
            var ship = ShipFor(player.Entity);
            if (ship?.Mounts == null) return TextCommandResult.Error("No ship within 64 blocks.");
            var block = sapi.World.GetBlock(new AssetLocation(blockCode ?? ""));
            if (block == null || block.Id == 0) return TextCommandResult.Error($"No block '{blockCode}'.");
            var m = ship.Mounts.Get(mountId);
            if (m == null) return TextCommandResult.Error($"{ship.GetName()} has no mount point '{mountId}' (/ships mounts ids lists them).");
            int dir = DirArg(facing);
            if (dir < 0) return TextCommandResult.Error("Facing: bow, starboard, stern or port.");
            var stack = new ItemStack(block);
            var plan = PiecePlanner.Plan(ship.Mounts, m, new VanillaPieceBlock(sapi.World, stack), dir);
            string err = ship.PlacePiece(plan, stack, false, out var placed);
            return err != null ? TextCommandResult.Error(Lang.Get(err)) :
                TextCommandResult.Success($"#{placed.Id} {block.Code} on {string.Join("+", placed.Mounts)} of {ship.GetName()} ({placed.Role.ToString().ToLowerInvariant()}), facing {MountLayout.DirCode[placed.Dir]}.");
        }

        TextCommandResult PaintCommand(IServerPlayer player, string zone, string colour)
        {
            var ship = ShipFor(player.Entity);
            if (ship?.Def == null) return TextCommandResult.Error("No ship within 64 blocks.");
            var plan = PaintPlanFor(ship.Def);
            if (plan == null || plan.Zones.Length == 0)
                return TextCommandResult.Error(HasPaint(ship.Def) ? "Her paint zones are still being read." : $"The {ship.Def.Code} has no paint zones yet (the design needs paintZones and the mesh paintQuads/paintFaces).");
            colour = colour?.ToLowerInvariant();
            string paint = colour is "none" or "bare" or "default" ? null : colour;
            if (paint != null && !ShipPaint.Known(paint)) return TextCommandResult.Error($"No dye '{colour}' (blue, red, yellow, purple, woad, green, orange, black, gray, pink, white; gold; none).");
            // (0.3.17) "all": not a zone with nothing of hers in it on this design
            var zones = zone == "all" ? plan.Zones.Where((z, i) => plan.Area[i] > 0).ToList() : plan.Zone(zone) is PaintZoneDef one ? new List<PaintZoneDef> { one } : null;
            if (zones == null) return TextCommandResult.Error($"No zone '{zone}' (/ships zones lists them).");
            int n = 0;
            foreach (var z in zones)
            {
                if (paint == ShipPaint.Gold && !ShipPaint.Gildable(z)) continue;
                ship.SetPaint(z.Code, paint);
                n++;
            }
            return TextCommandResult.Success($"{ship.GetName()}: {n} zone{(n == 1 ? "" : "s")} {(paint == null ? "scraped back" : paint == ShipPaint.Gold ? "gilded" : "painted " + paint)}.");
        }

        TextCommandResult ZonesCommand(IServerPlayer player)
        {
            var ship = ShipFor(player.Entity);
            if (ship?.Def == null) return TextCommandResult.Error("No ship within 64 blocks.");
            var plan = PaintPlanFor(ship.Def);
            if (plan == null || plan.Zones.Length == 0)
                return TextCommandResult.Success(HasPaint(ship.Def) ? "Her paint zones are still being read." : $"The {ship.Def.Code} has no paint zones yet.");
            var inv = CultureInfo.InvariantCulture;
            // (0.3.17) not the zones with nothing of hers in them on this design (carrack-m's front mouldings, its casings)
            var lines = plan.Zones.Select((z, i) => plan.Area[i] <= 0 ? null : string.Format(inv, "{0} ({1}, {2}): {3:0.0} m², {4} quads, {5} faces; dye {6:0.00} L{7}; now {8}",
                z.Code, z.Kind, z.Default, plan.Area[i], plan.QuadCount[i], plan.FaceCount[i], ShipPaint.DyeItems(plan.Area[i]) / (double)ShipPaint.DyeItemsPerLitre,
                ShipPaint.Gildable(z) ? $", gold {ShipPaint.GoldPlates(plan.Area[i])} plates" : "", ship.Paint.Of(z.Code) ?? "as built")).Where(x => x != null).ToList();
            return TextCommandResult.Success($"{ship.GetName()}: {lines.Count} paint zones\n" + string.Join("\n", lines));
        }

        TextCommandResult MountsCommand(IServerPlayer player, string what, string filter)
        {
            var ship = ShipFor(player.Entity);
            if (ship?.Mounts == null) return TextCommandResult.Error("No ship within 64 blocks.");
            var l = ship.Mounts;
            if (what == "ids")
            {
                var ids = l.All.Where(m => filter == null || m.Id.Contains(filter)).Select(m => m.Id + (ship.Pieces.Taken(m.Id) ? " (taken)" : "")).Take(200);
                return TextCommandResult.Success(string.Join(", ", ids));
            }
            var lines = l.All.Where(m => m.Level == 0).GroupBy(m => (m.Kind, m.Room)).OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Room)
                .Select(g => $"{g.Key.Kind.ToString().ToLowerInvariant()} {g.Key.Room}: {g.Count()} ({g.Count(m => ship.Pieces.Taken(m.Id))} taken)");
            int stackedOpen = l.Stacked.Count(m => ship.Pieces.Supports(l, m));
            return TextCommandResult.Success($"{ship.GetName()}: {l.All.Count} mount points ({l.Floors.Count()} floor places, {l.Walls.Count()} wall spots, {l.Hooks.Count()} hooks, " +
                $"{l.Stacked.Count()} places on top of pieces, {stackedOpen} with a piece under them); " +
                $"{ship.Pieces.Count} pieces aboard\n" + string.Join("\n", lines) + (l.OnShipGrid ? "\non the ship's block grid: " : "\nalignments: ") +
                string.Join(", ", l.Align.Select(a => string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.00}, {2:0.00})", a.Key, a.Value.X, a.Value.Z))));
        }
    }
}
