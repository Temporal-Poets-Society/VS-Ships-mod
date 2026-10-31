using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    /// <summary>Held pump work, refreshed independently of the pump's stroke duration.</summary>
    public sealed class PumpActivity
    {
        public const int HeartbeatMs = 150, HoldMs = 450;
        readonly Dictionary<string, Dictionary<string, long>> pumps = new();

        public void Touch(string pump, string who, long now)
        {
            if (!pumps.TryGetValue(pump, out var hands)) pumps[pump] = hands = new();
            hands[who] = now;
        }

        public void Stop(string who)
        {
            foreach (var hands in pumps.Values) hands.Remove(who);
        }

        public (int Count, string Worked) Read(long now, int perPump)
        {
            int count = 0;
            var worked = new List<string>();
            foreach (var pair in pumps.ToArray())
            {
                foreach (var hand in pair.Value.ToArray())
                    if (now - hand.Value >= HoldMs) pair.Value.Remove(hand.Key);
                if (pair.Value.Count == 0) { pumps.Remove(pair.Key); continue; }
                count += Math.Min(pair.Value.Count, perPump);
                worked.Add(pair.Key);
            }
            return (count, string.Join(",", worked));
        }
    }
}
