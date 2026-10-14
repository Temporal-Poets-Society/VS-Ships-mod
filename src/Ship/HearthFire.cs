using System;
using Vintagestory.API.Datastructures;

namespace Hansa
{
    /// <summary>
    /// What the fire needs to know about what's in the hearth, and what it does to it: the fuel, the input (an item to
    /// cook or smelt, or a cooking pot with its ingredients) and the output. On the server it's the hearth's vanilla
    /// inventory and vanilla's own collectible methods (<see cref="EntityShip.HearthContents"/>); the offline checks use
    /// plain numbers.
    /// </summary>
    public interface IHearthContents
    {
        /// <summary>The fuel slot's burn temperature (°C), burn duration (s) and smoke level; false if it's empty or nothing that burns.</summary>
        bool Fuel(out int burnTemperature, out float burnDuration, out float smokeLevel);
        /// <summary>One fuel out of the fuel slot (it's burning now).</summary>
        void TakeFuel();
        bool InputEmpty { get; }
        /// <summary>Can the input be cooked or smelted (vanilla's canSmeltInput: OnSmeltAttempt, then CanSmelt, and no crucible needed)?</summary>
        bool CanSmeltInput();
        /// <summary>The input's "allowHeating" attribute (a cooked pot warming up): heated though it doesn't cook.</summary>
        bool InputAllowsHeating { get; }
        /// <summary>The output's "allowHeating" attribute: it's kept warm.</summary>
        bool OutputAllowsHeating { get; }
        /// <summary>The input's temperature (a pot's: its ingredients', the coldest), and the output's.</summary>
        float InputTemp { get; set; }
        float OutputTemp { get; set; }
        float InputMeltingPoint { get; }
        int InputStackSize { get; }
        /// <summary>The hottest the input and the output get (a clay pot 200 °C); 0: no limit.</summary>
        int InputMaxTemperature { get; }
        int OutputMaxTemperature { get; }
        /// <summary>How long the input cooks at its melting point (s; vanilla's maxCookingTime: 30 with nothing in).</summary>
        float MaxCookingTime { get; }
        /// <summary>Cook or smelt it: the input into the output (vanilla's DoSmelt).</summary>
        void Smelt();
        /// <summary>The game's calendar (hours): embers go cold two hours after the fire went out.</summary>
        double TotalHours { get; }
    }

    /// <summary>
    /// The ship's hearth fire: vanilla's firepit (BlockEntityFirepit, read from the 1.22.7 game) ported number for number,
    /// so food cooks aboard as it does ashore. Its state is what the firepit block entity keeps (the furnace temperature,
    /// the fuel burning, the input's cooking time, whether it can relight) and its block's burn state (cold, lit,
    /// extinct). <see cref="Tick"/> is the firepit's burn tick (every 100 ms on the server), against what's in it.
    /// </summary>
    public class HearthFire
    {
        /// <summary>The air's temperature (vanilla's enviromentTemperature): what a fire that's out cools to.</summary>
        public const int EnvironmentTemperature = 20;
        /// <summary>Fuel burns this many times slower while the fire is at its heat and has nothing to cook.</summary>
        public const float EmptyBurnTimeBonus = 4f;
        /// <summary>Embers relight with new fuel this long after the fire went out (hours), then go cold.</summary>
        public const double EmbersHours = 2;
        /// <summary>Seconds of holding a firestarter or a lit torch to it before it catches (vanilla: more than 3).</summary>
        public const float IgniteSeconds = 3f;
        /// <summary>The share of a firestarter's tries that light it (vanilla's ItemFirestarter: one in four); a torch always does.</summary>
        public const double FirestarterChance = 0.25;
        /// <summary>The heat it gives (vanilla's IHeatSource strength): burning, and embers.</summary>
        public const float HeatBurning = 10f, HeatEmbers = 0.25f;

