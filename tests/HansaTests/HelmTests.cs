using System;
using System.Linq;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// The helm and the wind UI (0.3.10). The user: "steering works opposite: I need to push right to go left". Pins the
        /// sign the server sails her with: the helm to starboard (D) with headway swings her bow to starboard, at the wheel,
        /// at a boat's tiller and at her oars; the frames the wind panel and the map draw with; her plan for the map.
        /// </summary>
        static void Helm(ShipDefinition def, ShipDefinition boat)
        {
            Console.WriteLine("the helm and the wind UI");
            Check(HelmMath.WindSpeed(9) == "17 kn" && HelmMath.WindSpeed(11) == "21 kn" && HelmMath.WindSpeed(0) == "0 kn",
                "true and apparent wind display knots only (17 kn / 21 kn), without blocks per second");
            var sd = def.Sailing;

            // her frame: the bow is ship-local +x, starboard +z (ShipFrame), and starboard is on the right of the bow seen
            // from above (north up: +x east, +z south; the right of a heading (x, z) is (-z, x))
            bool frame = true;
            for (int i = 0; i < 24; i++)
            {
                double yaw = i * Math.PI / 12 - 3;
                ShipFrame.ToWorld(yaw, 1, 0, 0, out double bx, out _, out double bz);
                ShipFrame.ToWorld(yaw, 0, 0, 1, out double sx, out _, out double sz);
                var b = HelmMath.Bow(yaw);
                var s = HelmMath.Starboard(yaw);
                frame &= Math.Abs(b.X - bx) < 1e-9 && Math.Abs(b.Z - bz) < 1e-9 && Math.Abs(s.X - sx) < 1e-9 && Math.Abs(s.Z - sz) < 1e-9
                         && Math.Abs(s.X + b.Z) < 1e-9 && Math.Abs(s.Z - b.X) < 1e-9;
            }
            Check(frame, "her bow is ship-local +x and her starboard +z, on the right of the bow seen from above");
            Check(HelmMath.RudderInput(false, true) == 1 && HelmMath.RudderInput(true, false) == -1 && HelmMath.RudderInput(true, true) == 0,
                "D (the right key) puts the helm to starboard (+1), A to port");

            // a second of the server's turn with each helm, at every heading
            double Turned(double yaw, float rudder, double speed, bool rowing = false)
            {
                double steer = Sailing.Steer(speed, def.MaxSpeed, false, sd);
                if (rowing) steer = Math.Max(steer, 0.7);
                double rate = HelmMath.YawRate(rudder, def.TurnRate, steer);
                var after = HelmMath.Bow(yaw + rate * 1.0);
                var s = HelmMath.Starboard(yaw);
                return after.X * s.X + after.Z * s.Z;     // + : her bow went to starboard
            }
            bool ahead = true, astern = true;
            for (int i = 0; i < 16; i++)
            {
                double yaw = i * Math.PI / 8;
                ahead &= Turned(yaw, 1, 5) > 0 && Turned(yaw, -1, 5) < 0 && Turned(yaw, 1, 0) > 0;
                astern &= Turned(yaw, 1, -1) < 0;
            }
            Check(ahead, "helm to starboard (D) with headway (or barely moving) swings her bow to starboard, A to port, at every heading");
            Check(astern, "making sternway the helm works the other way (the wind panel says so at the helm)");

            // heading east (yaw π/2), D for a while: she comes round to the south (a compass heading growing: clockwise)
            double Heading(double y) { var b = HelmMath.Bow(y); return Sea.Compass.Bearing(b.X, b.Z); }
            double yawE = Math.PI / 2, h0 = Heading(yawE);
            double y1 = yawE + HelmMath.YawRate(1, def.TurnRate, 1) * 3;
            Check(Math.Abs(h0 - 90) < 1e-6 && Heading(y1) > 90 && Heading(y1) < 180,
                $"heading east with the helm to starboard she comes round toward the south ({h0:0}° → {Heading(y1):0}°)");

            // the boat: her tiller (the same helm) and her oars (D pulls the port oar harder, even with no way on)
            Check(boat.RowSpeed > 0 && Turned(0.3, HelmMath.RudderInput(false, true), 0, true) > 0 && Turned(0.3, HelmMath.RudderInput(true, false), -0.5, true) < 0,
                "a boat's oars: D turns her bow to starboard, A to port, rowing or backing");
            var helm = def.Fixtures.FirstOrDefault(f => f.Type == "helm");
            var boatHelm = boat.Fixtures.FirstOrDefault(f => f.Type == "helm");
            Check(helm != null && helm.Yaw == 0 && boatHelm != null && boatHelm.Yaw == 0 && boat.Fixtures.Where(f => f.Type == "oars").All(f => f.Yaw == 0),
                "the helmsman and the rowers face her bow (the seats' yaw 0: the view turns with her the way she turns)");

            // the wind panel's arrows: a wind blowing toward her starboard side is +90° (clockwise), toward the bow 0
            bool rel = true;
            for (int i = 0; i < 12; i++)
            {
                double yaw = i * Math.PI / 6;
                var s = HelmMath.Starboard(yaw);
                var b = HelmMath.Bow(yaw);
                rel &= Math.Abs(HelmMath.Relative(s.X, s.Z, yaw) - Math.PI / 2) < 1e-9 && Math.Abs(HelmMath.Relative(b.X, b.Z, yaw)) < 1e-9
                       && Math.Abs(HelmMath.Relative(-s.X, -s.Z, yaw) + Math.PI / 2) < 1e-9;
            }
            Check(rel, "the wind panel: a direction to starboard is drawn to the right of her bow, to port to the left");

            // the point of sail and the tack: a beam wind from starboard is a beam reach on the starboard tack
            var sb = HelmMath.Starboard(0.7);
            string beam = HelmMath.PointOfSail(-sb.X * 0.3, -sb.Z * 0.3, 0.7, 1, 3, sd, out int tack);
            var bw = HelmMath.Bow(0.7);
            string head = HelmMath.PointOfSail(-bw.X * 0.3, -bw.Z * 0.3, 0.7, 1, 0.5, sd, out _);
            string aback = HelmMath.PointOfSail(-bw.X * 0.3, -bw.Z * 0.3, 0.7, 1, -0.4, sd, out _);
            string run = HelmMath.PointOfSail(bw.X * 0.3, bw.Z * 0.3, 0.7, 1, 3, sd, out _);
            Check(beam == "beamreach" && tack == 1 && head == "inirons" && aback == "aback" && run == "running",
                $"points of sail: beam wind from starboard → {beam}, tack {tack}; head to wind → {head}, with sternway → {aback}; wind astern → {run}");

            // (0.3.12) the panel's red no-go wedge covers her bow exactly when she's in irons, at every heading and wind
            {
                int wrong = 0, n = 0;
                for (double yaw = -3; yaw < 3.2; yaw += 0.37)
                    for (double wa = 0; wa < 360; wa += 7)
                    {
                        double wx = Math.Sin(wa * Math.PI / 180) * 0.4, wz = Math.Cos(wa * Math.PI / 180) * 0.4;
                        HelmMath.NoGoWedge(wx, wz, yaw, sd.NoGo, out double f0, out double f1);
                        bool covers = HelmMath.InWedge(0, f0, f1);
                        bool irons = Sailing.Point(Sailing.WindAngle(wx, wz, yaw, out _), sd) == Hansa.PointOfSail.InIrons;
                        n++;
                        if (covers != irons) wrong++;
                    }
                Check(wrong == 0, $"the wind panel's no-go wedge (the true wind ± {sd.NoGo}°) covers her bow exactly when she's in irons ({n - wrong} of {n} winds and headings)");
            }

            // (after 0.1.0-dev.5, the user: "red wind zone does not match minimap and HUD") the map's no-go fan and the wind
            // panel's are one wedge: the same width (the map's had been half as wide), the card turned to her heading lying
            // on the map's, its edges the headings where the sailing model's true wind angle is the no-go angle, its middle
            // head to wind; for the caravel's 60°, the carrack's 67° and this design's, at headings and winds all round
            {
                double Wrap(double a) => Math.IEEERemainder(a, 2 * Math.PI);
                // a heading on the map (an angle from east toward south) as her yaw: the bow (sin yaw, cos yaw) = (cos a, sin a)
                double YawOf(double mapAngle) => Math.Atan2(Math.Cos(mapAngle), Math.Sin(mapAngle));
                int wrong = 0, n = 0;
                string first = null;
                foreach (double noGo in new double[] { 60, 67, sd.NoGo })
                {
                    var d = sd.Clone();
                    d.NoGo = (float)noGo;
                    for (double yaw = -3; yaw < 3.2; yaw += 0.41)
                        for (double wa = 3; wa < 360; wa += 23)
                        {
                            var w = Sea.Wind.FromCompass(wa, 0.15);
                            HelmMath.MapNoGo(w.X, w.Z, yaw, d.NoGo, out double centre, out double width);
                            HelmMath.CardNoGo(w.X, w.Z, yaw, d.NoGo, out double k0, out double k1);
                            // the card is bow-up: turned so its up lies along her bow on the map, its angles are the map's
                            double turn = HelmMath.MapAngle(0, yaw) - HelmMath.CardAngle(0);
                            double e0 = centre - width / 2, e1 = centre + width / 2;
                            bool same = Math.Abs(width - 2 * noGo * Math.PI / 180) < 1e-9 && HelmMath.FanDegrees(width) == (int)Math.Round(2 * noGo)
                                        && Math.Abs(Wrap(k0 + turn - e0)) < 1e-9 && Math.Abs(Wrap(k1 + turn - e1)) < 1e-9;
                            double a0 = Sailing.WindAngle(w.X, w.Z, YawOf(e0), out _), a1 = Sailing.WindAngle(w.X, w.Z, YawOf(e1), out _);
                            double ac = Sailing.WindAngle(w.X, w.Z, YawOf(centre), out _);
                            bool model = Math.Abs(a0 - noGo) < 1e-6 && Math.Abs(a1 - noGo) < 1e-6 && ac < 1e-6
                                         && Sailing.Point(Sailing.WindAngle(w.X, w.Z, YawOf(centre + width / 2 * 0.98), out _), d) == Hansa.PointOfSail.InIrons
                                         && Sailing.Point(Sailing.WindAngle(w.X, w.Z, YawOf(centre + width / 2 * 1.02), out _), d) != Hansa.PointOfSail.InIrons;
                            n++;
                            if (!(same && model)) { wrong++; first ??= $" first: no-go {noGo}°, yaw {yaw:0.00}, wind from {wa}°: map {width * 180 / Math.PI:0.0}° wide, edges at {a0:0.0}°/{a1:0.0}° off the wind"; }
                        }
                }
                // the user's case: heading NE, the wind from the SE (5 kn), the caravel's 60°: the bow directions from 75° to 195°
                var se = Sea.Wind.FromCompass(135, 0.15);
                double yawNE = YawOf(-Math.PI / 4);
                HelmMath.MapNoGo(se.X, se.Z, yawNE, 60, out double cNE, out double wNE);
                double Bearing(double mapAngle) => Sea.Compass.Bearing(Math.Cos(mapAngle), Math.Sin(mapAngle));
                bool user = Math.Abs(Sea.Compass.Bearing(HelmMath.Bow(yawNE).X, HelmMath.Bow(yawNE).Z) - 45) < 1e-6 && Sea.Compass.Name(se.FromDegrees) == "SE"
                            && Math.Abs(Bearing(cNE - wNE / 2) - 75) < 1e-6 && Math.Abs(Bearing(cNE + wNE / 2) - 195) < 1e-6
                            && Math.Abs(HelmMath.Relative(-se.X, -se.Z, yawNE) - Math.PI / 2) < 1e-9;     // (from her starboard beam)
                Check(wrong == 0 && user, $"the map's no-go fan and the wind panel's wedge are one: the true wind ± the no-go angle, as wide, turned alike, edged where she'd be in irons ({n - wrong} of {n}){first}"
                    + $"; heading NE with the wind from the SE the caravel's spans {Bearing(cNE - wNE / 2):0}°–{Bearing(cNE + wNE / 2):0}°");
            }

            // the map: her plan turned by π/2 - yaw (GL's RotateZ on the map's x, z) puts her bow and starboard where they are
            bool map = true;
            for (int i = 0; i < 12; i++)
            {
                double yaw = i * Math.PI / 6, t = Math.PI / 2 - yaw;
                var b = HelmMath.Bow(yaw);
                var s = HelmMath.Starboard(yaw);
                map &= Math.Abs(Math.Cos(t) - b.X) < 1e-9 && Math.Abs(Math.Sin(t) - b.Z) < 1e-9
                       && Math.Abs(-Math.Sin(t) - s.X) < 1e-9 && Math.Abs(Math.Cos(t) - s.Z) < 1e-9;
            }
            Check(map, "the map draws her plan turned to her heading, starboard on her right");

            // her motion (the user: "with no movement direction indicator I could not tell" she was going astern): the
            // word and the course-over-ground arrow relative to her bow
            string m1 = HelmMath.Motion(2, 0.2, out double o1, out double c1);
            string m2 = HelmMath.Motion(-0.6, 0, out _, out double c2);
            string m3 = HelmMath.Motion(0.1, -0.5, out _, out double c3);
            string m4 = HelmMath.Motion(0.01, 0.02, out _, out _);
            Check(m1 == "ahead" && c1 > 0 && c1 < 0.2 && Math.Abs(o1 - Math.Sqrt(4.04)) < 1e-9 && m2 == "astern" && Math.Abs(Math.Abs(c2) - Math.PI) < 1e-9
                  && m3 == "sideways" && c3 < -Math.PI / 4 && c3 > -Math.PI / 2 && m4 == "stopped",
                $"her motion: ahead with a little leeway → {m1} (arrow {c1 * 180 / Math.PI:0}° to starboard), sweeps astern → {m2} (arrow {c2 * 180 / Math.PI:0}°), drifting to port → {m3} ({c3 * 180 / Math.PI:0}°), barely moving → {m4}");

            var plan = ShipPlan.Outline(def);
            bool ordered = plan != null && plan.Zip(plan.Skip(1), (a, b) => b.X > a.X).All(x => x);
            double length = plan == null ? 0 : plan[^1].X - plan[0].X;
            double beamW = plan == null ? 0 : plan.Max(s => s.Starboard - s.Port);
            Check(ordered && plan.Length <= 50 && length > def.Length - 2 && length < def.Length + 10 && Math.Abs(beamW - def.Beam) < 2.5 && plan.All(s => s.Starboard >= s.Port),
                $"her plan for the map: {plan?.Length} stations, {length:0.0} long over all (design {def.Length:0.0} on deck; the head and the bowsprit reach further), {beamW:0.0} broad (design {def.Beam:0.0})");
        }
    }
}
