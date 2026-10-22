using System;
using System.IO;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (22 October 2026, the user: "Items cannot be dropped and picked on ship - they fall through") Things dropped
        /// aboard each ship come to rest on her (DeckItemMover against her own voxels): dropped all over her main deck,
        /// tossed at her side, dropped over her side (that one leaves her). The same for every design: nothing is ship-made.
        /// </summary>
        static void DeckItems(string root)
        {
            Console.WriteLine("things dropped aboard");
            foreach (var code in new[] { "caravel", "carrack", "hansa-carrack" })
            {
                string path = Path.Combine(root, "assets", "hansa", "config", "ships", code + ".json");
                if (!File.Exists(path)) { Console.WriteLine($"  ({code}.json not there)"); continue; }
                var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
                def.Init();
                var g = def.Grid;
                int dropped = 0, resting = 0, low = 0;
                string worst = null;
                // over her main deck, from a man's height: amidships out to a third of her length each way, across her waist
                for (double x = -def.Length / 3; x <= def.Length / 3; x += 1.0)
                    for (double z = -def.Beam / 4; z <= def.Beam / 4 + 1e-9; z += def.Beam / 4)
                    {
                        var m = new DeckItemMover(x, def.DeckY + 1.5, z);
                        if (m.Stuck(g) || !m.Over(g)) continue;
                        dropped++;
                        bool kept = true;
                        for (int i = 0; i < 60 && kept; i++) kept = m.Step(g, 0.05);
                        bool rest = kept && m.OnGround && m.VX == 0 && m.VZ == 0 && !m.Stuck(g) &&
                            g.Collides(m.X - 0.2, m.Y - 0.05, m.Z - 0.2, m.X + 0.2, m.Y, m.Z + 0.2);
                        if (rest) resting++;
                        else worst ??= $"{x:0.0} {z:0.0}: {(kept ? $"at y {m.Y:0.00}, moving" : "left her")}";
                        if (rest && m.Y < def.DeckY - 0.5) low++;
                    }
                Check(dropped > 20 && resting == dropped,
                    $"{code}: things dropped over her main deck come to rest on her ({resting} of {dropped}; {low} through a hatch to a deck below){(worst != null ? ", not " + worst : "")}");

                // tossed hard toward her side from amidships: what's in its way (her bulwark at most) stops it
                var t = new DeckItemMover(-1.5, def.DeckY + 0.2, 0.5) { VZ = 6, VY = 2 };
                bool tk = true;
                for (int i = 0; i < 60 && tk; i++) tk = t.Step(g, 0.05);
                Check(tk && t.OnGround && t.Z < def.Beam / 2, $"{code}: tossed hard toward her side, it stays aboard (comes to rest at z {t.Z:0.00}, her half-beam {def.Beam / 2:0.00})");

                // dropped over her side: nothing of her under it, it isn't hers
                var o = new DeckItemMover(-1.5, def.DeckY + 1.5, def.Beam / 2 + 1);
                Check(!o.Over(g), $"{code}: one dropped over her side isn't hers");
            }
        }
    }
}
