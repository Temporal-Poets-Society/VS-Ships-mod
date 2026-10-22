using System;
using Vintagestory.API.Client;

namespace Hansa
{
    /// <summary>
    /// Ship-local frame. Yaw follows the vanilla convention: an entity with yaw y faces (sin y, 0, cos y).
    /// Local +X is the bow, +Z is starboard (right when facing the bow).
    /// The drawn tilt: roll about the keel (+ = starboard down), then pitch (+ = bow up), then the yaw.
    /// </summary>
    public static class ShipFrame
    {
        public static void ToWorld(double yaw, double pitch, double roll, double lx, double ly, double lz, out double wx, out double wy, out double wz)
        {
            if (pitch == 0 && roll == 0) { ToWorld(yaw, lx, ly, lz, out wx, out wy, out wz); return; }
            double cr = Math.Cos(roll), sr = Math.Sin(roll), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            double y1 = ly * cr - lz * sr, z1 = ly * sr + lz * cr;
            double x2 = lx * cp - y1 * sp, y2 = lx * sp + y1 * cp;
            ToWorld(yaw, x2, y2, z1, out wx, out wy, out wz);
        }

        public static void ToLocal(double yaw, double pitch, double roll, double dx, double dy, double dz, out double lx, out double ly, out double lz)
        {
            ToLocal(yaw, dx, dy, dz, out double x2, out double y2, out double z1);
            if (pitch == 0 && roll == 0) { lx = x2; ly = y2; lz = z1; return; }
            double cr = Math.Cos(roll), sr = Math.Sin(roll), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            lx = x2 * cp + y2 * sp;
            double y1 = -x2 * sp + y2 * cp;
            ly = y1 * cr + z1 * sr;
            lz = -y1 * sr + z1 * cr;
        }

        /// <summary>
        /// m = the tilt as a turn in world axes (about the origin): what a passenger's body is turned by, so it leans
        /// with the deck. The same as going into the level ship frame, tilting, and coming back.
        /// </summary>
        public static Matrixf TiltMatrix(Matrixf m, double yaw, double pitch, double roll)
        {
            m.Identity();
            if (pitch == 0 && roll == 0) return m;
            float my = ModelYaw(yaw);
            return m.RotateY(my).RotateZ((float)pitch).RotateX((float)roll).RotateY(-my);
        }

        /// <summary>
        /// The tilted waterline (ship-local height h) as the water shader wants it: its world height at the origin, and
        /// how much it rises per block toward the bow and toward starboard, measured level from the origin.
        /// </summary>
        public static void WaterlinePlane(double pitch, double roll, double h, out double y0, out double slopeAlong, out double slopeAcross)
        {
            double cp = Math.Cos(pitch), cr = Math.Cos(roll);
            y0 = h / (cp * cr);
            slopeAlong = Math.Tan(pitch);
            slopeAcross = -Math.Tan(roll) / cp;
        }

        public static void ToWorld(double yaw, double lx, double ly, double lz, out double wx, out double wy, out double wz)
        {
            double s = Math.Sin(yaw), c = Math.Cos(yaw);
            wx = lx * s - lz * c;
            wy = ly;
            wz = lx * c + lz * s;
        }

        public static void ToLocal(double yaw, double dx, double dy, double dz, out double lx, out double ly, out double lz)
        {
            double s = Math.Sin(yaw), c = Math.Cos(yaw);
            lx = dx * s + dz * c;
            ly = dy;
            lz = -dx * c + dz * s;
        }

        /// <summary>Rotation for Mat4f.RotateY that maps ship-local axes to world axes.</summary>
        public static float ModelYaw(double yaw) => (float)(yaw - Math.PI / 2);
    }

