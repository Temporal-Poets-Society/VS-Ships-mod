using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Hansa
{
    // No connection or behavior initialization is needed; mounting and controls use EntityPlayer's real implementation.
    sealed class Notes157Player : EntityPlayer
    {
        public override string GetName() => "Notes157 player";
    }

    public partial class SelfTest
    {
        void Notes157Aboard(EntityShip ship)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var channelField = typeof(HansaMod).GetField("serverChannel", flags);
            var realChannel = channelField.GetValue(ship.Mod);
            var captured = HearthTestProxy.Make<IServerNetworkChannel>((m, a) => HearthTestProxy.Default(m));
            var ep = new Notes157Player { Api = sapi, World = sapi.World, EntityId = -157, Code = new AssetLocation("game:player"), AnimManager = null };
            var player = new HearthTestPlayer((ServerMain)sapi.World, ep);
            player.WorldData.CurrentGameMode = EnumGameMode.Creative;
            ep.WatchedAttributes.SetString("playerUID", player.PlayerUID);
            ep.LocalEyePos.Set(0, 1.62, 0);
            var hotbar = new InventoryGeneric(12, "hotbar", player.PlayerUID, sapi);
            ((HearthTestInventories)player.InventoryManager).Inventories[hotbar.InventoryID] = hotbar;
            bool cannons = ship.Mod.Config.Cannons, damage = ship.Mod.Config.Damage;
            bool shot = ship.Mod.Config.CannonsNeedShot;
            var hatch = ship.Pieces.All.Single(p => p.Role == PieceRole.Hatch);
            bool wasOpen = hatch.Open;
            float yaw = ship.Pos.Yaw;
            EntityAgent native = null;
            var gun = ship.Def.Fixtures.First(f => f.Type == "gun");
            var gunState = ship.Gun(gun.Code);
            var gunBefore = (gunState.FiredMs, gunState.Shots, gunState.Charge, gunState.RunIn, gunState.Lay, gunState.Aim);
            bool portBefore = ship.OpenFittings.Contains(gun.Code);
            void WorldAt(double x, double y, double z)
            {
                ship.LocalToWorld(x, y, z, out double wx, out double wy, out double wz);
                ep.Pos.SetPos(wx, wy, wz); ep.Pos.Dimension = ship.Pos.Dimension;
            }
            try
            {
                channelField.SetValue(ship.Mod, captured);
                ship.Mod.Config.Cannons = ship.Mod.Config.Damage = true;
                ship.Mod.Config.CannonsNeedShot = false;
                hatch.Open = false;
                var deck = ship.GetOrCreateDeckSeat(player.PlayerUID, -6.8, 1.125, .965);
                Check(ep.TryMount(deck) && ep.MountedOn == deck, "157 hatch: a real headless EntityPlayer mounts the server DeckSeat");
                typeof(HansaMod).GetMethod("ApplyDeckState", flags).Invoke(ship.Mod, new object[] { player, deck,
                    new DeckStatePacket { ShipId = ship.EntityId, X = -6.7f, Y = 1.125f, Z = .965f, Flags = DeckSeat.FlagOnGround }, sapi.World.ElapsedMilliseconds });
                Check(Math.Abs(deck.StoredLocal.X + 6.7) < 1e-5 && deck.AcceptedFitVersion == ship.FitVersion,
                    "157 hatch: the actual deck packet validation accepts and retains the recorded near-coaming location");
                // Native mounted world Pos may still be from a different tick. It must never be counted a second time.
                WorldAt(-5, 1.125, 0);
                Check(ship.ToggleHatch(player, hatch) && hatch.Open && ship.ToggleHatch(player, hatch) && !hatch.Open,
                    "157 hatch: server uses DeckSeat.LocalPos once, even with this player's stale world Pos inside the swing");
                WorldAt(-6.7, 1.125, .965);
                ship.UsePiece(player, hatch.Id);
                Check(hatch.Open, "157 hatch: the real player UsePiece path opens the recorded closed hatch");
                ship.UsePiece(player, hatch.Id);
                Check(!hatch.Open, "157 hatch: the real player UsePiece path closes from beside the coaming");
                deck.SetLocal(-5, 1.125, 0, DeckSeat.FlagOnGround);
                Check(!ship.ToggleHatch(null, hatch), "157 hatch: the same player's real body in the leaves refuses opening");
                ep.TryUnmount();

                var nativeType = sapi.World.GetEntityType(new AssetLocation("game:chicken-hen"));
                native = sapi.World.ClassRegistry.CreateEntity(nativeType) as EntityAgent;
                ship.Pos.Yaw = 0;
                ship.LocalToWorld(-6.5, 1.125, 0, out double nx, out double ny, out double nz);
                native.Pos.SetPos(nx, ny, nz); sapi.World.SpawnEntity(native);
                native.CollisionBox.Set(-.6f, 0, -.1f, .6f, 1, .1f);
                Check(ship.ToggleHatch(null, hatch) && ship.ToggleHatch(null, hatch),
                    "157 hatch: actual World.GetEntitiesAround rotates an elongated native body's box and clears the hatch end");
                ship.LocalToWorld(-5, 1.125, 0, out nx, out ny, out nz); native.Pos.SetPos(nx, ny, nz);
                Check(!ship.ToggleHatch(null, hatch), "157 hatch: actual native entity inside the swing still blocks");
                native.Die(EnumDespawnReason.Removed); native = null;
                ship.Pos.Yaw = yaw;

                var breech = ship.GunBreechLocal(gun);
                deck = ship.GetOrCreateDeckSeat(player.PlayerUID, breech.X + 8, breech.Y, breech.Z);
                WorldAt(breech.X, breech.Y, breech.Z); ep.TryMount(deck);
                ep.Pos.Yaw = 2.1f; ep.Pos.Pitch = 3.35f;
                string far = ship.ManGun(gun, ep);
                Check(far == "ships:gun-far" && ep.MountedOn == deck && ep.Pos.Yaw == 2.1f && ep.Pos.Pitch == 3.35f,
                    "157 guns: authoritative distant DeckSeat refuses manning despite stale nearby world Pos; mount and view unchanged");
                var place = ship.GunSeatLocal(gun);
                deck.SetLocal(place.X, place.Y, place.Z, DeckSeat.FlagOnGround);
                WorldAt(place.X + 8, place.Y, place.Z);
                Check(ship.ManGun(gun, ep) == null && ep.MountedOn == ship.GunSeatOf(gun) && ep.Pos.Yaw == 2.1f && ep.Pos.Pitch == 3.35f,
                    "157 guns: authoritative nearby DeckSeat mans the gun behind the breech, preserving yaw/pitch");
                ship.OpenGunPort(gun, true);
                var powder = sapi.World.GetItem(new AssetLocation(GunLoading.Powder));
                hotbar[1].Itemstack = new ItemStack(powder, 2);
                ship.Gun(gun.Code).Charge = 0;
                ship.Gun(gun.Code).FiredMs = long.MinValue / 2;
                int shotsBefore = ship.Gun(gun.Code).Shots;
                ship.Interact(player, gun, ctrl: true);
                Check(ship.Gun(gun.Code).Shots == shotsBefore && hotbar[1].StackSize == 2,
                    "157 guns: a repeated mount request is idempotent and never fires the mounted gun");
                ship.SetGunLay(gun, new GunLay(0, 3));
                Check(ship.StartLaying(gun, ep) == null && ship.GunLayer(gun) == ep, "157 guns: the mounted player can start laying");
                ship.LayerAim(gun, ep, new GunLay(4, 5));
                typeof(EntityShip).GetMethod("GunsTick", flags).Invoke(ship, new object[] { 10f });
                Check(Math.Abs(ship.GunLayNow(gun).Train - 4) < .001 && Math.Abs(ship.GunLayNow(gun).Elevation - 5) < .001,
                    "157 guns: actual mounted LayerAim/GunsTick trains and elevates");
                ship.Gun(gun.Code).FiredMs = long.MinValue / 2;
                Check(ship.FireGun(gun, ep, player) == null && hotbar[1].StackSize == 1 && !ship.GunRunIn(gun),
                    "157 guns: mounted fire takes one inventory powder and leaves automatic reload run out");
                var standTasks = new System.Collections.Generic.List<Action>();
                var events = HearthTestProxy.Make<IEventAPI>((m, a) => {
                    if (m.Name == "EnqueueMainThreadTask") { standTasks.Add((Action)a[0]); return null; }
                    return m.Invoke(sapi.Event, a);
                });
                var realApi = ship.Api;
                ship.Api = HearthTestProxy.Make<ICoreAPI>((m, a) => m.Name == "get_Event" ? events : m.Invoke(realApi, a));
                try { ship.GunSeatOf(gun).Controls.Sneak = true; }
                finally { ship.Api = realApi; }
                foreach (var task in standTasks) task();
                Check(standTasks.Count == 1 && ep.MountedOn is DeckSeat && ship.GunSeatOf(gun).Passenger == null && ship.GunLayer(gun) == null,
                    "157 guns: actual sneak control returns to a DeckSeat and stops laying immediately");

                var pump = ship.Def.Fixtures.First(f => f.Type == "pump");
                ship.PumpStroke(player.PlayerUID, pump);
                ship.PumpStroke(player.PlayerUID, pump);
                Check(ship.Pumpers == 1 && ship.PumpsWorked == pump.Code, "157 pumps: repeated server heartbeats count one pumper");
                ship.PumpStroke("notes157-other", pump);
                typeof(HansaMod).GetMethod("OnFixture", flags).Invoke(ship.Mod, new object[] { player,
                    new FixturePacket { ShipId = ship.EntityId, Fixture = pump.Index, PumpStop = true } });
                Check(ship.Pumpers == 1 && ship.PumpsWorked == pump.Code, "157 pumps: actual release packet removes its sender and retains the other hand");
                ship.StopPumping("notes157-other");
                Check(ship.Pumpers == 0 && ship.PumpsWorked == "" && ship.PumpedRate == 0 &&
                    ship.WatchedAttributes.GetTreeAttribute("ships").GetString("pumpsAt") == "",
                    "157 pumps: last release clears pumping/flow and synchronizes the stop immediately");
            }
            finally
            {
                native?.Die(EnumDespawnReason.Removed);
                ep.TryUnmount();
                ship.StopPumping(player.PlayerUID); ship.StopPumping("notes157-other");
                ship.Mod.Config.Cannons = cannons; ship.Mod.Config.Damage = damage; ship.Mod.Config.CannonsNeedShot = shot;
                hatch.Open = wasOpen; ship.Pos.Yaw = yaw;
                ship.StopLaying(gun, ep);
                (gunState.FiredMs, gunState.Shots, gunState.Charge, gunState.RunIn, gunState.Lay, gunState.Aim) = gunBefore;
                if (portBefore) ship.OpenFittings.Add(gun.Code); else ship.OpenFittings.Remove(gun.Code);
                typeof(EntityShip).GetMethod("SaveGunLays", flags).Invoke(ship, new object[] { true });
                typeof(EntityShip).GetMethod("FittingsChanged", flags).Invoke(ship, null);
                channelField.SetValue(ship.Mod, realChannel);
                ship.Attributes.GetTreeAttribute("hansaCrew")?.RemoveAttribute(player.PlayerUID);
            }
        }
    }
}
