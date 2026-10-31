using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.3.26, step D1; docs/slots.md §9) carrack-m's hull panels on a real server, in the gun pool: one under her waterline
    /// stove in by a blow (out of her collision, saved, the sea comes in, the hold open) and laid again (the water stops);
    /// cracked and mended; taken out above her waterline but not under it; a panel out kept over a reload; an old save's hull
    /// damage moved onto her panels; a carrack's gun firing into her side; eight panels out under her waterline: she founders
    /// and goes down, and her wreck would be laid without them.
    /// </summary>
    public partial class SelfTest
    {
        /// <summary>A seabed for a wreck's plan that's all water down to y 0.</summary>
        class OpenSea : IWreckSite
        {
            public WreckGround At(int x, int y, int z, out float top)
            {
                top = 1;
                return y < 0 ? WreckGround.Ground : WreckGround.Free;
            }
        }

        void HullPanelsWorks(HansaMod mod, BlockPos spawn, double wy, Action then)
        {
            var def = mod.GetShip("carrack-m");
            var mtype = sapi.World.GetEntityType(new AssetLocation("ships", "carrack-m"));
            var ctype = sapi.World.GetEntityType(new AssetLocation("ships", cfg.Ship));
            if (def?.Hull == null || mtype == null || ctype == null)
            {
                Log("0.3.26: carrack-m has no hull panels: they aren't checked");
                then();
                return;
            }
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            sea?.SetOverride(new Sea.WindOverride { FromDegrees = 0, Strength = 0.02, StartHours = sea.Hours - 24 });
            bool wrecks = mod.Config.Wrecks;
            mod.Config.Wrecks = false;
            // the gun pool again, its water as it was
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
            double sz = spawn.Z - 58 + 0.5, sx = spawn.X + 0.5, tz = sz + 17;
            EntityShip Launch(EntityProperties type, double z, byte[] watched = null, byte[] attrs = null, double dx = 0)
            {
                var s = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
                if (watched != null) s.WatchedAttributes.FromBytes(watched);
                if (attrs != null) s.Attributes.FromBytes(attrs);
                s.Pos.SetPos(sx + dx, wy, z);
                s.Pos.Yaw = (float)(Math.PI / 2);
                sapi.World.SpawnEntity(s);
                return s;
            }
            var l = def.Hull;
            var dl = def.DamageParts;
            var D = dl.D;
            // her port side faces the other ship (the shooter, at lower z): a port panel in her hold under her waterline
            // ((0.3.29) her planking's (the outer layer: her ceiling behind it)
            int P = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Side < 0 && l.Slots[n].Head < def.Waterline && l.Slots[n].Room == "hold" && !l.Slots[n].IsInner)
                .OrderBy(n => Math.Abs(l.Slots[n].Centre.X - 1.0) + Math.Abs(l.Slots[n].Centre.Y - (def.Waterline - 1.6))).FirstOrDefault(-1);
            if (P < 0) { Check(false, "0.3.26: a port panel of carrack-m in her hold under her waterline"); then(); return; }
            var ps = l.Slots[P];
            var hold = def.Rooms.First(r => r.Code == ps.Room);
            var pv = ps.SolidBoxes[0];
            double vx = (pv[0] + pv[3]) / 2, vy = (pv[1] + pv[4]) / 2, vz = (pv[2] + pv[5]) / 2;
            var target = Launch(mtype, tz);
            EntityShip shooter = null, again = null, old = null;
            void Cleanup()
            {
                foreach (var e in new[] { target, shooter, again, old }) if (e != null && e.Alive) e.Die(EnumDespawnReason.Removed);
                mod.Config.Wrecks = wrecks;
                sea?.SetOverride(null);
            }
            try
            {
                // 1. a blow of 200 kJ on it from outside: out
                var c = ps.Centre;
                var res = target.Damage(new Vec3d(c.X, c.Y, c.Z - 0.02), new Vec3d(0, 0, 1), 200, def.Cannon.HitRadius, "cannon");
                var tree = target.WatchedAttributes.GetTreeAttribute("hansaSlots");
                Check(res != null && target.PartDamage.Hurt[P] >= D.Holed && target.HullPanels[P] == SlotState.Empty && tree?.GetInt(ps.Id) == 2 &&
                      !target.Grid.SolidAt(vx, vy, vz) && !target.RoomEnclosed(hold),
                    $"0.3.26: 200 kJ on carrack-m's {ps.Id} stoves it in: out of her collision, saved ({ps.Id} = {tree?.GetInt(ps.Id)}), her {hold.Code} open ({res?.Hits.Count} parts hit: {string.Join(", ", res?.Hits.Select(h => dl.Parts[h.Part].Part.ToString()) ?? Array.Empty<string>())})");
                // (what the shot did beyond it, mended: only this one lets the sea in)
                foreach (var h in res?.Hits ?? new List<DamageHit>())
                    if (h.Part != P && h.Part < l.Count) target.SetHullState(h.Part, SlotState.Filled);
            }
            catch (Exception e) { Check(false, "0.3.26: carrack-m's hull panels: " + e); Cleanup(); then(); return; }
            double w0 = target.Water;
            WaitFor(() => target.Water > w0 + 1, 10, rising =>
            {
                try
                {
                    Check(rising, $"0.3.26: the sea comes in through it ({target.Water - w0:0.0} t in a few seconds, {target.WaterRate:0.00} t/s)");
                    // 2. laid again: in, solid, nothing saved; pumped dry, no more water. ((0.3.27, "made holes in ship but cannot
                    // repair") as a player does it: the repair tool with boards in hand, out of building mode, 4 a planks block)
                    var boardItem = sapi.World.GetItem(new AssetLocation("game:plank-oak"));
                    int needBoards = Math.Max(1, l.Slots[P].Mat.Count) * ShipRepairs.BoardsPerPlanks;
                    var boardSlots = new List<ItemSlot> { new DummySlot(boardItem == null ? null : new ItemStack(boardItem, needBoards + 1)) };
                    string mended = target.Repair(P, boardSlots, "game:plank-oak", false, out var layJob, out _);
                    string laid = layJob?.What == "lay" && boardSlots[0].StackSize == 1 ? null : $"the repair tool: {mended} (job {layJob?.What}, {boardSlots[0].StackSize} boards left of {needBoards + 1})";
                    if (laid != null) target.LayHull(P, true);
                    var t2 = target.WatchedAttributes.GetTreeAttribute("hansaSlots");
                    Check(laid == null && target.HullPanels[P] == SlotState.Filled && target.Grid.SolidAt(vx, vy, vz) && target.HullPanels.Differences == 0 &&
                          (t2 == null || t2.GetInt(ps.Id, -1) == -1) && target.PartDamage.Hurt[P] == 0 && target.RoomEnclosed(hold),
                        $"0.3.26/0.3.27: laid again with {needBoards} boards by the repair tool, out of building mode ({laid ?? "ok"}): in her collision, sound, nothing saved of it, her {hold.Code} closed");
                    target.Flood(-target.Water);
                }
                catch (Exception e) { Check(false, "0.3.26: carrack-m's hull panels: " + e); Cleanup(); then(); return; }
                WaitFor(() => false, 2, dry =>
                {
                    try
                    {
                        Check(target.Water < 0.05, $"0.3.26: pumped out, no more comes in ({target.Water:0.00} t after 2 s)");
                        // 3. 45 kJ cracks it ((0.3.29) its ceiling behind it takes CeilBacking: 30 did before); the repair tool's planks mend it
                        var c = ps.Centre;
                        target.Damage(new Vec3d(c.X, c.Y, c.Z - 0.02), new Vec3d(0, 0, 1), 45, def.Cannon.HitRadius, "cannon");
                        bool crackedNow = target.HullPanels[P] == SlotState.Cracked && target.Grid.SolidAt(vx, vy, vz);
                        string msg = target.Repair(P, new List<ItemSlot>(), "game:plank-oak", true, out var job, out _);
                        Check(crackedNow && job?.What == "planks" && target.HullPanels[P] == SlotState.Filled && target.PartDamage.Hurt[P] == 0,
                            $"0.3.26: 45 kJ cracks it (still in her collision: {crackedNow}; its ceiling behind it took {D.CeilBacking:0.0} of the blow); mended with planks ({msg}) it's sound");
                        // 4. not taken out under her waterline afloat; one over it is (its planks back to whoever takes it)
                        string under = target.TakeHull(P, null);
                        int q = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Sill > def.Waterline + 0.55 && target.HullTakeProblem(n) == null)
                            .OrderBy(n => Math.Abs(l.Slots[n].Centre.X)).FirstOrDefault(-1);
                        if (q < 0) { Check(false, "0.3.26: a panel over her waterline she can take out afloat"); Cleanup(); then(); return; }
                        string over = target.TakeHull(q, null);
                        var qb = l.Slots[q].SolidBoxes[0];
                        Check(under == "ships:hull-below-water" && over == null && target.HullOpen(q) && !target.Grid.SolidAt((qb[0] + qb[3]) / 2, (qb[1] + qb[4]) / 2, (qb[2] + qb[5]) / 2) && l.Slots[q].Mat.Count >= 1,
                            $"0.3.26: afloat, a panel under her waterline isn't taken out ({under}); {(q >= 0 ? l.Slots[q].Id : "?")} over it is ({over ?? "out"}: {l.Slots[q].Mat.Count} planks blocks back)");
                        // 5. loaded again: that one's still out
                        var watched = target.WatchedAttributes.ToBytes();
                        var attrs = target.Attributes.ToBytes();
                        target.Die(EnumDespawnReason.Removed);
                        again = Launch(mtype, tz, watched, attrs);
                        Check(again.HullPanels != null && again.HullPanels[q] == SlotState.Empty && again.PartDamage.Hurt[q] >= D.Holed && again.HullPanels.Differences == 1 &&
                              !again.Grid.SolidAt((qb[0] + qb[3]) / 2, (qb[1] + qb[4]) / 2, (qb[2] + qb[5]) / 2),
                            $"0.3.26: loaded again, {l.Slots[q].Id} is still out (saved as {again.HullPanels?.Differences} difference), not in her grid");
                        // 6. an old save (hansaDmgF without "v": her hull's damage by whole blocks from her keel): moved onto her panels
                        var oldAttrs = new TreeAttribute();
                        oldAttrs.FromBytes(attrs);
                        var dmg = new TreeAttribute();
                        int oldCode = ShipPart.Hull(-1, ps.Station, (int)Math.Floor(ps.Centre.Y - def.KeelY)).Encode();
                        dmg["codes"] = new IntArrayAttribute(new[] { oldCode });
                        dmg["hurt"] = new FloatArrayAttribute(new[] { 0.9f });
                        dmg["wear"] = new FloatArrayAttribute(new[] { 0.3f });
                        oldAttrs["hansaDmgF"] = dmg;
                        var oldWatched = new TreeAttribute();
                        oldWatched.FromBytes(watched);
                        oldWatched.RemoveAttribute("hansaSlots");
                        again.Die(EnumDespawnReason.Removed);
                        old = Launch(mtype, tz, oldWatched.ToBytes(), oldAttrs.ToBytes());
                        var v = old.Attributes.GetTreeAttribute("hansaDmgF")?.GetInt("v");
                        Check(old.PartDamage.Hurt[P] >= 0.9f - 1e-4 && old.HullPanels[P] == SlotState.Empty && v == EntityShip.DamageSaveVersion && !old.Grid.SolidAt(vx, vy, vz),
                            $"0.3.26: an old save's hull damage (band {(int)Math.Floor(ps.Centre.Y - def.KeelY)} from her keel, 0.9) moved onto {ps.Id} ({old.PartDamage.Hurt[P]:0.00}: {old.HullPanels[P]}), saved as v {v}");
                        old.SetHullState(P, SlotState.Filled);
                        old.Flood(-old.Water);
                        // 6b. (0.3.28, step D2) her deck cells take shots
                        CellsTakeShots(old, def);
                    }
                    catch (Exception e) { Check(false, "0.3.26: carrack-m's hull panels: " + e); Cleanup(); then(); return; }
                    // 7. a carrack's starboard gun into her port side over her waterline (the lower deck's band)
                    // ((0.3.28, D2.1) half a block along: her guns (on the odd x) abreast of a one-block panel's middle, not its edge)
                    // (a grid ship's guns stand at a cell's middle, x.5: no shift for her)
                    var g0 = mod.GetShip(cfg.Ship)?.Fixtures.FirstOrDefault(f => f.Type == "gun" && !f.Swivel);
                    double gdx = g0 != null && Math.Abs(g0.Pos[0] - Math.Floor(g0.Pos[0]) - 0.5) < 0.05 ? 0 : 0.5;
                    shooter = Launch(ctype, sz, dx: gdx);
                    var guns = shooter.Def.Fixtures.Where(f => f.Type == "gun" && !f.Swivel && f.Out[2] > 0 && shooter.GunMounted(f)).OrderBy(f => f.Pos[0]).ToArray();
                    foreach (var f in guns) shooter.OpenGunPort(f, true);
                    WaitFor(() => false, 1, w1 =>
                    {
                        FixtureDef gun = null;
                        int R = -1;
                        foreach (var f in guns)
                        {
                            shooter.GunMuzzle(f, 0, out var m, out _);
                            var lm = old.WorldToLocal(m.X, m.Y, m.Z);
                            R = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Side < 0 && l.Slots[n].Sill > def.Waterline && l.Slots[n].Head < def.DeckY && !l.Slots[n].IsInner &&
                                l.Slots[n].Box[0] + 0.1 < lm.X && lm.X < l.Slots[n].Box[3] - 0.1).FirstOrDefault(-1);
                            if (R >= 0) { gun = f; break; }
                        }
                        if (gun == null) { Check(false, "0.3.26: a carrack's gun abreast of a panel of carrack-m's port side"); Cleanup(); then(); return; }
                        var rs = l.Slots[R];
                        var rc = rs.Centre;
                        old.LocalToWorld(rc.X, rc.Y, rc.Z - 0.05, out double ax, out double ay, out double az);
                        double g = def.Cannon.Gravity, vel = def.Cannon.MuzzleVelocity, e = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            shooter.GunMuzzle(gun, e, out var m, out _);
                            double dx = ax - m.X, dz = az - m.Z, dist = Math.Sqrt(dx * dx + dz * dz), tt = dist / vel;
                            e = Math.Atan2(ay - m.Y + 0.5 * g * tt * tt, dist) * 180 / Math.PI;
                        }
                        var shots = mod.Shots;
                        long started = sapi.World.ElapsedMilliseconds;
                        string fired = FirePrepared(shooter, gun, e);
                        var ball = shots.Flying.Count > 0 ? shots.Flying[^1] : null;
                        WaitFor(() => ball == null || !shots.Flying.Contains(ball), 4, w2 =>
                        {
                            var rec = shots.Records.FirstOrDefault(r => r.What == "ship" && r.Entity == old.EntityId && r.Ms >= started);
                            int twin = l.IndexOf($"hull:s:{rs.Station}:{rs.Band}" + (rs.Half != null ? ":" + rs.Half : ""));
                            Check(fired == null && rec != null && old.HullPanels[R] != SlotState.Filled && rec.Damage?.Pierced == true,
                                $"0.3.26: a carrack's gun ({gun.Code}) into carrack-m's {rs.Id}: {old.HullPanels[R]} ({rec?.Energy:0} kJ on her side), on through her: " +
                                $"{(rec?.Damage?.Exit != null ? $"out of her far side with {rec.Damage.Remaining:0} kJ" : twin >= 0 ? $"its far side's {l.Slots[twin].Id} {old.HullPanels[twin]} (hurt {old.PartDamage.Hurt[twin]:0.00})" : "stopped in her")}");
                            // 8. eight panels under her waterline out: she founders and goes down; her wreck would be laid without them
                            // ((0.3.29) her planking and the ceiling behind it: through and through)
                            var deep = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Head < def.Waterline - 0.5 && !l.Slots[n].IsInner).OrderBy(n => l.Slots[n].Band).ThenBy(n => Math.Abs(l.Slots[n].Centre.X)).Take(8).ToList();
                            deep = deep.Concat(deep.Select(n => l.Slots[n].PartnerIndex).Where(n => n >= 0)).ToList();
                            foreach (int n in deep) old.SetHullState(n, SlotState.Empty);
                            var delta = ShipWrecks.MergeDeltas(old.Slots?.Delta(), old.HullPanels?.Delta());
                            long t0 = sapi.World.ElapsedMilliseconds;
                            WaitFor(() => old.GoneDown, 180, down =>
                            {
                                Check(down && delta != null && deep.All(n => delta.TryGetValue(l.Slots[n].Id, out int sv) && sv == 2),
                                    $"0.3.26: eight panels under her waterline out, and their ceilings ({string.Join(", ", deep.Select(n => l.Slots[n].Id))}): she founders and goes down in {(sapi.World.ElapsedMilliseconds - t0) / 1000.0:0} s ({old.Stage}, {old.Water:0} t)");
                                WreckInput Input(Dictionary<string, int> slots) => new()
                                {
                                    Def = def, X = 0, Y = 40, Z = 0, Seed = 7, Settle = 0, Slots = slots, Decay = false, Holes = false, Masts = false, Guns = false, Anchor = false,
                                };
                                var whole = WreckBuilder.Build(Input(null), new OpenSea());
                                var lacking = WreckBuilder.Build(Input(deep.ToDictionary(n => l.Slots[n].Id, n => delta != null && delta.TryGetValue(l.Slots[n].Id, out int dv) ? dv : 2)), new OpenSea());
                                int vox = deep.Sum(n => l.Slots[n].SolidBoxes.Sum(b => DeckSlotLayout.Voxels(def.Grid, b).Count()));
                                Check(whole.CellsDesign - lacking.CellsDesign == vox, $"0.3.26: her wreck would be laid without them ({whole.CellsDesign} → {lacking.CellsDesign} cells, their {vox})");
                                Cleanup();
                                then();
                            });
                        });
                    });
                });
            });
        }

        /// <summary>
        /// (0.3.28, step D2; docs/slots.md §11) A main deck cell of carrack-m shot from above: 40 kJ cracks it (still in her
        /// collision), mended with the repair tool; 110 kJ stoves it in (out of her collision, saved, the lower deck under it hit),
        /// laid again; a cell under a laid gun stops at cracked.
        /// </summary>
        void CellsTakeShots(EntityShip ship, ShipDefinition def)
        {
            var l = def.Slots;
            var dl = def.DamageParts;
            if (l == null || dl == null || !dl.CellSlotted) { Log("0.3.28: carrack-m has no deck cells as parts: not checked"); return; }
            var D = dl.D;
            var g = def.Grid;
            int top = -1, below = -1;
            foreach (int n in Enumerable.Range(0, l.Count).Where(n => l.Slots[n].IsCell && l.Slots[n].Deck == "main" && ship.Slots.Filled(n) && !ship.CellHeld(n))
                         .OrderBy(n => Math.Abs(l.Slots[n].CX - 4) + Math.Abs(l.Slots[n].CZ)))
            {
                var s0 = l.Slots[n];
                double t = g.RaycastCell(new Vec3d(s0.CX + 0.1, s0.Box[1] - 0.01, s0.CZ + 0.1), new Vec3d(0, -1, 0), 6, out int ci, out int cj, out int ck, out _);
                int m = t > 6 ? -1 : l.AtVoxel(g, ci, cj, ck);
                if (m >= 0 && l.Slots[m].IsCell && l.Slots[m].Deck == "lower" && ship.Slots.Filled(m)) { top = n; below = m; break; }
            }
            if (top < 0) { Check(false, "0.3.28: a main deck cell of carrack-m over a lower deck cell"); return; }
            var s = l.Slots[top];
            int P = dl.CellPart(top), Q = dl.CellPart(below);
            var at = new Vec3d(s.CX + 0.1, s.Top, s.CZ + 0.1);
            var down = new Vec3d(0, -1, 0);
            double vy = (s.Box[1] + s.Box[4]) / 2;
            ship.Damage(at, down, 40, def.Cannon.HitRadius, "cannon");
            bool cracked = ship.Slots[top] == SlotState.Cracked && ship.Grid.SolidAt(s.CX, vy, s.CZ) && ship.WatchedAttributes.GetTreeAttribute("hansaSlots")?.GetInt(s.Id) == 1;
            string msg = ship.Repair(P, new List<ItemSlot>(), "game:plank-oak", true, out var job, out _);
            Check(cracked && job?.What == "planks" && ship.Slots[top] == SlotState.Filled && ship.PartDamage.Hurt[P] == 0,
                $"0.3.28: 40 kJ down on carrack-m's deck cell {s.Id} cracks it (still in her collision, saved: {cracked}); mended with the repair tool ({msg}) it's sound");
            var res = ship.Damage(at, down, 110, def.Cannon.HitRadius, "cannon");
            bool stove = ship.Slots[top] == SlotState.Empty && !ship.Grid.SolidAt(s.CX, vy, s.CZ) && ship.WatchedAttributes.GetTreeAttribute("hansaSlots")?.GetInt(s.Id) == 2;
            Check(stove && ship.PartDamage.Hurt[Q] > 0,
                $"0.3.28: 110 kJ stoves it in (out of her collision, saved: {stove}) and goes on to {l.Slots[below].Id} (hurt {ship.PartDamage.Hurt[Q]:0.00}; {res?.Hits.Count} parts hit)");
            string laid = ship.LayCell(top, false);
            ship.Repair(Q, new List<ItemSlot>(), "game:plank-oak", true, out _, out _);
            Check(laid == null && ship.Slots[top] == SlotState.Filled && ship.PartDamage.Hurt[P] == 0 && ship.Grid.SolidAt(s.CX, vy, s.CZ),
                $"0.3.28: laid again ({laid ?? "ok"}): in her collision, sound");
            // a cell under a laid gun: cracked at most
            int gc = (def.GunPlaces ?? Array.Empty<GunPlaceDef>()).Where(ship.GunPlaceLaid).SelectMany(ship.GunPlaceCells).Where(n => l.Slots[n].IsCell && ship.Slots.Filled(n)).DefaultIfEmpty(-1).First();
            if (gc >= 0)
            {
                var gs = l.Slots[gc];
                ship.Damage(new Vec3d(gs.CX, gs.Top, gs.CZ), down, 300, def.Cannon.HitRadius, "cannon");
                int gp = dl.CellPart(gc);
                Check(ship.Slots[gc] == SlotState.Cracked && ship.PartDamage.Hurt[gp] < D.Holed, $"0.3.28: 300 kJ on {gs.Id} under a laid gun: cracked, not stove in ({ship.PartDamage.Hurt[gp]:0.00})");
                ship.RepairAll();
            }
            // (what the shots stove in beyond them laid again: the rest of the test as before)
            for (int n = 0; n < l.Count; n++)
                if (l.Slots[n].IsCell && !l.Slots[n].StartsEmpty && !ship.Slots.Filled(n)) ship.LayCell(n, false);
            ship.RepairAll();
        }
    }
}
