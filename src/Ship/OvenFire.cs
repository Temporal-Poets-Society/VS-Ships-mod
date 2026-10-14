using System;

namespace Hansa
{
    /// <summary>What a baked thing in the oven is doing (vanilla's OvenItemData, number for number).</summary>
    public sealed class OvenSlot
    {
        /// <summary>Over this it browns (°C; 0: 160); seconds to bake (its melting duration × 10); how far it's baked; how risen it's drawn; its temperature.</summary>
        public float BrowningPoint, TimeToBake, BakedLevel, CurHeightMul, Temp = 20;

        public OvenSlot() { }

        public OvenSlot(float browning, float time, float baked, float heightMul, float temp)
        {
            BrowningPoint = browning;
            TimeToBake = time;
            BakedLevel = baked;
            CurHeightMul = heightMul;
            Temp = temp;
        }
    }

    /// <summary>What's in the oven, as its burn tick sees it (the ship's vanilla inventory and vanilla's collectibles; a stand-in offline).</summary>
    public interface IOvenContents
    {
        /// <summary>How much fuel (firewood: isClayOvenFuel) lies in it, 0: none.</summary>
        int FuelCount { get; }
        /// <summary>The fuel has burnt out: it's gone (or becomes what it smelts into).</summary>
        void FuelBurntOut();
        /// <summary>Is there something to bake in this slot (not the fuel)?</summary>
        bool Has(int slot);
        /// <summary>The most its temperature goes to (its own; 0: no limit).</summary>
        int MaxTemperature(int slot);
        /// <summary>Its baking: the levels it's baked from and to, the height it's drawn at from and to; false: it has none.</summary>
        bool Baking(int slot, out float levelFrom, out float levelTo, out float startScaleY, out float endScaleY);
        /// <summary>It's baked through: it becomes what it bakes into (a new slot's data, its temperature kept), true if it did.</summary>
        bool Bake(int slot);
        /// <summary>The air's temperature there (°C).</summary>
        int EnvironmentTemperature { get; }
    }

    /// <summary>
    /// Vanilla's clay oven (BlockEntityOven) rebuilt aboard, its fire and baking ported number for number, pure (the
    /// server runs it against the ship's inventory, the offline checks against a stand-in): fuel (up to six firewood) is lit
    /// and burns 45 s and 5 more a log; while it burns the oven heats toward 300 °C (faster with more fuel); once it's out
    /// the oven cools slowly and only then what's in it (up to four, or one large thing) heats from it and bakes: browning
    /// over its browning point, faster the hotter, rising or sinking as it bakes, and baked through it becomes what it
    /// bakes into (bread dough to bread, a raw pie to a pie: vanilla's BakingProperties, else its smelting).
    /// </summary>
    public class OvenFire
    {
        public const float MaxTemperature = 300;
        public const int Capacity = 4, FuelCapacity = 6;
        /// <summary>Vanilla's BakingStageThreshold: the height it's drawn at goes in hundredths.</summary>
        public const int StageThreshold = 100;

        public bool Burning;
        public float Temperature = 20, FuelBurnTime;
        public readonly OvenSlot[] Slots = new OvenSlot[Capacity];

        public OvenFire()
        {
            for (int i = 0; i < Capacity; i++) Slots[i] = new OvenSlot();
        }

        /// <summary>Can it be lit (fuel in it, not burning)?</summary>
        public bool CanIgnite(int fuelCount) => fuelCount > 0 && !Burning;

        /// <summary>Light it (vanilla's TryIgnite): it burns 45 s and 5 more for each log in it.</summary>
        public bool TryIgnite(int fuelCount)
        {
            if (!CanIgnite(fuelCount)) return false;
            Burning = true;
            FuelBurnTime = 45 + fuelCount * 5;
            return true;
        }

        /// <summary>Put out at once (she went down, or the oven was switched off): cold, the fuel left as it is.</summary>
        public void Douse(int environment)
        {
            Burning = false;
            FuelBurnTime = 0;
            Temperature = environment;
            foreach (var s in Slots) s.Temp = Math.Min(s.Temp, environment);
        }

