using System;

namespace Hansa
{
    public readonly record struct DeckFloodFlow(double Aboard, double Drained, double Below);

    public static class DeckFlooding
    {
        /// <summary>
        /// Conserved weather-deck water, tonnes and seconds. Water crosses the rail/actual open ports first; a sea
        /// surface above a hatch inside intact bulwarks is not itself a route aboard. Scuppers have a finite aperture
        /// and flow either way under the difference of heads. Open hatches/cells drain the pond into the hold;
        /// a sound, closed weather deck seeps slowly. The pond is mixed over the exposed deck (not a breaking-wave solver).
        /// </summary>
        public static DeckFloodFlow Step(DamageLayout l, in FloodPose pose, Func<string, bool> isOpen, double dt,
            ref double deckWater)
        {
            if (dt <= 0) return default;
            var d = l.D;
            double area = Math.Max(1, l.DeckArea), depth = Math.Max(0, deckWater) / area;
            double aboard = 0, drain = 0, below = Math.Max(0, deckWater) * Math.Max(0, d.DeckSeepRate), direct = 0;
            foreach (var f in l.Downflood)
            {
                if (f.OpenFixture != null && isOpen?.Invoke(f.OpenFixture) != true) continue;
                pose.ToWorld(f.X, f.Y, f.Z, out double x, out double y, out double z);
                if (f.Hatch)
                {
                    // Cell openings have one square metre each; fixture hatches use their actual footprint.
                    double opening = f.Cell ? 1 : HatchArea(l.Def, f.OpenFixture);
                    double head = Math.Max(0, depth - Math.Max(0, f.Y - l.Def.DeckY));
                    below += ShipFlood.Flow(d.LeakCoefficient, opening, head, 0);
                    continue;
                }
                double outside = pose.Surface(x, z) - y;
                if (f.Direct)
                {
                    direct += Math.Max(0, SideFlow(d, f, outside, 0));
                    continue;
                }
                double inside = Math.Max(0, depth - Math.Max(0, f.Y - l.Def.DeckY));
                double flow;
                if (f.Weight < 1) // scuppers, not an unbounded weir along a fifth of the side
                    flow = ShipFlood.Flow(d.LeakCoefficient, f.Length * Math.Max(0, d.ScupperAreaPerLength), outside, inside);
                else
                    flow = SideFlow(d, f, outside, inside);
                if (flow > 0) aboard += flow;
                else drain -= flow;
            }
            // Never remove water which has not arrived. Share a nearly dry pond proportionally between outlets.
            double available = Math.Max(0, deckWater) + aboard * dt;
            double outlets = (below + drain) * dt;
            double share = outlets > available ? available / outlets : 1;
            below *= share;
            drain *= share;
            deckWater = Math.Max(0, available - (below + drain) * dt);
            return new DeckFloodFlow(aboard + direct, drain, below + direct);
        }

        static double HatchArea(ShipDefinition def, string code)
        {
            var b = def.Fixture(code)?.Box;
            return b == null ? 1 : Math.Max(0, (b[3] - b[0]) * (b[5] - b[2]));
        }

        static double SideFlow(DamageDef d, DownfloodPoint f, double outside, double inside)
        {
            // A covered gun port cannot grow into an infinitely tall opening when a storm crest submerges it.
            if (f.Height > 0 && Math.Max(outside, inside) >= f.Height)
                return ShipFlood.Flow(d.LeakCoefficient, f.Length * f.Height,
                    outside - f.Height / 2, inside - f.Height / 2);
            return ShipFlood.Pour(d, f.Length, f.Weight, outside) - ShipFlood.Pour(d, f.Length, f.Weight, inside);
        }
    }
}
