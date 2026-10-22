using System;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.1.0-dev.7, the user on the caravel: "Trying to use telescope from Astra on deck and almost everywhere it tries
        /// to use cannon instead") A right-click lays a gun only with its own selection box in the crosshair (the fixture the
        /// ray meets first), within reach of its breech: from every spot her decks stand a man on, rays all round (the sky,
        /// the sea, her deck and bulwarks) pick no gun unless they enter that gun's box. At a gun captain's place the gun
        /// takes the click, unless what's in hand has a right-click of its own (HeldUse: a telescope, food).
        ///   dotnet run --project tests/HansaTests -- gunclick
        /// </summary>
        static void GunClicks(string root)
        {
            Console.WriteLine("clicks by the guns");
            const double range = 4.5, reach = 2.5, eyeHeight = 1.62;
            foreach (var code in new[] { "caravel", "carrack", "hansa-carrack" })
            {
                string path = Path.Combine(root, "assets", "hansa", "config", "ships", code + ".json");
                if (!File.Exists(path)) { Console.WriteLine($"  ({code}.json not there)"); continue; }
                var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
                def.Init();
                var g = def.Grid;
                var guns = def.Fixtures.Where(f => f.Type == "gun" && f.SelBox != null).ToArray();
                if (guns.Length == 0) { Console.WriteLine($"  --   {code}: no guns"); continue; }
                int spots = 0, byBreech = 0, rays = 0, oldTook = 0;
                string wrong = null;
                for (double x = Math.Floor(-def.Length / 2); x <= def.Length / 2; x += 0.5)
                    for (double z = Math.Floor(-def.Beam / 2); z <= def.Beam / 2; z += 0.5)
                        for (double y = def.KeelY; y <= def.DeckY + 8; y += 0.125)
                        {
                            if (!DeckMover.HasFloor(g, x, y, z) ||
                                g.Collides(x - DeckMover.HalfWidth, y, z - DeckMover.HalfWidth, x + DeckMover.HalfWidth, y + DeckMover.Height, z + DeckMover.HalfWidth)) continue;
                            // clear of the guns: a block from each one's box on this deck
                            if (guns.Any(f => Math.Abs(y - f.Sel[1]) < 1.5 && x > f.Sel[0] - 1 && x < f.Sel[3] + 1 && z > f.Sel[2] - 1 && z < f.Sel[5] + 1)) continue;
                            spots++;
                            // (till 0.1.0-dev.6 a right-click here, with nothing in the crosshair, laid the nearest gun)
                            bool old = guns.Any(f => GunLaying.InReach(f, new GunLay(0, f.Elevation), x, y, z, reach));
                            if (old) byBreech++;
                            var eye = new Vec3d(x, y + eyeHeight, z);
                            for (int yaw = 0; yaw < 360; yaw += 15)
                                for (int pitch = -75; pitch <= 75; pitch += 15)
                                {
                                    double ya = yaw * Math.PI / 180, pa = pitch * Math.PI / 180;
                                    var dir = new Vec3d(Math.Cos(pa) * Math.Cos(ya), Math.Sin(pa), Math.Cos(pa) * Math.Sin(ya));
                                    var hit = ShipFittings.Pick(def, g, eye, dir, range, f => true, f => false, f => null, out _);
                                    bool inReach = hit != null && GunLaying.InReach(hit, new GunLay(0, hit.Elevation), x, y, z, reach);
                                    var lays = GunButtons.LaysGun(null, hit, inReach, false);
                                    rays++;
                                    bool enters = lays != null && ShipFittings.RayBox(eye, dir, lays.SelBox) is double t && t >= 0 && t <= range;
                                    if (lays == null && old) oldTook++;
                                    if (lays != null && (!enters || !inReach))
                                        wrong ??= $"from {x:0.0} {y:0.000} {z:0.0} yaw {yaw} pitch {pitch}: {lays.Code}";
                                }
                        }
                Check(spots > 0 && wrong == null, $"{code}: from {spots} deck spots clear of her {guns.Length} guns ({rays} rays), a right-click lays a gun only " +
                                                  $"with its own box in the crosshair, in reach{(wrong == null ? "" : ": not " + wrong)}");
                Console.WriteLine($"         ({byBreech} of those spots within {reach} of a breech, where till 0.1.0-dev.6 the nearest gun could take a click: {oldTook} rays from them now go to what's in hand)");
                // aimed at it from its gun captain's place: that gun
                int aimed = guns.Count(f =>
                {
                    if (f.Seat == null) return false;
                    var eye = new Vec3d(f.Seat[0], f.Seat[1] + eyeHeight, f.Seat[2]);
                    var b = f.SelBox;
                    var dir = new Vec3d((b.X1 + b.X2) / 2 - eye.X, (b.Y1 + b.Y2) / 2 - eye.Y, (b.Z1 + b.Z2) / 2 - eye.Z).Normalize();
                    var hit = ShipFittings.Pick(def, g, eye, dir, range, f2 => true, f2 => false, f2 => null, out _);
                    bool inReach = hit != null && GunLaying.InReach(hit, new GunLay(0, hit.Elevation), f.Seat[0], f.Seat[1], f.Seat[2], reach);
                    return GunButtons.LaysGun(null, hit, inReach, false) == f;
                });
                Check(aimed == guns.Count(f => f.Seat != null) && aimed > 0, $"{code}: each gun aimed at from its gun captain's place lays it ({aimed} of {guns.Length})");
            }
            var gun = new FixtureDef { Type = "gun", Code = "gun-test" };
            Check(GunButtons.LaysGun(gun, null, false, false) == gun && GunButtons.LaysGun(gun, null, false, true) == null &&
                  GunButtons.LaysGun(null, gun, false, false) == null && GunButtons.LaysGun(null, gun, true, true) == gun,
                  "at a gun captain's place the gun takes a right-click, not with a telescope in hand; aimed at, only in reach");
            var scope = new TestScope { Code = new AssetLocation("astraterra", "brasstelescope") };
            var plain = new Item { Code = new AssetLocation("game", "stick") };
            var food = new Item { Code = new AssetLocation("game", "bread-spelt"), NutritionProps = new FoodNutritionProperties() };
            var priority = new Item { Code = new AssetLocation("other", "thing"), HeldPriorityInteract = true };
            var ours = new TestScope { Code = new AssetLocation("hansa", "surveyorschalk") };
            Check(HeldUse.OwnRightClick(scope) && HeldUse.OwnRightClick(food) && HeldUse.OwnRightClick(priority) &&
                  !HeldUse.OwnRightClick(plain) && !HeldUse.OwnRightClick(ours) && !HeldUse.OwnRightClick(new Block()) && !HeldUse.OwnRightClick(null),
                  "an item with its own right-click (a telescope's class, food, heldPriorityInteract) keeps it; a stick, a block, Hansa's own don't");
        }

        /// <summary>An item whose class has its own right-click (as Astra's ItemTelescope).</summary>
        class TestScope : Item
        {
            public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
            {
                handling = EnumHandHandling.PreventDefault;
            }
        }
    }
}
