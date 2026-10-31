using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// (0.3.20, step B; docs/slots.md) Her deck cells: each one's state (in, cracked, taken up), saved as the differences
    /// from the design in WatchedAttributes "hansaSlots" and sent to every client; in her collision (a taken-up cell's
    /// voxels gone: people fall through, nothing stands there); taken up with an axe and laid again with planks in building
    /// mode; an open cell (taken up, no shut hatch over it) opens the rooms over and under it, lets the rain and the sea
    /// through; hatches (pieces of vanilla trapdoors) over openings. A design without deck slots has none of this.
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>Her deck cells' states (null: her design has none).</summary>
        public SlotStates Slots;
        public DeckSlotLayout SlotLayout => Def?.Slots;
        /// <summary>How long left mouse is held on a deck cell with an axe to take it up (s).</summary>
        public const float CellSeconds = 1.0f;

        /// <summary>Her design's collision with her deck cells and shut hatches as they are (what's hers, not what's put aboard): seeing through an opening.</summary>
        public VoxelGrid DesignGrid => hullGrid ?? Def?.Grid;
        VoxelGrid hullGrid;
        readonly Dictionary<string, List<double[]>> cellLeakCache = new();

        void InitSlots()
        {
            // ((0.3.26, step D1) and her hull panels: both families' differences in "hansaSlots")
            var t = WatchedAttributes.GetTreeAttribute("hansaSlots");
            if (Def?.Slots != null)
            {
                Slots = new SlotStates(Def.Slots);
                Slots.FromTree(t);
            }
            if (Def?.Hull != null)
            {
                HullPanels = new SlotStates(Def.Hull);
                HullPanels.FromTree(t);
            }
            if ((Slots != null || HullPanels != null) && Api.Side == EnumAppSide.Client) WatchedAttributes.RegisterModifiedListener("hansaSlots", LoadSlots);
            if (Api.Side == EnumAppSide.Server) MigrateLayout(t);
        }

        /// <summary>
        /// (0.3.29, docs/modular-pieces.md §14) A ship laid at an older layout revision of her design: her deck slots keep the
        /// states they had where a later revision changed their start (the old companion's well stays open, the new one's
        /// cells stay in); 0.3.28's port covers (filled: planked over) read as ports in her side's panels (filled: the port
        /// in), their halves with them; the modular entries the revisions brought are remembered as laid without being put
        /// (her own build isn't touched). A new ship is at her design's revision.
        /// </summary>
        void MigrateLayout(ITreeAttribute saved)
        {
            if (Def == null || Def.LayoutRev <= 0) return;
            int rev = WatchedAttributes.GetInt("hansaLayoutRev", 0);
            if (rev >= Def.LayoutRev) return;
            var laidTree = WatchedAttributes.GetTreeAttribute("hansaLaid");
            if (laidTree != null)
            {
                var savedStates = new Dictionary<string, int>();
                if (saved != null) foreach (var kv in saved) savedStates[kv.Key] = saved.GetInt(kv.Key);
                var laid = new HashSet<string>(laidTree.Select(kv => kv.Key));
                var r = LayoutMigration.Apply(Def, rev, savedStates, Slots, HullPanels, laid);
                foreach (string k in r.NewlyLaid) laidTree.SetBool(k, true);
                WatchedAttributes.MarkPathDirty("hansaLaid");
                SaveSlots();
                World.Logger.Audit("[ships] {0} #{1}: her layout from revision {2} to {3}: kept {4}; {5} new pieces of her design not put (she keeps her own)",
                    GetName(), EntityId, rev, Def.LayoutRev, r.Notes.Count == 0 ? "nothing to keep" : string.Join(", ", r.Notes), r.NewlyLaid.Count);
            }
            WatchedAttributes.SetInt("hansaLayoutRev", Def.LayoutRev);
        }

        void LoadSlots()
        {
            if (Slots == null && HullPanels == null) return;
            var t = WatchedAttributes.GetTreeAttribute("hansaSlots");
            Slots?.FromTree(t);
            HullPanels?.FromTree(t);
            RebuildGrid();
        }

        void SaveSlots()
        {
            if (Slots == null && HullPanels == null) return;
            var t = new TreeAttribute();
            Slots?.ToTree(t, false);
            HullPanels?.ToTree(t, false);
            WatchedAttributes["hansaSlots"] = t;
            WatchedAttributes.MarkPathDirty("hansaSlots");
        }

        /// <summary>Her collision: the cells' states on the design's (RebuildGrid, after her fittings, before her pieces).</summary>
        void ApplySlots(VoxelGrid grid)
        {
            cellLeakCache.Clear();
            hullLeakCache.Clear();
            if (Slots == null && HullPanels == null) { hullGrid = null; return; }
            Slots?.Apply(grid);
            foreach(var frame in OpeningFrames.Build(Def,Slots)) grid.Fill(frame.Box);
            // ((0.3.26) her hull panels out: their voxels gone)
            HullPanels?.Apply(grid);
            hullGrid ??= Def.Grid.Clone();
            hullGrid.CopyFrom(Def.Grid);
            Slots?.Apply(hullGrid);
            foreach(var frame in OpeningFrames.Build(Def,Slots)) hullGrid.Fill(frame.Box);
            HullPanels?.Apply(hullGrid);
            foreach (var p in Pieces.All)
                if (p.Role == PieceRole.Hatch && !p.Open)
                    foreach (var s in p.Solids) hullGrid.Fill(s);
        }

        /// <summary>(0.3.20) Is a place usable with her cells as they are (a floor place over a cell taken up isn't; one over a design opening laid is).</summary>
        public bool MountOpen(MountPoint m) => DeckCells.MountOpen(m, Slots);

        /// <summary>The hatch piece over a cell, or null.</summary>
        public ShipPiece HatchOver(int n) =>
            SlotLayout == null || n < 0 || n >= SlotLayout.Count ? null : Pieces.At(HatchPlanner.CellMount(SlotLayout.Slots[n])) is ShipPiece p && p.Role == PieceRole.Hatch ? p : null;

        /// <summary>An opening in her deck: the cell taken up and no shut hatch over it.</summary>
        public bool CellOpen(int n) => Slots != null && n >= 0 && n < Slots.Count && SlotLayout.Slots[n].IsCell && !Slots.Filled(n) && HatchOver(n) is not { Open: false };

        public double HatchCoaming(string opening) => opening?.StartsWith("cell:") == true && int.TryParse(opening.AsSpan(5),out int n)
            ? HatchOver(n)?.Coaming ?? 0 : 0;

        /// <summary>
        /// The openings (cell boxes) in a room's ceiling or floor that open it: an open cell over (under) it whose other side
        /// isn't one of the design's enclosed rooms (as a well's: docs/modular-pieces.md). Worked out again when she changes.
        /// </summary>
        public List<double[]> CellLeaks(RoomDef r)
        {
            if (Slots == null || r == null) return new List<double[]>();
            if (cellLeakCache.TryGetValue(r.Code ?? "", out var cached)) return cached;
            var list = DeckCells.Leaks(Def, CellOpen, r);
            cellLeakCache[r.Code ?? ""] = list;
            return list;
        }

        /// <summary>Is the place a design ladder's gate "opening" names open: the cell at its top (on the deck there) taken up, no shut hatch over it.</summary>
        bool OpeningAt(double[] p)
        {
            if (p == null || SlotLayout == null) return true;
            int n = SlotLayout.At(p[0], p[1] - 0.01, p[2]);
            if (n < 0) n = SlotLayout.At(p[0], p[1] + 0.2, p[2]);
            return n < 0 ? !Grid.SolidAt(p[0], p[1] - 0.1, p[2]) : CellOpen(n);
        }

        /// <summary>(0.3.20) What a downflooding point's open fixture code says: a fitting standing open, or a deck cell open ("cell:" + its index).</summary>
        public bool DownfloodOpen(string code)
        {
            if (code == null) return true;
            if (code.StartsWith("cell:")) return int.TryParse(code.AsSpan(5), out int n) && CellOpen(n);
            // ((0.3.26, step D1) a hull panel over her main deck that's out)
            if (code.StartsWith("hull:")) return int.TryParse(code.AsSpan(5), out int h) && HullOpen(h);
            return OpenFittings.Contains(code);
        }

        // ------------------------------------------------------------------ what stands on a cell

        /// <summary>
        /// Why a cell can't be taken up now (a lang code), or null: it isn't a cell (fixed), it's out already, a hatch is over
        /// it, a piece or a fixture of hers stands on it, hangs under it or passes through it.
        /// </summary>
        public string TakeProblem(int n)
        {
            var l = SlotLayout;
            if (Slots == null || n < 0 || n >= l.Count) return "ships:cell-fixed";
            // ((0.3.27) a length of a deck beam: its own rules)
            if (l.Slots[n].IsBeam) return BeamTakeProblem(n);
            if (l.Slots[n].IsStructure)
            {
                if (!Slots.Filled(n)) return "ships:cell-already-out";
                foreach (var piece in Pieces.All)
                    if (piece.Solids.Any(b => PiecePlanner.Overlap(b,l.Slots[n].Box,0.005))) return "ships:beam-under-piece";
                return null;
            }
            // ((0.3.29) a castle port: taken out (its trapdoor back) it leaves the opening; not with a gun on it or its lid open)
            if (l.Slots[n].IsGunPort)
            {
                if (!Slots.Filled(n)) return "ships:port-not-in";
                var pf = Def.Fixture(l.Slots[n].Gun);
                if (pf != null && GunMounted(pf)) return "ships:port-has-gun";
                if (pf?.Lid != null && OpenFittings.Contains("open:" + pf.Code)) return "ships:port-lid-open";
                return null;
            }
            // ((0.3.28, D2.4) a port's cover: taken out it's the lidded port)
            if (l.Slots[n].IsPort) return Slots.Filled(n) ? null : "ships:port-already-open";
            if (HatchOver(n) != null) return "ships:cell-hatch";
            if (!Slots.Filled(n)) return "ships:cell-already-out";
            var d = l.Slots[n];
            // (0.3.21, step C) under a mounted gun (its carriage, run-back and trail's sweep)
            if (Def.GunPlaces != null)
                foreach (var gp in Def.GunPlaces)
                    if (GunPlaceLaid(gp) && GunPlaceCells(gp).Contains(n)) return "ships:cell-under-gun";
            foreach (var p in Pieces.All)
                foreach (var b in p.Solids.Concat(p.Sel))
                    if (OnCell(d, b)) return "ships:cell-under-piece";
            foreach (var f in Def.Fixtures.Concat(PlacedFixtures))
            {
                if (GunAbsent(f)) continue;
                // (an attach place only with something on it: a chest, a boat)
                var boxes = f.Type == "attach"
                    ? new[] { Attached.TryGetValue(f.Code ?? "", out var st) ? ShipFittings.AttachBox(f, st, Def) : null }
                    : new[] { f.Box, f.Sel };
                foreach (var b in boxes)
                    if (b != null && b.Length == 6 && OnCell(d, b)) return "ships:cell-under-fixture";
                if (f.Pos != null && StandsOnDeck(f.Type) && Math.Abs(f.Pos[1] - d.Top) < 0.3 && In(d, f.Pos[0], f.Pos[2])) return "ships:cell-under-fixture";
            }
            return null;
        }

        /// <summary>A fixture standing on the deck at its position (the helm, the capstan, a gun...), not one on a wall or a storage's place.</summary>
        static bool StandsOnDeck(string type) => type is "helm" or "compass" or "anchor" or "pump" or "gun" or "hearth" or "oven" or "stove" or "bed" or "prop" or "seat" or "door" or "gate" or "hatch" or "window" or "piece";

        /// <summary>A box rests on a cell, hangs under it, or passes through its planking (its footprint over the cell's).</summary>
        public static bool OnCell(DeckSlotDef d, double[] b) => PieceDamage.OnCell(d, b);

        static bool In(DeckSlotDef d, double x, double z) => x > d.Box[0] && x < d.Box[3] && z > d.Box[2] && z < d.Box[5];

        /// <summary>Why a cell can't be laid now (a lang code), or null: it's in, a hatch is over it, something is in its space (a ladder through it), someone is.</summary>
        /// <summary>(0.3.27) The room a man needs on a stair or ladder-stair (a beam's length isn't laid back into it), or none.</summary>
        IEnumerable<double[]> ClimbRoom(ShipPiece p)
        {
            var m = p?.Mount != null ? Mounts?.Get(p.Mount) : null;
            if (m == null || p.Rise <= 0) return Enumerable.Empty<double[]>();
            return p.Role == PieceRole.Stair ? PiecePlanner.FlightRoom(m, p.Dir, p.Rise, PiecePlanner.RunOf(p))
                 : p.Role == PieceRole.Ladder ? PiecePlanner.StairClimbRoom(m, p.Dir, p.Rise) : Enumerable.Empty<double[]>();
        }

        public string LayProblem(int n, bool checkPeople)
        {
            var l = SlotLayout;
            if (Slots == null || n < 0 || n >= l.Count) return "ships:cell-fixed";
            if (Slots.Filled(n)) return "ships:cell-already-in";
            var d = l.Slots[n];
            if (d.IsGunPort)
            {
                // ((0.3.29) a port put into her side: its board out (the opening)
                if (l.IndexOf(d.Cover) is int cv && cv >= 0 && Slots.Filled(cv)) return "ships:port-needs-opening";
                return null;
            }
            if (d.IsPort)
            {
                // ((0.3.29) boarding over a port's place: not with the port in it)
                if (GunPortOfCover(n) is int gpn && gpn >= 0 && Slots.Filled(gpn)) return "ships:port-in-there";
                // ((0.3.28, D2.4) planking a port over: no gun there, its lid shut)
                var gf = Def.Fixture(d.Gun);
                if (gf != null && GunMounted(gf)) return "ships:port-has-gun";
                if (gf?.Lid != null && OpenFittings.Contains("open:" + gf.Code)) return "ships:port-lid-open";
                if (checkPeople && BoxOccupied(d.Box)) return "ships:piece-someone";
                return null;
            }
            if (d.IsCell && HatchOver(n) != null) return "ships:cell-hatch";
            foreach (var p in Pieces.All)
                foreach (var b in p.Solids.Concat(p.Sel).Concat(d.IsBeam ? ClimbRoom(p) : Enumerable.Empty<double[]>()))
                    if (PiecePlanner.Overlap(b, d.Box)) return "ships:cell-occupied";
            if (checkPeople && BoxOccupied(new[] { d.Box[0], d.Box[1], d.Box[2], d.Box[3], d.Box[4] + 0.05, d.Box[5] })) return "ships:piece-someone";
            return null;
        }

        /// <summary>
        /// (0.3.20) A piece planned to stand on an open deck cell (any of its boxes resting on a cell that's out, or its foot
        /// over one): it can't. A hatch goes over openings, and is the one exception.
        /// </summary>
        public string OverOpening(PiecePlan plan)
        {
            // (a ladder-stair or a stair goes up through an opening: its places say where it stands, her grid where it passes)
            if (Slots == null) return null;
            // ((0.3.28) shared with the offline tests)
            return OpeningRules.OverOpening(SlotLayout, n => !Slots.Filled(n) && HatchOver(n) is not { Open: false }, plan);
        }

        // ------------------------------------------------------------------ taking up and laying (server)

        /// <summary>Take a deck cell up (building mode, an axe in hand, left mouse held on it): its planks to the player. Returns an error lang code or null.</summary>
        public string TryTakeCell(IServerPlayer player, string slotId)
        {
            var l = SlotLayout;
            int n = l?.IndexOf(slotId) ?? -1;
            if (n < 0) return "ships:cell-fixed";
            if (GoneDown) return "ships:gone-down";
            var d = l.Slots[n];
            if (!VanillaPieceBlock.BuildingMode(player.Entity)) return "ships:cell-build-mode";
            var held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (held?.Collectible?.Tool != EnumTool.Axe) return "ships:cell-needs-axe";
            if (!InReach(player, d.CX, d.Top, d.CZ)) return "ships:piece-too-far";
            string err = TakeCell(n, player);
            if (err != null) return err;
            CellSound(d, "sounds/block/planks");
            World.Logger.Audit("{0} took up the deck cell {1} of {2} #{3}.", player.PlayerName, d.Id, GetName(), EntityId);
            return null;
        }

        /// <summary>Take a cell up (null player: just out, the commands and tests). Its material to the player (survival; all or nothing).</summary>
        public string TakeCell(int n, IServerPlayer player)
        {
            string err = TakeProblem(n);
            if (err != null) return err;
            var d = SlotLayout.Slots[n];
            if (player != null && !Creative(player))
            {
                var stack = MaterialStack(d);
                if (stack != null && !player.InventoryManager.TryGiveItemstack(stack, true)) return "ships:piece-inventory-full";
            }
            Slots.Set(n, SlotState.Empty);
            // ((0.3.28, step D2) taken up: its damage gone with it)
            CellMended(n);
            SlotsChanged();
            return null;
        }

        /// <summary>Lay a deck cell (building mode, planks in hand, right-click the opening). Returns an error lang code or null.</summary>
        public string TryLayCell(IServerPlayer player, string slotId)
        {
            var l = SlotLayout;
            int n = l?.IndexOf(slotId) ?? -1;
            if (n < 0) return "ships:cell-fixed";
            if (GoneDown) return "ships:gone-down";
            var d = l.Slots[n];
            // ((0.3.27) out of building mode too, and with boards: mending a hole is no building)
            var slot = player.InventoryManager.ActiveHotbarSlot;
            var held = slot?.Itemstack;
            int need = LayNeed(d.Mat, held);
            if (need < 0) return d.IsBeam || d.IsStructure ? "ships:cell-needs-supportbeam" : "ships:cell-needs-planks";
            if (!Creative(player) && held.StackSize < need) return d.IsBeam || d.IsStructure ? "ships:cell-needs-supportbeam" : "ships:cell-needs-planks";
            if (!InReach(player, d.CX, d.Top, d.CZ)) return "ships:piece-too-far";
            string err = LayCell(n, true);
            if (err != null) return err;
            if (!Creative(player))
            {
                slot.TakeOut(need);
                slot.MarkDirty();
            }
            CellSound(d, held.Block?.Sounds?.Place.Location?.ToString() ?? "sounds/block/planks");
            World.Logger.Audit("{0} laid the deck cell {1} of {2} #{3}.", player.PlayerName, d.Id, GetName(), EntityId);
            return null;
        }

        /// <summary>Lay a cell (the commands and tests: no material).</summary>
        public string LayCell(int n, bool checkPeople)
        {
            string err = LayProblem(n, checkPeople);
            if (err != null) return err;
            Slots.Set(n, SlotState.Filled);
            // ((0.3.28, step D2) laid again (a cell stove in, too): sound)
            CellMended(n);
            SlotsChanged();
            return null;
        }

        /// <summary>Does what's in hand lay this cell: its material's kind (any wood's planks for planks).</summary>
        public static bool MaterialFits(DeckSlotDef d, ItemStack held) => MaterialFits(d.Mat, held) || IsBoards(held) && IsPlanksMat(d.Mat);

        /// <summary>(0.3.27, the user's note 209: "says planks but actually boards") Boards (vanilla's plank items) a planks block is made of (vanilla's grid recipe: 2 × 2).</summary>
        public const int BoardsPerPlanks = ShipRepairs.BoardsPerPlanks;

        /// <summary>(0.3.27) Boards in hand (vanilla's <c>game:plank-*</c> items, not the planks block).</summary>
        public static bool IsBoards(ItemStack s) => s?.Item != null && s.Collectible.Code?.Domain == "game" && s.Collectible.Code.Path.StartsWith("plank-");

        static bool IsPlanksMat(SlotMaterialDef m)
        {
            string want = m?.Block ?? "game:planks-oak-ud";
            string first = want.Contains(':') ? want.Substring(want.IndexOf(':') + 1) : want;
            return first.Split('-')[0] == "planks";
        }

        /// <summary>
        /// (0.3.27, notes 209 and the coordinator: "made holes in ship but cannot repair") How many of what's in hand lay a
        /// slot of this material: its planks blocks, or boards (<see cref="BoardsPerPlanks"/> a block) for planks; -1 if what's
        /// in hand doesn't lay it.
        /// </summary>
        public static int LayNeed(SlotMaterialDef m, ItemStack held)
        {
            int blocks = Math.Max(1, m?.Count ?? 1);
            if (MaterialFits(m, held)) return blocks;
            if (IsBoards(held) && IsPlanksMat(m)) return blocks * BoardsPerPlanks;
            return -1;
        }

        /// <summary>(0.3.27) Is what's in hand something that lays an opening (a planks block or boards)?</summary>
        public static bool LaysOpenings(ItemStack held) => held?.Block != null && VanillaPieceBlock.IsPlanks(held.Block) || IsBoards(held) ||
            held?.Collectible?.Code?.Domain == "game" && held.Collectible.Code.Path.StartsWith("supportbeam-");

        /// <summary>(0.3.26) Does what's in hand lay a slot of this material (a deck cell's, a hull panel's)?</summary>
        public static bool MaterialFits(SlotMaterialDef m, ItemStack held)
        {
            if (held?.Collectible?.Code == null) return false;
            string want = m?.Block ?? "game:planks-oak-ud";
            string first = want.Contains(':') ? want.Substring(want.IndexOf(':') + 1) : want;
            first = first.Split('-')[0];
            if (first == "planks") return held.Block != null && VanillaPieceBlock.IsPlanks(held.Block);
            return held.Collectible.Code.Domain == "game" && held.Collectible.FirstCodePart() == first;
        }

        ItemStack MaterialStack(DeckSlotDef d) => MaterialStack(d.Mat);

        ItemStack MaterialStack(SlotMaterialDef m)
        {
            var block = World.GetBlock(new AssetLocation(m.Block ?? "game:planks-oak-ud"));
            if (block != null && block.Id != 0) return new ItemStack(block, Math.Max(1, m.Count));
            var item = World.GetItem(new AssetLocation(m.Block ?? ""));
            return item != null ? new ItemStack(item, Math.Max(1, m.Count)) : null;
        }

        void CellSound(DeckSlotDef d, string sound)
        {
            LocalToWorld(d.CX, d.Top, d.CZ, out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation(sound), x, y, z, null, true, 16);
        }

        /// <summary>(0.3.29) The castle port whose board this is (a deck slot index), or -1.</summary>
        public int GunPortOfCover(int cover)
        {
            var l = SlotLayout;
            if (l == null || cover < 0 || cover >= l.Count) return -1;
            string id = l.Slots[cover].Id;
            foreach (int n in l.Ports) if (l.Slots[n].IsGunPort && l.Slots[n].Cover == id) return n;
            return -1;
        }

        /// <summary>Her cells changed: saved, her collision and caches again.</summary>
        void SlotsChanged()
        {
            if (Api?.Side == EnumAppSide.Server) SaveSlots();
            RebuildGrid();
        }

        // ------------------------------------------------------------------ hatches

        /// <summary>The opening a cell is in (the open cells joined to it on its deck, no hatch over them).</summary>
        public List<int> OpeningOf(int n) => HatchPlanner.Opening(SlotLayout, n, c => !Slots.Filled(c) && HatchOver(c) == null);

        /// <summary>A hatch planned over the opening a cell is in (with the trapdoors in hand: glazed or not).</summary>
        public PiecePlan HatchPlanAt(int n, string block) => SwingClear(HatchPlanner.Plan(SlotLayout, OpeningOf(n), VanillaPieceBlock.IsGlazedTrapdoor(block)), null);

        /// <summary>
        /// (0.3.20) A hatch's plan with leaves that swing clear of her (her collision as it stands, her ladders and ladder-stairs):
        /// hinged as asked, else the other way if that way they don't; neither: why not.
        /// </summary>
        PiecePlan SwingClear(PiecePlan plan, string hinge)
        {
            if (plan?.Error != null || plan?.Box == null) return plan;
            var g = PlaceGrid ?? Grid;
            bool Solid(double x, double y, double z) => g.SolidAt(x, y, z);
            if (HatchPlanner.SwingClash(plan.Box, plan.Dir, Solid, Ladders,plan.DoubleLeaf) == null) return plan;
            if (hinge == null && HatchPlanner.SwingClash(plan.Box, 1 - plan.Dir, Solid, Ladders,plan.DoubleLeaf) == null) { plan.Dir = 1 - plan.Dir; return plan; }
            plan.Error = "ships:hatch-no-swing";
            return plan;
        }

        /// <summary>Lay a hatch over the opening a cell is in (building mode, trapdoors in hand: one a cell). Returns an error lang code or null.</summary>
        public string TryPlaceHatch(IServerPlayer player, string slotId)
        {
            var l = SlotLayout;
            int n = l?.IndexOf(slotId) ?? -1;
            if (n < 0) return "ships:hatch-no-opening";
            if (GoneDown) return "ships:gone-down";
            if (!VanillaPieceBlock.BuildingMode(player.Entity)) return "ships:cell-build-mode";
            var slot = player.InventoryManager.ActiveHotbarSlot;
            var held = slot?.Itemstack;
            if (!VanillaPieceBlock.IsTrapdoor(held?.Block)) return "ships:hatch-needs-trapdoors";
            var d = l.Slots[n];
            if (!InReach(player, d.CX, d.Top, d.CZ)) return "ships:piece-too-far";
            var plan = HatchPlanAt(n, held.Collectible.Code.ToString());
            if (plan.Error != null) return plan.Error;
            int need = plan.Mounts.Count;
            if (!Creative(player) && held.StackSize < need) return "ships:hatch-needs-trapdoors";
            string err = PlaceHatch(plan, held, true, out var placed);
            if (err != null) return err;
            if (!Creative(player))
            {
                slot.TakeOut(need);
                slot.MarkDirty();
            }
            PieceSound(placed, held.Block.Sounds?.Place.Location);
            World.Logger.Audit("{0} laid a {1} hatch over {2} cells of {3} #{4}.", player.PlayerName, held.Collectible.Code, need, GetName(), EntityId);
            return null;
        }

        /// <summary>A hatch as planned (its stack: the trapdoors, one a cell, given back when it's taken up).</summary>
        public string PlaceHatch(PiecePlan plan, ItemStack stack, bool checkPeople, out ShipPiece placed)
        {
            placed = null;
            string err = PlaceProblem(plan, checkPeople);
            if (err != null) return err;
            placed = ShipPiece.From(plan, Pieces.NextId, stack);
            if (placed.Stack != null) placed.Stack.StackSize = plan.Mounts.Count;
            Pieces.Add(placed);
            Changed();
            return null;
        }

        /// <summary>(0.3.20) A design's hatch entry laid over its opening (docs/slots.md §5). Returns an error or null.</summary>
        string PlaceModularHatch(ModularPieceDef e, out ShipPiece placed)
        {
            placed = null;
            var l = SlotLayout;
            if (l == null || e.At == null || e.At.Length < 3) return "no deck cells";
            int nx = e.Size?.Length > 0 ? e.Size[0] : 1, nz = e.Size?.Length > 1 ? e.Size[1] : 1;
            var cells = l.Rect(e.At[1], e.At[0] - nx / 2.0, e.At[2] - nz / 2.0, e.At[0] + nx / 2.0, e.At[2] + nz / 2.0);
            if (cells == null) return "not every cell of it is a deck cell";
            if (cells.Any(c => Slots.Filled(c))) return "a cell of its opening is laid";
            string code = e.Block ?? ModularPieceDef.DefaultBlock("hatch");
            var block = World.GetBlock(new AssetLocation(code));
            if (block == null || block.Id == 0) return "no such block " + code;
            var plan = SwingClear(HatchPlanner.Plan(l, cells, VanillaPieceBlock.IsGlazedTrapdoor(code), e.Hinge,e.Coaming,e.DoubleLeaf), e.Hinge);
            if (plan.Error != null) return plan.Error;
            string err = PlaceHatch(plan, new ItemStack(block), false, out placed);
            if (err != null) return err;
            if (e.Open) { placed.Open = true; Changed(); }
            return null;
        }

        /// <summary>A hatch opens or closes only when its actual leaf swing is clear of bodies.</summary>
        public bool ToggleHatch(IServerPlayer player, ShipPiece p)
        {
            if (p?.Role != PieceRole.Hatch) return false;
            if (HatchSwingBlocker(p, out string who, out double[] at))
            {
                World.Logger.Notification("[ships] hatch {0} on ship {1} (box {2}, dir {3}): {4} in its swing at local {5}", p.Id, EntityId,
                    string.Join(" ", p.Box.Select(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))), p.Dir, who,
                    string.Join(" ", at.Select(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
                var ep = player?.Entity;
                var mounted = ep?.MountedOn;
                var deck = mounted as DeckSeat;
                World.Logger.Notification("[ships] hatch state: coaming {0}, doubleLeaf {1}, glazed {2}; user {3}, mount {4}, deck local {5}, deck state age {6} ms, user world-local {7}",
                    p.Coaming, p.DoubleLeaf, p.Slots > 0, player?.PlayerName ?? "test", mounted?.SeatId ?? "none",
                    deck?.StoredLocal?.ToString() ?? "none", deck == null ? -1 : World.ElapsedMilliseconds - deck.StateMs,
                    ep == null ? "none" : WorldToLocal(ep.Pos.X, ep.Pos.Y, ep.Pos.Z).ToString());
                if (player != null)
                    player.SendMessage(Vintagestory.API.Config.GlobalConstants.InfoLogChatGroup, Vintagestory.API.Config.Lang.GetL(player.LanguageCode, "ships:hatch-blocked", who), EnumChatType.Notification);
                return false;
            }
            p.Open = !p.Open;
            PieceSound(p, new AssetLocation("sounds/block/door"));
            Changed();
            return true;
        }

        /// <summary>The first body in a hatch's leaf swing: its name and its ship-local box (for the refusal and the log).</summary>
        bool HatchSwingBlocker(ShipPiece p, out string who, out double[] at)
        {
            foreach(var seat in Seats.OfType<ShipSeat>()) {
                if(seat.Passenger is not Vintagestory.API.Common.EntityAgent agent || !agent.Alive || agent.MountedOn!=seat) continue;
                var l=seat.LocalPos;
                var body=new[]{l.X-DeckMover.HalfWidth,l.Y,l.Z-DeckMover.HalfWidth,l.X+DeckMover.HalfWidth,l.Y+DeckMover.Height,l.Z+DeckMover.HalfWidth};
                if(HatchPlanner.BodyInSwing(p.Box,p.Dir,p.DoubleLeaf,p.Slots>0,body)) { who=agent.GetName()+" ("+seat.SeatId+")"; at=body; return true; }
            }
            // Native creatures and people standing aboard without a deck seat also have real bodies.
            foreach(var entity in World.GetEntitiesAround(Pos.XYZ,20,10,e=>e is Vintagestory.API.Common.EntityAgent && e.Alive && e.Pos.Dimension==Pos.Dimension)) {
                var agent = (Vintagestory.API.Common.EntityAgent)entity;
                if (agent.MountedOn is ShipSeat seat && seat.Ship == this) continue;
                var pos = agent.MountedOn?.SeatPosition ?? entity.Pos;
                if (pos.Dimension != Pos.Dimension) continue;
                var l=WorldToLocal(pos.X,pos.Y,pos.Z); var b=entity.CollisionBox;
                var body=b==null?null:HatchPlanner.WorldBody(new double[]{b.X1,b.Y1,b.Z1,b.X2,b.Y2,b.Z2},l.X,l.Y,l.Z,Pos.Yaw,Pitch,Roll);
                if(body!=null && HatchPlanner.BodyInSwing(p.Box,p.Dir,p.DoubleLeaf,p.Slots>0,body)) { who=entity.GetName()+" ("+entity.Code+", "+(agent.MountedOn?.GetType().Name??"not mounted")+")"; at=body.Bounds; return true; }
            }
            who=null; at=null; return false;
        }

        /// <summary>(0.3.20) What her deck cells are now, for <c>.ships slots</c>.</summary>
        public string SlotsText()
        {
            if (Slots == null) return $"The {Def?.Code} has no deck cells that can be taken up.";
            var l = SlotLayout;
            int open = Enumerable.Range(0, l.Count).Count(CellOpen), outN = Enumerable.Range(0, l.Count).Count(i => !Slots.Filled(i));
            int hatches = Pieces.All.Count(p => p.Role == PieceRole.Hatch);
            var decks = l.Slots.Where(s => s != null).GroupBy(s => s.Deck).Select(g => $"{g.Key} {g.Count()}");
            return $"{l.Count} deck cells ({string.Join(", ", decks)}), {outN} out ({open} open, the rest under hatches), {Slots.Differences} differ from the design; {hatches} hatches; {l.Chunks.Count} chunks";
        }
    }
}
