using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    /// <summary>
    /// For the Sea mod: the inside of the ships' hulls below the main deck is dry though the world has water there (the
    /// ship is an entity, not blocks). Sea asks before it puts the camera under water (fog, tint, muffled sound) and
    /// before a creature's head in water runs out of air. Registered on both sides; asked every tick and maybe from
    /// another thread, so it works from a snapshot of the ships and a lookup in each design's <see cref="ShipDefinition.DryMask"/>.
    /// </summary>
    public class ShipDrySpaces
    {
        volatile EntityShip[] ships = Array.Empty<EntityShip>();

        /// <summary>The ships on this side, taken again whenever one comes or goes.</summary>
        public void Set(IEnumerable<EntityShip> list) => ships = list.ToArray();

        /// <summary>(22 October 2026) Any ships on this side (ShipDryParticles asks before it looks at a particle's block).</summary>
        public bool Any => ships.Length > 0;

        public bool IsDry(double x, double y, double z)
        {
            foreach (var ship in ships)
            {
                var mask = ship.Def?.DryMask;
                // (a ship going down, or on the bottom, is full of the sea)
                if (mask == null || !ship.Alive || ship.GoneDown) continue;
                double dx = x - ship.Pos.X, dz = z - ship.Pos.Z;
                if (dx * dx + dz * dz > 30 * 30) continue;
                // below the water in her hold it's wet (you can drown in a flooded hold): level in the world, as it's drawn
                if (ship.Flooded && y < ship.FloodWorldY) continue;
                // tilted on clients (where it's drawn), level on the server
                ship.WorldToLocal(x, y, z, out double lx, out double ly, out double lz);
                if (mask.SolidAt(lx, ly, lz)) return true;
                if (DrySweep.DryLocal(ship.Def, mask, lx, ly, lz)) return true;
            }
            return false;
        }

        /// <summary>
        /// (0.3.13, "there are still air bubbles from water in the cargo deck") For Sea's particles in and on the water: is
        /// this world place inside one of the hulls at any moment of the next seconds? Vanilla's water specks stand still in
        /// the world's water for two seconds, and she sails, rolls and heaves through them: those spawned ahead of her bow,
        /// beside her flaring sides or under her keel were inside her hold a moment later (Sea judged them only where they
        /// were spawned, outside her). See <see cref="DrySweep.SweptDry"/>.
        /// </summary>
        public bool IsDryWithin(double x, double y, double z, double seconds)
        {
            foreach (var ship in ships)
            {
                var def = ship.Def;
                var mask = def?.DrySweepMask;
                if (mask == null || !ship.Alive || ship.GoneDown) continue;
                double dx = x - ship.Pos.X, dz = z - ship.Pos.Z;
                if (dx * dx + dz * dz > 34 * 34) continue;
                if (ship.Flooded && y < ship.FloodWorldY) continue;
                // her way over the ground: along her heading and her leeway (as she's moved on clients: EntityShip.ClientFrame)
                double yaw = ship.Pos.Yaw;
                double vx = Math.Sin(yaw) * ship.Speed - Math.Cos(yaw) * ship.Leeway;
                double vz = Math.Cos(yaw) * ship.Speed + Math.Sin(yaw) * ship.Leeway;
                if (DrySweep.SweptDry(def, mask, ship.Pos.X, ship.Pos.Y, ship.Pos.Z, yaw, ship.Pitch, ship.Roll, vx, vz, x, y, z, seconds)) return true;
            }
            return false;
        }
    }
}
