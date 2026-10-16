using System;
using System.IO;
using System.Linq;
using Hansa;
using Hansa.Construction;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// The grid carrack as the game builds her (GridShips): her design from the construction core, her rig from
        /// carrack-m. Her collision, her rig moved with her main mast, her ladder-stairs laid by the piece system,
        /// her decks walked, her mesh's uvs inside their textures.
        ///   dotnet run --project tests/HansaTests -- grid
        /// </summary>
        static void GridShip(string root)
        {
            Console.WriteLine("grid carrack (construction core + carrack-m's rig)");
            string A(string p) => File.ReadAllText(Path.Combine(root, "assets", "hansa", p));
            var kinds = BlockKinds.Read(A("config/construction/kinds.json"));
            var design = ShipDesign.Read(A("config/shipdesigns/carrack.json"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var built = GridShips.Build(design, kinds, A("config/ships/carrack-m.json"), A("config/shipmeshes/carrack-m.json"));
            var def = built.Def;
            Check(def.Code == "hansa-carrack" && def.Grid.CountSolid() > 20000, $"built in {watch.ElapsedMilliseconds} ms: {built.Ship.Present.Count()} blocks, {def.Grid.CountSolid()} solid voxels");
            var main = def.Masts.First(m => m.Code == "main");
            Check(Math.Abs(main.X - 3.5) < 1e-9 && def.Masts.First(m => m.Code == "fore").X == 17.5 && def.Masts.First(m => m.Code == "mizzen").X == -12.5,
                "her main mast moved a cell forward to x 3.5, between its ribs; fore and mizzen where they were");
            var course = def.Sails.First(s => s.Code == "main-course");
            Check(Math.Abs(course.Offset[0] - 1) < 1e-9 && def.Sails.First(s => s.Code == "fore-course").Offset[0] == 0, "the main sails moved with it, the fore course not");
            Check(def.Fixtures.All(f => f.Type != "gun" && f.Type != "gate") && Math.Abs(def.Fixture("pump").Pos[0] - 4.5) < 1e-9,
                $"the donor's guns and entry gates left out; the pump beside the main mast moved with it ({def.Fixture("pump").Pos[0]})");
            Check(def.Grid.SolidAt(3.5, 10, 0) && !def.Grid.SolidAt(2.5, 10, 0), "the main mast's column of collision is at its new place");

            // her floors, every deck
            var l = def.Mounts;
            foreach (var deck in new[] { "hold", "lower", "main", "quarter", "forecastle", "poop" })
                Check(l.Floors.Count(m => m.Deck == deck) > 10, $"floor places on her {deck} deck ({l.Floors.Count(m => m.Deck == deck)})");

            // her ladder-stairs, laid as the server lays a new ship's pieces
            var set = new ShipPieceSet();
            int laid = 0;
            foreach (var e in def.ModularPieces)
            {
                var m = l.ForModular(e);
                string kind = m == null ? null : l.LaidKind(e);
                var plan = m == null ? null : PiecePlanner.Plan(l, m, ModularFake(e, kind), ModularToward(e, kind, m));
                var clash = plan?.Error == null && plan != null ? PiecePlanner.GridClash(l, plan, def.Grid) : null;
                bool ok = plan != null && plan.Error == null && plan.Role == PieceRole.Ladder && clash == null;
                if (ok) { set.Add(ShipPiece.From(plan, set.NextId, null)); laid++; }
                else Console.WriteLine($"         {e.Key}: place {(m == null ? "none" : m.Id)}, kind {kind}, plan {plan?.Error ?? plan?.Role.ToString()}, clash {(clash == null ? "-" : string.Join(",", clash.Select(c => c.ToString("0.##"))))}");
            }
            Check(laid == def.ModularPieces.Length && laid == 5, $"all her {def.ModularPieces.Length} ladder-stairs laid by the piece system ({laid})");

            // standing on each deck
            foreach (var (deck, x, z) in new[] { ("hold", 0.5, 0.5), ("lower", 0.5, 0.5), ("main", 0.5, 0.5), ("quarter", -7.5, -2.5), ("forecastle", 15.5, -1.5), ("poop", -14.5, -1.5) })
            {
                double y = def.Decks.First(d => d.Code == deck).Y;
                var mv = new DeckMover(x, y + 0.3, z) { OnGround = false };
                for (int k = 0; k < 40; k++) mv.Step(def.Grid, 0.05, 0, 0, 3.0, false, false);
                Check(Math.Abs(mv.Y - y) < 0.05, $"a player dropped onto her {deck} deck stands on it (y {mv.Y:0.###}, deck {y})");
            }

            // her mesh: her hull's quads inside their textures, the donor's planking gone
            var skin = built.Skin;
            var ext = skin.Groups["exterior"];
            int hullTex = Array.IndexOf(skin.Textures, "game:block/wood/planks/oak1");
            bool inside = ext.All(q => Enumerable.Range(0, 4).All(k => q[4 + 8 * k] >= -1e-3 && q[4 + 8 * k] <= 16 + 1e-3 && q[5 + 8 * k] >= -1e-3 && q[5 + 8 * k] <= 16 + 1e-3));
            Check(inside && hullTex >= 0 && ext.Length > 30000 && !skin.Groups.ContainsKey("interior") && !skin.Groups.Keys.Any(g => g.StartsWith("part:gun-")),
                $"her mesh: {ext.Length} quads in the exterior, every uv within its texture; the donor's interior and gun parts left out");
        }
    }
}
