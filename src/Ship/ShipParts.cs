using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public enum ShipPartKind { None = 0, Hull = 1, Deck = 2, Mast = 3, Structure = 4, Fixture = 5, Rudder = 6, Sail = 7, Rigging = 8, Piece = 9,
        /// <summary>(0.3.28, step D2) One deck cell's planking (a deck slot of a design with deck slots): <see cref="ShipPart.Cell"/>.</summary>
        Cell = 10 }

    /// <summary>
    /// A part of a ship: what the crosshair is on (the entity selection's "selection box index" on a ship), and later
    /// what the damage model keeps a state for.
    /// - Hull: the planking of one side between two frames and in a band of her height. Side -1 port, +1 starboard;
    ///   Station the frame space along the keel (0 between her first two frames, counted from the stern, FramesDef);
    ///   Band whole blocks up from the keel. The damage model's hull panels (the planking between two frames, 1.5 long,
    ///   by a band of strakes about a block high) are these, or map onto them one to one.
    /// - Deck: Index into the design's decks, Station along it; Band 0 its top, 1 its underside (seen from below).
    /// - Mast: Index into the design's masts.
    /// - Structure: the rest of her own woodwork (bulkheads, stairs, coamings, the keelson...), Side, Station and Band
    ///   as for the hull.
    /// - Fixture: Index is the fixture's in the design (the helm, a door, a chest put aboard...).
    /// - Rudder, Sail (Index into the design's sails) and Rigging (Index: its mast): parts the damage model keeps a state
    ///   for that the crosshair doesn't meet (they aren't in her collision).
    /// - Piece: a vanilla block put aboard (Index: the piece's id, ShipDecor).
    /// - Cell (0.3.28, step D2): a deck cell's planking, a design's deck slot (Index: its deck's index in bits 16..19, its
    ///   cell on her grid i + 128 in bits 8..15 and k + 128 in bits 0..7; bit 20: its underside, seen from below).
    /// One int holds it (<see cref="Encode"/>): the kind in the top byte; the hull's and structure's side in bit 16,
    /// station in bits 8..15, band in bits 0..7; a deck's index in bits 16..23; a mast's, fixture's, sail's or rigging's
    /// index below the kind. 0 is no part.
    /// </summary>
    public struct ShipPart
    {
        public ShipPartKind Kind;
        public int Side, Station, Band, Index;

        /// <summary>A hull part (a panel: side, frame space, band; (0.3.28, D2.1) half: 0 the frame space's after half (or the whole space), 1 its fore half).</summary>
        public static ShipPart Hull(int side, int station, int band, int half = 0, bool inner = false) => new() { Kind = ShipPartKind.Hull, Side = side < 0 ? -1 : 1, Station = Byte(station), Band = Byte(band), Index = (half != 0 ? 1 : 0) | (inner ? 2 : 0) };
        /// <summary>(0.3.29, LINED) A hull part that's her side's inner layer (a ceiling panel: "ceil:..."): Index bit 1, code bit 18.</summary>
        public bool InnerLayer => Kind == ShipPartKind.Hull && (Index & 2) != 0;
        public static ShipPart Deck(int deck, int station, bool under) => new() { Kind = ShipPartKind.Deck, Index = Byte(deck), Station = Byte(station), Band = under ? 1 : 0 };
        public static ShipPart Mast(int mast) => new() { Kind = ShipPartKind.Mast, Index = Math.Clamp(mast, 0, 0xFFFFFF) };
        public static ShipPart Structure(int side, int station, int band) => new() { Kind = ShipPartKind.Structure, Side = side < 0 ? -1 : 1, Station = Byte(station), Band = Byte(band) };
        public static ShipPart Fixture(int index) => new() { Kind = ShipPartKind.Fixture, Index = Math.Clamp(index, 0, 0xFFFFFF) };
        /// <summary>(0.3.28, step D2) A deck cell: its deck (index into the design's decks), its cell on her grid (DeckSlotLayout.CellI/CellK), its underside.</summary>
        public static ShipPart Cell(int deck, int i, int k, bool under = false) => new()
        {
            Kind = ShipPartKind.Cell, Index = Math.Clamp(deck, 0, 15) << 16 | Byte(i + 128) << 8 | Byte(k + 128) | (under ? 1 << 20 : 0)
        };
        /// <summary>(0.3.28) A deck cell's deck index, its cell's i and k, and whether it's its underside.</summary>
        public int CellDeck => Index >> 16 & 15;
        public int CellI => (Index >> 8 & 0xFF) - 128;
        public int CellK => (Index & 0xFF) - 128;
        public bool CellUnder => (Index & 1 << 20) != 0;
        /// <summary>(0.3.28) The same cell, its top (the damage part's code).</summary>
        public ShipPart CellTop => new() { Kind = Kind, Index = Index & ~(1 << 20) };

        static int Byte(int v) => Math.Clamp(v, 0, 255);

        public int Encode()
        {
            int kind = (int)Kind << 24;
            switch (Kind)
            {
                case ShipPartKind.Hull:
                    return kind | (Side > 0 ? 1 << 16 : 0) | ((Index & 1) != 0 ? 1 << 17 : 0) | ((Index & 2) != 0 ? 1 << 18 : 0) | Byte(Station) << 8 | Byte(Band);
                case ShipPartKind.Structure:
                    return kind | (Side > 0 ? 1 << 16 : 0) | Byte(Station) << 8 | Byte(Band);
                case ShipPartKind.Deck:
                    return kind | Byte(Index) << 16 | Byte(Station) << 8 | (Band != 0 ? 1 : 0);
                case ShipPartKind.Mast:
                case ShipPartKind.Fixture:
                case ShipPartKind.Rudder:
                case ShipPartKind.Sail:
                case ShipPartKind.Rigging:
                case ShipPartKind.Piece:
                case ShipPartKind.Cell:
                    return kind | Math.Clamp(Index, 0, 0xFFFFFF);
            }
            return 0;
        }

        public static ShipPart Decode(int code)
        {
            var kind = (ShipPartKind)((code >> 24) & 0xFF);
            switch (kind)
            {
                case ShipPartKind.Hull:
                case ShipPartKind.Structure:
                    return new ShipPart { Kind = kind, Side = (code >> 16 & 1) != 0 ? 1 : -1, Station = code >> 8 & 0xFF, Band = code & 0xFF,
                                          Index = kind == ShipPartKind.Hull ? (code >> 17 & 1) | (code >> 18 & 1) << 1 : 0 };
                case ShipPartKind.Deck:
                    return new ShipPart { Kind = kind, Index = code >> 16 & 0xFF, Station = code >> 8 & 0xFF, Band = code & 1 };
                case ShipPartKind.Mast:
                case ShipPartKind.Fixture:
                case ShipPartKind.Rudder:
                case ShipPartKind.Sail:
                case ShipPartKind.Rigging:
                case ShipPartKind.Piece:
                case ShipPartKind.Cell:
                    return new ShipPart { Kind = kind, Index = code & 0xFFFFFF };
            }
            return default;
        }

        public override string ToString() => Kind switch
        {
            ShipPartKind.Hull or ShipPartKind.Structure => $"{Kind} {(Side > 0 ? "starboard" : "port")} station {Station} band {Band}",
            ShipPartKind.Deck => $"Deck {Index} station {Station}{(Band != 0 ? " underside" : "")}",
            ShipPartKind.Cell => $"Deck cell {CellDeck}:{CellI}:{CellK}{(CellUnder ? " underside" : "")}",
            ShipPartKind.None => "none",
            _ => $"{Kind} {Index}"
        };
    }

    /// <summary>Where a ray meets a ship: how far along it, where (world and ship-local), the face it came through, the part.</summary>
    public struct ShipRayHit
    {
        public double Distance;
        public Vec3d World, Local;
        /// <summary>The outward normal of the face it came through, ship-local (Face is the same in the world, to the nearest axis).</summary>
        public Vec3d LocalNormal;
        public BlockFacing Face;
        public ShipPart Part;
        public FixtureDef Fixture;
    }

    /// <summary>
    /// What part of a ship a cell of her collision is, worked out once per design from the design's own voxels (doors
    /// open, nothing put aboard):
    /// - a top face at one of her decks' heights is that deck, the underside of one (within half a block under it) too;
    /// - near a mast's centreline, that mast;
    /// - within half a block of her outside (the outermost solid cell of her row to that side, the lowest of her column,
    ///   the aftmost or foremost of her row along the keel), up to her highest deck and a little over, the hull;
    /// - anything else of hers is structure.
    /// </summary>
    public class ShipPartMap
    {
        /// <summary>How far in from her outside (cells) still counts as her hull: the planking's thickness and a little.</summary>
        public const int ShellCells = 2;

        readonly ShipDefinition def;
        readonly VoxelGrid g;
        readonly short[] portK, starK, bottomJ, aftI, fwdI;
        // A row's outermost cell to port or starboard (or its aftmost or foremost) is her hull only where her side (or
        // her stern or bow) runs on up to it from below the main deck without stepping inboard: the castles' sides, the
        // bulwarks, the transom and the overhanging poop and forecastle, but not a stair, a post or a bulkhead standing
        // inboard over the rail.
        readonly bool[] portSide, starSide, aftEnd, fwdEnd;
        /// <summary>The hull's ends along the keel (below the main deck), for aft / amidships / forward.</summary>
        public readonly double HullX0, HullX1;
        /// <summary>Above this the outermost cells aren't the hull any more (the masts, the tops).</summary>
        public readonly double HullTop;

        public ShipPartMap(ShipDefinition def)
        {
            this.def = def;
            g = def.Grid;
            int nx = g.NX, ny = g.NY, nz = g.NZ;
            portK = Fill(nx * ny);
            starK = Fill(nx * ny);
            bottomJ = Fill(nx * nz);
            aftI = Fill(ny * nz);
            fwdI = Fill(ny * nz);
            double x0 = double.MaxValue, x1 = double.MinValue;
            int jKeel = g.CellY(def.KeelY - 0.5), jDeck = g.CellY(def.DeckY);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        if (!g.Solid(i, j, k)) continue;
                        int ij = i * ny + j, ik = i * nz + k, jk = j * nz + k;
                        if (portK[ij] < 0) portK[ij] = (short)k;
                        starK[ij] = (short)k;
                        if (bottomJ[ik] < 0) bottomJ[ik] = (short)j;
                        if (aftI[jk] < 0) aftI[jk] = (short)i;
                        fwdI[jk] = (short)i;
                        if (j >= jKeel && j <= jDeck)
                        {
                            x0 = Math.Min(x0, g.OX + i * g.V);
                            x1 = Math.Max(x1, g.OX + (i + 1) * g.V);
                        }
                    }
            HullX0 = x0 <= x1 ? x0 : g.OX;
            HullX1 = x0 <= x1 ? x1 : g.MaxX;
            portSide = new bool[nx * ny];
            starSide = new bool[nx * ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    int ij = i * ny + j;
                    if (j <= jDeck)
                    {
                        portSide[ij] = portK[ij] >= 0;
                        starSide[ij] = starK[ij] >= 0;
                        continue;
                    }
                    int below = ij - 1;
                    portSide[ij] = portK[ij] >= 0 && portSide[below] && portK[ij] <= portK[below] + 1;
                    starSide[ij] = starK[ij] >= 0 && starSide[below] && starK[ij] >= starK[below] - 1;
                }
            aftEnd = new bool[ny * nz];
            fwdEnd = new bool[ny * nz];
            for (int j = 0; j < ny; j++)
                for (int k = 0; k < nz; k++)
                {
                    int jk = j * nz + k;
                    if (j <= jDeck)
                    {
                        aftEnd[jk] = aftI[jk] >= 0;
                        fwdEnd[jk] = fwdI[jk] >= 0;
                        continue;
                    }
                    int below = jk - nz;
                    aftEnd[jk] = aftI[jk] >= 0 && aftEnd[below] && aftI[jk] <= aftI[below] + 1;
                    fwdEnd[jk] = fwdI[jk] >= 0 && fwdEnd[below] && fwdI[jk] >= fwdI[below] - 1;
                }
            // the castles' decks and their rails: up to the highest deck below the main top, and a rail's height over it
            double top = def.DeckY;
            foreach (var d in def.Decks) if (d.Code != "top" && d.Y > top) top = d.Y;
            HullTop = top + 1.75;
        }

        static short[] Fill(int n)
        {
            var a = new short[n];
            Array.Fill(a, (short)-1);
            return a;
        }

        /// <summary>The frame space along the keel ship-local x is in (0 between her first two frames).</summary>
        public int StationAt(double x) => Math.Clamp((int)Math.Floor((x - def.Frames.X0) / def.Frames.Space), 0, 255);

        /// <summary>The band of her height (whole blocks up from the keel) ship-local y is in.</summary>
        public int BandAt(double y) => Math.Clamp((int)Math.Floor(y - def.KeelY), 0, 255);

        /// <summary>The middle of a station along the keel, and of a band up her side (ship-local).</summary>
        public double StationX(int station) => def.Frames.X0 + (station + 0.5) * def.Frames.Space;
        public double BandY(int band) => def.KeelY + band + 0.5;

        /// <summary>
        /// What part of her the ray meets: the cell (i, j, k) of her collision it hit, the face it came in through (axis
        /// 0, 1, 2, or -1 if it started there) and its direction (ship-local), the point it hit (ship-local).
        /// </summary>
        public ShipPart Classify(int i, int j, int k, int axis, Vec3d dir, Vec3d at)
        {
            int station = StationAt(at.X), band = BandAt(at.Y);
            // ((0.3.26, step D1) a hull panel's voxel: that panel)
            if (def.Hull?.AtVoxel(g, i, j, k) is int hs && hs >= 0)
            {
                var s = def.Hull.Slots[hs];
                return ShipPart.Hull(s.Side, s.Station, s.Band);
            }
            // ((0.3.28, step D2) a deck cell's planking: that cell, its top or (seen from below) its underside)
            if (def.Slots?.AtVoxel(g, i, j, k) is int cs && cs >= 0 && def.Slots.Slots[cs].IsCell)
                return def.Slots.PartOf(cs, axis == 1 && dir.Y > 0);
            // a deck: its top seen from above, its underside from below
            if (axis == 1)
            {
                double top = g.OY + (j + 1) * g.V, bottom = g.OY + j * g.V;
                for (int n = 0; n < def.Decks.Length; n++)
                {
                    var d = def.Decks[n];
                    if (at.X < d.X0 || at.X > d.X1) continue;
                    if (dir.Y < 0 && Math.Abs(d.Y - top) < 0.13) return ShipPart.Deck(n, station, false);
                    // (not the bottom's planking under the cargo deck, seen from under the keel)
                    if (dir.Y > 0 && d.Y - bottom > -0.01 && d.Y - bottom < 0.76 && !IsShell(i, j, k)) return ShipPart.Deck(n, station, true);
                }
            }
            // a mast
            for (int n = 0; n < def.Masts.Length; n++)
            {
                var m = def.Masts[n];
                double dx = at.X - m.X, dz = at.Z - m.Z;
                if (at.Y >= m.Y0 - 0.1 && at.Y <= m.Y1 + 0.1 && dx * dx + dz * dz <= (m.Radius + 0.3) * (m.Radius + 0.3)) return ShipPart.Mast(n);
            }
            int side = at.Z < 0 ? -1 : 1;
            // ((0.3.26) a design with hull panels: her planking that isn't a panel's (a frame's strip, a wale, her ends) is
            // her structure, not a hull part: its codes are the panels')
            if (def.Hull != null && at.Y <= HullTop && IsShell(i, j, k)) return ShipPart.Structure(side, station, band);
            if (at.Y <= HullTop && IsShell(i, j, k)) return ShipPart.Hull(side, station, band);
            return ShipPart.Structure(side, station, band);
        }

        /// <summary>Is this cell of the design within the hull's planking from her outside (any side, the bottom, the ends)?</summary>
        public bool IsShell(int i, int j, int k)
        {
            if (i < 0 || j < 0 || k < 0 || i >= g.NX || j >= g.NY || k >= g.NZ) return false;
            int ij = i * g.NY + j, ik = i * g.NZ + k, jk = j * g.NZ + k;
            return portSide[ij] && k <= portK[ij] + ShellCells
                || starSide[ij] && k >= starK[ij] - ShellCells
                || bottomJ[ik] >= 0 && j <= bottomJ[ik] + ShellCells
                || aftEnd[jk] && i <= aftI[jk] + ShellCells
                || fwdEnd[jk] && i >= fwdI[jk] - ShellCells;
        }

        /// <summary>Is this cell the outermost of her planking there (her side's, her bottom's or an end's outside: tarred, or clad)? A wreck's outer skin.</summary>
        public bool IsOuterSkin(int i, int j, int k)
        {
            if (i < 0 || j < 0 || k < 0 || i >= g.NX || j >= g.NY || k >= g.NZ || !g.Solid(i, j, k)) return false;
            int ij = i * g.NY + j, ik = i * g.NZ + k, jk = j * g.NZ + k;
            return portSide[ij] && k == portK[ij] || starSide[ij] && k == starK[ij] || bottomJ[ik] == j
                || aftEnd[jk] && i == aftI[jk] || fwdEnd[jk] && i == fwdI[jk];
        }

        // ------------------------------------------------------------------ what a part is called (lang codes)

        /// <summary>Which fifth of her length a station is in: stern, aft, amidships, forward, bow.</summary>
        public string LengthZone(int station)
        {
            double u = (StationX(station) - HullX0) / Math.Max(1e-6, HullX1 - HullX0);
            return u < 0.2 ? "stern" : u < 0.4 ? "aft" : u < 0.6 ? "amidships" : u < 0.8 ? "forward" : "bow";
        }

        /// <summary>
        /// How high up her side a hull part is: below (the waterline), topsides (up to the main deck), castle (a castle's
        /// side over it), bulwark (the waist's, over it).
        /// </summary>
        public string HeightZone(int station, int band) => HeightZoneAt(station, BandY(band));

        /// <summary>(0.3.26) How high up her side a point of a station is (a hull panel's middle): as <see cref="HeightZone"/>.</summary>
        public string HeightZoneAt(int station, double y)
        {
            if (y < def.Waterline) return "below";
            if (y < def.DeckY) return "topsides";
            double x = StationX(station);
            foreach (var r in def.Rooms)
                if (r.Enclosed && r.Min[1] >= def.DeckY - 0.01 && x >= r.Min[0] && x <= r.Max[0]) return "castle";
            return "bulwark";
        }

        /// <summary>The room a part is in (a deck's: the room over it), or null.</summary>
        public RoomDef RoomOf(ShipPart p)
        {
            double x = StationX(p.Station);
            if (p.Kind == ShipPartKind.Deck && p.Index < def.Decks.Length)
                return p.Band == 0 ? def.RoomAt(x, def.Decks[p.Index].Y + 0.5, 0) : def.RoomAt(x, def.Decks[p.Index].Y - 1.0, 0);
            if (p.Kind == ShipPartKind.Structure) return def.RoomAt(x, BandY(p.Band), 0);
            return null;
        }
    }

    /// <summary>Where a ray meets a ship, and what part of her it is (both sides; the offline tests use it as is).</summary>
    public static class ShipParts
    {
        /// <summary>
        /// Where a ray (world; dir of any length, range in blocks along it) first meets a ship at this pose: her collision,
        /// or a fixture's selection box in front of it (visible: which fixtures can be picked now). pos: her position
        /// (in the ray's coordinates); yaw, pitch, roll: her pose, tilted as she's drawn.
        /// </summary>
        public static bool Ray(ShipDefinition def, VoxelGrid grid, double posX, double posY, double posZ, double yaw, double pitch, double roll,
            Vec3d origin, Vec3d dir, double range, System.Func<FixtureDef, bool> visible, System.Func<FixtureDef, bool> isOpen,
            System.Func<FixtureDef, ItemStack> attached, out ShipRayHit hit, System.Func<FixtureDef, bool> absent = null)
        {
            hit = default;
            double len = dir.Length();
            if (def?.PartMap == null || grid == null || len < 1e-9 || range <= 0) return false;
            ShipFrame.ToLocal(yaw, pitch, roll, origin.X - posX, origin.Y - posY, origin.Z - posZ, out double ox, out double oy, out double oz);
            ShipFrame.ToLocal(yaw, pitch, roll, dir.X / len, dir.Y / len, dir.Z / len, out double dx, out double dy, out double dz);
            var o = new Vec3d(ox, oy, oz);
            var d = new Vec3d(dx, dy, dz);
            // quick: is she within its reach at all
            if (ox + range < grid.OX - 1 || ox - range > grid.MaxX + 1 || oy + range < grid.OY - 1 || oy - range > grid.MaxY + 1 ||
                oz + range < grid.OZ - 1 || oz - range > grid.MaxZ + 1) return false;

            double tw = grid.RaycastCell(o, d, range, out int ci, out int cj, out int ck, out int axis, skipStart: true);
            var f = ShipFittings.Pick(def, grid, o, d, range, visible, isOpen, attached, out double tf, null, absent);
            double t;
            Vec3d normal;
            if (f != null && tf <= range && tf <= tw + 0.05)
            {
                t = tf;
                hit.Fixture = f;
                hit.Part = ShipPart.Fixture(f.Index);
                normal = BoxFace(f.SelBox, o.AddCopy(d.X * t, d.Y * t, d.Z * t), d);
            }
            else if (tw <= range)
            {
                t = tw;
                var at = o.AddCopy(d.X * t, d.Y * t, d.Z * t);
                hit.Part = def.PartMap.Classify(ci, cj, ck, axis, d, at);
                // the face the ray came in through looks back along it; from inside a cell, the way it looks
                int a = axis >= 0 ? axis : Math.Abs(d.X) >= Math.Abs(d.Y) && Math.Abs(d.X) >= Math.Abs(d.Z) ? 0 : Math.Abs(d.Y) >= Math.Abs(d.Z) ? 1 : 2;
                double s = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
                normal = new Vec3d(a == 0 ? -Math.Sign(s) : 0, a == 1 ? -Math.Sign(s) : 0, a == 2 ? -Math.Sign(s) : 0);
            }
            else return false;

            hit.Distance = t;
            hit.Local = o.AddCopy(d.X * t, d.Y * t, d.Z * t);
            ShipFrame.ToWorld(yaw, pitch, roll, hit.Local.X, hit.Local.Y, hit.Local.Z, out double wx, out double wy, out double wz);
            hit.World = new Vec3d(posX + wx, posY + wy, posZ + wz);
            hit.LocalNormal = normal;
            ShipFrame.ToWorld(yaw, pitch, roll, normal.X, normal.Y, normal.Z, out double nx, out double ny, out double nz);
            hit.Face = BlockFacing.FromVector(nx, ny, nz);
            return true;
        }

        /// <summary>The outward normal of the box's face a point on it lies on (the one nearest it), or back along the ray.</summary>
        static Vec3d BoxFace(Cuboidd b, Vec3d p, Vec3d d)
        {
            if (b == null) return new Vec3d(-d.X, -d.Y, -d.Z);
            double[] dist = { Math.Abs(p.X - b.X1), Math.Abs(p.X - b.X2), Math.Abs(p.Y - b.Y1), Math.Abs(p.Y - b.Y2), Math.Abs(p.Z - b.Z1), Math.Abs(p.Z - b.Z2) };
            int best = 0;
            for (int n = 1; n < 6; n++) if (dist[n] < dist[best]) best = n;
            double s = best % 2 == 0 ? -1 : 1;
            return new Vec3d(best / 2 == 0 ? s : 0, best / 2 == 1 ? s : 0, best / 2 == 2 ? s : 0);
        }
    }
}