    /// <summary>
    /// Walking on a ship: an axis-aligned player box moved against the ship's voxels in ship-local space,
    /// with gravity, jumping, step-up and sneak edge protection. Speeds are in blocks per second.
    /// </summary>
    public class DeckMover
    {
        public const double HalfWidth = 0.3;
        public const double Height = 1.85;
        public const double Gravity = 22.2;      // vanilla: 0.37 blocks/tick per second at 60 ticks/s
        public const double JumpSpeed = 8.2;     // vanilla BaseJumpForce
        public const double TerminalFall = 40;
        public const double StepHeight = 0.6;
        /// <summary>
        /// (0.1.0-dev.6, note 36: "stairs taking 1 block - NOT LADDERS") A ship's stair's step: on a short ship's stair (a deck's
        /// 3 blocks up over 1 block of run, her collision's columns a quarter block long) each step is 0.75, over a man's own
        /// <see cref="StepHeight"/>. Taken up and down over <see cref="SteepFlights"/> only.
        /// </summary>
        public const double ShipStairStep = 0.75;

        public double X, Y, Z;
        public double VX, VY, VZ;
        public bool OnGround;
        public bool Moving;
        /// <summary>
        /// Visual height offset: steps up and down are taken at once by the collision, but the seat (and so the
        /// camera) glides over them. Seen height = Y + SmoothOffset.
        /// </summary>
        public double SmoothOffset;
        public double SmoothY => Y + SmoothOffset;
        const double MinSmoothRate = 2.5;   // blocks per second

        /// <summary>Ratlines being climbed (null when walking), and how far up them (0 = bottom, 1 = top).</summary>
        public LadderDef Climbing;
        public double ClimbT;
        public const double ClimbSpeed = 2.2;
        LadderDef rearm;     // the ratlines just left: not grabbed again until you let go of the keys or walk away
        bool descendLatch;

        double jumpCooldown;
        static readonly double[] StepTries = { 0.25, 0.5, StepHeight };
        static readonly double[] SteepStepTries = { 0.25, 0.5, StepHeight, ShipStairStep };

        /// <summary>(0.1.0-dev.6, note 36) Is the walker over one of her short ship's stairs (<see cref="SteepFlights"/>)?</summary>
        bool OnSteepFlight()
        {
            if (SteepFlights == null) return false;
            foreach (var b in SteepFlights)
                if (b != null && X + HalfWidth > b[0] && X - HalfWidth < b[3] && Y + Height > b[1] && Y < b[4] && Z + HalfWidth > b[2] && Z - HalfWidth < b[5]) return true;
            return false;
        }

        /// <summary>Ok; Overboard (walked or jumped off the ship); LeftShip (climbed down a rope ladder into the water).</summary>
        public enum Result { Ok, Overboard, LeftShip }

        /// <summary>Ladders whose top can't be passed right now (their entry port gate is shut).</summary>
        public Func<LadderDef, bool> TopClosed;
        /// <summary>(0.3.12) The hatch a ladder passes through is shut (<see cref="LadderDef.Through"/>).</summary>
        public Func<LadderDef, bool> ThroughClosed;
        /// <summary>
        /// (0.3.23, note 169: "can climb through a closed hatch") The underside (ship-local y) of a shut hatch over a ladder's
        /// top, or NaN: climbing up, the climber's head stops under it.
        /// </summary>
        public Func<LadderDef, double> CoverUnder;
        /// <summary>
        /// (0.1.0-dev.6, note 36) Her short ship's stairs' flights (ship-local boxes, <see cref="PiecePlanner.SteepFlightBox"/>):
        /// a walker overlapping one takes a <see cref="ShipStairStep"/> up or down there. Null: none.
        /// </summary>
        public double[][] SteepFlights;

        public DeckMover(double x, double y, double z) { X = x; Y = y; Z = z; }

