using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.3.18, the user's point 1: "the place search is too aggressive: it picks places in mid-air, more likely than on
    /// the floor") Where a block in hand goes is found as vanilla finds it: next to what the crosshair hits, never among
    /// places the ray merely passes through. The hit (ship-local: where, and the face's normal) is her own woodwork or a
    /// piece put aboard; the places offered, in order:
    /// - on a floor (a face up: her deck, a piece's top): the floor place over it (a stacked one on a piece), the border
    ///   place on the cell's edge nearest the hit (a wall piece in hand), the horizontal place over a well's edge;
    /// - under a deck or a beam (a face down): a hook there, the top-row border place under it, a shelf's place;
    /// - on a wall (a face across): its wall spot, a panel's place, the border place there (the line nearest the point in
    ///   front of it), the floor place in front of it, a shelf's place at that height;
    /// - on a partition, a door, a window or a railing put up, with a wall piece in hand: the place past its nearest edge on
    ///   its line (along, over or under it: a second row goes on top of the first);
    /// - on a hearth or a flue, with a brick chimney in hand: the flue's next segment over it.
    /// Pure geometry: the client's crosshair and the offline tests.
    /// </summary>
    public static class AimPlace
    {
        /// <summary>How far off the face the point in front of it is taken (the cell or row it's in is the place).</summary>
        public const double Off = 0.05;

        /// <summary>
        /// The way from a mount point to the player (ship-local, snapped: 0 +x, 1 +z, 2 -x, 3 -z): a piece faces whoever puts
        /// it, a stair climbs away from them. (0.3.16, note 159) From where the crosshair meets the place (hit), not its middle.
        /// </summary>
        public static int Toward(MountPoint m, Vec3d eye, Vec3d hit = null)
        {
            double vx = eye.X - (hit?.X ?? m.CX), vz = eye.Z - (hit?.Z ?? m.CZ);
            if (Math.Abs(vx) < 1e-6 && Math.Abs(vz) < 1e-6) return 1;
            return MountLayout.Snap(vx, vz);
        }

        /// <summary>The face a ray enters a box by: its outward normal (axis-aligned), or null if it misses.</summary>
        public static double[] BoxNormal(Vec3d o, Vec3d d, double[] b)
        {
            double tmin = double.MinValue;
            int axis = -1;
            double[] oo = { o.X, o.Y, o.Z }, dd = { d.X, d.Y, d.Z };
            for (int a = 0; a < 3; a++)
            {
                if (Math.Abs(dd[a]) < 1e-12) continue;
                double t0 = (b[a] - oo[a]) / dd[a], t1 = (b[a + 3] - oo[a]) / dd[a];
                double tn = Math.Min(t0, t1);
                if (tn > tmin) { tmin = tn; axis = a; }
            }
            if (axis < 0) return null;
            var n = new double[3];
            n[axis] = dd[axis] > 0 ? -1 : 1;
            return n;
        }

        /// <summary>Where a ray meets her collision, and the face's normal there (axis-aligned), or false within range.</summary>
        public static bool GridHit(VoxelGrid g, Vec3d o, Vec3d d, double range, out double t, out double[] normal)
        {
            normal = null;
            t = g.RaycastCell(o, d, range, out _, out _, out _, out int axis);
            if (t > range || axis < 0) return false;
            normal = new double[3];
            double comp = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            normal[axis] = comp > 0 ? -1 : 1;
            return true;
        }

        /// <summary>
        /// The places next to a hit, in the order they're offered (the first a block in hand can go on wins): at p (ship-local)
        /// on a face of normal n, a piece put aboard (or null: her own woodwork). ok: what the block in hand may go on (its
        /// kind, free, a piece under it). Nothing but what's next to the hit.
        /// </summary>
        /// <summary>
        /// (0.3.24, note 178: "pointing at a block border in building mode doesn't find the partition place, only the floor")
        /// With a wall piece in hand, a hit on a floor this near a cell's edge offers the border place on that edge before
        /// the floor place (the middle of the cell still offers the floor place first).
        /// </summary>
        public const double EdgeBand = 0.3;

        public static List<MountPoint> Candidates(MountLayout l, ShipPieceSet pieces, Vec3d p, double[] n, ShipPiece piece, Func<MountPoint, bool> ok, bool wallInHand = false)
        {
            var list = new List<MountPoint>();
            if (l == null || p == null || n == null) return list;
            void Add(MountPoint m) { if (m != null && !list.Contains(m) && (ok == null || ok(m))) list.Add(m); }
            double qx = p.X + n[0] * Off, qy = p.Y + n[1] * Off, qz = p.Z + n[2] * Off;
            bool up = n[1] > 0.5, down = n[1] < -0.5;
            var pm = piece != null && piece.Mounts.Length > 0 ? l.Get(piece.Mounts[0]) : null;

            // on a border piece: the place past its nearest edge, along its line or over or under it
            if (piece != null && piece.OnBorder && pm?.Kind == MountKind.Border)
            {
                var (_, ax) = MountLayout.BorderLine(pm);
                foreach (var m in Beside(l, pm, piece.Role == PieceRole.Door ? 2 : 1, ax ? p.X : p.Z, p.Y)) Add(m);
            }
            // on a hearth or a flue: the flue's next segment over it
            if (piece != null && pm != null && piece.Role == PieceRole.Hearth) Add(l.FlueOver(pm));
            if (piece != null && pm?.Kind == MountKind.Flue && pm.OutPlace != null) Add(l.FlueOver(l.Get(pm.OutPlace)));
            // ((0.3.28, note 230) on a hood's roof board: the next one past its nearest edge)
            if (piece != null && pm?.Kind == MountKind.Horizontal && MountLayout.IsHood(pm.Id))
                foreach (var m in HoodsBeside(l, pm, p.X, p.Z)) Add(m);

            if (up)
            {
                // (0.3.24, note 178) a wall piece aimed near a floor cell's edge: the border place there first
                if (wallInHand && FloorAt(l, qx, qy, qz) is MountPoint fc &&
                    Math.Min(Math.Min(qx - fc.X, fc.X + 1 - qx), Math.Min(qz - fc.Z, fc.Z + 1 - qz)) < EdgeBand)
                    foreach (var m in Borders(l, qx, qy, qz, pm)) Add(m);
                Add(FloorAt(l, qx, qy, qz));
                foreach (var m in Borders(l, qx, qy, qz, pm)) Add(m);
                foreach (var m in Shelves(l, qx, qy, qz, p.Y, true)) Add(m);
                Add(FlueAt(l, qx, qy, qz));
            }
            else if (down)
            {
                foreach (var m in HooksAt(l, qx, qy, qz)) Add(m);
                foreach (var m in Borders(l, qx, qy, qz, pm)) Add(m);
                foreach (var m in Shelves(l, qx, qy, qz, p.Y, false)) Add(m);
                Add(FlueAt(l, qx, qy, qz));
                Add(FloorAt(l, qx, qy, qz));
            }
            else
            {
                foreach (var m in WallsAt(l, p, n)) Add(m);
                foreach (var m in PanelsAt(l, qx, qy, qz)) Add(m);
                foreach (var m in Borders(l, qx, qy, qz, pm)) Add(m);
                Add(FloorAt(l, qx, qy, qz));
                foreach (var m in Shelves(l, qx, qy, qz, p.Y, false)) Add(m);
                Add(FlueAt(l, qx, qy, qz));
                // ((0.3.28, note 230) on a wall's upper half: a hood's roof on its top, the side more walled in first)
                foreach (var m in Hoods(l, pieces, qx, qy, qz, piece != null && piece.OnBorder ? pm : null)) Add(m);
            }
            return list;
        }

        /// <summary>
        /// A border place's neighbours past its edges (rows high), nearest edge first from the hit (along it, y): along its
        /// line either way, over it, under it.
        /// </summary>
        static IEnumerable<MountPoint> Beside(MountLayout l, MountPoint mp, int rows, double along, double y)
        {
            var (_, alongX) = MountLayout.BorderLine(mp);
            double u = along - ((alongX ? mp.X : mp.Z) - 0.5), v = y - mp.Y;
            var edges = new List<(double D, int Da, int Dy)> { (u, -1, 0), (1 - u, 1, 0), (v, 0, -1), (rows - v, 0, rows) };
            foreach (var (_, da, dy) in edges.OrderBy(e => e.D))
                if (l.BorderNext(mp, da, dy) is MountPoint m)
                {
                    yield return m;
                    // ((0.3.28, note 230) its twin across the line: round a well, the one anchored on the deck beside it)
                    if (l.TwinOf(m) is MountPoint t) yield return t;
                }
        }

        /// <summary>The floor place (on a deck, or stacked on a piece) whose block holds the point.</summary>
        static MountPoint FloorAt(MountLayout l, double x, double y, double z)
        {
            foreach (var m in l.All)
                if (m.Kind == MountKind.Floor && x >= m.X - 1e-6 && x <= m.X + 1 + 1e-6 && z >= m.Z - 1e-6 && z <= m.Z + 1 + 1e-6 && y >= m.Y - 1e-6 && y < m.Y + 1)
                    return m;
            return null;
        }

        /// <summary>The border places whose block (along its segment, up its row) holds the point, nearest line first, within half a block of their line.</summary>
        static IEnumerable<MountPoint> Borders(MountLayout l, double x, double y, double z, MountPoint skip)
        {
            var found = new List<(MountPoint M, double D)>();
            foreach (var m in l.Borders)
            {
                if (m == skip || y < m.Y - 1e-6 || y > m.Y + 1 + 1e-6) continue;
                var (line, alongX) = MountLayout.BorderLine(m);
                double a = alongX ? x : z, c = alongX ? z : x, mid = alongX ? m.X : m.Z;
                if (Math.Abs(a - mid) > 0.5 + 1e-6) continue;
                double dl = Math.Abs(c - line);
                if (dl > 0.55) continue;
                found.Add((m, dl));
            }
            return found.OrderBy(f => f.D).Select(f => f.M);
        }

        /// <summary>
        /// The horizontal places (shelves, a well's top) in the column over the point, their plane within 0.6 of it; on a
        /// floor near a cell's edge (onFloor), also the one flush with it over the next cell (closing a well from beside it).
        /// </summary>
        static IEnumerable<MountPoint> Shelves(MountLayout l, double x, double y, double z, double hitY, bool onFloor)
        {
            var found = new List<(MountPoint M, double D)>();
            foreach (var m in l.Horizontals)
            {
                double dx = Math.Abs(x - m.X), dz = Math.Abs(z - m.Z);
                if (dx <= 0.5 + 1e-6 && dz <= 0.5 + 1e-6 && Math.Abs(m.Y - y) <= 0.6) found.Add((m, Math.Abs(m.Y - y)));
                else if (onFloor && Math.Abs(m.Y - hitY) < 0.1 && (dx <= 0.85 && dz <= 0.5 || dz <= 0.85 && dx <= 0.5)) found.Add((m, 0.6 + Math.Max(dx, dz)));
            }
            return found.OrderBy(f => f.D).Select(f => f.M);
        }

        /// <summary>
        /// (0.3.28, note 230) The hoods' roof places for a hit on a wall's face at q: the one over the cell q is in, its
        /// underside a little over q up to 1.2 over it; on a wall piece (wall: its place) also the one over the cell across it,
        /// the cell with more wall pieces round it (row 1, the hood's inside) first.
        /// </summary>
        static IEnumerable<MountPoint> Hoods(MountLayout l, ShipPieceSet pieces, double x, double y, double z, MountPoint wall)
        {
            var found = new List<(MountPoint M, int Walls)>();
            void Try(double px, double pz)
            {
                foreach (var m in l.Horizontals)
                {
                    if (!MountLayout.IsHood(m.Id) || Math.Abs(px - m.X) > 0.5 + 1e-6 || Math.Abs(pz - m.Z) > 0.5 + 1e-6) continue;
                    double up = m.Box[1] - y;
                    if (up < -0.2 || up > 1.2 || found.Any(f => f.M == m)) continue;
                    found.Add((m, WallsRound(l, pieces, m)));
                }
            }
            Try(x, z);
            if (wall != null)
            {
                var (line, alongX) = MountLayout.BorderLine(wall);
                double c = alongX ? z : x, across = 2 * line - c;
                if (alongX) Try(x, across + Math.Sign(across - line) * 0.3); else Try(across + Math.Sign(across - line) * 0.3, z);
            }
            return found.OrderByDescending(f => f.Walls).Select(f => f.M);
        }

        /// <summary>(0.3.28) How many of a cell's four sides (a hood's) have a wall piece put up on them under its roof (row 1).</summary>
        static int WallsRound(MountLayout l, ShipPieceSet pieces, MountPoint hood)
        {
            if (pieces == null) return 0;
            int n = 0;
            double x0 = hood.Box[0], z0 = hood.Box[2], y = hood.Box[1] - 1;
            foreach (var (alongX, line, along) in new[] { (true, z0, x0 + 0.5), (true, z0 + 1, x0 + 0.5), (false, x0, z0 + 0.5), (false, x0 + 1, z0 + 0.5) })
            {
                var b = l.BorderAt(alongX, line, along, y);
                if (b == null) continue;
                if (pieces.Taken(b.Id) || l.TwinOf(b) is MountPoint t && pieces.Taken(t.Id)) n++;
            }
            return n;
        }

        /// <summary>(0.3.28) The hoods' roof places beside one (on the four cells round its cell), the nearest edge to (x, z) first.</summary>
        static IEnumerable<MountPoint> HoodsBeside(MountLayout l, MountPoint hood, double x, double z)
        {
            double u = x - hood.Box[0], v = z - hood.Box[2];
            var edges = new List<(double D, int Di, int Dk)> { (u, -1, 0), (1 - u, 1, 0), (v, 0, -1), (1 - v, 0, 1) };
            foreach (var (_, di, dk) in edges.OrderBy(e => e.D))
                if (l.HoodAt(hood.Deck, hood.I + di, hood.K + dk) is MountPoint m) yield return m;
        }

        /// <summary>The flue place whose chute holds the point (a little round it).</summary>
        static MountPoint FlueAt(MountLayout l, double x, double y, double z)
        {
            foreach (var m in l.Flues)
            {
                var b = m.Box;
                if (x >= b[0] - 0.3 && x <= b[3] + 0.3 && z >= b[2] - 0.3 && z <= b[5] + 0.3 && y >= b[1] - 0.3 && y <= b[4] + 0.3) return m;
            }
            return null;
        }

        /// <summary>Hooks whose box (a little round it) holds the point under a ceiling, nearest first.</summary>
        static IEnumerable<MountPoint> HooksAt(MountLayout l, double x, double y, double z)
        {
            return l.Hooks.Where(m => m.Box != null && x >= m.Box[0] - 0.25 && x <= m.Box[3] + 0.25 && z >= m.Box[2] - 0.25 && z <= m.Box[5] + 0.25 && y >= m.Box[1] - 0.1 && y <= m.Box[4] + 0.1)
                .OrderBy(m => (m.X - x) * (m.X - x) + (m.Z - z) * (m.Z - z));
        }

        /// <summary>Wall spots on the face hit: facing the way its normal does, their face within 0.35 of the hit, the hit within their block.</summary>
        static IEnumerable<MountPoint> WallsAt(MountLayout l, Vec3d p, double[] n)
        {
            int dir = MountLayout.Snap(n[0], n[2]);
            var found = new List<(MountPoint M, double D)>();
            foreach (var m in l.Walls)
            {
                if (m.In != dir) continue;
                bool xFace = dir == 0 || dir == 2;
                double face = xFace ? m.X : m.Z, at = xFace ? p.X : p.Z, along = xFace ? p.Z : p.X, c = xFace ? m.Z : m.X;
                if (Math.Abs(face - at) > 0.35 || p.Y < m.Y - 1e-6 || p.Y > m.Y + 1 + 1e-6 || Math.Abs(along - c) > 0.5 + 1e-6) continue;
                found.Add((m, Math.Abs(face - at)));
            }
            return found.OrderBy(f => f.D).Select(f => f.M);
        }

        /// <summary>Panel places (the wedge by her side) whose box holds the point (a little round it).</summary>
        static IEnumerable<MountPoint> PanelsAt(MountLayout l, double x, double y, double z) =>
            l.Panels.Where(m => m.Box != null && x >= m.Box[0] - 0.1 && x <= m.Box[3] + 0.1 && y >= m.Box[1] - 0.01 && y <= m.Box[4] + 0.01 && z >= m.Box[2] - 0.1 && z <= m.Box[5] + 0.1);
    }
}
