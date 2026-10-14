using System;
using System.Linq;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// What drives her (0.3.10): the sails by the wind (their damage), the water in her, the wind on her hull and rig
        /// under bare poles (windage). The user's reports: "set sails and my speed drops to zero; I furl them and I get
        /// speed" (Beaufort 12 from the north, sails patched, foundering) and "without sails I am stranded".
        /// </summary>
        static void Drive(ShipDefinition def)
        {
            Console.WriteLine("what drives her: sails, damage, water, windage");
            var sd = def.Sailing;
            var l = def.DamageParts;
            var d = l.D;
            double max = def.MaxSpeed;
            // the wind from the north (where the air goes: +z, south), force 12 (1.5 units) and force 7
            double Target(double windZ, double yawDeg, double sail, double drive, double water)
            {
                double yaw = yawDeg * Math.PI / 180;
                double slow = ShipFlood.Sluggish(l, water);
                double t = Sailing.TargetSpeed(0, windZ, yaw, sail * drive, max * slow, sd);
                Sailing.Windage(0, windZ, yaw, sail * drive, sd, 1, out double ahead, out _);
                return t + ahead * slow;
            }
            // headings: the bow's way (an entity's yaw: 0 → +z, south; so the wind from the north is right astern at 0)
            var table = new System.Text.StringBuilder();
            bool setNeverWorse = true;
            string worse = "";
            foreach (double water in new[] { 0.0, 0.5 * l.Displacement, 0.9 * l.Displacement })
                foreach (double drive in new[] { 1.0, d.TornArea, d.ShreddedArea })
                    for (int h = 0; h < 360; h += 30)
                    {
                        double full = Target(1.5, h, 1, drive, water), half = Target(1.5, h, 0.55, drive, water), furled = Target(1.5, h, 0, drive, water);
                        double angle = Sailing.WindAngle(0, 1.5, h * Math.PI / 180, out _);
                        if (angle >= sd.NoGo && (full < furled - 1e-6 || half < furled - 1e-6)) { setNeverWorse = false; worse += $" {h}° water {water:0} drive {drive:0.00};"; }
                        if (water == 0 && drive == 1) table.Append($" {h}°: {full:0.0}/{half:0.0}/{furled:0.00}");
                    }
            Console.WriteLine("         force 12 from the north, sound, dry, full / half / furled (b/s) by the bow's yaw:" + table);
            Check(setNeverWorse, "with the wind abaft her no-go angle, sails set never give her less way than furled (sound, torn, shredded; dry, half and nine tenths full of water)" +
                (worse.Length > 0 ? ":" + worse : ""));

            // bare poles: force 7 (about 15 m/s): she drifts downwind at about 0.5-1 m/s, steers up to 40° off it, beam-on makes leeway
            double f7 = 15.5 / Sailing.WindUnit;
            double run = Target(f7, 0, 0, 1, 0), off30 = Target(f7, 30, 0, 1, 0), off50 = Target(f7, 50, 0, 1, 0);
            Sailing.Windage(0, f7, Math.PI / 2, 0, sd, 1, out double beamAhead, out double beamLee);
            Sailing.Windage(0, f7, 0, 0, sd, 0, out double offAhead, out double offLee);
            Check(run > 0.5 && run < 1.0 && off30 > 0.1 && off50 <= 1e-9 && Math.Abs(beamAhead) < 1e-9 && Math.Abs(beamLee) > 0.4 && offAhead == 0 && offLee == 0,
                $"under bare poles in force 7 ({f7 * Sailing.WindUnit:0.0} m/s) she drifts downwind at {run:0.00} b/s, {off30:0.00} steering 30° off it, none 50° off; " +
                $"beam-on {Math.Abs(beamLee):0.00} b/s of leeway; the config's Windage 0 none");

            // a shredded sail mended with 8 cloth alone (no spare sail aboard), a torn one with 4
            int sail = Array.FindIndex(l.Parts, p => p.Kind == DamageKind.Sail);
            var st = new DamageState(l.Count);
            st.Hurt[sail] = d.Shredded + 0.05f;
            var job = ShipRepairs.For(l, st, sail, "game:cloth-red");
            bool eight = job != null && ShipRepairs.Plan(job, new[] { ("game:cloth-red", 8) }, out _, out _) && !ShipRepairs.Plan(job, new[] { ("game:cloth-red", 7) }, out _, out _);
            st.Hurt[sail] = d.Torn + 0.05f;
            var tornJob = ShipRepairs.For(l, st, sail, "game:cloth-red");
            bool four = tornJob != null && ShipRepairs.Plan(tornJob, new[] { ("game:cloth-red", 4) }, out _, out _);
            Check(eight && four, "a shredded sail is mended with 8 cloth alone (7 aren't enough), a torn one with 4");
        }
    }
}