        /// <summary>
        /// Vanilla's burn tick (every 100 ms): dt seconds (× 1.25, as vanilla). True when what it holds changed (the fuel
        /// burnt out, something baked through or rose a step): send it.
        /// </summary>
        public bool Tick(float dt, IOvenContents c)
        {
            dt *= 1.25f;
            bool changed = false;
            if (FuelBurnTime > 0)
            {
                FuelBurnTime -= dt;
                if (FuelBurnTime <= 0)
                {
                    FuelBurnTime = 0;
                    Burning = false;
                    c.FuelBurntOut();
                    if (c.FuelCount == 0) foreach (var s in Slots) s.CurHeightMul = 1;
                    changed = true;
                }
            }
            if (Burning) Temperature = ChangeTemperature(Temperature, MaxTemperature, dt * c.FuelCount / FuelCapacity);
            else
            {
                int env = c.EnvironmentTemperature;
                if (Temperature > env)
                {
                    changed |= HeatInput(dt, c);
                    Temperature = ChangeTemperature(Temperature, env, dt / 24f);
                }
            }
            return changed;
        }

        bool HeatInput(float dt, IOvenContents c)
        {
            bool changed = false;
            for (int i = 0; i < Capacity; i++)
                if (c.Has(i) && HeatStack(dt, i, c) >= 100) changed |= Bake(dt * 1.2f, i, c);
            return changed;
        }

        float HeatStack(float dt, int i, IOvenContents c)
        {
            var s = Slots[i];
            float temp = s.Temp, next = temp;
            if (temp < Temperature)
            {
                float step = (1 + Math.Clamp((Temperature - temp) / 28f, 0, 1.6f)) * dt;
                next = ChangeTemperature(temp, Temperature, step);
                int max = c.MaxTemperature(i);
                if (max > 0) next = Math.Min(max, next);
            }
            else if (temp > Temperature)
            {
                float step = (1 + Math.Clamp((temp - Temperature) / 28f, 0, 1.6f)) * dt;
                next = ChangeTemperature(temp, Temperature, step);
            }
            s.Temp = next;
            return next;
        }

        bool Bake(float dt, int i, IOvenContents c)
        {
            var s = Slots[i];
            float browning = s.BrowningPoint == 0 ? 160 : s.BrowningPoint;
            float ratio = s.Temp / browning;
            float time = s.TimeToBake == 0 ? 1 : s.TimeToBake;
            float inc = Math.Clamp((int)ratio, 1, 30) * dt / time;
            float level = s.BakedLevel;
            if (s.Temp > browning) level = s.BakedLevel += inc;
            float from = 0, to = 1, startY = 1, endY = 1;
            c.Baking(i, out from, out to, out startY, out endY);
            float f = Math.Clamp((level - from) / (to - from), 0, 1);
            float height = (int)((startY + (endY - startY) * f) * StageThreshold) / (float)StageThreshold;
            bool changed = height != s.CurHeightMul;
            s.CurHeightMul = height;
            if (level > to && c.Bake(i)) changed = true;
            return changed;
        }

        /// <summary>Vanilla's oven's ChangeTemperature (faster the further off; cooling at half the pace).</summary>
        public static float ChangeTemperature(float from, float to, float dt)
        {
            float diff = Math.Abs(from - to);
            diff *= (float)Math.Sqrt(diff);
            dt += dt * (diff / 480f);
            if (diff < dt) return to;
            if (from > to) dt = -dt / 2;
            if (Math.Abs(from - to) < 1) return to;
            return from + dt;
        }

        /// <summary>Its state for saving and sending (vanilla's oven's own keys).</summary>
        public void ToTree(Vintagestory.API.Datastructures.ITreeAttribute t)
        {
            t.SetInt("burn", Burning ? 1 : 0);
            t.SetFloat("temp", Temperature);
            t.SetFloat("tfuel", FuelBurnTime);
            for (int i = 0; i < Capacity; i++)
            {
                var s = Slots[i];
                t.SetFloat("brown" + i, s.BrowningPoint);
                t.SetFloat("tbake" + i, s.TimeToBake);
                t.SetFloat("baked" + i, s.BakedLevel);
                t.SetFloat("heightmul" + i, s.CurHeightMul);
                t.SetFloat("temp" + i, s.Temp);
            }
        }

        public void FromTree(Vintagestory.API.Datastructures.ITreeAttribute t)
        {
            Burning = t.GetInt("burn") > 0;
            Temperature = t.GetFloat("temp", 20);
            FuelBurnTime = t.GetFloat("tfuel");
            for (int i = 0; i < Capacity; i++)
                Slots[i] = new OvenSlot(t.GetFloat("brown" + i), t.GetFloat("tbake" + i), t.GetFloat("baked" + i), t.GetFloat("heightmul" + i), t.GetFloat("temp" + i, 20));
        }
    }
}
