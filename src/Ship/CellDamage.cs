using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Func2 = System.Func<int, bool>;

namespace Hansa
{
    /// <summary>(0.3.28, step D2) A deck slot as a ship part: what the crosshair and the damage model call a deck cell.</summary>
    public static class DeckSlotParts
    {
        /// <summary>The index of a deck slot's deck in the design's decks (by its code; else the deck whose height is its top's).</summary>
        public static int DeckIndex(this DeckSlotLayout l, int n)
        {
            var s = l.Slots[n];
            var decks = l.Def?.Decks ?? Array.Empty<DeckDef>();
            int i = Array.FindIndex(decks, d => d.Code == s.Deck);
            if (i >= 0 || decks.Length == 0) return Math.Max(0, i);
            double best = double.MaxValue;
            for (int k = 0; k < decks.Length; k++)
                if (Math.Abs(decks[k].Y - s.Top) < best) { best = Math.Abs(decks[k].Y - s.Top); i = k; }
            return i;
        }

        /// <summary>A deck cell as a part: its deck, its cell on her grid; its top, or its underside (seen from below).</summary>
        public static ShipPart PartOf(this DeckSlotLayout l, int n, bool under)
        {
            var s = l.Slots[n];
            return ShipPart.Cell(l.DeckIndex(n), l.CellI(s.CX), l.CellK(s.CZ), under);
        }
    }

    /// <summary>
    /// (0.3.28, step D2; docs/slots.md §11) Deck cells and pieces put aboard taking shots, pure (the server and the offline
    /// tests): which pieces take shots and what, the hooks a blow uses (<see cref="ShotHooks"/>), what a hurt piece needs,
    /// the rule that a cell something stands on stops at cracked.
    /// </summary>
    public static class PieceDamage
    {
        /// <summary>A piece that takes shots (a partition, a door, a window, a railing).</summary>
        public static bool Takes(ShipPiece p) => p != null && p.Role is PieceRole.Partition or PieceRole.Door or PieceRole.Window or PieceRole.Railing;

        /// <summary>What it takes (kJ): 0 for one that doesn't take shots (it's woodwork to the shot, as before).</summary>
        public static double Toughness(DamageDef d, ShipPiece p) => p?.Role switch
        {
            PieceRole.Partition => d.PartitionToughness,
            PieceRole.Door => d.DoorToughness,
            PieceRole.Window => d.WindowToughness,
            PieceRole.Railing => d.RailingToughness,
            _ => 0
        };

        /// <summary>The most hurt a cell or a piece something stands on or hangs from may have: cracked, never shot away.</summary>
        public static float HeldCap(DamageDef d) => Math.Max(d.Cracked, d.Holed - 0.01f);

        /// <summary>The piece (id) that takes shots whose solid a voxel of her grid is (an open door's leaf isn't in her grid), or -1.</summary>
        public static int At(ShipPieceSet pieces, VoxelGrid g, int i, int j, int k)
        {
            if (pieces == null || g == null || pieces.Count == 0) return -1;
            double x = g.OX + (i + 0.5) * g.V, y = g.OY + (j + 0.5) * g.V, z = g.OZ + (k + 0.5) * g.V;
            foreach (var p in pieces.All)
            {
                if (!Takes(p) || p.Role == PieceRole.Door && p.Open) continue;
                foreach (var s in p.Solids)
                    if (s?.Length >= 6 && x > s[0] && x < s[3] && y > s[1] && y < s[4] && z > s[2] && z < s[5]) return p.Id;
            }
            return -1;
        }

        /// <summary>
        /// The hooks for a blow on her: her deck cells that are out (cellOut, by slot), a cell something stands on (cellHeld, by
        /// slot: stops at cracked), her pieces that take shots (a piece something hangs on or stands on stops at cracked).
        /// </summary>
        public static ShotHooks Hooks(DamageLayout l, VoxelGrid g, ShipPieceSet pieces, MountLayout mounts, Func2 cellOut, Func2 cellHeld)
        {
            var d = l.D;
            return new ShotHooks
            {
                Out = cellOut == null ? null : n => l.Parts[n].Cell >= 0 && cellOut(l.Parts[n].Cell),
                Cap = cellHeld == null ? null : n => l.Parts[n].Cell >= 0 && cellHeld(l.Parts[n].Cell) ? HeldCap(d) : 1f,
                PieceAt = pieces == null ? null : (i, j, k) => At(pieces, g, i, j, k),
                PieceToughness = id => Toughness(d, pieces?.Get(id)),
                PieceHurt = id => pieces?.Get(id)?.Hurt ?? 0,
                PieceCap = id => mounts != null && pieces?.Get(id) is ShipPiece p && pieces.StandingOn(mounts, p) != null ? HeldCap(d) : 1f,
                SetPieceHurt = (id, h) => { if (pieces?.Get(id) is ShipPiece p) p.Hurt = h; },
            };
        }

