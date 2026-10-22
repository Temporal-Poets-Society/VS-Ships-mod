using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// The ships on the map (0.3.10, a vanilla map layer, so it's on vanilla's world map and minimap, and on Wmap's
    /// minimap; Wmap's own atlas draws other mods' layers too since Wmap 0.3.4): every ship drawn as her real-size plan
    /// (her outline seen from above, from her collision voxels: hull and castles), filled, a darker edge, turned to her
    /// heading, never smaller than a few pixels; the player's own ships (ones they've been aboard, or bound their spawn
    /// to) gold, the rest pale; the rowing boats near by small and brown; her name beside her. And, aboard a ship, the wind
    /// (Sea's) where the player is: a pale blue arrow blowing toward the player's marker. The tab "Ships" switches it on and off.
    /// The ships come from the server (<see cref="HelmUiSystem"/>: the player's own anywhere, the loaded ones near);
    /// ships loaded here are drawn where they are this frame.
    /// </summary>
    public class ShipMapLayer : MapLayer
    {
        public const string Code = "hansa-ships", Group = "ships";

        /// <summary>
        /// (0.3.12) The depths drawn at: vanilla's terrain is at 50, its player and creature markers (and the waypoints)
        /// at 60, so the ships, their names and the wind arrow lie between: on the map, under every marker. (Till 0.3.11
        /// they were at 60–61, drawn after the players' layer: the player's own arrow vanished under her own ship.)
        /// </summary>
        const float DepthPlan = 55f, DepthLabel = 56f;

        readonly ICoreClientAPI capi;
        readonly Dictionary<string, MeshRef> plans = new();
        readonly Dictionary<string, double> planLength = new();
        readonly Dictionary<string, LoadedTexture> labels = new();
        MeshRef boatPlan, windArrow;
        readonly Matrixf mat = new();
        readonly Vec3d world = new();
        Vec2f view = new();

        public override string Title => "Ships";
        public override string LayerGroupCode => Group;
        public override EnumMapAppSide DataSide => EnumMapAppSide.Client;
        public override bool RequireChunkLoaded => false;

        public ShipMapLayer(ICoreAPI api, IWorldMapManager sink) : base(api, sink)
        {
            capi = api as ICoreClientAPI;
        }

        HelmUiSystem Sys => capi?.ModLoader.GetModSystem<HelmUiSystem>();

        // ------------------------------------------------------------------ drawing

        public override void Render(GuiElementMap map, float dt)
        {
            var sys = Sys;
            if (!Active || capi == null || sys == null) return;
            sys.MapShown();
            var ep = capi.World.Player?.Entity;
            if (ep == null || ep.Pos.Dimension != 0) return;

            double zoom = PixelsPerBlock(map);
            var prog = capi.Render.GetEngineShader(EnumShaderProgram.Gui);
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            prog.Uniform("applyColor", 0);
            prog.Uniform("extraGlow", 0);
            prog.Uniform("noTexture", 1f);
            capi.Render.GlToggleBlend(true);

            var pending = new List<(string Text, float X, float Y, bool Own)>();
            foreach (var s in sys.MapShips())
            {
                if (!Visible(map, s.X, s.Z, s.Length * zoom + 40)) continue;
                DrawPlan(map, prog, s.Code, s.X, s.Z, s.Yaw, zoom, s.Own);
                if (s.Name != null && (s.Own || s.Length * zoom >= 18 * GuiElement.scaled(1)))
                {
                    Screen(map, s.X, s.Z, out float sx, out float sy);
                    float below = (float)Math.Max(6 * GuiElement.scaled(1), s.Length * zoom * 0.35);
                    pending.Add((s.Name, sx, sy + below, s.Own));
                }
            }
            foreach (var b in sys.Hansa?.Client?.RowBoats ?? new List<EntityRowBoat>())
            {
                if (b == null || !b.Alive) continue;
                var (bx, bz) = HelmMath.Bow(b.Pos.Yaw);
                double cx = b.Pos.X + bx * 2.05, cz = b.Pos.Z + bz * 2.05;
                if (!Visible(map, cx, cz, 40)) continue;
                DrawPlan(map, prog, null, cx, cz, b.Pos.Yaw, zoom, false);
            }
            // (0.3.24, the user: "while aboard, show on the minimap the same sailing info as the dial") her no-go wedge and
            // her course; the true wind's arrow stays at the player (DrawWind)
            DrawSailing(map, prog, ep);
            DrawWind(map, prog, ep);
            prog.Uniform("noTexture", 0f);

            if (pending.Count > 0)
            {
                var shader = capi.Render.CurrentActiveShader;
                foreach (var (text, x, y, own) in pending)
                {
                    var tex = Label(text, own);
                    capi.Render.Render2DTexturePremultipliedAlpha(tex.TextureId, x - tex.Width / 2f, y, tex.Width, tex.Height, DepthLabel);
                }
                // (back to the shader the next layer expects)
                if (shader != null && capi.Render.CurrentActiveShader != shader)
                {
                    capi.Render.CurrentActiveShader?.Stop();
                    shader.Use();
                }
            }
        }

        /// <summary>Map pixels a block (vanilla's ZoomLevel: the view's width over the element's).</summary>
        static double PixelsPerBlock(GuiElementMap map)
        {
            var v = map.CurrentBlockViewBounds;
            double w = v.X2 - v.X1;
            return w > 1e-6 ? map.Bounds.InnerWidth / w : map.ZoomLevel;
        }

        void Screen(GuiElementMap map, double x, double z, out float sx, out float sy)
        {
            world.Set(x, 0, z);
            map.TranslateWorldPosToViewPos(world, ref view);
            sx = (float)(map.Bounds.renderX + view.X);
            sy = (float)(map.Bounds.renderY + view.Y);
        }

        bool Visible(GuiElementMap map, double x, double z, double marginPx)
        {
            Screen(map, x, z, out float sx, out float sy);
            var b = map.Bounds;
            return sx > b.renderX - marginPx && sx < b.renderX + b.InnerWidth + marginPx && sy > b.renderY - marginPx && sy < b.renderY + b.InnerHeight + marginPx;
        }

        /// <summary>Her plan at (x, z) turned to her heading: model x along her keel (bow +), y to starboard.</summary>
        void DrawPlan(GuiElementMap map, IShaderProgram prog, string code, double x, double z, double yaw, double zoom, bool own)
        {
            var mesh = Plan(code, out double length);
            if (mesh == null) return;
            Screen(map, x, z, out float sx, out float sy);
            // real size, but never under a few pixels long
            double minPx = (code == null ? 7 : 12) * GuiElement.scaled(1);
            float k = (float)Math.Max(zoom, minPx / Math.Max(1, length));
            // her bow (sin yaw, cos yaw) is the map's (x, z); model +x turned by π/2 - yaw lands there, model +y on starboard
            float turn = (float)(Math.PI / 2 - yaw);
            float edge = (float)Math.Min(1.5 * GuiElement.scaled(1), k * 0.6);
            Vec4f dark = code == null ? new Vec4f(0.22f, 0.13f, 0.06f, 0.95f) : own ? new Vec4f(0.35f, 0.22f, 0.02f, 0.95f) : new Vec4f(0.1f, 0.1f, 0.12f, 0.9f);
            Vec4f fill = code == null ? new Vec4f(0.62f, 0.42f, 0.22f, 1f) : own ? new Vec4f(1f, 0.78f, 0.2f, 1f) : new Vec4f(0.88f, 0.88f, 0.92f, 1f);
            // the edge: the same plan a little bigger, darker, under it
            float grow = (float)(1 + 2 * edge / Math.Max(1, length * k));
            Render(prog, mesh, sx, sy, k * grow, turn, dark, DepthPlan);
            Render(prog, mesh, sx, sy, k, turn, fill, DepthPlan + 0.5f);
        }

        void Render(IShaderProgram prog, MeshRef mesh, float x, float y, float scale, float turn, Vec4f color, float depth)
        {
            prog.Uniform("rgbaIn", color);
            mat.Set(capi.Render.CurrentModelviewMatrix).Translate(x, y, depth).Scale(scale, scale, 1).RotateZ(turn);
            prog.UniformMatrix("modelViewMatrix", mat.Values);
            capi.Render.RenderMesh(mesh);
        }

        /// <summary>
        /// (0.3.24) Aboard a ship (her helm, her deck): round her on the map, as the wind dial shows them, the no-go wedge (red,
        /// the bow directions she can't point: her design's no-go angle either side of the true wind, a ship that sails by the
        /// wind with her mast up) and her course over ground (a green arrow from her, longer the faster she goes). A fixed size
        /// in pixels at every zoom. Cheap: two cached meshes (the wedge by its width in degrees).
        /// </summary>
        void DrawSailing(GuiElementMap map, IShaderProgram prog, EntityPlayer ep)
        {
            if (ep.MountedOn is not ShipSeat seat || seat.Ship is not EntityShip ship || ship.Def == null || ship.GoneDown) return;
            Screen(map, ship.Pos.X, ship.Pos.Z, out float sx, out float sy);
            double g = GuiElement.scaled(1), yaw = ship.Pos.Yaw;
            float Turn(double rel) => (float)HelmMath.MapAngle(rel, yaw);
            // (the wind her sails and the panel use: WindHere)
            var w = ship.WindHere(ship.Pos.X, ship.Pos.Z);
            if (w.Strength >= 0.02 && ship.SailByWind && ship.MastUp && ship.Def.Sailing != null)
            {
                // (after 0.1.0-dev.5) the fan as wide as the panel's wedge: till then it was built from the half-angle, half as wide
                HelmMath.MapNoGo(w.X, w.Z, yaw, ship.SailingNow.NoGo, out double centre, out double width);
                int deg = HelmMath.FanDegrees(width);
                if (!wedges.TryGetValue(deg, out var fan)) wedges[deg] = fan = capi.Render.UploadMesh(FanMesh(deg * Math.PI / 180));
                float r = (float)(44 * g);
                prog.Uniform("rgbaIn", new Vec4f(0.95f, 0.15f, 0.1f, 0.35f));
                mat.Set(capi.Render.CurrentModelviewMatrix).Translate(sx, sy, DepthLabel - 0.5f).RotateZ((float)centre).Scale(r, r, 1);
                prog.UniformMatrix("modelViewMatrix", mat.Values);
                capi.Render.RenderMesh(fan);
            }
            string motion = HelmMath.Motion(ship.Speed, ship.Leeway, out double over, out double relCourse);
            if (motion != "stopped")
            {
                windArrow ??= capi.Render.UploadMesh(ArrowMesh());
                float len = (float)((22 + 26 * Math.Min(1, over / 7)) * g);
                float turn = Turn(relCourse);
                float hx = sx + (float)Math.Cos(turn) * len, hy = sy + (float)Math.Sin(turn) * len;
                mat.Set(capi.Render.CurrentModelviewMatrix).Translate(hx, hy, DepthLabel).RotateZ(turn).Scale(len, (float)(4 * g), 1);
                prog.Uniform("rgbaIn", new Vec4f(0.03f, 0.15f, 0.05f, 0.85f));
                var under = new Matrixf().Set(mat.Values).Scale(1.08f, 1.5f, 1);
                prog.UniformMatrix("modelViewMatrix", under.Values);
                capi.Render.RenderMesh(windArrow);
                prog.Uniform("rgbaIn", new Vec4f(0.3f, 0.9f, 0.35f, 1f));
                prog.UniformMatrix("modelViewMatrix", mat.Values);
                capi.Render.RenderMesh(windArrow);
            }
        }

        readonly Dictionary<int, MeshRef> wedges = new();

        /// <summary>(0.3.24) A fan round +x, the given width (radians) across, radius 1, its point at the origin.</summary>
        static MeshData FanMesh(double width)
        {
            const int n = 16;
            var m = new MeshData(n + 2, n * 3, false, true, true, false);
            m.AddVertex(0, 0, 0, 0, 0, -1);
            for (int i = 0; i <= n; i++)
            {
                double a = -width / 2 + width * i / n;
                m.AddVertex((float)Math.Cos(a), (float)Math.Sin(a), 0, 0, 0, -1);
            }
            for (int i = 0; i < n; i++) { m.AddIndex(0); m.AddIndex(i + 1); m.AddIndex(i + 2); }
            return m;
        }

        /// <summary>
        /// The wind where the player is: a pale blue arrow blowing toward their marker, from upwind. Only aboard a ship (her
        /// helm, her deck; the user: ashore it is "not needed").
        /// </summary>
        static bool Aboard(EntityPlayer ep) => ep.MountedOn is ShipSeat seat && seat.Ship is EntityShip ship && ship.Def != null && !ship.GoneDown;

        void DrawWind(GuiElementMap map, IShaderProgram prog, EntityPlayer ep)
        {
            var sea = Sys?.Sea;
            if (sea == null || !Aboard(ep)) return;
            var w = sea.WindAt(ep.Pos.X, ep.Pos.Z);
            double strength = w.Strength;
            if (strength < 0.02) return;
            windArrow ??= capi.Render.UploadMesh(ArrowMesh());
            Screen(map, ep.Pos.X, ep.Pos.Z, out float sx, out float sy);
            double g = GuiElement.scaled(1);
            double dx = w.X / strength, dz = w.Z / strength;
            // the arrow's head 12 px upwind of the player, longer the stronger it blows
            float len = (float)((14 + 14 * Math.Min(1, strength / 0.6)) * g);
            float hx = (float)(sx - dx * 12 * g), hy = (float)(sy - dz * 12 * g);
            float turn = (float)Math.Atan2(dz, dx);
            mat.Set(capi.Render.CurrentModelviewMatrix).Translate(hx, hy, DepthLabel).RotateZ(turn).Scale(len, (float)(5 * g), 1);
            prog.Uniform("rgbaIn", new Vec4f(0.05f, 0.1f, 0.2f, 0.85f));
            var under = new Matrixf().Set(mat.Values).Scale(1.15f, 1.5f, 1);
            prog.UniformMatrix("modelViewMatrix", under.Values);
            capi.Render.RenderMesh(windArrow);
            prog.Uniform("rgbaIn", new Vec4f(0.75f, 0.9f, 1f, 1f));
            prog.UniformMatrix("modelViewMatrix", mat.Values);
            capi.Render.RenderMesh(windArrow);
        }

        public override void OnMouseMoveClient(MouseEvent args, GuiElementMap map, StringBuilder hoverText)
        {
            var sys = Sys;
            if (!Active || sys == null) return;
            double zoom = PixelsPerBlock(map);
            foreach (var s in sys.MapShips())
            {
                Screen(map, s.X, s.Z, out float sx, out float sy);
                double r = Math.Max(6 * GuiElement.scaled(1), s.Length * zoom / 2);
                if (Math.Abs(args.X - sx) > r || Math.Abs(args.Y - sy) > r) continue;
                string name = s.Name ?? Lang.Get("hansa:map-ship-unnamed");
                // (0.3.12: knots with m/s, as the wind panel says every speed)
                hoverText.AppendLine(Lang.Get(s.Own ? "hansa:map-ship-own" : "hansa:map-ship", name, HansaMod.HeadingDeg(s.Yaw),
                    WindPanel.Speed(Math.Abs(s.Speed))) + (s.Loaded ? "" : " · " + Lang.Get("hansa:map-ship-lastseen")));
            }
            var ep = capi.World.Player?.Entity;
            var sea = sys.Sea;
            if (ep != null && sea != null && Aboard(ep))
            {
                Screen(map, ep.Pos.X, ep.Pos.Z, out float px, out float py);
                if (Math.Abs(args.X - px) < 40 * GuiElement.scaled(1) && Math.Abs(args.Y - py) < 40 * GuiElement.scaled(1))
                {
                    var w = sea.WindAt(ep.Pos.X, ep.Pos.Z);
                    if (w.Strength >= 0.02) hoverText.AppendLine(WindPanel.WindLine(w));
                }
            }
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>A design's plan (null: the rowing boat's), uploaded once.</summary>
        MeshRef Plan(string code, out double length)
        {
            if (code == null)
            {
                length = 7.5;
                return boatPlan ??= capi.Render.UploadMesh(PlanMesh(BoatOutline()));
            }
            if (plans.TryGetValue(code, out var m)) { length = planLength[code]; return m; }
            var def = Sys?.Hansa?.GetShip(code);
            var outline = def != null ? ShipPlan.Outline(def) : null;
            if (outline == null) { length = 0; return null; }
            length = outline[^1].X - outline[0].X;
            planLength[code] = length;
            return plans[code] = capi.Render.UploadMesh(PlanMesh(outline));
        }

        static ShipPlan.Station[] BoatOutline()
        {
            var st = new ShipPlan.Station[13];
            for (int i = 0; i < st.Length; i++)
            {
                double t = -1 + 2.0 * i / (st.Length - 1);
                double w = 1.35 * Math.Sqrt(Math.Max(0, 1 - t * t * t * t));
                st[i] = new ShipPlan.Station(t * 3.75, -w, w);
            }
            return st;
        }

        /// <summary>A strip of quads between the stations' port and starboard edges, in her own frame (her origin at 0).</summary>
        static MeshData PlanMesh(ShipPlan.Station[] st)
        {
            var m = new MeshData(st.Length * 2, (st.Length - 1) * 6, false, true, true, false);
            foreach (var s in st)
            {
                m.AddVertex((float)s.X, (float)s.Port, 0, 0, 0, -1);
                m.AddVertex((float)s.X, (float)s.Starboard, 0, 0, 0, -1);
            }
            for (int i = 0; i + 1 < st.Length; i++)
            {
                int a = i * 2;
                m.AddIndex(a); m.AddIndex(a + 1); m.AddIndex(a + 3);
                m.AddIndex(a); m.AddIndex(a + 3); m.AddIndex(a + 2);
            }
            return m;
        }

        /// <summary>An arrow along +x from -1 to 0 (its head at the origin), 2 wide.</summary>
        static MeshData ArrowMesh()
        {
            var m = new MeshData(7, 9, false, true, true, false);
            m.AddVertex(-1f, -0.25f, 0, 0, 0, -1);
            m.AddVertex(-0.4f, -0.25f, 0, 0, 0, -1);
            m.AddVertex(-0.4f, 0.25f, 0, 0, 0, -1);
            m.AddVertex(-1f, 0.25f, 0, 0, 0, -1);
            m.AddVertex(-0.45f, -1f, 0, 0, 0, -1);
            m.AddVertex(0f, 0f, 0, 0, 0, -1);
            m.AddVertex(-0.45f, 1f, 0, 0, 0, -1);
            m.AddIndex(0); m.AddIndex(1); m.AddIndex(2);
            m.AddIndex(0); m.AddIndex(2); m.AddIndex(3);
            m.AddIndex(4); m.AddIndex(5); m.AddIndex(6);
            return m;
        }

        LoadedTexture Label(string text, bool own)
        {
            string key = (own ? "1" : "0") + text;
            if (labels.TryGetValue(key, out var t)) return t;
            if (labels.Count > 64) { foreach (var l in labels.Values) l.Dispose(); labels.Clear(); }
            var font = CairoFont.WhiteDetailText().WithFontSize(12).WithStroke(new double[] { 0, 0, 0, 0.9 }, 2);
            if (own) font.Color = new double[] { 1, 0.85, 0.35, 1 };
            return labels[key] = capi.Gui.TextTexture.GenUnscaledTextTexture(text, font);
        }

        public override void Dispose()
        {
            foreach (var m in plans.Values) m?.Dispose();
            plans.Clear();
            boatPlan?.Dispose(); boatPlan = null;
            windArrow?.Dispose(); windArrow = null;
            foreach (var m in wedges.Values) m?.Dispose();
            wedges.Clear();
            foreach (var l in labels.Values) l.Dispose();
            labels.Clear();
        }
    }
}
