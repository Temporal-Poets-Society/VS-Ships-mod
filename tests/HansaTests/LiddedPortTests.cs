using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.22, step C; docs/slots.md §7) A lidded gun port cut in a room's side: with the lid shut the room is sealed
        /// (ShipCompartments.Sealed fills the port), with it open it isn't, and without the lid (the same cut, no gun) it's
        /// open too (the cut is real). A synthetic gun fixture put in the room's side by the floor place nearest it, its port
        /// cut in the given collision (her closures already in it), taken out again after.
        /// </summary>
        static void LiddedPortSeal(ShipDefinition def, VoxelGrid closed, IEnumerable<ShipPiece> pieces, RoomDef r, string what, Func<MountPoint, bool> free = null)
        {
            var l = def.Mounts;
            var deck = l?.FloorOf(r);
            var floor = l?.Floors.Where(m => m.Room == r.Code && (free == null || free(m))).OrderByDescending(m => Math.Abs(m.Z + 0.5)).FirstOrDefault();
            if (deck == null || floor == null) { Check(false, $"{what}: {r.Code} has a floor and a floor place for the test port"); return; }
            double fx = floor.X + 0.5, fz = floor.Z + 0.5;
            int side = fz < 0 ? -1 : 1;
            double sill = deck.Y + 0.5, head = deck.Y + 1.25, my = (sill + head) / 2;
            // her side there (the design's collision): its inner face, then its outer one
            double zin = double.NaN, zout = double.NaN;
            for (double z = fz; Math.Abs(z) < 12; z += side * def.Grid.V / 2)
            {
                bool solid = def.Grid.SolidAt(fx, my, z);
                if (double.IsNaN(zin)) { if (solid) zin = Math.Abs(z); }
                else if (!solid) { zout = Math.Abs(z); break; }
            }
            if (double.IsNaN(zout)) { Check(false, $"{what}: her side found beside {r.Code}'s floor place at x {fx:0.00}"); return; }
            zin = Math.Floor(zin / def.Grid.V) * def.Grid.V - 0.01;      // (the voxels' faces, a hair beyond)
            zout = Math.Ceiling(zout / def.Grid.V) * def.Grid.V + 0.01;
            var port = new FixtureDef
            {
                Type = "gun", Code = "test-lidded-port", Lid = "test-lidded-port-lid",
                Pos = new[] { fx, deck.Y, side * (zin - 0.75) }, Out = new[] { 0.0, 0, side },
                Port = new[] { fx - 0.375, sill, fx + 0.375, head }, Wall = new[] { zin, zin, zout, zout },
            };
            var keep = def.Fixtures;
            def.Fixtures = keep.Append(port).ToArray();
            try
            {
                var cut = closed.Clone();
                var pb = ShipCompartments.PortBox(port);
                cut.Fill(pb, false);
                double sx = floor.X + 0.5, sy = deck.Y + 0.1, sz = floor.Z + 0.5;
                bool before = ShipCompartments.Sealed(l, closed, pieces, r, null, sx, sy, sz);
                string why = null;
                ShipCompartments.Why = w => why = w;
                bool shut = ShipCompartments.Sealed(l, cut, pieces, r, null, sx, sy, sz);
                string whyShut = why;
                bool open = ShipCompartments.Sealed(l, cut, pieces, r, null, sx, sy, sz, f => f == port);
                ShipCompartments.Why = null;
                def.Fixtures = keep;
                bool bare = ShipCompartments.Sealed(l, cut, pieces, r, null, sx, sy, sz);
                Check(before && shut && !open && !bare,
                    $"{what}: a lidded port cut in {r.Code}'s side (x {pb[0]:0.00}..{pb[3]:0.00}, y {pb[1]:0.00}..{pb[4]:0.00}, |z| {zin:0.00}..{zout:0.00}): " +
                    $"sealed before ({before}), lid shut sealed ({shut}{(shut ? "" : ": open " + whyShut)}), lid open not ({!open}), the cut alone without a lid not ({!bare})");
            }
            finally { def.Fixtures = keep; ShipCompartments.Why = null; }
        }

        /// <summary>(0.3.22, step C) The synthetic lidded port in the carrack's lower deck (her design room, no pieces; hatches and doors shut).</summary>
        static void LiddedPortSealCarrack(ShipDefinition def)
        {
            Console.WriteLine("lidded gun ports and her rooms (0.3.22, step C)");
            var closed = def.Grid.Clone();
            ShipFittings.ApplySolids(def, closed, f => false, f => null);
            var lower = def.Rooms.FirstOrDefault(r => r.Code == "lowerdeck");
            if (lower == null) { Check(false, $"{def.Code}: a lower deck"); return; }
            LiddedPortSeal(def, closed, Array.Empty<ShipPiece>(), lower, def.Code);
        }

        /// <summary>
        /// (0.3.22, step C) A design's lidded gun ports in her closed rooms (carrack-m's great-cabin guns): her rooms sealed
        /// with every lid shut (Sealed's default), each lid opened alone opens its room, and the sea comes in through it
        /// only while it's open (a downflood point at its sill tied to the gun).
        /// </summary>
        static void LiddedPortRooms(ShipDefinition md, VoxelGrid shut, IEnumerable<ShipPiece> pieces, Func<RoomDef, MountPoint> freeFloor)
        {
            var ports = ShipCompartments.LiddedPorts(md).Where(f => ShipCompartments.PortRoom(md, f)?.Enclosed == true).ToList();
            if (ports.Count == 0) { Console.WriteLine($"         ({md.Code}: no lidded gun ports in her closed rooms)"); return; }
            var dl = md.DamageParts;
            foreach (var f in ports)
            {
                var r = ShipCompartments.PortRoom(md, f);
                var pb = ShipCompartments.PortBox(f);
                int empty = 0, all = 0;
                for (double x = pb[0] + md.Grid.V / 2; x < pb[3]; x += md.Grid.V)
                    for (double y = pb[1] + md.Grid.V / 2; y < pb[4]; y += md.Grid.V)
                    {
                        all++;
                        bool through = true;
                        for (double z = pb[2] + md.Grid.V / 2; z < pb[5] && through; z += md.Grid.V) through = !md.Grid.SolidAt(x, y, z);
                        if (through) empty++;
                    }
                bool cut = empty * 2 >= all;
                var fl = freeFloor(r);
                bool opened = fl != null && !ShipCompartments.Sealed(md.Mounts, shut, pieces, r, ShipCompartments.WellLeaks(md, r, pieces), fl.X + 0.5, fl.Y + 0.6, fl.Z + 0.5, g => g == f);
                bool shutSealed = fl != null && ShipCompartments.Sealed(md.Mounts, shut, pieces, r, ShipCompartments.WellLeaks(md, r, pieces), fl.X + 0.5, fl.Y + 0.6, fl.Z + 0.5, g => false);
                var points = dl.Downflood.Where(p => p.OpenFixture == f.Code).ToList();
                var pose = new FloodPose { X = 0, Y = 0, Z = 0, Surface = (x, z) => pb[1] + 0.3 };
                double others = ShipFlood.Downflooding(dl, pose, c => false), through2 = ShipFlood.Downflooding(dl, pose, c => c == f.Code);
                Check(shutSealed && opened && points.Count > 0 && through2 > others,
                    $"{md.Code}: {f.Code}'s port ({(cut ? "cut" : "not cut")} in her collision, {empty}/{all} columns through) in {r.Code}: lid shut sealed ({shutSealed}), " +
                    $"opened alone the room's open ({opened}); {points.Count} downflood points at its sill, the sea 0.3 over it pours in only while open ({through2 - others:0.000} more a second)");
            }
            // (all her ports cut through her side, as the generator will: still sealed with the lids shut, each one open opens it)
            var allCut = shut.Clone();
            foreach (var f in ports) allCut.Fill(ShipCompartments.PortBox(f), false);
            foreach (var r in ports.Select(f => ShipCompartments.PortRoom(md, f)).Distinct())
            {
                var fl = freeFloor(r);
                if (fl == null) continue;
                var leaks = ShipCompartments.WellLeaks(md, r, pieces);
                string why = null;
                ShipCompartments.Why = w => why = w;
                bool sealedCut = ShipCompartments.Sealed(md.Mounts, allCut, pieces, r, leaks, fl.X + 0.5, fl.Y + 0.6, fl.Z + 0.5);
                ShipCompartments.Why = null;
                int opens = ports.Count(f => ShipCompartments.PortRoom(md, f) == r &&
                                             !ShipCompartments.Sealed(md.Mounts, allCut, pieces, r, leaks, fl.X + 0.5, fl.Y + 0.6, fl.Z + 0.5, g => g == f));
                int mine = ports.Count(f => ShipCompartments.PortRoom(md, f) == r);
                Check(sealedCut && opens == mine, $"{md.Code}: {r.Code} with all {mine} of its ports cut through her side: sealed with the lids shut ({sealedCut}{(sealedCut ? "" : ": open " + why)}), each lid open opens it ({opens} of {mine})");
            }
        }
    }
}