        public float FurnaceTemperature = EnvironmentTemperature;
        public int MaxTemperature;
        /// <summary>How long the input has cooked at its melting point or over (vanilla's "oreCookingTime").</summary>
        public float InputStackCookingTime;
        public float FuelBurnTime, MaxFuelBurnTime;
        public float SmokeLevel;
        /// <summary>Lit, or embers: new fuel catches by itself (vanilla's canIgniteFuel; a torch or firestarter sets it).</summary>
        public bool CanIgniteFuel;
        public float CachedFuel;
        public double ExtinguishedTotalHours;
        /// <summary>The firepit block's variant: "cold", "lit" or "extinct".</summary>
        public string BurnState = "cold";

        public bool IsBurning => FuelBurnTime > 0;
        public bool IsSmoldering => CanIgniteFuel;

        /// <summary>The heat it gives (vanilla's GetHeatStrength): 10 burning, 0.25 smouldering, else none.</summary>
        public float HeatStrength => IsBurning ? HeatBurning : IsSmoldering ? HeatEmbers : 0;

        /// <summary>
        /// How much of a heat source's strength warms someone (vanilla's body temperature, getNearHeatSourceStrength): all of
        /// it within about a block, less further off, falling off slower in a closed room.
        /// </summary>
        public static float Warmth(float strength, double distSqToEdge, bool enclosed) =>
            strength * Math.Min(1f, 9f / (8f + (float)Math.Pow(distSqToEdge, enclosed ? 0.875 : 1.25)));

        /// <summary>
        /// Vanilla's BlockPos.DistanceSqToNearerEdge, from a point to the block whose corner is (bx, by, bz): nothing across
        /// the block's width and length, from 0.75 up it in height (and from its lower edges as they are).
        /// </summary>
        public static double DistanceSqToNearerEdge(double bx, double by, double bz, double x, double y, double z)
        {
            double dx = x - bx, dy = y - by - 0.75, dz = z - bz;
            if (dx > 0) dx = dx <= 1 ? 0 : dx - 1;
            if (dz > 0) dz = dz <= 1 ? 0 : dz - 1;
            return dx * dx + dy * dy + dz * dz;
        }

        // ------------------------------------------------------------------ the burn tick

        /// <summary>Vanilla's changeTemperature: a step toward a temperature, faster the further off (1 °C/s plus 1/28 of the gap a second).</summary>
        public static float ChangeTemperature(float fromTemp, float toTemp, float dt)
        {
            float diff = Math.Abs(fromTemp - toTemp);
            dt += dt * (diff / 28f);
            if (diff < dt) return toTemp;
            if (fromTemp > toTemp) dt = -dt;
            if (Math.Abs(fromTemp - toTemp) < 1f) return toTemp;
            return fromTemp + dt;
        }

        /// <summary>
        /// The firepit's burn tick (BlockEntityFirepit.OnBurnTick, server), dt seconds (vanilla: every 100 ms): the fuel
        /// burns (four times slower at heat with nothing to cook), the next fuel catches when one's done, the fire goes out
        /// when there's none and its embers go cold two hours later, the furnace heats toward the fuel's burn temperature
        /// (or cools to the air), the input heats and cooks, the output's kept warm, and what's cooked goes out.
        /// </summary>
        public void Tick(float dt, IHearthContents c)
        {
            if (FuelBurnTime > 0)
            {
                bool idle = Math.Abs(FurnaceTemperature - MaxTemperature) < 50 && c.InputEmpty;
                FuelBurnTime -= dt / (idle ? EmptyBurnTimeBonus : 1f);
                if (FuelBurnTime <= 0)
                {
                    FuelBurnTime = 0;
                    MaxFuelBurnTime = 0;
                    if (!CanBurn(c))
                    {
                        BurnState = "extinct";
                        ExtinguishedTotalHours = c.TotalHours;
                    }
                }
            }
            if (!IsBurning && BurnState == "extinct" && c.TotalHours - ExtinguishedTotalHours > EmbersHours)
            {
                CanIgniteFuel = false;
                BurnState = "cold";
            }
            if (IsBurning) FurnaceTemperature = ChangeTemperature(FurnaceTemperature, MaxTemperature, dt);
            if (CanHeatInput(c)) HeatInput(dt, c);
            else InputStackCookingTime = 0;
            if (c.OutputAllowsHeating) HeatOutput(dt, c);
            if (c.CanSmeltInput() && InputStackCookingTime > c.MaxCookingTime)
            {
                c.Smelt();
                InputStackCookingTime = 0;
            }
            if (!IsBurning && CanIgniteFuel && CanBurn(c)) IgniteFuel(c);
            if (!IsBurning) FurnaceTemperature = ChangeTemperature(FurnaceTemperature, EnvironmentTemperature, dt);
        }

