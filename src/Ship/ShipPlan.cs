using System;
using System.Collections.Generic;

namespace Hansa
{
    /// <summary>
    /// A design's plan, her outline seen from above (the map's ships layer): for each station along her keel the
    /// furthest her collision voxels reach to port and to starboard, at any height (hull, castles, the head). Ship-local:
    /// x forward, z to starboard.
    /// </summary>
    public static class ShipPlan
    {
        public readonly struct Station
        {
            public readonly double X, Port, Starboard;
            public Station(double x, double port, double starboard) { X = x; Port = port; Starboard = starboard; }
        }

        /// <summary>Stations from stern to bow (at most maxStations, the ends pinched to her stem and stern), or null.</summary>
        public static Station[] Outline(ShipDefinition def, int maxStations = 48)
        {
            var g = def?.Grid;
            if (g == null) return null;
            var cols = new List<Station>();
            for (int i = 0; i < g.NX; i++)
            {
                int kMin = int.MaxValue, kMax = int.MinValue;
                for (int j = 0; j < g.NY; j++)
                    for (int k = 0; k < g.NZ; k++)
                        if (g.Solid(i, j, k)) { if (k < kMin) kMin = k; if (k > kMax) kMax = k; }
                if (kMax < kMin) continue;
                cols.Add(new Station(g.OX + (i + 0.5) * g.V, g.OZ + kMin * g.V, g.OZ + (kMax + 1) * g.V));
            }
            if (cols.Count < 2) return null;
            // fewer stations: the widest of each run, so the outline never shrinks
            int step = Math.Max(1, (int)Math.Ceiling(cols.Count / (double)maxStations));
            var st = new List<Station>();
            double x0 = cols[0].X - g.V / 2, x1 = cols[^1].X + g.V / 2;
            st.Add(new Station(x0, Mid(cols[0]), Mid(cols[0])));
            for (int a = 0; a < cols.Count; a += step)
            {
                double port = 0, stbd = 0, x = 0;
                int n = 0;
                for (int b = a; b < Math.Min(cols.Count, a + step); b++, n++)
                {
                    port = n == 0 ? cols[b].Port : Math.Min(port, cols[b].Port);
                    stbd = n == 0 ? cols[b].Starboard : Math.Max(stbd, cols[b].Starboard);
                    x += cols[b].X;
                }
                st.Add(new Station(x / n, port, stbd));
            }
            st.Add(new Station(x1, Mid(cols[^1]), Mid(cols[^1])));
            return st.ToArray();
        }

        static double Mid(Station s) => (s.Port + s.Starboard) / 2;
    }
}
