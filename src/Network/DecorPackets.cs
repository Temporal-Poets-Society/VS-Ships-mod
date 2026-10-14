using ProtoBuf;

namespace Hansa
{
    /// <summary>
    /// Client -> server, decorating a ship (ShipDecor): put the block in hand on a mount point facing Dir (toward the player,
    /// as the crosshair showed it); take a piece up (left mouse held on it long enough); use a piece (right-click: open a
    /// container, sleep, sit); paint, gild or scrape the zone the crosshair is on at (X, Y, Z), ship-local, with what's in hand.
    /// The server checks the reach, that the place is free, the item in hand, and takes what it costs.
    /// </summary>
    [ProtoContract]
    public class DecorPacket
    {
        /// <summary>(0.3.19) Build: building mode, Dir 0 off, 1 on, 2 the other way (no ship needed).</summary>
        public const int Place = 1, Remove = 2, Use = 3, Paint = 4, Build = 5;
        /// <summary>(0.3.20, step B) A deck cell (Mount: its slot id): taken up (an axe), laid (planks), a hatch laid over its opening (trapdoors).</summary>
        public const int CellTake = 6, CellLay = 7, CellHatch = 8;
        /// <summary>(0.3.21, step C) A gun place (Mount: its id): a gun mounted there (the gun item in hand), its gun taken up (empty hand, Shift).</summary>
        public const int GunMount = 9, GunTake = 10;
        /// <summary>(0.3.26, step D1) A hull panel (Mount: its slot id): taken out (an axe), laid (planks).</summary>
        public const int HullTake = 11, HullLay = 12;
        /// <summary>(0.3.27, note 199) A piece (Piece: its id) turned with a wrench.</summary>
        public const int Rotate = 13;

        [ProtoMember(1)] public long ShipId;
        [ProtoMember(2)] public int Action;
        [ProtoMember(3)] public string Mount;
        [ProtoMember(4)] public int Dir;
        [ProtoMember(5)] public int Piece;
        [ProtoMember(6)] public string Zone;
        [ProtoMember(7)] public double X;
        [ProtoMember(8)] public double Y;
        [ProtoMember(9)] public double Z;
    }
}
