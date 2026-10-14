using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// (0.3.21, step C of the slot plan; docs/slots.md §7) Guns as items: her gun places (each backed by a gun fixture) have
    /// their gun mounted or not. A mounted gun is today's gun in every way; a place without one has nothing of the gun (not
    /// in her collision, the crosshair, her seats, the guns' ticks, the shots' targets, her wreck; its moving parts not
    /// drawn, its lid shut). The state (<see cref="GunPlaceStates"/>) is saved as differences from the design's
    /// <c>start</c> in WatchedAttributes "hansaGunPlaces" (place id → 1 mounted / 0 empty) and sent to every client.
    /// </summary>
    public partial class EntityShip
    {
        GunPlaceStates gunPlaceStates;
        /// <summary>Her gun places' states (never null once she has a design).</summary>
        public GunPlaceStates GunPlaceStates => gunPlaceStates ??= new GunPlaceStates(Def);

        /// <summary>A gun item's block (any hansa:gun-* block: the bronze gun on its carriage, any side).</summary>
        public static bool IsGunBlock(CollectibleObject c) => c?.Code != null && c.Code.Domain == "hansa" && c.Code.Path.StartsWith("gun-");

        public GunPlaceDef GunPlaceOf(FixtureDef f) => Def == null ? null : GunPlaceStates.PlaceOf(f);
        public GunPlaceDef GunPlace(string id) => Def == null ? null : GunPlaceStates.Place(id);
        public FixtureDef GunOf(GunPlaceDef p) => p == null ? null : Def?.Fixture(p.Gun);
        /// <summary>Is a gun mounted on this place now?</summary>
        public bool GunPlaceLaid(GunPlaceDef p) => Def != null && GunPlaceStates.Laid(p);
        /// <summary>A gun fixture that's there: a fixed gun (no place) always, a place's while it's mounted.</summary>
        public bool GunMounted(FixtureDef f) => f?.Type == "gun" && (Def?.GunPlaces == null || Def.GunPlaces.Length == 0 || GunPlaceStates.Mounted(f));
        /// <summary>A gun fixture whose place has no gun: nothing of it is there.</summary>
        public bool GunAbsent(FixtureDef f) => f?.Type == "gun" && !GunMounted(f);
        /// <summary>The fixture codes of her guns that aren't there (for her wreck), or null: all are.</summary>
        public string[] GunsGone() => Def == null ? null : GunPlaceStates.Gone();
        /// <summary>(0.3.28, D2.4) The deck slot index of a place's port cover (-1: none, its port is always there).</summary>
        public int PortCoverOf(GunPlaceDef p) => p?.Port == null || Def?.Slots == null ? -1 : Def.Slots.IndexOf(p.Port);
        /// <summary>
        /// (0.3.28, D2.4) Is this gun's port planked over now (its cover in)? Then it has no lid and takes no gun. ((0.3.29) a
        /// port in her side's panels: no port in there.)
        /// </summary>
        public bool PortCovered(string gunCode)
        {
            var f = Def?.Fixture(gunCode);
            var gp = f == null ? null : GunPlaceOf(f);
            int n = PortCoverOf(gp);
            if (n < 0 || Slots == null) return false;
            // ((0.3.29) a castle port's place: no lid unless the port's in)
            if (gp?.GunPort != null && Def.Slots.IndexOf(gp.GunPort) is int g && g >= 0) return !Slots.Filled(g);
            return Slots.Filled(n);
        }
        /// <summary>The deck cells a place needs filled.</summary>
        public List<int> GunPlaceCells(GunPlaceDef p) => GunPlaces.Cells(Def, p);

        void InitGunPlaces()
        {
            gunPlaceStates = Def == null ? null : new GunPlaceStates(Def);
            if (Def?.GunPlaces == null || Def.GunPlaces.Length == 0) return;
            ReadGunPlaces();
            if (Api.Side == EnumAppSide.Client) WatchedAttributes.RegisterModifiedListener("hansaGunPlaces", () => { ReadGunPlaces(); RebuildGrid(); });
            else { RescueOrphanedGuns(); OpenPortsUnderGuns(); }
        }

        /// <summary>
        /// (0.3.28, D2.4; note 223: the poop's ports start planked over now) A gun mounted on a place whose port's cover is in
        /// (a ship saved before her ports had covers): its port opened, the gun kept.
        /// </summary>
        void OpenPortsUnderGuns()
        {
            if (Slots == null || Def.GunPlaces == null) return;
            bool any = false;
            foreach (var p in Def.GunPlaces)
            {
                int n = PortCoverOf(p);
                if (n < 0 || !GunPlaceLaid(p) || !PortCovered(p.Gun)) continue;
                Slots.Set(n, SlotState.Empty);
                // ((0.3.29) and the port itself in)
                if (p.GunPort != null && Def.Slots.IndexOf(p.GunPort) is int g && g >= 0) Slots.Set(g, SlotState.Filled);
                any = true;
                World.Logger.Audit("[hansa] {0} #{1}: the port of her gun {2} opened (a gun was mounted there with no port in her side).", GetName(), EntityId, p.Id);
            }
            if (any) SaveSlots();
        }

        /// <summary>
        /// (0.3.27, note 203: the poop's gun by the transom gone) A gun mounted on a place her design no longer has: the gun
        /// (its item) into her hold, so nothing is lost; the place's state dropped.
        /// </summary>
        void RescueOrphanedGuns()
        {
            if (WatchedAttributes.GetTreeAttribute("hansaGunPlaces") is not ITreeAttribute t) return;
            var gone = t.Select(kv => kv.Key).Where(k => !Def.GunPlaces.Any(p => p.Id == k) && t.GetInt(k) != 0).ToList();
            if (gone.Count == 0) return;
            var block = World.GetBlock(new AssetLocation(GunPlaceDef.DefaultBlock));
            foreach (var k in gone)
            {
                t.RemoveAttribute(k);
                if (block != null && block.Id != 0) StowInHold(new ItemStack(block));
                World.Logger.Notification("[hansa] {0} #{1}: the gun on {2} (a place her design no longer has) is in her hold.", GetName(), EntityId, k);
            }
            WatchedAttributes.MarkPathDirty("hansaGunPlaces");
        }

        void ReadGunPlaces()
        {
            var st = GunPlaceStates;
            st.Delta.Clear();
            st.Version++;
            if (WatchedAttributes.GetTreeAttribute("hansaGunPlaces") is not ITreeAttribute t) return;
            foreach (var p in Def.GunPlaces)
                if (t.HasAttribute(p.Id))
                {
                    bool laid = t.GetInt(p.Id) != 0;
                    if (laid != p.StartsLaid) st.Delta[p.Id] = laid;
                }
        }

        void SaveGunPlaces()
        {
            var t = new TreeAttribute();
            foreach (var kv in GunPlaceStates.Delta) t.SetInt(kv.Key, kv.Value ? 1 : 0);
            WatchedAttributes["hansaGunPlaces"] = t;
            WatchedAttributes.MarkPathDirty("hansaGunPlaces");
        }

        void SetGunPlace(GunPlaceDef p, bool laid)
        {
            GunPlaceStates.Set(p, laid);
            var f = GunOf(p);
            if (f != null)
            {
                // a gun mounted or taken up starts afresh: cold, laid square, housed
                guns.Remove(f.Code);
                gunLayers.Remove(f.Code);
                gunOpenedMs.Remove(f.Code);
                OpenFittings.Remove(f.Code);
            }
            if (Api?.Side == EnumAppSide.Server)
            {
                SaveGunPlaces();
                SaveGunLays(true);
            }
            FittingsChanged();
        }

        /// <summary>What's put aboard that a gun's working room must be clear of: the pieces' boxes, the chests and boats on attach places.</summary>
        IEnumerable<double[]> ThingsAboard()
        {
            foreach (var piece in Pieces.All)
            {
                if (piece.Role == PieceRole.Railing) continue;          // ((0.3.29) RailingsAboard)
                foreach (var b in piece.Solids.Concat(piece.Sel)) yield return b;
            }
            foreach (var a in Def.Fixtures)
                if (a.Type == "attach" && Attached.TryGetValue(a.Code ?? "", out var st) && ShipFittings.AttachBox(a, st, Def) is double[] ab) yield return ab;
        }

        /// <summary>(0.3.29, note 232) The railings put aboard (a gun's carriage keeps clear of them; its crew step round them).</summary>
        IEnumerable<double[]> RailingsAboard()
        {
            foreach (var piece in Pieces.All)
                if (piece.Role == PieceRole.Railing)
                    foreach (var b in piece.Solids.Concat(piece.Sel)) yield return b;
        }

        /// <summary>Why a gun can't be mounted on this place now (a lang code), or null.</summary>
        public string MountGunProblem(GunPlaceDef p, bool checkPeople)
        {
            var f = GunOf(p);
            if (f == null) return "hansa:gunplace-none";
            if (GoneDown) return "hansa:gone-down";
            string err = GunPlaces.MountProblem(Def, p, GunPlaceStates, Slots, ThingsAboard(), RailingsAboard());
            if (err != null) return err;
            // (someone standing where the carriage goes)
            if (checkPeople && (f.Box ?? f.Sel) is double[] cb && BoxOccupied(cb)) return "hansa:piece-someone";
            return null;
        }

        /// <summary>Mount a gun on a place (the commands and tests: no item). Returns an error lang code or null.</summary>
        public string MountGun(GunPlaceDef p, bool checkPeople)
        {
            string err = MountGunProblem(p, checkPeople);
            if (err != null) return err;
            SetGunPlace(p, true);
            return null;
        }

        /// <summary>Why this place's gun can't be taken up now (a lang code), or null: it must be there, cold, housed, and nobody at it.</summary>
        public string TakeGunProblem(GunPlaceDef p)
        {
            var f = GunOf(p);
            if (f == null) return "hansa:gunplace-none";
            if (GoneDown) return "hansa:gone-down";
            bool laying = Api?.Side == EnumAppSide.Server ? GunLayer(f) != null : LayingHere == f;
            bool manned = GunSeatOf(f) is GunSeat gs && gs.Passenger != null && gs.Passenger.Alive;
            return GunPlaces.TakeProblem(Def, p, GunPlaceStates, GunPlaceLaid(p) ? GunReloading(f) : 0, GunOpen(f), laying, manned);
        }

        /// <summary>Take a place's gun up (the commands and tests: no item given). Returns an error lang code or null.</summary>
        public string TakeGun(GunPlaceDef p)
        {
            string err = TakeGunProblem(p);
            if (err != null) return err;
            SetGunPlace(p, false);
            return null;
        }

        /// <summary>Building mode, a gun item in hand, right-click an empty gun place: the gun is mounted there.</summary>
        public string TryMountGun(IServerPlayer player, string placeId)
        {
            var p = GunPlace(placeId);
            var f = GunOf(p);
            if (f == null) return "hansa:gunplace-none";
            if (!VanillaPieceBlock.BuildingMode(player.Entity)) return "hansa:cell-build-mode";
            var slot = player.InventoryManager.ActiveHotbarSlot;
            var held = slot?.Itemstack;
            if (!IsGunBlock(held?.Collectible)) return "hansa:gunplace-needs-gun";
            if (!InReach(player, f.Pos[0], f.Pos[1], f.Pos[2])) return "hansa:piece-too-far";
            string err = MountGun(p, true);
            if (err != null) return err;
            if (!Creative(player))
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }
            GunPlaceSound(f);
            World.Logger.Audit("{0} mounted a gun on {1} of {2} #{3}.", player.PlayerName, p.Id, GetName(), EntityId);
            return null;
        }

        /// <summary>Building mode, empty hand, Shift + right-click a place's gun: it's taken up, the item to the player (else dropped by them).</summary>
        public string TryTakeGun(IServerPlayer player, string placeId)
        {
            var p = GunPlace(placeId);
            var f = GunOf(p);
            if (f == null) return "hansa:gunplace-fixed";
            if (!VanillaPieceBlock.BuildingMode(player.Entity)) return "hansa:cell-build-mode";
            if (player.InventoryManager.ActiveHotbarSlot?.Itemstack != null) return "hansa:gun-take-empty-hand";
            if (!InReach(player, f.Pos[0], f.Pos[1], f.Pos[2])) return "hansa:piece-too-far";
            string err = TakeGunProblem(p);
            if (err != null) return err;
            if (!Creative(player))
            {
                var block = World.GetBlock(new AssetLocation(p.BlockCode)) ?? World.GetBlock(new AssetLocation(GunPlaceDef.DefaultBlock));
                if (block != null)
                {
                    var stack = new ItemStack(block);
                    if (!player.InventoryManager.TryGiveItemstack(stack, true))
                        World.SpawnItemEntity(stack, player.Entity.Pos.XYZ.Add(0, 0.5, 0));
                }
            }
            SetGunPlace(p, false);
            GunPlaceSound(f);
            World.Logger.Audit("{0} took up the gun on {1} of {2} #{3}.", player.PlayerName, p.Id, GetName(), EntityId);
            return null;
        }

        void GunPlaceSound(FixtureDef f)
        {
            LocalToWorld(f.Pos[0], f.Pos[1] + 0.5, f.Pos[2], out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/block/anvil"), x, y, z, null, true, 16, 0.6f);
        }

        /// <summary>`/hansa gunplaces`: her gun places and what's on them.</summary>
        public string GunPlacesText()
        {
            if (Def?.GunPlaces == null || Def.GunPlaces.Length == 0) return $"{GetName()} has no gun places (her guns are fixed).";
            // ((0.3.25) an empty place another gun's in the way of says which; a stern chaser says it fires aft)
            return $"{GetName()}'s gun places: " + string.Join(", ", Def.GunPlaces.Select(p => p.Id +
                (ShipCompartments.AlongX(GunOf(p)) ? " [aft]" : "") +
                (GunPlaceLaid(p) ? " (gun)" : GunPlaces.GunInTheWay(Def, p, GunPlaceStates) is GunPlaceDef o ? $" (empty, {o.Id} in the way)" : " (empty)")));
        }
    }
}
