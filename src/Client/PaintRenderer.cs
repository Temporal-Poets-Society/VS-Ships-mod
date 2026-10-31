using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// Her paint drawn (ShipPaint, ShipDecor). A design's meshes are split once (the first time a painted ship of hers is
    /// drawn) into what no zone has and each zone's own part, in each group of her smooth mesh and in her two voxel shapes;
    /// a painted ship draws a zone's part from her own mesh (its quads on the paint's texture), the rest as the design has
    /// it. Her hull's planking is drawn by chunks for her damage already (HullDamageSkin): a chunk with a painted zone in it
    /// is built for her like a damaged one, with the paint under its damage. A change of paint builds again only the zones it
    /// changed and the chunks they reach. Anything going wrong is logged once and her paint isn't drawn this session.
    /// </summary>
    public partial class ShipRenderer
    {
        class PaintSplit
        {
            public PaintPlan Plan;
            public readonly List<string> TexNames = new();
            public readonly List<TextureAtlasPosition> Tex = new();
            public readonly Dictionary<string, int> TexIndex = new();
            /// <summary>Per group: what no zone has (not the hull chunks'), each zone's own quads, and the hull chunks.</summary>
            public readonly Dictionary<string, MultiTextureMeshRef> Rest = new();
            public readonly Dictionary<(string Group, int Zone), List<int>> ZoneQuads = new();
            public readonly Dictionary<(string Group, int Zone), MultiTextureMeshRef> ZoneMesh = new();
            public readonly Dictionary<string, List<int>> ZonesIn = new();
            public readonly Dictionary<string, HashSet<int>> ZonesOfGroup = new();
            public readonly Dictionary<string, List<int>> ChunksOfGroup = new();
            public readonly Dictionary<int, HashSet<int>> ChunksOfZone = new();
            public readonly Dictionary<int, HashSet<int>> ZonesOfChunk = new();
            /// <summary>The voxel shapes ("exterior", "interior"): what no zone has, each zone's faces.</summary>
            public readonly Dictionary<string, MultiTextureMeshRef> ShapeRest = new();
            public readonly Dictionary<(string Shape, int Zone), MultiTextureMeshRef> ShapeZone = new();
            public readonly Dictionary<string, HashSet<int>> ZonesOfShape = new();
            /// <summary>The voxel shapes as loaded (once; each zone's mesh is made from a copy).</summary>
            public readonly Dictionary<string, (Shape Shape, string Loc)> Shapes = new();

            public void Dispose()
            {
                foreach (var r in Rest.Values) r?.Dispose();
                foreach (var r in ZoneMesh.Values) r?.Dispose();
                foreach (var r in ShapeRest.Values) r?.Dispose();
                foreach (var r in ShapeZone.Values) r?.Dispose();
                Rest.Clear();
                ZoneMesh.Clear();
                ShapeRest.Clear();
                ShapeZone.Clear();
            }
        }

        /// <summary>One ship's paint as drawn: what each zone was built with, and her own meshes for the painted zones.</summary>
        class PaintLook
        {
            public int Version = int.MinValue;
            public readonly Dictionary<int, string> Paint = new();
            public readonly Dictionary<(string Group, int Zone), MultiTextureMeshRef> Own = new();
            public readonly Dictionary<(string Shape, int Zone), MultiTextureMeshRef> ShapeOwn = new();

            public void Dispose()
            {
                foreach (var r in Own.Values) r?.Dispose();
                foreach (var r in ShapeOwn.Values) r?.Dispose();
                Own.Clear();
                ShapeOwn.Clear();
            }
        }

        readonly Dictionary<string, PaintSplit> paintSplits = new();
        readonly Dictionary<long, PaintLook> paintLooks = new();
        bool paintFailed;

        /// <summary>
        /// Every frame, after her damage: her paint as drawn follows hers (the zones whose paint changed built again, the hull
        /// chunks they reach queued like damaged ones), and queued chunks built a few a frame.
        /// </summary>
        void UpdatePaint(EntityShip ship, Meshes m)
        {
            if (paintFailed || m.SkinLoad != null || m.File == null) return;
            try
            {
                paintLooks.TryGetValue(ship.EntityId, out var pl);
                if (pl == null && ship.Paint.Zones.Count == 0) return;
                var plan = sys.Hansa.PaintPlanFor(ship.Def);
                if (plan == null || !plan.Usable) return;
                if (pl == null) paintLooks[ship.EntityId] = pl = new PaintLook();
                damageLooks.TryGetValue(ship.EntityId, out var dl);
                if (pl.Version != ship.PaintVersion)
                {
                    var sp = EnsurePaintSplit(ship.Def, m, plan);
                    if (sp == null) return;
                    pl.Version = ship.PaintVersion;
                    var now = new Dictionary<int, string>();
                    // ((0.1.0-dev.9) a row's zone painted as the whole zone was, if it isn't itself: PaintPlan.PaintOf)
                    for (int zi = 0; zi < plan.Zones.Length; zi++)
                        if (plan.PaintOf(ship.Paint, zi) is string p) now[zi] = p;
                    var changed = new HashSet<int>(now.Keys.Concat(pl.Paint.Keys).Where(z => (now.TryGetValue(z, out var a) ? a : null) != (pl.Paint.TryGetValue(z, out var b) ? b : null)));
                    pl.Paint.Clear();
                    foreach (var kv in now) pl.Paint[kv.Key] = kv.Value;
                    foreach (int z in changed)
                    {
                        string paint = now.TryGetValue(z, out var p) ? p : null;
                        foreach (var key in sp.ZoneQuads.Keys.Where(k => k.Zone == z).ToList())
                        {
                            if (pl.Own.TryGetValue(key, out var old)) { old?.Dispose(); pl.Own.Remove(key); }
                            if (paint != null) pl.Own[key] = BuildZone(m, sp, key.Group, sp.ZoneQuads[key], paint);
                        }
                        foreach (string shape in sp.ZonesOfShape.Keys)
                        {
                            if (!sp.ZonesOfShape[shape].Contains(z)) continue;
                            var key = (shape, z);
                            if (pl.ShapeOwn.TryGetValue(key, out var old)) { old?.Dispose(); pl.ShapeOwn.Remove(key); }
                            if (paint != null) pl.ShapeOwn[key] = ShapeZoneMesh(sp, ship.Def, shape, plan, z, paint);
                        }
                        if (m.Hull != null && sp.ChunksOfZone.TryGetValue(z, out var chunks))
                        {
                            if (dl == null) damageLooks[ship.EntityId] = dl = new DamageLook();
                            foreach (int c in chunks) dl.Pending.Add(c);
                        }
                    }
                }
                // (with her damage drawn, UpdateDamage builds them too; this keeps them coming with it off)
                for (int n = 0; n < ChunksPerFrame && dl != null && dl.Pending.Count > 0 && m.Hull != null; n++)
                {
                    int c = dl.Pending.Min;
                    dl.Pending.Remove(c);
                    RebuildChunk(m, dl, c, ship);
                }
            }
            catch (Exception e)
            {
                paintFailed = true;
                capi.Logger.Error("[ships] Drawing ship paint failed; it's off until the game is restarted: {0}", e);
            }
        }

        /// <summary>A hull chunk's base textures for her paint (a painted zone's quads on the paint's), or null if none of its quads is painted.</summary>
        System.Func<int, int> ChunkPaint(EntityShip ship, Meshes m, HullChunk chunk)
        {
            if (ship == null || paintFailed || !paintLooks.TryGetValue(ship.EntityId, out var pl) || pl.Paint.Count == 0) return null;
            if (!paintSplits.TryGetValue(ship.Def.Code, out var sp) || sp == null || !sp.ZonesOfChunk.TryGetValue(chunk.Index, out var zones)) return null;
            if (!zones.Any(z => pl.Paint.ContainsKey(z))) return null;
            if (!sp.Plan.QuadZones.TryGetValue(chunk.Group, out var zoneOf)) return null;
            var quads = m.File.Groups[chunk.Group];
            var hull = m.Hull;
            return q =>
            {
                int own = (int)quads[q][0];
                int z = q < zoneOf.Length ? zoneOf[q] : -1;
                if (z < 0 || !pl.Paint.TryGetValue(z, out var paint) || own < 0 || own >= hull.Textures.Count) return own;
                return hull.TextureIndex(ShipPaint.Painted(hull.Textures[own], paint));
            };
        }

        /// <summary>
        /// A group of her smooth mesh drawn with her paint, if any painted zone is in it: what no zone has, each zone (hers
        /// painted, else as the design has it), and the hull chunks in it (hers where her damage or paint changed them). False:
        /// draw it as usual.
        /// </summary>
        bool DrawPainted(IRenderAPI rapi, EntityShip ship, Meshes m, string group)
        {
            if (paintFailed || !paintLooks.TryGetValue(ship.EntityId, out var pl) || pl.Paint.Count == 0) return false;
            if (!paintSplits.TryGetValue(ship.Def.Code, out var sp) || sp == null) return false;
            if (!sp.ZonesOfGroup.TryGetValue(group, out var zones) || !zones.Any(z => pl.Paint.ContainsKey(z))) return false;
            if (sp.Rest.TryGetValue(group, out var rest)) Draw(rapi, rest);
            if (sp.ZonesIn.TryGetValue(group, out var zs))
                foreach (int z in zs)
                    Draw(rapi, pl.Own.TryGetValue((group, z), out var own) ? own : sp.ZoneMesh.TryGetValue((group, z), out var dm) ? dm : null);
            if (sp.ChunksOfGroup.TryGetValue(group, out var chunks) && m.Split != null)
            {
                damageLooks.TryGetValue(ship.EntityId, out var dl);
                foreach (int c in chunks) Draw(rapi, dl != null && dl.Own.TryGetValue(c, out var o) ? o : m.Split.Sound[c]);
            }
            return true;
        }

        /// <summary>A voxel shape ("exterior", "interior") drawn with her paint, if any painted zone has faces in it. False: draw it as usual.</summary>
        bool DrawPaintedShape(IRenderAPI rapi, EntityShip ship, string shape)
        {
            if (paintFailed || !paintLooks.TryGetValue(ship.EntityId, out var pl) || pl.Paint.Count == 0) return false;
            if (!paintSplits.TryGetValue(ship.Def.Code, out var sp) || sp == null) return false;
            if (!sp.ZonesOfShape.TryGetValue(shape, out var zones) || !zones.Any(z => pl.Paint.ContainsKey(z))) return false;
            if (sp.ShapeRest.TryGetValue(shape, out var rest)) Draw(rapi, rest);
            foreach (int z in zones)
                Draw(rapi, pl.ShapeOwn.TryGetValue((shape, z), out var own) ? own : sp.ShapeZone.TryGetValue((shape, z), out var dm) ? dm : null);
            return true;
        }

        /// <summary>The design's meshes split by her zones (once), or null if they can't be.</summary>
        PaintSplit EnsurePaintSplit(ShipDefinition def, Meshes m, PaintPlan plan)
        {
            if (paintSplits.TryGetValue(def.Code, out var sp)) return sp;
            paintSplits[def.Code] = null;
            sp = new PaintSplit { Plan = plan };
            sp.TexNames.AddRange(m.File.Textures);
            sp.Tex.AddRange(m.Tex);
            for (int i = 0; i < sp.TexNames.Count; i++) sp.TexIndex[sp.TexNames[i]] = i;
            if (m.Hull != null) EnsureSplit(m);
            foreach (var kv in plan.QuadZones)
            {
                string g = kv.Key;
                // (0.3.20) the deck cells' groups are painted as their chunks are built (SlotRenderer)
                if (g.StartsWith("slot:")) continue;
                var zoneOf = kv.Value;
                if (!m.File.Groups.TryGetValue(g, out var quads)) continue;
                var zones = new HashSet<int>();
                bool split = m.Hull != null && m.Hull.Static.TryGetValue(g, out _);
                IEnumerable<int> statics = split ? m.Hull.Static[g] : Enumerable.Range(0, quads.Length);
                var rest = new List<int>();
                foreach (int i in statics)
                {
                    int z = i < zoneOf.Length ? zoneOf[i] : -1;
                    if (z < 0) { rest.Add(i); continue; }
                    zones.Add(z);
                    if (!sp.ZoneQuads.TryGetValue((g, z), out var list)) sp.ZoneQuads[(g, z)] = list = new List<int>();
                    list.Add(i);
                }
                if (split)
                {
                    var chunks = new List<int>();
                    foreach (var c in m.Hull.Chunks)
                    {
                        if (c.Group != g) continue;
                        chunks.Add(c.Index);
                        foreach (int q in c.Quads)
                        {
                            int z = q < zoneOf.Length ? zoneOf[q] : -1;
                            if (z < 0) continue;
                            zones.Add(z);
                            if (!sp.ChunksOfZone.TryGetValue(z, out var cz)) sp.ChunksOfZone[z] = cz = new HashSet<int>();
                            cz.Add(c.Index);
                            if (!sp.ZonesOfChunk.TryGetValue(c.Index, out var zc)) sp.ZonesOfChunk[c.Index] = zc = new HashSet<int>();
                            zc.Add(z);
                        }
                    }
                    sp.ChunksOfGroup[g] = chunks;
                }
                if (zones.Count == 0) continue;
                sp.ZonesOfGroup[g] = zones;
                sp.ZonesIn[g] = sp.ZoneQuads.Keys.Where(k => k.Group == g).Select(k => k.Zone).OrderBy(z => z).ToList();
                sp.Rest[g] = BuildZone(m, sp, g, rest, null);
                foreach (int z in sp.ZonesIn[g]) sp.ZoneMesh[(g, z)] = BuildZone(m, sp, g, sp.ZoneQuads[(g, z)], null);
            }
            foreach (var kv in plan.FaceZones)
            {
                var zones = new HashSet<int>(kv.Value.Values.SelectMany(f => f).Where(z => z >= 0));
                if (zones.Count == 0) continue;
                sp.ZonesOfShape[kv.Key] = zones;
                sp.ShapeRest[kv.Key] = ShapeZoneMesh(sp, def, kv.Key, plan, -1, null);
                foreach (int z in zones) sp.ShapeZone[(kv.Key, z)] = ShapeZoneMesh(sp, def, kv.Key, plan, z, null);
            }
            paintSplits[def.Code] = sp;
            return sp;
        }

        /// <summary>Some quads of a group as one mesh, on a paint's textures (null paint: their own).</summary>
        MultiTextureMeshRef BuildZone(Meshes m, PaintSplit sp, string group, List<int> idx, string paint)
        {
            if (idx == null || idx.Count == 0) return null;
            var quads = m.File.Groups[group];
            var list = new List<float[]>(idx.Count);
            foreach (int i in idx)
            {
                var q = quads[i];
                if (paint != null)
                {
                    q = (float[])q.Clone();
                    q[0] = PaintTex(sp, ShipPaint.Painted(sp.TexNames[(int)q[0]], paint));
                }
                list.Add(q);
            }
            return BuildSkin(list.ToArray(), sp.Tex.ToArray());
        }

        int PaintTex(PaintSplit sp, string name)
        {
            if (sp.TexIndex.TryGetValue(name, out int i)) return i;
            capi.BlockTextureAtlas.GetOrInsertTexture(AssetLocation.Create(name), out _, out var pos);
            i = sp.TexNames.Count;
            sp.TexNames.Add(name);
            sp.Tex.Add(pos ?? capi.BlockTextureAtlas.UnknownTexturePosition);
            sp.TexIndex[name] = i;
            return i;
        }

        /// <summary>
        /// A voxel shape's faces of one zone (zone -1: every face no zone has), its elements only those with such faces, on a
        /// paint's textures (null: their own); tesselated as the design's shape is.
        /// </summary>
        MultiTextureMeshRef ShapeZoneMesh(PaintSplit sp, ShipDefinition def, string role, PaintPlan plan, int zone, string paint)
        {
            if (!plan.FaceZones.TryGetValue(role, out var faces)) return null;
            if (!sp.Shapes.TryGetValue(role, out var loaded))
            {
                string shapeLoc = role == "exterior" ? def.Shapes?.Exterior : def.Shapes?.Interior;
                if (shapeLoc == null) return null;
                var l = AssetLocation.Create(shapeLoc, "ships").WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                sp.Shapes[role] = loaded = (Shape.TryGet(capi, l), l.ToString());
                if (loaded.Shape != null) ShipRenderer.PrepareShape(capi, loaded.Shape, loaded.Loc);
            }
            if (loaded.Shape?.Elements == null || loaded.Shape.Textures == null) return null;
            // (a copy: its faces are the loaded shape's own objects, so a painted face is a new one)
            var shape = loaded.Shape.Clone();
            shape.Textures = new Dictionary<string, AssetLocation>(loaded.Shape.Textures);
            string loc = loaded.Loc;
            var keep = new List<ShapeElement>();
            var added = new HashSet<string>();
            foreach (var e in shape.Elements)
            {
                faces.TryGetValue(e.Name ?? "", out var zf);
                bool any = false;
                if (e.FacesResolved != null)
                    for (int f = 0; f < e.FacesResolved.Length; f++)
                    {
                        var face = e.FacesResolved[f];
                        if (face == null) continue;
                        int z = zf != null && f < zf.Length ? zf[f] : -1;
                        if (z != zone) { e.FacesResolved[f] = null; continue; }
                        any = true;
                        if (paint == null || !shape.Textures.TryGetValue(face.Texture ?? "", out var tl) || tl == null) continue;
                        string code = face.Texture + "-paint-" + paint;
                        if (added.Add(code)) shape.Textures[code] = AssetLocation.Create(ShipPaint.Painted(tl.ToString(), paint));
                        var copy = (ShapeElementFace)CloneFace.Invoke(face, null);
                        copy.Texture = code;
                        e.FacesResolved[f] = copy;
                    }
                if (any) keep.Add(e);
            }
            if (keep.Count == 0) return null;
            shape.Elements = keep.ToArray();
            capi.Tesselator.TesselateShape("ships ship paint", shape, out MeshData md, new ShapeTextureSource(capi, shape, loc + " (paint)"));
            return md != null && md.VerticesCount > 0 ? capi.Render.UploadMultiTextureMesh(md) : null;
        }

        static readonly System.Reflection.MethodInfo CloneFace =
            typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        void DisposePaint(long shipId)
        {
            if (paintLooks.TryGetValue(shipId, out var pl))
            {
                pl.Dispose();
                paintLooks.Remove(shipId);
            }
        }

        void DisposeAllPaint()
        {
            foreach (var pl in paintLooks.Values) pl.Dispose();
            paintLooks.Clear();
            foreach (var sp in paintSplits.Values) sp?.Dispose();
            paintSplits.Clear();
        }
    }
}
