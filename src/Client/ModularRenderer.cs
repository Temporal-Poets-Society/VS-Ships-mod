using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.3.16, notes 154–161; 0.3.18, notes 162–166) The modular pieces drawn from their parts (<see cref="ModularGeometry"/>):
    /// a window (four lights in a mullion and transom, each leaded two by two, the ship's clear glass), a railing (posts,
    /// one shared at a corner, balusters, a handrail and a foot rail; raked beside a stair), a companion ladder-stair (two
    /// stringers, flat treads, iron shoes and hooks), a horizontal plank panel, a hearth's brick firebed and its flue, and
    /// the iron eye of a lantern on a ceiling hook the mod found. Each from boxes (sheared along a rail's or a stringer's
    /// slope), in the piece's wood (the block's: oak, pine...) or its paint.
    /// </summary>
    public partial class ShipRenderer
    {
        Piece OpeningFramePiece(EntityShip ship)
        {
            if(ship.OpeningFrameParts.Count==0) return null;
            var tex=AtlasTex("game:block/wood/debarked/oak",null);
            var md=new MeshData(64,96,false,true,true,true);
            foreach(var q in OpeningFrames.Mesh(ship.Def,ship.OpeningFrameParts)) {
                int start=md.VerticesCount,flags=VertexFlags.PackNormal(q[6],q[7],q[8]);
                for(int k=0;k<4;k++) md.AddWithFlagsVertex(q[1+8*k],q[2+8*k],q[3+8*k],tex.x1+(tex.x2-tex.x1)*q[4+8*k]/16,tex.y1+(tex.y2-tex.y1)*q[5+8*k]/16,-1,flags);
                md.AddIndices(start,start+1,start+2,start,start+2,start+3);md.AddTextureId(tex.atlasTextureId);
            }
            var p=new Piece {Fixture=new FixtureDef {Type="piece",Code="opening-frames"},Interior=true};
            Mat4f.Identity(p.Fit);p.Mesh=capi.Render.UploadMultiTextureMesh(md);return p;
        }
        TextureAtlasPosition AtlasTex(string name, string fallback)
        {
            TextureAtlasPosition tp = null;
            try { capi.BlockTextureAtlas.GetOrInsertTexture(AssetLocation.Create(name), out _, out tp); }
            catch (Exception) { tp = null; }
            if ((tp == null || tp == capi.BlockTextureAtlas.UnknownTexturePosition) && fallback != null && fallback != name) return AtlasTex(fallback, null);
            return tp ?? capi.BlockTextureAtlas.UnknownTexturePosition;
        }

        /// <summary>
        /// A modular piece's mesh (ship-local), or null: its parts (<see cref="ModularGeometry"/>) in the piece's wood (the
        /// block's: oak, pine...) or its paint, the ship's glass, lead, iron, the bricks of a hearth or a flue.
        /// </summary>
        Piece ModularPiece(EntityShip ship, ShipPiece pc,IEnumerable<ModularPart> nativeContacts=null)
        {
            string code = pc.Parts.Count > 0 ? pc.Parts[0].Block : pc.Stack?.Block?.Code?.ToString();
            string wood = VanillaPieceBlock.WoodOf(code);
            var m = ship.Mounts?.Get(pc.Mount);
            if (m == null) return null;
            List<ModularPart> parts;
            switch (pc.Role)
            {
                case PieceRole.Partition:
                    parts=ModularGeometry.Partition(ship.Mounts,ship.Def.Surfaces,ship.Pieces.All,pc);
                    break;
                case PieceRole.Window:
                {
                    var (lo, hi) = ModularGeometry.WallEnds(ship.Mounts, ship.Pieces.All, pc);
                    parts = ModularGeometry.Window(m, lo, hi);
                    // ((25 October 2026, note 90) on a place only partly free of her: cut to its free parts, as a partition)
                    if (m.Clip != null) parts = ModularGeometry.ClipToPlace(parts, m, m.Clip);
                    break;
                }
                case PieceRole.Railing:
                {
                    // (0.3.18, note 166) its ends: one post at a corner for all the railings meeting there, none at a wall
                    var (line, alongX) = MountLayout.BorderLine(m);
                    double a0 = (alongX ? m.X : m.Z) - 0.5, a1 = a0 + 1;
                    if (m.Clip != null && pc.Box != null && pc.Rake == null) { a0 = alongX ? pc.Box[0] : pc.Box[2]; a1 = alongX ? pc.Box[3] : pc.Box[5]; }
                    var e0 = ModularGeometry.RailingEnd(ship.Mounts, ship.Pieces.All, pc, alongX ? a0 : line, alongX ? line : a0, out double s0);
                    var e1 = ModularGeometry.RailingEnd(ship.Mounts, ship.Pieces.All, pc, alongX ? a1 : line, alongX ? line : a1, out double s1);
                    parts = ModularGeometry.Railing(m, pc.Rake, pc.Box, e0, s0, e1, s1, ModularGeometry.RailingCap(ship.Mounts, ship.Pieces, pc));
                    break;
                }
                case PieceRole.Ladder:
                    parts = ModularGeometry.Ladder(m, pc.Dir, pc.Rise > 0 ? pc.Rise : 3);
                    // (0.3.19) its guard rails where a side borders a drop
                    parts.AddRange(ModularGeometry.LadderGuardParts(m, pc.Dir, pc.Solids.Skip(PiecePlanner.LadderSegments)));
                    break;
                case PieceRole.Stair:
                    // ((0.3.23) a ship's stair: its length, its own handrails)
                    // ((0.1.0-dev.6, note 39) cut at its deck's underside)
                    parts = ModularGeometry.Flight(m, pc.Dir, pc.Rise > 0 ? pc.Rise : 3, PiecePlanner.RunOf(pc), pc.Rails, ModularGeometry.DeckThickness(ship.Def, m.Y));
                    break;
                case PieceRole.Shelf when pc.Box != null:
                    parts = new List<ModularPart> { ModularPart.B(PartMat.Planks, pc.Box) };
                    break;
                case PieceRole.Hearth:
                    parts = ModularGeometry.Hearth(m, pc.Dir);
                    break;
                case PieceRole.Flue:
                    parts = ModularGeometry.Flue(m.Box, ship.Pieces.At(m.Under)?.Role == PieceRole.Hearth);
                    break;
                default:
                    return null;
            }
            // (painted: any wood's planks take oak's plank grain under the dye, as her zones and the partitions do)
            // ((0.3.28, step D2) cracked by a shot: the damage overlay over its wood)
            string dmg = PieceOverlay(ship, pc) is string ov ? "++0~hansa:block/" + ov : "";
            var planks = AtlasTex((pc.Paint != null ? ShipPaint.Painted("game:block/wood/planks/oak1", pc.Paint) : $"game:block/wood/planks/{wood}1") + dmg, "game:block/wood/planks/oak1");
            var timber = AtlasTex((pc.Paint != null ? ShipPaint.Painted("game:block/wood/debarked/oak", pc.Paint) : $"game:block/wood/debarked/{wood}") + dmg, "game:block/wood/debarked/oak");
            TextureAtlasPosition Tex(PartMat mat) => mat switch
            {
                PartMat.Planks => planks,
                PartMat.Glass => AtlasTex("hansa:block/glass-clear", "game:block/glass/plain"),
                PartMat.Lead => AtlasTex("game:block/metal/plate/lead", "game:block/metal/plate/iron"),
                PartMat.Iron => AtlasTex("game:block/metal/plate/iron", null),
                PartMat.Brick => AtlasTex(BrickTexture(code), "game:block/clay/brick/four/running/red1"),
                PartMat.Soot => AtlasTex("game:block/coal/bituminous", "game:block/metal/plate/iron"),
                _ => timber
            };
            var md = new MeshData(64, 96, false, true, true, true);
            if(pc.Role is PieceRole.Stair or PieceRole.Ladder or PieceRole.Hearth or PieceRole.Partition)
                foreach(var q in ModularMesh.Quads(parts,ModularGeometry.Contacts(ship.Mounts,ship.Def.Surfaces,ship.Pieces,pc).Concat(nativeContacts??Enumerable.Empty<ModularPart>()),pc.Role!=PieceRole.Partition))
                {
                    var tex=Tex((PartMat)(int)q[0]);int start=md.VerticesCount;
                    int flags=VertexFlags.PackNormal(q[6],q[7],q[8]);
                    for(int k=0;k<4;k++) md.AddWithFlagsVertex(q[1+8*k],q[2+8*k],q[3+8*k],
                        tex.x1+(tex.x2-tex.x1)*q[4+8*k]/16,tex.y1+(tex.y2-tex.y1)*q[5+8*k]/16,-1,flags);
                    md.AddIndices(start,start+1,start+2,start,start+2,start+3);md.AddTextureId(tex.atlasTextureId);
                }
            else foreach (var part in parts)
            {
                if (part.IsBeam) Beam(md, Tex(part.Mat), part.P0, part.P1, part.AlongX, part.W, part.H);
                else AddBox(md, Tex(part.Mat), part.Box[0], part.Box[1], part.Box[2], part.Box[3], part.Box[4], part.Box[5]);
            }
            if (md.VerticesCount == 0) return null;
            var c = pc.Center;
            // (a hearth's firebed isn't its stand-in fixture: that's the firepit on it, built again as it burns)
            var fixture = pc.Role is PieceRole.Hearth or PieceRole.Oven ? new FixtureDef { Type = "piece", Code = pc.StorageCode + "-bed" } : ship.PieceFixture(pc);
            var p = new Piece { Fixture = fixture, Glass = pc.Role == PieceRole.Window, Interior = ship.Def.RoomAt(c[0], c[1], c[2])?.Enclosed == true };
            Mat4f.Identity(p.Fit);
            p.Mesh = capi.Render.UploadMultiTextureMesh(md);
            return p;
        }

        /// <summary>
        /// (0.3.20, step B; docs/slots.md §5) A hatch's leaves, each a piece of its own drawn shut and turned about its hinge
        /// as the hatch opens (eased, as a door's swing): a cargo hatch's plank leaves in the trapdoors' wood, a glazed one's
        /// frames with the ship's glass (drawn with her glass).
        /// </summary>
        List<Piece> HatchPieces(EntityShip ship, ShipPiece pc)
        {
            var list = new List<Piece>();
            if (pc.Box == null) return list;
            string code = pc.Stack?.Collectible?.Code?.ToString();
            bool glazed = VanillaPieceBlock.IsGlazedTrapdoor(code);
            // (a trapdoor's wood: trapdoor-solid-oak-1)
            var parts = code?.Split('-');
            string wood = parts != null && parts.Length > 2 ? parts[2] : "oak";
            var planks = AtlasTex($"game:block/wood/planks/{wood}1", "game:block/wood/planks/oak1");
            var timber = AtlasTex($"game:block/wood/debarked/{wood}", "game:block/wood/debarked/oak");
            TextureAtlasPosition Tex(PartMat mat) => mat switch
            {
                PartMat.Planks => planks,
                PartMat.Glass => AtlasTex("hansa:block/glass-clear", "game:block/glass/plain"),
                PartMat.Lead => AtlasTex("game:block/metal/plate/lead", "game:block/metal/plate/iron"),
                PartMat.Iron => AtlasTex("game:block/metal/plate/iron", null),
                _ => timber
            };
            var c = pc.Center;
            bool interior = ship.Def.RoomAt(c[0], c[1] - 0.5, c[2])?.Enclosed == true && ship.Def.RoomAt(c[0], c[1] + 0.5, c[2])?.Enclosed == true;
            // ((0.3.30, note 241: "textures are weird on hatch") each leaf laid with the trapdoor's own block, as vanilla draws
            // it, a tile about a block square (a 3 x 1.5 leaf: 3 x 2 tiles): its frame, boards and handle at their scale, not
            // one plank texture stretched over the whole leaf)
            var tdBlock = pc.Stack?.Block ?? (code != null ? capi.World.GetBlock(new AssetLocation(code)) : null);
            var tile = VanillaPieceBlock.IsTrapdoor(tdBlock) ? BlockMesh(tdBlock, null) : null;
            float tileH = 0.1875f;
            if (tile != null && tile.VerticesCount > 0)
            {
                Bounds(tile, out var tmin, out var tmax);
                tile.Translate(-tmin.X, -tmin.Y, -tmin.Z);
                float tx = Math.Max(1e-3f, tmax.X - tmin.X), tz = Math.Max(1e-3f, tmax.Z - tmin.Z);
                tileH = Math.Max(1e-3f, tmax.Y - tmin.Y);
                tile.Scale(new Vec3f(0, 0, 0), 1 / tx, 1, 1 / tz);
            }
            foreach (var (box, hinge, axis, sign) in HatchPlanner.Leaves(pc.Box, pc.Dir))
            {
                double lo = axis == 0 ? box[2] : box[0];
                bool hingeLow = Math.Abs((axis == 0 ? hinge[2] : hinge[0]) - lo) < 1e-6;
                MeshData md = null;
                if (tile != null && tile.VerticesCount > 0)
                {
                    double dx = box[3] - box[0], dz = box[5] - box[2], th = box[4] - box[1];
                    int nx = Math.Max(1, (int)Math.Round(dx)), nz = Math.Max(1, (int)Math.Round(dz));
                    float sx = (float)(dx / nx), sz = (float)(dz / nz), sy = (float)(th / tileH);
                    for (int i = 0; i < nx; i++)
                        for (int k = 0; k < nz; k++)
                        {
                            var t = tile.Clone();
                            t.Scale(new Vec3f(0, 0, 0), sx, sy, sz);
                            t.Translate((float)(box[0] + i * sx), (float)box[1], (float)(box[2] + k * sz));
                            if (md == null) md = t;
                            else md.AddMeshData(t);
                        }
                }
                else
                {
                    md = new MeshData(64, 96, false, true, true, true);
                    foreach (var part in ModularGeometry.HatchLeaf(box, axis, hingeLow, glazed))
                        AddBox(md, Tex(part.Mat), part.Box[0], part.Box[1], part.Box[2], part.Box[3], part.Box[4], part.Box[5]);
                }
                if (md == null || md.VerticesCount == 0) continue;
                var p = new Piece
                {
                    Fixture = ship.PieceFixture(pc), Glass = glazed, Interior = interior, HatchId = pc.Id, HatchAxis = axis,
                    HingeX = (float)hinge[0], HingeY = (float)hinge[1], HingeZ = (float)hinge[2], Swing = sign * HatchPlanner.OpenAngle * GameMath.DEG2RAD
                };
                Mat4f.Identity(p.Fit);
                p.Mesh = capi.Render.UploadMultiTextureMesh(md);
                list.Add(p);
            }
            return list;
        }

        /// <summary>(0.3.18) The bricks a hearth or a flue is drawn in: fire bricks' own, a brick chimney's colour (claybrickchimney-four-red-ns).</summary>
        static string BrickTexture(string code)
        {
            if (code == null) return "game:block/clay/brick/four/running/fire1";
            string path = code.Contains(':') ? code.Substring(code.IndexOf(':') + 1) : code;
            var parts = path.Split('-');
            if (parts[0] == "claybrickchimney" && parts.Length > 2) return $"game:block/clay/brick/four/running/{parts[2]}1";
            if (parts[0] == "claybricks" && parts.Length > 2) return $"game:block/clay/brick/four/running/{parts[2]}1";
            return "game:block/clay/brick/four/running/fire1";
        }

        /// <summary>(0.3.16, note 161) A ceiling hook's iron eye, as the design's (a plate in the deck's underside, a ring under it): at (x, y, z) in the mesh's frame.</summary>
        MeshData HookEye(double x, double y, double z)
        {
            var iron = AtlasTex("game:block/metal/plate/iron", null);
            var md = new MeshData(48, 72, false, true, true, true);
            AddBox(md, iron, x - 0.08, y - 0.024, z - 0.08, x + 0.08, y, z + 0.08);
            const double r = 0.04, t = 0.012;
            AddBox(md, iron, x - t, y - 0.024 - 2 * r, z - r, x + t, y - 0.024 - 2 * r + t, z + r);
            AddBox(md, iron, x - t, y - 0.024 - 2 * r, z - r, x + t, y - 0.024, z - r + t);
            AddBox(md, iron, x - t, y - 0.024 - 2 * r, z + r - t, x + t, y - 0.024, z + r);
            return md;
        }

        /// <summary>
        /// (0.3.27, note 202) A mast hook's fitting, as the design's mast hook (carrack.py): an iron arm 0.04 wide, 0.05 deep
        /// from the mast's face out to the hook, a short drop at its end the lantern hangs from (the hook at (x, y, z), the
        /// arm 0.06 over it), and with band a ring round the mast there. In the mesh's frame.
        /// </summary>
        MeshData MastArm(double x, double y, double z, double mx, double mz, double radius, bool band)
        {
            var iron = AtlasTex("game:block/metal/plate/iron", null);
            var md = new MeshData(160, 240, false, true, true, true);
            double ay = y + 0.06, dx = x - mx, dz = z - mz, len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) return md;
            double ux = dx / len, uz = dz / len, r = Math.Max(0.05, radius);
            // the arm, from just inside the mast's face (into the band) to the hook
            var a0 = new[] { mx + ux * (r - 0.01), ay, mz + uz * (r - 0.01) };
            var a1 = new[] { x + ux * 0.02, ay, z + uz * 0.02 };
            Beam(md, iron, a0, a1, Math.Abs(ux) > Math.Abs(uz), 0.04, 0.05);
            // the drop at its end (the lantern's ring hangs on it)
            AddBox(md, iron, x - 0.012, y, z - 0.012, x + 0.012, ay - 0.025, z + 0.012);
            if (band)
            {
                const int n = 16;
                double r0 = r + 0.005, r1 = r + 0.025;
                for (int k = 0; k < n; k++)
                {
                    double t0 = 2 * Math.PI * k / n, t1 = 2 * Math.PI * (k + 1) / n;
                    var c = new double[8][];
                    for (int q = 0; q < 8; q++)
                    {
                        double t = (q & 1) == 0 ? t0 : t1, rr = (q & 4) == 0 ? r0 : r1, yy = (q & 2) == 0 ? ay - 0.04 : ay + 0.04;
                        c[q] = new[] { mx + Math.Cos(t) * rr, yy, mz + Math.Sin(t) * rr };
                    }
                    AddHexa(md, iron, c);
                }
            }
            return md;
        }

        /// <summary>An axis-aligned box, each face's texture from its size (a block's worth at most, not stretched).</summary>
        static void AddBox(MeshData md, TextureAtlasPosition tp, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var c = new double[8][];
            for (int n = 0; n < 8; n++) c[n] = new[] { (n & 1) == 0 ? x0 : x1, (n & 2) == 0 ? y0 : y1, (n & 4) == 0 ? z0 : z1 };
            AddHexa(md, tp, c);
        }

        /// <summary>
        /// A beam from p0 to p1 (its end faces' middles), w wide across (level, square to it) and h high (upright): a box
        /// sheared along a slope (a raked handrail, a stringer), its end faces upright.
        /// </summary>
        static void Beam(MeshData md, TextureAtlasPosition tp, double[] p0, double[] p1, bool alongX, double w, double h)
        {
            var c = new double[8][];
            for (int n = 0; n < 8; n++)
            {
                var p = (n & 1) == 0 ? p0 : p1;
                double cy = (n & 2) == 0 ? -h / 2 : h / 2, cc = (n & 4) == 0 ? -w / 2 : w / 2;
                c[n] = alongX ? new[] { p[0], p[1] + cy, p[2] + cc } : new[] { p[0] + cc, p[1] + cy, p[2] };
            }
            AddHexa(md, tp, c);
        }

        /// <summary>A six-faced solid from its 8 corners (bit 0: x or along, bit 1: y, bit 2: z or across), faces facing out.</summary>
        static void AddHexa(MeshData md, TextureAtlasPosition tp, double[][] c)
        {
            double mx = 0, my = 0, mz = 0;
            foreach (var p in c) { mx += p[0] / 8; my += p[1] / 8; mz += p[2] / 8; }
            int[][] faces = { new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 } };
            foreach (var f in faces)
            {
                var v = new[] { c[f[0]], c[f[1]], c[f[2]], c[f[3]] };
                double ux = v[1][0] - v[0][0], uy = v[1][1] - v[0][1], uz = v[1][2] - v[0][2];
                double wx = v[3][0] - v[0][0], wy = v[3][1] - v[0][1], wz = v[3][2] - v[0][2];
                double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
                double fx = (v[0][0] + v[2][0]) / 2 - mx, fy = (v[0][1] + v[2][1]) / 2 - my, fz = (v[0][2] + v[2][2]) / 2 - mz;
                if (nx * fx + ny * fy + nz * fz < 0) { Array.Reverse(v); nx = -nx; ny = -ny; nz = -nz; }
                double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl < 1e-12) continue;
                double l1 = Math.Sqrt(Sq(v[1][0] - v[0][0]) + Sq(v[1][1] - v[0][1]) + Sq(v[1][2] - v[0][2]));
                double l2 = Math.Sqrt(Sq(v[3][0] - v[0][0]) + Sq(v[3][1] - v[0][1]) + Sq(v[3][2] - v[0][2]));
                float su = (float)Math.Min(1, l1), sv = (float)Math.Min(1, l2);
                float[] us = { 0, su, su, 0 }, vs = { sv, sv, 0, 0 };
                int b = md.VerticesCount;
                int flags = VertexFlags.PackNormal(nx / nl, ny / nl, nz / nl);
                for (int k = 0; k < 4; k++)
                    md.AddWithFlagsVertex((float)v[k][0], (float)v[k][1], (float)v[k][2], tp.x1 + (tp.x2 - tp.x1) * us[k], tp.y1 + (tp.y2 - tp.y1) * vs[k], -1, flags);
                md.AddIndices(b, b + 1, b + 2, b, b + 2, b + 3);
                md.AddTextureId(tp.atlasTextureId);
            }
        }

        static double Sq(double v) => v * v;
    }
}
