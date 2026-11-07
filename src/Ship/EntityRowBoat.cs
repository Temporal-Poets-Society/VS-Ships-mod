using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// Hansa's ship's boat since 0.3.3: vanilla's sailboat made a rowing boat (tools/shipgen/rowboat.py: no mast or
    /// rudder, a block shorter, a thwart in the middle with a pair of oars). Vanilla's <see cref="EntityBoat"/> does the
    /// rest: sitting in her and rowing (W/S, A/D), the places for chests, vessels, baskets and boat seats, Shift +
    /// right-click to pick her up. This adds the oars' stroke while she moves, keeps her out of ships' hulls, and holds
    /// a dry water cut under her (Sea). Sea also supplies the native boat's wave motion and moving seats.
    /// </summary>
    public class EntityRowBoat : EntityBoat
    {
        HansaMod mod;
        RowboatWaterClip clipDefinition;

        /// <summary>
        /// Her outline, to keep out of hulls: points round her waterline and her gunwale, in her own frame (the frame
        /// her collision boxes are given in: her length along z, the bow toward -z; turned by her yaw + pi).
        /// </summary>
        static readonly Vec3d[] Outline = BuildOutline();
        const double MidZ = -2.05, HalfLen = 3.75, HalfBeam = 1.35;

        static Vec3d[] BuildOutline()
        {
            var pts = new List<Vec3d>();
            foreach (double y in new[] { 0.35, 1.0 })
            {
                for (double t = -1; t <= 1.001; t += 0.25)
                {
                    double w = HalfBeam * Math.Sqrt(Math.Max(0, 1 - t * t * t * t));
                    pts.Add(new Vec3d(w, y, MidZ + t * HalfLen * 0.95));
                    pts.Add(new Vec3d(-w, y, MidZ + t * HalfLen * 0.95));
                }
                pts.Add(new Vec3d(0, y, MidZ - HalfLen));
                pts.Add(new Vec3d(0, y, MidZ + HalfLen));
            }
            return pts.ToArray();
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            mod = api.ModLoader.GetModSystem<HansaMod>();
            clipDefinition = api.Assets.Get(new AssetLocation("ships", "config/rowboat-waterclip.json")).ToObject<RowboatWaterClip>();
            if (clipDefinition?.DryProfile?.Length != WaterClipDef.DryStations * WaterClipDef.DryLevels)
                throw new InvalidOperationException("The rowing boat's fitted water outline is missing or invalid.");
            mod?.Client?.RowBoats.Add(this);
            // Without Sea nothing cuts the water out of her: vanilla's own way, as its sailboat does (her shape's hideWater
            // element drawn depth-only over the still water inside her).
            if (api.Side == EnumAppSide.Client && mod != null && mod.SeaLink == null)
            {
                hideWater = new EntityBehaviorHideWaterSurface(this);
                hideWater.Initialize(properties, new JsonObject(new JObject { ["hideWaterElement"] = "ORIGIN/hideWater/*" }));
                AddBehavior(hideWater);
            }
        }

        EntityBehaviorHideWaterSurface hideWater;

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            mod?.Client?.RowBoats.Remove(this);
            // (vanilla's behaviour leaves its renderer registered)
            if (hideWater != null && Api is ICoreClientAPI capi) capi.Event.UnregisterRenderer(hideWater, EnumRenderStage.OIT);
        }

        protected override void updateBoatAngleAndMotion(float dt)
        {
            float yawBefore = Pos.Yaw;
            base.updateBoatAngleAndMotion(dt);
            KeepOutOfShips(yawBefore);
        }

        public override void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            base.OnRenderFrame(dt, stage);
            // the oars sweep while she's rowed (or turned); at rest they lie out on their pins
            if (AnimManager?.Animator == null) return;
            bool rowing = Swimming && (Math.Abs(ForwardSpeed) > 0.004 || Math.Abs(AngularVelocity) > 0.004);
            bool on = AnimManager.IsAnimationActive("row");
            if (rowing && !on) AnimManager.StartAnimation("row");
            else if (!rowing && on) AnimManager.StopAnimation("row");
        }

        IEnumerable<EntityShip> Ships => Api?.Side == EnumAppSide.Server ? mod?.ServerShips : (IEnumerable<EntityShip>)mod?.Client?.Ships;

        /// <summary>Where a point of her own frame is in the world, with her at (x, y, z) turned to yaw.</summary>
        static Vec3d ToWorld(Vec3d p, double x, double y, double z, float yaw)
        {
            double a = yaw + Math.PI, c = Math.Cos(a), s = Math.Sin(a);
            // as vanilla turns her collision boxes (Matrixf.RotateY(yaw + pi))
            return new Vec3d(x + p.X * c + p.Z * s, y + p.Y, z - p.X * s + p.Z * c);
        }

        /// <summary>Would she, at (x, z) turned to yaw, be inside this ship's hull (below its main deck)?</summary>
        bool Touches(EntityShip ship, double x, double z, float yaw) => Touches(ship, x, Pos.Y, z, yaw);

        static bool Touches(EntityShip ship, double x, double y, double z, float yaw)
        {
            foreach (var p in Outline)
            {
                var w = ToWorld(p, x, y, z, yaw);
                var l = ship.WorldToLocal(w.X, w.Y, w.Z);
                if (l.Y < ship.Def.DeckY && ship.HullAt(l.X, l.Y, l.Z)) return true;
            }
            return false;
        }

        /// <summary>Would a boat put in the water at (x, y, z), her bow the way yaw looks, be inside a ship?</summary>
        public static bool TouchesAnyShip(IEnumerable<EntityShip> ships, double x, double y, double z, float yaw, int dimension)
        {
            foreach (var ship in ships)
            {
                if (ship?.Def?.DryMask == null || !ship.Alive || ship.Def.BoardFromWater || ship.Pos.Dimension != dimension) continue;
                double reach = ship.Def.Length / 2 + 8;
                if (ship.Pos.SquareDistanceTo(x, y, z) > reach * reach) continue;
                if (Touches(ship, x, y, z, yaw)) return true;
            }
            return false;
        }

        /// <summary>
        /// Ships' hulls stop her like a quay: her way on into one is taken off (she can still slide along it), a turn into
        /// one doesn't happen, and if a ship has come onto her she's moved out of it the shortest way.
        /// </summary>
        void KeepOutOfShips(float yawBefore)
        {
            var ships = Ships;
            if (ships == null) return;
            var pos = Pos;
            foreach (var ship in ships)
            {
                if (ship?.Def?.DryMask == null || !ship.Alive || ship.Def.BoardFromWater || ship.Pos.Dimension != pos.Dimension) continue;
                double reach = ship.Def.Length / 2 + 8;
                if (ship.Pos.SquareDistanceTo(pos.X, pos.Y, pos.Z) > reach * reach) continue;
                if (Touches(ship, pos.X, pos.Z, pos.Yaw))
                {
                    if (!Touches(ship, pos.X, pos.Z, yawBefore)) { pos.Yaw = yawBefore; AngularVelocity = 0; }
                    else { PushOut(ship); continue; }
                }
                double nx = pos.X + pos.Motion.X, nz = pos.Z + pos.Motion.Z;
                if (!Touches(ship, nx, nz, pos.Yaw)) continue;
                if (!Touches(ship, nx, pos.Z, pos.Yaw)) pos.Motion.Z = 0;
                else if (!Touches(ship, pos.X, nz, pos.Yaw)) pos.Motion.X = 0;
                else { pos.Motion.X = 0; pos.Motion.Z = 0; ForwardSpeed = 0; }
            }
        }

        void PushOut(EntityShip ship)
        {
            var pos = Pos;
            double best = double.MaxValue, bx = 0, bz = 0;
            foreach (var (dx, dz) in new[] { (0.0, 1.0), (0.0, -1.0), (1.0, 0.0), (-1.0, 0.0) })
            {
                var w = ship.LocalDirToWorld(dx, 0, dz);
                for (double d = 0.1; d < Math.Min(best, 12); d += 0.1)
                {
                    if (Touches(ship, pos.X + w.X * d, pos.Z + w.Z * d, pos.Yaw)) continue;
                    if (d < best) { best = d; bx = w.X * (d + 0.05); bz = w.Z * (d + 0.05); }
                    break;
                }
            }
            if (best == double.MaxValue) return;
            pos.X += bx;
            pos.Z += bz;
            double len = Math.Sqrt(bx * bx + bz * bz);
            double into = (pos.Motion.X * bx + pos.Motion.Z * bz) / len;
            if (into < 0) { pos.Motion.X -= into * bx / len; pos.Motion.Z -= into * bz / len; }
        }

        /// <summary>
        /// Is a player's feet at (x, y, z) in her: inside her outline (widened by margin: a swimmer alongside), from under
        /// her bottom (swimming) up to a jump over her gunwale?
        /// </summary>
        public bool Holds(double x, double y, double z, double margin)
        {
            double a = Pos.Yaw + Math.PI, c = Math.Cos(a), s = Math.Sin(a);
            double dx = x - Pos.X, dz = z - Pos.Z, dy = y - Pos.Y;
            double px = c * dx - s * dz, pz = s * dx + c * dz;
            double t = (pz - MidZ) / (HalfLen + margin);
            if (Math.Abs(t) > 1 || dy < -1.8 || dy > 1.8) return false;
            return Math.Abs(px) < HalfBeam * Math.Sqrt(Math.Max(0, 1 - t * t * t * t)) + margin;
        }

        /// <summary>The world point over her middle, h above her origin.</summary>
        public Vec3d Middle(double h) => ToWorld(new Vec3d(0, h, MidZ), Pos.X, Pos.Y, Pos.Z, Pos.Yaw);

        /// <summary>A seat for someone getting in: the rower's (it steers) if it's free, else any free one.</summary>
        public IMountableSeat FreeSeat()
        {
            var seats = GetBehavior<EntityBehaviorSeatable>()?.Seats;
            if (seats == null) return null;
            foreach (var st in seats) if (st.Passenger == null && st.CanControl) return st;
            foreach (var st in seats) if (st.Passenger == null) return st;
            return null;
        }

        /// <summary>Nobody in her and nothing on her: she can be picked up.</summary>
        public bool IsEmptyForPickup()
        {
            var seats = GetBehavior<EntityBehaviorSeatable>();
            var acc = GetBehavior<EntityBehaviorRideableAccessories>();
            return (seats == null || !seats.AnyMounted()) && (acc == null || acc.Inventory.Empty);
        }

        /// <summary>
        /// Her footprint for Sea: no water drawn inside her outline (a dry cut, as the ships have), from under her keel to
        /// over her gunwale, so the waves don't show inside (vanilla's boats hide only still water). The waves run on
        /// round her as they are. Sea's vanilla-boat integration supplies her heave and tilt; the dry outline follows
        /// that same pose, rather than holding the surrounding waves down to the old still-water level.
        /// </summary>
        public HullWaterClip WaterClip(Vec3d at)
        {
            var def = clipDefinition ?? throw new InvalidOperationException("The rowing boat is not initialized.");
            var clip = new HullWaterClip
            {
                X = at.X + Math.Cos(Pos.Yaw) * def.CentreRight,
                Z = at.Z - Math.Sin(Pos.Yaw) * def.CentreRight,
                WaterY = at.Y + def.WaterY, Yaw = Pos.Yaw,
                HalfLength = def.HalfLength, CentreOffset = def.CentreOffset, HalfWidth = def.HalfWidth,
                Profile = def.Profile, DryProfile = def.DryProfile,
                DryLevel0 = def.DryLevel0, DryLevelStep = def.DryLevelStep,
                Dry = true, DryDepth = def.DryDepth, DryHeight = def.DryHeight
            };
            mod?.SeaLink?.ApplyBoatPose(this, ref clip);
            return clip;
        }

        public class RowboatWaterClip
        {
            public float WaterY, CentreOffset, CentreRight, HalfLength, HalfWidth, DryDepth, DryHeight, DryLevel0, DryLevelStep;
            public float[] Profile, DryProfile;
        }
    }
}