        /// <param name="wishX">desired horizontal direction (ship-local, unit length or zero)</param>
        /// <param name="climb">on ratlines: +1 up, -1 down, 0 hold</param>
        public Result Step(VoxelGrid g, double dt, double wishX, double wishZ, double speed, bool jump, bool sneak, double jumpMul = 1,
            LadderDef[] ladders = null, int climb = 0)
        {
            if (Climbing != null)
            {
                // the way you walk on it says up or down (note 77): toward the ladder as you'd step onto it from its foot
                // is up, away from it down, whichever way you face; jump and sneak still climb and descend
                if (!jump && !sneak && (wishX != 0 || wishZ != 0)) climb = ClimbFromWish(Climbing, wishX, wishZ, climb);
                return StepClimb(g, dt, climb, ladders);
            }
            if (rearm != null && (wishX == 0 && wishZ == 0 || FarFrom(rearm.ExitBottom) && FarFrom(rearm.ExitTop))) rearm = null;
            if (ladders != null && (wishX != 0 || wishZ != 0) && TryStartClimb(ladders, wishX, wishZ))
            {
                Glide(dt, 0);
                return Result.Ok;
            }
            double k = OnGround ? 14 : 2.5;
            double a = 1 - Math.Exp(-k * dt);
            VX += (wishX * speed - VX) * a;
            VZ += (wishZ * speed - VZ) * a;
            if (OnGround && wishX == 0 && wishZ == 0 && VX * VX + VZ * VZ < 0.01) { VX = 0; VZ = 0; }

            jumpCooldown -= dt;
            if (jump && OnGround && jumpCooldown <= 0)
            {
                VY = JumpSpeed * Math.Sqrt(Math.Max(1, jumpMul));
                OnGround = false;
                jumpCooldown = 0.5;
            }
            VY = Math.Max(VY - Gravity * dt, -TerminalFall);

            bool canStep = OnGround;
            double dy = VY * dt;
            double my = g.Sweep(1, dy, X - HalfWidth, Y, Z - HalfWidth, X + HalfWidth, Y + Height, Z + HalfWidth);
            Y += my;
            if (my != dy)
            {
                if (dy < 0) OnGround = true;
                VY = 0;
            }
            else OnGround = false;

            double x0 = X, z0 = Z;
            MoveHorizontal(g, 0, VX * dt, canStep, sneak);
            MoveHorizontal(g, 2, VZ * dt, canStep, sneak);
            Moving = Math.Abs(X - x0) + Math.Abs(Z - z0) > 1e-4;

            // walking down stairs: snap onto the next step instead of falling off each one
            if (canStep && Moving && VY <= 0)
            {
                double reach = (OnSteepFlight() ? ShipStairStep : StepHeight) + 0.05;
                double down = g.Sweep(1, -reach, X - HalfWidth, Y, Z - HalfWidth, X + HalfWidth, Y + Height, Z + HalfWidth);
                if (down < -1e-6 && down > -reach + 1e-6)
                {
                    Y += down;
                    SmoothOffset -= down;
                    OnGround = true;
                    VY = 0;
                }
            }
            Glide(dt, Math.Sqrt(VX * VX + VZ * VZ));

            if (Y < g.OY - 1) return Result.Overboard;
            if (!OnGround && !g.Collides(X - HalfWidth, g.OY, Z - HalfWidth, X + HalfWidth, Y + 0.01, Z + HalfWidth))
            {
                return Result.Overboard;  // nothing of the ship below us: we went over the side
            }
            return Result.Ok;
        }

        /// <summary>Glide over steps at a steady rate a bit faster than we move, so stairs feel like a ramp.</summary>
        void Glide(double dt, double horizontalSpeed)
        {
            double rate = Math.Max(MinSmoothRate, 1.25 * horizontalSpeed) * dt;
            SmoothOffset = SmoothOffset > 0 ? Math.Max(0, SmoothOffset - rate) : Math.Min(0, SmoothOffset + rate);
            if (Math.Abs(SmoothOffset) > 2.5) SmoothOffset = Math.Sign(SmoothOffset) * 2.5;
        }

        // ------------------------------------------------------------------ ratlines

        static void At(double[] p, out double x, out double y, out double z) { x = p[0]; y = p[1]; z = p[2]; }

