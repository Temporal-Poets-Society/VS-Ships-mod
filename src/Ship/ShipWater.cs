using System;
using System.Collections.Generic;

namespace Hansa
{
    /// <summary>
    /// The water in a ship's hold as it's drawn, as Sea is told about it and as it's wet for breathing and the view: a level
    /// plane in the world (she rolls and pitches round it) through her middle at her flood level
    /// (<see cref="EntityShip.FloodLevel"/>, ship-local), clipped to the inside of her hull at that height. No client API
    /// here, so the offline tests check it.
    /// </summary>
    public static class ShipWater
    {
        /// <summary>How far into the planking the water's edge goes (a little, so no gap shows against the ceiling), unless the mesh says how thick it is.</summary>
        public const double DefaultInset = 0.15;

        /// <summary>The ship-local x her flood level is measured at: the middle of her waterline.</summary>
        public static double FloodX(ShipDefinition def) => def?.WaterClip?.CenterX ?? 0;

        /// <summary>
        /// The water's level plane in her own frame, for her drawn pitch and roll: ship-local y = level + along·(x − FloodX)
        /// + across·z (level in the world).
        /// </summary>
        public static void Plane(double pitch, double roll, out double along, out double across)
        {
            ShipFrame.ToLocal(0, pitch, roll, 0, 1, 0, out double ux, out double uy, out double uz);
            uy = Math.Max(0.2, uy);
            along = -ux / uy;
            across = -uz / uy;
        }

        /// <summary>
        /// The inside of her hull, half its width at ship-local x and y: the generator's exact outline of her outside
        /// (the dry outline) less inset, else her dry mask's widest cell there; 0 outside her.
        /// </summary>
        public static double InnerHalfWidth(ShipDefinition def, double x, double y, double inset)
        {
            var o = def.WaterClip?.DryOutline;
            if (o?.HalfWidths != null && o.HalfWidths.Length == o.NX * o.NY && o.NX > 1 && o.NY > 1)
            {
                if (y < o.Y0 || y > o.Y0 + (o.NY - 1) * o.DY) return 0;
                return Math.Max(0, o.At(x, y) - inset);
            }
            var m = def.DryMask;
            if (m == null) return 0;
            int i = m.CellX(x), j = m.CellY(y);
            if (i < 0 || j < 0 || i >= m.NX || j >= m.NY) return 0;
            double w = 0;
            for (int k = 0; k < m.NZ; k++)
                if (m.Solid(i, j, k)) w = Math.Max(w, Math.Max(Math.Abs(m.OZ + k * m.V), Math.Abs(m.OZ + (k + 1) * m.V)));
            return Math.Max(0, w - inset);
        }

