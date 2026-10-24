using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Hansa
{
    /// <summary>
    /// A hook the design gives for a lantern (or later other things): where the piece hangs from ("down") or stands on
    /// ("up"), and what may go there ("light"). The stern lantern's is Shown: the crosshair finds it empty with nothing in hand.
    /// </summary>
    public class HookDef
    {
        public string Code;
        public double[] Pos;
        public string Hang = "down";
        /// <summary>Optional: a hook on a wall, the wall's normal into the room (for later wall brackets).</summary>
        public double[] Normal;
        public string[] Kinds = { "light" };
        public bool Shown;
    }

    /// <summary>A stair (from the generator): its foot and head (ship-local, on the decks), and its width across. Kept clear, with a block before its foot and after its head.</summary>
    public class StairDef
    {
        public string Code;
        public double[] Foot, Head;
        public double Width = 1;
    }

    /// <summary>A box (ship-local x0 y0 z0 x1 y1 z1) nothing may be placed in: a window, a light, a gun port, a walkway. What: why (for the reports).</summary>
    public class KeepClearDef
    {
        public string What;
        public double[] Box;
    }

    /// <summary>What a new ship starts with: a vanilla block on a mount point (a lantern on its hook), facing Yaw (degrees: 0 the bow, 90 port, as a fixture's).</summary>
    public class StartPieceDef
    {
        public string Block;
        public string Mount;
        public float Yaw;
        /// <summary>The stack's attributes (a lantern's "material", "lining", "glass"), or null.</summary>
        public Dictionary<string, string> Attributes;
    }

    /// <summary>
    /// (0.3.16, notes 154–158) A piece a new ship starts with (docs/modular-pieces.md): a partition, a window, a door or a
    /// railing on a border place (At: the middle of the segment's foot on the grid line, Along the way the wall runs), a
    /// stair on a floor place (At: the middle of its foot cell, Up the way it climbs), a horizontal panel (At: the cell's
    /// middle at its plane). Block: what it's made of (default per kind).
    /// </summary>
    public class ModularPieceDef
    {
        public string Kind;
        public double[] At;
        public string Along;
        public string Up;
        public double Rise;
        public string Block;
        public string Paint;
        public string Code;
        public bool Open;
        /// <summary>(0.3.17) A door: the way it swings ("+x", "-x", "+z", "-z", across its line); null: into the anchor cell (a closed room's side of a front onto an open deck).</summary>
        public string Into;
        /// <summary>(0.3.18) A hearth's open front, an oven's mouth: the way it faces ("+x", "-x", "+z", "-z"), where the cook stands.</summary>
        public string Facing;
        /// <summary>(0.3.20, step B) A hatch: its opening's cells along x and along z (docs/slots.md §5), and the way its leaves' hinges run ("x", "z"; null: along its long sides, along x when square).</summary>
        public int[] Size;
        public string Hinge;
        /// <summary>(0.3.23) A stair's cells along its run: 3 (default, the long ship's stair) or 2 (the steep one); (0.1.0-dev.6, note 36) 1 (the short one); 0: not given.</summary>
        public int Length;
        /// <summary>
        /// (0.3.29, notes 230, 233: the companion walled in) A wall piece's anchor: the way from its line into the cell it stands
        /// on ("+x", "-x", "+z", "-z"): round a well the place across the line (the twin) where the well's own cell is out;
        /// null: the line's own place.
        /// </summary>
        public string Anchor;
        /// <summary>(0.3.29) A panel on a hood's place (the roof boards on the walls' tops: "hh:deck:i:k"), not a shelf's.</summary>
        public bool Hood;
        /// <summary>(0.3.29) The layout revision (her design's "layoutRev") that brought this entry: a ship laid before it isn't given it.</summary>
        public int Since;

        /// <summary>(0.3.23) The ship's stairs, by their cells: the steep one (2) and the long one (3); (0.1.0-dev.6, note 36) the short one (1).</summary>
        public const string SteepStair = "hansa:shipstair-steep", LongStair = "hansa:shipstair-long", ShortStair = "hansa:shipstair-short";

        /// <summary>(0.1.0-dev.6) The ship's stair of so many cells (any other: the long one).</summary>
        public static string StairBlock(int cells) => cells switch
        {
            PiecePlanner.ShortCells => ShortStair,
            PiecePlanner.SteepCells => SteepStair,
            _ => LongStair
        };

        /// <summary>(0.3.23) The block an entry is laid with: its own, else its kind's (a stair's by its length); a stair entry naming a block that isn't a ship's stair (vanilla plank stairs before 0.3.23) gets the ship's stair of its length.</summary>
        public string BlockFor(string kind)
        {
            if (kind == "stair")
            {
                string want = StairBlock(Length);
                return Block != null && Block.StartsWith("hansa:shipstair-") && (Length == 0 || Block == want) ? Block : want;
            }
            return Block ?? DefaultBlock(kind);
        }

        /// <summary>The block a kind is made of when the entry names none.</summary>
        public static string DefaultBlock(string kind) => kind switch
        {
            "window" => "game:glasspane-leaded-oak-ew",
            "door" => "game:door-solid-oak",
            "railing" => "game:woodenfence-oak-ew-free",
            // (0.3.23) the ship's long stair (its length 2: the steep one, BlockFor)
            "stair" => LongStair,
            // (0.3.19) the companion ladder-stair is a vanilla ladder
            "ladder" => "game:ladder-wood-oak-north",
            "panel" => "game:plankslab-oak-down-free",
            // (0.3.18, note 165) the ship's hearth, oven and flue as pieces
            "hearth" => "game:claybricks-good-fire",
            "oven" => "game:clayoven-north",
            "flue" => "game:claybrickchimney-four-red-ns",
            // (0.3.20) a hatch of vanilla trapdoors (a window trapdoor's is glazed)
            "hatch" => "game:trapdoor-solid-oak-1",
            _ => "game:planks-oak-ud"
        };

        /// <summary>Does an entry's kind name the piece its block made?</summary>
        public static bool Matches(string kind, PieceRole role) => kind switch
        {
            "partition" => role == PieceRole.Partition, "window" => role == PieceRole.Window, "door" => role == PieceRole.Door,
            "railing" => role == PieceRole.Railing, "stair" => role == PieceRole.Stair, "ladder" => role == PieceRole.Ladder, "panel" => role == PieceRole.Shelf,
            "hearth" => role == PieceRole.Hearth, "oven" => role == PieceRole.Oven, "flue" => role == PieceRole.Flue, "hatch" => role == PieceRole.Hatch, _ => false
        };

        /// <summary>Its key among those laid on a ship (remembered, so what the player took up never comes back).</summary>
        public string Key
        {
            get
            {
                if (!string.IsNullOrEmpty(Code)) return Code;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string at = At == null ? "?" : string.Join(",", At.Select(v => Math.Round(v, 3).ToString("0.###", inv)));
                return Kind + "@" + at + (Along != null ? "," + Along : "") + (Up != null ? "," + Up : "");
            }
        }
    }

    /// <summary>(0.3.16) An opening in a deck (a stair well), ship-local box x0 y0 z0 x1 y1 z1 (y the deck's thickness).</summary>
    public class WellDef
    {
        public string Code;
        public double[] Box;
    }

    /// <summary>Horizontal (0.3.16, note 159): a plank panel's place over a floor cell (a shelf, a well's top).</summary>
    /// <summary>Flue (0.3.18, note 165): a hearth's chimney segment's place over a floor cell, up through the deck above.</summary>
    public enum MountKind { Floor, Wall, Hook, Panel, Border, Horizontal, Flue }

    /// <summary>
    /// A place on a ship where a player may put a vanilla block (<see cref="MountLayout"/>):
    /// - Floor: a whole block on a deck (its cell from X, Y (the deck), Z to one block more); I, K its cell on the deck's
    ///   alignment (the ship's grid when the design has one); Level 0 on the deck, 1, 2 stacked on a piece standing on the
    ///   place under it (a chest on a chest: open only while that piece is there, <see cref="ShipPieceSet.Supports"/>);
    /// - Wall: a block's space against a room's wall (the hull's inside or a bulkhead), Row 0 or 1 up it; X, Y, Z the middle
    ///   of the spot's foot on the wall's face; In the way into the room (0 +x, 1 +z, 2 -x, 3 -z), Normal the wall's own
    ///   (tilted where the side leans); I the spot along the wall;
    /// - Hook: a design's hook (X, Y, Z the hook), Hang down or up;
    /// - Panel (a design with a block grid): the wedge between a room's outermost floor cell and her leaning side, a block
    ///   high (Row 0 or 1 over the floor): a planks block put there panels it, a vertical face on the cell's border (X, Y, Z
    ///   the middle of its foot, In the way into the room) filling the wedge behind it (Depth deep); the panel's face then
    ///   takes wall pieces (the wall spot whose Under is the panel's id). Row 1 over a row 0 panel place stands on its panel.
    /// - Border (a design with a block grid): a floor cell's side in a closed room, a block high (Row 0 or 1 over the floor,
    ///   up to the deck structure's band): a planks block put there is a thin partition on the grid line (1/8 thick), a
    ///   vanilla door (one block wide, two high) takes both rows and opens and shuts. X, Y, Z the middle of its foot on the
    ///   line, In the way into the cell it was found from (the anchor cell); Box straddles the line (what's picked). Between
    ///   two floor cells one place per border (from the cell to the west or north: ids "b:deck:i:k:e|s:row"); by a cell that
    ///   isn't a floor place (a stair, a hatch's surround, a fixture) the anchor's own side. Row 1 stands on a partition on row 0.
    /// </summary>
    public class MountPoint
    {
        public string Id;
        public MountKind Kind;
        /// <summary>The deck (floor) or room (wall) it's on, and the room it's in (or the deck's code on an open deck).</summary>
        public string Deck, Room;
        public double X, Y, Z;
        public int I, K, Row;
        /// <summary>Floor: 0 on the deck; 1, 2 on top of a piece on the place under it ("f:deck:i:k:level").</summary>
        public int Level;
        /// <summary>
        /// What it needs a piece on to be usable: a stacked floor place the place under it (whose piece it stands on), a
        /// panel's wall spot that panel's place, a row 1 panel place the row 0 one under it. Null: always usable.
        /// </summary>
        public string Under;
        /// <summary>Panel: how far the wedge behind its face reaches out to her side (blocks).</summary>
        public double Depth;
        /// <summary>Wall: the way into the room; which wall ("greatcabin:s").</summary>
        public int In;
        public string Wall;
        public double[] Normal;
        /// <summary>Hook: down (hangs under it) or up (stands on it); what goes there; shown empty.</summary>
        public string Hang;
        public string[] Kinds;
        public bool Shown;
        /// <summary>The space it gives (ship-local x0 y0 z0 x1 y1 z1): the floor's or wall's block, a hook's small box.</summary>
        public double[] Box;
        /// <summary>
        /// (0.3.13, note 137) Border: only part of it is free of her (under her beams and deck, against her curved side): the
        /// free parts, rectangles in the line's plane (along0, y0, along1, y1; along is x for a line along x, else z). Null:
        /// the whole block.
        /// </summary>
        public List<double[]> Clip;
        /// <summary>(0.3.14, notes 148, 149) A cut place's partition as drawn, out to her real surfaces (<see cref="MountLayout.DrawnClip"/>), once worked out.</summary>
        [JsonIgnore] public List<double[]> Drawn;
        /// <summary>(0.3.18) Flue: the deck it comes out on (a block over it), that deck's height, and the floor place it comes out on (or null).</summary>
        public string Over, OutPlace;
        public double OverY;
        [JsonIgnore] public bool DrawnDone;
        /// <summary>
        /// (0.3.20, step B; docs/slots.md) The deck cell it stands on, hangs under or is anchored on (-1 none): usable only
        /// while that cell is in; and the cell it needs open (-1 none: a panel's place at a well's top).
        /// </summary>
        [JsonIgnore] public int SlotUnder = -1, SlotOpen = -1;
        /// <summary>
        /// (0.3.28, note 230) A border place's twin on the same segment anchored on the cell across the line (its solid layer
        /// there): Twin the other one's id; TwinSide true on the twin, usable only while the first one's anchor cell is out
        /// (a wall round a well, its solid layer on the deck beside it). Null: none. <see cref="MountLayout.WellPlaces"/>.
        /// </summary>
        public string Twin;
        public bool TwinSide;

        public double CX => (Box[0] + Box[3]) / 2;
        public double CY => (Box[1] + Box[4]) / 2;
        public double CZ => (Box[2] + Box[5]) / 2;

        public override string ToString() => Id;
    }

    /// <summary>A space kept clear of placed pieces (ship-local): a box, or a round one standing up (a capstan's bars, a mast).</summary>
    public struct KeepClear
    {
        public string Why;
        public double[] Box;
        public bool Round;
        public double CX, CZ, R;
        /// <summary>
        /// (0.3.17) A margin round a fixture (a helm's, a binnacle's, a hearth's room to stand at it): the fixture's own box
        /// before it was grown. A wall piece's place (a railing at a castle's front beside the binnacle) is kept clear of the
        /// fixture itself only, not of its margin.
        /// </summary>
        public double[] Core;

        public bool Hits(double x0, double y0, double z0, double x1, double y1, double z1) => Hits(x0, y0, z0, x1, y1, z1, false);

        /// <summary>coreOnly: a margin's fixture box only (a wall piece's place). (0.3.17) Boxes that only touch don't hit (within 0.001).</summary>
        public bool Hits(double x0, double y0, double z0, double x1, double y1, double z1, bool coreOnly)
        {
            const double e = 1e-3;
            if (coreOnly && Core != null)
                return x0 < Core[3] - e && x1 > Core[0] + e && y0 < Core[4] - e && y1 > Core[1] + e && z0 < Core[5] - e && z1 > Core[2] + e;
            if (!Round)
                return x0 < Box[3] - e && x1 > Box[0] + e && y0 < Box[4] - e && y1 > Box[1] + e && z0 < Box[5] - e && z1 > Box[2] + e;
            if (y0 >= Box[4] - e || y1 <= Box[1] + e) return false;
            double nx = Math.Clamp(CX, x0, x1), nz = Math.Clamp(CZ, z0, z1);
            return (nx - CX) * (nx - CX) + (nz - CZ) * (nz - CZ) < R * R - e;
        }
    }

    /// <summary>
    /// Where pieces may go on a design, worked out from the definition alone (the same on the server and every client):
    /// - Floor places: whole blocks on every deck (not the main top), on the alignment of the 0.25 collision grid that gives
    ///   that deck the most (tried: 16), each with its floor solid under all of it and a block of room over it, kept clear of
    ///   doorways and the cell each side of a door, ladders and their head and foot, stairs (the generator's, and treads
    ///   found in the grid: anything standing on air within a block of the deck's height), hatches and the companionway,
    ///   the room each gun needs to train and run back, the capstan's bars' sweep, the gates' swing, the pump's brake, the
    ///   helm's standing spot, the binnacle, masts, where people come aboard and respawn, and every other fixture's boxes;
    /// - Wall spots: in the closed rooms, a block's space against the wall (the hull's inside planking, the bulkheads at
    ///   their ends), in rows 0.75 and 1.75 over the floor, where the wall behind is even (within half a block) and the
    ///   space before it is free; the piece hangs flush, tilted as the wall leans;
    /// - Hooks: the design's, and the lantern places of an older design (attach "lantern");
    /// - Stacked places: over each floor place, a block up (and two), where there's a block of room in her: open while a
    ///   piece with a top to stand on stands on the place under it (ShipPieceSet.Supports).
    /// A design with a block grid ("grid": its origin) has every floor place and wall spot on that one grid; an older one
    /// takes, deck by deck, the alignment of the 0.25 grid that gives the most places. On the grid (the user, 2026-10-03:
    /// nothing hung at an angle) wall spots are only where the wall stands upright (bulkheads), in rows a block apart from
    /// the floor, and on the panels put up in the wedges along her leaning sides (panel places, below).
    /// Ids: "f:deck:i:k" (stacked "f:deck:i:k:level"), "w:room:side:i:row" (side p, s, f, a; on a panel the side and its
    /// line, "s5.5"), "h:code", "p:deck:i:k:side:row" (side e, s, w, n: the way out to her side).
    /// </summary>
    public partial class MountLayout
    {
        public static readonly int[] DX = { 1, 0, -1, 0 }, DZ = { 0, 1, 0, -1 };
        /// <summary>A direction in the ship's frame as vanilla names a block's side: +x east, +z south, -x west, -z north.</summary>
        public static readonly string[] DirCode = { "east", "south", "west", "north" };
        /// <summary>Rows of wall spots: how far over the floor each starts (an older design without a block grid).</summary>
        public static readonly double[] RowHeights = { 0.75, 1.75 };
        /// <summary>Rows of wall spots on the ship's block grid: on the blocks over the floor.</summary>
        public static readonly double[] GridRowHeights = { 0, 1 };
        /// <summary>On the grid a wall spot's wall stands upright: its lean and turn within this (the normal's other parts).</summary>
        public const double Upright = 0.05;
        /// <summary>Panels go up to the deck structure's band: two blocks over the floor.</summary>
        public const int PanelRows = 2;
        /// <summary>How deep a panel's wedge may be along her side (where she flares out going up), in blocks.</summary>
        public const double MaxPanelDepth = 2.5;
        /// <summary>A wall more uneven than this (blocks across one spot) takes no spot.</summary>
        public const double WallEven = 0.5;
        /// <summary>The space a wall spot needs free in front of the wall.</summary>
        public const double WallDepth = 0.6;
        /// <summary>How many pieces may stand on one another on a floor place (over the one on the deck).</summary>
        public const int MaxStack = 2;

        public readonly ShipDefinition Def;
        public readonly List<MountPoint> All = new();
        public readonly Dictionary<string, MountPoint> ById = new();
        public readonly List<KeepClear> Clear = new();
        /// <summary>Each deck's alignment (the x and z of a cell's corner, 0..1): the ship's grid's on every deck when she has one.</summary>
        public readonly Dictionary<string, (double X, double Z)> Align = new();
        /// <summary>The design's block grid was used (one alignment for every deck), not a search per deck.</summary>
        public bool OnShipGrid;
        readonly Dictionary<(string, int, int, int), MountPoint> floor = new();
        readonly Dictionary<(string, int, int), MountPoint> wall = new();

        /// <summary>The floor places on her decks (not the stacked ones).</summary>
        public IEnumerable<MountPoint> Floors => All.Where(m => m.Kind == MountKind.Floor && m.Level == 0);
        /// <summary>The places on top of a piece on a floor place.</summary>
        public IEnumerable<MountPoint> Stacked => All.Where(m => m.Kind == MountKind.Floor && m.Level > 0);
        public IEnumerable<MountPoint> Walls => All.Where(m => m.Kind == MountKind.Wall);
        public IEnumerable<MountPoint> Hooks => All.Where(m => m.Kind == MountKind.Hook);
        public IEnumerable<MountPoint> Panels => All.Where(m => m.Kind == MountKind.Panel);
        public IEnumerable<MountPoint> Borders => All.Where(m => m.Kind == MountKind.Border);
        public IEnumerable<MountPoint> Horizontals => All.Where(m => m.Kind == MountKind.Horizontal);
        public IEnumerable<MountPoint> Flues => All.Where(m => m.Kind == MountKind.Flue);
        /// <summary>A partition's thickness as drawn (on the grid line, half each side); its collision is a voxel layer on the anchor side.</summary>
        public const double PartitionThickness = 0.125;
        /// <summary>The rows of wall spots this design takes.</summary>
        public double[] Rows => OnShipGrid ? GridRowHeights : RowHeights;

        public MountPoint Get(string id) => id != null && ById.TryGetValue(id, out var m) ? m : null;
        public MountPoint FloorAt(string deck, int i, int k, int level = 0) => floor.TryGetValue((deck, i, k, level), out var m) ? m : null;
        /// <summary>The stacked place right over a floor place (or over a stacked one), or null.</summary>
        public MountPoint Above(MountPoint m) => m?.Kind == MountKind.Floor ? FloorAt(m.Deck, m.I, m.K, m.Level + 1) : null;
        public MountPoint WallAt(string wallKey, int i, int row) => wall.TryGetValue((wallKey, i, row), out var m) ? m : null;

        MountLayout(ShipDefinition def) { Def = def; }

        /// <summary>The direction (0..3) a horizontal vector is nearest to.</summary>
        public static int Snap(double x, double z) => Math.Abs(x) >= Math.Abs(z) ? (x >= 0 ? 0 : 2) : (z >= 0 ? 1 : 3);

        /// <summary>
        /// Her places from her design. cellsLaid: the layout with every deck cell laid (<see cref="MergeOpenings"/>'s), where
        /// the design's "hatch" keep-clear boxes over cells that start empty aren't kept: laid, those cells are deck, no
        /// hatchway (22 October 2026, caravel note 49: "where stairs were i cannot build partition wall over").
        /// </summary>
        public static MountLayout Build(ShipDefinition def, bool cellsLaid = false)
        {
            var l = new MountLayout(def) { cellsLaid = cellsLaid };
            if (def?.Grid == null) return l;
            l.KeepClearZones();
            l.FloorPlaces();
            l.PanelPlaces();
            l.BorderPlaces();
            l.HorizontalPlaces();
            l.FluePlaces();
            l.WallSpots();
            l.PartitionSpots();
            l.HookPoints();
            l.CeilingHooks();
            l.MastHooks();
            foreach (var m in l.All) l.ById[m.Id] = m;
            return l;
        }

        // ------------------------------------------------------------------ (0.3.20, step B) the deck cells

        /// <summary>
        /// (0.3.20, step B) A design with deck slots: the places her openings would have if their cells were laid (full: her
        /// layout worked out with every slot in) added to hers, those that stand on, hang under or are anchored on a cell
        /// that starts empty and that she hasn't already (same id, or the same kind of place over the same space). Her own
        /// places keep their ids. Returns how many were added.
        /// </summary>
        public int MergeOpenings(MountLayout full, DeckSlotLayout slots)
        {
            if (full == null || slots == null) return 0;
            full.BindSlots(slots);
            int n = 0;
            foreach (var m in full.All)
            {
                if (m.Box == null || ById.ContainsKey(m.Id)) continue;
                if (!(m.SlotUnder >= 0 && slots.Slots[m.SlotUnder].StartsEmpty)) continue;
                if (m.Kind is MountKind.Wall or MountKind.Panel or MountKind.Flue || m.Kind == MountKind.Horizontal && m.Level >= 3) continue;
                if (All.Any(o => o.Kind == m.Kind && o.Row == m.Row && o.Level == m.Level && SameSpace(o.Box, m.Box))) continue;
                All.Add(m);
                ById[m.Id] = m;
                if (m.Kind == MountKind.Floor) floor.TryAdd((m.Deck, m.I, m.K, m.Level), m);
                n++;
            }
            return n;
        }

        static bool SameSpace(double[] a, double[] b)
        {
            double ox = Math.Min(a[3], b[3]) - Math.Max(a[0], b[0]), oy = Math.Min(a[4], b[4]) - Math.Max(a[1], b[1]), oz = Math.Min(a[5], b[5]) - Math.Max(a[2], b[2]);
            if (ox <= 0 || oy <= 0 || oz <= 0) return false;
            double va = (a[3] - a[0]) * (a[4] - a[1]) * (a[5] - a[2]), vb = (b[3] - b[0]) * (b[4] - b[1]) * (b[5] - b[2]);
            return ox * oy * oz > 0.5 * Math.Min(va, vb);
        }

        /// <summary>
        /// (0.3.20) Each place's deck cell (<see cref="MountPoint.SlotUnder"/>, <see cref="MountPoint.SlotOpen"/>): a floor
        /// place's (and those stacked on it, the shelves over it) the cell under it; a border place's its anchor cell's; a
        /// hook's the cell it hangs under (or stands on); a well top's panel place needs its cell open. Null slots: none.
        /// </summary>
        public void BindSlots(DeckSlotLayout slots)
        {
            foreach (var m in All)
            {
                m.SlotUnder = m.SlotOpen = -1;
                if (slots == null || m.Box == null) continue;
                double top = Def.Decks.FirstOrDefault(d => d.Code == m.Deck)?.Y ?? double.NaN;
                switch (m.Kind)
                {
                    case MountKind.Floor:
                        if (!double.IsNaN(top)) m.SlotUnder = slots.OnDeck(top, m.CX, m.CZ);
                        break;
                    case MountKind.Horizontal:
                        // ((0.3.28, note 230) a hood's roof stands on the walls round it, not on the cell under it)
                        if (IsHood(m.Id)) break;
                        if (m.Level >= 3) m.SlotOpen = slots.At(m.CX, m.Box[4] - 0.05, m.CZ);
                        else if (!double.IsNaN(top)) m.SlotUnder = slots.OnDeck(top, m.CX, m.CZ);
                        break;
                    case MountKind.Border:
                        if (!double.IsNaN(top)) m.SlotUnder = slots.OnDeck(top, m.X + DX[m.In] * 0.3, m.Z + DZ[m.In] * 0.3);
                        // ((0.3.28, note 230) a twin: only while the cell across the line (the first one's anchor) is out)
                        if (m.TwinSide && !double.IsNaN(top)) m.SlotOpen = slots.OnDeck(top, m.X - DX[m.In] * 0.3, m.Z - DZ[m.In] * 0.3);
                        break;
                    case MountKind.Hook:
                        m.SlotUnder = m.Hang == "up" ? slots.At(m.X, m.Y - 0.05, m.Z) : slots.At(m.X, m.Y + 0.05, m.Z);
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ what's kept clear

        void Box(string why, double x0, double y0, double z0, double x1, double y1, double z1) =>
            Clear.Add(new KeepClear { Why = why, Box = new[] { Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1) } });

        void Box(string why, double[] b, double grow = 0, double growY = 0)
        {
            if (b == null || b.Length < 6) return;
            if (grow <= 0 && growY <= 0) { Box(why, b[0], b[1], b[2], b[3], b[4], b[5]); return; }
            Margin(why, b, new[] { b[0] - grow, b[1] - growY, b[2] - grow, b[3] + grow, b[4] + growY, b[5] + grow });
        }

        /// <summary>
        /// (0.3.17) A fixture's margin (grown is its box core grown): kept within the closed room the fixture is in (a
        /// hearth's half block doesn't reach through the forecastle's front into the waist), and remembered with its core
        /// (wall pieces' places are kept clear of the core only).
        /// </summary>
        void Margin(string why, double[] core, double[] grown)
        {
            var g = (double[])grown.Clone();
            var r = Def.RoomAt((core[0] + core[3]) / 2, (core[1] + core[4]) / 2, (core[2] + core[5]) / 2);
            if (r != null && r.Enclosed)
                foreach (int a in new[] { 0, 2 })
                {
                    g[a] = Math.Max(g[a], Math.Min(r.Min[a], core[a]));
                    g[a + 3] = Math.Min(g[a + 3], Math.Max(r.Max[a], core[a + 3]));
                }
            Clear.Add(new KeepClear
            {
                Why = why, Core = new[] { Math.Min(core[0], core[3]), Math.Min(core[1], core[4]), Math.Min(core[2], core[5]), Math.Max(core[0], core[3]), Math.Max(core[1], core[4]), Math.Max(core[2], core[5]) },
                Box = new[] { Math.Min(g[0], g[3]), Math.Min(g[1], g[4]), Math.Min(g[2], g[5]), Math.Max(g[0], g[3]), Math.Max(g[1], g[4]), Math.Max(g[2], g[5]) }
            });
        }

        void Round(string why, double x, double z, double r, double y0, double y1) =>
            Clear.Add(new KeepClear { Why = why, Round = true, CX = x, CZ = z, R = r, Box = new[] { x - r, y0, z - r, x + r, y1, z + r } });

        public string ClearHit(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            foreach (var c in Clear) if (c.Hits(x0, y0, z0, x1, y1, z1)) return c.Why;
            return null;
        }

        /// <summary>(0.3.17) As ClearHit, a fixture's margin counting as its own box only: for a wall piece's place.</summary>
        public string ClearHitCore(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            foreach (var c in Clear) if (c.Hits(x0, y0, z0, x1, y1, z1, true)) return c.Why;
            return null;
        }

        bool cellsLaid;

        /// <summary>(22 October 2026, note 49) Does a box stand on her deck cells that start empty (a hatchway's)?</summary>
        bool OverOpening(double[] b)
        {
            if (b == null || b.Length < 6 || Def.Slots == null) return false;
            foreach (var c in Def.Slots.Slots)
                if (c?.Box != null && c.IsCell && c.StartsEmpty && Math.Abs(c.Top - b[1]) < 0.05 &&
                    Math.Min(c.Box[3], b[3]) - Math.Max(c.Box[0], b[0]) > 0.1 && Math.Min(c.Box[5], b[5]) - Math.Max(c.Box[2], b[2]) > 0.1) return true;
            return false;
        }

        void KeepClearZones()
        {
            var def = Def;
            foreach (var f in def.Fixtures)
            {
                var b = f.Box;
                var s = f.Sel;
                var p = f.Pos;
                if (ShipMounts.IsLegacyHook(f)) continue;
                switch (f.Type)
                {
                    case "door" when b != null:
                        // the doorway, a block each side of it, and its swing
                        if (f.Normal == "z") Box("door", b[0] - 0.25, b[1], b[2] - 1, b[3] + 0.25, b[4], b[5] + 1);
                        else Box("door", b[0] - 1, b[1], b[2] - 0.25, b[3] + 1, b[4], b[5] + 0.25);
                        break;
                    case "gate" when b != null:
                        // the leaf swings in (and the side ladder comes up there)
                        // (as far in as the leaf is long: it swings in about either end, 0.3.9's about its after one)
                        double inb = p[2] > 0 ? -1 : 1, reach = Math.Max(1.5, Math.Max(b[3] - b[0], b[5] - b[2]) + 0.25);
                        Box("gate", b[0] - 0.25, b[1], inb < 0 ? b[2] - reach : b[2], b[3] + 0.25, b[4] + 0.5, inb < 0 ? b[5] : b[5] + reach);
                        break;
                    case "hatch":
                        Box("hatch", s ?? b, 0.75, 0.5);
                        if ((s ?? b) is double[] hb) Margin("hatch", hb, new[] { hb[0] - 0.75, hb[1], hb[2] - 0.75, hb[3] + 0.75, hb[4] + 1.5, hb[5] + 0.75 });
                        break;
                    case "gun":
                        // (0.3.24, notes 176, 181) an empty gun place's room isn't kept: it blocks pieces only while a gun is
                        // mounted there (EntityShip.PlaceProblem), and a gun isn't mounted while something stands in it
                        if (ReservesRoom(def, f)) GunClear(f);
                        break;
                    case "anchor":
                        // the capstan's bars sweep round it (a ring 1.3 kept clear in the design), and the men at them
                        Round("capstan", p[0], p[2], 1.9, p[1], p[1] + 2);
                        break;
                    case "pump":
                        Box("pump", s ?? b, 1.0);
                        break;
                    case "helm":
                        // (0.3.17) the helmsman's room, a margin round the wheel (a railing may stand at a deck's edge in it)
                        Margin("helm", s ?? new[] { p[0] - 0.1, p[1], p[2] - 0.1, p[0] + 0.1, p[1] + 1, p[2] + 0.1 }, new[] { p[0] - 0.8, p[1], p[2] - 0.8, p[0] + 0.8, p[1] + 2, p[2] + 0.8 });
                        Box("helm", s, 0.3, 0.5);
                        break;
                    case "compass":
                        Box("binnacle", s, 0.3);
                        break;
                    case "hearth":
                        Box("hearth", b ?? s, 0.5);
                        break;
                    case "attach" when f.Kind == "plate":
                        if (f.Spawn is double[] sp) Box("spawn", sp[0] - 0.6, sp[1], sp[2] - 0.6, sp[0] + 0.6, sp[1] + 2, sp[2] + 0.6);
                        Box("plate", s, 0.3);
                        break;
                    case "window":
                        Box("window", b ?? s, 0.1);
                        break;
                    case "storage":
                        break;
                    default:
                        Box(f.Type == "attach" ? "attach-" + f.Kind : f.Type, s, 0.05);
                        Box(f.Type == "attach" ? "attach-" + f.Kind : f.Type, b, 0.05);
                        break;
                }
            }
            foreach (var l in def.Ladders)
            {
                foreach (var e in new[] { l.ExitBottom, l.ExitTop })
                {
                    if (e == null || l.Outside && e == l.ExitBottom) continue;
                    Box("ladder", e[0] - 0.75, e[1], e[2] - 0.75, e[0] + 0.75, e[1] + 2, e[2] + 0.75);
                }
                if (!l.Outside && l.Bottom != null && l.Top != null)
                    Box("ladder", Math.Min(l.Bottom[0], l.Top[0]) - 0.6, Math.Min(l.Bottom[1], l.Top[1]), Math.Min(l.Bottom[2], l.Top[2]) - 0.6,
                        Math.Max(l.Bottom[0], l.Top[0]) + 0.6, Math.Max(l.Bottom[1], l.Top[1]) + 1.9, Math.Max(l.Bottom[2], l.Top[2]) + 0.6);
            }
            foreach (var st in def.Stairs ?? Array.Empty<StairDef>())
            {
                if (st.Foot == null || st.Head == null) continue;
                double w = Math.Max(0.5, st.Width) / 2 + 0.25;
                double dx = st.Head[0] - st.Foot[0], dz = st.Head[2] - st.Foot[2], len = Math.Sqrt(dx * dx + dz * dz);
                double ux = len > 1e-6 ? dx / len : 1, uz = len > 1e-6 ? dz / len : 0;
                // the stair, a block before its foot (on the deck it starts from) and after its head (on the deck it comes
                // up to: not through the bulkhead under it into the room below, 0.3.9)
                var a = new[] { st.Foot[0] - ux, st.Foot[2] - uz };
                var c = new[] { st.Head[0] + ux, st.Head[2] + uz };
                double wx = Math.Abs(uz) * w, wz = Math.Abs(ux) * w;
                Box("stair", Math.Min(st.Foot[0], st.Head[0]) - wx, Math.Min(st.Foot[1], st.Head[1]), Math.Min(st.Foot[2], st.Head[2]) - wz,
                    Math.Max(st.Foot[0], st.Head[0]) + wx, Math.Max(st.Foot[1], st.Head[1]) + 2, Math.Max(st.Foot[2], st.Head[2]) + wz);
                Box("stair", Math.Min(a[0], st.Foot[0]) - wx, st.Foot[1], Math.Min(a[1], st.Foot[2]) - wz,
                    Math.Max(a[0], st.Foot[0]) + wx, st.Foot[1] + 2, Math.Max(a[1], st.Foot[2]) + wz);
                Box("stair", Math.Min(c[0], st.Head[0]) - wx, st.Head[1], Math.Min(c[1], st.Head[2]) - wz,
                    Math.Max(c[0], st.Head[0]) + wx, st.Head[1] + 2, Math.Max(c[1], st.Head[2]) + wz);
            }
            foreach (var k in def.KeepClear ?? Array.Empty<KeepClearDef>())
            {
                if (cellsLaid && k.What == "hatch" && OverOpening(k.Box)) continue;
                // (0.3.24, note 181) the design's own "gun"/"gun port" boxes of an empty gun place: not kept either
                if (k.What != null && k.What.StartsWith("gun") && FreeGunRooms(def).Any(r => r.Any(b => PiecePlanner.Overlap(b, k.Box, 0.05)))) continue;
                Box(k.What ?? "kept clear", k.Box);
            }
            foreach (var m in def.Masts)
            {
                if (def.BlockGrid?.Origin?.Length >= 3)
                {
                    // on the ship's grid a mast stands inside its own cell (cells, if it's thicker): only that is kept
                    // clear, a table goes right beside it (note 101)
                    double ox = def.BlockGrid.Frac(0), oz = def.BlockGrid.Frac(2), r = Math.Max(0.05, m.Radius) - 1e-3;
                    Box("mast", Math.Floor(m.X - r - ox) + ox, m.Y0, Math.Floor(m.Z - r - oz) + oz,
                        Math.Ceiling(m.X + r - ox) + ox, m.Y1, Math.Ceiling(m.Z + r - oz) + oz);
                }
                else Round("mast", m.X, m.Z, m.Radius + 0.35, m.Y0, m.Y1);
            }
            foreach (var bp in def.BoardPoints)
                if (bp?.Length == 3) Box("boarding", bp[0] - 0.6, bp[1], bp[2] - 0.6, bp[0] + 0.6, bp[1] + 2, bp[2] + 0.6);
        }

        /// <summary>
        /// (0.3.24, notes 176, 181: "gun ports should be symmetric on both sides; conflict with stairs is a thing to resolve
        /// by checking whether the gun or the stairs can be placed") Is this gun's working room kept clear in her layout (no
        /// places for pieces in it)? A fixed gun's and a place's that starts laid: yes. A place that starts empty: no, its
        /// room is free for pieces and stairs; it's checked when a piece is put (while a gun is mounted there,
        /// <see cref="FreeGunRoomHit"/>) and when a gun is mounted (nothing may stand in its room).
        /// </summary>
        public static bool ReservesRoom(ShipDefinition def, FixtureDef f) =>
            f?.Type == "gun" && !(f.Code != null && def?.GunPlaceOf != null && def.GunPlaceOf.TryGetValue(f.Code, out var gp) && !gp.StartsLaid);

        /// <summary>(0.3.24) The working rooms of her gun places that start empty (not kept in her layout).</summary>
        public static IEnumerable<List<double[]>> FreeGunRooms(ShipDefinition def)
        {
            foreach (var f in def?.Fixtures ?? Array.Empty<FixtureDef>())
                if (f.Type == "gun" && !ReservesRoom(def, f)) yield return GunRoom(f);
        }

        /// <summary>
        /// (0.3.24, note 181) The first of these boxes that runs into the working room of a gun mounted on a place that
        /// starts empty (its room isn't kept in her layout), or null. <paramref name="mounted"/>: is that gun there now.
        /// </summary>
        public static FixtureDef FreeGunRoomHit(ShipDefinition def, IEnumerable<double[]> boxes, Func<FixtureDef, bool> mounted, bool railing = false)
        {
            if (def?.Fixtures == null || boxes == null) return null;
            var list = boxes.Where(b => b?.Length >= 6).ToList();
            foreach (var f in def.Fixtures)
            {
                if (f.Type != "gun" || ReservesRoom(def, f) || !mounted(f)) continue;
                var room = GunRoom(f);
                for (int i = 0; i < room.Count; i++)
                {
                    // ((0.3.29, note 232) a railing only out of the carriage's part: its crew step round it)
                    if (railing && i >= GunPlaces.CarriageSlices && i < GunRoomSlices) continue;
                    foreach (var b in list)
                        if (PiecePlanner.Overlap(b, room[i])) return f;
                }
            }
            return null;
        }

        /// <summary>
        /// A gun's room: from its port in to behind its gun captain's place (it runs back that far), its trail's swept shape
        /// as it's trained to each side's own limit (0.3.19: before, the larger limit both ways and one box round the whole
        /// sweep, which took floor cells the trail never reaches); its own boxes. The sweep is kept as one box per slice of
        /// the trail's length (8), each round that slice turned through its train (a degree at a time).
        /// </summary>
        void GunClear(FixtureDef f)
        {
            var room = GunRoom(f);
            for (int i = 0; i < room.Count; i++)
            {
                // (its own two boxes grown a little, as before)
                if (i >= GunRoomSlices) Box("gun", room[i], 0.05);
                else Box("gun", room[i]);
            }
        }

        /// <summary>
        /// (0.3.21, step C) A gun's working room as boxes (ship-local): the trail's sweep, one box per slice (see
        /// <see cref="GunClear"/>), then its selection box and its collision box when it has them (the last two, ungrown).
        /// A gun place needs it clear to take a gun.
        /// </summary>
        public const int GunRoomSlices = 8;

        public static List<double[]> GunRoom(FixtureDef f)
        {
            var room = new List<double[]>();
            if (f?.Pos == null) return room;
            const int slices = GunRoomSlices;
            var o = f.Out ?? new[] { 0.0, 0, f.Pos[2] < 0 ? -1.0 : 1.0 };
            var piv = f.Pivot ?? f.Pos;
            var seat = f.Seat ?? new[] { f.Pos[0] - o[0] * 1.9, f.Pos[1], f.Pos[2] - o[2] * 1.9 };
            double ax = -o[0], az = -o[2], al = Math.Sqrt(ax * ax + az * az);
            if (al < 1e-6) { ax = 0; az = 1; } else { ax /= al; az /= al; }
            double len = Math.Sqrt((seat[0] - piv[0]) * (seat[0] - piv[0]) + (seat[2] - piv[2]) * (seat[2] - piv[2])) + 0.8;
            GunLaying.Limits(f, out float t0, out float t1, out _, out _);
            // (gate open or not: the wider of its two trains each way)
            if (f.TrainLimitsGateOpen?.Length >= 2) { GunLaying.Limits(f, out float g0, out float g1, out _, out _, true); t0 = Math.Min(t0, g0); t1 = Math.Max(t1, g1); }
            double sign = GunLaying.TrainSign(f);
            double cx = -az, cz = ax;
            const double half = 0.45;
            for (int i = 0; i < slices; i++)
            {
                double u0 = len * i / slices, u1 = len * (i + 1) / slices;
                double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
                int steps = Math.Max(1, (int)Math.Ceiling(t1 - t0));
                for (int k = 0; k <= steps; k++)
                {
                    double a = (t0 + (t1 - t0) * k / steps) * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
                    foreach (double u in new[] { u0, u1 })
                        foreach (double w in new[] { -half, half })
                        {
                            // a point of the trail in the gun's frame, turned about the upright through its pivot as GunLaying turns it
                            double vx = ax * u + cx * w, vz = az * u + cz * w;
                            double rx = vx * c + sign * vz * sn, rz = vz * c - sign * vx * sn;
                            x0 = Math.Min(x0, piv[0] + rx); x1 = Math.Max(x1, piv[0] + rx);
                            z0 = Math.Min(z0, piv[2] + rz); z1 = Math.Max(z1, piv[2] + rz);
                        }
                }
                room.Add(new[] { x0, f.Pos[1], z0, x1, f.Pos[1] + 1.6, z1 });
            }
            if (f.Sel?.Length >= 6 && f.Box?.Length >= 6) { room.Add(f.Sel); room.Add(f.Box); }
            else if (f.Sel?.Length >= 6) { room.Add(f.Sel); room.Add(f.Sel); }
            else if (f.Box?.Length >= 6) { room.Add(f.Box); room.Add(f.Box); }
            return room;
        }

        // ------------------------------------------------------------------ floor places

        int Sub => Math.Max(1, (int)Math.Round(1 / Def.Grid.V));

        /// <summary>Cells of the grid at a coordinate (rounded to the grid, so a cell's corner on a grid line is that line).</summary>
        int Cell(double v, double o) => (int)Math.Floor((v - o) / Def.Grid.V + 1e-6);

        void FloorPlaces()
        {
            var g = Def.Grid;
            var grid = Def.BlockGrid?.Origin?.Length >= 3 ? Def.BlockGrid : null;
            OnShipGrid = grid != null;
            foreach (var deck in Def.Decks)
            {
                if (deck.Code == "top") continue;
                List<MountPoint> best = null;
                (double, double) bestAlign = (0, 0);
                if (grid != null)
                {
                    // the ship's one grid, on every deck alike
                    bestAlign = (grid.Frac(0), grid.Frac(2));
                    best = FloorCells(deck, bestAlign.Item1, bestAlign.Item2, true);
                }
                else
                    for (int a = 0; a < 16; a++)
                    {
                        double ox = (a >> 2) * 0.25, oz = (a & 3) * 0.25;
                        var cells = FloorCells(deck, ox, oz);
                        if (best == null || cells.Count > best.Count)
                        {
                            best = cells;
                            bestAlign = (ox, oz);
                        }
                    }
                Align[deck.Code] = bestAlign;
                foreach (var m in best)
                {
                    All.Add(m);
                    floor[(deck.Code, m.I, m.K, 0)] = m;
                }
                // a block up (and two) over each, where she has the room: a piece put on one standing there
                foreach (var m in best)
                {
                    var under = m;
                    for (int level = 1; level <= MaxStack; level++)
                    {
                        double y = m.Y + level;
                        if (y + 1 > g.MaxY - 1e-6) break;
                        if (g.Collides(m.X + 0.01, y + 0.01, m.Z + 0.01, m.X + 0.99, y + 0.99, m.Z + 0.99)) break;
                        if (ClearHit(m.X, y, m.Z, m.X + 1, y + 1, m.Z + 1) != null) break;
                        var s = new MountPoint
                        {
                            Id = $"{m.Id}:{level}", Kind = MountKind.Floor, Deck = m.Deck, Room = Def.RoomAt(m.X + 0.5, y + 0.5, m.Z + 0.5)?.Code ?? m.Room,
                            X = m.X, Y = y, Z = m.Z, I = m.I, K = m.K, Level = level, Under = under.Id,
                            Box = new[] { m.X, y, m.Z, m.X + 1, y + 1, m.Z + 1 }
                        };
                        All.Add(s);
                        floor[(deck.Code, m.I, m.K, level)] = s;
                        under = s;
                    }
                }
            }
        }

        /// <summary>
        /// (0.3.17) Cells on the ship's grid that would be floor places but for a fixture's margin (the helm's, the binnacle's):
        /// no floor place, but the anchor of a wall piece's place at the deck's edge (a railing at the quarterdeck's front).
        /// </summary>
        readonly Dictionary<(string, int, int), MountPoint> marginCells = new();

        List<MountPoint> FloorCells(DeckDef deck, double ox, double oz, bool collect = false)
        {
            var g = Def.Grid;
            var list = new List<MountPoint>();
            double y = deck.Y;
            int j0 = Cell(y, g.OY), n = Sub;
            if (j0 < 1 || j0 + n > g.NY) return list;
            double xa = Math.Max(deck.X0, g.OX), xb = Math.Min(deck.X1, g.MaxX);
            int i0 = (int)Math.Ceiling(xa - ox - 1e-9), i1 = (int)Math.Floor(xb - ox - 1 + 1e-9);
            int k0 = (int)Math.Ceiling(g.OZ - oz - 1e-9), k1 = (int)Math.Floor(g.MaxZ - oz - 1 + 1e-9);
            for (int i = i0; i <= i1; i++)
            {
                double x0 = ox + i;
                int ci = Cell(x0, g.OX);
                for (int k = k0; k <= k1; k++)
                {
                    double z0 = oz + k;
                    int ck = Cell(z0, g.OZ);
                    bool free = FloorFree(ci, j0, ck, n);
                    if (!free && !(collect && FloorUnder(ci, j0, ck, n))) continue;
                    // (0.3.17: kept clear only for a fixture's margin, or by something standing on air near it (a binnacle's
                    // bowl, a tread): no floor place, but a wall piece's place at the deck's edge may stand on its side)
                    // (or something of hers or a fixture standing in it, a binnacle at a castle's front: its side may still be free)
                    bool margin = !free || ClearHit(x0, y, z0, x0 + 1, y + 1, z0 + 1) != null || TreadNear(ci, j0, ck, n);
                    if (margin && !collect) continue;
                    var room = Def.RoomAt(x0 + 0.5, y + 0.5, z0 + 0.5);
                    if (margin)
                    {
                        marginCells[(deck.Code, i, k)] = new MountPoint
                        {
                            Id = $"f:{deck.Code}:{i}:{k}", Kind = MountKind.Floor, Deck = deck.Code, Room = room?.Code ?? deck.Code + "-deck",
                            X = x0, Y = y, Z = z0, I = i, K = k, Box = new[] { x0, y, z0, x0 + 1, y + 1, z0 + 1 }
                        };
                        continue;
                    }
                    list.Add(new MountPoint
                    {
                        Id = $"f:{deck.Code}:{i}:{k}", Kind = MountKind.Floor, Deck = deck.Code, Room = room?.Code ?? deck.Code + "-deck",
                        X = x0, Y = y, Z = z0, I = i, K = k, Box = new[] { x0, y, z0, x0 + 1, y + 1, z0 + 1 }
                    });
                }
            }
            return list;
        }

        /// <summary>(0.3.17) Solid floor under the whole cell.</summary>
        bool FloorUnder(int ci, int j0, int ck, int n)
        {
            var g = Def.Grid;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    if (!g.Solid(ci + a, j0 - 1, ck + b)) return false;
            return true;
        }

        /// <summary>Solid floor under the whole cell, and a block of room over it.</summary>
        bool FloorFree(int ci, int j0, int ck, int n)
        {
            var g = Def.Grid;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                {
                    if (!g.Solid(ci + a, j0 - 1, ck + b)) return false;
                    for (int h = 0; h < n; h++) if (g.Solid(ci + a, j0 + h, ck + b)) return false;
                }
            return true;
        }

        /// <summary>
        /// A stair's tread (or anything standing on air) within a block of the cell, at most a block and a quarter over the
        /// deck: something you step onto there (the cell before a stair's foot, and under its high end, are kept clear).
        /// </summary>
        bool TreadNear(int ci, int j0, int ck, int n)
        {
            var g = Def.Grid;
            for (int i = ci - n; i < ci + 2 * n; i++)
                for (int k = ck - n; k < ck + 2 * n; k++)
                    for (int j = j0 + 1; j <= j0 + n + n / 4; j++)
                    {
                        if (!g.Solid(i, j, k) || g.Solid(i, j - 1, k) || !g.Solid(i, j0 - 1, k)) continue;
                        bool free = true;
                        for (int h = 1; h <= n + n / 2 && free; h++) if (g.Solid(i, j + h, k)) free = false;
                        if (free) return true;
                    }
            return false;
        }

        // ------------------------------------------------------------------ wall spots

        /// <summary>The deck a room's floor is (the lowest deck within its height).</summary>
        public DeckDef FloorOf(RoomDef r)
        {
            DeckDef best = null;
            foreach (var d in Def.Decks)
            {
                if (d.Code == "top" || d.Y < r.Min[1] - 0.01 || d.Y >= r.Max[1]) continue;
                double cx = (Math.Max(r.Min[0], d.X0) + Math.Min(r.Max[0], d.X1)) / 2;
                if (cx < d.X0 || cx > d.X1) continue;
                if (best == null || d.Y < best.Y) best = d;
            }
            return best;
        }

        void WallSpots()
        {
            var g = Def.Grid;
            foreach (var r in Def.Rooms)
            {
                if (!r.Enclosed) continue;
                var deck = FloorOf(r);
                if (deck == null) continue;
                double fy = deck.Y;
                var (ox, oz) = Align.TryGetValue(deck.Code, out var al) ? al : (0.0, 0.0);
                var rows = new List<(int Row, double Y0)>();
                var heights = Rows;
                for (int ri = 0; ri < heights.Length; ri++)
                    if (fy + heights[ri] + 1 <= r.Max[1] + 1e-6) rows.Add((ri, fy + heights[ri]));
                // the sides: along the room's length
                for (int s = -1; s <= 1; s += 2)
                {
                    int iIn = s > 0 ? 3 : 1;
                    string key = r.Code + ":" + (s > 0 ? "s" : "p");
                    int i0 = (int)Math.Ceiling(r.Min[0] - ox - 1e-9), i1 = (int)Math.Floor(r.Max[0] - ox - 1 + 1e-9);
                    for (int i = i0; i <= i1; i++)
                    {
                        double xc = ox + i + 0.5;
                        foreach (var (row, y0) in rows)
                        {
                            var w = new double[9];
                            bool ok = true;
                            int n = 0;
                            for (int a = -1; a <= 1 && ok; a++)
                                for (int b = 0; b < 3 && ok; b++)
                                {
                                    double? z = SideSurface(xc + a * 0.4, y0 + 0.15 + b * 0.35, s);
                                    if (z == null) ok = false; else w[n++] = z.Value;
                                }
                            if (!ok) continue;
                            AddWall(r, deck, key, i, row, iIn, xc, y0, w, s, axisX: false);
                        }
                    }
                }
                // the ends: across the room
                for (int e = -1; e <= 1; e += 2)
                {
                    int iIn = e > 0 ? 2 : 0;
                    string key = r.Code + ":" + (e > 0 ? "f" : "a");
                    int k0 = (int)Math.Ceiling(r.Min[2] - oz - 1e-9), k1 = (int)Math.Floor(r.Max[2] - oz - 1 + 1e-9);
                    for (int k = k0; k <= k1; k++)
                    {
                        double zc = oz + k + 0.5;
                        foreach (var (row, y0) in rows)
                        {
                            var w = new double[9];
                            bool ok = true;
                            int n = 0;
                            for (int a = -1; a <= 1 && ok; a++)
                                for (int b = 0; b < 3 && ok; b++)
                                {
                                    double? x = EndSurface(e > 0 ? r.Max[0] : r.Min[0], y0 + 0.15 + b * 0.35, zc + a * 0.4, e);
                                    if (x == null) ok = false; else w[n++] = x.Value;
                                }
                            if (!ok) continue;
                            AddWall(r, deck, key, k, row, iIn, zc, y0, w, e, axisX: true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A wall spot from its wall's face sampled at 3 × 3 points (along it, up it): the face it hangs on is the most
        /// inboard sample; the wall's lean and turn from the samples; the space before it must be free and clear.
        /// side: which way is out (+1: +z or +x). axisX: the wall faces along x (a room's end).
        /// </summary>
        void AddWall(RoomDef r, DeckDef deck, string key, int index, int row, int iIn, double c, double y0, double[] w, int side, bool axisX)
        {
            var g = Def.Grid;
            double inner = side > 0 ? w.Min() : w.Max(), outer = side > 0 ? w.Max() : w.Min();
            if (Math.Abs(outer - inner) > WallEven) return;
            // the wall must be this room's (not the hull beyond the room's own bounds)
            double lo = axisX ? r.Min[0] : r.Min[2], hi = axisX ? r.Max[0] : r.Max[2];
            if (inner < lo - 0.75 || inner > hi + 0.75) return;
            double a0 = Math.Min(inner, inner - side * WallDepth), a1 = Math.Max(inner, inner - side * WallDepth);
            double[] box = axisX ? new[] { a0, y0, c - 0.5, a1, y0 + 1, c + 0.5 } : new[] { c - 0.5, y0, a0, c + 0.5, y0 + 1, a1 };
            if (g.Collides(box[0] + 0.02, box[1] + 0.02, box[2] + 0.02, box[3] - 0.02, box[4] - 0.02, box[5] - 0.02)) return;
            if (ClearHit(box[0], box[1], box[2], box[3], box[4], box[5]) != null) return;
            // the lean (up the wall) and the turn (along it): a least-squares plane through the samples
            double up = 0, along = 0;
            for (int a = -1; a <= 1; a++)
                for (int b = 0; b < 3; b++)
                {
                    double v = w[(a + 1) * 3 + b];
                    up += v * (b - 1) / (0.35 * 6);
                    along += v * a / (0.4 * 6);
                }
            // the normal into the room: -side along the wall's axis, leaning by the slopes
            double nx, ny, nz;
            if (axisX) { nx = -side; ny = side * up; nz = side * along; }
            else { nz = -side; ny = side * up; nx = side * along; }
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= nl; ny /= nl; nz /= nl;
            // a wall leaning more than 25° isn't a wall to hang things on; on the ship's grid only an upright one is
            double most = OnShipGrid ? Upright : 0.42;
            if (Math.Abs(ny) > most || Math.Abs(axisX ? nz : nx) > most) return;
            if (OnShipGrid) { nx = axisX ? -side : 0; ny = 0; nz = axisX ? 0 : -side; }
            string side2 = key.Substring(key.LastIndexOf(':') + 1);
            var m = new MountPoint
            {
                Id = $"w:{r.Code}:{side2}:{index}:{row}", Kind = MountKind.Wall, Deck = deck.Code, Room = r.Code, Wall = key,
                I = index, Row = row, In = iIn, Normal = new[] { nx, ny, nz },
                X = axisX ? inner : c, Y = y0, Z = axisX ? c : inner, Box = box
            };
            All.Add(m);
            wall[(key, index, row)] = m;
        }

        // ------------------------------------------------------------------ panels (the ship's grid)

        /// <summary>A coordinate for an id: "5.5", "-4.5", "12".</summary>
        static string F(double v) => Math.Round(v, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// On the ship's grid: where a room's outermost floor cell meets her side with a wedge of room between the cell's
        /// border and the side (it leans in over it, or flares out under it), a panel place a block high, on the floor and
        /// a block up (to the deck structure's band). The wedge: beyond the border, within a block, part free and part her
        /// side (all free is open room, none free the side is flush already). Not where anything is kept clear (a doorway, a
        /// stair, a gun's room, a window). Each panel place has a wall spot on its face for a piece hung on the panel.
        /// </summary>
        void PanelPlaces()
        {
            if (!OnShipGrid) return;
            var g = Def.Grid;
            int n = Math.Max(1, (int)Math.Round(1 / g.V));
            foreach (var m in Floors.ToList())
            {
                var room = Def.RoomAt(m.X + 0.5, m.Y + 0.5, m.Z + 0.5);
                if (room == null || !room.Enclosed) continue;
                for (int dir = 0; dir < 4; dir++)
                {
                    if (FloorAt(m.Deck, m.I + DX[dir], m.K + DZ[dir]) != null) continue;
                    bool alongX = dir == 1 || dir == 3;          // the border runs along x (her side)
                    double line = dir switch { 0 => m.X + 1, 2 => m.X, 1 => m.Z + 1, _ => m.Z };
                    int outSign = dir < 2 ? 1 : -1;
                    // where her side flares out going up (the hold, the lower deck's ends: note 80) the wedge is widest at
                    // the panel's top and may run deeper than a block (to MaxPanelDepth): when its foot is near (the lowest
                    // layer of the first row meets her within a block), and over a panel so placed
                    int layers = (int)Math.Round(MaxPanelDepth * n);
                    MountPoint below = null;
                    for (int row = 0; row < PanelRows; row++)
                    {
                        double y0 = m.Y + row;
                        if (y0 + 1 > room.Max[1] + 1e-6) break;
                        // the wedge: n × n columns across the border's block, each walked out from the border a block
                        int free = 0, full = 0, open = 0;
                        double deepest = 0;
                        var depth = new int[n, n];
                        bool footNear = true;
                        for (int a = 0; a < n; a++)
                            for (int b = 0; b < n; b++)
                            {
                                double along = (alongX ? m.X : m.Z) + (a + 0.5) * g.V, y = y0 + (b + 0.5) * g.V;
                                int layer = 0;
                                for (; layer < layers; layer++)
                                {
                                    double o = line + outSign * (layer + 0.5) * g.V;
                                    if (alongX ? g.SolidAt(along, y, o) : g.SolidAt(o, y, along)) break;
                                }
                                depth[a, b] = layer;
                                if (b == 0 && layer >= n) footNear = false;
                            }
                        int most = (row == 0 ? footNear : below != null) ? layers : n;
                        for (int a = 0; a < n; a++)
                            for (int b = 0; b < n; b++)
                            {
                                int layer = depth[a, b];
                                if (layer == 0) full++;
                                else if (layer >= most) open++;
                                else { free++; deepest = Math.Max(deepest, layer * g.V); }
                            }
                        if (open > 0 || free == 0) { below = null; continue; }
                        double d0 = line, d1 = line + outSign * deepest;
                        var box = alongX
                            ? new[] { m.X, y0, Math.Min(d0, d1), m.X + 1, y0 + 1, Math.Max(d0, d1) }
                            : new[] { Math.Min(d0, d1), y0, m.Z, Math.Max(d0, d1), y0 + 1, m.Z + 1 };
                        if (ClearHit(box[0], box[1], box[2], box[3], box[4], box[5]) != null) { below = null; continue; }
                        int inn = (dir + 2) % 4;
                        double fx = alongX ? m.X + 0.5 : line, fz = alongX ? line : m.Z + 0.5;
                        string sideCode = dir switch { 0 => "f", 2 => "a", 1 => "s", _ => "p" };
                        var normal = new double[] { DX[inn], 0, DZ[inn] };
                        var panel = new MountPoint
                        {
                            Id = $"p:{m.Deck}:{m.I}:{m.K}:{DirCode[dir][0]}:{row}", Kind = MountKind.Panel, Deck = m.Deck, Room = room.Code,
                            X = fx, Y = y0, Z = fz, I = m.I, K = m.K, Row = row, In = inn, Normal = normal, Depth = deepest, Box = box,
                            Under = row > 0 ? below?.Id : null
                        };
                        All.Add(panel);
                        below = panel;
                        // the spot on its face (a painting, wallpaper, a shelf hung on the panel)
                        string key = room.Code + ":" + sideCode + F(line);
                        int index = alongX ? m.I : m.K;
                        double a0 = Math.Min(line, line - outSign * WallDepth), a1 = Math.Max(line, line - outSign * WallDepth);
                        double[] sb = alongX ? new[] { m.X, y0, a0, m.X + 1, y0 + 1, a1 } : new[] { a0, y0, m.Z, a1, y0 + 1, m.Z + 1 };
                        if (wall.ContainsKey((key, index, row)) || ClearHit(sb[0], sb[1], sb[2], sb[3], sb[4], sb[5]) != null) continue;
                        var spot = new MountPoint
                        {
                            Id = $"w:{room.Code}:{sideCode}{F(line)}:{index}:{row}", Kind = MountKind.Wall, Deck = m.Deck, Room = room.Code, Wall = key,
                            I = index, Row = row, In = inn, Normal = (double[])normal.Clone(), X = fx, Y = y0, Z = fz, Box = sb, Under = panel.Id
                        };
                        All.Add(spot);
                        wall[(key, index, row)] = spot;
                    }
                }
            }
        }

        /// <summary>
        /// On the ship's grid: the borders of the closed rooms' floor cells where a partition (or a door in one) may stand
        /// (<see cref="MountKind.Border"/>): a block high on the floor and a block up (to the deck structure's band), where
        /// the layer of the cell along the border is free of her (not her leaning side: the panels are for that), the other
        /// side isn't solid (a flush wall needs no partition), and nothing is kept clear in the cell along it (doorways).
        /// (0.3.13, note 137: "cannot still fill till ceiling timber partition walls, also cannot do it to curved hull") Also
        /// where that layer is only partly free of her: the top row under her beams and deck (up to the room's top), and the
        /// cells along a partition line out to her curved side beyond the last floor cell (the wedge). Such a place's
        /// <see cref="MountPoint.Clip"/> is what's free there (the partition is drawn, picked and solid only in it), so a
        /// wall put up meets her timbers and planking with no gap.
        /// </summary>
        void BorderPlaces()
        {
            if (!OnShipGrid) return;
            var g = Def.Grid;
            var panels = new HashSet<string>(Panels.Select(p => p.Id.Substring(2)));
            var ids = new HashSet<string>();
            segments.Clear();
            // (cells along a partition line out to her side: done once per cell and way, after every floor cell's own)
            var wedgeDone = new HashSet<(string, int, int, int)>();
            var lines = new List<(MountPoint m, RoomDef room, int dir)>();
            var openCells = new List<MountPoint>();
            foreach (var m in Floors.ToList())
            {
                var room = Def.RoomAt(m.X + 0.5, m.Y + 0.5, m.Z + 0.5);
                // (0.3.16, notes 155, 157) on an open deck too (railings at a castle's front, round a well, a cabin front
                // seen from the waist): two rows over the deck, no wedge cells; after the closed rooms' places (theirs keep
                // their ids and segments as before)
                if (room == null || !room.Enclosed) { openCells.Add(m); continue; }
                for (int dir = 0; dir < 4; dir++)
                {
                    var n = FloorAt(m.Deck, m.I + DX[dir], m.K + DZ[dir]);
                    if (n != null)
                    {
                        // between two floor cells: once, from the west or north one; (0.3.16) from the closed room's when
                        // the other is an open deck (a cabin front: its top row under the beams is the room's)
                        bool nClosed = Def.RoomAt(n.X + 0.5, n.Y + 0.5, n.Z + 0.5)?.Enclosed == true;
                        if (nClosed != room.Enclosed) { if (!room.Enclosed) continue; }
                        else if (dir >= 2) continue;
                    }
                    if (BorderColumn(m.Deck, room, m.I, m.K, m.X, m.Y, m.Z, dir, n == null, panels, ids, false)) lines.Add((m, room, dir));
                }
            }
            // (0.3.13) along the line, past the last floor cell toward her side: the wedge cells, partly her
            // (0.3.17, the generator: "open decks get no wedge/partial places", fixed railing bits at the castle fronts'
            // corners by her sides) on an open deck too: its cells not in a closed room
            void Wedges(MountPoint m, RoomDef room, int dir)
            {
                bool alongX = dir == 1 || dir == 3;
                foreach (int s in new[] { -1, 1 })
                {
                    int di = alongX ? s : 0, dk = alongX ? 0 : s;
                    for (int step = 1; step <= 3; step++)
                    {
                        int ci = m.I + di * step, ck = m.K + dk * step;
                        if (FloorAt(m.Deck, ci, ck) != null) break;
                        if (!wedgeDone.Add((m.Deck, ci, ck, dir))) break;
                        double cx = m.X + di * step, cz = m.Z + dk * step;
                        var rc = Def.RoomAt(cx + 0.5, m.Y + 0.5, cz + 0.5);
                        if (room.Enclosed ? rc?.Code != room.Code : rc?.Enclosed == true) break;
                        if (!BorderColumn(m.Deck, room.Enclosed ? room : FrontRoom(room, cx, m.Y, cz, dir), ci, ck, cx, m.Y, cz, dir,
                                          FloorAt(m.Deck, ci + DX[dir], ck + DZ[dir]) == null, panels, ids, true)) break;
                    }
                }
            }
            foreach (var (m, room, dir) in lines) Wedges(m, room, dir);
            // (0.3.16) the open decks' places after the closed rooms' (theirs keep their ids and segments as before)
            var openLines = new List<(MountPoint m, RoomDef room, int dir)>();
            foreach (var m in openCells)
            {
                var room = OpenDeckRoom(m);
                for (int dir = 0; dir < 4; dir++)
                {
                    var n = FloorAt(m.Deck, m.I + DX[dir], m.K + DZ[dir]);
                    if (n != null && (dir >= 2 || Def.RoomAt(n.X + 0.5, n.Y + 0.5, n.Z + 0.5)?.Enclosed == true)) continue;
                    if (BorderColumn(m.Deck, FrontRoom(room, m.X, m.Y, m.Z, dir), m.I, m.K, m.X, m.Y, m.Z, dir, n == null, panels, ids, false)) openLines.Add((m, room, dir));
                }
            }
            foreach (var (m, room, dir) in openLines) Wedges(m, room, dir);
            // (0.3.17) a cell kept clear only for a fixture's margin (the helm's room, the binnacle's): its sides at the deck's
            // edge (no floor place nor such a cell beyond) are places, a railing at a castle's front by the helm (note 155)
            foreach (var m in marginCells.Values)
            {
                // (on the open decks only: a castle's front by the helm and the binnacle; a closed room's places are as before)
                if (Def.RoomAt(m.X + 0.5, m.Y + 0.5, m.Z + 0.5)?.Enclosed == true) continue;
                var room = OpenDeckRoom(m);
                for (int dir = 0; dir < 4; dir++)
                {
                    if (FloorAt(m.Deck, m.I + DX[dir], m.K + DZ[dir]) != null || marginCells.ContainsKey((m.Deck, m.I + DX[dir], m.K + DZ[dir]))) continue;
                    BorderColumn(m.Deck, room, m.I, m.K, m.X, m.Y, m.Z, dir, true, panels, ids, false);
                }
            }
        }

        /// <summary>
        /// (22 October 2026, caravel note 38: "cannot close of aft deck (open cabin) with partition walls up to upper deck") The
        /// room an open deck's border column on a cell's side dir is in: a closed room's where the cell across the line is in
        /// it but no floor cell of it (a rib's cell at the steerage's front, by her side), so the column goes up to that room's
        /// top under the deck over it (its partial top row) as from the room's own side; else the open deck's.
        /// </summary>
        RoomDef FrontRoom(RoomDef open, double x0, double y0, double z0, int dir)
        {
            var across = Def.RoomAt(x0 + DX[dir] + 0.5, y0 + 0.5, z0 + DZ[dir] + 0.5);
            return across?.Enclosed == true ? across : open;
        }

        /// <summary>
        /// The border places up one cell's side (a cell on the deck at x0 y0 z0, its corner; way dir), from the floor up to
        /// the room's top: a whole place where the layer along the border is free of her, else (0.3.13) the part that's free.
        /// wedge: a cell past the last floor cell (no whole places there, and the floor may be her side). Whether any was made.
        /// </summary>
        /// <summary>(0.3.13) The border segments placed so far (deck, row, the line, along it, its way): one place on each, from either side.</summary>
        readonly HashSet<(string, int, double, double, bool)> segments = new();

        bool BorderColumn(string deck, RoomDef room, int ci, int ck, double x0, double y0, double z0, int dir, bool lastCell,
            HashSet<string> panels, HashSet<string> ids, bool wedge)
        {
            var g = Def.Grid;
            const double L = 0.25, e = 0.02;
            int cells = Math.Max(1, (int)Math.Round(1 / g.V));
            bool alongX = dir == 1 || dir == 3;
            double line = dir switch { 0 => x0 + 1, 2 => x0, 1 => z0 + 1, _ => z0 };
            int outSign = dir < 2 ? 1 : -1;
            int inn = (dir + 2) % 4;
            double inSign = inn < 2 ? 1 : -1;
            double layer = line + inSign * g.V / 2;      // the anchor side's voxel layer along the line
            double along0 = alongX ? x0 : z0;
            double top = room.Max[1];
            MountPoint below = null;
            bool belowShut = wedge;     // (the row under is her: a place over it needs nothing under it)
            bool any = false;
            for (int row = 0; row <= PanelRows; row++)
            {
                double ry = y0 + row;
                if (ry >= top - 0.2) break;
                string tail = $"{deck}:{ci}:{ck}:{DirCode[dir][0]}:{row}";
                if (panels.Contains(tail)) { below = null; belowShut = false; continue; }
                // what's free of her in the layer: cells × cells sub-blocks (along, up), not over the room's top
                var free = new bool[cells, cells];
                int nFree = 0;
                for (int a = 0; a < cells; a++)
                    for (int bb = 0; bb < cells; bb++)
                    {
                        double al = along0 + (a + 0.5) * g.V, y = ry + (bb + 0.5) * g.V;
                        bool solid = alongX ? g.SolidAt(al, y, layer) : g.SolidAt(layer, y, al);
                        // (0.3.14, note 149) and inside her: a wedge cell's layer past her planking is the sea air (it made a
                        // place outside her side, and the whole place inboard of it ended a voxel short of her ceiling)
                        free[a, bb] = !solid && y < top + g.V && (alongX ? InsideHer(al, y, layer) : InsideHer(layer, y, al));
                        if (free[a, bb]) nFree++;
                    }
                double a0 = Math.Min(line, line - outSign * L), a1 = Math.Max(line, line - outSign * L);
                double b0 = Math.Min(line, line + outSign * L), b1 = Math.Max(line, line + outSign * L);
                double rowTop = Math.Min(ry + 1, top + g.V);
                double[] inside = alongX ? new[] { x0, ry, a0, x0 + 1, ry + 1, a1 } : new[] { a0, ry, z0, a1, ry + 1, z0 + 1 };
                double[] beyond = alongX ? new[] { x0, ry, b0, x0 + 1, ry + 1, b1 } : new[] { b0, ry, z0, b1, ry + 1, z0 + 1 };
                bool whole = nFree == cells * cells && !wedge && ry + 1 <= top + 1e-6 &&
                             !g.Collides(inside[0] + e, inside[1] + e, inside[2] + e, inside[3] - e, inside[4] - e, inside[5] - e);
                if (whole && lastCell && g.Collides(beyond[0] + e, beyond[1] + e, beyond[2] + e, beyond[3] - e, beyond[4] - e, beyond[5] - e)) { below = null; belowShut = false; continue; }
                List<double[]> clip = null;
                if (!whole)
                {
                    // (22 October 2026, caravel note 48: "partition penetrates deck beam below") a partial row stands in both
                    // layers along the line: a timber of the deck over it hanging into the other one (up into the band over
                    // the room's top, free under it there: a carling flush with the line beside a hatchway; not her side
                    // leaning in) cuts it too, as one in its own layer does (DrawnClip carries it up to the timber's underside)
                    double farLayer = line - inSign * g.V / 2;
                    for (int a = 0; a < cells; a++)
                    {
                        bool under = false;
                        for (int bb = 0; bb < cells; bb++)
                        {
                            double al = along0 + (a + 0.5) * g.V, y = ry + (bb + 0.5) * g.V;
                            bool farSolid = y + g.V / 2 > top + 1e-6 && (alongX ? HangingAt(al, y, farLayer) : HangingAt(farLayer, y, al));
                            if (!farSolid) under = true;
                            else if (under && free[a, bb]) { free[a, bb] = false; nFree--; }
                        }
                    }
                    // (a sliver isn't worth a place: an eighth of the block at least)
                    if (nFree * 8 < cells * cells) { below = null; belowShut = nFree == 0; continue; }
                    clip = ClipRuns(free, cells, along0, ry, g.V);
                }
                // (kept clear: only the cell's own side of the line, the other side may be a doorway's or a stair's)
                double h0 = Math.Min(line, line - outSign * 0.01), h1 = Math.Max(line, line - outSign * 0.01);
                double cy1 = clip == null ? ry + 1 : clip.Max(c => c[3]), cy0 = clip == null ? ry : clip.Min(c => c[1]);
                double ca0 = clip == null ? along0 : clip.Min(c => c[0]), ca1 = clip == null ? along0 + 1 : clip.Max(c => c[2]);
                // (0.3.17: of a fixture's margin only the fixture itself: a railing at the quarterdeck's front by the binnacle)
                if (alongX ? ClearHitCore(ca0, cy0, h0, ca1, cy1, h1) != null : ClearHitCore(h0, cy0, ca0, h1, cy1, ca1) != null) { below = null; belowShut = false; continue; }
                if (row > 0 && below == null && !belowShut) continue;   // (a row over the floor only over a place, or over her)
                double fx = alongX ? x0 + 0.5 : line, fz = alongX ? line : z0 + 0.5;
                double s0 = line - 0.125, s1 = line + 0.125;
                var bp = new MountPoint
                {
                    Id = "b:" + tail, Kind = MountKind.Border, Deck = deck, Room = room.Code, X = fx, Y = ry, Z = fz, I = ci, K = ck, Row = row,
                    In = inn, Normal = new double[] { DX[inn], 0, DZ[inn] },
                    Box = alongX ? new[] { ca0, cy0, s0, ca1, cy1, s1 } : new[] { s0, cy0, ca0, s1, cy1, ca1 },
                    Under = row > 0 ? below?.Id : null,
                    Clip = clip
                };
                var seg = (deck, row, Math.Round(line, 3), Math.Round(along0, 3), alongX);
                if (segments.Contains(seg) || !ids.Add(bp.Id)) { below = null; belowShut = false; continue; }
                segments.Add(seg);
                All.Add(bp);
                any = true;
                below = bp;
                belowShut = false;
                // (a partial row: nothing stands over it but the next partial row's own free part)
                if (clip != null && row < PanelRows) { below = null; belowShut = true; }
            }
            return any;
        }

        int[,] firstSolidZ, lastSolidZ;

        /// <summary>
        /// (0.3.14, note 149) Is a point within her athwartships: between the first and the last of her cells across her at
        /// that x and y (not outside her side, where a wedge cell past her planking reaches)? Off her grid: no.
        /// </summary>
        public bool InsideHer(double x, double y, double z)
        {
            var g = Def.Grid;
            if (firstSolidZ == null)
            {
                firstSolidZ = new int[g.NX, g.NY];
                lastSolidZ = new int[g.NX, g.NY];
                for (int i = 0; i < g.NX; i++)
                    for (int j = 0; j < g.NY; j++)
                    {
                        int f = int.MaxValue, l = int.MinValue;
                        for (int k = 0; k < g.NZ; k++)
                            if (g.Solid(i, j, k)) { if (f == int.MaxValue) f = k; l = k; }
                        firstSolidZ[i, j] = f;
                        lastSolidZ[i, j] = l;
                    }
            }
            int ci = g.CellX(x), cj = g.CellY(y), ck = g.CellZ(z);
            if (ci < 0 || cj < 0 || ck < 0 || ci >= g.NX || cj >= g.NY || ck >= g.NZ) return false;
            if (ck > firstSolidZ[ci, cj] && ck < lastSolidZ[ci, cj]) return true;
            // ((0.3.28, note 230) over a low bulwark or by an open gun port her side isn't there at that height: a point well
            // inboard (a block from her side) of where her side is lower down is inside her. It clipped the open waist's top
            // row along the centreline (a half-height wall, no door) forward of the quarterdeck.)
            int inboard = (int)Math.Round(InboardOfSide / g.V);
            for (int j = cj - 1; j >= Math.Max(0, cj - (int)Math.Round(3 / g.V)); j--)
            {
                if (firstSolidZ[ci, j] == int.MaxValue) continue;
                if (ck > firstSolidZ[ci, j] && ck < lastSolidZ[ci, j]) return ck > firstSolidZ[ci, j] + inboard && ck < lastSolidZ[ci, j] - inboard;
            }
            return false;
        }

        /// <summary>
        /// (22 October 2026, note 48) Her cell at a point, a timber inboard of her side (a carling, a beam, half a block or more
        /// from the first and last of her cells across her there): not her side's planking, nor a deck's (its cells).
        /// </summary>
        bool HangingAt(double x, double y, double z)
        {
            var g = Def.Grid;
            if (!g.SolidAt(x, y, z)) return false;
            if (Def.Slots != null && Def.Slots.At(x, y - 0.01, z) >= 0 && Def.Slots.At(x, y + 0.01, z) >= 0) return false;
            InsideHer(x, y, z);     // (her cells across her, worked out once)
            int ci = g.CellX(x), cj = g.CellY(y), ck = g.CellZ(z), m = (int)Math.Round(0.5 / g.V);
            if (ci < 0 || cj < 0 || ck < 0 || ci >= g.NX || cj >= g.NY || ck >= g.NZ) return false;
            return ck >= firstSolidZ[ci, cj] + m && ck <= lastSolidZ[ci, cj] - m;
        }

        /// <summary>(0.3.28) How far inboard of her side lower down a point over a gap in her side counts as inside her.</summary>
        public const double InboardOfSide = 1.0;

        /// <summary>
        /// (0.3.13) The free sub-blocks of a border's layer (free[along, up], cells a side, v each) as rectangles in the
        /// line's plane: (along0, y0, along1, y1), each column's runs up it, side by side columns with the same runs joined.
        /// </summary>
        public static List<double[]> ClipRuns(bool[,] free, int cells, double along0, double y0, double v)
        {
            var cols = new List<(int b0, int b1)>[cells];
            for (int a = 0; a < cells; a++)
            {
                cols[a] = new List<(int, int)>();
                int start = -1;
                for (int b = 0; b <= cells; b++)
                {
                    bool f = b < cells && free[a, b];
                    if (f && start < 0) start = b;
                    if (!f && start >= 0) { cols[a].Add((start, b)); start = -1; }
                }
            }
            var rects = new List<double[]>();
            int a0 = 0;
            while (a0 < cells)
            {
                int a1 = a0 + 1;
                while (a1 < cells && cols[a1].SequenceEqual(cols[a0])) a1++;
                foreach (var (b0, b1) in cols[a0])
                    rects.Add(new[] { along0 + a0 * v, y0 + b0 * v, along0 + a1 * v, y0 + b1 * v });
                a0 = a1;
            }
            return rects;
        }

        /// <summary>(0.3.14, notes 148, 149) How finely a cut partition is drawn out to her surfaces: cells a block.</summary>
        public const int DrawCells = 32;
        /// <summary>
        /// (0.3.14) How far past its free cells a partition may be drawn to meet her surfaces, within her cells (a quarter block
        /// each: a beam's, more where her curved side's cells step down under the line, the planking slanting through them).
        /// </summary>
        public const double DrawReach = 1.0;

        /// <summary>
        /// (0.3.14, notes 148, 149: "almost works: partition placement between beams, leaving a small gap around beams"; "still
        /// cannot fill these gaps" at her curved side) What a partition on a border place is drawn as: its free cells (the
        /// <see cref="MountPoint.Clip"/> or the whole block, on her quarter-block collision) carried on into the cells of hers
        /// next to them, up to her real surfaces (a beam's underside and sides, her ceiling planking: <see cref="ShipSurfaces"/>),
        /// DrawCells to the block: each free run's end walked up or down, and along, by rays at both faces of the partition and
        /// both edges of the fine cell, to the farthest surface met within DrawReach (so neither face leaves a gap; what reaches
        /// past the near one is inside the timber), past the place's own block where her cells go on there (a whole place
        /// ending a voxel short of her ceiling). Where no surface is met her cell's face is hers (a deck's planks are drawn on
        /// her cells) and the run ends there. Rectangles as the Clip's (along0, y0, along1, y1), possibly reaching past the
        /// block; null: draw the place as before (nothing to carry on). Kept on the place.
        /// </summary>
        public List<double[]> DrawnClip(MountPoint m, ShipSurfaces s)
        {
            if (m == null || m.Kind != MountKind.Border || s == null || s.Count == 0 || Def.Grid == null) return null;
            if (m.DrawnDone) return m.Drawn;
            m.DrawnDone = true;
            var g = Def.Grid;
            var room = Def.Rooms.FirstOrDefault(r => r.Code == m.Room);
            double top = room?.Max[1] ?? double.MaxValue;
            var (line, alongX) = BorderLine(m);
            double inSign = m.In < 2 ? 1 : -1;
            double layer = line + inSign * g.V / 2;
            const int n = DrawCells;
            int pad = (int)Math.Ceiling(DrawReach * n), w = n + 2 * pad;
            double c = 1.0 / n, h = PartitionThickness / 2 * 0.9;
            // the fine grid: the place's block and pad cells round it
            double along0 = (alongX ? m.X : m.Z) - 0.5 - pad * c, y0 = m.Y - pad * c;
            bool Solid(double al, double y) => alongX ? g.SolidAt(al, y, layer) : g.SolidAt(layer, y, al);
            // ((22 October 2026, note 48) a partial place cut by a deck timber hanging into the other layer, over the room's top:
            // up to it, her cells there count as hers)
            double farLayer = line - inSign * g.V / 2;
            bool Over(double al, double y) => Solid(al, y) || m.Clip != null && g.OY + (g.CellY(y) + 1) * g.V > top + 1e-6 && (alongX ? HangingAt(al, y, farLayer) : HangingAt(farLayer, y, al));
            var free = new bool[w, w];
            for (int a = pad; a < pad + n; a++)
                for (int b = pad; b < pad + n; b++)
                {
                    double al = along0 + (a + 0.5) * c, y = y0 + (b + 0.5) * c;
                    if (m.Clip != null ? m.Clip.Any(r => al > r[0] && al < r[2] && y > r[1] && y < r[3]) : !Solid(al, y)) free[a, b] = true;
                }
            var fill = (bool[,])free.Clone();
            // (cells drawn wholly short of her nearest surface met: open air, the second pass's)
            var open = new bool[w, w];
            double near = 0;
            bool more = false;
            // a ray in the line's plane from (along, y) the way (da, dy): how far to her farthest surface, at either face, within
            // her cells from there (a surface past them is something else's: under a deck drawn on her cells, a beam below it)
            double Far(double al, double y, double da, double dy)
            {
                double run = 0;
                Func<double, double, bool> hers = dy > 0 ? Over : Solid;
                // ((note 48) up into a timber of the other layer only, beside free air in its own: to the timber's underside, the
                // nearest surface met, not on through it)
                bool otherOnly = dy > 0 && !Solid(al + da * 0.02, y + dy * 0.02);
                // (from 0.01 inside the free cell: her cells begin 0.01 on)
                while (run < DrawReach && hers(al + da * (run + 0.02), y + dy * (run + 0.02))) run += 0.01;
                if (run <= 0) return -1;
                double best = -1;
                foreach (double off in new[] { -h, h })
                {
                    double o = line + off;
                    double t = alongX ? s.Ray(al, y, o, da, dy, 0, run + 0.03) : s.Ray(o, y, al, 0, dy, da, run + 0.03);
                    if (t < double.MaxValue) { best = otherOnly && best >= 0 ? Math.Min(best, t) : Math.Max(best, t); near = Math.Min(near, t); }
                    else near = 0;
                }
                return best;
            }
            // twice: from the free cells, then a little from what that drew (the corner where a deck's edge meets her side,
            // a surface the first rays ran along)
            for (int pass = 0; pass < 2; pass++)
            {
                var src = pass == 0 ? free : (bool[,])fill.Clone();
                int most = pass == 0 ? w : 3;
                for (int a = 0; a < w; a++)
                    for (int b = 0; b < w; b++)
                    {
                        if (!src[a, b] || pass > 0 && (free[a, b] || !open[a, b])) continue;
                        double al0 = along0 + a * c, ya0 = y0 + b * c, mid = al0 + c / 2, ym = ya0 + c / 2;
                        // up, down: the run's end, her cell beyond it
                        for (int dir = -1; dir <= 1; dir += 2)
                        {
                            int nb = b + dir;
                            Func<double, double, bool> hers = dir > 0 ? Over : Solid;
                            if (nb < 0 || nb >= w || src[a, nb] || !hers(mid, ym + dir * c)) continue;
                            double y = dir > 0 ? ya0 + c - 0.01 : ya0 + 0.01;
                            near = double.MaxValue;
                            double t = Math.Max(Far(al0 + 0.002, y, 0, dir), Far(al0 + c - 0.002, y, 0, dir));
                            if (t <= 0.011) continue;
                            double end = y + dir * t, nearEnd = y + dir * near;
                            for (int k = nb, i = 0; i < most && k >= 0 && k < w && (dir > 0 ? y0 + k * c < end : y0 + (k + 1) * c > end) && hers(mid, y0 + (k + 0.5) * c); k += dir, i++)
                            {
                                fill[a, k] = more = true;
                                if (pass == 0 && (dir > 0 ? y0 + (k + 1) * c <= nearEnd : y0 + k * c >= nearEnd)) open[a, k] = true;
                            }
                        }
                        // along: likewise
                        for (int dir = -1; dir <= 1; dir += 2)
                        {
                            int na = a + dir;
                            if (na < 0 || na >= w || src[na, b] || !Solid(mid + dir * c, ym)) continue;
                            double al = dir > 0 ? al0 + c - 0.01 : al0 + 0.01;
                            near = double.MaxValue;
                            double t = Math.Max(Far(al, ya0 + 0.002, dir, 0), Far(al, ya0 + c - 0.002, dir, 0));
                            if (t <= 0.011) continue;
                            double end = al + dir * t, nearEnd = al + dir * near;
                            for (int k = na, i = 0; i < most && k >= 0 && k < w && (dir > 0 ? along0 + k * c < end : along0 + (k + 1) * c > end) && Solid(along0 + (k + 0.5) * c, ym); k += dir, i++)
                            {
                                fill[k, b] = more = true;
                                if (pass == 0 && (dir > 0 ? along0 + (k + 1) * c <= nearEnd : along0 + k * c >= nearEnd)) open[k, b] = true;
                            }
                        }
                    }
            }
            if (!more && m.Clip == null) return null;
            return m.Drawn = ClipRuns(fill, w, along0, y0, c);
        }

        Dictionary<(bool, long, long, long), MountPoint> borderAt;

        /// <summary>
        /// (0.3.14, note 147) The border place on a line (along x or not, at line) whose block's middle along it is at along and
        /// whose foot is at y, or null: a place's neighbours along its line and up and down it.
        /// </summary>
        public MountPoint BorderAt(bool alongX, double line, double along, double y)
        {
            if (borderAt == null)
            {
                borderAt = new Dictionary<(bool, long, long, long), MountPoint>();
                foreach (var b in Borders)
                {
                    // ((0.3.28) a twin shares its segment: the first one is the segment's place, TwinOf gives the other)
                    if (b.TwinSide) continue;
                    var (bl, bx) = BorderLine(b);
                    borderAt[(bx, (long)Math.Round(bl * 8), (long)Math.Round((bx ? b.X : b.Z) * 8), (long)Math.Round(b.Y * 8))] = b;
                }
            }
            return borderAt.TryGetValue((alongX, (long)Math.Round(line * 8), (long)Math.Round(along * 8), (long)Math.Round(y * 8)), out var m) ? m : null;
        }

        /// <summary>(0.3.14, note 147) The border place next to this one on its line: da blocks along it, dy up it (or null).</summary>
        public MountPoint BorderNext(MountPoint m, int da, int dy)
        {
            if (m?.Kind != MountKind.Border) return null;
            var (line, alongX) = BorderLine(m);
            return BorderAt(alongX, line, (alongX ? m.X : m.Z) + da, m.Y + dy);
        }

        /// <summary>(0.3.14, note 147) How near an edge of a partition (or a door) the crosshair picks the place past that edge.</summary>
        public const double EdgePick = 0.25;
        /// <summary>(0.3.14, note 147) How far from her timber the crosshair is on (a beam a partition butts against) a border place is picked.</summary>
        public const double TimberPick = 0.4;

        /// <summary>
        /// (0.3.14, note 147: "When pointing at the sides of other walls placement does not work") The crosshair on a partition
        /// or a door put on a border place (mp; rows high) at (along, y) on its line: near one of its edges (EdgePick, or on
        /// its thin end), the place past that edge on the line (along it, or over or under it), if ok takes it; else null.
        /// The nearest edge first, then the others within EdgePick.
        /// </summary>
        public MountPoint NextToPiece(MountPoint mp, int rows, double along, double y, Func<MountPoint, bool> ok)
        {
            if (mp?.Kind != MountKind.Border) return null;
            var (_, alongX) = BorderLine(mp);
            double u = along - ((alongX ? mp.X : mp.Z) - 0.5), v = y - mp.Y;
            var edges = new List<(double D, int Da, int Dy)> { (u, -1, 0), (1 - u, 1, 0), (v, 0, -1), (rows - v, 0, rows) };
            foreach (var (dist, da, dy) in edges.OrderBy(e => e.D))
            {
                if (dist > EdgePick || dist < -0.1) continue;
                var n = BorderNext(mp, da, dy);
                if (n != null && (ok == null || ok(n))) return n;
                // ((0.3.28) or its twin across the line, round a well)
                if (TwinOf(n) is MountPoint tn && (ok == null || ok(tn))) return tn;
            }
            return null;
        }

        /// <summary>
        /// (0.3.14, note 147: "facing a beam it does not work and does not highlight") The crosshair stops on her timber at p
        /// (ship-local): the border place whose box is nearest it, within TimberPick, that ok takes (the place under a beam,
        /// beside a frame or a post a partition there would butt against); else null.
        /// </summary>
        public MountPoint NearTimber(double px, double py, double pz, Func<MountPoint, bool> ok)
        {
            MountPoint best = null;
            double bestD = TimberPick;
            foreach (var m in All)
            {
                if (m.Kind != MountKind.Border || m.Box == null) continue;
                var bx = m.Box;
                double dx = Math.Max(0, Math.Max(bx[0] - px, px - bx[3])), dy = Math.Max(0, Math.Max(bx[1] - py, py - bx[4])), dz = Math.Max(0, Math.Max(bx[2] - pz, pz - bx[5]));
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d >= bestD || ok != null && !ok(m)) continue;
                bestD = d;
                best = m;
            }
            return best;
        }

        /// <summary>The border place a block up from this one (a door's upper half), or null.</summary>
        public MountPoint BorderAbove(MountPoint m) =>
            m?.Kind == MountKind.Border && m.Row == 0 ? Get(m.Id.Substring(0, m.Id.LastIndexOf(':')) + ":1") : null;

        /// <summary>The line a border place stands on, and whether it runs along x (her side's way) or across.</summary>
        public static (double Line, bool AlongX) BorderLine(MountPoint m) =>
            m.In == 1 || m.In == 3 ? (m.Z, true) : (m.X, false);

        /// <summary>The inside face of her side (+1 starboard) at (x, y): from outside in, past the hull, the first free cell's outer face.</summary>
        double? SideSurface(double x, double y, int s)
        {
            var g = Def.Grid;
            int i = g.CellX(x), j = g.CellY(y);
            if (i < 0 || i >= g.NX || j < 0 || j >= g.NY) return null;
            bool seen = false;
            for (int n = 0; n < g.NZ; n++)
            {
                int k = s > 0 ? g.NZ - 1 - n : n;
                if (g.Solid(i, j, k)) { seen = true; continue; }
                if (seen) return s > 0 ? g.OZ + (k + 1) * g.V : g.OZ + k * g.V;
            }
            return null;
        }

        /// <summary>A room's end wall (+1 forward): from just beyond the room's end inward, past the air and the wall, the first free cell's face.</summary>
        double? EndSurface(double xEnd, double y, double z, int e)
        {
            var g = Def.Grid;
            int j = g.CellY(y), k = g.CellZ(z);
            if (j < 0 || j >= g.NY || k < 0 || k >= g.NZ) return null;
            int i = g.CellX(xEnd + e * 0.5);
            bool seen = false;
            int steps = (int)(3.5 / g.V);
            for (int n = 0; n < steps; n++, i -= e)
            {
                if (i < 0 || i >= g.NX) continue;
                if (g.Solid(i, j, k)) seen = true;
                else if (seen) return e > 0 ? g.OX + (i + 1) * g.V : g.OX + i * g.V;
            }
            return null;
        }

        // ------------------------------------------------------------------ (0.3.16) open decks, shelves, walls on partitions, ceiling hooks

        readonly Dictionary<string, RoomDef> openDecks = new();

        /// <summary>(0.3.16, notes 155, 157) An open deck's border places: two rows over it, as if it were a room that high.</summary>
        RoomDef OpenDeckRoom(MountPoint m)
        {
            if (openDecks.TryGetValue(m.Deck, out var r)) return r;
            return openDecks[m.Deck] = new RoomDef
            {
                Code = m.Deck + "-deck", Enclosed = false, Min = new[] { -1e4, m.Y, -1e4 }, Max = new[] { 1e4, m.Y + OpenDeckRows, 1e4 }
            };
        }

        /// <summary>How many rows of border places an open deck has (a railing, a partition over it).</summary>
        public const int OpenDeckRows = 2;
        /// <summary>(0.3.16, note 159) A horizontal plank panel's thickness (under its plane), as a partition's.</summary>
        public const double ShelfThickness = 0.125;

        /// <summary>The horizontal place over a floor cell at h blocks up (1, 2; 3 a well's top), or null.</summary>
        public MountPoint HorizontalAt(string deck, int i, int k, int h) => Get($"hb:{deck}:{i}:{k}:{h}");

        /// <summary>
        /// (0.3.16, note 159: "ability seal horizontal surfaces to make shelf or sealing top of stairs") Over each floor cell a
        /// plank panel's place a block and two up in a closed room (a shelf: the block under its plane free of her), and three
        /// up where the cell is open to the deck above (a well: a deck at that height round it), its plane flush with that
        /// deck. Ids "hb:deck:i:k:h".
        /// </summary>
        void HorizontalPlaces()
        {
            if (!OnShipGrid) return;
            var g = Def.Grid;
            const double e = 0.02;
            foreach (var m in Floors.ToList())
            {
                var room = Def.RoomAt(m.X + 0.5, m.Y + 0.5, m.Z + 0.5);
                bool closed = room?.Enclosed == true;
                for (int h = 1; h <= 3; h++)
                {
                    double y = m.Y + h;
                    if (g.Collides(m.X + e, y - 1 + e, m.Z + e, m.X + 1 - e, y - e, m.Z + 1 - e)) break;
                    if (h == 3) { if (!WellTop(m, y)) break; }
                    else if (!closed) continue;
                    var box = new[] { m.X, y - ShelfThickness, m.Z, m.X + 1, y, m.Z + 1 };
                    if (h < 3 && ClearHit(box[0], box[1], box[2], box[3], box[4], box[5]) != null) continue;
                    All.Add(new MountPoint
                    {
                        Id = $"hb:{m.Deck}:{m.I}:{m.K}:{h}", Kind = MountKind.Horizontal, Deck = m.Deck, Room = room?.Code ?? m.Room,
                        X = m.X + 0.5, Y = y, Z = m.Z + 0.5, I = m.I, K = m.K, Level = h, Box = box
                    });
                }
            }
        }

        /// <summary>Is a plane over a floor cell a well's top: one of the design's wells there, or a deck at that height beside it (her solid just under the plane, free over it)?</summary>
        bool WellTop(MountPoint m, double y)
        {
            double cx = m.X + 0.5, cz = m.Z + 0.5;
            foreach (var w in Def.Wells ?? Array.Empty<WellDef>())
                if (w?.Box?.Length >= 6 && cx > w.Box[0] && cx < w.Box[3] && cz > w.Box[2] && cz < w.Box[5] && Math.Abs(w.Box[4] - y) < 0.3) return true;
            if (!Def.Decks.Any(d => d.Code != "top" && Math.Abs(d.Y - y) < 0.05 && cx >= d.X0 && cx <= d.X1)) return false;
            var g = Def.Grid;
            for (int dir = 0; dir < 4; dir++)
            {
                double nx = cx + DX[dir], nz = cz + DZ[dir];
                if (g.SolidAt(nx, y - g.V / 2, nz) && !g.SolidAt(nx, y + g.V / 2, nz)) return true;
            }
            return false;
        }

        /// <summary>(0.3.18) A flue's chute: how wide (square), how thick the deck it goes through may be at most, how far over that deck it comes out.</summary>
        public const double FlueWidth = 0.5, FlueDeckThickness = 0.75, FlueOut = 1.0;

        /// <summary>
        /// (0.3.18, note 165: "a chute for the smoke") Over each floor cell (on the ship's grid), a flue's place: a brick chute
        /// FlueWidth square at the cell's middle from FlueOut over its deck up through the deck above to FlueOut over that,
        /// where the column is free of her up to the deck above's underside, that deck is at most FlueDeckThickness thick
        /// there (between the beams), and the block over it at the column is free. Y: its deck; Over: the deck it comes out
        /// on; Under: the floor place under it (the hearth's, or where the segment under it came out). Ids "fl:deck:i:k".
        /// </summary>
        void FluePlaces()
        {
            if (!OnShipGrid) return;
            var g = Def.Grid;
            const double e = 0.02, h = FlueWidth / 2;
            var byXZ = new Dictionary<(string, long, long), MountPoint>();
            foreach (var f in Floors) byXZ[(f.Deck, (long)Math.Round(f.X * 8), (long)Math.Round(f.Z * 8))] = f;
            foreach (var m in Floors.ToList())
            {
                double cx = m.X + 0.5, cz = m.Z + 0.5;
                DeckDef over = null;
                foreach (var d in Def.Decks)
                    if (d.Code != "top" && d.Y > m.Y + 1.5 && d.Y < m.Y + 4.6 && cx >= d.X0 && cx <= d.X1 && (over == null || d.Y < over.Y)) over = d;
                if (over == null) continue;
                bool ok = true;
                foreach (var (sx, sz) in new[] { (0.0, 0.0), (-h + e, -h + e), (h - e, -h + e), (-h + e, h - e), (h - e, h - e) })
                {
                    double? c = Ceiling(cx + sx, m.Y, cz + sz);
                    if (c == null || c.Value < over.Y - FlueDeckThickness - 1e-6 || c.Value > over.Y + 0.01) { ok = false; break; }
                }
                if (!ok || g.Collides(cx - h + e, over.Y + e, cz - h + e, cx + h - e, over.Y + FlueOut - e, cz + h - e)) continue;
                byXZ.TryGetValue((over.Code, (long)Math.Round(m.X * 8), (long)Math.Round(m.Z * 8)), out var up);
                All.Add(new MountPoint
                {
                    Id = $"fl:{m.Deck}:{m.I}:{m.K}", Kind = MountKind.Flue, Deck = m.Deck, Room = m.Room, X = cx, Y = m.Y, Z = cz, I = m.I, K = m.K,
                    Under = m.Id, Over = over.Code, OverY = over.Y, OutPlace = up?.Id,
                    Box = new[] { cx - h, m.Y + FlueOut, cz - h, cx + h, over.Y + FlueOut, cz + h }
                });
            }
        }

        /// <summary>(0.3.18) The flue's place over a floor place, or null.</summary>
        public MountPoint FlueOver(MountPoint floorPlace) => floorPlace == null ? null : Get($"fl:{floorPlace.Deck}:{floorPlace.I}:{floorPlace.K}");

        /// <summary>
        /// (0.3.16, note 159: "Partition walls should accept paintings, wallpapers, lanterns, shelves, etc") A wall spot on
        /// each face of every whole border place, usable while a partition stands there (Under: the border place); the same
        /// line's spots side by side (a 2 × 1 painting across two partitions). Ids "w:b:deck:i:k:side:row:+|-".
        /// </summary>
        void PartitionSpots()
        {
            if (!OnShipGrid) return;
            var g = Def.Grid;
            const double e = 0.02, h = PartitionThickness / 2;
            foreach (var b in Borders.ToList())
            {
                if (b.Clip != null) continue;
                var (line, alongX) = BorderLine(b);
                double along = alongX ? b.X : b.Z;
                int index = (int)Math.Floor(along - 0.5 + 1e-6);
                for (int s = -1; s <= 1; s += 2)
                {
                    int inn = alongX ? (s > 0 ? 1 : 3) : (s > 0 ? 0 : 2);
                    double face = line + s * h, f0 = Math.Min(face, face + s * WallDepth), f1 = Math.Max(face, face + s * WallDepth);
                    double[] box = alongX ? new[] { b.X - 0.5, b.Y, f0, b.X + 0.5, b.Y + 1, f1 } : new[] { f0, b.Y, b.Z - 0.5, f1, b.Y + 1, b.Z + 0.5 };
                    if (g.Collides(box[0] + e, box[1] + e, box[2] + e, box[3] - e, box[4] - e, box[5] - e)) continue;
                    string key = $"pw:{b.Deck}:{(alongX ? "x" : "z")}{F(line)}:{(s > 0 ? "+" : "-")}";
                    if (wall.ContainsKey((key, index, b.Row))) continue;
                    var spot = new MountPoint
                    {
                        Id = $"w:b:{b.Id.Substring(2)}:{(s > 0 ? "+" : "-")}", Kind = MountKind.Wall, Deck = b.Deck, Room = b.Room, Wall = key,
                        I = index, Row = b.Row, In = inn, Normal = new double[] { DX[inn], 0, DZ[inn] },
                        X = alongX ? b.X : face, Y = b.Y, Z = alongX ? face : b.Z, Box = box, Under = b.Id
                    };
                    All.Add(spot);
                    wall[(key, index, b.Row)] = spot;
                }
            }
        }

        /// <summary>(0.3.16, note 161) How high over the floor a ceiling must be for a lantern hook.</summary>
        public const double MinHookHeight = 2.25;

        /// <summary>
        /// (0.3.16, note 161: "lanterns should be placeable on any suitable ceiling space between floor beams") A hook under
        /// the deck over each floor cell of a closed room where its underside is flat over the cell (no beam through it) and
        /// at least MinHookHeight over the floor, not by a design's hook. Ids "h:c:deck:i:k"; a lantern on one gets the
        /// iron eye the design's ceiling hooks have.
        /// </summary>
        void CeilingHooks()
        {
            if (!OnShipGrid) return;
            var existing = All.Where(x => x.Kind == MountKind.Hook).ToList();
            foreach (var m in Floors.ToList())
            {
                // (0.3.24, note 193: "rethink lantern placement to be more custom ... as in the hold") under a flat ceiling
                // on an open deck too (a castle deck's overhang, a gallows' beams), not only in her rooms
                var room = Def.RoomAt(m.X + 0.5, m.Y + 0.5, m.Z + 0.5);
                double cx = m.X + 0.5, cz = m.Z + 0.5;
                double? c = Ceiling(cx, m.Y, cz);
                if (c == null || c.Value - m.Y < MinHookHeight) continue;
                bool flat = true;
                for (int a = -1; a <= 1 && flat; a++)
                    for (int bb = -1; bb <= 1 && flat; bb++)
                    {
                        // (the beams stand every other block: a cell's middle is a quarter block from one's side)
                        double? c2 = Ceiling(cx + a * 0.2, m.Y, cz + bb * 0.2);
                        if (c2 == null || Math.Abs(c2.Value - c.Value) > 0.05) flat = false;
                    }
                if (!flat) continue;
                double y = c.Value;
                if (existing.Any(x => Math.Abs(x.X - cx) < 0.9 && Math.Abs(x.Z - cz) < 0.9 && Math.Abs(x.Y - y) < 1.0)) continue;
                All.Add(new MountPoint
                {
                    Id = $"h:c:{m.Deck}:{m.I}:{m.K}", Kind = MountKind.Hook, Deck = m.Deck, Room = room?.Code ?? "deck", X = cx, Y = y, Z = cz, I = m.I, K = m.K,
                    Hang = "down", Kinds = new[] { "light" }, Box = new[] { cx - 0.3, y - 0.75, cz - 0.3, cx + 0.3, y, cz + 0.3 }
                });
            }
        }

        /// <summary>(0.3.24, note 193) How high over a deck a lantern hook on a mast's side is, and how far out from the mast's face.</summary>
        public const double MastHookHeight = 2.6, MastHookOut = 0.3;

        /// <summary>
        /// (0.3.24, note 193: "can't place the lantern back ... rethink lantern placement to be more custom") Lantern hooks on
        /// each mast's four sides (fore, aft, port, starboard) over every deck it stands through: <see cref="MastHookHeight"/>
        /// over the deck, <see cref="MastHookOut"/> out from its face, where her deck is under it, the lantern's room is free
        /// of her and no deck is over it within that height. Not where a design hook (or a ceiling hook) is already near.
        /// Ids "h:m:&lt;mast&gt;:&lt;deck&gt;:&lt;side&gt;".
        /// </summary>
        void MastHooks()
        {
            if (!OnShipGrid) return;
            var g = Def.Grid;
            var existing = All.Where(x => x.Kind == MountKind.Hook).ToList();
            foreach (var mast in Def.Masts)
            {
                // ((0.3.27, note 202: "not on the same level ... it should show attachment") at the design's own hook's height
                // over its deck and its reach from the mast, when the mast has one, so the four sides match it)
                var design = DesignMastHook(mast, existing);
                double height = MastHookHeight, reach = Math.Max(0.05, mast.Radius) + MastHookOut;
                if (design != null)
                {
                    var under = Def.Decks.Where(dk => dk.Code != "top" && dk.Y < design.Y - 1.0 && mast.X >= dk.X0 && mast.X <= dk.X1).OrderByDescending(dk => dk.Y).FirstOrDefault();
                    if (under != null) height = design.Y - under.Y;
                    // (its reach, or as far out as keeps the lantern clear of the mast where that's further)
                    reach = Math.Max(Math.Sqrt((design.X - mast.X) * (design.X - mast.X) + (design.Z - mast.Z) * (design.Z - mast.Z)), Math.Max(0.05, mast.Radius) + MastHookOut);
                }
                foreach (var deck in Def.Decks)
                {
                    if (deck.Code == "top" || deck.Y <= mast.Y0 + 0.5 || deck.Y + height >= mast.Y1) continue;
                    if (mast.X < deck.X0 || mast.X > deck.X1) continue;
                    for (int dir = 0; dir < 4; dir++)
                    {
                        double r = reach;
                        double x = mast.X + DX[dir] * r, z = mast.Z + DZ[dir] * r, y = deck.Y + height;
                        if (!g.SolidAt(x, deck.Y - g.V / 2, z)) continue;
                        if (g.Collides(x - 0.2, deck.Y + 0.02, z - 0.2, x + 0.2, y + 0.1, z + 0.2)) continue;
                        if (existing.Any(h => Math.Abs(h.X - x) < 0.6 && Math.Abs(h.Z - z) < 0.6 && Math.Abs(h.Y - y) < 1.0)) continue;
                        var room = Def.RoomAt(x, y - 0.5, z);
                        All.Add(new MountPoint
                        {
                            Id = $"h:m:{mast.Code}:{deck.Code}:{DirCode[dir]}", Kind = MountKind.Hook, Deck = deck.Code, Room = room?.Code ?? "deck",
                            X = x, Y = y, Z = z, Hang = "down", Kinds = new[] { "light" },
                            Box = new[] { x - 0.3, y - 0.75, z - 0.3, x + 0.3, y, z + 0.3 }
                        });
                    }
                }
            }
        }

        /// <summary>(0.3.27, note 202) The design's own lantern hook on a mast (one within 1.5 of its axis, not the mod's), or null.</summary>
        public static MountPoint DesignMastHook(MastDef mast, IEnumerable<MountPoint> hooks) =>
            mast == null ? null : hooks.FirstOrDefault(h => h.Kind == MountKind.Hook && !IsCeilingHook(h.Id) && !IsMastHook(h.Id) && h.Hang != "up" &&
                Math.Abs(h.X - mast.X) < 1.5 && Math.Abs(h.Z - mast.Z) < 1.5 && h.Y > mast.Y0 && h.Y < mast.Y1);

        /// <summary>(0.3.27, note 202) A mast hook's mast (its id names it), or null.</summary>
        public MastDef MastOfHook(string mountId)
        {
            if (!IsMastHook(mountId)) return null;
            var parts = mountId.Split(':');
            return parts.Length > 2 ? Def?.Masts?.FirstOrDefault(m => m.Code == parts[2]) : null;
        }

        /// <summary>(0.3.27, note 202) Is there a design hook's band round this mast at this height (the mod's arm then needs none of its own)?</summary>
        public bool MastBandAt(MastDef mast, double y) => DesignMastHook(mast, All) is MountPoint d && Math.Abs(d.Y - y) < 0.2;

        /// <summary>The underside of what's over a floor at (x, z): the first of her cells up from the floor (within 4.5), or null.</summary>
        double? Ceiling(double x, double floorY, double z)
        {
            var g = Def.Grid;
            for (double y = floorY + g.V / 2; y < floorY + 4.5; y += g.V)
                if (g.SolidAt(x, y, z)) return g.OY + g.CellY(y) * g.V;
            return null;
        }

        /// <summary>(0.3.16) The direction (0 +x, 1 +z, 2 -x, 3 -z) a modular stair's "up" names ("+x", "-z"...); +x if none.</summary>
        public static int DirOf(string up) => up switch { "+z" or "z" => 1, "-x" => 2, "-z" => 3, _ => 0 };

        /// <summary>
        /// (0.3.16, docs/modular-pieces.md) The place a design's modular piece goes on: a border place on its line (along x or
        /// z) whose segment's foot is within 0.2 of At (partition, window, door, railing), the floor place whose cell's middle
        /// is (stair), the horizontal place whose plane's middle is (panel); null if there's none.
        /// </summary>
        public MountPoint ForModular(ModularPieceDef e)
        {
            if (e?.At == null || e.At.Length < 3) return null;
            double x = e.At[0], y = e.At[1], z = e.At[2];
            const double tol = 0.2;
            switch (e.Kind)
            {
                case "stair" or "ladder" or "hearth" or "oven":
                    return Floors.FirstOrDefault(m => Math.Abs(m.X + 0.5 - x) < tol && Math.Abs(m.Z + 0.5 - z) < tol && Math.Abs(m.Y - y) < tol);
                case "flue":
                    return Flues.FirstOrDefault(m => Math.Abs(m.X - x) < tol && Math.Abs(m.Z - z) < tol && Math.Abs(m.Y - y) < tol);
                case "panel":
                    return Horizontals.FirstOrDefault(m => IsHood(m.Id) == e.Hood && Math.Abs(m.X - x) < tol && Math.Abs(m.Z - z) < tol && Math.Abs(m.Y - y) < tol);
                case "partition" or "window" or "door" or "railing":
                {
                    bool alongX = e.Along == "x";
                    double line = alongX ? z : x, along = alongX ? x : z;
                    int anchor = e.Anchor == null ? -1 : DirOf(e.Anchor);
                    foreach (var m in Borders)
                    {
                        // ((0.3.29) an anchor named: the place on the line anchored that way (the twin across it, round a well))
                        if (anchor >= 0 ? m.In != anchor : m.TwinSide) continue;
                        var (bl, bx) = BorderLine(m);
                        if (bx != alongX || Math.Abs(bl - line) > tol || Math.Abs((alongX ? m.X : m.Z) - along) > tol || Math.Abs(m.Y - y) > tol) continue;
                        return m;
                    }
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// (0.3.19) The kind a design's entry is laid as. "stair" was the companion ladder-stair till 0.3.18 (1 × 2) and is a
        /// walked stair (1 × 3) now; an older entry is still laid as a ladder ("ladder"): one whose third cell up its run
        /// (where a walked stair's head would be) is under the deck above and not in a well (a 2-cell well over its foot and
        /// the next cell, or a castle's front over its second cell, as carrack-m had them). Every other kind as it says.
        /// </summary>
        public string LaidKind(ModularPieceDef e)
        {
            // ((0.3.23) a stair entry with its length given is a ship's stair of that length; (0.1.0-dev.6) 1 too)
            if (e?.Kind != "stair" || e.Length is PiecePlanner.ShortCells or PiecePlanner.SteepCells or PiecePlanner.FlightCells || Def?.Decks == null) return e?.Kind;
            var foot = ForModular(e);
            if (foot == null) return e.Kind;
            int up = DirOf(e.Up);
            double px = foot.X + 0.5 + DX[up] * 2, pz = foot.Z + 0.5 + DZ[up] * 2;
            foreach (var d in Def.Decks)
            {
                double r = d.Y - foot.Y;
                if (d.Code == "top" || r < 1 - 1e-6 || r > 4.5 || px < d.X0 || px > d.X1) continue;
                if (e.Rise > 0 && Math.Abs(r - e.Rise) > 0.3) continue;
                bool inWell = (Def.Wells ?? Array.Empty<WellDef>()).Any(w => w.Box != null && px > w.Box[0] && px < w.Box[3] && pz > w.Box[2] && pz < w.Box[5] &&
                                                                          d.Y > w.Box[1] - 0.3 && d.Y < w.Box[4] + 0.3);
                if (!inWell) return "ladder";
            }
            return e.Kind;
        }

        /// <summary>(0.3.16, note 161) A hook the mod found under a deck (not the design's): a lantern there is drawn with its eye.</summary>
        public static bool IsCeilingHook(string mountId) => mountId != null && mountId.StartsWith("h:c:");
        /// <summary>(0.3.24, note 193) A lantern hook the mod found on a mast's side (<see cref="MastHooks"/>).</summary>
        public static bool IsMastHook(string mountId) => mountId != null && mountId.StartsWith("h:m:");

        // ------------------------------------------------------------------ hooks

        void HookPoints()
        {
            var hooks = new List<HookDef>(Def.Hooks ?? Array.Empty<HookDef>());
            // an older design's lantern places are hooks
            foreach (var f in Def.Fixtures)
            {
                if (!ShipMounts.IsLegacyHook(f) || f.Pos == null) continue;
                if (hooks.Any(h => h.Code == f.Code || h.Pos != null && Math.Abs(h.Pos[0] - f.Pos[0]) + Math.Abs(h.Pos[1] - f.Pos[1]) + Math.Abs(h.Pos[2] - f.Pos[2]) < 0.1)) continue;
                hooks.Add(new HookDef { Code = f.Code, Pos = f.Pos, Hang = f.Hang ?? "down", Shown = f.Shown });
            }
            foreach (var h in hooks)
            {
                if (h.Pos == null || h.Code == null) continue;
                bool down = h.Hang != "up";
                double x = h.Pos[0], y = h.Pos[1], z = h.Pos[2];
                All.Add(new MountPoint
                {
                    Id = "h:" + h.Code, Kind = MountKind.Hook, Deck = null, Room = Def.RoomAt(x, y + (down ? -0.5 : 0.5), z)?.Code ?? "deck",
                    X = x, Y = y, Z = z, Hang = down ? "down" : "up", Kinds = h.Kinds ?? new[] { "light" }, Shown = h.Shown, Normal = h.Normal,
                    Box = down ? new[] { x - 0.3, y - 0.75, z - 0.3, x + 0.3, y, z + 0.3 } : new[] { x - 0.3, y, z - 0.3, x + 0.3, y + 0.75, z + 0.3 }
                });
                // ((0.1.0-dev.9, caravel note 78) a figurehead's place: the block the figure stands in, in front of her stem (and
                // wider than it, so it's seen past it from her deck))
                if (h.Kinds != null && Array.IndexOf(h.Kinds, PiecePlanner.FigureheadKind) >= 0)
                {
                    double nx = h.Normal?.Length >= 3 ? h.Normal[0] : 1, nz = h.Normal?.Length >= 3 ? h.Normal[2] : 0;
                    double fx = Math.Abs(nx) >= Math.Abs(nz) ? (nx >= 0 ? 1 : -1) : 0, fz = fx == 0 ? (nz >= 0 ? 1 : -1) : 0;
                    double cx = x + fx * 0.5, cz = z + fz * 0.5;
                    All[^1].Box = new[] { cx - 0.5, y, cz - 0.5, cx + 0.5, y + 1, cz + 0.5 };
                }
            }
        }

        // ------------------------------------------------------------------ the crosshair on a mount point

        /// <summary>
        /// The mount point a ray (ship-local, d normalised) looks at: a floor place's top, a wall spot's face, a hook's box,
        /// the nearest, if nothing of her collision is in front of it (wall: how far the ray goes before it meets her; a floor
        /// or a wall is that collision itself, so it may be up to a quarter block behind it). filter: which to consider.
        /// </summary>
        public MountPoint Pick(Vintagestory.API.MathTools.Vec3d o, Vintagestory.API.MathTools.Vec3d d, double range, double wall, Func<MountPoint, bool> filter, out double dist)
        {
            dist = double.MaxValue;
            MountPoint best = null;
            foreach (var m in All)
            {
                if (filter != null && !filter(m)) continue;
                double t;
                switch (m.Kind)
                {
                    case MountKind.Floor:
                    {
                        if (Math.Abs(d.Y) < 1e-9 || d.Y > 0) continue;
                        t = (m.Y - o.Y) / d.Y;
                        if (t < 0) continue;
                        double x = o.X + d.X * t, z = o.Z + d.Z * t;
                        if (x < m.X || x > m.X + 1 || z < m.Z || z > m.Z + 1) continue;
                        if (t > wall + 0.3) continue;
                        break;
                    }
                    case MountKind.Wall:
                    {
                        // the face the piece hangs on (level, square to the wall's way in)
                        int a = m.In;
                        double n = a == 0 || a == 2 ? d.X : d.Z;
                        double sgn = a < 2 ? 1 : -1;               // +1: the way in is +x / +z
                        if (n * sgn >= -1e-9) continue;            // looking at it from inside the room
                        double plane = a == 0 || a == 2 ? m.X : m.Z;
                        t = (plane - (a == 0 || a == 2 ? o.X : o.Z)) / n;
                        if (t < 0) continue;
                        double y = o.Y + d.Y * t, along = a == 0 || a == 2 ? o.Z + d.Z * t : o.X + d.X * t;
                        double c = a == 0 || a == 2 ? m.Z : m.X;
                        if (y < m.Y || y > m.Y + 1 || along < c - 0.5 || along > c + 0.5) continue;
                        if (t > wall + 0.6) continue;
                        break;
                    }
                    default:
                    {
                        t = ShipFittings.RayBox(o, d, new Vintagestory.API.MathTools.Cuboidd(m.Box[0], m.Box[1], m.Box[2], m.Box[3], m.Box[4], m.Box[5]));
                        if (t < 0 || t > wall + 0.05) continue;
                        break;
                    }
                }
                if (t > range || t >= dist) continue;
                dist = t;
                best = m;
            }
            return best;
        }

        /// <summary>How many places of each kind in each room or deck (the reports and the command).</summary>
        public SortedDictionary<string, int> Counts()
        {
            var c = new SortedDictionary<string, int>();
            foreach (var m in All)
            {
                string k = (m.Kind switch
                {
                    MountKind.Floor => m.Level > 0 ? "stacked " : "floor ",
                    MountKind.Wall => m.Under != null ? "wall on panel " : "wall ",
                    MountKind.Panel => "panel ",
                    MountKind.Border => "border ",
                    MountKind.Horizontal => "shelf ",
                    MountKind.Flue => "flue ",
                    _ => m.Id.StartsWith("h:c:") ? "ceiling hook " : "hook "
                }) + (m.Room ?? "-");
                c[k] = c.TryGetValue(k, out int n) ? n + 1 : 1;
            }
            return c;
        }
    }

    /// <summary>
    /// Rooms closed by partitions and doors (notes 78, 98): within a design room that's open now (its hatch or the door
    /// that unseals it stands open), the space a block-high partition line, shut doors, her sides and bulkheads close off
    /// is a closed room of its own. Worked on her collision (0.25 voxels: shut doors and partitions are solid in it) from
    /// the room's floor up two blocks (the deck structure's band above is taken as closed: the deck over it), from the
    /// place asked about outward: open if it gets a block beyond the room's bounds (a doorway out of it) or under an open
    /// fitting that unseals the room (the hatch's opening). A partition line that ends at her side's wedge (where no panel
    /// fills it) is carried on through the wedge to her side, as built.
    /// (0.3.12, note 123: it must seal against her curved sides and under her beams without panels.) Also carried on
    /// through the next floor cell along its line when her side or a timber cuts into that cell's border (no partition can
    /// go there), and up to her timbers over it where no partition can go over it (the top row, a beam, her side leaning in
    /// over the outermost cell), each voxel column to the first solid within <see cref="CeilingGap"/>.
    /// </summary>
    public static class ShipCompartments
    {
        /// <summary>Told where a room was found open (the offline tests' diagnosis), or null.</summary>
        public static Action<string> Why;
        /// <summary>Given the collision with the partitions carried on, before the room is walked (the offline tests), or null.</summary>
        public static Action<VoxelGrid> Filled;

        /// <param name="portOpen">(0.3.22, step C) which lidded gun ports stand open now (the gun run out: EntityShip.PortOpen);
        /// null: every lid shut (a new ship, an empty gun place). A shut lid closes its port (cut in her collision or not),
        /// an open one in this room is an opening (<see cref="LiddedPorts"/>).</param>
        public static bool Sealed(MountLayout layout, VoxelGrid grid, IEnumerable<ShipPiece> pieces, RoomDef r, IEnumerable<double[]> leaks,
            double x, double y, double z, Func<FixtureDef, bool> portOpen = null)
        {
            if (layout?.Def == null || grid == null || r == null) return false;
            var deck = layout.FloorOf(r);
            if (deck == null) return false;
            double fy = deck.Y;
            var g = grid.Clone();
            // (0.3.22, step C) her lidded gun ports: shut, the lid is her side over the port; open, the room is open there
            var portLeaks = new List<double[]>();
            foreach (var f in LiddedPorts(layout.Def))
            {
                if (portOpen != null && portOpen(f))
                {
                    if (PortRoom(layout.Def, f) == r) portLeaks.Add(PortBox(f));
                }
                else g.Fill(PortFill(f, g.V));
            }
            if (portLeaks.Count > 0) leaks = (leaks ?? Enumerable.Empty<double[]>()).Concat(portLeaks);
            // (0.3.16) railings and stairs are in her collision for walking, but don't close anything
            // (0.3.17: only their own voxels: not her deck a raked railing passes, nor a partition's layer beside it)
            var design = layout.Def.Grid;
            foreach (var p in pieces ?? Enumerable.Empty<ShipPiece>())
                if (p.Role is PieceRole.Railing or PieceRole.Ladder or PieceRole.Stair)
                    foreach (var s in p.Solids)
                    {
                        var cells = g.CellsOf(s);
                        if (cells == null) continue;
                        for (double cx = cells[0] + g.V / 2; cx < cells[3]; cx += g.V)
                            for (double cy = cells[1] + g.V / 2; cy < cells[4]; cy += g.V)
                                for (double cz = cells[2] + g.V / 2; cz < cells[5]; cz += g.V)
                                    if (design == null || !design.SolidAt(cx, cy, cz))
                                        g.Fill(new[] { cx - g.V / 4, cy - g.V / 4, cz - g.V / 4, cx + g.V / 4, cy + g.V / 4, cz + g.V / 4 }, false);
                    }
            foreach (var p in pieces ?? Enumerable.Empty<ShipPiece>())
                if (p.Role is PieceRole.Partition or PieceRole.Window or PieceRole.Shelf or PieceRole.Panel || p.Role == PieceRole.Door && !p.Open)
                    foreach (var s in p.Solids) g.Fill(s);
            // partition lines through the wedges at their ends
            foreach (var p in pieces ?? Enumerable.Empty<ShipPiece>())
            {
                if (!(p.Role is PieceRole.Partition or PieceRole.Window) && !(p.Role == PieceRole.Door && !p.Open)) continue;
                var m = layout.Get(p.Mount);
                if (m == null || m.Deck != deck.Code || p.Solids.Count == 0) continue;
                var s = p.Solids[0];
                var (line, alongX) = MountLayout.BorderLine(m);
                for (int end = -1; end <= 1; end += 2)
                {
                    // the cell along the line beyond this end: a floor place there means the line goes on as pieces (or not)
                    // (0.3.12, note 123: unless her side or a timber cuts into that cell's border, so no partition can go
                    // there: then the line is carried on through it to her as through a wedge)
                    int di = alongX ? end : 0, dk = alongX ? 0 : end;
                    var beyond = layout.FloorAt(m.Deck, m.I + di, m.K + dk);
                    if (beyond != null && !CutByHer(layout, m, beyond, s, alongX)) continue;
                    // (only when it meets her within the panel depth: else it's open room there, a stair's or a hatch's)
                    // (each voxel row of it on its own: her side flares or leans, so it's met at different depths)
                    var fill = new List<double[]>();
                    bool allMet = true;
                    for (double yy = s[1]; yy < s[4] - 1e-6 && allMet; yy += g.V)
                    {
                        bool met = false;
                        for (int step = 0; step < (int)(MountLayout.MaxPanelDepth / g.V) && !met; step++)
                        {
                            double a0 = end > 0 ? (alongX ? s[3] : s[5]) + step * g.V : (alongX ? s[0] : s[2]) - (step + 1) * g.V;
                            var box = alongX ? new[] { a0, yy, s[2], a0 + g.V, yy + g.V, s[5] } : new[] { s[0], yy, a0, s[3], yy + g.V, a0 + g.V };
                            if (AllSolid(g, box)) met = true;
                            else fill.Add(box);
                        }
                        allMet = met;
                    }
                    if (allMet) foreach (var box in fill) g.Fill(box);
                }
                // (0.3.12, note 123) up to the deck over it: where no partition can go over this one (it's the top row, or
                // a beam, a knee or the deck's underside leaves no border place there), the gap between its top and her
                // timbers over it is closed, each voxel column on its own up to the first solid (the beams' gaps, a deck
                // stepping up), at most CeilingGap; where a border place over it stands empty, that's an opening.
                bool topRow = p.Role == PieceRole.Door || layout.BorderAbove(m) == null;
                if (topRow) CloseToCeiling(g, s, fy + 2);
            }
            Filled?.Invoke(g);
            int i0 = Math.Max(0, g.CellX(r.Min[0] - 1)), i1 = Math.Min(g.NX - 1, g.CellX(r.Max[0] + 1));
            int k0 = Math.Max(0, g.CellZ(r.Min[2] - 1)), k1 = Math.Min(g.NZ - 1, g.CellZ(r.Max[2] + 1));
            int j0 = Math.Max(0, g.CellY(fy + 0.01)), j1 = Math.Min(g.NY - 1, g.CellY(fy + 2 - 0.01));
            var leakList = (leaks ?? Enumerable.Empty<double[]>()).Where(b => b != null && b.Length >= 6).ToList();
            int si = g.CellX(x), sk = g.CellZ(z), sj = Math.Clamp(g.CellY(Math.Max(y, fy) + 0.6), j0, j1);
            if (si < i0 || si > i1 || sk < k0 || sk > k1) return false;
            if (g.Solid(si, sj, sk))
            {
                // (feet on the floor: look a little up)
                bool found = false;
                for (int j = j0; j <= j1 && !found; j++) if (!g.Solid(si, j, sk)) { sj = j; found = true; }
                if (!found) return false;
            }
            int ni = i1 - i0 + 1, nj = j1 - j0 + 1, nk = k1 - k0 + 1;
            var seen = new bool[ni * nj * nk];
            var queue = new Queue<(int, int, int)>();
            queue.Enqueue((si, sj, sk));
            seen[((si - i0) * nj + (sj - j0)) * nk + (sk - k0)] = true;
            while (queue.Count > 0)
            {
                var (i, j, k) = queue.Dequeue();
                double cx = g.OX + (i + 0.5) * g.V, cz = g.OZ + (k + 0.5) * g.V;
                if (i == i0 || i == i1 || k == k0 || k == k1)
                {
                    Why?.Invoke($"out of the room at ({cx:0.00}, {g.OY + (j + 0.5) * g.V:0.00}, {cz:0.00})");
                    return false;      // out of the room: a doorway left open
                }
                foreach (var b in leakList)
                    if (cx > b[0] - 0.25 && cx < b[3] + 0.25 && cz > b[2] - 0.25 && cz < b[5] + 0.25)
                    {
                        Why?.Invoke($"under the open hatch at ({cx:0.00}, {cz:0.00})");
                        return false;   // under the open hatch
                    }
                for (int n = 0; n < 6; n++)
                {
                    int a = i + (n == 0 ? 1 : n == 1 ? -1 : 0), c = j + (n == 2 ? 1 : n == 3 ? -1 : 0), e = k + (n == 4 ? 1 : n == 5 ? -1 : 0);
                    if (a < i0 || a > i1 || c < j0 || c > j1 || e < k0 || e > k1) continue;
                    int idx = ((a - i0) * nj + (c - j0)) * nk + (e - k0);
                    if (seen[idx] || g.Solid(a, c, e)) continue;
                    seen[idx] = true;
                    queue.Enqueue((a, c, e));
                }
            }
            return true;
        }

        /// <summary>
        /// (0.3.12) The border of the next floor cell along a partition's line takes no partition because her own timbers
        /// (her side curving in, a knee, a beam: the design's collision) cut into its layer, not because it's kept clear: no
        /// border place there on that line at that row, and the design's collision in the layer.
        /// </summary>
        public static bool CutByHer(MountLayout layout, MountPoint m, MountPoint beyond, double[] s, bool alongX)
        {
            var (line, _) = MountLayout.BorderLine(m);
            double a0 = alongX ? beyond.X : beyond.Z, a1 = a0 + 1;
            foreach (var b in layout.Borders)
            {
                if (b.Deck != m.Deck || b.Row != m.Row || b.In % 2 != m.In % 2) continue;
                var (bl, _) = MountLayout.BorderLine(b);
                if (Math.Abs(bl - line) > 1e-6) continue;
                double c = alongX ? b.X : b.Z;
                if (c > a0 && c < a1) return false;       // a place there: the line goes on as pieces (or it's open)
            }
            var g = layout.Def.Grid;
            const double e = 0.02;
            return alongX ? g.Collides(a0 + e, s[1] + e, s[2] + e, a1 - e, s[4] - e, s[5] - e)
                          : g.Collides(s[0] + e, s[1] + e, a0 + e, s[3] - e, s[4] - e, a1 - e);
        }

        /// <summary>
        /// (0.3.16) The design's wells in a room's ceiling (their foot within 0.6 of its top, their middle over it) that open
        /// onto an open deck (no closed room over them) and aren't panelled over (horizontal panels at their top covering all
        /// but 2 % of them): the room is open there as under an open hatch.
        /// </summary>
        public static List<double[]> WellLeaks(ShipDefinition def, RoomDef r, IEnumerable<ShipPiece> pieces)
        {
            var list = new List<double[]>();
            if (def?.Wells == null || r == null) return list;
            // (a shelf over it, or a shut hatch: a scuttle over a ladder's way up, note 169)
            var shelves = (pieces ?? Enumerable.Empty<ShipPiece>()).Where(p => (p.Role == PieceRole.Shelf || p.Role == PieceRole.Hatch && !p.Open) && p.Box != null).ToList();
            foreach (var w in def.Wells)
            {
                var b = w?.Box;
                if (b == null || b.Length < 6) continue;
                double cx = (b[0] + b[3]) / 2, cz = (b[2] + b[5]) / 2;
                if (cx < r.Min[0] || cx > r.Max[0] || cz < r.Min[2] || cz > r.Max[2] || Math.Abs(b[1] - r.Max[1]) > 0.6) continue;
                if (def.RoomAt(cx, b[4] + 0.5, cz)?.Enclosed == true) continue;
                double area = (b[3] - b[0]) * (b[5] - b[2]), covered = 0;
                foreach (var s in shelves)
                {
                    if (Math.Abs(s.Box[4] - b[4]) > 0.3) continue;
                    double ox = Math.Min(b[3], s.Box[3]) - Math.Max(b[0], s.Box[0]), oz = Math.Min(b[5], s.Box[5]) - Math.Max(b[2], s.Box[2]);
                    if (ox > 0 && oz > 0) covered += ox * oz;
                }
                if (covered < area * 0.98) list.Add(b);
            }
            return list;
        }

        /// <summary>
        /// (0.3.22, step C) Her guns with a lidded port (a gun fixture with <see cref="FixtureDef.Lid"/>, its port and her
        /// side's faces there): the waist's and the castles' (whose ports may be cut in her collision: the lid seals them
        /// while it's shut, docs/slots.md §7).
        /// </summary>
        public static IEnumerable<FixtureDef> LiddedPorts(ShipDefinition def) =>
            (def?.Fixtures ?? Enumerable.Empty<FixtureDef>()).Where(f => f != null && f.Type == "gun" && f.Lid != null &&
                f.Port?.Length >= 4 && f.Wall?.Length >= 4 && f.Pos?.Length >= 3);

        /// <summary>The side a gun's port is in: -1 port, +1 starboard (by its muzzle's way out, else where it stands).</summary>
        public static int PortSide(FixtureDef f) => f.Out?.Length >= 3 && Math.Abs(f.Out[2]) > 0.5 ? Math.Sign(f.Out[2]) : Math.Sign(f.Pos[2]);

        /// <summary>
        /// (0.3.25, the stern chasers) A gun that points along her length (out of her stern or her bow, not her side): its
        /// port is in the transom, given as (z0 y0 z1 y1), and its wall as the transom's faces' x (docs/slots.md §7).
        /// </summary>
        public static bool AlongX(FixtureDef f) => f?.Out?.Length >= 3 && Math.Abs(f.Out[0]) > Math.Abs(f.Out[2]);

        /// <summary>
        /// A lidded gun's port through her side (ship-local x0 y0 z0 x1 y1 z1): its opening (x, y) from her side's inner face
        /// to its outer one (the side tumbles home: the inner face at the head to the outer face at the sill). (0.3.25) A
        /// stern chaser's: its opening (z, y) through the transom, between the transom's faces (x).
        /// </summary>
        public static double[] PortBox(FixtureDef f)
        {
            if (AlongX(f))
            {
                double xa = Math.Min(Math.Min(f.Wall[0], f.Wall[1]), Math.Min(f.Wall[2], f.Wall[3]));
                double xb = Math.Max(Math.Max(f.Wall[0], f.Wall[1]), Math.Max(f.Wall[2], f.Wall[3]));
                return new[] { xa, Math.Min(f.Port[1], f.Port[3]), Math.Min(f.Port[0], f.Port[2]), xb, Math.Max(f.Port[1], f.Port[3]), Math.Max(f.Port[0], f.Port[2]) };
            }
            int side = PortSide(f);
            double zin = Math.Min(Math.Min(f.Wall[0], f.Wall[1]), Math.Min(f.Wall[2], f.Wall[3]));
            double zout = Math.Max(Math.Max(f.Wall[0], f.Wall[1]), Math.Max(f.Wall[2], f.Wall[3]));
            double z0 = side < 0 ? -zout : zin, z1 = side < 0 ? -zin : zout;
            return new[] { Math.Min(f.Port[0], f.Port[2]), Math.Min(f.Port[1], f.Port[3]), z0, Math.Max(f.Port[0], f.Port[2]), Math.Max(f.Port[1], f.Port[3]), z1 };
        }

        /// <summary>A shut port's lid in a collision grid of voxel size v: the port's box, a little under a voxel more each way (every voxel the cut may have taken).</summary>
        public static double[] PortFill(FixtureDef f, double v)
        {
            var b = PortBox(f);
            double m = v * 0.45;
            return new[] { b[0] - m, b[1] - m, b[2] - m, b[3] + m, b[4] + m, b[5] + m };
        }

        /// <summary>The room a gun's port opens (where the gun stands), or null (the open waist).</summary>
        public static RoomDef PortRoom(ShipDefinition def, FixtureDef f) => def?.RoomAt(f.Pos[0], f.Pos[1] + 0.5, f.Pos[2]);

        /// <summary>How far over a partition's top (blocks) her timbers may be and still close it off to them (0.3.12).</summary>
        public const double CeilingGap = 1.25;

        /// <summary>
        /// A partition's (or a door's) solid carried up to her timbers over it: each voxel column of it from its top up to
        /// the first solid voxel, if that's within CeilingGap (or the band over the room, top, is reached first).
        /// </summary>
        static void CloseToCeiling(VoxelGrid g, double[] s, double top)
        {
            for (double x = s[0] + g.V / 2; x < s[3]; x += g.V)
                for (double z = s[2] + g.V / 2; z < s[5]; z += g.V)
                {
                    double y = s[4] + g.V / 2;
                    double limit = Math.Min(top, s[4] + CeilingGap);
                    while (y < limit && !g.SolidAt(x, y, z)) y += g.V;
                    if (y < limit || y >= top - 1e-6)
                        for (double yy = s[4] + g.V / 2; yy < y; yy += g.V)
                        {
                            // (the voxel the sample is in, by its middle)
                            double cx = g.OX + (g.CellX(x) + 0.5) * g.V, cy = g.OY + (g.CellY(yy) + 0.5) * g.V, cz = g.OZ + (g.CellZ(z) + 0.5) * g.V;
                            g.Fill(new[] { cx - g.V / 4, cy - g.V / 4, cz - g.V / 4, cx + g.V / 4, cy + g.V / 4, cz + g.V / 4 });
                        }
                }
        }

        static bool AllSolid(VoxelGrid g, double[] b)
        {
            for (double x = b[0] + g.V / 2; x < b[3]; x += g.V)
                for (double y = b[1] + g.V / 2; y < b[4]; y += g.V)
                    for (double z = b[2] + g.V / 2; z < b[5]; z += g.V)
                        if (!g.SolidAt(x, y, z)) return false;
            return true;
        }
    }

    public static class ShipMounts
    {
        /// <summary>An older design's lantern place: a hook now (the lantern on it a piece), not an attach place any more.</summary>
        public static bool IsLegacyHook(FixtureDef f) => f != null && f.Type == "attach" && f.Kind == "lantern";

        /// <summary>The design's mount points, worked out once (on both sides alike: from the definition only).</summary>
        public static MountLayout For(ShipDefinition def)
        {
            if (def == null) return null;
            return def.Mounts ??= MountLayout.Build(def);
        }

        /// <summary>Direction (0 +x, 1 +z, 2 -x, 3 -z) a fixture's yaw faces (0 the bow, 90 port).</summary>
        public static int DirOfYaw(double yawDeg)
        {
            double a = yawDeg * Math.PI / 180;
            return MountLayout.Snap(Math.Cos(a), -Math.Sin(a));
        }

        /// <summary>A fixture's yaw (degrees) for a direction.</summary>
        public static float YawOfDir(int dir) => dir switch { 0 => 0f, 1 => 270f, 2 => 180f, _ => 90f };
    }
}
