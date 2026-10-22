using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// (0.3.27, the user's question with notes 211 and 222: "stairs need a 3-block opening and the beams are every 2: rebuild
    /// with 3-block frame spacing, or make partial beam removal possible?"; docs/slots.md §10) Trimmed beams. A deck beam
    /// under a well is in lengths a cell wide (beam segments: deck slots of kind "beam"). One comes out (an axe in building
    /// mode, or by itself when a stair is put through it) only once the deck cells either side of it are open, at most two
    /// next to each other of one beam, never with something hung on it or standing in it; then its neighbours' carlings (fore
    /// and aft timbers along the opening's sides, from the whole beam aft of it to the whole beam forward of it) and its cut
    /// ends are drawn (the generator's quads, by the slots' draw rule). Laying it again (planks or boards) puts the beam back
    /// and the carlings go. The design marks which beams have segments: not her castle decks' edge beams, not at a mast's
    /// partners, not where her own fittings are (docs/slots.md §10).
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>Why a beam segment can't come out now (a lang code), or null.</summary>
        public string BeamTakeProblem(int n)
        {
            var l = SlotLayout;
            var d = l.Slots[n];
            string rule = BeamRules.TakeProblem(l, Slots, n);
            if (rule != null) return rule;
            // nothing hung on it or standing in it (a partition under it, a lantern, a piece through it), nothing of hers
            var near = new[] { d.Box[0] - 0.05, d.Box[1] - 0.05, d.Box[2] + 0.02, d.Box[3] + 0.05, d.Box[4] + 0.02, d.Box[5] - 0.02 };
            foreach (var p in Pieces.All)
                foreach (var b in p.Solids.Concat(p.Sel))
                    if (PiecePlanner.Overlap(b, near, 0.005)) return "hansa:beam-under-piece";
            foreach (var f in Def.Fixtures.Concat(PlacedFixtures))
            {
                if (GunAbsent(f)) continue;
                foreach (var b in new[] { f.Box, f.Sel })
                    if (b != null && b.Length == 6 && PiecePlanner.Overlap(b, near, 0.005)) return "hansa:beam-under-fixture";
            }
            return null;
        }

        /// <summary>
        /// (0.3.27) The beam segments a planned piece runs into that could come out for it (<see cref="BeamRules.InTheWay"/>),
        /// or null.
        /// </summary>
        /// ((after 0.1.0-dev.6, Codex's review) worked out once for a plan while her collision is as it was (FitVersion): the
        /// hint asks every frame for the stair in the crosshair. Callers only read the list.)
        public List<int> BeamsInTheWay(PiecePlan plan)
        {
            if (SlotLayout == null || Slots == null) return null;
            if (ReferenceEquals(plan, beamsPlan) && beamsVersion == FitVersion) return beamsSaid;
            beamsSaid = BeamRules.InTheWay(Mounts, SlotLayout, Slots, plan, PlaceGrid ?? Grid, n => BeamTakeProblem(n) == null);
            beamsPlan = plan;
            beamsVersion = FitVersion;
            return beamsSaid;
        }

        PiecePlan beamsPlan;
        int beamsVersion = -1;
        List<int> beamsSaid;

        /// <summary>
        /// (0.3.27) Is a deck beam in this box (a stair's clash): a beam segment's index, -1 for a beam with no segments there
        /// (a fixed beam: a deck's edge, a mast's partners...), null for no beam at all.
        /// </summary>
        public int? BeamAt(double[] box)
        {
            var l = SlotLayout;
            if (l != null)
                foreach (var kv in l.Beams)
                    foreach (int n in kv.Value)
                        if (Slots.Filled(n) && PiecePlanner.Overlap(l.Slots[n].Box, box, 0.005)) return n;
            // a beam with no segment there: her collision under a deck (within its beams' depth) across the box
            if (Def?.Decks == null) return null;
            foreach (var dk in Def.Decks)
                if (box[4] > dk.Y - 1.0 && box[1] < dk.Y - 0.2 && box[4] <= dk.Y + 0.01) return -1;
            return null;
        }

        /// <summary>Server: beam segments out for a stair (their planks to the player, survival; all or none). An error lang code, or null.</summary>
        public string TakeBeamsFor(List<int> beams, IServerPlayer player)
        {
            if (beams == null || beams.Count == 0) return null;
            if (player != null && !Creative(player))
            {
                int count = beams.Sum(n => Math.Max(1, SlotLayout.Slots[n].Mat.Count));
                var stack = MaterialStack(new SlotMaterialDef { Block = SlotLayout.Slots[beams[0]].Mat.Block, Count = count });
                if (stack != null && !player.InventoryManager.TryGiveItemstack(stack, true)) return "hansa:piece-inventory-full";
            }
            foreach (int n in beams) Slots.Set(n, SlotState.Empty);
            SlotsChanged();
            foreach (int n in beams)
                World.Logger.Audit("{0}: the beam segment {1} of {2} #{3} taken out for a stair.", player?.PlayerName ?? "-", SlotLayout.Slots[n].Id, GetName(), EntityId);
            return null;
        }
    }
}
