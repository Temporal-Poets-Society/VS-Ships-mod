using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    /// <summary>What a drawn part of a modular piece is made of (the renderer's texture for it).</summary>
    public enum PartMat { Wood, Planks, Glass, Lead, Iron, Brick, Soot }

    /// <summary>
    /// A drawn part of a modular piece (ship-local): an axis-aligned box, or a beam from P0 to P1 (its end faces' middles),
    /// W wide across (level) and H high (upright), sheared along a slope (a raked handrail, a ladder's stringer).
    /// </summary>
    public struct ModularPart
    {
        public PartMat Mat;
        public double[] Box;
        public double[] P0, P1;
        public double W, H;
        public bool AlongX;
        public bool IsBeam => P0 != null;

        public static ModularPart B(PartMat mat, double x0, double y0, double z0, double x1, double y1, double z1) =>
            new() { Mat = mat, Box = new[] { Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1) } };

        public static ModularPart B(PartMat mat, double[] b) => B(mat, b[0], b[1], b[2], b[3], b[4], b[5]);

        public static ModularPart Beam(PartMat mat, double[] p0, double[] p1, bool alongX, double w, double h) =>
            new() { Mat = mat, P0 = p0, P1 = p1, W = w, H = h, AlongX = alongX };

        /// <summary>A beam whose ends are at one height is a box; else null.</summary>
        public double[] AsBox()
        {
            if (!IsBeam) return Box;
            if (Math.Abs(P0[1] - P1[1]) > 1e-9) return null;
            double hw = W / 2, hh = H / 2;
            return AlongX
                ? new[] { Math.Min(P0[0], P1[0]), P0[1] - hh, P0[2] - hw, Math.Max(P0[0], P1[0]), P0[1] + hh, P0[2] + hw }
                : new[] { P0[0] - hw, P0[1] - hh, Math.Min(P0[2], P1[2]), P0[0] + hw, P0[1] + hh, Math.Max(P0[2], P1[2]) };
        }
    }

    /// <summary>
    /// (0.3.18) The modular pieces' shapes as parts (pure geometry: the renderer draws them, the offline tests look for
    /// faces of two parts lying in one plane, which flicker: notes 164, 166): a window (two by two lights in a mullion
    /// and transom, each leaded two by two: note 163), a railing (its posts shared at a corner: note 166), a ladder-stair
    /// (note 162), the hearth's firebed and its flue (note 165).
    /// </summary>
    public static class ModularGeometry
    {
        public const double WindowFrame = 0.0625, Mullion = 0.05, CameWidth = 0.014, CameDepth = 0.012;
        /// <summary>(0.3.18, note 164) How far a door put aboard swings open: 86° of a quarter turn (its open leaf never in a wall's plane beside it).</summary>
        public const float DoorOpenFraction = 86f / 90f;
        /// <summary>A railing's posts (half each way) and how far apart from the line they stand.</summary>
        public const double PostHalf = 0.06;

        static double[] OnLine(bool alongX, double line, double a, double y, double c) => alongX ? new[] { a, y, line + c } : new[] { line + c, y, a };

        static ModularPart LineBox(PartMat mat, bool alongX, double line, double a0, double a1, double y0, double y1, double c0, double c1) =>
            alongX ? ModularPart.B(mat, a0, y0, line + c0, a1, y1, line + c1) : ModularPart.B(mat, line + c0, y0, a0, line + c1, y1, a1);

        /// <summary>
        /// (0.3.18, note 163: "the leaded window looks like jail bars") A window: its casing inside the block, a wooden
        /// mullion and transom splitting it into four lights, each light's clear glass leaded two by two (4 × 4 small panes
        /// in all), the lead thin; the across-way cames a hair proud of the upright ones where they cross.
        /// </summary>
        public static List<ModularPart> Window(MountPoint m, double lo = 0, double hi = 0)
        {
            var list = new List<ModularPart>();
            var (line, alongX) = MountLayout.BorderLine(m);
            double a0 = (alongX ? m.X : m.Z) - 0.5, a1 = a0 + 1, y0 = m.Y, y1 = m.Y + 1, h = MountLayout.PartitionThickness / 2, f = WindowFrame;
            // (its stiles reach over a corner, or stop short of it: WallEnds)
            list.Add(LineBox(PartMat.Wood, alongX, line, a0 - lo, a0 + f, y0, y1, -h, h));
            list.Add(LineBox(PartMat.Wood, alongX, line, a1 - f, a1 + hi, y0, y1, -h, h));
            list.Add(LineBox(PartMat.Wood, alongX, line, a0 + f, a1 - f, y0, y0 + f, -h, h));
            list.Add(LineBox(PartMat.Wood, alongX, line, a0 + f, a1 - f, y1 - f, y1, -h, h));
            // the mullion (upright, whole height between the casing) and the transom (across, in two halves beside it)
            double am = (a0 + a1) / 2, ym = (y0 + y1) / 2, mw = Mullion / 2, mh = h * 0.8;
            list.Add(LineBox(PartMat.Wood, alongX, line, am - mw, am + mw, y0 + f, y1 - f, -mh, mh));
            list.Add(LineBox(PartMat.Wood, alongX, line, a0 + f, am - mw, ym - mw, ym + mw, -mh, mh));
            list.Add(LineBox(PartMat.Wood, alongX, line, am + mw, a1 - f, ym - mw, ym + mw, -mh, mh));
            // each light: its glass, a came up its middle and one across
            foreach (var (la0, la1) in new[] { (a0 + f, am - mw), (am + mw, a1 - f) })
                foreach (var (ly0, ly1) in new[] { (y0 + f, ym - mw), (ym + mw, y1 - f) })
                {
                    list.Add(LineBox(PartMat.Glass, alongX, line, la0 + 0.001, la1 - 0.001, ly0 + 0.001, ly1 - 0.001, -0.004, 0.004));
                    double ca = (la0 + la1) / 2, cy = (ly0 + ly1) / 2, w = CameWidth / 2;
                    list.Add(LineBox(PartMat.Lead, alongX, line, ca - w, ca + w, ly0, ly1, -CameDepth, CameDepth));
                    list.Add(LineBox(PartMat.Lead, alongX, line, la0, ca - w, cy - w, cy + w, -CameDepth - 0.002, CameDepth + 0.002));
                    list.Add(LineBox(PartMat.Lead, alongX, line, ca + w, la1, cy - w, cy + w, -CameDepth - 0.002, CameDepth + 0.002));
                }
            return list;
        }

        /// <summary>
        /// (0.3.18, notes 164, 166: corners flicker) Where two wall pieces (partitions, windows, doors) meet at a corner, both
        /// filled the corner's square and their tops lay in one plane: a wall along x stops short of the corner by half its
        /// thickness and one along z reaches over it by that much (unless another along z goes on past the corner), so the
        /// corner is filled once. Doors (drawn from their block) keep their length, and a wall along x meeting a door across
        /// it stops at the door's face ((0.3.29); it kept its own before). Returns how much longer (+) or shorter (-) the piece is drawn at its -x/-z end and its +x/+z end.
        /// </summary>
        public static (double Lo, double Hi) WallEnds(MountLayout layout, IEnumerable<ShipPiece> pieces, ShipPiece wall)
        {
            var m = layout?.Get(wall?.Mount);
            if (m == null || m.Kind != MountKind.Border || wall.Role is not (PieceRole.Partition or PieceRole.Window)) return (0, 0);
            var (line, alongX) = MountLayout.BorderLine(m);
            double a0 = (alongX ? m.X : m.Z) - 0.5, a1 = a0 + 1, y0 = m.Y, y1 = m.Y + 1, h = MountLayout.PartitionThickness / 2;
            double End(double a, int side)
            {
                double ex = alongX ? a : line, ez = alongX ? line : a;
                bool perp = false, beyond = false, door = false;
                foreach (var q in pieces)
                {
                    if (q.Id == wall.Id || !q.OnBorder || q.Role == PieceRole.Railing || q.Mounts.Length == 0) continue;
                    var qm = layout.Get(q.Mounts[0]);
                    // (a wall only partly free of her, under a beam, takes part too: its free parts at the corner: AdjustClip)
                    if (qm == null || qm.Kind != MountKind.Border) continue;
                    double qy0 = qm.Y, qy1 = qm.Y + (q.Role == PieceRole.Door ? 2 : 1);
                    if (Math.Min(y1, qy1) - Math.Max(y0, qy0) < 0.01) continue;
                    var (ql, qx) = MountLayout.BorderLine(qm);
                    double qa = qx ? qm.X : qm.Z;
                    bool atE = false;
                    foreach (double qe in new[] { qa - 0.5, qa + 0.5 })
                        if (Math.Abs((qx ? qe : ql) - ex) < 0.01 && Math.Abs((qx ? ql : qe) - ez) < 0.01) atE = true;
                    if (!atE) continue;
                    if (qx != alongX) { perp |= q.Role != PieceRole.Door; door |= q.Role == PieceRole.Door; }
                    else if (Math.Abs(ql - line) < 0.01 && Math.Sign(qa - a) == side) beyond = true;
                }
                // ((0.3.29, note 233: the companion's walls meet its door, the great cabin's) a door across its end: the wall
                // stops at the door's face (its box keeps its length)
                if (!perp) return door ? -h : 0;
                if (alongX) return -h;
                return beyond ? 0 : h;
            }
            return (End(a0, -1), End(a1, 1));
        }

        /// <summary>
        /// (0.3.18) A partly free wall's parts (along0, y0, along1, y1 in its line's plane) with its ends as WallEnds has
        /// them: a part reaching its -x/-z end drawn lo longer there, one reaching its +x/+z end hi longer.
        /// </summary>
        public static List<double[]> AdjustClip(MountPoint m, IEnumerable<double[]> rects, double lo, double hi)
        {
            var (_, alongX) = MountLayout.BorderLine(m);
            double a0 = (alongX ? m.X : m.Z) - 0.5, a1 = a0 + 1;
            var list = new List<double[]>();
            foreach (var r in rects)
            {
                var c = (double[])r.Clone();
                if (c[0] <= a0 + 0.01) c[0] -= lo;
                if (c[2] >= a1 - 0.01) c[2] += hi;
                if (c[2] > c[0] + 1e-6) list.Add(c);
            }
            return list;
        }

        /// <summary>How a railing's end meets what's there: its own half post, a corner post it draws for the railings meeting there, another's post or a wall (none).</summary>
        public enum EndPost { Half, Shared, None }

        /// <summary>
        /// (0.3.18, note 166: "railing corners flicker") What a railing's end (at a point ship-local x, z on its line, its
        /// deck's y) meets: another railing's end there (the one with the lowest id draws one post for all of them, centred
        /// on the corner; the others none), a wall piece across it (a partition, a window, a door ending there: no post, the
        /// rails stop at its face), or nothing (a half post inside its own segment).
        /// </summary>
        public static EndPost RailingEnd(MountLayout layout, IEnumerable<ShipPiece> pieces, ShipPiece railing, double ex, double ez, out double stop)
        {
            stop = PostHalf;
            var m = layout?.Get(railing.Mount);
            if (m == null) return EndPost.Half;
            var (line, alongX) = MountLayout.BorderLine(m);
            bool shared = false, owner = true;
            // (0.3.19) a raked railing's end and a deck railing's end on the deck its stair climbs to, at one corner (a walked
            // stair's head at a castle's front): the raked one draws a newel post there, centred on the corner, up to its
            // handrail; the deck's railing none (its rails stop at the newel's face)
            double EndRise(ShipPiece r, MountPoint rm, double x, double z)
            {
                if (r.Rake == null) return 0;
                var (rl, rx) = MountLayout.BorderLine(rm);
                double ra = rx ? rm.X : rm.Z;
                return Math.Abs((rx ? x : z) - (ra - 0.5)) < 0.01 ? r.Rake[0] : r.Rake[1];
            }
            foreach (var q in pieces)
            {
                if (q.Id == railing.Id || q.Role != PieceRole.Railing || q.Mounts.Length == 0) continue;
                var qm2 = layout.Get(q.Mounts[0]);
                if (qm2 == null || (railing.Rake == null) == (q.Rake == null)) continue;
                var (ql2, qx2) = MountLayout.BorderLine(qm2);
                double qa2 = qx2 ? qm2.X : qm2.Z;
                bool meets = false;
                foreach (double qe in new[] { qa2 - 0.5, qa2 + 0.5 })
                    if (Math.Abs((qx2 ? qe : ql2) - ex) < 0.01 && Math.Abs((qx2 ? ql2 : qe) - ez) < 0.01) meets = true;
                if (!meets) continue;
                var (raked, rm, flat, fm) = railing.Rake != null ? (railing, m, q, qm2) : (q, qm2, railing, m);
                double top = rm.Y + EndRise(raked, rm, ex, ez);
                if (fm.Y < rm.Y + 0.5 || fm.Y > top + 0.01) continue;
                return railing.Rake != null ? EndPost.Shared : NoneStop(out stop);
            }
            static EndPost NoneStop(out double s) { s = PostHalf; return EndPost.None; }
            foreach (var q in pieces)
            {
                if (q.Id == railing.Id || !q.OnBorder || q.Mounts.Length == 0) continue;
                if (q.Role == PieceRole.Door && q.Open) continue;
                var qm = layout.Get(q.Mounts[0]);
                if (qm == null || qm.Kind != MountKind.Border) continue;
                double qtop = q.Role == PieceRole.Door ? qm.Y + 2 : qm.Y + 1;
                if (m.Y < qm.Y - 0.01 || m.Y > qtop - 0.01) continue;
                var (ql, qx) = MountLayout.BorderLine(qm);
                double qa = qx ? qm.X : qm.Z;
                bool atEnd = false;
                foreach (double qe in new[] { qa - 0.5, qa + 0.5 })
                {
                    double qex = qx ? qe : ql, qez = qx ? ql : qe;
                    if (Math.Abs(qex - ex) < 0.01 && Math.Abs(qez - ez) < 0.01) atEnd = true;
                }
                if (!atEnd) continue;
                if (q.Role != PieceRole.Railing)
                {
                    // a wall piece across the end: it's the post
                    if (qx != alongX) { stop = MountLayout.PartitionThickness / 2; return EndPost.None; }
                    continue;
                }
                if (Math.Abs(qm.Y - m.Y) > 0.01) continue;
                shared = true;
                if (q.Id < railing.Id) owner = false;
            }
            return !shared ? EndPost.Half : owner ? EndPost.Shared : EndPost.None;
        }

        /// <summary>
        /// (0.3.18, note 166) A railing raked beside a stair is drawn up to the deck its stair climbs to (absolute y), not
        /// over it, where that deck has a piece of its own on the same line over it (the well's railing): the two were
        /// drawn in one plane. Else no cap.
        /// </summary>
        public static double RailingCap(MountLayout layout, ShipPieceSet pieces, ShipPiece railing)
        {
            var m = layout?.Get(railing?.Mount);
            if (m == null || railing.Rake == null || pieces == null) return double.MaxValue;
            foreach (var st in pieces.All)
            {
                // (0.3.19: only a walked stair has raked railings beside it)
                if (st.Role != PieceRole.Stair || st.Rise <= 0 || layout.Get(st.Mount) is not MountPoint foot) continue;
                if (PiecePlanner.StairRake(foot, st.Dir, st.Rise, m) == null) continue;
                var (line, alongX) = MountLayout.BorderLine(m);
                var up = layout.BorderAt(alongX, line, alongX ? m.X : m.Z, m.Y + st.Rise);
                return up != null && pieces.At(up.Id) is ShipPiece over && over.OnBorder ? m.Y + st.Rise : double.MaxValue;
            }
            return double.MaxValue;
        }

        /// <summary>
        /// A railing (0.3.16): its posts (by <see cref="RailingEnd"/>: a half post, one corner post shared, or none where a
        /// wall piece or another's post is), a handrail and a foot rail between them (stopping at the posts' faces: no face
        /// in two parts' plane), four balusters; raked beside a stair (rake: its handrail over the ladder's line at its two
        /// ends). ends: how its -x/-z and +x/+z ends meet what's there, and where the rails stop from each.
        /// </summary>
        /// <summary>(0.3.24, note 188) A railing's balusters: half their thickness (0.07 square), their pitch and the first's place from a segment's end.</summary>
        public const double BalusterHalf = 0.035, BalusterPitch = 0.2, BalusterFirst = 0.1;

        public static List<ModularPart> Railing(MountPoint m, double[] rake, double[] box, EndPost end0 = EndPost.Half, double stop0 = PostHalf, EndPost end1 = EndPost.Half, double stop1 = PostHalf, double cap = double.MaxValue)
        {
            var list = new List<ModularPart>();
            var (line, alongX) = MountLayout.BorderLine(m);
            double a0 = (alongX ? m.X : m.Z) - 0.5, a1 = a0 + 1, y = m.Y;
            double r0 = rake?[0] ?? 0, r1 = rake?[1] ?? 0, top = rake == null ? 1.0 : PiecePlanner.RailHeight;
            if (m.Clip != null && box != null && rake == null)
            {
                a0 = alongX ? box[0] : box[2];
                a1 = alongX ? box[3] : box[5];
                y = box[1];
                top = Math.Max(0.2, Math.Min(1.0, box[4] - box[1]));
            }
            double span = Math.Max(1e-6, a1 - a0);
            double H(double a) => r0 + (r1 - r0) * (a - a0) / span;
            double foot = rake == null ? 0.06 : -0.12, ph = PostHalf;
            double capH = cap - y;
            void Post(double p0, double p1)
            {
                double hb = Math.Max(0, Math.Min(H(p0), H(p1)) + (rake == null ? 0 : -0.25)), ht = Math.Min(capH, Math.Max(H(p0), H(p1)) + top);
                if (ht > hb + 0.05) list.Add(LineBox(PartMat.Wood, alongX, line, p0, p1, y + hb, y + ht, -ph, ph));
            }
            if (end0 == EndPost.Half) Post(a0, a0 + 2 * ph);
            else if (end0 == EndPost.Shared) Post(a0 - ph, a0 + ph);
            if (end1 == EndPost.Half) Post(a1 - 2 * ph, a1);
            else if (end1 == EndPost.Shared) Post(a1 - ph, a1 + ph);
            // (a half post is 0.12 wide inside the segment; a shared one 0.06 each side of the corner)
            double s0 = a0 + (end0 == EndPost.Half ? 2 * ph : stop0), s1 = a1 - (end1 == EndPost.Half ? 2 * ph : stop1);
            // (raked up past a deck with its own railing over it: the rails stop where they reach that deck)
            double rs0 = s0, rs1 = s1;
            if (capH < double.MaxValue / 2 && Math.Abs(r1 - r0) > 1e-6)
            {
                double aCap = a0 + (capH - top - r0) * span / (r1 - r0);
                if (r1 > r0) rs1 = Math.Min(s1, aCap); else rs0 = Math.Max(s0, aCap);
            }
            if (rs1 > rs0 + 0.01)
                list.Add(ModularPart.Beam(PartMat.Wood, OnLine(alongX, line, rs0, y + H(rs0) + top - 0.04, 0), OnLine(alongX, line, rs1, y + H(rs1) + top - 0.04, 0), alongX, 0.1, 0.08));
            if (s1 > s0 + 0.01)
                list.Add(ModularPart.Beam(PartMat.Wood, OnLine(alongX, line, s0, y + H(s0) + foot + 0.035, 0), OnLine(alongX, line, s1, y + H(s1) + foot + 0.035, 0), alongX, 0.08, 0.07));
            // (0.3.24, note 188: "in tight spaces the railing squeezes the verticals together ... keep a fixed distance and cut
            // what's not needed; the verticals thicker") balusters at a fixed pitch from the segment's grid end (three a block:
            // the same on every railing, a cut one keeping its neighbours' rhythm), those that don't fit between its posts left
            // out; as thick as the rails (a hair less across than the foot rail: no faces in one plane)
            double baseA = (alongX ? m.X : m.Z) - 0.5;
            for (int k = -1; k <= 6; k++)
            {
                double a = baseA + BalusterFirst + BalusterPitch * k;
                if (a - BalusterHalf < s0 + 0.03 || a + BalusterHalf > s1 - 0.03) continue;
                double h = H(a);
                double b0 = h + foot + 0.07, b1 = Math.Min(h + top - 0.08, capH - 0.02);
                if (b1 > b0 + 0.02) list.Add(LineBox(PartMat.Wood, alongX, line, a - BalusterHalf, a + BalusterHalf, y + b0, y + b1, -BalusterHalf, BalusterHalf));
            }
            return list;
        }

        /// <summary>
        /// (0.3.18, note 162) A companion ladder-stair: two stringers along its line (from its feet on the deck to its top at
        /// the head cell's far edge), flat treads between them about 0.3 apart, iron shoes at its feet and two iron hooks
        /// over the coaming of the deck above.
        /// </summary>
        public static List<ModularPart> Ladder(MountPoint foot, int up, double rise)
        {
            var list = new List<ModularPart>();
            bool alongX = up == 0 || up == 2;
            const double st = 0.0625, depth = 0.25;
            double cs = 0.5 - PiecePlanner.StairInset - st / 2, uF = PiecePlanner.LadderFoot, uT = uF + PiecePlanner.LadderRun;
            double[] P(double u, double yy, double c) => PiecePlanner.StairPoint(foot, up, u, yy, c);
            foreach (double c in new[] { -cs, cs })
            {
                list.Add(ModularPart.Beam(PartMat.Wood, P(uF, 0.05 - depth / 2, c), P(uT, rise + 0.05 - depth / 2, c), alongX, st, depth));
                // its shoe on the deck, its hook over the coaming
                list.Add(ModularPart.B(PartMat.Iron, PiecePlanner.StairBox(foot, up, uF - 0.07, 0.003, c - st / 2 - 0.012, uF + 0.13, 0.06, c + st / 2 + 0.012)));
                list.Add(ModularPart.B(PartMat.Iron, PiecePlanner.StairBox(foot, up, uT - 0.06, rise + 0.004, c - 0.025, uT + 0.16, rise + 0.03, c + 0.025)));
            }
            int n = PiecePlanner.LadderTreads(rise);
            double ci = cs - st / 2;
            for (int k = 1; k <= n; k++)
            {
                double yy = rise * k / (n + 1), u = uF + PiecePlanner.LadderRun * yy / rise;
                list.Add(ModularPart.B(PartMat.Planks, PiecePlanner.StairBox(foot, up, u - 0.14, yy - 0.05, -ci, u + 0.02, yy, ci)));
            }
            list.AddRange(LadderHandrails(foot, up, rise));
            return list;
        }

        /// <summary>
        /// (0.3.19) A ladder-stair's guard rails (<see cref="PiecePlanner.LadderGuards"/>: its solid boxes past its ladder's
        /// segments) as drawn: on the border line, a post near each end (0.1 in), a rail on top and one halfway, running the span.
        /// </summary>
        public static List<ModularPart> LadderGuardParts(MountPoint foot, int up, IEnumerable<double[]> guards)
        {
            var list = new List<ModularPart>();
            if (foot == null || guards == null) return list;
            const double ph = 0.04, rh = 0.035;
            foreach (var g in guards)
            {
                var (ua, ca) = PiecePlanner.StairUC(foot, up, g[0], g[2]);
                var (ub, cb) = PiecePlanner.StairUC(foot, up, g[3], g[5]);
                double u0 = Math.Min(ua, ub), u1 = Math.Max(ua, ub), side = Math.Sign(ca + cb), line = side * 0.5;
                double top = g[4] - foot.Y;
                // (its posts 0.1 in from the span's ends: a railing round the well meeting its corner has its own post there)
                foreach (double u in new[] { u0 + 0.1 + ph, u1 - 0.1 - ph })
                    list.Add(ModularPart.B(PartMat.Wood, PiecePlanner.StairBox(foot, up, u - ph, 0, line - ph, u + ph, top - 0.08, line + ph)));
                double r0 = u0 + 0.02, r1 = u1 - 0.02;
                list.Add(ModularPart.B(PartMat.Wood, PiecePlanner.StairBox(foot, up, r0, top - 0.11, line - rh, r1, top - 0.05, line + rh)));
                list.Add(ModularPart.B(PartMat.Wood, PiecePlanner.StairBox(foot, up, r0, top / 2 - 0.03, line - rh * 0.8, r1, top / 2 + 0.03, line + rh * 0.8)));
            }
            return list;
        }

        /// <summary>(0.3.19) How high a ladder-stair's handrails run over its line (the stringers' tops), their stanchions' and rail's sizes.</summary>
        public const double LadderRailHeight = 0.9, LadderStanchion = 0.045, LadderRailW = 0.05, LadderRailH = 0.06;

        /// <summary>
        /// (0.3.19, the user: "rails to ladder anyway need fixing: they are wrongly constructed") A ladder-stair's own
        /// handrails, as companion ladders had them: on each side over its stringer, upright stanchions (at its foot, its
        /// middle and its head, the last standing on the coaming of the deck above) and a wooden handrail parallel to the
        /// stringer <see cref="LadderRailHeight"/> over its line. Drawn only (the climber is moved along the ladder).
        /// </summary>
        public static List<ModularPart> LadderHandrails(MountPoint foot, int up, double rise)
        {
            var list = new List<ModularPart>();
            bool alongX = up == 0 || up == 2;
            double cs = 0.5 - PiecePlanner.StairInset - 0.0625 / 2, uF = PiecePlanner.LadderFoot, uT = uF + PiecePlanner.LadderRun;
            double[] P(double u, double yy, double c) => PiecePlanner.StairPoint(foot, up, u, yy, c);
            double Line(double u) => PiecePlanner.LadderHeight(rise, u);
            double sh = LadderStanchion / 2, top = LadderRailHeight;
            foreach (double c in new[] { -cs, cs })
            {
                // the stanchions: upright, from the stringer's top (its line + 0.05) to the rail's underside
                foreach (double u in new[] { uF + 0.08, uF + PiecePlanner.LadderRun / 2, uT - 0.06 })
                {
                    double y0 = Line(u) + 0.05, y1 = Line(u) + top - LadderRailH / 2;
                    list.Add(ModularPart.B(PartMat.Wood, PiecePlanner.StairBox(foot, up, u - sh, y0, c - sh, u + sh, y1, c + sh)));
                }
                // the handrail over them, parallel to the stringer
                list.Add(ModularPart.Beam(PartMat.Wood, P(uF + 0.08 - sh, Line(uF + 0.08 - sh) + top, c), P(uT - 0.06 + sh, Line(uT - 0.06 + sh) + top, c), alongX, LadderRailW, LadderRailH));
            }
            return list;
        }

        /// <summary>
        /// (0.3.19) A walked stair: two stringers under its nosing line (from its deck at the foot cell's near edge up to the
        /// deck above at the head cell's far edge), its treads between them a plank thick (the top one a hair under the deck
        /// above, which may reach back over it: no two faces in one plane), open underneath. (0.1.0-dev.6, note 39: "stair
        /// beams protrude") Nothing of it reaches below its deck's underside (footDeck: that deck's planking under its top):
        /// the stringers' feet are cut there, a shallower piece of each carrying its top line down to the deck. Their heads
        /// stop short of the deck above's edge, under its top. (Note 36) The short stair's treads overlap (each reaching
        /// under the next), its stringers deeper for their slope.
        /// </summary>
        public static List<ModularPart> Flight(MountPoint foot, int up, double rise, double run = PiecePlanner.FlightRun, bool rails = false, double footDeck = FlightFootDeck)
        {
            var list = new List<ModularPart>();
            bool alongX = up == 0 || up == 2;
            // (0.3.24, note 189: "stair beams could be a bit thicker and more serious") stringers 0.1 thick, 0.36 deep
            const double st = FlightStringer, tread = 0.07;
            double slope = rise / run, depth = StringerDepth(slope);
            double cs = 0.5 - PiecePlanner.StairInset - st / 2;
            double[] P(double u, double yy, double c) => PiecePlanner.StairPoint(foot, up, u, yy, c);
            // (the stringer's top along the nosings, a little under them (StringerTop); its ends cut where it meets the decks:
            // at the foot where its top meets the deck, at the head short of the deck above's edge)
            double Top(double u) => StringerTop(rise, u, run);
            double u0 = (FlightStringerDrop - FlightStringerDepth / 2 + 0.01) / slope, u1 = run - 0.05;
            // ((note 39) its bottom no lower than a hair over the deck's underside: the full stringer from where it is, its foot
            // a shallower piece (a hair lower and narrower, overlapping it: no faces in one plane))
            double floor = 0.01 - Math.Max(0.05, footDeck), uc = Math.Min(u1, Math.Max(u0, (floor + depth - Top(0)) / slope));
            foreach (double c in new[] { -cs, cs })
            {
                list.Add(ModularPart.Beam(PartMat.Wood, P(uc, Top(uc) - depth / 2, c), P(u1, Top(u1) - depth / 2, c), alongX, st, depth));
                if (uc - u0 < 0.01) continue;
                double fd = Top(u0) - 0.004 - floor - 0.002;
                list.Add(ModularPart.Beam(PartMat.Wood, P(u0, Top(u0) - 0.004 - fd / 2, c), P(uc + 0.03, Top(uc + 0.03) - 0.004 - fd / 2, c), alongX, st - 0.008, fd));
            }
            int n = PiecePlanner.FlightTreads(rise);
            double ci = cs - st / 2, past = Math.Max(0.03, FlightTreadDepth - run / n);
            for (int k = 1; k <= n; k++)
            {
                var (a, b, y) = PiecePlanner.FlightTread(rise, k, run);
                double top = k == n ? y - 0.02 : y;
                list.Add(ModularPart.B(PartMat.Planks, PiecePlanner.StairBox(foot, up, a, top - tread, -ci, k == n ? b : Math.Min(run, b + past), top, ci)));
            }
            if (rails) list.AddRange(FlightHandrails(foot, up, rise, run));
            return list;
        }

        /// <summary>
        /// (0.3.23, the user: handrails built into the ship's stairs, as the ladder's) A ship's stair's own handrails: on each
        /// side over its stringer, upright stanchions (near its foot, at its middle, near its head) and a wooden handrail
        /// parallel to its nosings' line, <see cref="PiecePlanner.RailHeight"/> over it. Drawn; its solid guards are
        /// <see cref="PiecePlanner.FlightGuards"/>.
        /// </summary>
        public static List<ModularPart> FlightHandrails(MountPoint foot, int up, double rise, double run = PiecePlanner.FlightRun)
        {
            var list = new List<ModularPart>();
            bool alongX = up == 0 || up == 2;
            double cs = 0.5 - PiecePlanner.StairInset - FlightStringer / 2;
            double[] P(double u, double yy, double c) => PiecePlanner.StairPoint(foot, up, u, yy, c);
            double Nose(double u) => PiecePlanner.FlightNosing(rise, u, run);
            double sh = FlightBaluster / 2, top = PiecePlanner.RailHeight;
            double uA = 0.1, uB = run - 0.1;
            // (0.3.24, note 189: "stairs need more railing verticals and all a bit more beefy") balusters about every
            // FlightBalusterPitch along the run (the first near its foot, the last near its head), the rail heavier
            int n = Math.Max(2, (int)Math.Round((uB - uA) / FlightBalusterPitch));
            foreach (double c in new[] { -cs, cs })
            {
                // the balusters: upright, from down in the stringer (0.3.27, note 200: "should go to and embed into stair
                // beams, now they hang a bit": they stopped 0.25 over its top) to the rail's underside
                for (int i = 0; i <= n; i++)
                {
                    double u = uA + (uB - uA) * i / n;
                    double foot0 = StringerTop(rise, u - sh, run) - FlightBalusterEmbed;
                    list.Add(ModularPart.B(PartMat.Wood, PiecePlanner.StairBox(foot, up, u - sh, foot0, c - sh, u + sh, Nose(u) + top - FlightRailH / 2, c + sh)));
                }
                list.Add(ModularPart.Beam(PartMat.Wood, P(uA - sh, Nose(uA - sh) + top, c), P(uB + sh, Nose(uB + sh) + top, c), alongX, FlightRailW, FlightRailH));
            }
            return list;
        }

        /// <summary>(0.3.24, note 189) A ship's stair's stringers' thickness, its handrails' balusters (square), their pitch along the run, its rail's width and height.</summary>
        public const double FlightStringer = 0.1, FlightBaluster = 0.06, FlightBalusterPitch = 0.3, FlightRailW = 0.09, FlightRailH = 0.08;
        /// <summary>(0.3.27, note 200) How deep a stair's baluster's foot is let into its stringer.</summary>
        public const double FlightBalusterEmbed = 0.04;
        /// <summary>(0.3.19) A walked stair's stringers: their depth, and how far their middle line is under the nosings' slope (their top 0.06 under it).</summary>
        public const double FlightStringerDepth = 0.36, FlightStringerDrop = 0.06 + FlightStringerDepth / 2;

        /// <summary>
        /// (0.1.0-dev.6, note 39) How thick a stair's foot deck is taken to be under its top when not known (her deck slots
        /// say: the caravel's 0.2, the carrack's 0.25): the thinnest, so nothing shows under either.
        /// </summary>
        public const double FlightFootDeck = 0.2;
        /// <summary>(0.1.0-dev.6, note 36) A walked stair's treads at least this deep (the short stair's overlap: each reaches under the next).</summary>
        public const double FlightTreadDepth = 0.16;

        /// <summary>(0.1.0-dev.6, note 36) A stringer's depth (upright) at this slope: 0.36, or at least 0.2 square to its line (the short stair's 72°).</summary>
        public static double StringerDepth(double slope) => Math.Max(FlightStringerDepth, 0.2 * Math.Sqrt(1 + slope * slope));

        /// <summary>(0.1.0-dev.6, note 39) The planking's thickness under a deck's top (ship-local y), from her deck slots; <see cref="FlightFootDeck"/> if they don't say.</summary>
        public static double DeckThickness(ShipDefinition def, double top)
        {
            if (def?.Slots?.Tops != null)
                foreach (var (t, thick) in def.Slots.Tops)
                    if (Math.Abs(t - top) < 0.02 && thick > 0.04) return thick;
            return FlightFootDeck;
        }

        /// <summary>(0.3.27) The top of a walked stair's stringer at u along its run, over its deck.</summary>
        public static double StringerTop(double rise, double u, double run = PiecePlanner.FlightRun) => rise / run * u - FlightStringerDrop + FlightStringerDepth / 2;

        /// <summary>
        /// (0.3.18, note 165) A placed hearth's firebed: a brick plinth filling its cell (vanilla's firepit stands on it) and a
        /// kerb on its three sides away from its front (front: the way it faces), the kerbs along z between those along x
        /// (no two in one corner).
        /// </summary>
        public static List<ModularPart> Hearth(MountPoint m, int front)
        {
            var list = new List<ModularPart>();
            double x0 = m.X, z0 = m.Z, y = m.Y, b = PiecePlanner.FirebedHeight, k = PiecePlanner.KerbHeight, t = PiecePlanner.KerbThickness;
            list.Add(ModularPart.B(PartMat.Brick, x0, y, z0, x0 + 1, y + b, z0 + 1));
            bool e = front != 0, w = front != 2;
            if (e) list.Add(ModularPart.B(PartMat.Brick, x0 + 1 - t, y + b, z0, x0 + 1, y + k, z0 + 1));
            if (w) list.Add(ModularPart.B(PartMat.Brick, x0, y + b, z0, x0 + t, y + k, z0 + 1));
            double xa = x0 + (w ? t : 0), xb = x0 + 1 - (e ? t : 0);
            if (front != 1) list.Add(ModularPart.B(PartMat.Brick, xa, y + b, z0 + 1 - t, xb, y + k, z0 + 1));
            if (front != 3) list.Add(ModularPart.B(PartMat.Brick, xa, y + b, z0, xb, y + k, z0 + t));
            // the hearth's floor, sooty, a hair over the plinth's top inside the kerb
            list.Add(ModularPart.B(PartMat.Soot, xa + 0.05, y + b, z0 + (front != 3 ? t : 0) + 0.05, xb - 0.05, y + b + 0.01, z0 + 1 - (front != 1 ? t : 0) - 0.05));
            return list;
        }

        /// <summary>(0.3.18, note 165) A flue segment: a brick chute over its box; over a hearth a brick hood at its foot, the chute starting inside it.</summary>
        public static List<ModularPart> Flue(double[] box, bool hood)
        {
            var list = new List<ModularPart>();
            double cx = (box[0] + box[3]) / 2, cz = (box[2] + box[5]) / 2, hw = (box[3] - box[0]) / 2;
            double y0 = box[1];
            if (hood)
            {
                list.Add(ModularPart.B(PartMat.Brick, cx - 0.42, y0, cz - 0.42, cx + 0.42, y0 + 0.3, cz + 0.42));
                y0 += 0.05;
            }
            list.Add(ModularPart.B(PartMat.Brick, cx - hw, y0, cz - hw, cx + hw, box[4], cz + hw));
            // its mouth: a soot-dark square a hair over its top
            list.Add(ModularPart.B(PartMat.Soot, cx - hw + 0.06, box[4], cz - hw + 0.06, cx + hw - 0.06, box[4] + 0.01, cz + hw - 0.06));
            return list;
        }

        /// <summary>
        /// (0.3.20, step B; docs/slots.md §5) A hatch's leaf, shut (its box: x0 y0 z0 x1 y1 z1, its top the deck's), hinged
        /// along axis (0 x, 2 z) at its hinge side (hingeLow: the leaf's low side along the other axis). A cargo hatch's: a
        /// plank board, two iron straps from the hinge, a ring at the free edge. A glazed one's: a wooden frame (rails along
        /// the hinge, stiles across, a bar on each cell line), the ship's glass in it, lead cames over the glass two by two a
        /// cell. No two parts' faces in one plane.
        /// </summary>
        public static List<ModularPart> HatchLeaf(double[] b, int axis, bool hingeLow, bool glazed)
        {
            var list = new List<ModularPart>();
            bool alongX = axis == 0;
            double y0 = b[1], y1 = b[4];
            // along the hinge (a0..a1) and across it (c0 at the hinge .. c1 the free edge)
            double a0 = alongX ? b[0] : b[2], a1 = alongX ? b[3] : b[5];
            double lo = alongX ? b[2] : b[0], hi = alongX ? b[5] : b[3];
            double c0 = hingeLow ? lo : hi, c1 = hingeLow ? hi : lo, cs = Math.Sign(c1 - c0);
            ModularPart Box(PartMat m, double p0, double p1, double q0, double q1, double ya, double yb) =>
                alongX ? ModularPart.B(m, p0, ya, q0, p1, yb, q1) : ModularPart.B(m, q0, ya, p0, q1, yb, p1);
            if (!glazed)
            {
                list.Add(Box(PartMat.Planks, a0, a1, lo, hi, y0, y1));
                // two straps from the hinge, a quarter in from the ends, two thirds across; a ring at the free edge
                double len = Math.Abs(c1 - c0) * 0.66;
                foreach (double f in new[] { 0.22, 0.78 })
                {
                    double m = a0 + (a1 - a0) * f;
                    double s0 = c0 + cs * 0.02;
                    list.Add(Box(PartMat.Iron, m - 0.045, m + 0.045, Math.Min(s0, s0 + cs * len), Math.Max(s0, s0 + cs * len), y1 - 0.004, y1 + 0.012));
                }
                double am = (a0 + a1) / 2, ce = c1 - cs * 0.12;
                list.Add(Box(PartMat.Iron, am - 0.07, am + 0.07, Math.Min(ce, ce - cs * 0.05), Math.Max(ce, ce - cs * 0.05), y1 - 0.002, y1 + 0.018));
                return list;
            }
            const double w = 0.09;
            // rails along the hinge at both sides (full length), stiles across between them at both ends, a bar on each cell line
            list.Add(Box(PartMat.Wood, a0, a1, lo, lo + w, y0, y1));
            list.Add(Box(PartMat.Wood, a0, a1, hi - w, hi, y0, y1));
            list.Add(Box(PartMat.Wood, a0, a0 + w, lo + w, hi - w, y0, y1));
            list.Add(Box(PartMat.Wood, a1 - w, a1, lo + w, hi - w, y0, y1));
            int cells = Math.Max(1, (int)Math.Round(a1 - a0));
            for (int n = 1; n < cells; n++)
            {
                double m = a0 + n * (a1 - a0) / cells;
                list.Add(Box(PartMat.Wood, m - w / 2, m + w / 2, lo + w, hi - w, y0 + 0.01, y1 - 0.01));
            }
            // the glass, a pane at the middle of the leaf's thickness, its edges in the frame
            double gy = (y0 + y1) / 2;
            list.Add(Box(PartMat.Glass, a0 + w / 2, a1 - w / 2, lo + w / 2, hi - w / 2, gy - 0.01, gy + 0.01));
            // lead cames: two by two lights a cell, over the glass
            for (int n = 0; n < cells; n++)
            {
                double m = a0 + (n + 0.5) * (a1 - a0) / cells;
                list.Add(Box(PartMat.Lead, m - 0.012, m + 0.012, lo + w, hi - w, gy + 0.008, gy + 0.022));
            }
            double cm = (lo + hi) / 2;
            list.Add(Box(PartMat.Lead, a0 + w, a1 - w, cm - 0.012, cm + 0.012, gy + 0.024, gy + 0.034));
            return list;
        }

        /// <summary>
        /// (0.3.18, notes 164, 166) Faces of two parts lying in one plane, facing the same way and overlapping (they flicker):
        /// each pair once, with the overlap's area. Beams that slope are left out.
        /// </summary>
        public static List<(int A, int B, double Area)> Coplanar(IList<double[]> boxes, double eps = 1e-4, double minArea = 1e-5)
        {
            var found = new List<(int, int, double)>();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i];
                    var b = boxes[j];
                    if (a == null || b == null) continue;
                    double best = 0;
                    for (int ax = 0; ax < 3; ax++)
                    {
                        int u = (ax + 1) % 3, v = (ax + 2) % 3;
                        double ou = Math.Min(a[u + 3], b[u + 3]) - Math.Max(a[u], b[u]), ov = Math.Min(a[v + 3], b[v + 3]) - Math.Max(a[v], b[v]);
                        if (ou <= eps || ov <= eps) continue;
                        // both low faces, or both high faces, in one plane
                        if (Math.Abs(a[ax] - b[ax]) < eps || Math.Abs(a[ax + 3] - b[ax + 3]) < eps) best = Math.Max(best, ou * ov);
                    }
                    if (best > minArea) found.Add((i, j, best));
                }
            return found;
        }
    }

    /// <summary>(0.3.18, note 165) Whether a placed hearth's smoke gets out: open air over it, or up its flue to open air.</summary>
    public static class HearthFlue
    {
        /// <summary>How far over a placed hearth (or its flue's top) must be free of her to count as open air.</summary>
        public const double OpenAirClear = 6.0;

        /// <summary>
        /// (0.3.18) Where a placed hearth's smoke comes out (ship-local), and whether it gets out: open air straight over it,
        /// or up its flue (segment on segment, deck by deck) to a top with open air over it.
        /// </summary>
        public static bool FlueTop(MountLayout Mounts, ShipPieceSet Pieces, VoxelGrid g, ShipPiece hearth, out double[] top)
        {
            top = null;
            var m = Mounts?.Get(hearth.Mount);
            if (m == null) return false;
            double cx = m.X + 0.5, cz = m.Z + 0.5;
            if (OpenOver(g, cx, m.Y + PiecePlanner.KerbHeight + 0.05, cz))
            {
                top = new[] { cx, m.Y + 1.2, cz };
                return true;
            }
            var floor = m;
            for (int n = 0; n < 6; n++)
            {
                var fl = Mounts.FlueOver(floor);
                if (fl == null || Pieces.At(fl.Id) is not ShipPiece seg || seg.Role != PieceRole.Flue) return false;
                top = new[] { cx, fl.OverY + MountLayout.FlueOut + 0.1, cz };
                if (OpenOver(g, cx, fl.OverY + MountLayout.FlueOut, cz)) return true;
                floor = fl.OutPlace != null ? Mounts.Get(fl.OutPlace) : null;
                if (floor == null) return false;
            }
            return false;
        }

        /// <summary>Is the column over (x, y, z) (ship-local, a flue's width) free of her for <see cref="OpenAirClear"/> or to her top?</summary>
        public static bool OpenOver(VoxelGrid g, double x, double y, double z)
        {
            if (g == null) return true;
            double h = MountLayout.FlueWidth / 2 - 0.02, top = Math.Min(y + OpenAirClear, g.OY + g.NY * g.V);
            return top <= y || !g.Collides(x - h, y, z - h, x + h, top, z + h);
        }

    }
}
