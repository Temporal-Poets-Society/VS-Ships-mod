using System;
using System.Linq;
using Hansa;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// The bilge pump's water (the user's note 39, PumpJet): the carrack's spout from her design (or the fallback
        /// without one), the water leaving it falls clear of the pump onto the deck beside it; how much comes out of each
        /// pump (none while her hold is dry, shared by the pumps worked), as particles a second, in gushes with the strokes.
        /// </summary>
        static void PumpWater(ShipDefinition def)
        {
            Console.WriteLine("the bilge pump's water: its spout, where it lands, how much comes out");
            var pump = def.Fixtures.FirstOrDefault(f => f.Type == "pump");
            Check(pump != null, "she has a pump");
            if (pump == null) return;

            PumpJet.Spout(pump, out var at, out var dir);
            double t = PumpJet.Landing(pump, out var land);
            var box = pump.Sel;
            bool fromDesign = pump.Spout?.Length >= 3 && pump.SpoutDir?.Length >= 3;
            bool overDeck = at.Y > pump.Pos[1] + 0.2 && at.Y < pump.Pos[1] + 1.5 && Math.Abs(dir.Length() - 1) < 1e-9 && dir.Y <= 0.05;
            // it lands beside the pump (out of its box), on her deck, within a block and a half of the spout
            bool clear = box == null || !(land.X > box[0] && land.X < box[3] && land.Z > box[2] && land.Z < box[5]);
            double reach = Math.Sqrt((land.X - at.X) * (land.X - at.X) + (land.Z - at.Z) * (land.Z - at.Z));
            bool onDeck = def.Grid.SolidAt(land.X, land.Y - 0.1, land.Z) && !def.Grid.SolidAt(land.X, land.Y + 0.15, land.Z);
            Check(overDeck && clear && onDeck && reach > 0.1 && reach < 1.5 && t > 0.15 && t < 0.6,
                $"the water leaves its spout ({at.X:0.00}, {at.Y:0.00}, {at.Z:0.00}{(fromDesign ? ", from her design" : ", the fallback")}) and falls " +
                $"{t:0.00} s onto the deck {reach:0.00} beside it ({land.X:0.00}, {land.Z:0.00}), clear of the pump");

            // without a spout in the design: over it and to its right (facing her bow: starboard), out that way and down
            var bare = new FixtureDef { Type = "pump", Code = "p", Pos = new[] { 1.0, 2.0, 0.0 }, Yaw = 0 };
            PumpJet.Spout(bare, out var at0, out var dir0);
            var port = new FixtureDef { Type = "pump", Code = "q", Pos = new[] { 1.0, 2.0, 0.0 }, Yaw = 90 };
            PumpJet.Spout(port, out var at1, out var dir1);
            Check(Math.Abs(at0.Y - 2.65) < 1e-9 && at0.Z > 0.4 && dir0.Z > 0.9 && dir0.Y < 0 && at1.X > 1.4 && dir1.X > 0.9,
                "without a spout in her design: 0.65 over the pump and 0.45 to its right, pointing that way and a little down (turned with its facing)");

            // how much comes out
            double f1 = PumpJet.FlowAt(pump.Code, pump.Code, 0.16), f2 = PumpJet.FlowAt(pump.Code, pump.Code + ",other", 0.16);
            double none = PumpJet.FlowAt(pump.Code, "other", 0.16), dry = PumpJet.FlowAt(pump.Code, pump.Code, 0);
            Check(Math.Abs(f1 - 0.16) < 1e-9 && Math.Abs(f2 - 0.08) < 1e-9 && none == 0 && dry == 0 && PumpJet.FlowAt(pump.Code, "", 0.16) == 0,
                "a worked pump gives what she pumps (shared with the others worked), one nobody works and a dry hold nothing");
            var d = def.DamageParts.D;
            double full = ShipFlood.PumpRate(d, 1);
            double one = PumpJet.Rate(full, full), two = PumpJet.Rate(ShipFlood.PumpRate(d, d.PumpersPerPump), full), low = PumpJet.Rate(full * 0.1, full);
            // (the user's note 60: one man pumping gave a feeble stream)
            Check(PumpJet.Rate(0, full) == 0 && Math.Abs(one - PumpJet.FullRate) < 1e-9 && (d.PumpersPerPump < 2 || two > one) && low >= PumpJet.MinRate && low < one &&
                  PumpJet.Rate(10, full) == PumpJet.MaxRate && PumpJet.Volume(0) == 0 && PumpJet.Volume(0.08) > 0,
                $"its water as particles: none dry, a full stream ({one:0} a second) with one at the brake, {two:0} with {d.PumpersPerPump}, {low:0} as her hold runs dry, " +
                $"never more than {PumpJet.MaxRate:0}; the pour heard only with water");
            double lo = 1, hi = 0;
            for (double s = 0; s < 2; s += 0.01) { double p = PumpJet.Pulse(s, d.PumpStroke); lo = Math.Min(lo, p); hi = Math.Max(hi, p); }
            Check(lo >= 0.75 - 1e-9 && hi > 0.99 && hi <= 1 + 1e-9, $"a steady stream, swelling a little with the brake ({lo:0.00} .. {hi:0.00} of it)");
        }
    }
}
