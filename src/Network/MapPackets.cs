using System.Collections.Generic;
using ProtoBuf;

namespace Hansa
{
    /// <summary>Client -> server: "my map is open, where are the ships?" (every few seconds while a map with the ships layer is open).</summary>
    [ProtoContract]
    public class ShipMapRequest
    {
        [ProtoMember(1)] public bool Open;
    }

    /// <summary>One ship on the map: where she is and which way she heads (her yaw), and whether she's the asking player's.</summary>
    [ProtoContract]
    public class ShipMapEntry
    {
        [ProtoMember(1)] public long Id;
        [ProtoMember(2)] public string Code;
        [ProtoMember(3)] public string Name;
        [ProtoMember(4)] public double X;
        [ProtoMember(5)] public double Z;
        [ProtoMember(6)] public float Yaw;
        [ProtoMember(7)] public bool Own;
        [ProtoMember(8)] public float Speed;
        /// <summary>She's in the world now (loaded); otherwise where she was last seen.</summary>
        [ProtoMember(9)] public bool Loaded;
    }

    /// <summary>Server -> client: the ships the player may see on the map (theirs wherever they are, and the loaded ones near them).</summary>
    [ProtoContract]
    public class ShipMapPacket
    {
        [ProtoMember(1)] public List<ShipMapEntry> Ships = new();
    }

    /// <summary>The server's record of a ship for the map (kept in the save game: unloaded ships are still on the map).</summary>
    [ProtoContract]
    public class ShipMapRecord
    {
        [ProtoMember(1)] public long Id;
        [ProtoMember(2)] public string Code;
        [ProtoMember(3)] public string Name;
        [ProtoMember(4)] public double X;
        [ProtoMember(5)] public double Z;
        [ProtoMember(6)] public float Yaw;
        /// <summary>Players she's theirs: who have been aboard her, and whose spawn is bound to her.</summary>
        [ProtoMember(7)] public HashSet<string> Crew = new();
        /// <summary>Her height (world y, her still-water level; 0.3.11: where someone who logged out aboard comes back).</summary>
        [ProtoMember(8)] public double Y;
    }
}
