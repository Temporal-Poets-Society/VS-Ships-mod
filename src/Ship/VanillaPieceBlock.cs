using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// A vanilla block as a piece (<see cref="PieceBlock"/>), read from the game: its behaviours (HorizontalOrientable,
    /// HorizontalAttachable, OmniAttachable, Multiblock), its class (beds, typed containers and crates), its light, its
    /// variants and boxes. Typed containers take their collision and selection boxes by type, as their block entity does.
    /// </summary>
    public class VanillaPieceBlock : PieceBlock
    {
        public readonly Block Block;
        readonly IWorldAccessor world;
        readonly string variantKey;
        readonly string containerType;

        /// <param name="building">(0.3.18; 0.3.19: the player's building mode, <see cref="BuildingMode(EntityAgent)"/>, or a design
        /// laying its pieces) Planks, fences, glass panes, plank stairs, plank slabs and ladders are ship structure
        /// (partitions, railings, windows, walked stairs, panels, ladder-stairs); without it they're ordinary blocks
        /// (decoration where they fit a floor place).</param>
        public VanillaPieceBlock(IWorldAccessor world, ItemStack stack, bool building = true)
        {
            this.world = world;
            Block = stack.Block;
            Code = Block.Code.ToString();
            var behaviors = Block.BlockBehaviors ?? Array.Empty<BlockBehavior>();
            if (Block is BlockBed)
            {
                Orient = PieceOrient.Bed;
                SleepEfficiency = Block.Attributes?["sleepEfficiency"].AsFloat(0.5f) ?? 0.5f;
            }
            else if (behaviors.FirstOrDefault(b => b is BlockBehaviorOmniAttachable) is BlockBehaviorOmniAttachable omni)
            {
                Orient = PieceOrient.Omni;
                variantKey = omni.facingCode ?? "orientation";
                OmniWalls = world.GetBlock(Block.CodeWithVariant(variantKey, "north")) != null;
            }
            else if (behaviors.Any(b => b is BlockBehaviorHorizontalAttachable))
            {
                Orient = PieceOrient.Attachable;
                variantKey = Block.Variant.ContainsKey("side") ? "side" : Block.Variant.Keys.LastOrDefault();
            }
            else if (behaviors.Any(b => b is BlockBehaviorHorizontalOrientable) || IsHorizontal(Block, "horizontalorientation") || IsHorizontal(Block, "side"))
            {
                Orient = PieceOrient.Orientable;
                variantKey = IsHorizontal(Block, "horizontalorientation") ? "horizontalorientation" : "side";
            }
            else Orient = PieceOrient.None;

            if (Block is BlockGenericTypedContainer || Block is BlockCrate)
            {
                Container = true;
                TurnMesh = true;
                Slots = ShipFittings.ContainerSlots(stack);
                containerType = Block is BlockCrate
                    ? stack.Attributes.GetString("type") ?? Block.Attributes?["defaultType"].AsString("wood-aged") ?? "wood-aged"
                    : ShipFittings.ChestType(stack);
            }
            else if (Block.Attributes?["quantitySlots"].Exists == true && Block.EntityClass != null)
            {
                Container = true;
                Slots = Math.Clamp(Block.Attributes["quantitySlots"].AsInt(16), 1, 64);
            }
            Overlay = IsOverlay(Block);
            Door = IsDoor(Block);
            // (0.3.16, notes 154-159) the modular pieces: a pane a window, a fence a railing, stairs a stair, a plank slab a horizontal panel
            // (0.3.18: only in building mode; 0.3.19: the player's toggle, not a saw)
            Building = building;
            Planks = building && IsPlanks(Block);
            Pane = building && IsPane(Block);
            Fence = building && IsFence(Block);
            // (0.3.23) the mod's own ship's stairs (vanilla's plank stairs are plain decoration now)
            Stairs = building && IsShipStair(Block);
            NeedsBuilding = !building && IsShipStair(Block);
            StairCells = ShipStairCells(Block);
            StairRails = IsShipStair(Block);
            Slab = building && IsPlankSlab(Block);
            // (0.3.19) a ladder: the companion ladder-stair (plank stairs are a walked stair now)
            Ladder = building && IsLadder(Block);
            // (0.3.18, note 165) the ship's hearth, oven and flue
            Hearth = IsHearthBlock(Block);
            Oven = IsOvenBlock(Block);
            Flue = IsFlueBlock(Block);
            Figurehead = IsFigurehead(Block);
            if (Hearth || Oven) { Container = false; Slots = 0; }
            AshoreOnly = !Container && !Door && !Pane && !Fence && !Stairs && !Slab && !Ladder && !Hearth && !Oven && !Flue && Orient != PieceOrient.Bed && IsWorkstation(Block);
            var hsv = Block.GetLightHsv(world.BlockAccessor, null, stack);
            Light = hsv != null && hsv.Length > 2 && hsv[2] > 0;
            string first = Block.FirstCodePart() ?? "";
            Seat = !Container && Orient != PieceOrient.Bed &&
                   (first is "chair" or "stool" or "bench" or "seat" or "throne" || Block.Code.Path.Contains("chair") || Block.Code.Path.Contains("stool") || Block.Code.Path.Contains("bench"));
        }

        /// <summary>Was it read in building mode (structure from planks, fences, panes, stairs, slabs)?</summary>
        public bool Building;

        /// <summary>(0.3.19) The player's building mode, on the player entity (saved with them, sent to every client).</summary>
        public const string BuildAttribute = "hansaBuild";

        /// <summary>
        /// (0.3.19, the user: "the saw can't be held in the offhand") Building mode: the player's own toggle (a hotkey,
        /// `.hansa build`), set by the server (<see cref="SetBuilding"/>) and read on both sides from the player entity.
        /// </summary>
        public static bool BuildingMode(EntityAgent e) => e?.WatchedAttributes?.GetBool(BuildAttribute) == true;

        /// <summary>(0.3.19) Server: building mode on or off for a player (synced to the clients with the entity).</summary>
        public static void SetBuilding(EntityAgent e, bool on)
        {
            if (e?.WatchedAttributes == null) return;
            e.WatchedAttributes.SetBool(BuildAttribute, on);
            e.WatchedAttributes.MarkPathDirty(BuildAttribute);
        }

        /// <summary>
        /// (0.1.0-dev.9, caravel note 78) A figurehead for her stem head: vanilla's carved ones (game:figurehead-*, their wood
        /// in the stack's "material"), or a chiseled block, as vanilla's sailboat takes them on its figurehead place.
        /// </summary>
        public static bool IsFigurehead(Block b) => b is BlockFigurehead || b is BlockMicroBlock;

        /// <summary>(0.3.18) A block that is ship structure only in building mode: planks, a fence, a glass pane, plank stairs, a plank slab; (0.3.19) a ladder.</summary>
        public static bool IsBuildingBlock(Block b) => b != null && (IsPlanks(b) || IsPane(b) || IsFence(b) || IsShipStair(b) || IsPlankSlab(b) || IsLadder(b) || IsTrapdoor(b));

        /// <summary>
        /// (after 0.1.0-dev.6, the user: "while in build mode ... we can skip regular block placement snap findings as those are
        /// placeable with it off") What building mode puts aboard: ship structure (<see cref="IsBuildingBlock"/>) and doors.
        /// Any other block (a chest, a lantern, decoration, the hearth, the oven, a flue) is put aboard with building mode off.
        /// </summary>
        public static bool IsStructureBlock(Block b) => IsBuildingBlock(b) || IsDoor(b);

        /// <summary>(after 0.1.0-dev.6) Why a block in hand isn't put on a place aboard in building mode (a lang code), or null.</summary>
        public static string BuildingRefusal(Block b, bool building) => building && b != null && !IsStructureBlock(b) ? "hansa:piece-build-decor" : null;

        /// <summary>
        /// (0.3.23, the user: "maybe for ships stairs better to have a special block") The mod's ship's stairs
        /// (hansa:shipstair-steep, hansa:shipstair-long; 0.1.0-dev.6: hansa:shipstair-short): in building mode a walked stair 1,
        /// 2 or 3 cells long with its own handrails.
        /// Vanilla's plank stairs no longer make stairs.
        /// </summary>
        public static bool IsShipStair(Block b) => b?.Code?.Domain == "hansa" && b.FirstCodePart() == "shipstair";

        /// <summary>(0.3.23) A ship's stair's cells along its run: the steep one 2, the long one 3; (0.1.0-dev.6, note 36) the short one 1.</summary>
        public static int ShipStairCells(Block b) => !IsShipStair(b) ? PiecePlanner.FlightCells : b.Variant?["length"] switch
        {
            "short" => PiecePlanner.ShortCells,
            "steep" => PiecePlanner.SteepCells,
            _ => PiecePlanner.FlightCells
        };

        /// <summary>(0.3.20, step B) A vanilla trapdoor: in building mode a hatch over an opening in her deck (docs/slots.md §5).</summary>
        public static bool IsTrapdoor(Block b) => b?.FirstCodePart() == "trapdoor";

        /// <summary>(0.3.20) A window trapdoor (trapdoor-window-*): a glazed hatch; a solid one a cargo hatch.</summary>
        public static bool IsGlazedTrapdoor(string code) => code != null && code.Contains("trapdoor-window");

        /// <summary>(0.3.19) A vanilla ladder (ladder-wood-*, ladder-stick-*; not a rope ladder): a companion ladder-stair.</summary>
        public static bool IsLadder(Block b) => b?.FirstCodePart() == "ladder";

        /// <summary>(0.3.18, note 165) Fire bricks (claybricks-*-fire) or vanilla's firepit: the ship's hearth.</summary>
        public static bool IsHearthBlock(Block b)
        {
            string path = b?.Code?.Path;
            return path != null && (b.FirstCodePart() == "firepit" && !path.Contains("construct") || path.StartsWith("claybricks-") && path.EndsWith("-fire"));
        }

        /// <summary>(0.3.18, note 165) Vanilla's clay oven: the ship's oven.</summary>
        public static bool IsOvenBlock(Block b) => b != null && (b.FirstCodePart() == "clayoven" || b.GetType().Name == "BlockClayOven");

        /// <summary>(0.3.18, note 165) Vanilla's brick chimney: a segment of the hearth's flue.</summary>
        public static bool IsFlueBlock(Block b) =>
            b != null && (b.FirstCodePart() == "claybrickchimney" || (b.BlockBehaviors ?? Array.Empty<BlockBehavior>()).Any(x => x.GetType().Name == "BlockBehaviorChimney") && b.FirstCodePart() != "bloomerychimney");

        /// <summary>A planks block (planks-oak-ud, vanilla's wallpapered planks): what panels a wedge along her side.</summary>
        public static bool IsPlanks(Block b)
        {
            string first = b?.FirstCodePart();
            return first != null && (first == "planks" || first.EndsWith("planks")) && b.DrawType == EnumDrawType.Cube;
        }

        /// <summary>(0.3.16) A glass pane (vanilla's leaded pane, any glass pane): a window on a cell's border.</summary>
        public static bool IsPane(Block b) => b != null && (b.FirstCodePart() == "glasspane" || b.GetType().Name == "BlockGlassPane");

        /// <summary>(0.3.16) A wooden fence: a railing on a cell's border.</summary>
        public static bool IsFence(Block b)
        {
            string first = b?.FirstCodePart();
            return first != null && (first is "woodenfence" or "roughhewnfence" || first.StartsWith("fence") && b.BlockMaterial == EnumBlockMaterial.Wood);
        }

        /// <summary>(0.3.16) Stairs (vanilla's plank stairs, any stairs): (0.3.19) a walked 1 x 3 stair on the floor.</summary>
        public static bool IsStairs(Block b) => b != null && (b.FirstCodePart()?.EndsWith("stairs") == true || b is BlockStairs);

        /// <summary>(0.3.16) A plank slab: a horizontal plank panel (a shelf, a well's top).</summary>
        public static bool IsPlankSlab(Block b) => b?.FirstCodePart() == "plankslab";

        /// <summary>(0.3.16) The wood a modular piece's block is of (its planks texture): glasspane-leaded-oak-ew, plankstairs-oak-..., woodenfence-oak-....</summary>
        public static string WoodOf(string code)
        {
            if (code == null) return "oak";
            int colon = code.IndexOf(':');
            var parts = (colon >= 0 ? code.Substring(colon + 1) : code).Split('-');
            // (0.3.19: ladder-wood-oak-north; a stick ladder's wood is oak's)
            if (parts[0] == "ladder") return parts.Length > 2 && parts[1] == "wood" ? parts[2] : "oak";
            // (0.3.23: the ship's stairs, hansa:shipstair-steep / -long / -short, are oak)
            if (parts[0] == "shipstair") return "oak";
            int at = parts[0] == "glasspane" ? 2 : 1;
            return parts.Length > at ? parts[at] : "oak";
        }

        /// <summary>A layer laid on a block's face (vanilla's wallpaper: the "Decor" behaviour, drawn as a surface layer).</summary>
        public static bool IsOverlay(Block b) =>
            b != null && (b.DrawType == EnumDrawType.SurfaceLayer || (b.BlockBehaviors ?? Array.Empty<BlockBehavior>()).Any(x => x.GetType().Name == "BlockBehaviorDecor"));

        /// <summary>A vanilla door ("Door" behaviour) one block wide, at most two high: a piece on a cell's border that opens.</summary>
        public static bool IsDoor(Block b)
        {
            if (b == null || !(b.BlockBehaviors ?? Array.Empty<BlockBehavior>()).Any(x => x.GetType().Name == "BlockBehaviorDoor")) return false;
            int w = b.Attributes?["width"].AsInt(1) ?? 1, h = b.Attributes?["height"].AsInt(2) ?? 2;
            return w == 1 && h <= 2;
        }

        /// <summary>
        /// A block whose use is its block entity's workings (a forge, an anvil, a quern, a firepit, a barrel, a kiln...):
        /// aboard it's only a thing standing there (pieces aren't world blocks; only the hearth and the oven are ported).
        /// Not containers (their storage works), beds, seats, lights, doors, signs, clutter.
        /// </summary>
        public static bool IsWorkstation(Block b)
        {
            if (b == null) return false;
            // (the bellows has no block entity of its own: it blows a forge next to it)
            if (b.GetType().Name == "BlockBellows" || b.FirstCodePart() == "bellows") return true;
            if (b.EntityClass == null) return false;
            string ec = b.EntityClass;
            if (ec is "Generic" or "GenericTyped" or "Sign" or "SignPost" or "Lantern" or "Bed" or "Label" or "Clutter" or "Painting" or "Tapestry") return false;
            string first = b.FirstCodePart() ?? "";
            if (first is "lantern" or "chair" or "table" or "clutter" or "painting" or "tapestry" or "bookshelves" or "mannequin" or "banner") return false;
            return true;
        }

        static bool IsHorizontal(Block b, string key) =>
            b.Variant != null && b.Variant.TryGetValue(key, out string v) && v is "north" or "east" or "south" or "west";

        public override string Variant(string state)
        {
            if (variantKey == null || !Block.Variant.ContainsKey(variantKey)) return null;
            var code = Orient == PieceOrient.Attachable && variantKey == Block.Variant.Keys.LastOrDefault()
                ? Block.CodeWithParts(state) : Block.CodeWithVariant(variantKey, state);
            return world.GetBlock(code) is Block b && b.Id != 0 ? b.Code.ToString() : null;
        }

        public override string BedPart(string part, string side)
        {
            if (Block.Variant == null || !Block.Variant.ContainsKey("part") || !Block.Variant.ContainsKey("side")) return null;
            var code = Block.CodeWithVariants(new[] { "part", "side" }, new[] { part, side });
            return world.GetBlock(code) is Block b && b.Id != 0 ? b.Code.ToString() : null;
        }

        Block Get(string code) => code == null ? Block : world.GetBlock(new AssetLocation(code)) ?? Block;

        public override List<int[]> Cells(string variant)
        {
            var b = Get(variant);
            var list = new List<int[]> { new[] { 0, 0, 0 } };
            var mb = b.BlockBehaviors?.OfType<BlockBehaviorMultiblock>().FirstOrDefault();
            if (mb == null) return list;
            mb.IterateOverEach(new BlockPos(0, 0, 0, 0), p =>
            {
                if (p.X != 0 || p.Y != 0 || p.Z != 0) list.Add(new[] { p.X, p.Y, p.Z });
                return true;
            });
            return list;
        }

        public override double[][] Boxes(string variant, bool collision)
        {
            var b = Get(variant);
            Cuboidf[] boxes = null;
            // a typed container's boxes are its type's (its block entity's collisionSelectionBoxes)
            if (containerType != null && b.Attributes?["collisionSelectionBoxes"][containerType].Exists == true)
                boxes = b.Attributes["collisionSelectionBoxes"][containerType].AsObject<Cuboidf[]>(null);
            boxes ??= collision ? b.CollisionBoxes : b.SelectionBoxes ?? b.CollisionBoxes;
            if (boxes == null) return collision ? Array.Empty<double[]>() : null;
            return boxes.Where(c => c != null).Select(c => new double[] { c.X1, c.Y1, c.Z1, c.X2, c.Y2, c.Z2 }).ToArray();
        }

        /// <summary>The type a typed container or crate was made as ("normal-generic", "wood-aged"), or null.</summary>
        public string ContainerType => containerType;

        /// <summary>
        /// A piece's stack as vanilla would have it: a lantern (BlockLantern builds its name, textures and light from the
        /// stack's "material", "lining" and "glass") without them, or with ones it has no texture for, gets vanilla's own
        /// defaults (its block entity's: copper, plain, quartz), so it never shows as "lantern-small-up-" untextured.
        /// "bronze" is taken as tin bronze. Returns true if it changed the stack.
        /// </summary>
        public static bool WithDefaults(IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Block is not BlockLantern lantern) return false;
            var a = stack.Attributes;
            var tex = lantern.Textures;
            bool Has(string key) => key != null && (tex == null || tex.ContainsKey(key));
            bool changed = false;
            string mat = a.GetString("material");
            if (mat == "bronze" && Has("tinbronze")) mat = "tinbronze";
            if (!Has(mat)) mat = "copper";
            if (mat != a.GetString("material")) { a.SetString("material", mat); changed = true; }
            string lining = a.GetString("lining");
            if (lining == null || lining != "plain" && !Has(lining)) { a.SetString("lining", "plain"); changed = true; }
            string glass = a.GetString("glass");
            if (glass == null || world != null && (world.GetBlock(new AssetLocation("game", "glass-" + glass)) is not Block gb || gb.Id == 0))
            {
                a.SetString("glass", "quartz");
                changed = true;
            }
            return changed;
        }
    }
}
