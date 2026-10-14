using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    /// <summary>
    /// (0.3.28, note 230: "i want to move companion way here. managed to place stairs but cannot enclose it with walls, roof
    /// and doors") Enclosing a stair well on an open deck: a companion hood.
    /// - Walls round an opening: a border place between two deck cells is anchored on one of them (its solid quarter-block
    ///   layer there, usable only while that cell is in). Round a well whose cells the player took up, half its sides were
    ///   anchored on the well's own cells ("cell-empty-under", and the crosshair offered nothing there). Each such place now
    ///   has a twin on the same segment anchored on the cell across the line, usable only while the first one's anchor cell
    ///   is out: a wall round a well always stands with its solid layer on the deck beside it, never in the well (the stair
    ///   in it keeps its whole width), and a door at the stair's head swings out onto the landing.
    /// - A roof over it: on an open deck a hood's place over every deck cell (and over an opening: it doesn't need the cell),
    ///   a plank board lying on the walls' tops, its underside 2 over the deck ("hh:deck:i:k"), solid in the voxel layer over
    ///   that: a man (1.85) walks up a stair under it and steps off it under the roof.
    /// Made for a design with deck slots (the cells the player can take up), after her openings' places
    /// (<see cref="ShipDefinition.Init"/>).
    /// </summary>
    public partial class MountLayout
    {
        /// <summary>(0.3.28) A hood's roof board: its underside over the deck (on the open deck's walls, two rows high), its thickness.</summary>
        public const double HoodHeight = OpenDeckRows + 0.002, HoodThickness = ShelfThickness;   // ((0.3.29) a hair over the walls' tops: not in a wall's plane where one meets it)

        /// <summary>(0.3.28) Is this place a hood's roof ("hh:deck:i:k"): a horizontal place lying on the walls' tops, not under its plane.</summary>
        public static bool IsHood(string mountId) => mountId != null && mountId.StartsWith("hh:");

        /// <summary>(0.3.28) A border place's twin across its line (or the first one, from the twin), or null.</summary>
        public MountPoint TwinOf(MountPoint m) => m?.Twin != null ? Get(m.Twin) : null;

        /// <summary>(0.3.28) The hood's place over a deck cell, or null.</summary>
        public MountPoint HoodAt(string deck, int i, int k) => Get($"hh:{deck}:{i}:{k}");

        /// <summary>
        /// (0.3.28, note 230) The places for enclosing a well (her deck cells known: slots): the border places' twins across
        /// their lines where their anchor cell is a deck slot, and the hoods' places over the open decks' cells. Returns how
        /// many were added; binds them to her cells.
        /// </summary>
        public int WellPlaces(DeckSlotLayout slots)
        {
            if (slots == null || !OnShipGrid || Def?.Grid == null) return 0;
            int n = Twins(slots) + Hoods();
            foreach (var m in All) ById[m.Id] = m;
            borderAt = null;
            BindSlots(slots);
            return n;
        }

        int Twins(DeckSlotLayout slots)
        {
            var g = Def.Grid;
            const double e = 0.02;
            int cells = Math.Max(1, (int)Math.Round(1 / g.V));
            int added = 0;
            // (row by row up each segment: a row's twin stands over the twin under it)
            foreach (var m in Borders.Where(b => !b.TwinSide && b.Twin == null).OrderBy(b => b.Row).ToList())
            {
                double top = Def.Decks.FirstOrDefault(d => d.Code == m.Deck)?.Y ?? double.NaN;
                if (double.IsNaN(top)) continue;
                // (only where the first one's anchor is a cell that can be taken up)
                if (slots.OnDeck(top, m.X + DX[m.In] * 0.3, m.Z + DZ[m.In] * 0.3) < 0) continue;
                int dir = (m.In + 2) % 4;          // from the line into the twin's anchor cell
                var (line, alongX) = BorderLine(m);
                double tx = m.X + DX[dir] * 0.5, tz = m.Z + DZ[dir] * 0.5;
                // (deck across the line: a cell of hers, or her planking there)
                if (slots.OnDeck(top, tx, tz) < 0 && !g.SolidAt(tx, top - g.V / 2, tz)) continue;
                string under = null;
                if (m.Row > 0)
                {
                    var below = m.Under != null ? Get(m.Under) : null;
                    if (below?.Twin == null) continue;
                    under = below.Twin;
                }
                int ci = m.I + DX[dir], ck = m.K + DZ[dir];
                string id = $"b:{m.Deck}:{ci}:{ck}:{DirCode[(dir + 2) % 4][0]}:{m.Row}";
                if (ById.ContainsKey(id)) continue;
                // the twin's quarter-block layer, on its anchor side of the line
                double outSign = dir < 2 ? 1 : -1;
                double layer = line + outSign * g.V / 2;
                double along0 = (alongX ? m.X : m.Z) - 0.5, ry = m.Y;
                var free = new bool[cells, cells];
                int nFree = 0;
                for (int a = 0; a < cells; a++)
                    for (int bb = 0; bb < cells; bb++)
                    {
                        double al = along0 + (a + 0.5) * g.V, y = ry + (bb + 0.5) * g.V;
                        bool solid = alongX ? g.SolidAt(al, y, layer) : g.SolidAt(layer, y, al);
                        // (not over the first one's own free part: the twin's no bigger than the segment it shares)
                        bool inFirst = m.Clip == null || m.Clip.Any(c => al > c[0] && al < c[2] && y > c[1] && y < c[3]);
                        free[a, bb] = !solid && inFirst && (alongX ? InsideHer(al, y, layer) : InsideHer(layer, y, al));
                        if (free[a, bb]) nFree++;
                    }
                if (nFree * 8 < cells * cells) continue;
                double c0 = Math.Min(line, line + outSign * 0.25), c1 = Math.Max(line, line + outSign * 0.25);
                var inside = alongX ? new[] { along0, ry, c0, along0 + 1, ry + 1, c1 } : new[] { c0, ry, along0, c1, ry + 1, along0 + 1 };
                bool whole = nFree == cells * cells && !g.Collides(inside[0] + e, inside[1] + e, inside[2] + e, inside[3] - e, inside[4] - e, inside[5] - e);
                var clip = whole ? null : ClipRuns(free, cells, along0, ry, g.V);
                double cy1 = clip == null ? ry + 1 : clip.Max(c => c[3]), cy0 = clip == null ? ry : clip.Min(c => c[1]);
                double ca0 = clip == null ? along0 : clip.Min(c => c[0]), ca1 = clip == null ? along0 + 1 : clip.Max(c => c[2]);
                // (kept clear: the twin's own side of the line)
                double h0 = Math.Min(line, line + outSign * 0.01), h1 = Math.Max(line, line + outSign * 0.01);
                if (alongX ? ClearHitCore(ca0, cy0, h0, ca1, cy1, h1) != null : ClearHitCore(h0, cy0, ca0, h1, cy1, ca1) != null) continue;
                double s0 = line - 0.125, s1 = line + 0.125;
                var t = new MountPoint
                {
                    Id = id, Kind = MountKind.Border, Deck = m.Deck, Room = m.Room, X = m.X, Y = m.Y, Z = m.Z, I = ci, K = ck, Row = m.Row,
                    In = dir, Normal = new double[] { DX[dir], 0, DZ[dir] },
                    Box = alongX ? new[] { ca0, cy0, s0, ca1, cy1, s1 } : new[] { s0, cy0, ca0, s1, cy1, ca1 },
                    Under = under, Clip = clip, Twin = m.Id, TwinSide = true
                };
                m.Twin = id;
                All.Add(t);
                ById[id] = t;
                added++;
            }
            return added;
        }

        int Hoods()
        {
            var g = Def.Grid;
            const double e = 0.02;
            int added = 0;
            foreach (var m in Floors.ToList())
            {
                // (on an open deck: a closed room has its shelves and its own deck over it)
                var room = Def.RoomAt(m.X + 0.5, m.Y + 0.5, m.Z + 0.5);
                if (room?.Enclosed == true) continue;
                double y0 = m.Y + HoodHeight;
                // (the board and the voxel layer it's solid in free of her)
                if (g.Collides(m.X + e, y0 + e, m.Z + e, m.X + 1 - e, y0 + g.V - e, m.Z + 1 - e)) continue;
                var box = new[] { m.X, y0, m.Z, m.X + 1, y0 + HoodThickness, m.Z + 1 };
                if (ClearHit(box[0], box[1], box[2], box[3], box[4], box[5]) != null) continue;
                string id = $"hh:{m.Deck}:{m.I}:{m.K}";
                if (ById.ContainsKey(id)) continue;
                var h = new MountPoint
                {
                    Id = id, Kind = MountKind.Horizontal, Deck = m.Deck, Room = room?.Code ?? m.Room,
                    X = m.X + 0.5, Y = box[4], Z = m.Z + 0.5, I = m.I, K = m.K, Level = OpenDeckRows, Box = box
                };
                All.Add(h);
                ById[id] = h;
                added++;
            }
            return added;
        }
    }

    /// <summary>(0.3.28) A walked stair's landings: where one steps off it at its foot and onto the deck past its top.</summary>
    public static class StairLanding
    {
        /// <summary>Is one of a stair plan's two landings (its room's last two boxes) in her collision (grid: what it's put against)?</summary>
        public static bool At(PiecePlan plan, VoxelGrid grid)
        {
            if (plan?.Role != PieceRole.Stair || grid == null || plan.Room.Count < 2) return false;
            for (int n = plan.Room.Count - 2; n < plan.Room.Count; n++)
            {
                var s = plan.Room[n];
                if (grid.Collides(s[0] + 0.03, s[1] + 0.03, s[2] + 0.03, s[3] - 0.03, s[4] - 0.03, s[5] - 0.03)) return true;
            }
            return false;
        }
    }

    /// <summary>(0.3.28) What open deck cells refuse a piece (pure: the ship and the offline tests share it).</summary>
    public static class OpeningRules
    {
        /// <summary>
        /// (0.3.20) A piece planned to stand on an open deck cell (open(n): taken up, no shut hatch over it): any of its boxes
        /// resting on it. A hatch goes over openings, a stair or a ladder-stair through them. Its lang code, or null.
        /// </summary>
        public static string OverOpening(DeckSlotLayout l, Func<int, bool> open, PiecePlan plan)
        {
            if (l == null || plan == null || plan.Role is PieceRole.Hatch or PieceRole.Ladder or PieceRole.Stair) return null;
            foreach (var b in plan.Solids.Concat(plan.Sel))
                foreach (int n in l.Under(b, double.NaN))
                {
                    var d = l.Slots[n];
                    if (!open(n)) continue;
                    if (b[1] >= d.Top - 0.02 && b[1] <= d.Top + 0.3) return "hansa:cell-empty-under";
                }
            return null;
        }
    }
}
