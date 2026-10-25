namespace Hansa
{
    /// <summary>
    /// (0.1.0-dev.10, the user, 25 October 2026: back after a crash, in the water, and the caravel sailed on under the
    /// helm lashed before it) A ship with nobody aboard is stopped (config StopWhenUnmanned), as pure functions (the
    /// server, the offline tests):
    /// - under way and unmanned for UnmannedStopSeconds, she lets go her anchor and furls, and loses her way as at anchor;
    /// - the last of her people logging out stops her at once, not seconds later, so the save written as they leave
    ///   (a server's shutdown follows a single player's logout at once) has her at anchor;
    /// - loaded from the save with nobody aboard, she comes up anchored, furled and dead in the water. The game saves a
    ///   chunk when it unloads, or on a crash's way out, without saving the players' places and Hansa's logout records
    ///   (those wait for the next whole save): she may have been saved under the sail set before her people logged out,
    ///   and they come back where that earlier save had them.
    /// </summary>
    public static class Unmanned
    {
        /// <summary>
        /// Is there anything to stop: her anchor up, sail set, under the sweeps; loaded (from the save): or any way, turn
        /// or drift on her at all.
        /// </summary>
        public static bool UnderWay(bool anchored, int sailSet, bool crawl, float speed, float yawRate, float leeway, bool loaded) =>
            !anchored || sailSet != 0 || crawl || loaded && (speed != 0 || yawRate != 0 || leeway != 0);
    }
}
