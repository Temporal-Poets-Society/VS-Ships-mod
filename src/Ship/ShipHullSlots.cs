using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// (0.3.26, step D1; docs/slots.md §9) Her hull's panels: each one's state (in, cracked, out) saved with the deck cells'
    /// as differences from the design in WatchedAttributes "hansaSlots" and sent to every client; on the server it's
    /// always what its damage part's hurt says (<see cref="SyncSlotsFromHurt"/>: cracked at Cracked, out at Holed). Out,
    /// its voxels are gone from her collision (shots, rays, people and the rain go through), the sea comes in through it
    /// (ShipFlood) and the room behind it is open. Cracked ones are mended with the repair tool (planks and nails); one that's
    /// out is laid again with planks in building mode; a sound one above her waterline can be taken out with an axe.
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>Her hull panels' states (null: her design has none).</summary>
        public SlotStates HullPanels;
        public HullSlotLayout HullLayout => Def?.Hull;
        /// <summary>How long left mouse is held on a hull panel with an axe to take it out (s).</summary>
        public const float HullSeconds = 1.5f;
        readonly Dictionary<string, List<double[]>> hullLeakCache = new();

        /// <summary>Is this hull panel out (an opening in her side)?</summary>
        public bool HullOpen(int n) => HullPanels != null && n >= 0 && n < HullPanels.Count && !HullPanels.Filled(n);

        /// <summary>
        /// Server: every hull panel's state as its damage part's hurt has it (holed: out, cracked: cracked, else in). True if
        /// one changed (then her slots are saved and her collision built again by the caller).
        /// </summary>
        public bool SyncSlotsFromHurt()
        {
            // ((0.3.28, step D2) and her deck cells: cracked, stove in, a held one stopping at cracked)
            bool changed = SyncCellsFromHurt();
            if (HullPanels == null || PartDamage == null || DamageParts == null) return changed;
            var d = DamageParts.D;
            for (int n = 0; n < HullPanels.Count && n < PartDamage.Count; n++)
            {
                var want = HullPanelRules.StateFor(d, PartDamage.Hurt[n]);
                if (HullPanels[n] == SlotState.Glazed && want != SlotState.Empty) continue;
                if (HullPanels[n] == want) continue;
                HullPanels.Set(n, want);
                changed = true;
            }
            return changed;
        }

        /// <summary>Server: after her hurt changed: the panels' states follow, saved and her collision again if one changed.</summary>
        void HullFollowsHurt()
        {
            if (Api?.Side != EnumAppSide.Server || !SyncSlotsFromHurt()) return;
            SaveSlots();
            RebuildGrid();
        }

        /// <summary>
        /// (0.3.26) The open hull panels a room has (an empty panel whose ceiling faces it): their boxes. Worked out again when
        /// she changes.
        /// </summary>
        public List<double[]> HullLeaks(RoomDef r)
        {
            if (HullPanels == null || r == null) return new List<double[]>();
            if (hullLeakCache.TryGetValue(r.Code ?? "", out var cached)) return cached;
            var list = HullPanelRules.Leaks(HullLayout, HullOpen, r);
            hullLeakCache[r.Code ?? ""] = list;
            return list;
        }

        // ------------------------------------------------------------------ taking out and laying

        /// <summary>
        /// Why a hull panel can't be taken out now (a lang code), or null: it's fixed (not a panel), it's out already, any of it
        /// is under her waterline while she's afloat, a piece or something hung aboard is against it.
        /// </summary>
        public string HullTakeProblem(int n)
        {
            var l = HullLayout;
            if (HullPanels == null || n < 0 || n >= l.Count) return "ships:hull-fixed";
            if (!HullPanels.Filled(n)) return "ships:hull-already-out";
            var s = l.Slots[n];
            // half a block of freeboard: waves and her heel reach a panel just over the waterline
            // ((0.3.29) a ceiling panel (her side's inner layer) with her planking in front of it: no sea behind it)
            var near = Grow(s.Box, 0.3);
            foreach (var p in Pieces.All)
                foreach (var b in p.Solids.Concat(p.Sel))
                    if (PiecePlanner.Overlap(b, near)) return "ships:hull-under-piece";
            foreach (var f in Def.Fixtures.Concat(PlacedFixtures))
            {
                if (GunAbsent(f)) continue;
                var boxes = f.Type == "attach"
                    ? new[] { Attached.TryGetValue(f.Code ?? "", out var st) ? ShipFittings.AttachBox(f, st, Def) : null }
                    : new[] { f.Box };
                foreach (var b in boxes)
                    if (b != null && b.Length == 6 && PiecePlanner.Overlap(b, near)) return "ships:hull-under-piece";
            }
            return null;
        }

        static double[] Grow(double[] b, double e) => new[] { b[0] - e, b[1] - e, b[2] - e, b[3] + e, b[4] + e, b[5] + e };

        /// <summary>
        /// (0.3.30) A ceiling panel's planking behind it when that's out (laid from inside through the ceiling, or with it),
        /// else -1.
        /// </summary>
        public int HullBehind(int n)
        {
            var l = HullLayout;
            if (l == null || HullPanels == null || n < 0 || n >= l.Count) return -1;
            var s = l.Slots[n];
            if (!s.IsInner || s.PartnerIndex < 0 || s.PartnerIndex >= l.Count) return -1;
            return HullPanels.Filled(s.PartnerIndex) ? -1 : s.PartnerIndex;
        }

        /// <summary>(0.3.30) Planks (in the panels' own count) laying a hull panel takes, with her planking behind it if that's out too.</summary>
        public int HullLayCount(int n, ItemStack held)
        {
            var l = HullLayout;
            if (l == null || n < 0 || n >= l.Count) return 0;
            int b = HullBehind(n), need = 0;
            if (HullPanels.Filled(n)) return b >= 0 ? Math.Max(1, LayNeed(l.Slots[b].Mat, held)) : 0;
            need = Math.Max(1, LayNeed(l.Slots[n].Mat, held));
            if (b >= 0) need += Math.Max(1, LayNeed(l.Slots[b].Mat, held));
            return need;
        }

        /// <summary>Why a hull panel can't be laid now (a lang code), or null: it's in, something is in its space, someone is.</summary>
        public string HullLayProblem(int n, bool checkPeople)
        {
            var l = HullLayout;
            if (HullPanels == null || n < 0 || n >= l.Count) return "ships:hull-fixed";
            if (HullPanels.Filled(n)) return "ships:hull-already-in";
            var s = l.Slots[n];
            foreach (var p in Pieces.All)
                foreach (var b in p.Solids)
                    foreach (var sb in s.SolidBoxes)
                        if (PiecePlanner.Overlap(b, sb)) return "ships:hull-occupied";
            if (checkPeople && s.SolidBoxes.Any(BoxOccupied)) return "ships:piece-someone";
            return null;
        }

        /// <summary>Take a hull panel out (null player: just out, the commands and tests): its planks to the player (survival; all or nothing).</summary>
        public string TakeHull(int n, IServerPlayer player)
        {
            string err = HullTakeProblem(n);
            if (err != null) return err;
            var s = HullLayout.Slots[n];
            if (player != null && !Creative(player))
            {
                var stack = MaterialStack(HullPanels[n]==SlotState.Glazed
                    ? new SlotMaterialDef {Block=WatchedAttributes.GetTreeAttribute("hansaHullMaterials")?.GetString(s.Id,ModularPieceDef.DefaultBlock("window"))??ModularPieceDef.DefaultBlock("window"),Count=1} : s.Mat);
                if (stack != null && !player.InventoryManager.TryGiveItemstack(stack, true)) return "ships:piece-inventory-full";
            }
            SetHullState(n, SlotState.Empty);
            return null;
        }

        /// <summary>Lay a hull panel again (the commands and tests: no material): sound, its seams new.</summary>
        public string LayHull(int n, bool checkPeople)
        {
            string err = HullLayProblem(n, checkPeople);
            if (err != null) return err;
            SetHullState(n, SlotState.Filled);
            return null;
        }

        /// <summary>
        /// Server: a hull panel put in a state by its hurt (out: 1, cracked: Cracked, in: 0 and its seams new), and everything
        /// that follows (her slots saved, her collision, her damage sent).
        /// </summary>
        public void SetHullState(int n, SlotState state)
        {
            if (HullPanels == null || PartDamage == null || n < 0 || n >= HullPanels.Count) return;
            var d = DamageParts.D;
            switch (state)
            {
                case SlotState.Empty: PartDamage.Hurt[n] = 1; break;
                case SlotState.Cracked: PartDamage.Hurt[n] = Math.Max(d.Cracked, Math.Min(PartDamage.Hurt[n], d.Holed - 0.01f)); break;
                default: PartDamage.Hurt[n] = 0; PartDamage.Wear[n] = 0; ClearScars(HullLayout.Slots[n].Box, 0.3); break;
            }
            SyncSlotsFromHurt();
            HullPanels.Set(n,state);
            if (Api?.Side == EnumAppSide.Server)
            {
                SaveSlots();
                SaveDamage(true);
            }
            RebuildGrid();
        }

        /// <summary>Take a hull panel out (building mode, an axe in hand, left mouse held on it). Returns an error lang code or null.</summary>
        public string TryTakeHull(IServerPlayer player, string slotId)
        {
            var l = HullLayout;
            int n = l?.IndexOf(slotId) ?? -1;
            if (n < 0) return "ships:hull-fixed";
            if (GoneDown) return "ships:gone-down";
            var s = l.Slots[n];
            if (!VanillaPieceBlock.BuildingMode(player.Entity)) return "ships:cell-build-mode";
            var held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (held?.Collectible?.Tool != EnumTool.Axe) return "ships:cell-needs-axe";
            var c = s.Centre;
            if (!InReach(player, c.X, c.Y, c.Z, 1.2)) return "ships:piece-too-far";
            string err = TakeHull(n, player);
            if (err != null) return err;
            HullSound(s, "sounds/block/planks");
            World.Logger.Audit("{0} took out the hull panel {1} of {2} #{3}.", player.PlayerName, s.Id, GetName(), EntityId);
            return null;
        }

        /// <summary>Lay a hull panel that's out (planks blocks or boards in hand, right-click the opening; (0.3.27) building mode or not). Returns an error lang code or null.</summary>
        public string TryLayHull(IServerPlayer player, string slotId)
        {
            var l = HullLayout;
            int n = l?.IndexOf(slotId) ?? -1;
            if (n < 0) return "ships:hull-fixed";
            if (GoneDown) return "ships:gone-down";
            var s = l.Slots[n];
            // ((0.3.30, the user: "repairing missing hull elements from inside plugs only inner layer ... better would be ability
            // to plug both from inside") a ceiling laid from inside with her planking behind it out: the planking first (it's
            // what keeps the sea out), then the ceiling if the planks in hand go round; a ceiling that's in with her planking
            // behind it out: that planking, laid through it)
            int behind = HullBehind(n);
            var paneSlot=player.InventoryManager.ActiveHotbarSlot;
            if (paneSlot?.Itemstack?.Block is Block pane && VanillaPieceBlock.IsPane(pane))
            {
                if(!HullWindows.Fits(Def,s)) return "ships:hull-window-above";
                if(!InReach(player,s.Centre.X,s.Centre.Y,s.Centre.Z,1.2)) return "ships:piece-too-far";
                string problem=HullLayProblem(n,true);if(problem!=null) return problem;
                var materials=WatchedAttributes.GetTreeAttribute("hansaHullMaterials")??new TreeAttribute();
                materials.SetString(s.Id,pane.Code.ToString());WatchedAttributes["hansaHullMaterials"]=materials;WatchedAttributes.MarkPathDirty("hansaHullMaterials");
                SetHullState(n,SlotState.Glazed);
                if(!Creative(player)) {paneSlot.TakeOut(1);paneSlot.MarkDirty();}
                return null;
            }
            var order = new List<int>();
            if (HullPanels.Filled(n))
            {
                if (behind < 0) return "ships:hull-already-in";
                order.Add(behind);
            }
            else
            {
                if (behind >= 0) order.Add(behind);
                order.Add(n);
            }
            // ((0.3.27, "made holes in ship but cannot repair") out of building mode too, with planks blocks or boards)
            var slot = player.InventoryManager.ActiveHotbarSlot;
            var held = slot?.Itemstack;
            int need = LayNeed(l.Slots[order[0]].Mat, held);
            if (need < 0) return "ships:hull-needs-planks";
            if (!Creative(player) && held.StackSize < need) return "ships:hull-needs-planks";
            var c = s.Centre;
            if (!InReach(player, c.X, c.Y, c.Z, 1.2)) return "ships:piece-too-far";
            // (under her waterline: from inside her, or a diver outside under the water, as a repair)
            var ep = player.Entity;
            bool aboard = ep?.MountedOn is ShipSeat seat && seat.Ship == this;
            if (s.Sill < Def.Waterline + Sinkage && !aboard && ep?.IsEyesSubmerged() != true) return "ships:repair-below";
            int laid = 0;
            foreach (int m in order)
            {
                int needM = LayNeed(l.Slots[m].Mat, held);
                if (laid > 0 && (needM < 0 || !Creative(player) && (held == null || held.StackSize < needM))) break;
                string err = LayHull(m, true);
                if (err != null)
                {
                    if (laid == 0) return err;
                    break;
                }
                if (!Creative(player))
                {
                    slot.TakeOut(needM);
                    slot.MarkDirty();
                    held = slot.Itemstack;
                }
                laid++;
                World.Logger.Audit("{0} laid the hull panel {1} of {2} #{3}.", player.PlayerName, l.Slots[m].Id, GetName(), EntityId);
            }
            HullSound(s, slot?.Itemstack?.Block?.Sounds?.Place.Location?.ToString() ?? "sounds/block/planks");
            return null;
        }

        void HullSound(HullSlotDef s, string sound)
        {
            var c = s.Centre;
            LocalToWorld(c.X, c.Y, c.Z, out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation(sound), x, y, z, null, true, 16);
        }

        /// <summary>(0.3.26) What her hull panels are now, for <c>.ships hull</c>.</summary>
        public string HullText()
        {
            if (HullPanels == null) return $"The {Def?.Code} has no hull panels that can be taken out (her damage is her planking's, as before).";
            var l = HullLayout;
            int outN = Enumerable.Range(0, l.Count).Count(i => !HullPanels.Filled(i)), cracked = Enumerable.Range(0, l.Count).Count(i => HullPanels[i] == SlotState.Cracked);
            int below = l.Slots.Count(s => s.Sill < Def.Waterline);
            var outIds = Enumerable.Range(0, l.Count).Where(i => !HullPanels.Filled(i)).Select(i => l.Slots[i].Id).Take(12).ToList();
            return $"{l.Count} hull panels ({below} reaching under her waterline), {outN} out{(outIds.Count > 0 ? " (" + string.Join(", ", outIds) + (outN > outIds.Count ? ", ..." : "") + ")" : "")}, {cracked} cracked; {HullPanels.Differences} differ from the design; {l.Chunks.Count} chunks";
        }
    }
}
