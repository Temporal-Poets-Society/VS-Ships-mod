using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Hansa
{
    /// <summary>
    /// Vanilla blocks on a ship. The design has spots for them: doors are part of the ship; lanterns, chests, beds, the
    /// hold's cargo (chests, trunks, storage vessels, crates), the ship's boats (a vanilla raft or Hansa's own boat) and
    /// the metal plate by the helm that binds spawns to her are brought aboard by the crew and put on attachment points.
    /// </summary>
    public static class ShipFittings
    {
        /// <summary>Height of a vanilla bed's mattress (9/16).</summary>
        public const double BedHeight = 0.5625;
        public const double ChestHeight = 0.875;

        /// <summary>Can this item go on this attachment point?</summary>
        public static bool Accepts(FixtureDef f, ItemStack stack)
        {
            if (f.Type != "attach" || stack == null) return false;
            // the ship's boats: a vanilla raft or our own small boat (a sailed boat, 8 blocks long with its mast, doesn't fit)
            if (f.Kind == "boat") return IsShipsBoat(stack);
            // the plate on the wall by the helm (the ship's spawn point): any vanilla metal plate
            if (f.Kind == "plate") return IsMetalPlate(stack);
            var block = stack.Block;
            if (block == null) return false;
            switch (f.Kind)
            {
                case "lantern":
                    return block is BlockLantern;
                case "chest":
                    // plain and labeled chests (one block); trunks are two blocks wide
                    return block is BlockGenericTypedContainer && !(block is BlockGenericTypedContainerTrunk) &&
                           (block.FirstCodePart() == "chest" || block.FirstCodePart() == "labeledchest");
                case "cargo":
                    // in the hold: a chest, a storage vessel or a crate on one spot, a trunk across a pair of them
                    if (block is BlockGenericTypedContainerTrunk) return f.Pair != null;
                    if (block is BlockCrate) return true;
                    return block is BlockGenericTypedContainer &&
                           (block.FirstCodePart() == "chest" || block.FirstCodePart() == "labeledchest" || block.FirstCodePart() == "storagevessel");
                case "bed":
                    return block is BlockBed;
            }
            return false;
        }

        /// <summary>A vanilla raft, or Hansa's own boat.</summary>
        /// <summary>
        /// (0.3.24, note 197: "the rowing boat hangs in the air on its gallows") Where a boat place's boat rests: the
        /// gallows' beams under it, found in her mesh file as level tops (faces up) at the place's height less 0.02 (the
        /// generator puts a boat place 0.02 over its beams), each narrower than half a block along the place, within its box.
        /// The beams' middles along the place's length (ship-local x for a place along x, z for one along z); empty if none.
        /// </summary>
        public static List<double> BoatRests(ShipSkinFile file, FixtureDef f)
        {
            var rests = new List<double>();
            var b = f?.Box ?? f?.Sel;
            if (file?.Groups == null || b == null || b.Length < 6 || f.Pos == null) return rests;
            bool alongX = b[3] - b[0] >= b[5] - b[2];
            double top = f.Pos[1] - 0.02;
            var mids = new List<double>();
            foreach (var g in new[] { "exterior", "interior" })
            {
                if (!file.Groups.TryGetValue(g, out var quads) || quads == null) continue;
                foreach (var q in quads)
                {
                    if (q == null || q.Length < 32 || q[7] < 0.9f) continue;
                    double a0 = double.MaxValue, a1 = double.MinValue, c0 = double.MaxValue, c1 = double.MinValue;
                    bool level = true;
                    for (int k = 0; k < 4; k++)
                    {
                        double x = q[1 + 8 * k], y = q[2 + 8 * k], z = q[3 + 8 * k];
                        if (Math.Abs(y - top) > 0.04) { level = false; break; }
                        double a = alongX ? x : z, c = alongX ? z : x;
                        a0 = Math.Min(a0, a); a1 = Math.Max(a1, a); c0 = Math.Min(c0, c); c1 = Math.Max(c1, c);
                    }
                    if (!level || a1 - a0 > 0.5) continue;
                    double lo = alongX ? b[0] : b[2], hi = alongX ? b[3] : b[5], clo = alongX ? b[2] : b[0], chi = alongX ? b[5] : b[3];
                    if (a0 < lo || a1 > hi || c1 < clo || c0 > chi) continue;
                    mids.Add((a0 + a1) / 2);
                }
            }
            foreach (double m in mids.OrderBy(v => v))
                if (rests.Count == 0 || m - rests[^1] > 0.3) rests.Add(m);
            return rests;
        }

        public static bool IsShipsBoat(ItemStack stack)
        {
            // Hansa's own boat: an item that puts one of our boats in the water (ItemShipDeed with "boat")
            if (stack?.Collectible?.Code?.Domain == "hansa" && stack.Collectible.Attributes?["boat"].AsBool(false) == true) return true;
            if (stack?.Collectible is not ItemBoat) return false;
            var code = stack.Collectible.Code;
            return code.Domain == "hansa" ? code.Path.StartsWith("boat-") || code.Path.StartsWith("rowboat-") : stack.Collectible.Variant["type"] == "raft";
        }

        public static bool IsTrunk(ItemStack stack) => stack?.Block is BlockGenericTypedContainerTrunk;

        /// <summary>A vanilla metal plate (game:metalplate-*), whatever the metal.</summary>
        public static bool IsMetalPlate(ItemStack stack)
        {
            var code = stack?.Collectible?.Code;
            return code != null && code.Domain == "game" && code.Path.StartsWith("metalplate-");
        }

        /// <summary>
        /// (0.1.0-dev.9, note 101: "cannot add copper plates to the hull bottom") What sheathes her bottom: vanilla's copper
        /// plate (two ingots of copper), or its copper sheet (one ingot: two of them a plate's worth). How many of the item
        /// go to a plate; 0 if it isn't copper for her bottom.
        /// </summary>
        public static int CladdingItemsPerPlate(ItemStack stack)
        {
            var code = stack?.Collectible?.Code;
            if (code == null || code.Domain != "game") return 0;
            if (code.Path == "metalplate-copper") return 1;
            return code.Path.StartsWith("metalsheet-copper-") ? 2 : 0;
        }

        /// <summary>
        /// The world block a plate's spawn spot is in, for vanilla's spawn position (which is a block): at the ship's
        /// still-water level (baseY; the waves' heave would move it up and down all the time), the feet just above her
        /// deck. The server keeps her level, so no pitch or roll.
        /// </summary>
        public static void SpawnBlock(double[] spot, double shipX, double baseY, double shipZ, double yaw, out int x, out int y, out int z)
        {
            ShipFrame.ToWorld(yaw, spot[0], spot[1], spot[2], out double wx, out double wy, out double wz);
            x = (int)Math.Floor(shipX + wx);
            y = (int)Math.Ceiling(baseY + wy - 0.01);
            z = (int)Math.Floor(shipZ + wz);
        }

        /// <summary>The chest's type ("normal-generic", "owl", ...), as the vanilla container reads it.</summary>
        public static string ChestType(ItemStack stack)
        {
            return stack.Attributes.GetString("type") ?? stack.Block?.Attributes?["defaultType"].AsString("normal-generic") ?? "normal-generic";
        }

        public static int ChestSlots(ItemStack stack) => ContainerSlots(stack);

        /// <summary>How many slots a container holds: a chest, trunk or vessel by its type, a crate by its wood.</summary>
        public static int ContainerSlots(ItemStack stack)
        {
            var attrs = stack.Block?.Attributes;
            if (attrs == null) return 16;
            string type = ChestType(stack);
            if (stack.Block is BlockCrate)
            {
                type = stack.Attributes.GetString("type") ?? attrs["defaultType"].AsString("wood-aged");
                return Math.Clamp(attrs["properties"][type]["quantitySlots"].AsInt(attrs["properties"]["*"]["quantitySlots"].AsInt(16)), 1, 64);
            }
            return Math.Clamp(attrs["quantitySlots"][type].AsInt(16), 1, 64);
        }

        /// <summary>Solid space an attached chest, bed or boat takes (ship-local), or null. A trunk lies across its pair.</summary>
        public static double[] AttachBox(FixtureDef f, ItemStack stack = null, ShipDefinition def = null)
        {
            if (f.Type != "attach" || f.Pos == null) return null;
            double x = f.Pos[0], y = f.Pos[1], z = f.Pos[2];
            switch (f.Kind)
            {
                case "chest":
                    return new[] { x - 0.44, y, z - 0.44, x + 0.44, y + ChestHeight, z + 0.44 };
                case "cargo":
                    if (IsTrunk(stack) && f.Pair != null && def?.Fixture(f.Pair) is FixtureDef p)
                    {
                        return new[] { Math.Min(x, p.Pos[0]) - 0.44, y, Math.Min(z, p.Pos[2]) - 0.44,
                                       Math.Max(x, p.Pos[0]) + 0.44, y + ChestHeight, Math.Max(z, p.Pos[2]) + 0.44 };
                    }
                    return new[] { x - 0.44, y, z - 0.44, x + 0.44, y + ChestHeight, z + 0.44 };
                case "bed":
                    var (fx, fz) = f.Facing;
                    double hx = Math.Abs(fx) > 0.5 ? 1.0 : 0.5, hz = Math.Abs(fx) > 0.5 ? 0.5 : 1.0;
                    return new[] { x - hx, y, z - hz, x + hx, y + BedHeight, z + hz };
                case "boat":
                    return f.Box;
            }
            return null;
        }

        /// <summary>
        /// A ship's collision: the design's voxels, plus closed doors and gates, plus the chests, beds and cargo aboard.
        /// </summary>
        public static void ApplySolids(ShipDefinition def, VoxelGrid grid, System.Func<FixtureDef, bool> isOpen, System.Func<FixtureDef, ItemStack> attached,
            System.Func<FixtureDef, bool> absent = null)
        {
            grid.CopyFrom(def.Grid);
            foreach (var f in def.Fixtures)
            {
                // (0.3.21, step C) a gun place without its gun: nothing there
                if (absent != null && absent(f)) continue;
                if (SolidBox(f, def, isOpen, attached) is double[] box) grid.Fill(box);
            }
        }

        /// <summary>The space a fixture fills in the ship's collision right now (a shut door or gate, a chest or boat put there, a gun where it stands run out), or null.</summary>
        public static double[] SolidBox(FixtureDef f, ShipDefinition def, System.Func<FixtureDef, bool> isOpen, System.Func<FixtureDef, ItemStack> attached)
        {
            if (f.Opens && f.Box != null && !isOpen(f)) return f.Box;
            if (f.Type == "gun" && f.Box != null) return f.Box;
            if (f.Type == "attach" && attached(f) is ItemStack st) return AttachBox(f, st, def);
            return null;
        }

        /// <summary>
        /// What a ray (ship-local, dir normalised) picks among the fixtures: the nearest selection box that isn't behind
        /// the ship's collision. A fixture that fills cells of that collision itself (a boat on the gallows, a chest, a
        /// shut door) may be what the ray meets first; that doesn't hide it (the cells stick out a little past its box,
        /// the collision being on a 0.25 grid). Nor does its own body in the design's collision (<see cref="BodyOf"/>:
        /// the hearth's bricks round its firebed): the ray meeting that body picks it, there. visible: whether a fixture
        /// can be picked at all.
        /// </summary>
        public static FixtureDef Pick(ShipDefinition def, VoxelGrid grid, Vec3d o, Vec3d d, double range, System.Func<FixtureDef, bool> visible,
            System.Func<FixtureDef, bool> isOpen, System.Func<FixtureDef, ItemStack> attached, out double dist, System.Collections.Generic.IEnumerable<FixtureDef> extra = null,
            System.Func<FixtureDef, bool> absent = null, System.Func<FixtureDef, Vec3d, Vec3d, double> posedPick = null)
        {
            dist = double.MaxValue;
            FixtureDef best = null;
            double wall = grid.Raycast(o, d, range);
            // (0.3.18) and a hearth's or an oven's put aboard (their stand-in fixtures)
            foreach (var f in extra == null ? def.Fixtures : System.Linq.Enumerable.Concat(def.Fixtures, extra))
            {
                // ((0.1.0-dev.9, note 61) an open gate where its leaf stands open, not the gap it shuts)
                var box = PickBox(f, isOpen);
                if (box == null || !visible(f) || absent != null && absent(f)) continue;
                double t = f.Type == "gun" && posedPick != null ? posedPick(f,o,d) : RayBox(o, d, box);
                // (0.3.7: the hearth's firebed is sunk inside its bricks, which are solid, so the ray always met them first
                // and the crosshair named the forecastle's woodwork: nothing on the hearth could be clicked)
                if (wall <= range && BodyOf(f) is double[] body && grid.CellsOf(body) is double[] bc &&
                    Within(o.X + d.X * wall, o.Y + d.Y * wall, o.Z + d.Z * wall, bc, 0.02))
                    t = t < 0 ? wall : Math.Min(t, wall);
                // ((25 October 2026, caravel note 91: "here opening click on gate does not register") nor the cells of her
                // collision it fills itself (a shut gate, a door): her quarter-block grid takes them out past its selection
                // box (the gate's 0.09 aft of it, to its jamb's foot), and the crosshair names it there, so a click there is its)
                else if (wall <= range && SolidBox(f, def, isOpen, attached) is double[] own && grid.CellsOf(own) is double[] oc &&
                    Within(o.X + d.X * wall, o.Y + d.Y * wall, o.Z + d.Z * wall, oc, 0.02) && (f.Opens || f.Type == "attach"))
                    t = t < 0 ? wall : Math.Min(t, wall);
                if (t < 0 || t > range || t >= dist) continue;
                double reach = wall;
                if (SolidBox(f, def, isOpen, attached) is double[] solid && grid.CellsOf(solid) is double[] cells)
                {
                    double tc = RayBox(o, d, new Cuboidd(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5]));
                    if (tc >= 0 && tc <= wall + 0.05) reach = Math.Max(reach, t);
                }
                if (t > reach + 0.05) continue;
                best = f;
                dist = t;
            }
            return best;
        }

        /// <summary>
        /// (0.1.0-dev.9, caravel note 61: "gates dont close when trying to close them when aim on open state gate. aim only at
        /// gate closed position opens them") Where a fixture is picked now: an open gate, door or hatch where its leaf stands
        /// open (<see cref="FixtureDef.OpenSel"/>), anything else at its selection box.
        /// </summary>
        public static Cuboidd PickBox(FixtureDef f, System.Func<FixtureDef, bool> isOpen) =>
            f.OpenSel != null && isOpen != null && isOpen(f) ? f.OpenSel : f.SelBox;

        /// <summary>
        /// A fixture's own body, built into the design's collision (ship-local x0 y0 z0 x1 y1 z1), or null: the hearth's
        /// (and the older stove's) bricks, its Box. Looking at it is looking at the fixture.
        /// </summary>
        public static double[] BodyOf(FixtureDef f) => (f.Type == "hearth" || f.Type == "stove" || f.Type == "oven") && f.Box?.Length == 6 ? f.Box : null;

        static bool Within(double x, double y, double z, double[] b, double eps) =>
            x >= b[0] - eps && x <= b[3] + eps && y >= b[1] - eps && y <= b[4] + eps && z >= b[2] - eps && z <= b[5] + eps;

        /// <summary>Where a ray (o + t d) enters a box: t, or -1 if it misses; 0 from inside.</summary>
        public static double RayBox(Vec3d o, Vec3d d, Cuboidd b)
        {
            double tmin = 0, tmax = double.MaxValue;
            double[] oo = { o.X, o.Y, o.Z }, dd = { d.X, d.Y, d.Z }, lo = { b.X1, b.Y1, b.Z1 }, hi = { b.X2, b.Y2, b.Z2 };
            for (int a = 0; a < 3; a++)
            {
                if (Math.Abs(dd[a]) < 1e-12)
                {
                    if (oo[a] < lo[a] || oo[a] > hi[a]) return -1;
                    continue;
                }
                double t1 = (lo[a] - oo[a]) / dd[a], t2 = (hi[a] - oo[a]) / dd[a];
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = Math.Max(tmin, t1);
                tmax = Math.Min(tmax, t2);
                if (tmin > tmax) return -1;
            }
            return tmin;
        }
    }
}

