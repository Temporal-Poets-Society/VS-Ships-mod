using System.Collections.Generic;
using System.IO;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>A stand-in for vanilla's door behaviour (its constructor reads the game's door attributes): VanillaPieceBlock.IsDoor goes by the name.</summary>
        sealed class BlockBehaviorDoor : BlockBehavior
        {
            public BlockBehaviorDoor(Block block) : base(block) { }
        }

        /// <summary>
        /// (after 0.1.0-dev.6, the user: "while in build mode ... we can skip regular block placement snap findings as those are
        /// placeable with it off") Building mode puts ship structure only: planks, panes, fences, ship's stairs, plank slabs,
        /// ladders, trapdoors and doors. A chest, a lantern, fire bricks (the hearth) and anything else is refused in it, and put
        /// aboard with it off; the hint and the refusal have their lines.
        /// </summary>
        static void BuildModeStructureOnly(string root)
        {
            Block B(string code, EnumDrawType draw = EnumDrawType.JSON) => new Block { Code = new AssetLocation(code), DrawType = draw };
            var door = B("game:door-plank-oak");
            door.BlockBehaviors = new BlockBehavior[] { new BlockBehaviorDoor(door) };
            var structure = new[]
            {
                B("game:planks-oak-ud", EnumDrawType.Cube), B("game:glasspane-leaded-oak-ew"), B("game:woodenfence-oak-ew-free"),
                B("ships:shipstair-short-north"), B("game:plankslab-oak-down-free"), B("game:ladder-wood-oak-north"),
                B("game:trapdoor-wood-oak-1-north"), door
            };
            var decor = new[] { B("game:chest-east"), B("game:lantern-small-up"), B("game:claybricks-red-fire", EnumDrawType.Cube), B("game:plankstairs-oak-up-north-free") };
            var wrong = new List<string>();
            foreach (var b in structure)
                if (!VanillaPieceBlock.IsStructureBlock(b) || VanillaPieceBlock.BuildingRefusal(b, true) != null || VanillaPieceBlock.BuildingRefusal(b, false) != null)
                    wrong.Add(b.Code + " (structure)");
            foreach (var b in decor)
                if (VanillaPieceBlock.IsStructureBlock(b) || VanillaPieceBlock.BuildingRefusal(b, true) != "ships:piece-build-decor" || VanillaPieceBlock.BuildingRefusal(b, false) != null)
                    wrong.Add(b.Code + " (decoration)");
            Check(wrong.Count == 0, "building mode puts ship structure only (partitions, windows, railings, stairs, panels, ladders, hatches, doors); " +
                                    "a chest, a lantern, the hearth's bricks and plank stairs are put aboard with it off" + (wrong.Count > 0 ? ": " + string.Join(", ", wrong) : ""));
            var lang = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "assets", "ships", "lang", "en.json")));
            Check(lang.TryGetValue("hint-build-decor", out var hint) && hint.Contains("{0}") && lang.ContainsKey("piece-build-decor"),
                "building mode's hint and refusal for decoration in hand have their lines (the hint names the key that turns it off)");
        }
    }
}
