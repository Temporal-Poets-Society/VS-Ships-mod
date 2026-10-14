using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// Masthead pennants (0.3.10, the user: "no indicator of wind ... on the ship"): a long red streamer at the truck of
    /// the mainmast and a shorter one at the fore, streaming with the apparent wind (the wind her sails feel: the true
    /// wind less her own way), fluttering faster the harder it blows, hanging slack in a calm. Drawn by the mod from the
    /// masts' tops in the design (ships/*.json "masts": x, z, y1), every frame on this client only.
    /// .hansa pennants switches them off (HansaClientConfig.Pennants).
    /// </summary>
    public class PennantRenderer : IRenderer, IDisposable
    {
        const int Segments = 12;
        const double Range = 250;
        readonly ICoreClientAPI capi;
        readonly HelmUiSystem sys;
        readonly Dictionary<(long, int), (MeshRef Ref, MeshData Data)> meshes = new();
        LoadedTexture texture;
        bool failed;
        readonly float[] model = new float[16];

        public double RenderOrder => 0.45;
        public int RenderRange => 9999;

        public PennantRenderer(ICoreClientAPI capi, HelmUiSystem sys) { this.capi = capi; this.sys = sys; }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            var client = sys.Hansa?.Client;
            if (failed || client == null || !client.Comfort.Pennants || client.Ships.Count == 0 || sys.Sea == null) return;
            var ep = capi.World.Player?.Entity;
            if (ep == null) return;
            IStandardShaderProgram prog = null;
            try
            {
                texture ??= Texture();
                var cam = ep.CameraPos;
                var rapi = capi.Render;
                double t = capi.InWorldEllapsedMilliseconds / 1000.0;
                rapi.GlDisableCullFace();
                foreach (var ship in client.Ships)
                {
                    var def = ship?.Def;
                    if (def?.Masts == null || ship.GoneDown || !ship.MastUp) continue;
                    double ddx = ship.Pos.X - cam.X, ddz = ship.Pos.Z - cam.Z;
                    if (ddx * ddx + ddz * ddz > Range * Range) continue;
                    var w = sys.Sea.WindAt(ship.Pos.X, ship.Pos.Z);
                    Sailing.Apparent(w.X, w.Z, ship.Pos.Yaw, ship.Speed, out double ax, out double az);
                    for (int m = 0; m < def.Masts.Length; m++)
                    {
                        var mast = def.Masts[m];
                        // the mainmast's long one, the fore's shorter; none on the mizzen (or a mast with no name)
                        double length = mast.Code == "main" ? 4.5 : mast.Code == "fore" ? 3 : 0;
                        if (length <= 0) continue;
                        ship.LocalToWorld(mast.X, mast.Y1, mast.Z, out double px, out double py, out double pz);
                        var (mesh, data) = Mesh(ship.EntityId, m);
                        Build(data, ax, az, length, t + ship.EntityId % 7 + m * 1.3);
                        rapi.UpdateMesh(mesh, data);

                        prog = rapi.PreparedStandardShader((int)Math.Floor(px), (int)Math.Floor(py), (int)Math.Floor(pz));
                        prog.ViewMatrix = rapi.CameraMatrixOriginf;
                        prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
                        Mat4f.Identity(model);
                        Mat4f.Translate(model, model, (float)(px - cam.X), (float)(py - cam.Y), (float)(pz - cam.Z));
                        prog.ModelMatrix = model;
                        prog.Tex2D = texture.TextureId;
                        rapi.RenderMesh(mesh);
                        prog.Stop();
                        prog = null;
                    }
                }
                rapi.GlEnableCullFace();
            }
            catch (Exception e)
            {
                failed = true;
                capi.Logger.Error("[hansa] Drawing the masthead pennants failed; they're off until the game is restarted: {0}", e);
                try { prog?.Stop(); } catch (Exception) { }
            }
        }

        (MeshRef, MeshData) Mesh(long ship, int mast)
        {
            if (meshes.TryGetValue((ship, mast), out var m)) return m;
            // (ships come and go: start afresh now and then rather than keep meshes for ships long gone)
            if (meshes.Count > 64) { foreach (var old in meshes.Values) old.Ref?.Dispose(); meshes.Clear(); }
            int nv = (Segments + 1) * 2;
            var data = new MeshData(nv, Segments * 6, false, true, true, true);
            for (int i = 0; i < nv; i++) data.AddWithFlagsVertex(0, 0, 0, 0, 0, -1, 0);
            for (int i = 0; i < Segments; i++)
            {
                int a = i * 2;
                data.AddIndices(a, a + 1, a + 3, a, a + 3, a + 2);
            }
            var r = capi.Render.UploadMesh(data);
            return meshes[(ship, mast)] = (r, data);
        }

        /// <summary>The streamer from the truck (the origin) downwind: (ax, az) is where the apparent air goes.</summary>
        static void Build(MeshData md, double ax, double az, double length, double t)
        {
            double a = Math.Sqrt(ax * ax + az * az);
            double h = Math.Clamp(a / 0.12, 0, 1);                       // how far it streams out rather than hangs
            double dx = a > 1e-6 ? ax / a : 1, dz = a > 1e-6 ? az / a : 0;
            // its line: downwind, drooping as the wind fails
            double ux = dx * h, uy = -(1 - h) * 0.97 - 0.03, uz = dz * h;
            double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            ux /= ul; uy /= ul; uz /= ul;
            // across it, level; its breadth: across both (upright when it streams)
            double sx = -dz, sz = dx;
            double wx = uy * sz, wy = uz * sx - ux * sz, wz = -uy * sx;
            double wl = Math.Sqrt(wx * wx + wy * wy + wz * wz);
            if (wl < 1e-6) { wx = 0; wy = 1; wz = 0; } else { wx /= wl; wy /= wl; wz /= wl; }
            double rate = 3 + 14 * Math.Min(1, a / 0.6), amp = 0.12 + 0.25 * h;
            var xyz = md.xyz;
            var uv = md.Uv;
            for (int i = 0; i <= Segments; i++)
            {
                double f = i / (double)Segments, s = f * length;
                // a wave running out along it, growing toward the fly
                double flap = Math.Sin(t * rate - f * 7) * amp * f;
                double cx = ux * s + sx * flap, cy = uy * s + Math.Sin(t * rate * 0.7 - f * 5) * amp * 0.4 * f, cz = uz * s + sz * flap;
                double half = 0.22 * (1 - 0.8 * f);
                for (int k = 0; k < 2; k++)
                {
                    double side = k == 0 ? -half : half;
                    int v = i * 2 + k;
                    xyz[v * 3] = (float)(cx + wx * side);
                    xyz[v * 3 + 1] = (float)(cy + wy * side);
                    xyz[v * 3 + 2] = (float)(cz + wz * side);
                    uv[v * 2] = (float)f;
                    uv[v * 2 + 1] = k;
                }
            }
        }

        /// <summary>Red bunting with a white stripe along it.</summary>
        LoadedTexture Texture()
        {
            using var surface = new ImageSurface(Format.Argb32, 32, 8);
            using (var ctx = new Context(surface))
            {
                ctx.SetSourceRGBA(0.72, 0.08, 0.06, 1);
                ctx.Paint();
                ctx.SetSourceRGBA(0.95, 0.93, 0.88, 1);
                ctx.Rectangle(0, 3, 32, 2);
                ctx.Fill();
            }
            int id = capi.Gui.LoadCairoTexture(surface, false);
            return new LoadedTexture(capi, id, 32, 8);
        }

        public void Dispose()
        {
            foreach (var m in meshes.Values) m.Ref?.Dispose();
            meshes.Clear();
            texture?.Dispose();
        }
    }
}
