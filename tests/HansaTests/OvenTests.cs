using System;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>A clay oven's contents for the offline checks: n firewood, or loaves of dough (vanilla's bread dough's baking).</summary>
        class FakeOven : IOvenContents
        {
            public int Fuel;
            public readonly bool[] Dough = new bool[OvenFire.Capacity];
            public readonly bool[] Bread = new bool[OvenFire.Capacity];
            public OvenFire Fire;
            public int FuelCount => Fuel;
            public void FuelBurntOut() => Fuel = 0;
            public bool Has(int i) => Dough[i] || Bread[i];
            public int MaxTemperature(int i) => 0;
            public bool Baking(int i, out float levelFrom, out float levelTo, out float startScaleY, out float endScaleY)
            {
                // (vanilla's bread dough: bakes from 0 to 1, rising from 0.5 to 1 high; the bread: on from 1 to 2, a little lower)
                if (Dough[i]) { levelFrom = 0; levelTo = 1; startScaleY = 0.5f; endScaleY = 1; return true; }
                levelFrom = 1; levelTo = 2; startScaleY = 1; endScaleY = 0.95f;
                return Bread[i];
            }
            public bool Bake(int i)
            {
                if (!Dough[i]) return false;
                Dough[i] = false;
                Bread[i] = true;
                float t = Fire.Slots[i].Temp;
                Fire.Slots[i] = new OvenSlot(160, 150, 1, 1, t);
                return true;
            }
            public int EnvironmentTemperature => 20;
        }

        /// <summary>
        /// The clay oven aboard (the user's note 51, OvenFire: vanilla's BlockEntityOven ported): its temperature (vanilla's
        /// ChangeTemperature), lit with six logs it burns 60 s and gets hot (with one, slower), what's in it doesn't heat
        /// while it burns; after, loaves of dough heat from it, rise and bake into bread; the oven cools.
        /// </summary>
        static void Oven()
        {
            Console.WriteLine("the clay oven: its fire, its heat, baking bread");
            // vanilla's ChangeTemperature: the further off the faster; cooling at half the pace; within a degree it's there
            float up = OvenFire.ChangeTemperature(20, 300, 1), down = OvenFire.ChangeTemperature(300, 20, 1), near = OvenFire.ChangeTemperature(299.5f, 300, 0.1f);
            Check(up > 20 && up < 300 && 300 - down < up - 20 && near == 300,
                $"its temperature changes as vanilla's oven's (20 → {up:0.0} in a second toward 300, 300 → {down:0.0} cooling, the last degree at once)");

            (float peak, float seconds) Burn(int logs)
            {
                var fire = new OvenFire();
                var c = new FakeOven { Fuel = logs, Fire = fire };
                fire.TryIgnite(logs);
                float t = 0, peak = 0;
                while (fire.Burning && t < 600) { fire.Tick(0.1f, c); t += 0.1f; peak = Math.Max(peak, fire.Temperature); }
                return (peak, t);
            }
            var six = Burn(6);
            var one = Burn(1);
            Check(Math.Abs(six.seconds - 60) < 0.5 && Math.Abs(one.seconds - 40) < 0.5 && six.peak > 250 && one.peak < six.peak,
                $"lit with six logs it burns {six.seconds:0} s (vanilla's 45 + 5 a log, its tick × 1.25) and gets to {six.peak:0} °C; with one, {one.seconds:0} s and {one.peak:0} °C");

            // six logs burnt, then four loaves of dough in it
            {
                var fire = new OvenFire();
                var c = new FakeOven { Fuel = 6, Fire = fire };
                bool notLitWithout = !fire.TryIgnite(0);
                fire.TryIgnite(6);
                for (int i = 0; i < OvenFire.Capacity; i++) { c.Dough[i] = true; fire.Slots[i] = new OvenSlot(160, 150, 0, 0.5f, 20); }
                float t = 0;
                bool heatedWhileBurning = false;
                while (fire.Burning) { fire.Tick(0.1f, c); t += 0.1f; if (fire.Burning) heatedWhileBurning |= fire.Slots[0].Temp > 21; }
                float hot = fire.Temperature;
                float baked = -1, rose = 0;
                while (t < 1200)
                {
                    fire.Tick(0.1f, c);
                    t += 0.1f;
                    rose = Math.Max(rose, fire.Slots[1].CurHeightMul);
                    if (baked < 0 && c.Bread[0] && c.Bread[3]) baked = t;
                }
                Check(notLitWithout && !heatedWhileBurning && hot > 250 && baked > 60 && c.Bread[1] && c.Bread[2] && rose >= 0.99f && fire.Temperature < hot - 100 && !fire.Burning,
                    $"without fuel it won't light; what's in it doesn't heat while the fire burns; the fire out ({hot:0} °C), four loaves of dough heat, rise " +
                    $"(drawn {rose:0.00} high) and are bread {baked - 60:0} s later; the oven cools slowly, as vanilla's ({fire.Temperature:0} °C after {t / 60:0} minutes)");
            }
        }
    }
}
