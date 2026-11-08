using System;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (8 November 2026) Where her hull's rail is along her, against the dry cut's top (DeckY + 1): the exact outline's
        /// top and the voxels' top at her sides (|z| over 0.5, leaving out masts on the centreline).
        ///   dotnet run --project tests/HansaTests -c Release -- drytop [code]
        /// </summary>
        static void DryTopProbe(string root, string code)
        {
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "ships", "config", "ships", code + ".json")));
            def.Init();
            var wc = def.WaterClip;
            var o = wc.DryOutline;
            var g = def.Grid;
            Console.WriteLine($"{code}: DeckY {def.DeckY} KeelY {def.KeelY} waterline (WaterClip.Height) {wc.Height} DryTop {wc.DryTop} (Sea's DryHeight sent: {def.DeckY + 1.0f})");
            Console.WriteLine($"  decks: {string.Join(", ", def.Decks.Select(d => d.Code + " y " + d.Y))}");
            if (o != null) Console.WriteLine($"  exact outline: x {o.X0}..{o.X0 + (o.NX - 1) * o.DX} step {o.DX}, y {o.Y0}..{o.Y0 + (o.NY - 1) * o.DY} step {o.DY}");
            Console.WriteLine($"  dry rows at y {wc.DryRowY0} + j * {wc.DryRowDY}; stations {wc.CenterX - wc.HalfLength:0.00}..{wc.CenterX + wc.HalfLength:0.00}");
            for (double x = Math.Floor(g.OX); x <= g.MaxX; x += 1)
            {
                double exactTop = double.NaN;
                if (o?.HalfWidths != null)
                    for (int r = o.NY - 1; r >= 0; r--) if (o.At(x, o.Y0 + r * o.DY) > 0.05) { exactTop = o.Y0 + r * o.DY; break; }
                double voxTop = double.NaN;
                int i = g.CellX(x);
                if (i >= 0 && i < g.NX)
                    for (int j = g.NY - 1; j >= 0 && double.IsNaN(voxTop); j--)
                        for (int k = 0; k < g.NZ; k++)
                        {
                            double z = Math.Abs(g.OZ + (k + 0.5) * g.V);
                            if (z > 0.5 && g.Solid(i, j, k)) { voxTop = g.OY + (j + 1) * g.V; break; }
                        }
                Console.WriteLine($"  x {x,6:0.0}: outline top {exactTop,6:0.00}  side voxels top {voxTop,6:0.00}");
            }
        }
    }
}