        /// <summary>The ladder we'd start climbing if we kept walking this way, or null.</summary>
        public LadderDef LadderAhead(LadderDef[] ladders, double wishX, double wishZ, out double startT)
        {
            startT = 0;
            double wl = Math.Sqrt(wishX * wishX + wishZ * wishZ);
            if (ladders == null || wl < 1e-6) return null;
            wishX /= wl; wishZ /= wl;
            foreach (var l in ladders)
            {
                if (l == rearm) continue;
                // from the deck or the bulwark: face outboard into the shrouds
                Flat(l.ExitBottom, l.Bottom, out double ax, out double az);
                double px = l.Bottom[0] - X, pz = l.Bottom[2] - Z;
                double along = px * ax + pz * az, side = Math.Abs(px * az - pz * ax);
                bool levelOk = Math.Abs(Y - l.ExitBottom[1]) < 0.6 || Math.Abs(Y - l.Bottom[1]) < 0.6;
                // (0.3.18) a ladder-stair is grabbed from anywhere over its foot cell too (nobody walks up its treads)
                double behind = l.Kind == "stair" ? -0.8 : -0.3;
                if (levelOk && along > behind && along < 1.4 && side < (l.Kind == "stair" ? 0.6 : 0.8) && wishX * ax + wishZ * az > 0.7)
                {
                    startT = 0;
                    return l;
                }
                // from the top: walk to the edge of the platform above the shrouds
                Flat(l.Top, l.Bottom, out double ox, out double oz);
                double ex = l.ExitTop[0] - X, ez = l.ExitTop[2] - Z;
                if (Math.Abs(Y - l.ExitTop[1]) < 0.4 && ex * ex + ez * ez < 1.0 && wishX * ox + wishZ * oz > 0.7 &&
                    TopClosed?.Invoke(l) != true)
                {
                    startT = 1;
                    return l;
                }
            }
            return null;
        }

        /// <summary>
        /// Up (+1) or down (-1) on a ladder by the walk's direction against the way onto it from its foot (from its bottom
        /// exit to its bottom: facing the shrouds outboard, aft onto the hold's ladder); walking across it, the keys' own.
        /// </summary>
        public static int ClimbFromWish(LadderDef l, double wishX, double wishZ, int keys)
        {
            double wl = Math.Sqrt(wishX * wishX + wishZ * wishZ);
            if (wl < 1e-6 || l?.ExitBottom == null || l.Bottom == null) return keys;
            Flat(l.ExitBottom, l.Bottom, out double ax, out double az);
            double dot = (wishX * ax + wishZ * az) / wl;
            if (Math.Abs(dot) < 0.3) return keys;
            return dot > 0 ? 1 : -1;
        }

        bool FarFrom(double[] p) => (p[0] - X) * (p[0] - X) + (p[1] - Y) * (p[1] - Y) + (p[2] - Z) * (p[2] - Z) > 1.5 * 1.5;

        /// <summary>Horizontal unit direction from a to b.</summary>
        static void Flat(double[] a, double[] b, out double x, out double z)
        {
            x = b[0] - a[0]; z = b[2] - a[2];
            double l = Math.Sqrt(x * x + z * z);
            if (l < 1e-9) { x = 1; z = 0; return; }
            x /= l; z /= l;
        }

        bool TryStartClimb(LadderDef[] ladders, double wishX, double wishZ)
        {
            var l = LadderAhead(ladders, wishX, wishZ, out double t);
            if (l == null) return false;
            Climbing = l;
            ClimbT = t;
            descendLatch = t >= 1;
            MoveTo(PathPoint(l, t));
            VX = VY = VZ = 0;
            OnGround = false;
            return true;
        }

        static (double x, double y, double z) PathPoint(LadderDef l, double t)
        {
            At(l.Bottom, out double bx, out double by, out double bz);
            At(l.Top, out double tx, out double ty, out double tz);
            return (bx + (tx - bx) * t, by + (ty - by) * t, bz + (tz - bz) * t);
        }

        /// <summary>Move somewhere; the camera glides there vertically.</summary>
        void MoveTo((double x, double y, double z) p)
        {
            SmoothOffset -= p.y - Y;
            X = p.x; Y = p.y; Z = p.z;
        }

