using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.3.27, note 207) Her shot scars (<see cref="ShotScar"/>) drawn: each one's ray followed to the first of her drawn
    /// surfaces that faces it (the mesh file's quads, else her voxel shapes' faces: her collision's), and a splintered patch
    /// (textures/block/dmg-shot-*) laid on it a hair proud, alpha-tested; outside ones in the light outside, inside ones in
    /// her rooms'. Built again when her scars or her hull panels change.
    /// </summary>
    public partial class ShipRenderer
    {
        class ScarMeshes
        {
            public int Version = int.MinValue, PanelVersion = int.MinValue;
            public MultiTextureMeshRef Outside, Inside;
            public void Dispose() { Outside?.Dispose(); Inside?.Dispose(); Outside = Inside = null; }
        }

        readonly Dictionary<long, ScarMeshes> scarMeshes = new();
        /// <summary>Per design: its mesh file's quads by the blocks their bounds reach (ship-local, whole blocks).</summary>
        readonly Dictionary<string, Dictionary<(int, int, int), List<float[]>>> quadIndex = new();
        bool scarsFailed;
        /// <summary>How big a scar is drawn (blocks, square).</summary>
        public const double ScarSize = 0.5;

        Dictionary<(int, int, int), List<float[]>> QuadIndex(ShipDefinition def, Meshes m)
        {
            if (quadIndex.TryGetValue(def.Code, out var ix)) return ix;
            ix = new Dictionary<(int, int, int), List<float[]>>();
            foreach (var kv in m.File.Groups)
            {
                // (not her moving parts, sails, ropes or glass: her planking and timbers)
                if (kv.Key.StartsWith("part:") || kv.Key.StartsWith("sail") || kv.Key.StartsWith("glass") || kv.Key.StartsWith("rig:") || kv.Value == null) continue;
                foreach (var q in kv.Value)
                {
                    if (q == null || q.Length < 33) continue;
                    double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
                    for (int k = 0; k < 4; k++)
                    {
                        int o = 1 + k * 8;
                        x0 = Math.Min(x0, q[o]); y0 = Math.Min(y0, q[o + 1]); z0 = Math.Min(z0, q[o + 2]);
                        x1 = Math.Max(x1, q[o]); y1 = Math.Max(y1, q[o + 1]); z1 = Math.Max(z1, q[o + 2]);
                    }
                    if (x1 - x0 > 6 || z1 - z0 > 6 || y1 - y0 > 6) continue;
                    for (int i = (int)Math.Floor(x0); i <= (int)Math.Floor(x1); i++)
                        for (int j = (int)Math.Floor(y0); j <= (int)Math.Floor(y1); j++)
                            for (int k = (int)Math.Floor(z0); k <= (int)Math.Floor(z1); k++)
                            {
                                if (!ix.TryGetValue((i, j, k), out var list)) ix[(i, j, k)] = list = new List<float[]>();
                                list.Add(q);
                            }
                }
            }
            quadIndex[def.Code] = ix;
            return ix;
        }

        /// <summary>Where a scar's ray first meets a drawn face turned toward it: the point and that face's normal (ship-local), or false.</summary>
        bool ScarSurface(EntityShip ship, Dictionary<(int, int, int), List<float[]>> ix, ShotScar s, out Vec3d at, out Vec3d nrm)
        {
            at = nrm = null;
            var o = new Vec3d(s.X, s.Y, s.Z);
            var d = new Vec3d(s.DX, s.DY, s.DZ);
            double best = s.Reach;
            var seen = new HashSet<float[]>();
            for (double t = 0; t <= s.Reach + 0.5; t += 0.25)
            {
                var c = (Floor(o.X + d.X * t), Floor(o.Y + d.Y * t), Floor(o.Z + d.Z * t));
                if (!ix.TryGetValue(c, out var list)) continue;
                foreach (var q in list)
                {
                    if (!seen.Add(q)) continue;
                    var corners = new double[4][];
                    for (int k = 0; k < 4; k++) { int b = 1 + k * 8; corners[k] = new double[] { q[b], q[b + 1], q[b + 2] }; }
                    double tq = HullSlotLayout.RayQuad(o, d, corners);
                    if (tq < 0 || tq >= best) continue;
                    var n = QuadNormal(corners);
                    if (n == null) continue;
                    double dn = n.X * d.X + n.Y * d.Y + n.Z * d.Z;
                    if (Math.Abs(dn) < 0.2) continue;
                    // (drawn single-sided or not: the face turned toward where we look from)
                    if (dn > 0) n = new Vec3d(-n.X, -n.Y, -n.Z);
                    best = tq;
                    at = new Vec3d(o.X + d.X * tq, o.Y + d.Y * tq, o.Z + d.Z * tq);
                    nrm = n;
                }
            }
            // her voxel shapes (decks, castle walls, her collision's own faces): the grid's first cell, its face
            var g = ship.Grid;
            if (g != null)
            {
                double tg = g.RaycastCell(o, d, Math.Min(best, s.Reach), out int ci, out int cj, out int ck, out int axis);
                if (tg >= 0 && tg < best && axis >= 0 && ci >= 0)
                {
                    double sx = axis == 0 ? -Math.Sign(d.X) : 0, sy = axis == 1 ? -Math.Sign(d.Y) : 0, sz = axis == 2 ? -Math.Sign(d.Z) : 0;
                    at = new Vec3d(o.X + d.X * tg, o.Y + d.Y * tg, o.Z + d.Z * tg);
                    nrm = new Vec3d(sx, sy, sz);
                    best = tg;
                }
            }
            return at != null;
        }

        static int Floor(double v) => (int)Math.Floor(v);

        static Vec3d QuadNormal(double[][] c)
        {
            double ax = c[2][0] - c[0][0], ay = c[2][1] - c[0][1], az = c[2][2] - c[0][2];
            double bx = c[3][0] - c[1][0], by = c[3][1] - c[1][1], bz = c[3][2] - c[1][2];
            double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
            double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return l < 1e-9 ? null : new Vec3d(nx / l, ny / l, nz / l);
        }

        /// <summary>Every frame: her scars' meshes built again when they changed.</summary>
        void UpdateScars(EntityShip ship, Meshes m)
        {
            if (scarsFailed || m.File?.Groups == null || ship.Def == null) return;
            if (!scarMeshes.TryGetValue(ship.EntityId, out var sm)) scarMeshes[ship.EntityId] = sm = new ScarMeshes();
            int pv = ship.HullPanels?.Version ?? 0;
            if (sm.Version == ship.ScarVersion && sm.PanelVersion == pv) return;
            sm.Version = ship.ScarVersion;
            sm.PanelVersion = pv;
            sm.Dispose();
            if (ship.Scars.Count == 0) return;
            try
            {
                var ix = QuadIndex(ship.Def, m);
                var texA = ScarTex("hansa:block/dmg-shot-a");
                var texB = ScarTex("hansa:block/dmg-shot-b");
                MeshData outside = null, inside = null;
                foreach (var s in ship.Scars)
                {
                    if (ship.ScarOnOpening(s) || !ScarSurface(ship, ix, s, out var at, out var n)) continue;
                    var tp = (s.Variant & 1) == 0 ? texA : texB;
                    ref MeshData md = ref (s.Inside != 0 ? ref inside : ref outside);
                    md ??= new MeshData(64, 96, false, true, true, true);
                    AddScarQuad(md, tp, at, n, s.Variant);
                }
                if (outside != null && outside.VerticesCount > 0) sm.Outside = capi.Render.UploadMultiTextureMesh(outside);
                if (inside != null && inside.VerticesCount > 0) sm.Inside = capi.Render.UploadMultiTextureMesh(inside);
            }
            catch (Exception e)
            {
                scarsFailed = true;
                capi.Logger.Error("[hansa] Drawing shot scars failed; they're off until the game is restarted: {0}", e);
            }
        }

        TextureAtlasPosition ScarTex(string name)
        {
            capi.BlockTextureAtlas.GetOrInsertTexture(AssetLocation.Create(name), out _, out var pos);
            return pos ?? capi.BlockTextureAtlas.UnknownTexturePosition;
        }

        /// <summary>A scar's square on a face (at, its normal), turned by its variant, a hair proud of the face.</summary>
        static void AddScarQuad(MeshData md, TextureAtlasPosition tp, Vec3d at, Vec3d n, byte variant)
        {
            // two directions along the face: level (or along x on a level face) and across
            double ux, uy, uz;
            if (Math.Abs(n.Y) > 0.9) { ux = 1; uy = 0; uz = 0; }
            else { double l = Math.Sqrt(n.X * n.X + n.Z * n.Z); ux = -n.Z / l; uy = 0; uz = n.X / l; }
            double vx = n.Y * uz - n.Z * uy, vy = n.Z * ux - n.X * uz, vz = n.X * uy - n.Y * ux;
            double a = (variant >> 1) * Math.PI / 2 + (variant & 1) * 0.35, ca = Math.Cos(a), sa = Math.Sin(a);
            double rx = ux * ca + vx * sa, ry = uy * ca + vy * sa, rz = uz * ca + vz * sa;
            double sx = vx * ca - ux * sa, sy = vy * ca - uy * sa, sz = vz * ca - uz * sa;
            double h = ScarSize / 2, off = 0.012;
            double cx = at.X + n.X * off, cy = at.Y + n.Y * off, cz = at.Z + n.Z * off;
            int flags = VertexFlags.PackNormal(n.X, n.Y, n.Z);
            double[][] p =
            {
                new[] { cx - rx * h - sx * h, cy - ry * h - sy * h, cz - rz * h - sz * h },
                new[] { cx + rx * h - sx * h, cy + ry * h - sy * h, cz + rz * h - sz * h },
                new[] { cx + rx * h + sx * h, cy + ry * h + sy * h, cz + rz * h + sz * h },
                new[] { cx - rx * h + sx * h, cy - ry * h + sy * h, cz - rz * h + sz * h }
            };
            float[] us = { 0, 1, 1, 0 }, vs = { 1, 1, 0, 0 };
            int b = md.VerticesCount;
            for (int k = 0; k < 4; k++)
                md.AddWithFlagsVertex((float)p[k][0], (float)p[k][1], (float)p[k][2], tp.x1 + (tp.x2 - tp.x1) * us[k], tp.y1 + (tp.y2 - tp.y1) * vs[k], -1, flags);
            // (both ways round: seen from the side it faces, culling or not)
            md.AddIndices(b, b + 1, b + 2, b, b + 2, b + 3);
            md.AddTextureId(tp.atlasTextureId);
        }

        /// <summary>Her scars, alpha-tested (outside in the light outside, inside in her rooms').</summary>
        void DrawScars(IRenderAPI rapi, IStandardShaderProgram prog, EntityShip ship, Vec4f ext)
        {
            if (!scarMeshes.TryGetValue(ship.EntityId, out var sm) || sm.Outside == null && sm.Inside == null) return;
            prog.AlphaTest = 0.3f;
            if (sm.Outside != null) rapi.RenderMultiTextureMesh(sm.Outside, "tex");
            if (sm.Inside != null)
            {
                prog.RgbaLightIn = interiorLight;
                rapi.RenderMultiTextureMesh(sm.Inside, "tex");
                prog.RgbaLightIn = ext;
            }
            prog.AlphaTest = 0f;
        }

        void DisposeScars(long shipId)
        {
            if (scarMeshes.TryGetValue(shipId, out var sm)) { sm.Dispose(); scarMeshes.Remove(shipId); }
        }

        /// <summary>(review H1-23) All ships' scar meshes, the renderer going.</summary>
        void DisposeAllScars()
        {
            foreach (var sm in scarMeshes.Values) sm.Dispose();
            scarMeshes.Clear();
        }
    }
}
