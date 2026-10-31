using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.29) Chalk notes 232-233 and the user's 2026-10-14 items: the companion beside the quarterdeck stair, walled in
        /// (233, 230 again: the user's own layout on #12600 and the new default), the quarterdeck's third guns (232: every gun
        /// place takes its gun on a ship as launched), the castle guns' train to their ports' cheeks, gun ports in her side's
        /// panels (the 0.3.28 covers migrated), the layout revision (a ship laid before keeps hers).
        ///   dotnet run -c Debug -- notes0329
        /// </summary>
        static void Notes0329(string root)
        {
            Console.WriteLine("0.3.29: chalk notes 232-233, ports in her side's panels, the companion beside the quarterdeck stair");
            var (md, _) = LoadHullDesign(root);
            if (Environment.GetEnvironmentVariable("HANSA_PROBE") == "1") { Companion0329Probe(md); Beam0329Probe(md); }
            CompanionDefault0329(md);
            CompanionUser0329(md);
            GunsMount0329(md);
            Ports0329(md);
            Migration0329(md);
            Lining0329(md);
        }

        /// <summary>
        /// (0.3.29, step D2.3 as the user chose it: "two panel blocks in thickness placed. in open decks just one placed";
        /// "this also would count to hull damage resistance") Her side under her weather decks two layers: the planking
        /// ("hull:") and the ceiling inside the ribs ("ceil:"), each a panel; the ceiling takes CeilBacking of a blow on the
        /// planking, a shot goes through both; a holed planking panel behind its ceiling leaks LinedLeak; grounding hurts
        /// the ceiling only once the planking is cracked.
        /// </summary>
        static void Lining0329(ShipDefinition md)
        {
            var l = md.Hull;
            var dl = md.DamageParts;
            var D = dl.D;
            int inner = l.Slots.Count(s => s.IsInner), lined = l.Slots.Count(s => s.Layer == "outer"), single = l.Slots.Count(s => s.Layer == null);
            bool paired = l.Slots.Where(s => s.Layer != null).All(s => s.PartnerIndex >= 0 && l.Slots[s.PartnerIndex].PartnerIndex == s.Index && l.Slots[s.PartnerIndex].IsInner != s.IsInner &&
                                                                      l.Slots[s.PartnerIndex].Station == s.Station && l.Slots[s.PartnerIndex].Band == s.Band && l.Slots[s.PartnerIndex].Half == s.Half);
            // (the bulwarks over her open decks are her fixed planking, not panels: one layer, the frames inside them)
            bool bulwarksOne = l.Slots.Where(s => s.Layer == null).All(s => s.Sill >= md.DeckY - 0.3);
            Check(inner > 100 && inner == lined && paired && bulwarksOne,
                $"0.3.29: her side under her weather decks two layers ({lined} planking panels each with its ceiling panel: {inner}), one layer panels over her open decks {single}; each pair the same place, naming each other ({paired})");
            // the parts: the ceilings their own (inner bit in the code), CeilToughness
            var pc = l.Slots.First(s => s.IsInner);
            var code = ShipPart.Decode(ShipPart.Hull(pc.Side, pc.Station, pc.Band, pc.HalfIndex, true).Encode());
            Check(code.InnerLayer && !ShipPart.Decode(ShipPart.Hull(pc.Side, pc.Station, pc.Band, pc.HalfIndex).Encode()).InnerLayer && dl.Toughness(dl.Parts[pc.Index]) == D.CeilToughness,
                $"0.3.29: a ceiling panel's damage code its own (bit 18), its toughness {D.CeilToughness} kJ (the planking's {D.HullToughness})");
            // shots square onto a planking panel in her hold under her waterline
            int P = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Layer == "outer" && l.Slots[n].Side > 0 && l.Slots[n].Room == "hold" && l.Slots[n].Head < md.Waterline)
                .OrderBy(n => Math.Abs(l.Slots[n].Centre.X - 2)).First();
            int C = l.Slots[P].PartnerIndex;
            var s0 = l.Slots[P];
            var c = s0.Centre;
            var hit = new Vec3d(c.X, c.Y, c.Z + 0.02);
            float radius = new CannonDef().HitRadius;
            (DamageState st, DamageResult r) Shot(double e, bool ceilingOut = false)
            {
                var st = new DamageState(dl.Count);
                var g = md.Grid.Clone();
                if (ceilingOut) { st.Hurt[C] = 1; foreach (var b in l.Slots[C].SolidBoxes) g.Fill(b, false); }
                var r = ShipDamage.Apply(dl, st, g, hit, new Vec3d(0, 0, -1), e, radius, DamageKinds.All);
                return (st, r);
            }
            string Say(DamageState st) => $"planking {st.Hurt[P]:0.00} {HullPanelRules.StateFor(D, st.Hurt[P])}, ceiling {st.Hurt[C]:0.00} {HullPanelRules.StateFor(D, st.Hurt[C])}";
            var (s40, _) = Shot(40);
            var (s80, _) = Shot(80);
            var (s145, r145) = Shot(145);
            var (s80o, r80o) = Shot(100, true);
            Console.WriteLine($"  --   lined: 40 kJ {Say(s40)}; 80 kJ {Say(s80)}; 145 kJ (a 9-pounder) {Say(s145)}, on through her {r145.Pierced} ({r145.Remaining:0} kJ out); its ceiling out, 100 kJ: planking {s80o.Hurt[P]:0.00}, on {r80o.Pierced}");
            Check(HullPanelRules.StateFor(D, s40.Hurt[P]) == SlotState.Cracked && HullPanelRules.StateFor(D, s80.Hurt[P]) == SlotState.Empty && s80.Hurt[C] >= D.Cracked &&
                  HullPanelRules.StateFor(D, s145.Hurt[P]) == SlotState.Empty && HullPanelRules.StateFor(D, s145.Hurt[C]) == SlotState.Empty && r80o.Pierced,
                $"0.3.29: a lined spot: 40 kJ cracks the planking, 80 holes it (its ceiling behind it cracked: it took {D.CeilBacking:0.0} of it), a 9-pounder's 145 holes both; with its ceiling out 100 kJ holes the planking and goes on (before 0.3.29: 56 kJ holed it, 28 cracked it, 100 went on)");
            // grounding (a broad blow, the bottom's parts): the ceiling not hurt while the planking's sound
            var gst = new DamageState(dl.Count);
            ShipDamage.Apply(dl, gst, md.Grid, hit, null, 15, 1.25, DamageKinds.Bottom);
            var gst2 = new DamageState(dl.Count);
            gst2.Hurt[P] = D.Cracked + 0.01f;
            ShipDamage.Apply(dl, gst2, md.Grid, hit, null, 15, 1.25, DamageKinds.Bottom);
            Check(gst.Hurt[P] > 0 && gst.Hurt[C] == 0 && gst2.Hurt[C] > 0,
                $"0.3.29: grounding (15 kJ, a broad blow) scrapes the planking ({gst.Hurt[P]:0.000}), not the ceiling ({gst.Hurt[C]:0.000}); the planking cracked, the ceiling takes its share ({gst2.Hurt[C]:0.000})");
            // leaks: the planking out behind its ceiling half, both out all of it, the ceiling out alone nothing
            var pose = new FloodPose { X = 0, Y = 0, Z = 0, Surface = (x, z) => md.Waterline };
            double Leak(float hp, float hc) { var st = new DamageState(dl.Count); st.Hurt[P] = hp; st.Hurt[C] = hc; return ShipFlood.Leaks(dl, st, 0, pose, 1, out _); }
            double half = Leak(1, 0), full = Leak(1, 1), ceil = Leak(0, 1);
            Check(Math.Abs(half - full * D.LinedLeak) < 1e-9 && full > 0 && ceil == 0,
                $"0.3.29: the planking out behind its ceiling lets in {half:0.00} t/s, both out {full:0.00} ({D.LinedLeak:0.0} of it with the ceiling in), the ceiling out alone nothing ({ceil:0.00})");
        }

        static readonly Dictionary<ShipPiece, string> Keys0329 = new();
        static string KeyOf0329(ShipPiece p) => p != null && Keys0329.TryGetValue(p, out var k) ? k : null;

        /// <summary>A launched ship's pieces: every modular entry of the design laid as the server lays them (stairs first).</summary>
        static ShipPieceSet LaunchSet0329(ShipDefinition md, SlotStates st, List<string> refused = null)
        {
            var ml = md.Mounts;
            var set = new ShipPieceSet();
            var g = md.Grid.Clone();
            st.Apply(g);
            static int Order(ModularPieceDef d) => d?.Kind switch { "stair" or "ladder" => 0, "hearth" or "oven" => 1, "flue" => 2, _ => 3 };
            foreach (var e in (md.ModularPieces ?? Array.Empty<ModularPieceDef>()).OrderBy(Order))
            {
                if (e.Kind == "hatch") continue;
                var m = ml.ForModular(e);
                if (m == null) { refused?.Add(e.Key + ": no place"); continue; }
                string kind = ml.LaidKind(e);
                var plan = PiecePlanner.Plan(ml, m, ModularFake(e, kind), ModularToward(e, kind, m));
                string err = Problem0328(ml, md.Slots, st, set, g, plan);
                if (err != null) { refused?.Add(e.Key + ": " + err); continue; }
                var p = ShipPiece.From(plan, set.NextId, null);
                if (e.Open && p.Role == PieceRole.Door) p.Open = true;
                Keys0329[p] = e.Key;
                set.Add(p);
            }
            return set;
        }

        /// <summary>Her collision with her cells as they are and every piece's solids (the doors as they stand: open ones not).</summary>
        static VoxelGrid Walked0329(ShipDefinition md, SlotStates st, ShipPieceSet set, IEnumerable<ShipPiece> leaveOut = null)
        {
            var g = md.Grid.Clone();
            st.Apply(g);
            var skip = new HashSet<ShipPiece>(leaveOut ?? Enumerable.Empty<ShipPiece>());
            foreach (var p in set.All)
                if (!skip.Contains(p) && !(p.Role == PieceRole.Door && p.Open))
                    foreach (var s in p.Solids) g.Fill(s);
            return g;
        }

        static void CompanionDefault0329(ShipDefinition md)
        {
            var l = md.Slots;
            var ml = md.Mounts;
            double mainY = md.Decks.First(d => d.Code == "main").Y;
            var st = new SlotStates(l);
            var refused = new List<string>();
            var set = LaunchSet0329(md, st, refused);
            Check(refused.Count == 0, $"0.3.29: every modular entry of carrack-m laid on a new ship ({set.All.Count()} pieces){(refused.Count > 0 ? ": " + string.Join("; ", refused.Take(8)) : "")}");
            // the well: the centreline cells x -3..0 and the beam at -2 out as designed
            var well = new[] { "main:-3:-1", "main:-2:-1", "main:-1:-1", "beam:main:-2:-1" };
            Check(well.All(id => l.Get(id)?.StartsEmpty == true), $"note 233: the companion's well out as designed ({string.Join(", ", well.Select(id => id + (l.Get(id)?.StartsEmpty == true ? " out" : " in")))})");
            var stair = set.All.FirstOrDefault(p => KeyOf0329(p) == "stair-companion");
            var foot = stair == null ? null : ml.Get(stair.Mount);
            Check(stair != null && foot?.Id == "f:lower:-2:-1" && stair.Dir == 0,
                $"note 233: the companion stair as the user laid hers: its foot on the lower deck's centreline at x -2 ({foot?.Id}), up forward ({(stair == null ? "-" : MountLayout.DirCode[stair.Dir])}) to the waist at x 0, beside the quarterdeck stair");
            // walled in: both sides x -3..0, the after end in the cabin, a door at its head, the roof over the waist's two cells
            var walls = set.All.Where(p => KeyOf0329(p) != null && (p.Role is PieceRole.Partition or PieceRole.Door or PieceRole.Shelf) && md.ModularPieces.First(e => e.Key == KeyOf0329(p)).Since > 0).ToList();
            var door = walls.FirstOrDefault(p => p.Role == PieceRole.Door);
            int sides = walls.Count(p => p.Role == PieceRole.Partition && (Math.Abs(p.Box[2] + p.Box[5]) / 2 is double zc && (Math.Abs(zc - 0.5) < 0.2 || Math.Abs(zc + 0.5) < 0.2)));
            int roofs = walls.Count(p => p.Role == PieceRole.Shelf);
            Console.WriteLine($"  --   the companion's pieces: {string.Join(", ", walls.Select(p => p.Mounts[0]))}");
            Check(door != null && door.Mounts[0] == "b:main:0:-1:w:0" && sides >= 12 && roofs == 2,
                $"note 233: the companion walled in: {sides} side walls (z -0.5 and 0.5, x -3..0), its after end in the great cabin, a door at its head ({door?.Mounts[0] ?? "none"}), {roofs} roof boards over the waist's cells");
            // the great cabin's door a cell to port, its front open into the companion
            var gcDoor = set.All.FirstOrDefault(p => KeyOf0329(p) == "door-greatcabin");
            Check(gcDoor != null && Math.Abs((gcDoor.Box[2] + gcDoor.Box[5]) / 2 + 1.0) < 0.1 && Math.Abs((gcDoor.Box[0] + gcDoor.Box[3]) / 2 + 2.0) < 0.1,
                $"note 233: the great cabin's door a cell to port of the companion (z {(gcDoor == null ? "-" : ((gcDoor.Box[2] + gcDoor.Box[5]) / 2).ToString("0.##"))}) on its front at x -2");
            // the old stairwell in the cabin's starboard corner gone (one companion)
            Check(!md.ModularPieces.Any(e => e.Key is "door-companion-stair" or "door-companion-cabin") && md.ModularPieces.Count(e => e.Kind == "stair" && e.At[1] < mainY - 1 && e.At[1] > mainY - 4) == 1,
                "note 233: one companion: the stairwell in the cabin's starboard corner gone (its doors, its stair)");
            // it walks: down from the waist through the door and up again; the quarterdeck stair beside it too
            door.Open = true;
            var gw = Walked0329(md, st, set);
            bool down = PiecePlanner.WalkFlight(gw, foot, stair.Dir, stair.Rise, out double top, out double back, PiecePlanner.RunOf(stair));
            var qs = set.All.First(p => KeyOf0329(p) == "stair-quarterdeck");
            // (the quarterdeck stair walked a hand off the companion's wall: its layer is a quarter block into the stair's
            // cell, 0.05 into a man walking its middle)
            var qf = ml.Get(qs.Mount);
            var qShift = new MountPoint { Id = "shifted", Kind = MountKind.Floor, Deck = qf.Deck, X = qf.X, Y = qf.Y, Z = qf.Z + 0.1, I = qf.I, K = qf.K, Box = qf.Box };
            bool qWalk = PiecePlanner.WalkFlight(gw, qShift, qs.Dir, qs.Rise, out double qt, out double qb, PiecePlanner.RunOf(qs));
            Check(down && qWalk, $"note 233: the companion stair walks up ({top:0.00}) and down ({back:0.00}) inside its walls, its door open; the quarterdeck stair beside it too, 0.1 off its middle ({qt:0.00}, {qb:0.00})" +
                (down && qWalk ? "" : $" [companion: {WalkSay0329(gw, foot, stair.Dir, stair.Rise, PiecePlanner.RunOf(stair))}; quarterdeck: {WalkSay0329(gw, qShift, qs.Dir, qs.Rise, PiecePlanner.RunOf(qs))}]"));
            // its door swings out onto the waist clear of her and the quarterdeck stair
            var dplan = door.Solids;
            bool clear = dplan.All(s => !md.Grid.Collides(s[0] + 0.02, s[1] + 0.02, s[2] + 0.02, s[3] - 0.02, s[4] - 0.02, s[5] - 0.02)) &&
                         set.All.Where(p => p != door).All(p => !p.Sel.Any(a => dplan.Any(b => PiecePlanner.Overlap(a, b))));
            Check(clear, "note 233: the companion's door opens out onto the waist clear of her and of the pieces beside it");
        }

        /// <summary>(0.3.29) The user's own layout on #12600 (0.3.27, the cabin's front at x -3 there): every wall round it, a door and the roof go on, and it walks.</summary>
        static void CompanionUser0329(ShipDefinition md)
        {
            var l = md.Slots;
            var ml = md.Mounts;
            double mainY = md.Decks.First(d => d.Code == "main").Y;
            var st = new SlotStates(l);
            // her cells as saved (#12600): the well's and the beam out (the design's well the same now)
            foreach (var id in new[] { "main:-3:-1", "main:-2:-1", "main:-1:-1", "beam:main:-2:-1" }) st.Set(l.IndexOf(id), SlotState.Empty);
            var set = new ShipPieceSet();
            var g = md.Grid.Clone();
            st.Apply(g);
            // her castle stair (the design's) and her stair down (piece 180: f:lower:-2:-1, up +x)
            var qe = md.ModularPieces.First(e => e.Code == "stair-quarterdeck");
            var qplan = PiecePlanner.Plan(ml, ml.ForModular(qe), ShipStair0327(2), (MountLayout.DirOf(qe.Up) + 2) % 4);
            set.Add(ShipPiece.From(qplan, set.NextId, null));
            var foot = ml.Get("f:lower:-2:-1");
            var sp = PiecePlanner.Plan(ml, foot, ShipStair0327(2), 2);
            string serr = Problem0328(ml, l, st, set, g, sp);
            var stair = ShipPiece.From(sp, set.NextId, null);
            if (serr == null) set.Add(stair);
            var said = new List<string>();
            var bad = new List<string>();
            void Put(string mount, FakePiece what, int toward = -1)
            {
                var m = ml.Get(mount);
                if (m == null) { bad.Add(mount + ": no place"); return; }
                if (!DeckCells.MountOpen(m, st)) { bad.Add(mount + ": not usable with her cells as they are"); return; }
                var plan = PiecePlanner.Plan(ml, m, what, toward < 0 ? m.In : toward);
                string err = Problem0328(ml, l, st, set, g, plan);
                if (err != null) { bad.Add($"{mount}: {err}"); return; }
                set.Add(ShipPiece.From(plan, set.NextId, null));
                said.Add(mount);
            }
            // her walls as saved: the port side, two rows; the cabin's old door planked (the front at -3)
            foreach (int i in new[] { -3, -2, -1 }) foreach (int row in new[] { 0, 1 }) Put($"b:main:{i}:-2:s:{row}", Planks0328);
            foreach (int row in new[] { 0, 1, 2 }) Put($"b:main:-4:-1:e:{row}", Planks0328);
            // what she couldn't put: the starboard side between the well and the quarterdeck stair (the twins, on the stair's
            // cells; under the quarterdeck the landing's), the door at its head (swinging out forward), the roof
            foreach (int i in new[] { -3, -2, -1 }) foreach (int row in new[] { 0, 1 }) Put($"b:main:{i}:0:n:{row}", Planks0328);
            Put("b:main:0:-1:w:0", Door0328);
            foreach (int i in new[] { -2, -1 }) Put($"hh:main:{i}:-1", Slab0328, 1);
            Check(serr == null && bad.Count == 0,
                $"note 233 (#12600, the user's layout): her stair down ({serr ?? "ok"}), her port wall, the planked old door, and the walls she couldn't put: both rows to starboard between the well and the quarterdeck stair, the door at the head, the roof ({said.Count} put){(bad.Count > 0 ? ": " + string.Join("; ", bad) : "")}");
            if (serr != null) return;
            var door = set.All.FirstOrDefault(p => p.Role == PieceRole.Door);
            if (door != null) door.Open = true;
            var gw = Walked0329(md, st, set);
            bool walks = PiecePlanner.WalkFlight(gw, foot, sp.Dir, sp.Rise, out double top, out double back, 2);
            var qf = ml.ForModular(qe);
            var qShift = new MountPoint { Id = "shifted", Kind = MountKind.Floor, Deck = qf.Deck, X = qf.X, Y = qf.Y, Z = qf.Z + 0.1, I = qf.I, K = qf.K, Box = qf.Box };
            bool qWalk = PiecePlanner.WalkFlight(gw, qShift, qplan.Dir, qplan.Rise, out double qt, out double qb, 2);
            Check(walks && qWalk, $"note 233 (#12600): her stair walks up ({top:0.00}) and down ({back:0.00}) in its walls, the door open; the quarterdeck stair beside them, 0.1 off its middle ({qt:0.00}, {qb:0.00})" +
                (walks && qWalk ? "" : $" [hers: {WalkSay0329(gw, foot, sp.Dir, sp.Rise, 2)}; quarterdeck: {WalkSay0329(gw, qShift, qplan.Dir, qplan.Rise, 2)}]"));
        }

        /// <summary>(0.3.29, note 232: "here and other side gun cannot be placed as says someone standing") Every gun place takes its gun on a ship as launched (her ports in).</summary>
        static void GunsMount0329(ShipDefinition md)
        {
            var l = md.Slots;
            var st = new SlotStates(l);
            var set = LaunchSet0329(md, st);
            // her ports all in (the poop's start planked: put in for this)
            foreach (int n in l.Ports) st.Set(n, l.Slots[n].IsGunPort ? SlotState.Filled : SlotState.Empty);
            var things = set.All.Where(p => p.Role != PieceRole.Railing).SelectMany(p => p.Solids.Concat(p.Sel)).ToList();
            var rails = set.All.Where(p => p.Role == PieceRole.Railing).SelectMany(p => p.Solids.Concat(p.Sel)).ToList();
            var states = new GunPlaceStates(md);
            var bad = new List<string>();
            var q3 = new List<string>();
            foreach (var gp in md.GunPlaces.Where(p => !p.StartsLaid))
            {
                string err = GunPlaces.MountProblem(md, gp, states, st, things, rails);
                if (err == "ships:gunplace-blocked")
                {
                    var room = MountLayout.GunRoom(md.Fixture(gp.Gun));
                    var who = set.All.FirstOrDefault(pc => pc.Solids.Concat(pc.Sel).Any(b => room.Any(r => PiecePlanner.Overlap(b, r))));
                    var box = who?.Solids.Concat(who.Sel).First(b => room.Any(r => PiecePlanner.Overlap(b, r)));
                    var rb = box == null ? null : room.First(r => PiecePlanner.Overlap(box, r));
                    err += $" (by {KeyOf0329(who) ?? who?.Mounts[0]} {(box == null ? "" : string.Join(" ", box.Select(v => v.ToString("0.###"))))} vs room {(rb == null ? "" : string.Join(" ", rb.Select(v => v.ToString("0.###"))))})";
                }
                if (err != null) bad.Add($"{gp.Id}: {err}");
                if (gp.Gun.EndsWith("-quarter-3")) q3.Add($"{gp.Id} {err ?? "mounted"}");
            }
            Check(bad.Count == 0 && q3.Count == 2,
                $"note 232: a gun goes on every gun place of a new ship (her ports in), the quarterdeck's third both sides ({string.Join(", ", q3)}){(bad.Count > 0 ? ": " + string.Join("; ", bad) : "")}");
            // where the trouble was: quarter-3's working room against the quarterdeck's breastwork on its edge (x -2)
            var f3 = md.Fixture("gun-port-quarter-3");
            double reach = MountLayout.GunRoom(f3).Take(MountLayout.GunRoomSlices).Max(b => b[3]);
            Check(reach < -2.0625, $"note 232: the quarterdeck's third gun's working room stays aft of the breastwork's railings on the edge (to x {reach:0.000}, the railings from -2.0625)");
            // the castle guns train to their ports' cheeks: the forecastle's
            foreach (var code in new[] { "gun-port-forecastle-1", "gun-port-forecastle-2", "gun-starboard-forecastle-1", "gun-starboard-forecastle-2" })
            {
                var f = md.Fixture(code);
                Console.WriteLine($"  --   {code}: trains {f.TrainLimits[0]:0.##}..{f.TrainLimits[1]:0.##}° (0.3.28: forecastle-1 -9.5..15.25, forecastle-2 -15.5..24.5)");
            }
        }

        /// <summary>(0.3.29, the user: "removable side board as with hull and then we place port inside it") A castle port's place: its board, the port itself.</summary>
        static void Ports0329(ShipDefinition md)
        {
            var l = md.Slots;
            var ports = l.Ports.Where(n => l.Slots[n].IsGunPort).ToList();
            if (ports.Count == 0) { Console.WriteLine("  --   0.3.29's boards and ports in her side: dropped in 0.3.30 (note 239: fixed ports)"); return; }
            var startIn = ports.Where(n => !l.Slots[n].StartsEmpty).Select(n => l.Slots[n].Gun).ToList();
            var badPair = ports.Where(n => l.IndexOf(l.Slots[n].Cover) is int c && (c < 0 || l.Slots[c].StartsEmpty == l.Slots[n].StartsEmpty ||
                                                                                   md.GunPlaces.First(p => p.Gun == l.Slots[n].Gun).GunPort != l.Slots[n].Id))
                               .Select(n => l.Slots[n].Id).ToList();
            Check(ports.Count == 16 && startIn.Count == 10 && startIn.All(g => !g.Contains("-poop-")) && badPair.Count == 0,
                $"0.3.29: 16 castle ports each a board and a port ({ports.Count}), {startIn.Count} ports in to start (the quarterdeck's and the forecastle's), the poop's boarded over; each board and its port start the other way, its gun place names the port{(badPair.Count > 0 ? ": " + string.Join(", ", badPair) : "")}");
            // collision: boarded over (the hole shut), the port in or an opening (the hole open)
            int pn = l.IndexOf("gunport:gun-port-quarter-2"), cn = l.IndexOf("port:gun-port-quarter-2");
            var hb = l.Slots[cn].Box;
            double hx = (hb[0] + hb[3]) / 2, hy = (hb[1] + hb[4]) / 2, hz = (hb[2] + hb[5]) / 2;
            string State(SlotState board, SlotState port)
            {
                var ds = new SlotStates(l);
                ds.Set(cn, board);
                ds.Set(pn, port);
                var g = md.Grid.Clone();
                ds.Apply(g);
                return g.SolidAt(hx, hy, hz) ? "shut" : "open";
            }
            string boarded = State(SlotState.Filled, SlotState.Empty), port = State(SlotState.Empty, SlotState.Filled), open = State(SlotState.Empty, SlotState.Empty);
            Check(boarded == "shut" && port == "open" && open == "open" && l.Slots[pn].SolidBoxes.Length == 0,
                $"0.3.29: her side at a port's place: boarded over {boarded}; the port in {port}; an opening {open} (the port no voxels of its own)");
            // a gun only with the port in
            var gp = md.GunPlaces.First(p => p.Gun == "gun-port-quarter-2");
            var st = new SlotStates(l);
            st.Set(cn, SlotState.Empty);
            st.Set(pn, SlotState.Empty);
            string noPort = GunPlaces.MountProblem(md, gp, new GunPlaceStates(md), st, null);
            st.Set(pn, SlotState.Filled);
            string withPort = GunPlaces.MountProblem(md, gp, new GunPlaceStates(md), st, null);
            st.Set(cn, SlotState.Filled);
            st.Set(pn, SlotState.Empty);
            string boardedGun = GunPlaces.MountProblem(md, gp, new GunPlaceStates(md), st, null);
            Check(noPort == "ships:gunplace-no-port" && withPort == null && boardedGun == "ships:gunplace-port-covered",
                $"0.3.29: a gun on an opening with no port refused ({noPort}); with the port in, mounted ({withPort ?? "ok"}); boarded over refused ({boardedGun})");
        }

        /// <summary>(0.3.29) A ship laid before (#12600, #14372 as saved): her cells kept, her 0.3.28 ports read as ports, the new entries not put.</summary>
        static void Migration0329(ShipDefinition md)
        {
            var l = md.Slots;
            Check(md.LayoutRev == 1 && md.LayoutBefore?.Length == 1, $"0.3.29: carrack-m's layout revision {md.LayoutRev}, its steps {md.LayoutBefore?.Length ?? 0}");
            (SlotStates, SlotStates, LayoutMigration.Result) Run(Dictionary<string, int> saved)
            {
                var ds = new SlotStates(l);
                var hs = new SlotStates(md.Hull);
                foreach (var kv in saved)
                {
                    int n = l.IndexOf(kv.Key);
                    if (n >= 0) ds.Set(n, (SlotState)kv.Value);
                    int h = md.Hull.IndexOf(kv.Key);
                    if (h >= 0) hs.Set(h, (SlotState)kv.Value);
                }
                var laid = new HashSet<string>(md.ModularPieces.Where(e => e.Since == 0).Select(e => e.Key));
                var r = LayoutMigration.Apply(md, 0, saved, ds, hs, laid);
                return (ds, hs, r);
            }
            bool Out(SlotStates s, string id) => !s.Filled(l.IndexOf(id));
            // #12600 (0.3.27, her own companion, guns on the poop's ports 2 and 3)
            var s12600 = new Dictionary<string, int>
            {
                ["lower:-6:0"] = 0, ["lower:-5:0"] = 0, ["lower:-4:0"] = 0, ["main:-3:-1"] = 2, ["main:-2:-1"] = 2, ["main:-1:-1"] = 2,
                ["beam:main:-2:-1"] = 2, ["port:gun-port-poop-2"] = 2, ["port:gun-starboard-poop-2"] = 2, ["port:gun-port-poop-3"] = 2,
                ["port:gun-starboard-poop-3"] = 2
            };
            var (d1, h1, r1) = Run(s12600);
            bool oldWell = new[] { "main:-7:2", "main:-6:2", "main:-5:2" }.All(id => Out(d1, id));
            bool hers = new[] { "main:-3:-1", "main:-2:-1", "main:-1:-1", "beam:main:-2:-1" }.All(id => Out(d1, id));
            // ((0.3.30, note 239) no board or port slots: a port is the fixed one)
            bool PortIn(SlotStates s_, string gun) => l.IndexOf("gunport:" + gun) < 0 || s_.Filled(l.IndexOf("gunport:" + gun)) && !s_.Filled(l.IndexOf("port:" + gun));
            bool poopPorts = l.IndexOf("port:gun-port-poop-1") < 0 || PortIn(d1, "gun-port-poop-2") && PortIn(d1, "gun-starboard-poop-3") && !PortIn(d1, "gun-port-poop-1") && d1.Filled(l.IndexOf("port:gun-port-poop-1"));
            bool quarter = PortIn(d1, "gun-port-quarter-1") && PortIn(d1, "gun-starboard-forecastle-2");
            Check(oldWell && hers && poopPorts && quarter && r1.NewlyLaid.Count > 10,
                $"0.3.29 (#12600 as saved): her old stairwell's well stays open ({oldWell}), her own companion's cells and beam out ({hers}); her poop's ports with guns ports in her side, the others planked ({poopPorts}); the quarterdeck's ports in ({quarter}); {r1.NewlyLaid.Count} new entries not put ({string.Join("; ", r1.Notes.Take(6))}...)");
            // #14372 (0.3.28, launched new: her starboard poop ports opened for guns)
            var s14372 = new Dictionary<string, int> { ["port:gun-starboard-poop-1"] = 2, ["port:gun-starboard-poop-2"] = 2, ["port:gun-starboard-poop-3"] = 2 };
            var (d2, h2, r2) = Run(s14372);
            bool newCellsIn = new[] { "main:-3:-1", "main:-2:-1", "main:-1:-1", "beam:main:-2:-1" }.All(id => !Out(d2, id));
            bool sp = l.IndexOf("gunport:gun-starboard-poop-1") < 0 ||
                      new[] { 1, 2, 3 }.All(k => d2.Filled(l.IndexOf($"gunport:gun-starboard-poop-{k}"))) && new[] { 1, 2, 3 }.All(k => !d2.Filled(l.IndexOf($"gunport:gun-port-poop-{k}")));
            Check(newCellsIn && sp, $"0.3.29 (#14372 as saved): her deck at the new companion's place stays laid ({newCellsIn}); her starboard poop ports in (their guns), the port side's planked ({sp})");
        }


        /// <summary>(0.3.29) WalkFlight with where the walker got to (for the checks' messages).</summary>
        static string WalkSay0329(VoxelGrid g, MountPoint foot, int up, double rise, double run)
        {
            var a = PiecePlanner.FlightFoot(foot, up);
            var b = PiecePlanner.FlightTop(foot, up, rise, run);
            double ax = MountLayout.DX[up], az = MountLayout.DZ[up];
            var mv = new DeckMover(a[0], a[1], a[2]) { OnGround = true };
            mv.Unstick(g);
            string start = $"start ({mv.X:0.00},{mv.Y:0.00},{mv.Z:0.00})";
            for (int n = 0; n < 200; n++)
            {
                mv.Step(g, 0.05, ax, az, 3.0, false, false);
                if ((mv.X - b[0]) * ax + (mv.Z - b[2]) * az > 0) break;
            }
            for (int n = 0; n < 10; n++) mv.Step(g, 0.05, 0, 0, 3.0, false, false);
            string topAt = $"up to ({mv.X:0.00},{mv.Y:0.00},{mv.Z:0.00}) floor {DeckMover.HasFloor(g, mv.X, mv.Y + 0.01, mv.Z)}";
            for (int n = 0; n < 200; n++)
            {
                mv.Step(g, 0.05, -ax, -az, 3.0, false, false);
                if ((mv.X - a[0]) * ax + (mv.Z - a[2]) * az < 0) break;
            }
            for (int n = 0; n < 10; n++) mv.Step(g, 0.05, 0, 0, 3.0, false, false);
            return $"{start}, {topAt}, back to ({mv.X:0.00},{mv.Y:0.00},{mv.Z:0.00})";
        }

        static void Beam0329Probe(ShipDefinition md)
        {
            var sl = md.Slots;
            var ml = md.Mounts;
            double mainY = md.Decks.First(d => d.Code == "main").Y;
            var st = new SlotStates(sl);
            var set = LaunchSet0329(md, st);
            foreach (var id in new[] { "beam:main:-6:1", "beam:main:-4:1", "beam:main:2:1" })
            {
                int seg = sl.IndexOf(id);
                if (seg < 0) { Console.WriteLine($"  probe beam {id}: none"); continue; }
                var sd = sl.Slots[seg];
                double xc = sd.Box[3];
                var foot = ml.Floors.FirstOrDefault(m => m.Deck == "lower" && Math.Abs(m.X - xc) < 1e-6 && Math.Abs(m.Z + 0.5 - sd.CZ) < 1e-6);
                var s2 = new SlotStates(sl);
                foreach (var x in new[] { xc - 0.5, xc + 0.5, xc + 1.5 }) { int c = sl.OnDeck(mainY, x, sd.CZ); if (c >= 0) s2.Set(c, SlotState.Empty); }
                s2.Set(seg, SlotState.Empty);
                var set2 = LaunchSet0329(md, new SlotStates(sl));
                var plan = PiecePlanner.Plan(ml, foot, ShipStair0327(2), 2);
                if (plan.Error == null) set2.Add(ShipPiece.From(plan, set2.NextId, null));
                var g = Walked0329(md, s2, set2);
                bool ok = PiecePlanner.WalkFlight(g, foot, plan.Dir, plan.Rise, out double t, out double b, 2);
                Console.WriteLine($"  probe beam {id}: box {string.Join(" ", sd.Box.Select(v => v.ToString("0.##")))} foot {foot?.Id} plan {plan.Error ?? "ok"} walk {ok} {WalkSay0329(g, foot, plan.Dir, plan.Rise, 2)}");
                foreach (var p in set.All.Where(p => p.Box != null && p.Box[3] > xc - 1 && p.Box[0] < xc + 4 && p.Box[5] > sd.CZ - 1.5 && p.Box[2] < sd.CZ + 1.5 && p.Box[4] > mainY - 3.5))
                    Console.WriteLine($"    piece {p.Id} {p.Role} {string.Join(",", p.Mounts.Take(3))} box {string.Join(" ", p.Box.Select(v => v.ToString("0.###")))}");
            }
        }

        static void Companion0329Probe(ShipDefinition md)
        {
            var l = md.Slots;
            var ml = md.Mounts;
            double mainY = md.Decks.First(d => d.Code == "main").Y;
            var st = new SlotStates(l);
            var refused = new List<string>();
            var set = LaunchSet0329(md, st, refused);
            Console.WriteLine($"  probe: launched set {set.All.Count()} pieces, refused {refused.Count}: {string.Join("; ", refused.Take(6))}");
            foreach (var p in set.All.Where(p => p.Box != null && p.Box[3] > -3.2 && p.Box[0] < 1.2 && Math.Abs((p.Box[2] + p.Box[5]) / 2) < 2.6 && p.Box[1] < mainY + 3.2 && p.Box[4] > mainY - 3.5))
                Console.WriteLine($"  probe: piece {p.Id} {p.Role} {string.Join(",", p.Mounts.Take(3))} box {string.Join(" ", p.Box.Select(v => v.ToString("0.###")))}");
        }
    }
}