        Result StepClimb(VoxelGrid g, double dt, int climb, LadderDef[] ladders = null)
        {
            var l = Climbing;
            At(l.Bottom, out double bx, out double by, out double bz);
            At(l.Top, out double tx, out double ty, out double tz);
            double len = Math.Sqrt((tx - bx) * (tx - bx) + (ty - by) * (ty - by) + (tz - bz) * (tz - bz));
            // stepping off the top: whatever key walked you over the edge keeps taking you down until you let go
            if (descendLatch) { if (climb == 0) descendLatch = false; else climb = -1; }
            double wasT = ClimbT;
            ClimbT += climb * ClimbSpeed * dt / Math.Max(0.1, len);
            Moving = climb != 0;
            // (0.3.12) a shut hatch on the way: from below your head stops under it, from above your feet on it
            if (l.Through != null && !double.IsNaN(l.ThroughY) && Math.Abs(ty - by) > 1e-3 && ThroughClosed?.Invoke(l) == true)
            {
                double tHatch = (l.ThroughY - by) / (ty - by), head = Height / Math.Max(0.1, len);
                if (wasT <= tHatch - head + 1e-6 && ClimbT > tHatch - head) { ClimbT = tHatch - head; Moving = false; }
                else if (wasT >= tHatch - 1e-6 && ClimbT < tHatch) { ClimbT = tHatch; Moving = false; }
            }
            // (0.3.23, note 169) a shut hatch over its top: the head stops under it (not through it, onto the deck above)
            if (CoverUnder != null && Math.Abs(ty - by) > 1e-3 && climb > 0)
            {
                double under = CoverUnder(l);
                if (!double.IsNaN(under))
                {
                    double tMax = (under - Height - by) / (ty - by);
                    if (ClimbT > tMax && wasT <= tMax + 1e-6) { ClimbT = Math.Max(0, tMax); Moving = false; }
                }
            }
            VX = VY = VZ = 0;
            if (ClimbT >= 1 && TopClosed?.Invoke(l) == true)
            {
                ClimbT = 1;          // the gate at the top is shut: hang on until someone opens it
                Moving = false;
            }
            if (ClimbT >= 1 && TopClosed?.Invoke(l) != true)
            {
                Climbing = null;
                rearm = l;
                MoveTo((l.ExitTop[0], l.ExitTop[1], l.ExitTop[2]));
                Unstick(g);
                OnGround = true;
            }
            else if (ClimbT <= 0 && climb < 0)
            {
                Climbing = null;
                rearm = l;
                if (l.Outside)
                {
                    // off the bottom of a rope ladder down the side: into the water, off the ship
                    X = l.ExitBottom[0]; Y = l.ExitBottom[1]; Z = l.ExitBottom[2];
                    SmoothOffset = 0;
                    return Result.LeftShip;
                }
                // (0.3.13, note 136) down a ladder that stands on a hatch (the waist's, over the hold's): with that hatch
                // open there's no floor at its foot, so on down the ladder under it, if its gate is open
                if (ladders != null && !HasFloor(g, l.ExitBottom[0], l.ExitBottom[1], l.ExitBottom[2]))
                {
                    At(l.Bottom, out double fx, out double fy, out double fz);
                    foreach (var below in ladders)
                    {
                        if (below == l || below.Outside) continue;
                        At(below.Top, out double ux, out double uy, out double uz);
                        if (fy - uy < -0.1 || fy - uy > 1.0 || (ux - fx) * (ux - fx) + (uz - fz) * (uz - fz) > 0.5 * 0.5) continue;
                        if (TopClosed?.Invoke(below) == true) continue;
                        Climbing = below;
                        rearm = null;
                        ClimbT = 1;
                        MoveTo(PathPoint(below, 1));
                        OnGround = false;
                        return Result.Ok;
                    }
                }
                MoveTo((l.ExitBottom[0], l.ExitBottom[1], l.ExitBottom[2]));
                Unstick(g);
                OnGround = true;
            }
            else
            {
                ClimbT = Math.Clamp(ClimbT, 0, 1);
                var p = PathPoint(l, ClimbT);
                X = p.x; Y = p.y; Z = p.z;   // moving along the ratlines is already smooth
            }
            Glide(dt, climb != 0 ? ClimbSpeed : 0);
            return Result.Ok;
        }

