using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// Her crew (0.3.11, the user's rules for multiplayer): a list of players written on the bronze plaque by the great
    /// cabin's door, each a captain or crew. Captains add and remove names and make others captains; anyone on it can take
    /// their own name off. Everyone on it has the run of her; others may come aboard and walk about while one of her crew
    /// is online, open doors and hatches, and nothing else.
    ///
    /// Laid up: while none of her crew is online she lies at anchor with her sails furled, still rocking in the waves and
    /// swinging to the wind, solid to everyone else, and closed to them; she takes no water, no damage and no wear until
    /// one of them comes back (else every ship on a server sinks in the end).
    ///
    /// A ship nobody has written their name on yet (an older one, or a boat) has no crew list: everyone may use her and
    /// she's never laid up, as before. Whoever launches her from a deed is her first captain.
    /// Synced in WatchedAttributes "hansaCrewList": uid -> { name, captain }, and "hansaLaidUp".
    /// </summary>
    public partial class EntityShip
    {
        public const string CrewKey = "hansaCrewList";

        public class CrewMember
        {
            public string Uid, Name;
            public bool Captain;
        }

        /// <summary>Her crew by uid (captains and the rest), in the order they were written on.</summary>
        public readonly List<CrewMember> Crew = new();

        /// <summary>Whether she has a crew list at all (a boat never does: anyone rows a boat).</summary>
        public bool HasCrewList => Crew.Count > 0;

        /// <summary>Laid up now: no one of her crew online (server: worked out every second; clients: synced).</summary>
        public bool LaidUp { get; private set; }

        public bool CrewCanHave => Def != null && Def.PickUpItem == null;

        public CrewMember CrewEntry(string uid) => uid == null ? null : Crew.FirstOrDefault(c => c.Uid == uid);
        public bool IsCrew(string uid) => CrewEntry(uid) != null;
        public bool IsCaptain(string uid) => CrewEntry(uid)?.Captain == true;

        /// <summary>May this player use her fittings: anyone while she has no crew list, else her crew (and server admins).</summary>
        public bool MayUse(IPlayer player)
        {
            if (player == null) return false;
            if (!HasCrewList || IsCrew(player.PlayerUID)) return true;
            return player is IServerPlayer sp && sp.HasPrivilege(Privilege.controlserver);
        }

        /// <summary>What someone not of her crew may still use while she's not laid up: doors, gates, hatches, the heads, the plaque.</summary>
        public static bool GuestMayUse(FixtureDef f) =>
            f.Type is "door" or "gate" or "seat" or "plaque" || f.Type == "hatch" && f.Storage == null;

        /// <summary>(review H1-4) The same for a piece put aboard: its doors, hatches and seats.</summary>
        public static bool GuestMayUse(ShipPiece p) => p != null && p.Role is PieceRole.Door or PieceRole.Hatch or PieceRole.Seat;

        /// <summary>May this player come aboard: anyone, except while she's laid up (then only her crew, and admins).</summary>
        public bool MayBoard(IPlayer player) => !LaidUp || MayUse(player);

        // ------------------------------------------------------------------ the list (server)

        /// <summary>Whoever launched her from a deed is her first captain.</summary>
        public void SetFirstCaptain(IServerPlayer player)
        {
            if (!CrewCanHave || player == null || HasCrewList) return;
            Crew.Add(new CrewMember { Uid = player.PlayerUID, Name = player.PlayerName, Captain = true });
            SaveCrew();
        }

        /// <summary>
        /// A change to the list from the plaque (CrewPacket.Action): "claim" (her first captain, while she has none),
        /// "add" a name, "remove" one, "captain" / "crew" (make a captain, or not), "leave" (take your own name off).
        /// Returns an error lang code or null.
        /// </summary>
        public string ChangeCrew(IServerPlayer by, string action, string name) =>
            ChangeCrew(by.PlayerUID, by.PlayerName, by.HasPrivilege(Privilege.controlserver), action, name);

        /// <summary>The same, by a player's uid and name (the self-test's stand-ins).</summary>
        public string ChangeCrew(string byUid, string byName, bool admin, string action, string name)
        {
            if (!CrewCanHave) return "ships:crew-boat";
            bool captain = IsCaptain(byUid) || admin;
            switch (action)
            {
                case "claim":
                    if (HasCrewList) return "ships:crew-claimed";
                    Crew.Add(new CrewMember { Uid = byUid, Name = byName, Captain = true });
                    break;
                case "add":
                {
                    if (!captain) return "ships:crew-not-captain";
                    var who = FindPlayer(name, out string err);
                    if (who.uid == null) return err;
                    if (IsCrew(who.uid)) return "ships:crew-already";
                    if (Crew.Count >= MaxCrew) return "ships:crew-full";
                    Crew.Add(new CrewMember { Uid = who.uid, Name = who.name, Captain = false });
                    break;
                }
                case "remove":
                case "leave":
                {
                    var m = action == "leave" ? CrewEntry(byUid) : Crew.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (m == null) return "ships:crew-unknown";
                    if (action == "remove" && m.Uid != byUid && !captain) return "ships:crew-not-captain";
                    // (her last captain can't go while others are on the list: someone must be able to keep it)
                    if (m.Captain && Crew.Count(c => c.Captain) == 1 && Crew.Count > 1) return "ships:crew-last-captain";
                    Crew.Remove(m);
                    Mod?.Spawns?.EndPlayerOnShip(m.Uid, EntityId, ShipSpawns.EndCrew);
                    break;
                }
                case "captain":
                case "crew":
                {
                    if (!captain) return "ships:crew-not-captain";
                    var m = Crew.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (m == null) return "ships:crew-unknown";
                    if (action == "crew" && m.Captain && Crew.Count(c => c.Captain) == 1) return "ships:crew-last-captain";
                    m.Captain = action == "captain";
                    break;
                }
                default:
                    return null;
            }
            SaveCrew();
            World.Logger.Audit("[ships] {0}: crew {1} {2} on {3} #{4}", byName, action, name ?? "", GetName(), EntityId);
            UpdateLaidUp();
            return null;
        }

        public const int MaxCrew = 24;

        /// <summary>The self-test's stand-ins: a name to (uid, name), or null to look the name up as usual.</summary>
        public static System.Func<string, (string, string)?> KnownPlayers;

        /// <summary>A player by name: online, or one the server has seen (offline). (uid, name as they spell it).</summary>
        (string uid, string name) FindPlayer(string name, out string err)
        {
            err = null;
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) { err = "ships:crew-noname"; return (null, null); }
            var sapi = (ICoreServerAPI)Api;
            if (KnownPlayers?.Invoke(name) is (string tu, string tn)) return (tu, tn);
            foreach (var p in sapi.World.AllPlayers)
                if (string.Equals(p.PlayerName, name, StringComparison.OrdinalIgnoreCase)) return (p.PlayerUID, p.PlayerName);
            var data = sapi.PlayerData.GetPlayerDataByLastKnownName(name);
            if (data != null) return (data.PlayerUID, data.LastKnownPlayername ?? name);
            err = "ships:crew-nosuch";
            return (null, null);
        }

        void SaveCrew()
        {
            var t = new TreeAttribute();
            int i = 0;
            foreach (var c in Crew)
            {
                var e = new TreeAttribute();
                e.SetString("uid", c.Uid);
                e.SetString("name", c.Name);
                e.SetBool("captain", c.Captain);
                t[(i++).ToString("D3")] = e;
            }
            WatchedAttributes[CrewKey] = t;
            WatchedAttributes.MarkPathDirty(CrewKey);
        }

        void LoadCrew()
        {
            Crew.Clear();
            if (WatchedAttributes.GetTreeAttribute(CrewKey) is not ITreeAttribute t) return;
            foreach (var kv in t.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (kv.Value is not ITreeAttribute e || e.GetString("uid") is not string uid) continue;
                Crew.Add(new CrewMember { Uid = uid, Name = e.GetString("name") ?? uid, Captain = e.GetBool("captain") });
            }
            if (Api is Vintagestory.API.Client.ICoreClientAPI) Mod?.Client?.OnCrewChanged(this);
        }

        void LoadLaidUp()
        {
            LaidUp = WatchedAttributes.GetBool("hansaLaidUp");
            if (Api is Vintagestory.API.Client.ICoreClientAPI) Mod?.Client?.OnCrewChanged(this);
        }

        // ------------------------------------------------------------------ laid up (server)

        float laidUpAccum = 99;

        /// <summary>Is one of her crew online (playing)?</summary>
        public bool CrewOnline()
        {
            foreach (IServerPlayer sp in ((ICoreServerAPI)Api).World.AllOnlinePlayers)
                if (sp.ConnectionState == EnumClientState.Playing && IsCrew(sp.PlayerUID)) return true;
            return false;
        }

        /// <summary>
        /// Every second: laid up or not. Laying up: anchor down, sails furled, anyone not of her crew still aboard is put
        /// off her, and her wear's clock stops (it starts again from when she's back in commission).
        /// </summary>
        void UpdateLaidUp()
        {
            laidUpAccum = 0;
            bool want = Mod.Config.LayUpWhenCrewOffline && CrewCanHave && HasCrewList && !GoneDown && !CrewOnline();
            if (want)
            {
                if (!Anchored || SailSet != 0) { Anchored = true; SailSet = 0; SaveState(true); }
                foreach (var s in seatArray.ToArray())
                    if (s.Passenger is EntityPlayer ep && ep.Player is IServerPlayer guest && !MayUse(guest))
                    {
                        ep.TryUnmount();
                        Notify(guest, "ships:laidup-put-off");
                    }
            }
            if (want == LaidUp) return;
            LaidUp = want;
            wearHours = double.NaN;
            WatchedAttributes.SetBool("hansaLaidUp", LaidUp);
            WatchedAttributes.MarkPathDirty("hansaLaidUp");
            World.Logger.Notification("[ships] {0} #{1} {2}", GetName(), EntityId, LaidUp ? "laid up: her crew is offline" : "back in commission");
        }

        void LaidUpTick(float dt)
        {
            laidUpAccum += dt;
            if (laidUpAccum >= 1) UpdateLaidUp();
        }
    }
}
