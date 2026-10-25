using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// The pieces put aboard (ShipDecor), drawn among her vanilla blocks: each block of a piece (a bed's head and feet) from
    /// its own mesh as vanilla draws it (a typed container's or crate's by its type, a lantern's with its metal and glass),
    /// turned by its quarter turns about the block's middle, at its cell; one hung from a hook by its mesh's bounds; one on
    /// a wall tilted onto the wall's face. A light piece gives its block's light, as a lantern on a hook did. A block's
    /// see-through faces (a lantern's glass) are drawn with her glass.
    /// </summary>
    public partial class ShipRenderer
    {
        bool pieceWarned;

        /// <summary>Her pieces, added to her vanilla blocks when they're built again (her fittings' version).</summary>
        void AddPlacedPieces(EntityShip ship, Decor d)
        {
            foreach (var pc in ship.Pieces.All)
            {
                try
                {
                    // (0.3.20, step B) a hatch: its leaves, each turning about its own hinge
                    if (pc.Role == PieceRole.Hatch) { d.Pieces.AddRange(HatchPieces(ship, pc)); continue; }
                    var p = BuildPlacedPiece(ship, pc, out var seen);
                    if (p != null) d.Pieces.Add(p);
                    if (seen != null) d.Pieces.Add(seen);
                }
                catch (Exception e)
                {
                    if (!pieceWarned) capi.Logger.Warning("[hansa] Could not draw the piece {0} ({1}) on the {2}: {3}", pc.Id, pc.Stack?.Collectible?.Code, ship.Def.Code, e);
                    pieceWarned = true;
                }
            }
        }

        /// <param name="seen">its blocks' see-through faces (a lantern's glass), drawn with her glass; null if none</param>
        Piece BuildPlacedPiece(EntityShip ship, ShipPiece pc, out Piece seen)
        {
            seen = null;
            MeshData all = null, clear = null;
            bool glass = false;
            if (pc.Role is PieceRole.Panel or PieceRole.Partition) return PanelPiece(ship, pc);
            if (pc.Role == PieceRole.Door) return DoorPiece(ship, pc);
            // (0.3.16, notes 154-159) a window, a railing, a stair, a horizontal plank panel: built from boxes (ModularRenderer)
            if (pc.Role is PieceRole.Window or PieceRole.Railing or PieceRole.Ladder or PieceRole.Stair or PieceRole.Shelf) return ModularPiece(ship, pc);
            // (0.3.18, note 165) a hearth's firebed and a flue from boxes; the firepit on the hearth and the oven are drawn as
            // the design's are, from their stand-in fixtures (ShipRenderer.HearthPiece, OvenPiece)
            if (pc.Role is PieceRole.Hearth or PieceRole.Flue) return ModularPiece(ship, pc);
            if (pc.Role == PieceRole.Oven) return null;
            foreach (var part in pc.Parts)
            {
                var block = capi.World.GetBlock(new AssetLocation(part.Block));
                if (block == null || block.Id == 0) continue;
                // (0.3.12) a see-through block (a glass pane, a glass block: vanilla's transparent pass) is drawn with the
                // ship's glass, blended, its frame solid (it was drawn solid: a milky sheet you couldn't see through)
                if (block.RenderPass is EnumChunkRenderPass.Transparent or EnumChunkRenderPass.BlendNoCull) glass = true;
                var md = VanillaPieceBlock.IsOverlay(block) ? OverlayMesh(block) : PieceMesh(block, pc.Stack);
                if (md == null || md.VerticesCount == 0) continue;
                // ((0.1.0-dev.7, note 43: "lanterns does not have light") a block drawn solid with see-through faces in its
                // shape (vanilla's transparent pass: a lantern's glowing glass) has them drawn with her glass: drawn solid
                // they were black panes hiding the lit candle)
                MeshData see = null;
                if (!glass) md = SeeThroughFaces.Split(md, out see);
                // ((0.1.0-dev.9, note 78) a figurehead stands in its block on its place, turned as planned)
                if (pc.Kind != MountKind.Hook || pc.Hang == PiecePlanner.FigureheadHang)
                {
                    if (part.Quarter != 0)
                    {
                        md.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, part.Quarter * GameMath.PIHALF, 0);
                        see?.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, part.Quarter * GameMath.PIHALF, 0);
                    }
                    md.Translate((float)part.Origin[0], (float)part.Origin[1], (float)part.Origin[2]);
                    see?.Translate((float)part.Origin[0], (float)part.Origin[1], (float)part.Origin[2]);
                }
                if (md.VerticesCount > 0)
                {
                    if (all == null) all = md;
                    else all.AddMeshData(md);
                }
                if (see != null)
                {
                    if (clear == null) clear = see;
                    else clear.AddMeshData(see);
                }
            }
            // (all of it see-through: drawn as a pane is)
            if (all == null || all.VerticesCount == 0) { all = clear; clear = null; glass = true; }
            if (all == null || all.VerticesCount == 0) return null;
            var p = new Piece { Fixture = ship.PieceFixture(pc), Glass = glass };
            var c = pc.Center;
            p.Interior = ship.Def.RoomAt(c[0], c[1], c[2])?.Enclosed == true;
            Mat4f.Identity(p.Fit);
            if (pc.Kind == MountKind.Hook && pc.Parts.Count > 0 && pc.Hang != PiecePlanner.FigureheadHang)
            {
                // hung from the hook (or stood on it): the mesh's middle under it, its top (bottom) at it
                Bounds(all, out var min, out var max);
                if (clear != null)
                {
                    Bounds(clear, out var cmin, out var cmax);
                    min.Set(Math.Min(min.X, cmin.X), Math.Min(min.Y, cmin.Y), Math.Min(min.Z, cmin.Z));
                    max.Set(Math.Max(max.X, cmax.X), Math.Max(max.Y, cmax.Y), Math.Max(max.Z, cmax.Z));
                }
                var m = ship.Mounts?.Get(pc.Mount);
                double hx = m?.X ?? pc.Parts[0].Origin[0] + 0.5, hy = m?.Y ?? pc.Parts[0].Origin[1] + 1, hz = m?.Z ?? pc.Parts[0].Origin[2] + 0.5;
                float y = pc.Hang == "up" ? (float)hy - min.Y : (float)hy - max.Y;
                if (pc.Parts[0].Quarter != 0)
                {
                    all.Rotate(new Vec3f((min.X + max.X) / 2, 0, (min.Z + max.Z) / 2), 0, pc.Parts[0].Quarter * GameMath.PIHALF, 0);
                    clear?.Rotate(new Vec3f((min.X + max.X) / 2, 0, (min.Z + max.Z) / 2), 0, pc.Parts[0].Quarter * GameMath.PIHALF, 0);
                }
                float tx = (float)hx - (min.X + max.X) / 2, tz = (float)hz - (min.Z + max.Z) / 2;
                Mat4f.Translate(p.Fit, p.Fit, tx, y, tz);
                // (0.3.16, note 161) on a ceiling hook the mod found: the iron eye the design's ceiling hooks have (in the
                // mesh's own frame, which Fit moves by (tx, y, tz))
                if (pc.Hang != "up" && MountLayout.IsCeilingHook(pc.Mount)) { try { all.AddMeshData(HookEye(hx - tx, hy - y, hz - tz)); } catch (Exception) { } }
                // ((0.3.27, note 202: "only the original shows the special hook fitting") on a mast's side: its iron arm out
                // from the mast (and a band round it where the design's hook has none at that height), as the design's
                if (pc.Hang != "up" && MountLayout.IsMastHook(pc.Mount) && ship.Mounts?.MastOfHook(pc.Mount) is MastDef mast && m != null)
                {
                    try { all.AddMeshData(MastArm(hx - tx, hy - y, hz - tz, mast.X - tx, mast.Z - tz, mast.Radius, !ship.Mounts.MastBandAt(mast, m.Y))); }
                    catch (Exception) { }
                }
            }
            else if (pc.Kind == MountKind.Wall && pc.Tilt != null && pc.Pivot != null)
            {
                // tilted onto the wall's face where her side leans: from her level way into the room to the wall's normal
                double sx = MountLayout.DX[pc.Dir], sz = MountLayout.DZ[pc.Dir];
                double nx = pc.Tilt[0], ny = pc.Tilt[1], nz = pc.Tilt[2];
                // turned about s × n by the angle between them (s level, n the wall's)
                double ax = -sz * ny, ay = sz * nx - sx * nz, az = sx * ny;
                double sin = Math.Sqrt(ax * ax + ay * ay + az * az), cos = sx * nx + sz * nz;
                if (sin > 1e-4)
                {
                    float angle = (float)Math.Atan2(sin, cos);
                    var axis = new float[] { (float)(ax / sin), (float)(ay / sin), (float)(az / sin) };
                    float px = (float)pc.Pivot[0], py = (float)pc.Pivot[1], pz = (float)pc.Pivot[2];
                    Mat4f.Translate(p.Fit, p.Fit, px, py, pz);
                    Mat4f.Rotate(p.Fit, p.Fit, angle, axis);
                    Mat4f.Translate(p.Fit, p.Fit, -px, -py, -pz);
                }
            }
            p.Mesh = capi.Render.UploadMultiTextureMesh(all);
            // ((0.1.0-dev.9, note 66: "lanters does not show light") a lantern drawn in its own light, its glass glowing)
            p.Own = SeeThroughFaces.OwnLight(PieceHsv(capi.World, pc));
            if (clear != null && clear.VerticesCount > 0)
            {
                seen = new Piece { Fixture = p.Fixture, Glass = true, Interior = p.Interior, Mesh = capi.Render.UploadMultiTextureMesh(clear), Own = p.Own, OwnAlpha = p.Own != null };
                Array.Copy(p.Fit, seen.Fit, 16);
            }
            return p;
        }

        /// <summary>A block's mesh in its block (0..1), as vanilla draws it placed: a typed container or crate by its type (front +z), a block with a mesh of its own from the stack (a lantern's metal and glass), else its own shape.</summary>
        MeshData PieceMesh(Block block, ItemStack stack)
        {
            MeshData md = null;
            if (block is BlockGenericTypedContainer gtc)
            {
                string type = stack != null ? ShipFittings.ChestType(stack) : gtc.Attributes?["defaultType"].AsString("normal-generic") ?? "normal-generic";
                string shape = gtc.Attributes?["shape"][type].AsString();
                if (shape != null) md = gtc.GenMesh(capi, type, shape, null, new Vec3f());
            }
            else if (block is BlockTapestry tapestry && stack != null)
            {
                // a tapestry (or wall carving): its picture by its type, as its block entity draws it placed
                try { md = tapestry.genMesh(false, stack.Attributes.GetString("type", ""), 0)?.Clone(); }
                catch (Exception) { md = null; }
            }
            else if (block is BlockMaterialFromAttributes bma)
            {
                // ((0.1.0-dev.9, note 78) a vanilla figurehead: carved in the wood its stack names, as vanilla draws it)
                try { md = bma.GetOrCreateMesh(stack?.Attributes.GetString("material", "oak") ?? "oak")?.Clone(); }
                catch (Exception) { md = null; }
            }
            else if (block is BlockCrate crate)
            {
                string type = stack?.Attributes.GetString("type") ?? crate.Attributes?["defaultType"].AsString("wood-aged") ?? "wood-aged";
                if (crate.Shape != null) md = crate.GenMesh(capi, null, type, null, "closed", crate.Shape, new Vec3f());
            }
            if (md == null || md.VerticesCount == 0)
            {
                ItemStack s = null;
                if (stack != null)
                {
                    s = new ItemStack(block);
                    s.Attributes = stack.Attributes.Clone();
                }
                md = BlockMesh(block, s);
            }
            if (md == null || md.VerticesCount == 0)
            {
                capi.Tesselator.TesselateBlock(block, out md);
                md = md?.Clone();
            }
            if (md == null || md.VerticesCount == 0)
            {
                // never an invisible piece: a plain oak box if the block can't be drawn
                md = PlankSlab(0.8f, 0.8f, 0.8f);
                md.Translate(0.1f, 0, 0.1f);
            }
            return md;
        }

        /// <summary>
        /// A panel: the planks block's own cube (its texture, as vanilla draws it) squeezed into the wedge it fills, its face
        /// on the cell's border into the room.
        /// </summary>
        Piece PanelPiece(EntityShip ship, ShipPiece pc)
        {
            var block = pc.Parts.Count > 0 ? capi.World.GetBlock(new AssetLocation(pc.Parts[0].Block)) : pc.Stack?.Block;
            // (a partition: its thin box on the line; a panel: the wedge it fills)
            var box = pc.Role == PieceRole.Partition ? pc.Box : pc.Solids.Count > 0 ? pc.Solids[0] : pc.Box;
            if (box == null) return null;
            var md = BlockMesh(block, null);
            if (md == null || md.VerticesCount == 0) md = PlankSlab(1, 1, 1);
            // (0.3.12) painted as her own surfaces are; ((0.3.28) cracked by a shot: the damage overlay on it)
            md = PaintedMesh(md, block, pc.Paint, PieceOverlay(ship, pc));
            // (0.3.13, note 137) a partition on a place only partly free of her (under her beams, against her curved side):
            // the planks' cube laid on the place's whole block, then cut to the free parts, so it meets her timbers with
            // its texture where a whole one's would be
            var mount = pc.Role == PieceRole.Partition ? ship.Mounts?.Get(pc.Mount) : null;
            // (0.3.14, notes 148, 149) drawn out past her cells to her real surfaces: a beam's sides, her ceiling planking,
            // possibly past the place's block (where her cells go on there): each part cut from the planks' cube laid on the
            // block it's in, so the grain runs on as a whole wall's would
            var rects = mount == null ? null : (ship.Def.Surfaces != null ? ship.Mounts.DrawnClip(mount, ship.Def.Surfaces) : null) ?? mount.Clip;
            if (rects != null)
            {
                // (0.3.18, notes 164, 166) its parts at a corner with another wall: the corner's square drawn once
                if (pc.Role == PieceRole.Partition)
                {
                    var (clo, chi) = ModularGeometry.WallEnds(ship.Mounts, ship.Pieces.All, pc);
                    if (clo != 0 || chi != 0) rects = ModularGeometry.AdjustClip(mount, rects, clo, chi);
                }
                var whole = PiecePlanner.BorderBlock(mount);
                var (_, alongX) = MountLayout.BorderLine(mount);
                double along0 = alongX ? whole[0] : whole[2];
                var laid = new System.Collections.Generic.Dictionary<(int, int), MeshData>();
                MeshData cut = null;
                foreach (var c in rects)
                    for (int da = (int)Math.Floor(c[0] - along0 + 1e-6); da < c[2] - along0 - 1e-6; da++)
                        for (int dy = (int)Math.Floor(c[1] - whole[1] + 1e-6); dy < c[3] - whole[1] - 1e-6; dy++)
                        {
                            double a0 = Math.Max(c[0], along0 + da), a1 = Math.Min(c[2], along0 + da + 1);
                            double y0 = Math.Max(c[1], whole[1] + dy), y1 = Math.Min(c[3], whole[1] + dy + 1);
                            if (a1 - a0 < 1e-6 || y1 - y0 < 1e-6) continue;
                            if (!laid.TryGetValue((da, dy), out var block1))
                            {
                                block1 = md.Clone();
                                double ox = whole[0] + (alongX ? da : 0), oz = whole[2] + (alongX ? 0 : da), oy = whole[1] + dy;
                                block1.MatrixTransform(new Matrixf().Identity().Translate((float)ox, (float)oy, (float)oz)
                                    .Scale((float)(whole[3] - whole[0]), (float)(whole[4] - whole[1]), (float)(whole[5] - whole[2])).Values);
                                laid[(da, dy)] = block1;
                            }
                            var part = ClipMesh(block1, PiecePlanner.ClipBox(mount, new[] { a0, y0, a1, y1 }, MountLayout.PartitionThickness / 2));
                            if (cut == null) cut = part; else cut.AddMeshData(part);
                        }
                if (cut == null || cut.VerticesCount == 0) return null;
                var pp = new Piece { Fixture = ship.PieceFixture(pc), Interior = true };
                Mat4f.Identity(pp.Fit);
                pp.Mesh = capi.Render.UploadMultiTextureMesh(cut);
                return pp;
            }
            // (0.3.18, notes 164, 166) at a corner with another wall the corner's square is drawn once (ModularGeometry.WallEnds)
            if (pc.Role == PieceRole.Partition && mount != null)
            {
                var (lo, hi) = ModularGeometry.WallEnds(ship.Mounts, ship.Pieces.All, pc);
                if (lo != 0 || hi != 0)
                {
                    box = (double[])box.Clone();
                    int ax = MountLayout.BorderLine(mount).AlongX ? 0 : 2;
                    box[ax] -= lo;
                    box[ax + 3] += hi;
                }
            }
            md.MatrixTransform(new Matrixf().Identity().Translate((float)box[0], (float)box[1], (float)box[2])
                .Scale((float)(box[3] - box[0]), (float)(box[4] - box[1]), (float)(box[5] - box[2])).Values);
            var p = new Piece { Fixture = ship.PieceFixture(pc), Interior = true };
            Mat4f.Identity(p.Fit);
            p.Mesh = capi.Render.UploadMultiTextureMesh(md);
            return p;
        }

        /// <summary>
        /// (0.3.13, note 137) A box mesh (a block's cube, laid where it goes) cut to a smaller box inside it: every vertex
        /// moved into the box, each face's texture coordinates moved with it along the face (a face's are linear in its
        /// plane), so what's left shows the part of the texture that was there. Faces of 4 vertices, as the tesselator makes
        /// a cube.
        /// </summary>
        public static MeshData ClipMesh(MeshData src, double[] box)
        {
            var md = src.Clone();
            int n = md.VerticesCount;
            var xyz = md.xyz;
            var uv = md.Uv;
            for (int q = 0; q + 3 < n; q += 4)
            {
                // the face's plane: the axis it doesn't run along
                int normal = 0;
                double least = double.MaxValue;
                for (int ax = 0; ax < 3; ax++)
                {
                    double lo = double.MaxValue, hi = double.MinValue;
                    for (int v = q; v < q + 4; v++) { lo = Math.Min(lo, xyz[v * 3 + ax]); hi = Math.Max(hi, xyz[v * 3 + ax]); }
                    if (hi - lo < least) { least = hi - lo; normal = ax; }
                }
                int pa = normal == 0 ? 1 : 0, pb = normal == 2 ? 1 : 2;
                // u, v as linear in (a, b) from three of its corners
                int v0 = q, v1 = -1, v2 = -1;
                for (int v = q + 1; v < q + 4 && (v1 < 0 || v2 < 0); v++)
                {
                    bool sameA = Math.Abs(xyz[v * 3 + pa] - xyz[v0 * 3 + pa]) < 1e-6, sameB = Math.Abs(xyz[v * 3 + pb] - xyz[v0 * 3 + pb]) < 1e-6;
                    if (sameB && !sameA && v1 < 0) v1 = v;          // along a
                    else if (sameA && !sameB && v2 < 0) v2 = v;     // along b
                }
                double dua = 0, dva = 0, dub = 0, dvb = 0;
                if (v1 >= 0)
                {
                    double da = xyz[v1 * 3 + pa] - xyz[v0 * 3 + pa];
                    dua = (uv[v1 * 2] - uv[v0 * 2]) / da; dva = (uv[v1 * 2 + 1] - uv[v0 * 2 + 1]) / da;
                }
                if (v2 >= 0)
                {
                    double db = xyz[v2 * 3 + pb] - xyz[v0 * 3 + pb];
                    dub = (uv[v2 * 2] - uv[v0 * 2]) / db; dvb = (uv[v2 * 2 + 1] - uv[v0 * 2 + 1]) / db;
                }
                double a0 = xyz[v0 * 3 + pa], b0 = xyz[v0 * 3 + pb], u0 = uv[v0 * 2], w0 = uv[v0 * 2 + 1];
                for (int v = q; v < q + 4; v++)
                {
                    for (int ax = 0; ax < 3; ax++)
                        xyz[v * 3 + ax] = (float)Math.Clamp(xyz[v * 3 + ax], box[ax], box[ax + 3]);
                    double a = xyz[v * 3 + pa] - a0, b = xyz[v * 3 + pb] - b0;
                    uv[v * 2] = (float)(u0 + dua * a + dub * b);
                    uv[v * 2 + 1] = (float)(w0 + dva * a + dvb * b);
                }
            }
            return md;
        }

        /// <summary>
        /// A door put aboard (on a cell's border): its block's mesh as vanilla draws it shut, laid along the border (its long
        /// side along the line, its thin side across it, a block wide and two high), hung on the border's lower end
        /// (<see cref="ShipPiece.Pivot"/>); open, swung a quarter turn about that end into the cell it opens into.
        /// </summary>
        Piece DoorPiece(EntityShip ship, ShipPiece pc)
        {
            var block = pc.Parts.Count > 0 ? capi.World.GetBlock(new AssetLocation(pc.Parts[0].Block)) : pc.Stack?.Block;
            if (block == null || pc.Pivot == null || pc.Box == null) return null;
            var md = BlockMesh(block, null);
            if (md == null || md.VerticesCount == 0) md = PlankSlab(1, 2, 0.1875f);
            Bounds(md, out var min, out var max);
            float ex = Math.Max(1e-3f, max.X - min.X), ey = Math.Max(1e-3f, max.Y - min.Y), ez = Math.Max(1e-3f, max.Z - min.Z);
            // its foot at y 0, its long side from 0 at the hinge, its thin side centred on 0
            bool longZ = ez > ex;
            md.Translate(longZ ? -(min.X + max.X) / 2 : -min.X, -min.Y, longZ ? -min.Z : -(min.Z + max.Z) / 2);
            float len = longZ ? ez : ex;
            bool alongX = pc.Box[3] - pc.Box[0] > pc.Box[5] - pc.Box[2];
            // the long side along the line: about y, a quarter turn if it lies the other way (x' = z, z' = -x for +90°)
            if (alongX && longZ) md.Rotate(new Vec3f(0, 0, 0), 0, GameMath.PIHALF, 0);
            else if (!alongX && !longZ) md.Rotate(new Vec3f(0, 0, 0), 0, -GameMath.PIHALF, 0);
            float rows = (float)(pc.Box[4] - pc.Box[1]);
            md.Scale(new Vec3f(0, 0, 0), alongX ? 1 / len : 1, rows / ey, alongX ? 1 : 1 / len);
            md = PaintedMesh(md, block, pc.Paint, PieceOverlay(ship, pc));
            md.Translate((float)pc.Pivot[0], (float)pc.Pivot[1], (float)pc.Pivot[2]);
            // open: into the cell it opens into (Dir), the leaf (1, 0, 0) or (0, 0, 1) turned a quarter to point that way;
            // (0.3.12, note 123) drawn shut and swung about the hinge as it opens and shuts, as the design's doors are
            double ix = MountLayout.DX[pc.Dir], iz = MountLayout.DZ[pc.Dir];
            // (0.3.18, note 164: "an open door against another wall flickers") it stops a few degrees short of square, so its
            // open leaf never lies in the plane of a wall beside it on the line it opens along
            float a = (alongX ? (float)(-iz * GameMath.PIHALF) : (float)(ix * GameMath.PIHALF)) * ModularGeometry.DoorOpenFraction;
            var p = new Piece { Fixture = ship.PieceFixture(pc), Interior = true, DoorId = pc.Id, HingeX = (float)pc.Pivot[0], HingeZ = (float)pc.Pivot[2], Swing = a };
            Mat4f.Identity(p.Fit);
            p.Mesh = capi.Render.UploadMultiTextureMesh(md);
            return p;
        }

        /// <summary>
        /// (0.3.12, the user: placed walls can't be painted.) A wall piece's mesh in its paint: each of its block's textures
        /// (and the plain oak board's, the stand-in) swapped in its UVs for the paint's own, as her zones are painted
        /// (<see cref="ShipPaint.Painted"/>: a grey grain times the dye; any wood's planks take oak's plank grain, anything
        /// else the timber grain). No paint, or nothing to swap: the mesh as it is.
        /// </summary>
        MeshData PaintedMesh(MeshData md, Block block, string paint, string overlay = null)
        {
            if (paint == null && overlay == null || md == null || md.Uv == null) return md;
            var atlas = capi.BlockTextureAtlas;
            var map = new System.Collections.Generic.List<(TextureAtlasPosition From, TextureAtlasPosition To)>();
            void Add(TextureAtlasPosition from, string name)
            {
                if (from == null || name == null) return;
                // (any wood's planks: oak's plank grain, the same plank; (0.3.28) cracked: the damage overlay over it)
                // ((0.1.0-dev.8, note 71) by its baked name, turned as the block's own: vanilla's planks laid up-down turn
                // their grain a quarter, and a partition's paint ran across it)
                name = ShipPaint.PaintedBaked(name, paint, overlay);
                atlas.GetOrInsertTexture(AssetLocation.Create(name), out _, out var to);
                if (to != null) map.Add((from, to));
            }
            if (block?.Textures != null)
                foreach (var ct in block.Textures.Values)
                {
                    // (each of its variants: oak1..oak4 are the planks' alternates, each baked with the turn)
                    var baked = ct?.Baked;
                    if (baked == null) continue;
                    foreach (var b in baked.BakedVariants ?? new[] { baked })
                    {
                        int sub = b?.TextureSubId ?? -1;
                        if (sub >= 0 && sub < atlas.Positions.Length) Add(atlas.Positions[sub], (b.BakedName ?? ct.Base)?.ToString());
                    }
                }
            atlas.GetOrInsertTexture(new AssetLocation("game", "block/wood/planks/oak1"), out _, out var oak);
            Add(oak, "game:block/wood/planks/oak1");
            if (map.Count == 0) return md;
            int vpf = Math.Max(1, md.VerticesPerFace);
            for (int v = 0; v < md.VerticesCount; v++)
            {
                float u = md.Uv[2 * v], w = md.Uv[2 * v + 1];
                foreach (var (from, to) in map)
                {
                    const float e = 1e-5f;
                    if (u < from.x1 - e || u > from.x2 + e || w < from.y1 - e || w > from.y2 + e) continue;
                    md.Uv[2 * v] = to.x1 + (u - from.x1) / Math.Max(1e-9f, from.x2 - from.x1) * (to.x2 - to.x1);
                    md.Uv[2 * v + 1] = to.y1 + (w - from.y1) / Math.Max(1e-9f, from.y2 - from.y1) * (to.y2 - to.y1);
                    // (the face's atlas page: the paint's may be another)
                    int face = v / vpf;
                    if (to.atlasTextureId != from.atlasTextureId && md.TextureIndices != null && face < md.TextureIndicesCount)
                    {
                        int idx = Array.IndexOf(md.TextureIds, to.atlasTextureId);
                        if (idx < 0)
                        {
                            var ids = md.TextureIds;
                            idx = ids.Length;
                            Array.Resize(ref ids, idx + 1);
                            ids[idx] = to.atlasTextureId;
                            md.TextureIds = ids;
                        }
                        md.TextureIndices[face] = (byte)idx;
                    }
                    break;
                }
            }
            return md;
        }

        /// <summary>
        /// (0.3.28, step D2) A piece's damage overlay (a partition, a door, a window, a railing cracked by a shot: the hull's
        /// cracked look, two of them by its id), or null.
        /// </summary>
        static string PieceOverlay(EntityShip ship, ShipPiece pc)
        {
            var d = ship?.DamageParts?.D;
            if (d == null || !ship.DamageOn || pc == null || !PieceDamage.Takes(pc) || pc.Hurt < d.Cracked) return null;
            var c = pc.Center;
            bool inside = ship.Def.RoomAt(c[0], c[1], c[2])?.Enclosed == true;
            return HullLook.Overlay(HullLook.Cracked, inside, pc.Id & 1);
        }

        /// <summary>
        /// A layer on a wall (vanilla's wallpaper): its texture on a square a hair off the wall's face, the back of its
        /// block (unturned, -z: the piece's quarter turns put that on the wall), facing into the room.
        /// </summary>
        MeshData OverlayMesh(Block block)
        {
            var src = capi.Tesselator.GetTextureSource(block);
            TextureAtlasPosition tp = null;
            foreach (string key in new[] { "south", "north", "all" })
            {
                try { tp = src[key]; } catch (Exception) { tp = null; }
                if (tp != null) break;
            }
            if (tp == null && block.Textures != null)
                foreach (var k in block.Textures.Keys) { try { tp = src[k]; } catch (Exception) { } if (tp != null) break; }
            tp ??= capi.BlockTextureAtlas.UnknownTexturePosition;
            var md = new MeshData(4, 6, false, true, true, true);
            int flags = VertexFlags.PackNormal(0, 0, 1);
            const float z = 0.006f;
            md.AddWithFlagsVertex(0, 0, z, tp.x1, tp.y2, -1, flags);
            md.AddWithFlagsVertex(1, 0, z, tp.x2, tp.y2, -1, flags);
            md.AddWithFlagsVertex(1, 1, z, tp.x2, tp.y1, -1, flags);
            md.AddWithFlagsVertex(0, 1, z, tp.x1, tp.y1, -1, flags);
            md.AddIndices(0, 1, 2, 0, 2, 3);
            md.AddTextureId(tp.atlasTextureId);
            return md;
        }

        /// <summary>Her light pieces' lights (as a lantern on a hook gave), into her lights.</summary>
        void AddPieceLights(EntityShip ship, System.Collections.Generic.List<ShipLight> list)
        {
            foreach (var pc in ship.Pieces.All)
                if (PieceLight(capi.World, pc, out var local, out var on)) list.Add(new ShipLight { Local = local, On = on });
        }

        /// <summary>
        /// A piece's light, if its block gives light (a lantern, a candle, a torch holder; not only those put as lights):
        /// where it is (ship-local; a hung one's a little under its top) and its colour on the lanterns' scale (blue, green,
        /// red, as the game's point lights have it). Either side (the self-test checks it on the server).
        /// </summary>
        public static bool PieceLight(IWorldAccessor world, ShipPiece pc, out double[] local, out Vec3f on)
        {
            local = null;
            on = null;
            var hsv = PieceHsv(world, pc);
            if (hsv == null) return false;
            int rgb = ColorUtil.HsvToRgb(hsv[0] * 4, hsv[1] * 32, 255);
            float r = ((rgb >> 16) & 255) / 255f, g = ((rgb >> 8) & 255) / 255f, b = (rgb & 255) / 255f;
            float strength = Math.Max(4, hsv[2] * 0.55f);
            var c = pc.Center;
            double y = pc.Hang == "down" ? (pc.Box?[1] ?? c[1]) + 0.3 : c[1];
            local = new[] { c[0], y, c[2] };
            on = new Vec3f(b * strength, g * strength, r * strength);
            return true;
        }

        /// <summary>The light a piece's block gives (vanilla's hue, saturation, level; a lantern's by its glass and lining), or null: none.</summary>
        static byte[] PieceHsv(IWorldAccessor world, ShipPiece pc)
        {
            if (pc.Role == PieceRole.Panel || pc.Stack?.Block == null || pc.Parts.Count == 0) return null;
            var block = world.GetBlock(new AssetLocation(pc.Parts[0].Block)) ?? pc.Stack.Block;
            byte[] hsv;
            try { hsv = block.GetLightHsv(world.BlockAccessor, null, pc.Stack) ?? pc.Stack.Block.GetLightHsv(world.BlockAccessor, null, pc.Stack); }
            catch (Exception) { hsv = block.LightHsv; }
            return hsv == null || hsv.Length < 3 || hsv[2] == 0 ? null : hsv;
        }
    }
}