        /// <summary>Is there fuel that burns (vanilla's canSmelt: a firepit burns all fuel, cooking or not)?</summary>
        public static bool CanBurn(IHearthContents c) => c.Fuel(out int t, out _, out _) && t > 0;

        /// <summary>Is the input heated: it cooks or smelts, or it's something kept warm (a cooked pot)?</summary>
        public static bool CanHeatInput(IHearthContents c) => c.CanSmeltInput() || c.InputAllowsHeating;

        /// <summary>
        /// Vanilla's heatInput: the input warms toward the furnace (a little faster the hotter the fire, eleven times slower
        /// once it's at its melting point, a stack of several only as one item's worth), never past its own limit; at its
        /// melting point or over it cooks (twice as fast at twice the melting point, and so on); below it, it loses a
        /// second of cooking a tick.
        /// </summary>
        public void HeatInput(float dt, IHearthContents c)
        {
            float oldTemp = c.InputTemp;
            float temp = oldTemp;
            float meltingPoint = c.InputMeltingPoint;
            float stackSize = c.InputStackSize;
            if (oldTemp < FurnaceTemperature)
            {
                float step = (1f + Math.Clamp((FurnaceTemperature - oldTemp) / 30f, 0f, 1.6f)) * dt;
                if (temp >= meltingPoint) step /= 11f;
                float newTemp = ChangeTemperature(oldTemp, FurnaceTemperature, step);
                newTemp = (newTemp + (stackSize - 1f) * oldTemp) / stackSize;
                int max = c.InputMaxTemperature;
                if (max > 0) newTemp = Math.Min(max, newTemp);
                if (oldTemp != newTemp)
                {
                    c.InputTemp = newTemp;
                    temp = newTemp;
                }
            }
            if (temp >= meltingPoint)
            {
                float ratio = temp / meltingPoint;
                InputStackCookingTime += Math.Clamp((int)ratio, 1, 30) * dt;
            }
            else if (InputStackCookingTime > 0) InputStackCookingTime -= 1f;
        }

        /// <summary>Vanilla's heatOutput: what's in the output and may be kept warm warms toward the furnace, twice as fast.</summary>
        public void HeatOutput(float dt, IHearthContents c)
        {
            float oldTemp = c.OutputTemp;
            if (oldTemp >= FurnaceTemperature) return;
            float newTemp = ChangeTemperature(oldTemp, FurnaceTemperature, 2f * dt);
            int max = c.OutputMaxTemperature;
            if (max > 0) newTemp = Math.Min(max, newTemp);
            if (oldTemp != newTemp) c.OutputTemp = newTemp;
        }

        /// <summary>Vanilla's igniteFuel: the next fuel catches (it burns its duration at its temperature), one less in the slot.</summary>
        public void IgniteFuel(IHearthContents c)
        {
            if (!c.Fuel(out int temperature, out float duration, out float smoke)) return;
            MaxFuelBurnTime = FuelBurnTime = duration;
            MaxTemperature = temperature;
            SmokeLevel = smoke;
            BurnState = "lit";
            c.TakeFuel();
        }

        // ------------------------------------------------------------------ lighting it, putting it out

