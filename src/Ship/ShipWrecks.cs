using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>What a wreck holds: a container (a chest, trunk, vessel or crate, from a fixture) with its contents, or (Fixture and Container null) things she carried loose.</summary>
    public class WreckLoot
    {
        public string Fixture;
        public ItemStack Container;
        public List<ItemStack> Items = new();
        /// <summary>A container put aboard as a piece (no fixture of the design): where it stood, ship-local x, y, z and yaw.</summary>
        public double[] Place;
    }

    /// <summary>The wrecks made so far, by the ship's entity id (saved with the world): never two wrecks from one ship.</summary>
    public class WreckRegistry
    {
        public Dictionary<long, WreckRecord> Done = new();
    }

    public class WreckRecord
    {
        public string Name;
        public int X, Y, Z, Blocks;
    }

    /// <summary>What making a wreck did (for the log, the command and the self-test).</summary>
    public class WreckReport
    {
        public long Id;
        public WreckPlan Plan;
        public int Placed, Skipped, Starts, Guns, Sediment, Dropped;
        /// <summary>Every container put down (a fixture's own, or a chest for it, and the extra chests), and what each is.</summary>
        public readonly List<(BlockPos Pos, string What)> Containers = new();
        public bool Finished;
    }

    /// <summary>
    /// Server: turns a ship that has sunk to the bottom into her wreck (config Wrecks): world blocks of her hull, broken up
    /// (<see cref="WreckBuilder"/>), her cargo in vanilla chests and vessels in and beside her, her guns, her anchor; then
    /// she's removed. Hooked on <see cref="ShipWreck.Sunk"/>, and every second it looks for sunk ships that aren't wrecks
    /// yet (after a reload, or with Wrecks switched on since).
    /// - It waits till every chunk the wreck touches is loaded (the builder asks the world block by block and stops at an
    ///   unloaded one: tried again 2 s later).
    /// - Her blocks go down bottom up, BlocksPerTick a tick; whoever is inside the wreck's cells is moved up out of them
    ///   first. The containers, guns and sand, the moving of her cargo, the registry entry and her removal all happen in
    ///   one last tick, so a save in between finds either her (with her cargo) and some of her blocks, or her wreck done.
    /// - Built again (a reload in the middle), the same seed and input make the same wreck, laid over the blocks laid
    ///   before (they're hers: their wreck id); nothing of her cargo has moved yet, so nothing is doubled.
    /// - Nothing is lost: what doesn't fit a container goes into another chest; what finds no place is dropped as items.
    /// </summary>
    public class ShipWrecks
    {
        public const string SaveKey = "hansaWrecks";
        public const int BlocksPerTick = 400;

        readonly ICoreServerAPI sapi;
        readonly HansaMod mod;
        public WreckRegistry Registry = new();
        readonly Dictionary<long, Job> jobs = new();
        /// <summary>Each wreck's report (this session).</summary>
        public readonly Dictionary<long, WreckReport> Reports = new();
        /// <summary>The self-test's hook: a wreck's blocks placed so far, after each tick of them.</summary>
        public Action<long, int> Placed;
        float lookAccum;
        long madeUp = -1;

        class Job
        {
            public long Id;
            public EntityShip Ship;
            public WreckInput Input;
            public List<WreckLoot> Loot;
            public WreckPlan Plan;
            public List<long> Order;
            public int Next;
            public long RetryAt;
            public WreckReport Report;
        }

        public ShipWrecks(ICoreServerAPI api, HansaMod mod)
        {
            sapi = api;
            this.mod = mod;
            ShipWreck.Sunk += OnSunk;
            api.Event.SaveGameLoaded += Load;
            api.Event.GameWorldSave += Save;
            api.Event.RegisterGameTickListener(Tick, 100);
        }

        public void Dispose() => ShipWreck.Sunk -= OnSunk;

        void Load()
        {
            try
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData(SaveKey);
                if (data != null) Registry = Deserialize(data) ?? new WreckRegistry();
            }
            catch (Exception e)
            {
                sapi.Logger.Error("[hansa] Couldn't read the wrecks made so far: {0}", e.Message);
            }
        }

        void Save() => sapi.WorldManager.SaveGame.StoreData(SaveKey, Serialize());

        public byte[] Serialize() => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Registry));
        public static WreckRegistry Deserialize(byte[] data) => JsonConvert.DeserializeObject<WreckRegistry>(Encoding.UTF8.GetString(data));

        void OnSunk(EntityShip ship)
        {
            if (mod.Config.Wrecks) Convert(ship);
        }

        /// <summary>Make a sunk ship's wreck (now, or once her chunks are loaded). Null if it's under way, else why not (a lang-free reason).</summary>
        public string Convert(EntityShip ship)
        {
            if (ship == null) return "no ship";
            if (Registry.Done.ContainsKey(ship.EntityId)) return "she's a wreck already";
            if (ship.Def == null || !ship.Alive) return "no ship";
            if (ship.Stage != SinkStage.Sunk) return "she isn't on the bottom";
            if (!jobs.ContainsKey(ship.EntityId)) jobs[ship.EntityId] = new Job { Id = ship.EntityId, Ship = ship };
            return null;
        }

        /// <summary>
        /// A wreck that isn't a ship's: a design, where and how she lies, her damage, what she carries (worldgen, the
        /// command). Placed over the next ticks like a ship's. Returns its id (made up, negative).
        /// </summary>
        public long Place(WreckInput input, List<WreckLoot> loot)
        {
            long id = madeUp--;
            while (Registry.Done.ContainsKey(id) || jobs.ContainsKey(id)) id = madeUp--;
            input.WreckId = id;
            jobs[id] = new Job { Id = id, Input = input, Loot = loot ?? new List<WreckLoot>() };
            return id;
        }

        /// <summary>The self-test: forget a wreck under way as a reload would (its blocks laid so far stay); it's begun again from her.</summary>
        public void Restart(long id) => jobs.Remove(id);

        public bool Busy(long id) => jobs.ContainsKey(id);

        void Tick(float dt)
        {
            lookAccum += dt;
            if (lookAccum >= 1)
            {
                lookAccum = 0;
                if (mod.Config.Wrecks)
                    foreach (var s in mod.ServerShips.ToArray())
                        if (s.Alive && s.Stage == SinkStage.Sunk && !Registry.Done.ContainsKey(s.EntityId) && !jobs.ContainsKey(s.EntityId)) Convert(s);
            }
            foreach (var job in jobs.Values.ToArray())
            {
                try { Step(job); }
                catch (Exception e)
                {
                    sapi.Logger.Error("[hansa] Making the wreck #{0} failed: {1}", job.Id, e);
                    jobs.Remove(job.Id);
                }
            }
        }

        void Step(Job job)
        {
            long now = sapi.World.ElapsedMilliseconds;
            if (now < job.RetryAt) return;
            if (job.Ship != null && (!job.Ship.Alive || !mod.ServerShips.Contains(job.Ship) || Registry.Done.ContainsKey(job.Id)))
            {
                // her chunk unloaded, or she was removed: she's looked for again when she's back
                jobs.Remove(job.Id);
                return;
            }
            if (job.Plan == null)
            {
                if (!Begin(job)) job.RetryAt = now + 2000;
                return;
            }
            var ba = sapi.World.BlockAccessor;
            var wreckBlock = sapi.World.GetBlock(new AssetLocation(BlockWreck.BlockCode));
            int dim = Dim(job);
            var site = new WorldWreckSite(ba, job.Id, dim);
            int end = Math.Min(job.Order.Count, job.Next + BlocksPerTick);
            for (; job.Next < end; job.Next++)
            {
                long k = job.Order[job.Next];
                WreckPlan.Unkey(k, out int x, out int y, out int z);
                var pos = new BlockPos(x, y, z, dim);
                if (ba.GetChunkAtBlockPos(pos) == null)
                {
                    // a chunk went away under her: start over once it's back (the blocks laid are hers)
                    job.Plan = null;
                    job.RetryAt = now + 2000;
                    return;
                }
                var g = site.At(x, y, z, out _);
                if (g == WreckGround.Ground) { job.Report.Skipped++; continue; }
                var cells = job.Plan.Blocks[k];
                if (g == WreckGround.Ours && ba.GetBlockEntity(pos) is BlockEntityWreck mine)
                {
                    mine.Set(cells, job.Id);
                    mine.MarkDirty(true);
                }
                else ba.SetBlock(wreckBlock.Id, pos, BlockEntityWreck.Stack(wreckBlock, cells, job.Id));
                job.Report.Placed++;
            }
            Placed?.Invoke(job.Id, job.Next);
            if (job.Next < job.Order.Count) return;
            Finish(job);
        }

        static int Dim(Job job) => job.Ship?.Pos.Dimension ?? 0;

        /// <summary>Work out her wreck (if every chunk it touches is loaded) and move anyone out of its cells. False: not yet.</summary>
        bool Begin(Job job)
        {
            var ship = job.Ship;
            int dim = Dim(job);
            var ba = sapi.World.BlockAccessor;
            if (ship != null)
            {
                // (first, cheaply: the chunks round her hull; the builder itself stops at any other it needs that isn't)
                double reach = ship.Def.Length / 2 + 4;
                for (double dx = -reach; dx <= reach + 31; dx += 32)
                    for (double dz = -reach; dz <= reach + 31; dz += 32)
                        if (ba.GetChunkAtBlockPos(new BlockPos((int)(ship.Pos.X + Math.Min(dx, reach)), (int)ship.Pos.Y, (int)(ship.Pos.Z + Math.Min(dz, reach)), dim)) == null) return false;
                // where she lies: on the bottom (if she stopped in sea that wasn't loaded, down till her hull meets it)
                double y = ship.Pos.Y;
                for (int n = 0; n < 256 && y + ship.Def.KeelY > 1; n++)
                {
                    var st = EntityShip.CheckPose(sapi.World, ship.Def, ship.Pos.X, y - 0.25, ship.Pos.Z, ship.Pos.Yaw, dim);
                    if (st == EntityShip.PoseState.Unloaded) return false;
                    if (st == EntityShip.PoseState.Blocked) break;
                    y -= 0.25;
                }
                var loot = LootOf(ship, take: false);
                job.Input = new WreckInput
                {
                    Def = ship.Def, X = ship.Pos.X, Y = y, Z = ship.Pos.Z, Yaw = ship.Pos.Yaw, List = ship.ListAngle, Trim = ship.Trim,
                    Hurt = ship.PartDamage?.Hurt, Clad = ship.Clad, Seed = Seed(ship.EntityId), WreckId = ship.EntityId,
                    // ((0.3.20) her deck cells taken up stay out of her wreck)
                    // ((0.3.26) and her hull panels out: both families' differences)
                    Slots = MergeDeltas(ship.Slots?.Delta(), ship.HullPanels?.Delta()),
                    GunsGone = ship.GunsGone(),
                    Containers = loot.Where(l => l.Fixture != null).Select(l => l.Fixture).ToArray(),
                    ContainerPlaces = loot.Where(l => l.Fixture != null && l.Place != null).ToDictionary(l => l.Fixture, l => l.Place),
                    ExtraChests = ChestsFor(loot),
                };
            }
            else job.Input.ExtraChests = Math.Max(job.Input.ExtraChests, ChestsFor(job.Loot));
            WreckPlan plan;
            try { plan = WreckBuilder.Build(job.Input, new WorldWreckSite(ba, job.Id, dim)); }
            catch (WreckSiteUnloaded) { return false; }
            job.Plan = plan;
            job.Order = plan.Order();
            job.Next = 0;
            if (!Reports.TryGetValue(job.Id, out var report)) Reports[job.Id] = report = new WreckReport { Id = job.Id };
            report.Plan = plan;
            report.Starts++;
            job.Report = report;
            MoveOut(plan, dim);
            return true;
        }

        /// <summary>Her seed: her entity id, mixed.</summary>
        public static long Seed(long entityId) => unchecked(entityId * 0x5DEECE66DL + 0x2545F4914F6CDD1DL);

        /// <summary>The extra chests her loose things need (with the trunks and crates, which join them).</summary>
        /// <summary>(0.3.26) Two families' slot differences in one (deck cells', hull panels'; ids don't clash), or null if neither has any.</summary>
        public static Dictionary<string, int> MergeDeltas(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if ((a == null || a.Count == 0) && (b == null || b.Count == 0)) return a ?? b;
            var m = new Dictionary<string, int>();
            if (a != null) foreach (var kv in a) m[kv.Key] = kv.Value;
            if (b != null) foreach (var kv in b) m[kv.Key] = kv.Value;
            return m;
        }

        static int ChestsFor(List<WreckLoot> loot)
        {
            int loose = loot.Where(l => l.Fixture == null).Sum(l => l.Items.Count) + loot.Count(l => l.Fixture != null && l.Container != null && PlaceAsIs(l.Container) == null);
            return (loose + 15) / 16;
        }

        /// <summary>
        /// What she holds: each container on a chest or cargo place with its contents; and loose, everything else (the
        /// hold's and any other storage's contents, what's in her hearth, the lanterns, beds, plates and boats on her places,
        /// the copper plates nailed on if her bottom wasn't finished). take: out of her (the last tick), else only counted.
        /// </summary>
        internal static List<WreckLoot> LootOf(EntityShip ship, bool take)
        {
            var list = new List<WreckLoot>();
            var loose = new WreckLoot();
            foreach (var gun in ship.Def.Fixtures)
            {
                if (gun.Type != "gun" || ship.GunAbsent(gun)) continue;
                var state = ship.Gun(gun.Code);
                if ((state.Charge & GunLoading.PowderIn) != 0 && ship.World.GetItem(new AssetLocation(GunLoading.Powder)) is Item powder)
                    loose.Items.Add(new ItemStack(powder));
                if (state.Charge == GunLoading.Loaded && ship.World.GetItem(new AssetLocation(GunLoading.Shot)) is Item shot)
                    loose.Items.Add(new ItemStack(shot, gun.Swivel ? 1 : 4));
                if (take) state.Charge = 0;
            }
            foreach (var f in ship.Def.Fixtures)
            {
                if (f.Type != "attach" || !ship.Attached.TryGetValue(f.Code, out var stack)) continue;
                if (f.Kind is "chest" or "cargo")
                {
                    var l = new WreckLoot { Fixture = f.Code, Container = stack };
                    if (ship.Storages.TryGetValue(f.Code, out var inv)) l.Items.AddRange(Contents(inv, take));
                    list.Add(l);
                }
                else loose.Items.Add(stack);
            }
            // her hearth's fuel, what was cooking and what was cooked (and a pot's ingredients); her oven's fuel and what was
            // baking. (Before her pieces: taking up a placed hearth or oven takes its inventory away with it.)
            if (ship.HearthInv != null) loose.Items.AddRange(Contents(ship.HearthInv, take));
            if (ship.OvenInv != null) loose.Items.AddRange(Contents(ship.OvenInv, take));
            // the pieces put aboard: each container with what's in it, where it stood; the rest loose (ShipDecor)
            ship.PieceLoot(list, loose, take);
            foreach (var kv in ship.Storages)
                if (ship.Def.Fixture(kv.Key)?.Type != "attach" && !kv.Key.StartsWith("piece-")) loose.Items.AddRange(Contents(kv.Value, take));
            if (!ship.Clad && ship.CladPlates > 0 && ship.World.GetItem(new AssetLocation("game:metalplate-copper")) is Item plate)
                loose.Items.Add(new ItemStack(plate, ship.CladPlates));
            if (take)
            {
                ship.Attached.Clear();
                ship.CladPlates = 0;
            }
            list.Add(loose);
            return list;
        }

        static IEnumerable<ItemStack> Contents(InventoryBase inv, bool take)
        {
            var got = new List<ItemStack>();
            foreach (var slot in inv)
            {
                if (slot?.Itemstack == null || slot.StackSize <= 0) continue;
                got.Add(slot.Itemstack);
                // (straight out of the slot, not TakeOut: each change would save the whole hold again, and she's removed
                // in this same tick)
                if (take) slot.Itemstack = null;
            }
            return got;
        }

        /// <summary>Anyone (creature or player) whose box would be in a cell of hers: up out of it (and out of ground). How many were moved.</summary>
        public int MoveOut(WreckPlan plan, int dim)
        {
            int moved = 0;
            var c = new Vec3d((plan.MinX + plan.MaxX + 1) / 2.0, (plan.MinY + plan.MaxY + 1) / 2.0, (plan.MinZ + plan.MaxZ + 1) / 2.0);
            float hr = (plan.MaxX - plan.MinX) / 2f + 3, vr = (plan.MaxY - plan.MinY) / 2f + 3, dr = (plan.MaxZ - plan.MinZ) / 2f + 3;
            var ents = sapi.World.GetEntitiesAround(c, Math.Max(hr, dr), vr, e => e is EntityAgent && e is not EntityShip && e.Alive);
            foreach (var e in ents)
            {
                var box = e.CollisionBox;
                if (box == null) continue;
                double x = e.Pos.X, y = e.Pos.Y, z = e.Pos.Z;
                double dy = 0;
                while (dy < 40 && Inside(plan, dim, x + box.X1, y + dy + box.Y1, z + box.Z1, x + box.X2, y + dy + box.Y2, z + box.Z2)) dy += 0.25;
                if (dy <= 0) continue;
                e.TeleportToDouble(x, y + dy, z);
                moved++;
                sapi.Logger.Notification("[hansa] Moved {0} up {1:0.00} out of a wreck's blocks", e.GetName(), dy);
            }
            return moved;
        }

        bool Inside(WreckPlan plan, int dim, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            const double e = 1e-4;
            var ba = sapi.World.BlockAccessor;
            for (int bx = (int)Math.Floor(x0 + e); bx <= (int)Math.Floor(x1 - e); bx++)
                for (int by = (int)Math.Floor(y0 + e); by <= (int)Math.Floor(y1 - e); by++)
                    for (int bz = (int)Math.Floor(z0 + e); bz <= (int)Math.Floor(z1 - e); bz++)
                    {
                        if (plan.Blocks.TryGetValue(WreckPlan.Key(bx, by, bz), out var cells))
                        {
                            foreach (var b in WreckCells.Boxes(cells.All))
                            {
                                double a0 = bx + b[0] * 0.25, a1 = bx + b[3] * 0.25, c0 = by + b[1] * 0.25, c1 = by + b[4] * 0.25, d0 = bz + b[2] * 0.25, d1 = bz + b[5] * 0.25;
                                if (x0 < a1 - e && x1 > a0 + e && y0 < c1 - e && y1 > c0 + e && z0 < d1 - e && z1 > d0 + e) return true;
                            }
                            continue;
                        }
                        var pos = new BlockPos(bx, by, bz, dim);
                        var bl = ba.GetBlock(pos, BlockLayersAccess.Solid);
                        if (bl == null || bl.Id == 0) continue;
                        var boxes = bl.GetCollisionBoxes(ba, pos);
                        if (boxes == null) continue;
                        foreach (var b in boxes)
                            if (x0 < bx + b.X2 - e && x1 > bx + b.X1 + e && y0 < by + b.Y2 - e && y1 > by + b.Y1 + e && z0 < bz + b.Z2 - e && z1 > bz + b.Z1 + e) return true;
                    }
            return false;
        }

        // ------------------------------------------------------------------ the last tick

        /// <summary>The containers with her cargo, the extra chests, the guns, the sand; then she's taken away.</summary>
        void Finish(Job job)
        {
            var ship = job.Ship;
            var plan = job.Plan;
            var report = job.Report;
            int dim = Dim(job);
            var ba = sapi.World.BlockAccessor;
            var world = sapi.World;
            Registry.Done[job.Id] = new WreckRecord
            {
                Name = ship?.GetName() ?? job.Input.Def.Code, X = (int)Math.Floor(job.Input.X), Y = (int)Math.Floor(job.Input.Y), Z = (int)Math.Floor(job.Input.Z),
                Blocks = plan.Blocks.Count,
            };
            jobs.Remove(job.Id);
            // her cargo out of her, and she's gone (with nothing left in her, nothing drops): from here on it's only in
            // these lists, so anything that goes wrong below drops what's left beside her
            var loot = ship != null ? LootOf(ship, take: true) : job.Loot;
            ship?.Die(EnumDespawnReason.Removed);
            var loose = new List<ItemStack>();
            // ((0.3.20) bottom up: a container planned over another one's spot stands on it once it's there. Put down first, a
            // storage vessel over the block a chest was still to take had nothing under it and fell, its contents with it: the
            // self-test's wreck lost its vessel's flint with one seed)
            int SpotY(WreckLoot l)
            {
                int i = Array.IndexOf(job.Input.Containers, l.Fixture);
                return i >= 0 && i < plan.Containers.Count && plan.Containers[i] != null ? plan.Containers[i].Y : int.MaxValue;
            }
            var pending = loot.Where(l => l.Fixture != null).OrderBy(SpotY).ToList();
            foreach (var l in loot) if (l.Fixture == null) loose.AddRange(l.Items.Where(s => s != null && s.StackSize > 0));
            var drop = new Vec3d(job.Input.X, plan.MinY + 1, job.Input.Z);
            if (plan.Extra.Count > 0) drop = new Vec3d(plan.Extra[0].X + 0.5, plan.Extra[0].Y + 1, plan.Extra[0].Z + 0.5);
            try
            {
                var site = new WorldWreckSite(ba, job.Id, dim);
                // her containers where the plan has them (a chest, a labeled chest or a vessel as itself; else a chest for
                // its contents, and the trunk or crate itself with the loose things)
                while (pending.Count > 0)
                {
                    var l = pending[0];
                    int i = Array.IndexOf(job.Input.Containers, l.Fixture);
                    var spot = i >= 0 && i < plan.Containers.Count ? plan.Containers[i] : null;
                    var items = l.Items.Where(s => s != null && s.StackSize > 0).ToList();
                    if (spot == null || site.At(spot.X, spot.Y, spot.Z, out _) == WreckGround.Ground)
                    {
                        pending.RemoveAt(0);
                        if (l.Container != null) loose.Add(l.Container);
                        loose.AddRange(items);
                        continue;
                    }
                    var pos = new BlockPos(spot.X, spot.Y, spot.Z, dim);
                    string what;
                    if (l.Container != null && PlaceAsIs(l.Container) is Block own)
                    {
                        var placed = own.Variant["side"] != null ? world.GetBlock(own.CodeWithVariant("side", spot.Side)) ?? own : own;
                        // ((0.3.20) a storage vessel falls (vanilla's UnstableFalling) where nothing holds it up (her cells taken out
                        // for the spot under it, water): put down where it would come to rest, so its contents are where it is)
                        if (placed.BlockBehaviors?.Any(bb => bb.GetType().Name.Contains("UnstableFalling")) == true)
                            for (int n = 0; n < 16 && pos.Y > 1 && Unsupported(ba, pos.DownCopy()) && site.At(pos.X, pos.Y - 1, pos.Z, out _) != WreckGround.Ground; n++)
                                pos = pos.DownCopy();
                        ba.SetBlock(placed.Id, pos, l.Container);
                        l.Container = null;
                        what = l.Fixture + ": " + placed.Code;
                    }
                    else
                    {
                        PlaceChest(pos, spot.Side);
                        what = l.Fixture + ": a chest for " + (l.Container?.Collectible?.Code?.ToString() ?? "it");
                    }
                    report.Containers.Add((pos, what));
                    var left = Fill(pos, items);
                    pending.RemoveAt(0);
                    if (l.Container != null) loose.Add(l.Container);
                    loose.AddRange(left);
                }
                // what she carried loose: chests on the seabed beside her
                int e = 0;
                while (loose.Count > 0 && e < plan.Extra.Count)
                {
                    var spot = plan.Extra[e++];
                    if (site.At(spot.X, spot.Y, spot.Z, out _) == WreckGround.Ground) continue;
                    var pos = new BlockPos(spot.X, spot.Y, spot.Z, dim);
                    if (!PlaceChest(pos, spot.Side)) continue;
                    report.Containers.Add((pos, "extra chest"));
                    var some = loose.Take(16).ToList();
                    loose.RemoveRange(0, some.Count);
                    loose.InsertRange(0, Fill(pos, some));
                }
                // the guns
                foreach (var spot in plan.Guns)
                {
                    var gun = world.GetBlock(new AssetLocation("hansa:gun-bronze-" + spot.Side));
                    if (gun == null) continue;
                    var pos = new BlockPos(spot.X, spot.Y, spot.Z, dim);
                    if (site.At(spot.X, spot.Y, spot.Z, out _) != WreckGround.Ground) ba.SetBlock(gun.Id, pos);
                    else world.SpawnItemEntity(new ItemStack(world.GetBlock(new AssetLocation("hansa:gun-bronze-north")) ?? gun), pos.ToVec3d().Add(0.5, 1, 0.5));
                    report.Guns++;
                }
                // sand or gravel against her lowest blocks, where the seabed is sand or gravel
                foreach (var spot in plan.Sediment)
                {
                    var pos = new BlockPos(spot.X, spot.Y, spot.Z, dim);
                    var below = ba.GetBlock(pos.DownCopy(), BlockLayersAccess.Solid);
                    string first = below?.FirstCodePart();
                    if (first is not ("sand" or "gravel" or "muddygravel") || site.At(spot.X, spot.Y, spot.Z, out _) != WreckGround.Free) continue;
                    ba.SetBlock(below.Id, pos);
                    report.Sediment++;
                }
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[hansa] Placing the wreck #{0}'s containers and guns failed; what's left of her cargo is dropped beside her: {1}", job.Id, ex);
                foreach (var l in pending)
                {
                    if (l.Container != null) loose.Add(l.Container);
                    loose.AddRange(l.Items.Where(s => s != null && s.StackSize > 0));
                }
            }
            // anything left: dropped on the seabed beside her middle
            foreach (var s in loose) world.SpawnItemEntity(s, drop);
            report.Dropped += loose.Count;
            report.Finished = true;
            var rec = Registry.Done[job.Id];
            sapi.Logger.Notification("[hansa] {0} #{1} is a wreck at {2},{3},{4}: {5} blocks ({6} cells), {7} containers, {8} guns{9}",
                rec.Name, job.Id, rec.X, rec.Y, rec.Z, plan.Blocks.Count, plan.CellsPlaced, report.Containers.Count, report.Guns,
                report.Dropped > 0 ? $", {report.Dropped} stacks dropped beside her" : "");
        }

        /// <summary>A container that goes down as itself: a chest, a labeled chest or a storage vessel (one block). Trunks and crates don't (a chest takes their contents).</summary>
        static Block PlaceAsIs(ItemStack container)
        {
            var b = container?.Block;
            if (b is not BlockGenericTypedContainer || b is BlockGenericTypedContainerTrunk) return null;
            return b.FirstCodePart() is "chest" or "labeledchest" or "storagevessel" ? b : null;
        }

        /// <summary>(0.3.20) Nothing under a block to stand on: air or water (or anything replaceable) in the solid layer.</summary>
        static bool Unsupported(IBlockAccessor ba, BlockPos below)
        {
            var b = ba.GetBlock(below, BlockLayersAccess.Solid);
            return b == null || b.Id == 0 || b.Replaceable >= 6000;
        }

        /// <summary>A vanilla chest (an aged one) here, facing this way.</summary>
        bool PlaceChest(BlockPos pos, string side)
        {
            var chest = sapi.World.GetBlock(new AssetLocation("game:chest-" + side)) ?? sapi.World.GetBlock(new AssetLocation("game:chest-east"));
            if (chest == null) return false;
            var stack = new ItemStack(chest);
            stack.Attributes.SetString("type", "normal-aged");
            sapi.World.BlockAccessor.SetBlock(chest.Id, pos, stack);
            return sapi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityContainer;
        }

        /// <summary>Put the stacks into the container here (a stack a slot); what doesn't fit (or no container) comes back.</summary>
        List<ItemStack> Fill(BlockPos pos, List<ItemStack> stacks)
        {
            var be = sapi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityContainer;
            var inv = be?.Inventory;
            var left = new List<ItemStack>();
            int slot = 0;
            foreach (var s in stacks)
            {
                while (inv != null && slot < inv.Count && !inv[slot].Empty) slot++;
                if (inv == null || slot >= inv.Count) { left.Add(s); continue; }
                inv[slot].Itemstack = s;
                inv[slot].MarkDirty();
                slot++;
            }
            be?.MarkDirty(true);
            return left;
        }
    }

    /// <summary>Thrown when a wreck's builder asks about a block in a chunk that isn't loaded: it's tried again later.</summary>
    public class WreckSiteUnloaded : Exception
    {
    }

    /// <summary>
    /// The world, for a wreck's builder: air and water (in the fluid layer) are free; a block without collision (seaweed,
    /// water plants), a plant or a replaceable one is soft (the wreck may take its place); a wreck block of hers is hers;
    /// anything else is ground (rock, soil, sand, gravel, someone's blocks, another wreck's).
    /// </summary>
    public class WorldWreckSite : IWreckSite
    {
        readonly IBlockAccessor ba;
        readonly long id;
        readonly BlockPos pos;
        readonly int mapY;

        public WorldWreckSite(IBlockAccessor ba, long wreckId, int dim)
        {
            this.ba = ba;
            id = wreckId;
            pos = new BlockPos(dim);
            mapY = ba.MapSizeY;
        }

        public WreckGround At(int x, int y, int z, out float top)
        {
            top = 1;
            if (y < 0) return WreckGround.Ground;
            if (y >= mapY) return WreckGround.Free;
            pos.Set(x, y, z);
            if (ba.GetChunkAtBlockPos(pos) == null) throw new WreckSiteUnloaded();
            var b = ba.GetBlock(pos, BlockLayersAccess.Solid);
            if (b == null || b.Id == 0) return WreckGround.Free;
            if (b is BlockWreck) return ba.GetBlockEntity(pos) is BlockEntityWreck be && be.WreckId == id ? WreckGround.Ours : WreckGround.Ground;
            var boxes = b.GetCollisionBoxes(ba, pos);
            if (boxes == null || boxes.Length == 0 || b.BlockMaterial is EnumBlockMaterial.Plant or EnumBlockMaterial.Leaves || b.Replaceable >= 6000)
                return WreckGround.Soft;
            top = Math.Clamp(boxes.Max(c => c.Y2), 0, 1);
            return WreckGround.Ground;
        }
    }
}
