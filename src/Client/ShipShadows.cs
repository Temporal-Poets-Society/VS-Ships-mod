using System;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// What of a ship goes into the sun's shadow maps (ShipRenderer draws it there, in her drawn pose): what shapes her
    /// shadow. The inside of the hull (its inside planking, the cabins' own faces, the cargo, the bunks) is in her own
    /// shadow anyway and is left out, and so is glass (vanilla glass casts none either). The far map (the shadows past
    /// the near map's 30-odd blocks) also leaves out the ropes that move and the small things on deck.
    /// No client API here, so the offline tests check it.
    /// </summary>
    public static class ShipShadows
    {
        /// <summary>
        /// How far her image in the near and the far shadow map is pushed away from the sun (blocks). It changes only the
        /// depth she's stored at, not where her shadow falls, and keeps her sunlit sides from shadowing themselves
        /// (stripes on the curved hull). The engine's own allowance is about 0.1 (near) and 0.2 (far) blocks.
        /// </summary>
        public const float NearDepthPush = 0.1f, FarDepthPush = 0.3f;

        /// <summary>
        /// Does this smooth-mesh group, drawn with her model matrix alone, cast a shadow? Her planking outside, the masts
        /// and the standing rigging (exterior) and the bottom in both maps; a figurehead in the near one only; never the
        /// inside planking or the glass. (Moving parts and sails have their own rules.)
        /// </summary>
        public static bool CastsSkin(string group, bool far)
        {
            if (group == null || group == "interior" || group == "glass" || group.StartsWith("glass:")) return false;
            if (group == "exterior" || group == "bottom" || group == "bottomclad") return true;
            return !far;
        }

        /// <summary>
        /// Does this moving part cast a shadow? Not the ones inside her (the tiller and its ropes under the quarterdeck,
        /// the hold hatch). The far map also leaves out the ropes that follow a part (braces, sheets, the anchor cable):
        /// lines a few hundredths of a block thick, far off.
        /// </summary>
        public static bool CastsPart(MovingPartDef part, bool far) => part != null && !part.Interior && !(far && part.Kind == "follow");

        /// <summary>
        /// Does a vanilla block aboard cast a shadow? Nothing indoors (<see cref="ShipDefinition.Indoors"/>) and no glass.
        /// Doors, gates and the boats on the gallows in both maps; the small things on deck (a lantern on a mast, a
        /// chest on the quarterdeck, the plate by the helm) in the near one only.
        /// </summary>
        public static bool CastsFixture(FixtureDef f, bool indoors, bool glass, bool far)
        {
            if (f == null || indoors || glass || f.Type == "window") return false;
            if (f.Type == "door" || f.Type == "gate" || f.Kind == "boat") return true;
            return !far;
        }

        /// <summary>
        /// A sphere round all of her that's drawn (a ship-local centre and a radius): her bounds, with room for the anchor
        /// let go. A ship whose sphere is outside a shadow map's box isn't drawn into it.
        /// </summary>
        public static void Sphere(ShipDefinition def, out double cx, out double cy, out double cz, out double radius)
        {
            double x0, y0, z0, x1, y1, z1;
            var b = def?.Bounds;
            if (b?.Length == 2 && b[0]?.Length == 3 && b[1]?.Length == 3)
            {
                x0 = b[0][0]; y0 = b[0][1]; z0 = b[0][2];
                x1 = b[1][0]; y1 = b[1][1]; z1 = b[1][2];
            }
            else if (def?.Grid != null)
            {
                var g = def.Grid;
                x0 = g.OX; y0 = g.OY; z0 = g.OZ;
                x1 = g.MaxX; y1 = g.MaxY; z1 = g.MaxZ;
            }
            else
            {
                cx = cy = cz = 0;
                radius = Math.Max(def?.Length ?? 0, 8);
                return;
            }
            cx = (x0 + x1) / 2;
            cy = (y0 + y1) / 2;
            cz = (z0 + z1) / 2;
            double slide = 0;
            if (def.MovingParts != null)
                foreach (var p in def.MovingParts) if (p.Kind == "slide") slide = Math.Max(slide, Math.Abs(p.Distance));
            double dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
            radius = 0.5 * Math.Sqrt(dx * dx + dy * dy + dz * dz) + slide + 1;
        }

        /// <summary>
        /// The matrix the engine's shadow shader takes (its mvpMatrix, with its origin at 0): the shadow map's projection ×
        /// the sun's view × her model matrix (camera-relative, the same as the normal pass), her image moved
        /// <paramref name="push"/> blocks further from the sun after the view.
        /// </summary>
        public static void ShadowMvp(float[] mvp, float[] projection, float[] view, float[] model, float push)
        {
            Mat4f.Mul(mvp, view, model);
            // translate(0, 0, -push) × that: the view looks along its -z, away from the sun
            for (int k = 0; k < 4; k++) mvp[4 * k + 2] -= push * mvp[4 * k + 3];
            Mat4f.Mul(mvp, projection, mvp);
        }
    }
}