        /// <summary>
        /// Where the level plane meets her side at ship-local x (side -1 port, +1 starboard): the half-width z (≥ 0) where
        /// the plane's height and the inside of her hull there agree; 0 if the plane is under her there.
        /// </summary>
        public static double Edge(ShipDefinition def, double x, double level, double along, double across, int side, double inset)
        {
            double fx = FloodX(def), y0 = level + along * (x - fx);
            double G(double z) => InnerHalfWidth(def, x, y0 + across * side * z, inset) - z;
            if (G(0) <= 0) return 0;
            double lo = 0, hi = Math.Max(2, def.Beam);
            if (G(hi) > 0) return hi;
            for (int it = 0; it < 30; it++)
            {
                double mid = (lo + hi) / 2;
                if (G(mid) > 0) lo = mid; else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// The water's surface in her hold (ship-local; level in the world for the pitch and roll the plane was made for):
        /// runs of stations along her, every <paramref name="step"/> blocks from her stern to her bow (her hull's inside at
        /// each end found closely), each station strips + 1 points across her from port to starboard (x y z). Nothing where
        /// the plane is under her bottom.
        /// </summary>
        public static List<List<double[]>> Surface(ShipDefinition def, double level, double along, double across, double inset, int strips = 8, double step = 0.5)
        {
            var runs = new List<List<double[]>>();
            var map = def.PartMap;
            double x0 = map?.HullX0 ?? def.Grid.OX, x1 = map?.HullX1 ?? def.Grid.MaxX;
            List<double[]> run = null;
            double lastX = double.NaN;
            bool Wet(double x) => Edge(def, x, level, along, across, 1, inset) + Edge(def, x, level, along, across, -1, inset) > 0.02;
            int n = Math.Max(2, (int)Math.Ceiling((x1 - x0) / step));
            for (int s = 0; s <= n; s++)
            {
                double x = x0 + (x1 - x0) * s / n;
                bool wet = Wet(x);
                if (wet && run == null)
                {
                    // her hull's end inside, between the dry station before and this one
                    run = new List<double[]>();
                    if (s > 0) run.Add(Station(def, Crossing(Wet, lastX, x), level, along, across, inset, strips));
                    runs.Add(run);
                }
                if (wet) run.Add(Station(def, x, level, along, across, inset, strips));
                if (!wet && run != null)
                {
                    run.Add(Station(def, Crossing(Wet, x, lastX), level, along, across, inset, strips));
                    run = null;
                }
                lastX = x;
            }
            runs.RemoveAll(r => r.Count < 2);
            return runs;
        }

        /// <summary>Between a dry x and a wet one: where it turns wet (to a hundredth of a block).</summary>
        static double Crossing(Func<double, bool> wet, double dry, double isWet)
        {
            for (int it = 0; it < 12; it++)
            {
                double mid = (dry + isWet) / 2;
                if (wet(mid)) isWet = mid; else dry = mid;
            }
            return isWet;
        }

        /// <summary>One station across her: strips + 1 points on the plane, from where it meets her port side to her starboard side.</summary>
        static double[] Station(ShipDefinition def, double x, double level, double along, double across, double inset, int strips)
        {
            double zs = Edge(def, x, level, along, across, 1, inset), zp = -Edge(def, x, level, along, across, -1, inset);
            double fx = FloodX(def);
            var p = new double[(strips + 1) * 3];
            for (int k = 0; k <= strips; k++)
            {
                double z = zp + (zs - zp) * k / strips, y = level + along * (x - fx) + across * z;
                // where her section isn't round (by the sternpost, rolled hard), kept inside her along the plane
                for (int it = 0; it < 3 && k > 0 && k < strips; it++)
                {
                    double w = InnerHalfWidth(def, x, y, inset);
                    if (Math.Abs(z) <= w) break;
                    z = Math.Sign(z) * w;
                    y = level + along * (x - fx) + across * z;
                }
                p[3 * k] = x;
                p[3 * k + 1] = y;
                p[3 * k + 2] = z;
            }
            return p;
        }

        /// <summary>
        /// Sea's water clip for a hull (<see cref="Sea.WaterClipBox"/>): her footprint and tilted waterline, dry inside her
        /// outline (a dry design), and when water's in her, its level (world y, level) under which the sea isn't cut away.
        /// o: where her origin is drawn; pitch, roll: as drawn; heel: her lean in the wind (the sea round her doesn't lean);
        /// calm: the ring the waves settle over; floodY: the water in her (world y), NaN when she's dry.
        /// </summary>
        public static Sea.WaterClipBox Clip(ShipDefinition def, double ox, double oy, double oz, double yaw, double pitch, double roll, double heel,
            float calm, double floodY)
        {
            var wc = def.WaterClip;
            ShipFrame.WaterlinePlane(pitch, roll, wc.Height, out double y0, out double along, out double across);
            ShipFrame.WaterlinePlane(pitch, roll - heel, wc.Height, out _, out _, out double calmAcross);
            return new Sea.WaterClipBox
            {
                X = ox, Z = oz, WaterY = oy + y0, Yaw = yaw,
                HalfLength = wc.HalfLength, HalfWidth = wc.HalfWidth, CentreOffset = wc.CenterX, Profile = wc.Profile,
                SlopeAlong = (float)along, SlopeAcross = (float)across, CalmSlopeAcross = (float)calmAcross, CalmZone = calm,
                // a dry hull: no water drawn inside its outline, keel to main deck; the waves run up to it (no calm ring)
                // (up to the waist's rail: a sea breaking higher than the deck outside isn't drawn inside the bulwarks)
                Dry = def.DryMask != null, DryInset = wc.Margin, DryDepth = 0.5f - def.KeelY, DryHeight = def.DeckY + 1.0f,
                // the hull's own outline at four heights (Sea's heights are from the waterline, Height)
                DryProfile = wc.DryProfile, DryLevel0 = wc.DryRowY0 - wc.Height, DryLevelStep = wc.DryRowDY,
                // its stern end: a plane through the transom, raked like it
                DryRake = wc.DryRake,
                // the water in her: the sea shows under it (a little under the water drawn in her, so the two don't fight)
                Flooded = !double.IsNaN(floodY), FloodY = double.IsNaN(floodY) ? 0 : floodY - FloodGap,
            };
        }

        /// <summary>How far under the water drawn in her hold the sea starts to show (so the two surfaces don't flicker where they meet).</summary>
        public const double FloodGap = 0.05;
    }
}
