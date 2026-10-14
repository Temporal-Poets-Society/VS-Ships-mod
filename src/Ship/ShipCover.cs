using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.3.13, the user: "rain is in the lower deck") The weather under her decks, by what's overhead: is anything of hers
    /// straight up from a place (a deck, a castle's roof, the glazed hatch shut), and the weather drawn through an open
    /// hatch only (<see cref="ThroughOpening"/>). No client API beyond the particle types, so the offline tests check it;
    /// ShipShelter and the patches use it.
    /// </summary>
    public static class ShipCover
    {
        /// <summary>How far up a deck, a castle's roof or anything else of hers shelters someone (her tops, high on the masts, don't).</summary>
        public const double Range = 8.0;

        /// <summary>
        /// Is there anything of hers straight up from this place (ship-local; her collision grid as it is now, with her
        /// hatches, doors and what's on her gallows)? Up is the world's, in her frame (she rolls). How far up it is, or
        /// MaxValue under the open sky.
        /// </summary>
        public static double Above(VoxelGrid grid, double x, double y, double z, Vec3d up)
        {
            if (grid == null) return double.MaxValue;
            double t = grid.Raycast(new Vec3d(x, y, z), up, Range);
            return t <= Range ? t : double.MaxValue;
        }
    }

    /// <summary>
    /// (0.3.13) A particle manager in front of the game's while the player stands under an open hatch in one of her rooms:
    /// a wide weather box (rain, hail, snow, storm dust: 10 or more across) is spawned over the opening instead, its drops
    /// starting a few blocks over the player's head (over her deck) and upwind of it by their drift, as many as fall on
    /// that much of the box. Splashes and the rest (small boxes) go through as they are (Sea leaves out those inside her
    /// hull). Vanilla's box is put back after each spawn.
    /// </summary>
    public sealed class ThroughOpening : IAsyncParticleManager
    {
        readonly IAsyncParticleManager inner;
        readonly double[] box;
        readonly double headY;
        /// <summary>How far over the head the drops start (over her deck, under the open sky).</summary>
        public const double Rise = 4.0;

        /// <param name="box">the opening, world x0 z0 x1 z1</param>
        /// <param name="headY">the player's head, world</param>
        public ThroughOpening(IAsyncParticleManager inner, double[] box, double headY) { this.inner = inner; this.box = box; this.headY = headY; }

        public IBlockAccessor BlockAccess => inner.BlockAccess;
        public int ParticlesAlive(EnumParticleModel model) => inner.ParticlesAlive(model);

        public int Spawn(IParticlePropertiesProvider p)
        {
            if (p is not SimpleParticleProperties sp || !Wide(sp)) return inner.Spawn(p);
            var minPos = sp.MinPos.Clone();
            var addPos = sp.AddPos.Clone();
            float q = sp.MinQuantity, aq = sp.AddQuantity;
            try
            {
                Reshape(sp, box, headY);
                return inner.Spawn(p);
            }
            finally
            {
                sp.MinPos.Set(minPos);
                sp.AddPos.Set(addPos);
                sp.MinQuantity = q;
                sp.AddQuantity = aq;
            }
        }

        public static bool Wide(SimpleParticleProperties sp) =>
            sp.MinPos != null && sp.AddPos != null && Math.Abs(sp.AddPos.X) >= 10 && Math.Abs(sp.AddPos.Z) >= 10;

        /// <summary>The weather box made the opening's (box x0 z0 x1 z1, world): where, how high and how many.</summary>
        public static void Reshape(SimpleParticleProperties sp, double[] box, double headY)
        {
            double w = Math.Max(0.5, box[2] - box[0]), d = Math.Max(0.5, box[3] - box[1]);
            double area = Math.Max(1, Math.Abs(sp.AddPos.X * sp.AddPos.Z));
            float k = (float)Math.Min(1, w * d / area);
            double vx = (sp.MinVelocity?.X ?? 0) + (sp.AddVelocity?.X ?? 0) / 2, vz = (sp.MinVelocity?.Z ?? 0) + (sp.AddVelocity?.Z ?? 0) / 2;
            double vy = Math.Min(-1, (sp.MinVelocity?.Y ?? -1) + (sp.AddVelocity?.Y ?? 0) / 2);
            double t = Rise / -vy;
            sp.MinPos.Set(box[0] - vx * t, headY + Rise, box[1] - vz * t);
            sp.AddPos.Set(w, 1, d);
            sp.MinQuantity *= k;
            sp.AddQuantity *= k;
        }
    }

    /// <summary>
    /// (0.3.13, "there are still air bubbles from water in the cargo deck") Vanilla's water specks stand still in the
    /// world's water for two seconds, and she sails, rolls and heaves through them: is a place inside her at any moment of
    /// a speck's life? For ShipDrySpaces.IsDryWithin (Sea's particles); no client API, so the offline tests check it.
    /// </summary>
    public static class DrySweep
    {
        /// <summary>
        /// How far her sides, bottom and decks sweep through the water in a rolling, heaving sea over a speck's life (two
        /// seconds), past where she is now: <see cref="ShipDefinition.DrySweepMask"/>.
        /// </summary>
        public const double Margin = 0.5;
        /// <summary>How often along a speck's life her place is looked at (seconds).</summary>
        public const double Step = 0.25;

        /// <summary>
        /// Dry at this ship-local place, by a mask of her inside: in it, or on deck and in the castles over it. Heaving,
        /// pitching and rolling in a big sea she dips her decks under the level the world's water blocks stand at, and the
        /// sea doesn't come aboard for that (no splashing, bubbles, underwater view or drowning there).
        /// </summary>
        public static bool DryLocal(ShipDefinition def, VoxelGrid mask, double x, double y, double z)
        {
            if (mask.SolidAt(x, y, z)) return true;
            double deck = def.DeckY;
            return y >= deck - 0.25 && y <= deck + 6.5 && mask.SolidAt(x, deck - 0.5, z);
        }

        /// <summary>
        /// A place standing still in the world (x y z) and a ship at (sx sy sz), turned (yaw pitch roll), making way (vx vz)
        /// a second: is it inside her (the mask given, normally <see cref="ShipDefinition.DrySweepMask"/>: her inside grown
        /// by <see cref="Margin"/> for her roll and heave) at any moment of the next seconds? Every <see cref="Step"/> it's
        /// where she'll be then (moved on, turned as now).
        /// </summary>
        public static bool SweptDry(ShipDefinition def, VoxelGrid mask, double sx, double sy, double sz, double yaw, double pitch, double roll,
            double vx, double vz, double x, double y, double z, double seconds)
        {
            int steps = (int)Math.Ceiling(Math.Clamp(seconds, 0, 10) / Step);
            for (int s = 0; s <= steps; s++)
            {
                double t = Math.Min(seconds, s * Step);
                ShipFrame.ToLocal(yaw, pitch, roll, x - sx - vx * t, y - sy, z - sz - vz * t, out double lx, out double ly, out double lz);
                if (DryLocal(def, mask, lx, ly, lz)) return true;
                if (Math.Abs(vx) + Math.Abs(vz) < 1e-6) break;    // (lying still: where she is now is where she'll be)
            }
            return false;
        }
    }
}
