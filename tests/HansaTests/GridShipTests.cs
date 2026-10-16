using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Hansa.Construction;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// Which of carrack-m's pieces (cabin walls, doors, windows, railings, panels, the galley) the grid carrack can take:
        /// each tried with the piece planner on her places in the server's order after her own ladders and hatches,
        /// shifted with her rig. Prints the ones laid as JSON for her design's "game.pieces".
        ///   dotnet run --project tests/HansaTests -- grid-pieces
        /// </summary>
        static void GridPieces(string root)
        {
            string A(string p) => File.ReadAllText(Path.Combine(root, "assets", "hansa", p));
            var kinds = BlockKinds.Read(A("config/construction/kinds.json"));
            var designJson = Newtonsoft.Json.Linq.JObject.Parse(A("config/shipdesigns/carrack.json"));
            designJson["game"]["pieces"] = new Newtonsoft.Json.Linq.JArray();
            var design = ShipDesign.Read(designJson.ToString());
            var def = GridShips.Build(design, kinds, A("config/ships/carrack-m.json"), null).Def;
            var game = design.Game.ToObject<GridShips.GameSpec>();
            var donor = Newtonsoft.Json.JsonConvert.DeserializeObject<ShipDefinition>(A("config/ships/carrack-m.json"));
            var l = def.Mounts;
            var set = new ShipPieceSet();
            static int Order(ModularPieceDef d) => d?.Kind switch { "stair" or "ladder" => 0, "hearth" or "oven" => 1, "flue" => 2, _ => 3 };
            // hers first
            foreach (var e in def.ModularPieces.Where(e => e.Kind == "ladder"))
            {
                var m = l.ForModular(e);
                var plan = m == null ? null : PiecePlanner.Plan(l, m, ModularFake(e, l.LaidKind(e)), ModularToward(e, l.LaidKind(e), m));
                if (plan?.Error == null && plan != null) set.Add(ShipPiece.From(plan, set.NextId, null));
            }
            var laid = new List<ModularPieceDef>();
            var refused = new Dictionary<string, int>();
            foreach (var e0 in donor.ModularPieces.Where(e => e.Kind is not ("stair" or "ladder" or "hatch")).OrderBy(Order))
            {
                var e = Newtonsoft.Json.JsonConvert.DeserializeObject<ModularPieceDef>(Newtonsoft.Json.JsonConvert.SerializeObject(e0));
                if (e.At != null) e.At[0] = GridShips.Warp(game, e.At[0]);
                var m = l.ForModular(e);
                string why = null;
                PiecePlan plan = null;
                if (m == null) why = "no place";
                else
                {
                    string kind = l.LaidKind(e);
                    plan = PiecePlanner.Plan(l, m, ModularFake(e, kind), ModularToward(e, kind, m));
                    if (plan.Error != null) why = plan.Error;
                    else if (plan.Mounts.Any(set.Taken) || set.Blocking(plan) != null) why = "in another's way";
                    else if (plan.Role == PieceRole.Railing && (PiecePlanner.BesideLadder(l, set.All, m) != null || PiecePlanner.BesideRailed(l, set.All, m) != null)) why = "beside a stair";
                    else if (plan.Role is PieceRole.Ladder or PieceRole.Stair or PieceRole.Hearth or PieceRole.Oven)
                    {
                        var pg = def.Grid.Clone();
                        foreach (var q in set.All) if (!q.WallLike && q.Role != PieceRole.Door) foreach (var b in q.Solids) pg.Fill(b);
                        if (PiecePlanner.GridClash(l, plan, pg) != null) why = "her collision";
                    }
                }
                if (why != null) { string k = $"{e.Kind}: {why}"; refused[k] = refused.TryGetValue(k, out int n) ? n + 1 : 1; continue; }
                set.Add(ShipPiece.From(plan, set.NextId, null));
                laid.Add(e);
            }
            Console.WriteLine($"laid {laid.Count} of carrack-m's pieces: {string.Join(", ", laid.GroupBy(e => e.Kind).Select(g => $"{g.Count()} {g.Key}"))}");
            foreach (var kv in refused.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  refused {kv.Value}: {kv.Key}");
            var settings = new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore, DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() };
            File.WriteAllText(Path.Combine(root, "testserver-grid-pieces.json"), Newtonsoft.Json.JsonConvert.SerializeObject(laid, Newtonsoft.Json.Formatting.None, settings));
        }

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
            Check(def.Fixtures.All(f => f.Type != "gate" && !(f.Code ?? "").StartsWith("gun-port-") && !(f.Code ?? "").StartsWith("gun-starboard-")) && Math.Abs(def.Fixture("pump").Pos[0] - 4.5) < 1e-9,
                $"the donor's guns and entry gates left out; the pump beside the main mast moved with it ({def.Fixture("pump").Pos[0]})");
            Check(def.Grid.SolidAt(3.5, 10, 0) && !def.Grid.SolidAt(2.5, 10, 0), "the main mast's column of collision is at its new place");
            // nothing solid of the donor's beyond her bow (its beakhead, which she hasn't), and her name board on her own transom
            {
                int beak = 0;
                for (double x = 21.6; x < 27; x += 0.25) for (double y = 2; y < 7; y += 0.25) for (double z = -2.5; z < 2.5; z += 0.25) if (def.Grid.SolidAt(x, y, z)) beak++;
                var nb = def.Fixture("nameboard");
                Check(beak == 0 && nb != null && def.Grid.SolidAt(nb.Pos[0] + 0.3, nb.Pos[1], 0) && !def.Grid.SolidAt(nb.Pos[0] - 0.3, nb.Pos[1], 0),
                    $"no old beakhead beyond her bow ({beak} voxels); her name board against her transom (at x {nb?.Pos[0]})");
            }
            // her rooms, from her decks: each from its floor's top up to the deck over it, the castles' along their decks
            {
                var rr = def.Rooms.ToDictionary(r => r.Code);
                bool Is(string c, double x0, double y0, double x1, double y1) => rr.TryGetValue(c, out var r) && r.Enclosed &&
                    Math.Abs(r.Min[0] - x0) < 1e-6 && Math.Abs(r.Min[1] - y0) < 1e-6 && Math.Abs(r.Max[0] - x1) < 1e-6 && Math.Abs(r.Max[1] - y1) < 1e-6;
                Check(def.Rooms.Length == 5 && Is("hold", -18, -3.875, 21.5, -1.125) && Is("lowerdeck", -18, -0.875, 21.5, 1.875) && Is("greatcabin", -17.5, 2.125, -2, 4.875)
                      && Is("poopcabin", -17.5, 5.125, -9, 7.875) && Is("forecastle", 13, 2.125, 21, 4.875) && def.RoomAt(17.5, 2.6, 2)?.Code == "forecastle",
                    $"her rooms from her decks: {string.Join("; ", def.Rooms.Select(r => $"{r.Code} x {r.Min[0]}..{r.Max[0]} y {r.Min[1]}..{r.Max[1]}"))}");
            }
            // places kept clear of pieces: the rig's, her 8 guns', her hatchways', her windows'
            Check(def.KeepClear != null && def.KeepClear.Count(k => k.What == "gun") == 8 && def.KeepClear.Count(k => k.What == "hatch") == 2 &&
                  def.KeepClear.Any(k => k.What == "plaque") && def.KeepClear.Any(k => k.What == "pump" && k.Box[0] > 3) && def.KeepClear.Count(k => k.What == "window") == 2,
                $"kept clear of pieces: {string.Join(", ", def.KeepClear?.GroupBy(k => k.What).Select(g => g.Count() + " " + g.Key) ?? Array.Empty<string>())}");
            // her hull samples: her own, just inside her planking (in her collision), from her stern to her bow and her bottom up her castles
            {
                var hs = def.HullSamples;
                int inSolid = hs.Count(p => def.Grid.SolidAt(p[0], p[1], p[2]));
                double sx0 = hs.Min(p => p[0]), sx1 = hs.Max(p => p[0]), sy0 = hs.Min(p => p[1]), sy1 = hs.Max(p => p[1]);
                Check(hs.Length > 300 && inSolid >= hs.Length * 0.9 && sx0 < -17 && sx1 > 20.5 && sx1 < 21 && Math.Abs(sy0 - (def.KeelY + 0.02)) < 1e-6 && sy1 > 7
                      && hs.Any(p => Math.Abs(p[1] - 0.25) < 1e-6 && p[2] > 2),
                    $"{hs.Length} hull samples, {inSolid} of them in her planking, x {sx0:0.0}..{sx1:0.0}, y {sy0:0.00}..{sy1:0.00}");
            }

            // her floors, every deck
            var l = def.Mounts;
            foreach (var deck in new[] { "hold", "lower", "main", "quarter", "forecastle", "poop" })
                Check(l.Floors.Count(m => m.Deck == deck) > 10, $"floor places on her {deck} deck ({l.Floors.Count(m => m.Deck == deck)})");

            // her ladder-stairs, laid as the server lays a new ship's pieces
            var set = new ShipPieceSet();
            int laid = 0;
            foreach (var e in def.ModularPieces.Where(e => e.Kind == "ladder"))
            {
                var m = l.ForModular(e);
                string kind = m == null ? null : l.LaidKind(e);
                var plan = m == null ? null : PiecePlanner.Plan(l, m, ModularFake(e, kind), ModularToward(e, kind, m));
                var clash = plan?.Error == null && plan != null ? PiecePlanner.GridClash(l, plan, def.Grid) : null;
                bool ok = plan != null && plan.Error == null && plan.Role == PieceRole.Ladder && clash == null;
                if (ok) { set.Add(ShipPiece.From(plan, set.NextId, null)); laid++; }
                else Console.WriteLine($"         {e.Key}: place {(m == null ? "none" : m.Id)}, kind {kind}, plan {plan?.Error ?? plan?.Role.ToString()}, clash {(clash == null ? "-" : string.Join(",", clash.Select(c => c.ToString("0.##"))))}");
            }
            Check(laid == def.ModularPieces.Count(e => e.Kind == "ladder") && laid == 5, $"all her {laid} ladder-stairs laid by the piece system");
            // her design's own pieces (the galley, her castles' doors), after her ladders, as the server lays them
            var own = def.ModularPieces.Where(e => e.Kind is not ("ladder" or "hatch")).OrderBy(e => e.Kind is "hearth" or "oven" ? 0 : 1).ToList();
            var ownLaid = new List<string>();
            foreach (var e in own)
            {
                var m = l.ForModular(e);
                string kind = m == null ? null : l.LaidKind(e);
                var plan = m == null ? null : PiecePlanner.Plan(l, m, ModularFake(e, kind), ModularToward(e, kind, m));
                bool ok = plan != null && plan.Error == null && !plan.Mounts.Any(set.Taken) && set.Blocking(plan) == null;
                if (ok) { set.Add(ShipPiece.From(plan, set.NextId, null)); ownLaid.Add(e.Key); }
                else Console.WriteLine($"         {e.Key}: {(m == null ? "no place" : plan?.Error ?? "in another's way")}");
            }
            // her scuttles over her ladders' wells, after her ladders, as the server lays them
            var scuttleWhy = new List<string>();
            foreach (var e in def.ModularPieces.Where(e => e.Kind == "hatch" && e.Code.StartsWith("scuttle@")))
            {
                var cells = def.Slots.Rect(e.At[1], e.At[0] - e.Size[0] / 2.0, e.At[2] - e.Size[1] / 2.0, e.At[0] + e.Size[0] / 2.0, e.At[2] + e.Size[1] / 2.0);
                var hp = cells == null ? null : HatchPlanner.Plan(def.Slots, cells, false, e.Hinge);
                string why = cells == null ? "no cells" : hp.Error ?? (hp.Mounts.Any(set.Taken) ? "taken" : set.Blocking(hp)?.Role.ToString());
                if (why != null) scuttleWhy.Add(e.Code + ": " + why);
            }
            Check(scuttleWhy.Count == 0, $"a scuttle shuts over each ladder's way up through a weather deck{(scuttleWhy.Count > 0 ? ": " + string.Join("; ", scuttleWhy) : "")}");
            Check(own.Count == 5 && ownLaid.Count == own.Count, $"her galley and her castles' doors laid ({ownLaid.Count} of {own.Count})");
            var doors = set.All.Where(p => p.Role == PieceRole.Door).ToList();
            // her castles' doorways: open the whole cell (two blocks high, a block wide) in her collision, each door in its
            // bulkhead's plane, filling it
            {
                var why = new List<string>();
                foreach (var d in doors)
                {
                    var b = d.Solids.Count > 0 ? PiecePlanner.Union(d.Solids) : null;
                    if (b == null) { why.Add(d.Mount + ": no solids"); continue; }
                    double x = (b[0] + b[3]) / 2;
                    int open = 0, frame = 0;
                    for (double y = b[1] + 0.125; y < b[1] + 2; y += 0.25)
                        for (double z = -0.375; z < 0.5; z += 0.25)
                            if (!def.Grid.SolidAt(x, y, z)) open++;
                    for (double y = b[1] + 0.125; y < b[1] + 2; y += 0.25)
                        if (def.Grid.SolidAt(x, y, -0.625) && def.Grid.SolidAt(x, y, 0.625)) frame++;
                    if (open != 32 || frame != 8) why.Add($"{d.Mount} at x {x:0.###}: {open} of 32 voxels of the doorway open, bulkhead beside it {frame} of 8");
                }
                Check(doors.Count == 3 && why.Count == 0, $"her castles' doorways open the whole cell, each door in its bulkhead{(why.Count > 0 ? ": " + string.Join("; ", why) : "")}");
            }
            Check(doors.Count == 3, $"her three castle doors laid ({doors.Count})");

            // standing on each deck
            foreach (var (deck, x, z) in new[] { ("hold", 0.5, 0.5), ("lower", 0.5, 0.5), ("main", 0.5, 0.5), ("quarter", -7.5, -2.5), ("forecastle", 15.5, -1.5), ("poop", -14.5, -1.5) })
            {
                double y = def.Decks.First(d => d.Code == deck).Y;
                var mv = new DeckMover(x, y + 0.3, z) { OnGround = false };
                for (int k = 0; k < 40; k++) mv.Step(def.Grid, 0.05, 0, 0, 3.0, false, false);
                Check(Math.Abs(mv.Y - y) < 0.05, $"a player dropped onto her {deck} deck stands on it (y {mv.Y:0.###}, deck {y})");
            }

            // her slots (docs/legacy/slots.md): worked out without problems, their meshes' owners and facings in range
            Check(def.Hull != null && def.Hull.Problems.Count == 0 && def.Hull.Count > 200,
                $"her plain planking as {def.Hull?.Count ?? 0} hull panels, no problems{(def.Hull?.Problems.Count > 0 ? ": " + string.Join("; ", def.Hull.Problems.Take(4)) : "")}");
            Check(def.Slots != null && def.Slots.Problems.Count == 0 && def.Slots.Count > 300,
                $"her plain deck planking as {def.Slots?.Count ?? 0} deck cells ({def.DeckSlots.Count(d => d.StartsEmpty)} starting out: wells and hatchways), no problems{(def.Slots?.Problems.Count > 0 ? ": " + string.Join("; ", def.Slots.Problems.Take(4)) : "")}");
            var hullPlan = new SlotMeshPlan(def.Hull, built.Skin, SlotMeshPlan.HullGroups);
            var deckPlan = new SlotMeshPlan(def.Slots, built.Skin);
            Check(hullPlan.Problems.Count == 0 && deckPlan.Problems.Count == 0 && hullPlan.ChunkQuads.Count > 0 && deckPlan.ChunkQuads.Count > 0,
                $"her slot meshes: every quad's owner and facing slot in range ({hullPlan.ChunkQuads.Values.Sum(q => q.Count)} hull quads, {deckPlan.ChunkQuads.Values.Sum(q => q.Count)} deck quads in chunks)");
            // her water clip: from her own hull's exact widths, round her at the waterline
            {
                var wcl = def.WaterClip;
                var o = wcl?.DryOutline;
                Check(o != null && o.NX * o.NY == o.HalfWidths.Length && wcl.DryProfile != null && Math.Abs(o.At(0, 0.25) - 6) < 0.01 && o.At(-30, 0) == 0
                      && wcl.CenterX - wcl.HalfLength < -14 && wcl.CenterX + wcl.HalfLength > 21,
                    $"her water clip from her hull's exact widths: {o?.NX}x{o?.NY}, half-width at midships on the waterline {o?.At(0, 0.25):0.00}, x {wcl?.CenterX - wcl?.HalfLength:0.0}..{wcl?.CenterX + wcl?.HalfLength:0.0}");
            }
            // her paint zones: every one on her mesh, with an area
            {
                var paint = PaintPlan.Build(def, built.Skin, null);
                var areas = paint.Zones.Select((z, i) => $"{z.Code} {paint.Area[i]:0}").ToList();
                Check(paint.Zones.Length >= 8 && paint.Area.All(a => a > 1) && paint.Zone("rail-caps")?.Kind == "moulding"
                      && paint.Area[paint.ZoneIndex("bottom")] > 200 && paint.Area[paint.ZoneIndex("topsides")] > 50,
                    $"her {paint.Zones.Length} paint zones on her mesh, m²: {string.Join(", ", areas)}");
            }
            var thin = def.Hull.Slots.Where(h => !(h.Area > 0.2 && h.Outer != null && h.Head > h.Sill)).ToList();
            foreach (var h in thin.Take(8)) Console.WriteLine($"         {h.Id}: area {h.Area:0.###}, sill {h.Sill:0.###}, head {h.Head:0.###}, normal [{string.Join(", ", h.Normal.Select(v => v.ToString("0.##")))}]");
            Check(def.DamageParts != null && thin.Count == 0,
                $"every panel has an outside of its own (its area, corners, sill and head) for its damage and leaks ({thin.Count} without)");
            var hatches = def.ModularPieces.Where(e => e.Kind == "hatch" && e.Code.StartsWith("hatch@")).ToList();
            var scuttles = def.ModularPieces.Where(e => e.Kind == "hatch" && e.Code.StartsWith("scuttle@")).ToList();
            Check(scuttles.Count == 4 && scuttles.All(e => e.Size[0] * e.Size[1] == 2 && def.Wells.Any(w => Math.Abs((w.Box[0] + w.Box[3]) / 2 - e.At[0]) < 1e-6 && Math.Abs(w.Box[4] - e.At[1]) < 1e-6)),
                $"a scuttle over each of her {scuttles.Count} ladder wells up through a weather deck (not the hold's, under her lower deck)");
            Check(hatches.Count == 2 && hatches.All(e => def.Slots.Rect(e.At[1], e.At[0] - e.Size[0] / 2.0, e.At[2] - e.Size[1] / 2.0, e.At[0] + e.Size[0] / 2.0, e.At[2] + e.Size[1] / 2.0) is List<int> cells && cells.Count == 6 && cells.All(c => def.Slots.Slots[c].StartsEmpty)),
                "her two hatchways (2 x 3) are open deck cells, each under a hatch piece");

            // her guns: copies of carrack-m's waist gun at her ports
            var guns = def.Fixtures.Where(f => f.Type == "gun").ToList();
            var placeCells = def.GunPlaces.Select(gp => GunPlaces.Cells(def, gp).Count).ToList();
            Check(guns.Count == 8 && def.GunPlaces.Length == 8 && placeCells.All(n => n >= 2) && guns.All(g => def.GunPlaceOf.ContainsKey(g.Code)) &&
                  guns.All(g => def.GunOfPart.Values.Count(v => v == g.Code) >= 8),
                $"her 8 guns, each with its parts and its place's deck cells ({string.Join(" ", placeCells)})");
            var gunTrouble = new List<string>();
            foreach (var g in guns)
            {
                var b = g.Box;
                if (def.Grid.Collides(b[0] + 0.05, b[1] + 0.05, b[2] + 0.05, b[3] - 0.05, b[4] - 0.05, b[5] - 0.05))
                {
                    var hit = new List<string>();
                    for (double vx = b[0] + 0.125; vx < b[3]; vx += 0.25) for (double vy = b[1] + 0.125; vy < b[4]; vy += 0.25) for (double vz = b[2] + 0.125; vz < b[5]; vz += 0.25)
                        if (def.Grid.SolidAt(vx, vy, vz)) hit.Add($"({vx:0.###},{vy:0.###},{vz:0.###})");
                    gunTrouble.Add(g.Code + $": carriage [{string.Join(", ", b.Select(v => v.ToString("0.###")))}] in her collision at {string.Join(" ", hit.Take(4))}");
                }
                double px = (g.Port[0] + g.Port[2]) / 2, py = (g.Port[1] + g.Port[3]) / 2, pz = g.Muzzle[2] - Math.Sign(g.Muzzle[2]) * 0.6;
                if (def.Grid.SolidAt(px, py, pz)) gunTrouble.Add(g.Code + $": her side shut at its port ({px:0.##}, {py:0.##}, {pz:0.##})");
                var outerSec = design.Form.Stations.Length > 0 ? HullForm.HalfWidth(built.Ship.Lattice.Form.SectionAt(px), py + design.Waterline) : 0;
                if (Math.Abs(g.Muzzle[2]) < outerSec - 0.3) gunTrouble.Add(g.Code + $": muzzle inside her ({g.Muzzle[2]:0.##} vs side {outerSec:0.##})");
            }
            Check(gunTrouble.Count == 0, "every gun's carriage clear of her, its port open, its muzzle at her side" + (gunTrouble.Count > 0 ? ": " + string.Join("; ", gunTrouble.Take(6)) : ""));

            // her mesh: her hull's quads inside their textures, the donor's planking gone
            var skin = built.Skin;
            int hullTex = Array.IndexOf(skin.Textures, "game:block/wood/planks/oak1");
            var all = skin.Groups.Where(kv => !kv.Key.StartsWith("part:") && !kv.Key.StartsWith("sail")).SelectMany(kv => kv.Value).ToList();
            bool inside = all.All(q => Enumerable.Range(0, 4).All(k => q[4 + 8 * k] >= -1e-3 && q[4 + 8 * k] <= 16 + 1e-3 && q[5 + 8 * k] >= -1e-3 && q[5 + 8 * k] <= 16 + 1e-3));
            Check(inside && hullTex >= 0 && skin.Groups["exterior"].Length > 20000 && skin.Groups["interior"].Length > 1000 && !skin.Groups.Keys.Any(g => g.StartsWith("part:gun-port-") || g.StartsWith("part:gun-starboard-")),
                $"her mesh: {skin.Groups["exterior"].Length} exterior and {skin.Groups["interior"].Length} interior quads beside her slots', every uv within its texture; the donor's gun parts left out");
        }
    }
}
