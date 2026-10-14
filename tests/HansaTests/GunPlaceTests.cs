using System;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.21, step C; docs/slots.md §7) Gun places: carrack-m's guns as places (the generator's when she has them, else
        /// given by the test, with two deck cells under gun-port-3's trail made slots), the contract checks, which moving
        /// parts are a gun's, mounting and taking up round trip, refusals (a cell out, something in its room, hot, run out,
        /// manned), her collision and the crosshair without a gun, a laid gun's data as a fixed gun's, a wreck's guns.
        /// </summary>
        static void GunPlacesTest(string root)
        {
            string path = Path.Combine(root, "assets", "hansa", "config", "ships", "carrack-m.json");
            var jo = JObject.Parse(File.ReadAllText(path));
            // (her without gun places: fixed guns, as before)
            var plainJo = (JObject)jo.DeepClone();
            plainJo.Remove("gunPlaces");
            var plain = plainJo.ToObject<ShipDefinition>();
            plain.Init();
            bool generators = jo["gunPlaces"] is JArray ga && ga.Count > 0;
            if (!generators)
            {
                Console.WriteLine("gun places (0.3.21, step C) on a carrack-m given gun places by the test");
                var places = new JArray();
                // two whole cells under gun-port-3's trail (x 4..6, z -3.5..-2.5 on the main deck) made slots here
                var slots = (JArray)jo["deckSlots"];
                foreach (int i in new[] { 4, 5 })
                    if (!slots.Any(s => (string)s["id"] == $"main:{i}:-4"))
                        slots.Add(new JObject { ["id"] = $"main:{i}:-4", ["deck"] = "main", ["box"] = new JArray(i, 1.875, -3.5, i + 1, 2.125, -2.5) });
                // (each place lists the slots under its room: a cell two rooms share is listed by both)
                var withCells = jo.ToObject<ShipDefinition>();
                withCells.Init();
                foreach (var f in plain.Fixtures.Where(f => f.Type == "gun"))
                {
                    var gp = new JObject { ["id"] = f.Code, ["gun"] = f.Code };
                    var under = GunPlaces.RoomCells(withCells, withCells.Fixture(f.Code));
                    if (under.Count > 0) gp["cells"] = new JArray(under.Select(n => withCells.Slots.Slots[n].Id));
                    places.Add(gp);
                }
                jo["gunPlaces"] = places;
            }
            else Console.WriteLine("gun places (0.3.21, step C) on the generator's carrack-m");
            var def = jo.ToObject<ShipDefinition>();
            def.Init();

            // --- the contract
            var bad = GunPlaces.DesignProblems(def);
            Check(bad.Count == 0, $"her {def.GunPlaces.Length} gun places keep the contract{(bad.Count > 0 ? ": " + string.Join("; ", bad.Take(6)) : "")}");
            int fixedGuns = plain.Fixtures.Count(f => f.Type == "gun");
            // (0.3.22, step C: the generator's castle guns are places too, "start": "empty": she's launched without them)
            var startEmpty = def.GunPlaces.Where(p => !p.StartsLaid).Select(p => p.Gun).ToHashSet();
            bool SameGone(string[] gone, params string[] more) => (gone ?? Array.Empty<string>()).ToHashSet().SetEquals(startEmpty.Concat(more));
            int laidWaist = def.GunPlaces.Count(p => p.StartsLaid && def.RoomAt(def.Fixture(p.Gun).Pos[0], def.Fixture(p.Gun).Pos[1] + 0.5, def.Fixture(p.Gun).Pos[2])?.Enclosed != true);
            Check(def.GunPlaces.Count(p => p.StartsLaid) == 10 && laidWaist == 10 && fixedGuns == def.GunPlaces.Length,
                $"the ten guns of her waist are laid places ({def.GunPlaces.Count(p => p.StartsLaid)} laid of {def.GunPlaces.Length}, {laidWaist} in the waist; the rest start empty: {string.Join(", ", startEmpty)})");
            var g1 = def.Fixture("gun-port-1");
            var parts = def.GunOfPart.Where(kv => kv.Value == "gun-port-1").Select(kv => kv.Key).OrderBy(s => s).ToList();
            Check(parts.Contains("gun-port-1") && parts.Contains("gun-port-1-barrel") && parts.Contains("gun-port-1-train") && parts.Contains("gun-port-1-breeching-a-out") &&
                  parts.Contains("gun-port-1-trucks-aft") && !parts.Contains("gun-port-1-lid") && parts.Count == 9,
                $"gun-port-1's moving parts are the gun's ({parts.Count}: {string.Join(", ", parts)}), its lid isn't");
            Check(def.MovingParts.Where(p => p.Code.StartsWith("gun-port-1")).All(p => p.Code == "gun-port-1-lid" || def.GunOfPart.ContainsKey(p.Code)),
                "every part of gun-port-1 but its lid is hidden with it");

            // --- a laid gun is a fixed gun: the same fixture, the same working room, the same keep-clear
            var gPlain = plain.Fixture("gun-port-1");
            string Sig(FixtureDef f) => string.Join("|", new object[] { f.Code, f.Lid, f.Gate, f.Elevation, f.Yaw }.Select(o => o?.ToString() ?? "-")) + "|" +
                string.Join(";", new double[][] { f.Pos, f.Sel, f.Box, f.Muzzle, f.Out, f.Trunnion, f.Pivot, f.Seat, f.Port, f.Wall }.Select(a => a == null ? "-" : string.Join(",", a))) + string.Join(",", f.TrainLimits) + string.Join(",", f.ElevationLimits);
            Check(Sig(gPlain) == Sig(g1), "a laid place's gun fixture is the fixed gun's (lays, limits, port, lid, seat: laying and firing unchanged)");
            GunLaying.Muzzle(g1, new GunLay(5, 3), 0.3, 0.02, -0.04, out var m1, out var d1);
            GunLaying.Muzzle(gPlain, new GunLay(5, 3), 0.3, 0.02, -0.04, out var m0, out var d0);
            Check(m1.X == m0.X && m1.Y == m0.Y && m1.Z == m0.Z && d1.X == d0.X && d1.Z == d0.Z, "its muzzle laid 5° / 3° is the fixed gun's");
            // (0.3.24, notes 176, 181) a laid place's room is kept as the fixed gun's; an empty place's isn't kept at all
            var plainBoxes = plain.Mounts.Clear.Where(c => c.Why == "gun").Select(c => c.Box).ToList();
            var defBoxes = def.Mounts.Clear.Where(c => c.Why == "gun").Select(c => c.Box).ToList();
            var laidRooms = def.GunPlaces.Where(p => p.StartsLaid).SelectMany(p => MountLayout.GunRoom(def.Fixture(p.Gun))).ToList();
            var emptyRooms = def.GunPlaces.Where(p => !p.StartsLaid).SelectMany(p => MountLayout.GunRoom(def.Fixture(p.Gun))).ToList();
            bool laidKept = laidRooms.Take(MountLayout.GunRoomSlices).All(r => defBoxes.Any(b => PiecePlanner.Overlap(b, r)));
            bool emptyFree = emptyRooms.All(r => !def.Mounts.Clear.Any(c => (c.Why ?? "").StartsWith("gun") && PiecePlanner.Overlap(c.Box, r, 0.06)));
            Check(defBoxes.All(b => plainBoxes.Any(pb => pb.SequenceEqual(b))) && defBoxes.Count >= def.GunPlaces.Count(p => p.StartsLaid) * MountLayout.GunRoomSlices &&
                  laidKept && emptyFree && (startEmpty.Count == 0 || defBoxes.Count < plainBoxes.Count),
                $"0.3.24: the laid places' rooms kept clear as the fixed guns' ({defBoxes.Count} boxes, {plainBoxes.Count} without places); the empty places' rooms not kept ({startEmpty.Count} places)");
            // ... a piece may stand in an empty place's room; while it does the gun isn't mounted; mounted, nothing more goes in it
            {
                var ep = def.GunPlaces.FirstOrDefault(p => !p.StartsLaid);
                var ef = ep == null ? null : def.Fixture(ep.Gun);
                var inRoom = ef == null ? null : def.Mounts.Floors.FirstOrDefault(m => m.Deck != null && MountLayout.GunRoom(ef).Take(MountLayout.GunRoomSlices).Any(r => PiecePlanner.Overlap(m.Box, r, 0.1)));
                if (ep != null)
                {
                    var stE = new GunPlaceStates(def);
                    var chestE = new FakePiece { Code = "game:chest-east", Family = "chest", Container = true, Slots = 16, Sel = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 }, Col = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 } };
                    var plan = inRoom == null ? null : PiecePlanner.Plan(def.Mounts, inRoom, chestE, 0);
                    var boxes = plan?.Error == null && plan != null ? plan.Solids.Concat(plan.Sel).ToList() : null;
                    string before = boxes == null ? "no place" : MountLayout.FreeGunRoomHit(def, boxes, stE.Mounted)?.Code;
                    string blocked = boxes == null ? "no place" : GunPlaces.MountProblem(def, ep, stE, null, boxes);
                    stE.Set(ep, true);
                    string after = boxes == null ? null : MountLayout.FreeGunRoomHit(def, boxes, stE.Mounted)?.Code;
                    Check(inRoom != null && before == null && blocked == "hansa:gunplace-blocked" && after == ef.Code,
                        $"0.3.24: a chest on {inRoom?.Id ?? "(no floor place)"} in {ep.Id}'s room: fine while it's empty ({before ?? "ok"}), its gun then can't be mounted ({blocked ?? "mounted"}); with the gun mounted, a piece there is refused ({after ?? "allowed"})");
                }
            }

            // --- mounting and taking up
            var st = new GunPlaceStates(def);
            var cells = new SlotStates(def.Slots);
            var p3 = st.Place("gun-port-3");
            var g3 = def.Fixture("gun-port-3");
            Check(def.Fixtures.Where(f => f.Type == "gun").All(f => st.Mounted(f) != startEmpty.Contains(f.Code)) && SameGone(st.Gone()),
                $"a new ship has every laid place's gun mounted, none on the {startEmpty.Count} empty ones");
            Check(GunPlaces.TakeProblem(def, p3, st, 2.0, false, false, false) == "hansa:gun-take-hot" &&
                  GunPlaces.TakeProblem(def, p3, st, 0, true, false, false) == "hansa:gun-take-run-out" &&
                  GunPlaces.TakeProblem(def, p3, st, 0, false, true, false) == "hansa:gun-taken" &&
                  GunPlaces.TakeProblem(def, p3, st, 0, false, false, true) == "hansa:gun-seat-taken",
                "a gun isn't taken up hot, run out, being laid or manned");
            Check(GunPlaces.MountProblem(def, p3, st, cells, null) == "hansa:gunplace-taken", "a place with its gun takes no other");
            Check(GunPlaces.TakeProblem(def, p3, st, 0, false, false, false) == null, "cold, housed, nobody at it: it's taken up");
            st.Set(p3, false);
            Check(st.Absent(g3) && !st.Mounted(g3) && SameGone(st.Gone(), "gun-port-3") && st.Delta.Count == 1, "taken up: gun-port-3 is gone (one difference saved)");
            Check(GunPlaces.TakeProblem(def, p3, st, 0, false, false, false) == "hansa:gunplace-no-gun", "nothing more to take up there");
            var need = GunPlaces.Cells(def, p3);
            // (the generator's place lists its own cells; the test's had main:4:-4 and main:5:-4 made slots)
            Check(need.Count >= 2 && need.Contains(def.Slots.IndexOf("main:4:-4")) && (generators ? (p3.Cells ?? Array.Empty<string>()).All(id => need.Contains(def.Slots.IndexOf(id))) : need.Contains(def.Slots.IndexOf("main:5:-4"))),
                $"gun-port-3 needs its cells ({string.Join(", ", need.Select(n => def.Slots.Slots[n].Id))})");
            int c4 = def.Slots.IndexOf("main:4:-4");
            cells.Set(c4, SlotState.Empty);
            Check(GunPlaces.MountProblem(def, p3, st, cells, null) == "hansa:gunplace-cell-empty", "a gun is refused while a cell under it is out");
            cells.Set(c4, SlotState.Filled);
            var room = MountLayout.GunRoom(g3);
            var chest = new[] { room[3][0] + 0.1, g3.Pos[1], room[3][2] + 0.1, room[3][0] + 0.6, g3.Pos[1] + 0.8, room[3][2] + 0.6 };
            Check(GunPlaces.MountProblem(def, p3, st, cells, new[] { chest }) == "hansa:gunplace-blocked", "a gun is refused while something stands in its working room");
            var aside = new[] { g3.Pos[0] + 3, g3.Pos[1], 0.0, g3.Pos[0] + 3.5, g3.Pos[1] + 0.8, 0.5 };
            Check(GunPlaces.MountProblem(def, p3, st, cells, new[] { aside }) == null, "with the cells in and its room clear it's mounted");
            st.Set(p3, true);
            Check(st.Mounted(g3) && st.Delta.Count == 0 && SameGone(st.Gone()), "mounted again: as designed (no differences saved)");

            // --- her collision and the crosshair without it
            var gridAll = def.Grid.Clone();
            ShipFittings.ApplySolids(def, gridAll, f => false, f => null, st.Absent);
            var gridPlain = plain.Grid.Clone();
            ShipFittings.ApplySolids(plain, gridPlain, f => false, f => null, f => startEmpty.Contains(f.Code));
            bool same = true;
            for (int i = 0; i < gridAll.NX && same; i++)
                for (int j = 0; j < gridAll.NY && same; j++)
                    for (int k = 0; k < gridAll.NZ && same; k++)
                        if (gridAll.Solid(i, j, k) != gridPlain.Solid(i, j, k)) same = false;
            Check(same, "all laid guns mounted: her collision is the fixed guns' ship's (without the empty places' guns)");
            st.Set(p3, false);
            var gridGone = def.Grid.Clone();
            ShipFittings.ApplySolids(def, gridGone, f => false, f => null, st.Absent);
            var bx = g3.Box;
            double cx = (bx[0] + bx[3]) / 2, cy = bx[1] + 0.3, cz = (bx[2] + bx[5]) / 2;
            Check(gridAll.SolidAt(cx, cy, cz) && !gridGone.SolidAt(cx, cy, cz), "taken up: its carriage is gone from her collision (no obstacle, no target for a shot)");
            var eye = new Vec3d(cx, g3.Pos[1] + 1.6, cz + 2.5);
            var look = new Vec3d(cx - eye.X, cy + 0.2 - eye.Y, cz - eye.Z).Normalize();
            var hitWith = ShipFittings.Pick(def, gridAll, eye, look, 6, f => true, f => false, f => null, out _);
            var hitWithout = ShipFittings.Pick(def, gridGone, eye, look, 6, f => true, f => false, f => null, out _, null, st.Absent);
            Check(hitWith == g3 && hitWithout != g3, $"the crosshair finds the gun while it's there, not once it's taken up ({hitWith?.Code} / {hitWithout?.Code ?? "nothing"})");
            st.Set(p3, true);

            // --- wrecks: the guns that are there go into her wreck
            var sand = new TestSite { Floor = (x, z) => 30 };
            WreckInput Sunk(string[] gone) => new()
            {
                Def = def, X = 200.4, Y = 33, Z = 90.6, Yaw = 0.6, List = 0.2, Trim = -0.05, Seed = 7, WreckId = 99, GunsGone = gone,
            };
            var all = WreckBuilder.Build(Sunk(null), sand);
            var less = WreckBuilder.Build(Sunk(new[] { "gun-port-3", "gun-starboard-2" }), sand);
            Check(all.Guns.Count == fixedGuns && less.Guns.Count == fixedGuns - 2, $"her wreck has her guns as loot ({all.Guns.Count}); two taken up before she sank: {less.Guns.Count}");
        }
    }
}