        public enum IgniteState { NotIgnitable, NotIgnitablePreventDefault, Ignitable, IgniteNow }

        /// <summary>Vanilla's GetIgnitableState: no fuel, or burning: not; held to it for more than 3 s: it catches now.</summary>
        public IgniteState IgnitableState(float secondsIgniting, bool fuelEmpty)
        {
            if (fuelEmpty || IsBurning) return IgniteState.NotIgnitablePreventDefault;
            return secondsIgniting > IgniteSeconds ? IgniteState.IgniteNow : IgniteState.Ignitable;
        }

        /// <summary>
        /// Vanilla's OnTryIgniteBlockOver: held long enough, the tinder has caught: the fuel catches at the next tick (and
        /// so does new fuel for two hours after it goes out).
        /// </summary>
        public void Ignited(double totalHours)
        {
            if (CanIgniteFuel) return;
            CanIgniteFuel = true;
            ExtinguishedTotalHours = totalHours;
        }

        /// <summary>Doused (vanilla's CoolNow, all the way: water): out and cold at once, no embers left to relight it.</summary>
        public void Douse()
        {
            BurnState = "cold";
            ExtinguishedTotalHours = -99;
            CanIgniteFuel = false;
            FuelBurnTime = 0;
            MaxFuelBurnTime = 0;
        }

        // ------------------------------------------------------------------ saved with her, and sent to clients

        /// <summary>The fire's state into a tree, by vanilla's own keys (the firepit block entity's).</summary>
        public void ToTree(ITreeAttribute t)
        {
            t.SetFloat("furnaceTemperature", FurnaceTemperature);
            t.SetInt("maxTemperature", MaxTemperature);
            t.SetFloat("oreCookingTime", InputStackCookingTime);
            t.SetFloat("fuelBurnTime", FuelBurnTime);
            t.SetFloat("maxFuelBurnTime", MaxFuelBurnTime);
            t.SetDouble("extinguishedTotalHours", ExtinguishedTotalHours);
            t.SetBool("canIgniteFuel", CanIgniteFuel);
            t.SetFloat("cachedFuel", CachedFuel);
            t.SetFloat("smokeLevel", SmokeLevel);
            t.SetString("burnstate", BurnState);
        }

        public void FromTree(ITreeAttribute t)
        {
            FurnaceTemperature = t.GetFloat("furnaceTemperature", EnvironmentTemperature);
            MaxTemperature = t.GetInt("maxTemperature");
            InputStackCookingTime = t.GetFloat("oreCookingTime");
            FuelBurnTime = t.GetFloat("fuelBurnTime");
            MaxFuelBurnTime = t.GetFloat("maxFuelBurnTime");
            ExtinguishedTotalHours = t.GetDouble("extinguishedTotalHours");
            CanIgniteFuel = t.GetBool("canIgniteFuel");
            CachedFuel = t.GetFloat("cachedFuel");
            SmokeLevel = t.GetFloat("smokeLevel");
            string state = t.GetString("burnstate", "cold");
            BurnState = state == "lit" || state == "extinct" ? state : "cold";
        }

        // ------------------------------------------------------------------ how it's drawn

        /// <summary>
        /// The firepit's model as vanilla draws it (BlockEntityFirepit.OnTesselation): its burn state, except that a cold
        /// one with no fuel in it is drawn as ashes (extinct); and with what's cooking in it, "normal", "spit" (a joint on
        /// the spit) or "wide" (a pot): the shape game:block/wood/firepit/{burn}-{model}.
        /// </summary>
        public static string DrawnBurnState(string burnState, bool fuelEmpty) => burnState == "cold" && fuelEmpty ? "extinct" : burnState ?? "cold";
    }

