using System;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (review H1-2 to H4-6) The server's checks of a deck position a client sends (MagelanMod.DeckMovePossible), as plain
    /// geometry in her collision grid so the offline tests walk her stairs through them. Positions are ship-local feet: the
    /// walker's own collision height (<see cref="DeckMover.Y"/>), not the height its camera glides at.
    /// </summary>
    public static class DeckMoveCheck
    {
        /// <summary>
        /// (review H3-2, H4-1) Is a body standing here in her collision? Its box a little smaller all round (0.02 at the sides,
        /// 0.05 at feet and head) for a packet's float rounding of a walker pressed against a wall or standing on a tread.
        /// </summary>
        public static bool BodyIn(VoxelGrid g, double x, double y, double z)
        {
            const double w = DeckMover.HalfWidth - 0.02;
            return g.Collides(x - w, y + 0.05, z - w, x + w, y + DeckMover.Height - 0.05, z + w);
        }

        /// <summary>
        /// (review H2-1, H2-7, H4-3) Nothing of hers between two deck places, waist high: straight, or round one corner (a long
        /// step at a low frame rate). escape: the server's own change to her (a piece put, a door shut) has them in her: a short
        /// way out to a free place is let go (a client can't make that happen: every place it's let move to is free).
        /// </summary>
        public static bool PathFree(VoxelGrid g, Vec3d from, double x, double y, double z, bool escape)
        {
            if (escape && BodyIn(g, from.X, from.Y, from.Z))
                return (x - from.X) * (x - from.X) + (z - from.Z) * (z - from.Z) <= 1 && Math.Abs(y - from.Y) <= 1;
            if (Clear(g, from.X, from.Y, from.Z, x, y, z)) return true;
            return Clear(g, from.X, from.Y, from.Z, x, from.Y, from.Z) && Clear(g, x, from.Y, from.Z, x, y, z) ||
                   Clear(g, from.X, from.Y, from.Z, from.X, from.Y, z) && Clear(g, from.X, from.Y, z, x, y, z);
        }

        /// <summary>A straight line between two feet, waist high (feet + 1), meets nothing of hers.</summary>
        public static bool Clear(VoxelGrid g, double ax, double ay, double az, double bx, double by, double bz)
        {
            double dx = bx - ax, dy = by - ay, dz = bz - az, len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-4) return true;
            return g.Raycast(new Vec3d(ax, ay + 1, az), new Vec3d(dx / len, dy / len, dz / len), len) >= len;
        }

        /// <summary>
        /// (review H3-10, H4-6) May they rise dy with this much level run? Their up budget, or on her short ship's stairs (steep:
        /// a deck's 3 blocks over 1 of run) three times the run and a step: the rise there goes with the run along it, not
        /// straight up off it.
        /// </summary>
        public static bool RiseAllowed(double dy, double level, double upBudget, bool steep) =>
            dy <= upBudget || steep && dy <= 3 * level + DeckMover.ShipStairStep + 0.05;

        /// <summary>Does a body standing here overlap one of these boxes (her short ship's stairs' flights)?</summary>
        public static bool InAny(double[][] boxes, double x, double y, double z)
        {
            if (boxes == null) return false;
            foreach (var b in boxes)
                if (b != null && x + DeckMover.HalfWidth > b[0] && x - DeckMover.HalfWidth < b[3] && y + DeckMover.Height > b[1] && y < b[4] &&
                    z + DeckMover.HalfWidth > b[2] && z - DeckMover.HalfWidth < b[5]) return true;
            return false;
        }

        /// <summary>
        /// (review H3-1) On a ladder's way: within 0.6 level of the line from its foot to its top, or of where one steps off at
        /// its foot (or its top: exitTop), and 0.6 up or down.
        /// </summary>
        public static bool OnLadderWay(LadderDef l, double x, double y, double z, bool exitTop)
        {
            if (exitTop && l.ExitTop != null && NearPoint(l.ExitTop[0], l.ExitTop[1], l.ExitTop[2], x, y, z) ||
                l.ExitBottom != null && NearPoint(l.ExitBottom[0], l.ExitBottom[1], l.ExitBottom[2], x, y, z)) return true;
            double ax = l.Top[0] - l.Bottom[0], ay = l.Top[1] - l.Bottom[1], az = l.Top[2] - l.Bottom[2];
            double len2 = ax * ax + ay * ay + az * az;
            double t = len2 < 1e-9 ? 0 : Math.Clamp(((x - l.Bottom[0]) * ax + (y - l.Bottom[1]) * ay + (z - l.Bottom[2]) * az) / len2, 0, 1);
            return NearPoint(l.Bottom[0] + ax * t, l.Bottom[1] + ay * t, l.Bottom[2] + az * t, x, y, z);
        }

        static bool NearPoint(double px, double py, double pz, double x, double y, double z) =>
            (px - x) * (px - x) + (pz - z) * (pz - z) <= 0.6 * 0.6 && Math.Abs(py - y) <= 0.6;
    }
}
