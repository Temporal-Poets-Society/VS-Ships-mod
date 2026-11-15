using System;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (SH-0033, review M4) <see cref="CurrentPush"/>: a ship in a steady current against a wall, ticked as the server
        /// ticks her (the current's pose first, her own pose when that's blocked): one blow on the first contact, then none
        /// while she lies there; let go (the current turned away) and set on again, one more. Her own way into the wall still
        /// grounds her every tick it presses on (the 0.4 s gate on blows is the ship's, not this).
        /// </summary>
        static void CurrentPushAgainstShore()
        {
            Console.WriteLine("SH-0033: a beach's current pushing her against the shore");
            const double wall = 10, dt = 0.05;
            int Run(double current, double ownSpeed, int ticks, ref double x, ref bool held, out int grounded)
            {
                int blows = 0;
                grounded = 0;
                for (int i = 0; i < ticks; i++)
                {
                    double withX = x + (ownSpeed + current) * dt, withoutX = x + ownSpeed * dt;
                    var outcome = CurrentPush.Decide(current != 0, withX > wall, () => withoutX > wall, ref held, out bool blow);
                    if (blow) blows++;
                    if (outcome == CurrentPush.Outcome.Free) x = withX;
                    else if (outcome == CurrentPush.Outcome.Held) x = withoutX;
                    else grounded++;
                }
                return blows;
            }
            double x = 9;
            bool held = false;
            int first = Run(0.5, 0, 400, ref x, ref held, out int g1);
            Check(first == 1 && g1 == 0 && x <= wall && x > wall - 0.03,
                $"a 0.5 b/s current carries her onto the wall: one blow on contact, then none in 20 s ({first} blows, {g1} groundings), lying against it at {x:0.000}");
            int away = Run(-0.5, 0, 40, ref x, ref held, out _);
            int again = Run(0.5, 0, 400, ref x, ref held, out int g2);
            Check(away == 0 && again == 1 && g2 == 0, $"carried off and set on again: one more blow ({away}, {again})");
            x = 9.99;
            held = false;
            Run(0, 0.5, 40, ref x, ref held, out int own);
            Check(own == 40, $"her own way into the wall grounds her every tick it presses on ({own} of 40)");
            x = 9.99;
            held = false;
            int both = Run(0.5, 0.5, 40, ref x, ref held, out int ownAndCurrent);
            Check(both == 0 && ownAndCurrent == 40, $"her own way and the current both into it: aground as before ({ownAndCurrent} of 40, {both} current blows)");
        }
    }
}
