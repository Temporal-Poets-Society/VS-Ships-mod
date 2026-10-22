using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// (22 October 2026, the user after trying the caravel: "Items cannot be dropped and picked on ship - they fall
    /// through") Things dropped aboard: vanilla's item entities (thrown with Q, a broken block's drops, what spills out of
    /// something). Her decks aren't world blocks, so vanilla's physics let them fall through her into the sea.
    /// - Server: an item that turns up over her (spawned there, or falling onto her out of the air) is hers. It falls,
    ///   slides and rests against her voxels in her level ship-local frame (<see cref="DeckItemMover"/>), and every tick
    ///   it's put where that place on her is in the world, so it rides along; vanilla's physics skips it (Patches). It's
    ///   picked up as any item is (vanilla collects what's near a player, and a player on her deck is where she has him).
    ///   It's no longer hers when nothing of her is under it (over the side, off her ends) or she's gone.
    ///   Its place aboard is kept in its WatchedAttributes (<see cref="Key"/>: her entity id, ship-local x y z, at rest):
    ///   saved with it (after a restart it waits for her to load) and sent to clients.
    /// - Client: an item with that place is drawn there on her as she's drawn (her pitch and roll, her smoothed place, round
    ///   the camera of a player aboard), not where the server's level frame puts it, and vanilla's remote physics leaves it be (no splashing in her hold,
    ///   no tumbling as if falling).
    /// Floating items she sails over aren't taken in; nor are items lying on world blocks (a quay beside her).
    /// </summary>
    public class DeckItems
    {
        public const string Key = "hansaDeck";
        /// <summary>After a restart, how long an item waits for her to load before vanilla has it again.</summary>
        const long WaitMs = 30000;

        class Held
        {
            public EntityItem Item;
            public long ShipId;
            public EntityShip Ship;
            public DeckItemMover Mover;
            public long WaitUntil;
            public double SentX = double.NaN, SentY, SentZ;
            public bool SentRest;
        }

        /// <summary>Server: the items aboard, by entity id (vanilla's physics thread asks <see cref="HeldOnServer"/>).</summary>
        static readonly ConcurrentDictionary<long, Held> serverHeld = new();
        /// <summary>Client: the items its server says are aboard (main thread only).</summary>
        static readonly Dictionary<long, EntityItem> clientHeld = new();
        static readonly List<long> gone = new();
        static readonly Dictionary<long, (EntityShip Ship, Vec3d Origin)> frameShips = new();

        readonly ICoreServerAPI sapi;
        readonly HansaMod mod;
        readonly List<EntityItem> fresh = new();

        public DeckItems(ICoreServerAPI api, HansaMod mod)
        {
            sapi = api;
            this.mod = mod;
            serverHeld.Clear();
            api.Event.OnEntitySpawn += e => { if (e is EntityItem item) fresh.Add(item); };
            api.Event.OnEntityLoaded += e => { if (e is EntityItem item) Restore(item); };
            api.Event.RegisterGameTickListener(Tick, 20);
        }

        /// <summary>Server: vanilla's passive physics leaves this one to us.</summary>
        public static bool HeldOnServer(Entity e) => e is EntityItem && serverHeld.ContainsKey(e.EntityId);

        /// <summary>Server: is this item lying aboard this ship (for the self-test)?</summary>
        public static bool IsAboard(Entity e, EntityShip ship) => e != null && serverHeld.TryGetValue(e.EntityId, out var h) && h.Ship == ship;

        /// <summary>Server: its place aboard, ship-local (the middle of its bottom), and whether it's at rest; false if not aboard.</summary>
        public static bool PlaceAboard(Entity e, out Vec3d local, out bool rest)
        {
            local = null;
            rest = false;
            if (e == null || !serverHeld.TryGetValue(e.EntityId, out var h)) return false;
            local = new Vec3d(h.Mover.X, h.Mover.Y, h.Mover.Z);
            rest = h.Mover.OnGround && h.Mover.VX == 0 && h.Mover.VZ == 0;
            return true;
        }

        // (not her Active state: with no player within her simulation range she's Inactive, and still sails)
        static bool Usable(EntityShip ship) => ship != null && ship.Alive && ship.State != EnumEntityState.Despawned && ship.Def != null && ship.Grid != null;

        // ------------------------------------------------------------------ server

        void Tick(float dt)
        {
            dt = Math.Min(dt, 0.2f);
            var ships = mod.ServerShips.Where(Usable).ToArray();
            // new ones over her: hers even afloat in her hold (that's where they were dropped)
            if (fresh.Count > 0)
            {
                foreach (var e in fresh)
                {
                    if (!e.Alive || serverHeld.ContainsKey(e.EntityId)) continue;
                    foreach (var ship in ships) if (Take(e, ship, true)) break;
                }
                fresh.Clear();
            }
            // ones falling onto her out of the air
            foreach (var ship in ships)
            {
                var g = ship.Grid;
                float r = (float)(Math.Max(Math.Max(-g.OX, g.MaxX), Math.Max(-g.OZ, g.MaxZ)) + 1);
                float ry = (float)((g.MaxY - g.OY) / 2 + 2);
                var mid = new Vec3d(ship.Pos.X, ship.Pos.Y + (g.OY + g.MaxY) / 2, ship.Pos.Z);
                foreach (var e in sapi.World.GetEntitiesAround(mid, r, ry, e => e is EntityItem && e.Alive && !serverHeld.ContainsKey(e.EntityId)))
                {
                    Take((EntityItem)e, ship, false);
                }
            }
            if (serverHeld.IsEmpty) return;
            long now = sapi.World.ElapsedMilliseconds;
            foreach (var h in serverHeld.Values.ToArray()) Step(h, dt, now);
        }

        /// <summary>Make it hers if she's under it. Not fresh: not one afloat (she sails over it) or lying on world blocks.</summary>
        bool Take(EntityItem e, EntityShip ship, bool isFresh)
        {
            if (!isFresh && (e.OnGround || e.FeetInLiquid || e.Swimming)) return false;
            var l = ship.WorldToLocal(e.Pos.X, e.Pos.Y, e.Pos.Z);
            var m = Mover(e, l.X, l.Y, l.Z);
            var g = ship.Grid;
            if (!m.Over(g) || m.Stuck(g) && !m.Free(g)) return false;
            // thrown: vanilla's motion (blocks a 60th of a second), as she carries it
            var v = ship.WorldDirToLocal(e.Pos.Motion.X * 60, e.Pos.Motion.Y * 60, e.Pos.Motion.Z * 60);
            m.VX = v.X; m.VY = v.Y; m.VZ = v.Z;
            var h = new Held { Item = e, ShipId = ship.EntityId, Ship = ship, Mover = m };
            serverHeld[e.EntityId] = h;
            Place(h, ship);
            return true;
        }

        static DeckItemMover Mover(Entity e, double x, double y, double z)
        {
            var box = e.CollisionBox;
            return box == null ? new DeckItemMover(x, y, z) : new DeckItemMover(x, y, z, Math.Max(0.05, box.XSize / 2), Math.Max(0.05, box.YSize));
        }

        /// <summary>Loaded with its place aboard (a restart, its chunk coming back): hers again once she's there.</summary>
        void Restore(EntityItem e)
        {
            if (e.WatchedAttributes[Key] is not ITreeAttribute t) return;
            serverHeld[e.EntityId] = new Held
            {
                Item = e,
                ShipId = t.GetLong("ship"),
                Mover = Mover(e, t.GetDouble("x"), t.GetDouble("y"), t.GetDouble("z")),
                WaitUntil = sapi.World.ElapsedMilliseconds + WaitMs,
            };
        }

        void Step(Held h, float dt, long now)
        {
            var e = h.Item;
            if (!e.Alive || e.State == EnumEntityState.Despawned) { serverHeld.TryRemove(e.EntityId, out _); return; }
            if (!Usable(h.Ship))
            {
                var ship = sapi.World.GetEntityById(h.ShipId) as EntityShip;
                if (ship == null && now < h.WaitUntil) return;
                if (!Usable(ship)) { Release(h, null); return; }
                h.Ship = ship;
            }
            if (!h.Mover.Step(h.Ship.Grid, dt)) { Release(h, h.Ship); return; }
            Place(h, h.Ship);
        }

        void Place(Held h, EntityShip ship)
        {
            var e = h.Item;
            var m = h.Mover;
            ship.LocalToWorld(m.X, m.Y, m.Z, out double wx, out double wy, out double wz);
            e.Pos.SetPos(wx, wy, wz);
            // into the chunk it's now over at once (vanilla catches up once a second): picking up looks by chunk
            const int cs = GlobalConstants.ChunkSize;
            long chunk = sapi.WorldManager.ChunkIndex3D((int)Math.Floor(wx) / cs, (int)Math.Floor(wy) / cs, (int)Math.Floor(wz) / cs);
            if (chunk != e.InChunkIndex3d) sapi.World.UpdateEntityChunk(e, chunk);
            e.Pos.Motion.Set(0, 0, 0);
            e.OnGround = e.CollidedVertically = m.OnGround;
            e.FeetInLiquid = e.Swimming = false;
            if (m.OnGround) e.PositionBeforeFalling.Set(wx, wy, wz);

            bool rest = m.OnGround && m.VX == 0 && m.VZ == 0;
            if (Math.Abs(m.X - h.SentX) + Math.Abs(m.Y - h.SentY) + Math.Abs(m.Z - h.SentZ) < 1e-3 && rest == h.SentRest &&
                e.WatchedAttributes[Key] is ITreeAttribute) return;
            var t = new TreeAttribute();
            t.SetLong("ship", ship.EntityId);
            t.SetDouble("x", m.X);
            t.SetDouble("y", m.Y);
            t.SetDouble("z", m.Z);
            t.SetBool("rest", rest);
            e.WatchedAttributes[Key] = t;
            e.WatchedAttributes.MarkPathDirty(Key);
            h.SentX = m.X; h.SentY = m.Y; h.SentZ = m.Z; h.SentRest = rest;
        }

        /// <summary>No longer hers: vanilla has it again, going on with her way and its own (over the side into the sea).</summary>
        static void Release(Held h, EntityShip ship)
        {
            var e = h.Item;
            serverHeld.TryRemove(e.EntityId, out _);
            e.WatchedAttributes.RemoveAttribute(Key);
            e.WatchedAttributes.MarkAllDirty();
            e.OnGround = e.CollidedVertically = false;
            if (ship == null) return;
            var m = h.Mover;
            var v = ship.LocalDirToWorld(m.VX + ship.Speed, m.VY, m.VZ + ship.Leeway);
            e.Pos.Motion.Set(v.X / 60, v.Y / 60, v.Z / 60);
        }

        // ------------------------------------------------------------------ client

        static readonly AccessTools.FieldRef<EntityItemRenderer, Vec3d> lerpedPos = TryLerpedPos();

        static AccessTools.FieldRef<EntityItemRenderer, Vec3d> TryLerpedPos()
        {
            try { return AccessTools.FieldRefAccess<EntityItemRenderer, Vec3d>("lerpedPos"); }
            catch { return null; }
        }

        /// <summary>
        /// Client, from vanilla's remote physics (Patches): true when the server says the item is aboard, so vanilla's
        /// physics leaves it be; noted to be drawn on her (<see cref="ClientFrame"/>).
        /// </summary>
        public static bool ClientRemote(Entity e)
        {
            if (e is not EntityItem item || e.World?.Side != EnumAppSide.Client) return false;
            if (e.WatchedAttributes[Key] is not ITreeAttribute t)
            {
                clientHeld.Remove(e.EntityId);
                return false;
            }
            clientHeld[e.EntityId] = item;
            Settle(item, t.GetBool("rest"));
            return true;
        }

        static void Settle(Entity e, bool rest)
        {
            e.OnGround = e.CollidedVertically = rest;
            e.FeetInLiquid = e.Swimming = false;
        }

        /// <summary>
        /// Client, each frame after the ships and the player have moved: the items aboard where their places on her are
        /// drawn. (dev.6, the user: "they bit shuffle around when player moves") From where her hull is drawn
        /// (<see cref="ShipRenderer.RenderOrigin"/>), not her position: for whoever is aboard she's drawn round their
        /// camera, which runs ahead of or behind their seat between physics ticks as they walk; placed from her position the
        /// items slid about on her deck by that much.
        /// </summary>
        public static void ClientFrame(ICoreClientAPI capi, ShipRenderer renderer, EntityPlayer ep)
        {
            // ((after 0.1.0-dev.6, Codex's review) no copy of the list, and each ship's drawn place once a frame; cleared
            // first, so nothing of a ship is kept once nothing lies aboard)
            gone.Clear();
            frameShips.Clear();
            if (clientHeld.Count == 0) return;
            foreach (var (id, e) in clientHeld)
            {
                if (!e.Alive || e.State == EnumEntityState.Despawned || e.WatchedAttributes[Key] is not ITreeAttribute t)
                {
                    gone.Add(id);
                    continue;
                }
                long sid = t.GetLong("ship");
                if (!frameShips.TryGetValue(sid, out var at))
                {
                    var sh = capi.World.GetEntityById(sid) as EntityShip;
                    at = (sh, sh?.Def == null ? null : renderer != null && ep != null ? renderer.RenderOrigin(sh, ep) : new Vec3d(sh.Pos.X, sh.Pos.Y, sh.Pos.Z));
                    frameShips[sid] = at;
                }
                if (at.Ship == null || at.Origin == null) continue;
                var ship = at.Ship;
                var o = at.Origin;
                ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, t.GetDouble("x"), t.GetDouble("y"), t.GetDouble("z"), out double dx, out double dy, out double dz);
                double wx = o.X + dx, wy = o.Y + dy, wz = o.Z + dz;
                e.Pos.SetPos(wx, wy, wz);
                Settle(e, t.GetBool("rest"));
                // vanilla's item renderer eases toward the place (a moving ship would leave it trailing behind her)
                if (lerpedPos != null && e.Properties?.Client?.Renderer is EntityItemRenderer r) lerpedPos(r).Set(wx, wy, wz);
            }
            foreach (long id in gone) clientHeld.Remove(id);
        }
    }
}
