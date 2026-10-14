using System;

namespace Hansa
{
    /// <summary>
    /// (0.3.25, the user: "slowmo movement of walking speed with sails down, including turning … being at wheel") Crawling
    /// her under the sweeps, in a harbour or off a shore, as pure functions (the server's helm, the offline tests). At the
    /// wheel with her sails furled, Ctrl (the sprint key) puts her under the sweeps (or S, as the sweeps astern did till
    /// 0.3.24); then holding W creeps her ahead, holding S astern, at about a walk, picking up and losing way in about two
    /// seconds, stopping when let go; A/D turn her directly about her middle, even standing still. No wind, no leeway, no
    /// sails. Setting sail (Ctrl again, then W) or dropping the anchor ends it. Boats keep their oars.
    /// </summary>
    public static class Crawl
    {
        /// <summary>The server's defaults (HansaConfig CrawlSpeed, CrawlAsternSpeed, CrawlTurn, CrawlRampSeconds).</summary>
        public const float DefaultSpeed = 1.5f, DefaultAsternSpeed = 1.0f, DefaultTurn = 6f, DefaultRampSeconds = 2f;

        /// <summary>The way she's driven to (blocks/s, + ahead): ahead +1 (W held), -1 (S held), 0 (neither, or both).</summary>
        public static float Target(int ahead, float speedAhead, float speedAstern) =>
            ahead > 0 ? Math.Max(0, speedAhead) : ahead < 0 ? -Math.Max(0, speedAstern) : 0f;

        /// <summary>
        /// Her way after dt seconds going toward target: up to the crawl's speed (or down from it) in rampSeconds; faster
        /// than a crawl (still coasting from under sail) she loses it at least as fast as she would anyway (decel).
        /// </summary>
        public static float StepSpeed(float speed, float target, float crawlSpeed, float rampSeconds, float decel, float dt)
        {
            float rate = Math.Max(0.05f, crawlSpeed) / Math.Max(0.1f, rampSeconds);
            if (Math.Abs(speed) > crawlSpeed + 0.05f) rate = Math.Max(rate, decel);
            return speed + Math.Clamp(target - speed, -rate * dt, rate * dt);
        }

        /// <summary>
        /// The turn the helm asks for (radians a second, + = the yaw growing = her bow swinging to port, as
        /// <see cref="HelmMath.YawRate"/>): D (turn +1) swings her bow to starboard, A to port, at turnDeg degrees a second.
        /// </summary>
        public static float YawTarget(float turn, float turnDeg) => (float)(-Math.Clamp(turn, -1f, 1f) * turnDeg * Math.PI / 180);

        /// <summary>Her turn after dt seconds going toward target: the whole rate gained or lost in easeSeconds.</summary>
        public static float StepYaw(float yawRate, float target, float turnDeg, float dt, float easeSeconds = 0.5f)
        {
            float rate = (float)(Math.Max(0.1, turnDeg) * Math.PI / 180) / Math.Max(0.05f, easeSeconds);
            return yawRate + Math.Clamp(target - yawRate, -rate * dt, rate * dt);
        }

        /// <summary>
        /// Does she stay under the sweeps: only with her sails furled, her anchor up, afloat, and a ship (not a boat with
        /// oars).
        /// </summary>
        public static bool Holds(int sailSet, bool anchored, bool goneDown, float rowSpeed) => sailSet == 0 && !anchored && !goneDown && rowSpeed <= 0;
    }
}
