using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// (0.3.19, the charter's double launch) Server: one launch per use of a charter or a boat item, per player. A use is
    /// known by when it began (the engine's <c>EntityControls.UsingBeginMS</c>): the engine's second stop for the same use
    /// is refused, and so is any launch within <see cref="CooldownMs"/> of the last one by the same player (a lagging
    /// client's repeated packets, a double click). The spot is guarded by <see cref="HansaMod.OverlapsShip"/>.
    /// </summary>
    public static class LaunchGuard
    {
        /// <summary>How long after a launch the server refuses another by the same player (ms).</summary>
        public const long CooldownMs = 2000;

        static readonly Dictionary<string, (long Use, long At)> last = new();

        static string Key(EntityAgent e) => e is EntityPlayer ep && ep.PlayerUID != null ? ep.PlayerUID : "e" + e.EntityId;

        /// <summary>
        /// May this use launch now? use: when it began (-1: no use known, a click). False with why (null: the same use's
        /// second stop, said nowhere) if not; true records it.
        /// </summary>
        public static bool TryLaunch(ICoreAPI api, EntityAgent e, long use, out string why) => TryLaunch(Key(e), use, api.World.ElapsedMilliseconds, out why);

        /// <summary>The rule itself (the offline tests drive it as the engine does).</summary>
        public static bool TryLaunch(string key, long use, long now, out string why)
        {
            why = null;
            if (last.TryGetValue(key, out var l))
            {
                if (use >= 0 && l.Use == use) return false;
                if (now - l.At < CooldownMs) { why = $"{now - l.At} ms after the last"; return false; }
            }
            last[key] = (use, now);
            return true;
        }

        /// <summary>Forget a key (the tests).</summary>
        public static void Reset(string key) => last.Remove(key);
    }
}
