using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>Creatures keep vanilla AI and movement. Their physics result is resolved on the moving deck before it can fall through.</summary>
    public sealed class DeckCreatures
    {
        public const string Key="hansaCreatureDeck";
        sealed class Held
        {
            public EntityAgent Entity;
            public EntityShip Ship;
            public CreatureDeckMotion Motion;
            public float ShipYaw;
        }
        static readonly ConcurrentDictionary<long,Held> held=new();
        static readonly Dictionary<long,EntityAgent> client=new();
        static EntityShip[] ships=Array.Empty<EntityShip>();
        internal static readonly ConcurrentDictionary<long,string> TestTrace=new();
        readonly ICoreServerAPI api;
        readonly HansaMod mod;
        long nextRestore;
        public DeckCreatures(ICoreServerAPI api,HansaMod mod)
        {
            this.api=api;this.mod=mod;held.Clear();client.Clear();
            api.Event.RegisterGameTickListener(Tick,20);
        }
        static bool Eligible(Entity e) => e is EntityAgent and not EntityPlayer &&
            e.Alive && ((EntityAgent)e).MountedOn==null;
        public static bool IsAboard(Entity e,EntityShip ship) => e!=null && held.TryGetValue(e.EntityId,out var h) && h.Ship==ship;
        /// <summary>The ship a creature stands aboard, or null.</summary>
        public static EntityShip ShipOf(Entity e) => e!=null && held.TryGetValue(e.EntityId,out var h) ? h.Ship : null;
        static CreatureDeckMotion Motion(Entity e,Vec3d at,EntityShip ship)
        {
            var b=e.CollisionBox;
            return CreatureDeckMotion.Of(at.X,at.Y,at.Z,Math.Max(b?.XSize??.6,b?.ZSize??.6),b?.YSize??1,LocalYaw(e,ship));
        }
        /// <summary>The creature's heading in her frame.</summary>
        internal static double LocalYaw(Entity e,EntityShip ship)
        {
            var d=ship.WorldDirToLocal(Math.Sin(e.Pos.Yaw),0,Math.Cos(e.Pos.Yaw));
            return Math.Atan2(d.X,d.Z);
        }
        /// <summary>(1 November 2026, the flute) Creatures put on her deck (ship-local feet), taken aboard there by their next physics tick.</summary>
        static readonly ConcurrentDictionary<long,(EntityShip Ship,Vec3d At,long Until)> expected=new();
        public static void Expect(Entity e,EntityShip ship,Vec3d local) => expected[e.EntityId]=(ship,local,e.World.ElapsedMilliseconds+3000);
        public static void AfterPhysics(Entity e,Vec3d before)
        {
            if(e!=null && TestTrace.ContainsKey(e.EntityId)) TestTrace[e.EntityId]=$"physics: {e.State}, eligible {Eligible(e)}, ships {ships.Length}, motion {e.Pos.Motion}";
            if(e?.World?.Side!=EnumAppSide.Server || !Eligible(e) || before==null) return;
            if(!held.TryGetValue(e.EntityId,out var h))
            {
                if(expected.TryRemove(e.EntityId,out var put) && Usable(put.Ship) && e.World.ElapsedMilliseconds<put.Until)
                {
                    var motion=Motion(e,put.At,put.Ship);
                    if(motion.Catch(put.Ship.Grid,LocalYaw(e,put.Ship)))
                    {
                        motion.Move(put.Ship.Grid,0,-.05,0);
                        h=new Held {Entity=(EntityAgent)e,Ship=put.Ship,Motion=motion,ShipYaw=put.Ship.Pos.Yaw};
                        held[e.EntityId]=h;Place(h);return;
                    }
                }
                foreach(var ship in ships)
                {
                    if(!Usable(ship) || ship.Pos.Dimension!=e.Pos.Dimension || e.Pos.SquareDistanceTo(ship.Pos)>1600) continue;
                    var start=ship.WorldToLocal(before.X,before.Y,before.Z);
                    var end=ship.WorldToLocal(e.Pos.X,e.Pos.Y,e.Pos.Z);
                    var motion=Motion(e,start,ship);
                    if(TestTrace.ContainsKey(e.EntityId)) TestTrace[e.EntityId]+=$"; {ship.EntityId}: {start} -> {end}, stuck {motion.Stuck(ship.Grid)}, over {motion.Over(ship.Grid)}, size {motion.HalfX}/{motion.Height}/{motion.HalfZ}";
                    // (1 November 2026) stepping in at her gate: its body turned or squared to the gap, eased into it, or
                    // hopped up onto her sill when a swell has lifted her above the quay
                    if(!motion.Catch(ship.Grid,LocalYaw(e,ship),e.OnGround && !e.Swimming && !e.FeetInLiquid ? CreatureDeckMotion.Hop : 0,ship.Def.DeckY)) continue;
                    double lift=motion.Y-start.Y;
                    motion.Move(ship.Grid,end.X-start.X,end.Y-start.Y,end.Z-start.Z);
                    if(!motion.OnGround) continue;
                    if(TestTrace.ContainsKey(e.EntityId)) TestTrace[e.EntityId]+=$", caught (lift {lift:0.###})";
                    h=new Held {Entity=(EntityAgent)e,Ship=ship,Motion=motion,ShipYaw=ship.Pos.Yaw};
                    held[e.EntityId]=h;Place(h);return;
                }
                return;
            }
            lock(h)
            {
                if(!Usable(h.Ship) || e.Pos.Dimension!=h.Ship.Pos.Dimension || e.Pos.SquareDistanceTo(before)>36) {Release(h);return;}
                var d=h.Ship.WorldDirToLocal(e.Pos.X-before.X,e.Pos.Y-before.Y,e.Pos.Z-before.Z);
                h.Motion.Turn(h.Ship.Grid,LocalYaw(e,h.Ship));
                if(!h.Motion.Move(h.Ship.Grid,d.X,d.Y,d.Z)) {Release(h);return;}
                Place(h);
            }
        }
        static bool Usable(EntityShip ship) => ship?.Grid!=null && ship.Alive && !ship.GoneDown && ship.State!=EnumEntityState.Despawned;
        void Tick(float dt)
        {
            ships=mod.ServerShips.Where(Usable).ToArray();
            // Loaded creatures reconnect to the saved ship when its chunk is available.
            if(api.World.ElapsedMilliseconds>=nextRestore)
            {
            nextRestore=api.World.ElapsedMilliseconds+1000;
            foreach(var ship in ships)
                foreach(var entity in api.World.GetEntitiesAround(ship.Pos.XYZ,35,35,e=>Eligible(e) && !held.ContainsKey(e.EntityId)))
                    if(entity.WatchedAttributes[Key] is ITreeAttribute t && t.GetLong("ship")==ship.EntityId)
                    {
                        var m=Motion(entity,new Vec3d(t.GetDouble("x"),t.GetDouble("y"),t.GetDouble("z")),ship);
                        if(!m.Stuck(ship.Grid) && m.Over(ship.Grid)) held[entity.EntityId]=new Held {Entity=(EntityAgent)entity,Ship=ship,Motion=m,ShipYaw=ship.Pos.Yaw};
                        else entity.WatchedAttributes.RemoveAttribute(Key);
                    }
            }
            foreach(var h in held.Values)
                lock(h)
                {
                    if(!Eligible(h.Entity) || !Usable(h.Ship) || h.Entity.State==EnumEntityState.Despawned) {Release(h);continue;}
                    Place(h);
                }
        }
        static void Place(Held h)
        {
            var e=h.Entity;var m=h.Motion;
            h.Ship.LocalToWorld(m.X,m.Y,m.Z,out double x,out double y,out double z);
            e.Pos.SetPos(x,y,z);e.OnGround=e.CollidedVertically=m.OnGround;e.FeetInLiquid=e.Swimming=false;
            float turn=h.Ship.Pos.Yaw-h.ShipYaw;e.Pos.Yaw+=turn;e.BodyYaw+=turn;h.ShipYaw=h.Ship.Pos.Yaw;
            if(m.OnGround) {e.Pos.Motion.Y=0;e.PositionBeforeFalling.Set(x,y,z);}
            var old=e.WatchedAttributes.GetTreeAttribute(Key);
            if(old!=null && old.GetLong("ship")==h.Ship.EntityId && Math.Abs(old.GetDouble("x")-m.X)+Math.Abs(old.GetDouble("y")-m.Y)+Math.Abs(old.GetDouble("z")-m.Z)<.001) return;
            var t=new TreeAttribute();t.SetLong("ship",h.Ship.EntityId);t.SetDouble("x",m.X);t.SetDouble("y",m.Y);t.SetDouble("z",m.Z);
            t.SetBool("ground",m.OnGround);e.WatchedAttributes[Key]=t;e.WatchedAttributes.MarkPathDirty(Key);
        }
        static void Release(Held h)
        {
            held.TryRemove(h.Entity.EntityId,out _);h.Entity.WatchedAttributes.RemoveAttribute(Key);h.Entity.WatchedAttributes.MarkPathDirty(Key);
        }
        public static void ClientRemote(Entity e)
        {
            if(e is EntityAgent agent && Eligible(e) && e.WatchedAttributes[Key] is ITreeAttribute) client[e.EntityId]=agent;
        }
        static readonly HarmonyLib.AccessTools.FieldRef<Vintagestory.GameContent.EntityShapeRenderer,double> prevY=
            HarmonyLib.AccessTools.FieldRefAccess<Vintagestory.GameContent.EntityShapeRenderer,double>("prevY");
        static readonly Dictionary<long,double> heave=new();
        /// <summary>
        /// (1 November 2026, the user's elk tied in the hold: "it rock back and forth like this (storm)") Client, prefix of
        /// EntityShapeRenderer.updateStepPitch: vanilla pitches a walking creature up or down as it climbs or drops, from how
        /// its height changed; aboard, her heave and roll change it too, and the elk reared and pitched with every swell.
        /// Her part of that change (its height in the world less its height on her deck) is taken out of the height vanilla
        /// compares with, so it pitches only for a real step on her deck.
        /// </summary>
        public static void StepPitchBefore(Vintagestory.GameContent.EntityShapeRenderer __instance)
        {
            var e=__instance.entity;
            if(e==null) return;
            if(e.WatchedAttributes[Key] is not ITreeAttribute t) {heave.Remove(e.EntityId);return;}
            double now=e.Pos.Y-t.GetDouble("y");
            if(heave.TryGetValue(e.EntityId,out double last)) prevY(__instance)+=now-last;
            heave[e.EntityId]=now;
        }
        public static void ClientFrame(ICoreClientAPI api,ShipRenderer renderer,EntityPlayer player)
        {
            foreach(var e in client.Values.ToArray())
            {
                if(e.State==EnumEntityState.Despawned || e.WatchedAttributes[Key] is not ITreeAttribute t) {client.Remove(e.EntityId);heave.Remove(e.EntityId);continue;}
                if(api.World.GetEntityById(t.GetLong("ship")) is not EntityShip ship || ship.Def==null) continue;
                var origin=renderer.RenderOrigin(ship,player);
                ShipFrame.ToWorld(ship.Pos.Yaw,ship.Pitch,ship.Roll,t.GetDouble("x"),t.GetDouble("y"),t.GetDouble("z"),out double x,out double y,out double z);
                e.Pos.SetPos(origin.X+x,origin.Y+y,origin.Z+z);e.OnGround=t.GetBool("ground");e.FeetInLiquid=e.Swimming=false;
            }
        }
    }
}
