using System;

namespace Hansa
{
    /// <summary>
    /// (25 October 2026, the user's decision: the caravel's quarterdeck loses its carriage guns and gets swivel guns on its rail)
    /// A swivel gun, the Portuguese berço: a small bronze breech-loader on an iron yoke, the yoke's pin in a socket on the rail
    /// cap, laid by the man standing at it over a wide arc and fired as her carriage guns are (<see cref="GunLaying"/>,
    /// <see cref="EntityShip.FireGun"/>), lighter: a small ball, less of a blow, quickly fired again. Its design gives where its
    /// socket's top is (Pos), the way it points laid square (Out, level and outboard) and its arc; the rest is its own:
    /// <list type="bullet">
    /// <item>it trains about the socket's upright (Pivot = Pos) and elevates about the yoke's pins (Trunnion, over the socket);</item>
    /// <item>its muzzle is <see cref="MuzzleReach"/> out from the pins, its breech handle <see cref="TailReach"/> in;</item>
    /// <item>its selection box is round its barrel laid square; it isn't solid (it's over the rail, out of the way);</item>
    /// <item>the gunner stands on the deck inboard of it (Seat), unless the design says where.</item>
    /// </list>
    /// No port, lid, carriage or gun place: it's always mounted, always out. Its chamber takes powder and one lead shot;
    /// it can fire again after loading and <see cref="ReloadSeconds"/>.
    /// </summary>
    public static class SwivelGun
    {
        public const string Kind = "swivel";
        /// <summary>The yoke's pins over the socket's top (blocks).</summary>
        public const double PinHeight = 0.62;
        /// <summary>From the pins to the muzzle, and back to the end of the breech's handle (blocks).</summary>
        public const double MuzzleReach = 1.05, TailReach = 0.7621;
        /// <summary>A berço's bore (a 4.5 cm ball), its ball (kg: a 2-pounder's lead and iron), how soon it fires again (s), the radius it strikes with.</summary>
        public const float BoreBlocks = 0.045f, ShotKg = 0.45f, ReloadSeconds = 4, StrikeRadius = 0.05f;
        /// <summary>Its arc when the design gives none (degrees from square, + toward her bow; up and down), and how fast one man lays it.</summary>
        public static readonly float[] Train = { -100, 100 }, Elevate = { -25, 40 };
        public const float TrainRate = 90, ElevationRate = 60;
        /// <summary>How far inboard of the socket the gunner stands (blocks), unless the design says.</summary>
        public const double StandIn = 0.85;

        /// <summary>
        /// How far out and in from its socket the barrel's and the tail's middle line must stay clear over the rail when its
        /// place doesn't say (<see cref="FixtureDef.Rail"/>: outboard the cap and her side flaring out under it; inboard the
        /// cap only: the caravel's first quarterdeck rail, 0.15 to 0.25 wide, her side 0.1 more out under it), and by how
        /// much (a barrel's radius and a little).
        /// </summary>
        public const double RailOut = 0.2, RailIn = 0.12, RailClear = 0.10;

        /// <summary>
        /// Can it be laid so without its barrel or its tail going into the rail cap under it? Trained along the rail, the
        /// barrel lies over the cap: depressed (or the tail, elevated) it would go into it; trained out, the muzzle is past
        /// the cap and it can be depressed down the ship's side. Each point of the barrel's line, out to the muzzle and back to
        /// the tail's end, that's over the rail (within its place's <see cref="FixtureDef.Rail"/> of the socket: by default
        /// <see cref="RailOut"/> outboard, <see cref="RailIn"/> inboard) stays <see cref="RailClear"/> over its top.
        /// </summary>
        public static bool ClearOfRail(FixtureDef f, GunLay lay)
        {
            double railIn = f?.Rail?.Length >= 2 ? f.Rail[0] : RailIn, railOut = f?.Rail?.Length >= 2 ? f.Rail[1] : RailOut;
            double t = lay.Train * Math.PI / 180, e = lay.Elevation * Math.PI / 180;
            // how far across the rail a point r along the barrel is, and how far up from the pins
            double across = Math.Abs(Math.Cos(e) * Math.Cos(t)), up = Math.Sin(e);
            foreach (double r in new[] { MuzzleReach, -TailReach })
            {
                // the farthest point of that half of the barrel still over the cap
                double half = r*Math.Cos(t) > 0 ? railOut : railIn;
                double over = across < 1e-6 ? Math.Abs(r) : Math.Min(Math.Abs(r), half / across);
                if (PinHeight + Math.Sign(r) * over * up < RailClear) return false;
            }
            return true;
        }

        /// <summary>
        /// The nearest elevation it can be laid to at this train (within e0..e1) with barrel and tail clear of the rail
        /// (<see cref="ClearOfRail"/>): back toward level, which always is.
        /// </summary>
        public static float ClampElevation(FixtureDef f, float train, float elevation, float e0, float e1)
        {
            float e = Math.Clamp(elevation, e0, e1);
            for (int i = 0; i < 400 && !ClearOfRail(f, new GunLay(train, e)); i++) e += e > 0 ? -0.25f : 0.25f;
            return e;
        }

