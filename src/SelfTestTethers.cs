using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Hansa
{
    // No connected player/session is created. Only the mount reference is supplied by the stand-in;
    // the rope item, ownership check, cloth manager and inventory transactions below are the game's.
    public sealed class TetherTestEntity : EntityPlayer
    {
        public void OnDeck(DeckSeat seat) => MountedOn=seat;
    }

    // Keep native player-slot suitability, transfers and rope inventory callbacks. Only the
    // connected player's stat refresh is absent from this connection-free stand-in.
    public sealed class TetherTestHotbar : InventoryPlayerHotbar
    {
        public TetherTestHotbar(string uid,ICoreAPI api) : base("hotbar",uid,api) { }
        public override void updateSlotStatMods(ItemSlot slot) { }
    }

    public partial class SelfTest
    {
        void TethersAboard(EntityShip ship)
        {
            ShipPiece post=null;EntityAgent elk=null;ClothSystem lead=null;TetherTestEntity ep=null;
            var cloth=sapi.ModLoader.GetModSystem<ClothManager>();
            var original=new EntityPos().SetFrom(ship.Pos);
            try {
                var fence=sapi.World.GetBlock(new AssetLocation("game:woodenfence-oak-ew-free"));
                var mount=ship.Mounts.Get("f:hold:2:-1");
                var plan=PiecePlanner.Plan(ship.Mounts,mount,new VanillaPieceBlock(sapi.World,new ItemStack(fence),false),0);
                string error=ship.PlacePiece(plan,new ItemStack(fence),true,out post);
                Check(error==null && ShipTethers.IsPost(post),"155 rope flow: reported native fence places at f:hold:2:-1 ("+(error??"ok")+")");
                if(post==null) return;
                var at=ShipTethers.PostPosition(post);
                ep=new TetherTestEntity {Api=sapi,World=sapi.World,EntityId=-155};ep.LocalEyePos.Set(0,1.62,0);
                // Reuse the existing connection-free native server-player/inventory stand-in.
                var player=new HearthTestPlayer((ServerMain)sapi.World,ep);
                ep.WatchedAttributes.SetString("playerUID",player.PlayerUID);
                // RegisterCloth restores references through GetEntityById. Publish the stand-in only for
                // this synchronous check; it never enters a game tick, spawn pipeline or player save.
                if(!sapi.World.LoadedEntities.TryAdd(ep.EntityId,ep)) throw new Exception("tether stand-in entity id is already in use");
                var inventory=player.InventoryManager;
                var hotbar=new TetherTestHotbar(player.PlayerUID,sapi);
                ((HearthTestInventories)inventory).Inventories[hotbar.InventoryID]=hotbar;hotbar.Open(player);
                inventory.ActiveHotbarSlotNumber=0;
                var slot=inventory.ActiveHotbarSlot;
                if(slot==null) throw new Exception($"tether stand-in has no active hotbar slot (hotbar {hotbar.InventoryID}, inventories {string.Join(",",inventory.Inventories.Keys)})");
                var seat=new DeckSeat(ship,player.PlayerUID,at[0]-1.5,mount.Y,at[2]);ep.OnDeck(seat);
                void Stand() {
                    var pos=seat.SeatPosition;ep.Pos.SetPos(pos.X,pos.Y,pos.Z);
                }
                Stand();
                var type=sapi.World.GetEntityType(new AssetLocation("game:tameddeer-elk-male-adult"));
                elk=sapi.World.ClassRegistry.CreateEntity(type) as EntityAgent;
                // The elk is 1.3 m wide. The old side position overlapped the hold's solid
                // hull/ribs, so native deck acquisition refused it and it fell below the ship.
                ship.LocalToWorld(at[0]-1.5,mount.Y+.05,at[2],out double x,out double y,out double z);
                elk.Pos.SetPos(x,y,z);sapi.World.SpawnEntity(elk);
                var owner=new TreeAttribute();owner.SetString("uid",player.PlayerUID);elk.WatchedAttributes["ownedby"]=owner;
                // Drive the native physics as the normal creature tick does (no client activates AI here).
                elk.State=EnumEntityState.Active;
                var physics=elk.GetBehavior<EntityBehaviorControlledPhysics>();
                DeckCreatures.TestTrace[elk.EntityId]="no controlled-physics tick";
                for(int i=0;i<60;i++) physics?.OnPhysicsTick(.02f);
                var elkLocal=ship.WorldToLocal(elk.Pos.X,elk.Pos.Y,elk.Pos.Z);
                Check(physics!=null && DeckCreatures.IsAboard(elk,ship) && elk.OnGround,
                    $"155 rope flow: native elk physics boards the deck before leading (local {elkLocal}, post {string.Join(",",at)}, floor {mount.Y}, ground {elk.OnGround}; {DeckCreatures.TestTrace[elk.EntityId]})");
                seat.SetLocal(elkLocal.X-.1,elkLocal.Y,elkLocal.Z,DeckSeat.FlagOnGround);Stand();
                slot.Itemstack=new ItemStack(sapi.World.GetItem(new AssetLocation("game:rope")));
                var item=(ItemRope)slot.Itemstack.Collectible;
                var handling=EnumHandHandling.NotHandled;
                owner.SetString("uid","another-owner");
                item.OnHeldUseStart(slot,ep,null,new EntitySelection {Entity=elk,Position=elk.Pos.XYZ},EnumHandInteract.HeldItemInteract,true,ref handling);
                Check(slot.StackSize==1 && slot.Itemstack.Attributes.GetInt("clothId")==0,
                    "155 rope flow: native ownership rejection still prevents leading someone else's elk");
                owner.SetString("uid",player.PlayerUID);
                item.OnHeldUseStart(slot,ep,null,new EntitySelection {Entity=elk,Position=elk.Pos.XYZ},EnumHandInteract.HeldItemInteract,true,ref handling);
                lead=cloth.GetClothSystem(slot.Itemstack?.Attributes.GetInt("clothId")??0);
                Check(lead!=null && lead.Active && lead.FirstPoint.PinnedToEntity==ep && lead.LastPoint.PinnedToEntity==elk &&
                    elk.GetBehavior<EntityBehaviorRopeTieable>().ClothIds.value.Contains(lead.ClothId),
                    "155 rope flow: mounted player OnHeldUseStart creates a registered active native player/elk lead and the elk records its cloth id");
                if(lead==null) return;
                int pointCount=0;lead.WalkPoints(_=>pointCount++);
                Check(pointCount>=4 && ep.Pos.DistanceTo(elk.Pos.XYZ)<.5,
                    "155 rope flow: overlapping mounted player/elk still produces distinct cloth endpoints and renderable segments");
                var hand=lead.FirstPoint;var animal=lead.LastPoint;
                hand.extension=1;hand.TensionDirection.Set(100,10,100);var motion=ship.Pos.Motion.Clone();
                hand.update(.02f,sapi.World);
                Check(ship.Pos.Motion.DistanceTo(motion)<1e-10 && lead.CanPull && hand.pinnedToOffset!=null &&
                    Math.Abs(hand.Pos.Y-(ep.Pos.Y+ep.LocalEyePos.Y-.3))<.15,
                    "155 rope flow: hand stays by mounted player's eyes and tension cannot pull the ship; native pull setting is restored");
                hand.extension=0;
                seat.SetLocal(at[0]-1.5,mount.Y,at[2],DeckSeat.FlagOnGround);Stand();
                for(int i=0;i<100;i++) lead.updateFixedStep(.02f);
                bool finite=true;lead.WalkPoints(p=>finite &= double.IsFinite(p.Pos.X+p.Pos.Y+p.Pos.Z));
                Check(finite && hand.Pos.DistanceTo(animal.Pos)>.1,"155 rope flow: native simulation retains finite, separated visible rope endpoints aboard");
                // This is the actual piece-use server path. There is deliberately no native ship selection
                // and the player's view points away: the piece id is validated by UsePiece, not re-picked.
                ep.Pos.Pitch=0;ep.Pos.Yaw=0;
                ship.UsePiece(player,post.Id);
                Check(slot.Empty && hand.PinnedToEntity==ship && animal.PinnedToEntity==elk &&
                    ship.WatchedAttributes.GetTreeAttribute("hansaTethers")?.GetString(lead.ClothId.ToString())==ShipTethers.PostKey(post),
                    "155 rope flow: validated post click runs ItemRope, transfers only the held end and consumes its rope item");
                var saved=hand.pinnedToOffset.Clone();
                ClothPoint loose=null;lead.WalkPoints(p=> {if(!p.Pinned) loose=p;});
                var looseLocal=loose==null?null:ShipRopes.Pose(ship).Local(loose.Pos);
                bool elkAboard=DeckCreatures.IsAboard(elk,ship);
                ship.Pos.X+=2;ship.Pos.Z+=1;ship.Pos.Yaw+=.3f;Stand();
                typeof(DeckCreatures).GetMethod("Tick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ship.Mod.DeckCreatures,new object[]{.02f});
                ShipRopes.BeforeStep(lead);
                Check(looseLocal!=null && ShipRopes.Pose(ship).Local(loose.Pos).DistanceTo(looseLocal)<.0001,
                    $"155 rope flow: loose native cloth points share the moving deck frame before constraints run (local drift {looseLocal?.DistanceTo(ShipRopes.Pose(ship).Local(loose.Pos)):G6}, elk aboard {elkAboard}/{DeckCreatures.IsAboard(elk,ship)}, deck id {elk.WatchedAttributes.GetTreeAttribute(DeckCreatures.Key)?.GetLong("ship")}, ship {ship.EntityId})");
                hand.extension=2;hand.update(.02f,sapi.World);
                var postWorld=ShipRopes.Pose(ship).World(new Vec3d(at[0],at[1],at[2]));
                Check(hand.Pos.DistanceTo(postWorld)<.0001 && hand.pinnedToOffset.Equals(saved) && ship.Pos.Motion.DistanceTo(motion)<1e-10,
                    "155 rope flow: tied endpoint translates/turns with post, preserving saved local coordinates and ship velocity");
                {
                    var loaded=SerializerUtil.Deserialize<ClothSystem>(SerializerUtil.Serialize(lead));
                    loaded.Init(sapi,cloth);loaded.restoreReferences();loaded.FirstPoint.update(0,sapi.World);
                    Check(loaded.FirstPoint.PinnedToEntity==ship && loaded.LastPoint.PinnedToEntity==elk &&
                        loaded.FirstPoint.Pos.DistanceTo(postWorld)<.0001 && loaded.FirstPoint.pinnedToOffset.Equals(saved),
                        "155 rope flow: native cloth serialization restores ship/elk references and the post's local pin");
                }
                var synced=new ClothPoint(lead);synced.updateFromPoint(hand,sapi.World);synced.update(0,sapi.World);
                Check(synced.Pos.DistanceTo(postWorld)<.0001 && synced.pinnedToOffset.Equals(saved),
                    "155 rope flow: native point-packet update also keeps a ship-local pin at the visible post");
                hand.extension=0;
                ship.UsePiece(player,post.Id);
                var returned=inventory.Inventories.Values.SelectMany(inv=>inv).FirstOrDefault(s=>s.Itemstack?.Attributes.GetInt("clothId")==lead.ClothId);
                Check(returned!=null && hand.PinnedToEntity==ep && animal.PinnedToEntity==elk && !hand.NoAttachTransform &&
                    !ship.WatchedAttributes.GetTreeAttribute("hansaTethers").HasAttribute(lead.ClothId.ToString()),
                    $"155 rope flow: empty-hand post click gives the native lead item back and repins the player end, retaining the elk (returned {returned!=null}, hand {hand.PinnedToEntity?.EntityId}, player {ep.EntityId}, elk {animal.PinnedToEntity==elk}, no transform {hand.NoAttachTransform}, linked {ship.WatchedAttributes.GetTreeAttribute("hansaTethers").HasAttribute(lead.ClothId.ToString())})");
                hand.update(0,sapi.World);
                Check(hand.PinnedToEntity==ep && hand.Pos.DistanceTo(ep.Pos.XYZ)<2.5 && double.IsFinite(hand.Pos.X+hand.Pos.Y+hand.Pos.Z),
                    "155 rope flow: taken-back lead remains at the mounted player");
                // A second cycle catches stale holder ids and restores the tether for removal.
                if(returned!=null && returned!=slot) {slot.Itemstack=returned.TakeOutWhole();}
                ship.UsePiece(player,post.Id);
                Check(returned!=null && hand.PinnedToEntity==ship && slot.Empty && cloth.GetClothSystem(lead.ClothId)==lead,"155 rope flow: taking back and tying again consumes one lead without creating another cloth system");
                ship.RemovePiece(post,null);
                Check(!hand.Pinned && animal.PinnedToEntity==elk,"155 rope flow: removing the post frees its end without detaching the elk");
            }
            catch(Exception e) {Check(false,"155 rope flow: "+e);}
            finally {
                if(lead!=null) {elk?.GetBehavior<EntityBehaviorRopeTieable>()?.Detach(lead);cloth.UnregisterCloth(lead.ClothId);}
                if(post!=null && ship.Pieces.Get(post.Id)!=null) ship.RemovePiece(post,null);
                if(ep!=null && sapi.World.GetEntityById(ep.EntityId)==ep) sapi.World.LoadedEntities.TryRemove(ep.EntityId,out _);
                if(elk!=null) DeckCreatures.TestTrace.TryRemove(elk.EntityId,out _);
                elk?.Die(EnumDespawnReason.Removed);ship.Pos.SetFrom(original);
            }
        }
    }
}
