using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.28) Chalk notes 223-231 and step D2: the castle ports (the poop's planked over by default, 223; a third a side
        /// on the quarterdeck, 224), the port covers (D2.4), the castle fronts on the edge beams' lines (225, 227), the guns'
        /// full train both ways (226), the poop stair on the centreline and the cabin door beside it (231), one-block hull
        /// panels in halves (D2.1) and their save codes, and the agents' parts: the companion hood (230) and the stern stair
        /// (Notes0328Hood), deck cells and pieces taking shots (Notes0328Damage).
        ///   dotnet run -c Debug -- notes0328
        /// </summary>
        static void Notes0328(string root)
        {
            Console.WriteLine("0.3.28: chalk notes 223-231, step D2");
            var (md, _) = LoadHullDesign(root);
            Ports0328(md);
            Fronts0328(md);
            Trains0328(md);
            PoopStair0328(md);
            Halves0328(md);
            Notes0328Hood(root);
            Notes0328Damage(root);
        }

        static void Ports0328(ShipDefinition md)
        {
            var l = md.Slots;
            var covers = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].IsPort).ToList();
            if (covers.Count == 0)
            {
                // ((0.3.30, notes 236, 239) no covers: the ports fixed, none on the poop's sides)
                Console.WriteLine("  --   D2.4: the port covers dropped in 0.3.30 (note 239); the poop's side ports gone (note 236)");
                var q3n = md.GunPlaces.Where(p => p.Gun.EndsWith("-quarter-3")).ToList();
                Check(q3n.Count == 2 && q3n.All(p => !p.StartsLaid) && q3n.All(p => Math.Abs(md.Fixture(p.Gun).Pos[0] - -3.0) < 0.01),
                    $"note 224: a third port a side on the quarterdeck at x -3 ({string.Join(", ", q3n.Select(p => p.Gun))}), no gun yet");
                return;
            }
            var shut = covers.Where(n => !l.Slots[n].StartsEmpty).Select(n => l.Slots[n].Gun).OrderBy(g => g).ToList();
            Check(covers.Count == 16 && shut.Count == 6 && shut.All(g => g.Contains("-poop-")),
                $"note 223: the poop's six side ports planked over by default ({string.Join(", ", shut)}), the other castle ports open ({covers.Count - shut.Count})");
            var q3 = md.GunPlaces.Where(p => p.Gun.EndsWith("-quarter-3")).ToList();
            Check(q3.Count == 2 && q3.All(p => !p.StartsLaid && p.Port != null && l.Get(p.Port)?.StartsEmpty == true) &&
                  q3.All(p => Math.Abs(md.Fixture(p.Gun).Pos[0] - -3.0) < 0.01) && Math.Sign(md.Fixture(q3[0].Gun).Pos[2]) != Math.Sign(md.Fixture(q3[1].Gun).Pos[2]),
                $"note 224: a third port a side on the quarterdeck at x -3 ({string.Join(", ", q3.Select(p => p.Gun))}), open, no gun yet");
            // D2.4: a gun can't be mounted on a planked-over port; opened it can
            var poop = md.GunPlaces.First(p => p.Gun == "gun-port-poop-1");
            int pn = l.IndexOf(poop.Port);
            var states = new GunPlaceStates(md);
            var slots = new SlotStates(l);
            string covered = GunPlaces.MountProblem(md, poop, states, slots, null);
            slots.Set(pn, SlotState.Empty);
            // ((0.3.29) and the port itself put in)
            if (poop.GunPort != null) slots.Set(l.IndexOf(poop.GunPort), SlotState.Filled);
            string open = GunPlaces.MountProblem(md, poop, states, slots, null);
            Check(covered == "hansa:gunplace-port-covered" && open == null,
                $"D2.4: a gun on the planked-over port refused ({covered}); its planking out, it's mounted ({open ?? "ok"})");
            // the cover's voxels: solid while it's in, her side open when it's out
            var g = md.Grid.Clone();
            bool solidIn = l.Slots[pn].SolidBoxes.All(b => DeckSlotLayout.Voxels(g, b).Any());
            Check(solidIn && GunPlaces.DesignProblems(md).Count == 0, $"D2.4: the covers' holes and the gun places' ports keep the contract ({string.Join("; ", GunPlaces.DesignProblems(md).Take(3))})");
        }

        static void Fronts0328(ShipDefinition md)
        {
            var mp = md.ModularPieces ?? Array.Empty<ModularPieceDef>();
            bool Wall(ModularPieceDef e) => e.Kind is "partition" or "door" or "window";
            var aft = mp.Where(e => Wall(e) && e.Along == "z" && e.At[0] > -3.5 && e.At[0] < -1.5 && e.At[1] < 4.5 && e.Since == 0 || e.Kind == "door" && e.Code == "door-greatcabin").ToList();
            var fwd = mp.Where(e => Wall(e) && e.Along == "z" && e.At[0] > 13.5 && e.At[0] < 15.5 && e.At[1] < 4.5).ToList();
            var gc = md.Rooms.First(r => r.Code == "greatcabin");
            var fc = md.Rooms.First(r => r.Code == "forecastle");
            Check(aft.Count > 10 && aft.All(e => Math.Abs(e.At[0] - -2.0) < 1e-3) && Math.Abs(gc.Max[0] - -2.0) < 1e-3,
                $"note 225: the great cabin's front on the quarterdeck's edge beam's line, x -2 ({aft.Count} pieces at {string.Join(", ", aft.Select(e => e.At[0]).Distinct())}; the room to {gc.Max[0]})");
            Check(fwd.Count > 10 && fwd.All(e => Math.Abs(e.At[0] - 14.0) < 1e-3) && Math.Abs(fc.Min[0] - 14.0) < 1e-3,
                $"note 227: the forecastle's front on the forecastle deck's edge beam's line, x 14 ({fwd.Count} pieces at {string.Join(", ", fwd.Select(e => e.At[0]).Distinct())}; the room from {fc.Min[0]})");
        }

        static void Trains0328(ShipDefinition md)
        {
            var guns = md.Fixtures.Where(f => f.Type == "gun").ToList();
            // ((0.3.29, note 232) the quarterdeck's third, a block from the deck's edge: its carriage over the waist past 7.5°)
            var narrow = guns.Where(f => f.TrainLimits[0] > (f.Code.EndsWith("quarter-3") ? -7.5 : -9) || f.TrainLimits[1] < 9).Select(f => $"{f.Code} {f.TrainLimits[0]:0.#}..{f.TrainLimits[1]:0.#}").ToList();
            Check(narrow.Count == 0, $"note 226: every gun trains 9° or more both ways from square (the least {guns.Min(f => -f.TrainLimits[0]):0.#} aft, {guns.Min(f => f.TrainLimits[1]):0.#} forward){(narrow.Count > 0 ? ": " + string.Join("; ", narrow) : "")}");
        }

        static void PoopStair0328(ShipDefinition md)
        {
            var mp = md.ModularPieces ?? Array.Empty<ModularPieceDef>();
            var st = mp.FirstOrDefault(e => e.Code == "stair-poop");
            var door = mp.FirstOrDefault(e => e.Code == "door-poopcabin");
            Check(st != null && Math.Abs(st.At[2]) < 1e-3 && door != null && Math.Abs(door.At[2] - 1.0) < 1e-3,
                $"note 231: the poop stair on the centreline (z {st?.At[2]}), the poop cabin's door where it stood (z {door?.At[2]})");
        }

        static void Halves0328(ShipDefinition md)
        {
            var h = md.Hull;
            var bad = h.Slots.Where(s => s.Half is not ("a" or "f") || s.Box[3] - s.Box[0] > 1.0 + 1e-3 || !s.Id.EndsWith(":" + s.Half)).Select(s => s.Id).ToList();
            // ((0.3.29) the planking's: the ceilings are their own layer)
            Check(h.Count > 250 && bad.Count == 0 && h.Problems.Count == 0,
                $"D2.1: {h.Count} hull panels, each a block long, the halves of its frame space (\"a\" aft, \"f\" fore){(bad.Count > 0 ? ": " + string.Join(", ", bad.Take(4)) : "")}");
            // their damage codes: the halves apart, an older save's whole panel (the after half's code) both halves'
            var a = ShipPart.Hull(1, 12, 3, 0).Encode();
            var f = ShipPart.Hull(1, 12, 3, 1).Encode();
            var df = ShipPart.Decode(f);
            Check(a != f && df.Index == 1 && df.Station == 12 && df.Band == 3 && ShipPart.Decode(a).Index == 0,
                "D2.1: a panel half's damage code its own (the fore half's bit), decoded again");
            var pairs = h.Slots.Where(s => !s.IsInner).GroupBy(s => (s.Side, s.Station, s.Band)).Count(gr => gr.Count() == 2);
            Check(pairs > 100, $"D2.2: {pairs} frame spaces with both halves panels (taken out together, only the rib between them)");
        }
    }
}
