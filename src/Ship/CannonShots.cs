using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// The server's cannon balls in flight (config Cannons): each is stepped every tick in the fixed steps of
    /// <see cref="Ballistics"/>, and each step's stretch of its path is tested against what's in its way, the nearest
    /// first:
    /// - a ship (her collision as she really lies: <see cref="EntityShip.ShotHit"/>): <see cref="EntityShip.Damage"/> with
    ///   the ball's energy. Through her planking, it goes on out of her far side with what it has left (and strikes whoever
    ///   stands in its way inside her); else it stops in her. The ship that fired it can't be hit in its first few blocks.
    /// - a creature or a player: vanilla damage (blunt, from the one who fired it; players only with PvP on), and it goes on
    ///   with less.
    /// - a block: it stops there in a puff of its dust, unless blocks may break (config CannonsBreakBlocks): then a few soft
    ///   ones go (not in a land claim the firer may not build in).
    /// - the water: a splash; at a shallow angle it skips, else it goes in and is soon spent (it can still hole a hull a
    ///   block or two under the water).
    /// Clients are told when one is fired, goes on from somewhere, or stops (<see cref="ShotPacket"/>) and fly it
    /// themselves in between; the splashes, smoke, splinters and dust are vanilla particles the server spawns (they reach
    /// every client that has the chunk), the sounds vanilla's.
    /// </summary>
    public class CannonShots
    {
        readonly ICoreServerAPI sapi;
        readonly HansaMod mod;
        readonly List<Ball> flying = new();
        int nextId;
        bool failedOnce;

        /// <summary>What the balls did lately (the self-test and the fire command read it): the last 400 events.</summary>
        public readonly List<ShotRecord> Records = new();
        public int Fired;

        public IReadOnlyList<Ball> Flying => flying;

        public class Ball
        {
            public int Id;
            public ShotState S;
            public double Mass;
            /// <summary>(25 October 2026) The radius it strikes with: its gun's (a swivel's small ball), else the cannon's.</summary>
            public float HitRadius;
            public CannonDef D;
            public EntityShip From;
            public Entity Firer;
            public IServerPlayer Player;
            /// <summary>Blocks it has flown, and the time not yet stepped.</summary>
            public double Travel, Carry;
            /// <summary>Ships and creatures it has gone through (not met again).</summary>
            public readonly HashSet<long> Through = new();
            public int Blocks;
            public bool Done;

            public double Energy => Ballistics.Energy(D, Mass, S.Speed);
        }

        /// <summary>One thing a ball did: fired, ship, creature, block, broke, water, ricochet, spent, lost.</summary>
        public class ShotRecord
        {
            public int Id;
            public string What;
            public Vec3d At;
            public long Entity;
            public double Energy;
            public DamageResult Damage;
            public long Ms;
        }

        public CannonShots(ICoreServerAPI sapi, HansaMod mod)
        {
            this.sapi = sapi;
            this.mod = mod;
        }

        // ------------------------------------------------------------------ firing

        /// <summary>A gun fires: the ball leaves the muzzle (world) with this velocity; the boom, the flash and the smoke.</summary>
        public Ball Fire(EntityShip ship, FixtureDef gun, Entity firer, IServerPlayer player, Vec3d at, Vec3d vel, Vec3d dir)
        {
            var d = ship.Def?.Cannon ?? new CannonDef();
            var b = new Ball
            {
                Id = ++nextId, D = d, Mass = Ballistics.MassOf(gun, d), HitRadius = gun.HitRadius > 0 ? gun.HitRadius : d.HitRadius, From = ship, Firer = firer, Player = player,
                S = new ShotState { X = at.X, Y = at.Y, Z = at.Z, VX = vel.X, VY = vel.Y, VZ = vel.Z },
            };
            flying.Add(b);
            Fired++;
            mod.SendShot(new ShotPacket
            {
                Id = b.Id, Kind = ShotPacket.Fired, X = at.X, Y = at.Y, Z = at.Z, VX = (float)vel.X, VY = (float)vel.Y, VZ = (float)vel.Z,
                ShipId = ship.EntityId, Gun = gun.Index
            }, at);
            CannonEffects.Fired(sapi.World, at, dir, mod.WindAt(at.X, at.Z), d);
            Record(b, "fired", at, ship.EntityId, b.Energy);
            return b;
        }

        // ------------------------------------------------------------------ flying

        public void Tick(float dt)
        {
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var b = flying[i];
                try { Fly(b, dt); }
                catch (Exception e)
                {
                    b.Done = true;
                    if (!failedOnce) sapi.Logger.Error("[ships] A cannon ball's flight failed (it's dropped): {0}", e);
                    failedOnce = true;
                }
                if (b.Done) flying.RemoveAt(i);
            }
        }

        void Fly(Ball b, float dt)
        {
            b.Carry += Math.Min(dt, 0.25f);
            while (b.Carry >= Ballistics.Step && !b.Done)
            {
                b.Carry -= Ballistics.Step;
                var a = b.S.Pos;
                var next = b.S;
                Ballistics.Advance(ref next, b.D);
                if (Meets(b, a, next)) continue;
                b.Travel += a.DistanceTo(next.Pos);
                b.S = next;
                if (b.S.InWater && b.S.Speed < b.D.Spent) { Stop(b, "spent", b.S.Pos); continue; }
                var bp = new BlockPos((int)Math.Floor(b.S.X), (int)Math.Floor(b.S.Y), (int)Math.Floor(b.S.Z), Dim(b));
                if (b.S.Time > b.D.MaxFlight || b.S.Y < 1 || sapi.World.BlockAccessor.GetChunkAtBlockPos(bp) == null) Stop(b, "lost", b.S.Pos);
            }
        }

        int Dim(Ball b) => b.From?.Pos.Dimension ?? 0;

        /// <summary>
        /// The stretch from a to next (one step): what it meets first, if anything, is dealt with (the ball goes on from
        /// there, or stops). True if it met something (the step is used up).
        /// </summary>
        bool Meets(Ball b, Vec3d a, ShotState next)
        {
            var seg = next.Pos.SubCopy(a.X, a.Y, a.Z);
            double len = seg.Length();
            if (len < 1e-9) return false;
            var u = new Vec3d(seg.X / len, seg.Y / len, seg.Z / len);
            double best = double.MaxValue;
            int kind = 0;       // 1 ship, 2 creature, 3 block, 4 water
            EntityShip ship = null;
            ShipRayHit shipHit = default;
            double sPitch = 0, sRoll = 0;
            Entity creature = null;
            BlockPos block = null;
            Block blockType = null;

            // ships (not the one that fired it in its first blocks, nor one it has gone through)
            foreach (var s in mod.ServerShips)
            {
                if (s.Def == null || !s.Alive || s.Pos.Dimension != Dim(b) || b.Through.Contains(s.EntityId)) continue;
                if (s == b.From && b.Travel < b.D.SelfSafe) continue;
                double reach = s.Def.Length / 2 + 12 + len;
                if (s.Pos.SquareDistanceTo(a.X, a.Y, a.Z) > reach * reach) continue;
                if (s.ShotHit(a, u, len, out var h, out double p, out double r) && h.Distance < best)
                {
                    best = h.Distance; kind = 1; ship = s; shipHit = h; sPitch = p; sRoll = r;
                }
            }
            // creatures and players
            double t = FirstCreature(b, a, seg, len, out var c);
            if (c != null && t * len < best) { best = t * len; kind = 2; creature = c; }
            // blocks
            t = FirstBlock(b, a, seg, out var bp, out var bt);
            if (bp != null && t * len < best) { best = t * len; kind = 3; block = bp; blockType = bt; }
            // the water's surface
            if (!b.S.InWater)
            {
                double surf = mod.SeaSurface(next.X, next.Y, next.Z, Dim(b));
                if (!double.IsNaN(surf))
                {
                    double tw = Ballistics.Crossing(a, next.Pos, surf);
                    if (tw < 0 && a.Y < surf) tw = 0;       // (under it already: a ball come out of a hull below the waterline)
                    if (tw >= 0 && tw * len < best) { best = tw * len; kind = 4; }
                }
            }
            if (kind == 0) return false;

            // where it is when it gets there, going as fast as it was
            var at = a.AddCopy(u.X * best, u.Y * best, u.Z * best);
            b.Travel += best;
            var s0 = b.S;
            s0.X = at.X; s0.Y = at.Y; s0.Z = at.Z;
            s0.Time += Ballistics.Step * best / len;
            b.S = s0;
            switch (kind)
            {
                case 1: HitShip(b, ship, shipHit, at, u, sPitch, sRoll); break;
                case 2: HitCreature(b, creature, at, u); break;
                case 3: HitBlock(b, block, blockType, at, u); break;
                case 4: HitWater(b, at); break;
            }
            return true;
        }

        // ------------------------------------------------------------------ what it meets

        void HitShip(Ball b, EntityShip ship, ShipRayHit hit, Vec3d at, Vec3d u, double pitch, double roll)
        {
            double energy = b.Energy;
            ShipFrame.ToLocal(ship.Pos.Yaw, pitch, roll, u.X, u.Y, u.Z, out double lx, out double ly, out double lz);
            var res = ship.Damage(hit.Local, new Vec3d(lx, ly, lz), (float)energy, b.HitRadius, "cannon");
            Record(b, "ship", at, ship.EntityId, energy, res);
            // ((0.3.27, note 207) a scar where it went in and came out; (0.3.30, note 234: "nothing about holes from cannon")
            // none: what it hit is cracked or out (a hull panel, a deck cell, a piece), her fixed planking's nearest slot for it)
            CannonEffects.Splinters(sapi.World, at, u);
            if (res?.Pierced == true && res.Exit != null && res.Remaining >= 1)
            {
                // through her: whoever stands in its way inside her, then out of her far side with what it has left
                ShipFrame.ToWorld(ship.Pos.Yaw, pitch, roll, res.Exit.X, res.Exit.Y, res.Exit.Z, out double ex, out double ey, out double ez);
                var exit = new Vec3d(ship.Pos.X + ex, ship.Pos.InternalY + ey, ship.Pos.Z + ez);
                double left = StrikeAlong(b, at, exit, res.Remaining);
                b.Through.Add(ship.EntityId);
                if (left < 1) { Stop(b, "ship", exit); return; }
                double v = Ballistics.SpeedFor(b.D, b.Mass, left);
                var s = b.S;
                s.X = exit.X; s.Y = exit.Y; s.Z = exit.Z;
                s.VX = u.X * v; s.VY = u.Y * v; s.VZ = u.Z * v;
                double surf = mod.SeaSurface(exit.X, exit.Y, exit.Z, Dim(b));
                s.InWater = !double.IsNaN(surf) && exit.Y < surf;
                b.S = s;
                Going(b);
                return;
            }
            // it stopped in her: where it got to (the last thing it hit), and whoever stood there
            double depth = 0.3;
            if (res?.Pierced == true && res.Hits.Count > 0)
            {
                var c = ship.DamageParts.Parts[res.Hits[^1].Part].Center;
                depth = Math.Max(0.3, (c.X - hit.Local.X) * lx + (c.Y - hit.Local.Y) * ly + (c.Z - hit.Local.Z) * lz);
                StrikeAlong(b, at, at.AddCopy(u.X * depth, u.Y * depth, u.Z * depth), energy * 0.3);
            }
            if (res == null) CannonEffects.Thud(sapi.World, at);
            Stop(b, null, at.AddCopy(u.X * Math.Min(depth, 0.3), u.Y * Math.Min(depth, 0.3), u.Z * Math.Min(depth, 0.3)));
        }

        void HitCreature(Ball b, Entity e, Vec3d at, Vec3d u)
        {
            double left = Strike(b, e, at, b.Energy);
            if (left < 1) { Stop(b, null, at); return; }
            double v = Ballistics.SpeedFor(b.D, b.Mass, left), f = v / Math.Max(1e-9, b.S.Speed);
            var s = b.S;
            s.VX *= f; s.VY *= f; s.VZ *= f;
            b.S = s;
            Going(b);
        }

        /// <summary>Strike a creature or a player with this much energy; returns what the ball has left (or all of it, going through someone it may not hurt).</summary>
        double Strike(Ball b, Entity e, Vec3d at, double energy)
        {
            b.Through.Add(e.EntityId);
            if (!MayHurt(b, e)) return energy;
            float dmg = (float)(energy * b.D.CreatureDamage);
            e.ReceiveDamage(new DamageSource
            {
                Source = b.Firer != null ? EnumDamageSource.Entity : EnumDamageSource.Unknown,
                Type = EnumDamageType.BluntAttack,
                SourceEntity = b.Firer,
                CauseEntity = b.Firer,
                SourcePos = at.Clone(),
                HitPosition = at.SubCopy(e.Pos.X, e.Pos.Y, e.Pos.Z),
                KnockbackStrength = 2,
            }, dmg);
            Record(b, "creature", at, e.EntityId, energy);
            return energy - b.D.CreatureTakes;
        }

        /// <summary>Whoever stands on its way from a to c (through a ship's inside); returns the energy it has left.</summary>
        double StrikeAlong(Ball b, Vec3d a, Vec3d c, double energy)
        {
            var seg = c.SubCopy(a.X, a.Y, a.Z);
            double len = seg.Length();
            for (int guard = 0; guard < 8 && energy >= 1 && len > 1e-6; guard++)
            {
                double t = FirstCreature(b, a, seg, len, out var e);
                if (e == null) break;
                energy = Strike(b, e, a.AddCopy(seg.X * t, seg.Y * t, seg.Z * t), energy);
            }
            return energy;
        }

        /// <summary>Vanilla's rules for a projectile: a player shoots players only with PvP on and the right to, creatures with the right to.</summary>
        bool MayHurt(Ball b, Entity target)
        {
            if (b.Player == null) return true;
            if (target is EntityPlayer && (!sapi.Server.Config.AllowPvP || !b.Player.HasPrivilege("attackplayers"))) return false;
            if (target is EntityAgent && !(target is EntityPlayer) && !b.Player.HasPrivilege("attackcreatures")) return false;
            return true;
        }

        void HitBlock(Ball b, BlockPos pos, Block block, Vec3d at, Vec3d u)
        {
            double energy = b.Energy;
            double cost = b.D.BlockBreak * Math.Max(0.1, block.Resistance);
            if (mod.Config.CannonsBreakBlocks && b.Blocks < b.D.MaxBlocks && Soft(block, b.D) && energy > cost && MayBreak(b, pos))
            {
                CannonEffects.Dust(sapi.World, pos, at);
                sapi.World.BlockAccessor.BreakBlock(pos, b.Player);
                b.Blocks++;
                Record(b, "broke", at, 0, energy);
                double v = Ballistics.SpeedFor(b.D, b.Mass, energy - cost), f = v / Math.Max(1e-9, b.S.Speed);
                var s = b.S;
                s.VX *= f; s.VY *= f; s.VZ *= f;
                b.S = s;
                Going(b);
                return;
            }
            CannonEffects.Dust(sapi.World, pos, at);
            CannonEffects.Thud(sapi.World, at);
            Record(b, "block", at, 0, energy);
            Stop(b, null, at);
        }

        /// <summary>What a ball may break: nothing harder than MaxResistance, and no stone, ore, metal or the like.</summary>
        public static bool Soft(Block block, CannonDef d)
        {
            if (block == null || block.Id == 0 || block.Resistance > d.MaxResistance) return false;
            switch (block.BlockMaterial)
            {
                case EnumBlockMaterial.Stone:
                case EnumBlockMaterial.Ore:
                case EnumBlockMaterial.Metal:
                case EnumBlockMaterial.Mantle:
                case EnumBlockMaterial.Brick:
                case EnumBlockMaterial.Ceramic:
                case EnumBlockMaterial.Meta:
                case EnumBlockMaterial.Water:
                case EnumBlockMaterial.Lava:
                    return false;
            }
            return true;
        }

        /// <summary>Land claims: the one who fired it must be allowed to break blocks there; a shot fired by no one breaks none in a claim.</summary>
        bool MayBreak(Ball b, BlockPos pos)
        {
            var claims = sapi.World.Claims;
            if (b.Player != null) return claims.TestAccess(b.Player, pos, EnumBlockAccessFlags.BuildOrBreak) == EnumWorldAccessResponse.Granted;
            var here = claims.Get(pos);
            return here == null || here.Length == 0;
        }

        void HitWater(Ball b, Vec3d at)
        {
            var s = b.S;
            if (Ballistics.Ricochets(b.D, s))
            {
                Ballistics.Ricochet(b.D, ref s);
                b.S = s;
                CannonEffects.Splash(sapi.World, at, 0.6);
                Record(b, "ricochet", at, 0, b.Energy);
                Going(b);
                return;
            }
            Ballistics.EnterWater(b.D, ref s);
            b.S = s;
            CannonEffects.Splash(sapi.World, at, 1);
            Record(b, "water", at, 0, b.Energy);
            if (b.S.Speed < b.D.Spent) { Stop(b, "spent", at); return; }
            Going(b);
        }

        /// <summary>The first creature or player (not the one who fired it, in its first blocks; none it went through already) the stretch a + t·seg meets: t, or MaxValue.</summary>
        double FirstCreature(Ball b, Vec3d a, Vec3d seg, double len, out Entity found)
        {
            found = null;
            double best = double.MaxValue;
            var mid = a.AddCopy(seg.X / 2, seg.Y / 2, seg.Z / 2);
            float r = (float)(len / 2 + 2.5);
            var near = sapi.World.GetEntitiesAround(mid, r, r, e => e is EntityAgent && e.Alive);
            if (near == null) return best;
            const double ball = 0.07;
            foreach (var e in near)
            {
                if (b.Through.Contains(e.EntityId) || e == b.Firer && b.Travel < b.D.SelfSafe) continue;
                var box = e.SelectionBox ?? e.CollisionBox;
                if (box == null) continue;
                double t = Ballistics.RayBox(a, seg, e.Pos.X + box.X1 - ball, e.Pos.Y + box.Y1 - ball, e.Pos.Z + box.Z1 - ball,
                    e.Pos.X + box.X2 + ball, e.Pos.Y + box.Y2 + ball, e.Pos.Z + box.Z2 + ball);
                if (t < 0 || t > 1 || t >= best) continue;
                best = t;
                found = e;
            }
            return best;
        }

        /// <summary>
        /// The first block with collision the stretch a + t·seg (t 0..1) meets (its boxes, not just its cell): t, or MaxValue.
        /// The cells are walked in the order the stretch enters them, so the first one met is the nearest.
        /// </summary>
        double FirstBlock(Ball b, Vec3d a, Vec3d seg, out BlockPos found, out Block type)
        {
            found = null;
            type = null;
            var ba = sapi.World.BlockAccessor;
            int dim = Dim(b);
            int x = (int)Math.Floor(a.X), y = (int)Math.Floor(a.Y), z = (int)Math.Floor(a.Z);
            int ex = (int)Math.Floor(a.X + seg.X), ey = (int)Math.Floor(a.Y + seg.Y), ez = (int)Math.Floor(a.Z + seg.Z);
            int sx = Math.Sign(seg.X), sy = Math.Sign(seg.Y), sz = Math.Sign(seg.Z);
            double tdx = sx != 0 ? Math.Abs(1 / seg.X) : double.MaxValue, tdy = sy != 0 ? Math.Abs(1 / seg.Y) : double.MaxValue, tdz = sz != 0 ? Math.Abs(1 / seg.Z) : double.MaxValue;
            double tmx = sx > 0 ? (x + 1 - a.X) / seg.X : sx < 0 ? (a.X - x) / -seg.X : double.MaxValue;
            double tmy = sy > 0 ? (y + 1 - a.Y) / seg.Y : sy < 0 ? (a.Y - y) / -seg.Y : double.MaxValue;
            double tmz = sz > 0 ? (z + 1 - a.Z) / seg.Z : sz < 0 ? (a.Z - z) / -seg.Z : double.MaxValue;
            var bp = new BlockPos(dim);
            for (int guard = 0; guard < 64; guard++)
            {
                bp.Set(x, y, z);
                var block = ba.GetBlock(bp, BlockLayersAccess.Solid);
                if (block != null && block.Id != 0)
                {
                    var boxes = block.GetCollisionBoxes(ba, bp);
                    if (boxes != null)
                    {
                        double best = double.MaxValue;
                        foreach (var c in boxes)
                        {
                            double t = Ballistics.RayBox(a, seg, x + c.X1, y + c.Y1, z + c.Z1, x + c.X2, y + c.Y2, z + c.Z2);
                            if (t >= 0 && t <= 1 && t < best) best = t;
                        }
                        if (best <= 1)
                        {
                            found = bp.Copy();
                            type = block;
                            return best;
                        }
                    }
                }
                if (x == ex && y == ey && z == ez) break;
                if (tmx < tmy && tmx < tmz) { if (tmx > 1) break; x += sx; tmx += tdx; }
                else if (tmy < tmz) { if (tmy > 1) break; y += sy; tmy += tdy; }
                else { if (tmz > 1) break; z += sz; tmz += tdz; }
            }
            return double.MaxValue;
        }

        // ------------------------------------------------------------------ telling clients

        void Going(Ball b)
        {
            var s = b.S;
            mod.SendShot(new ShotPacket
            {
                Id = b.Id, Kind = ShotPacket.Going, X = s.X, Y = s.Y, Z = s.Z, VX = (float)s.VX, VY = (float)s.VY, VZ = (float)s.VZ, InWater = s.InWater
            }, s.Pos);
        }

        void Stop(Ball b, string what, Vec3d at)
        {
            if (b.Done) return;
            b.Done = true;
            if (what != null) Record(b, what, at, 0, b.Energy);
            mod.SendShot(new ShotPacket { Id = b.Id, Kind = ShotPacket.Stopped, X = at.X, Y = at.Y, Z = at.Z }, at);
        }

        void Record(Ball b, string what, Vec3d at, long entity, double energy, DamageResult res = null)
        {
            Records.Add(new ShotRecord { Id = b.Id, What = what, At = at.Clone(), Entity = entity, Energy = energy, Damage = res, Ms = sapi.World.ElapsedMilliseconds });
            if (Records.Count > 400) Records.RemoveRange(0, Records.Count - 400);
        }
    }

    /// <summary>
    /// What a cannon looks and sounds like, from the server: vanilla particles (sent to every client that has the chunk)
    /// and vanilla sounds. The flash of light and the gun running back are the clients' own (<see cref="ShotPacket"/>).
    /// </summary>
    public static class CannonEffects
    {
        /// <summary>
        /// Fired: a spurt of flame from the muzzle, and a thick white-grey cloud blown out along the barrel that grows, drifts
        /// off with the wind and thins out. (0.3.25) Its report is the clients' own, played as the shot's packet comes
        /// (CannonClient.Report): not from here, so it isn't heard twice.
        /// </summary>
        public static void Fired(IWorldAccessor world, Vec3d at, Vec3d dir, Vec3d wind, CannonDef d = null)
        {
            d ??= new CannonDef();
            var flame = new SimpleParticleProperties(10, 16, ColorUtil.ToRgba(255, 255, 190, 70), at.AddCopy(-0.1, -0.1, -0.1), at.AddCopy(0.1, 0.1, 0.1),
                new Vec3f((float)(dir.X * 8 - 1.5), (float)(dir.Y * 8 - 1.5), (float)(dir.Z * 8 - 1.5)),
                new Vec3f((float)(dir.X * 16 + 1.5), (float)(dir.Y * 16 + 1.5), (float)(dir.Z * 16 + 1.5)), 0.15f, 0f, 0.4f, 1.0f, EnumParticleModel.Cube);
            flame.VertexFlags = 255;
            flame.WithTerrainCollision = false;
            flame.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.4f);
            world.SpawnParticles(flame);
            // the smoke, in two puffs: a dense one out of the muzzle, a wider one hanging round the gun
            for (int i = 0; i < 2; i++)
            {
                double push = i == 0 ? 6 : 1.5, spread = i == 0 ? 1.2 : 2.0;
                var smoke = new SimpleParticleProperties(i == 0 ? 30 : 20, i == 0 ? 40 : 28, ColorUtil.ToRgba(i == 0 ? 200 : 150, 222, 220, 214),
                    at.AddCopy(-0.4, -0.3, -0.4), at.AddCopy(0.4, 0.3, 0.4),
                    new Vec3f((float)(dir.X * push + wind.X - spread), (float)(dir.Y * push + 0.2), (float)(dir.Z * push + wind.Z - spread)),
                    new Vec3f((float)(dir.X * push * 1.6 + wind.X + spread), (float)(dir.Y * push + 1.0), (float)(dir.Z * push * 1.6 + wind.Z + spread)),
                    i == 0 ? 5f : 7f, -0.01f, 3f, 6f, EnumParticleModel.Quad);
                smoke.WithTerrainCollision = false;
                smoke.SelfPropelled = true;
                smoke.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 10f);
                smoke.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -(i == 0 ? 200f : 150f));
                world.SpawnParticles(smoke);
            }
        }

        /// <summary>A ball into the water (or skipping off it): a column of spray and vanilla's splash sound.</summary>
        public static void Splash(IWorldAccessor world, Vec3d at, double strength)
        {
            world.PlaySoundAt(new AssetLocation(strength >= 1 ? "sounds/environment/largesplash" : "sounds/environment/mediumsplash"), at.X, at.Y, at.Z, null, true, 64,
                (float)Math.Min(1, 0.6 + 0.4 * strength));
            for (int i = 0; i < 3; i++)
            {
                var splash = new WaterSplashParticles();
                splash.BasePos.Set(at.X - 0.5, at.Y - 0.2, at.Z - 0.5);
                splash.AddPos.Set(1, 0.3, 1);
                splash.AddVelocity.Set(0, (float)((3 + 3 * i) * strength), 0);
                splash.QuantityMul = (float)(2 * strength);
                world.SpawnParticles(splash);
            }
            var spray = new SimpleParticleProperties(20, 30, ColorUtil.ToRgba(170, 235, 240, 245), at.AddCopy(-0.4, 0, -0.4), at.AddCopy(0.4, 0.2, 0.4),
                new Vec3f(-1.5f, (float)(5 * strength), -1.5f), new Vec3f(1.5f, (float)(10 * strength), 1.5f), 1.4f, 1f, 1.0f, 2.2f, EnumParticleModel.Quad);
            spray.WithTerrainCollision = false;
            spray.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 2f);
            spray.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -170f);
            world.SpawnParticles(spray);
        }

        /// <summary>A ball into a ship's side: oak splinters thrown back out, a little dust.</summary>
        public static void Splinters(IWorldAccessor world, Vec3d at, Vec3d dir)
        {
            var wood = new SimpleParticleProperties(16, 26, ColorUtil.ToRgba(255, 150, 112, 70), at.AddCopy(-0.2, -0.2, -0.2), at.AddCopy(0.2, 0.2, 0.2),
                new Vec3f((float)(-dir.X * 3 - 2), 1f, (float)(-dir.Z * 3 - 2)), new Vec3f((float)(-dir.X * 6 + 2), 5f, (float)(-dir.Z * 6 + 2)),
                1.6f, 1f, 0.3f, 0.8f, EnumParticleModel.Cube);
            world.SpawnParticles(wood);
            var dust = new SimpleParticleProperties(6, 10, ColorUtil.ToRgba(110, 150, 130, 105), at.AddCopy(-0.3, -0.3, -0.3), at.AddCopy(0.3, 0.3, 0.3),
                new Vec3f(-0.6f, 0.1f, -0.6f), new Vec3f(0.6f, 0.8f, 0.6f), 2.5f, -0.01f, 1.5f, 3f, EnumParticleModel.Quad);
            dust.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 3f);
            dust.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -110f);
            world.SpawnParticles(dust);
        }

        /// <summary>A ball into a block: a puff of that block's own dust.</summary>
        public static void Dust(IWorldAccessor world, BlockPos pos, Vec3d at)
        {
            world.SpawnCubeParticles(pos, at, 0.6f, 24, 0.7f);
        }

        public static void Thud(IWorldAccessor world, Vec3d at)
        {
            world.PlaySoundAt(new AssetLocation("sounds/thud"), at.X, at.Y, at.Z, null, true, 48, 1f);
        }
    }
}
