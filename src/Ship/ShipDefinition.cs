using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// One ship design, loaded from assets/hansa/config/ships/*.json (written by tools/shipgen).
    /// All positions are ship-local blocks: +X = bow, +Y = up, +Z = starboard, origin = waterline midships.
    /// </summary>
    public class ShipDefinition
    {
        public string Code;
        public string Name;
        public float VoxelSize;
        public double[] GridOrigin;
        public int[] GridSize;
        public string Solid;
        public ShipShapes Shapes;
        public RoomDef[] Rooms = Array.Empty<RoomDef>();
        public FixtureDef[] Fixtures = Array.Empty<FixtureDef>();
        public LightDef[] Lights = Array.Empty<LightDef>();
        public SailDef[] Sails = Array.Empty<SailDef>();
        public double[][] BoardPoints = Array.Empty<double[]>();

        public float Length, Beam, Draft, Waterline, KeelY, DeckY, HullHalfWidthAtDeck;
        public double[][] Bounds;
        public float MaxSpeed = 6, Acceleration = 0.3f, Deceleration = 0.5f, TurnRate = 5, RudderSpeed = 60, ReverseSpeed = 0.6f;
        /// <summary>A boat rowed from its "oars" fixtures: how fast one rower drives her (blocks/s; two go √2 faster). 0: no oars.</summary>
        public float RowSpeed;
        /// <summary>How many may be aboard at once (0: no limit): a boat's crew.</summary>
        public int MaxAboard;
        /// <summary>A boat low enough to climb into from the water (a ship has rope ladders instead).</summary>
        public bool BoardFromWater;
        /// <summary>The item she becomes when picked up (Shift + right-click, nobody aboard, nothing in her), or null.</summary>
        public string PickUpItem;
        public double[][] HullSamples = Array.Empty<double[]>();
        public WaterClipDef WaterClip;
        public LadderDef[] Ladders = Array.Empty<LadderDef>();
        public MovingPartDef[] MovingParts = Array.Empty<MovingPartDef>();
        /// <summary>Copper sheathing: metal plates it takes, and how much faster the clean bottom makes the ship.</summary>
        public int CladdingPlates = 48;
        public float CladdingSpeedBonus = 0.08f;
        /// <summary>How she answers the waves and the wind.</summary>
        public MotionDef Motion = new();
        /// <summary>How she sails: her polar, how close to the wind she points, how the yards brace.</summary>
        public SailingDef Sailing = new();
        /// <summary>Her frames (ribs) along the keel: the hull's parts are counted between them (<see cref="ShipPart"/>).</summary>
        public FramesDef Frames;
        /// <summary>Her decks (and the main top): what looking at a floor says, and the deck parts.</summary>
        public DeckDef[] Decks = Array.Empty<DeckDef>();
        /// <summary>Her masts: what looking at one says, and the mast parts.</summary>
        public MastDef[] Masts = Array.Empty<MastDef>();
        /// <summary>How she takes damage, leaks, floods and founders (null: the defaults).</summary>
        public DamageDef Damage;
        /// <summary>How her guns shoot (null: the defaults, <see cref="CannonDef"/>).</summary>
        public CannonDef Cannon;
        /// <summary>
        /// Decorating (ShipMounts, ShipPieces, ShipPaint): her hooks for lanterns, what a new ship starts with on them, her
        /// stairs and anything else kept clear of placed pieces (windows), and her paint zones (in the mesh file: which quads
        /// and voxel faces are each).
        /// </summary>
        public HookDef[] Hooks;
        public StartPieceDef[] StartPieces;
        public StairDef[] Stairs;
        public KeepClearDef[] KeepClear;
        /// <summary>(0.3.16, notes 154–158) The pieces a new ship starts with on her places: cabin fronts, windows, doors, railings, stairs, panels (docs/modular-pieces.md).</summary>
        public ModularPieceDef[] ModularPieces;
        /// <summary>(0.3.16) Openings in a deck (stair wells): the room under one is open to the deck above while it isn't panelled over.</summary>
        public WellDef[] Wells;
        /// <summary>
        /// (0.3.29, docs/modular-pieces.md §14) Her layout's revision: a ship laid at an older one keeps her deck slots' states
        /// where a later revision changed their start (<see cref="LayoutBefore"/>) and isn't given the modular entries it
        /// brought (<see cref="ModularPieceDef.Since"/>).
        /// </summary>
        public int LayoutRev;
        public LayoutStepDef[] LayoutBefore;
        /// <summary>(0.3.20, step B; docs/slots.md) The deck cells that may be taken up and laid again (null: none, as before).</summary>
        public DeckSlotDef[] DeckSlots;
        /// <summary>(0.3.20) <see cref="DeckSlots"/> worked out (null without them).</summary>
        [JsonIgnore] public DeckSlotLayout Slots;
        /// <summary>(0.3.26, step D1; docs/slots.md §9) Her hull's panels that may be stove in, taken out and laid again (null: none, as before).</summary>
        public HullSlotDef[] HullSlots;
        /// <summary>(0.3.26) <see cref="HullSlots"/> worked out (null without them).</summary>
        [JsonIgnore] public HullSlotLayout Hull;
        /// <summary>
        /// (0.3.26, step D1) Her collision with every slot of hers filled (the hull panels: step E may start a ship without
        /// them): what her dry spaces, her parts and her hydrostatics are worked out from. <see cref="Grid"/> itself when every
        /// panel starts filled.
        /// </summary>
        [JsonIgnore] public VoxelGrid FullGrid;
        /// <summary>(0.3.21, step C; docs/slots.md §7) Where a gun may be mounted, each backed by a gun fixture (null: none; her guns are fixed).</summary>
        public GunPlaceDef[] GunPlaces;
        /// <summary>(0.3.21) Gun fixture code → its place (none: a fixed gun, always mounted).</summary>
        [JsonIgnore] public Dictionary<string, GunPlaceDef> GunPlaceOf = new();
        /// <summary>(0.3.21) Moving part code → the gun it belongs to (train:/elevate:/fire: + the gun, their children, ropes following them; not the lid).</summary>
        [JsonIgnore] public Dictionary<string, string> GunOfPart = new();
        /// <summary>(0.3.28, D2.4) A lid's moving part code → its gun (FixtureDef.Lid).</summary>
        [JsonIgnore] public Dictionary<string, string> GunOfLid = new();
        public PaintZoneDef[] PaintZones;
        /// <summary>(0.3.14, notes 150–152) The generator's rig check: her standing rigging, which her sails are pressed against (<see cref="SailPress"/>).</summary>
        public RigCheckDef RigCheck;
        /// <summary>
        /// The ship's one block grid (0.3.9, the generator's "grid"): block borders at Origin + whole blocks (ship-local x, y,
        /// z), the same on every deck. With it every floor place and wall spot is on it; without it (older designs, the
        /// rowing boat) each deck takes the alignment that gives it the most places (<see cref="MountLayout"/>).
        /// </summary>
        [JsonProperty("grid")] public BlockGridDef BlockGrid;
        /// <summary>Where pieces may go (worked out once from the definition: <see cref="ShipMounts.For"/>).</summary>
        [JsonIgnore] public MountLayout Mounts;

        [JsonIgnore] public VoxelGrid Grid;
        /// <summary>A mesh built in memory (a grid ship's: <see cref="GridShips"/>) instead of <see cref="ShipShapes.Skin"/>'s file.</summary>
        [JsonIgnore] public ShipSkinFile SkinFile;
        /// <summary>What part of her a spot of her collision is (the hull's shell, a deck, a mast...): <see cref="ShipPartMap"/>.</summary>
        [JsonIgnore] public ShipPartMap PartMap;
        /// <summary>
        /// The inside of the hull below the main deck (ship-local, on the collision grid): the empty cells a flood from
        /// outside the hull can't reach, and the hull's own solid cells (so a check at an eye next to a wall never
        /// flickers). It's dry there though the world has water: the hold and the lower deck, even in a storm's trough.
        /// Sea asks through ShipDrySpaces.
        /// </summary>
        [JsonIgnore] public VoxelGrid DryMask;
        /// <summary>
        /// (0.3.13, the bubbles in the cargo deck) <see cref="DryMask"/> grown by <see cref="DrySweep.Margin"/>
        /// every way: the water her sides, bottom and decks sweep through as she rolls and heaves. A speck in the world's
        /// water there now would be inside her a moment later (DrySweep.SweptDry).
        /// </summary>
        [JsonIgnore] public VoxelGrid DrySweepMask;
        /// <summary>The parts the damage model keeps a state for, and her hydrostatics (<see cref="DamageLayout"/>).</summary>
        [JsonIgnore] public DamageLayout DamageParts;
        /// <summary>
        /// (0.3.14, notes 148, 149) Her timbers and planking as drawn, for drawing partitions out to them (client side: built
        /// from her mesh file and shapes once they're loaded; null until then, and on the server).
        /// </summary>
        [JsonIgnore] public volatile ShipSurfaces Surfaces;

        public void Init()
        {
            Grid = VoxelGrid.Decode(GridOrigin, GridSize, VoxelSize, Solid);
            Solid = null;
            if (WaterClip != null)
            {
                // the band Sea is told is dry (ShipRenderer.AddWaterClips), with a little to spare
                WaterClip.KeelBottom = KeelY - 0.5;
                WaterClip.DryTop = DeckY + 1.0;       // up to the waist's rail
                WaterClip.Build(Grid);
            }
            // ((0.3.26, step D1) her hull panels first: her dry spaces, parts and hydrostatics are her hull's whole, every
            // panel in)
            Hull = HullSlotLayout.Build(this);
            FullGrid = Grid;
            if (Hull != null && Hull.Slots.Any(s => s != null && s.StartsEmpty))
            {
                FullGrid = Grid.Clone();
                foreach (var s in Hull.Slots) if (s?.Box != null) foreach (var b in s.SolidBoxes) FullGrid.Fill(b);
            }
            // up to just under the main deck, where the hull has no openings (the hatches and doors open above it)
            DryMask = VoxelGrid.InsideMask(FullGrid, DeckY - 0.25);
            DrySweepMask = DryMask.Dilated((int)Math.Ceiling(DrySweep.Margin / Grid.V - 1e-9));
            for (int i = 0; i < Fixtures.Length; i++) Fixtures[i].Index = i;
            // (0.3.21, step C) her gun places, and which moving parts are each gun's
            IndexGunPlaces();
            GunOfPart = GunPartMap(this);
            GunOfLid = new Dictionary<string, string>();
            foreach (var f in Fixtures) if (f?.Type == "gun" && f.Lid != null && f.Code != null) GunOfLid[f.Lid] = f.Code;
            // (0.3.20, step B) her deck cells, before her damage layout (an open cell on the main deck lets the sea below)
            Slots = DeckSlotLayout.Build(this);
            // a design without frames (the launch): counted every 1.5 from her stern
            Frames ??= new FramesDef { X0 = Grid.OX, Space = 1.5 };
            if (Frames.Space <= 0) Frames.Space = 1.5;
            Damage ??= new DamageDef();
            {
                var design = Grid;
                Grid = FullGrid;
                try
                {
                    PartMap = new ShipPartMap(this);
                    DamageParts = new DamageLayout(this);
                }
                finally { Grid = design; }
            }
            Cannon ??= new CannonDef();
            // where players may put vanilla blocks (ShipMounts)
            Mounts = MountLayout.Build(this);
            // (0.3.20, step B) and where they could, over her openings, once their cells are laid; each place's cell
            if (Slots != null)
            {
                var keep = Grid;
                var full = Grid.Clone();
                foreach (var s in Slots.Slots) if (s?.Box != null) foreach (var b in s.SolidBoxes) full.Fill(b);
                Grid = full;
                MountLayout opened;
                try { opened = MountLayout.Build(this); }
                finally { Grid = keep; }
                Mounts.MergeOpenings(opened, Slots);
                Mounts.BindSlots(Slots);
                // (0.3.28, note 230) walls round a well on the deck beside it, a hood's roof over it
                Mounts.WellPlaces(Slots);
            }
        }

        public RoomDef RoomAt(double x, double y, double z)
        {
            foreach (var r in Rooms)
            {
                if (x >= r.Min[0] && x < r.Max[0] && y >= r.Min[1] && y < r.Max[1] && z >= r.Min[2] && z < r.Max[2]) return r;
            }
            return null;
        }

        /// <summary>
        /// Is this vanilla block aboard (a prop, a bunk, or what's on an attach place) inside an enclosed room: a cabin,
        /// the lower deck, the hold? It's lit darker there, and casts no shadow (it's in her own). A block standing on a
        /// floor is tested a little above its foot, one on an attach place (a lantern's hook) a little below.
        /// </summary>
        public bool Indoors(FixtureDef f) =>
            f?.Pos != null && RoomAt(f.Pos[0], f.Pos[1] + (f.Type == "attach" ? -0.3 : 0.3), f.Pos[2])?.Enclosed == true;

        /// <summary>(0.3.21) Her gun places indexed (Init; the self-test after giving carrack-m places of its own).</summary>
        public void IndexGunPlaces()
        {
            GunPlaces ??= Array.Empty<GunPlaceDef>();
            GunPlaceOf = new Dictionary<string, GunPlaceDef>();
            for (int i = 0; i < GunPlaces.Length; i++)
            {
                var gp = GunPlaces[i];
                gp.Index = i;
                if (gp.Gun != null && !GunPlaceOf.ContainsKey(gp.Gun)) GunPlaceOf[gp.Gun] = gp;
            }
        }

        /// <summary>
        /// (0.3.21) Which moving parts are a gun's (drawn only while it's mounted): a part moved by "train:", "elevate:" or
        /// "fire:" + a gun, a part riding on one of a gun's parts, a rope following one (the breechings). The lid ("open:" +
        /// the gun) is her side's, not the gun's.
        /// </summary>
        public static Dictionary<string, string> GunPartMap(ShipDefinition def)
        {
            var map = new Dictionary<string, string>();
            var parts = def.MovingParts ?? Array.Empty<MovingPartDef>();
            foreach (var p in parts)
            {
                string src = p?.Source ?? "";
                foreach (var pre in new[] { "train:", "elevate:", "fire:" })
                    if (src.StartsWith(pre) && p.Code != null) map[p.Code] = src.Substring(pre.Length);
            }
            for (int pass = 0; pass < 4; pass++)
                foreach (var p in parts)
                {
                    if (p?.Code == null || map.ContainsKey(p.Code) || (p.Source ?? "").StartsWith("open:")) continue;
                    if (p.Parent != null && map.TryGetValue(p.Parent, out string g) || p.Follow != null && map.TryGetValue(p.Follow, out g) ||
                        p.AnchorFollow != null && map.TryGetValue(p.AnchorFollow, out g)) map[p.Code] = g;
                }
            return map;
        }

        public FixtureDef Fixture(string code)
        {
            foreach (var f in Fixtures) if (f.Code == code) return f;
            return null;
        }

        /// <summary>The waterline's half length, half width and centre along the keel (from the measured footprint).</summary>
        public void Waterplane(out double halfLength, out double halfBeam, out double centreX)
        {
            var wc = WaterClip;
            if (wc?.Profile != null)
            {
                halfLength = wc.WaterlineHalfLength;
                halfBeam = wc.HalfWidth - wc.Margin;
                centreX = wc.WaterlineCentre;
                return;
            }
            halfLength = Length / 2;
            halfBeam = Beam / 2;
            centreX = 0;
        }
    }

    /// <summary>
    /// Waterline footprint where drawn waves are held down (Sea keeps the water at Height inside it and calms the waves
    /// around it). Its outline is measured from the hull itself: <see cref="Build"/>.
    /// </summary>
    public class WaterClipDef
    {
        public const int Stations = 16;
        /// <summary>The detailed dry outline's size, as Sea's water shader takes it (Sea.WaterShader.DryStations, DryLevels).</summary>
        public const int DryStations = 32, DryLevels = 4;

        public float CenterX;
        public float HalfLength;
        public float HalfWidth;
        /// <summary>Highest the water may be drawn inside the footprint, ship-local.</summary>
        public float Height = 0.1f;
        /// <summary>Half-widths at <see cref="Stations"/> evenly spaced stations from stern to bow.</summary>
        [JsonIgnore] public float[] Profile;
        /// <summary>How far the footprint reaches beyond the hull.</summary>
        [JsonIgnore] public float Margin;
        /// <summary>
        /// The hull's exact half-width on a fine grid, from the generator (ship-local): row r is height Y0 + r * DY,
        /// column c is X0 + c * DX. Optional: without it the dry outline is measured from the collision voxels.
        /// </summary>
        public DryOutlineDef DryOutline;
        /// <summary>
        /// The dry outline Sea cuts the water out of (Sea.WaterClipBox.DryProfile): Sea.WaterShader.DryLevels rows of
        /// Sea.WaterShader.DryStations half-widths from stern to bow, row j at ship-local height DryRowY0 + j * DryRowDY.
        /// Fitted so Sea's interpolation between them never falls inside the hull.
        /// </summary>
        [JsonIgnore] public float[] DryProfile;
        [JsonIgnore] public float DryRowY0, DryRowDY;
        /// <summary>
        /// The dry outline's stern end is a raked plane through the transom (Sea.WaterClipBox.DryRake): at the waterline
        /// (Height) it's the first station, and it moves this far along the keel per block up (negative: aft).
        /// </summary>
        [JsonIgnore] public float DryRake;
        /// <summary>The hull's length at the waterline, as measured from the voxels (for how she answers the waves).</summary>
        [JsonIgnore] public double WaterlineHalfLength, WaterlineCentre;

        public class DryOutlineDef
        {
            public double X0, DX, Y0, DY;
            public int NX, NY;
            public float[] HalfWidths;

            /// <summary>Bilinear between the grid's samples; 0 outside it.</summary>
            public double At(double x, double y)
            {
                double u = (x - X0) / DX, v = Math.Clamp((y - Y0) / DY, 0, NY - 1);
                if (u < 0 || u > NX - 1 || HalfWidths == null || HalfWidths.Length != NX * NY) return 0;
                int i = Math.Min(NX - 2, (int)u), j = Math.Min(NY - 2, (int)v);
                double fu = u - i, fv = v - j;
                double a = HalfWidths[j * NX + i] + (HalfWidths[j * NX + i + 1] - HalfWidths[j * NX + i]) * fu;
                double b = HalfWidths[(j + 1) * NX + i] + (HalfWidths[(j + 1) * NX + i + 1] - HalfWidths[(j + 1) * NX + i]) * fu;
                return a + (b - a) * fv;
            }
        }

        /// <summary>
        /// Measure the footprint from the collision voxels around the waterline (-0.5..1.5, where waves reach the hull):
        /// the hull's length and its half-width along it, plus a margin so no water vertex between the hull and the
        /// margin can rise (they're a block apart).
        /// </summary>
        public void Build(VoxelGrid g, double margin = 1.0)
        {
            int j0 = Math.Max(0, g.CellY(-0.5)), j1 = Math.Min(g.NY - 1, g.CellY(1.5));
            var halfAt = new double[g.NX];
            double stern = double.MaxValue, bow = double.MinValue;
            for (int i = 0; i < g.NX; i++)
            {
                halfAt[i] = -1;
                for (int j = j0; j <= j1; j++)
                    for (int k = 0; k < g.NZ; k++)
                    {
                        if (!g.Solid(i, j, k)) continue;
                        double z = Math.Max(Math.Abs(g.OZ + k * g.V), Math.Abs(g.OZ + (k + 1) * g.V));
                        halfAt[i] = Math.Max(halfAt[i], z);
                    }
                if (halfAt[i] < 0) continue;
                stern = Math.Min(stern, g.OX + i * g.V);
                bow = Math.Max(bow, g.OX + (i + 1) * g.V);
            }
            if (stern > bow) return;
            Margin = (float)margin;
            WaterlineHalfLength = (bow - stern) / 2;
            WaterlineCentre = (stern + bow) / 2;
            // the dry outline's stations: from the transom at the waterline to the stem's foremost point up to the
            // main deck, with the transom's rake (from the design's exact widths, when it gives them)
            bool exact = DryOutline?.HalfWidths != null && DryOutline.HalfWidths.Length == DryOutline.NX * DryOutline.NY && DryOutline.NX > 1 && DryOutline.NY > 1;
            Func<double, double> firstX = null;
            if (exact)
            {
                var o = DryOutline;
                firstX = y =>
                {
                    for (int c = 0; c < o.NX; c++) if (o.At(o.X0 + c * o.DX, y) > 0.05) return o.X0 + c * o.DX;
                    return double.NaN;
                };
                // (the widths ramp up over one step of the grid where a transom starts: the cut starts a step aft of the
                // first width, so it takes in all of the ramp)
                double s0 = firstX(Height) - o.DX, top = double.MinValue;
                // forward, the stations reach the stem up to the top row of the outline (a raked stem goes further forward
                // above it, well above the waterline)
                for (double y = KeelBottom; y <= Height + BowReach + 1e-9; y += 0.125)
                    for (int c = o.NX - 1; c >= 0; c--)
                        if (o.At(o.X0 + c * o.DX, y) > 0.05) { top = Math.Max(top, o.X0 + (c + 1) * o.DX); break; }
                if (!double.IsNaN(s0) && top > s0)
                {
                    // the rake: as far aft as the transom leans at any height above the waterline, but never so far that
                    // the plane cuts into the hull below it. A transom that leans aft more above the waterline than below
                    // it may leave no such plane through the first station: then the plane starts a step further aft.
                    for (int tries = 0; tries < 4; tries++, s0 -= o.DX)
                    {
                        double lo = double.MinValue, hi = double.MaxValue;
                        for (double y = KeelBottom; y <= DryTop + 1e-9; y += 0.125)
                        {
                            double f = firstX(y) - o.DX, h = y - Height;
                            if (double.IsNaN(f) || Math.Abs(h) < 1e-6) continue;
                            if (h > 0) hi = Math.Min(hi, (f - s0) / h);
                            else lo = Math.Max(lo, (f - s0) / h);
                        }
                        DryRake = hi == double.MaxValue ? 0 : (float)Math.Max(lo, Math.Min(0, hi));
                        if (lo <= Math.Min(0, hi) + 1e-9) break;
                    }
                    stern = s0;
                    bow = top;
                }
            }
            CenterX = (float)((stern + bow) / 2);
            HalfLength = (float)((bow - stern) / 2 + margin);
            Profile = new float[Stations];
            double step = 2.0 * HalfLength / (Stations - 1);
            for (int s = 0; s < Stations; s++)
            {
                double x = CenterX - HalfLength + s * step;
                // widest hull within the stretch this station stands for (the outline is interpolated between stations)
                double w = 0;
                int ia = Math.Max(0, g.CellX(x - step)), ib = Math.Min(g.NX - 1, g.CellX(x + step));
                for (int i = ia; i <= ib; i++) w = Math.Max(w, halfAt[i]);
                Profile[s] = (float)(w + margin);
            }
            HalfWidth = Profile.Max();

            // the dry outline, from the design's exact widths or else from the voxels (the widest solid cell there)
            Func<double, double, double> width;
            if (exact)
            {
                // Aft of the transom (but forward of the raked plane that ends the cut) the hull is taken as going on
                // at the transom's width: the outline runs on square to the plane, instead of swelling out past her
                // quarters to reach the transom's corners from the stations forward of it.
                double sternX = stern, rake = DryRake, h0 = Height;
                width = (x, y) =>
                {
                    if (x < sternX + rake * (y - h0) - 1e-9) return 0;
                    double f = firstX(y);
                    return double.IsNaN(f) ? 0 : DryOutline.At(Math.Max(x, f), y);
                };
            }
            else
            {
                width = (x, y) =>
                {
                    int i = g.CellX(x), j = g.CellY(y);
                    double w = 0;
                    for (int k = 0; k < g.NZ; k++)
                        if (g.Solid(i, j, k)) w = Math.Max(w, Math.Max(Math.Abs(g.OZ + k * g.V), Math.Abs(g.OZ + (k + 1) * g.V)));
                    return w;
                };
            }
            DryProfile = FitDryOutline(width, stern, bow, DryStations, DryLevels, DryRowY0 = DryRowY0Default, DryRowDY = DryRowDYDefault, KeelBottom, DryTop,
                exact ? stern + Math.Min(0, DryRake) * (DryTop - Height) : stern);
        }

        /// <summary>How far above the waterline the dry outline's stations reach the stem (a raked stem runs on forward above that).</summary>
        public static double BowReach = 1.25;

        /// <summary>The heights of the dry outline's rows (ship-local): the lowest, and the step between them.</summary>
        public const float DryRowY0Default = -0.75f, DryRowDYDefault = 1.0f;

        /// <summary>Lowest and highest ship-local heights the dry outline has to cover (Sea is told the same band).</summary>
        [JsonIgnore] public double KeelBottom = -3.0, DryTop = 4.0;

        /// <summary>
        /// Half-widths at stations x0..x1 (evenly spaced) on rows y0, y0 + dy, ... such that interpolating them linearly
        /// between stations and rows (and holding the end rows beyond them, down to bottom and up to top) never gives less
        /// than the hull's own width there, and as little more as it can. First the nodes round each spot where the
        /// interpolation comes out narrower are raised (least change first) until the hull is covered everywhere; then
        /// each node in turn is lowered as far as the spots it helps cover allow (the raising overdoes it where the hull
        /// changes fast, at a transom stern). A hair wider for single precision.
        /// </summary>
        /// <param name="spotX0">the spots to cover start here (aft of x0 the first station's widths hold: a transom raked aft)</param>
        public static float[] FitDryOutline(Func<double, double, double> width, double x0, double x1, int stations, int rows,
            double y0, double dy, double bottom, double top, double spotX0 = double.NaN)
        {
            if (double.IsNaN(spotX0)) spotX0 = x0;
            var p = new double[rows * stations];
            double sx = (x1 - x0) / (stations - 1);
            for (int j = 0; j < rows; j++)
                for (int s = 0; s < stations; s++) p[j * stations + s] = width(x0 + s * sx, y0 + j * dy);
            // the spots to cover: every 0.1 along the hull and every 0.125 up it, from the bottom to the top of the band
            var need = new System.Collections.Generic.List<double>();
            var nodes = new System.Collections.Generic.List<int>();      // four per spot
            var weights = new System.Collections.Generic.List<double>();
            for (double y = bottom; y <= top + 1e-9; y += 0.125)
            {
                double t = Math.Clamp((y - y0) / dy, 0, rows - 1);
                int j = Math.Min(rows - 2, (int)Math.Floor(t));
                double ft = t - j;
                for (double xx = spotX0; xx <= x1 + 1e-9; xx += 0.1)
                {
                    double x = Math.Min(xx, x1), w = width(x, y);
                    if (w <= 0) continue;
                    double s = Math.Clamp((x - x0) / sx, 0, stations - 1);
                    int i = Math.Min(stations - 2, (int)Math.Floor(s));
                    double fs = s - i;
                    int n00 = j * stations + i;
                    need.Add(w + 0.02);
                    nodes.AddRange(new[] { n00, n00 + 1, n00 + stations, n00 + stations + 1 });
                    weights.AddRange(new[] { (1 - fs) * (1 - ft), fs * (1 - ft), (1 - fs) * ft, fs * ft });
                }
            }
            int count = need.Count;
            double Got(int k)
            {
                double g = 0;
                for (int q = 0; q < 4; q++) g += p[nodes[4 * k + q]] * weights[4 * k + q];
                return g;
            }
            // raise until every spot is covered
            for (int pass = 0; pass < 400; pass++)
            {
                double worst = 0;
                for (int k = 0; k < count; k++)
                {
                    double lack = need[k] - Got(k);
                    if (lack <= 0) continue;
                    worst = Math.Max(worst, lack);
                    double norm = 0;
                    for (int q = 0; q < 4; q++) norm += weights[4 * k + q] * weights[4 * k + q];
                    for (int q = 0; q < 4; q++) p[nodes[4 * k + q]] += lack * weights[4 * k + q] / norm;
                }
                if (worst < 1e-4) break;
            }
            // then lower each node to just what the spots it helps cover need of it, the others as they are
            var spotsOf = new System.Collections.Generic.List<int>[p.Length];
            for (int n = 0; n < p.Length; n++) spotsOf[n] = new System.Collections.Generic.List<int>();
            for (int k = 0; k < count; k++)
                for (int q = 0; q < 4; q++)
                    if (weights[4 * k + q] > 1e-6) spotsOf[nodes[4 * k + q]].Add(4 * k + q);
            for (int pass = 0; pass < 40; pass++)
            {
                double change = 0;
                for (int n = 0; n < p.Length; n++)
                {
                    double lowest = 0;
                    foreach (int kq in spotsOf[n])
                    {
                        int k = kq / 4;
                        double rest = Got(k) - p[n] * weights[kq];
                        lowest = Math.Max(lowest, (need[k] - rest) / weights[kq]);
                    }
                    change = Math.Max(change, Math.Abs(p[n] - lowest));
                    p[n] = lowest;
                }
                if (change < 1e-4) break;
            }
            var result = new float[p.Length];
            for (int n = 0; n < p.Length; n++) result[n] = (float)(p[n] + 0.005);
            return result;
        }

        /// <summary>Footprint half-width at ship-local x (0 outside its length).</summary>
        public double HalfWidthAt(double x)
        {
            if (Profile == null) return HalfWidth;
            double u = (x - CenterX) / HalfLength;
            if (u < -1 || u > 1) return 0;
            double s = (u + 1) * (Stations - 1) / 2;
            int i = Math.Min(Stations - 2, (int)Math.Floor(s));
            return Profile[i] + (Profile[i + 1] - Profile[i]) * (s - i);
        }
    }

    /// <summary>The ship's block grid: a block border's corner (ship-local x, y, z); the others whole blocks from it.</summary>
    public class BlockGridDef
    {
        public double[] Origin;

        /// <summary>The grid's offset (0..1) on an axis: where its block borders fall within a block.</summary>
        public double Frac(int axis)
        {
            if (Origin == null || Origin.Length <= axis) return 0;
            double f = Origin[axis] - Math.Floor(Origin[axis]);
            return f > 1 - 1e-9 ? 0 : f;
        }
    }

    public class ShipShapes
    {
        public string Exterior;
        public string Interior;
        /// <summary>Mesh (planked hull skin, round masts, rigging): config/shipmeshes/*.json</summary>
        public string Skin;
    }

    /// <summary>
    /// Something to climb along a straight path from Bottom to Top: the ratlines (from the bulwark to just under the
    /// top), a rope ladder down the side (Outside: its bottom is in the water, and getting off there leaves the ship;
    /// Gate: the entry port gate that has to be open to get on or off at the top), or the companionway's ladder.
    /// </summary>
    public class LadderDef
    {
        public string Code;
        /// <summary>ratlines, rope (down the side) or ladder (the companionway): what the hints call it.</summary>
        public string Kind;
        public double[] Bottom, Top, ExitBottom, ExitTop;
        public bool Outside;
        public string Gate;
        /// <summary>
        /// (0.3.12, note 110) A hatch the ladder passes through on its way up (the hold's hatch, for the ladder from the
        /// cargo deck up to the glazed waist hatch), and the height of its opening (ship-local y): while it's shut you
        /// can't climb past it.
        /// </summary>
        public string Through;
        public double ThroughY = double.NaN;

        public string KindOrDefault => Kind ?? (Outside ? "rope" : "ratlines");
    }

    /// <summary>
    /// A mesh group ("part:" + Code, its glass in "glass:" + Code) moved by a ship value:
    /// - rotate (default): turned about an axis, Angle degrees at value 1 (the wheel, the rudder and tiller, the gates
    ///   and hatches);
    /// - slide: moved Distance blocks along AxisDir at value 1 (the anchor going down, a gun running back);
    /// - follow: a rope from a fixed Anchor to Tip, where Tip goes with the part named by Follow (the tiller ropes, the
    ///   anchor cable, a gun's breeching).
    /// </summary>
    public class MovingPartDef
    {
        public string Code;
        public string Kind = "rotate";
        public double[] AxisPoint;
        public double[] AxisDir;
        /// <summary>
        /// What moves it: "rudder" (-1..1), "anchor" (0 up .. 1 down), "open:" + a door, gate or hatch (0..1), "fire:" +
        /// a gun (0 run out .. 1 run back, as it recoils: <see cref="PartMotion.Recoil"/>).
        /// </summary>
        public string Source = "rudder";
        /// <summary>rotate, slide: the part it rides on, moved first (a gun's trucks turn on its carriage as it runs back); listed before it.</summary>
        public string Parent;
        /// <summary>rotate: degrees at value 1.</summary>
        public float Angle = 30;
        /// <summary>rotate: never turned further than this many degrees either way (0 = no limit): the lateen, stopped by the mizzen's shrouds.</summary>
        public float Limit;
        /// <summary>slide: blocks at value 1.</summary>
        public float Distance;
        /// <summary>follow: the part whose movement the rope's tip follows, and the rope's two ends (ship-local).</summary>
        public string Follow;
        public double[] Anchor, Tip;
        /// <summary>follow: a sheet's tip is on its sail's clew, which goes up to the yard as the sail is furled.</summary>
        public string Sail;
        /// <summary>follow: the rope's other end is on this part and moves with it (the topsail's sheets, on the course yard).</summary>
        public string AnchorFollow;
        /// <summary>Below decks: drawn with the interior's light.</summary>
        public bool Interior;
        /// <summary>
        /// Drawn only while: "manned:" / "unmanned:" + a seat's fixture (an oar out while someone rows, stowed
        /// otherwise), "open:" / "shut:" + a fixture (a boat's sail only while her mast is up). Null: always.
        /// </summary>
        public string Shown;
    }

    public class RoomDef
    {
        public string Code;
        public double[] Min, Max;
        public bool Enclosed = true;
        /// <summary>(0.3.16, note 154) Its walls are (partly) placed pieces: it's sealed only when they close it (a flood fill every time).</summary>
        public bool Pieces;
    }

    /// <summary>The frames (ribs): the first one's centre along the keel (ship-local x), and the space between two.</summary>
    public class FramesDef
    {
        public double X0;
        public double Space = 1.5;
        public int Count;
    }

    /// <summary>A deck (or the main top) you stand on: its top (ship-local y) and how far it runs along the keel.</summary>
    public class DeckDef
    {
        /// <summary>hold (the cargo deck), lower, main, quarter, forecastle, poop, top: what it's called ("hansa:deck-" + Code).</summary>
        public string Code;
        public double Y;
        public double X0 = -1000, X1 = 1000;
    }

    /// <summary>A mast: its foot's centre (ship-local x, z), its radius there, and from where to where it stands.</summary>
    public class MastDef
    {
        /// <summary>main, fore, mizzen ("hansa:mast-" + Code).</summary>
        public string Code;
        public double X, Z, Radius = 0.4, Y0, Y1;
    }

    /// <summary>
    /// (0.3.21, step C; docs/slots.md §7) A place a gun may be mounted on: its gun fixture (the port, the carriage's place,
    /// the lays and limits, the seat, the lid, its moving parts), the deck cells it needs filled, whether a new ship has its
    /// gun, and the item it takes and gives back.
    /// </summary>
    public class GunPlaceDef
    {
        public string Id;
        public string Gun;
        public string[] Cells;
        /// <summary>"laid" (default): a new ship has the gun mounted; "empty": a place without a gun.</summary>
        public string Start;
        public string Block;
        /// <summary>
        /// (0.3.28, step D2.4; docs/slots.md §11) The id of its port's cover (a deck slot of kind "port"), or null: its port is
        /// always there. Filled, the port is planked over: no lid, no gun mounted; empty, it's the lidded port.
        /// </summary>
        public string Port;
        /// <summary>(0.3.29) A castle port's port itself (a deck slot of kind "gunport"): the gun needs it in.</summary>
        public string GunPort;

        [JsonIgnore] public int Index;
        [JsonIgnore] public bool StartsLaid => Start != "empty";
        [JsonIgnore] public string BlockCode => string.IsNullOrEmpty(Block) ? DefaultBlock : Block;
        public const string DefaultBlock = "hansa:gun-bronze-north";
    }

    public class FixtureDef
    {
        /// <summary>
        /// helm, anchor (the capstan), storage (the cargo hold), door (a vanilla door), gate (a hinged section of the
        /// bulwark), hatch (opens like a door, lying flat), window (a vanilla glass pane), attach (a spot where the crew
        /// puts a vanilla lantern, chest, bed or boat), bed (a built-in bunk, drawn with a vanilla bed), prop (a vanilla
        /// block drawn for show), oars (a boat's rowing thwart: sit and row), mast (a boat's mast that folds down: "open"
        /// is lowered), gun (a cannon on its carriage: hold right mouse to lay it, left-click fires it), hearth (the ship's firehearth: vanilla's firepit
        /// rebuilt aboard, see <see cref="Fire"/> and ShipHearth), and the older stove.
        /// </summary>
        public string Type;
        public string Code;
        public double[] Pos;
        public float Yaw;        // degrees, around +Y, 0 = facing the bow, 90 = facing port
        public double[] Sel;     // x0 y0 z0 x1 y1 z1
        public float Efficiency = 0.7f;
        public int Slots = 16;
        public float Heat;
        public string Room;

        /// <summary>
        /// attach: lantern, chest, bed, boat, cargo (a chest, a trunk, a storage vessel or a crate in the hold), or plate (a
        /// vanilla metal plate on a wall: bind your spawn to the ship there). bed: "bed".
        /// </summary>
        public string Kind;
        /// <summary>attach cargo: the spot next to this one; a trunk (a double chest) takes both.</summary>
        public string Pair;
        /// <summary>figurehead: the figureheads there are to choose from, and the mesh group each is drawn with ("" = none).</summary>
        public string[] Options, Groups;
        /// <summary>nameboard: the board's face (Pos its centre): which way the text runs, which way is up, its outward direction, and the space for the name.</summary>
        public double[] Right, Up, Out;
        public float Width, Height;
        /// <summary>attach lantern: "down" hangs from a hook at Pos, "up" stands on Pos.</summary>
        public string Hang = "down";
        /// <summary>
        /// attach: the crosshair finds it even empty and with nothing for it in hand (other empty places only show up
        /// holding what goes there): the stern lantern's, which says what it's for.
        /// </summary>
        public bool Shown;
        /// <summary>
        /// door, window, prop: the vanilla block drawn (a gate without one is drawn by the design itself, as a moving
        /// part), and an older gate's open block. bed: the vanilla bed ("game:bed-wood") it's drawn with.
        /// </summary>
        public string Block, BlockOpen;
        /// <summary>
        /// door, gate, hatch, window: the opening it fills (x0 y0 z0 x1 y1 z1), solid while closed. attach boat: the
        /// space the boat takes.
        /// </summary>
        public double[] Box;
        /// <summary>door, gate, window: the axis ("x" or "z") the opening faces along.</summary>
        public string Normal = "x";
        /// <summary>door: the hinge line (x, z) and how far it swings open (degrees, sign = direction).</summary>
        public double[] Hinge;
        public float OpenAngle = 90;
        /// <summary>door, gate, hatch: while it stands open, this room isn't a closed room (the waist hatch and the lower deck).</summary>
        public string Unseals;
        /// <summary>hatch: the storage it gives onto; using the hatch opens it (the hold hatch).</summary>
        public string Storage;
        /// <summary>prop: how big the vanilla block is drawn.</summary>
        public float Scale = 1;
        /// <summary>helm, oars: "sit" (on a thwart or the stern sheets) instead of standing at a wheel.</summary>
        public string Pose;
        /// <summary>door, gate, hatch, mast: open on a new ship (a boat's mast starts lowered).</summary>
        public bool StartOpen;
        /// <summary>attach plate: where someone whose spawn is bound to her stands when they come back (ship-local feet).</summary>
        public double[] Spawn;
        /// <summary>
        /// gun: where the shot leaves the muzzle, run out and laid square (ship-local); Out (above) is the level way the
        /// barrel points then; how a new ship's gun is laid (degrees up), what it's cast of ("bronze": a wreck gives it back
        /// whole), its bore (blocks) and the weight of its shot (kg). Its moving parts are its own code (it runs back,
        /// source "fire:"), code + "-train" (it trains, "train:"), code + "-barrel" (it elevates, "elevate:"); Box is where
        /// it stands, solid.
        /// </summary>
        public double[] Muzzle;
        public float Elevation;
        public string Material;
        public float Bore = 0.14f, Shot;
        /// <summary>
        /// gun, laying it (<see cref="GunLaying"/>): the point on the upright it trains about (in its port), the point on
        /// the trunnions' line it elevates about (across the barrel), where the gun captain stands (feet, laid square, run
        /// out); how far it trains (degrees, + toward her bow: [aft, forward]) and is elevated ([down, up]), and how fast
        /// (degrees a second). For the offline checks: its port (x0 y0 x1 y1) and her side's faces there, out from the
        /// centreline (inside at the sill and the head, outside at the sill and the head).
        /// </summary>
        public double[] Pivot, Trunnion, Seat, Port, Wall;
        public float[] TrainLimits = { -12, 12 }, ElevationLimits = { -5, 15 };
        public float TrainRate = 10, ElevationRate = 6;
        /// <summary>
        /// gun: an entry port's gate it's next to, and how far it trains while that stands open (its leaf swung in where the
        /// gun's trail would go); the gate won't open while the gun is trained further.
        /// </summary>
        public string Gate;
        public float[] TrainLimitsGateOpen;
        /// <summary>
        /// gun: its port's lid (the moving part's code; its source "open:" + the gun's code): the gun is housed (run in, the
        /// lid shut: it can't fire) or run out with the port open (its code among the open fittings). Null: an open port.
        /// </summary>
        public string Lid;
        /// <summary>
        /// hearth (ship-local; Pos is where the vanilla firepit block's bottom centre stands, on the sunken firebed; Sel
        /// the firebed and the fire over it; Box the hearth's bricks; Room the room it warms): the fire's middle (its
        /// light), where a pot's bottom sits on the fire (vanilla's inFirePitProps, 1/8 over the block's bottom; unused: a
        /// pot is drawn as vanilla's renderer draws it, from the firepit block), and the stovepipe's mouth under its rain cap
        /// (the smoke).
        /// </summary>
        public double[] Fire, Cook, Chimney;
        /// <summary>
        /// pump: where the water comes out of its spout (ship-local) and the way it leaves it (a unit vector): what the
        /// pump takes out of her hold gushes from there onto the deck (<see cref="PumpJet"/>). Without them, 0.65 over Pos
        /// and 0.45 to its right (its facing turned a quarter to starboard), pointing that way and a little down.
        /// </summary>
        public double[] Spout, SpoutDir;
        /// <summary>seat (the heads): Pos is the middle of the seat's top (where the sitter is); Feet the deck spot in front of it, where getting up puts you.</summary>
        public double[] Feet;

        [JsonIgnore] public int Index;

        public Cuboidd SelBox => Sel == null ? null : new Cuboidd(Sel[0], Sel[1], Sel[2], Sel[3], Sel[4], Sel[5]);
        public Vec3d PosVec => new Vec3d(Pos[0], Pos[1], Pos[2]);
        public bool Opens => Type == "door" || Type == "gate" || Type == "hatch" || Type == "mast";

        /// <summary>Unit direction (ship-local x, z) the fixture faces: yaw 0 = bow, 90 = port.</summary>
        public (double X, double Z) Facing
        {
            get
            {
                double a = Yaw * Math.PI / 180;
                return (Math.Cos(a), -Math.Sin(a));
            }
        }
    }

    public class LightDef
    {
        public double[] Pos;
        public float[] Rgb = { 1f, 0.78f, 0.45f };   // 0..1
        public float Strength = 7;                   // like a block light level
        public bool Interior;
    }

    public class SailDef
    {
        public string Code;
        public string Shape;
        /// <summary>A sail drawn as a smooth mesh (the ship's skin group of this name) instead of a shape (the lateen).</summary>
        public string Mesh;
        public double[] AxisPoint;
        public double[] AxisDir;
        public float Area;
        /// <summary>Where its shape is drawn from where the shape file has it (a grid ship's rig moved with its mast).</summary>
        [JsonIgnore] public double[] Offset;
        /// <summary>The fixture of the mast it's set on, if that folds down: lowered, the sail isn't drawn and doesn't drive her.</summary>
        public string Mast;
    }

    /// <summary>Solid voxels of a ship in ship-local space.</summary>
    public class VoxelGrid
    {
        public readonly double OX, OY, OZ;
        public readonly int NX, NY, NZ;
        public readonly double V;
        readonly byte[] bits;

        public VoxelGrid Clone() => new VoxelGrid(OX, OY, OZ, NX, NY, NZ, V, (byte[])bits.Clone());

        /// <summary>Make this grid a copy of another of the same size (cheap: one array copy).</summary>
        public void CopyFrom(VoxelGrid other) => Buffer.BlockCopy(other.bits, 0, bits, 0, bits.Length);

        /// <summary>Set every cell whose centre lies in the box (ship-local x0 y0 z0 x1 y1 z1).</summary>
        public void Fill(double[] box, bool solid = true)
        {
            int i0 = Math.Max(0, (int)Math.Ceiling((box[0] - OX) / V - 0.5)), i1 = Math.Min(NX - 1, (int)Math.Floor((box[3] - OX) / V - 0.5));
            int j0 = Math.Max(0, (int)Math.Ceiling((box[1] - OY) / V - 0.5)), j1 = Math.Min(NY - 1, (int)Math.Floor((box[4] - OY) / V - 0.5));
            int k0 = Math.Max(0, (int)Math.Ceiling((box[2] - OZ) / V - 0.5)), k1 = Math.Min(NZ - 1, (int)Math.Floor((box[5] - OZ) / V - 0.5));
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    for (int k = k0; k <= k1; k++)
                    {
                        long n = ((long)i * NY + j) * NZ + k;
                        if (solid) bits[n >> 3] |= (byte)(1 << (int)(n & 7));
                        else bits[n >> 3] &= (byte)~(1 << (int)(n & 7));
                    }
        }

        /// <summary>The bounds of the cells <see cref="Fill"/> sets for this box (x0 y0 z0 x1 y1 z1), or null if none.</summary>
        public double[] CellsOf(double[] box)
        {
            int i0 = Math.Max(0, (int)Math.Ceiling((box[0] - OX) / V - 0.5)), i1 = Math.Min(NX - 1, (int)Math.Floor((box[3] - OX) / V - 0.5));
            int j0 = Math.Max(0, (int)Math.Ceiling((box[1] - OY) / V - 0.5)), j1 = Math.Min(NY - 1, (int)Math.Floor((box[4] - OY) / V - 0.5));
            int k0 = Math.Max(0, (int)Math.Ceiling((box[2] - OZ) / V - 0.5)), k1 = Math.Min(NZ - 1, (int)Math.Floor((box[5] - OZ) / V - 0.5));
            if (i1 < i0 || j1 < j0 || k1 < k0) return null;
            return new[] { OX + i0 * V, OY + j0 * V, OZ + k0 * V, OX + (i1 + 1) * V, OY + (j1 + 1) * V, OZ + (k1 + 1) * V };
        }

        public VoxelGrid(double ox, double oy, double oz, int nx, int ny, int nz, double v, byte[] bits)
        {
            OX = ox; OY = oy; OZ = oz; NX = nx; NY = ny; NZ = nz; V = v; this.bits = bits;
        }

        /// <summary>
        /// Cells below topY that are inside the hull: flood the empty cells from the grid's sides and bottom (not its top
        /// at topY, where the hatches open), and take every cell the flood can't reach, plus the solid ones.
        /// </summary>
        public static VoxelGrid InsideMask(VoxelGrid g, double topY)
        {
            int nx = g.NX, nz = g.NZ, band = Math.Clamp(g.CellY(topY), 0, g.NY);
            var reached = new bool[nx * band * nz];
            var queue = new System.Collections.Generic.Queue<int>();
            void Seed(int i, int j, int k)
            {
                if (i < 0 || j < 0 || k < 0 || i >= nx || j >= band || k >= nz || g.Solid(i, j, k)) return;
                int n = (i * band + j) * nz + k;
                if (reached[n]) return;
                reached[n] = true;
                queue.Enqueue(n);
            }
            for (int j = 0; j < band; j++)
            {
                for (int k = 0; k < nz; k++) { Seed(0, j, k); Seed(nx - 1, j, k); }
                for (int i = 0; i < nx; i++) { Seed(i, j, 0); Seed(i, j, nz - 1); }
            }
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++) Seed(i, 0, k);
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                int k = n % nz, j = n / nz % band, i = n / nz / band;
                Seed(i + 1, j, k); Seed(i - 1, j, k);
                Seed(i, j + 1, k); Seed(i, j - 1, k);
                Seed(i, j, k + 1); Seed(i, j, k - 1);
            }
            var bits = new byte[(nx * (long)g.NY * nz + 7) / 8];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < band; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        if (!g.Solid(i, j, k) && reached[(i * band + j) * nz + k]) continue;
                        long n = ((long)i * g.NY + j) * nz + k;
                        bits[n >> 3] |= (byte)(1 << (int)(n & 7));
                    }
            return new VoxelGrid(g.OX, g.OY, g.OZ, nx, g.NY, nz, g.V, bits);
        }

        public static VoxelGrid Decode(double[] origin, int[] size, double v, string b64)
        {
            byte[] packed = Convert.FromBase64String(b64);
            using var ms = new MemoryStream(packed);
            using var z = new ZLibStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            z.CopyTo(outMs);
            return new VoxelGrid(origin[0], origin[1], origin[2], size[0], size[1], size[2], v, outMs.ToArray());
        }

        public bool Solid(int i, int j, int k)
        {
            if (i < 0 || j < 0 || k < 0 || i >= NX || j >= NY || k >= NZ) return false;
            long n = ((long)i * NY + j) * NZ + k;
            return (bits[n >> 3] >> (int)(n & 7) & 1) != 0;
        }

        public int CellX(double x) => (int)Math.Floor((x - OX) / V);
        public int CellY(double y) => (int)Math.Floor((y - OY) / V);
        public int CellZ(double z) => (int)Math.Floor((z - OZ) / V);

        public double MaxX => OX + NX * V;
        public double MaxY => OY + NY * V;
        public double MaxZ => OZ + NZ * V;

        public bool SolidAt(double x, double y, double z) => Solid(CellX(x), CellY(y), CellZ(z));

        /// <summary>
        /// (0.3.13) This grid grown by n cells every way (a cell is solid if any within n cells along each axis is), on a
        /// grid n cells bigger all round so nothing is cut off at its edges.
        /// </summary>
        public VoxelGrid Dilated(int n)
        {
            if (n <= 0) return Clone();
            int nx = NX + 2 * n, ny = NY + 2 * n, nz = NZ + 2 * n;
            var a = new bool[nx * ny * nz];
            int Ix(int i, int j, int k) => (i * ny + j) * nz + k;
            for (int i = 0; i < NX; i++)
                for (int j = 0; j < NY; j++)
                    for (int k = 0; k < NZ; k++)
                        if (Solid(i, j, k)) a[Ix(i + n, j + n, k + n)] = true;
            // separable: along z, then y, then x, each a running window of 2n + 1
            var b = new bool[a.Length];
            void Pass(bool[] src, bool[] dst, int axis)
            {
                int len = axis == 0 ? nx : axis == 1 ? ny : nz;
                int o1 = axis == 0 ? ny : nx, o2 = axis == 2 ? ny : nz;
                for (int p = 0; p < o1; p++)
                    for (int q = 0; q < o2; q++)
                    {
                        int I(int t) => axis == 0 ? Ix(t, p, q) : axis == 1 ? Ix(p, t, q) : Ix(p, q, t);
                        int count = 0;
                        for (int t = 0; t < Math.Min(n, len); t++) if (src[I(t)]) count++;
                        for (int t = 0; t < len; t++)
                        {
                            if (t + n < len && src[I(t + n)]) count++;
                            if (t - n - 1 >= 0 && src[I(t - n - 1)]) count--;
                            dst[I(t)] = count > 0;
                        }
                    }
            }
            Pass(a, b, 2);
            Pass(b, a, 1);
            Pass(a, b, 0);
            var bits = new byte[(nx * (long)ny * nz + 7) / 8];
            for (long m = 0; m < b.Length; m++)
                if (b[m]) bits[m >> 3] |= (byte)(1 << (int)(m & 7));
            return new VoxelGrid(OX - n * V, OY - n * V, OZ - n * V, nx, ny, nz, V, bits);
        }

        public int CountSolid()
        {
            int c = 0;
            foreach (byte b in bits) for (int i = 0; i < 8; i++) c += (b >> i) & 1;
            return c;
        }

        /// <summary>Does the box (ship-local) overlap any solid voxel? Boxes touching a voxel face don't count.</summary>
        public bool Collides(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            const double e = 1e-6;
            int i0 = CellX(x0 + e), i1 = CellX(x1 - e);
            int j0 = CellY(y0 + e), j1 = CellY(y1 - e);
            int k0 = CellZ(z0 + e), k1 = CellZ(z1 - e);
            if (i1 < 0 || j1 < 0 || k1 < 0 || i0 >= NX || j0 >= NY || k0 >= NZ) return false;
            i0 = Math.Max(i0, 0); j0 = Math.Max(j0, 0); k0 = Math.Max(k0, 0);
            i1 = Math.Min(i1, NX - 1); j1 = Math.Min(j1, NY - 1); k1 = Math.Min(k1, NZ - 1);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    for (int k = k0; k <= k1; k++)
                        if (Solid(i, j, k)) return true;
            return false;
        }

        /// <summary>
        /// Moves a box along one axis by delta, stopping at the first solid voxel. Returns the distance actually moved.
        /// </summary>
        public double Sweep(int axis, double delta, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            if (delta == 0) return 0;
            const double e = 1e-6;
            double[] lo = { x0, y0, z0 }, hi = { x1, y1, z1 };
            double[] o = { OX, OY, OZ };
            int[] n = { NX, NY, NZ };

            // cells spanned on the two other axes
            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
            int p0 = (int)Math.Floor((lo[a1] + e - o[a1]) / V), p1 = (int)Math.Floor((hi[a1] - e - o[a1]) / V);
            int q0 = (int)Math.Floor((lo[a2] + e - o[a2]) / V), q1 = (int)Math.Floor((hi[a2] - e - o[a2]) / V);
            if (p1 < 0 || q1 < 0 || p0 >= n[a1] || q0 >= n[a2]) return delta;
            p0 = Math.Max(p0, 0); q0 = Math.Max(q0, 0); p1 = Math.Min(p1, n[a1] - 1); q1 = Math.Min(q1, n[a2] - 1);

            int[] c = new int[3];
            if (delta > 0)
            {
                int s0 = (int)Math.Floor((hi[axis] - e - o[axis]) / V) + 1;   // first cell ahead of the box
                int s1 = (int)Math.Floor((hi[axis] + delta - e - o[axis]) / V);
                s0 = Math.Max(s0, 0); s1 = Math.Min(s1, n[axis] - 1);
                for (int s = s0; s <= s1; s++)
                {
                    c[axis] = s;
                    for (int p = p0; p <= p1; p++)
                        for (int q = q0; q <= q1; q++)
                        {
                            c[a1] = p; c[a2] = q;
                            if (Solid(c[0], c[1], c[2])) return Math.Max(0, o[axis] + s * V - hi[axis]);
                        }
                }
                return delta;
            }
            else
            {
                int s0 = (int)Math.Floor((lo[axis] + e - o[axis]) / V) - 1;   // first cell behind the box
                int s1 = (int)Math.Floor((lo[axis] + delta + e - o[axis]) / V);
                s0 = Math.Min(s0, n[axis] - 1); s1 = Math.Max(s1, 0);
                for (int s = s0; s >= s1; s--)
                {
                    c[axis] = s;
                    for (int p = p0; p <= p1; p++)
                        for (int q = q0; q <= q1; q++)
                        {
                            c[a1] = p; c[a2] = q;
                            if (Solid(c[0], c[1], c[2])) return Math.Min(0, o[axis] + (s + 1) * V - lo[axis]);
                        }
                }
                return delta;
            }
        }

        /// <summary>Distance along a ray (ship-local, dir normalised) to the first solid voxel, or double.MaxValue.</summary>
        public double Raycast(Vec3d from, Vec3d dir, double maxDist)
        {
            // Amanatides & Woo voxel traversal, starting where the ray enters the grid
            double tEnter = 0, tExit = maxDist;
            double[] p = { from.X, from.Y, from.Z }, d = { dir.X, dir.Y, dir.Z };
            double[] lo = { OX, OY, OZ }, hi = { MaxX, MaxY, MaxZ };
            for (int a = 0; a < 3; a++)
            {
                if (Math.Abs(d[a]) < 1e-12)
                {
                    if (p[a] < lo[a] || p[a] >= hi[a]) return double.MaxValue;
                    continue;
                }
                double t0 = (lo[a] - p[a]) / d[a], t1 = (hi[a] - p[a]) / d[a];
                if (t0 > t1) (t0, t1) = (t1, t0);
                tEnter = Math.Max(tEnter, t0);
                tExit = Math.Min(tExit, t1);
            }
            if (tEnter > tExit) return double.MaxValue;

            double t = tEnter + 1e-7;
            int[] cell = new int[3], step = new int[3];
            double[] tMax = new double[3], tDelta = new double[3];
            int[] n = { NX, NY, NZ };
            for (int a = 0; a < 3; a++)
            {
                double pos = p[a] + d[a] * t;
                cell[a] = Math.Clamp((int)Math.Floor((pos - lo[a]) / V), 0, n[a] - 1);
                if (d[a] > 0) { step[a] = 1; tMax[a] = t + (lo[a] + (cell[a] + 1) * V - pos) / d[a]; tDelta[a] = V / d[a]; }
                else if (d[a] < 0) { step[a] = -1; tMax[a] = t + (lo[a] + cell[a] * V - pos) / d[a]; tDelta[a] = -V / d[a]; }
                else { step[a] = 0; tMax[a] = double.MaxValue; tDelta[a] = double.MaxValue; }
            }
            while (t <= tExit)
            {
                if (Solid(cell[0], cell[1], cell[2])) return t;
                int ax = tMax[0] < tMax[1] ? (tMax[0] < tMax[2] ? 0 : 2) : (tMax[1] < tMax[2] ? 1 : 2);
                t = tMax[ax];
                cell[ax] += step[ax];
                if (cell[ax] < 0 || cell[ax] >= n[ax]) return double.MaxValue;
                tMax[ax] += tDelta[ax];
            }
            return double.MaxValue;
        }

        /// <summary>
        /// Like <see cref="Raycast"/>, and which cell the ray meets and through which of its faces it came in: axis 0 (x),
        /// 1 (y) or 2 (z), the face looking back along the ray; -1 if the ray starts in that cell. skipStart: a ray that
        /// starts inside solid cells (an eye against a wall) goes on until it has left them, instead of meeting them at once.
        /// </summary>
        public double RaycastCell(Vec3d from, Vec3d dir, double maxDist, out int ci, out int cj, out int ck, out int axis, bool skipStart = false)
        {
            ci = cj = ck = axis = -1;
            double tEnter = 0, tExit = maxDist;
            int enterAxis = -1;
            double[] p = { from.X, from.Y, from.Z }, d = { dir.X, dir.Y, dir.Z };
            double[] lo = { OX, OY, OZ }, hi = { MaxX, MaxY, MaxZ };
            for (int a = 0; a < 3; a++)
            {
                if (Math.Abs(d[a]) < 1e-12)
                {
                    if (p[a] < lo[a] || p[a] >= hi[a]) return double.MaxValue;
                    continue;
                }
                double t0 = (lo[a] - p[a]) / d[a], t1 = (hi[a] - p[a]) / d[a];
                if (t0 > t1) (t0, t1) = (t1, t0);
                if (t0 > tEnter) { tEnter = t0; enterAxis = a; }
                tExit = Math.Min(tExit, t1);
            }
            if (tEnter > tExit) return double.MaxValue;

            double t = tEnter + 1e-7;
            int[] cell = new int[3], step = new int[3];
            double[] tMax = new double[3], tDelta = new double[3];
            int[] n = { NX, NY, NZ };
            for (int a = 0; a < 3; a++)
            {
                double pos = p[a] + d[a] * t;
                cell[a] = Math.Clamp((int)Math.Floor((pos - lo[a]) / V), 0, n[a] - 1);
                if (d[a] > 0) { step[a] = 1; tMax[a] = t + (lo[a] + (cell[a] + 1) * V - pos) / d[a]; tDelta[a] = V / d[a]; }
                else if (d[a] < 0) { step[a] = -1; tMax[a] = t + (lo[a] + cell[a] * V - pos) / d[a]; tDelta[a] = -V / d[a]; }
                else { step[a] = 0; tMax[a] = double.MaxValue; tDelta[a] = double.MaxValue; }
            }
            int last = tEnter > 0 ? enterAxis : -1;
            bool leaving = skipStart && last < 0;
            while (t <= tExit)
            {
                bool solid = Solid(cell[0], cell[1], cell[2]);
                if (solid && !leaving)
                {
                    ci = cell[0]; cj = cell[1]; ck = cell[2]; axis = last;
                    return Math.Max(0, t - 1e-7);
                }
                if (!solid) leaving = false;
                int ax = tMax[0] < tMax[1] ? (tMax[0] < tMax[2] ? 0 : 2) : (tMax[1] < tMax[2] ? 1 : 2);
                t = tMax[ax];
                cell[ax] += step[ax];
                last = ax;
                if (cell[ax] < 0 || cell[ax] >= n[ax]) return double.MaxValue;
                tMax[ax] += tDelta[ax];
            }
            return double.MaxValue;
        }
    }

    /// <summary>(0.3.29) What a layout revision changed: each deck slot's start before it ("filled"/"empty"), and whether her castle ports became a board and a port (a ship laid before: a port in wherever its board is out).</summary>
    public class LayoutStepDef
    {
        public int Rev;
        public Dictionary<string, string> Cells;
        public bool GunPorts;
    }
}
