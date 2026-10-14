using System;
using System.Linq;
using Hansa;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.13, the user: "rain is in the lower deck") The rain by what's overhead (ShipCover.Above): in the
        /// design (her hatches stand open in it) the lower deck is covered everywhere but under the waist hatch, the hold
        /// everywhere but under both hatches, the great cabin everywhere, the open waist nowhere; and the weather drawn
        /// through the opening (ThroughOpening) comes down through it and is put back afterwards.
        /// </summary>
        static void Rain(ShipDefinition def)
        {
            Console.WriteLine("rain: under her decks by what's overhead");
            var g = def.Grid;
            var up = new Vec3d(0, 1, 0);
            var waist = (def.Fixture("hatch-waist") is FixtureDef wf ? wf.Sel ?? wf.Box : null);
            var holdHatch = (def.Fixture("hatch-hold") is FixtureDef hf ? hf.Sel ?? hf.Box : null);
            Check(waist != null && holdHatch != null, "she has a waist hatch and a hold hatch");
            if (waist == null || holdHatch == null) return;
            bool Under(double[] b, double x, double z) => x >= b[0] - 0.25 && x <= b[3] + 0.25 && z >= b[2] - 0.25 && z <= b[5] + 0.25;

            // every place to stand in a room: its head 1.6 over a floor, in the air, inside her
            // (inside her: her sides both ways at the head's height; a room's box is wider than her sides up high)
            bool Inside(double x, double y, double z) =>
                g.Raycast(new Vec3d(x, y, z), new Vec3d(0, 0, 1), 8) < 8 && g.Raycast(new Vec3d(x, y, z), new Vec3d(0, 0, -1), 8) < 8;
            (int covered, int open, int stray) Survey(string room, System.Func<double, double, bool> mayBeOpen, double fixedHead = double.NaN)
            {
                var r = def.Rooms.FirstOrDefault(q => q.Code == room);
                int c = 0, o = 0, s = 0;
                if (r == null) return (0, 0, 0);
                for (double x = r.Min[0] + 0.25; x < r.Max[0]; x += 0.5)
                    for (double z = r.Min[2] + 0.25; z < r.Max[2]; z += 0.5)
                    {
                        // the floor under the room's middle height (or a height given: over the hold's hatch the lower deck has none)
                        double head;
                        if (double.IsNaN(fixedHead))
                        {
                            double y = r.Min[1] + 1.0;
                            while (y > r.Min[1] - 0.6 && !g.SolidAt(x, y - 0.01, z)) y -= g.V;
                            if (!g.SolidAt(x, y - 0.01, z) || g.SolidAt(x, y + 0.1, z)) continue;
                            head = y + 1.6;
                        }
                        else head = fixedHead;
                        if (g.SolidAt(x, head, z) || def.RoomAt(x, head, z)?.Code != room || !Inside(x, head, z)) continue;
                        if (ShipCover.Above(g, x, head, z, up) < double.MaxValue) c++;
                        else { o++; if (!mayBeOpen(x, z)) { s++; if (s <= 3) Console.WriteLine($"         open at {x:0.00} {head:0.00} {z:0.00}"); } }
                    }
                return (c, o, s);
            }
            var lowerRoom0 = def.Rooms.First(q => q.Code == "lowerdeck");
            var lower = Survey("lowerdeck", (x, z) => Under(waist, x, z), lowerRoom0.Min[1] + 0.25 + 1.6);
            Check(lower.covered > 100 && lower.open > 0 && lower.stray == 0,
                $"the lower deck: covered everywhere ({lower.covered} places) but under the open waist hatch ({lower.open}, {lower.stray} elsewhere)");
            var hold = Survey("hold", (x, z) => Under(waist, x, z) && Under(holdHatch, x, z));
            Check(hold.covered > 100 && hold.stray == 0,
                $"the hold: covered everywhere ({hold.covered}) but under both hatches open ({hold.open}, {hold.stray} elsewhere)");
            var cabin = Survey("greatcabin", (x, z) => false);
            
            Check(cabin.covered > 20 && cabin.open == 0, $"the great cabin: covered everywhere ({cabin.covered}, open {cabin.open})");
            double deckHead = def.DeckY + 1.6;
            var lowerRoom = def.Rooms.First(q => q.Code == "lowerdeck");
            double lowerHead = lowerRoom.Min[1] + 0.25 + 1.6;
            double hx = (waist[0] + waist[3]) / 2, hz = (waist[2] + waist[5]) / 2;
            Check(ShipCover.Above(g, 0.5, deckHead, 2.0, up) == double.MaxValue, "on her open waist there's nothing overhead");
            // rolled 10° and pitched 3°: the world's up in her frame, still out through the hatch
            ShipFrame.ToLocal(0, 3 * Math.PI / 180, 10 * Math.PI / 180, 0, 1, 0, out double ux, out double uy, out double uz);
            Check(ShipCover.Above(g, hx, lowerHead, hz, up) == double.MaxValue && ShipCover.Above(g, hx, lowerHead, hz, new Vec3d(ux, uy, uz)) == double.MaxValue,
                "under the open waist hatch the sky is overhead, level and rolled 10°");
            // shut, the glazed hatch covers it (as on a ship: ShipFittings fills it in)
            var shut = g.Clone();
            shut.Fill(def.Fixture("hatch-waist").Box);
            double t = ShipCover.Above(shut, hx, lowerHead, hz, up);
            Check(t < def.DeckY + 1 - lowerHead, $"the hatch shut, it's overhead ({t:0.00} up)");

            // the weather through the opening: a rain box made the opening's, the drops falling through it, and put back
            var rain = new SimpleParticleProperties
            {
                MinPos = new Vec3d(-30, 15, -30), AddPos = new Vec3d(60, 9, 60), MinQuantity = 1200, AddQuantity = 25,
                MinVelocity = new Vec3f(-0.025f + 8 * 0.4f, -10, -0.025f + 8 * -0.2f), AddVelocity = new Vec3f(0.05f, 0.05f, 0.05f), LifeLength = 1.5f
            };
            double[] box = { 10, 20, 12, 23 };
            double headY = 70;
            var counting = new SpawnLog();
            var through = new ThroughOpening(counting, box, headY);
            through.Spawn(rain);
            var seen = counting.Last;
            bool restored = rain.MinPos.X == -30 && rain.AddPos.X == 60 && rain.MinQuantity == 1200;
            // a drop from the middle of the reshaped box, falling at the mean velocity, at the head's height
            double vx = 8 * 0.4, vz = 8 * -0.2, vy = -10 + 0.025;
            double tf = (seen.MinY + seen.AddY / 2 - headY) / -vy;
            double dx = seen.MinX + seen.AddX / 2 + vx * tf, dz = seen.MinZ + seen.AddZ / 2 + vz * tf;
            Check(restored && dx >= box[0] && dx <= box[2] && dz >= box[1] && dz <= box[3] && seen.MinY > headY + 2 && seen.Quantity < 10 && seen.Quantity > 0,
                $"rain through the opening: it starts {seen.MinY - headY:0.0} over the head, comes down at ({dx:0.0}, {dz:0.0}) in the opening, {seen.Quantity:0.0} drops a tick; vanilla's box put back ({restored})");
            var splash = new SimpleParticleProperties { MinPos = new Vec3d(3, 64, 3), AddPos = new Vec3d(0, 0, 0), MinQuantity = 1 };
            through.Spawn(splash);
            Check(counting.Last.MinX == 3 && counting.Count == 2, "a splash (a small box) goes through as it is");
        }

        sealed class SpawnLog : IAsyncParticleManager
        {
            public (double MinX, double MinY, double MinZ, double AddX, double AddY, double AddZ, float Quantity) Last;
            public int Count;
            public IBlockAccessor BlockAccess => null;
            public int ParticlesAlive(EnumParticleModel model) => 0;
            public int Spawn(IParticlePropertiesProvider p)
            {
                Count++;
                if (p is SimpleParticleProperties sp) Last = (sp.MinPos.X, sp.MinPos.Y, sp.MinPos.Z, sp.AddPos.X, sp.AddPos.Y, sp.AddPos.Z, sp.MinQuantity);
                return 1;
            }
        }

        /// <summary>
        /// (0.3.13, "there are still air bubbles from water in the cargo deck") The water specks are judged over their life
        /// (DrySweep.SweptDry): a speck just ahead of her bow at the hold's depth is in her a moment later while she
        /// sails, not while she lies still; one just outside her side is swept by her roll; well clear of her it isn't.
        /// </summary>
        static void SweptSpecks(ShipDefinition def)
        {
            Console.WriteLine("water specks swept into her hold");
            var dry = def.DryMask;
            var sweep = def.DrySweepMask;
            Check(sweep != null && sweep.CountSolid() > dry.CountSolid(), $"her inside grown for her roll and heave ({dry.CountSolid()} → {sweep?.CountSolid()} cells)");
            if (sweep == null) return;
            // her way is along her keel: local +x (the bow) is (sin yaw, cos yaw) in the world, as EntityShip.ClientFrame moves her
            double yaw = 0.7;
            ShipFrame.ToWorld(yaw, 0, 0, 1, 0, 0, out double bx, out _, out double bz);
            Check(Math.Abs(bx - Math.Sin(yaw)) < 1e-9 && Math.Abs(bz - Math.Cos(yaw)) < 1e-9, "her way is along her keel");

            // the hold's depth: the stem's outside there
            double y = -2.5;
            int j = dry.CellY(y), k = dry.CellZ(0.01);
            int i = dry.NX - 1;
            while (i > 0 && !dry.Solid(i, j, k)) i--;
            double stem = dry.OX + (i + 1) * dry.V;
            double speed = 3;
            bool At(double lx, double ly, double lz, double v, double life = 2)
            {
                // ship at (100, 60, 200) heading yaw; the place given ship-local, put in the world
                ShipFrame.ToWorld(yaw, lx, ly, lz, out double wx, out double wy, out double wz);
                return DrySweep.SweptDry(def, sweep, 100, 60, 200, yaw, 0, 0, Math.Sin(yaw) * v, Math.Cos(yaw) * v, wx + 100, wy + 60, wz + 200, life);
            }
            Check(At(stem + 3, y, 0, speed) && !At(stem + 3, y, 0, 0) && !At(stem + 9, y, 0, speed),
                $"a speck 3 ahead of her stem at the hold's depth: in her within two seconds at {speed} a second, not lying still; 9 ahead it isn't");
            // her side at the hold's depth amidships
            int ks = dry.NZ - 1, im = dry.CellX(5.0);
            while (ks > 0 && !dry.Solid(im, j, ks)) ks--;
            double side = dry.OZ + (ks + 1) * dry.V;
            Check(At(5.0, y, side + 0.3, 0) && !At(5.0, y, side + 1.5, 0) && !dry.SolidAt(5.0, y, side + 0.3),
                $"0.3 outside her side (z {side:0.00}) is swept by her roll, 1.5 out isn't");
            Check(At(5.0, def.Decks.FirstOrDefault(d => d.Code == "hold")?.Y + 0.5 ?? -3.4, 0, 0), "in her hold it's dry");
        }

        /// <summary>
        /// (0.3.13, item 4: "The wind's too strong for the sail she has set…" four times in a row) The sails' strain notice
        /// is said once per episode (EpisodeNotice): gusts taking the strain on and off every few seconds for ten minutes
        /// say it once; after a quiet minute a fresh episode is said again.
        /// </summary>
        static void StrainNotice()
        {
            Console.WriteLine("the sails' strain notice: once an episode");
            var n = new EpisodeNotice();
            int said = 0;
            for (double t = 0; t < 600; t += 0.25)
                if (n.Due(t % 8 < 5, t)) said++;
            int quiet = 0;
            for (double t = 600; t < 670; t += 0.25)
                if (n.Due(false, t)) quiet++;
            int again = 0;
            for (double t = 670; t < 680; t += 0.25)
                if (n.Due(true, t)) again++;
            Check(said == 1 && quiet == 0 && again == 1, $"gusty for ten minutes: said {said} time(s) (1); quiet, then straining again: said {again} more (1)");
        }
    }
}