        /// <summary>
        /// Is a deck cell held by what's on it (the offline tests' rule; the server's is <see cref="EntityShip.CellHeld"/>, the
        /// same and her fixtures too): a laid gun's carriage and working room over it, a piece standing on it, hanging under it
        /// or passing through it.
        /// </summary>
        public static bool CellHeld(ShipDefinition def, int n, IEnumerable<ShipPiece> pieces, System.Func<GunPlaceDef, bool> gunLaid)
        {
            var l = def?.Slots;
            if (l == null || n < 0 || n >= l.Count || l.Slots[n]?.Box == null) return false;
            if (def.GunPlaces != null)
                foreach (var gp in def.GunPlaces)
                    if (gunLaid?.Invoke(gp) != false && GunPlaces.Cells(def, gp).Contains(n)) return true;
            if (pieces != null)
                foreach (var p in pieces)
                    foreach (var b in p.Solids.Concat(p.Sel))
                        if (OnCell(l.Slots[n], b)) return true;
            return false;
        }

        /// <summary>A box rests on a cell, hangs under it, or passes through its planking (its footprint over the cell's).</summary>
        public static bool OnCell(DeckSlotDef d, double[] b)
        {
            const double e = 0.02;
            var c = d.Box;
            if (b[0] >= c[3] - e || b[3] <= c[0] + e || b[2] >= c[5] - e || b[5] <= c[2] + e) return false;
            bool rests = b[1] >= c[4] - e && b[1] <= c[4] + 0.3;
            bool hangs = b[4] <= c[1] + e && b[4] >= c[1] - 0.3;
            bool through = b[1] < c[4] - e && b[4] > c[1] + e;
            return rests || hangs || through;
        }

        /// <summary>What a hurt piece needs: cracked, 2 boards and nails; scraped, a board (held right-click, the repair tool's way).</summary>
        public static List<RepairJob> Needs(DamageDef d, float hurt)
        {
            var jobs = new List<RepairJob>();
            if (hurt >= d.Cracked) jobs.Add(new RepairJob { Part = -1, What = "planks", Kits = new[] { new RepairKit((ShipRepairs.Plank, 2), (ShipRepairs.Nails, 1)) }, HurtAfter = 0 });
            else if (hurt >= d.Scraped) jobs.Add(new RepairJob { Part = -1, What = "planks", Kits = new[] { new RepairKit((ShipRepairs.Plank, 1)) }, HurtAfter = 0 });
            return jobs;
        }

        /// <summary>The repair the held item is for, or null.</summary>
        public static RepairJob For(DamageDef d, float hurt, string held)
        {
            if (held == null) return null;
            var jobs = Needs(d, hurt);
            return jobs.Count > 0 && Array.Exists(jobs[0].Kits, k => Array.Exists(k.Items, it => RepairKit.Matches(it.Code, held))) ? jobs[0] : null;
        }

        /// <summary>A piece's state by its hurt (for the look-at text and the notices): sound, scraped, cracked, holed (shot away).</summary>
        public static PartState StateOf(DamageDef d, float hurt) =>
            hurt >= d.Holed ? PartState.Holed : hurt >= d.Cracked ? PartState.Cracked : hurt >= d.Scraped ? PartState.Scraped : PartState.Sound;
    }

    /// <summary>
    /// (0.3.28, step D2) Damage saved before her deck cells were parts (hansaDmgF "v" under 3) for a design with deck slots:
    /// her decks were parts a frame space long. Each one cracked or worse cracks the deck cell nearest the middle of its frame
    /// space on that deck (never more than cracked: nothing opens on loading); the rest is dropped. Pure (the offline tests).
    /// </summary>
    public static class CellMigration
    {
        public static (int[] Codes, float[] Hurt, float[] Wear, int Moved) Map(ShipDefinition def, int[] codes, float[] hurt, float[] wear)
        {
            var l = def?.Slots;
            if (l == null || codes == null || hurt == null || wear == null) return (codes, hurt, wear, 0);
            var d = def.Damage ?? new DamageDef();
            var keep = new List<(int, float, float)>();
            var moved = new Dictionary<int, float>();
            double x0 = def.Frames?.X0 ?? 0, space = def.Frames?.Space ?? 1.5;
            for (int i = 0; i < codes.Length && i < hurt.Length && i < wear.Length; i++)
            {
                var p = ShipPart.Decode(codes[i]);
                if (p.Kind != ShipPartKind.Deck) { keep.Add((codes[i], hurt[i], wear[i])); continue; }
                if (hurt[i] < d.Cracked || p.Index >= def.Decks.Length) continue;
                string deck = def.Decks[p.Index].Code;
                double x = x0 + (p.Station + 0.5) * space;
                int best = -1;
                double bd = double.MaxValue;
                foreach (var s in l.Slots)
                {
                    if (s?.Box == null || !s.IsCell || s.Deck != deck || s.StartsEmpty) continue;
                    double dist = Math.Abs(s.CX - x) + 0.01 * Math.Abs(s.CZ);
                    if (dist < bd) { bd = dist; best = s.Index; }
                }
                if (best < 0 || bd > space) continue;
                int code = l.PartOf(best, false).Encode();
                moved[code] = Math.Max(moved.TryGetValue(code, out float h) ? h : 0f, Math.Min(hurt[i], d.Cracked));
            }
            foreach (var kv in moved) keep.Add((kv.Key, kv.Value, 0f));
            return (keep.Select(k => k.Item1).ToArray(), keep.Select(k => k.Item2).ToArray(), keep.Select(k => k.Item3).ToArray(), moved.Count);
        }
    }
}
