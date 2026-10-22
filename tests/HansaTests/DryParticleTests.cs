using System;
using System.IO;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (22 October 2026, the caravel in game: "in hull underwater still can see some water air bubbles") Every design's
        /// hold is dry where it's inside her hull: each empty cell under her main deck with her hull on both sides (across
        /// her) and under it is in her dry mask, which Sea and the particle patches (ShipDryParticles) ask.
        /// </summary>
        static void DryHold(string root, string code)
        {
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", code + ".json");
            if (!File.Exists(path)) { Check(false, $"{code}.json is there"); return; }
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            var g = def.FullGrid;
            var dry = def.DryMask;
            Check(dry != null, $"{code}: a mask of the inside of her hull");
            if (dry == null) return;
            int band = g.CellY(def.DeckY - 0.25);
            int inside = 0, wet = 0;
            string first = null;
            for (int i = 0; i < g.NX; i++)
                for (int k = 0; k < g.NZ; k++)
                    for (int j = 0; j < band; j++)
                    {
                        if (g.Solid(i, j, k)) continue;
                        bool below = false, left = false, right = false;
                        for (int b = j - 1; b >= 0 && !below; b--) below = g.Solid(i, b, k);
                        for (int c = k - 1; c >= 0 && !left; c--) left = g.Solid(i, j, c);
                        for (int c = k + 1; c < g.NZ && !right; c++) right = g.Solid(i, j, c);
                        if (!(below && left && right)) continue;
                        inside++;
                        double x = g.OX + (i + 0.5) * g.V, y = g.OY + (j + 0.5) * g.V, z = g.OZ + (k + 0.5) * g.V;
                        if (dry.SolidAt(x, y, z)) continue;
                        wet++;
                        first ??= FormattableString.Invariant($"x {x:0.00} y {y:0.00} z {z:0.00}");
                    }
            Check(inside > 0 && wet == 0, $"{code}: her hold is dry under her main deck ({wet} of {inside} cells inside her hull aren't{(first != null ? ", first at " + first : "")})");
        }
    }
}
