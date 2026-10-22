using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public partial class SelfTest
    {
        /// <summary>
        /// (0.3.18) The game's own blocks read as step A reads them: building mode (planks a partition only with a saw in the
        /// off hand), fire bricks the ship's hearth, the clay oven her oven, the brick chimney a flue segment (the codes the
        /// contract names exist), and the saw found in an off hand.
        /// </summary>
        void StepABlocks()
        {
            var w = sapi.World;
            Block B(string code) => w.GetBlock(new AssetLocation(code)) is Block b && b.Id != 0 ? b : null;
            var planks = B("game:planks-oak-ud");
            var fence = B("game:woodenfence-oak-ew-free");
            if (planks != null && fence != null)
            {
                var off = new VanillaPieceBlock(w, new ItemStack(planks), false);
                var on = new VanillaPieceBlock(w, new ItemStack(planks), true);
                var foff = new VanillaPieceBlock(w, new ItemStack(fence), false);
                Check(!off.Planks && on.Planks && !foff.Fence && VanillaPieceBlock.IsBuildingBlock(planks) && VanillaPieceBlock.IsBuildingBlock(fence),
                    "0.3.18: planks and fences are structure only in building mode (a saw in the off hand); without one, plain blocks");
            }
            else Log("0.3.18: no planks or fence block: building mode not checked");
            foreach (var kind in new[] { "hearth", "oven", "flue" })
            {
                var b = B(ModularPieceDef.DefaultBlock(kind));
                var vb = b == null ? null : new VanillaPieceBlock(w, new ItemStack(b), false);
                Check(vb != null && (kind == "hearth" ? vb.Hearth : kind == "oven" ? vb.Oven : vb.Flue) && !vb.AshoreOnly && !vb.Container,
                    $"0.3.18: {ModularPieceDef.DefaultBlock(kind)} is the ship's {kind} piece (no saw needed)");
            }
            var firepit = B("game:firepit-cold");
            if (firepit != null) Check(VanillaPieceBlock.IsHearthBlock(firepit), "0.3.18: a firepit (creative) is a hearth piece too");
            // (0.3.19) a vanilla ladder is the ladder-stair, plank stairs the walked stair (in building mode), and the codes the
            // contract names exist
            var ladder = B(ModularPieceDef.DefaultBlock("ladder"));
            var stairs = B(ModularPieceDef.DefaultBlock("stair"));
            var lon = ladder == null ? null : new VanillaPieceBlock(w, new ItemStack(ladder), true);
            var son = stairs == null ? null : new VanillaPieceBlock(w, new ItemStack(stairs), true);
            var loff = ladder == null ? null : new VanillaPieceBlock(w, new ItemStack(ladder), false);
            Check(lon != null && lon.Ladder && !lon.Stairs && son != null && son.Stairs && !son.Ladder && !loff.Ladder && VanillaPieceBlock.IsBuildingBlock(ladder) &&
                  VanillaPieceBlock.WoodOf(ladder.Code.ToString()) == "oak",
                $"0.3.19: {ModularPieceDef.DefaultBlock("ladder")} is a ladder-stair and {ModularPieceDef.DefaultBlock("stair")} a walked stair in building mode; out of it, plain blocks");
            // (0.3.23) the ship's stairs: the mod's own blocks (steep 2, long 3, their own handrails), placed in building mode
            // only; vanilla's plank stairs are plain decoration now
            var steep = B(ModularPieceDef.SteepStair);
            var plank = B("game:plankstairs-oak-up-north-free");
            var ston = steep == null ? null : new VanillaPieceBlock(w, new ItemStack(steep), true);
            var stoff = steep == null ? null : new VanillaPieceBlock(w, new ItemStack(steep), false);
            var pon = plank == null ? null : new VanillaPieceBlock(w, new ItemStack(plank), true);
            Check(ston != null && ston.Stairs && ston.StairCells == 2 && ston.StairRails && son != null && son.StairCells == 3 && son.StairRails && stoff != null && !stoff.Stairs && stoff.NeedsBuilding &&
                  (pon == null || !pon.Stairs) && VanillaPieceBlock.IsBuildingBlock(steep) && steep is BlockShipStair && VanillaPieceBlock.WoodOf(steep.Code.ToString()) == "oak",
                $"0.3.23: {ModularPieceDef.SteepStair} (2 cells) and {ModularPieceDef.LongStair} (3) are ship's stairs with their own handrails in building mode, refused out of it; plank stairs aren't stairs any more");
            // (0.1.0-dev.6, note 36) the short one (1 cell)
            var shortB = B(ModularPieceDef.ShortStair);
            var shon = shortB == null ? null : new VanillaPieceBlock(w, new ItemStack(shortB), true);
            Check(shon != null && shon.Stairs && shon.StairCells == 1 && shon.StairRails && shortB is BlockShipStair,
                $"0.1.0-dev.6 (note 36): {ModularPieceDef.ShortStair} is a ship's stair 1 cell long with its own handrails in building mode");
        }

        /// <summary>
        /// (0.3.19, the charter's double launch) The charter used as the engine uses it on the server (ServerSystemBlockSimulation's
        /// StopHeldItemUse: the client's steps replayed, the step that ends the use calling the stop, then the packet's own stop):
        /// one launch per use; a second use within the cooldown, and the use going on while the button stays down, none.
        /// </summary>
        void CharterOnce()
        {
            var deed = sapi.World.Items.OfType<ItemShipDeed>().FirstOrDefault(i => !i.IsBoat);
            var et = sapi.World.GetEntityType(new AssetLocation("game", "chicken-rooster")) ?? sapi.World.EntityTypes.FirstOrDefault(t => t.Class == "EntityAgent");
            EntityAgent agent = null;
            try { agent = et == null ? null : sapi.World.ClassRegistry.CreateEntity(et) as EntityAgent; } catch (Exception) { }
            if (deed == null || agent?.Controls == null) { Log("0.3.19: no charter or no stand-in entity: the charter's one launch per use not checked"); return; }
            int launches = 0;
            ItemShipDeed.LaunchForTest = (e, s) => { launches++; return null; };
            try
            {
                var slot = new DummySlot(new ItemStack(deed));
                int Use(long beganAgo, bool first)
                {
                    int before = launches;
                    var c = agent.Controls;
                    EnumHandHandling h = EnumHandHandling.NotHandled;
                    deed.OnHeldUseStart(slot, agent, null, null, EnumHandInteract.HeldItemInteract, first, ref h);
                    if (h == EnumHandHandling.NotHandled) return -1;
                    var useType = EnumHandInteract.HeldItemInteract;
                    c.HandUse = useType;
                    c.UsingBeginMS = sapi.World.ElapsedMilliseconds - beganAgo;
                    c.UsingCount = 0;
                    // the client's stop packet after its 1.5 s of steps (one a frame): the server replays them
                    float secs = (sapi.World.ElapsedMilliseconds - c.UsingBeginMS) / 1000f;
                    while (c.HandUse != EnumHandInteract.None && c.UsingCount < 90)
                    {
                        c.UsingCount++;
                        c.HandUse = deed.OnHeldUseStep(secs, slot, agent, null, null);
                        if (c.HandUse == EnumHandInteract.None) deed.OnHeldUseStop(secs, slot, agent, null, null, useType);
                        secs += 0.02f;
                    }
                    c.HandUse = EnumHandInteract.None;
                    deed.OnHeldUseStop(secs, slot, agent, null, null, useType);
                    return launches - before;
                }
                int first = Use(1600, true);
                int again = Use(1700, true);
                int held = Use(1600, false);
                Check(first == 1 && again == 0 && held == -1,
                    $"0.3.19: the charter used as the engine does (its stop called twice for one use): one launch ({first}); another use within {LaunchGuard.CooldownMs} ms none ({again}); the use going on with the button held isn't handled ({held})");
            }
            finally
            {
                ItemShipDeed.LaunchForTest = null;
                LaunchGuard.Reset("e" + agent.EntityId);
            }
        }

        /// <summary>
        /// (0.3.21, step C) carrack-m's gun places aboard: launched with her guns laid (each mounted, in her collision); one
        /// run out and fired as any gun; taken up only housed and cold (refused run out, refused hot), then gone (not in her
        /// collision, not fired, not laid); a cell under its room taken up refuses it; mounted again as designed. Another is
        /// left taken up for the reload check: its place's id, or null.
        /// </summary>
        string GunPlacesAboard(EntityShip ship)
        {
            var def = ship.Def;
            if (def.GunPlaces == null || def.GunPlaces.Length == 0) { Log("0.3.21: carrack-m has no gun places: guns as items not checked"); return null; }
            int laidWant = def.GunPlaces.Count(p => p.StartsLaid);
            int mounted = def.GunPlaces.Count(p => ship.GunPlaceLaid(p) && ship.GunMounted(ship.GunOf(p)) && ship.GunOf(p).Box is double[] b &&
                ship.Grid.SolidAt((b[0] + b[3]) / 2, b[1] + 0.3, (b[2] + b[5]) / 2));
            Check(laidWant >= 10 && mounted == laidWant, $"0.3.21: carrack-m launched with her {laidWant} laid guns mounted, in her collision ({mounted})");
            var p3 = def.GunPlaces.FirstOrDefault(p => p.Gun == "gun-port-3" && ship.GunPlaceLaid(p)) ?? def.GunPlaces.FirstOrDefault(p => ship.GunPlaceLaid(p));
            var g = ship.GunOf(p3);
            if (g == null) { Check(false, "0.3.21: a gun place of carrack-m with its gun"); return null; }
            // fired as any gun
            ship.OpenGunPort(g, true);
            string fired = ship.FireGun(g, null, null);
            string runOut = ship.TakeGunProblem(p3);
            ship.HouseGun(g);
            string hot = ship.TakeGunProblem(p3);
            Check(fired == null && ship.Gun(g.Code).Shots == 1 && (g.Lid == null || runOut == "hansa:gun-take-run-out") && hot == "hansa:gun-take-hot",
                $"0.3.21: her {g.Code} (a gun place's) runs out and fires ({fired ?? "fired"}); it isn't taken up run out ({runOut}) nor hot ({hot})");
            ship.Gun(g.Code).FiredMs = long.MinValue / 2;
            var cb = g.Box;
            double cx = (cb[0] + cb[3]) / 2, cy = cb[1] + 0.3, cz = (cb[2] + cb[5]) / 2;
            string took = ship.TakeGun(p3);
            var saved = ship.WatchedAttributes.GetTreeAttribute("hansaGunPlaces");
            Check(took == null && ship.GunAbsent(g) && !ship.Grid.SolidAt(cx, cy, cz) && ship.FireGun(g, null, null) == "hansa:gunplace-no-gun" &&
                  saved?.Count == 1,
                $"0.3.21: cold and housed, {g.Code} is taken up ({took ?? "ok"}): gone from her collision, not fired, saved as one difference");
            // a cell under its room taken up: no gun there till it's laid again
            var cells = ship.GunPlaceCells(p3);
            int cell = cells.FirstOrDefault(n => ship.Slots != null && ship.Slots.Filled(n) && ship.TakeProblem(n) == null, -1);
            if (cell >= 0)
            {
                string tc = ship.TakeCell(cell, null);
                string refused = ship.MountGunProblem(p3, false);
                string lc = ship.LayCell(cell, false);
                Check(tc == null && refused == "hansa:gunplace-cell-empty" && lc == null, $"0.3.21: with a cell under its room taken up ({ship.SlotLayout.Slots[cell].Id}) the gun is refused ({refused}); laid again");
            }
            else Log($"0.3.21: {p3.Id} has no deck cells of its own under it (fixed strips): the empty-cell refusal is checked offline");
            string back = ship.MountGun(p3, false);
            Check(back == null && ship.GunMounted(g) && ship.Grid.SolidAt(cx, cy, cz) && ship.WatchedAttributes.GetTreeAttribute("hansaGunPlaces")?.Count == 0,
                $"0.3.21: {g.Code} mounted again ({back ?? "ok"}): in her collision, as designed");
            ship.OpenGunPort(g, true);
            string again = ship.FireGun(g, null, null);
            Check(again == null, $"0.3.21: mounted again, it fires ({again ?? "fired"})");
            ship.Gun(g.Code).FiredMs = long.MinValue / 2;
            ship.HouseGun(g);
            SternChaserAboard(ship);
            Notes0329Aboard(ship);
            Notes0330Aboard(ship);
            // another left taken up for the reload
            var other = def.GunPlaces.FirstOrDefault(p => p != p3 && ship.GunPlaceLaid(p));
            string otherErr = other == null ? "no other place with a gun" : ship.TakeGun(other);
            if (otherErr != null) { Log($"0.3.21: no gun left taken up for the reload ({otherErr})"); return null; }
            return other.Id;
        }

        /// <summary>
        /// (0.3.25, note 179) A stern chaser: its place starts empty; a gun mounted on it runs out (its lid opens) and fires
        /// aft, the ball leaving the muzzle outside the transom; while it's mounted the poop's broadside gun in its way on that
        /// side can't be mounted (gunplace-gun-room); taken up again (housed, cold), that one can.
        /// </summary>
        /// <summary>
        /// (0.3.29) A stair walked up and down along its middle, or a hand (0.1) to either side of it: the quarterdeck stair
        /// beside the companion's wall (that wall's voxel a quarter block into its cell, 0.05 into a man on its middle).
        /// </summary>
        static bool WalkNear(VoxelGrid g, MountPoint foot, int dir, double rise, double run, out double top, out double back, out string off)
        {
            off = "";
            if (PiecePlanner.WalkFlight(g, foot, dir, rise, out top, out back, run)) return true;
            foreach (double s in new[] { 0.1, -0.1 })
            {
                double sx = MountLayout.DZ[dir] * s, sz = MountLayout.DX[dir] * s;
                var m = new MountPoint { Id = foot.Id, Kind = foot.Kind, Deck = foot.Deck, X = foot.X + sx, Y = foot.Y, Z = foot.Z + sz, I = foot.I, K = foot.K, Box = foot.Box };
                if (PiecePlanner.WalkFlight(g, m, dir, rise, out top, out back, run)) { off = $" ({s:+0.0;-0.0} off its middle: a wall beside it)"; return true; }
            }
            return false;
        }

        void Notes0329Aboard(EntityShip ship)
        {
            var def = ship.Def;
            var l = ship.SlotLayout;
            try
            {
                // note 232: the quarterdeck's third gun place takes a gun on both sides of a new ship (nobody near)
                var q3 = def.GunPlaces.Where(p => p.Gun.EndsWith("-quarter-3")).ToList();
                var said = new List<string>();
                bool all = q3.Count == 2;
                foreach (var p in q3)
                {
                    string err = ship.MountGun(p, true);
                    said.Add($"{p.Id} {err ?? "mounted"}");
                    all &= err == null;
                    if (err == null) ship.TakeGun(p);
                }
                Check(all, $"0.3.29 (note 232): guns mounted on the quarterdeck's third places, both sides ({string.Join(", ", said)})");
                // ((0.3.30, notes 236, 239) no poop side ports, no boards or trapdoor ports: every place's port fixed and lidded)
                bool noPoop = !def.GunPlaces.Any(p => p.Gun.Contains("-poop-"));
                bool fixedPorts = def.GunPlaces.All(p => p.Port == null && p.GunPort == null) && !l.Slots.Any(s => s.IsPort || s.IsGunPort);
                Check(noPoop && fixedPorts, $"0.3.30 (notes 236, 239): no gun places on the poop's sides ({noPoop}), every port fixed, no boards or trapdoor ports ({fixedPorts})");
                // note 233: the companion beside the quarterdeck stair, laid as designed
                var stair = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Stair && p.Mount == "f:lower:-2:-1");
                var door = ship.Pieces.At("b:main:0:-1:w:0");
                int walls = ship.Pieces.All.Count(p => p.Role == PieceRole.Partition && p.Mount != null && (p.Mount.StartsWith("b:main:-3:-2:s") || p.Mount.StartsWith("b:main:-2:-2:s") || p.Mount.StartsWith("b:main:-1:-2:s") ||
                                                                                                          p.Mount.StartsWith("b:main:-3:0:n") || p.Mount.StartsWith("b:main:-2:0:n") || p.Mount.StartsWith("b:main:-1:0:n")));
                bool well = new[] { "main:-3:-1", "main:-2:-1", "main:-1:-1", "beam:main:-2:-1" }.All(id => l.IndexOf(id) is int n && n >= 0 && !ship.Slots.Filled(n));
                Check(stair != null && door?.Role == PieceRole.Door && walls >= 12 && well,
                    $"0.3.29 (note 233): a new carrack-m's companion beside the quarterdeck stair: its well open ({well}), its stair down ({stair?.Mount ?? "none"}), {walls} walls, its door ({door?.Mount ?? "none"})");
            }
            catch (Exception e) { Check(false, "0.3.29: notes 232-233 aboard: " + e); }
        }

        /// <summary>
        /// (0.3.30) Aboard a new carrack-m: a shot on her fixed planking cracks or knocks out the nearest panel (note 234: no
        /// scars), an old save's scar becomes a cracked slot, a hull spot two layers deep laid from inside both at once (the
        /// user: "plug both from inside"), the planking behind a sound ceiling mended through it.
        /// </summary>
        void Notes0330Aboard(EntityShip ship)
        {
            try
            {
                var hl = ship.HullLayout;
                var dl = ship.DamageParts;
                if (hl == null || dl == null) { Check(false, "0.3.30: carrack-m has hull panels and damage parts"); return; }
                var D = dl.D;
                // a shot on a frame's strip between two planking panels over her waterline, starboard: the nearest panel takes it
                int P = Enumerable.Range(0, hl.Count).Where(n => hl.Slots[n].Layer == "outer" && hl.Slots[n].Side > 0 && hl.Slots[n].Half == "a" &&
                    hl.Slots[n].Sill > ship.Def.Waterline + 0.3).OrderBy(n => Math.Abs(hl.Slots[n].Centre.X - 1)).First();
                var s = hl.Slots[P];
                double y = (s.Sill + s.Head) / 2;
                var res = ship.Damage(new Vec3d(s.Box[0] + 0.02, y, s.OuterZ(y) + 0.02), new Vec3d(0, 0, -1), 145, new CannonDef().HitRadius, "cannon");
                var hurt = res?.Hits.Select(h => h.Part).Distinct().Where(i => dl.Parts[i].Slot >= 0).ToList() ?? new List<int>();
                bool twoForms = hurt.All(i => ship.PartDamage.Hurt[i] >= D.Cracked && ship.PartDamage.Wear[i] == 0);
                bool shown = hurt.Any(i => ship.HullPanels[i] != SlotState.Filled);
                Check(hurt.Count > 0 && twoForms && shown && ship.Scars.Count == 0,
                    $"0.3.30 (note 234): a 9-pounder on her fixed planking by {s.Id}: {hurt.Count} panels round it cracked or out ({string.Join(", ", hurt.Take(5).Select(i => hl.Slots[i].Id + " " + ship.HullPanels[i]))}), no scar");
                foreach (int i in hurt) ship.SetHullState(i, SlotState.Filled);
                // an old save's scar: the slot nearest it cracked as she loads
                int Q = Enumerable.Range(0, hl.Count).Where(n => hl.Slots[n].Layer == "outer" && hl.Slots[n].Side < 0 && hl.Slots[n].Sill > ship.Def.Waterline + 0.3)
                    .OrderBy(n => Math.Abs(hl.Slots[n].Centre.X - 4)).First();
                var qc = hl.Slots[Q].Centre;
                ship.Scars.Add(new ShotScar { X = (float)qc.X, Y = (float)qc.Y, Z = (float)qc.Z - 0.5f, DX = 0, DY = 0, DZ = 1, Reach = 1f, Inside = 0 });
                int cracked = ship.ScarsToCracks();
                Check(cracked == 1 && ship.HullPanels[Q] == SlotState.Cracked && ship.Scars.Count == 0,
                    $"0.3.30 (note 234): an older save's shot scar by {hl.Slots[Q].Id} became a cracked panel ({ship.HullPanels[Q]}), no scars kept");
                ship.SetHullState(Q, SlotState.Filled);
                // plugging from inside: her planking and its ceiling both out; the ceiling mended from inside lays both
                int R = Enumerable.Range(0, hl.Count).Where(n => hl.Slots[n].IsInner && hl.Slots[n].PartnerIndex >= 0 && hl.Slots[n].Side > 0 &&
                    hl.Slots[n].Room == "lowerdeck").OrderBy(n => Math.Abs(hl.Slots[n].Centre.X - 3)).First();
                int O = hl.Slots[R].PartnerIndex;
                ship.SetHullState(O, SlotState.Empty);
                ship.SetHullState(R, SlotState.Empty);
                string msg = ship.Repair(R, new List<ItemSlot>(), "game:plank-oak", true, out var job, out _);
                bool both = ship.HullPanels.Filled(O) && ship.HullPanels.Filled(R);
                // the planking alone out behind a sound ceiling: the ceiling's part leads to it, mended from inside
                ship.SetHullState(O, SlotState.Empty);
                int via = ship.DamagePartFor(ShipPart.Hull(hl.Slots[R].Side, hl.Slots[R].Station, hl.Slots[R].Band, hl.Slots[R].HalfIndex, true));
                string msg2 = ship.Repair(via, new List<ItemSlot>(), "game:plank-oak", true, out _, out _);
                bool behind = via == O && ship.HullPanels.Filled(O);
                Check(both && behind, $"0.3.30 (the user: \"plug both from inside\"): {hl.Slots[R].Id} and its planking {hl.Slots[O].Id} both out, the ceiling laid from inside lays both ({msg}: {both}); " +
                      $"the planking alone out behind a sound ceiling is what the ceiling's spot mends ({via == O}, {msg2})");
                ship.SetHullState(O, SlotState.Filled);
                ship.SetHullState(R, SlotState.Filled);
            }
            catch (Exception e) { Check(false, "0.3.30 aboard: " + e); }
        }

        void SternChaserAboard(EntityShip ship)
        {
            var def = ship.Def;
            var sp = def.GunPlaces.FirstOrDefault(p => ShipCompartments.AlongX(ship.GunOf(p)) && ship.GunOf(p).Out[0] < 0);
            if (sp == null) { Check(false, "0.3.25: carrack-m has stern chasers (gun places firing aft)"); return; }
            var sg = ship.GunOf(sp);
            bool emptyAtStart = !ship.GunPlaceLaid(sp);
            string mounted = ship.MountGun(sp, false);
            ship.OpenGunPort(sg, true);
            bool lidOpen = ship.PortOpen(sg);
            ship.SetGunLay(sg, new GunLay(0, 2));
            ship.GunMuzzle(sg, ship.GunLayNow(sg), out var at, out var dir);
            double aft = -(dir.X * Math.Sin(ship.Pos.Yaw) + dir.Z * Math.Cos(ship.Pos.Yaw));
            GunLaying.MuzzleLocal(sg, ship.GunLayNow(sg), out var ml, out _);
            double transomOut = Math.Min(Math.Min(sg.Wall[0], sg.Wall[1]), Math.Min(sg.Wall[2], sg.Wall[3]));
            int shotsBefore = ship.Gun(sg.Code).Shots;
            // (fired 40° up: over the carrack sailing in the pool astern of her, out of the pool)
            string fired = ship.FireGun(sg, null, null, 40);
            int shotsAfter = ship.Gun(sg.Code).Shots;
            // trained to starboard by the crew's laying (positive train: GunLaying.TrainAcross)
            int across = GunLaying.TrainAcross(sg);
            var blocker = def.GunPlaces.FirstOrDefault(p => p != sp && GunPlaces.RoomsClash(sg, ship.GunOf(p)));
            // ((0.3.28, D2.4, note 223) the poop's ports start planked over: that one's opened first (its planking out))
            int bport = blocker == null ? -1 : ship.PortCoverOf(blocker);
            bool reopened = bport >= 0 && ship.Slots.Filled(bport) && ship.TakeCell(bport, null) == null;
            // ((0.3.29) and the port itself put in)
            int gport = blocker?.GunPort != null ? ship.SlotLayout.IndexOf(blocker.GunPort) : -1;
            bool portIn = gport >= 0 && !ship.Slots.Filled(gport) && ship.LayCell(gport, false) == null;
            string refused = blocker == null ? "none in its way" : ship.MountGunProblem(blocker, false);
            ship.Gun(sg.Code).FiredMs = long.MinValue / 2;
            ship.HouseGun(sg);
            string took = ship.TakeGun(sp);
            string then = blocker == null ? null : ship.MountGunProblem(blocker, false);
            if (portIn) ship.TakeCell(gport, null);
            if (reopened) ship.LayCell(bport, false);
            // ((0.3.30, note 236) the poop's broadside guns gone: none may be in its way now)
            Check(emptyAtStart && mounted == null && lidOpen && fired == null && shotsAfter == shotsBefore + 1 && aft > 0.95 && ml.X < transomOut &&
                  across == 1 && (blocker == null || refused == "hansa:gunplace-gun-room") && took == null && then == null,
                $"0.3.25 (note 179): the stern chaser {sp.Id} starts empty ({emptyAtStart}), takes a gun ({mounted ?? "ok"}), runs out with its lid open ({lidOpen}) and fires " +
                $"({fired ?? "fired"}) aft ({aft:0.00} along her stern), its muzzle {transomOut - ml.X:0.00} out of the transom, trained + to starboard ({across}); " +
                $"meanwhile {blocker?.Id ?? "no broadside gun"} in its way is refused ({refused}); taken up again ({took ?? "ok"}), that one may be mounted ({then ?? "ok"})");
        }

        /// <summary>
        /// (0.3.23, notes 170, 172) A stair or ladder put on a foot cell by hand, as the game does it: the player standing a
        /// little before the cell, the crosshair on its floor (her collision), the first place next to the hit the block fits
        /// planned toward the player (the client), then planned again by the server toward the packet's direction (the plan's
        /// Toward) and put with the people check (nobody stands in it). Returns an error or null; placed: the piece.
        /// </summary>
        string HandPlace(EntityShip ship, MountPoint foot, int up, Block block, out ShipPiece placed, bool beams = false)
        {
            placed = null;
            var l = ship.Mounts;
            var feet = PiecePlanner.StairPoint(foot, up, -1.3, 0);
            var eye = new Vec3d(feet[0], feet[1] + 1.62, feet[2]);
            var aim = PiecePlanner.StairPoint(foot, up, 0.5, 0);
            var d = new Vec3d(aim[0] - eye.X, aim[1] - eye.Y, aim[2] - eye.Z);
            d.Mul(1 / d.Length());
            if (!AimPlace.GridHit(ship.Grid, eye, d, 6, out double t, out var normal)) return "the crosshair meets nothing";
            var hit = eye.AddCopy(d.X * t, d.Y * t, d.Z * t);
            var vb = new VanillaPieceBlock(sapi.World, new ItemStack(block), true);
            PiecePlan client = null;
            MountPoint chosen = null;
            foreach (var c in AimPlace.Candidates(l, ship.Pieces, hit, normal, null, x => !ship.Pieces.Taken(x.Id) && ship.MountOpen(x) && x.Kind == MountKind.Floor && x.Level == 0))
            {
                var pl = PiecePlanner.Plan(l, c, vb, AimPlace.Toward(c, eye, hit));
                if (chosen == null) { chosen = c; client = pl; }
                if (pl.Error == null) { chosen = c; client = pl; break; }
            }
            if (client == null) return "no place next to the hit";
            if (client.Error != null) return client.Error;
            if (ship.PlaceProblem(client, true) is string cp) return "the client says " + cp;
            if (chosen != foot) return $"the crosshair's place is {chosen.Id}, not {foot.Id}";
            var server = PiecePlanner.Plan(l, chosen, vb, client.Toward);
            if (server.Error != null) return server.Error;
            if (server.Dir != up) return $"the server planned it climbing {MountLayout.DirCode[server.Dir]}";
            // ((0.3.27) as the server's TryPlacePiece: the beam lengths in a stair's way come out with it)
            return beams ? ship.PlaceTakingBeams(server, new ItemStack(block), null, out placed) : ship.PlacePiece(server, new ItemStack(block), true, out placed);
        }

        /// <summary>
        /// (0.3.23, note 169: "hatches cannot be opened from bottom; can climb through a closed hatch") A shut hatch of hers
        /// seen from below: the crosshair from the deck under it picks it (where her collision is met), it's seen (to open it:
        /// UsePiece's check), a ladder coming up under it stops the climber's head under it.
        /// </summary>
        void HatchFromBelowAboard(EntityShip ship)
        {
            var hatch = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Hatch);
            if (hatch == null) { Log("0.3.23: no hatch aboard: the hatch from below isn't checked here (offline: -- stairs)"); return; }
            bool wasOpen = hatch.Open;
            if (wasOpen) ship.ToggleHatch(null, hatch);
            var b = PiecePlanner.Union(hatch.Solids);
            double cx = (b[0] + b[3]) / 2, cz = (b[2] + b[5]) / 2;
            // from the deck under it, 2.2 blocks off, his eye 1.62 over that deck
            double below = ship.Def.Decks.Where(dk => dk.Y < b[1] - 1).Select(dk => dk.Y).DefaultIfEmpty(b[1] - 3).Max();
            // (an eye on the deck under it whose look at the hatch's middle meets her collision first at the hatch's underside)
            Vec3d eye = null, d = null;
            double wall = 0;
            foreach (var (ox, oz) in new[] { (-1.6, 0.0), (1.6, 0.0), (0.0, -1.6), (0.0, 1.6), (-1.2, -1.2), (1.2, 1.2), (-1.2, 1.2), (1.2, -1.2), (-0.8, 0.0), (0.8, 0.0) })
            {
                var e = new Vec3d(cx + ox, below + 1.62, cz + oz);
                var dd = new Vec3d(cx - e.X, b[1] + 0.05 - e.Y, cz - e.Z);
                dd.Mul(1 / dd.Length());
                double w = ship.Grid.Raycast(e, dd, 8);
                double hy = e.Y + dd.Y * w, hx = e.X + dd.X * w, hz = e.Z + dd.Z * w;
                if (Math.Abs(hy - b[1]) < 0.05 && hx > b[0] && hx < b[3] && hz > b[2] && hz < b[5]) { eye = e; d = dd; wall = w; break; }
            }
            if (eye == null) { Log("0.3.23: no clear look up at her shut hatch from the deck under it: the crosshair from below isn't checked here (offline: -- stairs)"); eye = new Vec3d(cx, below + 1.62, cz); d = new Vec3d(0, 1, 0); wall = ship.Grid.Raycast(eye, d, 8); }
            var picked = ship.Pieces.Pick(eye, d, 8, out double tp);
            bool seen = ShipPieceSet.Seen(ship.DesignGrid, eye, hatch.SeenBox);
            // a ladder up under it (hers, or one made for the test): the climber's head stops under it
            var lad = ship.Ladders.FirstOrDefault(x => !double.IsNaN(ship.LadderCover(x))) ??
                      new LadderDef { Code = "test", Kind = "ladder", Bottom = new[] { cx, below, cz - 0.2 }, Top = new[] { cx, b[4], cz + 0.2 }, ExitBottom = new[] { cx, below, cz - 1 }, ExitTop = new[] { cx, b[4], cz + 1.2 } };
            double under = ship.LadderCover(lad);
            var mv = new DeckMover(lad.ExitBottom[0], lad.ExitBottom[1], lad.ExitBottom[2]) { OnGround = true, TopClosed = ship.LadderTopClosed, CoverUnder = ship.LadderCover };
            double ax = lad.Bottom[0] - lad.ExitBottom[0], az = lad.Bottom[2] - lad.ExitBottom[2], al = Math.Max(1e-6, Math.Sqrt(ax * ax + az * az));
            for (int n = 0; n < 200; n++) mv.Step(ship.Grid, 0.05, ax / al, az / al, 3.0, false, false, 1, new[] { lad }, 1);
            double head = mv.Y + DeckMover.Height;
            Check(picked == hatch && tp <= wall + 0.15 && seen && !double.IsNaN(under) && ship.LadderTopClosed(lad) && mv.Climbing != null && head <= under + 0.01,
                $"0.3.23 (note 169): her shut hatch from below: picked ({picked == hatch}, at {tp:0.00}, her collision at {wall:0.00}), seen to be opened ({seen}); up the ladder {lad.Code} under it the head stops at {head:0.00} (its underside {under:0.00}), still on the ladder ({mv.Climbing != null})");
            if (wasOpen) ship.ToggleHatch(null, hatch);
        }

        /// <summary>(0.3.21) carrack-m without gun places of her own (till the generator's update): every gun a laid place, ids its code.</summary>
        static bool GiveGunPlaces(ShipDefinition def)
        {
            if (def.GunPlaces != null && def.GunPlaces.Length > 0) return false;
            def.GunPlaces = def.Fixtures.Where(f => f.Type == "gun").Select(f => new GunPlaceDef { Id = f.Code, Gun = f.Code }).ToArray();
            def.IndexGunPlaces();
            return true;
        }

        /// <summary>
        /// (0.3.20, step B; docs/slots.md) carrack-m's deck cells: a waist cell taken up (its voxels out of her grid, someone
        /// standing on it falls through, the lower deck open to the weather, the sea let below there) and laid again; a hatch
        /// laid over an opening (her own, else one made of four cells taken up) shut and opened. A cell is left out for the
        /// reload check: its id, or null.
        /// </summary>
        string DeckCellsAboard(EntityShip ship)
        {
            var sl = ship.SlotLayout;
            if (ship.Slots == null || sl == null) { Log("0.3.20: carrack-m has no deck cells yet (the generator's update): taking cells up isn't checked"); return null; }
            var def = ship.Def;
            var g = ship.Grid;
            double FallX(int n) => new[] { 0.5, 0.375, 0.625 }.Select(f => sl.Slots[n].Box[0] + f).FirstOrDefault(x =>
                !g.Collides(x - 0.32, sl.Slots[n].Top - 2.0, sl.Slots[n].CZ - 0.32, x + 0.32, sl.Slots[n].Top - 0.25, sl.Slots[n].CZ + 0.32), double.NaN);
            var waist = Enumerable.Range(0, sl.Count).Where(n => sl.Slots[n].Deck == "main" && ship.Slots.Filled(n) && ship.TakeProblem(n) == null &&
                def.RoomAt(sl.Slots[n].CX, sl.Slots[n].Top + 0.5, sl.Slots[n].CZ) == null && !double.IsNaN(FallX(n))).ToList();
            if (waist.Count == 0) { Check(false, "0.3.20: a waist cell of carrack-m's main deck that can be taken up"); return null; }
            int c = waist[0];
            var d = sl.Slots[c];
            var lower = def.Rooms.FirstOrDefault(r => r.Code == "lowerdeck");
            bool enclosedBefore = lower != null && ship.RoomEnclosed(lower);
            string took = ship.TakeCell(c, null);
            var mover = new DeckMover(FallX(c), d.Top, d.CZ) { OnGround = true };
            for (int i = 0; i < 60; i++) mover.Step(ship.Grid, 1 / 60.0, 0, 0, 3.5, false, false);
            bool open = lower != null && !ship.RoomEnclosed(lower);
            Check(took == null && !ship.Grid.SolidAt(d.CX, d.Top - 0.1, d.CZ) && mover.Y < d.Top - 1.5 && ship.CellOpen(c) && ship.DownfloodOpen("cell:" + c) && (open || !enclosedBefore),
                $"0.3.20: her deck cell {d.Id} taken up ({took ?? "ok"}): out of her grid, someone on it falls through ({d.Top:0.00} to {mover.Y:0.00}), the lower deck open ({open}; before {enclosedBefore}), the sea let below there");
            string laid = ship.LayCell(c, false);
            Check(laid == null && ship.Grid.SolidAt(d.CX, d.Top - 0.1, d.CZ) && !ship.CellOpen(c) && ship.Slots.Differences == 0,
                $"0.3.20: laid again ({laid ?? "ok"}): in her grid, nothing to save");
            // a hatch: hers, else one laid over four cells taken up
            var trapdoor = sapi.World.GetBlock(new AssetLocation("game:trapdoor-window-oak-1")) ?? sapi.World.GetBlock(new AssetLocation(ModularPieceDef.DefaultBlock("hatch")));
            var hatch = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Hatch);
            var made = new List<int>();
            if (hatch == null && trapdoor != null && trapdoor.Id != 0)
            {
                foreach (int n in waist)
                {
                    int e = sl.Neighbours[n][0], s2 = sl.Neighbours[n][1], es = e >= 0 ? sl.Neighbours[e][1] : -1;
                    var four = new[] { n, e, s2, es };
                    if (four.Any(x => x < 0 || !waist.Contains(x))) continue;
                    foreach (int x in four) if (ship.TakeCell(x, null) == null) made.Add(x);
                    var plan = ship.HatchPlanAt(n, trapdoor.Code.ToString());
                    string err = plan.Error ?? ship.PlaceHatch(plan, new ItemStack(trapdoor), false, out hatch);
                    Check(err == null && hatch != null && hatch.Stack?.StackSize == 4, $"0.3.20: a glazed hatch laid over four cells taken up ({err ?? "ok"}: {hatch?.Stack?.StackSize} trapdoors)");
                    break;
                }
            }
            if (hatch == null) Log("0.3.20: no hatch aboard and none could be laid: hatches not checked");
            else
            {
                var hb = hatch.Box;
                double hx = (hb[0] + hb[3]) / 2, hz = (hb[2] + hb[5]) / 2;
                int hc = sl.At(hx, hb[4] - 0.1, hz);
                bool wasOpen = hatch.Open;
                if (wasOpen) ship.ToggleHatch(null, hatch);
                string why = $"shut: solid {ship.Grid.SolidAt(hx, hb[4] - 0.1, hz)}, cell {hc} open {ship.CellOpen(hc)}, take {ship.TakeProblem(hc)}, lay {ship.LayProblem(hc, false)}";
                bool shutSolid = ship.Grid.SolidAt(hx, hb[4] - 0.1, hz) && !ship.CellOpen(hc) && ship.TakeProblem(hc) == "hansa:cell-hatch" && ship.LayProblem(hc, false) == "hansa:cell-hatch";
                ship.ToggleHatch(null, hatch);
                why += $"; open: solid {ship.Grid.SolidAt(hx, hb[4] - 0.1, hz)}, cell open {ship.CellOpen(hc)}";
                bool openHole = !ship.Grid.SolidAt(hx, hb[4] - 0.1, hz) && ship.CellOpen(hc);
                ship.ToggleHatch(null, hatch);
                Check(shutSolid && openHole && !hatch.Open, $"0.3.20: her hatch over {hatch.Mounts.Length} cells: shut it's her deck (walked on, its cells neither taken up nor laid under it); open, the opening's a hole ({why})");
                if (wasOpen) ship.ToggleHatch(null, hatch);
            }
            if (made.Count > 0)
            {
                if (hatch != null) ship.RemovePiece(hatch, null);
                foreach (int x in made) ship.LayCell(x, false);
            }
            // one cell left out for the reload check
            return ship.TakeCell(c, null) == null ? d.Id : null;
        }

        /// <summary>
        /// (0.3.17) The modular carrack launched (docs/modular-pieces.md): every one of her modular pieces laid on a new ship,
        /// her rooms walled by pieces sealed with their doors shut (and open with one open), a stair walked up and down, and a
        /// laid partition taken up stays up after she's loaded again ("hansaLaid"). In the pool, before the carrack sails;
        /// both ships removed after.
        /// </summary>
        void ModularLaunch(HansaMod mod, BlockPos spawn, int y0)
        {
            StepABlocks();
            CharterOnce();
            var def = mod.GetShip("carrack-m");
            var type = sapi.World.GetEntityType(new AssetLocation("hansa", "carrack-m"));
            if (def == null || type == null) { Log("0.3.17: no carrack-m (definition or entity): her launch not checked"); return; }
            int want = def.ModularPieces?.Length ?? 0;
            // (0.3.20, step B) until the generator gives her deck cells, the test's own (whole free cells, drawn nowhere): her
            // ships here are the only ones of her in this world, and she gets none again after
            bool synth = def.DeckSlots == null || def.DeckSlots.Length == 0;
            if (synth)
            {
                DeckSlotSynth.Give(def, DeckSlotSynth.Cells(def));
                Log($"0.3.20: carrack-m has no deck cells of her own yet: the test gives her {def.Slots?.Count ?? 0} (whole free cells of her decks)");
            }
            // (0.3.21, step C) and gun places, till the generator gives her her own
            bool synthGuns = GiveGunPlaces(def);
            if (synthGuns) Log($"0.3.21: carrack-m has no gun places of her own yet: the test makes her {def.GunPlaces.Length} guns laid places");
            double wy = EntityShip.FindWaterSurface(sapi.World, new BlockPos(spawn.X, y0 + 5, spawn.Z, 0), 2, 6);
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X + 12.5, wy, spawn.Z + 0.5);
            ship.Pos.Yaw = (float)(Math.PI / 2);
            sapi.World.SpawnEntity(ship);
            EntityShip again = null;
            try
            {
                var laid = ship.WatchedAttributes.GetTreeAttribute("hansaLaid");
                int modular = ship.Pieces.All.Count(p => p.Role is PieceRole.Partition or PieceRole.Window or PieceRole.Door or PieceRole.Railing or PieceRole.Ladder or PieceRole.Stair or PieceRole.Shelf or PieceRole.Hearth or PieceRole.Oven or PieceRole.Flue or PieceRole.Hatch);
                // (0.3.19) no charter launches another on her, nor where she was just put
                Check(mod.OverlapsShip(def, ship.Pos.X, ship.Pos.Z, ship.Pos.Yaw, ship.Pos.Dimension) && !mod.OverlapsShip(def, ship.Pos.X + 200, ship.Pos.Z + 200, ship.Pos.Yaw, ship.Pos.Dimension),
                    "0.3.19: a launch on her spot is refused (another ship there), one well clear of her isn't");
                // ((0.3.23) a railing entry beside a ship's stair is skipped: the stair has its own handrails; an older design's wait on the generator)
                int besideStairs = def.ModularPieces.Count(e => e.Kind == "railing" && ship.Mounts.ForModular(e) is MountPoint rm && PiecePlanner.BesideRailed(ship.Mounts, ship.Pieces.All, rm)?.Role == PieceRole.Stair);
                if (besideStairs > 0) Log($"0.3.23: {besideStairs} of carrack-m's railing entries stand beside her ship's stairs and were skipped (their own handrails; the generator drops them)");
                Check(want > 0 && modular == want - besideStairs && laid != null && laid.Count == want,
                    $"0.3.17: a new carrack-m has all her {want} modular pieces laid ({modular}, {besideStairs} railings beside her stairs skipped; {laid?.Count ?? 0} remembered)");
                var l = ship.Mounts;
                // her rooms walled by pieces: sealed with the doors shut
                foreach (var r in def.Rooms.Where(r => r.Pieces))
                {
                    var f = l.Floors.FirstOrDefault(m => m.Room == r.Code && !ship.Pieces.Taken(m.Id));
                    if (f == null) { Log($"0.3.17: no free floor place in {r.Code}"); continue; }
                    bool shut = ship.SealedAt(f.X + 0.5, f.Y + 0.6, f.Z + 0.5);
                    Check(shut, $"0.3.17: carrack-m's {r.Code} (walled by pieces) is sealed with her doors shut");
                }
                // a door opened: the room it opens is open (the great cabin's onto the waist)
                var door = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Door && l.Get(p.Mount)?.Room == "greatcabin");
                var gf = l.Floors.FirstOrDefault(m => m.Room == "greatcabin" && !ship.Pieces.Taken(m.Id));
                if (door != null && gf != null)
                {
                    ship.ToggleDoorPiece(null, door);
                    bool open = !ship.SealedAt(gf.X + 0.5, gf.Y + 0.6, gf.Z + 0.5);
                    ship.ToggleDoorPiece(null, door);
                    Check(open && ship.SealedAt(gf.X + 0.5, gf.Y + 0.6, gf.Z + 0.5), $"0.3.17: her great cabin's door ({door.Mount}) open, the cabin is open; shut again, sealed");
                }
                // (0.3.18, note 162) a ladder-stair climbed up and down in her own collision, her ladders' list has it; taken up
                // and put back
                var st = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Ladder && l.Get(p.Mount) != null);
                if (st != null)
                {
                    var foot = l.Get(st.Mount);
                    var lad = ship.Ladders.FirstOrDefault(x => x.Code == "piece-" + st.Id);
                    double topY = 0, backY = 0;
                    bool ok = lad != null && DeckMover.ClimbStair(ship.PlaceGrid ?? ship.Grid, lad, out topY, out backY, out _);
                    Check(ok, $"0.3.18: her {st.Mount} ladder-stair climbed up ({topY:0.00} of {st.Rise}) and down again ({backY:0.00}), in her ladders: {lad != null}");
                    int dir = st.Dir;
                    double rise = st.Rise;
                    var stack = st.Stack;
                    string rem = ship.RemovePiece(st, null);
                    // (0.3.19: a ladder makes a ladder-stair; plank stairs taken up from a 0.3.18 one make a walked stair now)
                    var block = stack?.Block is Block sb && VanillaPieceBlock.IsLadder(sb) ? sb : sapi.World.GetBlock(new AssetLocation(ModularPieceDef.DefaultBlock("ladder")));
                    // (0.3.23, note 172) by hand: the crosshair on its foot cell from before it, the client's plan's Toward to the server
                    string put = rem;
                    ShipPiece back = null;
                    if (put == null) put = HandPlace(ship, foot, dir, block, out back);
                    Check(rem == null && put == null && back?.Role == PieceRole.Ladder && back.Mount == foot.Id && back.Dir == dir,
                        $"0.3.18/0.3.23 (notes 162, 172): her {foot.Id} ladder-stair taken up and put back by hand ({put ?? "ok"})");
                }
                // (0.3.19) a walked stair walked up and down in her collision (all her pieces in it), taken up and put back
                var ws = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Stair && l.Get(p.Mount) != null);
                if (ws == null) Log("0.3.19: carrack-m has no walked stair yet (the generator's update): its walk isn't checked here");
                else
                {
                    var foot = l.Get(ws.Mount);
                    bool ok = WalkNear(ship.Grid, foot, ws.Dir, ws.Rise, PiecePlanner.RunOf(ws), out double topY, out double backY, out string off);
                    Check(ok, $"0.3.19: her {ws.Mount} walked stair ({PiecePlanner.RunOf(ws)} long, its own handrails {ws.Rails}) walked up ({topY:0.00} of {ws.Rise}) and down again ({backY:0.00}){off}");
                    int dir = ws.Dir;
                    var block = ws.Stack?.Block is Block wb && VanillaPieceBlock.IsShipStair(wb) ? wb : sapi.World.GetBlock(new AssetLocation(ModularPieceDef.DefaultBlock("stair")));
                    string rem = ship.RemovePiece(ws, null);
                    // (0.3.23) a steep ship's stair by hand where its head meets the same deck edge (its foot a cell nearer), walked
                    var steepB = sapi.World.GetBlock(new AssetLocation(ModularPieceDef.SteepStair));
                    // ((0.3.24) her own stair already steep (the user's castle stairs): the steep one on the same foot)
                    MountPoint HeadAt(int cells) { int c = (int)PiecePlanner.RunOf(ws) - cells; return l.FloorAt(foot.Deck, foot.I + MountLayout.DX[dir] * c, foot.K + MountLayout.DZ[dir] * c); }
                    var steepFoot = HeadAt(PiecePlanner.SteepCells);
                    if (rem == null && steepB != null && steepFoot != null)
                    {
                        string sp = HandPlace(ship, steepFoot, dir, steepB, out var steepPiece);
                        bool sw = steepPiece != null && WalkNear(ship.Grid, steepFoot, dir, steepPiece.Rise, PiecePlanner.RunOf(steepPiece), out _, out _, out _);
                        Check(sp == null && steepPiece?.Cells == 2 && steepPiece.Rails && sw,
                            $"0.3.23: a steep ship's stair put by hand on {steepFoot.Id} (up to the same deck edge): {sp ?? "placed"}, walked up and down {sw}");
                        if (steepPiece != null) ship.RemovePiece(steepPiece, null);
                    }
                    // (0.1.0-dev.6, note 36) the short one by hand where its head meets the same deck edge (on her own stair's
                    // head cell, the cell before it open), walked with a ship's stair's step, in her flights for the walker
                    var shortB = sapi.World.GetBlock(new AssetLocation(ModularPieceDef.ShortStair));
                    var shortFoot = HeadAt(PiecePlanner.ShortCells);
                    if (rem == null && shortB != null && shortFoot != null)
                    {
                        int flights = ship.SteepFlights?.Length ?? 0;
                        string sp = HandPlace(ship, shortFoot, dir, shortB, out var shortPiece);
                        bool sw = shortPiece != null && WalkNear(ship.Grid, shortFoot, dir, shortPiece.Rise, PiecePlanner.RunOf(shortPiece), out _, out _, out _);
                        bool flight = shortPiece != null && (ship.SteepFlights?.Length ?? 0) == flights + 1;
                        Check(sp == null && shortPiece?.Cells == 1 && shortPiece.Rails && sw && flight,
                            $"0.1.0-dev.6 (note 36): a short ship's stair put by hand on {shortFoot.Id} (up to the same deck edge): {sp ?? "placed"}, walked up and down {sw}, a ship's stair's step for her walker there {flight}");
                        if (shortPiece != null) ship.RemovePiece(shortPiece, null);
                        Check((ship.SteepFlights?.Length ?? 0) == flights, "0.1.0-dev.6: the short stair taken up again, her walker's ship's stair's steps go with it");
                    }
                    // (0.3.23, note 170) the long one put back by hand: the crosshair on its foot cell from before it, the client's plan's Toward to the server
                    string put = rem;
                    ShipPiece back = null;
                    if (put == null) put = HandPlace(ship, foot, dir, block, out back);
                    bool walkedAgain = back != null && WalkNear(ship.Grid, foot, dir, back.Rise, PiecePlanner.RunOf(back), out _, out _, out _);
                    Check(rem == null && put == null && back?.Role == PieceRole.Stair && back.Mount == foot.Id && back.Dir == dir && walkedAgain,
                        $"0.3.19/0.3.23 (note 170): her {foot.Id} walked stair taken up and put back by hand ({put ?? "ok"}), walked again {walkedAgain}");
                }
                // (0.3.18, note 165) her hearth, oven and flue laid as pieces: her hearth and oven are theirs (stand-in fixtures),
                // the hearth draws through its flue, lights with fuel in it, and its flue and itself don't come up while it burns
                var hp = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Hearth);
                var op = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Oven);
                var fp = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Flue);
                if (hp == null) Log("0.3.18: carrack-m has no hearth piece yet: the placed hearth isn't checked");
                else
                {
                    Check(ship.HearthFixture != null && ship.HearthFixture == ship.PlacedHearth && ship.HearthInv != null && ship.HearthFlued &&
                          ship.FixtureByIndex(ship.HearthFixture.Index) == ship.HearthFixture && (fp == null || ship.HearthFixture.Chimney != null),
                        $"0.3.18: her hearth is the one laid ({hp.Mount}): its stand-in is her hearth, its inventory there, its smoke {(fp == null ? "from its fire (no flue: 0.3.24, note 192)" : $"out of its flue at y {ship.HearthFixture?.Chimney?[1]:0.00}")}");
                    if (op != null) Check(ship.OvenFixture != null && ship.OvenFixture == ship.PlacedOven && ship.OvenInv != null, $"0.3.18: her oven is the one laid ({op.Mount})");
                    var fuel = sapi.World.GetItem(new AssetLocation("game:firewood"));
                    if (fuel != null && ship.HearthInv != null)
                    {
                        ship.HearthInv[0].Itemstack = new ItemStack(fuel, 4);
                        var torch = sapi.World.GetBlock(new AssetLocation("game:torch-basic-lit-up"));
                        var slot = torch != null ? new DummySlot(new ItemStack(torch)) : null;
                        string lit = slot == null ? "no torch" : ship.TryIgniteHearth(null, slot, 4f, 0) ?? (ship.Fire.CanIgniteFuel || ship.Fire.IsBurning ? null : "didn't catch");
                        Check(lit == null, $"0.3.18: the laid hearth lights with fuel in it ({lit ?? "lit"})");
                        if (lit == null && fp != null)
                        {
                            Check(ship.RemovePiece(fp, null) == "hansa:flue-hot" && ship.RemovePiece(hp, null) == "hansa:hearth-flue-first",
                                "0.3.18: while it burns its flue doesn't come off, nor the hearth from under its flue");
                        }
                        ship.Fire.Douse();
                        ship.HearthInv[0].Itemstack = null;
                        ship.SaveHearth(true);
                    }
                    // its flue taken off: (0.3.24, note 192) it still burns, its smoke from the fire; put back: up the flue
                    if (fp != null && ship.RemovePiece(fp, null) == null)
                    {
                        bool unflued = ship.HearthFlued && ship.HearthFixture?.Chimney == null;
                        var chimney = sapi.World.GetBlock(new AssetLocation(ModularPieceDef.DefaultBlock("flue")));
                        var fm = l.Get(fp.Mount);
                        string back = chimney == null || fm == null ? "no chimney block" : ship.PlacePiece(PiecePlanner.Plan(l, fm, new VanillaPieceBlock(sapi.World, new ItemStack(chimney)), 1), new ItemStack(chimney), false, out _);
                        Check(unflued && back == null && ship.HearthFlued && ship.HearthFixture?.Chimney != null, $"0.3.24 (note 192): without its flue the hearth still burns, its smoke from the fire ({unflued}); its flue put back, the smoke goes up it ({back ?? "ok"})");
                    }
                }
                // (0.3.20, step B) her deck cells: one taken up (and left out for the reload below), a hatch
                string outCell = DeckCellsAboard(ship);
                // (0.3.23, note 169) a shut hatch from below: picked, seen, the climber's head stopped under it
                HatchFromBelowAboard(ship);
                // (0.3.27) stairs through the beams: a beam's length out for a stair, a stair across her between two beams
                BeamsAboard(ship);
                // (0.3.21, step C) her guns as items: one fired, taken up and mounted again, another left taken up
                string outGun = GunPlacesAboard(ship);
                // a laid partition taken up stays up when she's loaded again
                var part = ship.Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Partition && ship.Pieces.StandingOn(l, p) == null);
                if (part != null)
                {
                    string mount = part.Mount;
                    Check(ship.RemovePiece(part, null) == null && ship.PlaceModularPieces() == 0 && ship.Pieces.At(mount) == null,
                        $"0.3.17: a laid partition ({mount}) taken up isn't laid again");
                    again = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
                    again.WatchedAttributes.FromBytes(ship.WatchedAttributes.ToBytes());
                    again.Pos.SetPos(spawn.X + 12.5, wy, spawn.Z + 0.5);
                    again.Pos.Yaw = (float)(Math.PI / 2);
                    ship.Die(EnumDespawnReason.Removed);
                    ship = null;
                    sapi.World.SpawnEntity(again);
                    // (her start lanterns are pieces too: count the modular ones only)
                    int modularAgain = again.Pieces.All.Count(p => p.Role is PieceRole.Partition or PieceRole.Window or PieceRole.Door or PieceRole.Railing or PieceRole.Ladder or PieceRole.Stair or PieceRole.Shelf or PieceRole.Hearth or PieceRole.Oven or PieceRole.Flue or PieceRole.Hatch);
                    Check(again.Pieces.At(mount) == null && modularAgain == modular - 1,
                        $"0.3.17: loaded again, she has {modularAgain} modular pieces ({again.Pieces.Count} in all), the partition on {mount} still taken up");
                    if (outCell != null)
                    {
                        int on = again.SlotLayout?.IndexOf(outCell) ?? -1;
                        var od = on >= 0 ? again.SlotLayout.Slots[on] : null;
                        Check(od != null && !again.Slots.Filled(on) && !again.Grid.SolidAt(od.CX, od.Top - 0.1, od.CZ) && again.Slots.Differences == 1,
                            $"0.3.20: loaded again, her deck cell {outCell} is still out (saved as {again.Slots.Differences} difference from the design), not in her grid");
                    }
                    if (outGun != null)
                    {
                        var og = again.GunOf(again.GunPlace(outGun));
                        Check(og != null && again.GunAbsent(og) && again.Def.GunPlaces.Count(p => !again.GunPlaceLaid(p) && p.Start != "empty") == 1   /* (her castle places start empty by design) */ && og.Box is double[] ob && !again.Grid.SolidAt((ob[0] + ob[3]) / 2, ob[1] + 0.3, (ob[2] + ob[5]) / 2),
                            $"0.3.21: loaded again, her gun place {outGun} is still without its gun (the only one), not in her grid");
                    }
                }
            }
            catch (Exception e)
            {
                Check(false, "0.3.17: carrack-m's launch: " + e);
            }
            finally
            {
                ship?.Die(EnumDespawnReason.Removed);
                again?.Die(EnumDespawnReason.Removed);
                if (synth) DeckSlotSynth.Give(def, null);
                if (synthGuns) { def.GunPlaces = null; def.IndexGunPlaces(); }
            }
        }
    }
}
