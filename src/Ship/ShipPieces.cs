using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Hansa
{
    /// <summary>How a vanilla block chooses its look from where it's put (as vanilla places it).</summary>
    public enum PieceOrient
    {
        /// <summary>No variant for it: its mesh is turned to face whoever put it there.</summary>
        None,
        /// <summary>HorizontalOrientable (or any side variant): the side the placer looks toward ("side" = away from them).</summary>
        Orientable,
        /// <summary>HorizontalAttachable (paintings): hangs on a wall, its side the way to the wall.</summary>
        Attachable,
        /// <summary>OmniAttachable (lanterns): up on a floor, down from a hook, on a wall its side away from the wall.</summary>
        Omni,
        /// <summary>A vanilla bed: its feet where it's put, its head a block further from the placer.</summary>
        Bed,
    }

    /// <summary>What a placed piece does aboard.</summary>
    public enum PieceRole
    {
        Deco = 0, Storage = 1, Bed = 2, Seat = 3, Light = 4, Panel = 5, Partition = 6, Door = 7,
        /// <summary>
        /// (0.3.16, notes 154–159) A glazed panel on a border, a balustrade on one, a horizontal plank panel. (0.3.18) Ladder:
        /// the companion ladder-stair, 1 × 2, climbed (0.3.16–0.3.18 saved it as "the stair": 10 stays the ladder-stair).
        /// </summary>
        Window = 8, Railing = 9, Ladder = 10, Shelf = 11,
        /// <summary>(0.3.18, note 165) The ship's hearth (a brick firebed with vanilla's firepit on it), her clay oven, a segment of the hearth's flue.</summary>
        Hearth = 12, Oven = 13, Flue = 14,
        /// <summary>(0.3.19) A walked stair, 1 × 3 (about 45°), for open decks up to a castle's front (docs/modular-pieces.md §8).</summary>
        Stair = 15,
        /// <summary>(0.3.20, step B; docs/slots.md §5) A hatch over an opening in a deck (vanilla trapdoors): opens and shuts, flush with the deck.</summary>
        Hatch = 16
    }

    /// <summary>
    /// What placing needs to know of a vanilla block (<see cref="VanillaPieceBlock"/> reads it from the game; the offline
    /// tests make their own): how it orients, what it does, its variants, the cells it takes, its boxes.
    /// </summary>
    public abstract class PieceBlock
    {
        public string Code;
        public PieceOrient Orient;
        /// <summary>A container (its slots), a light, a seat (a chair, stool or bench), a bed (its sleep efficiency).</summary>
        public bool Container, Light, Seat;
        public int Slots;
        public float SleepEfficiency = 0.7f;
        /// <summary>Omni: it has wall variants (north, east, south, west) besides up and down.</summary>
        public bool OmniWalls;
        /// <summary>A container whose mesh is turned (vanilla: its block entity's mesh angle), even with a side variant (a trunk).</summary>
        public bool TurnMesh;
        /// <summary>A planks block: it panels a wedge along her side (a panel place).</summary>
        public bool Planks;
        /// <summary>A surface layer laid on a block's face (vanilla's wallpaper, the "Decor" behaviour): on a wall only, flat on it.</summary>
        public bool Overlay;
        /// <summary>A vanilla door (the "Door" behaviour) one block wide and at most two high: it goes on a cell's border and opens.</summary>
        public bool Door;
        /// <summary>A block with workings of its own (a block entity: a forge, a quern, an anvil) that don't run aboard yet.</summary>
        public bool AshoreOnly;
        /// <summary>
        /// (0.3.16) A glass pane (a window on a border), a fence (a railing), stairs (0.3.19: a walked 1 × 3 stair), a plank slab
        /// (a horizontal panel); (0.3.19) a ladder (the companion ladder-stair, 1 × 2, climbed).
        /// </summary>
        public bool Pane, Fence, Stairs, Slab, Ladder;
        /// <summary>(0.3.16) A stair's rise when it's given (a design's modular piece); 0: up to the deck above.</summary>
        public double Rise;
        /// <summary>
        /// (0.3.23) A ship's stair: its cells along its run (3 the long one, 2 the steep one; 0.1.0-dev.6, note 36: 1 the short
        /// one) and its own handrails (the mod's
        /// ship's stairs have them; no railing goes beside one). NeedsBuilding: a ship's stair read out of building mode
        /// (it's placed in building mode only).
        /// </summary>
        public int StairCells = 3;
        public bool StairRails, NeedsBuilding;
        /// <summary>(0.3.18, note 165) Fire bricks (or a firepit): the ship's hearth; vanilla's clay oven: her oven; a brick chimney: a flue segment.</summary>
        public bool Hearth, Oven, Flue;
        /// <summary>(0.1.0-dev.9, caravel note 78) A figurehead: vanilla's carved ones, or a chiseled block (as the sailboat takes them).</summary>
        public bool Figurehead;
        /// <summary>The disconnected native fence variant used when placing a rope post on a deck.</summary>
        public string FencePostCode;
        /// <summary>Visible bounds in the block's own frame (for hanging and figurehead shapes).</summary>
        public double[] VisualBounds;
        public virtual double[] BoundsOf(string variant) => VisualBounds;
        public virtual List<PieceVolume> VolumesOf(string variant) => null;
        /// <summary>(0.3.17) A door's way to swing (0 +x, 1 +z, 2 -x, 3 -z; across its line) when it's given (a design's modular piece); -1: into the anchor cell.</summary>
        public int DoorInto = -1;

        /// <summary>This block with its side (or position) variant set to state ("north", "up"...), or null if there's none.</summary>
        public abstract string Variant(string state);
        /// <summary>The cells a variant takes, from where it's put (a 2 × 1 painting, a trunk): (0, 0, 0) first.</summary>
        public abstract List<int[]> Cells(string variant);
        /// <summary>A variant's selection or collision boxes in its block (x0 y0 z0 x1 y1 z1, 0..1); none for no collision.</summary>
        public abstract double[][] Boxes(string variant, bool collision);
        /// <summary>A bed: its head or feet block with this side.</summary>
        public virtual string BedPart(string part, string side) => null;
    }

    /// <summary>A vanilla block of a piece: its code, where its block's corner is (ship-local), and its mesh turned by quarter turns about the block's middle.</summary>
    public class PiecePart
    {
        public string Block;
        public double[] Origin;
        public int Quarter;
    }

    /// <summary>A piece worked out for a mount point, or why it can't go there (a lang code).</summary>
    public class PiecePlan
    {
        public string Error;
        public MountPoint Mount;
        public MountKind Kind;
        /// <summary>The way toward whoever put it (0 +x, 1 +z, 2 -x, 3 -z): its front faces that way.</summary>
        public int Dir;
        public readonly List<string> Mounts = new();
        public readonly List<PiecePart> Parts = new();
        public readonly List<PieceVolume> Volumes = new();
        public readonly List<double[]> Sel = new(), Solids = new();
        public double[] Box;
        public PieceRole Role;
        public int Slots;
        public float Efficiency;
        /// <summary>Wall: the wall's face (a point on it) and its normal into the room, the piece tilted onto it.</summary>
        public double[] Pivot, Tilt;
        /// <summary>Hook: down or up (drawn from its own mesh's bounds, hung from or stood on the hook).</summary>
        public string Hang;
        /// <summary>A bed or a seat: where the sleeper or sitter is (feet, ship-local) and which way they face (degrees, a fixture's yaw).</summary>
        public double[] Seat;
        public float SeatYaw;
        /// <summary>Cells over the first a piece needs room in (a tall block): checked against her collision when it's put.</summary>
        public readonly List<double[]> Room = new();
        /// <summary>(0.3.16) A stair's rise (blocks, its deck to the deck above).</summary>
        public double Rise;
        /// <summary>
        /// (0.3.23, note 170/172) The way toward whoever put it that it was planned with (Plan's toward). The client sends
        /// this, not Dir, to the server: a stair's or a ladder's Dir is the way up (away from them), and the server planned
        /// it again turned round.
        /// </summary>
        public int Toward;
        /// <summary>(0.3.23) A stair's cells along its run, and its own handrails (a ship's stair).</summary>
        public int Cells;
        public bool Rails;
        public double Coaming;
        public bool DoubleLeaf;
        List<PieceVolume> placementVolumes;
        public IReadOnlyList<PieceVolume> PlacementVolumes => placementVolumes ??= Role == PieceRole.Stair
            ? PieceShape.StairVolumes(Mount, Dir, Rise, Cells, Rails).ToList()
            : Volumes.Count > 0 ? Volumes : Sel.Select(b => new PieceVolume { Box = b, Pose = PieceShape.Tilt(Pivot, Tilt, Dir) }).ToList();
    }

    /// <summary>
    /// Where and how a vanilla block goes on a mount point, as vanilla would place it there by a player at that side of it:
    /// the variant (a side for orientable, attachable and omni-attachable blocks, a bed's head and feet), the cells it takes
    /// (vanilla's multiblock: a trunk across two places, a 2 × 1 painting along a wall), its mesh's turn when it has no
    /// variant for it (turned to face the player, snapped to her four ways), its boxes (rotated with it), and what it does.
    /// Pure geometry: the server, every client and the tests get the same.
    /// </summary>
    public static class PiecePlanner
    {
        /// <summary>The quarter turns (about the block's middle) that turn a mesh facing +z (vanilla's chest) to face dir.</summary>
        public static int QuarterFor(int dir) => (5 - dir) % 4;

        /// <summary>A block box turned by quarter turns about (0.5, y, 0.5) the way Matrixf.RotateY turns it.</summary>
        public static double[] Turn(double[] b, int q)
        {
            q = ((q % 4) + 4) % 4;
            if (q == 0) return (double[])b.Clone();
            double x0 = b[0] - 0.5, x1 = b[3] - 0.5, z0 = b[2] - 0.5, z1 = b[5] - 0.5;
            double nx0, nx1, nz0, nz1;
            switch (q)
            {
                case 1: nx0 = z0; nx1 = z1; nz0 = -x1; nz1 = -x0; break;        // (x, z) → (z, -x)
                case 2: nx0 = -x1; nx1 = -x0; nz0 = -z1; nz1 = -z0; break;      // → (-x, -z)
                default: nx0 = -z1; nx1 = -z0; nz0 = x0; nz1 = x1; break;       // → (-z, x)
            }
            return new[] { nx0 + 0.5, b[1], nz0 + 0.5, nx1 + 0.5, b[4], nz1 + 0.5 };
        }

        /// <summary>A cell offset (dx, dz) turned by quarter turns.</summary>
        static (int, int) TurnCell(int dx, int dz, int q) => (((q % 4) + 4) % 4) switch
        {
            1 => (dz, -dx),
            2 => (-dx, -dz),
            3 => (-dz, dx),
            _ => (dx, dz)
        };

        /// <summary>
        /// As <see cref="Plan(MountLayout, MountPoint, PieceBlock, int)"/>; (24 October 2026, caravel notes 70, 79: "i can place
        /// two vessels here but cannot place trunk", "any unoccupied spot should allow placement, beds included") a piece of
        /// two or more cells on a floor place that doesn't fit as vanilla lays it (or that fits says no to) is laid another
        /// way over the place aimed at: its other cells on the near side, then turned a quarter (a trunk along the view, a
        /// bed across it), then (a bed) its head toward the placer. The first way that fits and fits says yes to; else the
        /// first. Client and server ask the same (fits leaves out who stands where: that's asked of the plan chosen).
        /// </summary>
        public static PiecePlan Plan(MountLayout layout, MountPoint m, PieceBlock b, int toward, System.Func<PiecePlan, bool> fits)
        {
            var first = Plan(layout, m, b, toward);
            if (first.Error == null && (fits == null || fits(first)) || m?.Kind != MountKind.Floor || b == null ||
                b.Ladder || b.Stairs || b.Hearth || b.Oven || b.Flue || b.Orient == PieceOrient.Attachable) return first;
            int look0 = (((toward % 4) + 4) % 4 + 2) % 4;
            bool bed = b.Orient == PieceOrient.Bed;
            foreach (int turn in bed ? new[] { 0, 1, 3, 2 } : new[] { 0, 1, 3 })
            {
                int look = (look0 + turn) % 4;
                foreach (var (di, dk) in FootprintShifts(b, look))
                {
                    if (turn == 0 && di == 0 && dk == 0 || bed && turn == 2 && (di != 0 || dk != 0)) continue;
                    var at = layout.FloorAt(m.Deck, m.I - di, m.K - dk, m.Level);
                    if (at == null) continue;
                    var alt = Plan(layout, at, b, (look + 2) % 4);
                    if (alt.Error != null || !alt.Mounts.Contains(m.Id) || fits != null && !fits(alt)) continue;
                    alt.Toward = first.Toward;
                    return alt;
                }
            }
            return first;
        }

        /// <summary>(24 October 2026) The offsets of a floor piece's cells from its place, looking that way (a bed's head a block on).</summary>
        static IEnumerable<(int, int)> FootprintShifts(PieceBlock b, int look)
        {
            if (b.Orient == PieceOrient.Bed)
            {
                yield return (0, 0);
                yield return (MountLayout.DX[look], MountLayout.DZ[look]);
                yield break;
            }
            string variant = b.Orient == PieceOrient.Orientable ? b.Variant(MountLayout.DirCode[look]) ?? b.Code : b.Orient == PieceOrient.Omni ? b.Variant("up") ?? b.Code : b.Code;
            int q = QuarterFor((look + 2) % 4);
            var seen = new HashSet<(int, int)>();
            foreach (var c in b.Cells(variant) ?? new List<int[]> { new[] { 0, 0, 0 } })
            {
                if (c[1] != 0) continue;
                var (dx, dz) = b.Orient == PieceOrient.None ? TurnCell(c[0], c[2], q) : (c[0], c[2]);
                if (seen.Add((dx, dz))) yield return (dx, dz);
            }
        }

        /// <summary>(25 October 2026, note 89) A bit on a plan's toward: a door hung at its border's +x/+z end, not its -x/-z end.</summary>
        public const int HingeHigh = 4;

        public static PiecePlan Plan(MountLayout layout, MountPoint m, PieceBlock b, int toward)
        {
            var p = new PiecePlan { Mount = m, Kind = m?.Kind ?? MountKind.Floor, Dir = ((toward % 4) + 4) % 4 };
            // ((25 October 2026, caravel note 89: "door can be placed only with hinges on one side") HingeHigh in toward: a
            // door's hinge at its border's +x/+z end, where the crosshair was nearer it)
            p.Toward = p.Dir | (toward >= 0 && (toward & HingeHigh) != 0 ? HingeHigh : 0);
            if (m == null || b == null) { p.Error = "ships:piece-nowhere"; return p; }
            // (0.3.23) a ship's stair is put aboard in building mode only
            if (b.NeedsBuilding) { p.Error = "ships:shipstair-build-mode"; return p; }
            int look = (p.Dir + 2) % 4;
            p.Role = b.Container ? PieceRole.Storage : b.Orient == PieceOrient.Bed ? PieceRole.Bed : b.Seat ? PieceRole.Seat : b.Light ? PieceRole.Light : PieceRole.Deco;
            p.Slots = b.Slots;
            p.Efficiency = b.SleepEfficiency;
            if (b.Overlay && m.Kind != MountKind.Wall) { p.Error = "ships:piece-needs-wall"; return p; }
            if (b.Door && m.Kind != MountKind.Border) { p.Error = "ships:door-needs-border"; return p; }
            // (0.3.16, notes 154-159) a pane is a window, a fence a railing: on a border; stairs on the floor; a slab is a
            // horizontal panel and nothing but a slab goes on a horizontal place
            if ((b.Pane || b.Fence) && m.Kind != MountKind.Border) { p.Error = "ships:piece-needs-border"; return p; }
            if ((b.Stairs || b.Ladder) && m.Kind != MountKind.Floor) { p.Error = "ships:stair-needs-floor"; return p; }
            if (m.Kind == MountKind.Horizontal && !b.Slab) { p.Error = "ships:shelf-needs-slab"; return p; }
            // (0.3.18, note 165) the hearth and the oven on a floor place, a flue segment on a flue place and nothing else there
            if ((b.Hearth || b.Oven) && m.Kind != MountKind.Floor) { p.Error = "ships:hearth-needs-floor"; return p; }
            if (b.Flue != (m.Kind == MountKind.Flue)) { p.Error = b.Flue ? "ships:flue-needs-place" : "ships:piece-nowhere"; return p; }
            switch (m.Kind)
            {
                case MountKind.Flue: return FluePlan(m, b, p);
                // (0.3.19) a ladder: the companion ladder-stair; plank stairs: a walked stair
                case MountKind.Floor: return b.Ladder ? LadderStair(layout, m, b, p, look) : b.Stairs ? Flight(layout, m, b, p, look) : b.Hearth || b.Oven ? HearthPlan(layout, m, b, p, look) : Floor(layout, m, b, p, look);
                case MountKind.Wall: return Wall(layout, m, b, p);
                case MountKind.Panel: return Panel(m, b, p);
                case MountKind.Border: return b.Door ? Door(layout, m, b, p) : b.Pane ? BorderPiece(m, b, p, PieceRole.Window) : b.Fence ? BorderPiece(m, b, p, PieceRole.Railing) : Partition(m, b, p);
                case MountKind.Horizontal: return Shelf(m, b, p);
                default: return Hook(m, b, p);
            }
        }

        /// <summary>
        /// A planks block on a cell's border: a partition, a block high, on the grid line (drawn 1/8 thick, picked as a sheet
        /// on the line so pieces stand right up to it either side); its collision the voxel layer on the anchor cell's side
        /// (so nobody walks through it). One per row; a row 1 one stands on a row 0 one.
        /// </summary>
        static PiecePlan Partition(MountPoint m, PieceBlock b, PiecePlan p)
        {
            if (!b.Planks) { p.Error = "ships:partition-needs-planks"; return p; }
            p.Role = PieceRole.Partition;
            p.Dir = m.In;
            p.Mounts.Add(m.Id);
            BorderBoxes(m, 1, p);
            p.Parts.Add(new PiecePart { Block = b.Code, Origin = new[] { p.Box[0], p.Box[1], p.Box[2] } });
            return p;
        }

        /// <summary>
        /// (0.3.16, notes 154, 155, 157) A glass pane on a border place: a window, a block-sized glazed panel of the
        /// partition's thickness (its casing inside the block, lead cames on a ~0.2 grid); a fence: a railing, a balustrade a
        /// block high (beside a stair raked to follow it: <see cref="ShipPiece.Rake"/>). Both picked, drawn and solid as a
        /// partition; a window seals, a railing doesn't. Not on a place only partly free of her.
        /// </summary>
        static PiecePlan BorderPiece(MountPoint m, PieceBlock b, PiecePlan p, PieceRole role)
        {
            // (0.3.17) a railing goes on a place only partly free of her too (a castle front's corner by her side), cut to it
            // ((25 October 2026, caravel note 90) and a window, cut to it as a partition is: not on a sliver)
            if (m.Clip != null && role == PieceRole.Window && ClipArea(m) < 0.25) { p.Error = "ships:window-no-room"; return p; }
            p.Role = role;
            p.Dir = m.In;
            p.Mounts.Add(m.Id);
            BorderBoxes(m, 1, p);
            p.Parts.Add(new PiecePart { Block = b.Code, Origin = new[] { p.Box[0], p.Box[1], p.Box[2] } });
            return p;
        }

        /// <summary>(25 October 2026) How much of a border place's block is free of her (its Clip's area; 1 whole).</summary>
        public static double ClipArea(MountPoint m) => m?.Clip == null ? 1 : m.Clip.Sum(c => Math.Max(0, c[2] - c[0]) * Math.Max(0, c[3] - c[1]));

        /// <summary>(0.3.16) How high a raked railing's handrail is over the treads (0.3.19: over the nosings' line, the usual 0.9).</summary>
        public const double RailHeight = 0.9;

        /// <summary>
        /// (0.3.16) A railing's boxes: flat (rake null), as a partition's (drawn on the line, picked as a sheet on it, solid
        /// in the quarter-block layer on its anchor side) a block high; raked beside a stair, in quarters along it, each from
        /// a little under the treads to RailHeight over them (rake: their height over the deck at its -x/-z and +x/+z ends).
        /// </summary>
        public static void RailingBoxes(MountPoint m, double[] rake, List<double[]> sel, List<double[]> solids, out double[] box, int solidSide = 0, double capY = double.MaxValue)
        {
            sel.Clear();
            solids.Clear();
            var (line, alongX) = MountLayout.BorderLine(m);
            // (0.3.17) beside a stair its quarter-block layer is on the side away from the stair (both a stair's railings
            // inside its cell left half a block: no way up it)
            double inSign = solidSide != 0 ? solidSide : m.In < 2 ? 1 : -1, h = MountLayout.PartitionThickness / 2;
            double c0 = Math.Min(line, line + inSign * 0.25), c1 = Math.Max(line, line + inSign * 0.25);
            double a0 = (alongX ? m.X : m.Z) - 0.5;
            int n = rake == null ? 1 : 4;
            var drawn = new List<double[]>();
            for (int i = 0; i < n; i++)
            {
                double s0 = a0 + (double)i / n, s1 = a0 + (double)(i + 1) / n, y0 = m.Y, y1 = m.Y + 1;
                if (rake != null)
                {
                    double hl = rake[0] + (rake[1] - rake[0]) * i / n, hh = rake[0] + (rake[1] - rake[0]) * (i + 1) / n;
                    y0 = m.Y + Math.Max(0, Math.Min(hl, hh) - 0.25);
                    y1 = m.Y + Math.Max(hl, hh) + RailHeight;
                }
                // (0.3.17) drawn up to its handrail, but solid and picked only up to the deck the stair climbs to: over it the
                // well's own railings are (a railing of the hold's stair rising through the lower deck by the companion stair)
                double sy1 = Math.Max(y0 + 0.25, Math.Min(y1, capY));
                if (alongX)
                {
                    drawn.Add(new[] { s0, y0, line - h, s1, y1, line + h });
                    sel.Add(new[] { s0, y0, line - 0.005, s1, sy1, line + 0.005 });
                    solids.Add(new[] { s0, y0, c0, s1, sy1, c1 });
                }
                else
                {
                    drawn.Add(new[] { line - h, y0, s0, line + h, y1, s1 });
                    sel.Add(new[] { line - 0.005, y0, s0, line + 0.005, sy1, s1 });
                    solids.Add(new[] { c0, y0, s0, c1, sy1, s1 });
                }
            }
            box = Union(drawn);
        }

        /// <summary>
        /// (0.3.16; 0.3.19: walked stairs only) A railing on the border along a walked stair's side (its deck, row 0, the line
        /// along its run, within its three cells): the nosings' line's height over the deck at its -x/-z and +x/+z ends
        /// (<see cref="FlightNosing"/>: its handrail follows the treads, <see cref="RailHeight"/> over them); else null.
        /// </summary>
        public static double[] StairRake(MountPoint foot, int up, double rise, MountPoint m) => StairRake(foot, up, rise, m, out _);

        /// <summary>
        /// (0.3.17) Every railing along a placed walked stair's side raked (its boxes up to its handrail over the treads, solid
        /// on the side away from the stair), the others flat; changed ones' ids. (0.3.19, the user: "ladders look better
        /// without rails") None beside a ladder-stair: a railing there is flat (and can't be put there: <see cref="BesideLadder"/>).
        /// </summary>
        public static List<int> ApplyRakes(MountLayout layout, IEnumerable<ShipPiece> pieces)
        {
            var changed = new List<int>();
            var all = pieces.ToList();
            // ((0.3.23) a ship's stair has its own handrails: nothing is raked to it)
            var stairs = all.Where(p => p.Role == PieceRole.Stair && p.Rise > 0 && !p.Rails).ToList();
            foreach (var r in all)
            {
                if (r.Role != PieceRole.Railing || layout.Get(r.Mount) is not MountPoint m || m.Clip != null) continue;
                double[] rake = null;
                int away = 0;
                ShipPiece by = null;
                foreach (var st in stairs)
                    if ((rake = StairRake(layout.Get(st.Mount), st.Dir, st.Rise, m, out away)) != null) { by = st; break; }
                // (its quarter-block layer: always away from the stair (0.3.19: a walked stair keeps its whole width for the
                // walker; 0.3.17 put it inside beside a well, for the ladder-stairs one over the other, which have no railings now)
                if (rake == null && r.Rake == null || rake != null && r.Rake != null && Math.Abs(rake[0] - r.Rake[0]) < 1e-6 && Math.Abs(rake[1] - r.Rake[1]) < 1e-6) continue;
                r.Rake = rake;
                RailingBoxes(m, rake, r.Sel, r.Solids, out r.Box, away, by == null ? double.MaxValue : layout.Get(by.Mount).Y + by.Rise);
                changed.Add(r.Id);
            }
            return changed;
        }

        /// <summary>
        /// (0.3.19) Is a border place on a stair's side: row 0 on its deck, on a line along its run, within its first cells
        /// cells from its foot? uLo, uHi: the place's ends along the run from the foot cell's near edge (-x/-z end first);
        /// away: which way across the line is away from the stair (+1, -1).
        /// </summary>
        public static bool BesideStair(MountPoint foot, int up, int cells, MountPoint m, out double uLo, out double uHi, out int away)
        {
            uLo = uHi = 0;
            away = 0;
            if (foot == null || m == null || m.Kind != MountKind.Border || m.Row != 0 || m.Deck != foot.Deck || Math.Abs(m.Y - foot.Y) > 0.01) return false;
            var (line, alongX) = MountLayout.BorderLine(m);
            bool runX = up == 0 || up == 2;
            if (alongX != runX) return false;
            double side0 = runX ? foot.Z : foot.X;
            if (Math.Abs(line - side0) > 0.01 && Math.Abs(line - (side0 + 1)) > 0.01) return false;
            int sgn = up < 2 ? 1 : -1;
            double start = runX ? (sgn > 0 ? foot.X : foot.X + 1) : (sgn > 0 ? foot.Z : foot.Z + 1);
            double mid = runX ? m.X : m.Z;
            uLo = sgn > 0 ? mid - 0.5 - start : start - (mid - 0.5);
            uHi = sgn > 0 ? mid + 0.5 - start : start - (mid + 0.5);
            if (Math.Min(uLo, uHi) < -0.01 || Math.Max(uLo, uHi) > cells + 0.01) return false;
            away = Math.Abs(line - side0) <= 0.01 ? -1 : 1;
            return true;
        }

        /// <summary>As StairRake; away: which way across its line is away from the stair (+1, -1).</summary>
        public static double[] StairRake(MountPoint foot, int up, double rise, MountPoint m, out int away)
        {
            if (!BesideStair(foot, up, FlightCells, m, out double uLo, out double uHi, out away)) return null;
            return new[] { FlightNosing(rise, uLo), FlightNosing(rise, uHi) };
        }

        /// <summary>
        /// (0.3.19, the user: "railings beside ladder-stairs are weirdly placed and look wrong") A border place along a
        /// ladder-stair's side (its deck, row 0, within its two cells): no railing goes there. The ladder-stair, or null.
        /// </summary>
        public static ShipPiece BesideLadder(MountLayout layout, IEnumerable<ShipPiece> pieces, MountPoint m)
        {
            if (layout == null || m == null || m.Kind != MountKind.Border) return null;
            foreach (var p in pieces)
                if (p.Role == PieceRole.Ladder && BesideStair(layout.Get(p.Mount), p.Dir, 2, m, out _, out _, out _)) return p;
            return null;
        }

        /// <summary>
        /// (0.3.23) A border place along the side of a stair with its own handrails (a ladder-stair: its two cells; a ship's
        /// stair: its cells) on its deck, row 0: no railing goes there. That piece, or null.
        /// </summary>
        public static ShipPiece BesideRailed(MountLayout layout, IEnumerable<ShipPiece> pieces, MountPoint m)
        {
            if (BesideLadder(layout, pieces, m) is ShipPiece l) return l;
            if (layout == null || m == null || m.Kind != MountKind.Border) return null;
            foreach (var p in pieces)
                if (p.Role == PieceRole.Stair && p.Rails && BesideStair(layout.Get(p.Mount), p.Dir, (int)RunOf(p), m, out _, out _, out _)) return p;
            return null;
        }

        // ------------------------------------------------------------------ (0.3.19) the walked stair; (0.3.23) the ship's stairs

        /// <summary>
        /// (0.3.19) A walked stair's cells along its run (foot, middle, head) and its run (blocks): the long ship's stair. (0.3.23)
        /// The steep one: 2. (0.1.0-dev.6, note 36: "stairs taking 1 block - NOT LADDERS") The short one: 1, walked up through
        /// a 2 × 1 opening (its cell and the cell before it), its steps <see cref="DeckMover.ShipStairStep"/> high.
        /// </summary>
        public const int FlightCells = 3, SteepCells = 2, ShortCells = 1;
        public const double FlightRun = 3.0;
        /// <summary>
        /// (0.3.19) Its risers at most this high: a quarter block (a voxel), so a man stepping up near its foot (his head over
        /// the 0.6 before the tread) stays under the deck at a 3 × 1 well's near edge.
        /// </summary>
        public const double FlightMaxRiser = 0.25;
        /// <summary>(0.3.23) Her collision's columns along a stair's run are a voxel long.</summary>
        public const double FlightColumn = 0.25;
        /// <summary>(0.3.19) How far the deck above may reach back over its head cell (its top tread, flush with that deck, is under it): a voxel.</summary>
        public static double FlightOverhang(double rise) => FlightColumn;

        /// <summary>(0.3.19) How many treads a walked stair of this rise has (risers at most <see cref="FlightMaxRiser"/>; the top one flush with the deck above).</summary>
        public static int FlightTreads(double rise) => Math.Max(2, (int)Math.Ceiling(rise / FlightMaxRiser - 1e-6));

        /// <summary>(0.3.19) The stair's nosing line over its deck at u along it (from the foot cell's near edge): 0 there, the rise at the head cell's far edge.</summary>
        public static double FlightHeight(double rise, double u, double run = FlightRun) => rise * Math.Clamp(u / run, 0, 1);

        /// <summary>
        /// (0.3.19) The line through the treads' nosings (each tread's leading edge, down the stair) over the deck at u: a
        /// tread's rise over <see cref="FlightHeight"/>. A raked railing's (0.3.23: a ship's stair's own) handrail runs
        /// <see cref="RailHeight"/> over it.
        /// </summary>
        public static double FlightNosing(double rise, double u, double run = FlightRun) => rise * u / run + rise / FlightTreads(rise);

        /// <summary>(0.3.19) Tread k (1 to n): from u0 to u1 along the run, its top y over the deck (as drawn).</summary>
        public static (double u0, double u1, double y) FlightTread(double rise, int k, double run = FlightRun)
        {
            int n = FlightTreads(rise);
            double d = run / n;
            return ((k - 1) * d, k * d, rise * k / n);
        }

        /// <summary>
        /// (0.3.23) Her collision's steps of a stair: a voxel-long column every quarter block along the run, its top the drawn
        /// tread's over the column's middle (the long stair: each column a tread; the steep one: 8 columns for its 12 treads,
        /// risers of a quarter or a half). From u0 to u1, top y. (0.1.0-dev.6, note 36) The short one: the tread's at the
        /// column's far end, 4 columns 0.75 apart, the last flush with the deck above as the others' are (a deck reaching back
        /// over its cell a quarter lies on it).
        /// </summary>
        public static List<(double u0, double u1, double y)> FlightColumns(double rise, double run = FlightRun)
        {
            var list = new List<(double, double, double)>();
            int n = FlightTreads(rise), cols = Math.Max(1, (int)Math.Round(run / FlightColumn));
            double d = run / n;
            for (int j = 0; j < cols; j++)
            {
                double u0 = j * FlightColumn, u1 = u0 + FlightColumn, mid = u0 + FlightColumn / 2;
                int k = Math.Clamp((int)Math.Ceiling((run < SteepCells ? u1 : mid) / d - 1e-9), 1, n);
                list.Add((u0, u1, rise * k / n));
            }
            return list;
        }

        /// <summary>(0.3.19) A walked stair's boxes (picked and solid): (0.3.23) her collision's columns, each a voxel thick, its width in from the cell's sides.</summary>
        public static List<double[]> FlightBoxes(MountPoint foot, int up, double rise, double run = FlightRun)
        {
            var list = new List<double[]>();
            double c = 0.5 - StairInset;
            foreach (var (u0, u1, y) in FlightColumns(rise, run))
                list.Add(StairBox(foot, up, u0, Math.Max(0, y - 0.25), -c, u1, y, c));
            return list;
        }

        /// <summary>(0.3.19) A man's height over a tread (the deck mover's 1.85) and over the 0.6 before it, where he stands stepping up (his width).</summary>
        public const double FlightHeadroom = DeckMover.Height, FlightBehind = 2 * DeckMover.HalfWidth;

        /// <summary>
        /// (0.3.19) The room a walked stair needs: a man's height over every step (0.3.23: her collision's columns) and over
        /// the 0.6 before it where he stands stepping up (his 0.6 width across its middle), and a man's room where he steps on
        /// at its foot (half a block before it) and off at its head (half a block past it, on the deck above).
        /// </summary>
        public static List<double[]> FlightRoom(MountPoint foot, int up, double rise, double run = FlightRun)
        {
            var list = new List<double[]>();
            foreach (var (u0, u1, y) in FlightColumns(rise, run))
                list.Add(StairBox(foot, up, u0 - FlightBehind, y + 0.05, -DeckMover.HalfWidth, u1, y + FlightHeadroom, DeckMover.HalfWidth));
            // ((0.3.28) as wide and as long as the man standing there: the stern's lower-deck stair stepped him off onto her
            // side's curve, 0.5 high, a quarter block out of a narrower check)
            double w = DeckMover.HalfWidth;
            foreach (var (u, y) in new[] { (-0.5, 0.0), (run + 0.5, rise) })
                list.Add(StairBox(foot, up, u - w, y + 0.05, -w, u + w, y + 1.8, w));
            return list;
        }

        /// <summary>(0.3.19) Where one steps onto a walked stair (half a block before its foot cell) and off it (half a block past its head, on the deck above): feet, ship-local.</summary>
        /// <summary>
        /// (25 October 2026, caravel note 99) Is there no floor of hers (grid: her collision as it is) where one steps onto a
        /// planned stair or ladder-stair at its foot, or off it at its top? The planner asks it of her decks laid; the ship
        /// asks it of her as she is (a hatchway shut by its hatch is a floor, a cell taken up isn't).
        /// </summary>
        public static bool LandingMissing(PiecePlan plan, VoxelGrid grid)
        {
            if (plan?.Mount == null || grid == null || plan.Error != null) return false;
            if (plan.Role == PieceRole.Stair)
            {
                double run = plan.Cells > 0 ? plan.Cells : FlightCells;
                var on = FlightFoot(plan.Mount, plan.Dir);
                var off = FlightTop(plan.Mount, plan.Dir, plan.Rise, run);
                return !DeckMover.HasFloor(grid, on[0], on[1] + 0.01, on[2]) || !DeckMover.HasFloor(grid, off[0], off[1] + 0.01, off[2]) ||
                       !grid.Collides(off[0] - 0.3, off[1] - 0.12, off[2] - 0.3, off[0] + 0.3, off[1] + 0.01, off[2] + 0.3);
            }
            if (plan.Role == PieceRole.Ladder)
            {
                var lad = StairLadder(plan.Mount, plan.Dir, plan.Rise, null);
                return !DeckMover.HasFloor(grid, lad.ExitBottom[0], lad.ExitBottom[1] + 0.01, lad.ExitBottom[2]) ||
                       !DeckMover.HasFloor(grid, lad.ExitTop[0], lad.ExitTop[1] + 0.01, lad.ExitTop[2]);
            }
            return false;
        }

        public static double[] FlightFoot(MountPoint foot, int up) => StairPoint(foot, up, -0.5, 0);
        public static double[] FlightTop(MountPoint foot, int up, double rise, double run = FlightRun) => StairPoint(foot, up, run + 0.5, rise);

        /// <summary>A walked stair's rise: from its deck to the lowest deck 1 to 4.5 blocks over it at the step off its head (half a cell past it).</summary>
        public static double FlightRise(MountLayout layout, MountPoint foot, int up, double run = FlightRun) => DeckRiseAt(layout, foot, up, run + 0.5);

        /// <summary>(0.3.23) A stair piece's run (blocks): its cells (saved; 0 for one saved before 0.3.23: the long one).</summary>
        public static double RunOf(ShipPiece p) => p?.Cells > 0 ? p.Cells : FlightCells;

        /// <summary>
        /// (0.1.0-dev.6, note 36) Where a walker may take a ship's stair's step (<see cref="DeckMover.ShipStairStep"/>) up or
        /// down: over a short stair's cell (and a little before and past it, where he stands to step on and off), from its
        /// deck to over the deck above. Null for a longer stair (its steps are a man's own).
        /// </summary>
        public static double[] SteepFlightBox(MountPoint foot, int up, double rise, double run)
        {
            if (foot == null || run >= SteepCells || rise <= 0) return null;
            return StairBox(foot, up, -0.35, -0.1, -0.5, run + 0.35, rise + 0.5, 0.5);
        }

        /// <summary>
        /// (0.3.19, the user: "maybe two versions: stairs and ladders; stairs can take more space, longer"; 0.3.23, the user:
        /// "can we make stairs for 2 blocks also? ladder animation is kind of bad"; "for ships stairs better to have a
        /// special block") A ship's stair (ships:shipstair-long or -steep, building mode) on a floor place: a walked stair 1
        /// wide over 3 cells (about 45°) or 2 (about 56°) climbing away from whoever puts it (its foot the place, then the next
        /// cells), from its deck to the deck above (<see cref="FlightRise"/>): twelve treads a quarter block high on two
        /// stringers, open underneath, walked by the deck mover over her collision's columns (<see cref="FlightColumns"/>). Its
        /// top is flush with the deck above at the head cell's far edge: on an open deck up to a castle's front no well is
        /// needed (the deck above may reach back over the head cell's last <see cref="FlightOverhang"/>); through a deck, a
        /// well over its cells (the steep one also a quarter block before its foot: docs/modular-pieces.md §10; 0.1.0-dev.6,
        /// note 36: the short one, ships:shipstair-short, 1 cell at about 72°, a 2 × 1 well over its cell and the one before it,
        /// docs/legacy/modular-pieces.md §15). Its own
        /// handrails (<see cref="FlightGuards"/>: solid beside its treads, drawn rails 0.9 over its nosings), so no railing goes
        /// beside it. It takes the foot's place and the places over its cells; the others' own places stay free (a chest under
        /// it). Dir: the way up.
        /// </summary>
        static PiecePlan Flight(MountLayout layout, MountPoint m, PieceBlock b, PiecePlan p, int look)
        {
            if (m.Level != 0) { p.Error = "ships:stair-needs-floor"; return p; }
            int n = b.StairCells is ShortCells or SteepCells or FlightCells ? b.StairCells : FlightCells;
            double run = n;
            var cells = new List<MountPoint> { m };
            for (int c = 1; c < n; c++)
            {
                var cell = layout.FloorAt(m.Deck, m.I + MountLayout.DX[look] * c, m.K + MountLayout.DZ[look] * c);
                // Only the foot and landings need floor support. A flight can cross a cell whose floor place is
                // reserved by the layout; its actual timbers and walking channel are checked against the ship below.
                if (cell != null) cells.Add(cell);
            }
            double rise = b.Rise > 0 ? b.Rise : FlightRise(layout, m, look, run);
            if (rise < 1 || rise > 4.5) { p.Error = "ships:stair-no-deck"; return p; }
            // (where one steps on and off it: her floor there)
            // ((25 October 2026, caravel note 99: "cannot place stairs in this direction up") her decks laid: her openings as
            // they are (a hatchway with its hatch shut, cells laid since) are the ship's to say, PlaceProblem)
            var grid = layout.LaidGrid ?? layout.Def?.Grid;
            var on = FlightFoot(m, look);
            var off = FlightTop(m, look, rise, run);
            if (grid != null && (!DeckMover.HasFloor(grid, on[0], on[1] + 0.01, on[2]) || !DeckMover.HasFloor(grid, off[0], off[1] + 0.01, off[2]) ||
                                 // ((0.3.29) past its head her deck itself, not a beam's top a quarter under it (a well's edge))
                                 !grid.Collides(off[0] - 0.3, off[1] - 0.12, off[2] - 0.3, off[0] + 0.3, off[1] + 0.01, off[2] + 0.3)))
            { p.Error = "ships:stair-no-landing"; return p; }
            p.Role = PieceRole.Stair;
            p.Dir = look;
            p.Rise = rise;
            p.Cells = n;
            p.Rails = b.StairRails;
            p.Mounts.Add(m.Id);
            foreach (var cell in cells)
                for (int level = 1; level <= MountLayout.MaxStack; level++)
                    if (layout.FloorAt(m.Deck, cell.I, cell.K, level) is MountPoint upPlace) p.Mounts.Add(upPlace.Id);
            foreach (var t in FlightBoxes(m, look, rise, run))
            {
                p.Solids.Add(t);
                p.Sel.Add((double[])t.Clone());
            }
            // (0.3.23) its own handrails' solid guards beside its treads (not picked)
            if (p.Rails) p.Solids.AddRange(FlightGuards(layout, m, look, rise, run));
            p.Room.AddRange(FlightRoom(m, look, rise, run));
            p.Box = Union(p.Sel);
            p.Parts.Add(new PiecePart { Block = b.Code, Origin = new[] { m.X, m.Y, m.Z }, Quarter = look });
            return p;
        }

        /// <summary>
        /// (0.3.23, the user: handrails built into the stair, as the ladder's) A ship's stair's own guards: on each side of it,
        /// in the quarter-block layer outside its cells (as a raked railing's), a box per voxel column of its run from a
        /// quarter under its nosings to <see cref="RailHeight"/> over them, solid so nobody steps off its side; left out where
        /// her collision is already there (a bulwark, a bulkhead beside it), and stopped at the deck above's top where that deck
        /// is beside it (a well's sides: that deck's own railings go there). Worked out from her design's collision (the same on
        /// the server, every client and the tests).
        /// </summary>
        public static List<double[]> FlightGuards(MountLayout layout, MountPoint foot, int up, double rise, double run = FlightRun)
        {
            var list = new List<double[]>();
            var grid = layout?.Def?.Grid;
            if (foot == null) return list;
            foreach (int side in new[] { -1, 1 })
                foreach (var (u0, u1, _) in FlightColumns(rise, run))
                {
                    double y0 = Math.Max(0, Math.Min(FlightNosing(rise, u0, run), rise) - 0.25), y1 = Math.Min(FlightNosing(rise, u1, run), rise) + RailHeight;
                    double ca = side * 0.5, cb = side * 0.75, c0 = Math.Min(ca, cb), c1 = Math.Max(ca, cb);
                    if (grid != null)
                    {
                        // ((0.3.27, note 215: "blocked moving forward by stairs when it looks like the path is clear") beside a
                        // walkable deck the guard starts over a man's height: whoever walks along the stair isn't stopped
                        // by it (its quarter-block layer is in the next cell); off its low treads one only steps down
                        // onto that deck. Over a drop (a well, an open edge) it's solid all the way down, as before)
                        // ((0.3.29, note 233) a design well's deck cell beside it counts as deck (the waist companion's well
                        // beside the quarterdeck stair: walled in, its stair's walker isn't stopped by the guard)
                        var bp = StairPoint(foot, up, (u0 + u1) / 2, 0, side * 0.625);
                        if (DeckBeside(grid, bp) || DesignWellBeside(layout?.Def, bp)) y0 = Math.Max(y0, FlightHeadroom + 0.05);
                        // (her own side there: nothing to add)
                        var strip = StairBox(foot, up, u0 + 0.03, y0 + 0.03, c0 + 0.03, u1 - 0.03, y1 - 0.03, c1 - 0.03);
                        if (grid.Collides(strip[0], strip[1], strip[2], strip[3], strip[4], strip[5])) continue;
                        // (the deck above beside it: up to its top)
                        var above = StairBox(foot, up, u0 + 0.03, rise - 0.2, c0 + 0.03, u1 - 0.03, rise - 0.03, c1 - 0.03);
                        if (y1 > rise && grid.Collides(above[0], above[1], above[2], above[3], above[4], above[5])) y1 = rise;
                    }
                    if (y1 <= y0 + 0.05) continue;
                    list.Add(StairBox(foot, up, u0, y0, c0, u1, y1, c1));
                }
            return list;
        }

        /// <summary>
        /// (0.3.19) Walk a walked stair up and down again in a collision grid, as a player would (the deck mover: steps up at
        /// most 0.6, snaps down the steps; 0.1.0-dev.6, note 36: on a short stair a ship's stair's step): from half a block before its foot walking up the run until past its head on the
        /// deck above, then back down to the foot. top: how high over its deck the walk up ended; back: where the walk down
        /// ended. True if both ended on her floor at the right decks.
        /// </summary>
        public static bool WalkFlight(VoxelGrid g, MountPoint foot, int up, double rise, out double top, out double back, double run = FlightRun)
        {
            var a = FlightFoot(foot, up);
            var b = FlightTop(foot, up, rise, run);
            double ax = MountLayout.DX[up], az = MountLayout.DZ[up];
            // ((0.1.0-dev.6, note 36) the short one's steps are a ship's stair's, as her walker takes them aboard)
            var steep = SteepFlightBox(foot, up, rise, run);
            var mv = new DeckMover(a[0], a[1], a[2]) { OnGround = true, SteepFlights = steep == null ? null : new[] { steep } };
            mv.Unstick(g);
            for (int n = 0; n < 200; n++)
            {
                mv.Step(g, 0.05, ax, az, 3.0, false, false);
                if ((mv.X - b[0]) * ax + (mv.Z - b[2]) * az > 0) break;
            }
            for (int n = 0; n < 10; n++) mv.Step(g, 0.05, 0, 0, 3.0, false, false);
            top = mv.Y - a[1];
            bool upOk = Math.Abs(top - rise) < 0.1 && DeckMover.HasFloor(g, mv.X, mv.Y + 0.01, mv.Z);
            for (int n = 0; n < 200; n++)
            {
                mv.Step(g, 0.05, -ax, -az, 3.0, false, false);
                if ((mv.X - a[0]) * ax + (mv.Z - a[2]) * az < 0) break;
            }
            for (int n = 0; n < 10; n++) mv.Step(g, 0.05, 0, 0, 3.0, false, false);
            back = mv.Y - a[1];
            return upOk && Math.Abs(back) < 0.1;
        }

        // ------------------------------------------------------------------ (0.3.18, note 162) the companion ladder-stair

        /// <summary>(0.3.18) Where a ladder-stair's feet stand (from its foot cell's near edge) and how far it runs to the head cell's far edge.</summary>
        public const double LadderFoot = 0.5, LadderRun = 1.5;
        /// <summary>(0.3.18) How far behind the ladder's line the climber's feet go (his middle), and how wide his head is checked through the well.</summary>
        public const double ClimbBack = 0.2, HeadHalf = 0.2;
        /// <summary>(0.3.18) The ladder's boxes (picked, solid): segments hugging its line, from a little under it to a little over it.</summary>
        public const int LadderSegments = 8;
        public const double LadderUnder = 0.15, LadderOver = 0.05;
        /// <summary>A stair's ladder stands in from the cell's sides (the borders' partition half-thickness: railings there).</summary>
        public const double StairInset = 0.0625;

        /// <summary>(0.3.18) The ladder's line's height over its deck at u along it (from the foot cell's near edge): 0 at its feet, the rise at the head cell's far edge.</summary>
        public static double LadderHeight(double rise, double u) => rise * Math.Clamp((u - LadderFoot) / LadderRun, 0, 1);

        /// <summary>How many treads a ladder-stair of this rise has (about 0.3 apart; the deck above is the last step).</summary>
        public static int LadderTreads(double rise) => Math.Max(2, (int)Math.Round(rise / 0.3) - 1);

        /// <summary>(0.3.18) A point by a stair (ship-local): u along its run from its foot cell's near edge, y over its deck, c across from its middle.</summary>
        public static double[] StairPoint(MountPoint foot, int up, double u, double y, double c = 0)
        {
            bool alongX = up == 0 || up == 2;
            int sgn = up < 2 ? 1 : -1;
            double start = alongX ? (sgn > 0 ? foot.X : foot.X + 1) : (sgn > 0 ? foot.Z : foot.Z + 1);
            double mid = (alongX ? foot.Z : foot.X) + 0.5;
            return alongX ? new[] { start + sgn * u, foot.Y + y, mid + c } : new[] { mid + c, foot.Y + y, start + sgn * u };
        }

        /// <summary>(0.3.18) A box by a stair from (u0, y0, c0) to (u1, y1, c1) in its own terms, ship-local.</summary>
        public static double[] StairBox(MountPoint foot, int up, double u0, double y0, double c0, double u1, double y1, double c1)
        {
            var a = StairPoint(foot, up, u0, y0, c0);
            var b = StairPoint(foot, up, u1, y1, c1);
            return new[] { Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]), Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2]) };
        }

        /// <summary>
        /// (0.3.18, note 162: "stairs need a rethink, steeper") The way up a ladder-stair, climbed as her ladders are
        /// (<see cref="DeckMover"/>, kind "stair"): the climber's feet from a little behind its feet to the deck above's
        /// level behind its top, stepping off half a block past the head cell's far edge, stepping on half a block before
        /// the foot cell's near edge.
        /// </summary>
        public static LadderDef StairLadder(MountPoint foot, int up, double rise, string code)
        {
            if (foot == null || rise <= 0) return null;
            return new LadderDef
            {
                Code = code, Kind = "stair",
                Bottom = StairPoint(foot, up, LadderFoot - ClimbBack, 0),
                Top = StairPoint(foot, up, LadderFoot + LadderRun - ClimbBack, rise),
                ExitBottom = StairPoint(foot, up, -0.5, 0),
                ExitTop = StairPoint(foot, up, 2.5, rise)
            };
        }

        /// <summary>(0.3.18) A ladder-stair's boxes (picked and solid): segments along its line, its width in from the cell's sides.</summary>
        public static List<double[]> StairLadderBoxes(MountPoint foot, int up, double rise)
        {
            var list = new List<double[]>();
            double c = 0.5 - StairInset;
            for (int k = 0; k < LadderSegments; k++)
            {
                double u0 = LadderFoot + LadderRun * k / LadderSegments, u1 = LadderFoot + LadderRun * (k + 1) / LadderSegments;
                double y0 = Math.Max(0, LadderHeight(rise, u0) - LadderUnder), y1 = LadderHeight(rise, u1) + LadderOver;
                list.Add(StairBox(foot, up, u0, y0, -c, u1, y1, c));
            }
            return list;
        }

        /// <summary>(0.3.18) How far over his feet the climber's room is checked at t up the ladder: all of him, but near its top his head and shoulders only (his legs go over the coaming it's hooked on).</summary>
        public static double ClimberLow(double t) => t <= 0.75 ? 0.1 : 1.1;

        /// <summary>
        /// (0.3.18) The room a ladder-stair needs: the climber (his head 0.4 wide, 1.85 over his feet) all the way up its
        /// path through the well in the deck above, and a man's room where he steps on at its foot and off at its top.
        /// </summary>
        public static List<double[]> StairClimbRoom(MountPoint foot, int up, double rise)
        {
            var list = new List<double[]>();
            var l = StairLadder(foot, up, rise, null);
            const int n = 12;
            for (int k = 0; k <= n; k++)
            {
                double t = (double)k / n;
                double x = l.Bottom[0] + (l.Top[0] - l.Bottom[0]) * t, y = l.Bottom[1] + (l.Top[1] - l.Bottom[1]) * t, z = l.Bottom[2] + (l.Top[2] - l.Bottom[2]) * t;
                list.Add(new[] { x - HeadHalf, y + ClimberLow(t), z - HeadHalf, x + HeadHalf, y + DeckMover.Height, z + HeadHalf });
            }
            foreach (var e in new[] { l.ExitBottom, l.ExitTop })
                list.Add(new[] { e[0] - 0.25, e[1] + 0.05, e[2] - 0.25, e[0] + 0.25, e[1] + 1.8, e[2] + 0.25 });
            return list;
        }

        /// <summary>
        /// (0.3.16, note 154; 0.3.18, note 162: a companion ladder-stair) Plank stairs on a floor place: a steep ladder 1 wide
        /// over 2 cells climbing away from whoever puts it (its foot the place, its head the next cell on), from its deck to
        /// the deck above (<see cref="StairRise"/>): its feet <see cref="LadderFoot"/> in from the foot cell's near edge, its
        /// top hooked over the deck above at the head cell's far edge (about 63° for a rise of 3). Climbed as a ladder
        /// (<see cref="StairLadder"/>), through a 2-cell well in the deck above (the climber's room checked:
        /// <see cref="StairClimbRoom"/>). It takes the foot's place and the places over both cells; the head's own place
        /// under its high end stays free (chalk note 157 of 1 November: a chest under it). Dir: the way up.
        /// </summary>
        static PiecePlan LadderStair(MountLayout layout, MountPoint m, PieceBlock b, PiecePlan p, int look)
        {
            if (m.Level != 0) { p.Error = "ships:stair-needs-floor"; return p; }
            var head = layout.FloorAt(m.Deck, m.I + MountLayout.DX[look], m.K + MountLayout.DZ[look]);
            if (head == null) { p.Error = "ships:stair-no-room"; return p; }
            double rise = b.Rise > 0 ? b.Rise : StairRise(layout, m, look);
            if (rise < 1 || rise > 4.5) { p.Error = "ships:stair-no-deck"; return p; }
            // (where one steps on and off it: her floor there; ((25 October 2026, note 99) her decks laid, as a flight's)
            var grid = layout.LaidGrid ?? layout.Def?.Grid;
            var lad = StairLadder(m, look, rise, null);
            if (grid != null && (!DeckMover.HasFloor(grid, lad.ExitBottom[0], lad.ExitBottom[1] + 0.01, lad.ExitBottom[2]) ||
                                 !DeckMover.HasFloor(grid, lad.ExitTop[0], lad.ExitTop[1] + 0.01, lad.ExitTop[2])))
            { p.Error = "ships:stair-no-landing"; return p; }
            p.Role = PieceRole.Ladder;
            p.Dir = look;
            p.Rise = rise;
            p.Mounts.Add(m.Id);
            foreach (var cell in new[] { m, head })
                for (int level = 1; level <= MountLayout.MaxStack; level++)
                    if (layout.FloorAt(m.Deck, cell.I, cell.K, level) is MountPoint up) p.Mounts.Add(up.Id);
            foreach (var t in StairLadderBoxes(m, look, rise))
            {
                p.Solids.Add(t);
                p.Sel.Add((double[])t.Clone());
            }
            // (0.3.19) its guard rails where a side borders a drop: solid, not picked
            p.Solids.AddRange(LadderGuards(layout, m, look));
            p.Room.AddRange(StairClimbRoom(m, look, rise));
            p.Box = Union(p.Sel);
            p.Parts.Add(new PiecePart { Block = b.Code, Origin = new[] { m.X, m.Y, m.Z }, Quarter = look });
            return p;
        }

        /// <summary>
        /// (0.3.18) A stair saved before 0.3.18 (a walked 2 × 1 flight: treads) gets its ladder's boxes; true if it changed.
        /// Worked out the same on the server and every client.
        /// </summary>
        public static bool RefreshStair(MountLayout layout, ShipPiece p)
        {
            // ((0.3.27, note 215) a ship's stair's guards as worked out now: saved ones kept the old, full-height layer)
            if (p?.Role == PieceRole.Stair && p.Rails && p.Rise > 0 && layout?.Get(p.Mount) is MountPoint sf)
            {
                double run = RunOf(p);
                var want = FlightBoxes(sf, p.Dir, p.Rise, run).Concat(FlightGuards(layout, sf, p.Dir, p.Rise, run)).ToList();
                if (p.Solids.Count == want.Count && p.Solids.Zip(want, (a, b2) => a.Zip(b2, (x, y) => Math.Abs(x - y)).Max()).All(d => d < 1e-6)) return false;
                p.Solids = want;
                return true;
            }
            if (p?.Role != PieceRole.Ladder || p.Rise <= 0 || layout?.Get(p.Mount) is not MountPoint foot) return false;
            var ladderBoxes = StairLadderBoxes(foot, p.Dir, p.Rise);
            // (0.3.19) and its guard rails where a side borders a drop
            var boxes = ladderBoxes.Concat(LadderGuards(layout, foot, p.Dir)).ToList();
            if (p.Solids.Count == boxes.Count && p.Solids.Zip(boxes, (a, b2) => a.Zip(b2, (x, y) => Math.Abs(x - y)).Max()).All(d => d < 1e-6)) return false;
            p.Solids = boxes;
            p.Sel = ladderBoxes.Select(x => (double[])x.Clone()).ToList();
            p.Box = Union(p.Sel);
            return true;
        }

        /// <summary>(0.3.19) How high a ladder-stair's guard rail stands over its deck (and is solid).</summary>
        public const double LadderGuardHeight = 1.0;

        /// <summary>
        /// (0.3.19, the coordinator: "someone stepping sideways off the companion ladder's foot falls 3 blocks into the hold")
        /// A ladder-stair's guard rails: on each side of its two cells that borders a drop (no deck beside it on its deck: a
        /// well, an open edge), a rail on the border line, solid in the quarter-block layer outside it (in the drop, never in
        /// the ladder's own cell, where one walks onto it) from its deck to <see cref="LadderGuardHeight"/>; one box per side
        /// over the cells that drop there, end to end. No railing goes beside a ladder (<see cref="BesideLadder"/>): this is
        /// the ladder's own. Worked out from her collision (the same on the server, every client and the tests).
        /// </summary>
        public static List<double[]> LadderGuards(MountLayout layout, MountPoint foot, int up)
        {
            var list = new List<double[]>();
            var grid = layout?.Def?.Grid;
            if (grid == null || foot == null) return list;
            foreach (int side in new[] { -1, 1 })
            {
                double from = -1, to = -1;
                for (int c = 0; c < 2; c++)
                {
                    var n = StairPoint(foot, up, c + 0.5, 0, side);
                    bool drop = !DeckBeside(grid, n);
                    if (drop) { if (from < 0) from = c; to = c + 1; }
                    else if (from >= 0) { list.Add(StairBox(foot, up, from, 0, side * 0.5, to, LadderGuardHeight, side * 0.75)); from = -1; }
                }
                if (from >= 0) list.Add(StairBox(foot, up, from, 0, side * 0.5, to, LadderGuardHeight, side * 0.75));
            }
            return list;
        }

        /// <summary>
        /// (0.3.19) Is there deck at a point (feet, ship-local): her collision within 0.15 of it, down to 0.3 under it? (Not
        /// <see cref="DeckMover.HasFloor"/>'s 0.6 square, which finds a well's edge from its middle.)
        /// </summary>
        /// <summary>(0.3.29) Is the point over a deck cell of the design that starts out (a design well's), on the deck the point is on?</summary>
        public static bool DesignWellBeside(ShipDefinition def, double[] p)
        {
            var l = def?.Slots;
            if (l == null) return false;
            foreach (var (top, _) in l.Tops)
                if (Math.Abs(top - p[1]) < 0.3 && l.OnDeck(top, p[0], p[2]) is int n && n >= 0 && l.Slots[n].StartsEmpty) return true;
            return false;
        }

        public static bool DeckBeside(VoxelGrid grid, double[] p) =>
            grid.Collides(p[0] - 0.15, p[1] - 0.3, p[2] - 0.15, p[0] + 0.15, p[1] + 0.02, p[2] + 0.15);

        /// <summary>(0.3.19) A point by a stair (ship-local x, z) in its own terms: u along its run from its foot cell's near edge, c across from its middle.</summary>
        public static (double u, double c) StairUC(MountPoint foot, int up, double x, double z)
        {
            bool alongX = up == 0 || up == 2;
            int sgn = up < 2 ? 1 : -1;
            double start = alongX ? (sgn > 0 ? foot.X : foot.X + 1) : (sgn > 0 ? foot.Z : foot.Z + 1);
            double mid = (alongX ? foot.Z : foot.X) + 0.5;
            return alongX ? ((x - start) * sgn, z - mid) : ((z - start) * sgn, x - mid);
        }

        /// <summary>A ladder-stair's rise: from its deck to the lowest deck 1 to 4.5 blocks over it at the step off its head (half a cell past it).</summary>
        public static double StairRise(MountLayout layout, MountPoint foot, int up) => DeckRiseAt(layout, foot, up, 2.5);

        /// <summary>From a foot cell's deck to the lowest deck 1 to 4.5 blocks over it at u along the way up (from the foot cell's near edge).</summary>
        static double DeckRiseAt(MountLayout layout, MountPoint foot, int up, double u)
        {
            double px = foot.X + 0.5 + MountLayout.DX[up] * (u - 0.5), pz = foot.Z + 0.5 + MountLayout.DZ[up] * (u - 0.5);
            double best = 0;
            foreach (var d in layout.Def.Decks)
            {
                double r = d.Y - foot.Y;
                if (d.Code == "top" || r < 1 - 1e-6 || r > 4.5 || px < d.X0 || px > d.X1) continue;
                if (best == 0 || r < best) best = r;
            }
            return best;
        }

        // ------------------------------------------------------------------ (0.3.18, note 165) the hearth, the oven, the flue

        /// <summary>How high a placed hearth's firebed is (the firepit stands on it) and its kerb round three sides.</summary>
        public const double FirebedHeight = 0.25, KerbHeight = 0.5, KerbThickness = 0.125;
        /// <summary>A placed hearth needs this much clear over its deck (its hood, the flue's start).</summary>
        public const double HearthHeadroom = 2.0;

        /// <summary>
        /// (0.3.18, note 165: "the hearth and oven could be placeable") Fire bricks (or a firepit) on a floor place: the ship's
        /// hearth, a brick firebed filling the cell (a kerb on three sides, open toward whoever put it: Dir) with vanilla's
        /// firepit on it, <see cref="HearthHeadroom"/> clear over it; vanilla's clay oven: her oven, its mouth toward them.
        /// Each takes its place and the places over it (nothing stands on a fire). What they do is the design's hearth's and
        /// oven's (<see cref="EntityShip.PieceFixture"/>: their stand-in fixtures).
        /// </summary>
        static PiecePlan HearthPlan(MountLayout layout, MountPoint m, PieceBlock b, PiecePlan p, int look)
        {
            // ((24 October 2026, caravel note 74: "cannot place oven on top of these blocks, i can place on floor") the oven on
            // a block put aboard too, as on her deck; the hearth on her deck only (its flue's places rise from there)
            if (m.Level != 0 && !b.Oven) { p.Error = "ships:hearth-needs-floor"; return p; }
            p.Role = b.Oven ? PieceRole.Oven : PieceRole.Hearth;
            p.Mounts.Add(m.Id);
            for (int level = m.Level + 1; level <= MountLayout.MaxStack; level++)
                if (layout.FloorAt(m.Deck, m.I, m.K, level) is MountPoint up) p.Mounts.Add(up.Id);
            var origin = new[] { m.X, m.Y, m.Z };
            if (b.Oven)
            {
                string variant = b.Variant(MountLayout.DirCode[look]) ?? b.Code;
                p.Parts.Add(new PiecePart { Block = variant, Origin = origin });
                AddBoxes(p, b, variant, 0, origin);
                if (p.Solids.Count == 0) p.Solids.Add(new[] { m.X, m.Y, m.Z, m.X + 1, m.Y + 1, m.Z + 1 });
            }
            else
            {
                p.Parts.Add(new PiecePart { Block = b.Code, Origin = origin, Quarter = QuarterFor(p.Dir) });
                var bed = new[] { m.X, m.Y, m.Z, m.X + 1, m.Y + KerbHeight, m.Z + 1 };
                p.Sel.Add(bed);
                p.Solids.Add((double[])bed.Clone());
                p.Room.Add(new[] { m.X + 0.05, m.Y + KerbHeight, m.Z + 0.05, m.X + 0.95, m.Y + HearthHeadroom, m.Z + 0.95 });
            }
            p.Box = Union(p.Sel);
            return p;
        }

        /// <summary>
        /// (0.3.18, note 165: "a chute for the smoke") A brick chimney on a flue place: a segment of the hearth's flue, a brick
        /// chute from a block over its deck up through the deck above to a block over that. It takes its place and the floor
        /// place it comes out on; it needs the hearth (or the segment it rises from) under it.
        /// </summary>
        static PiecePlan FluePlan(MountPoint m, PieceBlock b, PiecePlan p)
        {
            p.Role = PieceRole.Flue;
            p.Dir = 1;
            p.Mounts.Add(m.Id);
            if (m.OutPlace != null) p.Mounts.Add(m.OutPlace);
            var box = (double[])m.Box.Clone();
            p.Sel.Add(box);
            p.Solids.Add((double[])box.Clone());
            p.Box = (double[])box.Clone();
            p.Parts.Add(new PiecePart { Block = b.Code, Origin = new[] { box[0], box[1], box[2] } });
            return p;
        }

        /// <summary>
        /// (0.3.16, note 159) A plank slab on a horizontal place: a plank panel 1/8 thick under its plane (a shelf, a well's top
        /// closed flush with the deck), solid in the voxel layer under the plane; things stand on it.
        /// </summary>
        static PiecePlan Shelf(MountPoint m, PieceBlock b, PiecePlan p)
        {
            p.Role = PieceRole.Shelf;
            p.Dir = 1;
            p.Mounts.Add(m.Id);
            var box = (double[])m.Box.Clone();
            p.Sel.Add(box);
            p.Box = (double[])box.Clone();
            // ((0.3.28, note 230) a hood's roof lies on the walls' tops: solid in the voxel layer over its underside, a man's
            // height and more clear under it)
            double s0 = MountLayout.IsHood(m.Id) ? box[1] : box[4] - 0.25;
            p.Solids.Add(new[] { box[0], s0, box[2], box[3], s0 + 0.25, box[5] });
            p.Parts.Add(new PiecePart { Block = b.Code, Origin = new[] { box[0], box[1], box[2] } });
            return p;
        }

        /// <summary>
        /// (0.3.13, note 137) A border place's whole block on its line (x0 y0 z0 x1 y1 z1, the partition's thickness): what a
        /// partition there would be with nothing of her in the way. A partly free place's partition is this, cut to its
        /// <see cref="MountPoint.Clip"/> (the renderer cuts the planks' cube so, keeping its texture where it was).
        /// </summary>
        public static double[] BorderBlock(MountPoint m)
        {
            var (line, alongX) = MountLayout.BorderLine(m);
            double h = MountLayout.PartitionThickness / 2;
            return alongX ? new[] { m.X - 0.5, m.Y, line - h, m.X + 0.5, m.Y + 1, line + h } : new[] { line - h, m.Y, m.Z - 0.5, line + h, m.Y + 1, m.Z + 0.5 };
        }

        /// <summary>(0.3.13) A clip rectangle (along0, y0, along1, y1) as a box on a border's line, half thick each side of it.</summary>
        public static double[] ClipBox(MountPoint m, double[] c, double half)
        {
            var (line, alongX) = MountLayout.BorderLine(m);
            return alongX ? new[] { c[0], c[1], line - half, c[2], c[3], line + half } : new[] { line - half, c[1], c[0], line + half, c[3], c[2] };
        }

        /// <summary>
        /// A vanilla door on a cell's border (a gap in a partition line): both rows of the border (its upper half the place a
        /// block up), hung on the border's lower end (x or z), swinging into the anchor cell. Shut, it's a partition; open, the
        /// way through. Its block's mesh fitted onto the line (<see cref="ShipPiece.Open"/>).
        /// </summary>
        static PiecePlan Door(MountLayout layout, MountPoint m, PieceBlock b, PiecePlan p)
        {
            if (m.Row != 0) { p.Error = "ships:door-needs-floor"; return p; }
            var up = layout?.BorderAbove(m);
            // (0.3.13: a place only partly free of her takes a partition cut to it, not a door)
            if (up == null || m.Clip != null || up.Clip != null) { p.Error = "ships:door-no-room"; return p; }
            p.Role = PieceRole.Door;
            p.Dir = m.In;
            // (0.3.17) a design's door may swing the other way (into the landing, out onto the deck)
            if (b.DoorInto >= 0 && b.DoorInto % 2 == m.In % 2) p.Dir = b.DoorInto;
            p.Mounts.Add(m.Id);
            p.Mounts.Add(up.Id);
            BorderBoxes(m, 2, p);
            var (line, alongX) = MountLayout.BorderLine(m);
            // the hinge: the border's lower end, on the line; ((25 October 2026, note 89) or its upper end, as the player chose)
            var native = b.BoundsOf(b.Code) ?? new double[] {0,0,0,1,2,0.1875};
            double thick = Math.Clamp(Math.Min(native[3]-native[0],native[5]-native[2]),0.05,0.25);
            int thinAxis = alongX ? 2 : 0;
            p.Box[thinAxis] = line-thick/2; p.Box[thinAxis+3] = line+thick/2;
            p.Sel.Clear(); p.Sel.Add((double[])p.Box.Clone());
            double hingeAt = (p.Toward & HingeHigh) != 0 ? 0.5 : -0.5;
            p.Pivot = alongX ? new[] { m.X + hingeAt, m.Y, line } : new[] { line, m.Y, m.Z + hingeAt };
            p.Parts.Add(new PiecePart { Block = b.Code, Origin = (double[])p.Pivot.Clone() });
            return p;
        }

        /// <summary>A border piece's boxes, rows high: drawn on the line (Box), picked as a sheet on it (Sel), solid on the anchor side (Solids).</summary>
        static void BorderBoxes(MountPoint m, int rows, PiecePlan p)
        {
            var (line, alongX) = MountLayout.BorderLine(m);
            if (m.Clip != null && rows == 1)
            {
                // (0.3.13, note 137) only the free parts: drawn and picked there, solid on the anchor side there
                double inS = m.In < 2 ? 1 : -1, half = MountLayout.PartitionThickness / 2;
                double[] bb = null;
                foreach (var c in m.Clip)
                {
                    var drawn = ClipBox(m, c, half);
                    if (bb == null) bb = (double[])drawn.Clone();
                    else for (int a = 0; a < 3; a++) { bb[a] = Math.Min(bb[a], drawn[a]); bb[a + 3] = Math.Max(bb[a + 3], drawn[a + 3]); }
                    p.Sel.Add(ClipBox(m, c, 0.005));
                    var solid = ClipBox(m, c, 0);
                    int ax = alongX ? 2 : 0;
                    solid[ax] = Math.Min(line, line + inS * 0.25);
                    solid[ax + 3] = Math.Max(line, line + inS * 0.25);
                    p.Solids.Add(solid);
                }
                p.Box = bb;
                return;
            }
            double h = MountLayout.PartitionThickness / 2, y0 = m.Y, y1 = m.Y + rows;
            // the anchor cell is on the In side of the line
            double inSign = m.In < 2 ? 1 : -1;
            double c0 = Math.Min(line, line + inSign * 0.25), c1 = Math.Max(line, line + inSign * 0.25);
            if (alongX)
            {
                double x0 = m.X - 0.5, x1 = m.X + 0.5;
                p.Box = new[] { x0, y0, line - h, x1, y1, line + h };
                p.Sel.Add(new[] { x0, y0, line - 0.005, x1, y1, line + 0.005 });
                p.Solids.Add(new[] { x0, y0, c0, x1, y1, c1 });
            }
            else
            {
                double z0 = m.Z - 0.5, z1 = m.Z + 0.5;
                p.Box = new[] { line - h, y0, z0, line + h, y1, z1 };
                p.Sel.Add(new[] { line - 0.005, y0, z0, line + 0.005, y1, z1 });
                p.Solids.Add(new[] { c0, y0, z0, c1, y1, z1 });
            }
        }

        /// <summary>How thick a panel is drawn and picked out from its face (the rest of the wedge behind it is filled too).</summary>
        public const double PanelFace = 0.0625;

        /// <summary>
        /// A planks block on a panel place: a panel, its face on the cell's border facing into the room, the wedge behind it
        /// filled (solid, so nobody steps into it); drawn as the planks (Parts[0], its box the wedge: Solids[0]).
        /// </summary>
        static PiecePlan Panel(MountPoint m, PieceBlock b, PiecePlan p)
        {
            if (!b.Planks) { p.Error = "ships:panel-needs-planks"; return p; }
            p.Role = PieceRole.Panel;
            p.Dir = m.In;
            p.Mounts.Add(m.Id);
            var w = (double[])m.Box.Clone();
            // the face: from the border out a little (what's picked and outlined)
            int outAxis = m.In == 0 || m.In == 2 ? 0 : 2;
            double line = outAxis == 0 ? m.X : m.Z, outSign = m.In < 2 ? -1 : 1;
            var face = (double[])w.Clone();
            face[outAxis] = Math.Min(line, line + outSign * PanelFace);
            face[outAxis + 3] = Math.Max(line, line + outSign * PanelFace);
            // the wedge to fill: at least the face, out to her side
            w[outAxis] = Math.Min(w[outAxis], face[outAxis]);
            w[outAxis + 3] = Math.Max(w[outAxis + 3], face[outAxis + 3]);
            p.Parts.Add(new PiecePart { Block = b.Code, Origin = new[] { w[0], w[1], w[2] } });
            p.Sel.Add(face);
            p.Solids.Add(w);
            p.Box = (double[])face.Clone();
            p.Pivot = new[] { m.X, m.Y, m.Z };
            p.Tilt = m.Normal;
            return p;
        }

        static PiecePlan Floor(MountLayout layout, MountPoint m, PieceBlock b, PiecePlan p, int look)
        {
            if (b.Orient == PieceOrient.Attachable) { p.Error = "ships:piece-needs-wall"; return p; }
            if (b.Orient == PieceOrient.Bed) return Bed(layout, m, b, p, look);
            string variant = b.Orient switch
            {
                PieceOrient.Orientable => b.Variant(MountLayout.DirCode[look]) ?? b.Code,
                PieceOrient.Omni => b.Variant("up") ?? b.Code,
                _ => b.Code
            };
            variant=b.FencePostCode??variant;
            int q = b.Orient == PieceOrient.None || b.TurnMesh ? QuarterFor(p.Dir) : 0;
            var cells = b.Cells(variant) ?? new List<int[]> { new[] { 0, 0, 0 } };
            var origin = new[] { m.X, m.Y, m.Z };
            p.Parts.Add(new PiecePart { Block = variant, Origin = origin, Quarter = q });
            foreach (var c in cells)
            {
                // (a block with no variant of its own takes its cells turned with it)
                var (dx, dz) = b.Orient == PieceOrient.None ? TurnCell(c[0], c[2], q) : (c[0], c[2]);
                if (c[1] < 0) { p.Error = "ships:piece-no-room"; return p; }
                var at = layout.FloorAt(m.Deck, m.I + dx, m.K + dz, m.Level);
                if (at == null) { p.Error = "ships:piece-no-room"; return p; }
                if (!p.Mounts.Contains(at.Id)) p.Mounts.Add(at.Id);
                var cell = new[] { origin[0] + dx, origin[1] + c[1], origin[2] + dz };
                if (c[1] > 0) p.Room.Add(new[] { cell[0], cell[1], cell[2], cell[0] + 1, cell[1] + 1, cell[2] + 1 });
                AddBoxes(p, b, variant, q, cell);
            }
            Finish(p, b);
            if (p.Role == PieceRole.Seat)
            {
                // (toward the seat's front, the legs hanging over its edge as at the heads: note 91)
                p.Seat = new[] { m.X + 0.5 + MountLayout.DX[p.Dir] * SeatForward, m.Y + SeatHeight(p), m.Z + 0.5 + MountLayout.DZ[p.Dir] * SeatForward };
                p.SeatYaw = ShipMounts.YawOfDir(p.Dir);
            }
            return p;
        }

        /// <summary>How far forward of a chair's middle one sits (the hips; the legs hang over its front edge).</summary>
        public const double SeatForward = 0.25;

        /// <summary>How high a chair's seat is: its selection box's top, at most half a block (a chair's back stands over it).</summary>
        static double SeatHeight(PiecePlan p)
        {
            double top = 0;
            foreach (var s in p.Sel) top = Math.Max(top, s[4] - p.Mount.Y);
            return Math.Clamp(top, 0.3, 0.5);
        }

        /// <summary>A vanilla bed: its feet on the place, its head a block on (away from the one putting it), each its own block; side = toward them.</summary>
        static PiecePlan Bed(MountLayout layout, MountPoint m, PieceBlock b, PiecePlan p, int look)
        {
            var head = layout.FloorAt(m.Deck, m.I + MountLayout.DX[look], m.K + MountLayout.DZ[look], m.Level);
            if (head == null) { p.Error = "ships:piece-bed-room"; return p; }
            string side = MountLayout.DirCode[p.Dir];
            string feetCode = b.BedPart("feet", side) ?? b.Code, headCode = b.BedPart("head", side) ?? b.Code;
            p.Mounts.Add(m.Id);
            p.Mounts.Add(head.Id);
            var fo = new[] { m.X, m.Y, m.Z };
            var ho = new[] { head.X, head.Y, head.Z };
            p.Parts.Add(new PiecePart { Block = feetCode, Origin = fo });
            p.Parts.Add(new PiecePart { Block = headCode, Origin = ho });
            AddBoxes(p, b, feetCode, 0, fo);
            AddBoxes(p, b, headCode, 0, ho);
            Finish(p, b);
            p.Role = PieceRole.Bed;
            // on the mattress between the two blocks, the head the far way
            p.Seat = new[] { (m.X + head.X) / 2 + 0.5, m.Y, (m.Z + head.Z) / 2 + 0.5 };
            p.SeatYaw = ShipMounts.YawOfDir(look);
            return p;
        }

        static PiecePlan Wall(MountLayout layout, MountPoint m, PieceBlock b, PiecePlan p)
        {
            int toWall = (m.In + 2) % 4;
            string variant = b.Orient switch
            {
                PieceOrient.Attachable => b.Variant(MountLayout.DirCode[toWall]),
                PieceOrient.Omni when b.OmniWalls => b.Variant(MountLayout.DirCode[m.In]),
                PieceOrient.Orientable when !b.Container => b.Variant(MountLayout.DirCode[toWall]),
                // a block with no variant for it (an antler mount, a scroll rack): its front turned into the room
                PieceOrient.None when !b.Container => b.Code,
                _ => null
            };
            int q = b.Orient == PieceOrient.None ? QuarterFor(m.In) : 0;
            if (variant == null) { p.Error = "ships:piece-not-wall"; return p; }
            // the block against the wall: its wall side on the wall's face
            double[] origin = m.In switch
            {
                0 => new[] { m.X, m.Y, m.Z - 0.5 },
                1 => new[] { m.X - 0.5, m.Y, m.Z },
                2 => new[] { m.X - 1, m.Y, m.Z - 0.5 },
                _ => new[] { m.X - 0.5, m.Y, m.Z - 1 }
            };
            p.Dir = m.In;
            p.Parts.Add(new PiecePart { Block = variant, Origin = origin, Quarter = q });
            bool alongX = m.In == 1 || m.In == 3;
            foreach (var c0 in b.Cells(variant) ?? new List<int[]> { new[] { 0, 0, 0 } })
            {
                var (tx, tz) = b.Orient == PieceOrient.None ? TurnCell(c0[0], c0[2], q) : (c0[0], c0[2]);
                var c = new[] { tx, c0[1], tz };
                int along = alongX ? c[0] : c[2], depth = alongX ? c[2] : c[0];
                if (depth != 0 || c[1] < 0) { p.Error = "ships:piece-no-room"; return p; }
                var at = layout.WallAt(m.Wall, m.I + along, m.Row + c[1]);
                if (at == null) { p.Error = "ships:piece-wall-room"; return p; }
                if (!p.Mounts.Contains(at.Id)) p.Mounts.Add(at.Id);
                AddBoxes(p, b, variant, q, new[] { origin[0] + c[0], origin[1] + c[1], origin[2] + c[2] });
            }
            Finish(p, b);
            if (p.Role is PieceRole.Seat or PieceRole.Bed) p.Role = PieceRole.Deco;
            p.Pivot = new[] { m.X, m.Y, m.Z };
            p.Tilt = m.Normal;
            return p;
        }

        /// <summary>(0.1.0-dev.9, caravel note 78) A hook's kind that takes a figurehead: a vanilla one, or a chiseled block.</summary>
        public const string FigureheadKind = "figurehead";
        /// <summary>A figurehead's hang (<see cref="ShipPiece.Hang"/>): it stands in its block on the hook, facing the hook's normal, its back at the hook.</summary>
        public const string FigureheadHang = "figurehead";

        static PiecePlan Hook(MountPoint m, PieceBlock b, PiecePlan p)
        {
            // ((0.1.0-dev.9, caravel note 78: "we could have option to add vanilla figure heads") a figurehead's place at her stem
            // head: a figurehead only, standing in its block in front of the hook, facing the hook's way (the bow), its back at it
            if (m.Kinds != null && Array.IndexOf(m.Kinds, FigureheadKind) >= 0) return FigureheadPlan(m, b, p);
            if (m.Kinds != null && Array.IndexOf(m.Kinds, "light") >= 0 && m.Kinds.Length == 1 && !b.Light) { p.Error = "ships:piece-hook-light"; return p; }
            if (b.Orient is PieceOrient.Attachable or PieceOrient.Bed || b.Container) { p.Error = "ships:piece-hook-light"; return p; }
            bool down = m.Hang != "up";
            if (m.Yaw.HasValue) p.Dir = ShipMounts.DirOfYaw(m.Yaw.Value);
            string variant = b.Orient == PieceOrient.Omni ? b.Variant(down ? "down" : "up") ?? b.Code : b.Code;
            int q = b.Orient == PieceOrient.None || b.Light ? QuarterFor(p.Dir) : 0;
            var origin = new[] { m.X - 0.5, down ? m.Y - 1 : m.Y, m.Z - 0.5 };
            p.Parts.Add(new PiecePart { Block = variant, Origin = origin, Quarter = q });
            p.Mounts.Add(m.Id);
            p.Hang = down ? "down" : "up";
            AddBoxes(p, b, variant, q, origin);
            // The renderer hangs the visible mesh by its bounds, not by the surrounding unit cube.
            var bounds=Turn(b.BoundsOf(variant) ?? Union(b.Boxes(variant,false)) ?? new double[] {0,0,0,1,1,1},q);
            var offset=new[] { m.X-(bounds[0]+bounds[3])/2,m.Y-(down?bounds[4]:bounds[1]),m.Z-(bounds[2]+bounds[5])/2 };
            p.Parts[0].Origin=offset;
            PieceShape.FitVolumes(p,b.VolumesOf(variant),offset,q);
            p.Sel.Clear();
            p.Sel.Add(Offset(bounds,offset));
            // (hanging overhead: not solid)
            p.Solids.Clear();
            Finish(p, b);
            if (p.Role != PieceRole.Light) p.Role = PieceRole.Deco;
            return p;
        }

        /// <summary>
        /// (0.1.0-dev.9, caravel note 78) A figurehead on its place: vanilla's figureheads face +z in their shape, their back at
        /// z 0 (as the sailboat carries them), so the block is turned to the hook's normal (level; none: the bow, +x) and stands
        /// in the block in front of the hook, its foot level with it. Not solid (out over the water at her stem).
        /// </summary>
        static PiecePlan FigureheadPlan(MountPoint m, PieceBlock b, PiecePlan p)
        {
            if (!b.Figurehead) { p.Error = "ships:piece-hook-figurehead"; return p; }
            double nx = m.Normal?.Length >= 3 ? m.Normal[0] : 1, nz = m.Normal?.Length >= 3 ? m.Normal[2] : 0;
            // the quarter turn that takes the shape's +z nearest the normal (Turn: 1 → +x, 2 → -z, 3 → -x)
            int q = Math.Abs(nx) >= Math.Abs(nz) ? (nx >= 0 ? 1 : 3) : (nz >= 0 ? 0 : 2);
            double fx = q == 1 ? 1 : q == 3 ? -1 : 0, fz = q == 0 ? 1 : q == 2 ? -1 : 0;
            var bounds=Turn(b.VisualBounds ?? new double[] {0,0,0,1,1,1},q);
            var origin = new[] {
                m.X-(fx>0?bounds[0]:fx<0?bounds[3]:(bounds[0]+bounds[3])/2),
                m.Y-bounds[1],
                m.Z-(fz>0?bounds[2]:fz<0?bounds[5]:(bounds[2]+bounds[5])/2) };
            p.Parts.Add(new PiecePart { Block = b.Code, Origin = origin, Quarter = q });
            p.Mounts.Add(m.Id);
            p.Hang = FigureheadHang;
            p.Dir=MountLayout.Snap(fx,fz);
            p.Pivot=new[]{m.X,m.Y,m.Z};
            p.Tilt=m.Normal;
            PieceShape.FitVolumes(p,b.VolumesOf(b.Code),origin,q);
            AddBoxes(p, b, b.Code, q, origin);
            p.Sel.Clear();
            var tilt=PieceShape.Tilt(p.Pivot,p.Tilt,p.Dir);
            if(p.Volumes.Count>0)
                p.Sel.AddRange(p.Volumes.Select(v=>PlacementMesh.Bounds(PlacementMesh.Corners(v.Box).Select(c=>PlacementMesh.Transform(v.Pose,c)))));
            else {
                var box=Offset(bounds,origin);
                p.Sel.Add(tilt==null?box:PlacementMesh.Bounds(PlacementMesh.Corners(box).Select(c=>PlacementMesh.Transform(tilt,c))));
            }
            p.Solids.Clear();
            Finish(p, b);
            p.Role = PieceRole.Deco;
            return p;
        }

        static void AddBoxes(PiecePlan p, PieceBlock b, string variant, int q, double[] cell)
        {
            if (b.Overlay)
            {
                // a layer on the wall's face (its back, unturned -z): picked there, not solid
                p.Sel.Add(Offset(Turn(new[] { 0, 0, 0, 1, 1, 0.03 }, q), cell));
                return;
            }
            var sel = b.Boxes(variant, false);
            if (sel == null || sel.Length == 0) sel = new[] { new double[] { 0, 0, 0, 1, 1, 1 } };
            foreach (var s in sel) p.Sel.Add(Offset(Turn(s, q), cell));
            var col = b.Boxes(variant, true);
            if (col != null) foreach (var s in col) p.Solids.Add(Offset(Turn(s, q), cell));
        }

        static double[] Offset(double[] b, double[] o) => new[] { b[0] + o[0], b[1] + o[1], b[2] + o[2], b[3] + o[0], b[4] + o[1], b[5] + o[2] };

        static void Finish(PiecePlan p, PieceBlock b)
        {
            p.Box = Union(p.Sel);
        }

        public static double[] Union(IEnumerable<double[]> boxes)
        {
            double[] u = null;
            foreach (var s in boxes)
            {
                if (u == null) { u = (double[])s.Clone(); continue; }
                for (int a = 0; a < 3; a++) { u[a] = Math.Min(u[a], s[a]); u[a + 3] = Math.Max(u[a + 3], s[a + 3]); }
            }
            return u;
        }

        /// <summary>The blocks a floor piece takes (its places, and the blocks over them it reaches into), as one box; null if none.</summary>
        public static double[] PlaceBounds(MountLayout layout, PiecePlan plan)
        {
            double[] b = null;
            void Add(double[] x)
            {
                if (x == null || x.Length < 6) return;
                if (b == null) { b = (double[])x.Clone(); return; }
                for (int a = 0; a < 3; a++) { b[a] = Math.Min(b[a], x[a]); b[a + 3] = Math.Max(b[a + 3], x[a + 3]); }
            }
            foreach (string mid in plan.Mounts) Add(layout?.Get(mid)?.Box);
            foreach (var r in plan.Room) Add(r);
            return b;
        }

        /// <summary>A box cut to bounds (null: as it is), or null if nothing of it is left.</summary>
        public static double[] ClipTo(double[] s0, double[] cells)
        {
            if (cells == null) return s0;
            var s = new[] { Math.Max(s0[0], cells[0]), Math.Max(s0[1], cells[1]), Math.Max(s0[2], cells[2]), Math.Min(s0[3], cells[3]), Math.Min(s0[4], cells[4]), Math.Min(s0[5], cells[5]) };
            return s[3] <= s[0] || s[4] <= s[1] || s[5] <= s[2] ? null : s;
        }

        /// <summary>
        /// The first of a planned piece's boxes that runs into her collision (the grid it's put against: her design, her
        /// fittings and the pieces not walls), or null: its solid boxes and the room it needs, on floor places only within
        /// its places' blocks (a lip over the edge touching her side is no clash: note 72). (0.3.18, note 162) A ladder-stair
        /// only its climber's room and its landings: its own boxes rest on her coaming at the top (a beam up to 0.75 under the
        /// deck along the well's ends, docs/modular-pieces.md §7.1). The server's placing and the offline tests alike.
        /// </summary>
        public static double[] GridClash(MountLayout layout, PiecePlan plan, VoxelGrid grid) => GridClash(layout, plan, grid, null);

        /// <summary>(0.3.27) As <see cref="GridClash(MountLayout, PiecePlan, VoxelGrid)"/>, her collision's voxels in these boxes left out (beam segments that would come out for it).</summary>
        public static double[] GridClash(MountLayout layout, PiecePlan plan, VoxelGrid grid, List<double[]> ignore)
        {
            if (plan == null || grid == null) return null;
            double[] cells = plan.Kind == MountKind.Floor ? PlaceBounds(layout, plan) : null;
            // (0.3.19) a walked stair: its room and its treads but the top one (flush with the deck above, under its edge)
            // ((0.3.23) its steps only, not its handrails' guards beside it)
            IEnumerable<double[]> steps = null;
            if (plan.Role == PieceRole.Stair && plan.Mount != null)
            {
                var fb = FlightBoxes(plan.Mount, plan.Dir, plan.Rise, plan.Cells > 0 ? plan.Cells : FlightCells);
                steps = fb.Take(Math.Max(0, fb.Count - 1));
            }
            var boxes = plan.Role == PieceRole.Ladder ? plan.Room : plan.Role == PieceRole.Stair ? (steps ?? plan.Solids.Take(Math.Max(0, plan.Solids.Count - 1))).Concat(plan.Room) : plan.Solids.Concat(plan.Sel).Concat(plan.Room);
            // (a stair's two landings, its room's last boxes: stepped onto over a coaming or a sill)
            var landings = plan.Role == PieceRole.Stair && plan.Room.Count >= 2 ? plan.Room.Skip(plan.Room.Count - 2).ToList() : null;
            foreach (var s0 in boxes)
            {
                var s = ClipTo(s0, cells);
                if (s == null) continue;
                if (landings != null && landings.Contains(s0)) {if (StairLanding.Blocked(s, grid, ignore)) return s; continue;}
                if (ignore == null || ignore.Count == 0 ? grid.Collides(s[0] + 0.03, s[1] + 0.03, s[2] + 0.03, s[3] - 0.03, s[4] - 0.03, s[5] - 0.03)
                                                       : CollidesExcept(grid, new[] { s[0] + 0.03, s[1] + 0.03, s[2] + 0.03, s[3] - 0.03, s[4] - 0.03, s[5] - 0.03 }, ignore)) return s;
            }
            return null;
        }

        /// <summary>(0.3.27) Is any of her collision's voxels in the box, but those whose middles are in one of the ignored boxes?</summary>
        public static bool CollidesExcept(VoxelGrid g, double[] b, List<double[]> ignore)
        {
            double v = g.V;
            for (double x = g.OX + (Math.Floor((b[0] - g.OX) / v) + 0.5) * v; x < b[3]; x += v)
                for (double y = g.OY + (Math.Floor((b[1] - g.OY) / v) + 0.5) * v; y < b[4]; y += v)
                    for (double z = g.OZ + (Math.Floor((b[2] - g.OZ) / v) + 0.5) * v; z < b[5]; z += v)
                    {
                        if (!g.SolidAt(x, y, z)) continue;
                        bool skip = false;
                        foreach (var i in ignore)
                            if (x > i[0] && x < i[3] && y > i[1] && y < i[4] && z > i[2] && z < i[5]) { skip = true; break; }
                        if (!skip) return true;
                    }
            return false;
        }

        public static bool Overlap(double[] a, double[] b, double eps = 0.01) =>
            a[0] < b[3] - eps && a[3] > b[0] + eps && a[1] < b[4] - eps && a[4] > b[1] + eps && a[2] < b[5] - eps && a[5] > b[2] + eps;
    }

    /// <summary>A vanilla block put aboard (saved with her in WatchedAttributes "hansaPieces", one tree per piece).</summary>
    public class ShipPiece
    {
        public int Id;
        /// <summary>What was put there (one), given back when it's taken up.</summary>
        public ItemStack Stack;
        public MountKind Kind;
        public string Mount;
        public string[] Mounts = Array.Empty<string>();
        public int Dir;
        public PieceRole Role;
        public List<PiecePart> Parts = new();
        public List<double[]> Sel = new(), Solids = new();
        public double[] Box;
        public double[] Pivot, Tilt;
        public string Hang;
        public double[] Seat;
        public float SeatYaw;
        public int Slots;
        public float Efficiency;
        /// <summary>A door: standing open (saved; shut it's solid and closes its room).</summary>
        public bool Open;
        public double Coaming;
        public bool DoubleLeaf;
        /// <summary>(0.3.12) A wall put aboard painted: a vanilla dye's colour, or null as it is (saved).</summary>
        public string Paint;
        /// <summary>(0.3.28, step D2) What shots did to it (a partition, a door, a window, a railing: 0 sound .. Holed shot away; saved).</summary>
        public float Hurt;

        /// <summary>(0.3.12) A wall put aboard (a partition, a panel, a door) is painted like her own surfaces.</summary>
        public bool Paintable => Role is PieceRole.Partition or PieceRole.Panel or PieceRole.Door or PieceRole.Window or PieceRole.Railing or PieceRole.Ladder or PieceRole.Stair or PieceRole.Shelf;
        /// <summary>(0.3.16) A stair's rise; a railing beside a stair: its rake (how high the treads are at its two ends, along its line: -x/-z end first), null flat.</summary>
        public double Rise;
        public double[] Rake;
        /// <summary>(0.3.23) A stair's cells along its run (0: saved before 0.3.23, 3) and its own handrails (a ship's stair: no railings beside it).</summary>
        public int Cells;
        public bool Rails;
        /// <summary>(0.3.16) A piece on a cell's border (a partition, a door, a window, a railing).</summary>
        public bool OnBorder => Role is PieceRole.Partition or PieceRole.Door or PieceRole.Window or PieceRole.Railing;
        /// <summary>(0.3.16) Walls of a sort, solid after her place grid is taken (pieces go right up to them): border pieces, stairs, shelves.</summary>
        public bool WallLike => OnBorder || Role is PieceRole.Ladder or PieceRole.Stair or PieceRole.Shelf;
        /// <summary>
        /// Selection bounds. Hatch rays test the actual oriented leaf elements; their walking voxels and
        /// fixed coaming never become selectable moving leaves, including when aimed at from below.
        /// </summary>
        /// <remarks>((24 October 2026, caravel note 82: "then i cannot remove it") a panel by its whole wedge too: aimed at its
        /// end or its top (her side's curve leaves them showing) it's the panel, not her woodwork behind)</remarks>
        public IEnumerable<double[]> PickBoxes => Role == PieceRole.Hatch ? PlacementBoxes
            : Role == PieceRole.Panel ? Sel.Concat(Solids)
            : Role == PieceRole.Door && Open && OpenLeafBox is double[] leaf ? new[] {leaf} : Sel;

        public double DoorAngle => Role != PieceRole.Door || Box == null ? 0 :
            ((Box[3]-Box[0]>Box[5]-Box[2] ? -MountLayout.DZ[Dir] : MountLayout.DX[Dir]) *
            Math.PI/2 * ModularGeometry.DoorOpenFraction * (HingeHighEnd ? -1 : 1));

        public double[] DoorPoint(double[] point, bool inverse = false)
        {
            double a=DoorAngle*(inverse?-1:1),c=Math.Cos(a),s=Math.Sin(a);
            var hinge=DoorHinge;
            double x=point[0]-hinge[0],z=point[2]-hinge[2];
            return new[]{hinge[0]+c*x+s*z,point[1],hinge[2]-s*x+c*z};
        }

        // The hinge pin lies on the opening face of the leaf. Turning about its centre plane drove the
        // leaf's thickness through the adjoining partition at the hinge, including its bottom face.
        public double[] DoorHinge => Pivot==null || Box==null ? Pivot : new[]{
            Pivot[0]+MountLayout.DX[Dir]*Math.Max(.1875,Math.Min(Box[3]-Box[0],Box[5]-Box[2]))/2,
            Pivot[1],Pivot[2]+MountLayout.DZ[Dir]*Math.Max(.1875,Math.Min(Box[3]-Box[0],Box[5]-Box[2]))/2};

        /// <summary>
        /// (0.3.30, note 243: "check vanilla door closing mechanics. My mind can close pointing at open door and not only on
        /// where door was while closed") An open door's leaf as it stands swung into the cell it opens into (the renderer's
        /// <see cref="ModularGeometry.DoorOpenFraction"/> of a quarter turn about its hinge), a box round it: picked as the
        /// doorway is, so right-clicking the open leaf shuts it as vanilla's does. Null: not a door, or no hinge.
        /// </summary>
        public double[] OpenLeafBox
        {
            get
            {
                if (Role != PieceRole.Door || Pivot == null || Box == null || Dir < 0 || Dir > 3) return null;
                return PlacementMesh.Bounds(PlacementMesh.Corners(Box).Select(p=>DoorPoint(p)));
            }
        }
        /// <summary>
        /// (1 November 2026, the user: "Cannot close door from outside ships cabin") An open door seen from (x, z), on the side
        /// its leaf swings away from: the leaf lies back against the wall beyond, hidden behind it, so there its doorway is
        /// picked and seen as the door (vanilla's leaf stands out across its doorway and is seen from both sides).
        /// </summary>
        public bool DoorwayFrom(double x, double z)
        {
            if (OpenLeafBox is not double[] leaf || !Open) return false;
            bool alongX = Box[3] - Box[0] > Box[5] - Box[2];
            double mid = alongX ? (Box[2] + Box[5]) / 2 : (Box[0] + Box[3]) / 2;
            double leafMid = alongX ? (leaf[2] + leaf[5]) / 2 : (leaf[0] + leaf[3]) / 2;
            return ((alongX ? z : x) - mid) * (leafMid - mid) < 0;
        }

        /// <summary>(25 October 2026, note 89) A door hung at its border's +x/+z end (its hinge past the middle of its box).</summary>
        public bool HingeHighEnd => Role == PieceRole.Door && Pivot != null && Box != null &&
                                    (Box[3] - Box[0] > Box[5] - Box[2] ? Pivot[0] > (Box[0] + Box[3]) / 2 : Pivot[2] > (Box[2] + Box[5]) / 2);

        /// <summary>(0.3.23) The box that must be seen to use or take it (a shut hatch's from below too).</summary>
        public double[] SeenBox => Role == PieceRole.Door && Open ? OpenLeafBox ?? Box
            : Role == PieceRole.Hatch ? PiecePlanner.Union(PickBoxes) ?? Box : Box;

        /// <summary>(0.3.19) A ladder-stair or a walked stair.</summary>
        public bool IsStair => Role is PieceRole.Ladder or PieceRole.Stair;

        public IEnumerable<double[]> PlacementBoxes
        {
            get
            {
                if(Role==PieceRole.Door && Open && OpenLeafBox is double[] door) return new[]{door};
                if(Role!=PieceRole.Hatch || !Open) return Sel;
                return HatchPlanner.Leaves(Box,Dir,DoubleLeaf).Select(leaf=>{
                    var part=new MovingPartDef { AxisPoint=leaf.Hinge,AxisDir=leaf.Axis==0?new double[]{1,0,0}:new double[]{0,0,1},Angle=leaf.Sign*HatchPlanner.OpenAngle };
                    var matrix=new float[16];PartMotion.Place(part,1,matrix,null);
                    return PlacementMesh.Bounds(PlacementMesh.Corners(leaf.Box).Select(p=>PlacementMesh.Transform(matrix,p)));
                });
            }
        }

        List<PieceVolume> stairPlacement;
        public IEnumerable<PieceVolume> PlacementVolumes => Role==PieceRole.Stair && Parts.Count>0
            ? stairPlacement??=PieceShape.StairVolumes(new MountPoint {X=Parts[0].Origin[0],Y=Parts[0].Origin[1],Z=Parts[0].Origin[2]},Dir,Rise,Cells,Rails).ToList()
            : PlacementBoxes.Select(b=>new PieceVolume {Box=b});

        /// <summary>The area (m²) painting it covers: a partition's and a door's two faces, a panel's one, by their height.</summary>
        public double PaintArea
        {
            get
            {
                double h = Box == null ? 1 : Math.Max(0.25, Box[4] - Box[1]);
                return Role switch
                {
                    PieceRole.Partition or PieceRole.Door => 2 * h, PieceRole.Panel => h,
                    PieceRole.Window => 0.5, PieceRole.Railing => 1.0, PieceRole.Ladder => 3.0, PieceRole.Stair => 4.0, PieceRole.Shelf => 1.0, _ => 0
                };
            }
        }

        /// <summary>Its storage's key among the ship's storages (and in "hansaInv"), its seat's.</summary>
        public string StorageCode => "piece-" + Id;
        public string SeatCode => "piece-" + Id;

        public double[] Center => Box == null ? new[] { 0.0, 0, 0 } : new[] { (Box[0] + Box[3]) / 2, (Box[1] + Box[4]) / 2, (Box[2] + Box[5]) / 2 };

        public static ShipPiece From(PiecePlan p, int id, ItemStack stack)
        {
            var one = stack?.Clone();
            if (one != null) one.StackSize = 1;
            return new ShipPiece
            {
                Id = id, Stack = one, Kind = p.Kind, Mount = p.Mount?.Id, Mounts = p.Mounts.ToArray(), Dir = p.Dir, Role = p.Role,
                Parts = p.Parts.Select(x => new PiecePart { Block = x.Block, Origin = (double[])x.Origin.Clone(), Quarter = x.Quarter }).ToList(),
                Sel = p.Sel.Select(x => (double[])x.Clone()).ToList(), Solids = p.Solids.Select(x => (double[])x.Clone()).ToList(),
                Box = (double[])p.Box?.Clone(), Pivot = p.Pivot, Tilt = p.Tilt, Hang = p.Hang, Seat = p.Seat, SeatYaw = p.SeatYaw,
                Slots = p.Slots, Efficiency = p.Efficiency, Rise = p.Rise, Cells = p.Cells, Rails = p.Rails,
                Coaming = p.Coaming, DoubleLeaf = p.DoubleLeaf
            };
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public void ToTree(ITreeAttribute t)
        {
            t.SetInt("id", Id);
            if (Stack != null) t.SetItemstack("stack", Stack);
            t.SetInt("kind", (int)Kind);
            t.SetString("mount", Mount ?? "");
            t.SetString("mounts", string.Join(",", Mounts));
            t.SetInt("dir", Dir);
            t.SetInt("role", (int)Role);
            t.SetString("parts", string.Join(";", Parts.Select(x => string.Join("|", x.Block, F(x.Origin[0]), F(x.Origin[1]), F(x.Origin[2]), x.Quarter.ToString(Inv)))));
            t["sel"] = new DoubleArrayAttribute(Sel.SelectMany(x => x).ToArray());
            t["solids"] = new DoubleArrayAttribute(Solids.SelectMany(x => x).ToArray());
            if (Box != null) t["box"] = new DoubleArrayAttribute(Box);
            if (Pivot != null) t["pivot"] = new DoubleArrayAttribute(Pivot);
            if (Tilt != null) t["tilt"] = new DoubleArrayAttribute(Tilt);
            if (Seat != null) t["seat"] = new DoubleArrayAttribute(Seat);
            if (Hang != null) t.SetString("hang", Hang);
            t.SetFloat("seatYaw", SeatYaw);
            t.SetInt("slots", Slots);
            t.SetFloat("eff", Efficiency);
            if (Open) t.SetBool("open", true);
            if (Paint != null) t.SetString("paint", Paint);
            if (Rise > 0) t.SetDouble("rise", Rise);
            if (Rake != null) t["rake"] = new DoubleArrayAttribute(Rake);
            if (Cells > 0) t.SetInt("cells", Cells);
            if (Rails) t.SetBool("rails", true);
            if (Coaming > 0) t.SetDouble("coaming", Coaming);
            if (DoubleLeaf) t.SetBool("doubleLeaf", true);
            if (Hurt > 0) t.SetFloat("hurt", Hurt);
        }

        static string F(double v) => v.ToString("R", Inv);

        public static ShipPiece FromTree(ITreeAttribute t)
        {
            var p = new ShipPiece
            {
                Id = t.GetInt("id"), Stack = t.GetItemstack("stack"), Kind = (MountKind)t.GetInt("kind"), Mount = t.GetString("mount"),
                Mounts = (t.GetString("mounts") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries), Dir = t.GetInt("dir"),
                Role = (PieceRole)t.GetInt("role"), Hang = t.GetString("hang"), SeatYaw = t.GetFloat("seatYaw"), Slots = t.GetInt("slots"),
                Efficiency = t.GetFloat("eff", 0.7f), Open = t.GetBool("open"), Paint = ShipPaint.Known(t.GetString("paint")) ? t.GetString("paint") : null,
                Box = (t["box"] as DoubleArrayAttribute)?.value, Pivot = (t["pivot"] as DoubleArrayAttribute)?.value,
                Tilt = (t["tilt"] as DoubleArrayAttribute)?.value, Seat = (t["seat"] as DoubleArrayAttribute)?.value,
                Rise = t.GetDouble("rise"), Rake = (t["rake"] as DoubleArrayAttribute)?.value, Cells = t.GetInt("cells"), Rails = t.GetBool("rails"),
                Hurt = Math.Clamp(t.GetFloat("hurt"), 0, 1)
            };
            p.Coaming = t.GetDouble("coaming"); p.DoubleLeaf = t.GetBool("doubleLeaf");
            foreach (string part in (t.GetString("parts") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = part.Split('|');
                if (s.Length < 5) continue;
                p.Parts.Add(new PiecePart
                {
                    Block = s[0], Origin = new[] { double.Parse(s[1], Inv), double.Parse(s[2], Inv), double.Parse(s[3], Inv) },
                    Quarter = int.Parse(s[4], Inv)
                });
            }
            p.Sel = Boxes(t["sel"] as DoubleArrayAttribute);
            p.Solids = Boxes(t["solids"] as DoubleArrayAttribute);
            p.Box ??= PiecePlanner.Union(p.Sel);
            return p;
        }

        static List<double[]> Boxes(DoubleArrayAttribute a)
        {
            var list = new List<double[]>();
            if (a?.value == null) return list;
            for (int i = 0; i + 5 < a.value.Length; i += 6) list.Add(a.value.Skip(i).Take(6).ToArray());
            return list;
        }
    }

    /// <summary>The pieces on a ship: by id, which mount point each takes, saved and loaded as one tree.</summary>
    public class ShipPieceSet
    {
        public readonly SortedDictionary<int, ShipPiece> ById = new();
        readonly Dictionary<string, int> taken = new();
        public int NextId = 1;

        public int Count => ById.Count;
        public IEnumerable<ShipPiece> All => ById.Values;

        public ShipPiece Get(int id) => ById.TryGetValue(id, out var p) ? p : null;
        public ShipPiece At(string mount) => mount != null && taken.TryGetValue(mount, out int id) ? Get(id) : null;
        public bool Taken(string mount) => mount != null && taken.ContainsKey(mount);

        public void Add(ShipPiece p)
        {
            if (p.Id <= 0) p.Id = NextId;
            NextId = Math.Max(NextId, p.Id + 1);
            ById[p.Id] = p;
            foreach (string m in p.Mounts) taken[m] = p.Id;
        }

        public bool Remove(int id)
        {
            if (!ById.TryGetValue(id, out var p)) return false;
            ById.Remove(id);
            foreach (string m in p.Mounts) if (taken.TryGetValue(m, out int t) && t == id) taken.Remove(m);
            return true;
        }

        public void Clear()
        {
            ById.Clear();
            taken.Clear();
        }

        /// <summary>Does a plan's footprint run into a piece already there (a mount point it takes, or a box)?</summary>
        public ShipPiece Blocking(PiecePlan plan)
        {
            foreach (string m in plan.Mounts) if (At(m) is ShipPiece p) return p;
            var volumes=plan.PlacementVolumes;
            foreach (var p in ById.Values)
            {
                // (a hatch shuts over a ladder's way up, its top step in the opening: the climber opens it, note 169)
                if (plan.Role == PieceRole.Hatch && p.Role == PieceRole.Ladder) continue;
                // A hatch can shut over the last nosing of a stair coming up through its opening, just as over
                // a ladder. Rails or a stair continuing above this deck must still prevent it shutting.
                if (plan.Role == PieceRole.Hatch && p.Role == PieceRole.Stair && plan.Box != null &&
                    p.PlacementVolumes.All(v => v.Bounds[4] <= plan.Box[4] + 0.01)) continue;
                if (ModularGeometry.FittedContact(plan,p)) continue;
                foreach (var a in volumes)
                    foreach (var b in p.PlacementVolumes)
                        if(PieceShape.Overlap(a,b)) return p;
            }
            return null;
        }

        /// <summary>
        /// Does a piece stand on the floor place under a stacked place, with a top to put something on (a chest, a crate, a
        /// cabinet: solid up to at least three quarters of the block, over at least half of it each way)? A place on the deck
        /// always holds. As vanilla stacks blocks, what's put there stands a whole block up whatever the top's height.
        /// (0.3.12, note 124: it was 0.7 each way, and vanilla's cabinet is 0.6875 deep: nothing could go on a cabinet.)
        /// </summary>
        public bool Supports(MountLayout layout, MountPoint m)
        {
            if (m == null) return true;
            // (0.3.16, note 160: "cannot remove bottom partition wall if top is still on") a border piece hangs from the beam
            // or deck over it: it needs nothing under it
            if (m.Kind == MountKind.Border) return true;
            // (0.3.16, note 159) a stacked place over a horizontal plank panel (a shelf) stands on that
            if (m.Kind == MountKind.Floor && m.Level > 0 && OnShelf(layout, m)) return true;
            if (m.Under == null) return true;
            var under = layout?.Get(m.Under);
            var p = At(m.Under);
            // a panel's wall spot, a panel over another, a partition over another: that one up
            // (0.3.14, note 147: "above doors cannot place partition wall fragments") a partition over a door's upper half
            // (0.3.16, note 159) a partition's wall spot: the partition
            if (m.Kind == MountKind.Wall && under?.Kind == MountKind.Border) return p != null && p.Role == PieceRole.Partition;
            // (0.3.18) a flue segment on the hearth, or on the segment that came up through the deck it rises from
            if (m.Kind == MountKind.Flue) return p != null && p.Role is PieceRole.Hearth or PieceRole.Flue;
            if (m.Kind != MountKind.Floor) return p != null && p.Role == PieceRole.Panel;
            if (under == null || p == null || p.Kind != MountKind.Floor) return false;
            // (nothing stands on a fire, an oven or a flue)
            if (p.Role is PieceRole.Hearth or PieceRole.Oven or PieceRole.Flue) return false;
            double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue, top = double.MinValue;
            foreach (var s in p.Solids)
            {
                // (its boxes in that block)
                if (s[3] <= under.X + 0.01 || s[0] >= under.X + 0.99 || s[5] <= under.Z + 0.01 || s[2] >= under.Z + 0.99 || s[4] <= under.Y + 0.01 || s[1] >= under.Y + 0.99) continue;
                x0 = Math.Min(x0, Math.Max(s[0], under.X)); x1 = Math.Max(x1, Math.Min(s[3], under.X + 1));
                z0 = Math.Min(z0, Math.Max(s[2], under.Z)); z1 = Math.Max(z1, Math.Min(s[5], under.Z + 1));
                top = Math.Max(top, s[4]);
            }
            return top >= under.Y + 0.75 - 1e-6 && x1 - x0 >= MinTop - 1e-6 && z1 - z0 >= MinTop - 1e-6;
        }

        /// <summary>(0.3.16) A horizontal plank panel at the foot of a stacked floor place (the shelf it stands on).</summary>
        public bool OnShelf(MountLayout layout, MountPoint m) =>
            m?.Kind == MountKind.Floor && m.Level > 0 && layout?.HorizontalAt(m.Deck, m.I, m.K, m.Level) is MountPoint h && At(h.Id)?.Role == PieceRole.Shelf;

        /// <summary>How much of a block (each way) a piece's top must cover to stand something on it.</summary>
        public const double MinTop = 0.5;

        /// <summary>
        /// A piece that needs this one (standing on it, hung on the panel it is, a panel over it), or null: it can't be
        /// taken away from under that.
        /// </summary>
        public ShipPiece StandingOn(MountLayout layout, ShipPiece p)
        {
            if (p == null || layout == null) return null;
            foreach (var q in ById.Values)
            {
                // (0.3.16: a stair takes the places over its cells without standing on what's under them; note 160: a
                // border piece hangs from the beam over it, not from the one under it)
                if (q.Id == p.Id || q.IsStair) continue;
                foreach (string id in q.Mounts)
                {
                    var qm = layout.Get(id);
                    if (qm == null || qm.Kind == MountKind.Border || OnShelf(layout, qm) && p.Role != PieceRole.Shelf) continue;
                    if (qm.Under is string under && Array.IndexOf(p.Mounts, under) >= 0) return q;
                }
            }
            // (0.3.16) a shelf with something standing on it
            if (p.Role == PieceRole.Shelf && p.Mounts.Length > 0 && layout.Get(p.Mounts[0]) is MountPoint hp &&
                At(layout.FloorAt(hp.Deck, hp.I, hp.K, hp.Level)?.Id) is ShipPiece onIt && onIt.Id != p.Id) return onIt;
            return null;
        }

        /// <summary>
        /// Is something at t along a ray (ship-local, d normalised) behind her own woodwork (her design's collision: hull,
        /// decks, bulkheads; not what's put aboard), seen from the ray's start? (The user, 0.3.9: "repair from outside ... was
        /// trying to open chests placed inside".)
        /// </summary>
        public static bool Behind(VoxelGrid design, Vintagestory.API.MathTools.Vec3d o, Vintagestory.API.MathTools.Vec3d d, double t, double tolerance = 0.02) =>
            design != null && design.Raycast(o, d, t + 1) < t - tolerance;

        /// <summary>
        /// Can a piece's or a place's box be seen from a point (ship-local): its middle or one of its corners (pulled in 0.1)
        /// not behind her design's woodwork (<see cref="Behind"/>; a little leeway: a box may reach into her planking).
        /// </summary>
        public static bool Seen(VoxelGrid design, Vintagestory.API.MathTools.Vec3d eye, double[] box)
        {
            if (design == null || box == null) return true;
            var pts = new List<double[]> { new[] { (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2 } };
            for (int n = 0; n < 8; n++)
                pts.Add(new[] { (n & 1) == 0 ? box[0] + 0.1 : box[3] - 0.1, (n & 2) == 0 ? box[1] + 0.1 : box[4] - 0.1, (n & 4) == 0 ? box[2] + 0.1 : box[5] - 0.1 });
            foreach (var q in pts)
            {
                var d = new Vintagestory.API.MathTools.Vec3d(q[0] - eye.X, q[1] - eye.Y, q[2] - eye.Z);
                double len = d.Length();
                if (len < 1e-6) return true;
                d.Mul(1 / len);
                double te = ShipFittings.RayBox(eye, d, new Vintagestory.API.MathTools.Cuboidd(box[0], box[1], box[2], box[3], box[4], box[5]));
                if (te < 0) te = len;
                if (!Behind(design, eye, d, te, 0.1)) return true;
            }
            return false;
        }

        /// <summary>Visibility of the leaves as drawn, allowing a hatch's own rounded walking collision.</summary>
        public static bool SeenPiece(VoxelGrid design, Vintagestory.API.MathTools.Vec3d eye, ShipPiece p)
        {
            if(p?.Role!=PieceRole.Hatch) return Seen(design,eye,p?.SeenBox) || p!=null && p.DoorwayFrom(eye.X,eye.Z) && Seen(design,eye,p.Box);
            if(design==null) return true;
            foreach(var b in p.PickBoxes) for(int k=-1;k<8;k++) {
                var q=new double[3];
                for(int a=0;a<3;a++) q[a]=b[a]+(b[a+3]-b[a])*(k<0?.5:(k&(1<<a))==0?.1:.9);
                var d=new Vintagestory.API.MathTools.Vec3d(q[0]-eye.X,q[1]-eye.Y,q[2]-eye.Z);
                double len=d.Length();if(len<1e-6) return true;d.Mul(1/len);
                if(!AimPlace.PieceFace(p,eye,d,len+1,out double t,out _)) continue;
                double wall=design.Raycast(eye,d,t+.1);
                if(wall>=t-.02 || HatchVoxelHit(p,design,eye,d,wall)) return true;
            }
            return false;
        }

        /// <summary>
        /// (0.3.24, note 177: "from this angle cannot open doors") Is what a ray met at t (her collision as it is, with the
        /// pieces' solids) this piece's own collision? A door's or a partition's solid layer lies a quarter block on one side
        /// of its line, where it's picked: seen slanting from that side the ray goes through the layer some way before the
        /// sheet, and its own layer mustn't hide it. (The point is taken a hundredth inside what was met.)
        /// </summary>
        public static bool OwnHit(ShipPiece p, Vintagestory.API.MathTools.Vec3d o, Vintagestory.API.MathTools.Vec3d d, double t)
        {
            if (p?.Solids == null || double.IsNaN(t) || double.IsInfinity(t)) return false;
            double x = o.X + d.X * (t + 0.01), y = o.Y + d.Y * (t + 0.01), z = o.Z + d.Z * (t + 0.01);
            const double e = 1e-3;
            foreach (var s in p.Solids)
                if (s?.Length >= 6 && x >= s[0] - e && x <= s[3] + e && y >= s[1] - e && y <= s[4] + e && z >= s[2] - e && z <= s[5] + e) return true;
            return false;
        }

        /// <summary>A raised hatch's walking voxels extend beyond its drawn boards. They must not hide its own leaves.</summary>
        public static bool HatchVoxelHit(ShipPiece p, VoxelGrid grid, Vintagestory.API.MathTools.Vec3d o, Vintagestory.API.MathTools.Vec3d d, double t)
        {
            if (p?.Role != PieceRole.Hatch || grid == null || !double.IsFinite(t) || t == double.MaxValue) return false;
            var boxes = HatchPlanner.CoamingParts(p.Box,p.Coaming).Select(c=>c.Box);
            if (!p.Open) boxes=boxes.Concat(p.Solids);
            var at=o.AddCopy(d.X*(t+.001),d.Y*(t+.001),d.Z*(t+.001));
            return boxes.Any(b=>grid.CellsOf(b) is double[] cell && AimPlace.BoxDistance(cell,at.X,at.Y,at.Z)<1e-6);
        }

        /// <summary>
        /// (25 October 2026, caravel notes 95, 96: "cannot remove partition wall as all the time snapbox jumps to the hull") The
        /// piece whose collision holds a point a hundredth past where a ray met her collision (o, d normalised, t), or null:
        /// what the ray met is that piece's body (a partition's quarter-block layer, a block's side), though its thin pick
        /// sheet is elsewhere.
        /// </summary>
        public ShipPiece SolidHit(Vintagestory.API.MathTools.Vec3d o, Vintagestory.API.MathTools.Vec3d d, double t)
        {
            if (double.IsNaN(t) || double.IsInfinity(t) || t == double.MaxValue) return null;
            foreach (var p in ById.Values)
                if (p.Role != PieceRole.Hatch && (p.Role != PieceRole.Door || !p.Open))
                    if (OwnHit(p, o, d, t)) return p;
            return null;
        }

        /// <summary>The piece a ray (ship-local, d normalised) meets first within range, and how far.</summary>
        public ShipPiece Pick(Vintagestory.API.MathTools.Vec3d o, Vintagestory.API.MathTools.Vec3d d, double range, out double dist)
        {
            dist = double.MaxValue;
            ShipPiece best = null;
            foreach (var p in ById.Values)
                if (AimPlace.PieceFace(p,o,d,range,out double t,out _) && t < dist) { dist=t; best=p; }
            return best;
        }

        /// <summary>
        /// (1 November 2026, chalk note 157 of 1 November: "when clicking in middle area of hatch then it does not register and open") The piece the
        /// crosshair is on (ship-local ray, d normalised) with her collision as it is (grid, the pieces' solids in it), and how
        /// far; wall: where the ray met her collision. The client's choice (DecoClient), shared so the offline tests replay it.
        /// </summary>
        public static ShipPiece Aimed(ShipPieceSet pieces, VoxelGrid grid, Vintagestory.API.MathTools.Vec3d o, Vintagestory.API.MathTools.Vec3d d, double range, out double tp, out double wall)
        {
            wall = grid.Raycast(o, d, range + 1);
            var piece = pieces.Pick(o, d, range, out tp);
            // (0.3.10: not behind her decks, sides or masts: "a reading of items below deck or through the mast". A
            // piece's own cells are in her grid, up to half a cell outside its box)
            // (0.3.12, note 123: a partition or a door put aboard is picked as a sheet on its line, its collision the quarter
            // block on one side of it: from that side the collision came first and a door opened only from the other)
            double slack = piece?.Kind == MountKind.Border ? 0.3 : 0.15;
            // (0.3.24, note 177: "from this angle cannot open doors") unless what the ray met first is the piece's own
            // collision (a door's quarter-block layer, seen slanting from its side)
            // ((1 November 2026, chalk note 157 of 1 November) or a shut hatch's own walking voxels: a quarter-block grid rounds its leaves' top (1.5) up to
            // 1.625, so from a standing eye the middle of the lid was dropped; the server (SeenPiece, PieceRay) already allowed them)
            if (piece != null && tp > wall + slack && !OwnHit(piece, o, d, wall) && !HatchVoxelHit(piece, grid, o, d, wall)) { piece = null; tp = double.MaxValue; }
            // ((25 October 2026, notes 95, 96) her collision met first is a piece's own body (its pick sheet elsewhere): that piece
            if ((piece == null || tp > wall + 0.02) && wall < range && pieces.SolidHit(o, d, wall) is ShipPiece body) { piece = body; tp = wall; }
            return piece;
        }

        public void ToTree(ITreeAttribute t)
        {
            t.SetInt("next", NextId);
            var list = new TreeAttribute();
            foreach (var p in ById.Values)
            {
                var pt = new TreeAttribute();
                p.ToTree(pt);
                list[p.Id.ToString(CultureInfo.InvariantCulture)] = pt;
            }
            t["pieces"] = list;
        }

        public void FromTree(ITreeAttribute t)
        {
            Clear();
            NextId = Math.Max(1, t?.GetInt("next", 1) ?? 1);
            if (t?.GetTreeAttribute("pieces") is not ITreeAttribute list) return;
            foreach (var kv in list)
            {
                if (kv.Value is not ITreeAttribute pt) continue;
                try { Add(ShipPiece.FromTree(pt)); }
                catch (FormatException) { }
            }
        }
    }

    /// <summary>
    /// The carrack's chest, cargo, bed and lantern places as 0.3.7 had them (ship-local feet, yaw): when a design no longer
    /// has one, what an older ship had on it becomes a piece on the nearest free place there (a chest or vessel with what's in
    /// it, a bed), a lantern on the hook of the same code (or the nearest).
    /// </summary>
    public static class LegacyPlaces
    {
        public static bool TryGet(string ship, string code, out double[] pos, out float yaw)
        {
            pos = null;
            yaw = 0;
            if (ship != "carrack" || code == null || !Carrack.TryGetValue(code, out var v)) return false;
            pos = new[] { v[0], v[1], v[2] };
            yaw = (float)v[3];
            return true;
        }

        static readonly Dictionary<string, double[]> Carrack = new()
        {
            ["lantern-lowerdeck-1"] = new[] { -11.375, 1.5, 1.2, 0 }, ["lantern-lowerdeck-2"] = new[] { -5.375, 1.5, -1.2, 0 },
            ["lantern-lowerdeck-3"] = new[] { 0.625, 1.5, 1.2, 0 }, ["lantern-lowerdeck-4"] = new[] { 6.625, 1.5, -1.2, 0 },
            ["lantern-lowerdeck-5"] = new[] { 12.625, 1.5, 0.0, 0 }, ["lantern-hold-1"] = new[] { -6.5, -1.25, 1.4, 0 },
            ["lantern-hold-2"] = new[] { -2.5, -1.25, -1.4, 0 }, ["lantern-hold-3"] = new[] { 6.5, -1.25, -1.4, 0 },
            ["lantern-greatcabin"] = new[] { -8.375, 4.5, 1.0, 0 }, ["lantern-poopcabin"] = new[] { -9.875, 7.5, 0.0, 0 },
            ["lantern-mainmast"] = new[] { -0.8868, 4.29, 0.0, 0 }, ["lantern-foremast"] = new[] { 14.7123, 4.29, 0.0, 0 },
            ["lantern-mizzenmast"] = new[] { -11.9067, 4.29, 0.0, 0 }, ["lantern-stern"] = new[] { -18.5089, 9.655, 0.0, 0 },
            ["bed-lowerdeck-1"] = new[] { -11.5, -1.0, 2.2492, 180 }, ["bed-lowerdeck-2"] = new[] { -8.75, -1.0, 3.4708, 180 },
            ["bed-lowerdeck-3"] = new[] { -6.5, -1.0, 3.8146, 180 }, ["bed-lowerdeck-4"] = new[] { -4.25, -1.0, 3.8625, 180 },
            ["bed-lowerdeck-5"] = new[] { 5.25, -1.0, 3.8625, 180 }, ["bed-lowerdeck-6"] = new[] { 7.5, -1.0, 3.8167, 180 },
            ["bed-lowerdeck-7"] = new[] { 9.75, -1.0, 3.5689, 180 }, ["bed-lowerdeck-8"] = new[] { -11.5, -1.0, -2.2492, 180 },
            ["bed-lowerdeck-9"] = new[] { 0.75, -1.0, -3.8625, 180 }, ["bed-lowerdeck-10"] = new[] { 3.0, -1.0, -3.8625, 180 },
            ["bed-lowerdeck-11"] = new[] { 5.25, -1.0, -3.8625, 180 }, ["bed-lowerdeck-12"] = new[] { 7.5, -1.0, -3.8167, 180 },
            ["bed-lowerdeck-13"] = new[] { 9.75, -1.0, -3.5689, 180 }, ["bed-poopcabin-1"] = new[] { -13.25, 5.0, 3.4233, 180 },
            ["bed-poopcabin-2"] = new[] { -13.25, 5.0, -3.4233, 180 }, ["bed-forecastle"] = new[] { 15.5, 2.0, -2.1841, 0 },
            ["chest-lowerdeck-1"] = new[] { -1.25, -1.0, 3.8825, 90 }, ["chest-lowerdeck-2"] = new[] { 2.25, -1.0, 3.8825, 90 },
            ["chest-lowerdeck-3"] = new[] { -1.25, -1.0, -3.8825, 270 }, ["chest-lowerdeck-4"] = new[] { 11.75, -1.0, 3.2877, 90 },
            ["chest-lowerdeck-5"] = new[] { 11.75, -1.0, -3.2877, 270 }, ["chest-greatcabin-1"] = new[] { -10.5, 2.0, 3.9427, 90 },
            ["chest-greatcabin-2"] = new[] { -8.25, 2.0, 3.9825, 90 }, ["chest-poopcabin"] = new[] { -11.25, 5.0, -3.4929, 270 },
            ["chest-forecastle-1"] = new[] { 15.25, 2.0, 2.5485, 90 }, ["chest-forecastle-2"] = new[] { 13.25, 2.0, -3.1927, 270 },
        };

        static LegacyPlaces()
        {
            // the hold's cargo places: two rows either side of the keelson, a block apart (cargo-s+4 at x 4, z 0.85)
            foreach (int n in new[] { -10, -9, -8, -7, -6, -5, -4, -3, -2, 1, 2, 3, 4, 10, 11, 12, 13 })
            {
                string num = n < 0 ? n.ToString(CultureInfo.InvariantCulture) : "+" + n.ToString(CultureInfo.InvariantCulture);
                Carrack["cargo-s" + num] = new[] { (double)n, -3.5, 0.85, 90 };
                Carrack["cargo-p" + num] = new[] { (double)n, -3.5, -0.85, 270 };
            }
        }
    }
}