    /// <summary>
    /// A vanilla firepit run with plain numbers, for the offline checks and to work out how long cooking takes: fuel of
    /// one kind, one kind of item cooking (or nothing), the output piling up. Its temperature stays what the fire last made
    /// it (the game cools a stack only when nothing has heated it for half an hour of game time).
    /// </summary>
    public class HearthSimContents : IHearthContents
    {
        public int FuelCount, FuelTemperature = 700;
        public float FuelDuration = 24, FuelSmoke;
        /// <summary>The input: how many, its melting point and duration, how many make one cooked, its limit; 0 count: empty.</summary>
        public int InputCount, InputRatio = 1, InputMax;
        public float InputMelting = 150, InputDuration = 30, InputTempNow = HearthFire.EnvironmentTemperature;
        public int OutputCount;
        public bool OutputWarm;
        public float OutputTempNow = HearthFire.EnvironmentTemperature;
        public double Hours;

        public bool Fuel(out int burnTemperature, out float burnDuration, out float smokeLevel)
        {
            burnTemperature = FuelCount > 0 ? FuelTemperature : 0;
            burnDuration = FuelDuration;
            smokeLevel = FuelSmoke;
            return FuelCount > 0;
        }

        public void TakeFuel() => FuelCount--;
        public bool InputEmpty => InputCount <= 0;
        public bool CanSmeltInput() => InputCount >= InputRatio && InputMelting > 0;
        public bool InputAllowsHeating => false;
        public bool OutputAllowsHeating => OutputWarm && OutputCount > 0;
        public float InputTemp { get => InputCount > 0 ? InputTempNow : HearthFire.EnvironmentTemperature; set => InputTempNow = value; }
        public float OutputTemp { get => OutputTempNow; set => OutputTempNow = value; }
        public float InputMeltingPoint => InputMelting;
        public int InputStackSize => InputCount;
        public int InputMaxTemperature => InputMax;
        public int OutputMaxTemperature => 0;
        public float MaxCookingTime => InputCount > 0 ? InputDuration : 30;
        public double TotalHours => Hours;

        public void Smelt()
        {
            // (vanilla's DoSmelt: one cooked from SmeltedRatio raw; the output keeps the temperature of what's in it)
            InputCount -= InputRatio;
            if (OutputCount == 0) OutputTempNow = InputTempNow;
            OutputCount++;
            if (InputCount <= 0) InputTempNow = HearthFire.EnvironmentTemperature;
        }

        /// <summary>Game hours a real second at the game's default speed (60 game seconds a second, calendar at half speed: an hour in 2 minutes).</summary>
        public const double GameHoursPerSecond = 1 / 120.0;

        /// <summary>
        /// Run a fire from cold in 100 ms ticks, as the server does, for at most this long (s): when the first item was
        /// cooked, how hot the fire got, when it went out (NaN: not).
        /// </summary>
        public static HearthSimResult Run(HearthFire fire, HearthSimContents c, double seconds, float dt = 0.1f, double gameHoursPerSecond = GameHoursPerSecond)
        {
            var r = new HearthSimResult { FirstCooked = double.NaN, WentOut = double.NaN, LitAt = double.NaN };
            int cookedBefore = c.OutputCount;
            bool wasBurning = fire.IsBurning;
            for (double t = 0; t < seconds; t += dt)
            {
                fire.Tick(dt, c);
                c.Hours += dt * gameHoursPerSecond;
                double now = t + dt;
                r.Peak = Math.Max(r.Peak, fire.FurnaceTemperature);
                if (fire.IsBurning && double.IsNaN(r.LitAt)) r.LitAt = now;
                if (double.IsNaN(r.FirstCooked) && c.OutputCount > cookedBefore) r.FirstCooked = now;
                if (wasBurning && !fire.IsBurning && double.IsNaN(r.WentOut)) r.WentOut = now;
                wasBurning = fire.IsBurning;
                r.Seconds = now;
                if (!double.IsNaN(r.WentOut) && c.InputCount <= 0) break;
            }
            return r;
        }
    }

    public struct HearthSimResult
    {
        public double LitAt, FirstCooked, WentOut, Seconds;
        public float Peak;
    }
}
