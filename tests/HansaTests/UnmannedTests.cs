using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.1.0-dev.10) A ship with nobody aboard is stopped (<see cref="Unmanned"/>): what counts as under way, at once
        /// after a load and after the grace seconds otherwise.
        ///   dotnet run -c Debug -- unmanned
        /// </summary>
        static void UnmannedStops()
        {
            System.Console.WriteLine("0.1.0-dev.10: a ship with nobody aboard is stopped, at once when loaded from the save");
            // the caravel as the crash saved her: anchor up, full sail, the helm lashed (the rudder amidships), 8 b/s
            Check(Unmanned.UnderWay(false, 2, false, 8, 0, 0.3f, false) && Unmanned.UnderWay(false, 2, false, 8, 0, 0.3f, true),
                "under sail with her anchor up she's under way, loaded or not");
            Check(Unmanned.UnderWay(true, 1, false, 0, 0, 0, false) && Unmanned.UnderWay(false, 0, true, 0, 0, 0, false) &&
                  Unmanned.UnderWay(false, 0, false, 0, 0, 0, false),
                "a sail set at anchor, the sweeps, or only her anchor up: still to be stopped");
            // the grace seconds: an anchored, furled ship losing her way isn't stopped again; loaded, her way goes at once
            Check(!Unmanned.UnderWay(true, 0, false, 5, 0.01f, 0.2f, false), "anchored and furled, losing her way: nothing more to stop");
            Check(Unmanned.UnderWay(true, 0, false, 5, 0, 0, true) && Unmanned.UnderWay(true, 0, false, 0, 0.01f, 0, true) &&
                  Unmanned.UnderWay(true, 0, false, 0, 0, 0.2f, true),
                "loaded anchored and furled but with way, a turn or drift on her: stopped dead");
            Check(!Unmanned.UnderWay(true, 0, false, 0, 0, 0, true), "loaded lying at anchor, still: left as she is");
        }
    }
}
