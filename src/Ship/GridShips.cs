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
            /// <summary>Donor mesh textures left out below her highest rail: what went with its hull (its windows' leading and dark panes).</summary>
            public string[] DropTextures = { "metal/plate/lead", "game:block/black" };
            /// <summary>Her guns: each a copy of the donor's gun on that side (fixture, parts, mesh), at a cell along the keel.</summary>
            public GunSpec[] Guns = Array.Empty<GunSpec>();
            public string GunPort = "gun-port-3", GunStarboard = "gun-starboard-3";
            /// <summary>Donor fixtures and hooks set right for her (code → fields replacing theirs): what stood on the donor's raked transom.</summary>
            public Dictionary<string, JObject> Fixtures = new(), Hooks = new();
            /// <summary>A scuttle (a hatch piece, shut) over each ladder-stair's well that comes up through a weather deck, so the
            /// rooms under her weather decks close (rain, warmth) and a climber opens it to come up (note 169).</summary>
            public bool Scuttles = true;
            /// <summary>Her rooms, each between two of her decks (none: the donor's boxes).</summary>
            public RoomSpec[] Rooms = Array.Empty<RoomSpec>();
            /// <summary>Her paint zones, each the faces its rule picks (the first rule a face meets is its zone).</summary>
            public PaintRule[] Paint = Array.Empty<PaintRule>();
        }

        /// <summary>
        /// A paint zone and the faces of her blocks in it: by part (skin, deck, rib, beam, keel, wall, door), which way
        /// they face ("out" of her hull, "in" toward her rooms, "up", "down"; a side face counts as out when the point just
        /// beyond it is outside her hull's inner surface, so castle fronts and the transom are out) and the height of their
        /// middle (design blocks). Empty lists match anything.
        /// </summary>
        public sealed class PaintRule
        {
            public string Code, Name, Kind = "panel", Default = "oak";
            public string[] Parts = Array.Empty<string>(), Facing = Array.Empty<string>();
            public double YMin = double.MinValue, YMax = double.MaxValue;

            public bool Takes(string part, string facing, double y) =>
                (Parts.Length == 0 || Parts.Contains(part)) && (Facing.Length == 0 || Facing.Contains(facing)) && y >= YMin && y < YMax;
        }

        /// <summary>
        /// A room of hers (the code the game's rooms go by: hold, lowerdeck, greatcabin...): from a deck's top up to the
        /// underside of the deck over it, along the deck over it (from its start to its end, or her whole length), across her.
        /// pieces: its walls are partly placed pieces (a cabin's door), so it's sealed only when they close it.
        /// </summary>
        public sealed class RoomSpec { public string Code, Floor, Ceiling; public bool Pieces; }

        /// <summary>A gun at her main deck's side: its cell along the keel (its port cut in her bulwark there) and side (-1 port).</summary>
        public sealed class GunSpec { public int X, Side; public string Deck = "main"; }

        /// <summary>Move the rig at x by "by" along the keel: fully within "reach", fading to nothing over "falloff".</summary>
        public sealed class RigShift { public double At, By, Reach = 2.5, Falloff = 1.5; }

        public sealed class Built
        {
            public ShipDefinition Def;
            /// <summary>Her mesh (null when built without the donor's mesh).</summary>
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
        /// Run after the JSON ships are loaded (the donors), on both sides, her mesh with her.
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
                    // (both sides: the server works out her paint zones, partitions and damage from her mesh too)
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
            foreach (var c in ship.Present.Where(c => ship.At(c).Kind is "gate" or "gunport").ToList()) ship.Toggle(c);
            double wl = design.Waterline;
            // her hatchways: open deck cells with a hatch piece (vanilla trapdoors) over each, as carrack-m's
            var hatchCells = ship.Present.Where(c => ship.At(c).Kind == "hatch").ToList();
            foreach (var c in hatchCells) ship.Remove(c);

            var donor = JObject.Parse(donorDefJson);
            donor["code"] = game.Code;
            donor["name"] = game.Name ?? design.Name;
            // the donor's hull and everything laid out on it
            foreach (var f in new[] { "hullSlots", "hullBands", "deckSlots", "gunPlaces", "modularPieces", "wells", "layoutRev", "layoutBefore",
                                      "paintZones", "keepClear", "stairs", "sideCuts" })
                donor.Remove(f);
            donor["shapes"] = new JObject();
            donor["paintZones"] = new JArray(game.Paint.Select(r => new JObject { ["code"] = r.Code, ["name"] = r.Name, ["kind"] = r.Kind, ["default"] = r.Default }));
            // her rig, shifted where her masts moved
            var fixtures = new JArray(((JArray)donor["fixtures"]).Where(f => !game.DropFixtures.Contains((string)f["type"])));
            foreach (var f in fixtures) WarpFields(game, f, "pos", "sel", "box", "hinge");
            donor["fixtures"] = fixtures;
            var parts = new JArray(((JArray)donor["movingParts"]).Where(p => !game.DropParts.Any(pre => ((string)p["code"] ?? "").StartsWith(pre))
                && !((string)p["source"] ?? "").Split(':').Skip(1).Any(s => game.DropParts.Any(pre => s.StartsWith(pre)))));
            foreach (var p in parts) WarpFields(game, p, "axisPoint", "anchor", "tip");
            donor["movingParts"] = parts;
            foreach (var s in (JArray)donor["sails"]) WarpFields(game, s, "axisPoint");
            foreach (var l in (JArray)donor["ladders"] ?? new JArray())
            {
                WarpFields(game, l, "bottom", "top", "exitBottom", "exitTop");
                // (a ladder up her side through one of the donor's entry gates: her own gateway is always open)
                if (l["gate"] != null && !((JArray)donor["fixtures"]).Any(f => (string)f["code"] == (string)l["gate"])) l["gate"] = null;
            }
            // her lanterns' hooks (on her masts, at her stern) and the lanterns a new ship has on them and under her decks
            foreach (var h in (JArray)donor["hooks"] ?? new JArray()) WarpFields(game, h, "pos");
            void Patch(JToken list, Dictionary<string, JObject> by)
            {
                foreach (var o in (list as JArray ?? new JArray()).OfType<JObject>())
                    if (o["code"] != null && by.TryGetValue((string)o["code"], out var patch))
                        foreach (var prop in patch.Properties()) o[prop.Name] = prop.Value.DeepClone();
            }
            Patch(donor["fixtures"], game.Fixtures);
            Patch(donor["hooks"], game.Hooks);
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
                double wx0 = cells.Min(c => c.X) + lattice.Origin.X, wz0 = cells.Min(c => c.Z) + lattice.Origin.Z,
                       wx1 = cells.Max(c => c.X) + 1 + lattice.Origin.X, wz1 = cells.Max(c => c.Z) + 1 + lattice.Origin.Z;
                wells.Add(new JObject
                {
                    ["code"] = $"well@{s.FootCell}",
                    ["box"] = new JArray(wx0, top - 0.25, wz0, wx1, top, wz1),
                });
                // its scuttle, where it comes up through a weather deck (nothing of hers over it)
                int wellRow = s.FootCell.Y + rise - 1;
                bool weather = cells.All(c => !lattice.Cells.Keys.Any(o => o.X == c.X && o.Z == c.Z && o.Y > wellRow));
                if (game.Scuttles && weather)
                    pieces.Add(new JObject
                    {
                        ["kind"] = "hatch", ["code"] = $"scuttle@{s.FootCell}",
                        ["at"] = new JArray((wx0 + wx1) / 2, top, (wz0 + wz1) / 2),
                        ["size"] = new JArray(wx1 - wx0, wz1 - wz0),
                        ["block"] = "game:trapdoor-solid-oak-1",
                    });
            }
            foreach (var group in Rectangles(hatchCells))
            {
                var (lo0, _) = lattice.Bounds(group.lo);
                var (_, hi1) = lattice.Bounds(group.hi);
                pieces.Add(new JObject
                {
                    ["kind"] = "hatch", ["code"] = $"hatch@{group.lo}",
                    ["at"] = new JArray((lo0.X + hi1.X) / 2, hi1.Y - wl, (lo0.Z + hi1.Z) / 2),
                    ["size"] = new JArray(group.hi.X - group.lo.X + 1, group.hi.Z - group.lo.Z + 1),
                    ["block"] = hi1.Y - wl > 0 ? "game:trapdoor-window-oak-1" : "game:trapdoor-solid-oak-1",
                });
            }
            // the design's own pieces (cabins, the galley...): as the piece system lays them
            if (design.Game?["pieces"] is JArray extra) foreach (var e in extra) pieces.Add(e.DeepClone());
            donor["modularPieces"] = pieces;
            donor["wells"] = wells;

            // her guns: the donor's gun of that side copied to each of her ports, renamed and moved
            var gunMoves = new List<(string From, string To, double Dx, double Dz)>();
            var gunPlaces = new JArray();
            {
                var original = JObject.Parse(donorDefJson);
                var newFixtures = (JArray)donor["fixtures"];
                var newParts = (JArray)donor["movingParts"];
                foreach (var g in game.Guns)
                {
                    string tcode = g.Side < 0 ? game.GunPort : game.GunStarboard;
                    var tf = ((JArray)original["fixtures"]).FirstOrDefault(f => (string)f["code"] == tcode) as JObject;
                    if (tf == null) continue;
                    string code = "gun-" + (g.Side < 0 ? "p" : "s") + "-" + g.X;
                    double dx = g.X + 0.5 + lattice.Origin.X - (double)tf["pos"][0];
                    double portMid = ((double)tf["port"][1] + (double)tf["port"][3]) / 2 + wl;
                    // (her inside at its port's height, the narrowest along its carriage: toward her ends she narrows)
                    double ourIn = new[] { 0.05, 0.5, 0.95 }.Min(f => HullForm.HalfWidth(HullForm.Offset(lattice.Form.SectionAt(g.X + f), design.Form.Skin), portMid));
                    double outward = ourIn - (double)tf["wall"][0];
                    double dz = g.Side * outward;
                    var f = (JObject)tf.DeepClone();
                    f["code"] = code;
                    foreach (var key in new[] { "pos", "sel", "box", "muzzle", "trunnion", "pivot", "seat" }) Move(f[key], dx, dz);
                    if (f["port"] is JArray port) { port[0] = (double)port[0] + dx; port[2] = (double)port[2] + dx; }
                    if (f["wall"] is JArray wall) { wall[0] = (double)wall[0] + outward; wall[2] = (double)wall[2] + outward; }
                    if (f["lid"] != null) f["lid"] = ((string)f["lid"]).Replace(tcode, code);
                    newFixtures.Add(f);
                    foreach (var tp in ((JArray)original["movingParts"]).Where(x => ((string)x["code"]).StartsWith(tcode + "-") || (string)x["code"] == tcode))
                    {
                        var q = (JObject)tp.DeepClone();
                        string from = (string)q["code"];
                        q["code"] = code + from.Substring(tcode.Length);
                        foreach (var key in new[] { "source", "parent", "follow", "anchorFollow" })
                            if (q[key] != null) q[key] = ((string)q[key]).Replace(tcode, code);
                        foreach (var key in new[] { "axisPoint", "anchor", "tip" }) Move(q[key], dx, dz);
                        newParts.Add(q);
                        gunMoves.Add(("part:" + from, "part:" + (string)q["code"], dx, dz));
                    }
                    // its carriage's room: her plain deck cells beside it, three along the keel, three and four cells out
                    var cells = new JArray();
                    int row = (int)Math.Round(design.Decks.First(d => d.Code == g.Deck).Top - lattice.Origin.Y) - 1;
                    foreach (int x in new[] { g.X - 1, g.X, g.X + 1 })
                        foreach (int zc in new[] { 3, 4 })
                        {
                            var cell = new CellId(x, row, g.Side * zc);
                            if (lattice.SlotKey(cell) == Parts.Deck) cells.Add(g.Deck + ":" + cell.X + ":" + (cell.Z - 1));
                        }
                    gunPlaces.Add(new JObject { ["id"] = code, ["gun"] = code, ["cells"] = cells });
                }
            }
            donor["gunPlaces"] = gunPlaces;

            // where pieces may not go: the rig's places (pump, capstan, helm, the spawn by the plate, the plaque, the ladders up
            // her side and the shrouds), her guns with their carriages' room, her hatchways and her windows
            {
                var keep = new JArray();
                var fromDonor = new[] { "pump", "capstan", "helm", "spawn", "plaque", "ladder" };
                foreach (var k in JObject.Parse(donorDefJson)["keepClear"] as JArray ?? new JArray())
                    if (fromDonor.Contains((string)k["what"])) { var c = k.DeepClone(); WarpToken(game, c["box"]); keep.Add(c); }
                foreach (var f in ((JArray)donor["fixtures"]).Where(f => (string)f["type"] == "gun" && f["sel"] is JArray && f["seat"] is JArray))
                {
                    var sel = (JArray)f["sel"];
                    double y = (double)f["pos"][1], sz = (double)f["seat"][2];
                    keep.Add(new JObject { ["what"] = "gun", ["box"] = new JArray((double)sel[0], y, Math.Min((double)sel[2], sz), (double)sel[3], y + 1, Math.Max((double)sel[5], sz)) });
                }
                // (her cargo hatchways; a scuttle over a ladder's well is kept clear by its ladder)
                foreach (var h in pieces.Where(h => (string)h["kind"] == "hatch" && ((string)h["code"]).StartsWith("hatch@")))
                {
                    double hx = (double)h["at"][0], hy = (double)h["at"][1], hz = (double)h["at"][2], sx = (double)h["size"][0] / 2, sz = (double)h["size"][1] / 2;
                    keep.Add(new JObject { ["what"] = "hatch", ["box"] = new JArray(hx - sx, hy - 1, hz - sz, hx + sx, hy + 1, hz + sz) });
                }
                foreach (var c in ship.Present.Where(c => ship.At(c).Kind == "window"))
                {
                    var (lo, hi) = lattice.Bounds(c);
                    keep.Add(new JObject { ["what"] = "window", ["box"] = new JArray(lo.X, lo.Y - wl, lo.Z, hi.X, hi.Y - wl, hi.Z) });
                }
                donor["keepClear"] = keep;
            }

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
            donor["hullSamples"] = HullSamples(lattice.Form, design, wl);
            if (donor["waterClip"] is JObject wc) wc["dryOutline"] = DryOutline(lattice.Form, wl);

            // her plain planking and deck cells as Magelan's slots: damage, holes, leaks, mending, building mode
            var frames = donor["frames"]?.ToObject<FramesDef>() ?? new FramesDef { X0 = -24, Space = 2 };
            if (game.Rooms.Length > 0)
            {
                var made = new JArray();
                double half = design.Form.Stations.Max(st => st.Section.Max(p => p[1])) + 0.5;
                foreach (var rs in game.Rooms)
                {
                    var floor = design.Decks.First(d => d.Code == rs.Floor);
                    var ceiling = design.Decks.First(d => d.Code == rs.Ceiling);
                    double x0 = ceiling.From ?? lattice.Form.XMin - 0.5, x1 = ceiling.To ?? lattice.Form.XMax + 0.5;
                    made.Add(new JObject
                    {
                        ["code"] = rs.Code, ["enclosed"] = true, ["pieces"] = rs.Pieces,
                        ["min"] = new JArray(x0, floor.Top - wl, -half), ["max"] = new JArray(x1, ceiling.Top - ceiling.Thickness - wl, half),
                    });
                }
                donor["rooms"] = made;
            }
            var rooms = donor["rooms"]?.ToObject<RoomDef[]>() ?? Array.Empty<RoomDef>();
            string RoomAt(double x, double y, double z) => rooms.FirstOrDefault(r => x >= r.Min[0] && x < r.Max[0] && y >= r.Min[1] && y < r.Max[1] && z >= r.Min[2] && z < r.Max[2])?.Code;
            var slots = GridShipSlots.Build(ship, lattice, kinds, wl, frames, RoomAt);
            var plain = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            donor["hullSlots"] = JArray.FromObject(slots.Hull, plain);
            donor["deckSlots"] = JArray.FromObject(slots.Deck, plain);

            var def = donor.ToObject<ShipDefinition>();
            // a sail's shape is drawn where the donor had it: moved by as much as its axis was
            var donorSails = JObject.Parse(donorDefJson)["sails"].ToObject<SailDef[]>();
            for (int i = 0; i < def.Sails.Length && i < donorSails.Length; i++)
                def.Sails[i].Offset = new[] { def.Sails[i].AxisPoint[0] - donorSails[i].AxisPoint[0], 0, 0 };
            def.Init();

            ShipSkinFile skin = null;
            if (donorSkinJson != null) skin = Skin(ship, lattice, kinds, wl, JsonConvert.DeserializeObject<ShipSkinFile>(donorSkinJson), game, slots, gunMoves);
            def.SkinFile = skin;
            return new Built { Def = def, Skin = skin, Ship = ship, Waterline = wl };
        }

        static VoxelGrid Collision(ShipConstruction ship, ShipLattice lattice, double wl, VoxelGrid donor, MastDef[] masts, GameSpec game)
        {
            double v = lattice.Voxel;
            var cells = ship.Collision().Select(t => lattice.VoxelLo(t)).Select(p => (p.X, p.Y - wl, p.Z)).ToList();
            // the donor's rig: above her highest rail and the masts' columns through her (beyond her ends the donor had only its
            // beakhead, which she hasn't)
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
                        if (y + donor.V / 2 > top || mast) rig.Add((Warp(game, cx) - donor.V / 2, y, z));
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

        /// <summary>
        /// Her hull samples (the points tested against world blocks and other ships, a block apart so a post can't slip
        /// between): across her flat bottom a hair over its underside, along her sides just inside her planking at the
        /// bilge, the waterline and above it, and her castles' sides; across her ends at each of those levels.
        /// </summary>
        static JArray HullSamples(HullForm form, ShipDesign design, double wl)
        {
            var pts = new JArray();
            void S(double x, double y, double z) => pts.Add(new JArray(Math.Round(x, 3), Math.Round(y - wl, 3), Math.Round(z, 3)));
            double x0 = form.XMin + 0.3, x1 = form.XMax - 0.3;
            var xs = new List<double> { x0 };
            for (double x = Math.Ceiling(x0); x < x1 - 0.5; x += 1) if (x > x0 + 0.5) xs.Add(x);
            xs.Add(x1);
            void Across(double x, double y, double w)
            {
                S(x, y, 0);
                for (double z = 1; z < w - 0.3; z += 1) { S(x, y, z); S(x, y, -z); }
                if (w > 0.3) { S(x, y, w); S(x, y, -w); }
            }
            foreach (double x in xs)
            {
                var sec = form.SectionAt(x);
                double y = sec[0][0] + 0.02, w = HullForm.HalfWidth(sec, y) - 0.1;
                if (!double.IsNaN(w)) Across(x, y, w);
            }
            // her sides: the bilge, the waterline, above it, and up her castles
            var levels = new List<double> { wl - 2.5, wl + 0.25, wl + 1.5 };
            double main = design.Decks.FirstOrDefault(d => d.Code == "main")?.Top ?? wl + 2;
            for (double y = main + 2.5; y < design.Form.Stations.Max(st => st.Sheer ?? st.Section[^1][0]) - 0.2; y += 3) levels.Add(y);
            foreach (double y in levels)
                foreach (double x in xs)
                {
                    if (form.SheerAt(x) < y + 0.2) continue;
                    var sec = form.SectionAt(x);
                    if (y < sec[0][0] + 0.2) continue;
                    double w = HullForm.HalfWidth(sec, y) - 0.1;
                    if (double.IsNaN(w) || w < 0.3) continue;
                    if (x == x0 || x == x1) Across(x, y, w);
                    else { S(x, y, w); S(x, y, -w); }
                }
            return pts;
        }

        /// <summary>
        /// Her hull's exact outer half-width on a fine grid (ship-local, as Magelan's generator gives it: the water is cut out
        /// of her by it), from her bottom to well above the waterline; 0 outside her.
        /// </summary>
        static JObject DryOutline(HullForm form, double wl)
        {
            const double dx = 0.125, dy = 0.25;
            double x0 = form.XMin - dx, y0 = form.SectionAt(0)[0][0] - wl;
            int nx = (int)Math.Ceiling((form.XMax - x0) / dx) + 2, ny = (int)Math.Ceiling((5.0 - y0) / dy) + 1;
            var w = new JArray();
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double x = x0 + i * dx, y = y0 + j * dy + wl, h = 0;
                    if (x >= form.XMin && x <= form.XMax && y <= form.SheerAt(x))
                    {
                        var sec = form.SectionAt(x);
                        if (y >= sec[0][0]) { h = HullForm.HalfWidth(sec, y); if (double.IsNaN(h)) h = 0; }
                    }
                    w.Add(Math.Round(h, 3));
                }
            return new JObject { ["x0"] = x0, ["dx"] = dx, ["y0"] = y0, ["dy"] = dy, ["nx"] = nx, ["ny"] = ny, ["halfWidths"] = w };
        }

        /// <summary>Moves a point [x y z] or a box [x0 y0 z0 x1 y1 z1] along the keel and across.</summary>
        static void Move(JToken t, double dx, double dz)
        {
            if (t is not JArray a || a.Count < 3) return;
            a[0] = (double)a[0] + dx; a[2] = (double)a[2] + dz;
            if (a.Count >= 6) { a[3] = (double)a[3] + dx; a[5] = (double)a[5] + dz; }
        }

        /// <summary>Joined cells of one height (a hatchway each) and the rectangle round each.</summary>
        static List<(CellId lo, CellId hi)> Rectangles(List<CellId> cells)
        {
            var left = cells.ToHashSet();
            var result = new List<(CellId, CellId)>();
            while (left.Count > 0)
            {
                var first = left.Min();
                var group = new List<CellId>();
                var queue = new Queue<CellId>(new[] { first });
                left.Remove(first);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    group.Add(c);
                    foreach (var d in CellId.Sides.Where(d => d.Y == 0))
                        if (left.Remove(c + d)) queue.Enqueue(c + d);
                }
                result.Add((new CellId(group.Min(c => c.X), first.Y, group.Min(c => c.Z)), new CellId(group.Max(c => c.X), first.Y, group.Max(c => c.Z))));
            }
            return result;
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
        /// and the donor's rig groups (shifted), without its hull. A face of a slot is in its family's slot groups with its
        /// owner and facing slot (docs/legacy/slots.md §3: drawn while its owner is in and its facing slot is out); a fixed
        /// block's face toward a slot faces it; a slot's face toward fixed structure is never seen and left out.</summary>
        static ShipSkinFile Skin(ShipConstruction ship, ShipLattice lattice, BlockKinds kinds, double wl, ShipSkinFile donor, GameSpec game, GridShipSlots.Result slots,
            List<(string From, string To, double Dx, double Dz)> gunMoves)
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
                    bool Above(float[] q) => Enumerable.Range(0, 4).All(k => q[2 + 8 * k] > top + 0.25);
                    bool Hull(string t) => HullTexture(t) || game.DropTextures.Any(t.Contains);
                    keep = quads.Where(q => !Hull(donor.Textures[(int)q[0]]) || Above(q));
                }
                groups[name] = keep.Select(WarpQuad).ToArray();
            }
            // her guns' parts: the donor's gun's, moved to each of her ports
            foreach (var (from, to, dx, dz) in gunMoves)
                if (donor.Groups.TryGetValue(from, out var quads))
                    groups[to] = quads.Select(q =>
                    {
                        var c = (float[])q.Clone();
                        for (int k = 0; k < 4; k++) { c[1 + 8 * k] += (float)dx; c[3 + 8 * k] += (float)dz; }
                        return c;
                    }).ToArray();

            // every block she has by design, and her cells that start out (their planking, drawn while laid)
            var deckKind = kinds.Get("deck");
            var shapes = new Dictionary<CellId, BlockShape>();
            foreach (var c in ship.Present) shapes[c] = ship.Shape(c);
            foreach (var c in slots.StartOut) shapes[c] = BlockShapes.Build(lattice, c, deckKind, new BlockState { Kind = "deck" });
            (int Family, int Index)? SlotOf(CellId c) => slots.SlotOf.TryGetValue(c, out var s) ? s : null;
            bool OpenAbove(CellId c) => !lattice.Cells.Keys.Any(o => o.X == c.X && o.Z == c.Z && o.Y > c.Y);

            var lists = new Dictionary<string, List<float[]>>();
            var refs = new Dictionary<string, List<int>>();
            var zoneOf = new Dictionary<string, List<int>>();
            var form = lattice.Form;
            double skinT = lattice.Design.Form.Skin;
            int Zone(ShipFace f)
            {
                if (game.Paint.Length == 0) return -1;
                var m = Convex.Mean(f.Points);
                string facing = f.Normal.Y > 0.7 ? "up" : f.Normal.Y < -0.7 ? "down" : form.Inside(m + f.Normal * 0.3, skinT) ? "in" : "out";
                for (int i = 0; i < game.Paint.Length; i++) if (game.Paint[i].Takes(f.Part, facing, m.Y)) return i;
                return -1;
            }
            void Emit(string group, int tex, ShipFace f, int owner, int facing)
            {
                if (!lists.TryGetValue(group, out var list)) { lists[group] = list = new List<float[]>(); refs[group] = new List<int>(); zoneOf[group] = new List<int>(); }
                int zone = Zone(f);
                foreach (var piece in Tiles(f))
                    for (int i = 1; i + 1 < piece.Count; i++)
                    {
                        list.Add(Quad(tex, piece[0], piece[i], piece[i + 1], piece[i + 1], f.Normal, wl));
                        refs[group].Add(owner);
                        refs[group].Add(facing);
                        zoneOf[group].Add(zone);
                    }
            }
            foreach (var (c, shape) in shapes.OrderBy(kv => kv.Key))
            {
                var mine = SlotOf(c);
                double cz = lattice.Bounds(c).lo.Z + 0.5;
                int side = cz > 0 ? 1 : cz < 0 ? -1 : 0;
                string lead = lattice.Cells.TryGetValue(c, out var lc) ? lc.Lead : null;
                foreach (var f in shape.Faces)
                {
                    var across = lattice.CellAt(Convex.Mean(f.Points) + f.Normal * 1e-4);
                    CellId? n = null;
                    if (across != c && shapes.TryGetValue(across, out var other) && other.FaceKeys.Contains(Convex.FaceKey(f.Points))) n = across;
                    var theirs = n is CellId nn ? SlotOf(nn) : null;
                    int owner = -1, facing = -1, family = -1;
                    if (n == null) { if (mine != null) { owner = mine.Value.Index; family = mine.Value.Family; } }
                    else if (mine != null && theirs != null)
                    {
                        owner = mine.Value.Index; family = mine.Value.Family;
                        if (theirs.Value.Family == family) facing = theirs.Value.Index;
                    }
                    else if (mine == null && theirs != null) { facing = theirs.Value.Index; family = theirs.Value.Family; }
                    else continue;  // a slot's face against fixed structure, or fixed against fixed: never seen
                    bool outward = f.Normal.Y > 0.9 ? OpenAbove(c) : side != 0 && f.Normal.Z * side > 0.2 || f.Normal.Y < -0.7;
                    string group = family switch
                    {
                        0 => outward ? "slot:hull" : "slot:hull:interior",
                        1 => outward ? "slot:deck" : "slot:deck:interior",
                        _ => outward && lead is not (Parts.Deck or Parts.Beam) || f.Normal.Y > 0.9 && OpenAbove(c) ? "exterior" : "interior",
                    };
                    Emit(group, Tex(Texture(f.Part, f.Material)), f, owner, facing);
                }
            }
            // her paint zones: per group, runs of quads [first, end, zone] (her rig's quads, ahead of hers in "exterior", in none)
            var paintQuads = new Dictionary<string, int[]>();
            foreach (var (name, list) in lists)
            {
                int start = 0;
                if (name == "exterior" && groups.TryGetValue("exterior", out var rig)) { start = rig.Length; groups[name] = rig.Concat(list).ToArray(); }
                else groups[name] = list.ToArray();
                var runs = new List<int>();
                var zs = zoneOf[name];
                for (int i = 0; i < zs.Count;)
                {
                    int j = i;
                    while (j < zs.Count && zs[j] == zs[i]) j++;
                    if (zs[i] >= 0) runs.AddRange(new[] { start + i, start + j, zs[i] });
                    i = j;
                }
                if (runs.Count > 0) paintQuads[name] = runs.ToArray();
            }
            // the slot groups the renderer expects, empty where she has nothing in them
            var slotQuads = new Dictionary<string, int[]>();
            foreach (var g in SlotMeshPlan.Groups.Concat(SlotMeshPlan.HullGroups))
            {
                if (!groups.ContainsKey(g)) groups[g] = Array.Empty<float[]>();
                slotQuads[g] = refs.TryGetValue(g, out var r) ? r.ToArray() : Array.Empty<int>();
            }
            return new ShipSkinFile { Textures = textures.ToArray(), Groups = groups, SlotQuads = slotQuads, PaintQuads = paintQuads.Count > 0 ? paintQuads : null };
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
