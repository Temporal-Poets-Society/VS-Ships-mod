using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    public class SelfTestConfig
    {
        public bool Enabled;
        public bool Shutdown = true;
        /// <summary>(0.3.14) Run only one step after the pool is built: "blocks" (working blocks aboard, SelfTestBlocks),
        /// "modular" (carrack-m's pieces), "grid" (the grid carrack, SelfTestGrid).</summary>
        public string Only;
        /// <summary>The design the main checks launch, sail, sink and shoot ("hansa-carrack": the grid carrack).</summary>
        public string Ship = "carrack";
    }

    /// <summary>
    /// Headless server check (tools/run_selftest.sh): builds a walled pool of water high in the sky, launches a carrack,
    /// lets it ride out a storm at anchor, sails it at the far wall and checks it moves, turns, stops at the wall and
    /// survives save/load of its state. Writes SELFTEST lines to the server log.
    /// </summary>
    public partial class SelfTest : ModSystem
    {
        ICoreServerAPI sapi;
        SelfTestConfig cfg;
        int failures;
        readonly List<string> report = new();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            try { cfg = api.LoadModConfig<SelfTestConfig>("hansa-selftest.json"); } catch { cfg = null; }
            if (cfg?.Enabled != true) return;
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Event.RegisterCallback(_ => Run(), 3000));
        }

        void Log(string s)
        {
            report.Add(s);
            sapi.Logger.Notification("SELFTEST " + s);
        }

        void Check(bool ok, string what)
        {
            Log((ok ? "ok   " : "FAIL ") + what);
            if (!ok) failures++;
        }

        void Run()
        {
            var mod = sapi.ModLoader.GetModSystem<HansaMod>();
            // (18 October 2026) a build without the configured ship (the release carries the caravel, not the carracks)
            // tests its default ship instead
            if (mod.GetShip(cfg.Ship) == null && mod.DefaultShipCode != null)
            {
                Log($"note {cfg.Ship} is not in this build: testing {mod.DefaultShipCode}");
                cfg.Ship = mod.DefaultShipCode;
            }
            var def = mod.GetShip(cfg.Ship);
            Check(def != null, $"{cfg.Ship} definition loaded");
            Check(sapi.World.GetEntityType(new AssetLocation("hansa", cfg.Ship)) != null, $"{cfg.Ship} entity type registered");
            Check(sapi.World.GetItem(new AssetLocation("hansa", "shipdeed-" + cfg.Ship)) != null, "charter item registered");
            {
                // the surveyor's chalk (its notes and marks are the client's): there, with its class, and made by no recipe
                var chalk = sapi.World.GetItem(new AssetLocation("hansa", "surveyorschalk"));
                bool crafted = chalk != null && sapi.World.GridRecipes.Any(r => r.Output?.Code != null && r.Output.Code.Equals(chalk.Code));
                Check(chalk is ItemSurveyorsChalk && !crafted && chalk.CreativeInventoryTabs?.Contains("general") == true,
                    "the surveyor's chalk is registered (its own class, in the creative inventory, no recipe)");
            }
            // the crosshair on ships is the client's: here, whether its patches find what they patch and take their
            // arguments (the server's own selection ray runs through them; they leave it alone)
            string clientPatches = Patches.VerifyClient(() =>
            {
                BlockSelection bs = null;
                EntitySelection es = null;
                var eye = sapi.World.DefaultSpawnPosition.XYZ.Add(0, 2, 0);
                sapi.World.RayTraceForSelection(new Ray(eye, new Vec3d(0, -5, 0)), ref bs, ref es);
            });
            Check(clientPatches == null, "the client's patches (the crosshair on ships, the look-at box, the view on a rolling deck, particles in her dry hold) find what they patch" +
                (clientPatches != null ? ": " + clientPatches : ""));
            Check(Patches.ReachPatched, "the server measures the reach to a ship to her hull (anti-abuse), not to a box round her middle");
            ShadowPlans(mod);
            DamageLooks(mod);
            Notes0314(mod);
            if (def == null) { Finish(); return; }

            // a 90 x 26 pool, 5 deep, walled, well above the terrain
            var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
            int top = sapi.WorldManager.MapSizeY - 20;
            int y0 = top - 8, x0 = spawn.X - 45, z0 = spawn.Z - 13, x1 = spawn.X + 45, z1 = spawn.Z + 13;
            var water = sapi.World.GetBlock(new AssetLocation("game", "water-still-7"));
            var stone = sapi.World.GetBlock(new AssetLocation("game", "rock-granite"));
            Check(water != null && stone != null, "water and granite blocks found");

            // (and the chunks of a deep pool beside it, where a carrack is sunk: DeepSinking; and of the gun pool on its other
            // side, where one carrack fires at another: Gunnery)
            int cx0 = x0 >> 5, cx1 = x1 >> 5, cz0 = Math.Min(z0, spawn.Z - GunZ1) >> 5, cz1 = Math.Max(z1, spawn.Z + DeepZ1) >> 5;
            int pending = 0;
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    pending++;
                    sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true, OnLoaded = () => pending-- });
                }

            long waitId = 0;
            int waited = 0;
            waitId = sapi.Event.RegisterGameTickListener(dt =>
            {
                waited++;
                if (pending > 0 && waited < 600) return;
                sapi.Event.UnregisterGameTickListener(waitId);
                Check(pending <= 0, "pool chunks loaded");
                BuildPool(water, stone, x0, x1, y0, z0, z1);
                if (cfg.Only == "grid") { GridLaunch(mod, spawn, y0, Finish); return; }
                // (0.3.17) the modular carrack launched, laid, sealed, walked, reloaded (gone again before the carrack sails)
                ModularLaunch(mod, spawn, y0);
                if (cfg.Only == "modular") { Finish(); return; }
                if (cfg.Only == "blocks")
                {
                    WorkingBlocksWorks(mod, def, spawn, EntityShip.FindWaterSurface(sapi.World, new BlockPos(spawn.X, y0 + 5, spawn.Z, 0), 2, 6), Finish);
                    return;
                }
                Sail(mod, def, spawn, y0);
            }, 100);
        }

        /// <summary>
        /// (0.3.14) What this version's notes need of the designs, before the pool: the second design carrack-a there (its
        /// entity and charter); every design's sails pressed against her masts, shrouds, backstays and ratlines over the trim
        /// (notes 150-152, a sheet of points over each sail's outline as set); the carrack's partitions drawn out to her real
        /// surfaces from her mesh file (notes 148, 149); the crosshair near a partition's edge and on a beam (note 147).
        /// </summary>
        void Notes0314(HansaMod mod)
        {
            var defA = mod.GetShip("carrack-a");
            if (defA == null && mod.GetShip("carrack-m") == null)
                Log("note the carracks' charters: not in this build (the release carries the caravel)");
            else
            Check(defA != null && defA.Mounts?.Floors.Any() == true && sapi.World.GetEntityType(new AssetLocation("hansa", "carrack-a")) != null &&
                  sapi.World.GetItem(new AssetLocation("hansa", "shipdeed-carrack-a")) != null,
                "the second design carrack-a (companion option A) is loaded, her entity and charter (shipdeed-carrack-a) registered");
            {
                // (the user, 18 October 2026) only the caravel's charter is to be had (the creative inventory); the carracks'
                // stay registered, and where a build carries their ships (not the release) an old deed still launches its
                // ship (0.3.25 kept carrack-m's alone in the inventory)
                Item Deed(string c) => sapi.World.GetItem(new AssetLocation("hansa", "shipdeed-" + c));
                bool InCreative(Item it) => it?.CreativeInventoryTabs?.Contains("general") == true;
                var dc = Deed(HansaMod.DefaultShip);
                var carracks = new[] { "carrack", "carrack-a", "carrack-m", "hansa-carrack" };
                Check(InCreative(dc) && dc.Attributes?["ship"].AsString() == HansaMod.DefaultShip &&
                      carracks.All(c => Deed(c) is Item it && !InCreative(it) && it.Attributes?["ship"].AsString() == c &&
                          (mod.GetShip(c) == null || sapi.World.GetEntityType(new AssetLocation("hansa", c)) != null)),
                    $"only the {HansaMod.DefaultShip}'s charter is in the creative inventory; the carracks' ({string.Join(", ", carracks)}) stay registered, " +
                    $"and launch their ships where this build has them ({carracks.Count(c => mod.GetShip(c) != null)} of {carracks.Length})");
            }
            foreach (var code in new[] { "carrack", "carrack-a" })
            {
                var d = mod.GetShip(code);
                if (d?.DamageParts == null) continue;
                var press = SailPress.Of(d);
                int inside0 = 0, inside1 = 0, through1 = 0, trims = 0;
                var ym = new float[16];
                for (int si = 0; si < d.Sails.Length; si++)
                {
                    var f = d.DamageParts.SailFrames[si];
                    if (f == null) continue;
                    // a sheet over its outline, bellied: across the yard, down to its foot, a block or so apart
                    var pts = new List<float>();
                    for (double u = f.U0 + 0.4; u <= f.U1 - 0.4; u += 0.5)
                        for (double v = 0.2; v <= f.V1; v += 0.5)
                        {
                            if (!f.Inside(u, v, -0.2)) continue;
                            double n = f.ForeAndAft ? 0.3 * Math.Sin(Math.PI * Math.Clamp(v / f.ClewV, 0, 1)) : 0.8 * Math.Sin(Math.PI * Math.Clamp(v / f.V1, 0, 1));
                            var q = f.FromSail(u, v, n);
                            pts.Add((float)q[0]); pts.Add((float)q[1]); pts.Add((float)q[2]);
                        }
                    if (pts.Count == 0) continue;
                    foreach (int side in new[] { 1, -1 })
                        for (double awa = 45; awa <= 180; awa += 15)
                        {
                            Sailing.Trim(awa, side, d.Sailing, out double brace, out double sheet);
                            if (f.Yard != null) PartMotion.PartMatrix(f.Yard, PartMotion.TurnDegrees(f.Yard, (float)(f.ForeAndAft ? sheet : brace)) * GameMath.DEG2RAD, ym);
                            else Mat4f.Identity(ym);
                            var set = pts.ToArray();
                            var drawn = Turned(ym, set);
                            inside0 += press.Check(f, ym, drawn, drawn.Length / 3).Inside;
                            press.Press(f, ym, set, set.Length / 3);
                            var after = press.Check(f, ym, Turned(ym, set), set.Length / 3);
                            inside1 += after.Inside;
                            through1 += after.Through;
                            trims++;
                        }
                }
                Check(trims > 0 && inside1 == 0 && through1 == 0,
                    $"notes 150-152: {code}'s sails pressed against her masts and rigging over {trims} trims: none inside a mast or rope ({inside1}; {inside0} unpressed) or through one ({through1}); {press.Lines.Count} obstacles");
            }
            // the carrack's partitions out to her surfaces (her mesh file and shapes, as the client builds them)
            var def = mod.GetShip(cfg.Ship);
            if (def?.Mounts == null) return;
            try
            {
                var asset = def.SkinFile != null || def.Shapes?.Skin == null ? null : sapi.Assets.TryGet(AssetLocation.Create(def.Shapes.Skin, "hansa"));
                // (a grid ship's mesh is built in memory: def.SkinFile)
                var file = def.SkinFile ?? (asset == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<ShipSkinFile>(System.Text.Encoding.UTF8.GetString(asset.Data)));
                if (file == null) { Log($"{def.Code} has no mesh file here: her partitions' surfaces aren't checked"); return; }
                var surfaces = ShipSurfaces.From(file);
                foreach (var loc in new[] { def.Shapes.Interior, def.Shapes.Exterior })
                    if (loc != null && Shape.TryGet(sapi, AssetLocation.Create(loc, "hansa").WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json")) is Shape sh) surfaces.AddShape(sh);
                var l = def.Mounts;
                // (notes 148-149 are the carrack's own places: another design's beams and sides are elsewhere)
                bool theCarrack = def.Code is "carrack" or "carrack-a";
                var side = theCarrack ? l.Get("b:lower:-6:-6:w:1") : null;
                var beam = theCarrack ? l.Get("b:lower:-7:-2:s:2") : null;
                if (side == null || beam == null) Log($"{def.Code} hasn't the carrack's partition places of notes 148-149: only the general checks");
                var sd = side == null ? null : l.DrawnClip(side, surfaces);
                if (side != null) Check(sd != null && sd.Min(r => r[0]) < -5.6, $"note 149: the partition on b:lower:-6:-6:w:1 is drawn out to her ceiling (to z {sd?.Min(r => r[0]):0.000}, its block ends at -5.5)");
                var bd = beam == null ? null : l.DrawnClip(beam, surfaces);
                // (the beam over the frame at x -6: her cells from -6.25, its side about -6.09)
                if (beam != null) Check(bd != null && bd.Any(r => r[2] > -6.2 && r[3] > 1.6), $"note 148: the partition on b:lower:-7:-2:s:2 is drawn out to the beam's side ({bd?.Count ?? 0} parts)");
                Check(!l.Borders.Any(b => b.Clip != null && b.Clip.Any(r => { var (ln, ax) = MountLayout.BorderLine(b); double lay = ln + (b.In < 2 ? 1 : -1) * def.Grid.V / 2, al = (r[0] + r[2]) / 2, y = (r[1] + r[3]) / 2; return !(ax ? l.InsideHer(al, y, lay) : l.InsideHer(lay, y, al)); })),
                    "note 149: no border place is outside her side");
                // the crosshair on the beam just over a top-row place cut under it finds that place
                var cut = l.Borders.FirstOrDefault(b => b.Room == "lowerdeck" && b.Row == 2 && b.Clip != null && b.Clip.Max(c => c[3]) < b.Y + 0.5);
                if (cut != null)
                {
                    var (line, ax) = MountLayout.BorderLine(cut);
                    double inS = cut.In < 2 ? 1 : -1, y = cut.Clip.Max(c => c[3]) + 0.15, mid = ax ? cut.X : cut.Z;
                    var o = ax ? new Vec3d(mid, y, line + inS * 0.75) : new Vec3d(line + inS * 0.75, y, mid);
                    var dd = ax ? new Vec3d(0, 0, -inS) : new Vec3d(-inS, 0, 0);
                    double wall = def.Grid.Raycast(o, dd, 3);
                    var near = l.NearTimber(o.X + dd.X * wall, o.Y + dd.Y * wall, o.Z + dd.Z * wall, x => x.Kind == MountKind.Border);
                    Check(near?.Id == cut.Id, $"note 147: on the beam over {cut.Id} the crosshair finds it ({near?.Id ?? "nothing"})");
                }
                var a0 = l.Borders.FirstOrDefault(b => b.Row == 0 && b.Clip == null && l.BorderNext(b, 1, 0) is MountPoint nx && nx.Clip == null);
                if (a0 != null)
                {
                    var (_, ax0) = MountLayout.BorderLine(a0);
                    var next = l.NextToPiece(a0, 1, (ax0 ? a0.X : a0.Z) + 0.45, a0.Y + 0.5, null);
                    Check(next?.Id == l.BorderNext(a0, 1, 0)?.Id, $"note 147: near the end of a partition on {a0.Id} the crosshair picks the place past it ({next?.Id ?? "nothing"})");
                }
            }
            catch (Exception e)
            {
                Check(false, "notes 147-149: her surfaces and the partitions drawn to them: " + e.Message + " " + e.StackTrace?.Split((char)10).FirstOrDefault()?.Trim());
            }
        }

        static float[] Turned(float[] m, float[] pts)
        {
            var o = new float[pts.Length];
            for (int i = 0; i + 2 < pts.Length; i += 3)
            {
                float x = pts[i], y = pts[i + 1], z = pts[i + 2];
                o[i] = m[0] * x + m[4] * y + m[8] * z + m[12];
                o[i + 1] = m[1] * x + m[5] * y + m[9] * z + m[13];
                o[i + 2] = m[2] * x + m[6] * y + m[10] * z + m[14];
            }
            return o;
        }

        void BuildPool(Block water, Block stone, int x0, int x1, int y0, int z0, int z1)
        {
            var ba = sapi.World.BlockAccessor;
            var bp = new BlockPos(0);
            // (a ship drawing more than the pool's 4.875 of water, the grid carrack's 4.875: a block deeper for her)
            int bottom = (sapi.ModLoader.GetModSystem<HansaMod>().GetShip(cfg.Ship)?.Draft ?? 0) > 4.6 ? y0 - 1 : y0;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    bool wall = x == x0 || x == x1 || z == z0 || z == z1;
                    for (int y = bottom; y <= y0 + 7; y++)
                    {
                        bp.Set(x, y, z);
                        if (y == bottom || wall) ba.SetBlock(stone.Id, bp);
                        else if (y <= y0 + 5) { ba.SetBlock(0, bp); ba.SetBlock(water.Id, bp, BlockLayersAccess.Fluid); }
                        else ba.SetBlock(0, bp);
                    }
                }
            Log($"pool built {x1 - x0 + 1} x {z1 - z0 + 1} at y {y0}");
        }

        void Sail(HansaMod mod, ShipDefinition def, BlockPos spawn, int y0)
        {
            double wy = EntityShip.FindWaterSurface(sapi.World, new BlockPos(spawn.X, y0 + 5, spawn.Z, 0), 2, 6);
            Check(Math.Abs(wy - (y0 + 5.875)) < 0.01, $"water surface found at {wy}");

            // heading +X (east): yaw pi/2
            double yaw = Math.PI / 2;
            double sx = spawn.X - 25.5, sz = spawn.Z + 0.5;
            Check(!EntityShip.PoseBlocked(sapi.World, def, sx, wy, sz, yaw, 0), "start pose is clear water");
            Check(EntityShip.PoseBlocked(sapi.World, def, spawn.X + 40.5, wy, sz, yaw, 0), "pose straddling the far wall is blocked");

            var type = sapi.World.GetEntityType(new AssetLocation("hansa", cfg.Ship));
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(sx, wy, sz);
            ship.Pos.Yaw = (float)yaw;
            // as if saved by an older version: a chest the design no longer has, with 4 sticks in it
            var stick = sapi.World.GetItem(new AssetLocation("game", "stick"));
            var oldChest = new InventoryGeneric(16, "hansa-old", "0", sapi);
            oldChest[3].Itemstack = new ItemStack(stick, 4);
            var oldTree = new Vintagestory.API.Datastructures.TreeAttribute();
            oldChest.ToTreeAttributes(oldTree);
            ship.WatchedAttributes.GetOrAddTreeAttribute("hansaInv")["captain-chest-1"] = oldTree;
            // and a chest standing on a cargo place the design no longer has (0.3.6: the hold's ladder came down on two pairs)
            var crateChest = sapi.World.GetBlock(new AssetLocation("game", "chest-east"));
            var oldFit = ship.WatchedAttributes.GetOrAddTreeAttribute("hansaFit");
            var oldAttach = new Vintagestory.API.Datastructures.TreeAttribute();
            if (crateChest != null) oldAttach.SetItemstack("cargo-s+99", new ItemStack(crateChest));
            oldFit["attach"] = oldAttach;
            sapi.World.SpawnEntity(ship);
            Check(ship.Def != null && ship.Helm != null, "ship spawned with its helm");
            Check(ship.Storages.Count == 1 && ship.Storages.ContainsKey("hold"), $"a new ship has just its cargo hold ({ship.Storages.Count} storages)");
            Check(ship.Storages["hold"][0].Itemstack?.StackSize == 4 &&
                  ship.WatchedAttributes.GetTreeAttribute("hansaInv")?.HasAttribute("captain-chest-1") == false,
                "what was in an old version's chests ends up in the hold");
            Check(crateChest != null && ship.Storages["hold"].Any(s => s.Itemstack?.Block == crateChest) && !ship.Attached.ContainsKey("cargo-s+99") &&
                  ship.WatchedAttributes.GetTreeAttribute("hansaFit")?.GetTreeAttribute("attach")?.HasAttribute("cargo-s+99") != true,
                "a chest on a place the design no longer has ends up in the hold too");
            // (a new ship's fittings: as they would have been without the old ones)
            ship.WatchedAttributes.GetTreeAttribute("hansaFit")?.RemoveAttribute("attach");
            int seatsWant = 1 + def.Fixtures.Count(f => f.Type is "gun" or "bed" or "oars" or "seat" || f.Type == "attach" && f.Kind == "bed");
            Check(ship.Seats.Length >= seatsWant, $"ship has {ship.Seats.Length} seats (the helm, a gun captain's place at each gun, a bunk for every bed spot: {seatsWant})");
            Fittings(ship, def);
            Decorating(ship, def);
            ShipSpawnChecks(mod, ship, def);
            DryHold(ship, def);
            DryParticleChecks(mod, ship, def);

            // put something in the hold
            var hold = ship.Storages["hold"];
            hold[1].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "stick")), 7);
            hold[1].MarkDirty();

            RideOutAStorm(ship, wy, () => SailOn(mod, def, ship, spawn, sx, sz, wy, yaw));
        }

        /// <summary>
        /// A storm sea (fixed wind, fully built) at anchor: the ship heaves for real on the server (its height is the
        /// still-water level plus the heave from Sea's shared waves), while that level stays at the water surface.
        /// </summary>
        void RideOutAStorm(EntityShip ship, double surface, Action then)
        {
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            if (sea?.Model == null || sea.Spectrum == null)
            {
                Check(false, "Sea's waves are ready on the server");
                then();
                return;
            }
            sea.SetOverride(new Sea.WindOverride { FromDegrees = 0, Strength = 1.2, StartHours = sea.Hours - 24 });
            double lo = double.MaxValue, hi = double.MinValue, worstApplied = 0, worstAgain = 0, t0 = sea.WaveTime;
            int n = 0;
            long id = 0;
            id = sapi.Event.RegisterGameTickListener(dt =>
            {
                n++;
                double heave = ship.Pos.Y - ship.BaseY;
                lo = Math.Min(lo, heave);
                hi = Math.Max(hi, heave);
                worstApplied = Math.Max(worstApplied, Math.Abs(heave - ship.Heave));
                // the heave is the ship's answer to the waves at its place, worked out from nothing but shared inputs
                ship.Def.Waterplane(out double hl, out double hb, out double cx);
                var again = ShipMotion.Waves(ship.Waves, sea.WaveFactorAt(ship.Pos.X, ship.Pos.Z), ship.Waves.Time, ship.Pos.X, ship.Pos.Z,
                    ship.Pos.Yaw, ship.Speed, hl, hb, cx, ship.Def.Motion);
                worstAgain = Math.Max(worstAgain, Math.Abs(again.Heave - ship.Heave));
                if (n < 80) return;
                sapi.Event.UnregisterGameTickListener(id);
                double factor = sea.WaveFactorAt(ship.Pos.X, ship.Pos.Z);
                var s = sea.SeaAt(ship.Pos.X, ship.Pos.Z);
                Log($"storm at anchor: sea {s.Height:0.0} blocks × {factor:0.00} here (sheltered 0.2 .. open sea 1), {ship.Waves.Count} wave trains, wave clock {t0:0.0} → {sea.WaveTime:0.0} s");
                Check(sea.WaveTime - t0 > 6, $"the wave clock runs with the server ({sea.WaveTime - t0:0.0} s)");
                Check(hi - lo > 0.1 * s.Height * factor, $"she heaves with the waves: {lo:0.00} .. {hi:0.00} blocks");
                Check(worstApplied < 1e-9, "her height is the still-water level plus the heave");
                Check(worstAgain < 1e-9, $"the heave is the ship's answer to the shared waves (worst {worstAgain:e1})");
                Check(Math.Abs(ship.BaseY - surface) < 0.05, $"the still-water level stays at the surface ({ship.BaseY - surface:0.000})");
                Check(Math.Abs((ship.WatchedAttributes.GetTreeAttribute("hansa")?.GetDouble("baseY") ?? double.NaN) - ship.BaseY) < 1e-9, "the still-water level is synced to clients");
                // what riding the waves costs a ship each server tick
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 2000; i++) ship.WaveMotion(ship.Pos.X, ship.Pos.Z, ship.Pos.Yaw + i * 1e-4);
                double us = sw.Elapsed.TotalMilliseconds * 1000 / 2000;
                Check(us < 50, $"riding the waves costs {us:0.0} µs a tick");
                sea.SetOverride(null);
                then();
            }, 100);
        }

        void SailOn(HansaMod mod, ShipDefinition def, EntityShip ship, BlockPos spawn, double sx, double sz, double wy, double yaw)
        {
            mod.Config.StopWhenUnmanned = false;
            // sailing by the wind: a strong breeze from the north puts it on her port beam as she heads east
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            sea?.SetOverride(new Sea.WindOverride { FromDegrees = 0, Strength = 0.6, StartHours = sea.Hours - 24 });
            var w0 = ship.WindHere(ship.Pos.X, ship.Pos.Z);
            double beamAngle = Sailing.WindAngle(w0.X, w0.Z, ship.Pos.Yaw, out int beamSide);
            double beamTarget = Sailing.TargetSpeed(w0.X, w0.Z, ship.Pos.Yaw, 1, def.MaxSpeed, def.Sailing);
            Check(mod.Config.SailByWind && Math.Abs(beamAngle - 90) < 1 && beamSide < 0 && beamTarget > 3,
                $"sailing by the wind: a strong breeze on the port beam, her sails good for {beamTarget:0.0} b/s ({Sailing.Point(beamAngle, def.Sailing)})");
            ship.Anchored = false;
            ship.SailSet = 2;
            var start = ship.Pos.XYZ;
            double startBase = ship.BaseY;
            int t = 0;
            double maxSpeed = 0;
            long id = 0;
            id = sapi.Event.RegisterGameTickListener(dt =>
            {
                t++;
                maxSpeed = Math.Max(maxSpeed, ship.Speed);
                if (t == 60) Log($"after 6 s: x {ship.Pos.X - start.X:0.0} blocks, speed {ship.Speed:0.00}, sails {ship.SailFraction:0.00}");
                if (t < 400 && !ship.Aground) return;
                sapi.Event.UnregisterGameTickListener(id);

                Check(ship.Pos.X - start.X > 15, $"ship sailed east {ship.Pos.X - start.X:0.0} blocks (top speed {maxSpeed:0.00} b/s)");
                Check(Math.Abs(ship.Pos.Z - start.Z) < 0.5, "held its course");
                Check(ship.Aground, "ran aground at the wall");
                // her own bow: the foremost of her hull samples (was the carrack's 12 blocks, 14 reported)
                double bowX = def.HullSamples?.Length > 0 ? def.HullSamples.Max(p => p[0]) : 12;
                Check(ship.Pos.X + bowX < spawn.X + 45 + 0.5, $"stopped before the wall (bow at {ship.Pos.X + bowX - spawn.X:0.0} of 45)");
                Check(Math.Abs(ship.BaseY - startBase) < 0.1 && Math.Abs(ship.Pos.Y - (ship.BaseY - ship.Sinkage + ship.Heave)) < 1e-9,
                    "stayed at the water surface, riding the waves on it");
                GroundingDamage(ship, def, maxSpeed);
                Grounding(mod, def, ship);
                LaunchBoat(mod, ship, spawn, startBase);
                RowBoat(mod, ship);

                // turn test: back off, then full rudder
                // ((0.3.25) under the sweeps: backing astern, D held turning her to starboard)
                ship.SailSet = 0;
                ship.SetCrawl(true);
                ship.CrawlTestInput = (-1, 1f);
                ship.Rudder = 1;
                int t2 = 0;
                float yaw0 = ship.Pos.Yaw;
                float sternway = 0;
                long id2 = 0;
                id2 = sapi.Event.RegisterGameTickListener(dt2 =>
                {
                    t2++;
                    ship.Rudder = 1;
                    sternway = Math.Min(sternway, ship.Speed);
                    if (t2 < 60) return;
                    sapi.Event.UnregisterGameTickListener(id2);
                    Check(Math.Abs(GameMath.AngleRadDistance(yaw0, ship.Pos.Yaw)) > 0.02 && sternway < -0.5f,
                        $"turned {GameMath.AngleRadDistance(yaw0, ship.Pos.Yaw) * GameMath.RAD2DEG:0.0} deg while backing off under the sweeps (sternway up to {-sternway:0.00} b/s)");

                    // state round trip through the watched attributes (what is saved with the entity)
                    ship.SaveState(true);
                    var tree = ship.WatchedAttributes.GetTreeAttribute("hansa");
                    Check(tree != null && tree.GetInt("sailSet") == 0 && tree.GetBool("crawl") && tree.GetInt("crawlDrive") == -1, "state saved in watched attributes (under the sweeps, backing)");
                    ship.CrawlTestInput = null;
                    ship.SetCrawl(false);
                    var inv = ship.WatchedAttributes.GetTreeAttribute("hansaInv")?.GetTreeAttribute("hold");
                    Check(inv != null, "hold contents saved");

                    // like Cylinder World's seam jump: teleport the ship while it sails, it must carry on
                    Check(EntityShip.CheckPose(sapi.World, def, spawn.X + 6000.5, wy, sz, yaw, 0) == EntityShip.PoseState.Unloaded,
                        "sea that isn't loaded yet means wait, not aground");
                    ship.Rudder = 0;
                    ship.Pos.Yaw = (float)yaw;
                    ship.TeleportToDouble(sx, wy, sz);
                    ship.SailSet = 2;
                    ship.Anchored = false;
                    int t3 = 0;
                    double x3 = 0;
                    long id3 = 0;
                    id3 = sapi.Event.RegisterGameTickListener(dt3 =>
                    {
                        t3++;
                        if (t3 == 10)
                        {
                            Check(Math.Abs(ship.Pos.X - sx) < 3 && Math.Abs(ship.Pos.Z - sz) < 0.5, $"teleported back to the start ({ship.Pos.X - sx:0.0})");
                            Check(Math.Abs(ship.BaseY - wy) < 0.05, $"a teleport sets where she floats ({ship.BaseY - wy:0.000} from the surface)");
                            x3 = ship.Pos.X;
                        }
                        if (t3 < 90) return;
                        sapi.Event.UnregisterGameTickListener(id3);
                        Check(ship.Pos.X - x3 > 12 && !ship.Aground && !ship.Waiting, $"kept sailing after the teleport ({ship.Pos.X - x3:0.0} blocks in 8 s)");

                        // copper sheathing: plates add up; done at the design's count
                        int used = ship.AddCladdingPlates(10);
                        Check(used == 10 && !ship.Clad, $"10 copper plates nailed on ({ship.CladPlates} of {def.CladdingPlates})");
                        used = ship.AddCladdingPlates(1000);
                        Check(ship.Clad && ship.CladPlates == def.CladdingPlates && used == def.CladdingPlates - 10, "sheathed once all plates are on, no plate wasted");
                        Check(ship.WatchedAttributes.GetTreeAttribute("hansaFit")?.GetInt("clad") == def.CladdingPlates, "sheathing saved");

                        // the wind comes round to right ahead: with all sail set she can't sail into it and loses her way
                        sea?.SetOverride(new Sea.WindOverride { FromDegrees = 90, Strength = 0.6, StartHours = sea.Hours - 24 });
                        float before = ship.Speed;
                        int t4 = 0;
                        long id4 = 0;
                        id4 = sapi.Event.RegisterGameTickListener(dt4 =>
                        {
                            t4++;
                            ship.Rudder = 0;
                            ship.Pos.Yaw = (float)yaw;
                            if (t4 < 60) return;
                            sapi.Event.UnregisterGameTickListener(id4);
                            var tree = ship.WatchedAttributes.GetTreeAttribute("hansa");
                            Check(before > 1 && ship.Speed < before - 1 && ship.Speed < 0.5, $"head to wind she loses her way ({before:0.0} → {ship.Speed:0.0} b/s in 6 s)");
                            Check(tree?.GetBool("byWind") == true && Math.Abs(tree.GetFloat("maxSpeed") - def.MaxSpeed * (1 + def.CladdingSpeedBonus)) < 1e-4,
                                "clients know she sails by the wind and her top speed (sheathed)");
                            sea?.SetOverride(null);
                            ship.Die(EnumDespawnReason.Removed);
                            Flooding(mod, def, spawn, sx, sz, wy);
                        }, 100);
                    }, 100);
                }, 100);
            }, 100);
        }

        /// <summary>
        /// Lying against a rock she can be worked off (as long as she touches nothing new) and the rock dug out (the hull
        /// protection only stops things being put inside her); plants inside her hull are cleared, the water stays.
        /// </summary>
        void Grounding(HansaMod mod, ShipDefinition def, EntityShip ship)
        {
            var ba = sapi.World.BlockAccessor;
            var stone = sapi.World.GetBlock(new AssetLocation("game", "rock-granite"));
            BlockPos At(double lx, double ly, double lz)
            {
                ShipFrame.ToWorld(ship.Pos.Yaw, lx, ly, lz, out double wx, out double wy, out double wz);
                return new BlockPos((int)Math.Floor(ship.Pos.X + wx), (int)Math.Floor(ship.BaseY + wy), (int)Math.Floor(ship.Pos.Z + wz), ship.Pos.Dimension);
            }
            // a rock right at a hull sample on her starboard side at the waterline, as if she'd been left lying on it
            var sample = def.HullSamples.Where(p => Math.Abs(p[1] - 0.25) < 1e-6 && p[2] > 2).OrderBy(p => Math.Abs(p[0])).First();
            var rock = At(sample[0], sample[1], sample[2]);
            ba.SetBlock(stone.Id, rock);
            Check(EntityShip.CheckPose(sapi.World, def, ship.Pos.X, ship.BaseY, ship.Pos.Z, ship.Pos.Yaw, ship.Pos.Dimension) == EntityShip.PoseState.Blocked,
                "a rock where she lies, against her side");
            double fx = Math.Sin(ship.Pos.Yaw), fz = Math.Cos(ship.Pos.Yaw);
            Check(ship.PoseToward(ship.Pos.X - fx * 0.1, ship.Pos.Z - fz * 0.1, ship.Pos.Yaw) == EntityShip.PoseState.Clear,
                "she can be worked off it (a move that touches nothing new)");
            // another rock a block further along her side: moving into that is still blocked
            var rock2 = At(sample[0] + 1.0, sample[1], sample[2]);
            ba.SetBlock(stone.Id, rock2);
            Check(ship.PoseToward(ship.Pos.X + fx * 1.0, ship.Pos.Z + fz * 1.0, ship.Pos.Yaw) == EntityShip.PoseState.Blocked,
                "but not into another rock");
            Check(mod.MayPlaceOrBreak(rock), "the rock she lies against can be dug out (the hull protection lets you break it)");
            var inside = At(0, -1.0, 0);
            Check(!mod.MayPlaceOrBreak(inside), "nothing can be put inside her hull");
            ba.SetBlock(0, rock);
            ba.SetBlock(0, rock2);

            // seaweed: one inside her hull under the hold's floor, one beside her
            var weed = sapi.World.GetBlock(new AssetLocation("game", "seaweed-section"));
            Check(weed != null && weed.CollisionBoxes == null || weed?.CollisionBoxes?.Length == 0, "vanilla seaweed found (no collision)");
            if (weed == null) return;
            var weedIn = At(1.0, -1.0, 0.0);
            var weedOut = At(1.0, -1.0, def.Beam / 2 + 2.5);
            ba.SetBlock(weed.Id, weedIn, BlockLayersAccess.Solid);
            ba.SetBlock(weed.Id, weedOut, BlockLayersAccess.Solid);
            int cleared = ship.SweepPlants();
            Check(ba.GetBlock(weedIn, BlockLayersAccess.Solid).Id == 0 && (ba.GetBlock(weedIn, BlockLayersAccess.Fluid)?.IsLiquid() ?? false),
                $"seaweed inside her hull is cleared, the water stays ({cleared} cleared)");
            Check(ba.GetBlock(weedOut, BlockLayersAccess.Solid).Id == weed.Id, "seaweed beside her stays");
            ba.SetBlock(0, weedOut, BlockLayersAccess.Solid);
        }

        /// <summary>
        /// Hansa's own boat, a launch: put in the water, dry inside, her mast folds (lowered on a new boat, no sail then),
        /// a chest in her, her bow locker, and she doesn't pass through the carrack.
        /// </summary>
        void LaunchBoat(HansaMod mod, EntityShip carrack, BlockPos spawn, double wy)
        {
            var bdef = mod.GetShip("skiff");
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", "boat-skiff"));
            Check(bdef != null && type != null, "the ship's boat: design and entity loaded");
            if (bdef == null || type == null) return;
            var boat = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            double bx = spawn.X - 36.5, bz = spawn.Z + 0.5;
            boat.Pos.SetPos(bx, wy, bz);
            boat.Pos.Yaw = 0;
            sapi.World.SpawnEntity(boat);
            Check(boat.Def == bdef && boat.RowSeats.Count == 2 && boat.Helm != null && boat.Storages.ContainsKey("locker"),
                $"she has her tiller, two rowing thwarts and a bow locker ({boat.RowSeats.Count} thwarts, {boat.Storages.GetValueOrDefault("locker")?.Count} slots)");
            Check(!boat.MastUp, "a new boat's mast is lowered");
            var mastF = bdef.Fixtures.First(f => f.Type == "mast");
            boat.ToggleFitting(null, mastF);
            Check(boat.MastUp, "and it can be raised");
            boat.ToggleFitting(null, mastF);
            // dry inside her, below the waterline, but not beside her
            boat.LocalToWorld(-0.25, -0.1, 0, out double ix, out double iy, out double iz);
            boat.LocalToWorld(-0.25, -0.1, bdef.Beam / 2 + 0.6, out double ox, out double oy, out double oz);
            Check(mod.Dry.IsDry(ix, iy, iz) && !mod.Dry.IsDry(ox, oy, oz), "dry inside her below the waterline, not beside her");
            // a chest in her
            var chest = sapi.World.GetBlock(new AssetLocation("game", "chest-east"));
            var cargoF = bdef.Fixtures.First(f => f.Type == "attach" && f.Kind == "cargo");
            Check(chest != null && boat.TryAttach(cargoF, new ItemStack(chest)) == null && boat.Storages.ContainsKey(cargoF.Code), "a chest stowed in her");
            boat.TakeBack(null, cargoF);
            // she doesn't pass through the carrack (and the carrack not through her)
            double cy = carrack.Pos.Yaw;
            double beside = def_beam(carrack) / 2 + 0.4;
            double cx = carrack.Pos.X - Math.Cos(cy) * beside, czz = carrack.Pos.Z + Math.Sin(cy) * beside;   // just off her starboard side
            Check(!boat.HitsShip(boat.Pos.X, boat.Pos.Z, boat.Pos.Yaw), "out in the pool she touches no ship");
            Check(boat.HitsShip(cx, czz, cy), "alongside the carrack, overlapping her side, she's stopped");
            Check(carrack.HitsShip(carrack.Pos.X, carrack.Pos.Z, carrack.Pos.Yaw) == false, "the carrack touches no boat where she lies");
            // someone away had their spawn bound to her: when she's removed for good, it ends (and comes back when they join)
            var spawns = mod.Spawns;
            spawns?.Registry.Players.Add("hansa-selftest-away", new ShipSpawnBinding { ShipId = boat.EntityId, Plate = "plate" });
            boat.Die(EnumDespawnReason.Removed);
            // (the game despawns her, and tells the mods, at its next tick)
            if (spawns != null) sapi.Event.RegisterCallback(_ =>
            {
                spawns.Registry.Players.TryGetValue("hansa-selftest-away", out var away);
                Check(away?.Ended == ShipSpawns.EndGone, $"a ship removed for good ends the spawns bound to her ({away?.Ended ?? "not yet"})");
                spawns.Registry.Players.Remove("hansa-selftest-away");
            }, 1000);
        }

        static double def_beam(EntityShip s) => s.Def?.Beam ?? 9;

        /// <summary>
        /// The rowing boat (vanilla's sailboat made one): entity, item and recipe there; a new one is empty (so she can
        /// be picked up); the item won't put her in the water inside a ship; put against the carrack's side she's moved
        /// out of the hull.
        /// </summary>
        void RowBoat(HansaMod mod, EntityShip carrack)
        {
            var loc = new AssetLocation("hansa", "rowboat-oak");
            var type = sapi.World.GetEntityType(loc);
            Check(type != null && sapi.World.GetItem(loc) is ItemRowBoat, "the rowing boat: entity and item registered");
            Check(sapi.World.GridRecipes.Any(r => r.Output?.Code?.Path?.StartsWith("rowboat-") == true), "and a recipe for her");
            if (type == null) return;
            double cy = carrack.Pos.Yaw;
            // alongside the carrack's starboard side, overlapping it by half a block, heading the same way
            double beside = def_beam(carrack) / 2 + 1.35 - 0.5;
            double bx = carrack.Pos.X - Math.Cos(cy) * beside, bz = carrack.Pos.Z + Math.Sin(cy) * beside;
            Check(EntityRowBoat.TouchesAnyShip(mod.ServerShips, bx, carrack.Pos.Y, bz, (float)cy, carrack.Pos.Dimension),
                "put in overlapping the carrack's side she'd be in her hull: the item won't put her there");
            var boat = (EntityRowBoat)sapi.World.ClassRegistry.CreateEntity(type);
            boat.Pos.SetPos(bx, carrack.Pos.Y, bz);
            boat.Pos.Yaw = (float)cy;
            boat.Pos.Dimension = carrack.Pos.Dimension;
            sapi.World.SpawnEntity(boat);
            Check(boat.IsEmptyForPickup(), "a new rowing boat is empty: she can be picked up");
            var seatFree = boat.FreeSeat();
            Check(seatFree != null && seatFree.CanControl, "she has a free seat to row from, the thwart (it steers)");
            var mid = boat.Middle(0.5);
            Check(boat.Holds(mid.X, mid.Y, mid.Z, 0) && !boat.Holds(mid.X + 6, mid.Y, mid.Z, 0.6) && !boat.Holds(mid.X, mid.Y + 3, mid.Z, 0),
                "stepping into her middle counts as getting in (and not 6 blocks off, or high above her)");
            for (int i = 0; i < 20; i++) boat.OnGameTick(0.05f);
            double moved = Math.Sqrt((boat.Pos.X - bx) * (boat.Pos.X - bx) + (boat.Pos.Z - bz) * (boat.Pos.Z - bz));
            Check(!EntityRowBoat.TouchesAnyShip(mod.ServerShips, boat.Pos.X, boat.Pos.Y, boat.Pos.Z, boat.Pos.Yaw, boat.Pos.Dimension) && moved > 0.3,
                $"put against the carrack she's moved out of her hull ({moved:0.00} blocks)");
            boat.Die(EnumDespawnReason.Removed);
        }

        /// <summary>
        /// Decorating (ShipDecor), end to end on the server's own paths (no player's hand: the self-test has no players): a
        /// vanilla chest on a lower-deck floor place is a storage (things in and out, saved, not taken up full, taken up
        /// empty), a trunk, vessel and crate in the hold, a bed (two places, a bunk someone lies in), the design's lanterns
        /// on their hooks (lit, taken down and hung again), a zone painted with a bucket of dye (its cost), scraped with a
        /// knife, a carving gilded with gold plates. A chest with five sticks stays aboard for what follows.
        /// </summary>
        void Decorating(EntityShip ship, ShipDefinition def)
        {
            var mod = sapi.ModLoader.GetModSystem<HansaMod>();
            var l = ship.Mounts;
            Check(l != null && l.Floors.Any() && l.Walls.Any() && l.Hooks.Any(),
                $"mount points: {l?.Floors.Count()} floor places, {l?.Walls.Count()} wall spots, {l?.Hooks.Count()} hooks");
            if (l == null) return;
            Block B(string code) => sapi.World.GetBlock(new AssetLocation("game", code));
            Item I(string code) => sapi.World.GetItem(new AssetLocation("game", code));
            var stick = I("stick");
            PiecePlan PlanOn(MountPoint m, ItemStack s, int dir = 1) => PiecePlanner.Plan(l, m, new VanillaPieceBlock(sapi.World, s), dir);
            MountPoint FreeIn(string room, ItemStack s, int dir = 1) =>
                l.Floors.FirstOrDefault(m => m.Room == room && !ship.Pieces.Taken(m.Id) && ship.PlaceProblem(PlanOn(m, s, dir), true) == null);

            // ---- a chest on the lower deck: a storage; things put in and taken out; not taken up full; taken up empty
            var chest = new ItemStack(B("chest-east"));
            if (!l.Floors.Any(m => m.Room == "lowerdeck"))
            {
                Log($"{def.Code} has no lower deck: the lower deck's chest and cabinet checks are for ships with one");
                return;
            }
            var at = FreeIn("lowerdeck", chest);
            Check(at != null, "a free floor place on the lower deck for a chest");
            if (at == null) return;
            ShipPiece placed = null;
            InventoryGeneric inv = null;
            Check(ship.PlacePiece(PlanOn(at, chest), chest, true, out placed) == null && ship.Storages.TryGetValue(placed.StorageCode, out inv) && inv.Count == 16 &&
                  placed.Role == PieceRole.Storage, $"a vanilla chest put on {at.Id} is a 16-slot storage");
            if (placed == null || inv == null) return;
            var c = placed.Center;
            Check(ship.Grid.SolidAt(c[0], at.Y + 0.3, c[2]) && ship.PlaceProblem(PlanOn(at, chest), false) == "hansa:piece-taken",
                "the chest is solid, and its place taken");
            ship.LocalToWorld(c[0], at.Y + 1.7, c[2] + 0.8, out double ex, out double ey, out double ez);
            ship.LocalToWorld(c[0], at.Y + 0.6, c[2], out double tx, out double ty, out double tz);
            bool picked = ship.RayHit(new Vec3d(ex, ey, ez), new Vec3d(tx - ex, ty - ey, tz - ez), 5, null, out var hit);
            Check(picked && hit.Part.Kind == ShipPartKind.Piece && hit.Part.Index == placed.Id && ship.PartText(hit.Part)?.Length > 0,
                $"the crosshair on it names it ({(picked ? ship.PartText(hit.Part) : "nothing")})");
            inv[0].Itemstack = new ItemStack(stick, 3);
            inv[0].MarkDirty();
            var took = inv[0].TakeOut(2);
            inv[0].MarkDirty();
            Check(took?.StackSize == 2 && inv[0].StackSize == 1 &&
                  ship.WatchedAttributes.GetTreeAttribute("hansaInv")?.GetTreeAttribute(placed.StorageCode) != null &&
                  ship.WatchedAttributes.GetTreeAttribute("hansaPieces")?.GetTreeAttribute("pieces")?.HasAttribute(placed.Id.ToString()) == true,
                "three sticks put in, two taken out; saved with her: the piece, and what's in it");
            Check(ship.RemovePiece(placed, null) == "hansa:piece-not-empty", "a container with something in it isn't taken up");
            inv[0].Itemstack = null;
            inv[0].MarkDirty();
            // ---- stacking (0.3.9): a chest on the chest, a block up; the one under it isn't taken from under it
            if (l.Above(at) is MountPoint above)
            {
                ShipPiece onTop = null;
                Check(ship.PlacePiece(PlanOn(above, chest), chest, true, out onTop) == null && ship.RemovePiece(placed, null) == "hansa:piece-under" &&
                      ship.Grid.SolidAt(c[0], above.Y + 0.3, c[2]),
                    $"a chest on the chest ({above.Id}), solid; the one under it isn't taken up from under it");
                Check(onTop != null && ship.RemovePiece(onTop, null) == null, "the one on top is taken up first");
            }
            Check(ship.RemovePiece(placed, null) == null && !ship.Storages.ContainsKey(placed.StorageCode) && !ship.Grid.SolidAt(c[0], at.Y + 0.3, c[2]),
                "emptied, it's taken up: its storage gone, its place walkable again");
            Check(l.Above(at) == null || ship.PlaceProblem(PlanOn(l.Above(at), chest), false) == "hansa:piece-no-support",
                "with nothing under it, the place over it holds nothing");

            // ---- a panel (on the block grid): a planks block in the wedge along her side; a painting on it
            var panelAt = l.Panels.FirstOrDefault(p => p.Row == 0 && !ship.Pieces.Taken(p.Id) && l.Walls.Any(w => w.Under == p.Id));
            var planksBlock = B("planks-oak-ud");
            if (panelAt != null && planksBlock != null)
            {
                var planks = new ItemStack(planksBlock);
                ShipPiece panel = null;
                Check(ship.PlacePiece(PlanOn(panelAt, planks), planks, true, out panel) == null && panel.Role == PieceRole.Panel,
                    $"a planks block on {panelAt.Id} panels the wedge there");
                if (panel != null)
                {
                    var spot = l.Walls.First(w => w.Under == panelAt.Id);
                    var w0 = panel.Solids[0];
                    Check(ship.Grid.SolidAt((w0[0] + w0[3]) / 2, w0[1] + 0.5, (w0[2] + w0[5]) / 2), "the wedge behind it is solid");
                    var paintingBlock = sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Code.Path.StartsWith("painting-") && bl.Id != 0 &&
                        (bl.BlockBehaviors ?? Array.Empty<BlockBehavior>()).Any(x => x is Vintagestory.GameContent.BlockBehaviorHorizontalAttachable));
                    if (paintingBlock != null)
                    {
                        var pst = new ItemStack(paintingBlock);
                        // (a 1 × 1 painting, or the first that fits)
                        var pplan = PlanOn(spot, pst, spot.In);
                        ShipPiece pic = null;
                        string perr = pplan.Error ?? ship.PlaceProblem(pplan, true);
                        if (perr == null) ship.PlacePiece(pplan, pst, true, out pic);
                        Check(pic != null ? ship.RemovePiece(panel, null) == "hansa:piece-under" && ship.RemovePiece(pic, null) == null : true,
                            pic != null ? $"a {paintingBlock.Code} hung on the panel; the panel isn't taken down from under it" : $"(a {paintingBlock.Code} didn't fit on the panel: {perr})");
                    }
                    Check(ship.RemovePiece(panel, null) == null && !ship.Pieces.Taken(panelAt.Id), "the panel taken down");
                }
            }
            else Log(l.Panels.Any() ? "no planks block found: panels not checked" : "the design has no block grid (no panels): not checked");

            // ---- a partition and a door (notes 78, 98): planks on a cell's border, a vanilla door beside it that opens
            var doorBlock = sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Id != 0 && VanillaPieceBlock.IsDoor(bl) && bl.Code.Path.StartsWith("door-solid"))
                            ?? sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Id != 0 && VanillaPieceBlock.IsDoor(bl));
            var border = l.Borders.FirstOrDefault(b => b.Row == 0 && b.Clip == null && l.BorderAbove(b) is MountPoint ab && ab.Clip == null && !ship.Pieces.Taken(b.Id) && !ship.Pieces.Taken(ab.Id) &&
                                                       l.FloorAt(b.Deck, b.I, b.K) is MountPoint fc && !ship.Pieces.Taken(fc.Id));
            if (border != null && planksBlock != null && doorBlock != null)
            {
                var planks = new ItemStack(planksBlock);
                ShipPiece part = null, door = null;
                string perr = ship.PlacePiece(PlanOn(border, planks), planks, true, out part);
                Check(perr == null && part.Role == PieceRole.Partition, $"a planks block on {border.Id} is a partition ({perr ?? "ok"})");
                // (0.3.12) painted like her own surfaces: a bucket of dye, its two faces' worth; saved with her; a knife scrapes it
                var pbucket = sapi.World.GetBlock(new AssetLocation("game", "woodbucket")) as Vintagestory.GameContent.BlockLiquidContainerBase;
                if (part != null && pbucket != null && I("dye-blue") is Item blue)
                {
                    var pb = new ItemStack(pbucket);
                    pbucket.SetContent(pb, new ItemStack(blue, 100));
                    var pslot = new DummySlot(pb);
                    string paintErr = ship.PaintPieceWith(pslot, null, false, part, out _, out _, out _);
                    int pleft = pbucket.GetContent(pslot.Itemstack)?.StackSize ?? 0;
                    var ptree = ship.WatchedAttributes.GetTreeAttribute("hansaPieces")?.GetTreeAttribute("pieces")?.GetTreeAttribute(part.Id.ToString());
                    bool painted = paintErr == null && part.Paint == "blue" && pleft == 100 - ShipPaint.DyeItems(part.PaintArea) && ptree?.GetString("paint") == "blue";
                    var kn = I("knife-generic-copper");
                    bool scraped = kn == null || ship.PaintPieceWith(new DummySlot(new ItemStack(kn)), null, false, part, out _, out _, out _) == null && part.Paint == null;
                    Check(painted && scraped, $"the partition painted blue from a bucket ({part.PaintArea:0} m², {(100 - pleft) / 100.0:0.00} L), saved with her, scraped back with a knife ({paintErr ?? "ok"})");
                }
                if (part != null) ship.RemovePiece(part, null);
                var dst = new ItemStack(doorBlock);
                string derr = ship.PlacePiece(PlanOn(border, dst), dst, true, out door);
                Check(derr == null && door.Role == PieceRole.Door && door.Mounts.Length == 2, $"a {doorBlock.Code} on {border.Id}: a door, two high ({derr ?? "ok"})");
                if (door != null)
                {
                    var s0 = door.Solids[0];
                    double cx = (s0[0] + s0[3]) / 2, cy = s0[1] + 0.5, cz = (s0[2] + s0[5]) / 2;
                    bool shutSolid = ship.Grid.SolidAt(cx, cy, cz);
                    bool opened = ship.ToggleDoorPiece(null, door) && door.Open;
                    bool openFree = !ship.Grid.SolidAt(cx, cy, cz);
                    var tree = new TreeAttribute();
                    door.ToTree(tree);
                    bool saved = ShipPiece.FromTree(tree).Open;
                    bool shut = ship.ToggleDoorPiece(null, door) && !door.Open && ship.Grid.SolidAt(cx, cy, cz);
                    Check(shutSolid && opened && openFree && saved && shut, "shut it's solid; opened (right-click) the doorway is free and it stays open when saved; shut again");
                    Check(ship.RemovePiece(door, null) == null, "the door taken down");
                }
            }
            else Log(border == null ? "no border place free: partitions not checked" : "no planks or door block found: partitions not checked");
            // (0.3.13, note 137) a partition up to her beams and against her curved side: the places cut to what's free of her
            // take planks, solid where they're drawn (and sealing with them), not a door
            {
                var cutTop = l.Borders.FirstOrDefault(b => b.Clip != null && b.Row >= 1 && !ship.Pieces.Taken(b.Id) && b.Under == null);
                var cutSide = l.Borders.FirstOrDefault(b => b.Clip != null && b.Row == 0 && l.FloorAt(b.Deck, b.I, b.K) == null && !ship.Pieces.Taken(b.Id));
                foreach (var cb in new[] { cutTop, cutSide })
                {
                    if (cb == null || planksBlock == null) continue;
                    var planks = new ItemStack(planksBlock);
                    string cerr = ship.PlacePiece(PlanOn(cb, planks), planks, true, out var cp);
                    bool solid = cp != null && cp.Solids.Count == cb.Clip.Count && cp.Solids.All(s0 => ship.Grid.SolidAt((s0[0] + s0[3]) / 2, (s0[1] + s0[4]) / 2, (s0[2] + s0[5]) / 2));
                    Check(cerr == null && cp?.Role == PieceRole.Partition && solid,
                        $"planks on {cb.Id} (only part free of her: {cb.Clip.Count} part(s), up to {cb.Clip.Max(c => c[3]):0.00}): a partition solid where it's drawn ({cerr ?? "ok"})");
                    if (cp != null) ship.RemovePiece(cp, null);
                }
                if (cutTop == null && cutSide == null) Log("no place cut to her timbers free: not checked");
            }
            // (0.3.14, note 147: "above doors cannot place partition wall fragments") a door, and planks over it under her beams
            {
                var dAt = l.Borders.FirstOrDefault(b => b.Row == 0 && b.Clip == null && l.BorderAbove(b) is MountPoint ab && ab.Clip == null &&
                                                        l.BorderNext(ab, 0, 1) is MountPoint t && t.Under == ab.Id && !ship.Pieces.Taken(b.Id) && !ship.Pieces.Taken(ab.Id) && !ship.Pieces.Taken(t.Id));
                if (dAt != null && doorBlock != null && planksBlock != null)
                {
                    var over = l.BorderNext(l.BorderAbove(dAt), 0, 1);
                    var dst = new ItemStack(doorBlock);
                    var planks = new ItemStack(planksBlock);
                    string derr = ship.PlacePiece(PlanOn(dAt, dst), dst, true, out var dp);
                    string oerr = dp == null ? "no door" : ship.PlacePiece(PlanOn(over, planks), planks, true, out var op) ?? (op?.Role == PieceRole.Partition ? null : "not a partition");
                    Check(derr == null && oerr == null, $"note 147: a door on {dAt.Id} and planks over it on {over.Id} ({derr ?? "ok"}, {oerr ?? "ok"})");
                    var top = ship.Pieces.At(over.Id);
                    // (0.3.16, note 160: "cannot remove bottom partition wall if top is still on") the door comes down from
                    // under the partition over it, which stays, hanging from the beam
                    if (top != null) Check(dp == null || ship.RemovePiece(dp, null) == null && ship.Pieces.Get(top.Id) != null,
                        "note 160: the door is taken down from under the partition over it, which stays");
                    if (top != null) ship.RemovePiece(top, null);
                    if (dp != null && ship.Pieces.Get(dp.Id) != null) ship.RemovePiece(dp, null);
                }
                else Log("no border place with room for a door and a partition over it: note 147's door not checked");
            }

            // ---- (0.3.16, notes 154-161) the modular pieces with the game's own blocks: a window, a railing, a shelf, a
            // painting on a partition's face, a lantern on a ceiling hook, the bottom partition taken from under the top one
            {
                Block paneB = B("glasspane-leaded-oak-ew"), fenceB = B("woodenfence-oak-ew-free"), slabB = B("plankslab-oak-down-free"), stairB = B("plankstairs-oak-up-north-free");
                Check(paneB != null && VanillaPieceBlock.IsPane(paneB) && fenceB != null && VanillaPieceBlock.IsFence(fenceB) && slabB != null && VanillaPieceBlock.IsPlankSlab(slabB) &&
                      stairB != null && VanillaPieceBlock.IsStairs(stairB) && ModularPieceDef.DefaultBlock("door") != null,
                    "0.3.16: the modular pieces' vanilla blocks are known (a leaded pane, a fence, a plank slab, plank stairs)");
                var wb = l.Borders.FirstOrDefault(b => b.Row == 0 && b.Clip == null && b.Room == "lowerdeck" && l.BorderAbove(b) is MountPoint ab && ab.Clip == null &&
                                                       !ship.Pieces.Taken(b.Id) && !ship.Pieces.Taken(ab.Id) && l.Walls.Any(w => w.Under == b.Id));
                if (wb != null && paneB != null && fenceB != null && planksBlock != null)
                {
                    var ps = new ItemStack(paneB);
                    string e1 = ship.PlacePiece(PlanOn(wb, ps, wb.In), ps, false, out var win);
                    Check(e1 == null && win?.Role == PieceRole.Window && win.Paintable, $"0.3.16: a leaded pane on {wb.Id} is a window ({e1 ?? "ok"})");
                    if (win != null) ship.RemovePiece(win, null);
                    var fs = new ItemStack(fenceB);
                    string e2 = ship.PlacePiece(PlanOn(wb, fs, wb.In), fs, false, out var rail);
                    Check(e2 == null && rail?.Role == PieceRole.Railing, $"0.3.16: a fence there is a railing ({e2 ?? "ok"})");
                    if (rail != null) ship.RemovePiece(rail, null);
                    // a partition, one over it, a painting on its face; the bottom one comes down from under the top (note 160)
                    var pk = new ItemStack(planksBlock);
                    var ab2 = l.BorderAbove(wb);
                    ship.PlacePiece(PlanOn(wb, pk, wb.In), pk, false, out var bottom);
                    ship.PlacePiece(PlanOn(ab2, pk, ab2.In), pk, false, out var upper);
                    var face = l.Walls.First(w => w.Under == wb.Id);
                    var picB = sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Id != 0 && bl.Code.Path.StartsWith("painting-") && bl.Code.Path.EndsWith("-north"));
                    ShipPiece pic = null;
                    if (picB != null && bottom != null)
                    {
                        var pst = new ItemStack(picB);
                        string e3 = ship.PlacePiece(PlanOn(face, pst, face.In), pst, false, out pic);
                        Check(e3 == null || e3 == "hansa:piece-wall-room" || e3 == "hansa:piece-no-support", $"0.3.16, note 159: a {picB.Code} on the partition's face {face.Id} ({e3 ?? "ok"})");
                        if (pic != null) Check(ship.RemovePiece(bottom, null) == "hansa:piece-under", "the partition doesn't come down with a painting on it");
                        if (pic != null) ship.RemovePiece(pic, null);
                    }
                    if (bottom != null && upper != null)
                        Check(ship.RemovePiece(bottom, null) == null && ship.Pieces.Get(upper.Id) != null, "0.3.16, note 160: the bottom partition comes down with the top one on (it stays)");
                    if (upper != null) ship.RemovePiece(upper, null);
                    if (bottom != null && ship.Pieces.Get(bottom.Id) != null) ship.RemovePiece(bottom, null);
                }
                else Log("0.3.16: no free border place with a wall spot: the window, railing and partition-face checks not done");
                var hb = l.Horizontals.FirstOrDefault(h => h.Level == 1 && !ship.Pieces.Taken(h.Id) && l.FloorAt(h.Deck, h.I, h.K, 1) is MountPoint o && !ship.Pieces.Taken(o.Id) && !ship.Pieces.Taken(l.FloorAt(h.Deck, h.I, h.K).Id));
                if (hb != null && slabB != null)
                {
                    var ss = new ItemStack(slabB);
                    string e4 = ship.PlacePiece(PlanOn(hb, ss), ss, false, out var shelf);
                    Check(e4 == null && shelf?.Role == PieceRole.Shelf && ship.Pieces.Supports(l, l.FloorAt(hb.Deck, hb.I, hb.K, 1)),
                        $"0.3.16, note 159: a plank slab on {hb.Id} is a shelf, and things stand on it ({e4 ?? "ok"})");
                    if (shelf != null) ship.RemovePiece(shelf, null);
                }
                var ch = l.Hooks.FirstOrDefault(h => MountLayout.IsCeilingHook(h.Id) && !ship.Pieces.Taken(h.Id));
                var lanternB = B("lantern-small-up");
                if (ch != null && lanternB != null)
                {
                    var ls = new ItemStack(lanternB);
                    string e5 = ship.PlacePiece(PlanOn(ch, ls), ls, false, out var lan);
                    Check(e5 == null && lan?.Hang == "down", $"0.3.16, note 161: a lantern on the ceiling hook {ch.Id} ({l.Hooks.Count(h => MountLayout.IsCeilingHook(h.Id))} of them) ({e5 ?? "ok"})");
                    if (lan != null) ship.RemovePiece(lan, null);
                }
                else Log($"0.3.16: no ceiling hook ({ch?.Id}) or lantern block: note 161 not checked");
            }

            // ---- (0.3.12, note 124) a cabinet on a cabinet: vanilla's cabinet (0.6875 deep) has a top to stand things on
            var cabinetBlock = sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Id != 0 && bl.Code.Domain == "game" && bl.Code.Path.StartsWith("cabinet"));
            var cabFloor = l.Floors.FirstOrDefault(f => l.Above(f) is MountPoint a && !ship.Pieces.Taken(f.Id) && !ship.Pieces.Taken(a.Id) && f.Room == "lowerdeck");
            if (cabinetBlock != null && cabFloor != null)
            {
                var cst = new ItemStack(cabinetBlock);
                string c1 = ship.PlacePiece(PlanOn(cabFloor, cst), cst, false, out var cab1);
                string c2 = ship.PlacePiece(PlanOn(l.Above(cabFloor), cst), cst, false, out var cab2);
                Check(c1 == null && c2 == null && cab2 != null && Math.Abs(cab2.Box[1] - cabFloor.Y - 1) < 1e-6,
                    $"a {cabinetBlock.Code} on {cabFloor.Id} and a second one on top of it ({c1 ?? "ok"}, {c2 ?? "ok"})");
                if (cab2 != null) ship.RemovePiece(cab2, null);
                if (cab1 != null) ship.RemovePiece(cab1, null);
            }
            else Log("no vanilla cabinet or free floor place on the lower deck: stacking cabinets not checked");

            // ---- the hold's cargo: a trunk across two places, a storage vessel, a crate
            var trunkBlock = sapi.World.Blocks.FirstOrDefault(bl => bl is Vintagestory.GameContent.BlockGenericTypedContainerTrunk);
            var vesselBlock = sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Code.Path.StartsWith("storagevessel-") && bl.Code.Path.EndsWith("-fired"));
            var crateBlock = sapi.World.Blocks.FirstOrDefault(bl => bl is Vintagestory.GameContent.BlockCrate);
            if (trunkBlock != null)
            {
                var trunk = new ItemStack(trunkBlock);
                var tat = FreeIn("hold", trunk);
                ShipPiece tp = null;
                Check(tat != null && ship.PlacePiece(PlanOn(tat, trunk), trunk, true, out tp) == null && tp.Mounts.Length == 2 &&
                      ship.Storages.GetValueOrDefault(tp.StorageCode)?.Count >= 30,
                    $"a trunk in the hold takes two places ({string.Join(" + ", tp?.Mounts ?? Array.Empty<string>())}, {ship.Storages.GetValueOrDefault(tp?.StorageCode ?? "")?.Count} slots)");
                if (tp != null)
                {
                    var other = l.Get(tp.Mounts[1]);
                    Check(ship.Grid.SolidAt(other.CX, other.Y + 0.3, other.CZ) && vesselBlock != null && ship.PlaceProblem(PlanOn(other, new ItemStack(vesselBlock)), false) != null,
                        "it's solid across both, and nothing else goes where it lies");
                    Check(ship.RemovePiece(tp, null) == null && !ship.Pieces.Taken(other.Id), "taken up, both places are free again");
                }
            }
            foreach (var (blk, slots) in new[] { (vesselBlock, 12), (crateBlock, 16) })
            {
                if (blk == null) continue;
                var s = new ItemStack(blk);
                var vat = FreeIn("hold", s);
                ShipPiece vp = null;
                Check(vat != null && ship.PlacePiece(PlanOn(vat, s), s, true, out vp) == null && ship.Storages.GetValueOrDefault(vp.StorageCode)?.Count >= slots,
                    $"a {blk.Code} in the hold is a storage ({ship.Storages.GetValueOrDefault(vp?.StorageCode ?? "")?.Count} slots)");
                if (vp != null) ship.RemovePiece(vp, null);
            }

            // ---- a bed on the lower deck: two places, a bunk; slept in (a chicken stands in: no players here)
            var bed = new ItemStack(B("bed-wood-head-north"));
            var bedPlace = FreeIn("lowerdeck", bed);
            ShipPiece bp = null;
            Check(bedPlace != null && ship.PlacePiece(PlanOn(bedPlace, bed), bed, true, out bp) == null &&
                  bp.Mounts.Length == 2 && bp.Parts.Count == 2 && ship.GetSeat("bunk:" + bp.StorageCode) is BunkSeat,
                $"a vanilla bed put on the lower deck takes two places (head and feet) and is a bunk ({bp?.Parts.FirstOrDefault()?.Block}, {bp?.Parts.LastOrDefault()?.Block})");
            if (bp != null)
            {
                EntityProperties chickenType = null;
                foreach (string code in new[] { "chicken-rooster", "chicken-hen", "chicken-adult" })
                    if ((chickenType = sapi.World.GetEntityType(new AssetLocation("game", code))) != null) break;
                var sleeper = chickenType == null ? null : (EntityAgent)sapi.World.ClassRegistry.CreateEntity(chickenType);
                var bunk = ship.GetSeat("bunk:" + bp.StorageCode);
                if (sleeper != null && bunk != null)
                {
                    var sp = bunk.SeatPosition;
                    sleeper.Pos.SetPos(sp.X, sp.Y, sp.Z);
                    sapi.World.SpawnEntity(sleeper);
                    bool lying = sleeper.TryMount(bunk) && bunk.Passenger == sleeper && Math.Abs(bunk.LocalPos.Y - (bedPlace.Y + ShipFittings.BedHeight)) < 1e-6;
                    Check(lying && ship.RemovePiece(bp, null) == "hansa:bunk-taken",
                        "someone lies in it, on its mattress (and it isn't taken up while they do); vanilla beds set no spawn: the plate by the helm binds it");
                    sleeper.TryUnmount();
                    sleeper.Die(EnumDespawnReason.Removed);
                }
                Check(ship.RemovePiece(bp, null) == null && ship.GetSeat("bunk:" + bp.StorageCode) == null, "got up, the bed is taken up: its bunk gone");
            }

            // ---- her lanterns: the design's start pieces on their hooks (as a new ship gets them), lit, taken down and hung again
            if (def.StartPieces?.Length > 0)
            {
                ship.PlaceStartPieces();
                var lights = def.StartPieces.Select(sp => ship.Pieces.At(sp.Mount)).ToList();
                bool allLit = lights.All(p => p != null && p.Role == PieceRole.Light && p.Solids.Count == 0 &&
                    sapi.World.GetBlock(new AssetLocation(p.Parts[0].Block)) is Block lb &&
                    lb.GetLightHsv(sapi.World.BlockAccessor, null, p.Stack)?[2] > 0 &&
                    p.Parts[0].Block.EndsWith(l.Get(p.Mount).Hang == "up" ? "-up" : "-down"));
                Check(allLit, $"her {def.StartPieces.Length} lanterns on their hooks as a new ship has them: each a light ({lights.FirstOrDefault()?.Parts[0].Block}), hanging or standing as its hook is, not solid");
                var first = lights.FirstOrDefault();
                if (first != null)
                {
                    var stack = first.Stack.Clone();
                    var hook = l.Get(first.Mount);
                    Check(ship.RemovePiece(first, null) == null && !ship.Pieces.Taken(hook.Id) &&
                          ship.PlacePiece(PlanOn(hook, stack), stack, true, out var again) == null && again.Role == PieceRole.Light,
                        $"one taken down from {hook.Id} and hung up again");
                }
            }
            else Log("the design has no start pieces (lanterns): not checked");

            // ---- paint: a zone painted with dye from a bucket (its cost by the zone's area), scraped back with a knife; a
            // carving gilded with gold plates
            var plan = mod.PaintPlanFor(def);
            Check(plan != null && plan.Usable || def.PaintZones == null || def.PaintZones.Length == 0,
                $"her paint zones read ({plan?.Zones.Length ?? 0}, {(plan == null ? "not ready" : plan.Area.Count(a => a > 0) + " with an area")})");
            if (plan?.Usable == true)
            {
                var zone = plan.Zone("rail-caps") ?? plan.Zones.First(z => z.Kind == "moulding");
                double area = plan.Area[plan.ZoneIndex(zone.Code)];
                var bucketBlock = sapi.World.GetBlock(new AssetLocation("game", "woodbucket")) as Vintagestory.GameContent.BlockLiquidContainerBase;
                var dye = I("dye-red");
                if (bucketBlock != null && dye != null)
                {
                    var bucket = new ItemStack(bucketBlock);
                    bucketBlock.SetContent(bucket, new ItemStack(dye, 1000));
                    var slot = new DummySlot(bucket);
                    int need = ShipPaint.DyeItems(area);
                    string err = ship.PaintWith(slot, null, false, zone.Code, null, out _, out var kind, out string colour);
                    int left = bucketBlock.GetContent(slot.Itemstack)?.StackSize ?? 0;
                    Check(err == null && kind == PaintToolKind.Dye && colour == "red" && ship.Paint.Of(zone.Code) == "red" && left == 1000 - need &&
                          ship.WatchedAttributes.GetTreeAttribute("hansaPaint")?.GetString(zone.Code) == "red",
                        $"her {zone.Code} ({area:0.0} m²) painted red from a bucket of dye: {need / 100.0:0.00} L used ({(1000 - left) / 100.0:0.00}), saved with her");
                    var poor = new ItemStack(bucketBlock);
                    bucketBlock.SetContent(poor, new ItemStack(I("dye-blue") ?? dye, Math.Max(1, need / 2)));
                    Check(ship.PaintWith(new DummySlot(poor), null, false, zone.Code, null, out _, out _, out _) == "hansa:paint-dye-short" && ship.Paint.Of(zone.Code) == "red",
                        "with too little dye it isn't painted");
                }
                else Check(false, $"a wooden bucket and red dye to paint with ({bucketBlock?.Code}, {dye?.Code})");
                var knife = I("knife-generic-copper");
                if (knife != null)
                {
                    var kslot = new DummySlot(new ItemStack(knife));
                    Check(ship.PaintWith(kslot, null, false, zone.Code, null, out _, out _, out _) == null && ship.Paint.Of(zone.Code) == null,
                        "a knife scrapes it back to how she was built");
                }
                var carving = plan.Zone("stern-carvings") ?? plan.Zones.FirstOrDefault(z => z.Kind == "carving");
                var gold = I("metalplate-gold");
                if (carving != null && gold != null)
                {
                    int plates = ShipPaint.GoldPlates(plan.Area[plan.ZoneIndex(carving.Code)]);
                    var gslot = new DummySlot(new ItemStack(gold, plates + 2));
                    var panel = plan.Zones.FirstOrDefault(z => !ShipPaint.Gildable(z));
                    Check(ship.PaintWith(gslot, null, false, carving.Code, null, out _, out _, out _) == null && ship.Paint.Of(carving.Code) == ShipPaint.Gold && gslot.StackSize == 2 &&
                          (panel == null || ship.PaintWith(gslot, null, false, panel.Code, null, out _, out _, out _) == "hansa:paint-gild-kind"),
                        $"her {carving.Code} gilded with {plates} gold plates (a plate per 10 m²); a panel isn't gilded");
                    ship.SetPaint(carving.Code, null);
                }
            }
            Check(ship.Paint.Zones.Count == 0, "her paint scraped off again for what follows");

            // keep a chest aboard through the sailing tests (a piece on the lower deck), with something in it
            var keep = FreeIn("lowerdeck", chest);
            if (keep != null && ship.PlacePiece(PlanOn(keep, chest), chest, false, out var kept) == null && ship.Storages.TryGetValue(kept.StorageCode, out var kinv))
            {
                kinv[2].Itemstack = new ItemStack(stick, 5);
                kinv[2].MarkDirty();
            }
        }

        /// <summary>Doors, and vanilla chests, beds and lanterns put aboard: collision, storage, saving.</summary>
        void Fittings(EntityShip ship, ShipDefinition def)
        {
            var door = def.Fixture("door-greatcabin");
            if (door == null) Log($"{def.Code}'s doors are pieces, not fixtures: checked with her pieces (the grid step), not here");
            else
            {
                double[] b = door.Box;
                double cx = (b[0] + b[3]) / 2, cy = b[1] + 1, cz = (b[2] + b[5]) / 2;
                Check(ship.Grid.SolidAt(cx, cy, cz) && !def.Grid.SolidAt(cx, cy, cz), "a new ship's doors are shut (the doorway is solid)");
                ship.ToggleFitting(null, door);
                Check(!ship.Grid.SolidAt(cx, cy, cz) && ship.OpenFittings.Contains(door.Code), "an open door lets you through");
                ship.ToggleFitting(null, door);
                Check(ship.Grid.SolidAt(cx, cy, cz), "and shut again");
            }

            Block B(string code) => sapi.World.GetBlock(new AssetLocation("game", code));
            var chestBlock = B("chest-east");
            var bedBlock = B("bed-wood-head-north");
            var lanternBlock = B("lantern-small-up");
            Check(chestBlock != null && bedBlock != null && lanternBlock != null, "vanilla chest, bed and lantern found");
            if (chestBlock == null || bedBlock == null || lanternBlock == null) return;

            var chestF = def.Fixture("chest-lowerdeck-1");
            var bedF = def.Fixture("bed-lowerdeck-1");
            var lanternF = def.Fixture("lantern-lowerdeck-1");
            // (since the eighth test the design has no built-in chests, beds or lantern places: they're pieces, Decorating)
            if (chestF?.Type == "attach" && bedF?.Type == "attach" && lanternF?.Type == "attach") BuiltInPlaces(ship, chestF, bedF, lanternF, chestBlock, bedBlock, lanternBlock);
            else Log("the design has no built-in chest, bed or lantern places: they're pieces now (checked in Decorating)");
            FittingsRest(ship, def);
        }

        /// <summary>An older design's chest, bed and lantern places (attach fixtures).</summary>
        void BuiltInPlaces(EntityShip ship, FixtureDef chestF, FixtureDef bedF, FixtureDef lanternF, Block chestBlock, Block bedBlock, Block lanternBlock)
        {
            Check(ship.TryAttach(bedF, new ItemStack(chestBlock)) != null, "a chest won't go on a bed spot");
            Check(ship.TryAttach(chestF, new ItemStack(chestBlock)) == null && ship.Storages.TryGetValue(chestF.Code, out var chestInv) && chestInv.Count == 16,
                "a vanilla chest put aboard is a 16-slot storage");
            var cb = ShipFittings.AttachBox(chestF);
            Check(ship.Grid.SolidAt((cb[0] + cb[3]) / 2, cb[1] + 0.3, (cb[2] + cb[5]) / 2), "the chest is solid");
            var inv = ship.Storages[chestF.Code];
            inv[0].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "stick")), 3);
            inv[0].MarkDirty();
            Check(!ship.TakeBack(null, chestF), "a chest with things in it can't be taken");
            inv[0].Itemstack = null;
            inv[0].MarkDirty();
            Check(ship.TakeBack(null, chestF) && !ship.Storages.ContainsKey(chestF.Code), "an empty chest can be taken back");

            Check(ship.TryAttach(bedF, new ItemStack(bedBlock)) == null && ship.GetSeat("bunk:" + bedF.Code) != null, "a vanilla bed put aboard, with its bunk");
            var bb = ShipFittings.AttachBox(bedF);
            Check(ship.Grid.SolidAt((bb[0] + bb[3]) / 2, bb[1] + 0.2, (bb[2] + bb[5]) / 2), "the bed is solid (you can step onto it)");
            Check(ship.TryAttach(lanternF, new ItemStack(lanternBlock)) == null, "a vanilla lantern hung up");
            // keep a chest aboard through the sailing tests, with something in it
            ship.TryAttach(chestF, new ItemStack(chestBlock));
            ship.Storages[chestF.Code][2].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "stick")), 5);
            ship.Storages[chestF.Code][2].MarkDirty();
            var at = ship.WatchedAttributes.GetTreeAttribute("hansaFit")?.GetTreeAttribute("attach");
            Check(at != null && at.HasAttribute(chestF.Code) && at.HasAttribute(bedF.Code) && at.HasAttribute(lanternF.Code), "what's aboard is saved with the ship");
            Check(ship.WatchedAttributes.GetTreeAttribute("hansaInv")?.GetTreeAttribute(chestF.Code) != null, "the chest's contents are saved");
        }

        /// <summary>Hatches, doors below, the oven, the hearth's pick, the hold's storage, cargo places (an older design's), the boats, the plate.</summary>
        void FittingsRest(EntityShip ship, ShipDefinition def)
        {

            // the glazed waist hatch: shut it's solid and the lower deck is a closed room; open it lets the weather in
            var waist = def.Fixture("hatch-waist");
            var lower = def.Rooms.FirstOrDefault(r => r.Code == "lowerdeck");
            if (waist == null || lower == null) Log($"{def.Code} has no waist hatch fixture: her hatches are pieces (checked in the grid step)");
            else WaistHatch(ship, def, waist, lower);
            FittingsAfterWaist(ship, def, lower);
        }

        void WaistHatch(EntityShip ship, ShipDefinition def, FixtureDef waist, RoomDef lower)
        {
            var wb = waist.Box;
            double wx = (wb[0] + wb[3]) / 2, wy = (wb[1] + wb[4]) / 2, wz = (wb[2] + wb[5]) / 2;
            Check(ship.Grid.SolidAt(wx, wy, wz) && ship.RoomEnclosed(lower), "the waist hatch is shut on a new ship, the lower deck a closed room");
            // (0.3.13, "rain is in the lower deck") the rain by what's overhead: under the shut hatch the deck is over you; open,
            // only what's above her deck (the boats on their gallows, if aboard), elsewhere on the lower deck her deck
            double lowerHead = lower.Min[1] + 0.25 + 1.6;
            double coverShut = ShipCover.Above(ship.Grid, wx, lowerHead, wz, new Vec3d(0, 1, 0));
            double coverAway = ShipCover.Above(ship.Grid, wx - 4.5, lowerHead, 0, new Vec3d(0, 1, 0));
            ship.ToggleFitting(null, waist);
            Check(!ship.Grid.SolidAt(wx, wy, wz) && !ship.RoomEnclosed(lower), "opened, it's the cargo hatch and the lower deck is open to the weather");
            double coverOpen = ShipCover.Above(ship.Grid, wx, lowerHead, wz, new Vec3d(0, 1, 0));
            double coverAwayOpen = ShipCover.Above(ship.Grid, wx - 4.5, lowerHead, 0, new Vec3d(0, 1, 0));
            Check(coverShut < def.DeckY + 1 - lowerHead && coverAway < def.DeckY + 0.5 - lowerHead && coverAwayOpen < def.DeckY + 0.5 - lowerHead &&
                  coverOpen > def.DeckY + 0.75 - lowerHead,
                  $"rain below decks by what's overhead: under the waist hatch shut {coverShut:0.00} up, open {(coverOpen == double.MaxValue ? "open sky" : coverOpen.ToString("0.00") + " up (over her deck)")}; 4.5 aft of it her deck either way ({coverAway:0.00}, {coverAwayOpen:0.00})");
            ship.ToggleFitting(null, waist);
        }

        void FittingsAfterWaist(EntityShip ship, ShipDefinition def, RoomDef lower)
        {
            // (0.3.13, note 131) the pin rails work her sails as W/S at the helm: right-click more, Shift less
            {
                int keep = ship.SailSet;
                ship.SailSet = 0;
                string e1 = ship.WorkSails(true); int s1 = ship.SailSet;
                string e2 = ship.WorkSails(true); int s2 = ship.SailSet;
                string e3 = ship.WorkSails(true);
                string e4 = ship.WorkSails(false); int s4 = ship.SailSet;
                ship.WorkSails(false);
                string e5 = ship.WorkSails(false); int s5 = ship.SailSet;
                Check(e1 == null && s1 == 1 && e2 == null && s2 == 2 && e3 == "hansa:pinrail-full" && e4 == null && s4 == 1 && e5 == "hansa:pinrail-furled" && s5 == 0,
                    $"a pin rail sets sail furled → half → full ({s1}, {s2}, then {e3}) and takes it in ({s4}, then {e5} at {s5})");
                ship.SailSet = keep;
                var rails = def.Fixtures.Where(f => f.Type == "pinrail").ToList();
                // (the generator puts them in: a sel box over each, so clicking there isn't a gun's or the bulwark's)
                Check(rails.Count == 0 || rails.All(f => f.Sel != null && f.Pos != null),
                    rails.Count == 0 ? "no pin rail fixture in this design yet (the generator adds them: type pinrail, a sel box over each)"
                                     : $"her {rails.Count} pin rails can be clicked (a sel box each)");
            }
            // the companion house's door (0.3.7): shut on a new ship, the lower deck a closed room; open, the way down and the
            // lower deck open to the weather
            // (since the eighth test the great cabin's door: whichever door on the main deck unseals a room below)
            var compDoor = def.Fixtures.FirstOrDefault(f => f.Type == "door" && f.Unseals != null && f.Box != null && f.Box[1] >= def.DeckY - 0.3);
            if (compDoor != null)
            {
                var cb2 = compDoor.Box;
                double ccx = (cb2[0] + cb2[3]) / 2, ccy = cb2[1] + 1, ccz = (cb2[2] + cb2[5]) / 2;
                Check(ship.Grid.SolidAt(ccx, ccy, ccz) && ship.RoomEnclosed(lower), $"the door below ({compDoor.Code}) is shut on a new ship, the lower deck a closed room");
                ship.ToggleFitting(null, compDoor);
                Check(!ship.Grid.SolidAt(ccx, ccy, ccz) && !ship.RoomEnclosed(lower), "opened, it's the way down, and the lower deck is open to the weather");
                ship.ToggleFitting(null, compDoor);
            }
            // the clay oven (the eighth test, the user's note 51; in a design with one): six firewood in, lit, burning, and
            // clients told (its baking: the offline checks)
            var ovenF = def.Fixtures.FirstOrDefault(f => f.Type == "oven");
            if (ovenF != null)
            {
                var wood = sapi.World.GetItem(new AssetLocation("game", "firewood"));
                bool fuelled = false;
                if (wood != null && ship.OvenInv != null)
                {
                    ship.OvenInv[0].Itemstack = new ItemStack(wood, 6);
                    fuelled = ship.OvenFuel == 6;
                }
                bool lit = ship.Oven.TryIgnite(ship.OvenFuel);
                ship.SaveOven(true);
                var told = ship.WatchedAttributes.GetTreeAttribute("hansaOven");
                Check(ship.OvenFixture == ovenF && fuelled && lit && ship.Oven.Burning && told?.GetInt("burn") == 1 && !ship.OvenIgnitable,
                    $"her clay oven takes six firewood (vanilla's isClayOvenFuel: {wood?.Attributes?.IsTrue("isClayOvenFuel")}), is lit and burns, and clients are told");
                ship.Oven.Douse(20);
                if (ship.OvenInv != null) ship.OvenInv[0].Itemstack = null;
                ship.SaveOven(true);
            }
            // the crosshair on the firehearth picks it (0.3.7: its bricks hid it, the user's note 1): from where that note
            // was written, through her own ray
            var hearthPick = def.Fixture("hearth");
            if (hearthPick != null)
            {
                // (0.3.9: the forecastle moved: note 1's view from where the hearth stands now)
                ship.LocalToWorld(hearthPick.Pos[0], def.DeckY + 1.62, hearthPick.Pos[2] - 1.48, out double ex, out double ey, out double ez);
                ship.LocalToWorld(hearthPick.Pos[0], hearthPick.Pos[1] + 0.55, hearthPick.Pos[2] - 0.5, out double tx, out double ty, out double tz);
                bool picked = ship.RayHit(new Vec3d(ex, ey, ez), new Vec3d(tx - ex, ty - ey, tz - ez), 6, null, out var rh) && rh.Fixture == hearthPick;
                Check(picked, $"looking at the firehearth's bricks and firebed picks the hearth ({rh.Fixture?.Code ?? "her " + rh.Part.Kind})");
            }
            // the hold: its old one big storage stays behind the hatch for what an older ship still has in it; empty, the
            // hatch just opens and shuts
            var holdHatch = def.Fixture("hatch-hold");
            if (holdHatch == null) Log($"{def.Code} has no hold hatch fixture: her hold is reached through her hatchways (pieces)");
            else Check(holdHatch.Storage == "hold" && ship.Storages.TryGetValue("hold", out var hold) && hold.Count == def.Fixture("hold").Slots,
                "the hold's old one-big storage is still there behind the hatch, for what older ships have in it");
            // the hold's cargo: a trunk across a pair of places, a storage vessel and a crate on single ones
            var pairF = def.Fixtures.FirstOrDefault(f => f.Kind == "cargo" && f.Pair != null);
            var partner = pairF != null ? def.Fixture(pairF.Pair) : null;
            var trunkBlock = sapi.World.Blocks.FirstOrDefault(bl => bl is Vintagestory.GameContent.BlockGenericTypedContainerTrunk);
            var vesselBlock = sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Code.Path.StartsWith("storagevessel-") && bl.Code.Path.EndsWith("-fired"));
            var crateBlock = sapi.World.Blocks.FirstOrDefault(bl => bl is Vintagestory.GameContent.BlockCrate);
            // (since the eighth test the hold has no cargo places: its cargo is pieces, Decorating)
            if (pairF == null) Log("the design has no cargo places in the hold: its cargo is pieces now (checked in Decorating)");
            else Check(partner != null && trunkBlock != null && vesselBlock != null && crateBlock != null,
                $"cargo places in pairs, and a vanilla trunk ({trunkBlock?.Code}), storage vessel ({vesselBlock?.Code}) and crate ({crateBlock?.Code}) to try");
            if (pairF != null && partner != null && trunkBlock != null)
            {
                Check(ship.TryAttach(pairF, new ItemStack(trunkBlock)) == null && ship.CoveredBy(partner) == pairF &&
                      ship.Storages.TryGetValue(pairF.Code, out var trunkInv) && trunkInv.Count >= 30,
                    $"a trunk takes both places of a pair ({ship.Storages.GetValueOrDefault(pairF.Code)?.Count} slots)");
                var tb = ShipFittings.AttachBox(pairF, ship.Attached[pairF.Code], def);
                Check(ship.Grid.SolidAt(partner.Pos[0], partner.Pos[1] + 0.3, partner.Pos[2]), "the trunk is solid across both");
                Check(vesselBlock == null || ship.TryAttach(partner, new ItemStack(vesselBlock)) != null, "nothing else goes where the trunk lies");
                Check(ship.TakeBack(null, pairF) && ship.CoveredBy(partner) == null, "the trunk comes off, both places free again");
                if (vesselBlock != null)
                    Check(ship.TryAttach(partner, new ItemStack(vesselBlock)) == null && ship.Storages.GetValueOrDefault(partner.Code)?.Count == 12,
                        $"a storage vessel on one place ({ship.Storages.GetValueOrDefault(partner.Code)?.Count} slots)");
                if (crateBlock != null)
                    Check(ship.TryAttach(pairF, new ItemStack(crateBlock)) == null && ship.Storages.GetValueOrDefault(pairF.Code)?.Count >= 16,
                        $"a crate on the other ({ship.Storages.GetValueOrDefault(pairF.Code)?.Count} slots)");
                Check(trunkBlock == null || ship.TryAttach(pairF, new ItemStack(trunkBlock)) != null, "no trunk where there's no room for it");
                ship.TakeBack(null, pairF);
                ship.TakeBack(null, partner);
            }
            // the ship's boats: on the gallows, vanilla rafts or our own boat
            var raft = sapi.World.Items.FirstOrDefault(it => it?.Code != null && it.Code.Domain == "game" && it.Code.Path.StartsWith("boat-raft-"));
            var sailed = sapi.World.Items.FirstOrDefault(it => it?.Code != null && it.Code.Domain == "game" && it.Code.Path.StartsWith("boat-sailed-"));
            var skiff = sapi.World.GetItem(new AssetLocation("hansa", "boat-skiff"));
            var boats = def.Fixtures.Where(f => f.Kind == "boat").ToArray();
            if (boats.Length == 0) Log($"{def.Code} carries no boats on gallows: the gallows checks are for ships with them");
            else Check(boats.Length == 2, $"two boat places on the gallows ({boats.Length})");
            var boatF = boats.FirstOrDefault();
            if (boatF != null)
            {
                Check(raft != null && ship.TryAttach(boatF, new ItemStack(raft)) == null, $"a vanilla raft stowed on the gallows ({raft?.Code})");
                var ob = ShipFittings.AttachBox(boatF);
                Check(ob != null && ship.Grid.SolidAt((ob[0] + ob[3]) / 2, (ob[1] + ob[4]) / 2, (ob[2] + ob[5]) / 2), "the boat on the gallows is solid");
                Check(ship.TakeBack(null, boatF) && !ship.Attached.ContainsKey(boatF.Code), "and it comes off again");
                Check(skiff is ItemShipDeed deed && deed.IsBoat && sapi.World.GetEntityType(skiff.Code) != null && ship.TryAttach(boats[1], new ItemStack(skiff)) == null,
                    $"our own boat (an item that launches one of our vessels, {skiff?.Code}) goes on the gallows too");
                ship.TakeBack(null, boats[1]);
                Check(sailed == null || ship.TryAttach(boatF, new ItemStack(sailed)) != null, "a sailed boat doesn't fit the gallows");
            }
            // (0.3.8: the built-in beds are gone, beds are placed pieces now; checked only where a design still has one)
            if (def.Fixture("bed-captain") != null) Check(ship.GetSeat("bunk:bed-captain") != null, "the captain's bed is a bunk");
            // the ship's firehearth (0.3.6): the vanilla firepit it's drawn with is in the game, its bricks stand solid round
            // the fire (nobody steps into it), in the forecastle (the fire itself: HearthWorks)
            var hearthF = def.Fixture("hearth");
            if (hearthF != null)
                Check(sapi.World.GetBlock(new AssetLocation(hearthF.Block ?? "")) is Block pit && pit.Id != 0 && hearthF.Fire?.Length == 3 &&
                      ship.Grid.SolidAt(hearthF.Fire[0], hearthF.Fire[1], hearthF.Fire[2]) && def.RoomAt(hearthF.Pos[0], hearthF.Pos[1] + 0.5, hearthF.Pos[2])?.Code == "forecastle",
                    $"the firehearth in the forecastle: a vanilla firepit to stand in it ({hearthF.Block}), its bricks solid round the fire");
            // her figurehead and her name
            var fh = def.Fixtures.FirstOrDefault(f => f.Type == "figurehead");
            // (0.3.9, note 66: no figurehead on hull R)
            Check(fh == null || ship.FigureheadGroup(fh) != null, fh == null ? "no figurehead (note 66)" : "she carries her figurehead");
            if (fh != null)
            {
                ship.NextFigurehead(fh);
                Check(ship.Figurehead == "none" && string.IsNullOrEmpty(ship.FigureheadGroup(fh)), "the next figurehead is none");
                ship.NextFigurehead(fh);
                Check(ship.Figurehead == "mermaid", "and round to the mermaid again");
            }
            Check(ship.SetName("Santa Clara") == null && ship.ShipName == "Santa Clara" && ship.GetName() == "Santa Clara" &&
                  ship.WatchedAttributes.GetTreeAttribute("hansaFit")?.GetString("name") == "Santa Clara", "she can be named, and her name is saved");
            Check(ship.SetName(new string('x', EntityShip.MaxNameLength + 1)) != null && ship.ShipName == "Santa Clara", "a name too long for the board is refused");
        }

        /// <summary>
        /// The spawn point by the helm, with a stand-in player (there's no client here): the plate put up, a spawn bound to
        /// her (the one they had kept), vanilla's spawn position following her when she moves a few blocks (not one),
        /// a spawn set elsewhere since ending it, unbinding, the plate taken down, her gone while they're away (given back
        /// when they join), switched off, and the bindings saved.
        /// </summary>
        void ShipSpawnChecks(HansaMod mod, EntityShip ship, ShipDefinition def)
        {
            var sp = mod.Spawns;
            var plateF = def.Fixtures.FirstOrDefault(f => f.Type == "attach" && f.Kind == "plate");
            var plateItem = sapi.World.GetItem(new AssetLocation("game", "metalplate-copper"));
            Check(sp != null && plateF?.Spawn != null && plateItem != null, $"the spawn point: the plate's place by the helm and a vanilla metal plate ({plateItem?.Code})");
            if (sp == null || plateF?.Spawn == null || plateItem == null) return;
            Check(ship.TryAttach(plateF, new ItemStack(plateItem)) == null && ship.Attached.ContainsKey(plateF.Code), "a metal plate put up on the wall by the helm");
            var chest = sapi.World.GetBlock(new AssetLocation("game", "chest-east"));
            Check(chest == null || !ShipFittings.Accepts(plateF, new ItemStack(chest)), "a chest doesn't go there");

            var fake = new StandIn();
            ISpawnHolder player = fake;
            bool online = true;
            var findBefore = sp.FindPlayer;
            sp.FindPlayer = uid => uid == fake.Uid ? (online ? player : null) : findBefore(uid);
            try
            {
                PlayerSpawnPos Where(double dx = 0)
                {
                    double baseY = double.IsNaN(ship.BaseY) ? ship.Pos.Y : ship.BaseY;
                    ShipFittings.SpawnBlock(plateF.Spawn, ship.Pos.X + dx, baseY, ship.Pos.Z, ship.Pos.Yaw, out int x, out int y, out int z);
                    return new PlayerSpawnPos(x, y, z);
                }
                bool At(PlayerSpawnPos a, PlayerSpawnPos b) => a != null && b != null && a.x == b.x && a.y == b.y && a.z == b.z;
                string uid = fake.Uid;
                // they had a spawn of their own (a temporal gear's, 3 uses left)
                fake.Spawn = new PlayerSpawnPos(10, 50, 20) { RemainingUses = 3 };
                sp.Toggle(player, ship, plateF);
                sp.Registry.Players.TryGetValue(uid, out var b);
                Check(b != null && b.ShipId == ship.EntityId && b.Previous?.x == 10 && b.Previous.RemainingUses == 3 && ship.SpawnBound.Contains(uid),
                    "right-clicked empty-handed, the plate binds the spawn to her, and keeps the one they had");
                Check(At(fake.Spawn, Where()) && fake.Spawn.RemainingUses == -1 && fake.Said.Count > 0,
                    $"vanilla's spawn position is the spot by the plate ({fake.Spawn?.x}, {fake.Spawn?.y}, {fake.Spawn?.z}), for good, and they're told");
                Check(ship.WatchedAttributes.GetTreeAttribute("hansaSpawn")?.HasAttribute(uid) == true, "clients know whose spawn is bound to her (for the plate's look-at text)");

                // she sails on: vanilla's spawn follows her once she's more than a couple of blocks off (not for one)
                double x0 = ship.Pos.X;
                int sets = fake.SpawnSets;
                ship.Pos.X = x0 + 1;
                sp.Tick();
                bool stayed = fake.SpawnSets == sets;
                ship.Pos.X = x0 + 10;
                sp.Tick();
                bool followed = At(fake.Spawn, Where());
                ship.Pos.X = x0;
                sp.Tick();
                Check(stayed && followed && At(fake.Spawn, Where()), "vanilla's spawn position follows her as she moves (10 blocks: moved; 1: left alone), and back");

                // they used a temporal gear since: that's their spawn now, the binding ends
                fake.Spawn = new PlayerSpawnPos(1, 2, 3);
                ship.Pos.X = x0 + 10;
                sp.Tick();
                ship.Pos.X = x0;
                Check(!sp.Registry.Players.ContainsKey(uid) && At(fake.Spawn, new PlayerSpawnPos(1, 2, 3)) && !ship.SpawnBound.Contains(uid),
                    "a spawn they set elsewhere since (a temporal gear) ends it, and stays");

                // bound, then unbound: the one they had comes back
                sp.Toggle(player, ship, plateF);
                bool bound = sp.Registry.Players.ContainsKey(uid) && At(fake.Spawn, Where());
                sp.Toggle(player, ship, plateF);
                Check(bound && !sp.Registry.Players.ContainsKey(uid) && At(fake.Spawn, new PlayerSpawnPos(1, 2, 3)), "right-clicked again, unbound: their own spawn is back");

                // the plate taken down: every spawn bound to her ends
                sp.Toggle(player, ship, plateF);
                ship.TakeBack(null, plateF);
                Check(!sp.Registry.Players.ContainsKey(uid) && At(fake.Spawn, new PlayerSpawnPos(1, 2, 3)) && !ship.SpawnBound.Contains(uid) &&
                      fake.Said.LastOrDefault()?.Contains("plate") == true, $"the plate taken down: the spawn goes back, and they're told ({fake.Said.LastOrDefault()})");
                ship.TryAttach(plateF, new ItemStack(plateItem));

                // she's gone while they're away: when they come in, their own spawn is back
                sp.Toggle(player, ship, plateF);
                online = false;
                sp.EndShip(ship.EntityId, ShipSpawns.EndGone);
                bool waiting = sp.Registry.Players.TryGetValue(uid, out var ended) && ended.Ended == ShipSpawns.EndGone && At(fake.Spawn, Where());
                online = true;
                sp.OnJoin(player);
                Check(waiting && !sp.Registry.Players.ContainsKey(uid) && At(fake.Spawn, new PlayerSpawnPos(1, 2, 3)),
                    "she's gone while they're away: it waits, and their own spawn is back when they join");

                // saved with the world
                sp.Toggle(player, ship, plateF);
                var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<ShipSpawnRegistry>(System.Text.Encoding.UTF8.GetString(sp.Serialize()));
                Check(saved?.Players.TryGetValue(uid, out var sb) == true && sb.ShipId == ship.EntityId && sb.Previous?.x == 1 && saved.Ships.ContainsKey(ship.EntityId),
                    "the bindings are saved with the world");
                sp.Toggle(player, ship, plateF);

                // switched off: the plate does nothing
                mod.Config.ShipSpawn = false;
                sp.Toggle(player, ship, plateF);
                Check(!sp.Registry.Players.ContainsKey(uid) && At(fake.Spawn, new PlayerSpawnPos(1, 2, 3)), "with ShipSpawn off the plate binds nothing");
            }
            catch (Exception e)
            {
                Check(false, "the spawn point checks ran through: " + e);
            }
            finally
            {
                mod.Config.ShipSpawn = true;
                sp.FindPlayer = findBefore;
                sp.Registry.Players.Remove(fake.Uid);
                ship.SetSpawnBound(fake.Uid, null, false);
            }
        }

        /// <summary>
        /// The dry hull (item 18): a chicken on the cargo deck in the hold, its head below the pool's water level,
        /// breathes; one just outside the hull at the same depth doesn't. Sea's own check, with a real hull.
        /// </summary>
        void DryHold(EntityShip ship, ShipDefinition def)
        {
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            EntityProperties type = null;
            foreach (string code in new[] { "chicken-rooster", "chicken-hen", "chicken-adult", "chicken-baby" })
                if ((type = sapi.World.GetEntityType(new AssetLocation("game", code))) != null) break;
            if (sea == null || type == null || def.DryMask == null)
            {
                Check(false, $"dry hold: Sea ({sea != null}), a chicken ({type != null}) and the hull's mask ({def.DryMask != null})");
                return;
            }
            Entity Chicken(double lx, double ly, double lz)
            {
                ship.LocalToWorld(lx, ly, lz, out double x, out double y, out double z);
                var e = sapi.World.ClassRegistry.CreateEntity(type);
                e.Pos.SetPos(x, y, z);
                e.Pos.Dimension = ship.Pos.Dimension;
                sapi.World.SpawnEntity(e);
                return e;
            }
            var inside = Chicken(4.0, -1.75, 0.0);
            var outside = Chicken(4.0, -1.75, def.WaterClip.HalfWidth + 0.5);
            var bi = inside.GetBehavior<Vintagestory.GameContent.EntityBehaviorBreathe>();
            var bo = outside.GetBehavior<Vintagestory.GameContent.EntityBehaviorBreathe>();
            if (bi == null || bo == null) { Check(false, "dry hold: the chickens breathe (have the breathe behavior)"); }
            else
            {
                bi.Check();
                bo.Check();
                var ie = inside.Pos.XYZ.Add(inside.LocalEyePos);
                double surface = EntityShip.FindWaterSurface(sapi.World, inside.Pos.AsBlockPos, 6, 6);
                Check(ie.Y < surface && sea.IsDry(ie.X, ie.Y, ie.Z), $"in the hold a chicken's head is below the water level outside ({ie.Y:0.00} < {surface:0.00}), and Sea knows it's dry there");
                Check(bi.HasAir && !bo.HasAir, $"the chicken in the hold breathes ({bi.HasAir}), the one just outside the hull at the same depth doesn't ({!bo.HasAir})");
            }
            inside.Die(EnumDespawnReason.Removed);
            outside.Die(EnumDespawnReason.Removed);
            // on deck and in the castles too (a big sea dips her decks under the world's water level), not beside her
            ship.LocalToWorld(2.0, def.DeckY + 1.0, 0.0, out double dx1, out double dy1, out double dz1);
            ship.LocalToWorld(-6.0, def.DeckY + 4.0, 0.0, out double dx2, out double dy2, out double dz2);
            ship.LocalToWorld(2.0, def.DeckY + 1.0, def.Beam / 2 + 1.5, out double dx3, out double dy3, out double dz3);
            Check(sea.IsDry(dx1, dy1, dz1) && sea.IsDry(dx2, dy2, dz2) && !sea.IsDry(dx3, dy3, dz3),
                "dry on her deck and in her castles whatever the sea does, but not beside her");
            // (0.3.13, the bubbles in the cargo deck) over a water speck's life: just outside her side at the hold's depth
            // her roll sweeps it into her (Sea asks IsDryWithin for its particles), well clear of her not
            {
                var dm = def.DryMask;
                int j = dm.CellY(-2.5), i = dm.CellX(4.0), k = dm.NZ - 1;
                while (k > 0 && !dm.Solid(i, j, k)) k--;
                double side = dm.OZ + (k + 1) * dm.V;
                ship.LocalToWorld(4.0, -2.5, side + 0.3, out double sx1, out double sy1, out double sz1);
                ship.LocalToWorld(4.0, -2.5, side + 2.0, out double sx2, out double sy2, out double sz2);
                Check(!sea.IsDry(sx1, sy1, sz1) && sea.IsDryWithin(sx1, sy1, sz1, 2) && !sea.IsDryWithin(sx2, sy2, sz2, 2),
                    $"a water speck 0.3 outside her side (z {side:0.00}) isn't in her now but would be within its life (her roll), 2 out never");
            }
        }

        /// <summary>
        /// Ships' shadows are drawn on clients only (ShipRenderer.RenderShadows); here, for every design this server
        /// loaded: the sphere a ship is tested against for the shadow maps holds all her voxels and fixtures, and what goes
        /// into the maps (ShipShadows) leaves out everything inside her and keeps the big things in the far map.
        /// </summary>
        void ShadowPlans(HansaMod mod)
        {
            foreach (string code in mod.ShipCodes.OrderBy(c => c))
            {
                var d = mod.GetShip(code);
                if (d?.Grid == null) continue;
                ShipShadows.Sphere(d, out double cx, out double cy, out double cz, out double r);
                var g = d.Grid;
                double spare = double.MaxValue;
                for (int i = 0; i < g.NX; i++)
                    for (int j = 0; j < g.NY; j++)
                        for (int k = 0; k < g.NZ; k++)
                        {
                            if (!g.Solid(i, j, k)) continue;
                            double x = g.OX + (i + 0.5) * g.V - cx, y = g.OY + (j + 0.5) * g.V - cy, z = g.OZ + (k + 0.5) * g.V - cz;
                            spare = Math.Min(spare, r - g.V - Math.Sqrt(x * x + y * y + z * z));
                        }
                foreach (var f in d.Fixtures)
                {
                    if (f.Pos == null) continue;
                    double x = f.Pos[0] - cx, y = f.Pos[1] - cy, z = f.Pos[2] - cz;
                    spare = Math.Min(spare, r - Math.Sqrt(x * x + y * y + z * z));
                }
                Check(spare >= 0, $"{code}: the sphere her shadow is culled with holds her voxels and fixtures (radius {r:0.0}, {spare:0.0} to spare)");

                bool ok = ShipShadows.CastsSkin("exterior", true) && ShipShadows.CastsSkin("bottom", true) && ShipShadows.CastsSkin("bottomclad", true) &&
                          !ShipShadows.CastsSkin("interior", false) && !ShipShadows.CastsSkin("glass", false);
                int far = 0, near = 0;
                foreach (var p in d.MovingParts)
                {
                    bool pf = ShipShadows.CastsPart(p, true), pn = ShipShadows.CastsPart(p, false);
                    if (pf) far++;
                    if (pn) near++;
                    if (p.Interior && pn || pf && !pn) ok = false;
                    if (!p.Interior && (p.Code == "rudder" || p.Code.StartsWith("yard-") || p.Code.StartsWith("gate-")) && !pf) ok = false;
                }
                foreach (var f in d.Fixtures)
                {
                    // as ShipRenderer marks the vanilla blocks it draws; a ship with a hold keeps her cargo out of the maps
                    bool indoors = f.Type is "prop" or "bed" or "attach" && d.Indoors(f);
                    if (ShipShadows.CastsFixture(f, indoors, f.Type == "window", false) && (indoors || f.Kind == "cargo" && d.Fixture("hold") != null)) ok = false;
                    if (f.Type == "door" && !ShipShadows.CastsFixture(f, false, false, true)) ok = false;
                }
                Check(ok, $"{code}: her shadow leaves out everything inside her (the hold's cargo too), and keeps the hull, rudder, yards, gates and doors in the far map (parts far {far}, near {near} of {d.MovingParts.Length})");
            }
        }

        /// <summary>
        /// The damage drawn on a ship is the client's (ShipRenderer); here, for every design this server loaded, from the mod's
        /// own assets as shipped: her mesh marks her planking and sorts it into panels, a holed panel's hole is cut out of it,
        /// and every overlay it asks for is there.
        /// </summary>
        void DamageLooks(HansaMod mod)
        {
            foreach (string code in mod.ShipCodes.OrderBy(c => c))
            {
                var d = mod.GetShip(code);
                if (d?.DamageParts == null || d.Shapes?.Skin == null) continue;
                var asset = sapi.Assets.TryGet(AssetLocation.Create(d.Shapes.Skin, "hansa"));
                var file = asset == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<ShipSkinFile>(System.Text.Encoding.UTF8.GetString(asset.Data));
                // ((0.3.26, step D1) a design with hull panels draws her damage on their slot groups: no hullOut, no jagged holes)
                if (d.Hull != null)
                {
                    var plan = file == null ? null : new SlotMeshPlan(d.Hull, file, SlotMeshPlan.HullGroups);
                    Check(file != null && file.HullOut == null && plan.Problems.Count == 0 && SlotMeshPlan.HullGroups.All(g => file.Groups.ContainsKey(g)),
                        $"{code}: her mesh has her {d.Hull.Count} hull panels' groups (no jagged holes: the panels are drawn out whole)");
                    continue;
                }
                var hs = file?.HullOut != null ? new HullDamageSkin(file, d.DamageParts) : null;
                if (hs == null || !hs.Usable) { Check(false, $"{code}: her mesh marks her planking, to draw damage on"); continue; }
                var l = d.DamageParts;
                var st = new DamageState(l.Count);
                var panel = l.Parts.Where(p => p.Kind == DamageKind.Hull).OrderBy(p => Math.Abs(p.Center.X) + Math.Abs(p.Center.Y - d.Waterline)).First();
                st.Hurt[panel.Index] = 0.9f;
                var looks = hs.Looks(st);
                int before = 0, after = 0, built = 0;
                foreach (int c in hs.Affected(null, looks))
                {
                    before += hs.SoundQuads(hs.Chunks[c]).Count;
                    after += hs.Build(hs.Chunks[c], looks).Count;
                    built++;
                }
                bool textures = hs.Textures.All(t => !t.Contains("++0~hansa:block/dmg-") ||
                    sapi.Assets.TryGet(new AssetLocation("hansa", "textures/block/" + t.Substring(t.LastIndexOf('/') + 1) + ".png")) != null);
                Check(built > 0 && after > before && textures,
                    $"{code}: her planking in {hs.Chunks.Count} chunks by panel; a hole in {panel.Part} rebuilds {built} of them ({before} → {after} quads: cut round, and its reveal), its overlays shipped");
            }
        }

        // ------------------------------------------------------------------ damage, flooding, sinking

        /// <summary>A deep pool beside the first (z from spawn + DeepZ0 to spawn + DeepZ1, its floor DeepDepth under the first's), to sink a carrack in.</summary>
        const int DeepZ0 = 18, DeepZ1 = 50, DeepHalfX = 34, DeepDepth = 15;

        /// <summary>Wait (up to seconds) till cond holds, ticking; then go on, saying whether it did.</summary>
        void WaitFor(Func<bool> cond, double seconds, Action<bool> then)
        {
            long started = sapi.World.ElapsedMilliseconds, id = 0;
            id = sapi.Event.RegisterGameTickListener(_ =>
            {
                bool ok = false;
                try { ok = cond(); } catch (Exception e) { Log("exception while waiting: " + e.Message); ok = true; }
                if (!ok && sapi.World.ElapsedMilliseconds - started < seconds * 1000) return;
                sapi.Event.UnregisterGameTickListener(id);
                then(ok);
            }, 100);
        }

        /// <summary>Running into the pool's wall under full sail holed her bow (and nothing aft of it); then she's mended for the rest.</summary>
        void GroundingDamage(EntityShip ship, ShipDefinition def, double speed)
        {
            var l = def.DamageParts;
            var hurt = Enumerable.Range(0, l.Count).Where(i => l.Parts[i].Kind == DamageKind.Hull && ship.StateOfPart(i) >= PartState.Scraped).ToList();
            var worst = hurt.Count > 0 ? hurt.Max(i => ship.StateOfPart(i)) : PartState.Sound;
            // (18 October 2026) as heavy as she is: a carrack's thousand tonnes crack her bow at this speed, a light ship's
            // scrape it
            var expect = l.Displacement >= 600 ? PartState.Cracked : PartState.Scraped;
            Check(hurt.Count > 0 && hurt.All(i => l.Parts[i].Center.X > def.Length * 0.2) && worst >= expect,
                $"running into the wall at {speed:0.0} b/s hurt her bow where she touched: {hurt.Count} panels, the worst {worst}, nothing aft");
            Check(ship.WatchedAttributes.GetBytes("hansaDmg")?.Length == 2 * l.Count && ship.Attributes.GetTreeAttribute("hansaDmgF") != null,
                "the damage is synced to clients (a byte a part) and saved");
            ship.RepairAll();
            Check(Enumerable.Range(0, l.Count).All(i => ship.StateOfPart(i) == PartState.Sound) && ship.Water == 0, "/hansa repairall mends her and pumps her dry");
        }

        /// <summary>
        /// A carrack holed below the waterline in the (shallow) pool: water comes in, she floats deeper and lists toward
        /// the hole; mended with vanilla items taken from an inventory; two at the pump bring the water down; flooded full
        /// she comes to rest on the bottom with her deck above the water (foundered, not sunk), and floats off pumped dry.
        /// Then one sunk in a deep pool (DeepSinking).
        /// </summary>
        void Flooding(HansaMod mod, ShipDefinition def, BlockPos spawn, double sx, double sz, double wy)
        {
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            sea?.SetOverride(new Sea.WindOverride { FromDegrees = 0, Strength = 0.02, StartHours = sea.Hours - 24 });
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", cfg.Ship));
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(sx, wy, sz);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            var l = def.DamageParts;
            Check(mod.Config.Damage && ship.PartDamage?.Count == l.Count && ship.Stage == SinkStage.Afloat && ship.Water == 0 && ship.Sinkage == 0,
                $"a new ship is sound and dry ({l.Count} parts: hull panels, decks, the rudder, masts, sails, rigging)");
            // a hole below the waterline, port side amidships
            // the port panel nearest 2 blocks under the waterline amidships (any ship's depth, not only the carrack's)
            var panel = l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Part.Side < 0 && p.Center.Y < def.Waterline - 0.5 && Math.Abs(p.Center.X) < 4)
                .OrderBy(p => Math.Abs(p.Center.Y - (def.Waterline - 2.0))).ThenBy(p => Math.Abs(p.Center.X)).FirstOrDefault();
            if (panel == null)
            {
                Check(false, "a hull panel below the waterline amidships to hole");
                return;
            }
            int i = panel.Index;
            ship.DamagePart(i, 0.85f);
            Check(ship.StateOfPart(i) == PartState.Holed && ship.WatchedAttributes.GetBytes("hansaDmg")?[2 * i] == DamageState.Q(ship.PartDamage.Hurt[i]),
                $"a hole below the waterline ({ship.PartName(i)}): holed, and clients get it");
            WaitFor(() => false, 10, w10 =>
            {
                Check(ship.Water > 1 && ship.WaterRate > 0.05, $"water comes in through the hole: {ship.Water:0.0} t in 10 s ({ship.WaterRate * 1000:0} l/s)");
                Check(ship.Sinkage > 0.002 && Math.Abs(ship.Pos.Y - (ship.BaseY - ship.Sinkage + ship.Heave)) < 1e-9 &&
                      ship.WatchedAttributes.GetTreeAttribute("hansa")?.GetDouble("sink") == ship.Sinkage,
                    $"she floats deeper for it ({ship.Sinkage:0.000}), on the server as on clients");
                Check(ship.ListAngle < 0, $"and lists to port, where it came in ({ship.ListAngle * GameMath.RAD2DEG:0.00}°)");
                // (18 October 2026) a heavy load for her size: the carrack's 150 t, or 40 % of a smaller ship's reserve buoyancy
                double load = Math.Min(150, 0.4 * l.Reserve);
                ship.Flood(load);
                WaitFor(() => false, 6, w6 =>
                {
                    Check(ship.Sinkage > Math.Min(0.3, 0.3 * load / 150 * 2) && ship.ListAngle * GameMath.RAD2DEG < -1 && ship.SpeedFactorNow < 0.85 && ship.Stage <= SinkStage.Foundering,
                        $"with {load:0} t more in her: {ship.Sinkage:0.00} deeper, {ship.ListAngle * GameMath.RAD2DEG:0.0}° over, {ship.SpeedFactorNow:0.00} of her speed; still afloat ({ship.Stage}: the hole lets in {ship.WaterRate:0.00} t/s)");
                    ship.LocalToWorld(0, l.HoldBottom + 0.3, 0, out double hx, out double hy, out double hz);
                    ship.LocalToWorld(0, ship.FloodLevel + 1.0, 0, out double ax, out double ay, out double az);
                    Check(sea != null && !sea.IsDry(hx, hy, hz) && sea.IsDry(ax, ay, az), "in her hold it's wet under the water that's in her, and dry above it");
                    // the water's surface is level (world): wet just under it and dry just over it at her sides and ends alike,
                    // as it's drawn; Sea keeps the sea under it (her water clip)
                    double fy = ship.FloodWorldY, fx = ShipWater.FloodX(def);
                    int level = 0, spots = 0, cut = 0;
                    double kx = Math.Min(1, def.Length / 36.4), kz = Math.Min(1, def.Beam / 12.16);   // the carrack's spots, to her size
                    foreach (var (dx0, dz0) in new[] { (0.0, 0.0), (-4.0, 1.8), (4.0, -1.8), (6.0, 1.5) })
                    {
                        double dx = dx0 * kx, dz = dz0 * kz;
                        ship.LocalToWorld(fx + dx, ship.FloodLevel, dz, out double sx2, out _, out double sz2);
                        spots++;
                        if (!sea.IsDry(sx2, fy - 0.15, sz2) && sea.IsDry(sx2, fy + 0.15, sz2)) level++;
                        var clip = ShipWater.Clip(def, ship.Pos.X, ship.Pos.Y, ship.Pos.Z, ship.Pos.Yaw, 0, 0, 0, 1.5f, fy);
                        if (!clip.InDryHull(sx2, fy - 0.15, sz2) && clip.InDryHull(sx2, fy + 0.15, sz2)) cut++;
                    }
                    ShipWater.Plane(0, 0, out double fAlong, out double fAcross);
                    var surface = ShipWater.Surface(def, ship.FloodLevel, fAlong, fAcross, ShipWater.DefaultInset);
                    Check(level == spots && cut == spots && surface.Count > 0,
                        $"the water in her is level: wet just under {fy - ship.Pos.Y:0.00} over her origin and dry over it, at her middle, sides and ends ({level} of {spots}); " +
                        $"the sea shows under it in Sea's cut ({cut}), and its surface is drawn ({surface.Sum(r => r.Count)} stations)");

                    // mended with vanilla items from an inventory: the planks and nails taken as planned
                    var inv = new InventoryGeneric(4, "hansa-selftest-repair", "0", sapi);
                    inv[0].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "plank-oak")), 3);
                    inv[1].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "metalnailsandstrips-iron")), 5);
                    inv[2].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "plank-pine")), 2);
                    string m1 = ship.Repair(i, inv.ToList(), "game:plank-oak", false, out var job, out _);
                    if (job?.What == "lay")
                    {
                        // (a design with hull panels as slots, carrack-m's or a grid ship's: a panel holed through is out, and laid again)
                        int planksLeft = (inv[0].Itemstack?.StackSize ?? 0) + (inv[2].Itemstack?.StackSize ?? 0);
                        Check(ship.PartDamage.Hurt[i] == 0 && planksLeft < 5 && ship.StateOfPart(i) == PartState.Sound && ship.WatchedAttributes.GetBytes("hansaDmg")[2 * i] == 0,
                            $"her panel holed through is out, and laid again from an inventory's planks ({m1}: {5 - planksLeft} planks); then it's sound");
                    }
                    else Check(job?.What == "planks" && inv[0].Empty && inv[1].StackSize == 3 && inv[2].StackSize == 1 && ship.PartDamage.Hurt[i] == 0 &&
                          ship.WatchedAttributes.GetBytes("hansaDmg")[2 * i] == 0,
                        $"mended with 4 planks and 2 nails and strips from an inventory, the held planks first ({m1})");
                    if (job?.What != "lay")
                    {
                    var inv2 = new InventoryGeneric(2, "hansa-selftest-caulk", "0", sapi);
                    inv2[0].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "flaxfibers")), 1);
                    string m2 = ship.Repair(i, inv2.ToList(), "game:flaxfibers", false, out var none, out _);
                    inv2[1].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "resin")), 4);
                    string m3 = ship.Repair(i, inv2.ToList(), "game:flaxfibers", false, out var caulk, out _);
                    Check(none == null && m2 == "hansa:repair-needs" && caulk?.What == "caulk" && inv2[0].Empty && inv2[1].StackSize == 3 &&
                          ship.StateOfPart(i) == PartState.Sound && ship.Repair(i, inv2.ToList(), "game:resin", false, out _, out _) == "hansa:repair-nothing",
                        $"its seams caulked with flax fibres and resin (not without the resin: {m2}); then it's sound and needs nothing");
                    }

                    // two at the pump's brake bring the water down
                    var pump = def.Fixtures.First(f => f.Type == "pump");
                    double w0 = ship.Water;
                    int strokes = 0;
                    long pid = 0;
                    pid = sapi.Event.RegisterGameTickListener(tick =>
                    {
                        ship.PumpStroke("selftest-a", pump);
                        ship.PumpStroke("selftest-b", pump);
                        ship.PumpStroke("selftest-c", pump);   // (a third can't get at the brake)
                        if (++strokes < 10) return;
                        sapi.Event.UnregisterGameTickListener(pid);
                        WaitFor(() => false, 0.5, w05 =>
                        {
                            Check(ship.Pumpers == 2 && ship.Water < w0 - 0.12 && ship.WaterRate < -0.012,
                                $"two at the pump bring the water down ({w0:0.0} → {ship.Water:0.0} t in 10 s, {ship.WaterRate * 1000:0} l/s; a third can't get at the brake)");
                            // (the eighth client test, note 39: what comes up gushes out of its spout on clients)
                            float told = ship.WatchedAttributes.GetTreeAttribute("hansa")?.GetFloat("pumped") ?? 0;
                            Check(ship.PumpedRate > 0.05 && ship.PumpsWorked == pump.Code && told > 0.05,
                                $"what the pump brings up is told to clients for its spout's water ({ship.PumpedRate * 1000:0} l/s out of '{ship.PumpsWorked}', synced {told * 1000:0})");
                            // flooded full in shallow water: she comes to rest on the bottom, her deck above the water
                            // (18 October 2026) a ship drawing less than the carrack would go under in the pool's 4.9 of water
                            // before she touched: a shoal of stone under her, whole blocks, till what's under her keel is less
                            // than her freeboard (taken away again when she has floated off)
                            var shoal = new List<BlockPos>();
                            {
                                var ba = sapi.World.BlockAccessor;
                                var stoneB = sapi.World.GetBlock(new AssetLocation("game", "rock-granite"));
                                int fx = (int)Math.Floor(ship.Pos.X), fz = (int)Math.Floor(ship.Pos.Z);
                                int fy = (int)Math.Floor(wy);
                                while (fy > wy - 12 && ba.GetBlock(new BlockPos(fx, fy, fz, 0)).Id != stoneB.Id) fy--;
                                double freeboard = def.DeckY - def.Waterline, under = wy - (fy + 1) - def.Draft;
                                int layers = 0;
                                while (under - layers > 0.6 * freeboard) layers++;
                                int hx = (int)Math.Ceiling(def.Length / 2) + 2, hz = (int)Math.Ceiling(def.Beam / 2) + 2;
                                for (int x = fx - hx; x <= fx + hx; x++)
                                    for (int z = fz - hz; z <= fz + hz; z++)
                                        for (int y = fy + 1; y <= fy + layers; y++)
                                        {
                                            var bp = new BlockPos(x, y, z, 0);
                                            if (ba.GetBlock(bp, BlockLayersAccess.Fluid).Id == 0) continue;   // (the pool's walls)
                                            ba.SetBlock(0, bp, BlockLayersAccess.Fluid);
                                            ba.SetBlock(stoneB.Id, bp);
                                            shoal.Add(bp);
                                        }
                                if (layers > 0) Log($"a shoal {layers} high under her: {under - layers:0.00} under her keel, her freeboard {freeboard:0.00}");
                            }
                            ship.Flood(l.Capacity);
                            WaitFor(() => ship.OnBottom && ship.Stage == SinkStage.Foundering, 20, ok =>
                            {
                                Check(ok && ship.Aground && ship.Sinkage < def.DeckY - def.Waterline && !ship.GoneDown,
                                    $"flooded in shallow water she settles onto the bottom ({ship.Sinkage:0.00} deeper), her deck above the water: foundered, not sunk ({ship.Stage})");
                                ship.RepairAll();
                                WaitFor(() => !ship.OnBottom && ship.Sinkage < 0.05, 20, ok2 =>
                                {
                                    Check(ok2 && ship.Stage == SinkStage.Afloat, $"pumped dry, she floats off again ({ship.Sinkage:0.00}, {ship.Stage})");
                                    ship.Die(EnumDespawnReason.Removed);
                                    var waterB = sapi.World.GetBlock(new AssetLocation("game", "water-still-7"));
                                    foreach (var bp in shoal)
                                    {
                                        sapi.World.BlockAccessor.SetBlock(0, bp);
                                        sapi.World.BlockAccessor.SetBlock(waterB.Id, bp, BlockLayersAccess.Fluid);
                                    }
                                    DeepSinking(mod, def, spawn, wy);
                                });
                            });
                        });
                    }, 1000);
                });
            });
        }

        /// <summary>
        /// A carrack flooded in deep water goes down: she settles and lists, those aboard are put off, the rowing boat on her
        /// gallows floats free, the spawns bound to her end, she's wet inside; she sinks to the bottom, where the wreck's
        /// hook gets her, and (with Wrecks off: the wrecks are the gun pool's, Broadsides) she lies there frozen with her
        /// cargo in her.
        /// </summary>
        void DeepSinking(HansaMod mod, ShipDefinition def, BlockPos spawn, double wy)
        {
            bool wrecks = mod.Config.Wrecks;
            mod.Config.Wrecks = false;
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            var water = sapi.World.GetBlock(new AssetLocation("game", "water-still-7"));
            var stone = sapi.World.GetBlock(new AssetLocation("game", "rock-granite"));
            var ba = sapi.World.BlockAccessor;
            var bp = new BlockPos(0);
            int surf = (int)Math.Floor(wy), floor = surf - 5 - DeepDepth;
            int x0 = spawn.X - DeepHalfX, x1 = spawn.X + DeepHalfX, z0 = spawn.Z + DeepZ0, z1 = spawn.Z + DeepZ1;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    bool wall = x == x0 || x == x1 || z == z0 || z == z1;
                    for (int y = floor; y <= surf + 2; y++)
                    {
                        bp.Set(x, y, z);
                        if (y == floor || wall) ba.SetBlock(stone.Id, bp);
                        else if (y <= surf) { ba.SetBlock(0, bp); ba.SetBlock(water.Id, bp, BlockLayersAccess.Fluid); }
                        else ba.SetBlock(0, bp);
                    }
                }
            Log($"deep pool built {x1 - x0 + 1} x {z1 - z0 + 1}, {surf - floor} deep");
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", cfg.Ship));
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            double cx = spawn.X + 0.5 - 4, cz = (z0 + z1) / 2.0 + 0.5;
            ship.Pos.SetPos(cx, wy, cz);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            // a rowing boat on her gallows, a chest with something in it in the hold, someone aboard, a spawn bound to her
            var boatF = def.Fixtures.FirstOrDefault(f => f.Kind == "boat");   // (a ship without gallows carries none)
            var rowboat = sapi.World.GetItem(new AssetLocation("hansa", "rowboat-oak"));
            var chest = sapi.World.GetBlock(new AssetLocation("game", "chest-east"));
            // (the chest a piece on a floor place in the hold)
            var chestStack = new ItemStack(chest);
            var holdPlace = ship.Mounts?.Floors.FirstOrDefault(m => m.Room == "hold" &&
                ship.PlaceProblem(PiecePlanner.Plan(ship.Mounts, m, new VanillaPieceBlock(sapi.World, chestStack), 1), true) == null);
            ShipPiece cargoP = null;
            bool stowed = (boatF == null || rowboat != null && ship.TryAttach(boatF, new ItemStack(rowboat)) == null) && holdPlace != null &&
                          ship.PlacePiece(PiecePlanner.Plan(ship.Mounts, holdPlace, new VanillaPieceBlock(sapi.World, chestStack), 1), chestStack, true, out cargoP) == null;
            string cargoCode = cargoP?.StorageCode ?? "-";
            if (stowed)
            {
                ship.Storages[cargoCode][0].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "stick")), 6);
                ship.Storages[cargoCode][0].MarkDirty();
            }
            EntityProperties chickenType = null;
            foreach (string code in new[] { "chicken-rooster", "chicken-hen", "chicken-adult" })
                if ((chickenType = sapi.World.GetEntityType(new AssetLocation("game", code))) != null) break;
            EntityAgent sailor = null;
            if (chickenType != null)
            {
                var b = def.BoardPoints[0];
                ship.LocalToWorld(b[0], b[1], b[2], out double px, out double py, out double pz);
                sailor = (EntityAgent)sapi.World.ClassRegistry.CreateEntity(chickenType);
                sailor.Pos.SetPos(px, py, pz);
                sapi.World.SpawnEntity(sailor);
                sailor.TryMount(ship.GetOrCreateDeckSeat("hansa-selftest-sailor", b[0], b[1], b[2]));
            }
            Check(stowed && sailor?.MountedOn is DeckSeat, "a carrack in a deep pool with a rowing boat on her gallows, a chest in her hold and someone aboard");
            // and her hearth burning (lit with a torch: it catches at the next burn tick)
            if (ship.HearthInv != null)
            {
                ship.HearthInv[0].Itemstack = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "firewood")), 4);
                ship.HearthInv[0].MarkDirty();
                var torch = sapi.World.GetBlock(new AssetLocation("game", "torch-basic-lit-up"));
                if (torch != null) ship.TryIgniteHearth(null, new DummySlot(new ItemStack(torch)), 3.2f, 0);
            }
            bool hearthLit = false;
            var spawns = mod.Spawns;
            spawns?.Registry.Players.Add("hansa-selftest-sunk", new ShipSpawnBinding { ShipId = ship.EntityId, Plate = "plate" });
            EntityShip hooked = null;
            Action<EntityShip> hook = s => hooked = s;
            ShipWreck.Sunk += hook;
            int sunkBefore = ShipWreck.SunkCount;
            double listMax = 0;
            ship.Flood(def.DamageParts.Capacity);
            WaitFor(() => { listMax = Math.Max(listMax, Math.Abs(ship.ListAngle)); hearthLit |= ship.Fire.IsBurning; return ship.GoneDown; }, 40, ok =>
            {
                Check(ok && ship.Sinkage >= def.DeckY - def.Waterline - 0.1, $"flooded in deep water she settles till her deck's at the water, and goes down ({ship.Stage}, {ship.Sinkage:0.00} deeper)");
                if (ship.HearthInv != null) Check(hearthLit && !ship.Fire.IsBurning && ship.Fire.BurnState == "cold" && !ship.Fire.CanIgniteFuel && ship.HearthInv?[0].StackSize > 0 &&
                      ship.WatchedAttributes.GetTreeAttribute("hansaHearth")?.GetString("burnstate") == "cold",
                    $"her hearth was burning; going down, the sea puts it out (cold, no embers; the firewood left in it: {ship.HearthInv?[0].StackSize})");
                Check(sailor != null && sailor.MountedOn == null && ship.Seats.All(s => s.Passenger == null), "those aboard are put off into the water");
                var boat = ship.ReleasedBoats.Count == 1 ? sapi.World.GetEntityById(ship.ReleasedBoats[0]) : null;
                if (boatF != null) Check(boat is EntityRowBoat && boat.Alive && !ship.Attached.ContainsKey(boatF.Code) && Math.Abs(boat.Pos.Y - wy) < 1.5,
                    $"the rowing boat on her gallows floats free, on the water beside her ({boat?.Code})");
                ShipSpawnBinding ended = null;
                spawns?.Registry.Players.TryGetValue("hansa-selftest-sunk", out ended);
                Check(spawns == null || ended?.Ended == ShipSpawns.EndSunk, $"the spawns bound to her end ({ended?.Ended ?? "none"})");
                ship.LocalToWorld(0, def.DamageParts.HoldBottom + 0.5, 0, out double hx, out double hy, out double hz);
                Check(sea != null && !sea.IsDry(hx, hy, hz) && !mod.Dry.IsDry(hx, hy, hz), "her hold isn't dry any more");
                WaitFor(() => ship.Stage == SinkStage.Sunk, 60, sunk =>
                {
                    double keel = ship.Pos.Y + def.KeelY;
                    Check(sunk && hooked == ship && ShipWreck.SunkCount == sunkBefore + 1 && ship.OnBottom && keel < floor + 2.5 && keel > floor - 0.5,
                        $"she sinks to the bottom, and the wreck's hook gets her there (keel at {keel - floor - 1:0.00} over the floor, {ship.Sinkage:0.0} deep; listed {listMax * GameMath.RAD2DEG:0}° going down)");
                    var at = ship.Pos.XYZ;
                    float yaw = ship.Pos.Yaw;
                    WaitFor(() => false, 2, w2 =>
                    {
                        Check(ship.Alive && ship.Pos.XYZ.DistanceTo(at) < 1e-6 && ship.Pos.Yaw == yaw && ship.Speed == 0 && ship.Heave == 0,
                            "on the bottom she lies still: no motion, no sailing, no waves");
                        Check(ship.Storages.TryGetValue(cargoCode, out var inv) && inv[0].Itemstack?.StackSize == 6 && cargoP != null && ship.Pieces.Get(cargoP.Id) != null &&
                              ship.WatchedAttributes.GetTreeAttribute("hansa")?.GetInt("stage") == (int)SinkStage.Sunk,
                            "her cargo is still in her (a diver can open it), and she's saved as sunk");
                        Check(!mod.Wrecks.Busy(ship.EntityId) && !mod.Wrecks.Registry.Done.ContainsKey(ship.EntityId),
                            "with Wrecks off she's no wreck: she lies there as she is (3a)");
                        mod.Config.Wrecks = wrecks;
                        ShipWreck.Sunk -= hook;
                        spawns?.Registry.Players.Remove("hansa-selftest-sunk");
                        foreach (var e in new Entity[] { sailor, sapi.World.GetEntityById(ship.ReleasedBoats.FirstOrDefault()), ship })
                            if (e != null && e.Alive) e.Die(EnumDespawnReason.Removed);
                        sea?.SetOverride(null);
                        CrawlHarbour(mod, spawn, wy, () => Gunnery(mod, def, spawn, wy));
                    });
                });
            });
        }

        // ------------------------------------------------------------------ the guns

        /// <summary>A gun pool on the first one's other side (z from spawn − GunZ1 to spawn − GunZ0, GunDepth deep): one carrack fires at another.</summary>
        const int GunZ0 = 18, GunZ1 = 78, GunDepth = 14;

        /// <summary>
        /// (0.3.25) Crawling under the sweeps (<see cref="Crawl"/>), in the gun pool before the guns use it (91 × 61: room for a
        /// carrack to turn about her middle): a carrack-m heading east in a strong breeze on her beam, under the sweeps, W held
        /// 4 s creeps ahead at about a walk (CrawlSpeed) and makes no leeway; let go, she stops; D held turns her 90° to
        /// starboard about her middle, standing; setting sail takes her off the sweeps.
        /// </summary>
        void CrawlHarbour(HansaMod mod, BlockPos spawn, double wy, Action next)
        {
            // (18 October 2026) a build without carrack-m (the release with the caravel): the ship under test crawls
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", "carrack-m")) ?? sapi.World.GetEntityType(new AssetLocation("hansa", this.cfg.Ship));
            if (type == null) { Check(false, "0.3.25: a ship to crawl"); next(); return; }
            Log($"0.3.25: under the sweeps: {type.Code}");
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            sea?.SetOverride(new Sea.WindOverride { FromDegrees = 0, Strength = 0.6, StartHours = sea.Hours - 24 });
            mod.Config.StopWhenUnmanned = false;
            BuildGunPool(spawn, wy);
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X + 0.5, wy, spawn.Z - 48 + 0.5);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            var cfg = mod.Config;
            void Done()
            {
                ship.CrawlTestInput = null;
                if (ship.Alive) ship.Die(EnumDespawnReason.Removed);
                sea?.SetOverride(null);
                next();
            }
            WaitFor(() => false, 2, _ =>
            {
                ship.Anchored = false;
                ship.SailSet = 0;
                ship.SetCrawl(true);
                ship.CrawlTestInput = (1, 0f);
                var p0 = ship.Pos.XYZ;
                long t0 = sapi.World.ElapsedMilliseconds;
                WaitFor(() => false, 4, __ =>
                {
                    double ahead = ship.Pos.X - p0.X, side = Math.Abs(ship.Pos.Z - p0.Z);
                    Check(ship.Crawl && ahead > 2.5 && ahead < 6.5 && Math.Abs(ship.Speed - cfg.CrawlSpeed) < 0.15 && side < 0.2 && !ship.Aground,
                        $"0.3.25: under the sweeps, W held 4 s: she creeps {ahead:0.0} blocks ahead at {ship.Speed:0.00} b/s (CrawlSpeed {cfg.CrawlSpeed}), " +
                        $"{side:0.00} sideways in a strong breeze on her beam (no leeway, no sail)");
                    ship.CrawlTestInput = (0, 0f);
                    long let = sapi.World.ElapsedMilliseconds;
                    WaitFor(() => Math.Abs(ship.Speed) < 0.02f, 5, stopped =>
                    {
                        double took = (sapi.World.ElapsedMilliseconds - let) / 1000.0;
                        Check(stopped && took < 3.5, $"0.3.25: let go, she stops ({ship.Speed:0.00} b/s after {took:0.0} s)");
                        var c0 = ship.Pos.XYZ;
                        float yaw0 = ship.Pos.Yaw;
                        ship.CrawlTestInput = (0, 1f);
                        long turnAt = sapi.World.ElapsedMilliseconds;
                        double maxSpeed = 0;
                        WaitFor(() => { maxSpeed = Math.Max(maxSpeed, Math.Abs(ship.Speed)); return GameMath.AngleRadDistance(yaw0, ship.Pos.Yaw) <= -Math.PI / 2 + 0.005; }, 30, turned =>
                        {
                            double secs = (sapi.World.ElapsedMilliseconds - turnAt) / 1000.0;
                            ship.CrawlTestInput = (0, 0f);
                            double turn = GameMath.AngleRadDistance(yaw0, ship.Pos.Yaw) * GameMath.RAD2DEG;
                            double drift = ship.Pos.XYZ.HorizontalSquareDistanceTo(c0);
                            Check(turned && !ship.Aground && Math.Sqrt(drift) < 0.5 && maxSpeed < 0.05 && secs > 90 / cfg.CrawlTurn - 1 && secs < 90 / cfg.CrawlTurn + 4,
                                $"0.3.25: D held, standing, she turns {-turn:0.0}° to starboard about her middle in {secs:0.0} s (CrawlTurn {cfg.CrawlTurn}°/s; her middle moved {Math.Sqrt(drift):0.00}, way {maxSpeed:0.00})");
                            WaitFor(() => false, 1.5, ___ =>
                            {
                                Check(Math.Abs(ship.YawRate) < 1e-3, $"0.3.25: let go, she stops turning ({ship.YawRate * GameMath.RAD2DEG:0.00}°/s)");
                                ship.SailSet = 2;
                                WaitFor(() => !ship.Crawl, 2, off =>
                                {
                                    Check(off, "0.3.25: setting sail takes her off the sweeps");
                                    ship.SailSet = 0;
                                    ship.SetCrawl(true);
                                    ship.Anchored = true;
                                    WaitFor(() => !ship.Crawl, 2, off2 =>
                                    {
                                        Check(off2, "0.3.25: and so does letting go the anchor");
                                        Done();
                                    });
                                });
                            });
                        });
                    });
                });
            });
        }

        /// <summary>The gun pool (GunZ0..GunZ1 on the far side of the first one), its water's surface at wy.</summary>
        void BuildGunPool(BlockPos spawn, double wy)
        {
            var water = sapi.World.GetBlock(new AssetLocation("game", "water-still-7"));
            var stone = sapi.World.GetBlock(new AssetLocation("game", "rock-granite"));
            var ba = sapi.World.BlockAccessor;
            var bp = new BlockPos(0);
            int surf = (int)Math.Floor(wy), floor = surf - GunDepth;
            int x0 = spawn.X - 45, x1 = spawn.X + 45, z0 = spawn.Z - GunZ1, z1 = spawn.Z - GunZ0;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    bool wall = x == x0 || x == x1 || z == z0 || z == z1;
                    for (int y = floor; y <= surf + 5; y++)
                    {
                        bp.Set(x, y, z);
                        if (y == floor || wall && y <= surf + 4) ba.SetBlock(stone.Id, bp);
                        else if (y <= surf) { ba.SetBlock(0, bp); ba.SetBlock(water.Id, bp, BlockLayersAccess.Fluid); }
                        else ba.SetBlock(0, bp);
                    }
                }
        }

        /// <summary>
        /// Two carracks in a deep pool, heading the same way 17 apart. The first's port gun kills a chicken on a platform and
        /// its ball goes on into the pool's wall, where it stops (blocks don't break); with CannonsBreakBlocks on, a plank in
        /// the way goes; one fired down into the water splashes and is spent; a gun can't fire again while it's reloading,
        /// nor with Cannons off. Laying a gun (<see cref="Laying"/>). Then her starboard broadsides, laid at the other's
        /// waterline (lower than her guns can be laid: the test gives them the elevation), hole the other below it; water
        /// comes into her, she founders and goes down, and the wreck's hook gets her on the bottom. The one firing takes no
        /// hurt from her own guns.
        /// </summary>
        void Gunnery(HansaMod mod, ShipDefinition def, BlockPos spawn, double wy)
        {
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            sea?.SetOverride(new Sea.WindOverride { FromDegrees = 0, Strength = 0.02, StartHours = sea.Hours - 24 });
            var water = sapi.World.GetBlock(new AssetLocation("game", "water-still-7"));
            var stone = sapi.World.GetBlock(new AssetLocation("game", "rock-granite"));
            var planks = sapi.World.GetBlock(new AssetLocation("game", "planks-oak-ud"));
            var ba = sapi.World.BlockAccessor;
            var bp = new BlockPos(0);
            int surf = (int)Math.Floor(wy), floor = surf - GunDepth;
            int x0 = spawn.X - 45, x1 = spawn.X + 45, z0 = spawn.Z - GunZ1, z1 = spawn.Z - GunZ0;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    bool wall = x == x0 || x == x1 || z == z0 || z == z1;
                    for (int y = floor; y <= surf + 5; y++)
                    {
                        bp.Set(x, y, z);
                        if (y == floor || wall && y <= surf + 4) ba.SetBlock(stone.Id, bp);
                        else if (y <= surf) { ba.SetBlock(0, bp); ba.SetBlock(water.Id, bp, BlockLayersAccess.Fluid); }
                        else ba.SetBlock(0, bp);
                    }
                }
            Log($"gun pool built {x1 - x0 + 1} x {z1 - z0 + 1}, {surf - floor} deep");
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", cfg.Ship));
            double sz = spawn.Z - 58 + 0.5, sx = spawn.X + 0.5;
            EntityShip Launch(double z)
            {
                var s = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
                s.Pos.SetPos(sx, wy, z);
                s.Pos.Yaw = (float)(Math.PI / 2);
                sapi.World.SpawnEntity(s);
                return s;
            }
            var shooter = Launch(sz);
            var target = Launch(sz + 17);
            // (a new ship's guns: a design with gun places starting empty, carrack-m's castles', has only its laid ones)
            bool Laid(FixtureDef f) => !def.GunPlaceOf.TryGetValue(f.Code, out var gp) || gp.StartsLaid;
            var guns = def.Fixtures.Where(f => f.Type == "gun" && Laid(f)).ToArray();
            // (20 October 2026) the lowest deck's first, then aft to fore: the caravel's quarterdeck guns stand 3 higher than
            // her waist's, and one laid down at the chicken would put its ball into the water short of the pool's wall (the
            // carracks' laid guns are all on their main deck: aft to fore, as before)
            var port = guns.Where(f => f.Out[2] < 0).OrderBy(f => f.Pos[1]).ThenBy(f => f.Pos[0]).ToArray();
            var starboard = guns.Where(f => f.Out[2] > 0).OrderBy(f => f.Pos[1]).ThenBy(f => f.Pos[0]).ToArray();
            // (18 October 2026) a design with no guns laid: no gunnery and no broadsides; on to what follows them
            if (port.Length == 0 || starboard.Length == 0)
            {
                Log($"{def.Code} has no guns laid ({guns.Length}): the gunnery, laying and broadside checks are for ships with guns a side");
                foreach (var e in new Entity[] { shooter, target }) if (e.Alive) e.Die(EnumDespawnReason.Removed);
                sea?.SetOverride(null);
                MadeUpWreck(mod, def, spawn, wy, () => CrewWorks(mod, def, spawn, wy, () => SailStrainWorks(mod, def, spawn, wy, () => HearthWorks(mod, def, spawn, wy, () => WorkingBlocksWorks(mod, def, spawn, wy, () => HullPanelsWorks(mod, spawn, wy, Finish))))));
                return;
            }
            // (the carracks have 5 guns a side laid, the caravel 4: two on her quarterdeck, two in her waist)
            Check(guns.Length >= 6 && port.Length * 2 == guns.Length && mod.Shots != null && mod.Config.Cannons && !mod.Config.CannonsBreakBlocks,
                $"two {def.Code}s side by side, 17 apart, {port.Length} guns a side ({string.Join(", ", guns.Select(f => f.Code))}); cannons on, blocks don't break");
            // (the eighth client test) lidded ports: a new ship's guns are housed, their ports shut, and don't fire; opened
            // (someone laying it), one is run out first and fires after; Shift + right-click houses it again. Then all of
            // both ships' are run out at once for what follows.
            var lidded = guns.FirstOrDefault(f => f.Lid != null);
            if (lidded != null)
            {
                bool housedNew = guns.All(f => f.Lid == null || !shooter.GunOpen(f));
                string fireHoused = shooter.FireGun(lidded, null, null);
                shooter.OpenGunPort(lidded);
                bool opening = shooter.GunOpen(lidded) && shooter.GunRunningOut(lidded) > 1;
                string fireRunning = shooter.FireGun(lidded, null, null);
                string house = shooter.HouseGun(lidded);
                bool housedAgain = !shooter.GunOpen(lidded);
                Check(housedNew && fireHoused == "hansa:gun-housed" && opening && fireRunning == "hansa:gun-running-out" && house == null && housedAgain,
                    $"a new ship's guns are housed, their ports shut ({fireHoused}); a port opened, its gun is run out for {EntityShip.RunOutMs / 1000} s first ({fireRunning}); housed again");
            }
            foreach (var s in new[] { shooter, target }) foreach (var f in guns) s.OpenGunPort(f, true);
            var gunBlock = sapi.World.GetBlock(new AssetLocation("hansa", "gun-bronze-north"));
            var gunDrops = gunBlock?.Drops;
            Check(gunBlock != null && sapi.World.GetBlock(new AssetLocation("hansa", "gun-bronze-east")) != null && gunDrops?.Length == 1 &&
                  gunDrops[0].ResolvedItemstack?.Collectible == gunBlock && gunBlock.Attributes?["gun"]["material"].AsString() == "bronze",
                "the gun as a block is there (hansa:gun-bronze-<side>), for a wreck to give back: bronze, dropping itself");
            var shots = mod.Shots;
            double g = def.Cannon.Gravity, v = def.Cannon.MuzzleVelocity;
            // the elevation (degrees) that lays a gun on a world point (its drop allowed for; she lies level in a calm; the
            // muzzle comes down or up with the barrel as it's laid, so a few times over)
            double Lay(EntityShip ship, FixtureDef gun, Vec3d at)
            {
                double e = 0;
                for (int i = 0; i < 4; i++)
                {
                    ship.GunMuzzle(gun, e, out var m, out _);
                    double dx = at.X - m.X, dz = at.Z - m.Z, dist = Math.Sqrt(dx * dx + dz * dz), t = dist / v;
                    e = Math.Atan2(at.Y - m.Y + 0.5 * g * t * t, dist) * 180 / Math.PI;
                }
                return e;
            }
            List<CannonShots.ShotRecord> Of(CannonShots.Ball b) => b == null ? new() : shots.Records.Where(r => r.Id == b.Id).ToList();
            CannonShots.Ball Last() => shots.Flying.Count > 0 ? shots.Flying[^1] : null;

            WaitFor(() => false, 2, w0 =>
            {
                // a chicken on a platform off her port side, abreast of her first port gun, 8 blocks out
                var g1 = port[0];
                shooter.GunMuzzle(g1, 0, out var m1, out _);
                int cx = (int)Math.Floor(m1.X), cz = (int)Math.Floor(m1.Z) - 8;
                for (int x = cx - 1; x <= cx + 1; x++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                        for (int y = floor + 1; y <= surf + 2; y++) { bp.Set(x, y, z); ba.SetBlock(stone.Id, bp); ba.SetBlock(0, bp, BlockLayersAccess.Fluid); }
                EntityProperties chickenType = null;
                foreach (string code in new[] { "chicken-rooster", "chicken-hen", "chicken-adult" })
                    if ((chickenType = sapi.World.GetEntityType(new AssetLocation("game", code))) != null) break;
                var chicken = chickenType == null ? null : (EntityAgent)sapi.World.ClassRegistry.CreateEntity(chickenType);
                if (chicken != null)
                {
                    // (abreast of the muzzle: a grid ship's guns stand at a cell's middle)
                    chicken.Pos.SetPos(m1.X, surf + 3, cz + 0.5);
                    sapi.World.SpawnEntity(chicken);
                }
                WaitFor(() => false, 0.3, w1 =>
                {
                    CannonShots.Ball b1 = null;
                    string err = "no chicken";
                    if (chicken != null)
                    {
                        var box = chicken.SelectionBox;
                        var aim = new Vec3d(chicken.Pos.X, chicken.Pos.Y + (box.Y1 + box.Y2) / 2, chicken.Pos.Z);
                        err = shooter.FireGun(g1, null, null, Lay(shooter, g1, aim));
                        b1 = Last();
                    }
                    string again = shooter.FireGun(g1, null, null);
                    WaitFor(() => b1 == null || !shots.Flying.Contains(b1), 3, w2 =>
                    {
                        var r1 = Of(b1);
                        Check(err == null && chicken != null && !chicken.Alive && r1.Any(r => r.What == "creature" && r.Entity == chicken.EntityId),
                            $"her port gun kills a chicken on a platform 8 blocks off ({string.Join(", ", r1.Select(r => $"{r.What} at {r.At?.X - m1.X:0.0} {r.At?.Y - m1.Y:0.00} {r.At?.Z - m1.Z:0.0} from the muzzle"))}; " +
                            $"the ball had {r1.FirstOrDefault(r => r.What == "creature")?.Energy:0} kJ there; the platform's top {surf + 3 - m1.Y:0.00} from the muzzle, {m1.Z - (cz + 2):0.0} out)");
                        var stop = r1.LastOrDefault(r => r.What == "block");
                        Check(stop != null && stop.At.Z < z0 + 1.5 && ba.GetBlock(new BlockPos((int)Math.Floor(stop.At.X), (int)Math.Floor(stop.At.Y), z0, 0)).Id == stone.Id,
                            $"and goes on into the pool's wall, where it stops: the wall still stands ({stop?.At.Z - z0:0.00} from it)");
                        Check(again == "hansa:gun-reloading" && shooter.GunReloading(g1) > def.Cannon.Cooldown - 2, $"it can't fire again at once: {again} ({shooter.GunReloading(g1):0.0} s)");
                        mod.Config.Cannons = false;
                        string off = shooter.FireGun(port[1], null, null);
                        mod.Config.Cannons = true;
                        Check(off == "hansa:gun-off", $"with Cannons off nothing fires ({off})");

                        // blocks break when they may: a plank in the way of the next gun
                        var g2 = port[1];
                        shooter.GunMuzzle(g2, 0, out var m2, out _);
                        var plankAt = new BlockPos((int)Math.Floor(m2.X), (int)Math.Floor(m2.Y), (int)Math.Floor(m2.Z) - 6, 0);
                        ba.SetBlock(planks.Id, plankAt);
                        mod.Config.CannonsBreakBlocks = true;
                        string e2 = shooter.FireGun(g2, null, null, Lay(shooter, g2, new Vec3d(plankAt.X + 0.5, plankAt.Y + 0.5, plankAt.Z + 0.5)));
                        var b2 = Last();
                        WaitFor(() => b2 == null || !shots.Flying.Contains(b2), 3, w3 =>
                        {
                            mod.Config.CannonsBreakBlocks = false;
                            var r2 = Of(b2);
                            Check(e2 == null && ba.GetBlock(plankAt).Id == 0 && r2.Any(r => r.What == "broke") && r2.Any(r => r.What == "block"),
                                $"with CannonsBreakBlocks on, a plank in its way breaks and the ball goes on to the wall ({string.Join(", ", r2.Select(r => r.What))})");

                            // into the water: a splash, and it's spent
                            var g3 = port[2];
                            string e3 = shooter.FireGun(g3, null, null, -25);
                            var b3 = Last();
                            WaitFor(() => b3 == null || !shots.Flying.Contains(b3), 4, w4 =>
                            {
                                var r3 = Of(b3);
                                var splash = r3.FirstOrDefault(r => r.What == "water");
                                Check(e3 == null && splash != null && Math.Abs(splash.At.Y - (surf + 0.875)) < 0.3 && r3.Any(r => r.What == "spent") && !r3.Any(r => r.What == "ship"),
                                    $"one fired down into the sea splashes (at {splash?.At.Y - wy:+0.00;-0.00} to the surface) and sinks, spent ({string.Join(", ", r3.Select(r => r.What))})");
                                // laying the guns, on the target's far side (before she's sunk)
                                Laying(mod, def, target, type, () => Broadsides(mod, def, shooter, target, starboard, Lay, wy, sea, spawn));
                            });
                        });
                    });
                });
            });
        }

        /// <summary>
        /// Laying a gun (0.3.6), on a carrack's starboard side (the target's, before she's sunk: they face the pool's far
        /// wall). Laid through the server's API it fires along its lay (the ball's first velocity, from its muzzle), and no
        /// further than it goes; the lay toward where someone looks (a view as a player's client sends it, as she lies); its
        /// crew lays it toward an aim at their pace; the lays saved with her, and a carrack loaded with them laid so;
        /// /hansa fire with an elevation and a train. And a stand-in at the gun captain's place (a chicken: there are no
        /// players here, and the engine keeps a creature's view the seat's, so it can't lay the gun): behind the breech on
        /// the deck, room to stand there, the gun left as it is.
        /// </summary>
        void Laying(HansaMod mod, ShipDefinition def, EntityShip ship, EntityProperties type, Action next)
        {
            var shots = mod.Shots;
            // (the guns she carries: a design's gun places that start empty, carrack-m's castles', have none to lay)
            var guns = def.Fixtures.Where(f => f.Type == "gun" && f.Out[2] > 0 && ship.GunMounted(f)).OrderBy(f => f.Pos[0]).ToArray();
            double v = def.Cannon.MuzzleVelocity;
            // how far a ball's first velocity (less her own way) is from the way the gun is laid, degrees; and its start from the muzzle
            void Along(CannonShots.Ball b, FixtureDef gun, GunLay lay, out double angle, out double fromMuzzle)
            {
                angle = fromMuzzle = 999;
                if (b == null) return;
                ship.GunMuzzle(gun, lay, out var m, out var dir);
                double vx = b.S.VX - Math.Sin(ship.Pos.Yaw) * ship.Speed, vy = b.S.VY, vz = b.S.VZ - Math.Cos(ship.Pos.Yaw) * ship.Speed;
                double c = (vx * dir.X + vy * dir.Y + vz * dir.Z) / Math.Max(1e-9, Math.Sqrt(vx * vx + vy * vy + vz * vz));
                angle = Math.Acos(Math.Clamp(c, -1, 1)) * 180 / Math.PI;
                fromMuzzle = b.S.Pos.DistanceTo(m);
            }
            CannonShots.Ball Last() => shots.Flying.Count > 0 ? shots.Flying[^1] : null;

            // laid through the API, it fires along its lay
            var gun = guns[1];
            var want = new GunLay(-6, 2.5);
            ship.SetGunLay(gun, want);
            var lay = ship.GunLayNow(gun);
            string err = ship.FireGun(gun, null, null);
            Along(Last(), gun, lay, out double ang, out double from);
            double speed = Last() == null ? 0 : Last().S.Speed;
            Check(err == null && lay.Train == want.Train && lay.Elevation == want.Elevation && ang < 0.01 && from < 1e-6 && Math.Abs(speed - v) < Math.Abs(ship.Speed) + 0.01,
                $"{gun.Code} laid {GunLaying.Text(lay)} through the server's API fires that way: the ball leaves its muzzle ({from:0.0e0} off) at {speed:0.0} b/s, " +
                $"{ang:0.0000}° from its lay");
            GunLaying.Limits(gun, out float t0, out float t1, out float e0, out float e1);
            ship.SetGunLay(gun, new GunLay(40, -30));
            var stop = ship.GunLayNow(gun);
            Check(stop.Train == t1 && stop.Elevation == e0, $"laid far beyond its limits it goes as far as it goes ({GunLaying.Text(stop)}: it trains {t0:0.#}..{t1:0.#}°, elevation {e0:0.#}..{e1:0.#}°)");

            // the lay toward where someone looks, as she lies
            ship.RealPose(out double pitch, out double roll);
            GunLaying.Muzzle(gun, new GunLay(3, 1.5), ship.Pos.Yaw, pitch, roll, out _, out var wd);
            GunLaying.LookAlong(wd, out float viewYaw, out float viewPitch);
            var seen = ship.GunAimFromView(gun, viewYaw, viewPitch);
            Check(Math.Abs(seen.Train - 3) < 0.05 && Math.Abs(seen.Elevation - 1.5) < 0.05,
                $"someone at it looking along a way (view yaw {viewYaw * 180 / Math.PI:0.0}°, pitch {viewPitch * 180 / Math.PI:0.0}°, as their client sends it) lays it that way: {GunLaying.Text(seen)}");

            // its crew lays it toward an aim at their pace
            ship.SetGunLay(gun, new GunLay(-10, -2));
            ship.AimGun(gun, new GunLay(5, 3));
            long started = sapi.World.ElapsedMilliseconds;
            WaitFor(() => false, 0.5, paced =>
            {
                var mid = ship.GunLayNow(gun);
                double t = (sapi.World.ElapsedMilliseconds - started) / 1000.0;
                WaitFor(() => ship.GunLayNow(gun).Train == 5 && ship.GunLayNow(gun).Elevation == 3, 4, reached =>
                {
                    double total = (sapi.World.ElapsedMilliseconds - started) / 1000.0;
                    Check(reached && Math.Abs(mid.Train + 10 - gun.TrainRate * t) < gun.TrainRate * 0.25 && Math.Abs(mid.Elevation + 2 - Math.Min(5, gun.ElevationRate * t)) < gun.ElevationRate * 0.25 &&
                          total > 1.3 && total < 2.2,
                        $"aimed 15° and 5° away its crew lays it at their pace: {GunLaying.Text(mid)} after {t:0.00} s, there after {total:0.0} s");

                    // saved with her, and a carrack loaded with that is laid so
                    var tree = new TreeAttribute();
                    using (var ms = new System.IO.MemoryStream())
                    {
                        ship.WatchedAttributes.ToBytes(new System.IO.BinaryWriter(ms));
                        tree.FromBytes(new System.IO.BinaryReader(new System.IO.MemoryStream(ms.ToArray())));
                    }
                    var saved = tree.GetTreeAttribute("hansaGuns")?.GetBytes("lay");
                    var copy = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
                    if (saved != null) copy.WatchedAttributes.GetOrAddTreeAttribute("hansaGuns").SetBytes("lay", saved);
                    copy.Pos.SetPos(ship.Pos.X + 40, ship.Pos.Y, ship.Pos.Z);
                    copy.Pos.Yaw = ship.Pos.Yaw;
                    sapi.World.SpawnEntity(copy);
                    var all = def.Fixtures.Where(f => f.Type == "gun").ToArray();
                    int same = all.Count(f => Math.Abs(copy.GunLayNow(f).Train - ship.GunLayNow(f).Train) < 0.011 && Math.Abs(copy.GunLayNow(f).Elevation - ship.GunLayNow(f).Elevation) < 0.011);
                    Check(saved?.Length == 4 * all.Length && same == all.Length && copy.GunLayNow(gun).Train == 5,
                        $"her guns' lays are saved with her ({saved?.Length} bytes) and a carrack loaded with them is laid so ({same} of {all.Length} guns)");
                    copy.Die(EnumDespawnReason.Removed);

                    // /hansa fire with an elevation and a train (as far as it goes; "-" leaves one as it is)
                    var g3 = guns[2];
                    var res = mod.FireCommand(ship, null, null, g3.Code, "1.5", "-8");
                    var l3 = ship.GunLayNow(g3);
                    Along(Last(), g3, l3, out double ang3, out double from3);
                    var g1 = guns[0];
                    float e1Before = ship.GunLayNow(g1).Elevation;
                    var res1 = mod.FireCommand(ship, null, null, g1.Code, "-", "-30");
                    var l1 = ship.GunLayNow(g1);
                    GunLaying.Limits(g1, out float g1t0, out _, out _, out _);
                    Check(res.Status == EnumCommandStatus.Success && l3.Train == -8 && l3.Elevation == 1.5f && ang3 < 0.01 && from3 < 1e-6 &&
                          res1.Status == EnumCommandStatus.Success && l1.Train == g1t0 && l1.Elevation == e1Before && res1.StatusMessage.Contains("as far as it goes"),
                        $"/hansa fire {g3.Code} 1.5 -8 lays it and fires it that way ({ang3:0.0000}° off: \"{res.StatusMessage}\"); with a train beyond the limit " +
                        $"and the elevation left as it is: \"{res1.StatusMessage}\"");

                    // a stand-in at the gun captain's place: behind the breech, on the deck
                    EntityProperties chickenType = null;
                    foreach (string code in new[] { "chicken-rooster", "chicken-hen", "chicken-adult" })
                        if ((chickenType = sapi.World.GetEntityType(new AssetLocation("game", code))) != null) break;
                    var seat = ship.GunSeatOf(gun);
                    EntityAgent hand = null;
                    if (chickenType != null && seat != null)
                    {
                        hand = (EntityAgent)sapi.World.ClassRegistry.CreateEntity(chickenType);
                        var sp = seat.SeatPosition;
                        hand.Pos.SetPos(sp.X, sp.Y, sp.Z);
                        sapi.World.SpawnEntity(hand);
                        hand.TryMount(seat);
                    }
                    var before = ship.GunLayNow(gun);
                    WaitFor(() => false, 1, mounted =>
                    {
                        var at = seat?.LocalPos;
                        // (it fired a moment ago: the gun captain steps back as it runs back, and in again)
                        var expect = ship.GunSeatLocal(gun);
                        GunLaying.OutOf(gun, out double ox, out double oz);
                        double behind = at == null ? 0 : (gun.Pos[0] - at.X) * ox + (gun.Pos[2] - at.Z) * oz;
                        // (20 October 2026) behind its carriage, on the deck it stands on: a carrack's (1.15 behind its trunnions,
                        // on her main deck) wants 1.5 as before; the caravel's lighter gun (0.65) and her quarterdeck's guns
                        double carriage = gun.Box?.Length >= 6 ? Math.Max((gun.Pos[0] - gun.Box[0]) * ox + (gun.Pos[2] - gun.Box[2]) * oz,
                                                                          (gun.Pos[0] - gun.Box[3]) * ox + (gun.Pos[2] - gun.Box[5]) * oz) : 1.15;
                        double behindWant = Math.Min(1.5, carriage + 0.4);
                        var now = ship.GunLayNow(gun);
                        Check(hand?.MountedOn == seat && at != null && at.DistanceTo(expect) < 1e-6 && Math.Abs(at.Y - gun.Pos[1]) < 1e-6 && behind > behindWant && behind < 3.3 &&
                              ship.StandsFree(at) != null && now.Train == before.Train && now.Elevation == before.Elevation,
                            $"a stand-in at {gun.Code}'s gun captain's place stands {behind:0.00} behind its trunnions on the deck, with room to stand up there; a creature " +
                            $"can't lay it (still {GunLaying.Text(now)})");
                        hand?.TryUnmount();
                        if (hand?.Alive == true) hand.Die(EnumDespawnReason.Removed);
                        next();
                    });
                });
            });
        }

        /// <summary>
        /// Her starboard broadsides, every time her guns are ready, laid at the other's side just under the waterline (the
        /// other moved a frame space along between them, so they hole a new place each time), till the other goes down.
        /// </summary>
        void Broadsides(HansaMod mod, ShipDefinition def, EntityShip shooter, EntityShip target, FixtureDef[] guns,
            System.Func<EntityShip, FixtureDef, Vec3d, double> lay, double wy, Sea.SeaSystem sea, BlockPos spawn)
        {
            var l = def.DamageParts;
            var shots = mod.Shots;
            double baseX = target.Pos.X;
            double[] shifts = { 0, -1.5, 1.5, -3, 3, -4.5, 4.5, -6, 6 };
            int broadsides = 0, fired = 0;
            // (20 October 2026) as many broadsides as fire a carrack's three (5 guns a side: 15 shots), then how much water is
            // in her: a carrack's 3, the caravel's 4 (4 guns a side)
            int waterAt = (int)Math.Ceiling(15.0 / Math.Max(1, guns.Length));
            double waterAfter3 = double.NaN;
            long started = sapi.World.ElapsedMilliseconds;
            EntityShip hooked = null;
            Action<EntityShip> hook = s => hooked = s;
            ShipWreck.Sunk += hook;
            int sunkBefore = ShipWreck.SunkCount;
            // her cargo, for her wreck
            var had = StockForWreck(target, def, out int asThemselves);
            // where her port side is at a height, along her (ship-local z of its outer face)
            double SideZ(double x, double y)
            {
                var gr = target.Def.Grid;
                for (int k = 0; k < gr.NZ / 2; k++)
                    if (gr.SolidAt(x, y, gr.OZ + (k + 0.5) * gr.V)) return gr.OZ + k * gr.V;
                return -5;
            }
            long id = 0;
            id = sapi.Event.RegisterGameTickListener(dt =>
            {
                double secs = (sapi.World.ElapsedMilliseconds - started) / 1000.0;
                if (!target.GoneDown && secs < 300 && guns.All(f => shooter.GunReloading(f) <= 0))
                {
                    target.Pos.X = baseX + shifts[broadsides % shifts.Length];
                    foreach (var gun in guns)
                    {
                        shooter.GunMuzzle(gun, 0, out var m, out _);
                        double lx = m.X - target.Pos.X, ly = def.Waterline - 0.3;
                        target.LocalToWorld(lx, ly, SideZ(lx, ly) + 0.02, out double tx, out double ty, out double tz);
                        if (shooter.FireGun(gun, null, null, lay(shooter, gun, new Vec3d(tx, ty, tz))) == null) fired++;
                    }
                    broadsides++;
                    int n = broadsides;
                    if (n <= waterAt + 2) WaitFor(() => false, 3, w5 => Log($"  {n} broadsides ({fired} shots): {target.Water:0.0} t in her"));
                    if (n == waterAt) WaitFor(() => false, 3, w5 => waterAfter3 = target.Water);
                    return;
                }
                if (!target.GoneDown && secs < 300) return;
                sapi.Event.UnregisterGameTickListener(id);
                var recs = shots.Records.Where(r => r.What == "ship" && r.Entity == target.EntityId).ToList();
                // (since 0.3.7 a ball into the water loses most of its way at once and is spent within two blocks: laid
                // under her waterline from close by, it goes in a block or so short of her; some are spent before they get
                // there, the rest strike her a good deal weaker)
                int spent = shots.Records.Count(r => r.What == "spent" && r.Ms >= started);
                var below = l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Center.Y < def.Waterline && target.StateOfPart(p.Index) >= PartState.Cracked).ToList();
                int holed = below.Count(p => target.StateOfPart(p.Index) == PartState.Holed);
                Check(fired >= 9 && recs.Count >= fired * 0.4 && recs.Count + spent >= fired * 0.8 && holed >= 3,
                    $"{broadsides} broadsides, {fired} shots, {recs.Count} of them into her hull (through the water at her waterline, {recs.Where(r => r.Energy > 0).Select(r => r.Energy).DefaultIfEmpty(0).Average():0} kJ on average), " +
                    $"{spent} spent in the water short of her: {holed} panels below her waterline holed, {below.Count - holed} more cracked");
                // (20 October 2026) 3 t in a carrack (a thousand tonnes) as before; 0.3 % of a light ship's displacement (the caravel's 0.7 t)
                double waterWant = l.Displacement >= 600 ? 3 : 0.003 * l.Displacement;
                Check(waterAfter3 > waterWant, $"water comes into her ({waterAfter3:0.0} t after {waterAt} broadsides; more than {waterWant:0.0} t wanted)");
                Check(target.GoneDown, $"she founders and goes down {secs:0} s after the first broadside ({target.Stage}, {target.Water:0} t in her)");
                Check(Enumerable.Range(0, l.Count).All(i => shooter.StateOfPart(i) == PartState.Sound), "the one firing takes no hurt from her own guns");
                WaitFor(() => target.Stage == SinkStage.Sunk, 60, sunk =>
                {
                    Check(sunk && hooked == target && ShipWreck.SunkCount == sunkBefore + 1,
                        $"she sinks to the bottom of the gun pool, and the wreck's hook gets her there ({target.Sinkage:0.0} deep)");
                    ShipWreck.Sunk -= hook;
                    WreckChecks(mod, def, target, had, asThemselves, wy, () =>
                    {
                        foreach (var e in new Entity[] { shooter, target }) if (e.Alive) e.Die(EnumDespawnReason.Removed);
                        sea?.SetOverride(null);
                        MadeUpWreck(mod, def, spawn, wy, () => CrewWorks(mod, def, spawn, wy, () => SailStrainWorks(mod, def, spawn, wy, () => HearthWorks(mod, def, spawn, wy, () => WorkingBlocksWorks(mod, def, spawn, wy, () => HullPanelsWorks(mod, spawn, wy, Finish))))));
                    });
                });
            }, 200);
        }

        // ------------------------------------------------------------------ the wreck

        /// <summary>
        /// Her cargo before she's sunk, for her wreck: a chest and a storage vessel in her hold with things in them (they
        /// go down as themselves), a trunk across a pair of cargo places with some (a chest takes its contents, the trunk
        /// goes with the loose things), things in the hold's big storage, firewood and raw meat in her hearth, a lantern and
        /// the plate on their places. What she has, by item code (the containers that go down as themselves not counted:
        /// asThemselves of them).
        /// </summary>
        Dictionary<string, int> StockForWreck(EntityShip ship, ShipDefinition def, out int asThemselves)
        {
            asThemselves = 0;
            Block B(string c) => sapi.World.GetBlock(new AssetLocation("game", c));
            Item I(string c) => sapi.World.GetItem(new AssetLocation("game", c));
            var cargo = def.Fixtures.Where(f => f.Kind == "cargo").ToList();
            var vessel = sapi.World.Blocks.FirstOrDefault(bl => bl?.Code != null && bl.Code.Path.StartsWith("storagevessel-") && bl.Code.Path.EndsWith("-fired"));
            var trunk = sapi.World.Blocks.FirstOrDefault(bl => bl is Vintagestory.GameContent.BlockGenericTypedContainerTrunk);
            void Put(string code, params ItemStack[] stacks)
            {
                if (!ship.Storages.TryGetValue(code, out var inv)) return;
                for (int i = 0; i < stacks.Length && i < inv.Count; i++)
                {
                    if (stacks[i]?.Collectible == null) continue;
                    inv[i].Itemstack = stacks[i];
                    inv[i].MarkDirty();
                }
            }
            // (since the eighth test her hold's cargo is pieces on its floor places; an older design's cargo places as before)
            ShipPiece Piece(Block blk)
            {
                if (blk == null || ship.Mounts == null) return null;
                var s = new ItemStack(blk);
                foreach (var m in ship.Mounts.Floors.Where(m => m.Room == "hold"))
                {
                    var p = PiecePlanner.Plan(ship.Mounts, m, new VanillaPieceBlock(sapi.World, s), 1);
                    if (ship.PlaceProblem(p, true) == null && ship.PlacePiece(p, s, true, out var placed) == null) return placed;
                }
                return null;
            }
            string chestCode = null, vesselCode = null;
            if (cargo.Count > 0)
            {
                var chestF = cargo.First(f => f.Pair != null);
                if (ship.TryAttach(chestF, new ItemStack(B("chest-east"))) == null) { asThemselves++; chestCode = chestF.Code; }
                var vesselF = cargo.First(f => f.Pair == null);
                if (vessel != null && ship.TryAttach(vesselF, new ItemStack(vessel)) == null) { asThemselves++; vesselCode = vesselF.Code; }
                var trunkF = cargo.FirstOrDefault(f => f.Pair != null && f != chestF && f.Pair != chestF.Code && !ship.Attached.ContainsKey(f.Code));
                if (trunk != null && trunkF != null && ship.TryAttach(trunkF, new ItemStack(trunk)) == null) Put(trunkF.Code, new ItemStack(I("plank-oak"), 20));
            }
            else
            {
                chestCode = Piece(B("chest-east"))?.StorageCode;
                vesselCode = Piece(vessel)?.StorageCode;
                if (Piece(trunk) is ShipPiece tp) Put(tp.StorageCode, new ItemStack(I("plank-oak"), 20));
            }
            if (chestCode != null) Put(chestCode, new ItemStack(I("stick"), 6), new ItemStack(I("flaxfibers"), 9));
            if (vesselCode != null) Put(vesselCode, new ItemStack(I("flint"), 12));
            Put("hold", new ItemStack(I("plank-oak"), 30), new ItemStack(I("resin"), 5), new ItemStack(I("rope"), 4));
            // firewood and raw meat in her hearth (not lit)
            if (ship.HearthInv != null)
            {
                ship.HearthInv[0].Itemstack = new ItemStack(I("firewood"), 3);
                ship.HearthInv[0].MarkDirty();
                ship.HearthInv[1].Itemstack = new ItemStack(I("redmeat-raw"), 2);
                ship.HearthInv[1].MarkDirty();
            }
            var lanternF = def.Fixtures.FirstOrDefault(f => f.Kind == "lantern");
            if (lanternF != null) ship.TryAttach(lanternF, new ItemStack(B("lantern-small-up")));
            else if (def.StartPieces?.Length > 0) ship.PlaceStartPieces();
            var plateF = def.Fixtures.FirstOrDefault(f => f.Kind == "plate");
            if (plateF != null && !ship.Attached.ContainsKey(plateF.Code)) ship.TryAttach(plateF, new ItemStack(I("metalplate-copper")));
            // what she has: everything in her storages, and what's on her places but the two that go down as themselves
            var had = new Dictionary<string, int>();
            void Add(ItemStack s) { if (s?.Collectible != null) had[s.Collectible.Code.ToString()] = had.GetValueOrDefault(s.Collectible.Code.ToString()) + s.StackSize; }
            foreach (var inv in ship.Storages.Values) foreach (var slot in inv) Add(slot.Itemstack);
            if (ship.HearthInv != null) foreach (var slot in ship.HearthInv) Add(slot.Itemstack);
            foreach (var kv in ship.Attached) if (kv.Key != chestCode && kv.Key != vesselCode) Add(kv.Value);
            // her pieces: a container goes down as itself (but a trunk: a chest takes what's in it, the trunk goes loose),
            // anything else loose
            foreach (var p in ship.Pieces.All)
            {
                if (p.Role == PieceRole.Storage && p.Stack?.Block is not Vintagestory.GameContent.BlockGenericTypedContainerTrunk) asThemselves++;
                else Add(p.Stack);
            }
            Log($"her cargo for the wreck: {string.Join(", ", had.Select(kv => kv.Value + " " + kv.Key))} and {asThemselves} containers that go down as themselves");
            return had;
        }

        /// <summary>
        /// After she has sunk in the gun pool (Wrecks on): she becomes her wreck, begun again half way as after a reload (the
        /// self-test makes it forget the wreck under way): the ship is gone, her blocks are where she lay (once each, as
        /// planned), every item she had is in the containers put down (none lost, none doubled), her guns are on the
        /// seabed or in her, water is where it was; breaking a wreck block gives raw materials, not the block; she isn't
        /// made a wreck twice; someone in her cells is moved out of them.
        /// </summary>
        void WreckChecks(HansaMod mod, ShipDefinition def, EntityShip ship, Dictionary<string, int> had, int asThemselves, double surface, Action then)
        {
            var wrecks = mod.Wrecks;
            var ba = sapi.World.BlockAccessor;
            long id = ship.EntityId;
            var lay = ship.Pos.XYZ;
            bool restarted = false;
            Action<long, int> placed = (wid, n) =>
            {
                if (wid != id || restarted || n < ShipWrecks.BlocksPerTick) return;
                restarted = true;
                wrecks.Restart(wid);
            };
            wrecks.Placed += placed;
            // (and the ship's entity gone: it's despawned a tick after she dies, and hull R's wreck can be laid sooner)
            WaitFor(() => wrecks.Reports.TryGetValue(id, out var r) && r.Finished && sapi.World.GetEntityById(id) == null, 90, done =>
            {
                wrecks.Placed -= placed;
                var rep = wrecks.Reports.GetValueOrDefault(id);
                var plan = rep?.Plan;
                Check(done && !ship.Alive && sapi.World.GetEntityById(id) == null && wrecks.Registry.Done.ContainsKey(id),
                    $"on the bottom she becomes her wreck: the ship is gone, the wreck is recorded ({rep?.Placed} blocks laid, {rep?.Skipped} skipped; finished {done}, " +
                    $"alive {ship.Alive}, entity {(sapi.World.GetEntityById(id) != null ? "still there" : "gone")}, recorded {wrecks.Registry.Done.ContainsKey(id)})");
                if (plan == null) { then(); return; }
                Check(restarted && rep.Starts == 2, $"begun again half way (as after a reload: {rep.Starts} starts), it's still one wreck");
                // her blocks: where she lay, each as planned, once
                int found = 0, wrong = 0;
                double sx = 0, sz = 0;
                for (int x = plan.MinX; x <= plan.MaxX; x++)
                    for (int y = plan.MinY; y <= plan.MaxY; y++)
                        for (int z = plan.MinZ; z <= plan.MaxZ; z++)
                        {
                            if (ba.GetBlockEntity(new BlockPos(x, y, z, 0)) is not BlockEntityWreck be || be.WreckId != id) continue;
                            found++;
                            sx += x; sz += z;
                            if (!plan.Blocks.TryGetValue(WreckPlan.Key(x, y, z), out var c) || c.All != be.Cells?.All) wrong++;
                        }
                int floorY = (int)Math.Floor(plan.Input.Y) - 30;
                for (int y = plan.MinY - 1; y >= floorY && floorY > 0; y--)
                    if (ba.GetBlock(new BlockPos((int)Math.Floor(lay.X), y, (int)Math.Floor(lay.Z), 0)).Code?.Path == "rock-granite") { floorY = y; break; }
                Check(found > 500 && found == plan.Blocks.Count - rep.Skipped && wrong == 0,
                    $"{found} wreck blocks in the world, each with the cells planned ({wrong} not), {plan.CellsPlaced} cells: tarred, oak, pine, iron");
                {
                    // what the game asks a wreck block for: its collision and selection are its cells as boxes
                    int boxed = 0, off = 0;
                    // (20 October 2026) every 20th block of a carrack's wreck; of a smaller one (the caravel's 750), every 10th
                    int every = plan.Blocks.Count >= 1000 ? 20 : 10;
                    foreach (long k in plan.Order().Where((k, i) => i % every == 0))
                    {
                        var bp = PosOf(k);
                        var bl = ba.GetBlock(bp);
                        if (bl is not BlockWreck || ba.GetBlockEntity(bp) is not BlockEntityWreck be) continue;
                        boxed++;
                        var cbs = bl.GetCollisionBoxes(ba, bp);
                        var sbs = bl.GetSelectionBoxes(ba, bp);
                        double vol = cbs?.Sum(c => (double)(c.X2 - c.X1) * (c.Y2 - c.Y1) * (c.Z2 - c.Z1)) ?? -1;
                        if (Math.Abs(vol - be.Cells.Count / 64.0) > 1e-4 || sbs?.Length != cbs?.Length || cbs.Any(c => c.X1 < 0 || c.Y1 < 0 || c.Z1 < 0 || c.X2 > 1 || c.Y2 > 1 || c.Z2 > 1)) off++;
                    }
                    Check(boxed > 50 && off == 0, $"a wreck block's collision and selection are its cells ({boxed} looked at, {off} not)");
                }
                // (20 October 2026) from her hull's middle along her where that lies well off her origin: the caravel's is 2.7 aft
                // of hers (her quarterdeck and transom; a carrack's is 1.6 forward of hers, and hers is measured from her origin
                // as before)
                double hullMid = 0;
                {
                    var gr = def.Grid;
                    double sum = 0; long n = 0;
                    for (int i = 0; i < gr.NX; i++)
                        for (int j = 0; j < gr.NY; j++)
                            for (int k = 0; k < gr.NZ; k++)
                                if (gr.Solid(i, j, k)) { sum += gr.OX + (i + 0.5) * gr.V; n++; }
                    if (n > 0 && Math.Abs(sum / n) > 2) hullMid = sum / n;
                }
                double midX = lay.X + Math.Sin(ship.Pos.Yaw) * hullMid, midZ = lay.Z + Math.Cos(ship.Pos.Yaw) * hullMid;
                Check(found > 0 && Math.Abs(sx / found - midX) < 4 && Math.Abs(sz / found - midZ) < 6 && plan.MinY > floorY,
                    $"where she lay: their middle {sx / Math.Max(1, found) - midX:+0.0;-0.0}, {sz / Math.Max(1, found) - midZ:+0.0;-0.0} from her" +
                    (hullMid != 0 ? $" (her hull's middle, {hullMid:+0.0;-0.0} along her from her origin)" : "") + $", the lowest on the pool's floor (y {plan.MinY}, floor {floorY})");
                // her cargo: all of it in the containers put down
                var after = new Dictionary<string, int>();
                int themselves = 0;
                foreach (var (pos, what) in rep.Containers)
                {
                    if (ba.GetBlockEntity(pos) is Vintagestory.GameContent.BlockEntityContainer bc)
                        foreach (var slot in bc.Inventory)
                            if (slot?.Itemstack?.Collectible != null)
                                after[slot.Itemstack.Collectible.Code.ToString()] = after.GetValueOrDefault(slot.Itemstack.Collectible.Code.ToString()) + slot.Itemstack.StackSize;
                    if (what != "extra chest" && !what.Contains("a chest for")) themselves++;
                }
                bool same = had.Count > 0 && had.All(kv => after.GetValueOrDefault(kv.Key) == kv.Value) && after.Keys.All(had.ContainsKey);
                if (!same)
                    foreach (var (pos, what) in rep.Containers)
                    {
                        var cb = ba.GetBlock(pos);
                        var items = ba.GetBlockEntity(pos) is Vintagestory.GameContent.BlockEntityContainer bc2
                            ? string.Join(", ", bc2.Inventory.Where(sl => sl?.Itemstack != null).Select(sl => sl.Itemstack.StackSize + " " + sl.Itemstack.Collectible.Code.Path)) : "(no container there)";
                        Log($"  wreck container {what} at {pos}: {cb?.Code} holds {items}");
                    }
                Check(same && themselves == asThemselves && rep.Dropped == 0,
                    $"every item she had is in the {rep.Containers.Count} containers put down ({themselves} of hers as themselves, in her or beside her; extra chests): " +
                    $"{string.Join(", ", after.Select(kv => kv.Value + " " + kv.Key.Replace("game:", "")))}; {rep.Dropped} dropped");
                // her guns
                int guns = plan.Guns.Count(s => ba.GetBlock(new BlockPos(s.X, s.Y, s.Z, 0))?.Code?.ToString() == "hansa:gun-bronze-" + s.Side);
                int gunsWant = def.Fixtures.Count(f => f.Type == "gun" && (!def.GunPlaceOf.TryGetValue(f.Code, out var gp) || gp.StartsLaid));
                Check(guns == gunsWant && rep.Guns == gunsWant, $"her {gunsWant} bronze guns are there as blocks ({plan.Guns.Count(s => s.InWreck)} in her, {plan.Guns.Count(s => !s.InWreck)} tumbled onto the seabed; {guns} found)");
                // the water (under the pool's surface): still in her blocks (they don't push it out), and round her where
                // nothing was put
                int sy = (int)Math.Floor(surface);
                bool Wet(int x, int y, int z) => ba.GetBlock(new BlockPos(x, y, z, 0), BlockLayersAccess.Fluid)?.IsLiquid() == true;
                // (every 50th block of a carrack's wreck; of a smaller one, every 25th)
                int everyWet = plan.Blocks.Count >= 1000 ? 50 : 25;
                var some = plan.Order().Where((k, i) => i % everyWet == 0).Where(k => { WreckPlan.Unkey(k, out _, out int y, out _); return y <= sy; }).ToList();
                int dry = some.Count(k => { WreckPlan.Unkey(k, out int x, out int y, out int z); return !Wet(x, y, z); });
                var taken = new HashSet<long>(plan.Blocks.Keys);
                foreach (var s in plan.Containers.Concat(plan.Extra).Concat(plan.Guns).Concat(plan.Sediment)) if (s != null) taken.Add(WreckPlan.Key(s.X, s.Y, s.Z));
                int free = 0, freeDry = 0;
                for (int x = plan.MinX; x <= plan.MaxX; x += 3)
                    for (int z = plan.MinZ; z <= plan.MaxZ; z += 3)
                        for (int y = plan.MinY; y <= Math.Min(plan.MaxY, sy); y += 2)
                        {
                            if (taken.Contains(WreckPlan.Key(x, y, z)) || ba.GetBlock(new BlockPos(x, y, z, 0), BlockLayersAccess.Solid).Id != 0) continue;
                            free++;
                            if (!Wet(x, y, z)) freeDry++;
                        }
                Check(some.Count > 20 && dry == 0 && free > 100 && freeDry == 0,
                    $"the water stays: in her blocks' empty cells ({some.Count - dry} of {some.Count} looked at), and round her ({free - freeDry} of {free})");
                // breaking a wreck block: part of its raw materials, never the block
                var codes = WreckDrops.Table.Values.SelectMany(d => d).Select(d => d.Code).ToHashSet();
                // (20 October 2026) the one with the most timber: a caravel's first block of 40 cells may hold two kinds of oak
                // too thinly for either to roll a plank (1 stick in one run); a full block of it always gives planks
                int Timber(long k) => plan.Blocks[k].CountOf(WreckMat.Oak) + plan.Blocks[k].CountOf(WreckMat.Tarred);
                var full = plan.Order().Where(k => plan.Blocks[k].Count >= 40 && Timber(k) >= 32 && ba.GetBlockEntity(PosOf(k)) is BlockEntityWreck)
                    .OrderByDescending(Timber).FirstOrDefault();
                if (full != 0)
                {
                    var bp = PosOf(full);
                    var block = ba.GetBlock(bp);
                    var drops = block.GetDrops(sapi.World, bp, null);
                    Check(drops != null && drops.Length > 0 && drops.All(d => codes.Contains(d.Collectible.Code.ToString())) && drops.Any(d => d.Collectible.Code.Path.StartsWith("plank-")) &&
                          block is BlockWreck && block.GetBlockMaterial(ba, bp) == EnumBlockMaterial.Wood,
                        $"breaking a wreck block of {plan.Blocks[full].Count} cells gives raw materials: {string.Join(", ", drops.Select(d => d.StackSize + " " + d.Collectible.Code.Path))} (wood to the tools)");
                    block.OnBlockBroken(sapi.World, bp, null);
                    var items = sapi.World.GetEntitiesAround(bp.ToVec3d().Add(0.5, 0.5, 0.5), 2, 2, e => e is EntityItem);
                    Check(ba.GetBlock(bp, BlockLayersAccess.Solid).Id == 0 && ba.GetBlock(bp, BlockLayersAccess.Fluid)?.IsLiquid() == true &&
                          items.Length > 0 && items.All(e => codes.Contains(((EntityItem)e).Itemstack.Collectible.Code.ToString())),
                        $"broken, it's gone (water left there) and its {items.Length} drops lie there, none of them a wreck block or a chiselled one");
                }
                else Check(false, "a wreck block of timber to break");
                // never two wrecks from one ship
                string again = wrecks.Convert(ship);
                Check(again != null && !wrecks.Busy(id) && ShipWrecks.Deserialize(wrecks.Serialize()).Done.ContainsKey(id),
                    $"asked again, she isn't made a wreck twice ({again}); the wrecks made are saved with the world");
                // someone inside her cells is moved up out of them
                EntityProperties chickenType = null;
                foreach (string code in new[] { "chicken-rooster", "chicken-hen", "chicken-adult" })
                    if ((chickenType = sapi.World.GetEntityType(new AssetLocation("game", code))) != null) break;
                var inside = plan.Order().FirstOrDefault(k => plan.Blocks[k].Count >= 48 && ba.GetBlockEntity(PosOf(k)) is BlockEntityWreck);
                if (chickenType != null && inside != 0)
                {
                    var cp = PosOf(inside);
                    var chicken = (EntityAgent)sapi.World.ClassRegistry.CreateEntity(chickenType);
                    chicken.Pos.SetPos(cp.X + 0.5, cp.Y + 0.1, cp.Z + 0.5);
                    sapi.World.SpawnEntity(chicken);
                    int moved = wrecks.MoveOut(plan, 0);
                    WaitFor(() => false, 0.6, w =>
                    {
                        var box = chicken.CollisionBox;
                        bool clear = !WreckCells.Boxes(plan.Blocks[inside].All).Any(b =>
                            chicken.Pos.Y + box.Y1 < cp.Y + b[4] * 0.25 && chicken.Pos.Y + box.Y2 > cp.Y + b[1] * 0.25 &&
                            chicken.Pos.X + box.X1 < cp.X + b[3] * 0.25 && chicken.Pos.X + box.X2 > cp.X + b[0] * 0.25 &&
                            chicken.Pos.Z + box.Z1 < cp.Z + b[5] * 0.25 && chicken.Pos.Z + box.Z2 > cp.Z + b[2] * 0.25);
                        Check(moved >= 1 && clear && chicken.Pos.Y > cp.Y + 0.1, $"a chicken inside her cells is moved up out of them ({chicken.Pos.Y - cp.Y - 0.1:+0.00} blocks)");
                        chicken.Die(EnumDespawnReason.Removed);
                        then();
                    });
                }
                else
                {
                    Check(false, "a chicken and a full wreck block to put it in");
                    then();
                }
            });
        }

        /// <summary>
        /// A wreck that was never a ship (as the Earth map's worldgen would make one: a design, a place, a yaw, a damage
        /// state, a loot list): laid on the deep pool's floor, with its loot in a chest beside it.
        /// </summary>
        void MadeUpWreck(HansaMod mod, ShipDefinition def, BlockPos spawn, double wy, Action then)
        {
            int floor = (int)Math.Floor(wy) - 5 - DeepDepth;
            var l = def.DamageParts;
            var hurt = new float[l.Count];
            foreach (var p in l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Part.Side > 0 && p.Center.Y < def.Waterline).Take(4)) hurt[p.Index] = 0.9f;
            var flint = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "flint")), 8);
            var rope = new ItemStack(sapi.World.GetItem(new AssetLocation("game", "rope")), 2);
            var input = new WreckInput
            {
                Def = def, X = spawn.X + 0.5 - 4, Y = floor + 1 - def.Grid.OY, Z = (spawn.Z + DeepZ0 + spawn.Z + DeepZ1) / 2.0 + 0.5,
                Yaw = Math.PI / 2, List = 0.2, Trim = -0.05, Hurt = hurt, Seed = 1234,
            };
            long id = mod.Wrecks.Place(input, new List<WreckLoot> { new() { Items = { flint, rope } } });
            WaitFor(() => mod.Wrecks.Reports.TryGetValue(id, out var r) && r.Finished, 60, done =>
            {
                var rep = mod.Wrecks.Reports.GetValueOrDefault(id);
                var ba = sapi.World.BlockAccessor;
                int blocks = rep?.Plan?.Blocks.Keys.Count(k => ba.GetBlockEntity(PosOf(k)) is BlockEntityWreck be && be.WreckId == id) ?? 0;
                var chest = rep?.Containers.FirstOrDefault(c => c.What == "extra chest");
                var inv = chest?.Pos == null ? null : (ba.GetBlockEntity(chest.Value.Pos) as Vintagestory.GameContent.BlockEntityContainer)?.Inventory;
                int nFlint = inv?.Sum(s => s.Itemstack?.Collectible?.Code?.Path == "flint" ? s.StackSize : 0) ?? 0;
                int nRope = inv?.Sum(s => s.Itemstack?.Collectible?.Code?.Path == "rope" ? s.StackSize : 0) ?? 0;
                Check(done && id < 0 && blocks > 500 && blocks == rep.Plan.Blocks.Count && rep.Guns == def.Fixtures.Count(f => f.Type == "gun") && nFlint == 8 && nRope == 2 && mod.Wrecks.Registry.Done.ContainsKey(id),
                    $"a wreck that was never a ship (#{id}: a design, a place, a yaw, a damage state, a loot list) laid on the deep pool's floor: {blocks} blocks, {rep?.Guns} guns, its loot in a chest beside it ({nFlint} flint, {nRope} rope)");
                then();
            });
        }

        static BlockPos PosOf(long key)
        {
            WreckPlan.Unkey(key, out int x, out int y, out int z);
            return new BlockPos(x, y, z, 0);
        }

        // ------------------------------------------------------------------ the firehearth

        /// <summary>
        /// The firehearth working, on a carrack in the first pool: vanilla's firepit inventory on her; not lit without fuel,
        /// nor with a stick, nor held too short; a firestarter that doesn't catch and one that does (its durability); raw red
        /// meat on two firewood cooks in the time vanilla's numbers give (<see cref="HearthSimContents"/>), the forecastle
        /// warm meanwhile and the waist not; out of fuel it goes out, its embers left; new fuel and a pot with two grain
        /// relight on them and cook porridge (vanilla's cooking recipe); her hearth saved and a carrack loaded with it
        /// burning; switched off it goes out and can't be lit. (A sinking ship's fire going out: DeepSinking; her hearth's
        /// contents in her wreck: Broadsides.)
        /// </summary>
        /// <summary>
        /// Her crew list (0.3.11): signed first by a captain, names written on and taken off by captains only, the last captain
        /// kept; with her crew offline (the stand-ins are never online) she's laid up: anchored and furled, closed to others,
        /// no water through a hole and no damage; with the list cleared she's in commission again and the hole leaks. The
        /// plaque is on her design. The watch: an alarm wakes no one when no one sleeps.
        /// </summary>
        void CrewWorks(HansaMod mod, ShipDefinition def, BlockPos spawn, double wy, Action then)
        {
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", cfg.Ship));
            var plaque = def.Fixtures.FirstOrDefault(f => f.Type == "plaque");
            Check(plaque?.Sel != null && EntityShip.GuestMayUse(plaque) && def.KeepClear.Any(k => k.What == "plaque"),
                $"the carrack has the bronze plaque for her crew list ({plaque?.Code}), readable by anyone, kept clear");
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X - 25.5, wy, spawn.Z + 0.5);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            Check(!ship.HasCrewList && !ship.LaidUp && ship.CrewCanHave, "a ship from no deed has no crew list, and isn't laid up");
            EntityShip.KnownPlayers = n => n == "Mate" ? ("hansa-selftest-mate", "Mate") : n == "Cook" ? ("hansa-selftest-cook", "Cook") : null;
            string e0 = ship.ChangeCrew("hansa-selftest-cook", "Cook", false, "add", "Mate");
            string e1 = ship.ChangeCrew("hansa-selftest-capt", "Capt", false, "claim", null);
            string e2 = ship.ChangeCrew("hansa-selftest-cook", "Cook", false, "claim", null);
            string e3 = ship.ChangeCrew("hansa-selftest-capt", "Capt", false, "add", "Mate");
            string e4 = ship.ChangeCrew("hansa-selftest-mate", "Mate", false, "add", "Cook");
            string e5 = ship.ChangeCrew("hansa-selftest-capt", "Capt", false, "add", "Nobody at all");
            Check(e0 == "hansa:crew-not-captain" && e1 == null && e2 == "hansa:crew-claimed" && e3 == null && e4 == "hansa:crew-not-captain" &&
                  e5 == "hansa:crew-nosuch" && ship.IsCaptain("hansa-selftest-capt") && ship.IsCrew("hansa-selftest-mate") && !ship.IsCaptain("hansa-selftest-mate"),
                $"the first to sign is her captain; only a captain writes names on ({e0}, {e1}, {e2}, {e3}, {e4}, {e5})");
            string e6 = ship.ChangeCrew("hansa-selftest-capt", "Capt", false, "leave", null);
            string e7 = ship.ChangeCrew("hansa-selftest-capt", "Capt", false, "captain", "Mate");
            string e8 = ship.ChangeCrew("hansa-selftest-mate", "Mate", false, "crew", "Capt");
            string e9 = ship.ChangeCrew("hansa-selftest-mate", "Mate", false, "add", "Cook");
            string e10 = ship.ChangeCrew("hansa-selftest-cook", "Cook", false, "leave", null);
            Check(e6 == "hansa:crew-last-captain" && e7 == null && e8 == null && e9 == null && e10 == null && ship.Crew.Count == 2 &&
                  ship.IsCaptain("hansa-selftest-mate") && !ship.IsCaptain("hansa-selftest-capt") &&
                  ship.WatchedAttributes.GetTreeAttribute(EntityShip.CrewKey)?.Count == 2,
                $"her last captain can't leave; a captain makes others captains or not; anyone leaves ({e6}, {e7}, {e8}, {e9}, {e10}); synced");
            ship.Anchored = false;
            ship.SailSet = 2;
            var l = def.DamageParts;
            var panel = l.Parts.Where(p => p.Kind == DamageKind.Hull && p.Center.Y < def.Waterline - 1.5 && Math.Abs(p.Center.X) < 4).First();
            WaitFor(() => ship.LaidUp && ship.Anchored && ship.SailSet == 0, 3, laid =>
            {
                Check(laid && ship.Anchored && ship.SailSet == 0 && ship.WatchedAttributes.GetBool("hansaLaidUp") && !ship.MayBoard(null),
                    "her crew offline: laid up, anchored and furled, closed to others (and clients know)");
                var hit = ship.Damage(panel.Center, new Vec3d(0, 0, 1), 5000, 0.3f, "cannon");
                ship.DamagePart(panel.Index, 0.85f);
                double w0 = ship.Water;
                WaitFor(() => false, 4, _ =>
                {
                    Check(hit == null && ship.Water == w0 && ship.WaterRate == 0,
                        $"laid up she takes no damage from a shot and no water through a hole ({ship.Water:0.00} t)");
                    // her list cleared (the last name off): in commission, and the hole leaks
                    ship.ChangeCrew("hansa-selftest-capt", "Capt", false, "leave", null);
                    ship.ChangeCrew("hansa-selftest-mate", "Mate", false, "leave", null);
                    WaitFor(() => ship.Water > w0 + 0.5, 8, leaks =>
                    {
                        Check(!ship.HasCrewList && !ship.LaidUp && leaks, $"with no crew list she's in commission again, and the hole leaks ({ship.Water:0.0} t)");
                        ship.RaiseAlarm("kraken");
                        WaitFor(() => false, 1.5, __ =>
                        {
                            Check(ship.Seats.All(st => st?.Passenger == null), "an alarm with no one asleep aboard wakes no one");
                            EntityShip.KnownPlayers = null;
                            ship.Die(EnumDespawnReason.Removed);
                            then();
                        });
                    });
                });
            });
        }

        /// <summary>
        /// (0.3.12) The sails' strain: in force 12 under full sail they strain for minutes of real time before the first
        /// tear (half a minute of it leaves them sound), the strain eases off when she's furled, and is synced to clients.
        /// The strain tick is driven by hand (a second a step), so the check takes no time and no one need be aboard.
        /// </summary>
        void SailStrainWorks(HansaMod mod, ShipDefinition def, BlockPos spawn, double wy, Action then)
        {
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", cfg.Ship));
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X - 25.5, wy, spawn.Z + 0.5);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            sea?.SetOverride(new Sea.WindOverride { FromDegrees = 0, Strength = 1.5, StartHours = sea.Hours - 24 });
            WaitFor(() => ship.PartDamage != null, 3, ready =>
            {
                var l = def.DamageParts;
                var sails = l.Parts.Where(p => p.Kind == DamageKind.Sail).ToList();
                ship.Speed = 0;
                ship.SailFraction = 1;
                double wind = ship.WindHere(ship.Pos.X, ship.Pos.Z).Strength;
                for (int i = 0; i < 30; i++) ship.SailStrainTick(1);
                float half = ship.SailStrain;
                bool sound = sails.All(p => ship.PartDamage.Hurt[p.Index] == 0);
                int warn = WindPanel.SailWarning(ship, ship.WindHere(ship.Pos.X, ship.Pos.Z), out string said);
                Check(ready && wind > 1.2 && half > 0.1f && half < 0.4f && sound && warn >= 1,
                    $"force 12 (wind {wind:0.00}) under full sail: half a minute strains the sails ({half:0.00}) but doesn't tear them; the panel warns '{said}'");
                for (int i = 0; i < 240; i++) ship.SailStrainTick(1);
                bool torn = sails.Any(p => ship.PartDamage.Hurt[p.Index] > 0);
                Check(ship.SailStrain >= 1 && torn, $"held there four more minutes the strain is full and they tear (worst {sails.Max(p => ship.PartDamage.Hurt[p.Index]):0.00})");
                ship.SailFraction = 0;
                for (int i = 0; i < 100; i++) ship.SailStrainTick(1);
                ship.SaveState(true);
                Check(ship.SailStrain == 0 && ship.WatchedAttributes.GetTreeAttribute("hansa")?.GetFloat("sailStrain", -1) == 0 &&
                      Math.Abs((ship.WatchedAttributes.GetTreeAttribute("hansa")?.GetFloat("sailGaleW", 0) ?? 0) - SailWear.ForceFloor(mod.Config.SailStrainForce > 0 ? mod.Config.SailStrainForce : 9)) < 0.01,
                    "furled, the strain eases right off in a minute and a half, and clients know (and the server's sail limits: 0.3.24)");
                sea?.SetOverride(null);
                ship.Die(EnumDespawnReason.Removed);
                then();
            });
        }

        void HearthWorks(HansaMod mod, ShipDefinition def, BlockPos spawn, double wy, Action then)
        {
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", cfg.Ship));
            Item I(string c) => sapi.World.GetItem(new AssetLocation("game", c));
            Block B(string c) => sapi.World.GetBlock(new AssetLocation("game", c));
            var firewood = I("firewood");
            var meat = I("redmeat-raw");
            var firestarter = I("firestarter");
            var torch = B("torch-basic-lit-up");
            var pot = B("claypot-red-fired");
            var grain = I("grain-spelt");
            if (type == null) { Check(false, $"her entity type ({cfg.Ship})"); then(); return; }
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X - 25.5, wy, spawn.Z + 0.5);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            // (her design's hearth, or the one her design lays as a piece: a grid ship's galley)
            var hf = def.Fixture("hearth") ?? ship.HearthFixture;
            if (hf == null)
            {
                // (18 October 2026) the caravel has no galley yet
                Log($"{def.Code} has no hearth: the firehearth checks are for ships with one");
                ship.Die(EnumDespawnReason.Removed); then(); return;
            }
            Check(hf != null && firewood != null && meat != null && firestarter != null && torch != null && pot != null && grain != null,
                $"the firehearth ({(hf == null ? "none" : def.Fixture("hearth") != null ? "the design's" : "laid as a piece")}), and vanilla's firewood, raw red meat, a firestarter, a lit torch ({torch?.Code}), a clay pot ({pot?.Code}) and grain to try it with");
            if (hf == null || firewood == null || meat == null || firestarter == null || torch == null || pot == null || grain == null) { ship.Die(EnumDespawnReason.Removed); then(); return; }
            var inv = ship.HearthInv;
            Check(ship.HearthFixture == hf && inv is Vintagestory.GameContent.InventorySmelting && inv.Count == 7 && inv.InventoryID == EntityShip.HearthInventoryClass + "-" + ship.EntityId &&
                  !ship.Fire.IsBurning && ship.Fire.BurnState == "cold" && ship.HearthOn,
                $"her hearth has vanilla's firepit inventory ({inv?.GetType().Name}, {inv?.Count} slots, {inv?.InventoryID}), cold");
            var hand = new DummySlot(new ItemStack(torch));
            Check(ship.TryIgniteHearth(null, hand, 3.2f, 0) == "hansa:hearth-no-fuel" && !ship.Fire.CanIgniteFuel, "with no fuel in it a torch won't light it");

            // two firewood and a raw red meat in it
            inv[0].Itemstack = new ItemStack(firewood, 2);
            inv[0].MarkDirty();
            inv[1].Itemstack = new ItemStack(meat, 1);
            inv[1].MarkDirty();
            var saved = ship.WatchedAttributes.GetTreeAttribute("hansaHearth")?.GetTreeAttribute("inv")?.GetTreeAttribute("slots");
            Check(saved?.GetItemstack("0")?.StackSize == 2 && saved.GetItemstack("1")?.Collectible?.Code?.Path == "redmeat-raw",
                "two firewood and a raw red meat put in: saved with her and sent to clients");
            var stick = new DummySlot(new ItemStack(I("stick"), 1));
            Check(ship.TryIgniteHearth(null, stick, 3.2f, 0) == "hansa:hearth-needs-igniter" && ship.TryIgniteHearth(null, hand, 1f, 0) == "hansa:hearth-not-yet" &&
                  !ship.Fire.CanIgniteFuel, "a stick doesn't light it, nor a torch held to it only a second");
            var drill = new DummySlot(new ItemStack(firestarter));
            int dur = firestarter.GetRemainingDurability(drill.Itemstack);
            string miss = ship.TryIgniteHearth(null, drill, 3.2f, 0.9);
            Check(miss == "hansa:hearth-no-catch" && !ship.Fire.CanIgniteFuel && firestarter.GetRemainingDurability(drill.Itemstack) == dur,
                $"a firestarter's try that doesn't catch (three in four don't, as vanilla's): {miss}");
            string hit = ship.TryIgniteHearth(null, drill, 3.2f, 0.1);
            Check(hit == null && ship.Fire.CanIgniteFuel && firestarter.GetRemainingDurability(drill.Itemstack) == dur - 1,
                $"one that catches: the fuel will catch, the firestarter a point worn ({dur} → {firestarter.GetRemainingDurability(drill.Itemstack)})");
            // what vanilla's numbers say: lit at the next tick, the meat cooked this long after
            var simFire = new HearthFire();
            simFire.Ignited(0);
            var predicted = HearthSimContents.Run(simFire, new HearthSimContents { FuelCount = 2, InputCount = 1, InputMelting = 150, InputDuration = 30 }, 120);
            long lit = 0;
            WaitFor(() => ship.Fire.IsBurning, 2, burning =>
            {
                lit = sapi.World.ElapsedMilliseconds;
                var sent = ship.WatchedAttributes.GetTreeAttribute("hansaHearth");
                Check(burning && ship.Fire.BurnState == "lit" && inv[0].StackSize == 1 && ship.Fire.MaxTemperature == 700 && sent?.GetString("burnstate") == "lit" &&
                      ship.TryIgniteHearth(null, hand, 3.2f, 0) == "hansa:hearth-burning",
                    $"it catches at the next burn tick: lit, one firewood burning (700 °C for 24 s), one left; clients told; it can't be lit again");
                // someone by the fire in the forecastle is warmed; someone on the waist isn't
                EntityProperties chickenType = null;
                foreach (string code in new[] { "chicken-rooster", "chicken-hen", "chicken-adult" })
                    if ((chickenType = sapi.World.GetEntityType(new AssetLocation("game", code))) != null) break;
                EntityAgent cook = null, deckhand = null;
                if (chickenType != null)
                {
                    var spot = ship.FindStandSpot(hf.PosVec, hf.SelBox);
                    cook = (EntityAgent)sapi.World.ClassRegistry.CreateEntity(chickenType);
                    ship.LocalToWorld(spot.X, spot.Y, spot.Z, out double cx, out double cy, out double cz);
                    cook.Pos.SetPos(cx, cy, cz);
                    sapi.World.SpawnEntity(cook);
                    cook.TryMount(ship.GetOrCreateDeckSeat("hansa-selftest-cook", spot.X, spot.Y, spot.Z));
                    var b = def.BoardPoints[0];
                    deckhand = (EntityAgent)sapi.World.ClassRegistry.CreateEntity(chickenType);
                    ship.LocalToWorld(b[0], b[1], b[2], out double bx, out double by, out double bz);
                    deckhand.Pos.SetPos(bx, by, bz);
                    sapi.World.SpawnEntity(deckhand);
                    deckhand.TryMount(ship.GetOrCreateDeckSeat("hansa-selftest-deckhand", b[0], b[1], b[2]));
                }
                WaitFor(() => inv[2].Itemstack?.Collectible?.Code?.Path == "redmeat-cooked", 90, cooked =>
                {
                    double secs = (sapi.World.ElapsedMilliseconds - lit) / 1000.0;
                    double expect = predicted.FirstCooked - predicted.LitAt;
                    Check(cooked && inv[1].Empty && inv[2].StackSize == 1 && Math.Abs(secs - expect) < Math.Max(3, expect * 0.08),
                        $"the raw red meat cooked {secs:0.0} s after it caught, as vanilla's numbers say ({expect:0.0} s: 150 °C, then 30 s of cooking, faster the hotter); " +
                        $"the fire at {ship.Fire.FurnaceTemperature:0} °C, the meat at {inv[2].Itemstack?.Collectible.GetTemperature(sapi.World, inv[2].Itemstack):0} °C");
                    bool sheltered = cook != null && ShipShelter.IsSheltered(cook);
                    float warm = cook == null ? 0 : ShipShelter.HearthHeat(cook, sheltered);
                    float waist = deckhand == null ? -1 : ShipShelter.HearthHeat(deckhand, ShipShelter.IsSheltered(deckhand));
                    Check(sheltered && ShipShelter.Room(cook)?.Code == "forecastle" && warm > 5 && waist == 0,
                        $"while it burns the forecastle is warm: {warm:0.0} of its 10 for someone by it there (a closed room); none on the waist ({waist:0.0})");
                    WaitFor(() => !ship.Fire.IsBurning, 30, wentOut =>
                    {
                        double burnt = (sapi.World.ElapsedMilliseconds - lit) / 1000.0;
                        Check(wentOut && burnt > 44 && burnt < 54 && inv[0].Empty && ship.Fire.BurnState == "extinct" && ship.Fire.CanIgniteFuel &&
                              ship.WatchedAttributes.GetTreeAttribute("hansaHearth")?.GetString("burnstate") == "extinct",
                            $"out of fuel it goes out ({burnt:0.0} s after it caught: two firewood), its embers left; clients told");
                        float embers = cook == null ? 0 : ShipShelter.HearthHeat(cook, true);
                        Check(embers > 0 && embers < 0.3, $"its embers give a little warmth ({embers:0.00})");
                        foreach (var e in new Entity[] { cook, deckhand }) { (e as EntityAgent)?.TryUnmount(); if (e?.Alive == true) e.Die(EnumDespawnReason.Removed); }

                        // the cooked meat taken out; a pot with two grain and new fuel on the embers: they catch, and it makes porridge
                        inv[2].Itemstack = null;
                        inv[2].MarkDirty();
                        inv[1].Itemstack = new ItemStack(pot);
                        inv[1].MarkDirty();
                        inv[3].Itemstack = new ItemStack(grain, 1);
                        inv[3].MarkDirty();
                        inv[4].Itemstack = new ItemStack(grain, 1);
                        inv[4].MarkDirty();
                        inv[0].Itemstack = new ItemStack(firewood, 3);
                        inv[0].MarkDirty();
                        string willMake = inv.GetOutputText();
                        Check(inv.HaveCookingContainer && inv.CookingSlots.Length == 4 && !string.IsNullOrEmpty(willMake),
                            $"a clay pot in the input opens its four cooking slots; two grain in it: \"{willMake}\"");
                        float potMelt = pot.GetMeltingPoint(sapi.World, inv, inv[1]), potCook = pot.GetMeltingDuration(sapi.World, inv, inv[1]);
                        var simPot = new HearthFire { FurnaceTemperature = ship.Fire.FurnaceTemperature };
                        simPot.Ignited(0);
                        var potPredicted = HearthSimContents.Run(simPot, new HearthSimContents { FuelCount = 3, InputCount = 1, InputMelting = potMelt, InputDuration = potCook, InputMax = 200 }, 200);
                        long relit = sapi.World.ElapsedMilliseconds;
                        WaitFor(() => ship.Fire.IsBurning, 2, again =>
                        {
                            Check(again && inv[0].StackSize == 2, "new fuel on its embers catches by itself (no firestarter)");
                            // saved and loaded while it burns with the pot on: a carrack loaded with it has it burning so
                            var tree = new TreeAttribute();
                            using (var ms = new System.IO.MemoryStream())
                            {
                                ship.WatchedAttributes.ToBytes(new System.IO.BinaryWriter(ms));
                                tree.FromBytes(new System.IO.BinaryReader(new System.IO.MemoryStream(ms.ToArray())));
                            }
                            var copy = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
                            if (tree.GetTreeAttribute("hansaHearth") is ITreeAttribute h) copy.WatchedAttributes["hansaHearth"] = h;
                            copy.Pos.SetPos(ship.Pos.X + 40, ship.Pos.Y, ship.Pos.Z);
                            copy.Pos.Yaw = ship.Pos.Yaw;
                            sapi.World.SpawnEntity(copy);
                            var ci = copy.HearthInv;
                            Check(ci != null && ci[0].Itemstack?.StackSize == 2 && ci[1].Itemstack?.Block == pot && ci[3].Itemstack?.Collectible == grain && ci[4].Itemstack?.StackSize == 1 &&
                                  copy.Fire.IsBurning && copy.Fire.BurnState == "lit" && Math.Abs(copy.Fire.FurnaceTemperature - ship.Fire.FurnaceTemperature) < 30 && ci.HaveCookingContainer,
                                $"her hearth is saved with her: a carrack loaded with it has it burning ({copy.Fire.FurnaceTemperature:0} °C) with the pot, its grain and the firewood in it");
                            copy.Die(EnumDespawnReason.Removed);
                            WaitFor(() => inv[2].Itemstack?.Collectible is Vintagestory.GameContent.BlockCookedContainer, 120, made =>
                            {
                                double secs2 = (sapi.World.ElapsedMilliseconds - relit) / 1000.0;
                                double expect2 = potPredicted.FirstCooked;
                                var meal = inv[2].Itemstack?.Collectible as Vintagestory.GameContent.BlockCookedContainer;
                                string recipe = meal?.GetRecipeCode(sapi.World, inv[2].Itemstack);
                                float servings = meal?.GetQuantityServings(sapi.World, inv[2].Itemstack) ?? 0;
                                Check(made && inv[1].Empty && recipe == "porridge" && servings == 1 && inv[3].Empty && Math.Abs(secs2 - expect2) < Math.Max(4, expect2 * 0.1),
                                    $"the pot of grain cooks into a pot of porridge ({recipe}, {servings} serving) in the output, {secs2:0.0} s after the fuel went on " +
                                    $"(vanilla's numbers: {expect2:0.0} s: the pot to {potMelt:0} °C, then {potCook:0} s)");
                                // switched off: it goes out, and can't be lit
                                mod.Config.Hearth = false;
                                WaitFor(() => !ship.Fire.IsBurning && !ship.HearthOn, 2, off =>
                                {
                                    Check(off && ship.Fire.BurnState == "cold" && ship.WatchedAttributes.GetTreeAttribute("hansa")?.GetBool("hearth", true) == false &&
                                          ship.TryIgniteHearth(null, hand, 3.2f, 0) == "hansa:hearth-off",
                                        "with Hearth off it goes out at once, clients are told, and it can't be lit");
                                    mod.Config.Hearth = true;
                                    ship.Die(EnumDespawnReason.Removed);
                                    then();
                                });
                            });
                        });
                    });
                });
            });
        }

        void Finish()
        {
            Log(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            if (cfg.Shutdown) sapi.Event.RegisterCallback(_ => sapi.Server.ShutDown(), 1000);
        }
    }

    /// <summary>
    /// A stand-in for a player whose spawn is bound to a ship, for the self-test (its server has nobody on it, and
    /// vanilla's IServerPlayer can't be implemented outside the game): a uid, a name, a spawn position, what it's told.
    /// </summary>
    public class StandIn : ISpawnHolder
    {
        public string Uid => "hansa-selftest";
        public string Name => "Tester";
        public IServerPlayer Player => null;
        public PlayerSpawnPos Spawn;
        public int SpawnSets;
        public readonly List<string> Said = new();

        public void SetSpawn(PlayerSpawnPos pos) { Spawn = pos; SpawnSets++; }
        public void ClearSpawn() => Spawn = null;
        public bool TryReadSpawn(out PlayerSpawnPos pos) { pos = Spawn; return true; }
        public void Tell(string langCode, params object[] args) => Said.Add(Vintagestory.API.Config.Lang.Get(langCode, args));
    }
}
