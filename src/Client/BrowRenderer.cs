using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.3.27, note 219) Her brows drawn: each a plank 0.7 wide from the entry port's sill to its end on the quay (its top
    /// on the line walked), cleats across it every 0.4, a rope rail on each side on short stanchions; in her frame, lit as
    /// outside. Built again when her brows change.
    /// </summary>
    public partial class ShipRenderer
    {
        readonly Dictionary<long, (int Version, MultiTextureMeshRef Mesh)> browMeshes = new();

        void UpdateBrows(EntityShip ship)
        {
            browMeshes.TryGetValue(ship.EntityId, out var bm);
            if (bm.Mesh != null && bm.Version == ship.BrowVersion) return;
            if (bm.Mesh == null && bm.Version == ship.BrowVersion && ship.Brows.Count == 0) return;
            bm.Mesh?.Dispose();
            bm = (ship.BrowVersion, null);
            try
            {
                MeshData md = null;
                foreach (var l in ship.Ladders)
                {
                    if (l.Kind != "brow") continue;
                    md ??= new MeshData(256, 384, false, true, true, true);
                    BrowMesh(md, l);
                }
                if (md != null && md.VerticesCount > 0) bm.Mesh = capi.Render.UploadMultiTextureMesh(md);
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[hansa] Drawing a brow failed: {0}", e.Message);
            }
            browMeshes[ship.EntityId] = bm;
        }

        void BrowMesh(MeshData md, LadderDef l)
        {
            var wood = AtlasTex("game:block/wood/planks/oak1", null);
            var timber = AtlasTex("game:block/wood/debarked/oak", null);
            var rope = AtlasTex("game:block/cloth/reedrope", null);
            double[] a = l.Top, b = l.Bottom;
            double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 0.2) return;
            double hl = Math.Sqrt(dx * dx + dz * dz);
            // across it, level
            double cx = hl > 1e-6 ? -dz / hl : 1, cz = hl > 1e-6 ? dx / hl : 0;
            const double half = 0.35, thick = 0.08;
            double[] P(double t, double c, double up) => new[] { a[0] + dx * t + cx * c, a[1] + dy * t + up, a[2] + dz * t + cz * c };
            // the plank: its top on the line, its far end a little past the quay's edge
            double t1 = 1 + 0.3 / len;
            Hexa(md, wood, P(0, -half, -thick), P(t1, -half, -thick), P(0, -half, 0), P(t1, -half, 0), P(0, half, -thick), P(t1, half, -thick), P(0, half, 0), P(t1, half, 0));
            // cleats across it
            for (double s = 0.3; s < len; s += 0.4)
            {
                double ta = s / len, tb = (s + 0.06) / len;
                Hexa(md, timber, P(ta, -half + 0.03, 0), P(tb, -half + 0.03, 0), P(ta, -half + 0.03, 0.04), P(tb, -half + 0.03, 0.04),
                     P(ta, half - 0.03, 0), P(tb, half - 0.03, 0), P(ta, half - 0.03, 0.04), P(tb, half - 0.03, 0.04));
            }
            // stanchions and a rope rail each side
            int n = Math.Max(2, (int)Math.Round(len / 1.2));
            foreach (double c in new[] { -half + 0.03, half - 0.03 })
            {
                for (int k = 0; k <= n; k++)
                {
                    double t = (double)k / n * 0.98 + 0.01, w = 0.025;
                    var p = P(t, c, 0);
                    Hexa(md, timber, new[] { p[0] - w, p[1], p[2] - w }, new[] { p[0] + w, p[1], p[2] - w }, new[] { p[0] - w, p[1] + 0.85, p[2] - w }, new[] { p[0] + w, p[1] + 0.85, p[2] - w },
                         new[] { p[0] - w, p[1], p[2] + w }, new[] { p[0] + w, p[1], p[2] + w }, new[] { p[0] - w, p[1] + 0.85, p[2] + w }, new[] { p[0] + w, p[1] + 0.85, p[2] + w });
                }
                const double r = 0.018;
                Hexa(md, rope, P(0.01, c - r, 0.8), P(0.99, c - r, 0.8), P(0.01, c - r, 0.8 + 2 * r), P(0.99, c - r, 0.8 + 2 * r),
                     P(0.01, c + r, 0.8), P(0.99, c + r, 0.8), P(0.01, c + r, 0.8 + 2 * r), P(0.99, c + r, 0.8 + 2 * r));
            }
        }

        /// <summary>A six-faced solid from 8 corners in AddHexa's order (bit 0 along, bit 1 up, bit 2 across).</summary>
        static void Hexa(MeshData md, TextureAtlasPosition tp, double[] c0, double[] c1, double[] c2, double[] c3, double[] c4, double[] c5, double[] c6, double[] c7) =>
            AddHexa(md, tp, new[] { c0, c1, c2, c3, c4, c5, c6, c7 });

        void DrawBrows(IRenderAPI rapi, EntityShip ship)
        {
            if (browMeshes.TryGetValue(ship.EntityId, out var bm) && bm.Mesh != null) rapi.RenderMultiTextureMesh(bm.Mesh, "tex");
        }

        void DisposeBrows(long shipId)
        {
            if (browMeshes.TryGetValue(shipId, out var bm)) { bm.Mesh?.Dispose(); browMeshes.Remove(shipId); }
        }
    }
}