        /// <summary>Put the walker on a ladder at its bottom (boarding from the water).</summary>
        public void StartClimbAtBottom(LadderDef l)
        {
            Climbing = l;
            ClimbT = 0;
            descendLatch = false;
            MoveTo(PathPoint(l, 0));
            VX = VY = VZ = 0;
            OnGround = false;
        }

        void MoveHorizontal(VoxelGrid g, int axis, double d, bool canStep, bool sneak)
        {
            if (Math.Abs(d) < 1e-9) return;
            double m = g.Sweep(axis, d, X - HalfWidth, Y, Z - HalfWidth, X + HalfWidth, Y + Height, Z + HalfWidth);
            if (Math.Abs(m - d) > 1e-9 && canStep)
            {
                foreach (double s in OnSteepFlight() ? SteepStepTries : StepTries)
                {
                    if (g.Collides(X - HalfWidth, Y + s, Z - HalfWidth, X + HalfWidth, Y + s + Height, Z + HalfWidth)) continue;
                    double m2 = g.Sweep(axis, d, X - HalfWidth, Y + s, Z - HalfWidth, X + HalfWidth, Y + s + Height, Z + HalfWidth);
                    if (Math.Abs(m2) > Math.Abs(m) + 1e-4)
                    {
                        double y0 = Y;
                        Y += s;
                        Apply(axis, m2);
                        // settle onto the step
                        double down = g.Sweep(1, -s, X - HalfWidth, Y, Z - HalfWidth, X + HalfWidth, Y + Height, Z + HalfWidth);
                        Y += down;
                        SmoothOffset -= Y - y0;
                        return;
                    }
                }
            }

            if (sneak && OnGround && Math.Abs(m) > 0)
            {
                // don't walk off an edge while sneaking
                double nx = axis == 0 ? X + m : X, nz = axis == 2 ? Z + m : Z;
                if (!g.Collides(nx - HalfWidth, Y - 0.6, nz - HalfWidth, nx + HalfWidth, Y + 0.01, nz + HalfWidth))
                {
                    m = 0;
                }
            }

            Apply(axis, m);
            if (Math.Abs(m - d) > 1e-9)
            {
                if (axis == 0) VX = 0; else VZ = 0;
            }
        }

        void Apply(int axis, double m)
        {
            if (axis == 0) X += m; else Z += m;
        }

