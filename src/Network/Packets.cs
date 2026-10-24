using ProtoBuf;

namespace Hansa
{
    /// <summary>Client -> server: where I am on the deck (15 per second while walking a deck).</summary>
    [ProtoContract]
    public class DeckStatePacket
    {
        [ProtoMember(1)] public long ShipId;
        [ProtoMember(2)] public float X;
        [ProtoMember(3)] public float Y;
        [ProtoMember(4)] public float Z;
        [ProtoMember(5)] public byte Flags;
        /// <summary>Server -> clients: whose deck position this is.</summary>
        [ProtoMember(6)] public long PlayerEntityId;
        /// <summary>(review H3-3) Server -> the client that asked: boarding her (ShipId) was refused; it puts them back.</summary>
        [ProtoMember(7)] public bool Refused;
    }

    /// <summary>Client -> server: put me on this ship at this local position.</summary>
    [ProtoContract]
    public class BoardPacket
    {
        [ProtoMember(1)] public long ShipId;
        [ProtoMember(2)] public float X;
        [ProtoMember(3)] public float Y;
        [ProtoMember(4)] public float Z;
    }

    /// <summary>Client -> server: I stepped off the ship here, with this velocity (blocks per tick).</summary>
    [ProtoContract]
    public class LeavePacket
    {
        [ProtoMember(1)] public long ShipId;
        [ProtoMember(2)] public double X;
        [ProtoMember(3)] public double Y;
        [ProtoMember(4)] public double Z;
    }

    /// <summary>
    /// Client -> server: a change to her crew list at the plaque (ShipCrew.ChangeCrew): Action "claim", "add", "remove",
    /// "captain", "crew" or "leave"; Name the player it's about.
    /// </summary>
    [ProtoContract]
    public class CrewPacket
    {
        [ProtoMember(1)] public long ShipId;
        [ProtoMember(2)] public string Action;
        [ProtoMember(3)] public string Name;
    }

    /// <summary>Client -> server: paint this name on the ship's name board (blank rubs it off).</summary>
    [ProtoContract]
    public class NamePacket
    {
        [ProtoMember(1)] public long ShipId;
        [ProtoMember(2)] public string Name;
        /// <summary>How it's painted (NameStyle): the letters' colour, the ground's ("" as built), size 0..2, italic.</summary>
        [ProtoMember(3)] public string TextColour;
        [ProtoMember(4)] public string BackColour;
        [ProtoMember(5)] public int Size;
        [ProtoMember(6)] public bool Italic;
    }

    /// <summary>Client -> server: I've mended this part of her (held right-click with the items in hand for a few seconds).</summary>
    [ProtoContract]
    public class RepairPacket
    {
        [ProtoMember(1)] public long ShipId;
        /// <summary>The part the crosshair was on (<see cref="ShipPart"/>'s index).</summary>
        [ProtoMember(2)] public int Part;
    }

    /// <summary>
    /// Server -> clients near it: a cannon ball. Fired (from a ship's gun: it runs back, flashes, and the ball flies from
    /// here), going on from here with this velocity (through a hull, off the water, into it, through a creature), or
    /// stopped here. Clients fly it themselves between these (the same fixed steps as the server: <see cref="Ballistics"/>).
    /// </summary>
    [ProtoContract]
    public class ShotPacket
    {
        public const int Fired = 0, Going = 1, Stopped = 2;

        [ProtoMember(1)] public int Id;
        [ProtoMember(2)] public int Kind;
        [ProtoMember(3)] public double X;
        [ProtoMember(4)] public double Y;
        [ProtoMember(5)] public double Z;
        [ProtoMember(6)] public float VX;
        [ProtoMember(7)] public float VY;
        [ProtoMember(8)] public float VZ;
        [ProtoMember(9)] public bool InWater;
        /// <summary>Fired: the ship and her gun (the fixture's index).</summary>
        [ProtoMember(10)] public long ShipId;
        [ProtoMember(11)] public int Gun;
    }

    /// <summary>Client -> server: I used a fixture. Server -> client: open this storage.</summary>
    [ProtoContract]
    public class FixturePacket
    {
        /// <summary>Fixture value for "nail the copper plates in my hand onto the hull".</summary>
        public const int Cladding = -2;
        /// <summary>Fixture value for "pick this boat up" (Shift + right-click it, empty).</summary>
        public const int PickUp = -3;
        /// <summary>Fixture value for "sit me in this rowing boat" (stepped, jumped or swum into her).</summary>
        public const int BoardBoat = -4;

        [ProtoMember(1)] public long ShipId;
        [ProtoMember(2)] public int Fixture;
        /// <summary>Shift held: take an attached chest or bed back instead of using it.</summary>
        [ProtoMember(3)] public bool Sneak;
        /// <summary>Ctrl held: a gun: stand to it (the gun captain's place) instead of firing it.</summary>
        [ProtoMember(4)] public bool Ctrl;
    }

    /// <summary>
    /// Client -> server: I've held a firestarter or a lit torch to this ship's hearth for so many seconds (held
    /// right-click): light it (the server checks, and a firestarter catches one time in four, as vanilla's).
    /// </summary>
    [ProtoContract]
    public class HearthPacket
    {
        [ProtoMember(1)] public long ShipId;
        /// <summary>The hearth (the fixture's index).</summary>
        [ProtoMember(2)] public int Fixture;
        [ProtoMember(3)] public float Seconds;
        /// <summary>(review H1-25) Just begun holding it there: the server counts the seconds from here itself.</summary>
        [ProtoMember(4)] public bool Start;
    }

    /// <summary>
    /// Client -> server: I'm holding right mouse by this gun (lay it where I look), sent again twice a second while I hold
    /// it; or I've let go (On false).
    /// </summary>
    [ProtoContract]
    public class GunLayPacket
    {
        [ProtoMember(1)] public long ShipId;
        /// <summary>The gun (the fixture's index).</summary>
        [ProtoMember(2)] public int Gun;
        [ProtoMember(3)] public bool On;
        /// <summary>(0.3.27) Where this client lays it (its relative aim: GunLaying.Relative), if HasAim; else the server works it out from the view.</summary>
        [ProtoMember(4)] public bool HasAim;
        [ProtoMember(5)] public float Train;
        [ProtoMember(6)] public float Elevation;
    }
}