namespace Hansa
{
    /// <summary>(0.3.27, note 219) A brow's geometry (pure: the ship and the offline tests).</summary>
    public static class BrowGeometry
    {
        /// <summary>A brow's way, as a ladder of kind "brow": from its end on the quay (Bottom) to the port's sill (Top).</summary>
        public static LadderDef Ladder(FixtureDef gate, double[] end)
        {
            if (gate?.Box == null || end == null) return null;
            Port(gate, out var sill, out var inside, out _);
            double dx = end[0] - sill[0], dz = end[2] - sill[2], len = Math.Sqrt(dx * dx + dz * dz);
            double ux = len > 1e-6 ? dx / len : 0, uz = len > 1e-6 ? dz / len : 0;
            return new LadderDef
            {
                Code = "brow-" + gate.Code, Kind = "brow", Outside = true, Gate = gate.Code,
                Bottom = (double[])end.Clone(), Top = sill, ExitTop = inside,
                ExitBottom = new[] { end[0] + ux * 0.45, end[1], end[2] + uz * 0.45 }
            };
        }

        /// <summary>An entry port's sill (its outside, at her deck), a spot on her deck inside it, and outward (the side's sign).</summary>
        public static void Port(FixtureDef gate, out double[] sill, out double[] inside, out int s)
        {
            var b = gate.Box;
            s = gate.Pos[2] >= 0 ? 1 : -1;
            double x = (b[0] + b[3]) / 2, outer = s > 0 ? b[5] : b[2], inner = s > 0 ? b[2] : b[5];
            sill = new[] { x, b[1], outer + s * 0.05 };
            inside = new[] { x, b[1], inner - s * 0.75 };
        }
    }
}
