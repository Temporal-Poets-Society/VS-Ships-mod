using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// Without Sea and without any <c>seacontracts:hulls</c> owner nothing cuts the world's water out of a hull: vanilla
    /// draws its flat surface right through her hold. This hides it the way vanilla's own boats do
    /// (<c>EntityBehaviorHideWaterSurface</c>, VS 1.22.7): just before the game draws its water (the OIT stage, order 0.36,
    /// before the terrain's 0.37), a lid is drawn into the depth buffer only, with vanilla's depth program, so the water
    /// surface behind it isn't drawn.
    /// <para>
    /// The lid is level, a little above the still water at her (the world's water doesn't tilt with her), and its outline is
    /// her hull where that level crosses it as she lies now (heave, pitch, roll and heel), a little inside her planking, from
    /// her exact half-widths (<see cref="WaterClipDef.DryOutline"/>). So it hides the water surface inside her hold and
    /// nothing outside her. What it can't do (a lid isn't a cut): seen from below it (eyes under the still-water level in
    /// her hold) the water's surface still shows overhead; the camera there isn't drawn as under water
    /// (<see cref="OwnDryPlaces"/>); and a sea higher than the lid (another mod's waves, or her sinking under it) shows.
    /// Not drawn for a ship gone down (she's full of the sea).
    /// </para>
    /// </summary>
    public sealed class ShipWaterLids : IRenderer, IDisposable
    {
        /// <summary>How far above the still water the lid lies (vanilla's water surface sways a little).</summary>
        public const double Lift = 0.12;
        /// <summary>How far inside her hull's outer surface its edge stays.</summary>
        public const double Inset = 0.1;
        /// <summary>Stations along her.</summary>
        public const int Stations = 32;
        const double MaxDistance = 160;

        readonly ICoreClientAPI capi;
        readonly ShipClientSystem sys;
        readonly Dictionary<long, (MeshData Data, MeshRef Mesh)> meshes = new();
        readonly Dictionary<long, (double Y, long Ms)> water = new();
        readonly float[] mvp = Mat4f.Create();
        readonly HashSet<long> seen = new();
        bool failed;

        /// <summary>Drawn only while this is true (no Sea, no hull cut owner): <see cref="ShipClientSystem"/> sets it.</summary>
        public bool Active;

        public double RenderOrder => 0.36;
        public int RenderRange => 9999;

        public ShipWaterLids(ICoreClientAPI capi, ShipClientSystem sys)
        {
            this.capi = capi;
            this.sys = sys;
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (!Active || failed) return;
            var ep = capi.World.Player?.Entity;
            if (ep == null || ep.Pos.Dimension != 0 || sys.Ships.Count == 0) return;
            try { Draw(ep); }
            catch (Exception e)
            {
                failed = true;
                capi.Logger.Error("[ships] the water lids inside her hull failed and are off from now ({0}); the water shows in her hold", e);
            }
        }

        void Draw(EntityPlayer ep)
        {
            var rapi = capi.Render;
            var cam = ep.CameraPos;
            IShaderProgram prog = null;
            seen.Clear();
            foreach (var ship in sys.Ships)
            {
                var def = ship.Def;
                if (def?.WaterClip == null || !ship.Alive || ship.GoneDown || ship.Pos.Dimension != 0) continue;
                var o = sys.Renderer.RenderOrigin(ship, ep);
                if ((o.X - cam.X) * (o.X - cam.X) + (o.Z - cam.Z) * (o.Z - cam.Z) > MaxDistance * MaxDistance) continue;
                double still = StillWater(ship);
                if (double.IsNaN(still)) continue;
                var md = Mesh(ship.EntityId, out var mesh);
                if (!Outline(def, o, ship.Pos.Yaw, ship.Pitch, ship.Roll, still + Lift, cam, md)) continue;
                rapi.UpdateMesh(mesh, md);
                seen.Add(ship.EntityId);
                if (prog == null)
                {
                    // vanilla's lid: depth only (EntityBehaviorHideWaterSurface draws into no colour buffer; here the
                    // colour mask does the same for every buffer), its depth program, the world as the camera sees it
                    rapi.GlColorMask(false, false, false, false);
                    rapi.GLDepthMask(true);
                    rapi.GLEnableDepthTest();
                    prog = capi.Shader.GetProgram(rapi.UseSSBOs ? (int)EnumShaderProgram.Chunkshadowmap_NoSSBOs : (int)EnumShaderProgram.Chunkshadowmap);
                    prog.Use();
                    Mat4f.Mul(mvp, rapi.CurrentProjectionMatrix, rapi.CameraMatrixOriginf);
                    prog.BindTexture2D("tex2d", 0, 0);
                    prog.UniformMatrix("mvpMatrix", mvp);
                    prog.Uniform("origin", 0f, 0f, 0f);
                }
                rapi.RenderMesh(mesh);
            }
            if (prog != null)
            {
                prog.Stop();
                rapi.GLDepthMask(false);
                rapi.GLEnableDepthTest();
                rapi.GlColorMask(true, true, true, true);
            }
            // ships gone from this client: their meshes go too
            if (meshes.Count > seen.Count + 8)
            {
                var gone = new List<long>();
                foreach (var id in meshes.Keys) if (!seen.Contains(id) && !sys.Ships.Exists(s => s.EntityId == id)) gone.Add(id);
                foreach (var id in gone) { meshes[id].Mesh?.Dispose(); meshes.Remove(id); water.Remove(id); }
            }
        }

        /// <summary>The still water's top at her (world y), looked up once a second: NaN with no water under her.</summary>
        double StillWater(EntityShip ship)
        {
            long now = capi.ElapsedMilliseconds;
            if (water.TryGetValue(ship.EntityId, out var w) && now - w.Ms < 1000) return w.Y;
            var bp = new BlockPos((int)Math.Floor(ship.Pos.X), (int)Math.Floor(ship.Pos.Y), (int)Math.Floor(ship.Pos.Z), 0);
            double y = EntityShip.FindWaterSurface(capi.World, bp, 3, 8);
            water[ship.EntityId] = (y, now);
            return y;
        }

        MeshData Mesh(long id, out MeshRef mesh)
        {
            if (meshes.TryGetValue(id, out var m)) { mesh = m.Mesh; return m.Data; }
            // two faces (either way up) of a strip of quads between her two sides at each station
            var md = new MeshData((Stations - 1) * 8, (Stations - 1) * 12, false, true, true, true);
            for (int q = 0; q < (Stations - 1) * 2; q++)
            {
                for (int k = 0; k < 4; k++) md.AddWithFlagsVertex(0, 0, 0, 0, 0, -1, 0);
                md.AddIndices(4 * q, 4 * q + 1, 4 * q + 2, 4 * q, 4 * q + 2, 4 * q + 3);
            }
            mesh = capi.Render.UploadMesh(md);
            meshes[id] = (md, mesh);
            return md;
        }

        /// <summary>
        /// Fill md with the lid: level at world y <paramref name="lidY"/>, her hull's outline at that level as she lies (origin
        /// o as drawn, her yaw, pitch and roll), less <see cref="Inset"/>, relative to the camera. False: no lid (the level
        /// isn't within her hull: she's out of the water, or awash).
        /// </summary>
        public static bool Outline(ShipDefinition def, Vec3d o, double yaw, double pitch, double roll, double lidY, Vec3d cam, MeshData md)
        {
            var pts = OutlinePoints(def, o.X, o.Y, o.Z, yaw, pitch, roll, lidY);
            if (pts == null) return false;
            md.Clear();
            for (int i = 0; i + 1 < Stations; i++)
            {
                var (p0, s0) = pts[i];
                var (p1, s1) = pts[i + 1];
                for (int face = 0; face < 2; face++)
                {
                    int b = md.VerticesCount;
                    // either way up, so the lid stands whatever the cull face state
                    var a = face == 0 ? new[] { p0, p1, s1, s0 } : new[] { p0, s0, s1, p1 };
                    foreach (var v in a) md.AddWithFlagsVertex((float)(v.X - cam.X), (float)(v.Y - cam.Y), (float)(v.Z - cam.Z), 0, 0, -1, 0);
                    md.AddIndices(b, b + 1, b + 2, b, b + 2, b + 3);
                }
            }
            return true;
        }

        /// <summary>
        /// The lid's corners in the world: at each of <see cref="Stations"/> stations along her, her port and starboard
        /// side where the level lidY crosses her hull (as she lies, less <see cref="Inset"/>); null when that level misses
        /// her hull (below her keel or over her deck).
        /// </summary>
        public static (Vec3d SideA, Vec3d SideB)[] OutlinePoints(ShipDefinition def, double ox, double oy, double oz,
            double yaw, double pitch, double roll, double lidY)
        {
            var wc = def.WaterClip;
            if (wc == null) return null;
            var o = wc.DryOutline;
            bool exact = o?.HalfWidths != null && o.NX > 1 && o.NY > 1 && o.HalfWidths.Length == o.NX * o.NY;
            double x0, x1;
            if (exact) { x0 = o.X0; x1 = o.X0 + (o.NX - 1) * o.DX; }
            else { x0 = wc.CenterX - (wc.HalfLength - wc.Margin); x1 = wc.CenterX + (wc.HalfLength - wc.Margin); }
            if (!(x1 > x0)) return null;
            double Width(double x, double y)
            {
                double w;
                if (exact) w = o.At(x, y);
                else
                {
                    double u = wc.HalfLength > 0 ? (x - wc.CenterX) / wc.HalfLength : 0;
                    w = HalfWidthAt(wc.Profile, wc.HalfWidth, u) - wc.Margin + Inset;
                }
                return Math.Max(0, w - Inset);
            }
            // her local up in the world's y (how much a block up in her rises): the level is found along it
            double target = lidY - oy;
            ShipFrame.ToWorld(yaw, pitch, roll, 0, 1, 0, out _, out double upY, out _);
            if (upY < 0.3) return null;     // on her beam ends: no lid
            // the level must cross her between her keel and her deck where she's widest
            ShipFrame.ToWorld(yaw, pitch, roll, 0, def.KeelY, 0, out _, out double keelY, out _);
            ShipFrame.ToWorld(yaw, pitch, roll, 0, def.DeckY + 1, 0, out _, out double deckY, out _);
            if (target < keelY || target > deckY) return null;
            var pts = new (Vec3d, Vec3d)[Stations];
            for (int i = 0; i < Stations; i++)
            {
                double x = x0 + (x1 - x0) * i / (Stations - 1);
                Vec3d Side(double s)
                {
                    // the local height at which this station's side meets the level: a few steps along her up, and where
                    // her side's slope makes those overshoot (her rolled stern), halving between her keel and her deck
                    double wx = 0, wy = 0, wz = 0;
                    double Err(double y)
                    {
                        ShipFrame.ToWorld(yaw, pitch, roll, x, y, s * Width(x, y), out wx, out wy, out wz);
                        return target - wy;
                    }
                    double yy = target / upY, err = Err(yy);
                    for (int it = 0; it < 6 && Math.Abs(err) >= 1e-4; it++) err = Err(yy += err / upY);
                    if (Math.Abs(err) >= 1e-4)
                    {
                        double lo = def.KeelY - 1, hi = def.DeckY + 2;
                        if (Err(lo) > 0 && Err(hi) < 0)
                            for (int it = 0; it < 40; it++)
                            {
                                yy = (lo + hi) / 2;
                                err = Err(yy);
                                if (Math.Abs(err) < 1e-4) break;
                                if (err > 0) lo = yy; else hi = yy;
                            }
                        Err(yy);
                    }
                    return new Vec3d(ox + wx, oy + wy, oz + wz);
                }
                // (starboard is her local -z: ShipFrame's +z is to port)
                pts[i] = (Side(1), Side(-1));
            }
            return pts;
        }

        static double HalfWidthAt(float[] profile, float halfWidth, double u)
        {
            if (profile == null || profile.Length != WaterClipDef.Stations) return halfWidth;
            double s = Math.Clamp((u + 1) * 7.5, 0, 15);
            int i = (int)Math.Floor(s), j = Math.Min(i + 1, 15);
            return profile[i] + (profile[j] - profile[i]) * (s - i);
        }

        public void Dispose()
        {
            foreach (var m in meshes.Values) m.Mesh?.Dispose();
            meshes.Clear();
            water.Clear();
        }
    }
}
