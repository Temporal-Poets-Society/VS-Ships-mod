using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    /// <summary>
    /// (0.3.21, step C; docs/slots.md §7) Her gun places' states (which have their gun), kept as the differences from the
    /// design's <c>start</c>, and the rules for mounting and taking up a gun, apart from the entity (the offline tests use
    /// them as the ship does: <see cref="EntityShip"/> in ShipGunPlaces.cs).
    /// </summary>
    public class GunPlaceStates
    {
        public readonly ShipDefinition Def;
        /// <summary>The places whose state differs from their design's start: id → mounted.</summary>
        public readonly Dictionary<string, bool> Delta = new();
        public int Version;

        public GunPlaceStates(ShipDefinition def) { Def = def; }

        public GunPlaceDef Place(string id)
        {
            if (id == null || Def?.GunPlaces == null) return null;
            foreach (var p in Def.GunPlaces) if (p.Id == id) return p;
            return null;
        }

        public GunPlaceDef PlaceOf(FixtureDef f) =>
            f?.Code != null && Def?.GunPlaceOf != null && Def.GunPlaceOf.TryGetValue(f.Code, out var p) ? p : null;

        public bool Laid(GunPlaceDef p) => p != null && (Delta.TryGetValue(p.Id, out bool l) ? l : p.StartsLaid);

        public void Set(GunPlaceDef p, bool laid)
        {
            if (p == null) return;
            if (laid == p.StartsLaid) Delta.Remove(p.Id); else Delta[p.Id] = laid;
            Version++;
        }

        /// <summary>A gun fixture that's there: a fixed gun (no place) always, a place's while it's mounted.</summary>
        public bool Mounted(FixtureDef f) => f?.Type == "gun" && (PlaceOf(f) is not GunPlaceDef p || Laid(p));

        public bool Absent(FixtureDef f) => f?.Type == "gun" && !Mounted(f);

        /// <summary>The gun fixtures' codes of the places without their gun, or null: all there.</summary>
        public string[] Gone()
        {
            if (Def?.GunPlaces == null || Def.GunPlaces.Length == 0) return null;
            var gone = Def.GunPlaces.Where(p => !Laid(p)).Select(p => p.Gun).ToArray();
            return gone.Length == 0 ? null : gone;
        }
    }

    public static class GunPlaces
    {
        /// <summary>
        /// The deck cells a place needs filled: those it lists, and every slot of its gun's deck under its working room
        /// (<see cref="MountLayout.GunRoom"/>), listed or not.
        /// </summary>
        public static List<int> Cells(ShipDefinition def, GunPlaceDef p)
        {
            var cells = new List<int>();
            var l = def?.Slots;
            var f = p == null ? null : def.Fixture(p.Gun);
            if (l == null || f?.Pos == null) return cells;
            if (p.Cells != null)
                foreach (var id in p.Cells)
                {
                    int n = l.IndexOf(id);
                    if (n >= 0 && !cells.Contains(n)) cells.Add(n);
                }
            foreach (int n in RoomCells(def, f))
                if (!cells.Contains(n)) cells.Add(n);
            return cells;
        }

        /// <summary>The slots of a gun's deck (their top at its foot) whose square its working room's footprint covers.</summary>
        public static List<int> RoomCells(ShipDefinition def, FixtureDef f)
        {
            var cells = new List<int>();
            var l = def?.Slots;
            if (l == null || f?.Pos == null) return cells;
            var room = MountLayout.GunRoom(f);
            const double e = 0.02;
            for (int n = 0; n < l.Count; n++)
            {
                var c = l.Slots[n].Box;
                if (c == null || !l.Slots[n].IsCell || Math.Abs(c[4] - f.Pos[1]) > 0.1) continue;
                foreach (var b in room)
                    if (b[0] < c[3] - e && b[3] > c[0] + e && b[2] < c[5] - e && b[5] > c[2] + e) { cells.Add(n); break; }
            }
            return cells;
        }

        /// <summary>
        /// Why a gun can't be mounted on a place (a lang code), or null: the place has a gun already, a cell it needs is
        /// out, or one of these boxes (what's put aboard: pieces, chests) is in its working room.
        /// </summary>
        /// <summary>
        /// (0.3.29, note 232) Railings (a breastwork on a castle deck's edge) count only against its carriage's part of the
        /// room (the trail's first <see cref="CarriageSlices"/> slices, its own boxes): its crew behind the gun captain step
        /// round a waist-high rail, not over the deck's edge (the design keeps the room on her deck).
        /// </summary>
        public const int CarriageSlices = 5;

        public static string MountProblem(ShipDefinition def, GunPlaceDef p, GunPlaceStates states, SlotStates slots, IEnumerable<double[]> things,
            IEnumerable<double[]> railings = null)
        {
            var f = p == null ? null : def?.Fixture(p.Gun);
            if (f?.Type != "gun") return "ships:gunplace-none";
            if (states.Laid(p)) return "ships:gunplace-taken";
            // ((0.3.28, D2.4) its port planked over: take the planking out first (an axe in building mode))
            if (p.Port != null && slots != null && def.Slots?.IndexOf(p.Port) is int pn && pn >= 0 && slots.Filled(pn)) return "ships:gunplace-port-covered";
            // ((0.3.29) the port itself in her side)
            if (p.GunPort != null && slots != null && def.Slots?.IndexOf(p.GunPort) is int gn && gn >= 0 && !slots.Filled(gn)) return "ships:gunplace-no-port";
            if (slots != null)
                foreach (int n in Cells(def, p))
                    if (!slots.Filled(n)) return "ships:gunplace-cell-empty";
            var room = MountLayout.GunRoom(f);
            if (things != null)
                foreach (var t in things)
                    if (t?.Length >= 6)
                        foreach (var b in room)
                            if (PiecePlanner.Overlap(t, b)) return "ships:gunplace-blocked";
            if (railings != null)
                foreach (var t in railings)
                    if (t?.Length >= 6)
                        for (int i = 0; i < room.Count; i++)
                            if ((i < CarriageSlices || i >= MountLayout.GunRoomSlices) && PiecePlanner.Overlap(t, room[i])) return "ships:gunplace-blocked";
            // (0.3.25) another gun mounted where this one's carriage would stand in its room, or in this one's room
            if (GunInTheWay(def, p, states) != null) return "ships:gunplace-gun-room";
            return null;
        }

        /// <summary>
        /// (0.3.25, the stern chasers) Do two guns stand in each other's way: either's carriage (its collision box, run out;
        /// else its selection box) in the other's working room (the trail's sweep to behind its gun captain, <see
        /// cref="MountLayout.GunRoom"/>), on one deck? Then only one of the two can be mounted (the stern chasers and the
        /// aftmost guns of the poop's broadsides). Guns abreast that only share the crews' room behind them don't.
        /// </summary>
        public static bool RoomsClash(FixtureDef a, FixtureDef b)
        {
            if (a?.Pos == null || b?.Pos == null || a == b || Math.Abs(a.Pos[1] - b.Pos[1]) > 0.3) return false;
            return StandsIn(a, MountLayout.GunRoom(b)) || StandsIn(b, MountLayout.GunRoom(a));
        }

        static bool StandsIn(FixtureDef g, List<double[]> room)
        {
            var b = g.Box?.Length >= 6 ? g.Box : g.Sel;
            if (b == null || b.Length < 6) return false;
            for (int i = 0; i < Math.Min(MountLayout.GunRoomSlices, room.Count); i++)
                if (PiecePlanner.Overlap(b, room[i])) return true;
            return false;
        }

        /// <summary>(0.3.25) The place whose mounted gun is in the way of mounting one on this place (<see cref="RoomsClash"/>), or null.</summary>
        public static GunPlaceDef GunInTheWay(ShipDefinition def, GunPlaceDef p, GunPlaceStates states)
        {
            var f = p == null ? null : def?.Fixture(p.Gun);
            if (f == null || def.GunPlaces == null) return null;
            foreach (var o in def.GunPlaces)
            {
                if (o == p || !states.Laid(o)) continue;
                if (RoomsClash(f, def.Fixture(o.Gun))) return o;
            }
            return null;
        }

        /// <summary>
        /// Why a place's gun can't be taken up (a lang code), or null: it must be there, cold (not reloading), housed (a
        /// lidded gun run in, its port shut), and nobody laying it or at its gun captain's place.
        /// </summary>
        public static string TakeProblem(ShipDefinition def, GunPlaceDef p, GunPlaceStates states, double reloading, bool runOut, bool beingLaid, bool manned)
        {
            var f = p == null ? null : def?.Fixture(p.Gun);
            if (f?.Type != "gun") return "ships:gunplace-none";
            if (!states.Laid(p)) return "ships:gunplace-no-gun";
            if (f.Lid != null && runOut) return "ships:gun-take-run-out";
            if (reloading > 0) return "ships:gun-take-hot";
            if (beingLaid) return "ships:gun-taken";
            if (manned) return "ships:gun-seat-taken";
            return null;
        }

        /// <summary>A design's gun places against the contract (docs/slots.md §7): what's wrong, empty if nothing.</summary>
        public static List<string> DesignProblems(ShipDefinition def)
        {
            var bad = new List<string>();
            if (def?.GunPlaces == null) return bad;
            var ids = new HashSet<string>();
            var guns = new HashSet<string>();
            foreach (var p in def.GunPlaces)
            {
                if (string.IsNullOrEmpty(p.Id)) { bad.Add("a gun place without an id"); continue; }
                if (!ids.Add(p.Id)) bad.Add($"{p.Id}: id twice");
                var f = def.Fixture(p.Gun);
                if (f?.Type != "gun") { bad.Add($"{p.Id}: gun '{p.Gun}' isn't a gun fixture"); continue; }
                if (!guns.Add(p.Gun)) bad.Add($"{p.Id}: gun '{p.Gun}' named by two places");
                if (p.Start != null && p.Start != "laid" && p.Start != "empty") bad.Add($"{p.Id}: start '{p.Start}' (laid or empty)");
                // ((0.3.28, D2.4) its port's cover: a deck slot of kind port for this gun; a laid place's port open)
                if (p.Port != null)
                {
                    int pn = def.Slots?.IndexOf(p.Port) ?? -1;
                    if (pn < 0 || !def.Slots.Slots[pn].IsPort) bad.Add($"{p.Id}: port {p.Port} isn't a port cover");
                    else if (def.Slots.Slots[pn].Gun != p.Gun) bad.Add($"{p.Id}: port {p.Port} is {def.Slots.Slots[pn].Gun}'s");
                    else if (p.StartsLaid && !def.Slots.Slots[pn].StartsEmpty) bad.Add($"{p.Id}: laid with its port planked over");
                }
                // its parts: some, the lid not among them
                var parts = def.GunOfPart.Where(kv => kv.Value == f.Code).Select(kv => kv.Key).ToList();
                if (parts.Count == 0) bad.Add($"{p.Id}: no moving parts of the gun (train:/elevate:/fire:{f.Code})");
                if (f.Lid != null && parts.Contains(f.Lid)) bad.Add($"{p.Id}: the lid {f.Lid} would be hidden with the gun");
                // its cells: slots of its deck under its room; every slot under its room listed; a laid place's filled
                var l = def.Slots;
                if (p.Cells != null && p.Cells.Length > 0 && l == null) bad.Add($"{p.Id}: lists cells but she has no deck slots");
                if (l != null)
                {
                    var under = RoomCells(def, f);
                    var listed = new List<int>();
                    foreach (var id in p.Cells ?? Array.Empty<string>())
                    {
                        int n = l.IndexOf(id);
                        if (n < 0) { bad.Add($"{p.Id}: cell {id} isn't a deck slot"); continue; }
                        listed.Add(n);
                        if (Math.Abs(l.Slots[n].Top - f.Pos[1]) > 0.1) bad.Add($"{p.Id}: cell {id} isn't on the gun's deck");
                        else if (!under.Contains(n)) bad.Add($"{p.Id}: cell {id} isn't under its working room");
                        if (p.StartsLaid && l.Slots[n].StartsEmpty) bad.Add($"{p.Id}: cell {id} starts empty under a laid gun");
                    }
                    var missing = under.Where(n => !listed.Contains(n)).Select(n => l.Slots[n].Id).ToList();
                    if (missing.Count > 0) bad.Add($"{p.Id}: slots under its working room not listed in cells: {string.Join(", ", missing.Take(6))}{(missing.Count > 6 ? "..." : "")}");
                    if (p.StartsLaid)
                        foreach (int n in under)
                            if (l.Slots[n].StartsEmpty) bad.Add($"{p.Id}: slot {l.Slots[n].Id} under it starts empty");
                }
            }
            return bad;
        }
    }
}
