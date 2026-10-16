using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// Draws ships with the standard block shader: the exterior at the light where the ship is, the interior
    /// (cabins, lower deck) darker and lantern-warm, each sail scaled toward its yard as it's furled, and the wheel and
    /// rudder turned with the helm. Vanilla blocks aboard (doors, gates, glass panes, lanterns, chests, beds) are drawn
    /// from their own meshes where the design puts them.
    /// When the local player is aboard, the ship is drawn relative to their seat, like vanilla does for mounts,
    /// so ship and camera never drift apart. It's drawn with its pitch and roll on the waves.
    /// With shadows on in the graphics settings she's drawn into the sun's shadow maps too (RenderShadows), and the
    /// standard shader shades her with them, like everything else.
    /// </summary>
    public partial class ShipRenderer : IRenderer, IDisposable, Sea.IWaterClipSource
    {
        readonly ICoreClientAPI capi;
        readonly ShipClientSystem sys;
        readonly Dictionary<string, Meshes> meshes = new();
        readonly Dictionary<long, List<ShipLight>> lights = new();
        readonly Dictionary<long, Decor> decor = new();
        readonly Matrixf model = new Matrixf();
        readonly float[] furl = new float[16];
        readonly float[] tmp = new float[16];
        readonly float[] tmp2 = new float[16];
        readonly Vec4f interiorLight = new Vec4f();

        public double RenderOrder => 0.45;
        public int RenderRange => 9999;

        class Meshes
        {
            public MultiTextureMeshRef Exterior, Interior;
            public readonly Dictionary<string, MultiTextureMeshRef> Skin = new();
            public System.Threading.Tasks.Task<(ShipSkinFile File, HullDamageSkin Hull)> SkinLoad;
            public readonly List<(SailDef Sail, MultiTextureMeshRef Mesh)> Sails = new();
            /// <summary>The mesh file's quads (kept for building damage) and its textures in the atlas.</summary>
            public ShipSkinFile File;
            public TextureAtlasPosition[] Tex;
            /// <summary>Her planking by hull panel, to draw damage on (null: the mesh file doesn't mark it, or it failed).</summary>
            public HullDamageSkin Hull;
            /// <summary>The hull groups split for drawing damage, built the first time a ship of hers is damaged.</summary>
            public HullSplit Split;
            /// <summary>Torn (1) and shredded (2) sails, by the sail's index, built the first time one is.</summary>
            public readonly Dictionary<(int Sail, int Level), MultiTextureMeshRef> TornSails = new();
            /// <summary>(0.3.14) The design, for her surfaces (<see cref="ShipDefinition.Surfaces"/>) built once her mesh file is in.</summary>
            public ShipDefinition Def;
            /// <summary>(0.3.14, notes 150-152) Each sail mesh's vertices (whole, torn, shredded), to press a ship's against her rigging.</summary>
            public readonly Dictionary<MultiTextureMeshRef, MeshData> SailData = new();
            public SailPress Press;
        }

        /// <summary>
        /// A design's hull groups taken apart for drawing damage: what isn't a panel's planking (one mesh a group), and each
        /// chunk of planking as the sound design has it (a damaged ship draws her own instead of the chunks she changed).
        /// </summary>
        class HullSplit
        {
            public readonly Dictionary<string, MultiTextureMeshRef> Static = new();
            public MultiTextureMeshRef[] Sound;
            public readonly Dictionary<string, List<int>> ChunksOf = new();
            /// <summary>The damage textures (<see cref="HullDamageSkin.Textures"/>) in the atlas, as many as have been asked for.</summary>
            public TextureAtlasPosition[] Tex = Array.Empty<TextureAtlasPosition>();

            public void Dispose()
            {
                foreach (var r in Static.Values) r?.Dispose();
                Static.Clear();
                if (Sound != null) foreach (var r in Sound) r?.Dispose();
                Sound = null;
            }
        }

        /// <summary>One ship's damage as drawn: each panel's look, and her own meshes for the chunks it changes.</summary>
        class DamageLook
        {
            public int Version = int.MinValue;
            // (long.MinValue / 2, not long.MinValue: now - long.MinValue overflows to a negative number, and till 0.3.7 her
            // damage was never looked at, so no ship's damage was drawn)
            public long CheckedMs = long.MinValue / 2;
            public byte[] Looks;
            public readonly Dictionary<int, MultiTextureMeshRef> Own = new();
            public readonly SortedSet<int> Pending = new();
            /// <summary>Her sails as drawn: 0 whole, 1 torn, 2 shredded (by the sail's index).</summary>
            public int[] Sails;

            public void Dispose()
            {
                foreach (var r in Own.Values) r?.Dispose();
                Own.Clear();
            }
        }

        readonly Dictionary<long, DamageLook> damageLooks = new();

        /// <summary>The water drawn in a ship's hold: its mesh (rebuilt every frame: it stays level while she rolls) and how it's drawn now.</summary>
        class FloodMesh
        {
            public MeshRef Mesh;
            public MeshData Data;
            public int Capacity;
            /// <summary>Its level as drawn (ship-local, eased toward hers), her roll it lags behind (sloshing), and its height over her origin (world, level).</summary>
            public double Level = double.NaN, LagRoll, WorldDy = double.NaN;

            public void Dispose()
            {
                Mesh?.Dispose();
                Mesh = null;
            }
        }

        readonly Dictionary<long, FloodMesh> floods = new();
        bool floodFailed;
        int waterTexture;

        /// <summary>One vanilla block (or item) drawn on one ship.</summary>
        class Piece
        {
            public FixtureDef Fixture;
            public MultiTextureMeshRef Mesh, MeshOpen;
            public readonly float[] Fit = new float[16];
            public bool Interior, Glass;
            /// <summary>
            /// The firehearth: the look it was built for (<see cref="HearthKey"/>), and what's on its fire as vanilla draws it on
            /// a firepit: a pot and its lid, a meal in the pot, a joint on the spit (Content not ours to dispose when Shared: the
            /// game's meal mesh cache keeps it).
            /// </summary>
            public string Key;
            public MultiTextureMeshRef Content, Lid;
            public bool ContentShared;
            public readonly float[] ContentFit = new float[16];
            /// <summary>The clay oven: its fuel and what's baking in it, each placed in the oven block's space (drawn with it).</summary>
            public List<MultiTextureMeshRef> Extra;
            /// <summary>
            /// (0.3.12, note 123) A door put aboard: its piece's id (0: not one), the hinge (x, z, ship-local) and how far it
            /// swings open (radians about y, into the cell it opens into); drawn swinging as the design's doors do.
            /// </summary>
            public int DoorId;
            public float HingeX, HingeZ, Swing;
            /// <summary>(0.3.20, step B) A hatch's leaf: its piece's id (0: not one), the axis its hinge runs along (0 x, 2 z) and the hinge's height.</summary>
            public int HatchId, HatchAxis;
            public float HingeY;

            public void Dispose()
            {
                Mesh?.Dispose();
                MeshOpen?.Dispose();
                if (!ContentShared) Content?.Dispose();
                Lid?.Dispose();
                if (Extra != null) foreach (var e in Extra) e?.Dispose();
                Mesh = MeshOpen = Content = Lid = null;
                Extra = null;
            }
        }

        class Decor
        {
            public int Version = int.MinValue;
            public readonly List<Piece> Pieces = new();
            /// <summary>How far each door, gate and hatch stands open as drawn (eased toward its state), and how far down the anchor is.</summary>
            public readonly Dictionary<string, float> Open = new();
            public float Anchor = -1;
            /// <summary>(0.3.14) Built with her surfaces in (partitions drawn out to them): built again once they are.</summary>
            public bool Surfaces;
            /// <summary>(0.3.24, note 197) Built with her mesh file loaded (a boat on the gallows rests on its beams): built again once it is.</summary>
            public bool Skin;

            public void Dispose()
            {
                foreach (var p in Pieces) p.Dispose();
                Pieces.Clear();
            }
        }

        class ShipLight : IPointLight
        {
            public Vec3f Color { get; set; } = new Vec3f();
            public Vec3d Pos { get; } = new Vec3d();
            public double[] Local;
            public Vec3f On;
            public FixtureDef Stove;
            /// <summary>The firehearth's fire: on while it burns (flickering), a glow of embers while it smoulders.</summary>
            public bool Hearth;
        }

        public ShipRenderer(ICoreClientAPI capi, ShipClientSystem sys)
        {
            this.capi = capi;
            this.sys = sys;
        }

        // ------------------------------------------------------------------ meshes shared by a design

        /// <summary>(0.3.19) A design's meshes made (once) before any ship of it is drawn.</summary>
        public void Preload(ShipDefinition def)
        {
            if (def != null && !meshes.ContainsKey(def.Code)) Get(def);
        }

        Meshes Get(ShipDefinition def)
        {
            if (meshes.TryGetValue(def.Code, out var m)) return m;
            m = new Meshes { Exterior = Tesselate(def.Shapes?.Exterior), Interior = Tesselate(def.Shapes?.Interior), Def = def };
            m.SkinLoad = def.SkinFile != null ? System.Threading.Tasks.Task.FromResult((def.SkinFile, (HullDamageSkin)null)) : LoadSkin(def.Shapes?.Skin, def.DamageParts);
            foreach (var s in def.Sails)
            {
                MeshData smd = null;
                var r = s.Shape != null ? Tesselate(s.Shape, out smd, s.Offset) : null;
                if (r != null && smd != null) m.SailData[r] = smd;
                m.Sails.Add((s, r));
            }
            meshes[def.Code] = m;
            return m;
        }

        MultiTextureMeshRef Tesselate(string shapeLoc) => Tesselate(shapeLoc, out _);

        MultiTextureMeshRef Tesselate(string shapeLoc, out MeshData md, double[] offset = null)
        {
            md = null;
            if (shapeLoc == null) return null;
            var loc = AssetLocation.Create(shapeLoc, "hansa").WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            var shape = Shape.TryGet(capi, loc);
            if (shape == null)
            {
                capi.Logger.Error("[hansa] Ship shape {0} not found", loc);
                return null;
            }
            PrepareShape(capi, shape, loc.ToString());
            var texSource = new ShapeTextureSource(capi, shape, loc.ToString());
            capi.Tesselator.TesselateShape("hansa ship", shape, out md, texSource);
            // a design drawn all from its mesh (the ship's boat) has nothing here
            if (md == null || md.VerticesCount == 0) return null;
            if (offset != null) md.Translate((float)offset[0], (float)offset[1], (float)offset[2]);
            return capi.Render.UploadMultiTextureMesh(md);
        }

        static readonly HashSet<string> shapesChecked = new();

        /// <summary>
        /// A ship's shape before it's tesselated: every face's uv inside its texture (<see cref="ShapeUvs"/>: the atlas
        /// doesn't repeat), and, once a session per shape, a warning for each texture a face uses that the shape doesn't
        /// define or whose image the game hasn't got (drawn as the "unknown" texture).
        /// </summary>
        internal static void PrepareShape(ICoreClientAPI capi, Shape shape, string loc)
        {
            var (moved, squashed) = ShapeUvs.FitShape(shape);
            if (!shapesChecked.Add(loc)) return;
            if (moved > 0)
                capi.Logger.Warning("[hansa] Shape {0}: {1} faces had uvs outside their texture (moved inside it; {2} squashed): the atlas would draw its neighbours there", loc, moved, squashed);
            foreach (var m in ShapeUvs.MissingTextures(shape, a => capi.Assets.Exists(a)))
                capi.Logger.Warning("[hansa] Shape {0}: texture {1} not found, drawn as unknown", loc, m);
        }

        /// <summary>
        /// The smooth parts the generator can't express as boxes (planked hull inside and out, masts, rigging, the
        /// wheel): a list of quads per group, each [texture index, then per corner x y z u v nx ny nz] with u, v in
        /// texels of that texture (<see cref="ShipSkinFile"/>). A few MB of JSON, so it's parsed off the main thread and
        /// uploaded once it's ready. The hull's planking is sorted into panels there too, for drawing damage.
        /// </summary>
        System.Threading.Tasks.Task<(ShipSkinFile, HullDamageSkin)> LoadSkin(string path, DamageLayout layout)
        {
            if (path == null) return null;
            var asset = capi.Assets.TryGet(AssetLocation.Create(path, "hansa"));
            if (asset == null)
            {
                capi.Logger.Error("[hansa] Ship mesh {0} not found", path);
                return null;
            }
            byte[] data = asset.Data;
            var logger = capi.Logger;
            return System.Threading.Tasks.Task.Run(() =>
            {
                var file = JsonConvert.DeserializeObject<ShipSkinFile>(System.Text.Encoding.UTF8.GetString(data));
                // (the atlas doesn't repeat: a quad's uv kept inside its texture, as the shapes' faces are)
                int moved = 0, squashed = 0;
                if (file?.Groups != null)
                    foreach (var g in file.Groups.Values)
                        foreach (var q in g)
                        {
                            bool s = false;
                            if (ShapeUvs.FitQuad(q, ref s)) moved++;
                            if (s) squashed++;
                        }
                if (moved > 0) logger.Warning("[hansa] Ship mesh {0}: {1} quads had uvs outside their texture (moved inside it; {2} squashed)", path, moved, squashed);
                HullDamageSkin hull = null;
                try
                {
                    if (layout != null && file?.HullOut != null) hull = new HullDamageSkin(file, layout);
                }
                catch (Exception e)
                {
                    // the ship is still drawn, only her damage isn't
                    logger.Error("[hansa] Couldn't sort the planking of {0} into panels, damage won't be drawn on it: {1}", path, e);
                }
                return (file, hull);
            });
        }

        void UploadSkin(Meshes m)
        {
            var load = m.SkinLoad;
            if (load == null || !load.IsCompleted) return;
            m.SkinLoad = null;
            if (load.IsFaulted)
            {
                capi.Logger.Error("[hansa] Ship mesh failed to load: {0}", load.Exception?.InnerException);
                return;
            }
            var (file, hull) = load.Result;
            var tex = new TextureAtlasPosition[file.Textures.Length];
            for (int i = 0; i < tex.Length; i++)
            {
                // "base++3~overlay" paths are composited by the atlas (tarred and caulked oak)
                capi.BlockTextureAtlas.GetOrInsertTexture(AssetLocation.Create(file.Textures[i]), out _, out var pos);
                if (pos == null || ShapeUvs.MissingImage(file.Textures[i], a => capi.Assets.Exists(a)) != null)
                    capi.Logger.Warning("[hansa] Ship mesh texture {0} not found, drawn as unknown", file.Textures[i]);
                tex[i] = pos ?? capi.BlockTextureAtlas.UnknownTexturePosition;
            }
            LogHullTextures(file.Textures, tex);
            foreach (var kv in file.Groups)
            {
                // (0.3.20, step B) the deck cells' groups are drawn by chunks of her own (SlotRenderer), never whole
                if (kv.Key.StartsWith("slot:")) continue;
                // (0.3.14) a sail's (the lateen's) vertices kept, to press it against her rigging
                if (m.Def?.Sails != null && Array.Exists(m.Def.Sails, sd => sd.Mesh == kv.Key) && SkinData(kv.Value, tex) is MeshData sailData)
                    m.SailData[m.Skin[kv.Key] = capi.Render.UploadMultiTextureMesh(sailData)] = sailData;
                else m.Skin[kv.Key] = BuildSkin(kv.Value, tex);
            }
            m.File = file;
            m.Tex = tex;
            m.Hull = hull != null && hull.Usable ? hull : null;
            BuildSurfaces(m.Def, file);
        }

        /// <summary>
        /// (0.3.14, notes 148, 149) Her surfaces a partition is drawn out to (<see cref="ShipSurfaces"/>): her mesh file's interior,
        /// planking and inside planking, and her shapes' faces, built off the main thread; her pieces are built again once
        /// they're in (<see cref="Decor.Surfaces"/>).
        /// </summary>
        void BuildSurfaces(ShipDefinition def, ShipSkinFile file)
        {
            if (def == null || def.Surfaces != null || file == null) return;
            var shapes = new List<Shape>();
            foreach (var loc in new[] { def.Shapes?.Interior, def.Shapes?.Exterior })
            {
                if (loc == null) continue;
                var shape = Shape.TryGet(capi, AssetLocation.Create(loc, "hansa").WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
                if (shape != null) shapes.Add(shape);
            }
            var logger = capi.Logger;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var s = ShipSurfaces.From(file);
                    foreach (var sh in shapes) s.AddShape(sh);
                    def.Surfaces = s;
                }
                catch (Exception e)
                {
                    logger.Warning("[hansa] Couldn't sort out {0}'s surfaces; partitions are drawn to her cells only: {1}", def.Code, e.Message);
                }
            });
        }

        bool texturesLogged;

        /// <summary>
        /// Once a session: what the atlas really holds for plain, tarred and caulked oak (the mean of the colours the
        /// atlas sampled when the texture went in). If tarred or caulked oak comes out the same as plain oak, the
        /// composite didn't happen.
        /// </summary>
        void LogHullTextures(string[] paths, TextureAtlasPosition[] tex)
        {
            if (texturesLogged) return;
            texturesLogged = true;
            var sb = new System.Text.StringBuilder("[hansa] Hull textures in the atlas (mean of the atlas' colour samples):");
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].Contains("++") || paths[i].EndsWith("planks/oak1") || paths[i].Contains("copper1")) sb.Append($" {paths[i]} {MeanColour(tex[i])};");
            }
            const string caulked = "game:block/wood/planks/oak1++0~hansa:block/caulk";
            capi.BlockTextureAtlas.GetOrInsertTexture(new AssetLocation(caulked), out _, out var caulk);
            sb.Append($" {caulked} {MeanColour(caulk)}");
            capi.Logger.Notification(sb.ToString());
        }

        static string MeanColour(TextureAtlasPosition tp)
        {
            if (tp?.RndColors == null || tp.RndColors.Length == 0) return "(no samples)";
            long r = 0, g = 0, b = 0;
            foreach (int c in tp.RndColors) { r += (c >> 16) & 255; g += (c >> 8) & 255; b += c & 255; }
            int n = tp.RndColors.Length;
            return $"#{r / n:X2}{g / n:X2}{b / n:X2}";
        }

        MultiTextureMeshRef BuildSkin(float[][] quads, TextureAtlasPosition[] tex)
        {
            var md = SkinData(quads, tex);
            return md == null ? null : capi.Render.UploadMultiTextureMesh(md);
        }

        static MeshData SkinData(float[][] quads, TextureAtlasPosition[] tex)
        {
            if (quads.Length == 0) return null;
            var md = new MeshData(quads.Length * 4, quads.Length * 6, false, true, true, true);
            foreach (var q in quads)
            {
                var tp = tex[(int)q[0]];
                float du = (tp.x2 - tp.x1) / 16f, dv = (tp.y2 - tp.y1) / 16f;
                int b = md.VerticesCount;
                for (int k = 0; k < 4; k++)
                {
                    int o = 1 + k * 8;
                    md.AddWithFlagsVertex(q[o], q[o + 1], q[o + 2], tp.x1 + q[o + 3] * du, tp.y1 + q[o + 4] * dv, -1,
                        VertexFlags.PackNormal(q[o + 5], q[o + 6], q[o + 7]));
                }
                md.AddIndices(b, b + 1, b + 2, b, b + 2, b + 3);
                md.AddTextureId(tp.atlasTextureId);
            }
            return md;
        }

        static MultiTextureMeshRef SkinGroup(Meshes m, string group) => m.Skin.TryGetValue(group, out var r) ? r : null;

        // ------------------------------------------------------------------ damage drawn on her (per ship)

        /// <summary>Chunks built again at most this many a frame (a ship coming into view badly damaged builds over a few frames).</summary>
        const int ChunksPerFrame = 6;
        /// <summary>How often a ship's damage is looked at again when it changes (ms).</summary>
        const int DamageCheckMs = 500;
        bool damageFailed;

        /// <summary>
        /// Her damage as drawn follows her damage (<see cref="EntityShip.DamageVersion"/>), at most every half second: each
        /// hull panel's look (<see cref="HullLook"/>) is worked out again, and the chunks of planking whose looks changed
        /// (or that a hole reaches) are queued and built a few a frame. Her sails' state too. Anything going wrong here is
        /// logged once and her damage isn't drawn any more this session; she's still drawn.
        /// </summary>
        void UpdateDamage(EntityShip ship, Meshes m)
        {
            // (not before her mesh is in: its planking is what the damage is drawn on)
            if (damageFailed || ship.PartDamage == null || m.SkinLoad != null) return;
            try
            {
                if (!damageLooks.TryGetValue(ship.EntityId, out var dl)) damageLooks[ship.EntityId] = dl = new DamageLook();
                long now = capi.ElapsedMilliseconds;
                if (dl.Version != ship.DamageVersion && now - dl.CheckedMs >= DamageCheckMs)
                {
                    dl.Version = ship.DamageVersion;
                    dl.CheckedMs = now;
                    if (m.Hull != null && ship.PartDamage.Count == m.Hull.Layout.Count)
                    {
                        var looks = m.Hull.Looks(ship.PartDamage);
                        foreach (int c in m.Hull.Affected(dl.Looks, looks)) dl.Pending.Add(c);
                        dl.Looks = looks;
                    }
                    dl.Sails = SailStates(ship);
                }
                for (int n = 0; n < ChunksPerFrame && dl.Pending.Count > 0 && m.Hull != null; n++)
                {
                    int c = dl.Pending.Min;
                    dl.Pending.Remove(c);
                    RebuildChunk(m, dl, c, ship);
                }
            }
            catch (Exception e)
            {
                damageFailed = true;
                capi.Logger.Error("[hansa] Drawing ship damage failed; it's off until the game is restarted: {0}", e);
            }
        }

        /// <summary>One chunk of her planking as her damage (and her paint: PaintRenderer) has it: her own mesh, or none (the design's sound one is drawn).</summary>
        void RebuildChunk(Meshes m, DamageLook dl, int c, EntityShip ship = null)
        {
            if (dl.Own.TryGetValue(c, out var old))
            {
                old?.Dispose();
                dl.Own.Remove(c);
            }
            var chunk = m.Hull.Chunks[c];
            var paint = ChunkPaint(ship, m, chunk);
            if ((dl.Looks == null || !m.Hull.Damaged(chunk, dl.Looks)) && paint == null) return;
            EnsureSplit(m);
            var quads = m.Hull.Build(chunk, dl.Looks ?? new byte[m.Hull.Layout.Count], paint);
            var mesh = BuildSkin(quads.ToArray(), SplitTextures(m));
            if (mesh != null) dl.Own[c] = mesh;
        }

        /// <summary>The design's hull groups taken apart (the first time one of her ships is damaged).</summary>
        void EnsureSplit(Meshes m)
        {
            if (m.Split != null) return;
            var s = new HullSplit { Sound = new MultiTextureMeshRef[m.Hull.Chunks.Count] };
            SplitTextures(m, s);
            foreach (string g in HullDamageSkin.HullGroups)
            {
                if (!m.Hull.Static.ContainsKey(g)) continue;
                s.Static[g] = BuildSkin(m.Hull.StaticQuads(g).ToArray(), m.Tex);
                s.ChunksOf[g] = new List<int>();
            }
            foreach (var c in m.Hull.Chunks)
            {
                s.Sound[c.Index] = BuildSkin(m.Hull.SoundQuads(c).ToArray(), m.Tex);
                if (s.ChunksOf.TryGetValue(c.Group, out var list)) list.Add(c.Index);
            }
            m.Split = s;
        }

        /// <summary>
        /// The damage textures in the atlas, as many as the hull's builds have asked for so far: "base++0~hansa:block/dmg-…"
        /// composited like the tar (inserted as they're first needed).
        /// </summary>
        TextureAtlasPosition[] SplitTextures(Meshes m, HullSplit s = null)
        {
            s ??= m.Split;
            var names = m.Hull.Textures;
            if (s.Tex.Length == names.Count) return s.Tex;
            var tex = new TextureAtlasPosition[names.Count];
            for (int i = 0; i < tex.Length; i++)
            {
                if (i < s.Tex.Length) { tex[i] = s.Tex[i]; continue; }
                if (i < m.Tex.Length) { tex[i] = m.Tex[i]; continue; }
                capi.BlockTextureAtlas.GetOrInsertTexture(AssetLocation.Create(names[i]), out _, out var pos);
                tex[i] = pos ?? capi.BlockTextureAtlas.UnknownTexturePosition;
            }
            s.Tex = tex;
            return tex;
        }

        /// <summary>
        /// A hull group of her smooth mesh, drawn (or cast into the shadow map): as the design has it while her damage leaves
        /// it alone; else what isn't planking, and each chunk of planking, her own where her damage changed it.
        /// </summary>
        void DrawHull(IRenderAPI rapi, EntityShip ship, Meshes m, string group, bool shadow)
        {
            // painted zones in it: drawn by zones (PaintRenderer; her shadow is the same either way)
            if (!shadow && DrawPainted(rapi, ship, m, group)) return;
            DamageLook dl = null;
            List<int> chunks = null;
            bool own = !damageFailed && m.Split != null && damageLooks.TryGetValue(ship.EntityId, out dl) && dl.Own.Count > 0 &&
                       m.Split.ChunksOf.TryGetValue(group, out chunks);
            if (own)
            {
                own = false;
                foreach (int c in chunks) if (dl.Own.ContainsKey(c)) { own = true; break; }
            }
            if (!own)
            {
                var whole = SkinGroup(m, group);
                if (shadow) CastShadow(rapi, whole, null); else Draw(rapi, whole);
                return;
            }
            m.Split.Static.TryGetValue(group, out var rest);
            if (shadow) CastShadow(rapi, rest, null); else Draw(rapi, rest);
            foreach (int c in chunks)
            {
                var mesh = dl.Own.TryGetValue(c, out var o) ? o : m.Split.Sound[c];
                if (shadow) CastShadow(rapi, mesh, null); else Draw(rapi, mesh);
            }
        }

        /// <summary>Each sail's state as drawn: 0 whole, 1 torn, 2 shredded (by the design's sail index).</summary>
        static int[] SailStates(EntityShip ship)
        {
            var l = ship.DamageParts;
            var s = new int[ship.Def.Sails.Length];
            if (l == null) return s;
            foreach (var p in l.Parts)
            {
                if (p.Kind != DamageKind.Sail || p.Sail < 0 || p.Sail >= s.Length) continue;
                var st = ship.StateOfPart(p.Index);
                s[p.Sail] = st == PartState.Shredded ? 2 : st == PartState.Torn ? 1 : 0;
            }
            return s;
        }

        /// <summary>
        /// What the damage drawing has for a ship (<c>.hansa damagecheck</c>, 0.3.7: the user found a holed panel drawn
        /// as plain planking): her mesh file loaded and her hull's planking sorted into panels (its chunks), whether drawing
        /// damage has failed this session, her damage state against the design's parts, how many panels have a look that
        /// isn't sound, the chunks built and still to build, her sails as drawn. One line each.
        /// </summary>
        public string DamageReport(EntityShip ship)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"Damage drawn on {ship.GetName()} #{ship.EntityId} ({ship.Def?.Code}):");
            if (ship.Def == null || !meshes.TryGetValue(ship.Def.Code, out var m))
            {
                sb.Append("\n- her meshes aren't loaded yet (not drawn here so far)");
                return sb.ToString();
            }
            string skin = m.SkinLoad != null ? (m.SkinLoad.IsCompleted ? "loaded, not uploaded yet" : "still loading")
                : m.File == null ? "failed to load (see the client log)" : $"loaded, {m.File.Textures?.Length ?? 0} textures";
            sb.Append($"\n- mesh file: {skin}");
            sb.Append(m.Hull != null ? $"\n- hull skin: usable, {m.Hull.Chunks.Count} chunks ({m.Hull.Layout.Count} parts in its layout)"
                : "\n- hull skin: none (the mesh file marks no planking, or sorting it into panels failed: see the client log)");
            sb.Append($"\n- drawing damage failed this session: {(damageFailed ? "yes (see the client log)" : "no")}");
            int layoutCount = ship.DamageParts?.Count ?? -1;
            sb.Append($"\n- damage state: {(ship.PartDamage == null ? "none received" : ship.PartDamage.Count + " parts")} against " +
                      $"{(layoutCount < 0 ? "no layout" : layoutCount + " in her layout")}{(m.Hull != null ? $" ({m.Hull.Layout.Count} in the skin's)" : "")}; " +
                      $"version {ship.DamageVersion}");
            if (!damageLooks.TryGetValue(ship.EntityId, out var dl))
            {
                sb.Append("\n- looks: not worked out yet");
                return sb.ToString();
            }
            int notSound = 0, holed = 0;
            if (dl.Looks != null)
                foreach (byte b in dl.Looks)
                {
                    if (b == 0) continue;
                    notSound++;
                    if (HullLook.Look(b) == HullLook.Holed) holed++;
                }
            sb.Append($"\n- looks: {(dl.Looks == null ? "none yet" : $"{notSound} panels not sound ({holed} holed)")}, worked out for version {dl.Version}" +
                      (dl.Version != ship.DamageVersion ? " (behind hers)" : ""));
            sb.Append($"\n- chunks: {dl.Own.Count} built as damaged, {dl.Pending.Count} waiting to be built" +
                      (m.Split == null ? " (her hull isn't split for drawing damage yet)" : ""));
            if (dl.Sails != null && ship.Def.Sails != null)
            {
                var sails = new List<string>();
                for (int i = 0; i < dl.Sails.Length && i < ship.Def.Sails.Length; i++)
                    sails.Add($"{ship.Def.Sails[i].Code} {(dl.Sails[i] == 2 ? "shredded" : dl.Sails[i] == 1 ? "torn" : "whole")}");
                sb.Append("\n- sails: " + string.Join(", ", sails));
            }
            return sb.ToString();
        }

        /// <summary>How a sail of hers is drawn now: 0 whole, 1 torn, 2 shredded.</summary>
        int SailLevel(EntityShip ship, int sail) =>
            !damageFailed && damageLooks.TryGetValue(ship.EntityId, out var dl) && dl.Sails != null && sail >= 0 && sail < dl.Sails.Length ? dl.Sails[sail] : 0;

        /// <summary>The three torn and two shredded holes (textures/block/sail-*.png), each with its frayed edge.</summary>
        static string TornLinen(string linen, int level, int pick) => level >= 2
            ? $"{linen}++0~hansa:block/sail-shredfray-{(char)('a' + pick % 2)}++8~hansa:block/sail-shred-{(char)('a' + pick % 2)}"
            : $"{linen}++0~hansa:block/sail-fray-{(char)('a' + pick % 3)}++8~hansa:block/sail-hole-{(char)('a' + pick % 3)}";

        /// <summary>
        /// Which piece of a sail is torn: a fixed pick by the sail and the piece, so its holes stay where they are. Torn: about
        /// a fifth of the pieces have a hole; shredded: over half have big ones and one in eight is gone (-1). Else 0 (whole)
        /// or 1 + which hole.
        /// </summary>
        static int TornPiece(int sail, int piece, int level)
        {
            uint h = (uint)(sail * 7919 + piece * 104729 + 17) * 2654435761u;
            double r = (h >> 8 & 0xFFFF) / 65535.0;
            int pick = (int)(h >> 24 & 7);
            if (level >= 2) return r < 0.12 ? -1 : r < 0.68 ? 1 + pick : 0;
            return r < 0.2 ? 1 + pick : 0;
        }

        /// <summary>
        /// A torn or shredded sail (built once per design, sail and level): a square sail's shape tesselated again with some
        /// of its cloth pieces holed (a hole texture cut out of the linen, drawn alpha-tested) and, shredded, some gone; the
        /// lateen's quads with some of them holed. Null: not possible, draw it whole.
        /// </summary>
        MultiTextureMeshRef TornSail(Meshes m, int index, SailDef sail, int level)
        {
            if (m.TornSails.TryGetValue((index, level), out var mesh)) return mesh;
            m.TornSails[(index, level)] = null;
            try
            {
                if (sail.Shape != null)
                {
                    var loc = AssetLocation.Create(sail.Shape, "hansa").WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                    var shape = Shape.TryGet(capi, loc);
                    if (shape?.Elements == null || shape.Textures == null) return null;
                    PrepareShape(capi, shape, loc.ToString());
                    int piece = 0;
                    var added = new HashSet<string>();
                    void Tear(ShapeElement e)
                    {
                        int t = TornPiece(index, piece++, level);
                        if (e.FacesResolved != null)
                            for (int f = 0; f < e.FacesResolved.Length; f++)
                            {
                                var face = e.FacesResolved[f];
                                if (face == null || !shape.Textures.TryGetValue(face.Texture ?? "", out var tl) || tl == null || !tl.Path.Contains("linen")) continue;
                                if (t < 0) { e.FacesResolved[f] = null; continue; }
                                if (t == 0) continue;
                                string code = face.Texture + "-torn" + level + "-" + (t - 1);
                                if (added.Add(code)) shape.Textures[code] = AssetLocation.Create(TornLinen(tl.ToString(), level, t - 1));
                                face.Texture = code;
                            }
                        if (e.Children != null) foreach (var ch in e.Children) Tear(ch);
                    }
                    foreach (var e in shape.Elements) Tear(e);
                    capi.Tesselator.TesselateShape("hansa torn sail", shape, out MeshData md, new ShapeTextureSource(capi, shape, loc + " (torn)"));
                    if (md != null && sail.Offset != null) md.Translate((float)sail.Offset[0], (float)sail.Offset[1], (float)sail.Offset[2]);
                    if (md != null && md.VerticesCount > 0) m.SailData[mesh = capi.Render.UploadMultiTextureMesh(md)] = md;
                }
                else if (sail.Mesh != null && m.File?.Groups != null && m.File.Groups.TryGetValue(sail.Mesh, out var quads))
                {
                    // the lateen: its quads, some holed or gone, the holed ones on a texture of their own
                    var names = new List<string>(m.File.Textures);
                    var tex = new List<TextureAtlasPosition>(m.Tex);
                    var torn = new List<float[]>();
                    for (int q = 0; q < quads.Length; q++)
                    {
                        int t = TornPiece(index, q, level);
                        if (t < 0) continue;
                        var copy = (float[])quads[q].Clone();
                        string baseName = names[(int)copy[0]];
                        if (t > 0 && baseName.Contains("linen"))
                        {
                            string name = TornLinen(baseName, level, t - 1);
                            int at = names.IndexOf(name);
                            if (at < 0)
                            {
                                at = names.Count;
                                names.Add(name);
                                capi.BlockTextureAtlas.GetOrInsertTexture(AssetLocation.Create(name), out _, out var pos);
                                tex.Add(pos ?? capi.BlockTextureAtlas.UnknownTexturePosition);
                            }
                            copy[0] = at;
                        }
                        torn.Add(copy);
                    }
                    var tmd = SkinData(torn.ToArray(), tex.ToArray());
                    if (tmd != null) m.SailData[mesh = capi.Render.UploadMultiTextureMesh(tmd)] = tmd;
                }
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[hansa] Couldn't make the torn sail {0}; it's drawn whole: {1}", sail.Code, e.Message);
                mesh = null;
            }
            m.TornSails[(index, level)] = mesh;
            return mesh;
        }

        // ------------------------------------------------------------------ the water in her hold

        /// <summary>Strips across her the water's surface is drawn in (the middle ones bob with vanilla's liquid warp, the edges stay on her planking).</summary>
        const int FloodStrips = 8;
        /// <summary>Vanilla's liquid warp for a vertex (its wind mode 12, "LiquidWarp"): it bobs like water, with the "waving stuff" setting on.</summary>
        const int LiquidWarpFlags = 12 << 25;
        static readonly Vec4f FloodTint = new Vec4f(0.36f, 0.5f, 0.52f, 0.85f);

        /// <summary>
        /// (0.3.12, note 109) Glass is drawn with its alpha scaled by this (its colour as it is): vanilla's plain glass
        /// texture, as the game blends it, dimmed and greyed what was behind it, so the sea through the stern windows
        /// wasn't recognisable. A little bright, so the panes still catch the light.
        /// </summary>
        static readonly Vec4f GlassTint = new Vec4f(1.1f, 1.1f, 1.1f, 0.35f);

        /// <summary>
        /// The water in her hold (<see cref="EntityShip.FloodLevel"/>): a surface level in the world while she rolls and
        /// pitches, through her middle at her flood level (eased as it rises and falls), clipped to the inside of her hull at
        /// that height (<see cref="ShipWater"/>), sloshing a little toward the side she rolls to. Vanilla's salt water texture
        /// tinted murky and see-through, drifting slowly; its middle bobs with vanilla's liquid warp. Drawn blended after the
        /// glass, from both sides (you see it from under water too), in her interior's light. Anything going wrong is logged
        /// once and it isn't drawn any more this session.
        /// </summary>
        void DrawFlood(IRenderAPI rapi, IStandardShaderProgram prog, EntityShip ship, Meshes m, float dt)
        {
            floods.TryGetValue(ship.EntityId, out var f);
            if (floodFailed || !ship.Flooded || ship.GoneDown || ship.DamageParts == null || ship.Def.WaterClip == null)
            {
                if (f != null)
                {
                    f.Dispose();
                    floods.Remove(ship.EntityId);
                }
                return;
            }
            try
            {
                if (f == null) floods[ship.EntityId] = f = new FloodMesh { LagRoll = ship.Roll };
                double target = ship.FloodLevel;
                f.Level = double.IsNaN(f.Level) ? target : f.Level + (target - f.Level) * (1 - Math.Exp(-dt * 1.5));
                // sloshing: the water runs toward the side she rolls to, a little behind her, and back
                f.LagRoll += (ship.Roll - f.LagRoll) * (1 - Math.Exp(-dt * 1.2));
                double slosh = GameMath.Clamp((ship.Roll - f.LagRoll) * 0.5, -0.06, 0.06);
                ShipWater.Plane(ship.Pitch, ship.Roll, out double along, out double across);
                double inset = m.Hull != null ? m.Hull.Thickness / 2 : ShipWater.DefaultInset;
                var runs = ShipWater.Surface(ship.Def, f.Level, along, across + slosh, inset, FloodStrips);
                ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, ShipWater.FloodX(ship.Def), f.Level, 0, out _, out double wy, out _);
                f.WorldDy = wy;
                int quads = 0;
                foreach (var r in runs) quads += (r.Count - 1) * FloodStrips;
                if (quads == 0) return;
                if (waterTexture == 0) waterTexture = rapi.GetOrLoadTexture(new AssetLocation("game", "block/liquid/saltwater.png"));
                if (waterTexture == 0) return;

                if (f.Mesh == null || f.Capacity < quads)
                {
                    // the mesh's buffers are as big as what's first uploaded: room for more than now, updated every frame
                    f.Dispose();
                    f.Capacity = Math.Max(quads * 3 / 2, 256);
                    f.Data = new MeshData(f.Capacity * 4, f.Capacity * 6, false, true, true, true);
                    for (int q = 0; q < f.Capacity; q++)
                    {
                        for (int k = 0; k < 4; k++) f.Data.AddWithFlagsVertex(0, 0, 0, 0, 0, -1, 0);
                        f.Data.AddIndices(4 * q, 4 * q + 1, 4 * q + 2, 4 * q, 4 * q + 2, 4 * q + 3);
                    }
                    f.Mesh = rapi.UploadMesh(f.Data);
                }
                var md = f.Data.Clear();
                ShipFrame.ToLocal(0, ship.Pitch, ship.Roll, 0, 1, 0, out double nx, out double ny, out double nz);
                int flat = VertexFlags.PackNormal(nx, ny, nz), bob = flat | LiquidWarpFlags;
                double t = capi.ElapsedMilliseconds / 1000.0;
                float du = (float)(t * 0.021 % 16), dv = (float)(t * 0.013 % 16);
                void Vertex(double[] p, int k)
                {
                    float x = (float)p[3 * k], y = (float)p[3 * k + 1], z = (float)p[3 * k + 2];
                    // (the texture repeats: its own GL texture, not the atlas)
                    md.AddWithFlagsVertex(x, y, z, x * 0.5f + du, z * 0.5f + dv, -1, k == 0 || k == FloodStrips ? flat : bob);
                }
                foreach (var r in runs)
                    for (int s = 0; s + 1 < r.Count; s++)
                        for (int k = 0; k < FloodStrips; k++)
                        {
                            int b = md.VerticesCount;
                            Vertex(r[s], k);
                            Vertex(r[s + 1], k);
                            Vertex(r[s + 1], k + 1);
                            Vertex(r[s], k + 1);
                            md.AddIndices(b, b + 1, b + 2, b, b + 2, b + 3);
                        }
                rapi.UpdateMesh(f.Mesh, md);

                prog.ModelMatrix = model.Values;
                prog.RgbaTint = FloodTint;
                prog.RgbaLightIn = interiorLight;
                prog.Tex2D = waterTexture;
                rapi.GlDisableCullFace();
                rapi.RenderMesh(f.Mesh);
                prog.RgbaTint = ColorUtil.WhiteArgbVec;
            }
            catch (Exception e)
            {
                floodFailed = true;
                capi.Logger.Error("[hansa] Drawing the water in a ship's hold failed; it's off until the game is restarted: {0}", e);
            }
        }

        // ------------------------------------------------------------------ vanilla blocks aboard (per ship)

        Decor GetDecor(EntityShip ship)
        {
            if (!decor.TryGetValue(ship.EntityId, out var d)) decor[ship.EntityId] = d = new Decor();
            bool skin = meshes.TryGetValue(ship.Def.Code, out var skinMeshes) && skinMeshes.File != null;
            if (d.Version == ship.FitVersion && d.Surfaces == (ship.Def.Surfaces != null) && d.Skin == skin)
            {
                RefreshHearth(ship, d);
                RefreshOven(ship, d);
                return d;
            }
            // doors keep their swing across a rebuild (d.Open stays)
            d.Dispose();
            d.Version = ship.FitVersion;
            d.Surfaces = ship.Def.Surfaces != null;
            d.Skin = skin;
            // (0.3.18, note 165) and a hearth's or an oven's put aboard (their stand-in fixtures: the firepit, the oven)
            foreach (var f in System.Linq.Enumerable.Concat(ship.Def.Fixtures, ship.PlacedFixtures))
            {
                try
                {
                    var p = BuildPiece(ship, f);
                    if (p == null) continue;
                    d.Pieces.Add(p);
                }
                catch (Exception e)
                {
                    capi.Logger.Warning("[hansa] Could not draw {0} on the {1}: {2}", f.Code, ship.Def.Code, e.Message);
                }
            }
            // the pieces put aboard (PieceRenderer)
            AddPlacedPieces(ship, d);
            RemoveLights(ship);
            AddLights(ship);
            return d;
        }

        Piece BuildPiece(EntityShip ship, FixtureDef f)
        {
            switch (f.Type)
            {
                case "gate" when f.Block == null:     // a hinged section of the bulwark: the design's own moving part
                case "hatch":
                    return null;
                case "hearth" when f == ship.HearthFixture && ship.HearthInv != null:
                    // the firehearth's firepit as it burns, with what's cooking on it
                    return HearthPiece(ship, f);
                case "oven" when f == ship.OvenFixture && ship.OvenInv != null:
                    // the clay oven: vanilla's block, its fuel and what's baking in it as vanilla draws them
                    return OvenPiece(ship, f);
                case "prop":
                case "hearth":
                case "bed":
                {
                    // a vanilla block drawn for show (the captain's table), the firepit standing in the hearth, or a
                    // built-in bed
                    if (f.Block == null) return null;
                    var p = new Piece { Fixture = f, Interior = ship.Def.Indoors(f) };
                    var md = f.Type == "bed" ? BedMesh(BedMaterial(f.Block), f, p.Fit) : PropMesh(f, p.Fit);
                    if (md == null) return null;
                    p.Mesh = capi.Render.UploadMultiTextureMesh(md);
                    return p;
                }
                case "door":
                case "gate":
                case "window":
                {
                    var block = capi.World.GetBlock(new AssetLocation(f.Block));
                    var md = BlockMesh(block, null);
                    if (md == null || md.VerticesCount == 0)
                    {
                        // never an invisible door: plain planks if the vanilla block can't be drawn
                        if (f.Type == "window") return null;
                        capi.Logger.Warning("[hansa] No mesh for {0}, drawing plain planks", f.Block);
                        md = PlankSlab(1, f.Type == "door" ? 2 : 1, 0.15f);
                    }
                    var p = new Piece { Fixture = f, Glass = f.Type == "window" };
                    if (!FitToBox(md, f, p.Fit)) return null;
                    p.Mesh = capi.Render.UploadMultiTextureMesh(md);
                    if (f.BlockOpen != null && BlockMesh(capi.World.GetBlock(new AssetLocation(f.BlockOpen)), null) is MeshData open)
                    {
                        p.MeshOpen = capi.Render.UploadMultiTextureMesh(open);
                    }
                    return p;
                }
                case "attach":
                {
                    if (!ship.Attached.TryGetValue(f.Code, out var stack) || (stack.Block == null && f.Kind != "boat" && f.Kind != "plate")) return null;
                    if (f.Kind == "boat" && StowedBoatDef(stack) != null) return null;     // drawn from its own design, see DrawStowedBoats
                    var p = new Piece { Fixture = f, Interior = ship.Def.Indoors(f) };
                    MeshData md = f.Kind switch
                    {
                        "lantern" => LanternMesh(stack, f, p.Fit),
                        "chest" => ChestMesh(stack, f, p.Fit),
                        "cargo" => CargoMesh(ship, stack, f, p.Fit),
                        "bed" => BedMesh(stack.Block.Variant["material"] ?? "wood", f, p.Fit),
                        "boat" => BoatMesh(stack, f, p.Fit, meshes.TryGetValue(ship.Def.Code, out var bm) ? bm.File : null),
                        "plate" => PlateMesh(stack, f, p.Fit),
                        _ => null
                    };
                    if (md == null) return null;
                    p.Mesh = capi.Render.UploadMultiTextureMesh(md);
                    return p;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ the firehearth

        bool hearthWarned;

        /// <summary>
        /// What the hearth looks like now, as vanilla's firepit block entity draws itself (OnTesselation): the firepit's burn
        /// state (a cold one with no fuel drawn as ashes), its model for what's cooking ("normal", "spit", "wide") and what
        /// that is (the input, else the output). The piece is built again when this changes.
        /// </summary>
        static string HearthKey(EntityShip ship, out string burn, out string model, out ItemStack content, out bool isOutput)
        {
            burn = HearthFire.DrawnBurnState(ship.Fire.BurnState, ship.HearthFuelEmpty);
            model = HearthClient.HearthModel(ship);
            var inv = ship.HearthInv;
            content = inv?[1].Itemstack;
            isOutput = content == null && inv?[2].Itemstack != null;
            if (isOutput) content = inv[2].Itemstack;
            string recipe = content?.Attributes?.GetString("recipeCode");
            return $"{burn}-{model}|{content?.Collectible?.Code}|{isOutput}|{recipe}|{ship.Fire.IsBurning}";
        }

        /// <summary>The hearth's piece built again when what it shows has changed (it burns up, goes out, a pot goes on or comes off).</summary>
        void RefreshHearth(EntityShip ship, Decor d)
        {
            var f = ship.HearthFixture;
            if (f == null || ship.HearthInv == null) return;
            string key = HearthKey(ship, out _, out _, out _, out _);
            int at = d.Pieces.FindIndex(p => p.Fixture == f);
            if (at >= 0 && d.Pieces[at].Key == key) return;
            try
            {
                var fresh = HearthPiece(ship, f);
                if (at >= 0)
                {
                    d.Pieces[at].Dispose();
                    if (fresh != null) d.Pieces[at] = fresh;
                    else d.Pieces.RemoveAt(at);
                }
                else if (fresh != null) d.Pieces.Add(fresh);
            }
            catch (Exception e)
            {
                if (!hearthWarned) capi.Logger.Warning("[hansa] Could not draw the hearth on the {0}: {1}", ship.Def.Code, e.Message);
                hearthWarned = true;
            }
        }

        /// <summary>
        /// The hearth's firepit: vanilla's firepit shape for its burn state and what's cooking
        /// (game:block/wood/firepit/{burn}-{model}), turned to the fixture's yaw about its bottom centre on the firebed; and
        /// what's cooking on it as vanilla draws it (<see cref="HearthContent"/>).
        /// </summary>
        Piece HearthPiece(EntityShip ship, FixtureDef f)
        {
            string key = HearthKey(ship, out string burn, out string model, out var content, out bool isOutput);
            var p = new Piece { Fixture = f, Interior = ship.Def.Indoors(f), Key = key };
            var block = capi.World.GetBlock(new AssetLocation("game", "firepit-" + burn)) ?? capi.World.GetBlock(new AssetLocation(f.Block ?? "game:firepit-cold"));
            MeshData md = null;
            var shape = Shape.TryGet(capi, $"game:shapes/block/wood/firepit/{burn}-{model}.json");
            if (block != null && block.Id != 0 && shape != null) capi.Tesselator.TesselateShape(block, shape, out md);
            if (md == null || md.VerticesCount == 0) md = BlockMesh(block, null);
            if (md == null || md.VerticesCount == 0)
            {
                capi.Logger.Warning("[hansa] No mesh for the hearth's firepit ({0}-{1})", burn, model);
                return null;
            }
            var mat = new Matrixf().Identity()
                .Translate((float)f.Pos[0], (float)f.Pos[1], (float)f.Pos[2])
                .RotateY(f.Yaw * GameMath.DEG2RAD)
                .Translate(-0.5f, 0, -0.5f);
            Array.Copy(mat.Values, p.Fit, 16);
            p.Mesh = capi.Render.UploadMultiTextureMesh(md);
            try { HearthContent(ship, p, content, isOutput); }
            catch (Exception e)
            {
                if (!hearthWarned) capi.Logger.Warning("[hansa] Could not draw what's cooking in the hearth: {0}", e.Message);
                hearthWarned = true;
            }
            return p;
        }

        /// <summary>
        /// What's on the hearth's fire, as vanilla draws it on a firepit (in the firepit block's space, then the piece's fit):
        /// a pot in the input as PotInFirepitRenderer draws one (the open pot 1/16 up, its lid on it, rattling as it boils:
        /// <see cref="DrawPiece"/>), a cooked pot in the output with its meal in it; a joint on the spit as
        /// FirepitContentsRenderer draws it (by its inFirePitProps transform); a block with inFirePitProps its own mesh so
        /// transformed (a little lower while the fire's out).
        /// </summary>
        void HearthContent(EntityShip ship, Piece p, ItemStack content, bool isOutput)
        {
            if (content?.Collectible == null) return;
            var world = capi.World;
            if (content.Collectible is BlockCookingContainer || content.Collectible is BlockCookedContainer)
            {
                // the cooked pot's block gives the textures (vanilla's PotInFirepitRenderer)
                if (world.GetBlock(content.Collectible.CodeWithVariant("type", "cooked")) is not BlockCookedContainer cooked) return;
                if (isOutput)
                {
                    var cache = capi.ModLoader.GetModSystem<MealMeshCache>();
                    p.Content = cache?.GetOrCreateMealInContainerMeshRef(cooked, cooked.GetCookingRecipe(world, content), cooked.GetNonEmptyContents(world, content),
                        new Vec3f(0, 5f / 32f, 0));
                    p.ContentShared = true;
                }
                else
                {
                    if (Shape.TryGet(capi, "game:shapes/block/clay/pot-opened-empty.json") is Shape pot)
                    {
                        capi.Tesselator.TesselateShape(cooked, pot, out var md);
                        if (md?.VerticesCount > 0) p.Content = capi.Render.UploadMultiTextureMesh(md);
                    }
                    if (Shape.TryGet(capi, "game:shapes/block/clay/pot-part-lid.json") is Shape lid)
                    {
                        capi.Tesselator.TesselateShape(cooked, lid, out var md);
                        if (md?.VerticesCount > 0) p.Lid = capi.Render.UploadMultiTextureMesh(md);
                    }
                }
                Mat4f.Translate(p.ContentFit, p.Fit, 0.001f, 0.0625f, -0.001f);
                return;
            }
            if (content.Collectible is IInFirepitMeshSupplier || content.Collectible is IInFirepitRendererSupplier) return;
            var props = BlockEntityFirepit.GetRenderProps(content);
            if (props?.Transform == null) return;
            var t = props.Transform;
            if (content.Item != null)
            {
                capi.Tesselator.TesselateItem(content.Item, out var md);
                if (md == null || md.VerticesCount == 0) return;
                p.Content = capi.Render.UploadMultiTextureMesh(md);
                var m = new Matrixf().Set(p.Fit)
                    .Translate(t.Translation.X, t.Translation.Y, t.Translation.Z)
                    .Translate(t.Origin.X, 0.6f + t.Origin.Y, t.Origin.Z)
                    .RotateX(t.Rotation.X * GameMath.DEG2RAD)
                    .RotateY(t.Rotation.Y * GameMath.DEG2RAD)
                    .RotateZ(t.Rotation.Z * GameMath.DEG2RAD)
                    .Scale(t.ScaleXYZ.X, t.ScaleXYZ.Y, t.ScaleXYZ.Z)
                    .Translate(-t.Origin.X, -t.Origin.Y, -t.Origin.Z);
                Array.Copy(m.Values, p.ContentFit, 16);
            }
            else if (content.Block != null)
            {
                capi.Tesselator.TesselateBlock(content.Block, out var md);
                if (md == null || md.VerticesCount == 0) return;
                md.ModelTransform(t);
                if (!ship.Fire.IsBurning && props.UseFirepitModel != EnumFirepitModel.Spit) md.Translate(0, -0.0625f, 0);
                p.Content = capi.Render.UploadMultiTextureMesh(md);
                Array.Copy(p.Fit, p.ContentFit, 16);
            }
        }

        readonly float[] lidPlace = new float[16];

        /// <summary>A pot's lid on the hearth: 13/32 up, rattling as what's in it boils (vanilla's PotInFirepitRenderer: from 50 °C, fully at 100).</summary>
        float[] LidPlace(EntityShip ship, Piece p)
        {
            float ms = ship.World.ElapsedMilliseconds;
            float ox = GameMath.Sin(ms / 300f) * 5f / 16f, oz = GameMath.Cos(ms / 300f) * 5f / 16f;
            float boil = Math.Clamp((ship.HearthInputTemp() - 50f) / 50f, 0f, 1f);
            float rattle = boil * GameMath.Sin(ms / 50f) / 60f;
            var m = new Matrixf().Set(p.Fit).Translate(0, 13f / 32f, 0).Translate(-ox, 0, -oz).RotateX(rattle).RotateZ(rattle).Translate(ox, 0, oz);
            Array.Copy(m.Values, lidPlace, 16);
            return lidPlace;
        }

        // ------------------------------------------------------------------ the clay oven

        bool ovenWarned;

        /// <summary>What the oven shows: burning or not, what's in each slot and how risen it's drawn (the piece is built again when it changes).</summary>
        static string OvenKey(EntityShip ship)
        {
            var sb = new System.Text.StringBuilder(ship.Oven.Burning ? "b" : "o");
            var inv = ship.OvenInv;
            for (int i = 0; i < inv.Count; i++)
            {
                var s = inv[i].Itemstack;
                sb.Append('|').Append(s?.Collectible?.Code).Append('x').Append(s?.StackSize ?? 0);
                if (i < OvenFire.Capacity) sb.Append('@').Append(ship.Oven.Slots[i].CurHeightMul.ToString("0.00"));
            }
            return sb.ToString();
        }

        void RefreshOven(EntityShip ship, Decor d)
        {
            var f = ship.OvenFixture;
            if (f == null || ship.OvenInv == null) return;
            string key = OvenKey(ship);
            int at = d.Pieces.FindIndex(p => p.Fixture == f);
            if (at >= 0 && d.Pieces[at].Key == key) return;
            try
            {
                var fresh = OvenPiece(ship, f);
                if (at >= 0)
                {
                    d.Pieces[at].Dispose();
                    if (fresh != null) d.Pieces[at] = fresh;
                    else d.Pieces.RemoveAt(at);
                }
                else if (fresh != null) d.Pieces.Add(fresh);
            }
            catch (Exception e)
            {
                if (!ovenWarned) capi.Logger.Warning("[hansa] Could not draw the oven on the {0}: {1}", ship.Def.Code, e.Message);
                ovenWarned = true;
            }
        }

        /// <summary>
        /// The clay oven: vanilla's block (its variant faces the way it stands: not turned again) with its bottom centre at
        /// the fixture's place; in it, as vanilla's oven draws its contents (BlockEntityOven.genTransformationMatrices): the
        /// firewood as its stacked fuel shape, or what's baking in the four quadrants (or one large thing in the middle),
        /// risen or sunk as it bakes.
        /// </summary>
        Piece OvenPiece(EntityShip ship, FixtureDef f)
        {
            var block = capi.World.GetBlock(new AssetLocation(f.Block ?? "game:clayoven-south"));
            var md = BlockMesh(block, null);
            if (md == null || md.VerticesCount == 0)
            {
                if (!ovenWarned) capi.Logger.Warning("[hansa] No mesh for the oven ({0})", f.Block);
                ovenWarned = true;
                return null;
            }
            var p = new Piece { Fixture = f, Interior = ship.Def.Indoors(f), Key = OvenKey(ship) };
            var mat = new Matrixf().Identity().Translate((float)f.Pos[0], (float)f.Pos[1], (float)f.Pos[2]).Translate(-0.5f, 0, -0.5f);
            Array.Copy(mat.Values, p.Fit, 16);
            p.Mesh = capi.Render.UploadMultiTextureMesh(md);
            try { OvenContent(ship, block, p); }
            catch (Exception e)
            {
                if (!ovenWarned) capi.Logger.Warning("[hansa] Could not draw what's in the oven: {0}", e.Message);
                ovenWarned = true;
            }
            return p;
        }

        void OvenContent(EntityShip ship, Block oven, Piece p)
        {
            var inv = ship.OvenInv;
            int rot = (oven?.Variant?["side"]) switch { "south" => 270, "west" => 180, "east" => 0, _ => 90 };
            var list = new List<MultiTextureMeshRef>();
            if (ship.OvenFuel > 0)
            {
                // the firewood: the fuel's own oven shape (else the oven block's), as many logs as there are
                var fs = inv[0].Itemstack;
                string path = fs.Collectible.Attributes?["ovenFuelShape"].AsString(null) ?? oven?.Attributes?["ovenFuelShape"].AsString(null);
                if (path != null && fs.Item != null && oven != null)
                {
                    var loc = AssetLocation.Create(path, oven.Code.Domain).WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                    var shape = Shape.TryGet(capi, loc);
                    if (shape != null)
                    {
                        capi.Tesselator.TesselateShape("ovenFuelShape", shape, out var md, capi.Tesselator.GetTextureSource(fs.Item), null, 0, 0, 0, fs.StackSize);
                        if (md?.VerticesCount > 0)
                        {
                            md.MatrixTransform(new Matrixf().Translate(0.5f, 0, 0.5f).RotateYDeg(rot + 270).Scale(0.9f, 0.9f, 0.9f).Translate(-0.5f, 0, -0.5f).Values);
                            list.Add(capi.Render.UploadMultiTextureMesh(md));
                        }
                    }
                }
            }
            else
            {
                ItemStack first = null;
                for (int i = 0; i < inv.Count && first == null; i++) first = inv[i].Itemstack;
                bool large = first != null && BakingProperties.ReadFrom(first)?.LargeItem == true;
                var at = large ? new[] { new Vec3f(0, 0.0625f, 0) }
                    : new[] { new Vec3f(-0.125f, 0.0625f, -5f / 32f), new Vec3f(-0.125f, 0.0625f, 5f / 32f), new Vec3f(0.1875f, 0.0625f, -5f / 32f), new Vec3f(0.1875f, 0.0625f, 5f / 32f) };
                for (int i = 0; i < at.Length; i++)
                {
                    var st = inv[i].Itemstack;
                    if (st == null) continue;
                    var md = StackMesh(st);
                    if (md == null || md.VerticesCount == 0) continue;
                    float h = ship.Oven.Slots[i].CurHeightMul;
                    if (h <= 0) h = 1;
                    md.MatrixTransform(new Matrixf().Translate(at[i].X, at[i].Y, at[i].Z).Translate(0.5f, 0, 0.5f).RotateYDeg(rot)
                        .Scale(0.9f, h, 0.9f).Translate(-0.5f, 0, -0.5f).Values);
                    list.Add(capi.Render.UploadMultiTextureMesh(md));
                }
            }
            p.Extra = list;
        }

        /// <summary>A thing as vanilla's displays draw it (BlockEntityDisplay.getOrCreateMesh): its own mesh source, else its block's or item's mesh, with its display transform.</summary>
        MeshData StackMesh(ItemStack stack)
        {
            MeshData md = null;
            var cms = stack.Collectible.GetCollectibleInterface<IContainedMeshSource>();
            if (cms != null)
            {
                try { md = cms.GenMesh(new DummySlot(stack), capi.BlockTextureAtlas, null)?.Clone(); }
                catch (Exception) { md = null; }
            }
            if (md == null)
            {
                if (stack.Block != null) md = capi.TesselatorManager.GetDefaultBlockMesh(stack.Block)?.Clone();
                else if (stack.Item != null) capi.Tesselator.TesselateItem(stack.Item, out md);
            }
            if (md == null) return null;
            var tf = stack.Collectible.Attributes?["onDisplayTransform"].AsObject<ModelTransform>(null);
            if (tf != null)
            {
                tf.EnsureDefaultValues();
                md.ModelTransform(tf);
            }
            if (stack.Class == EnumItemClass.Item && (stack.Item.Shape == null || stack.Item.Shape.VoxelizeTexture))
            {
                md.Rotate(new Vec3f(0, 0, 0), GameMath.PIHALF, 0, 0);
                md.Scale(new Vec3f(0, 0, 0), 0.33f, 0.33f, 0.33f);
                md.Translate(0, -15f / 32f, 0);
            }
            return md;
        }

        /// <summary>A block's mesh in block space (0..1), from its own mesh source when it has one.</summary>
        MeshData BlockMesh(Block block, ItemStack stack)
        {
            if (block == null || block.Id == 0) return null;
            if (stack != null && block is IContainedMeshSource cms)
            {
                // (a copy: some give their cached mesh itself — vanilla's clutter, the scribe table with its cloth — which
                // turning and moving it into place would wreck for every later draw: note 100)
                MeshData md = null;
                try { md = cms.GenMesh(new DummySlot(stack), capi.BlockTextureAtlas, null)?.Clone(); }
                catch (Exception e) { capi.Logger.Warning("[hansa] {0} has no mesh for {1}: {2}", block.Code, stack.Attributes?.GetString("type"), e.Message); }
                if (md != null && md.VerticesCount > 0) return md;
            }
            return capi.TesselatorManager.GetDefaultBlockMesh(block)?.Clone();
        }

        /// <summary>A plain oak board w x h x t (block space), the stand-in for a block that has no mesh.</summary>
        MeshData PlankSlab(float w, float h, float t)
        {
            capi.BlockTextureAtlas.GetOrInsertTexture(new AssetLocation("game", "block/wood/planks/oak1"), out _, out var tp);
            tp ??= capi.BlockTextureAtlas.UnknownTexturePosition;
            var md = new MeshData(24, 36, false, true, true, true);
            float[][] faces =
            {
                new[] { 0, 0, t, w, 0, t, w, h, t, 0, h, t, 0, 0, 1f },        // +z
                new[] { w, 0, 0, 0, 0, 0, 0, h, 0, w, h, 0, 0, 0, -1f },       // -z
                new[] { w, 0, t, w, 0, 0, w, h, 0, w, h, t, 1f, 0, 0 },        // +x
                new[] { 0, 0, 0, 0, 0, t, 0, h, t, 0, h, 0, -1f, 0, 0 },       // -x
                new[] { 0, h, t, w, h, t, w, h, 0, 0, h, 0, 0, 1f, 0 },        // +y
                new[] { 0, 0, 0, w, 0, 0, w, 0, t, 0, 0, t, 0, -1f, 0 },       // -y
            };
            float[] us = { 0, 1, 1, 0 }, vs = { 1, 1, 0, 0 };
            foreach (var q in faces)
            {
                int b = md.VerticesCount;
                int flags = VertexFlags.PackNormal(q[12], q[13], q[14]);
                for (int k = 0; k < 4; k++)
                {
                    md.AddWithFlagsVertex(q[k * 3], q[k * 3 + 1], q[k * 3 + 2], tp.x1 + (tp.x2 - tp.x1) * us[k], tp.y1 + (tp.y2 - tp.y1) * vs[k], -1, flags);
                }
                md.AddIndices(b, b + 1, b + 2, b, b + 2, b + 3);
                md.AddTextureId(tp.atlasTextureId);
            }
            return md;
        }

        static void Bounds(MeshData md, out Vec3f min, out Vec3f max)
        {
            min = new Vec3f(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vec3f(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < md.VerticesCount; i++)
            {
                float x = md.xyz[i * 3], y = md.xyz[i * 3 + 1], z = md.xyz[i * 3 + 2];
                if (x < min.X) min.X = x;
                if (y < min.Y) min.Y = y;
                if (z < min.Z) min.Z = z;
                if (x > max.X) max.X = x;
                if (y > max.Y) max.Y = y;
                if (z > max.Z) max.Z = z;
            }
        }

        /// <summary>
        /// Place a door, gate or pane in its opening: turned so its thin side faces along the opening's normal,
        /// stretched to the opening's width and height, centred in the wall.
        /// </summary>
        static bool FitToBox(MeshData md, FixtureDef f, float[] m)
        {
            if (f.Box == null || md.VerticesCount == 0) return false;
            Bounds(md, out var min, out var max);
            float ex = max.X - min.X, ey = max.Y - min.Y, ez = max.Z - min.Z;
            if (ey < 1e-3) return false;
            bool meshThinX = ex < ez;
            bool wantThinX = f.Normal == "x";
            bool turn = meshThinX != wantThinX;
            float longSide = Math.Max(ex, ez);
            var b = f.Box;
            float width = (float)(wantThinX ? b[5] - b[2] : b[3] - b[0]);
            float height = (float)(b[4] - b[1]);
            float sl = longSide > 1e-3 ? width / longSide : 1, sh = height / ey;
            var mat = new Matrixf();
            mat.Identity()
               .Translate((float)(b[0] + b[3]) / 2, (float)b[1], (float)(b[2] + b[5]) / 2)
               .Scale(wantThinX ? 1 : sl, sh, wantThinX ? sl : 1);
            if (turn) mat.RotateY(GameMath.PIHALF);
            mat.Translate(-(min.X + max.X) / 2, -min.Y, -(min.Z + max.Z) / 2);
            Array.Copy(mat.Values, m, 16);
            return true;
        }

        MeshData LanternMesh(ItemStack stack, FixtureDef f, float[] m)
        {
            // hanging from a hook: the ceiling variant of the same lantern
            string size = stack.Block.Variant["size"] ?? "small";
            var block = capi.World.GetBlock(new AssetLocation("game", "lantern-" + size + "-" + (f.Hang == "up" ? "up" : "down"))) ?? stack.Block;
            var s = new ItemStack(block);
            s.Attributes = stack.Attributes.Clone();
            var md = BlockMesh(block, s);
            if (md == null || md.VerticesCount == 0) return null;
            Bounds(md, out var min, out var max);
            float y = f.Hang == "up" ? (float)f.Pos[1] - min.Y : (float)f.Pos[1] - max.Y;
            var mat = new Matrixf().Identity().Translate((float)f.Pos[0] - (min.X + max.X) / 2, y, (float)f.Pos[2] - (min.Z + max.Z) / 2);
            Array.Copy(mat.Values, m, 16);
            return md;
        }

        MeshData ChestMesh(ItemStack stack, FixtureDef f, float[] m)
        {
            MeshData md;
            if (stack.Block is BlockGenericTypedContainer gtc)
            {
                string type = ShipFittings.ChestType(stack);
                string shape = gtc.Attributes?["shape"][type].AsString();
                // unturned, the chest's front (the latch) faces +z
                md = gtc.GenMesh(capi, type, shape, null, new Vec3f(0, 0, 0));
            }
            else md = BlockMesh(stack.Block, stack);
            if (md == null || md.VerticesCount == 0) return null;
            // turn the front to face the fixture's yaw: +z turned by (yaw + 90) about Y ends up at (cos yaw, -sin yaw)
            var mat = new Matrixf().Identity()
                .Translate((float)f.Pos[0], (float)f.Pos[1], (float)f.Pos[2])
                .RotateY((f.Yaw + 90) * GameMath.DEG2RAD)
                .Translate(-0.5f, 0, -0.5f);
            Array.Copy(mat.Values, m, 16);
            return md;
        }

        /// <summary>
        /// Cargo in the hold: a chest, a storage vessel or a crate standing on its spot, a trunk across its pair of spots;
        /// the front facing the gangway along the keelson.
        /// </summary>
        MeshData CargoMesh(EntityShip ship, ItemStack stack, FixtureDef f, float[] m)
        {
            MeshData md;
            if (stack.Block is BlockGenericTypedContainer gtc)
            {
                string type = ShipFittings.ChestType(stack);
                string shape = gtc.Attributes?["shape"][type].AsString();
                md = shape != null ? gtc.GenMesh(capi, type, shape, null, new Vec3f(0, 0, 0)) : BlockMesh(stack.Block, stack);
            }
            else md = BlockMesh(stack.Block, stack);
            if (md == null || md.VerticesCount == 0) return null;
            Bounds(md, out var min, out var max);
            double cx = f.Pos[0], cz = f.Pos[2];
            if (ShipFittings.IsTrunk(stack) && f.Pair != null && ship.Def.Fixture(f.Pair) is FixtureDef pair)
            {
                cx = (f.Pos[0] + pair.Pos[0]) / 2;
                cz = (f.Pos[2] + pair.Pos[2]) / 2;
            }
            var mat = new Matrixf().Identity()
                .Translate((float)cx, (float)f.Pos[1], (float)cz)
                .RotateY((f.Yaw + 90) * GameMath.DEG2RAD)
                .Translate(-(min.X + max.X) / 2, -min.Y, -(min.Z + max.Z) / 2);
            Array.Copy(mat.Values, m, 16);
            return md;
        }

        // ------------------------------------------------------------------ her name on the transom

        readonly Dictionary<long, (string Name, LoadedTexture Tex)> nameTextures = new();
        MeshRef nameQuad;
        readonly float[] nameMat = new float[16];

        /// <summary>
        /// Her name on the name board (the design's nameboard fixture) as the board's own picture: its ground (the board as
        /// built, or a colour), the letters in their colour (gilt, or a dye) with a dark edge, as big as chosen (of the board's
        /// height, never wider than it), upright or slanted (italic) (the user's note 68: <see cref="NameStyle"/>).
        /// </summary>
        void DrawName(IRenderAPI rapi, IStandardShaderProgram prog, EntityShip ship)
        {
            string name = ship.ShipName;
            if (string.IsNullOrEmpty(name)) return;
            FixtureDef f = null;
            foreach (var x in ship.Def.Fixtures) if (x.Type == "nameboard") { f = x; break; }
            if (f?.Right == null || f.Up == null || f.Out == null || f.Width <= 0 || f.Height <= 0) return;
            var st = ship.NameStyle ?? new NameStyle();
            string key = string.Join("|", name, st.Text, st.Back, st.Size, st.Italic);
            if (!nameTextures.TryGetValue(ship.EntityId, out var nt) || nt.Name != key || nt.Tex == null)
            {
                nt.Tex?.Dispose();
                nt = (key, NameTexture(name, st, f.Width / f.Height));
                nameTextures[ship.EntityId] = nt;
            }
            if (nt.Tex == null || nt.Tex.Width <= 0 || nt.Tex.Height <= 0) return;
            nameQuad ??= rapi.UploadMesh(UnitQuad());
            // the picture is the board's shape: all of it
            float w = f.Width, h = f.Height;
            double[] r = f.Right, u = f.Up, n = f.Out, c = f.Pos;
            Mat4f.Identity(nameMat);
            for (int i = 0; i < 3; i++)
            {
                nameMat[i] = (float)r[i] * w;
                nameMat[4 + i] = (float)u[i] * h;
                nameMat[8 + i] = (float)n[i];
                nameMat[12 + i] = (float)(c[i] + n[i] * 0.004 - r[i] * w / 2 - u[i] * h / 2);
            }
            Mat4f.Mul(tmp, model.Values, nameMat);
            prog.ModelMatrix = tmp;
            prog.Tex2D = nt.Tex.TextureId;
            prog.AlphaTest = 0.2f;
            rapi.RenderMesh(nameQuad);
            prog.AlphaTest = 0f;
            prog.ModelMatrix = model.Values;
        }

        /// <summary>The name board's picture (aspect: the board's width over its height): its ground and her name on it, centred.</summary>
        LoadedTexture NameTexture(string name, NameStyle st, double aspect)
        {
            const int H = 128;
            int W = (int)Math.Clamp(Math.Round(H * aspect), 32, 2048);
            using var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, W, H);
            using (var ctx = new Cairo.Context(surface))
            {
                if (st.Back != null)
                {
                    var b = NameStyle.Rgb(st.Back);
                    ctx.SetSourceRGBA(b[0], b[1], b[2], 1);
                    ctx.Paint();
                }
                ctx.SelectFontFace(GuiStyle.DecorativeFontName, Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
                ctx.SetFontSize(100);
                // the letters' height from a capital's top to a descender's foot ("Hg"), whatever letters the name has
                var span = ctx.TextExtents("Hg");
                var te = ctx.TextExtents(name);
                const double slant = 0.22;
                double ink = Math.Max(1, span.Height), wide = Math.Max(1, te.Width + (st.Italic ? slant * ink : 0));
                double k = Math.Min(H * 0.88 * NameStyle.Sizes[Math.Clamp(st.Size, 0, NameStyle.Sizes.Length - 1)] / ink, W * 0.94 / wide);
                ctx.SetFontSize(100 * k);
                span = ctx.TextExtents("Hg");
                te = ctx.TextExtents(name);
                double x = (W - te.Width) / 2 - te.XBearing, y = (H - span.Height) / 2 - span.YBearing;
                if (st.Italic)
                {
                    // slanted about the baseline (the font may have no italic of its own)
                    x -= slant * -span.YBearing / 2;
                    ctx.Translate(x, y);
                    ctx.Transform(new Cairo.Matrix(1, 0, -slant, 1, 0, 0));
                    ctx.MoveTo(0, 0);
                }
                else ctx.MoveTo(x, y);
                ctx.TextPath(name);
                var fill = NameStyle.Rgb(st.Text);
                ctx.SetSourceRGBA(fill[0], fill[1], fill[2], 1);
                ctx.FillPreserve();
                var edge = st.StrokeRgb();
                ctx.SetSourceRGBA(edge[0], edge[1], edge[2], 1);
                ctx.LineWidth = Math.Max(1.2, 100 * k * 0.035);
                ctx.Stroke();
            }
            var tex = new LoadedTexture(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref tex);
            return tex;
        }

        /// <summary>A quad from (0,0) to (1,1) in its x-y plane facing +z, the whole texture on it (text reads along +x).</summary>
        static MeshData UnitQuad()
        {
            var md = new MeshData(4, 6, false, true, true, true);
            int flags = VertexFlags.PackNormal(0, 0, 1);
            md.AddWithFlagsVertex(0, 0, 0, 0, 1, -1, flags);
            md.AddWithFlagsVertex(1, 0, 0, 1, 1, -1, flags);
            md.AddWithFlagsVertex(1, 1, 0, 1, 0, -1, flags);
            md.AddWithFlagsVertex(0, 1, 0, 0, 0, -1, flags);
            md.AddIndices(0, 1, 2, 0, 2, 3);
            return md;
        }

        /// <summary>"game:bed-woodaged" → "woodaged".</summary>
        static string BedMaterial(string code)
        {
            string path = new AssetLocation(code).Path;
            int dash = path.IndexOf('-');
            return dash > 0 ? path.Substring(dash + 1).Split('-')[0] : "wood";
        }

        /// <summary>A vanilla block standing at the fixture, turned to its yaw about the block's centre.</summary>
        MeshData PropMesh(FixtureDef f, float[] m)
        {
            var md = BlockMesh(capi.World.GetBlock(new AssetLocation(f.Block)), null);
            if (md == null || md.VerticesCount == 0)
            {
                capi.Logger.Warning("[hansa] No mesh for {0} ({1})", f.Block, f.Code);
                return null;
            }
            float s = f.Scale > 0 ? f.Scale : 1;
            var mat = new Matrixf().Identity()
                .Translate((float)f.Pos[0], (float)f.Pos[1], (float)f.Pos[2])
                .RotateY(f.Yaw * GameMath.DEG2RAD)
                .Scale(s, s, s)
                .Translate(-0.5f, 0, -0.5f);
            Array.Copy(mat.Values, m, 16);
            return md;
        }

        /// <summary>
        /// The ship's boat (a vanilla raft) on its chocks: the item's own mesh laid lengthways along the fixture, its
        /// bottom at the fixture's height, made smaller only if it wouldn't fit the space.
        /// </summary>
        MeshData BoatMesh(ItemStack stack, FixtureDef f, float[] m, ShipSkinFile skin = null)
        {
            if (stack.Item == null) return null;
            capi.Tesselator.TesselateItem(stack.Item, out MeshData md);
            if (md == null || md.VerticesCount == 0) return null;
            Bounds(md, out var min, out var max);
            float ex = max.X - min.X, ez = max.Z - min.Z;
            bool turn = ez > ex;
            float len = Math.Max(ex, ez), wid = Math.Max(1e-3f, Math.Min(ex, ez));
            float s = 1;
            if (f.Box != null) s = Math.Min(1, Math.Min((float)(f.Box[3] - f.Box[0]) / len, (float)(f.Box[5] - f.Box[2]) / wid));
            var mat = new Matrixf().Identity()
                .Translate((float)f.Pos[0], (float)f.Pos[1], (float)f.Pos[2])
                .RotateY(f.Yaw * GameMath.DEG2RAD + (turn ? GameMath.PIHALF : 0))
                .Scale(s, s, s)
                .Translate(-(min.X + max.X) / 2, -min.Y, -(min.Z + max.Z) / 2);
            // (0.3.24, note 197: "the rowing boat hangs in the air") on the gallows' beams: her keel is lowest amidships and
            // the beams are near her ends, where her bottom has risen; lowered till her bottom over the higher beam is on it
            // (her keel then hangs between them a little, as in a cradle)
            var rests = ShipFittings.BoatRests(skin, f);
            if (rests.Count > 0 && f.Box?.Length >= 6)
            {
                bool alongX = f.Box[3] - f.Box[0] >= f.Box[5] - f.Box[2];
                double restBottom = double.MinValue;
                var v = new float[4];
                foreach (double r in rests)
                {
                    double low = double.MaxValue;
                    for (int i = 0; i < md.VerticesCount; i++)
                    {
                        v[0] = md.xyz[i * 3]; v[1] = md.xyz[i * 3 + 1]; v[2] = md.xyz[i * 3 + 2]; v[3] = 1;
                        var w = Mat4f.MulWithVec4(mat.Values, v);
                        if (Math.Abs((alongX ? w[0] : w[2]) - r) <= 0.2) low = Math.Min(low, w[1]);
                    }
                    if (low < double.MaxValue) restBottom = Math.Max(restBottom, low);
                }
                double drop = restBottom - (f.Pos[1] - 0.02);
                if (restBottom > double.MinValue && drop > 0.01)
                    mat = new Matrixf().Identity().Translate(0, (float)-Math.Min(drop, 1.5), 0).Mul(mat);
            }
            Array.Copy(mat.Values, m, 16);
            return md;
        }

        /// <summary>
        /// A vanilla metal plate on the wall by the helm (the ship's spawn point): the item's own mesh (a plate lying flat)
        /// stood up against the wall, its face turned the way the fixture faces, its back on the wall at Pos.
        /// </summary>
        MeshData PlateMesh(ItemStack stack, FixtureDef f, float[] m)
        {
            if (stack.Item == null) return null;
            capi.Tesselator.TesselateItem(stack.Item, out MeshData md);
            if (md == null || md.VerticesCount == 0) return null;
            Bounds(md, out var min, out var max);
            // RotateY(yaw) turns +x to the facing (cos yaw, -sin yaw); RotateZ(-90°) first stands the plate's top (+y) up as +x
            var mat = new Matrixf().Identity()
                .Translate((float)f.Pos[0], (float)f.Pos[1], (float)f.Pos[2])
                .RotateY(f.Yaw * GameMath.DEG2RAD)
                .Translate(0.005f, 0, 0)
                .RotateZ(-GameMath.PIHALF)
                .Translate(-(min.X + max.X) / 2, -min.Y, -(min.Z + max.Z) / 2);
            Array.Copy(mat.Values, m, 16);
            return md;
        }

        MeshData BedMesh(string material, FixtureDef f, float[] m)
        {
            var (hx, hz) = f.Facing;                         // where the head points
            // vanilla beds: the feet are one block from the head toward "side"
            string side = Math.Abs(hx) > 0.5 ? (hx > 0 ? "west" : "east") : (hz > 0 ? "north" : "south");
            var head = capi.World.GetBlock(new AssetLocation("game", $"bed-{material}-head-{side}"));
            var feet = capi.World.GetBlock(new AssetLocation("game", $"bed-{material}-feet-{side}"));
            var mh = BlockMesh(head, null);
            var mf = BlockMesh(feet, null);
            if (mh == null || mf == null) return null;
            double cx = f.Pos[0], cz = f.Pos[2];
            mh.Translate((float)(cx + hx * 0.5 - 0.5), 0, (float)(cz + hz * 0.5 - 0.5));
            mf.Translate((float)(cx - hx * 0.5 - 0.5), 0, (float)(cz - hz * 0.5 - 0.5));
            mh.AddMeshData(mf);
            var mat = new Matrixf().Identity().Translate(0, (float)f.Pos[1], 0);
            Array.Copy(mat.Values, m, 16);
            return mh;
        }

        // ------------------------------------------------------------------ lights

        public void AddLights(EntityShip ship)
        {
            if (ship.Def != null) Get(ship.Def);   // start loading the meshes as soon as a ship shows up
            if (ship.Def == null || lights.ContainsKey(ship.EntityId)) return;
            var list = new List<ShipLight>();
            foreach (var l in ship.Def.Lights)
            {
                var rgb = l.Rgb ?? new[] { 1f, 0.8f, 0.5f };
                list.Add(new ShipLight { Local = l.Pos, On = new Vec3f(rgb[2] * l.Strength, rgb[1] * l.Strength, rgb[0] * l.Strength) });
            }
            foreach (var f in ship.Def.Fixtures)
            {
                if (f.Type == "stove")
                {
                    float s = Math.Min(9, 3 + f.Heat * 0.5f);
                    list.Add(new ShipLight { Local = new[] { f.Pos[0], f.Pos[1] + 0.6, f.Pos[2] }, On = new Vec3f(0.25f * s, 0.55f * s, 1f * s), Stove = f });
                }
                else if (f == ship.HearthFixture && f.Fire != null)
                {
                    // the fire: the lit firepit's own light (vanilla [7, 7, 16]) on the lanterns' scale, a little over the fire
                    var lit = capi.World.GetBlock(new AssetLocation("game", "firepit-lit"));
                    byte[] hsv = lit?.LightHsv ?? new byte[] { 7, 7, 16 };
                    int rgb = ColorUtil.HsvToRgb(hsv[0] * 4, hsv[1] * 32, 255);
                    float r = ((rgb >> 16) & 255) / 255f, g = ((rgb >> 8) & 255) / 255f, b = (rgb & 255) / 255f;
                    float strength = hsv[2] * 0.55f;
                    list.Add(new ShipLight { Local = new[] { f.Fire[0], f.Fire[1] + 0.3, f.Fire[2] }, On = new Vec3f(b * strength, g * strength, r * strength), Hearth = true });
                }
                else if (f.Type == "attach" && f.Kind == "lantern" && ship.Attached.TryGetValue(f.Code, out var stack) && stack.Block != null)
                {
                    // the lantern's own colour and brightness (glass and lining change them)
                    byte[] hsv = stack.Block.GetLightHsv(capi.World.BlockAccessor, null, stack) ?? new byte[] { 7, 7, 14 };
                    int rgb = ColorUtil.HsvToRgb(hsv[0] * 4, hsv[1] * 32, 255);
                    float r = ((rgb >> 16) & 255) / 255f, g = ((rgb >> 8) & 255) / 255f, b = (rgb & 255) / 255f;
                    float strength = Math.Max(4, hsv[2] * 0.55f);
                    double y = f.Hang == "up" ? f.Pos[1] + 0.35 : f.Pos[1] - 0.45;
                    list.Add(new ShipLight { Local = new[] { f.Pos[0], y, f.Pos[2] }, On = new Vec3f(b * strength, g * strength, r * strength) });
                }
            }
            // (0.3.18) a hearth put aboard: its fire's light, as the design's
            if (ship.PlacedHearth != null && ship.PlacedHearth == ship.HearthFixture && ship.PlacedHearth.Fire is double[] pf)
            {
                var lit = capi.World.GetBlock(new AssetLocation("game", "firepit-lit"));
                byte[] hsv = lit?.LightHsv ?? new byte[] { 7, 7, 16 };
                int rgb = ColorUtil.HsvToRgb(hsv[0] * 4, hsv[1] * 32, 255);
                float r = ((rgb >> 16) & 255) / 255f, g = ((rgb >> 8) & 255) / 255f, b = (rgb & 255) / 255f;
                float strength = hsv[2] * 0.55f;
                list.Add(new ShipLight { Local = new[] { pf[0], pf[1] + 0.3, pf[2] }, On = new Vec3f(b * strength, g * strength, r * strength), Hearth = true });
            }
            // the light pieces put aboard (PieceRenderer)
            AddPieceLights(ship, list);
            // (registered with the game by UpdateLights: the nearest ones that are lit, as many as the game draws)
            lights[ship.EntityId] = list;
        }

        public void RemoveLights(EntityShip ship)
        {
            if (!lights.TryGetValue(ship.EntityId, out var list)) return;
            foreach (var pl in list)
                if (registeredLights.Remove(pl)) capi.Render.RemovePointLight(pl);
            lights.Remove(ship.EntityId);
        }

        /// <summary>The ships' lights the game has now (<see cref="ChooseLights"/>).</summary>
        readonly HashSet<ShipLight> registeredLights = new();
        readonly List<ShipLight> lightCandidates = new();
        long entityLightsCheckedMs = long.MinValue;
        int entityLights;

        /// <summary>
        /// The game draws only its first "max dynamic lights" (graphics settings, 10 by default) point lights, in the order
        /// they were added and after the lit entities near the player (a held torch), not the nearest: with every lantern,
        /// stove and hook of a ship added at once, the pieces' lanterns (added last) were never drawn (note 81). So only
        /// the lit ones nearest the player (a deck between counting double) are given to the game, as many as it has room
        /// for after the lit entities, chosen again as the player moves.
        /// </summary>
        void ChooseLights(Vec3d eye)
        {
            int max = 10;
            try { max = capi.Settings.Int["maxDynamicLights"]; } catch (Exception) { }
            long now = capi.ElapsedMilliseconds;
            if (now - entityLightsCheckedMs > 500)
            {
                entityLightsCheckedMs = now;
                entityLights = capi.World.GetEntitiesAround(eye, 120f, 120f, e => e.LightHsv != null && e.LightHsv[2] > 0).Length;
            }
            int room = Math.Max(0, max - entityLights);
            lightCandidates.Clear();
            foreach (var list in lights.Values)
                foreach (var pl in list)
                    if (pl.Color.X + pl.Color.Y + pl.Color.Z > 0.01f) lightCandidates.Add(pl);
            if (lightCandidates.Count > room)
            {
                double D(ShipLight l)
                {
                    double dx = l.Pos.X - eye.X, dy = (l.Pos.Y - eye.Y) * 2, dz = l.Pos.Z - eye.Z;
                    return dx * dx + dy * dy + dz * dz;
                }
                lightCandidates.Sort((a, b) => D(a).CompareTo(D(b)));
                lightCandidates.RemoveRange(room, lightCandidates.Count - room);
            }
            var keep = new HashSet<ShipLight>(lightCandidates);
            registeredLights.RemoveWhere(pl =>
            {
                if (keep.Contains(pl)) return false;
                capi.Render.RemovePointLight(pl);
                return true;
            });
            foreach (var pl in lightCandidates)
                if (registeredLights.Add(pl)) capi.Render.AddPointLight(pl);
        }

        public void RemoveShip(EntityShip ship)
        {
            RemoveLights(ship);
            if (nameTextures.TryGetValue(ship.EntityId, out var nt))
            {
                nt.Tex?.Dispose();
                nameTextures.Remove(ship.EntityId);
            }
            if (decor.TryGetValue(ship.EntityId, out var d))
            {
                d.Dispose();
                decor.Remove(ship.EntityId);
            }
            if (damageLooks.TryGetValue(ship.EntityId, out var dl))
            {
                dl.Dispose();
                damageLooks.Remove(ship.EntityId);
            }
            if (floods.TryGetValue(ship.EntityId, out var fl))
            {
                fl.Dispose();
                floods.Remove(ship.EntityId);
            }
            foreach (var key in System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(pressed.Keys, k => k.Ship == ship.EntityId)))
            {
                pressed[key].Mesh?.Dispose();
                pressed.Remove(key);
            }
            DisposePaint(ship.EntityId);
            DisposeSlots(ship.EntityId);
            DisposeScars(ship.EntityId);
            DisposeBrows(ship.EntityId);
        }

        /// <summary>World position the ship's origin is drawn at this frame.</summary>
        public Vec3d RenderOrigin(EntityShip ship, EntityPlayer ep)
        {
            if (ep.MountedOn is ShipSeat seat && seat.Ship == ship)
            {
                var sp = seat.SeatPosition;
                return new Vec3d(ep.CameraPos.X + ship.Pos.X - sp.X, ep.CameraPos.Y + ship.Pos.Y - sp.Y, ep.CameraPos.Z + ship.Pos.Z - sp.Z);
            }
            return new Vec3d(ship.Pos.X, ship.Pos.Y, ship.Pos.Z);
        }

        public void UpdateLights(EntityPlayer ep)
        {
            foreach (var ship in sys.Ships)
            {
                if (!lights.TryGetValue(ship.EntityId, out var list)) continue;
                var o = RenderOrigin(ship, ep);
                foreach (var pl in list)
                {
                    ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, pl.Local[0], pl.Local[1], pl.Local[2], out double x, out double y, out double z);
                    pl.Pos.Set(o.X + x, o.Y + y, o.Z + z);
                    if (pl.Hearth)
                    {
                        float k = HearthGlow(ship);
                        pl.Color.Set(pl.On.X * k, pl.On.Y * k, pl.On.Z * k);
                        continue;
                    }
                    bool on = pl.Stove == null || ship.LitStoves.Contains(pl.Stove.Code);
                    if (on) pl.Color.Set(pl.On); else pl.Color.Set(0, 0, 0);
                }
            }
            ChooseLights(ep.CameraPos ?? ep.Pos.XYZ);
        }

        /// <summary>
        /// How bright the hearth's fire is now (of the lit firepit's light): flickering between about 0.75 and 1 while it
        /// burns, the extinct firepit's glow (vanilla [7, 7, 3]: 3/16) while its embers smoulder, else dark.
        /// </summary>
        public static float HearthGlow(EntityShip ship)
        {
            if (!ship.HearthOn || ship.HearthInv == null) return 0;
            if (ship.Fire.IsBurning)
            {
                double t = ship.World.ElapsedMilliseconds / 1000.0 + (ship.EntityId % 97) * 0.37;
                return (float)(0.875 + 0.06 * Math.Sin(t * 7.3) + 0.045 * Math.Sin(t * 13.1 + 1.7) + 0.025 * Math.Sin(t * 29.7 + 0.4));
            }
            return ship.Fire.BurnState == "extinct" && ship.Fire.CanIgniteFuel ? 3f / 16f : 0;
        }

        // ------------------------------------------------------------------ draw

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (stage == EnumRenderStage.ShadowFar || stage == EnumRenderStage.ShadowNear)
            {
                RenderShadows(stage == EnumRenderStage.ShadowFar);
                return;
            }
            var ep = capi.World.Player?.Entity;
            if (ep == null) return;
            if (sys.Ships.Count == 0) return;
            var rapi = capi.Render;
            var cam = ep.CameraPos;
            double maxDist = MaxDrawDistance();
            foreach (var ship in sys.Ships) if (ship.Def != null) GetDecor(ship);
            UpdateLights(ep);
            // the hearths' flames and smoke, their crackle, where they're drawn now
            sys.Hearths?.Update(ep, this, dt);
            // the pumps' water gushing onto the deck while they bring it up
            sys.Pumps?.Update(ep, this, dt);

            rapi.GlToggleBlend(false);
            rapi.GlEnableCullFace();
            foreach (var ship in sys.Ships)
            {
                if (ship.Def == null) continue;
                var o = RenderOrigin(ship, ep);
                double rx = o.X - cam.X, ry = o.Y - cam.Y, rz = o.Z - cam.Z;
                if (rx * rx + rz * rz > maxDist * maxDist) continue;
                var m = Get(ship.Def);
                if (m.SkinLoad != null) UploadSkin(m);
                UpdateDamage(ship, m);
                UpdatePaint(ship, m);
                // (0.3.20, step B) her deck cells as her slots have them
                UpdateSlots(ship, m);
                // ((0.3.27, note 207) where balls struck her)
                UpdateScars(ship, m);
                // ((0.3.27, note 219) her brows run out to a quay)
                UpdateBrows(ship);

                int lx = (int)Math.Floor(ship.Pos.X), ly = (int)Math.Floor(ship.Pos.Y + ship.Def.DeckY + 1.5), lz = (int)Math.Floor(ship.Pos.Z);
                var prog = rapi.PreparedStandardShader(lx, ly, lz);
                var ext = capi.World.BlockAccessor.GetLightRGBs(lx, ly, lz);
                // below decks: a quarter of the daylight, plus the warm glow of the lanterns
                interiorLight.Set(Math.Max(ext.R, 0.55f), Math.Max(ext.G, 0.45f), Math.Max(ext.B, 0.32f), ext.A * 0.28f);
                prog.ViewMatrix = rapi.CameraMatrixOriginf;
                prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
                SetModel(ship, rx, ry, rz);
                prog.ModelMatrix = model.Values;

                // (with painted zones in them: drawn by zones, PaintRenderer)
                if (!DrawPaintedShape(rapi, ship, "exterior") && m.Exterior != null) rapi.RenderMultiTextureMesh(m.Exterior, "tex");
                if (m.Interior != null)
                {
                    prog.RgbaLightIn = interiorLight;
                    if (!DrawPaintedShape(rapi, ship, "interior")) rapi.RenderMultiTextureMesh(m.Interior, "tex");
                    prog.RgbaLightIn = ext;
                }

                // skin quads are single sheets (hull planking seen from one side, rope strands from all sides); the hull's
                // planking with her damage on it
                rapi.GlDisableCullFace();
                DrawHull(rapi, ship, m, "exterior", false);
                // ((0.3.26, step D1) her hull panels as their states have them, with her damage on them)
                DrawSlots(rapi, ship, "slot:hull", false);
                DrawHull(rapi, ship, m, ship.Clad ? "bottomclad" : "bottom", false);
                DrawSlots(rapi, ship, ship.Clad ? "slot:hull:bottomclad" : "slot:hull:bottom", false);
                DrawSlots(rapi, ship, "slot:deck", false);
                prog.RgbaLightIn = interiorLight;
                DrawHull(rapi, ship, m, "interior", false);
                DrawSlots(rapi, ship, "slot:deck:interior", false);
                DrawSlots(rapi, ship, "slot:hull:interior", false);
                prog.RgbaLightIn = ext;
                DrawScars(rapi, prog, ship, ext);
                DrawBrows(rapi, ship);

                // moving parts: the wheel, rudder and tiller with the helm, the gates and hatches as they open, the anchor
                // going down, and the ropes that follow them
                decor.TryGetValue(ship.EntityId, out var d);
                if (d != null) UpdateOpenings(ship, d, dt);
                ComputeParts(ship, d);
                foreach (var part in ship.Def.MovingParts)
                {
                    var mesh = SkinGroup(m, "part:" + part.Code);
                    if (mesh == null || !PartShown(ship, part)) continue;
                    Mat4f.Mul(tmp, model.Values, partMats[part.Code]);
                    prog.ModelMatrix = tmp;
                    prog.RgbaLightIn = part.Interior ? interiorLight : ext;
                    if (!DrawPainted(rapi, ship, m, "part:" + part.Code)) rapi.RenderMultiTextureMesh(mesh, "tex");
                }
                prog.RgbaLightIn = ext;

                // vanilla blocks aboard
                if (d != null)
                {
                    foreach (var p in d.Pieces) if (!p.Glass) DrawPiece(rapi, prog, ship, d, p, ext);
                    // (0.3.12) a vanilla pane's frame, its solid texels, drawn here with the solid things (its glass, made
                    // clearer, in the glass pass below: scaling the whole mesh's alpha there would make the frame see-through)
                    prog.AlphaTest = 0.95f;
                    foreach (var p in d.Pieces) if (p.Glass) DrawPiece(rapi, prog, ship, d, p, ext);
                    prog.AlphaTest = 0f;
                }
                DrawStowedBoats(rapi, prog, ship);

                // the figurehead she carries, and her name on the transom
                prog.ModelMatrix = model.Values;
                foreach (var f in ship.Def.Fixtures)
                {
                    if (f.Type == "figurehead" && ship.FigureheadGroup(f) is string g && g.Length > 0 && !DrawPainted(rapi, ship, m, g)) Draw(rapi, SkinGroup(m, g));
                }
                DrawName(rapi, prog, ship);
                prog.Tex2D = 0;

                float frac = PartMotion.DrawnSail(ship.SailFraction);
                for (int si = 0; si < m.Sails.Count; si++)
                {
                    var (sail, shaped) = m.Sails[si];
                    var mesh = SailMesh(ship, m, si, sail, shaped, out bool torn);
                    if (mesh == null) continue;
                    mesh = PressedSail(ship, m, si, sail, mesh, frac, out var sailAt);
                    Mat4f.Mul(tmp, model.Values, sailAt);
                    prog.ModelMatrix = tmp;
                    // a torn sail's holes are cut out of its texture: drawn alpha-tested
                    if (torn) prog.AlphaTest = 0.25f;
                    rapi.RenderMultiTextureMesh(mesh, "tex");
                    if (torn) prog.AlphaTest = 0f;
                }

                // glass last, blended over what's behind it: the vanilla panes, the stern gallery's lights, the glazed hatch.
                // (0.3.12, note 109: through the stern windows the sea wasn't recognisable, only the horizon.) The game
                // draws its water after this stage (the transparent pass, against the depth drawn so far), so glass that
                // wrote its depth hid the sea behind it: only the seabed and the sky showed, dimmed. Now glass leaves the
                // depth alone, so the sea, the ships and the creatures drawn after it show through; and it's much
                // clearer, whatever texture it has: its alpha is scaled down (GlassTint) and it's lit by the light
                // outside, not the cabin's dim light (a dark pane over a bright sea dims it).
                rapi.GlToggleBlend(true);
                rapi.GLDepthMask(false);
                prog.RgbaTint = GlassTint;
                if (d != null)
                {
                    foreach (var p in d.Pieces) if (p.Glass) DrawPiece(rapi, prog, ship, d, p, ext, true);
                }
                prog.RgbaLightIn = ext;
                prog.ModelMatrix = model.Values;
                Draw(rapi, SkinGroup(m, "glass"));
                foreach (var part in ship.Def.MovingParts)
                {
                    var mesh = SkinGroup(m, "glass:" + part.Code);
                    if (mesh == null || !PartShown(ship, part)) continue;
                    Mat4f.Mul(tmp, model.Values, partMats[part.Code]);
                    prog.ModelMatrix = tmp;
                    rapi.RenderMultiTextureMesh(mesh, "tex");
                }
                prog.RgbaTint = ColorUtil.WhiteArgbVec;
                rapi.GLDepthMask(true);
                // the water in her hold, blended over what's in it
                DrawFlood(rapi, prog, ship, m, dt);
                prog.RgbaLightIn = ext;
                prog.Tex2D = 0;
                rapi.GlToggleBlend(false);
                rapi.GlEnableCullFace();
                prog.Stop();
            }
            DrawShots(rapi, ep);
        }

        bool shotsFailed;

        /// <summary>
        /// The cannon balls in flight (CannonClient flies them): a design's "shot" mesh (a cast iron ball, from the generator)
        /// at each, in the light where it is. Anything going wrong is logged once and they aren't drawn for the session.
        /// </summary>
        void DrawShots(IRenderAPI rapi, EntityPlayer ep)
        {
            var cannons = sys.Cannons;
            if (shotsFailed || cannons == null || cannons.Balls.Count == 0) return;
            IStandardShaderProgram prog = null;
            try
            {
                MultiTextureMeshRef mesh = null;
                foreach (var m in meshes.Values)
                    if ((mesh = SkinGroup(m, "shot")) != null) break;
                if (mesh == null) return;
                var cam = ep.CameraPos;
                rapi.GlToggleBlend(false);
                rapi.GlEnableCullFace();
                foreach (var p in cannons.Drawn())
                {
                    prog = rapi.PreparedStandardShader((int)Math.Floor(p.X), (int)Math.Floor(p.Y), (int)Math.Floor(p.Z));
                    prog.ViewMatrix = rapi.CameraMatrixOriginf;
                    prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
                    model.Identity().Translate(p.X - cam.X, p.Y - cam.Y, p.Z - cam.Z);
                    prog.ModelMatrix = model.Values;
                    rapi.RenderMultiTextureMesh(mesh, "tex");
                    prog.Stop();
                    prog = null;
                }
            }
            catch (Exception e)
            {
                shotsFailed = true;
                capi.Logger.Error("[hansa] Drawing cannon balls failed; they're not drawn until the game is restarted: {0}", e);
                try { prog?.Stop(); } catch { }
            }
        }

        static void Draw(IRenderAPI rapi, MultiTextureMeshRef mesh)
        {
            if (mesh != null) rapi.RenderMultiTextureMesh(mesh, "tex");
        }

        /// <summary>Ships further off than this (horizontally) aren't drawn, nor their shadows.</summary>
        double MaxDrawDistance() => capi.World.Player.WorldData.DesiredViewDistance + 64;

        /// <summary>
        /// Her model matrix this frame, camera-relative (r = where her origin is drawn, less the camera): turned to her
        /// heading, pitched and rolled on the waves, at her height with the heave. Her shadow uses the same.
        /// </summary>
        void SetModel(EntityShip ship, double rx, double ry, double rz)
        {
            model.Identity().Translate(rx, ry, rz).RotateY(ShipFrame.ModelYaw(ship.Pos.Yaw)).RotateZ((float)ship.Pitch).RotateX((float)ship.Roll);
        }

        /// <summary>
        /// A sail's mesh (the lateen is a smooth mesh in the skin, the square sails are shapes), torn or shredded as it is
        /// (torn: its holes are cut out of the texture, draw it alpha-tested), or null while its mast is lowered.
        /// </summary>
        MultiTextureMeshRef SailMesh(EntityShip ship, Meshes m, int index, SailDef sail, MultiTextureMeshRef shaped, out bool torn)
        {
            torn = false;
            if (sail.Mast != null && ship.OpenFittings.Contains(sail.Mast)) return null;      // a boat's mast lowered: no sail
            var whole = shaped ?? (sail.Mesh != null ? SkinGroup(m, sail.Mesh) : null);
            int level = whole == null ? 0 : SailLevel(ship, index);
            if (level > 0 && TornSail(m, index, sail, level) is MultiTextureMeshRef t)
            {
                torn = true;
                return t;
            }
            return whole;
        }

        readonly float[] sailPlace = new float[16], bellyMat = new float[16], bellyTmp = new float[16];

        /// <summary>(0.3.14, notes 150-152) A ship's sail as drawn last, pressed against her rigging: for the trim (and its mesh) it was built for.</summary>
        class Pressed
        {
            public (MultiTextureMeshRef Mesh, int Trim, int Frac, int Belly) Key;
            public MultiTextureMeshRef Mesh;
            public long Built;
        }

        readonly Dictionary<(long Ship, int Sail), Pressed> pressed = new();
        readonly float[] pressSet = new float[16], pressTmp = new float[16], identity = Mat4f.Create();
        bool pressFailed;
        /// <summary>How often a sail is pressed again while its trim changes (ms): between, the last one is drawn turned with its yard.</summary>
        const long PressEveryMs = 80;

        /// <summary>
        /// (0.3.14, notes 150-152: the lateen through the mizzen mast; "main sails penetrate the ratlines. How to fix it?"; the
        /// fore course through its shrouds) A sail's mesh pressed against her masts, shrouds, backstays and ratlines
        /// (<see cref="SailPress"/>) as it's set and trimmed now, and where it's drawn (its yard's turn: the furl and the lateen's
        /// belly are in its vertices). Built again when the trim (to half a degree), the furl or the lateen's belly changes, at
        /// most every PressEveryMs; anything going wrong, the sail is drawn as made (logged once).
        /// </summary>
        MultiTextureMeshRef PressedSail(EntityShip ship, Meshes m, int si, SailDef sail, MultiTextureMeshRef mesh, float frac, out float[] place)
        {
            place = SailPlace(ship, sail, frac);
            if (pressFailed || mesh == null || !m.SailData.TryGetValue(mesh, out var md) || md == null) return mesh;
            try
            {
                var frames = ship.Def.DamageParts?.SailFrames;
                if (frames == null || si < 0 || si >= frames.Length || frames[si] is not SailFrame f) return mesh;
                m.Press ??= SailPress.Of(ship.Def);
                string yardCode = "yard-" + sail.Code;
                float[] yard = partMats.TryGetValue(yardCode, out var ym) && HasPart(ship.Def, yardCode) ? ym : null;
                double trim = f.ForeAndAft ? ship.Sheet : ship.Brace;
                bool lateen = sail.Mesh != null && f.ForeAndAft;
                float belly = lateen ? PartMotion.LateenBelly(ship.Sheet) : 1;
                var key = (mesh, (int)Math.Round(trim * 360 / Math.PI), (int)Math.Round(frac * 50), (int)Math.Round(belly * 20));
                pressed.TryGetValue((ship.EntityId, si), out var ps);
                long now = capi.World.ElapsedMilliseconds;
                if (ps == null || ps.Key != key && now - ps.Built >= PressEveryMs)
                {
                    var copy = md.Clone();
                    // as it's set before its yard turns it: furled, the lateen bellied to leeward
                    PartMotion.FurlMatrix(sail, frac, pressSet);
                    if (lateen)
                    {
                        PartMotion.BellyMatrix(f, belly, bellyMat);
                        Mat4f.Mul(pressTmp, pressSet, bellyMat);
                        Array.Copy(pressTmp, pressSet, 16);
                    }
                    copy.MatrixTransform(pressSet);
                    m.Press.Press(f, yard, copy.xyz, copy.VerticesCount);
                    if (ps == null) pressed[(ship.EntityId, si)] = ps = new Pressed();
                    ps.Mesh?.Dispose();
                    ps.Mesh = capi.Render.UploadMultiTextureMesh(copy);
                    ps.Key = key;
                    ps.Built = now;
                }
                if (ps.Mesh == null) return mesh;
                place = yard ?? identity;
                return ps.Mesh;
            }
            catch (Exception e)
            {
                pressFailed = true;
                capi.Logger.Error("[hansa] Pressing the sails against the rigging failed; they're drawn as made until the game is restarted: {0}", e);
                return mesh;
            }
        }

        /// <summary>Where a sail is drawn (ship-local): scaled toward its yard as it's furled, under its yard and braced round with it.</summary>
        float[] SailPlace(EntityShip ship, SailDef sail, float frac)
        {
            PartMotion.FurlMatrix(sail, frac, furl);
            // a fore-and-aft sail (the lateen) bellies out to leeward on either tack: its mesh is made bellied to starboard,
            // and is mirrored through its own plane with the wind on the other side (0.3.10: "the mizzen sail looks to be
            // the wrong direction to the wind": it bellied to starboard always, into the wind on the starboard tack)
            int si = Array.IndexOf(ship.Def.Sails, sail);
            var frames = ship.Def.DamageParts?.SailFrames;
            if (sail.Mesh != null && frames != null && si >= 0 && si < frames.Length && frames[si] is SailFrame sf && sf.ForeAndAft)
            {
                PartMotion.BellyMatrix(sf, PartMotion.LateenBelly(ship.Sheet), bellyMat);
                Mat4f.Mul(bellyTmp, furl, bellyMat);
                Array.Copy(bellyTmp, furl, 16);
            }
            if (partMats.TryGetValue("yard-" + sail.Code, out var yard) && HasPart(ship.Def, "yard-" + sail.Code))
            {
                Mat4f.Mul(sailPlace, yard, furl);
                return sailPlace;
            }
            return furl;
        }

        readonly float[] piecePlace = new float[16];

        /// <summary>Where a vanilla block aboard is drawn now (ship-local), and which of its meshes: a door swung about its hinge, a gate's open block while it's open.</summary>
        float[] PiecePlace(EntityShip ship, Decor d, Piece p, out MultiTextureMeshRef mesh)
        {
            var f = p.Fixture;
            mesh = p.Mesh;
            float[] place = p.Fit;
            if (p.HatchId > 0)
            {
                // (0.3.20) a hatch's leaf: about its hinge along x or z, eased (its mesh is drawn shut)
                float frac = d.Open.TryGetValue("piece:" + p.HatchId, out float hf) ? hf : ship.Pieces.Get(p.HatchId)?.Open == true ? 1 : 0;
                if (frac > 0)
                {
                    Mat4f.Identity(piecePlace);
                    Mat4f.Translate(piecePlace, piecePlace, p.HingeX, p.HingeY, p.HingeZ);
                    if (p.HatchAxis == 0) Mat4f.RotateX(piecePlace, piecePlace, p.Swing * EaseInOut(frac));
                    else Mat4f.RotateZ(piecePlace, piecePlace, p.Swing * EaseInOut(frac));
                    Mat4f.Translate(piecePlace, piecePlace, -p.HingeX, -p.HingeY, -p.HingeZ);
                    Mat4f.Mul(piecePlace, piecePlace, p.Fit);
                    place = piecePlace;
                }
                return place;
            }
            if (p.DoorId > 0)
            {
                // a door put aboard: about its hinge, eased (its mesh is drawn shut)
                float frac = d.Open.TryGetValue("piece:" + p.DoorId, out float pf) ? pf : ship.Pieces.Get(p.DoorId)?.Open == true ? 1 : 0;
                if (frac > 0)
                {
                    Mat4f.Identity(piecePlace);
                    Mat4f.Translate(piecePlace, piecePlace, p.HingeX, 0, p.HingeZ);
                    Mat4f.RotateY(piecePlace, piecePlace, p.Swing * EaseInOut(frac));
                    Mat4f.Translate(piecePlace, piecePlace, -p.HingeX, 0, -p.HingeZ);
                    Mat4f.Mul(piecePlace, piecePlace, p.Fit);
                    place = piecePlace;
                }
                return place;
            }
            if (f.Opens)
            {
                bool open = ship.OpenFittings.Contains(f.Code);
                float frac = d.Open.TryGetValue(f.Code, out float of) ? of : open ? 1 : 0;
                if (f.Type == "gate")
                {
                    if (open && p.MeshOpen != null) mesh = p.MeshOpen;
                }
                else if (f.Hinge != null && frac > 0)
                {
                    // swing about the hinge line
                    float hx = (float)f.Hinge[0], hz = (float)f.Hinge[1];
                    float a = f.OpenAngle * GameMath.DEG2RAD * EaseInOut(frac);
                    Mat4f.Identity(piecePlace);
                    Mat4f.Translate(piecePlace, piecePlace, hx, 0, hz);
                    Mat4f.RotateY(piecePlace, piecePlace, a);
                    Mat4f.Translate(piecePlace, piecePlace, -hx, 0, -hz);
                    Mat4f.Mul(piecePlace, piecePlace, p.Fit);
                    place = piecePlace;
                }
            }
            return place;
        }

        /// <param name="outside">lit by the light outside whatever room it's in (glass, 0.3.12)</param>
        void DrawPiece(IRenderAPI rapi, IStandardShaderProgram prog, EntityShip ship, Decor d, Piece p, Vec4f ext, bool outside = false)
        {
            float[] place = PiecePlace(ship, d, p, out var mesh);
            Mat4f.Mul(tmp, model.Values, place);
            prog.ModelMatrix = tmp;
            prog.RgbaLightIn = p.Interior && !outside ? interiorLight : ext;
            rapi.RenderMultiTextureMesh(mesh, "tex");
            // in the oven: its fuel, what's baking
            if (p.Extra != null) foreach (var e in p.Extra) rapi.RenderMultiTextureMesh(e, "tex");
            // on the hearth's fire: a pot and its lid, a meal, a joint on the spit
            if (p.Content != null)
            {
                Mat4f.Mul(tmp, model.Values, p.ContentFit);
                prog.ModelMatrix = tmp;
                rapi.RenderMultiTextureMesh(p.Content, "tex");
            }
            if (p.Lid != null)
            {
                Mat4f.Mul(tmp, model.Values, LidPlace(ship, p));
                prog.ModelMatrix = tmp;
                rapi.RenderMultiTextureMesh(p.Lid, "tex");
            }
            prog.RgbaLightIn = ext;
        }

        static float EaseInOut(float t) => t * t * (3 - 2 * t);

        /// <summary>Doors, gates and hatches swing open and shut, the anchor goes down and comes up, eased toward their state.</summary>
        void UpdateOpenings(EntityShip ship, Decor d, float dt)
        {
            foreach (var f in ship.Def.Fixtures)
            {
                if (!f.Opens && f.Lid == null) continue;
                // (a gun port's lid: open while the gun is out, and while it's still being run in)
                float goal = f.Lid != null ? ship.GunLidGoal(f) : ship.OpenFittings.Contains(f.Code) ? 1 : 0;
                d.Open[f.Code] = d.Open.TryGetValue(f.Code, out float v) ? GameMath.Clamp(v + Math.Sign(goal - v) * dt * 2.5f, 0, 1) : goal;
            }
            // (0.3.12, note 123) the doors put aboard swing as the design's do
            foreach (var p in d.Pieces)
            {
                // ((0.3.20) and the hatches' leaves)
                int pid = p.DoorId > 0 ? p.DoorId : p.HatchId;
                if (pid <= 0) continue;
                string key = "piece:" + pid;
                float goal = ship.Pieces.Get(pid)?.Open == true ? 1 : 0;
                d.Open[key] = d.Open.TryGetValue(key, out float v) ? GameMath.Clamp(v + Math.Sign(goal - v) * dt * 2.5f, 0, 1) : goal;
            }
            float anchorGoal = ship.Anchored ? 1 : 0;
            d.Anchor = d.Anchor < 0 ? anchorGoal : GameMath.Clamp(d.Anchor + Math.Sign(anchorGoal - d.Anchor) * dt * 0.3f, 0, 1);
        }

        /// <summary>
        /// What moves a part now: the rudder (-1..1), a door, gate or hatch opening (0..1, eased), the anchor (0 up .. 1
        /// down), the crew's trim of the square yards and of the lateen (radians, eased by the ship).
        /// </summary>
        static float PartValue(EntityShip ship, Decor d, MovingPartDef part)
        {
            string src = part.Source ?? "rudder";
            if (src == "rudder") return ship.RudderShown;
            if (src == "brace") return (float)ship.Brace;
            if (src == "sheet") return (float)ship.Sheet;
            if (src.StartsWith("pump:"))
            {
                // a pump's brake rocks while someone works it (a stroke a second)
                if (ship.Pumpers <= 0) return 0;
                double stroke = Math.Max(0.2, ship.DamageParts?.D.PumpStroke ?? 1);
                return (float)Math.Sin(ship.World.ElapsedMilliseconds / 1000.0 * 2 * Math.PI / stroke);
            }
            if (src.StartsWith("stroke:"))
            {
                // a boat's oars sweep fore and aft while the rower there pulls or backs water (about a stroke in 1.6 s)
                if (ship.Rowing == 0 || !Manned(ship, src.Substring(7))) return 0;
                return (float)Math.Sin(ship.World.ElapsedMilliseconds / 1000.0 * 2 * Math.PI / 1.6) * ship.Rowing;
            }
            // a gun runs back when it fires, and is run out again (PartMotion.Recoil)
            if (src.StartsWith("fire:")) return ship.GunRecoil(src.Substring(5));
            // a gun trained (degrees toward the bow) and elevated (degrees up), as it's drawn (EntityShip.GunsFrame)
            if (src.StartsWith("train:")) return ship.GunLayNow(src.Substring(6)).Train;
            if (src.StartsWith("elevate:")) return ship.GunLayNow(src.Substring(8)).Elevation;
            if (d == null) return 0;
            if (src == "anchor") return EaseInOut(Math.Max(0, d.Anchor));
            if (src.StartsWith("open:")) return d.Open.TryGetValue(src.Substring(5), out float v) ? EaseInOut(v) : 0;
            return 0;
        }

        /// <summary>This frame's matrix for every moving part of a ship (ship-local), followers after what they follow.</summary>
        void ComputeParts(EntityShip ship, Decor d)
        {
            foreach (var part in ship.Def.MovingParts)
            {
                if (!partMats.TryGetValue(part.Code, out var mat)) partMats[part.Code] = mat = new float[16];
                if (part.Kind == "follow") continue;
                // riding on another part (a gun's trucks and barrel on its carriage, the carriage on the turn it's
                // trained by): moved with it (the parent is listed first)
                PartMotion.Place(part, PartValue(ship, d, part), mat, partMats);
            }
            float frac = PartMotion.DrawnSail(ship.SailFraction);
            foreach (var part in ship.Def.MovingParts)
            {
                if (part.Kind != "follow") continue;
                float[] tipMove = part.Follow != null && partMats.TryGetValue(part.Follow, out var fm) ? fm : null;
                // a sheet: its tip is on the clew, which goes up toward the yard as the sail is furled
                if (tipMove != null && part.Sail != null && SailOf(ship.Def, part.Sail) is SailDef sd)
                {
                    PartMotion.FurlMatrix(sd, frac, furlTmp);
                    Mat4f.Mul(followTmp, tipMove, furlTmp);
                    tipMove = followTmp;
                }
                float[] anchorMove = part.AnchorFollow != null && partMats.TryGetValue(part.AnchorFollow, out var am) ? am : null;
                PartMotion.FollowMatrix(part, tipMove, partMats[part.Code], anchorMove);
            }
        }

        readonly float[] furlTmp = new float[16], followTmp = new float[16];

        /// <summary>Is someone on this fixture's seat (a boat's rowing thwart, a helm)?</summary>
        static bool Manned(EntityShip ship, string fixture) =>
            ship.GetSeat("row:" + fixture)?.Passenger != null || ship.GetSeat("helm:" + fixture)?.Passenger != null;

        /// <summary>A part drawn only some of the time (MovingPartDef.Shown): an oar out while someone rows there, stowed otherwise; a sail only while the mast is up.</summary>
        static bool PartShown(EntityShip ship, MovingPartDef part)
        {
            // (0.3.21, step C) a gun place's gun: drawn only while it's mounted
            if (part.Code != null && ship.Def?.GunOfPart != null && ship.Def.GunOfPart.TryGetValue(part.Code, out string gun) && ship.GunAbsent(ship.Def.Fixture(gun))) return false;
            // (0.3.28, D2.4) a port planked over (its cover filled): no lid
            if (part.Code != null && ship.Def?.GunOfLid != null && ship.Def.GunOfLid.TryGetValue(part.Code, out string lidGun) && ship.PortCovered(lidGun)) return false;
            string s = part.Shown;
            int c = s?.IndexOf(':') ?? -1;
            if (c < 0) return true;
            string what = s.Substring(0, c), code = s.Substring(c + 1);
            return what switch
            {
                "manned" => Manned(ship, code),
                "unmanned" => !Manned(ship, code),
                "open" => ship.OpenFittings.Contains(code),
                "shut" => !ship.OpenFittings.Contains(code),
                _ => true
            };
        }

        /// <summary>The design of Hansa's own boat this item is (on the gallows it's drawn from its mesh), or null.</summary>
        ShipDefinition StowedBoatDef(ItemStack stack)
        {
            if (stack?.Collectible is not ItemShipDeed deed || !deed.IsBoat) return null;
            return sys.Hansa.GetShip(stack.Collectible.Attributes?["ship"].AsString());
        }

        readonly float[] boatMat = new float[16], boatPart = new float[16];

        /// <summary>
        /// Hansa's own boats on a ship's gallows, drawn from their own mesh: keel on the chocks, mast lowered, oars
        /// stowed, the rudder shipped.
        /// </summary>
        void DrawStowedBoats(IRenderAPI rapi, IStandardShaderProgram prog, EntityShip ship)
        {
            foreach (var f in ship.Def.Fixtures)
            {
                if (f.Kind != "boat" || !ship.Attached.TryGetValue(f.Code, out var stack) || StowedBoatDef(stack) is not ShipDefinition bd) continue;
                var bm = Get(bd);
                if (bm.SkinLoad != null) UploadSkin(bm);
                StowedBoatPlace(f, bd);
                Mat4f.Mul(tmp, model.Values, boatMat);
                prog.ModelMatrix = tmp;
                Draw(rapi, bm.Exterior);
                Draw(rapi, SkinGroup(bm, "exterior"));
                Draw(rapi, SkinGroup(bm, "bottom"));
                Draw(rapi, SkinGroup(bm, "interior"));
                foreach (var part in bd.MovingParts)
                {
                    var mesh = SkinGroup(bm, "part:" + part.Code);
                    if (mesh == null || !StowedPart(part)) continue;
                    Mat4f.Mul(tmp2, boatMat, boatPart);
                    Mat4f.Mul(tmp, model.Values, tmp2);
                    prog.ModelMatrix = tmp;
                    rapi.RenderMultiTextureMesh(mesh, "tex");
                }
            }
            prog.ModelMatrix = model.Values;
        }

        /// <summary>Where a boat on the gallows lies (ship-local, into boatMat): keel on the chocks, turned the way the place faces.</summary>
        void StowedBoatPlace(FixtureDef f, ShipDefinition bd)
        {
            Mat4f.Identity(boatMat);
            Mat4f.Translate(boatMat, boatMat, (float)f.Pos[0], (float)(f.Pos[1] - bd.KeelY + 0.05), (float)f.Pos[2]);
            Mat4f.RotateY(boatMat, boatMat, f.Yaw * GameMath.DEG2RAD);
        }

        /// <summary>
        /// A stowed boat's moving part as it lies on the gallows (boat-local, into boatPart), or false if it isn't there:
        /// stowed shows what's shown when nobody rows and the mast is down (lowered), with the rudder and tiller unshipped.
        /// </summary>
        bool StowedPart(MovingPartDef part)
        {
            if (part.Kind == "follow") return false;
            string sh = part.Shown ?? "";
            if (sh.StartsWith("manned:") || sh.StartsWith("shut:")) return false;
            if (part.Source == "rudder" && part.Kind != "static") return false;      // the rudder and tiller unshipped
            float v = part.Source != null && part.Source.StartsWith("open:") ? 1 : 0;
            PartMotion.PartMatrix(part, PartMotion.TurnDegrees(part, v) * GameMath.DEG2RAD, boatPart);
            return true;
        }

        static SailDef SailOf(ShipDefinition def, string code)
        {
            foreach (var s in def.Sails) if (s.Code == code) return s;
            return null;
        }

        readonly Dictionary<string, float[]> partMats = new();

        static bool HasPart(ShipDefinition def, string code)
        {
            foreach (var p in def.MovingParts) if (p.Code == code) return true;
            return false;
        }

        // ------------------------------------------------------------------ her shadow

        // this pass's shadow map projection and sun's view (copied: the engine hands both out in one shared array)
        readonly float[] shadowProj = new float[16], shadowView = new float[16];
        readonly float[] shadowModel = new float[16], shadowLocal = new float[16], shadowMvp = new float[16];
        IShaderProgram shadowProg;
        float shadowPush;
        bool shadowsFailed;

        /// <summary>
        /// The engine's shadow stages (it runs them only with shadows on in the graphics settings, and ShadowNear only from
        /// the second step up): each ship within the shadow map's box is drawn into it in the pose she's drawn in, with
        /// the shader the engine draws chunks' and held items' shadows with. So her hull, sails and rigging shade the
        /// world, the water, the people aboard and her own decks. What goes in: <see cref="ShipShadows"/>.
        /// </summary>
        void RenderShadows(bool far)
        {
            if (shadowsFailed || !sys.Comfort.ShipShadows || sys.Ships.Count == 0) return;
            var ep = capi.World.Player?.Entity;
            if (ep == null) return;
            var rapi = capi.Render;
            IShaderProgram prog = null, before = null;
            bool swap = false;
            try
            {
                if (rapi.ShaderUniforms == null || rapi.ShaderUniforms.DropShadowIntensity <= 0.01f) return;
                prog = ShadowProgram(rapi);
                if (prog == null) return;
                // normally the engine's own shadow shader is on already (the entities' shadows, just before, leave it so)
                before = rapi.CurrentActiveShader;
                swap = before != prog;
                if (swap)
                {
                    before?.Stop();
                    prog.Use();
                }
                Array.Copy(rapi.CurrentProjectionMatrix, shadowProj, 16);
                Array.Copy(rapi.CurrentModelviewMatrix, shadowView, 16);
                shadowProg = prog;
                shadowPush = far ? ShipShadows.FarDepthPush : ShipShadows.NearDepthPush;
                if (prog.HasUniform("origin")) prog.Uniform("origin", 0f, 0f, 0f);
                // single sheets (planking, sails, rope strands) cast from either side
                rapi.GlDisableCullFace();
                var cam = ep.CameraPos;
                double maxDist = MaxDrawDistance();
                var culler = rapi.DefaultFrustumCuller;
                foreach (var ship in sys.Ships)
                {
                    if (ship.Def == null || !meshes.TryGetValue(ship.Def.Code, out var m)) continue;
                    var o = RenderOrigin(ship, ep);
                    double rx = o.X - cam.X, ry = o.Y - cam.Y, rz = o.Z - cam.Z;
                    if (rx * rx + rz * rz > maxDist * maxDist) continue;
                    // only a ship within this map's box (during the shadow stages the engine's culler holds it, in world coordinates)
                    ShipShadows.Sphere(ship.Def, out double lx, out double ly, out double lz, out double radius);
                    ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, lx, ly, lz, out double wx, out double wy, out double wz);
                    if (culler != null && !culler.SphereInFrustum(o.X + wx, o.Y + wy, o.Z + wz, radius)) continue;
                    SetModel(ship, rx, ry, rz);
                    CastShip(rapi, ship, m, far);
                }
            }
            catch (Exception e)
            {
                // never break the frame: no ship shadows for the rest of the session instead
                shadowsFailed = true;
                capi.Logger.Error("[hansa] Ship shadows failed; they're off until the game is restarted: {0}", e);
            }
            finally
            {
                try
                {
                    // left as the engine had it: its shadow shader, set up for chunks
                    if (prog != null && rapi.CurrentActiveShader == prog && rapi.CurrentShadowProjectionMatrix is float[] chunkMvp) prog.UniformMatrix("mvpMatrix", chunkMvp);
                    if (prog != null && swap)
                    {
                        prog.Stop();
                        before?.Use();
                    }
                }
                catch (Exception e)
                {
                    shadowsFailed = true;
                    capi.Logger.Error("[hansa] Ship shadows couldn't give the shadow shader back; they're off until the game is restarted: {0}", e);
                }
                shadowProg = null;
            }
        }

        /// <summary>
        /// The engine's shadow shader for meshes like ours (position, uv, colour and flags per vertex; it takes an
        /// mvpMatrix, an origin and the texture as tex2d, and skips see-through texels, so rigging and sail cut-outs cast
        /// their own shape): chunkshadowmap, or its NoSSBOs build when chunks are drawn from SSBOs (the plain one then
        /// reads no vertices). Null if it isn't there.
        /// </summary>
        static IShaderProgram ShadowProgram(IRenderAPI rapi)
        {
            var prog = rapi.GetEngineShader(rapi.UseSSBOs ? EnumShaderProgram.Chunkshadowmap_NoSSBOs : EnumShaderProgram.Chunkshadowmap);
            if (prog == null || prog.Disposed || prog.LoadError || !prog.HasUniform("mvpMatrix") || !prog.HasUniform("tex2d")) return null;
            return prog;
        }

        /// <summary>One mesh into the shadow map, at her model matrix × local (ship-local; null: none).</summary>
        void CastShadow(IRenderAPI rapi, MultiTextureMeshRef mesh, float[] local)
        {
            if (mesh == null || mesh.Disposed || !mesh.Initialized) return;
            float[] mm = model.Values;
            if (local != null)
            {
                Mat4f.Mul(shadowModel, model.Values, local);
                mm = shadowModel;
            }
            ShipShadows.ShadowMvp(shadowMvp, shadowProj, shadowView, mm, shadowPush);
            shadowProg.UniformMatrix("mvpMatrix", shadowMvp);
            rapi.RenderMultiTextureMesh(mesh, "tex2d");
        }

        /// <summary>What of her shapes her shadow (<see cref="ShipShadows"/>), placed as the normal pass places it.</summary>
        void CastShip(IRenderAPI rapi, EntityShip ship, Meshes m, bool far)
        {
            var def = ship.Def;
            // the decks, castles and bulwarks (the outer faces; the Interior shape holds the cabins' and the hold's own)
            CastShadow(rapi, m.Exterior, null);
            // (her planking as it's drawn: with the holes in it)
            if (ShipShadows.CastsSkin("exterior", far)) DrawHull(rapi, ship, m, "exterior", true);
            string bottom = ship.Clad ? "bottomclad" : "bottom";
            if (ShipShadows.CastsSkin(bottom, far)) DrawHull(rapi, ship, m, bottom, true);
            // (0.3.20) her deck cells as drawn (the holes taken up in them); (0.3.26) her hull panels
            DrawSlots(rapi, ship, "slot:deck", true);
            if (ShipShadows.CastsSkin("exterior", far)) DrawSlots(rapi, ship, "slot:hull", true);
            if (ShipShadows.CastsSkin(bottom, far)) DrawSlots(rapi, ship, "slot:hull:" + bottom, true);
            foreach (var f in def.Fixtures)
            {
                if (f.Type == "figurehead" && ship.FigureheadGroup(f) is string g && g.Length > 0 && ShipShadows.CastsSkin(g, far)) CastShadow(rapi, SkinGroup(m, g), null);
            }

            // the moving parts and the sails as they're drawn this frame (the parts are worked out here too: the shadow
            // stages come before the normal pass)
            decor.TryGetValue(ship.EntityId, out var d);
            ComputeParts(ship, d);
            foreach (var part in def.MovingParts)
            {
                if (!ShipShadows.CastsPart(part, far) || !PartShown(ship, part)) continue;
                var mesh = SkinGroup(m, "part:" + part.Code);
                if (mesh != null && partMats.TryGetValue(part.Code, out var pm)) CastShadow(rapi, mesh, pm);
            }
            float frac = PartMotion.DrawnSail(ship.SailFraction);
            for (int si = 0; si < m.Sails.Count; si++)
            {
                var (sail, shaped) = m.Sails[si];
                var mesh = SailMesh(ship, m, si, sail, shaped, out _);
                if (mesh != null)
                {
                    mesh = PressedSail(ship, m, si, sail, mesh, frac, out var sailAt);
                    CastShadow(rapi, mesh, sailAt);
                }
            }

            // vanilla blocks aboard, and our own boats on the gallows
            if (d != null)
            {
                foreach (var p in d.Pieces)
                {
                    if (!ShipShadows.CastsFixture(p.Fixture, p.Interior, p.Glass, far)) continue;
                    float[] place = PiecePlace(ship, d, p, out var mesh);
                    CastShadow(rapi, mesh, place);
                }
            }
            foreach (var f in def.Fixtures)
            {
                if (f.Kind != "boat" || !ship.Attached.TryGetValue(f.Code, out var stack) || StowedBoatDef(stack) is not ShipDefinition bd) continue;
                if (!ShipShadows.CastsFixture(f, def.Indoors(f), false, far) || !meshes.TryGetValue(bd.Code, out var bm)) continue;
                StowedBoatPlace(f, bd);
                CastShadow(rapi, bm.Exterior, boatMat);
                CastShadow(rapi, SkinGroup(bm, "exterior"), boatMat);
                CastShadow(rapi, SkinGroup(bm, "bottom"), boatMat);
                foreach (var part in bd.MovingParts)
                {
                    var mesh = SkinGroup(bm, "part:" + part.Code);
                    if (mesh == null || !ShipShadows.CastsPart(part, far) || !StowedPart(part)) continue;
                    Mat4f.Mul(shadowLocal, boatMat, boatPart);
                    CastShadow(rapi, mesh, shadowLocal);
                }
            }
        }

        /// <summary>
        /// Sea asks every frame where the hulls are, to keep the waves out of them. The water inside stays under the
        /// hull's tilted waterline; the waves next to it settle to the level she rides at (without her lean in the
        /// wind) over a ring that's narrow while the hull follows the sea and wider when this player's SeaMotion
        /// setting keeps the ship steadier than the water.
        /// </summary>
        public void AddWaterClips(List<Sea.WaterClipBox> clips)
        {
            var ep = capi.World.Player?.Entity;
            if (ep == null) return;
            AddBoatClips(clips, ep);
            float calm = RidingCalmZone + (Sea.WaterShader.CalmZone - RidingCalmZone) * (1 - sys.SeaMotion);
            foreach (var ship in sys.Ships)
            {
                var wc = ship.Def?.WaterClip;
                // (a ship going down, or on the bottom, is full of the sea: no cut)
                if (wc == null || ship.GoneDown) continue;
                var o = RenderOrigin(ship, ep);
                // the water in her hold (level in the world) as it's drawn (else as it's wet): the sea isn't cut away under it
                double flood = !ship.Flooded ? double.NaN
                    : floods.TryGetValue(ship.EntityId, out var fl) && !double.IsNaN(fl.WorldDy) ? o.Y + fl.WorldDy
                    : o.Y + ship.FloodWorldY - ship.Pos.Y;
                clips.Add(ShipWater.Clip(ship.Def, o.X, o.Y, o.Z, ship.Pos.Yaw, ship.Pitch, ship.Roll, ship.Heel, calm, flood));
            }
        }

        /// <summary>The rowing boats nearby: the water held down under them (EntityRowBoat.WaterClip).</summary>
        public void AddBoatClips(List<Sea.WaterClipBox> clips, EntityPlayer ep)
        {
            foreach (var boat in sys.RowBoats)
            {
                if (!boat.Alive || boat.Pos.Dimension != ep.Pos.Dimension) continue;
                if (boat.Pos.SquareDistanceTo(ep.Pos.X, ep.Pos.Y, ep.Pos.Z) > 96 * 96) continue;
                clips.Add(boat.WaterClip(boat.Pos.XYZ));
            }
        }

        /// <summary>Blocks around a hull that rides the waves over which they settle (Sea's default for a still hull is 4).</summary>
        public const float RidingCalmZone = 1.5f;

        public void Dispose()
        {
            foreach (var m in meshes.Values)
            {
                m.Exterior?.Dispose();
                m.Interior?.Dispose();
                foreach (var r in m.Skin.Values) r?.Dispose();
                foreach (var (_, mesh) in m.Sails) mesh?.Dispose();
                m.Split?.Dispose();
                foreach (var r in m.TornSails.Values) r?.Dispose();
                m.SailData.Clear();
            }
            meshes.Clear();
            foreach (var dl in damageLooks.Values) dl.Dispose();
            foreach (var ps in pressed.Values) ps.Mesh?.Dispose();
            pressed.Clear();
            damageLooks.Clear();
            foreach (var fl in floods.Values) fl.Dispose();
            floods.Clear();
            foreach (var (_, tex) in nameTextures.Values) tex?.Dispose();
            nameTextures.Clear();
            nameQuad?.Dispose();
            nameQuad = null;
            foreach (var d in decor.Values) d.Dispose();
            decor.Clear();
            DisposeAllPaint();
            DisposeAllSlots();
            foreach (var pl in registeredLights) capi.Render.RemovePointLight(pl);
            registeredLights.Clear();
            lights.Clear();
        }
    }
}
