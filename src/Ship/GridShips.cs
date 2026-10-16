using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Hansa.Construction;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hansa
{
    /// <summary>
    /// Ships built from a construction design (src/Construction, docs/proposals/ship-design-architecture.md) for the game:
    /// the hull, decks, collision and default layout come from the grid design; masts, yards, sails, rigging, rudder, helm,
    /// rooms and her sailing come from a rig donor (an older definition, e.g. carrack-m). The donor's own hull (its mesh's
    /// planking, its slots, guns and pieces) is left out.
    ///
    /// The grid design's vertical layout is the donor's: decks 3 apart, floors on block borders, her waterline 0.875 up a
    /// block. Ship-local coordinates are the design's less the waterline in y. The rig may be shifted along the keel near a
    /// mast ("rigShift": the grid design keeps a rib each side of every mast, so a mast may stand a cell from where the donor
    /// had it); everything near it moves with it, fading out beyond.
    ///
    /// This is the first in-game binding (M4, docs/proposals): the ship is her design's default blocks, built once per
    /// design. Editing blocks in game, grey guides and saving block changes are still to come.
    /// </summary>
    public static class GridShips
    {
        public sealed class GameSpec
        {
            /// <summary>Her design code in the game (entity "hansa:" + this, a charter "shipdeed-" + this).</summary>
            public string Code, Name;
            /// <summary>The donor definition whose rig and mechanics she takes.</summary>
            public string Rig;
            public RigShift[] RigShift = Array.Empty<RigShift>();
            /// <summary>Donor fixtures left out by type (her old guns, entry gates, the heads on the old beak).</summary>
            public string[] DropFixtures = { "gun", "gate", "seat" };
            /// <summary>Donor moving parts (and their mesh groups) left out by code prefix.</summary>
            public string[] DropParts = { "gun-", "gate-" };
        }

        /// <summary>Move the rig at x by "by" along the keel: fully within "reach", fading to nothing over "falloff".</summary>
        public sealed class RigShift { public double At, By, Reach = 2.5, Falloff = 1.5; }

        public sealed class Built
        {
            public ShipDefinition Def;
            /// <summary>Her mesh (client only; null when built without the donor's mesh).</summary>
            public ShipSkinFile Skin;
            public ShipConstruction Ship;
            public double Waterline;
        }

        /// <summary>The donor's hull textures: planking, sheathing, glass, frames. Her rig never uses these below her rail.</summary>
        static bool HullTexture(string t) =>
            t.Contains("/planks/") || t.Contains("/sheet/copper") || t.Contains("glass") || t.Contains("debarked/oak") || t.Contains("block/caulk") || t.Contains("block/tar");

        public static double Warp(GameSpec g, double x)
        {
            double dx = 0;
            foreach (var s in g.RigShift)
            {
                double d = Math.Abs(x - s.At);
                double w = d <= s.Reach ? 1 : d >= s.Reach + s.Falloff ? 0 : 1 - (d - s.Reach) / s.Falloff;
                dx += s.By * w;
            }
            return x + dx;
        }

        /// <summary>Shift x coordinates in place: a point [x y z], a box or segment [x0 y0 z0 x1 y1 z1 (r)], or arrays of them.</summary>
        static void WarpToken(GameSpec g, JToken t)
        {
            if (t is not JArray a || a.Count == 0) return;
            if (a.All(v => v.Type is JTokenType.Float or JTokenType.Integer))
            {
                if (a.Count >= 3) a[0] = Warp(g, (double)a[0]);
                if (a.Count >= 6) a[3] = Warp(g, (double)a[3]);
                return;
            }
            foreach (var v in a) WarpToken(g, v);
        }

        static void WarpFields(GameSpec g, JToken obj, params string[] fields)
        {
            if (obj is not JObject o) return;
            foreach (var f in fields) if (o[f] != null) WarpToken(g, o[f]);
        }

        /// <summary>
        /// Every design in config/shipdesigns with a "game" section, built against its rig donor and added to the ships.
        /// Run after the JSON ships are loaded (the donors), on both sides; the mesh is built on the client only.
        /// </summary>
        public static void LoadAll(Vintagestory.API.Common.ICoreAPI api, Dictionary<string, ShipDefinition> ships, Vintagestory.API.Common.ILogger log)
        {
            var kindsAsset = api.Assets.TryGet(new Vintagestory.API.Common.AssetLocation("hansa", "config/construction/kinds.json"));
            if (kindsAsset == null) return;
            var kinds = BlockKinds.Read(kindsAsset.ToText());
            foreach (var asset in api.Assets.GetMany("config/shipdesigns/", "hansa"))
            {
                try
                {
                    var design = ShipDesign.Read(asset.ToText());
                    var game = design.Game?.ToObject<GameSpec>();
                    if (game?.Code == null || game.Rig == null) continue;
                    var donorAsset = api.Assets.TryGet(new Vintagestory.API.Common.AssetLocation("hansa", $"config/ships/{game.Rig}.json"));
                    if (donorAsset == null) { log.Error("Grid ship {0}: rig donor {1} not found", design.Code, game.Rig); continue; }
                    string donorJson = donorAsset.ToText(), skinJson = null;
                    if (api.Side == Vintagestory.API.Common.EnumAppSide.Client)
                    {
                        string skinPath = (string)JObject.Parse(donorJson)["shapes"]?["skin"];
                        var skinAsset = skinPath == null ? null : api.Assets.TryGet(Vintagestory.API.Common.AssetLocation.Create(skinPath, "hansa"));
                        skinJson = skinAsset?.ToText();
                    }
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var built = Build(design, kinds, donorJson, skinJson);
                    ships[built.Def.Code] = built.Def;
                    log.Notification("Grid ship '{0}' from design {1} and rig {2}: {3} blocks, {4} solid voxels, {5} ladder-stairs, built in {6} ms",
                        built.Def.Code, design.Code, game.Rig, built.Ship.Present.Count(), built.Def.Grid.CountSolid(), built.Ship.Stairs.Count, watch.ElapsedMilliseconds);
                }
                catch (Exception e)
                {
                    log.Error("Failed to build grid ship from {0}: {1}", asset.Location, e);
                }
            }
        }

        public static Built Build(ShipDesign design, BlockKinds kinds, string donorDefJson, string donorSkinJson)
        {
            var game = design.Game?.ToObject<GameSpec>() ?? throw new ArgumentException($"Design {design.Code} has no game section");
            var lattice = new ShipLattice(design);
            if (lattice.Problems.Count > 0) throw new ArgumentException($"Design {design.Code}: " + string.Join("; ", lattice.Problems));
            var ship = new ShipConstruction(lattice, kinds);
            // her doorways in the castle fronts stand open: walked through, not opened, until editing aboard comes
            foreach (var c in ship.Present.Where(c => ship.At(c).Kind == "gate").ToList()) ship.Toggle(c);
            double wl = design.Waterline;

            var donor = JObject.Parse(donorDefJson);
            donor["code"] = game.Code;
            donor["name"] = game.Name ?? design.Name;
            // the donor's hull and everything laid out on it
            foreach (var f in new[] { "hullSlots", "hullBands", "deckSlots", "gunPlaces", "modularPieces", "wells", "layoutRev", "layoutBefore",
                                      "paintZones", "hooks", "startPieces", "keepClear", "stairs", "sideCuts" })
                donor.Remove(f);
            donor["shapes"] = new JObject();
            // her rig, shifted where her masts moved
            var fixtures = new JArray(((JArray)donor["fixtures"]).Where(f => !game.DropFixtures.Contains((string)f["type"])));
            foreach (var f in fixtures) WarpFields(game, f, "pos", "sel", "box", "hinge");
            donor["fixtures"] = fixtures;
            var parts = new JArray(((JArray)donor["movingParts"]).Where(p => !game.DropParts.Any(pre => ((string)p["code"] ?? "").StartsWith(pre))
                && !((string)p["source"] ?? "").Split(':').Skip(1).Any(s => game.DropParts.Any(pre => s.StartsWith(pre)))));
            foreach (var p in parts) WarpFields(game, p, "axisPoint", "anchor", "tip");
            donor["movingParts"] = parts;
            foreach (var s in (JArray)donor["sails"]) WarpFields(game, s, "axisPoint");
            foreach (var l in (JArray)donor["ladders"] ?? new JArray()) WarpFields(game, l, "bottom", "top", "exitBottom", "exitTop");
            WarpToken(game, donor["boardPoints"]);
            if (donor["rigCheck"] is JObject rc) { WarpToken(game, rc["standing"]); foreach (var y in (JArray)rc["yards"] ?? new JArray()) WarpFields(game, y, "pivot", "arms"); }
            var donorMasts = donor["masts"].ToObject<MastDef[]>();
            foreach (var m in (JArray)donor["masts"]) m["x"] = Warp(game, (double)m["x"]);
            foreach (var d in (JArray)donor["decks"])
                if ((string)d["code"] == "top") { d["x0"] = Warp(game, (double)d["x0"]); d["x1"] = Warp(game, (double)d["x1"]); }
            // her own layout as the old piece system lays it: each ladder-stair a companion ladder, its well
            var pieces = new JArray();
            var wells = new JArray();
            foreach (var s in ship.Stairs)
            {
                var (lo, _) = lattice.Bounds(s.FootCell);
                int rise = FittingShapes.Rise(lattice, s);
                pieces.Add(new JObject
                {
                    ["kind"] = "ladder", ["code"] = $"ladder@{s.FootCell}",
                    ["at"] = new JArray(lo.X + 0.5, lo.Y - wl, lo.Z + 0.5), ["up"] = s.Dir, ["rise"] = rise,
                });
                var cells = new[] { s.FootCell, s.Head };
                double top = lo.Y + rise - wl;
                wells.Add(new JObject
                {
                    ["code"] = $"well@{s.FootCell}",
                    ["box"] = new JArray(cells.Min(c => c.X) + lattice.Origin.X, top - 0.25, cells.Min(c => c.Z) + lattice.Origin.Z,
                                         cells.Max(c => c.X) + 1 + lattice.Origin.X, top, cells.Max(c => c.Z) + 1 + lattice.Origin.Z),
                });
            }
            donor["modularPieces"] = pieces;
            donor["wells"] = wells;

            // her collision: her blocks, and the donor's rig above and beyond her hull (masts, tops, bowsprit), shifted
            var donorGrid = VoxelGrid.Decode(donor["gridOrigin"].ToObject<double[]>(), donor["gridSize"].ToObject<int[]>(),
                (float)donor["voxelSize"], (string)donor["solid"]);
            var grid = Collision(ship, lattice, wl, donorGrid, donorMasts, game);
            donor["gridOrigin"] = new JArray(grid.OX, grid.OY, grid.OZ);
            donor["gridSize"] = new JArray(grid.NX, grid.NY, grid.NZ);
            donor["voxelSize"] = grid.V;
            donor["solid"] = Encode(grid);

            // her measures
            double keel = lattice.Cells.Values.SelectMany(c => c.Pieces).Min(p => p.Solid.Bounds().lo.Y) - wl;
            double bottom = design.Form.Stations.Min(s => s.Section[0][0]) - wl;
            donor["keelY"] = bottom;
            donor["draft"] = -keel;
            donor["length"] = lattice.Form.XMax - lattice.Form.XMin;
            donor["beam"] = 2 * design.Form.Stations.Max(s => s.Section.Max(p => p[1]));
            if (donor["hullSamples"] is JArray hs) foreach (var p in hs) p[1] = keel + 0.02;
            if (donor["waterClip"] is JObject wc) wc.Remove("dryOutline");

            var def = donor.ToObject<ShipDefinition>();
            // a sail's shape is drawn where the donor had it: moved by as much as its axis was
            var donorSails = JObject.Parse(donorDefJson)["sails"].ToObject<SailDef[]>();
            for (int i = 0; i < def.Sails.Length && i < donorSails.Length; i++)
                def.Sails[i].Offset = new[] { def.Sails[i].AxisPoint[0] - donorSails[i].AxisPoint[0], 0, 0 };
            def.Init();

            ShipSkinFile skin = null;
            if (donorSkinJson != null) skin = Skin(ship, lattice, wl, JsonConvert.DeserializeObject<ShipSkinFile>(donorSkinJson), game);
            def.SkinFile = skin;
            return new Built { Def = def, Skin = skin, Ship = ship, Waterline = wl };
        }

        static VoxelGrid Collision(ShipConstruction ship, ShipLattice lattice, double wl, VoxelGrid donor, MastDef[] masts, GameSpec game)
        {
            double v = lattice.Voxel;
            var cells = ship.Collision().Select(t => lattice.VoxelLo(t)).Select(p => (p.X, p.Y - wl, p.Z)).ToList();
            // the donor's rig: above her highest rail, beyond her ends, and the masts' columns through her
            var rig = new List<(double, double, double)>();
            double top = lattice.Design.Form.Stations.Max(s => s.Sheer ?? s.Section[^1][0]) - wl + 0.25;
            for (int i = 0; i < donor.NX; i++)
                for (int j = 0; j < donor.NY; j++)
                    for (int k = 0; k < donor.NZ; k++)
                    {
                        if (!donor.Solid(i, j, k)) continue;
                        double x = donor.OX + i * donor.V, y = donor.OY + j * donor.V, z = donor.OZ + k * donor.V;
                        double cx = x + donor.V / 2, cz = z + donor.V / 2;
                        // (the donor's masts, where the donor had them)
                        bool mast = masts.Any(m => Math.Abs(cx - m.X) < m.Radius + 0.3 && Math.Abs(cz - m.Z) < m.Radius + 0.3);
                        bool beyond = cx > lattice.Form.XMax + 0.5 || cx < lattice.Form.XMin - 0.5;
                        if (y + donor.V / 2 > top || mast || beyond && y > -1) rig.Add((Warp(game, cx) - donor.V / 2, y, z));
                    }
            var all = cells.Concat(rig).ToList();
            double ox = Math.Floor(all.Min(p => p.Item1) / v) * v - v, oy = Math.Floor(all.Min(p => p.Item2) / v) * v - v, oz = Math.Floor(all.Min(p => p.Item3) / v) * v - v;
            // keep her on the donor's voxel lattice (and so on the block grid): both are multiples of a voxel from it
            double Snap(double o, double reference) => reference + Math.Floor((o - reference) / v + 1e-6) * v;
            ox = Snap(ox, donor.OX); oy = Snap(oy, donor.OY); oz = Snap(oz, donor.OZ);
            int nx = (int)Math.Ceiling((all.Max(p => p.Item1) - ox) / v) + 2, ny = (int)Math.Ceiling((all.Max(p => p.Item2) - oy) / v) + 2,
                nz = (int)Math.Ceiling((all.Max(p => p.Item3) - oz) / v) + 2;
            var g = new VoxelGrid(ox, oy, oz, nx, ny, nz, v, new byte[(long)nx * ny * nz / 8 + 1]);
            foreach (var (x, y, z) in all)
                g.Fill(new[] { x + 0.01, y + 0.01, z + 0.01, x + v - 0.01, y + v - 0.01, z + v - 0.01 });
            return g;
        }

        static string Encode(VoxelGrid g)
        {
            long n = (long)g.NX * g.NY * g.NZ;
            var bits = new byte[n / 8 + 1];
            for (int i = 0; i < g.NX; i++)
                for (int j = 0; j < g.NY; j++)
                    for (int k = 0; k < g.NZ; k++)
                        if (g.Solid(i, j, k)) { long b = ((long)i * g.NY + j) * g.NZ + k; bits[b >> 3] |= (byte)(1 << (int)(b & 7)); }
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(bits, 0, bits.Length);
            return Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>Texture for a construction face: the game's planks for planking and decks, debarked wood for timber.</summary>
        static string Texture(string part, string material)
        {
            string wood = "oak";
            var code = material?.Split(':').Last() ?? "";
            if (code.StartsWith("planks-")) wood = code.Split('-')[1];
            return part switch
            {
                "glass" => "hansa:block/glass-clear",
                Parts.Rib or Parts.Beam or Parts.Keel => $"game:block/wood/debarked/{wood}",
                "wall" => $"game:block/wood/planks/{wood}2",
                "door" => $"game:block/wood/planks/{wood}3",
                _ => $"game:block/wood/planks/{wood}1",
            };
        }

        /// <summary>Her mesh: her blocks' faces, tiled so each piece's uv lies within one texture (the atlas doesn't repeat),
        /// and the donor's rig groups (shifted), without its hull.</summary>
        static ShipSkinFile Skin(ShipConstruction ship, ShipLattice lattice, double wl, ShipSkinFile donor, GameSpec game)
        {
            var textures = donor.Textures.ToList();
            int Tex(string t) { int i = textures.IndexOf(t); if (i < 0) { textures.Add(t); i = textures.Count - 1; } return i; }
            var groups = new Dictionary<string, float[][]>();
            float[] WarpQuad(float[] q)
            {
                var c = (float[])q.Clone();
                for (int k = 0; k < 4; k++) c[1 + 8 * k] = (float)Warp(game, c[1 + 8 * k]);
                return c;
            }
            foreach (var (name, quads) in donor.Groups)
            {
                if (name.StartsWith("slot:") || name is "interior" or "bottom" or "bottomclad" or "glass") continue;
                if (name.StartsWith("part:") && game.DropParts.Any(pre => name.Substring(5).StartsWith(pre))) continue;
                IEnumerable<float[]> keep = quads;
                if (name == "exterior")
                {
                    double top = lattice.Design.Form.Stations.Max(s => s.Sheer ?? s.Section[^1][0]) - wl;
                    // her rig: what isn't the donor's planking, and anything above her highest rail
                    keep = quads.Where(q => !HullTexture(donor.Textures[(int)q[0]]) || Enumerable.Range(0, 4).All(k => q[2 + 8 * k] > top + 0.25));
                }
                groups[name] = keep.Select(WarpQuad).ToArray();
            }
            var hull = new List<float[]>();
            foreach (var c in ship.Present)
                foreach (var f in ship.VisibleFaces(c))
                {
                    int t = Tex(Texture(f.Part, f.Material));
                    foreach (var piece in Tiles(f))
                        for (int i = 1; i + 1 < piece.Count; i++)
                            hull.Add(Quad(t, piece[0], piece[i], piece[i + 1], piece[i + 1], f.Normal, wl));
                }
            groups["exterior"] = (groups.TryGetValue("exterior", out var rig) ? rig : Array.Empty<float[]>()).Concat(hull).ToArray();
            return new ShipSkinFile { Textures = textures.ToArray(), Groups = groups };
        }

        static float[] Quad(int tex, (V3 p, double u, double v) a, (V3 p, double u, double v) b, (V3 p, double u, double v) c, (V3 p, double u, double v) d, V3 n, double wl)
        {
            var q = new float[33];
            q[0] = tex;
            int o = 1;
            foreach (var (p, u, v) in new[] { a, b, c, d })
            {
                q[o] = (float)p.X; q[o + 1] = (float)(p.Y - wl); q[o + 2] = (float)p.Z;
                q[o + 3] = (float)u; q[o + 4] = (float)v;
                q[o + 5] = (float)n.X; q[o + 6] = (float)n.Y; q[o + 7] = (float)n.Z;
                o += 8;
            }
            return q;
        }

        /// <summary>A face cut at every texture repeat (16 texels), each piece's uv moved into 0..16.</summary>
        static IEnumerable<List<(V3 p, double u, double v)>> Tiles(ShipFace f)
        {
            var poly = f.Points.Select((p, i) => (p, u: f.Uv[i][0], v: f.Uv[i][1])).ToList();
            int u0 = (int)Math.Floor(poly.Min(x => x.u) / 16 + 1e-9), u1 = (int)Math.Ceiling(poly.Max(x => x.u) / 16 - 1e-9);
            int v0 = (int)Math.Floor(poly.Min(x => x.v) / 16 + 1e-9), v1 = (int)Math.Ceiling(poly.Max(x => x.v) / 16 - 1e-9);
            for (int iu = u0; iu < u1; iu++)
                for (int iv = v0; iv < v1; iv++)
                {
                    var piece = Clip(poly, x => x.u - 16 * iu, true);
                    piece = Clip(piece, x => 16 * (iu + 1) - x.u, true);
                    piece = Clip(piece, x => x.v - 16 * iv, false);
                    piece = Clip(piece, x => 16 * (iv + 1) - x.v, false);
                    if (piece.Count < 3) continue;
                    yield return piece.Select(x => (x.p, x.u - 16 * iu, x.v - 16 * iv)).ToList();
                }
        }

        /// <summary>Keeps the part of a polygon where side(x) ≥ 0 (uv is affine on the face, so positions interpolate with it).</summary>
        static List<(V3 p, double u, double v)> Clip(List<(V3 p, double u, double v)> poly, Func<(V3 p, double u, double v), double> side, bool _)
        {
            var result = new List<(V3 p, double u, double v)>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double sa = side(a), sb = side(b);
                if (sa >= -1e-9) result.Add(a);
                if ((sa >= -1e-9) != (sb >= -1e-9))
                {
                    double t = sa / (sa - sb);
                    result.Add((a.p + (b.p - a.p) * t, a.u + (b.u - a.u) * t, a.v + (b.v - a.v) * t));
                }
            }
            // drop repeated corners
            var clean = new List<(V3 p, double u, double v)>();
            foreach (var x in result) if (clean.Count == 0 || (x.p - clean[^1].p).Length > 1e-9) clean.Add(x);
            if (clean.Count > 1 && (clean[0].p - clean[^1].p).Length < 1e-9) clean.RemoveAt(clean.Count - 1);
            return clean;
        }
    }
}
