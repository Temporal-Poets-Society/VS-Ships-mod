using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Hansa.Construction
{
    /// <summary>Structural parts a cell's location can contain, in lead priority order.
    /// The leading part names the block a player sees there (a rib cell's skin belongs to the rib).</summary>
    public static class Parts
    {
        public const string Keel = "keel", Rib = "rib", Beam = "beam", Skin = "skin", Deck = "deck";
        public static readonly string[] Priority = { Keel, Rib, Beam, Skin, Deck };
        public static string Lead(IEnumerable<string> parts) => Priority.FirstOrDefault(parts.Contains);
        public static string Key(IEnumerable<string> parts) => string.Join("+", parts.OrderBy(p => Array.IndexOf(Priority, p)));
    }

    /// <summary>One master block kind. A kind fits a cell either by the cell's leading part
    /// (structural kinds), by an exact slot (openings such as windows need a plain hull cell),
    /// or by a list of slots ("" is open space inside the hull).</summary>
    public sealed class BlockKind
    {
        public string Code;
        public string Lead, Slot;
        public string[] Slots;
        /// <summary>"slot" draws the location's structure; "opening", "stair" and "partition" generate.</summary>
        public string Generator = "slot";
        public OpeningSpec Opening;
        public bool Facing;
        /// <summary>Material class a player places: "planks" or "glass". Never a shaped item.</summary>
        public string Material = "planks";
        public double Health = 100;
        /// <summary>null, "always", or "belowWaterline".</summary>
        public string Required;

        public bool Fits(string slotKey, bool interior)
        {
            var parts = slotKey.Length == 0 ? Array.Empty<string>() : slotKey.Split('+');
            if (Lead != null) return Parts.Lead(parts) == Lead;
            if (Slot != null) return slotKey == Slot;
            if (Slots != null) return Slots.Contains(slotKey) && (slotKey.Length > 0 || interior);
            return false;
        }

        public bool Accepts(string material)
        {
            string code = material?.Split(':').Last() ?? "";
            return Material switch
            {
                "planks" => code.StartsWith("planks-"),
                "glass" => code.StartsWith("glasspane-") || code.StartsWith("glass-"),
                _ => false,
            };
        }
    }

    public sealed class OpeningSpec
    {
        /// <summary>Frame width left around the hole, in blocks, on the two in-surface axes.</summary>
        public double Margin = 0.1875;
        /// <summary>"glass" (always closed) or "lid" (opens).</summary>
        public string Panel = "glass";
        public bool Operable;
    }

    public sealed class BlockKinds
    {
        public int Schema;
        public BlockKind[] Kinds = Array.Empty<BlockKind>();
        [JsonIgnore] public Dictionary<string, BlockKind> ByCode;

        public static BlockKinds Read(string json)
        {
            var k = JsonConvert.DeserializeObject<BlockKinds>(json) ?? throw new ArgumentException("Empty kind library");
            if (k.Schema != 1) throw new ArgumentException("Unsupported kind library schema");
            k.ByCode = new Dictionary<string, BlockKind>();
            foreach (var kind in k.Kinds)
                if (!k.ByCode.TryAdd(kind.Code, kind)) throw new ArgumentException($"Duplicate block kind {kind.Code}");
            foreach (var lead in Parts.Priority)
                if (k.Kinds.Count(x => x.Lead == lead) != 1) throw new ArgumentException($"Exactly one kind must lead with {lead}");
            return k;
        }

        public BlockKind Get(string code) =>
            code != null && ByCode.TryGetValue(code, out var k) ? k : throw new ArgumentException($"Unknown block kind {code}");

        /// <summary>The structural kind a location gets by default.</summary>
        public BlockKind ForLead(string lead) => Kinds.First(k => k.Lead == lead);
    }
}
