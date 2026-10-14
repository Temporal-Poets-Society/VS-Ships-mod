using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace Hansa
{
    /// <summary>
    /// Decorating, the client's side: what the crosshair is on (a piece put aboard, a free mount point while holding a block,
    /// a paint zone while holding dye, gold plates or a knife), when that's nearer than any fixture; the hint for it; the
    /// outline drawn round it, as vanilla outlines a block (the mount point's footprint for the way the block would face,
    /// or the piece's selection box), in her moving, tilting frame; the clicks: right-click puts the block in hand, uses a
    /// piece or paints the zone, holding left mouse on a piece takes it up (its hit sound and a puff of its texture as it
    /// goes, a percentage above the hotbar). The server decides (DecorPacket).
    /// </summary>
    public class DecoClient : IRenderer
    {
        readonly ICoreClientAPI capi;
        readonly ShipClientSystem sys;
        readonly IClientNetworkChannel channel;
        WireframeCube cube, repairCube;
        double[] repairBox;

        public double RenderOrder => 0.9;
        public int RenderRange => 9999;

        /// <summary>Inspect: a free mount point under the crosshair while holding the surveyor's chalk (its outline and id, for notes).</summary>
        /// <summary>PaintPiece (0.3.12): a wall put aboard (a partition, a panel, a door) while holding dye or a knife.</summary>
        /// <summary>Cell (0.3.20, step B): a deck cell in building mode: taken up with an axe, an opening laid with planks or a hatch laid over it with trapdoors.</summary>
        /// <summary>GunPlace (0.3.21, step C): an empty gun place in building mode with the gun item in hand (Zone: the place's index).</summary>
        /// <summary>Hull (0.3.26, step D1): a hull panel in building mode: taken out with an axe (Cell: its index, CellTake), laid with planks (CellLay).</summary>
        public enum TargetKind { None, Piece, Mount, Zone, Shown, Inspect, PaintPiece, Cell, GunPlace, Hull }

        /// <summary>(0.3.20) What a deck cell in the crosshair is for: taking it up, laying it, a hatch over its opening.</summary>
        public const int CellTake = 1, CellLay = 2, CellHatch = 3;
        /// <summary>(0.3.27, note 201: "removed the hatches but could not find the item to replace them") an opening in building mode with nothing that lays it in hand: what does.</summary>
        public const int CellInfo = 4;

        public struct Target
        {
            public TargetKind Kind;
            public EntityShip Ship;
            public ShipPiece Piece;
            public MountPoint Mount;
            public PiecePlan Plan;
            public string Problem;
            public int Zone;
            public Vec3d At;
            public double Dist;
            /// <summary>(0.3.20) Cell: the deck slot (-1: a fixed part of a deck), and what's done to it (CellTake, CellLay, CellHatch).</summary>
            public int Cell, CellMode;
        }

        /// <summary>This frame's target (Kind None: nothing of decorating's nearer than a fixture).</summary>
        public Target Look;
        /// <summary>The HUD's line for it, and while a piece is being taken up.</summary>
        public string Hint, Busy;

        // taking a piece up: which, since when, the last hit sound
        EntityShip takingShip;
        int takingPiece;
        long takingSince, takingSound;
        bool takingSent;
        // (0.3.20) taking a deck cell up: which (-1 none); (0.3.26) or a hull panel out
        int takingCell = -1;
        bool takingHull;
        // the plans for the places by the crosshair, kept while nothing changes (0.3.18: a few are tried each frame)
        readonly Dictionary<(long Ship, string Mount, string Block, int Dir, int Version, bool Building), PiecePlan> plans = new();

        public DecoClient(ICoreClientAPI capi, ShipClientSystem sys, IClientNetworkChannel channel)
        {
            this.capi = capi;
            this.sys = sys;
            this.channel = channel;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "hansa-decor");
            // (0.3.19, the user: "the saw can't be held in the offhand") building mode: a hotkey (vanilla's controls list it)
            capi.Input.RegisterHotKey(BuildHotKey, Lang.Get("hansa:hotkey-build"), GlKeys.B, HotkeyType.CharacterControls);
            capi.Input.SetHotKeyHandler(BuildHotKey, _ => { SendBuild(null); return true; });
        }

        /// <summary>(0.3.19) Building mode's hotkey (B unless the player changes it in the controls).</summary>
        public const string BuildHotKey = "hansabuild";

        /// <summary>(0.3.19) Ask the server to turn building mode on, off, or (null) the other way. The server decides; the HUD follows the player entity.</summary>
        public void SendBuild(bool? on) =>
            channel.SendPacket(new DecorPacket { Action = DecorPacket.Build, Dir = on == null ? 2 : on.Value ? 1 : 0 });

        /// <summary>(0.3.19) The key building mode is toggled with, as the controls have it now.</summary>
        public static string BuildKeyName(ICoreClientAPI capi)
        {
            try { return capi?.Input.GetHotKeyByCode(BuildHotKey)?.CurrentMapping?.ToString() ?? "B"; }
            catch (Exception) { return "B"; }
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            cube?.Dispose();
            repairCube?.Dispose();
            cube = repairCube = null;
        }

        Vec3d EyePos(EntityPlayer ep) => ep.CameraPos.AddCopy(ep.LocalEyePos);

        // ------------------------------------------------------------------ what the crosshair is on

        /// <summary>Every frame: the target, if it's nearer than the fixture in the crosshair (fixtureDist), and its hint.</summary>
        public void Update(EntityPlayer ep, double fixtureDist)
        {
            Look = default;
            Hint = null;
            stackOver = false;
            var player = capi.World.Player;
            if (player == null || ep == null) return;
            double range = player.WorldData.PickingRange;
            var eye = EyePos(ep);
            var vd = EntityPos.GetViewVector(ep.Pos.Pitch, ep.Pos.Yaw);
            var dir = new Vec3d(vd.X, vd.Y, vd.Z);
            var held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            bool block = held?.Block != null && held.Block.Id != 0;
            // (0.3.18) building mode (0.3.19: the player's toggle, B or .hansa build); only then are planks, fences, panes,
            // plank stairs, slabs and ladders structure
            bool building = VanillaPieceBlock.BuildingMode(ep);
            bool planks = block && building && VanillaPieceBlock.IsPlanks(held.Block);
            bool door = block && VanillaPieceBlock.IsDoor(held.Block);
            // (0.3.16, notes 154-159) a pane (a window) and a fence (a railing) go on a border, stairs on a floor place, a
            // plank slab on a horizontal place (a shelf, a well's top) and only there
            bool pane = block && building && VanillaPieceBlock.IsPane(held.Block), fence = block && building && VanillaPieceBlock.IsFence(held.Block);
            bool stairs = block && building && (VanillaPieceBlock.IsShipStair(held.Block) || VanillaPieceBlock.IsLadder(held.Block)), slab = block && building && VanillaPieceBlock.IsPlankSlab(held.Block);
            // (0.3.18, note 165) a brick chimney goes on a flue place and only there; the hearth and the oven on a floor place
            bool flue = block && VanillaPieceBlock.IsFlueBlock(held.Block);
            bool fire = block && (VanillaPieceBlock.IsHearthBlock(held.Block) || VanillaPieceBlock.IsOvenBlock(held.Block));
            bool onBorder = planks || door || pane || fence;
            var tool = EntityShip.PaintTool(held, out _, out _);
            bool sneak = Sneaking(ep);
            // (0.3.18, the user: "couldn't paint a wall: the crosshair wanted to place the bucket on the wall") a dye
            // container always paints what it's aimed at; Shift + right-click puts the container down instead
            bool painting = tool != PaintToolKind.None && !(block && sneak);
            // the surveyor's chalk (the user's inspection tool): free mount points show too, with their ids
            bool chalk = held?.Collectible is ItemSurveyorsChalk;
            // (0.3.21, step C) the gun item goes on a gun place (building mode), never on a floor place
            bool gunItem = block && EntityShip.IsGunBlock(held.Collectible);
            bool placing = chalk || block && !painting && !gunItem;
            // (0.3.24, note 193) a lantern (a block giving light) in hand: its hooks are aimed at directly
            bool lightInHand = block && !chalk && held.Block.GetLightHsv(capi.World.BlockAccessor, null, held) is byte[] lhsv && lhsv.Length > 2 && lhsv[2] > 0;
            var best = new Target { Dist = double.MaxValue };
            foreach (var ship in sys.Ships)
            {
                if (ship.Def == null || ship.Mounts == null || ship.Pos.SquareDistanceTo(eye.X, eye.Y, eye.Z) > 50 * 50) continue;
                ship.RayToLocal(eye, dir, out var o, out var d);
                double wall = ship.Grid.Raycast(o, d, range + 1);
                var piece = ship.Pieces.Pick(o, d, range, out double tp);
                // (0.3.10: not behind her decks, sides or masts: "a reading of items below deck or through the mast". A
                // piece's own cells are in her grid, up to half a cell outside its box)
                // (0.3.12, note 123: a partition or a door put aboard is picked as a sheet on its line, its collision the quarter
                // block on one side of it: from that side the collision came first and a door opened only from the other)
                double slack = piece?.Kind == MountKind.Border ? 0.3 : 0.15;
                // (0.3.24, note 177: "from this angle cannot open doors") unless what the ray met first is the piece's own
                // collision (a door's quarter-block layer, seen slanting from its side)
                if (piece != null && tp > wall + slack && !ShipPieceSet.OwnHit(piece, o, d, wall)) { piece = null; tp = double.MaxValue; }
                // from outside her (the water, a dock, another ship): nothing inside through her side or deck, even where a
                // piece's box reaches into her planking (0.3.10: "trying to open chests placed inside" while mending her)
                bool outside = !(ep.MountedOn is ShipSeat on && on.Ship == ship);
                if (outside && piece != null && ShipPieceSet.Behind(ship.Def.Grid, o, d, tp)) { piece = null; tp = double.MaxValue; }
                // (0.3.12: a wall put aboard is painted like her own surfaces, with dye in hand, or scraped with a knife)
                if (piece != null && tp < best.Dist)
                    best = new Target { Kind = painting && tool is PaintToolKind.Dye or PaintToolKind.Knife && piece.Paintable ? TargetKind.PaintPiece : TargetKind.Piece, Ship = ship, Piece = piece, Dist = tp };
                if (ship.GoneDown) continue;
                // (0.3.20, step B; docs/slots.md) her deck cells in building mode: an axe takes one up, planks lay an opening,
                // trapdoors lay a hatch over it
                // ((0.3.27) an opening is laid with planks or boards out of building mode too: mending a hole is no building)
                bool layMat = EntityShip.LaysOpenings(held);
                if ((building || layMat) && ship.Slots != null)
                {
                    var ct = CellTarget(ship, held, o, d, range, wall, building);
                    if (ct.Kind == TargetKind.Cell && ct.Dist < best.Dist) best = ct;
                }
                // (0.3.26, step D1; docs/slots.md §9) her hull panels in building mode: an axe takes one out, planks lay one
                if ((building || layMat) && ship.HullPanels != null)
                {
                    var ht = HullTarget(ship, held, o, d, range, wall, building);
                    if (ht.Kind is TargetKind.Hull or TargetKind.Cell && ht.Dist < best.Dist - 1e-6) best = ht;
                }
                // (0.3.21, step C) an empty gun place's carriage box, the gun item in hand in building mode
                if (building && gunItem && ship.Def.GunPlaces != null)
                    foreach (var gp in ship.Def.GunPlaces)
                    {
                        var gf = ship.GunOf(gp);
                        var gb = gf?.Sel ?? gf?.Box;
                        if (gb == null || ship.GunPlaceLaid(gp)) continue;
                        double tg = ShipFittings.RayBox(o, d, new Cuboidd(gb[0], gb[1], gb[2], gb[3], gb[4], gb[5]));
                        if (tg < 0 || tg > range || tg > wall + 0.3 || tg >= best.Dist) continue;
                        best = new Target { Kind = TargetKind.GunPlace, Ship = ship, Zone = gp.Index, Dist = tg, At = o.AddCopy(d.X * tg, d.Y * tg, d.Z * tg) };
                    }
                MountPoint m = null;
                double tm = double.MaxValue;
                PiecePlan aimPlan = null;
                if (placing)
                {
                    // a free place: only holding a block
                    // (a place on top of a piece only while a piece with a top stands under it)
                    // (a panel place only with a planks block in hand; a wall spot on a panel only while the panel is up)
                    System.Func<MountPoint, bool> free = x => !ship.Pieces.Taken(x.Id) &&
                        (x.Kind != MountKind.Panel || chalk || planks) &&
                        // (a cell's border: a partition from planks, a door; a door goes nowhere else)
                        (x.Kind != MountKind.Border || chalk || planks || pane || fence || door && x.Row == 0) && (!(door || pane || fence) || chalk || x.Kind == MountKind.Border) &&
                        (x.Kind != MountKind.Horizontal || chalk || slab) && (!slab || chalk || x.Kind == MountKind.Horizontal) &&
                        (!stairs || chalk || x.Kind == MountKind.Floor && x.Level == 0) &&
                        (!fire || chalk || x.Kind == MountKind.Floor && x.Level == 0) &&
                        (x.Kind != MountKind.Flue || chalk || flue) && (!flue || chalk || x.Kind == MountKind.Flue) &&
                        // (0.3.20) not over a deck cell taken up; over a design opening laid, yes
                        ship.MountOpen(x) &&
                        // (0.3.16: with a wall piece in hand, a partition's own wall spots aren't places: the border past it is)
                        (!onBorder || chalk || x.Kind != MountKind.Wall || x.Under == null) &&
                        ship.Pieces.Supports(ship.Mounts, x);
                    // (0.3.18, the user's point 1) only next to what the crosshair hits: a piece in front of her, or her own woodwork
                    Vec3d hit = null;
                    double[] normal = null;
                    ShipPiece hitPiece = null;
                    double th = double.MaxValue;
                    // (0.3.24, note 177: a piece still here is in front of her, or behind only its own solid layer)
                    if (piece != null)
                    {
                        th = tp;
                        hitPiece = piece;
                        hit = o.AddCopy(d.X * tp, d.Y * tp, d.Z * tp);
                        foreach (var sb in piece.Sel)
                            if (Math.Abs(ShipFittings.RayBox(o, d, new Cuboidd(sb[0], sb[1], sb[2], sb[3], sb[4], sb[5])) - tp) < 1e-6) { normal = AimPlace.BoxNormal(o, d, sb); break; }
                    }
                    else if (AimPlace.GridHit(ship.Grid, o, d, range, out double tg, out var ng))
                    {
                        th = tg;
                        normal = ng;
                        hit = o.AddCopy(d.X * tg, d.Y * tg, d.Z * tg);
                    }
                    if (hit != null && normal != null)
                    {
                        // (the first place next to the hit the block in hand fits; else the first, to say why not)
                        foreach (var c in AimPlace.Candidates(ship.Mounts, ship.Pieces, hit, normal, hitPiece, free, onBorder && !chalk))
                        {
                            if (chalk) { m = c; break; }
                            int tw = c.Kind is MountKind.Wall or MountKind.Panel ? c.In : TowardPlayer(ship, c, eye, hit);
                            var pl = PlanFor(ship, c, held, tw, building);
                            if (m == null) { m = c; aimPlan = pl; }
                            if (pl.Error == null) { m = c; aimPlan = pl; break; }
                        }
                        if (m != null) tm = th;
                    }
                    // (0.3.24, note 193: "cannot place the lantern back" on the mast's hook or at the stern) a lantern in hand:
                    // a free hook whose box the crosshair is on (a hook hangs in the air by a mast or on a post: nothing of
                    // her is hit there), if it's nearer than what was hit or that took nothing
                    if (lightInHand)
                    {
                        var hk = ship.Mounts.Pick(o, d, range, Math.Min(wall, tp), x => x.Kind == MountKind.Hook && free(x), out double tk);
                        if (hk != null && (m == null || tk <= tm + 0.05 || aimPlan?.Error != null))
                        {
                            m = hk;
                            tm = tk;
                            aimPlan = PlanFor(ship, hk, held, TowardPlayer(ship, hk, eye, o.AddCopy(d.X * tk, d.Y * tk, d.Z * tk)), building);
                        }
                    }
                }
                else if (held == null)
                {
                    // (empty-handed: the stern lantern's hook shows, aimed at itself)
                    m = ship.Mounts.Pick(o, d, range, Math.Min(wall, tp), x => x.Kind == MountKind.Hook && x.Shown && !ship.Pieces.Taken(x.Id), out tm);
                }
                if (outside && m != null && m.Kind != MountKind.Hook && ShipPieceSet.Behind(ship.Def.Grid, o, d, tm)) m = null;
                // on the piece in the crosshair (next to it, or on top of it): holding a block, it's put there; but a piece
                // that's used with a click (a chest, a bed, a door, a working block, the hearth) only with Shift, as vanilla
                // places a block against a chest
                bool onPiece = m != null && best.Kind is TargetKind.Piece or TargetKind.PaintPiece && best.Ship == ship && tm <= best.Dist + 0.05;
                bool usable = onPiece && (Usable(best.Piece) || ship.IsWorkingPiece(best.Piece.Id) || best.Piece.Role is PieceRole.Hearth or PieceRole.Oven && !flue);
                // (0.3.24, note 191: "Shift + right-click to light the oven with a torch placed the torch on the floor") a
                // firestarter or a lit torch on a hearth or an oven put aboard lights it (Shift or not): it's never put down there
                bool igniting = onPiece && best.Piece.Role is PieceRole.Hearth or PieceRole.Oven && EntityShip.Igniter(held) > 0;
                if (m != null && (onPiece ? (block || chalk) && (sneak || !usable) && !igniting : tm < best.Dist - 0.02))
                    best = new Target { Kind = block ? TargetKind.Mount : chalk ? TargetKind.Inspect : TargetKind.Shown, Ship = ship, Mount = m, Dist = tm, At = o.AddCopy(d.X * tm, d.Y * tm, d.Z * tm), Plan = aimPlan };
                else if (onPiece && block) stackOver = true;
                if (painting && sys.Hansa.PaintPlanFor(ship.Def) is PaintPlan plan && plan.Usable)
                {
                    // ((0.3.20) not a deck cell's quads that aren't drawn: a cell taken up, the edges round one that's in)
                    var sts = ship.Slots;
                    var hps = ship.HullPanels;
                    int z = plan.Pick(o, d, Math.Min(range, Math.Min(wall, tp) + 0.4), out double tz,
                        sts == null ? null : (ow, fa) => (ow < 0 || sts.Filled(ow)) && (fa < 0 || !sts.Filled(fa)),
                        hps == null ? null : (ow, fa) => (ow < 0 || hps.Filled(ow)) && (fa < 0 || !hps.Filled(fa)));
                    if (z >= 0 && tz < best.Dist - 0.02)
                        best = new Target { Kind = TargetKind.Zone, Ship = ship, Zone = z, Dist = tz, At = o.AddCopy(d.X * tz, d.Y * tz, d.Z * tz) };
                }
            }
            // (0.3.18) planks, a fence, a pane, plank stairs, a slab or a ladder in hand aboard out of building mode: say so
            // (0.3.19, the user: "I can stack 3 plank blocks on top of each other ... but cannot add to the sides": out of
            // building mode they're decoration, on a floor place or on top of a piece only, and the hint says why)
            string needSaw = block && !building && (VanillaPieceBlock.IsBuildingBlock(held.Block) || gunItem) && ep.MountedOn is ShipSeat ? Lang.Get("hansa:hint-build-off", BuildKeyName(capi)) : null;
            if (best.Kind == TargetKind.None || best.Dist > fixtureDist + 0.05)
            {
                Hint = needSaw;
                return;
            }
            if (best.Kind == TargetKind.Cell && best.Cell >= 0) best.Problem = CellProblem(ref best, held);
            if (best.Kind == TargetKind.Hull && best.Cell >= 0) best.Problem = HullProblem(best, held);
            if (best.Kind == TargetKind.GunPlace) best.Problem = best.Ship.MountGunProblem(best.Ship.Def.GunPlaces[best.Zone], true);
            if (best.Kind == TargetKind.Mount)
            {
                if (best.Plan == null)
                {
                    int toward = best.Mount.Kind is MountKind.Wall or MountKind.Panel ? best.Mount.In : TowardPlayer(best.Ship, best.Mount, eye, best.At);
                    best.Plan = PlanFor(best.Ship, best.Mount, held, toward, building);
                }
                best.Problem = best.Ship.PlaceProblem(best.Plan, true);
            }
            Look = best;
            // (with the chalk its own line says what's in the crosshair: ChalkLine adds a mount point's id to it)
            // (0.3.18: a hearth or an oven put aboard says what its fixture says: ShipHud, as the design's)
            Hint = chalk || best.Kind == TargetKind.Piece && best.Piece.Role is PieceRole.Hearth or PieceRole.Oven ? null : HintFor(best, held, tool);
            if (needSaw != null && best.Kind is not (TargetKind.Hull or TargetKind.Cell)) Hint = Hint == null ? needSaw : Hint + " · " + needSaw;
        }

        /// <summary>A piece the crosshair is on has a free place on top of it for the block in hand (Shift puts it there).</summary>
        bool stackOver;

        /// <summary>A piece a right-click uses (opens, sleeps in, sits on).</summary>
        static bool Usable(ShipPiece p) => p.Role is PieceRole.Storage or PieceRole.Bed or PieceRole.Seat or PieceRole.Door or PieceRole.Hatch;

        /// <summary>(0.3.16) What the HUD says a modular piece in hand would be on the place in the crosshair (a window, a railing, a stair, a panel), or null.</summary>
        static string ModularHint(PiecePlan plan) => plan?.Role switch
        {
            PieceRole.Window => Lang.Get("hansa:hint-place-window"),
            PieceRole.Railing => Lang.Get("hansa:hint-place-railing"),
            PieceRole.Ladder => Lang.Get("hansa:hint-place-ladder", plan.Rise),
            PieceRole.Stair => Lang.Get(plan.Cells == PiecePlanner.SteepCells ? "hansa:hint-place-steepstair" : "hansa:hint-place-stair", plan.Rise),
            PieceRole.Shelf => Lang.Get(MountLayout.IsHood(plan.Mount?.Id) ? "hansa:hint-place-hood" : plan.Mount?.Level >= 3 ? "hansa:hint-place-welltop" : "hansa:hint-place-shelf"),
            PieceRole.Partition => Lang.Get("hansa:hint-place-partition"),
            PieceRole.Hearth => Lang.Get("hansa:hint-place-hearth"),
            PieceRole.Oven => Lang.Get("hansa:hint-place-oven"),
            PieceRole.Flue => Lang.Get("hansa:hint-place-flue"),
            _ => null
        };

        /// <summary>Shift held (aboard, the keys go to the deck seat).</summary>
        static bool Sneaking(EntityPlayer ep) => ep.Controls.Sneak || ep.Controls.ShiftKey || ep.MountedOn is ShipSeat s && s.Controls.Sneak;

        /// <summary>
        /// The way from a mount point to the player (ship-local, snapped): a piece faces whoever puts it. (0.3.16, note 159: "chest
        /// is placed bit differently if partitions are placed before the chest") As vanilla turns a chest: from where the
        /// crosshair meets the place (hit, ship-local), not the place's middle; from the middle a player standing off to one
        /// side (as one does beside a partition) got a chest turned side-on, its side's handle where its front's lock was.
        /// </summary>
        static int TowardPlayer(EntityShip ship, MountPoint m, Vec3d eye, Vec3d hit = null)
        {
            var l = ship.WorldToLocal(eye.X, eye.Y, eye.Z);
            return AimPlace.Toward(m, new Vec3d(l.X, l.Y, l.Z), hit);
        }

        PiecePlan PlanFor(EntityShip ship, MountPoint m, ItemStack held, int toward, bool building)
        {
            if (held?.Block == null) return new PiecePlan { Error = "hansa:piece-nowhere", Mount = m };
            string code = held.Block.Code.ToString();
            var key = (ship.EntityId, m.Id, code, toward, ship.FitVersion, building);
            if (plans.TryGetValue(key, out var cached)) return cached;
            if (plans.Count > 64) plans.Clear();
            PiecePlan planCache;
            try { planCache = PiecePlanner.Plan(ship.Mounts, m, new VanillaPieceBlock(capi.World, held, building), toward); }
            catch (Exception e)
            {
                capi.Logger.Warning("[hansa] Can't work out where {0} goes: {1}", code, e.Message);
                planCache = new PiecePlan { Error = "hansa:piece-nowhere", Mount = m };
            }
            plans[key] = planCache;
            return planCache;
        }

        string HintFor(Target t, ItemStack held, PaintToolKind tool)
        {
            var ship = t.Ship;
            switch (t.Kind)
            {
                case TargetKind.Piece:
                {
                    string name = ship.PieceName(t.Piece);
                    string use = t.Piece.Role switch
                    {
                        PieceRole.Storage => Lang.Get("hansa:hint-piece-open"),
                        PieceRole.Bed => Lang.Get("hansa:hint-piece-sleep"),
                        PieceRole.Seat => Lang.Get("hansa:hint-piece-sit"),
                        PieceRole.Door => Lang.Get(t.Piece.Open ? "hansa:hint-door-shut" : "hansa:hint-door-open"),
                        PieceRole.Hatch => Lang.Get(t.Piece.Open ? "hansa:hint-hatch-shut" : "hansa:hint-hatch-open"),
                        // (0.3.14) a working block aboard is the real block: vanilla's own clicks (ShipBlocks)
                        _ => ship.IsWorkingPiece(t.Piece.Id) ? Lang.Get(ShipBlocksClient.Loaded(capi, ship, t.Piece.Id) ? "hansa:hint-piece-working" : "hansa:hint-piece-working-notloaded") :
                             t.Piece.Stack?.Block is Block wb && VanillaPieceBlock.IsWorkstation(wb) ? Lang.Get("hansa:hint-piece-ashore") : null
                    };
                    if (ship.IsWorkingPiece(t.Piece.Id)) return Lang.Get("hansa:hint-piece", name) + " · " + use;
                    // ((0.3.27, note 199) the wrench turns it)
                    if (EntityShip.IsWrench(held)) return Lang.Get("hansa:hint-piece", name) + " · " + Lang.Get("hansa:hint-piece-turn");
                    return Lang.Get("hansa:hint-piece", name) + (use != null ? " · " + use : "") + " · " + Lang.Get("hansa:hint-piece-take") +
                           (stackOver && held?.Block != null ? " · " + Lang.Get("hansa:hint-piece-stack", held.GetName()) : "");
                }
                case TargetKind.Shown:
                {
                    string code = t.Mount.Id.StartsWith("h:") ? t.Mount.Id.Substring(2) : t.Mount.Id;
                    return Lang.HasTranslation("hansa:hint-attach-" + code, false, false) ? Lang.Get("hansa:hint-attach-" + code) : Lang.Get("hansa:hint-hook");
                }
                case TargetKind.Mount:
                    if (t.Problem != null) return Lang.Get(t.Problem);
                    // (0.3.19) ship structure in building mode: the hint says so, and how to leave it
                    if (ModularHint(t.Plan) is string mh)
                    {
                        // ((0.3.27) a stair through a well: the beam in its way comes out with it)
                        var bw = t.Plan.Role is PieceRole.Stair or PieceRole.Ladder ? ship.BeamsInTheWay(t.Plan) : null;
                        if (bw != null) mh += " · " + Lang.Get("hansa:hint-stair-beam", bw.Count);
                        return Lang.Get("hansa:hint-place", held.GetName()) + " · " + mh +
                            (t.Plan.Role is PieceRole.Hearth or PieceRole.Oven or PieceRole.Flue ? "" : " · " + Lang.Get("hansa:hint-build-on", BuildKeyName(capi)));
                    }
                    return Lang.Get(t.Plan.Mounts.Count > 1 ? "hansa:hint-place-two" : "hansa:hint-place", held.GetName()) +
                           // (0.3.14) a working block on a floor place on her grid works aboard (ShipBlocks)
                           (t.Plan.Kind == MountKind.Floor && ship.GridFrac != null && ShipBlocksSystem.ClientInstance?.IsWorking(held.Block) == true ? " · " + Lang.Get("hansa:hint-piece-works-aboard") :
                            VanillaPieceBlock.IsWorkstation(held.Block) ? " · " + Lang.Get("hansa:hint-piece-ashore") : "");
                case TargetKind.Zone:
                    return ZoneHint(ship, t.Zone, held, tool);
                case TargetKind.PaintPiece:
                    return PieceHint(ship, t.Piece, held, tool);
                case TargetKind.Cell:
                    return CellHint(t, held);
                case TargetKind.Hull:
                    return HullHint(t, held);
                case TargetKind.GunPlace:
                {
                    if (t.Problem != null) return Lang.Get(t.Problem);
                    var gf = ship.GunOf(ship.Def.GunPlaces[t.Zone]);
                    return Lang.Get("hansa:hint-gunplace-mount", held?.GetName() ?? "", Lang.Get(ShipCompartments.AlongX(gf) ? (gf.Out[0] < 0 ? "hansa:side-stern" : "hansa:side-bow")
                        : (gf?.Out?[2] ?? gf?.Pos?[2] ?? 0) < 0 ? "hansa:side-port" : "hansa:side-starboard"));
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ (0.3.20, step B) deck cells

        /// <summary>
        /// The deck cell a ray (ship-local) is on in building mode, for what's in hand: with an axe her planking it meets
        /// (a deck cell, or a fixed part of a deck: Cell -1, why in Problem); with planks or trapdoors the opening it passes
        /// through first (a cell taken up, no hatch over it), before her collision beyond.
        /// </summary>
        Target CellTarget(EntityShip ship, ItemStack held, Vec3d o, Vec3d d, double range, double wall, bool building = true)
        {
            var l = ship.SlotLayout;
            if (held?.Collectible?.Tool == EnumTool.Axe)
            {
                if (!building) return default;
                double t = ship.Grid.RaycastCell(o, d, range, out int ci, out int cj, out int ck, out _);
                if (t > range) return default;
                int n = l.AtVoxel(ship.Grid, ci, cj, ck);
                var at = o.AddCopy(d.X * t, d.Y * t, d.Z * t);
                // ((0.3.29) a castle port that's in (its board out, its hole open): the axe on its place takes it out
                foreach (int gpn in l.Ports)
                {
                    if (!l.Slots[gpn].IsGunPort || !ship.Slots.Filled(gpn)) continue;
                    var gb = l.Slots[gpn].Box;
                    double tg = ShipFittings.RayBox(o, d, new Cuboidd(gb[0] - 0.08, gb[1] - 0.08, gb[2] - 0.05, gb[3] + 0.08, gb[4] + 0.08, gb[5] + 0.05));
                    if (tg >= 0 && tg <= Math.Min(t, range)) return new Target { Kind = TargetKind.Cell, Ship = ship, Cell = gpn, CellMode = CellTake, Dist = tg, At = o.AddCopy(d.X * tg, d.Y * tg, d.Z * tg) };
                }
                if (n >= 0) return new Target { Kind = TargetKind.Cell, Ship = ship, Cell = n, CellMode = CellTake, Dist = t, At = at };
                // (a deck's planking that isn't a cell: say it's fixed)
                double y = ship.Grid.OY + (cj + 0.5) * ship.Grid.V;
                foreach (var (top, thick) in l.Tops)
                    if (y < top && y > top - thick)
                        return new Target { Kind = TargetKind.Cell, Ship = ship, Cell = -1, CellMode = CellTake, Dist = t, At = at, Problem = "hansa:cell-fixed" };
                // ((0.3.27) a deck beam with no lengths that come out there)
                if (l.Beams.Count > 0)
                    foreach (var (top, thick) in l.Tops)
                        if (y < top - thick && y > top - thick - 0.75)
                            return new Target { Kind = TargetKind.Cell, Ship = ship, Cell = -1, CellMode = CellTake, Dist = t, At = at, Problem = "hansa:beam-fixed" };
                return default;
            }
            bool planks = EntityShip.LaysOpenings(held), trap = building && held?.Block != null && VanillaPieceBlock.IsTrapdoor(held.Block);
            // ((0.3.27, note 201) in building mode with anything else in hand: the opening says what lays it or covers it)
            bool info = building && !planks && !trap;
            if (info && held?.Block != null && !VanillaPieceBlock.IsBuildingBlock(held.Block) && !EntityShip.IsGunBlock(held.Collectible)) info = false;
            if (!planks && !trap && !info) return default;
            int best = -1;
            double bt = Math.Min(range, wall + 0.05);
            foreach (var (top, thick) in l.Tops)
            {
                if (Math.Abs(d.Y) < 1e-6) continue;
                double t = (top - thick / 2 - o.Y) / d.Y;
                if (t <= 0 || t >= bt) continue;
                int n = l.OnDeck(top, o.X + d.X * t, o.Z + d.Z * t);
                if (n < 0 || ship.Slots.Filled(n) || ship.HatchOver(n) != null) continue;
                best = n;
                bt = t;
            }
            // ((0.3.29) a castle port's place with its board out and no port: a trapdoor in building mode puts the port in
            if (trap)
                foreach (int n in l.Ports)
                {
                    if (!l.Slots[n].IsGunPort || ship.Slots.Filled(n) || l.IndexOf(l.Slots[n].Cover) is int cv && cv >= 0 && ship.Slots.Filled(cv)) continue;
                    var b = l.Slots[n].Box;
                    double tb = ShipFittings.RayBox(o, d, new Cuboidd(b[0], b[1], b[2] - 0.05, b[3], b[4], b[5] + 0.05));
                    if (tb < 0 || tb >= bt + 0.4) continue;
                    best = n;
                    bt = tb;
                }
            // ((0.3.28, D2.4) a gun port (its cover out): planks or boards plank it over (the ray through its hole)
            if (planks)
                foreach (int n in l.Ports)
                {
                    if (ship.Slots.Filled(n) || l.Slots[n].IsGunPort) continue;
                    var b = l.Slots[n].Box;
                    double tb = ShipFittings.RayBox(o, d, new Cuboidd(b[0], b[1], b[2] - 0.05, b[3], b[4], b[5] + 0.05));
                    if (tb < 0 || tb >= bt + 0.4) continue;
                    best = n;
                    bt = tb;
                }
            // ((0.3.27) a beam segment that's out: planks or boards put it back (the ray through its box))
            if (planks)
                foreach (var kv in l.Beams)
                    foreach (int n in kv.Value)
                    {
                        if (ship.Slots.Filled(n)) continue;
                        var b = l.Slots[n].Box;
                        double tb = ShipFittings.RayBox(o, d, new Cuboidd(b[0] - 0.05, b[1] - 0.05, b[2], b[3] + 0.05, b[4], b[5]));
                        if (tb < 0 || tb >= bt) continue;
                        best = n;
                        bt = tb;
                    }
            if (best < 0) return default;
            // ((0.3.29) a port put in with a trapdoor: laid as the port's slot is)
            bool portLay = trap && l.Slots[best].IsGunPort;
            return new Target { Kind = TargetKind.Cell, Ship = ship, Cell = best, CellMode = portLay ? CellLay : trap ? CellHatch : info ? CellInfo : CellLay, Dist = bt, At = o.AddCopy(d.X * bt, d.Y * bt, d.Z * bt) };
        }

        /// <summary>
        /// (0.3.26, step D1) The hull panel a ray (ship-local) is on in building mode, for what's in hand: with an axe her
        /// collision it meets (a panel's voxel; her fixed planking: Cell -1, "hull-fixed"); with planks the opening of a panel
        /// that's out it passes through first, before her collision beyond.
        /// </summary>
        Target HullTarget(EntityShip ship, ItemStack held, Vec3d o, Vec3d d, double range, double wall, bool building = true)
        {
            var l = ship.HullLayout;
            if (held?.Collectible?.Tool == EnumTool.Axe)
            {
                if (!building) return default;
                double t = ship.Grid.RaycastCell(o, d, range, out int ci, out int cj, out int ck, out _);
                if (t > range) return default;
                int n = l.AtVoxel(ship.Grid, ci, cj, ck);
                var at = o.AddCopy(d.X * t, d.Y * t, d.Z * t);
                if (n >= 0) return new Target { Kind = TargetKind.Hull, Ship = ship, Cell = n, CellMode = CellTake, Dist = t, At = at };
                // (her fixed planking: a frame's strip, a wale, her ends, a panel with something on it)
                if (ship.Def.PartMap?.IsShell(ci, cj, ck) == true && at.Y <= ship.Def.PartMap.HullTop)
                    return new Target { Kind = TargetKind.Hull, Ship = ship, Cell = -1, CellMode = CellTake, Dist = t + 1e-4, At = at, Problem = "hansa:hull-fixed" };
                return default;
            }
            if (!EntityShip.LaysOpenings(held)) return default;
            int best = l.PickOpening(o, d, Math.Min(range, wall + 0.05), ship.HullOpen, out double bt);
            {
                // ((0.3.30) a ceiling that's in with her planking behind it out: that planking laid from inside, through it)
                double tc = ship.Grid.RaycastCell(o, d, Math.Min(range, wall + 0.05), out int ci, out int cj, out int ck, out _);
                int nc = tc <= range ? l.AtVoxel(ship.Grid, ci, cj, ck) : -1;
                if (nc >= 0 && ship.HullPanels.Filled(nc) && ship.HullBehind(nc) >= 0 && (best < 0 || tc < bt))
                    return new Target { Kind = TargetKind.Hull, Ship = ship, Cell = nc, CellMode = CellLay, Dist = tc, At = o.AddCopy(d.X * tc, d.Y * tc, d.Z * tc) };
            }
            if (best < 0) return default;
            return new Target { Kind = TargetKind.Hull, Ship = ship, Cell = best, CellMode = CellLay, Dist = bt, At = o.AddCopy(d.X * bt, d.Y * bt, d.Z * bt) };
        }

        /// <summary>(0.3.26) Why the hull panel in the crosshair can't be done (a lang code), or null.</summary>
        string HullProblem(Target t, ItemStack held)
        {
            var ship = t.Ship;
            if (t.CellMode == CellTake) return ship.HullTakeProblem(t.Cell);
            // ((0.3.30) with her planking behind a ceiling: that first)
            int first = ship.HullBehind(t.Cell) is int b && b >= 0 ? b : t.Cell;
            int need = EntityShip.LayNeed(ship.HullLayout.Slots[first].Mat, held);
            if (need < 0 || !EntityShip.Creative(capi.World.Player) && (held?.StackSize ?? 0) < need) return "hansa:hull-needs-planks";
            return ship.HullLayProblem(first, true);
        }

        string HullHint(Target t, ItemStack held)
        {
            if (t.Problem != null) return Lang.Get(t.Problem);
            var ship = t.Ship;
            var s = ship.HullLayout.Slots[t.Cell];
            string name = ship.PartText(ShipPart.Hull(s.Side, s.Station, s.Band, 0, s.IsInner)) ?? s.Id;
            if (t.CellMode != CellTake && ship.HullBehind(t.Cell) >= 0)
            {
                // ((0.3.30) her planking behind it out too: both laid from here (the planking first: the sea's kept out))
                int both = ship.HullLayCount(t.Cell, held);
                return ship.HullPanels.Filled(t.Cell) ? Lang.Get("hansa:hint-hull-lay-behind", both, held?.GetName() ?? "")
                    : Lang.Get("hansa:hint-hull-lay-both", both, held?.GetName() ?? "", name);
            }
            return t.CellMode == CellTake ? Lang.Get("hansa:hint-hull-take", name, s.Mat.Count) : Lang.Get("hansa:hint-hull-lay", Math.Max(1, EntityShip.LayNeed(s.Mat, held)), held?.GetName() ?? "", name);
        }

        /// <summary>Why the deck cell in the crosshair can't be done (a lang code), or null; a hatch's plan into the target's Plan.</summary>
        string CellProblem(ref Target t, ItemStack held)
        {
            var ship = t.Ship;
            switch (t.CellMode)
            {
                case CellTake: return ship.TakeProblem(t.Cell);
                case CellLay:
                    int need = EntityShip.LayNeed(ship.SlotLayout.Slots[t.Cell].Mat, held);
                    if (need < 0 || !EntityShip.Creative(capi.World.Player) && (held?.StackSize ?? 0) < need) return "hansa:cell-needs-planks";
                    return ship.LayProblem(t.Cell, true);
                case CellHatch:
                {
                    var plan = ship.HatchPlanAt(t.Cell, held?.Collectible?.Code?.ToString());
                    t.Plan = plan;
                    if (plan.Error != null) return plan.Error;
                    if (!EntityShip.Creative(capi.World.Player) && (held?.StackSize ?? 0) < plan.Mounts.Count) return "hansa:hatch-needs-trapdoors";
                    return ship.PlaceProblem(plan, true);
                }
            }
            return null;
        }

        string CellHint(Target t, ItemStack held)
        {
            if (t.Problem != null) return Lang.Get(t.Problem);
            var ship = t.Ship;
            var dd = ship.SlotLayout.Slots[t.Cell];
            string deck = Lang.HasTranslation("hansa:deck-" + dd.Deck, false, false) ? Lang.Get("hansa:deck-" + dd.Deck) : dd.Deck;
            // ((0.3.28, D2.4) a gun port's cover)
            if (dd.IsGunPort)
                return t.CellMode == CellTake ? Lang.Get("hansa:hint-port-out") : t.CellMode == CellLay ? Lang.Get("hansa:hint-port-in", held?.GetName() ?? "") : null;
            if (dd.IsPort)
                return t.CellMode == CellTake ? Lang.Get("hansa:hint-port-take") : t.CellMode == CellLay ? Lang.Get("hansa:hint-port-lay", Math.Max(1, EntityShip.LayNeed(dd.Mat, held)), held?.GetName() ?? "") : null;
            // ((0.3.27) a length of a deck beam)
            if (dd.IsBeam)
                return t.CellMode == CellTake ? Lang.Get("hansa:hint-beam-take", deck) : t.CellMode == CellLay ? Lang.Get("hansa:hint-beam-lay", Math.Max(1, EntityShip.LayNeed(dd.Mat, held)), held?.GetName() ?? "") : null;
            switch (t.CellMode)
            {
                case CellTake: return Lang.Get("hansa:hint-cell-take", deck);
                case CellLay: return Lang.Get("hansa:hint-cell-lay", Math.Max(1, EntityShip.LayNeed(dd.Mat, held)), held?.GetName() ?? "");
                case CellInfo: return Lang.Get("hansa:hint-cell-open");
                case CellHatch:
                {
                    var b = t.Plan?.Box;
                    int nx = b == null ? 0 : (int)Math.Round(b[3] - b[0]), nz = b == null ? 0 : (int)Math.Round(b[5] - b[2]);
                    return Lang.Get(VanillaPieceBlock.IsGlazedTrapdoor(held?.Collectible?.Code?.ToString()) ? "hansa:hint-cell-hatch-glazed" : "hansa:hint-cell-hatch", nx, nz, t.Plan?.Mounts.Count ?? 0);
                }
            }
            return null;
        }

        /// <summary>(0.3.12) Painting a wall put aboard: its name, its faces' area, the dye it takes (or scraping it back).</summary>
        string PieceHint(EntityShip ship, ShipPiece p, ItemStack held, PaintToolKind tool)
        {
            string name = ship.PieceName(p);
            double area = p.PaintArea;
            bool creative = EntityShip.Creative(capi.World.Player);
            EntityShip.PaintTool(held, out string colour, out int have);
            if (tool == PaintToolKind.Knife)
                return p.Paint == null ? Lang.Get("hansa:hint-scrape-bare-piece", name) : Lang.Get("hansa:hint-scrape-piece", name);
            if (p.Paint == colour) return Lang.Get("hansa:hint-paint-same", name, DyeName(colour));
            double need = ShipPaint.DyeItems(area) / (double)ShipPaint.DyeItemsPerLitre, got = have / (double)ShipPaint.DyeItemsPerLitre;
            return Lang.Get(creative ? "hansa:hint-paint-free" : got + 1e-9 < need ? "hansa:hint-paint-short" : "hansa:hint-paint", name, area, DyeName(colour), need, got);
        }

        string ZoneHint(EntityShip ship, int zi, ItemStack held, PaintToolKind tool)
        {
            var plan = sys.Hansa.PaintPlanFor(ship.Def);
            var zone = plan.Zones[zi];
            string name = ZoneName(zone);
            double area = plan.Area[zi];
            string now = ship.Paint.Of(zone.Code);
            bool creative = EntityShip.Creative(capi.World.Player);
            EntityShip.PaintTool(held, out string colour, out int have);
            switch (tool)
            {
                case PaintToolKind.Dye:
                {
                    if (now == colour) return Lang.Get("hansa:hint-paint-same", name, DyeName(colour));
                    double need = ShipPaint.DyeItems(area) / (double)ShipPaint.DyeItemsPerLitre, got = have / (double)ShipPaint.DyeItemsPerLitre;
                    return Lang.Get(creative ? "hansa:hint-paint-free" : got + 1e-9 < need ? "hansa:hint-paint-short" : "hansa:hint-paint", name, area, DyeName(colour), need, got);
                }
                case PaintToolKind.Gold:
                {
                    if (!ShipPaint.Gildable(zone)) return Lang.Get("hansa:hint-gild-kind", name);
                    if (now == ShipPaint.Gold) return Lang.Get("hansa:hint-gild-same", name);
                    int need = ShipPaint.GoldPlates(area);
                    return Lang.Get(creative ? "hansa:hint-gild-free" : have < need ? "hansa:hint-gild-short" : "hansa:hint-gild", name, area, need, have);
                }
                case PaintToolKind.Knife:
                    return now == null ? Lang.Get("hansa:hint-scrape-bare", name, DefaultName(zone)) : Lang.Get("hansa:hint-scrape", name, DefaultName(zone));
            }
            return null;
        }

        /// <summary>The chalk's line above the hotbar, and the free mount point in the crosshair (its kind, room and id) under it.</summary>
        public string ChalkLine(string chalkText)
        {
            if (chalkText == null || Look.Kind != TargetKind.Inspect || Look.Mount == null) return chalkText;
            var m = Look.Mount;
            string kind = Lang.Get("hansa:mount-kind-" + m.Kind.ToString().ToLowerInvariant());
            string room = Lang.HasTranslation("hansa:room-" + m.Room, false, false) ? Lang.Get("hansa:room-" + m.Room)
                : m.Deck != null && Lang.HasTranslation("hansa:deck-" + m.Deck, false, false) ? Lang.Get("hansa:deck-" + m.Deck) : m.Room;
            return chalkText + Environment.NewLine + Lang.Get("hansa:hint-inspect-mount", kind, room, m.Id);
        }

        public static string ZoneName(PaintZoneDef z) =>
            Lang.HasTranslation("hansa:zone-" + z.Code, false, false) ? Lang.Get("hansa:zone-" + z.Code) : z.Name ?? z.Code;

        public static string DyeName(string colour) => colour == null ? "" : Lang.Get("hansa:dye-" + colour);

        public static string DefaultName(PaintZoneDef z) =>
            Lang.HasTranslation("hansa:paint-default-" + z.Default, false, false) ? Lang.Get("hansa:paint-default-" + z.Default) : z.Default ?? "";

        // ------------------------------------------------------------------ clicks

        /// <summary>A click with something of decorating's in the crosshair: true if it was decorating's (nothing else gets it).</summary>
        public bool OnClick(EntityPlayer ep, EnumEntityAction action)
        {
            var t = Look;
            if (t.Kind == TargetKind.None) return false;
            // (0.3.21, step C) an empty gun place: right-click mounts the gun in hand
            if (t.Kind == TargetKind.GunPlace)
            {
                if (action != EnumEntityAction.InWorldRightMouseDown) return action == EnumEntityAction.InWorldLeftMouseDown;
                if (OnceAClick("gunplace:" + t.Zone))
                {
                    if (t.Problem != null) capi.TriggerIngameError(this, "hansa-decor", Lang.Get(t.Problem));
                    else channel.SendPacket(new DecorPacket { ShipId = t.Ship.EntityId, Action = DecorPacket.GunMount, Mount = t.Ship.Def.GunPlaces[t.Zone].Id });
                }
                return true;
            }
            // (0.3.26, step D1) a hull panel: an axe's left mouse held takes it out; right-click with planks lays it
            if (t.Kind == TargetKind.Hull)
            {
                if (action == EnumEntityAction.InWorldLeftMouseDown && t.CellMode == CellTake)
                {
                    if (t.Cell < 0 || t.Problem != null)
                    {
                        if (OnceAClick("hullfixed")) capi.TriggerIngameError(this, "hansa-decor", Lang.Get(t.Problem ?? "hansa:hull-fixed"));
                        return true;
                    }
                    if (takingShip == t.Ship && takingCell == t.Cell && takingHull) return true;
                    takingShip = t.Ship;
                    takingCell = t.Cell;
                    takingHull = true;
                    takingPiece = 0;
                    takingSince = capi.World.ElapsedMilliseconds;
                    takingSound = 0;
                    takingSent = false;
                    return true;
                }
                if (action == EnumEntityAction.InWorldRightMouseDown && t.CellMode == CellLay && t.Cell >= 0)
                {
                    if (OnceAClick("hull:" + t.Cell))
                    {
                        if (t.Problem != null) capi.TriggerIngameError(this, "hansa-decor", Lang.Get(t.Problem));
                        else channel.SendPacket(new DecorPacket { ShipId = t.Ship.EntityId, Action = DecorPacket.HullLay, Mount = t.Ship.HullLayout.Slots[t.Cell].Id });
                    }
                    return true;
                }
                return false;
            }
            // (0.3.20) a deck cell: an axe's left mouse held takes it up; right-click lays planks or a hatch
            if (t.Kind == TargetKind.Cell)
            {
                if (action == EnumEntityAction.InWorldLeftMouseDown && t.CellMode == CellTake)
                {
                    if (t.Cell < 0 || t.Problem != null)
                    {
                        if (OnceAClick("cellfixed")) capi.TriggerIngameError(this, "hansa-decor", Lang.Get(t.Problem ?? "hansa:cell-fixed"));
                        return true;
                    }
                    if (takingShip == t.Ship && takingCell == t.Cell && !takingHull) return true;
                    takingShip = t.Ship;
                    takingCell = t.Cell;
                    takingHull = false;
                    takingPiece = 0;
                    takingSince = capi.World.ElapsedMilliseconds;
                    takingSound = 0;
                    takingSent = false;
                    return true;
                }
                if (action == EnumEntityAction.InWorldRightMouseDown && t.CellMode == CellInfo)
                {
                    if (OnceAClick("cellinfo")) capi.TriggerIngameError(this, "hansa-decor", Lang.Get("hansa:hint-cell-open"));
                    return true;
                }
                if (action == EnumEntityAction.InWorldRightMouseDown && t.CellMode is CellLay or CellHatch && t.Cell >= 0)
                {
                    if (OnceAClick("cell:" + t.Cell))
                    {
                        if (t.Problem != null) capi.TriggerIngameError(this, "hansa-decor", Lang.Get(t.Problem));
                        else channel.SendPacket(new DecorPacket { ShipId = t.Ship.EntityId, Action = t.CellMode == CellLay ? DecorPacket.CellLay : DecorPacket.CellHatch, Mount = t.Ship.SlotLayout.Slots[t.Cell].Id });
                    }
                    return true;
                }
                return false;
            }
            if (action == EnumEntityAction.InWorldLeftMouseDown)
            {
                // (at a gun, or laying one: the gun's own click)
                if (t.Kind is not (TargetKind.Piece or TargetKind.PaintPiece) || ep.MountedOn is GunSeat || sys.Laying.Ship != null) return false;
                if (takingShip == t.Ship && takingPiece == t.Piece.Id && takingCell < 0) return true;      // (the game repeats a held click)
                takingShip = t.Ship;
                takingPiece = t.Piece.Id;
                takingCell = -1;
                takingSince = capi.World.ElapsedMilliseconds;
                takingSound = 0;
                takingSent = false;
                return true;
            }
            if (action != EnumEntityAction.InWorldRightMouseDown) return false;
            switch (t.Kind)
            {
                case TargetKind.Piece:
                    // ((0.3.27, note 199) a wrench in hand: turn it, as vanilla's wrench turns a block)
                    if (EntityShip.IsWrench(capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack))
                    {
                        if (OnceAClick("turn:" + t.Piece.Id)) channel.SendPacket(new DecorPacket { ShipId = t.Ship.EntityId, Action = DecorPacket.Rotate, Piece = t.Piece.Id });
                        return true;
                    }
                    // (0.3.18) a hearth or an oven put aboard: its clicks are its stand-in fixture's (as the design's: open it, light it)
                    if (t.Piece.Role is PieceRole.Hearth or PieceRole.Oven) return false;
                    if (t.Piece.Role is PieceRole.Storage or PieceRole.Bed or PieceRole.Seat or PieceRole.Door or PieceRole.Hatch)
                    {
                        if (OnceAClick("use:" + t.Piece.Id)) channel.SendPacket(new DecorPacket { ShipId = t.Ship.EntityId, Action = DecorPacket.Use, Piece = t.Piece.Id });
                        return true;
                    }
                    // (0.3.16) a working block the crosshair didn't get the real block of (ShipBlocksClient.Pick): why, in the log; never "ashore"
                    if (t.Ship.IsWorkingPiece(t.Piece.Id))
                    {
                        if (OnceAClick("working:" + t.Piece.Id))
                        {
                            capi.Logger.Notification("[hansa] working blocks: a click on {0} (piece {1} of {2} #{3}) didn't reach its block: {4}", t.Piece.Stack?.GetName(), t.Piece.Id,
                                t.Ship.GetName(), t.Ship.EntityId, ShipBlocksClient.WhyNot(capi, t.Ship, t.Piece.Id) ?? "the crosshair's pick didn't run");
                            capi.TriggerIngameError(this, "hansa-working", Lang.Get("hansa:piece-working-notloaded", t.Piece.Stack?.GetName() ?? ""));
                        }
                        return true;
                    }
                    // a workstation (a forge, an anvil, a quern): its workings don't run aboard yet, say so
                    if (t.Piece.Stack?.Block is Block ws && VanillaPieceBlock.IsWorkstation(ws) && OnceAClick("ashore:" + t.Piece.Id))
                        capi.TriggerIngameError(this, "hansa-ashore", Lang.Get("hansa:piece-ashore-only", t.Piece.Stack.GetName()));
                    // a decoration: a right-click on it does nothing (with a block in hand it isn't put on it either)
                    return capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block != null ||
                           t.Piece.Stack?.Block is Block ws2 && VanillaPieceBlock.IsWorkstation(ws2);
                case TargetKind.Mount:
                    if (OnceAClick("place:" + t.Mount.Id))
                    {
                        if (t.Problem != null) capi.TriggerIngameError(this, "hansa-decor", Lang.Get(t.Problem));
                        else channel.SendPacket(new DecorPacket { ShipId = t.Ship.EntityId, Action = DecorPacket.Place, Mount = t.Mount.Id, Dir = t.Plan.Toward });
                    }
                    return true;
                case TargetKind.Shown:
                    if (OnceAClick("shown")) capi.TriggerIngameError(this, "hansa-decor", Hint);
                    return true;
                case TargetKind.PaintPiece:
                    if (OnceAClick("paintpiece:" + t.Piece.Id))
                        channel.SendPacket(new DecorPacket { ShipId = t.Ship.EntityId, Action = DecorPacket.Paint, Piece = t.Piece.Id });
                    return true;
                case TargetKind.Zone:
                    if (OnceAClick("paint:" + t.Zone))
                    {
                        var plan = sys.Hansa.PaintPlanFor(t.Ship.Def);
                        channel.SendPacket(new DecorPacket { ShipId = t.Ship.EntityId, Action = DecorPacket.Paint, Zone = plan.Zones[t.Zone].Code, X = t.At.X, Y = t.At.Y, Z = t.At.Z });
                    }
                    return true;
            }
            return false;
        }

        string lastClick;
        long lastClickMs;

        /// <summary>The game repeats a held right-click: once a press (a new press after it's let go).</summary>
        bool OnceAClick(string what)
        {
            long now = capi.World.ElapsedMilliseconds;
            bool again = what == lastClick && now - lastClickMs < 400;
            lastClick = what;
            lastClickMs = now;
            return !again;
        }

        /// <summary>Every frame: taking a piece up goes on while left mouse is held on it; after PickupSeconds the server is asked. The HUD's line.</summary>
        public void UpdateTaking(EntityPlayer ep)
        {
            Busy = null;
            if (takingShip == null) return;
            long now = capi.World.ElapsedMilliseconds;
            bool held = capi.Input.InWorldMouseButton.Left;
            if (takingCell >= 0 && takingHull)
            {
                // (0.3.26) a hull panel being taken out with an axe
                var hl = takingShip.HullLayout;
                if (!held || hl == null || takingCell >= hl.Count || Look.Kind != TargetKind.Hull || Look.Ship != takingShip || Look.Cell != takingCell ||
                    !sys.Ships.Contains(takingShip) || !takingShip.HullPanels.Filled(takingCell))
                {
                    takingShip = null;
                    takingCell = -1;
                    takingHull = false;
                    return;
                }
                var hs = hl.Slots[takingCell];
                double hf = (now - takingSince) / 1000.0 / EntityShip.HullSeconds;
                if (now - takingSound > 250)
                {
                    takingSound = now;
                    var hc = hs.Centre;
                    takingShip.LocalToWorld(hc.X, hc.Y, hc.Z, out double sx, out double sy, out double sz);
                    capi.World.PlaySoundAt(new AssetLocation("sounds/block/planks"), sx, sy, sz, null, true, 12);
                }
                if (hf >= 1 && !takingSent)
                {
                    takingSent = true;
                    channel.SendPacket(new DecorPacket { ShipId = takingShip.EntityId, Action = DecorPacket.HullTake, Mount = hs.Id });
                }
                Busy = Lang.Get("hansa:hud-hull-taking", (int)(Math.Min(1, hf) * 100));
                return;
            }
            if (takingCell >= 0)
            {
                // (0.3.20) a deck cell being taken up with an axe
                var sl = takingShip.SlotLayout;
                if (!held || sl == null || takingCell >= sl.Count || Look.Kind != TargetKind.Cell || Look.Ship != takingShip || Look.Cell != takingCell ||
                    !sys.Ships.Contains(takingShip) || !takingShip.Slots.Filled(takingCell))
                {
                    takingShip = null;
                    takingCell = -1;
                    return;
                }
                var cd = sl.Slots[takingCell];
                double cf = (now - takingSince) / 1000.0 / EntityShip.CellSeconds;
                if (now - takingSound > 250)
                {
                    takingSound = now;
                    takingShip.LocalToWorld(cd.CX, cd.Top, cd.CZ, out double sx, out double sy, out double sz);
                    capi.World.PlaySoundAt(new AssetLocation("sounds/block/planks"), sx, sy, sz, null, true, 12);
                }
                if (cf >= 1 && !takingSent)
                {
                    takingSent = true;
                    channel.SendPacket(new DecorPacket { ShipId = takingShip.EntityId, Action = DecorPacket.CellTake, Mount = cd.Id });
                }
                Busy = Lang.Get("hansa:hud-cell-taking", (int)(Math.Min(1, cf) * 100));
                return;
            }
            var p = takingShip.Pieces.Get(takingPiece);
            if (!held || p == null || Look.Kind is not (TargetKind.Piece or TargetKind.PaintPiece) || Look.Ship != takingShip || Look.Piece?.Id != takingPiece || !sys.Ships.Contains(takingShip))
            {
                takingShip = null;
                return;
            }
            double f = (now - takingSince) / 1000.0 / EntityShip.PickupSeconds;
            if (now - takingSound > 250)
            {
                takingSound = now;
                Feedback(takingShip, p);
            }
            if (f >= 1 && !takingSent)
            {
                takingSent = true;
                channel.SendPacket(new DecorPacket { ShipId = takingShip.EntityId, Action = DecorPacket.Remove, Piece = takingPiece });
            }
            Busy = Lang.Get("hansa:hud-piece-taking", takingShip.PieceName(p), (int)(Math.Min(1, f) * 100));
        }

        /// <summary>The block's hit sound and a puff of its texture at the piece, as vanilla's breaking does.</summary>
        void Feedback(EntityShip ship, ShipPiece p)
        {
            var c = p.Center;
            ship.LocalToWorld(c[0], c[1], c[2], out double x, out double y, out double z);
            var sound = p.Stack?.Block?.Sounds?.Hit.Location ?? new AssetLocation("sounds/block/planks");
            capi.World.PlaySoundAt(sound, x, y, z, null, true, 12);
            if (p.Stack != null)
            {
                try { capi.World.SpawnCubeParticles(new Vec3d(x, y, z), p.Stack, 0.4f, 6, 0.6f); }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ the outline

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            var t = Look;
            // the repair crosshair's panel, picked through what's aboard and her inside planking: outlined (0.3.10)
            if (t.Kind == TargetKind.None && capi.World.Player?.CurrentEntitySelection?.Entity is EntityShip rs && rs.RepairPicked >= 0 &&
                rs.DamageParts != null && rs.RepairPicked < rs.DamageParts.Count)
            {
                var dp = rs.DamageParts.Parts[rs.RepairPicked];
                t = new Target { Kind = TargetKind.Inspect, Ship = rs };
                repairBox = new[] { dp.Min[0], dp.Min[1], dp.Min[2], dp.Max[0], dp.Max[1], dp.Max[2] };
            }
            else repairBox = null;
            if (t.Kind == TargetKind.None || t.Ship == null) return;
            double[] box = repairBox ?? t.Kind switch
            {
                TargetKind.Piece or TargetKind.PaintPiece => t.Piece.Box,
                TargetKind.Mount when t.Problem == null && t.Plan?.Box != null => t.Plan.Box,
                TargetKind.Shown or TargetKind.Inspect => t.Mount.Box,
                // (0.3.20) a deck cell, or a hatch's whole opening
                TargetKind.Cell when t.Cell >= 0 && t.Ship.SlotLayout != null => t.CellMode == CellHatch && t.Plan?.Box != null && t.Problem == null ? t.Plan.Box : t.Ship.SlotLayout.Slots[t.Cell].Box,
                // (0.3.26) a hull panel: its shell
                TargetKind.Hull when t.Cell >= 0 && t.Ship.HullLayout != null => t.Ship.HullLayout.Slots[t.Cell].Box,
                // (0.3.21) an empty gun place: where its carriage goes
                TargetKind.GunPlace => t.Ship.GunOf(t.Ship.Def.GunPlaces[t.Zone]) is FixtureDef gpf ? gpf.Sel ?? gpf.Box : null,
                _ => null
            };
            if (box == null) return;
            var ep = capi.World.Player?.Entity;
            if (ep == null || sys.Renderer == null) return;
            try
            {
                cube ??= WireframeCube.CreateUnitCube(capi, ColorUtil.ToRgba(128, 0, 0, 0));
                repairCube ??= WireframeCube.CreateUnitCube(capi, ColorUtil.ToRgba(255, 40, 160, 255));
                var ship = t.Ship;
                var o = sys.Renderer.RenderOrigin(ship, ep);
                var cam = ep.CameraPos;
                var m = new Matrixf().Set(capi.Render.CameraMatrixOriginf)
                    .Translate((float)(o.X - cam.X), (float)(o.Y - cam.Y), (float)(o.Z - cam.Z))
                    .RotateY(ShipFrame.ModelYaw(ship.Pos.Yaw)).RotateZ((float)ship.Pitch).RotateX((float)ship.Roll);
                const float grow = 0.003f;
                m.Translate((float)box[0] - grow, (float)box[1] - grow, (float)box[2] - grow)
                 .Scale((float)(box[3] - box[0]) + 2 * grow, (float)(box[4] - box[1]) + 2 * grow, (float)(box[5] - box[2]) + 2 * grow);
                (repairBox != null ? repairCube : cube).Render(capi, m, repairBox != null ? 2.2f : 1.6f);
                // (it leaves blending on: the opaque pass draws without)
                capi.Render.GlToggleBlend(false);
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[hansa] Drawing the outline failed: {0}", e.Message);
                Look = default;
            }
        }
    }
}