        /// <summary>A swivel gun's own: everything but its socket, its way and its arc (what the design gave is kept).</summary>
        public static void Fill(FixtureDef f, VoxelGrid grid)
        {
            if (f?.Pos == null || f.Pos.Length < 3) return;
            double ox = f.Out?.Length >= 3 ? f.Out[0] : 0, oz = f.Out?.Length >= 3 ? f.Out[2] : 1;
            double ol = Math.Sqrt(ox * ox + oz * oz);
            if (ol < 1e-9) { ox = 0; oz = 1; ol = 1; }
            ox /= ol;
            oz /= ol;
            f.Out = new[] { ox, 0, oz };
            double x = f.Pos[0], y = f.Pos[1], z = f.Pos[2];
            f.Pivot ??= new[] { x, y, z };
            f.Trunnion ??= new[] { x, y + PinHeight, z };
            f.Muzzle ??= new[] { f.Trunnion[0] + ox * MuzzleReach, f.Trunnion[1], f.Trunnion[2] + oz * MuzzleReach };
            // (the class defaults are a carriage gun's: those mean the design gave none)
            if (f.TrainLimits == null || f.TrainLimits.Length < 2 || f.TrainLimits[0] == -12 && f.TrainLimits[1] == 12 || f.TrainLimits[0] == -80 && f.TrainLimits[1] == 80) f.TrainLimits = (float[])Train.Clone();
            if (f.ElevationLimits == null || f.ElevationLimits.Length < 2 || f.ElevationLimits[0] == -5 && f.ElevationLimits[1] == 15 || f.ElevationLimits[0] == -25 && f.ElevationLimits[1] == 30) f.ElevationLimits = (float[])Elevate.Clone();
            if (f.TrainRate == 10) f.TrainRate = TrainRate;
            if (f.ElevationRate == 6) f.ElevationRate = ElevationRate;
            if (f.Bore == 0.14f) f.Bore = BoreBlocks;
            if (f.Shot <= 0) f.Shot = ShotKg;
            if (f.Reload <= 0) f.Reload = ReloadSeconds;
            if (f.HitRadius <= 0) f.HitRadius = StrikeRadius;
            f.Material ??= "bronze";
            // round its barrel laid square, from the handle's end to the muzzle, and the yoke and socket under it
            double hx = Math.Abs(oz) * 0.2, hz = Math.Abs(ox) * 0.2;
            double ax = x - ox * TailReach, az = z - oz * TailReach, bx = x + ox * (MuzzleReach + 0.05), bz = z + oz * (MuzzleReach + 0.05);
            f.Sel ??= new[] { Math.Min(ax, bx) - hx, y - 0.1, Math.Min(az, bz) - hz, Math.Max(ax, bx) + hx, y + PinHeight + 0.18, Math.Max(az, bz) + hz };
            f.Box = null;
            // the gunner on the deck inboard of it: the floor under that spot
            if (f.Seat == null)
            {
                double sx = x - ox * StandIn, sz = z - oz * StandIn;
                f.Seat = new[] { sx, FloorUnder(grid, sx, y - 0.3, sz) ?? y - 1, sz };
            }
        }

        /// <summary>The top of the first solid cell at or below y (ship-local), or null.</summary>
        public static double? FloorUnder(VoxelGrid g, double x, double y, double z)
        {
            if (g == null) return null;
            int i = g.CellX(x), k = g.CellZ(z);
            for (int j = g.CellY(y); j >= 0; j--)
                if (g.Solid(i, j, k)) return g.OY + (j + 1) * g.V;
            return null;
        }

        /// <summary>
        /// Its parts' matrices as it's laid (ship-local, column-major like Mat4f; the shape's own frame: the socket's top at
        /// the origin, the barrel along +z laid square, the pins' line along x at <see cref="PinHeight"/>): the yoke turned
        /// with its train about the socket's upright, the barrel with that and its elevation about the pins. As
        /// <see cref="GunLaying.Place"/> moves its points, so the drawn barrel's muzzle is where the shot leaves.
        /// </summary>
        public static void PartMatrices(FixtureDef f, GunLay lay, float[] socket, float[] yoke, float[] barrel)
        {
            GunLaying.OutOf(f, out double ox, out double oz);
            double x = f.Pos[0], y = f.Pos[1], z = f.Pos[2];
            // the shape's +z onto the way it points laid square (a turn about +y), at the socket
            double face = Math.Atan2(ox, oz);
            Base(socket, x, y, z, face);
            double train = lay.Train * Math.PI / 180 * GunLaying.TrainSign(f);
            Base(yoke, x, y, z, face + train);
            // elevation: about the pins (x in the shape's frame), muzzle up for +
            Base(barrel, x, y, z, face + train);
            double e = -lay.Elevation * Math.PI / 180;
            double c = Math.Cos(e), s = Math.Sin(e);
            // barrel = barrel · T(0, pin, 0) · Rx(e) · T(0, -pin, 0)
            var r = new float[16];
            r[0] = 1; r[5] = (float)c; r[6] = (float)s; r[9] = (float)-s; r[10] = (float)c; r[15] = 1;
            r[13] = (float)(PinHeight - (c * PinHeight)); r[14] = (float)(-s * PinHeight);
            Mul(barrel, (float[])barrel.Clone(), r);
        }

        /// <summary>A turn by a about +y, then moved to (x, y, z).</summary>
        static void Base(float[] m, double x, double y, double z, double a)
        {
            Array.Clear(m, 0, 16);
            double c = Math.Cos(a), s = Math.Sin(a);
            m[0] = (float)c; m[2] = (float)-s; m[5] = 1; m[8] = (float)s; m[10] = (float)c; m[15] = 1;
            m[12] = (float)x; m[13] = (float)y; m[14] = (float)z;
        }

        /// <summary>out = a · b (column-major 4x4).</summary>
        static void Mul(float[] o, float[] a, float[] b)
        {
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    float v = 0;
                    for (int k = 0; k < 4; k++) v += a[k * 4 + row] * b[col * 4 + k];
                    o[col * 4 + row] = v;
                }
        }

        /// <summary>A point of the shape's frame through a matrix (ship-local).</summary>
        public static double[] Apply(float[] m, double x, double y, double z) => new double[]
        {
            m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]
        };
    }
}
