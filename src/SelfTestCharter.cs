using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// (31 October 2026) The caravel's charter is crafted from bundles that hold her bill of materials
        /// (docs/proposals/caravel-charter-bom.md): every recipe loads, every ingredient is a real item, and the charter's
        /// raw materials add up to the BOM.
        /// </summary>
        void CaravelCharterRecipes()
        {
            var recipes=sapi.World.GridRecipes.Where(r=>r.Output?.Code?.Domain=="ships" &&
                (r.Output.Code.Path.StartsWith("shipkit-") || r.Output.Code.Path=="shipdeed-caravel")).ToList();
            Check(recipes.Count==7,$"charter: the six bundle recipes and the caravel's charter recipe load ({recipes.Count})");
            var missing=new List<string>();
            foreach(var r in recipes)
                foreach(var ing in r.ResolvedIngredients.Where(i=>i!=null))
                {
                    bool found=ing.Code.Path.Contains("*")
                        ? (ing.Type==EnumItemClass.Block ? sapi.World.Blocks.Any(b=>b?.Code!=null && WildcardUtil.Match(ing.Code,b.Code))
                                                          : sapi.World.Items.Any(i=>i?.Code!=null && WildcardUtil.Match(ing.Code,i.Code)))
                        : ing.ResolvedItemStack!=null;
                    if(!found) missing.Add(r.Output.Code.Path+": "+ing.Code);
                }
            Check(missing.Count==0,"charter: every bundle and charter ingredient is a real item ("+(missing.Count==0?"all found":string.Join(", ",missing))+")");

            // what one charter takes in raw materials, through its bundles
            var byOutput=recipes.ToDictionary(r=>r.Output.Code.Path);
            var raw=new Dictionary<string,int>();
            void Add(string path,int times)
            {
                if(!byOutput.TryGetValue(path,out var r)) return;
                foreach(var ing in r.ResolvedIngredients.Where(i=>i!=null))
                {
                    if(ing.Code.Domain=="ships" && ing.Code.Path.StartsWith("shipkit-")) {Add(ing.Code.Path,times*ing.Quantity);continue;}
                    string key=ing.Code.Path.Split('-')[0]+(ing.Code.Path.StartsWith("ingot-")?"-iron":"");
                    raw[key]=raw.GetValueOrDefault(key)+times*ing.Quantity;
                }
            }
            Add("shipdeed-caravel",1);
            var want=new Dictionary<string,int> {
                ["plank"]=3584,["supportbeam"]=288,["debarkedlog"]=50,["linen"]=244,["rope"]=176,["gun"]=10,
                ["metalnailsandstrips"]=256,["ingot-iron"]=12,["resin"]=64,["flaxfibers"]=64
            };
            string got=string.Join(", ",raw.OrderBy(kv=>kv.Key).Select(kv=>kv.Key+" "+kv.Value));
            Check(raw.Count==want.Count && want.All(kv=>raw.GetValueOrDefault(kv.Key)==kv.Value),
                "charter: one caravel charter takes her bill of materials ("+got+")");
        }
    }
}
