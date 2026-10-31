using System;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// The firehearth (vanilla's firepit aboard): the fire's sums as vanilla's BlockEntityFirepit has them (its temperature
        /// curve, fuel used, four times slower idle at heat, going out and its embers, lighting it), how long a known thing
        /// cooks on a known fuel (raw red meat on firewood, a stack of it, porridge in a pot), its warmth by distance, how
        /// it's drawn, and its state and vanilla inventory saved and loaded.
        /// </summary>
        static void Hearth(ShipDefinition def)
        {
            Console.WriteLine("the firehearth: vanilla's firepit fire, cooking times, warmth, saved and loaded");
            FirepitTargets();

            // ---- the crosshair on it picks it (0.3.7, the user's note 1: its firebed is sunk inside its bricks, which are
            // solid, so the ray met them first and named the forecastle's woodwork: nothing on the hearth could be clicked)
            {
                var hearth = def.Fixtures.FirstOrDefault(f => f.Type == "hearth");
                FixtureDef PickFrom(double[] eye, double[] at)
                {
                    var o = new Vintagestory.API.MathTools.Vec3d(eye[0], eye[1], eye[2]);
                    var d = new Vintagestory.API.MathTools.Vec3d(at[0] - eye[0], at[1] - eye[1], at[2] - eye[2]);
                    d.Normalize();
                    return ShipFittings.Pick(def, def.Grid, o, d, 6, f => f.Type != "attach", f => false, f => null, out _);
                }
                double fx = hearth?.Pos[0] ?? 0, fy = hearth?.Pos[1] ?? 0, fz = hearth?.Pos[2] ?? 0, deck = def.DeckY;
                var b = hearth?.Box;
                // the note's own view: standing in the forecastle, looking on the starboard beam 36° down at it
                // (0.3.9: the forecastle moved: the same view from where it stands now)
                var noted = PickFrom(new[] { fx - 0.0, deck + 1.62, fz - 1.48 }, new[] { fx - 0.05, fy + 0.42, fz - 0.26 });
                var down = PickFrom(new[] { fx, deck + 1.7, fz - 0.4 }, new[] { fx, fy, fz });
                // (0.3.8: from inboard of the oven, which stands just forward of it now)
                var side = hearth == null ? null : PickFrom(new[] { b[3] + 0.3, deck + 1.5, b[2] - 1.2 }, new[] { b[3] - 0.05, deck + 0.7, b[2] + 0.2 });
                var wall = PickFrom(new[] { fx, deck + 1.6, 1.0 }, new[] { fx, deck + 3.0, 4.0 });
                var floor = PickFrom(new[] { fx, deck + 1.6, 1.0 }, new[] { fx, deck, 1.2 });
                Check(hearth != null && noted == hearth && down == hearth && side == hearth && wall != hearth && floor != hearth,
                    $"the crosshair on the hearth picks it: from where note 1 was written ({noted?.Code ?? "nothing"}), into its firebed from over it " +
                    $"({down?.Code ?? "nothing"}), on its forward end ({side?.Code ?? "nothing"}); not the side over its hood ({wall?.Code ?? "the hull"}) nor the deck by it " +
                    $"({floor?.Code ?? "the deck"})");
            }

            // ---- vanilla's changeTemperature: 1 °C a second plus 1/28 of the gap, never past the target
            {
                float a = HearthFire.ChangeTemperature(20, 700, 0.1f), b = HearthFire.ChangeTemperature(700, 20, 0.1f), c = HearthFire.ChangeTemperature(699.5f, 700, 0.1f);
                Check(Math.Abs(a - (20 + 0.1f * (1 + 680f / 28))) < 1e-4 && Math.Abs(b - (700 - 0.1f * (1 + 680f / 28))) < 1e-4 && c == 700,
                    $"vanilla's temperature step: 20 → 700 °C in 0.1 s goes to {a:0.000} (1 °C/s + 1/28 of the gap a second), down the same, and snaps the last degree ({c})");
            }

            // ---- one firewood (700 °C, 24 s) with nothing to cook: it burns its 24 s, too short to get the fire to its heat
            {
                var fire = new HearthFire();
                var c = new HearthSimContents { FuelCount = 1 };
                fire.Ignited(0);
                var r = Track(fire, c, 60, null, () => !fire.IsBurning && c.FuelCount == 0 && fire.FurnaceTemperature < 400);
                Log($"    one firewood, nothing in it: lit at {r.LitAt:0.0} s, out at {r.WentOut:0.0} s, the fire at most {r.Peak:0} °C");
                Check(r.LitAt <= 0.11 && c.FuelCount == 0 && Math.Abs(r.WentOut - r.LitAt - 24) < 0.15 && r.Peak > 350 && r.Peak < 480,
                    $"one firewood: it catches at the first tick and burns its 24 s ({r.WentOut - r.LitAt:0.0}); the fire heats toward 700 °C (1 °C/s and 1/28 of the gap " +
                    $"a second) and gets to {r.Peak:0}");
                Check(fire.BurnState == "extinct" && fire.CanIgniteFuel && !fire.IsBurning && fire.HeatStrength == HearthFire.HeatEmbers,
                    "out of fuel it goes out: extinct, its embers still relight new fuel, a little heat");
                // new fuel within two hours catches by itself
                c.FuelCount = 1;
                fire.Tick(0.1f, c);
                Check(fire.IsBurning && fire.BurnState == "lit" && c.FuelCount == 0, "new fuel on the embers catches by itself");
                // two game hours after it went out the embers are cold
                Track(fire, c, 60, null, () => !fire.IsBurning);
                double outAt = fire.ExtinguishedTotalHours;
                c.Hours = outAt + HearthFire.EmbersHours - 0.01;
                fire.Tick(0.1f, c);
                bool still = fire.CanIgniteFuel;
                c.Hours = outAt + HearthFire.EmbersHours + 0.01;
                fire.Tick(0.1f, c);
                c.FuelCount = 1;
                fire.Tick(0.1f, c);
                Check(still && fire.BurnState == "cold" && !fire.CanIgniteFuel && !fire.IsBurning && c.FuelCount == 1 && fire.HeatStrength == 0,
                    "two game hours after it went out its embers are cold: new fuel doesn't catch, no heat");
            }
            {
                // four firewood with nothing to cook: once the fire is within 50 °C of its heat the wood burns four times slower
                var fire = new HearthFire();
                var c = new HearthSimContents { FuelCount = 4 };
                fire.Ignited(0);
                double t600 = double.NaN, t650 = double.NaN;
                var r = Track(fire, c, 400, (t, f) =>
                {
                    if (double.IsNaN(t600) && f.FurnaceTemperature >= 600) t600 = t;
                    if (double.IsNaN(t650) && f.FurnaceTemperature >= 650) t650 = t;
                }, () => !fire.IsBurning && c.FuelCount == 0);
                double expectOut = t650 + (4 * 24 - (t650 - r.LitAt)) * HearthFire.EmptyBurnTimeBonus;
                Log($"    four firewood, nothing in it: 600 °C at {t600:0.0} s, within 50 °C of its 700 at {t650:0.0} s, out at {r.WentOut:0.0} s (peak {r.Peak:0})");
                Check(t650 > 50 && t650 < 90 && r.Peak <= 700 && Math.Abs(r.WentOut - expectOut) < 0.6 && fire.BurnState == "extinct",
                    $"four firewood with nothing to cook: 600 °C at {t600:0.0} s, its heat at {t650:0.0} s, from then on four times slower: out at {r.WentOut:0.0} s " +
                    $"(expected {expectOut:0.0}, 96 s of wood)");
                // cooling: back to the air
                var cool = new HearthFire { FurnaceTemperature = 700 };
                double tc = 0;
                while (cool.FurnaceTemperature > HearthFire.EnvironmentTemperature && tc < 600) { cool.Tick(0.1f, new HearthSimContents()); tc += 0.1; }
                Check(cool.FurnaceTemperature == HearthFire.EnvironmentTemperature && tc > 40 && tc < 120, $"out, it cools from 700 °C to the air's 20 in {tc:0} s");
            }

            // ---- lighting it, as vanilla: fuel in it, not burning, held more than 3 s
            {
                var fire = new HearthFire();
                Check(fire.IgnitableState(5, true) == HearthFire.IgniteState.NotIgnitablePreventDefault && fire.IgnitableState(1, false) == HearthFire.IgniteState.Ignitable &&
                      fire.IgnitableState(3, false) == HearthFire.IgniteState.Ignitable && fire.IgnitableState(3.2f, false) == HearthFire.IgniteState.IgniteNow,
                    "lighting it: not without fuel; held to it it's catching, after more than 3 s it catches");
                var c = new HearthSimContents { FuelCount = 2 };
                fire.Tick(0.1f, c);
                Check(!fire.IsBurning && c.FuelCount == 2, "fuel in a cold hearth doesn't burn by itself");
                fire.Ignited(5);
                fire.Tick(0.1f, c);
                Check(fire.IsBurning && fire.BurnState == "lit" && c.FuelCount == 1 && fire.MaxTemperature == 700 && fire.MaxFuelBurnTime == 24 &&
                      fire.IgnitableState(4, false) == HearthFire.IgniteState.NotIgnitablePreventDefault && fire.HeatStrength == HearthFire.HeatBurning,
                    "lit, the first fuel catches at the next tick (one firewood used: 700 °C for 24 s), it can't be lit again while it burns, it gives its full heat");
                fire.Douse();
                Check(!fire.IsBurning && fire.BurnState == "cold" && !fire.CanIgniteFuel && c.FuelCount == 1, "doused (she's going down): out and cold at once, the fuel left in it");
            }

            // ---- cooking: raw red meat (150 °C, 30 s) on firewood, as vanilla's numbers have it
            double meatSecs;
            {
                var fire = new HearthFire();
                var c = new HearthSimContents { FuelCount = 4, InputCount = 1, InputMelting = 150, InputDuration = 30 };
                fire.Ignited(0);
                double at150 = double.NaN;
                int burnt = -1;
                var r = Track(fire, c, 300, (t, f) =>
                {
                    if (double.IsNaN(at150) && c.InputTempNow >= 150) at150 = t;
                    if (burnt < 0 && c.OutputCount > 0) burnt = 4 - c.FuelCount;
                });
                meatSecs = r.FirstCooked;
                Log($"    one raw red meat on firewood: 150 °C at {at150:0.0} s, cooked at {r.FirstCooked:0.0} s ({c.OutputTempNow:0} °C), {burnt} firewood lit by then");
                Check(r.FirstCooked > at150 + 8 && r.FirstCooked < at150 + 30 && at150 > 3 && at150 < 20 && c.OutputCount == 1 && c.InputCount == 0,
                    $"one raw red meat on firewood cooks in {r.FirstCooked:0.0} s: at 150 °C after {at150:0.0} s, then its 30 s of cooking, faster the hotter it gets");
            }
            {
                // a stack of four: heated as one, cooked one after another
                var fire = new HearthFire();
                var c = new HearthSimContents { FuelCount = 10, InputCount = 4, InputMelting = 150, InputDuration = 30 };
                fire.Ignited(0);
                double last = double.NaN;
                int seen = 0;
                Track(fire, c, 600, (t, f) => { if (c.OutputCount > seen) { seen = c.OutputCount; last = t; } });
                Log($"    four raw red meat on firewood: all cooked at {last:0.0} s");
                Check(c.OutputCount == 4 && last > meatSecs * 1.8 && last < meatSecs * 4, $"a stack of four heats as one and cooks one after another: all four in {last:0.0} s");
            }
            {
                // a pot of porridge (2 grain: vanilla's pot melts at 100 °C at least, cooks 40 s at least, and goes no hotter than 200 °C)
                var fire = new HearthFire();
                var c = new HearthSimContents { FuelCount = 4, InputCount = 1, InputMelting = 100, InputDuration = 40, InputMax = 200 };
                fire.Ignited(0);
                float hottest = 0;
                var r = Track(fire, c, 300, (t, f) => hottest = Math.Max(hottest, c.InputTempNow));
                Log($"    a pot of porridge on firewood: cooked at {r.FirstCooked:0.0} s, the pot never over {hottest:0} °C");
                Check(r.FirstCooked > 40 && r.FirstCooked < 80 && hottest <= 200 && hottest > 150, $"a pot of porridge cooks in {r.FirstCooked:0.0} s, the pot no hotter than its 200 °C");
            }
            {
                // a weaker fuel: sticks (700 °C, 8 s each)
                var fire = new HearthFire();
                var c = new HearthSimContents { FuelCount = 8, FuelDuration = 8, InputCount = 1, InputMelting = 150, InputDuration = 30 };
                fire.Ignited(0);
                int burnt = -1;
                var r = Track(fire, c, 300, (t, f) => { if (burnt < 0 && c.OutputCount > 0) burnt = 8 - c.FuelCount; });
                Check(r.FirstCooked > 0 && Math.Abs(r.FirstCooked - meatSecs) < 0.5 && burnt == (int)Math.Ceiling(r.FirstCooked / 8),
                    $"on sticks (700 °C, 8 s each) it cooks in the same time, {r.FirstCooked:0.0} s, lighting {burnt} of them");
                var none = new HearthFire();
                var cold = new HearthSimContents { InputCount = 1 };
                var rc = Track(none, cold, 60, null);
                Check(double.IsNaN(rc.FirstCooked) && cold.InputTempNow == HearthFire.EnvironmentTemperature, "unlit, nothing cooks");
            }

            // ---- its warmth: vanilla's falloff from the firepit block's nearer edge
            {
                double inside = HearthFire.DistanceSqToNearerEdge(0, 0, 0, 0.5, 0.75, 0.5);
                double beside = HearthFire.DistanceSqToNearerEdge(0, 0, 0, 2.5, 0.75, 0.5);
                double far = HearthFire.DistanceSqToNearerEdge(0, 0, 0, 5, 0.75, 0.5);
                float w0 = HearthFire.Warmth(10, inside, true), w1 = HearthFire.Warmth(10, beside, true), w4 = HearthFire.Warmth(10, far, true), w4o = HearthFire.Warmth(10, far, false);
                Check(inside == 0 && Math.Abs(beside - 2.25) < 1e-9 && w0 == 10 && w1 > 8.5 && w1 < 9.5 && w4 < w1 && w4 > 3 && w4o < w4,
                    $"its warmth: full by the fire ({w0:0.0}, {w1:0.0} 1.5 blocks off), {w4:0.0} 4 blocks off in a closed room, {w4o:0.0} in the open");
                var h = def.Fixture("hearth");
                var room = def.Rooms.FirstOrDefault(r => r.Code == h?.Room);
                Check(h?.Fire != null && room != null && room.Enclosed, $"the hearth warms a closed room, its own ({h?.Room})");
            }

            // ---- how it's drawn: as vanilla's firepit (a cold one with no fuel is ashes)
            Check(HearthFire.DrawnBurnState("cold", true) == "extinct" && HearthFire.DrawnBurnState("cold", false) == "cold" && HearthFire.DrawnBurnState("lit", true) == "lit",
                "drawn as vanilla draws its firepit: cold with fuel in it, ashes (extinct) with none, lit");

            // ---- saved and loaded: the fire's state by vanilla's keys, and vanilla's inventory
            {
                var fire = new HearthFire
                {
                    FurnaceTemperature = 543.5f, MaxTemperature = 700, InputStackCookingTime = 12.5f, FuelBurnTime = 7.25f, MaxFuelBurnTime = 24,
                    SmokeLevel = 1, CanIgniteFuel = true, ExtinguishedTotalHours = 1234.5, BurnState = "lit"
                };
                var t = new TreeAttribute();
                fire.ToTree(t);
                var back = new TreeAttribute();
                back.FromBytes(t.ToBytes());
                var again = new HearthFire();
                again.FromTree(back);
                Check(again.FurnaceTemperature == 543.5f && again.MaxTemperature == 700 && again.InputStackCookingTime == 12.5f && again.FuelBurnTime == 7.25f &&
                      again.MaxFuelBurnTime == 24 && again.CanIgniteFuel && again.ExtinguishedTotalHours == 1234.5 && again.BurnState == "lit" &&
                      t.HasAttribute("oreCookingTime") && t.HasAttribute("furnaceTemperature"),
                    "the fire's state saved (by vanilla's firepit's own keys) and loaded back");
                var none = new HearthFire();
                none.FromTree(new TreeAttribute());
                Check(none.BurnState == "cold" && !none.IsBurning && none.FurnaceTemperature == HearthFire.EnvironmentTemperature, "a hearth saved before 0.3.6's fire loads cold");

                var inv = new InventorySmelting(null, null);
                Check(inv.Count == 7 && inv[0] is ItemSlotSurvival && inv[1] is ItemSlotInput && inv[2] is ItemSlotOutput &&
                      Enumerable.Range(3, 4).All(i => inv[i] is ItemSlotWatertight) && inv.CookingSlots.Length == 0 && inv.Slots.Length == 4,
                    "vanilla's firepit inventory: fuel, input, output and four watertight cooking slots, closed without a pot");
                var firewood = new Item(17) { Code = new AssetLocation("game", "firewood") };
                var meat = new Item(23) { Code = new AssetLocation("game", "redmeat-raw") };
                var pot = new Block { BlockId = 41, Code = new AssetLocation("game", "claypot-red-fired"),
                    Attributes = new JsonObject(JObject.Parse("{ cookingContainerSlots: 4, maxContainerSlotStackSize: 6, storageType: 1 }")) };
                var grain = new Item(29) { Code = new AssetLocation("game", "grain-spelt") };
                inv[0].Itemstack = new ItemStack(firewood, 5);
                inv[1].Itemstack = new ItemStack(pot, 1);
                inv[2].Itemstack = new ItemStack(meat, 2);
                inv[3].Itemstack = new ItemStack(grain, 1);
                inv[4].Itemstack = new ItemStack(grain, 1);
                Check(inv.HaveCookingContainer && inv.CookingSlots.Length == 4, "a pot in the input opens the four cooking slots");
                var saved = new TreeAttribute();
                inv.ToTreeAttributes(saved);
                var loadedTree = new TreeAttribute();
                loadedTree.FromBytes(saved.ToBytes());
                var loaded = new InventorySmelting(null, null);
                loaded.FromTreeAttributes(loadedTree);
                bool same = Enumerable.Range(0, 7).All(i => (inv[i].Itemstack == null) == (loaded[i].Itemstack == null) &&
                    (inv[i].Itemstack == null || inv[i].Itemstack.Id == loaded[i].Itemstack.Id && inv[i].Itemstack.Class == loaded[i].Itemstack.Class &&
                     inv[i].Itemstack.StackSize == loaded[i].Itemstack.StackSize));
                Check(same && saved.GetInt("qslots") == 7, "its inventory saved and loaded: the fuel, the pot, the cooked meat and the pot's grain, each in its slot");
            }
        }

        static void FirepitTargets()
        {
            foreach (var role in new[] { PieceRole.Hearth, PieceRole.Oven })
            {
                var p = new ShipPiece { Id = 32, Role = role, Box = new[] { -9.0, 1.125, -1.5, -8, 2.125, -.5 } };
                var f = new FixtureDef { Code = p.StorageCode, Type = role == PieceRole.Hearth ? "hearth" : "oven",
                    Box = (double[])p.Box.Clone(), Pos = new[] { -8.5, 1.375, -1.0 },
                    Sel = new[] { -8.9375, 1.375, -1.4375, -8.0625, 1.975, -.5625 } };
                var target = FirepitTarget.ForPiece(p, role == PieceRole.Hearth ? f : null, role == PieceRole.Oven ? f : null);
                Check(target == f && !ReferenceEquals(f.Sel, p.Box) && f.Pos[1] != p.Box[1],
                    $"{role}: packet lookup keeps the active fire fixture when its firebed and selection differ from the furniture box");
                p.Box = (double[])p.Box.Clone();
                Check(FirepitTarget.ForPiece(p, role == PieceRole.Hearth ? f : null, role == PieceRole.Oven ? f : null) == f,
                    $"{role}: a network/save reload of identical furniture bounds keeps the fire target");
                p.Id++;
                Check(FirepitTarget.ForPiece(p, f, f) == null, $"{role}: another placed fire cannot reuse a removed fire's target");
                p.Id--; p.Box[0] -= 1;
                Check(FirepitTarget.ForPiece(p, f, f) == null, $"{role}: moved furniture requires a fresh fire fixture");
            }
            var oven = new FixtureDef { Pos = new[] { -8.5, 2.125, -2.0 }, Fire = new[] { -8.5, 2.275, -2.0 } };
            Check(FirepitTarget.SmokeOutlet(oven) == oven.Fire, "an unflued oven emits smoke at its fire");
            oven.Chimney = new[] { -8.5, 5.0, -2.0 };
            Check(FirepitTarget.SmokeOutlet(oven) == oven.Chimney, "a flued oven emits smoke at the chimney mouth");
        }

        static void Log(string s) => Console.WriteLine(s);

        /// <summary>Run a hearth fire in 100 ms ticks (as the server does), watching it each tick, till it's been so long or until says so.</summary>
        static HearthSimResult Track(HearthFire fire, HearthSimContents c, double seconds, Action<double, HearthFire> watch, Func<bool> until = null)
        {
            var r = new HearthSimResult { FirstCooked = double.NaN, WentOut = double.NaN, LitAt = double.NaN };
            int cooked = c.OutputCount;
            bool was = fire.IsBurning;
            for (int i = 1; i <= (int)Math.Round(seconds * 10); i++)
            {
                fire.Tick(0.1f, c);
                c.Hours += 0.1 * HearthSimContents.GameHoursPerSecond;
                double t = i * 0.1;
                r.Peak = Math.Max(r.Peak, fire.FurnaceTemperature);
                if (fire.IsBurning && double.IsNaN(r.LitAt)) r.LitAt = t;
                if (double.IsNaN(r.FirstCooked) && c.OutputCount > cooked) r.FirstCooked = t;
                if (was && !fire.IsBurning && double.IsNaN(r.WentOut)) r.WentOut = t;
                was = fire.IsBurning;
                watch?.Invoke(t, fire);
                r.Seconds = t;
                if (until != null && until()) break;
            }
            return r;
        }
    }
}