        /// <summary>If the box overlaps the ship, push it up until it's free (boarding, relog, ship bumped).</summary>
        public void Unstick(VoxelGrid g)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!g.Collides(X - HalfWidth, Y, Z - HalfWidth, X + HalfWidth, Y + Height, Z + HalfWidth)) return;
                Y = Math.Floor((Y - g.OY) / g.V + 1) * g.V + g.OY;
            }
        }

        /// <summary>
        /// (0.3.18) Climb a ladder-stair (its ladder) up and down again in a collision grid, as a player would: from where one
        /// steps on (half a block before it) walking toward it until on it, on up until off at the top; then from there
        /// walking back into the well until on it, on down until off at the foot. top: how high over its foot the climb up
        /// ended; back: where the climb down ended (over the foot's deck). True if both ended on her floor at the right
        /// decks. headClear: the climber's head (and body) never in her collision on the way (a deck's edge over the well).
        /// </summary>
        public static bool ClimbStair(VoxelGrid g, LadderDef l, out double top, out double back, out bool headClear)
        {
            top = back = 0;
            headClear = true;
            var ladders = new[] { l };
            double fx = l.ExitBottom[0], fy = l.ExitBottom[1], fz = l.ExitBottom[2];
            Flat(l.ExitBottom, l.Bottom, out double ax, out double az);
            // the climber all the way up its path: free of her (the ladder's own boxes aren't in g)
            for (int k = 0; k <= 24; k++)
            {
                var p = PathPoint(l, k / 24.0);
                if (g.Collides(p.x - 0.2 + 0.02, p.y + PiecePlanner.ClimberLow(k / 24.0), p.z - 0.2 + 0.02, p.x + 0.2 - 0.02, p.y + Height - 0.02, p.z + 0.2 - 0.02)) { headClear = false; break; }
            }
            var m = new DeckMover(fx, fy, fz) { OnGround = true };
            m.Unstick(g);
            for (int n = 0; n < 400 && (m.Climbing != null || n < 5 || m.FarFrom(l.ExitTop)); n++)
            {
                m.Step(g, 0.05, ax, az, 3.0, false, false, 1, ladders, 1);
                if (m.Climbing == null && !m.FarFrom(l.ExitTop) && n > 5) break;
            }
            for (int n = 0; n < 10; n++) m.Step(g, 0.05, 0, 0, 3.0, false, false);
            top = m.Y - fy;
            bool up = m.Climbing == null && !m.FarFrom(l.ExitTop) && HasFloor(g, m.X, m.Y + 0.01, m.Z);
            m.rearm = null;
            for (int n = 0; n < 400; n++)
            {
                m.Step(g, 0.05, -ax, -az, 3.0, false, false, 1, ladders, -1);
                if (m.Climbing == null && n > 5 && !m.FarFrom(l.ExitBottom)) break;
            }
            for (int n = 0; n < 10; n++) m.Step(g, 0.05, 0, 0, 3.0, false, false);
            back = m.Y - fy;
            bool down = m.Climbing == null && Math.Abs(back) < 0.1;
            return up && down && Math.Abs(top - (l.ExitTop[1] - fy)) < 0.1;
        }

        /// <summary>Is there ship floor right under these feet (within 0.3)?</summary>
        public static bool HasFloor(VoxelGrid g, double x, double y, double z)
        {
            return g.Collides(x - HalfWidth, y - 0.3, z - HalfWidth, x + HalfWidth, y + 0.02, z + HalfWidth);
        }
    }

    /// <summary>Getting aboard from the water or a dock (ShipClientSystem's clicks).</summary>
    public static class Boarding
    {
        /// <summary>
        /// A right-click on her side from the water or a dock (ship-local: where the player is, where the crosshair meets
        /// her): the rope ladder to climb (standing within 5 of its foot, aiming within 2 blocks of the ladder empty-handed,
        /// within 1 with anything in hand: on the ladder itself the click is the ladder's, the user 0.3.10), or none; hint:
        /// say how to get aboard (empty-handed, no ladder there; with something in hand the click on her plain side is the
        /// item's: mending her).
        /// </summary>
        public static LadderDef OutsideClick(LadderDef[] ladders, Vintagestory.API.MathTools.Vec3d me, Vintagestory.API.MathTools.Vec3d aim, bool holding, out bool hint)
        {
            hint = false;
            if (ladders == null) return null;
            LadderDef best = null;
            double bd = holding ? 1.0 : 2.0;
            foreach (var l in ladders)
            {
                if (!l.Outside || l.Bottom == null || l.Top == null) continue;
                double fx = l.Bottom[0] - me.X, fz = l.Bottom[2] - me.Z, fy = l.Bottom[1] - me.Y;
                if (fx * fx + fz * fz > 25 || fy < -2.5 || fy > 2.0) continue;
                double d = SegmentDistance(aim, l.Bottom, l.Top);
                if (d < bd) { bd = d; best = l; }
            }
            hint = best == null && !holding;
            return best;
        }

        static double SegmentDistance(Vintagestory.API.MathTools.Vec3d p, double[] a, double[] b)
        {
            double vx = b[0] - a[0], vy = b[1] - a[1], vz = b[2] - a[2];
            double len2 = vx * vx + vy * vy + vz * vz;
            double t = len2 < 1e-9 ? 0 : Math.Clamp(((p.X - a[0]) * vx + (p.Y - a[1]) * vy + (p.Z - a[2]) * vz) / len2, 0, 1);
            double dx = p.X - (a[0] + vx * t), dy = p.Y - (a[1] + vy * t), dz = p.Z - (a[2] + vz * t);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
