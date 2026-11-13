using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (13 November 2026, SH-0012, the author: the caravel craftable without cannons) Her charter's assets: the charter
        /// launches her unarmed, its recipe and no other takes or makes a battery; and her gun places, all empty as from her
        /// charter, each take a gun in turn (no two in each other's way). The launch, mounting by hand and firing run in the
        /// server self-test (SelfTestCharter.cs, HANSA_SELFTEST_ONLY=charter).
        /// </summary>
        static void CaravelCharterUnarmed(string root)
        {
            string assets = Path.Combine(root, "assets", "ships");
            var deed = JObject.Parse(File.ReadAllText(Path.Combine(assets, "itemtypes", "shipdeed.json")));
            var caravelAttrs = deed["attributesByType"]?["shipdeed-caravel"];
            Check((string)caravelAttrs?["ship"] == "caravel" && (bool?)caravelAttrs?["unarmed"] == true,
                "SH-0012: the caravel's charter launches her unarmed (attributes ship caravel, unarmed true)");
            var recipes = JArray.Parse(File.ReadAllText(Path.Combine(assets, "recipes", "grid", "shipkits.json")));
            var charter = recipes.FirstOrDefault(r => (string)r["output"]?["code"] == "ships:shipdeed-caravel");
            bool batteryAnywhere = recipes.Any(r => r.ToString(Formatting.None).Contains("shipkit-battery"));
            var codes = charter?["ingredients"]?.Children<JProperty>().Select(p => (string)p.Value["code"]).ToList();
            Check(charter != null && !batteryAnywhere && codes.Count == 7 && !codes.Any(c => c.Contains("gun")),
                $"SH-0012: her charter takes 5 bundles, resin and flax, no guns; no recipe makes or takes a battery ({string.Join(", ", codes ?? new())})");

            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(assets, "config", "ships", "caravel.json")));
            def.Init();
            var states = new GunPlaceStates(def);
            foreach (var p in def.GunPlaces) states.Set(p, false);
            int gone = states.Gone()?.Length ?? 0;
            var refused = def.GunPlaces.Select(p =>
            {
                string err = GunPlaces.MountProblem(def, p, states, null, null);
                if (err == null) states.Set(p, true);
                return (p.Id, err);
            }).Where(e => e.err != null).ToList();
            Check(def.GunPlaces.Length == 10 && gone == 10 && refused.Count == 0 && states.Gone() == null && states.Delta.Count == 0,
                $"SH-0012: her {def.GunPlaces.Length} gun places all empty ({gone} gone), then each takes a gun in turn " +
                $"({(refused.Count == 0 ? "none in another's way" : string.Join(", ", refused.Select(e => e.Id + ": " + e.err)))}); all mounted is her design again");
        }
    }
}
