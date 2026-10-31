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
        static CreatureDeckMotion Motion(Entity e,Vec3d at)
        {
            var b=e.CollisionBox;
            return new CreatureDeckMotion(at.X,at.Y,at.Z,Math.Max(.05,b?.XSize/2??.3),Math.Max(.1,b?.YSize??1),Math.Max(.05,b?.ZSize/2??.3));
        }
        public static void AfterPhysics(Entity e,Vec3d before)
        {
            if(e!=null && TestTrace.ContainsKey(e.EntityId)) TestTrace[e.EntityId]=$"physics: {e.State}, eligible {Eligible(e)}, ships {ships.Length}, motion {e.Pos.Motion}";
            if(e?.World?.Side!=EnumAppSide.Server || !Eligible(e) || before==null) return;
            if(!held.TryGetValue(e.EntityId,out var h))
            {
                foreach(var ship in ships)
                {
                    if(!Usable(ship) || ship.Pos.Dimension!=e.Pos.Dimension || e.Pos.SquareDistanceTo(ship.Pos)>1600) continue;
                    var start=ship.WorldToLocal(before.X,before.Y,before.Z);
                    var end=ship.WorldToLocal(e.Pos.X,e.Pos.Y,e.Pos.Z);
                    var motion=Motion(e,start);
                    if(TestTrace.ContainsKey(e.EntityId)) TestTrace[e.EntityId]+=$"; {ship.EntityId}: {start} -> {end}, stuck {motion.Stuck(ship.Grid)}, over {motion.Over(ship.Grid)}, size {motion.HalfX}/{motion.Height}/{motion.HalfZ}";
                    if(motion.Stuck(ship.Grid) || !motion.Over(ship.Grid)) continue;
                    motion.Move(ship.Grid,end.X-start.X,end.Y-start.Y,end.Z-start.Z);
                    if(!motion.OnGround) continue;
                    h=new Held {Entity=(EntityAgent)e,Ship=ship,Motion=motion,ShipYaw=ship.Pos.Yaw};
                    held[e.EntityId]=h;Place(h);return;
                }
                return;
            }
            lock(h)
            {
                if(!Usable(h.Ship) || e.Pos.Dimension!=h.Ship.Pos.Dimension || e.Pos.SquareDistanceTo(before)>36) {Release(h);return;}
                var d=h.Ship.WorldDirToLocal(e.Pos.X-before.X,e.Pos.Y-before.Y,e.Pos.Z-before.Z);
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
                        var m=Motion(entity,new Vec3d(t.GetDouble("x"),t.GetDouble("y"),t.GetDouble("z")));
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
        public static void ClientFrame(ICoreClientAPI api,ShipRenderer renderer,EntityPlayer player)
        {
            foreach(var e in client.Values.ToArray())
            {
                if(e.State==EnumEntityState.Despawned || e.WatchedAttributes[Key] is not ITreeAttribute t) {client.Remove(e.EntityId);continue;}
                if(api.World.GetEntityById(t.GetLong("ship")) is not EntityShip ship || ship.Def==null) continue;
                var origin=renderer.RenderOrigin(ship,player);
                ShipFrame.ToWorld(ship.Pos.Yaw,ship.Pitch,ship.Roll,t.GetDouble("x"),t.GetDouble("y"),t.GetDouble("z"),out double x,out double y,out double z);
                e.Pos.SetPos(origin.X+x,origin.Y+y,origin.Z+z);e.OnGround=t.GetBool("ground");e.FeetInLiquid=e.Swimming=false;
            }
        }
    }
}
