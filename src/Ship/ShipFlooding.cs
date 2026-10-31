using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>Afloat, foundering (the sea coming in over her side, or nearly all her reserve buoyancy gone), going down, on the bottom.</summary>
    public enum SinkStage { Afloat = 0, Foundering = 1, Sinking = 2, Sunk = 3 }

    /// <summary>
    /// The ship's damage, the water in her and her sinking (config Damage; the model is <see cref="ShipDamage"/>,
    /// <see cref="ShipFlood"/> and <see cref="ShipRepairs"/>). The server keeps each part's hurt and seam wear, the water in
    /// her hold and how deep that puts her, and saves them with her; clients get a byte a part and the numbers.
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>Each part's hurt and seam wear (<see cref="ShipDefinition.DamageParts"/>): floats on the server, the synced bytes on clients.</summary>
        public DamageState PartDamage;
        /// <summary>Water in her hold (cubic blocks: tonnes).</summary>
        public double Water;
        /// <summary>Shipped water still on the weather deck, before scuppers or openings drain it (tonnes).</summary>
        public double DeckWater;
        /// <summary>How much deeper than her design waterline she floats (blocks): the water's weight, and going down. Pos.Y is BaseY − this + the heave.</summary>
        public double Sinkage;
        /// <summary>Her list from the water in her (radians, + starboard down), and her trim going down (radians, + bow up).</summary>
        public double ListAngle, Trim;
        /// <summary>The water's net rate in (cubic blocks a second; negative: going down) and how many are at the pumps.</summary>
        public float WaterRate;
        public int Pumpers;
        /// <summary>
        /// What the pumps take out of her now (cubic blocks a second: nothing while her hold is dry), and the pumps someone
        /// is at (their codes, comma-separated): synced, for the water gushing from their spouts (PumpClient).
        /// </summary>
        public float PumpedRate;
        public string PumpsWorked = "";
        /// <summary>How foul her bottom is (0 clean .. 1): slower. Copper sheathing keeps it clean.</summary>
        public float Fouling;
        public SinkStage Stage;
        /// <summary>Resting on the bottom (flooded in shallow water, or sunk).</summary>
        public bool OnBottom;
        /// <summary>The server's Damage switch, for clients.</summary>
        public bool DamageOn = true;
        /// <summary>Bumped whenever a part's state (its byte) changes: what draws the damage rebuilds.</summary>
        public int DamageVersion;

        public DamageLayout DamageParts => Def?.DamageParts;

        /// <summary>
        /// Where the water in her stands (ship-local y), level in her own frame (the model's). Worked out again only when the
        /// water changes: it's asked every tick for breathing and the view (maybe off the main thread).
        /// </summary>
        public double FloodLevel
        {
            get
            {
                var l = DamageParts;
                if (l == null) return double.NegativeInfinity;
                double w = Water;
                var c = floodAt;
                if (c != null && c.Water == w) return c.Level;
                double level = l.FloodLevel(w);
                floodAt = new FloodAt(w, level);
                return level;
            }
        }

        sealed class FloodAt
        {
            public readonly double Water, Level;
            public FloodAt(double water, double level) { Water = water; Level = level; }
        }

        volatile FloodAt floodAt;

        /// <summary>
        /// The world height of the water in her hold as it's drawn and as it's wet (breath, the view under water): level in the
        /// world while she rolls and pitches, through her middle (<see cref="ShipWater.FloodX"/>) at her flood level. On the
        /// server she's level, so it's her flood level over her origin. NaN when she's dry.
        /// </summary>
        public double FloodWorldY
        {
            get
            {
                if (!Flooded || Def == null) return double.NaN;
                LocalToWorld(ShipWater.FloodX(Def), FloodLevel, 0, out _, out double y, out _);
                return y;
            }
        }

        public bool Flooded => Water > 0.5;
        public bool GoneDown => Stage >= SinkStage.Sinking;
        /// <summary>The height her design waterline is at in still water: the sea's level less her sinkage.</summary>
        public double FloatY => (double.IsNaN(BaseY) ? Pos.Y : BaseY) - Sinkage;

        // her motion settings with the water's free surface in her (a slower, more damped roll)
        MotionDef motionNow;
        double motionWater = -1;
        MotionDef MotionNow
        {
            get
            {
                if (Def == null) return new MotionDef();
                if (motionNow == null || Math.Abs(Water - motionWater) > 1)
                {
                    motionWater = Water;
                    motionNow = DamageParts != null ? ShipFlood.Motion(DamageParts, Def.Motion, Water) : Def.Motion;
                }
                return motionNow;
            }
        }

        // server
        float floodAccum, grindAccum, damageSaveAccum;
        double wearHours = double.NaN;
        byte[] sentDamage;
        int listSide = 1;
        double sinkSpeed, leakSide;
        long lastBumpMs = -100000, founderSinceMs, lastDamageNoticeMs = -100000;
        ShipAttitude seaAttitude;
        readonly List<(int sample, long block)> contactsAhead = new();
        readonly List<int> lastContacts = new();
        long lastContactMs;
        EntityShip shipContact;
        Vec3d shipContactAt;
        readonly Dictionary<string, Dictionary<string, long>> pumping = new();

        // client: what's drawn eases toward the synced values
        double sinkShown = double.NaN, listShown, trimShown;

        // ------------------------------------------------------------------ setup, saving, syncing

        void InitDamage()
        {
            var l = DamageParts;
            if (l == null) return;
            PartDamage = new DamageState(l.Count);
            if (Api.Side == EnumAppSide.Server)
            {
                bool migrated = false;
                if (Attributes.GetTreeAttribute("hansaDmgF") is ITreeAttribute t)
                {
                    int[] codes = (t["codes"] as IntArrayAttribute)?.value;
                    float[] hurt = (t["hurt"] as FloatArrayAttribute)?.value, wear = (t["wear"] as FloatArrayAttribute)?.value;
                    // ((0.3.26, step D1) saved before her hull had panels (v under 2): her old hull parts' damage onto the panels)
                    if (Def.Hull != null && t.GetInt("v") < 2 && codes != null)
                    {
                        (codes, hurt, wear) = HullMigration.Map(Def, codes, hurt, wear);
                        migrated = true;
                    }
                    // ((0.3.28, step D2) saved before her deck cells were parts (v under 3): her decks' frame-space hurt onto the
                    // nearest cell, at most cracked; the rest dropped)
                    if (Def.Slots != null && t.GetInt("v") < 3 && codes != null)
                    {
                        int had = codes.Count(c => ShipPart.Decode(c).Kind == ShipPartKind.Deck);
                        int moved;
                        (codes, hurt, wear, moved) = CellMigration.Map(Def, codes, hurt, wear);
                        if (had > 0) World.Logger.Audit("[hansa] {0} #{1}: her decks' damage of {2} frame spaces dropped for her deck cells (0.3.28), {3} cells cracked by it", GetName(), EntityId, had, moved);
                    }
                    // ((0.3.28, D2.1) saved before her hull panels were halves (v under 4): a whole frame space's panel
                    // (its code the after half's now) onto its fore half too
                    if (Def.Hull != null && t.GetInt("v") < 4 && codes != null && Def.Hull.Slots.Any(s => s.Half != null))
                    {
                        var c2 = codes.ToList(); var h2 = hurt.ToList(); var w2 = wear.ToList();
                        for (int i = 0; i < codes.Length; i++)
                        {
                            var p = ShipPart.Decode(codes[i]);
                            if (p.Kind != ShipPartKind.Hull || p.Index != 0) continue;
                            p.Index = 1;
                            c2.Add(p.Encode()); h2.Add(hurt[i]); w2.Add(wear[i]);
                        }
                        codes = c2.ToArray(); hurt = h2.ToArray(); wear = w2.ToArray();
                    }
                    PartDamage.Load(l, codes, hurt, wear);
                }
                else PartDamage.Unpack(WatchedAttributes.GetBytes("hansaDmg"));
                // ((0.3.29) a panel that's out by her states (her design's openings: a castle port's halves where the port is
                // in; a port put in under a saved gun) is holed: its hurt follows)
                if (HullPanels != null)
                    for (int n = 0; n < HullPanels.Count && n < PartDamage.Count; n++)
                        if (!HullPanels.Filled(n) && PartDamage.Hurt[n] < l.D.Holed) PartDamage.Hurt[n] = 1f;
                // ((0.3.26) her hull panels as their hurt has them)
                if (SyncSlotsFromHurt()) { SaveSlots(); RebuildGrid(); }
                if (migrated) World.Logger.Audit("[hansa] {0} #{1}: her hull's damage moved onto her hull panels (0.3.26): {2}", GetName(), EntityId, HullText());
                SaveDamage(true);
            }
            else
            {
                PartDamage.Unpack(WatchedAttributes.GetBytes("hansaDmg"));
                WatchedAttributes.RegisterModifiedListener("hansaDmg", () =>
                {
                    if (PartDamage != null && PartDamage.Unpack(WatchedAttributes.GetBytes("hansaDmg"))) DamageVersion++;
                });
            }
        }

        /// <summary>Server: send the parts' bytes if one changed (floats: also keep the exact values for the save).</summary>
        void SaveDamage(bool floats)
        {
            if (PartDamage == null || Api.Side != EnumAppSide.Server) return;
            var b = PartDamage.Pack();
            if (sentDamage == null || !b.AsSpan().SequenceEqual(sentDamage))
            {
                sentDamage = b;
                WatchedAttributes.SetBytes("hansaDmg", b);
                WatchedAttributes.MarkPathDirty("hansaDmg");
                DamageVersion++;
                floats = true;
            }
            if (!floats) return;
            var t = Attributes.GetOrAddTreeAttribute("hansaDmgF");
            // ((0.3.26) what the hull codes mean: 2, hull panels (side, frame space, band of strakes) where she has them)
            t.SetInt("v", DamageSaveVersion);
            t["codes"] = new IntArrayAttribute(PartDamage.Codes(DamageParts));
            t["hurt"] = new FloatArrayAttribute((float[])PartDamage.Hurt.Clone());
            t["wear"] = new FloatArrayAttribute((float[])PartDamage.Wear.Clone());
        }

        void SaveFloodState(StateWriter w)
        {
            w.Double("water", Water, 0.01);
            w.Double("deckWater", DeckWater, 0.01);
            w.Double("sink", Sinkage, 0.002);
            w.Double("list", ListAngle, 0.001);
            w.Double("trim", Trim, 0.001);
            w.Float("waterRate", WaterRate, 0.01f);
            w.Int("pumpers", Pumpers);
            w.Float("pumped", PumpedRate, 0.01f);
            w.Str("pumpsAt", PumpsWorked ?? "");
            w.Float("foul", Fouling, 0.001f);
            w.Int("stage", (int)Stage);
            w.Bool("onBottom", OnBottom);
            w.Bool("dmgOn", DamageOn);
            w.Float("sailStrain", SailStrain, 0.01f);
            w.Bool("sailsTear", SailsTear);
            // (0.3.24) the server's sail limits, for the clients' warning
            var sl = SailLimits;
            if (sl != null) { w.Float("sailGaleW", sl.SailGaleWind); w.Float("sailAbackW", sl.SailAbackWind); w.Float("sailTearW", sl.SailTearWind); }
        }

        void LoadFloodState(ITreeAttribute t)
        {
            Water = t.GetDouble("water");
            DeckWater = Math.Max(0, t.GetDouble("deckWater"));
            Sinkage = t.GetDouble("sink");
            ListAngle = t.GetDouble("list");
            Trim = t.GetDouble("trim");
            WaterRate = t.GetFloat("waterRate");
            Pumpers = t.GetInt("pumpers");
            PumpedRate = t.GetFloat("pumped");
            PumpsWorked = t.GetString("pumpsAt") ?? "";
            Fouling = t.GetFloat("foul");
            Stage = (SinkStage)t.GetInt("stage");
            OnBottom = t.GetBool("onBottom");
            DamageOn = t.GetBool("dmgOn", true);
            SailStrain = t.GetFloat("sailStrain");
            SailsTear = t.GetBool("sailsTear", true);
            syncedGale = t.GetFloat("sailGaleW");
            syncedAback = t.GetFloat("sailAbackW");
            syncedTear = t.GetFloat("sailTearW");
        }

        // ------------------------------------------------------------------ what the damage does to her sailing

        /// <summary>
        /// How much of her sails' drive she has (0..1), the sails' area weighted: torn and shredded sails draw only part of
        /// their cloth (<see cref="SailCloth"/>), a sprung or broken mast, frayed or parted rigging less. Her speed, her
        /// heel in the wind and how much her sails steady her roll go by it (as the sail set times it), on the server and
        /// on every client alike (from the parts' synced state; worked out again when it changes).
        /// </summary>
        public double SailDrive
        {
            get
            {
                if (PartDamage == null || DamageParts == null || !DamageOn) return 1;
                if (sailDriveVersion == DamageVersion) return sailDrive;
                sailDrive = SailArea.Drive(DamageParts, PartDamage);
                sailDriveVersion = DamageVersion;
                return sailDrive;
            }
        }

        double sailDrive = 1;
        int sailDriveVersion = int.MinValue;

        /// <summary>The sails' set as it draws: how far they're set times what of them is whole (<see cref="SailDrive"/>).</summary>
        public double SailsDrawing => SailFraction * SailDrive * LateenRig.Drive(Def, LeeShown);

        /// <summary>How well her rudder answers (0.2..1).</summary>
        public double RudderAnswers
        {
            get
            {
                var l = DamageParts;
                if (l == null || PartDamage == null || !DamageOn) return 1;
                foreach (var p in l.Parts) if (p.Kind == DamageKind.Rudder) return Math.Max(0.2, 1 - 0.8 * PartDamage.Hurt[p.Index]);
                return 1;
            }
        }

        /// <summary>Her speed as the water in her and a foul bottom leave it (a factor).</summary>
        public double SpeedFactorNow
        {
            get
            {
                if (DamageParts == null || !DamageOn) return 1;
                return ShipFlood.Sluggish(DamageParts, Water) * (1 - DamageParts.D.FoulingSlow * Math.Clamp(Fouling, 0, 1));
            }
        }

        // ------------------------------------------------------------------ the damage API (server)

        /// <summary>
        /// A blow to her at a ship-local point, going along localDir, with this much energy (kJ) spread over this radius
        /// (blocks): <see cref="ShipDamage.Apply"/> (a radius of <see cref="DamageDef.PierceRadius"/> or less is a shot, and
        /// goes on through what it holes). cause: grounding, collision, cannon, test... Server only; does nothing with the
        /// Damage switch off. Returns what it did (null if nothing).
        /// </summary>
        public DamageResult Damage(Vec3d localPoint, Vec3d localDir, float energy, float radius, string cause, DamageKinds kinds = DamageKinds.All)
        {
            if (Api?.Side != EnumAppSide.Server || !Mod.Config.Damage || PartDamage == null || GoneDown || energy <= 0) return null;
            // laid up (her crew offline): nothing hurts her (ShipCrew)
            if (LaidUp) return null;
            // ((0.3.28, step D2) her deck cells as they are, those held by what's on them, her pieces that take shots)
            var res = ShipDamage.Apply(DamageParts, PartDamage, Grid, localPoint, localDir, energy, radius, kinds, HurtFactors(cause), DamageHooks());
            AfterDamage(res, cause);
            return res;
        }

        /// <summary>Hurt one part (0..1 more; negative mends it): the test command, and whatever knows the part already.</summary>
        public DamageResult DamagePart(int partIndex, float amount, string cause = "test")
        {
            if (Api?.Side != EnumAppSide.Server || PartDamage == null || partIndex < 0 || partIndex >= PartDamage.Count) return null;
            var res = ShipDamage.Hurt(DamageParts, PartDamage, partIndex, amount);
            AfterDamage(res, cause);
            return res;
        }

        /// <summary>(0.3.26) What hansaDmgF's hull codes mean: 2 since hull panels (a design without them: the same as 1).</summary>
        /// <remarks>(0.3.28, step D2) 3: her deck cells are parts (code kind Cell) where she has deck slots; her decks' frame-space parts gone.</remarks>
        public const int DamageSaveVersion = 4;

        void AfterDamage(DamageResult res, string cause)
        {
            if (res == null || !res.Any) return;
            // ((0.3.28, step D2) the pieces it hurt: kept, or shot away)
            AfterPieceHits(res);
            if (res.Hits.Count == 0)
            {
                if (cause != "test") RaiseAlarm(cause is "cannon" or "collision" or "grounding" or "grinding" ? cause : "hit");
                return;
            }
            // anyone asleep aboard is turned out (ShipWatch; not for the test command)
            if (cause != "test") RaiseAlarm(cause is "cannon" or "collision" or "grounding" or "grinding" ? cause : "hit");
            SaveDamage(true);
            // ((0.3.26, step D1) her hull panels follow: one stove in is out of her collision, the sea comes in)
            HullFollowsHurt();
            // which side it came in on: the water runs to that side first
            foreach (var h in res.Hits)
            {
                var p = DamageParts.Parts[h.Part];
                if (p.Kind == DamageKind.Hull && p.Center.Y < Def.Waterline + 0.5) leakSide += Math.Sign(p.Center.Z) * h.Added;
            }
            if (!res.StateChanged) return;
            var worst = res.Hits.Max(h => h.After);
            LocalToWorld(DamageParts.Parts[res.Hits[0].Part].Center.X, DamageParts.Parts[res.Hits[0].Part].Center.Y, DamageParts.Parts[res.Hits[0].Part].Center.Z,
                out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation(worst >= PartState.Cracked ? "sounds/effect/woodgrind" : "sounds/block/planks"), x, y, z, null, true, 24);
            // tell those aboard when she's cracked or holed, once in a while
            if (worst is PartState.Cracked or PartState.Holed or PartState.Broken or PartState.Shredded or PartState.Parted &&
                World.ElapsedMilliseconds - lastDamageNoticeMs > 5000)
            {
                lastDamageNoticeMs = World.ElapsedMilliseconds;
                var hit = res.Hits.First(h => h.After == worst);
                // ((0.3.28) a deck cell stove in)
                if (worst == PartState.Holed && DamageParts.Parts[hit.Part].Cell >= 0) NotifyAboard("hansa:damage-cell-stove", PartName(hit.Part));
                else NotifyAboard("hansa:damage-notice", "hansa:state-" + worst.ToString().ToLowerInvariant(), PartName(hit.Part));
            }
            World.Logger.Audit("[hansa] {0} #{1}: {2} damage, worst {3}", GetName(), EntityId, cause, worst);
        }

        /// <summary>Mend every part, pump her dry, scrape her bottom (the test command; a ship gone down stays down).</summary>
        public void RepairAll()
        {
            if (PartDamage == null) return;
            PartDamage.Clear();
            if (!GoneDown)
            {
                Water = 0;
                DeckWater = 0;
                Fouling = 0;
                Stage = SinkStage.Afloat;
                ListAngle = 0;
            }
            SaveDamage(true);
            HullFollowsHurt();
            SaveState(true);
        }

        /// <summary>Pour water into her (or out, negative): the test command.</summary>
        public void Flood(double tonnes)
        {
            if (DamageParts == null || GoneDown) return;
            Water = Math.Clamp(Water + tonnes, 0, DamageParts.Capacity);
            if (tonnes > 0 && leakSide == 0) leakSide = 1;
            SaveState(true);
        }

        /// <summary>What a part is called: "Hull, port side, amidships, below the waterline (frames 12–13)", "Main course", "Rigging of the mainmast"...</summary>
        public string PartName(int index)
        {
            var l = DamageParts;
            if (l == null || index < 0 || index >= l.Count) return "?";
            var p = l.Parts[index];
            switch (p.Kind)
            {
                case DamageKind.Rudder: return Lang.Get("hansa:part-rudder");
                case DamageKind.Sail: return Lang.Get("hansa:sail-" + Def.Sails[p.Sail].Code);
                case DamageKind.Rigging: return Lang.Get("hansa:part-rigging", Lang.Get("hansa:mast-" + Def.Masts[p.Mast].Code));
                default: return PartText(p.Part);
            }
        }

        /// <summary>A part by a name typed in a command: hull-p-12-3 / hull-s-12-3 (side, station, band), deck-main-10, rudder, mast-main, sail-main-course, rigging-main.</summary>
        public int PartByName(string name)
        {
            var l = DamageParts;
            if (l == null || string.IsNullOrEmpty(name)) return -1;
            // ((0.3.28) a deck cell by its slot id: cell-main:5:-2)
            if (name.StartsWith("cell-", StringComparison.OrdinalIgnoreCase)) return CellPartByName(name.Substring(5));
            var w = name.ToLowerInvariant().Split('-');
            try
            {
                switch (w[0])
                {
                    case "hull" when w.Length == 4:
                        return l.Find(ShipPart.Hull(w[1].StartsWith("p") ? -1 : 1, int.Parse(w[2]), int.Parse(w[3])));
                    case "deck" when w.Length == 3:
                        int deck = Array.FindIndex(Def.Decks, dk => dk.Code == w[1]);
                        return deck < 0 ? -1 : l.Find(ShipPart.Deck(deck, int.Parse(w[2]), false));
                    case "rudder":
                        return Array.FindIndex(l.Parts, p => p.Kind == DamageKind.Rudder);
                    case "mast" when w.Length == 2:
                        return Array.FindIndex(l.Parts, p => p.Kind == DamageKind.Mast && Def.Masts[p.Mast].Code == w[1]);
                    case "rigging" when w.Length == 2:
                        return Array.FindIndex(l.Parts, p => p.Kind == DamageKind.Rigging && Def.Masts[p.Mast].Code == w[1]);
                    case "sail":
                        string code = name.Substring(5);
                        return Array.FindIndex(l.Parts, p => p.Kind == DamageKind.Sail && Def.Sails[p.Sail].Code == code);
                }
            }
            catch (FormatException) { }
            return -1;
        }

        /// <summary>How far behind the first surface (an inside planking, a deck's edge, the hold's floor) the repair crosshair looks for the panel it mends.</summary>
        public const double RepairLookBehind = 1.5;

        /// <summary>
        /// The hull panel or deck the repair crosshair picks (the user, 0.3.9: "repair from inside, but the floors prevent
        /// access, as well as chests or any other placed blocks"): with something in hand that mends one, her ray (ship-local
        /// o, d normalised) looks through what's put aboard (pieces, partitions, panels: her design's own collision only),
        /// and up to <see cref="RepairLookBehind"/> past the first of her surfaces it meets (the ceiling, a deck's edge or its
        /// band, the hold's floor over her bottom) to the nearest panel whose repair that is. The first surface must be in
        /// reach (range). Returns the part and how far along the ray the crosshair is (the nearer of the surface and the
        /// panel), or -1.
        /// </summary>
        public int RepairPick(Vec3d o, Vec3d d, double range, string held, bool aboard, out double t)
        {
            t = double.MaxValue;
            if (held == null || DamageParts == null || PartDamage == null || !DamageOn || GoneDown || Def?.Grid == null) return -1;
            // (from outside her, only the panel the ray meets: never through her side to what's inside)
            return ShipRepairs.Pick(DamageParts, PartDamage, Def.Grid, o, d, range, held, aboard ? RepairLookBehind : ShipRepairs.OutsideLookBehind, out t);
        }

        /// <summary>Client: the part the repair crosshair picked through something (outlined), or -1.</summary>
        public int RepairPicked = -1;

        /// <summary>
        /// The damage part for what the crosshair is on (<see cref="ShipPart"/>): a hull panel or a deck, a mast (its rigging
        /// and sails are mended at it too), the helm for the rudder. -1 if none.
        /// </summary>
        public int DamagePartFor(ShipPart p)
        {
            var l = DamageParts;
            if (l == null) return -1;
            switch (p.Kind)
            {
                case ShipPartKind.Hull:
                {
                    // ((0.3.30, the user: plugging from inside "thought that ship is safe but kept taking water") a sound ceiling
                    // with her planking behind it hurt: that planking is what's mended from here
                    int i = l.Find(p);
                    if (i >= 0 && PartDamage != null && l.Parts[i].Inner && l.Parts[i].Partner is int q && q >= 0 && q < PartDamage.Count &&
                        StateOfPart(i) == PartState.Sound && StateOfPart(q) != PartState.Sound)
                        return q;
                    return i;
                }
                case ShipPartKind.Deck:
                case ShipPartKind.Cell:
                    return l.Find(p);
                case ShipPartKind.Mast:
                    return l.Find(ShipPart.Mast(p.Index).Encode());
                case ShipPartKind.Fixture when p.Index < Def.Fixtures.Length && Def.Fixtures[p.Index].Type == "helm":
                    return Array.FindIndex(l.Parts, q => q.Kind == DamageKind.Rudder);
                case ShipPartKind.Rudder:
                case ShipPartKind.Sail:
                case ShipPartKind.Rigging:
                    return l.Find(p.Encode());
            }
            return -1;
        }

        /// <summary>
        /// Client: a sail in front of what her ray met (or nothing met), as her sails are set and trimmed now: the hit is
        /// the sail (to look at it and mend it there). Only with damage on: there's nothing else to do with a sail.
        /// </summary>
        public bool SailRay(Vec3d origin, Vec3d dir, double range, bool got, ref ShipRayHit hit)
        {
            var l = DamageParts;
            if (l == null || PartDamage == null || !DamageOn || GoneDown || Def.Sails.Length == 0) return got;
            double len = Math.Max(1e-9, dir.Length());
            ShipFrame.ToLocal(Pos.Yaw, Pitch, Roll, origin.X - Pos.X, origin.Y - Pos.InternalY, origin.Z - Pos.Z, out double ox, out double oy, out double oz);
            ShipFrame.ToLocal(Pos.Yaw, Pitch, Roll, dir.X / len, dir.Y / len, dir.Z / len, out double dx, out double dy, out double dz);
            var o = new Vec3d(ox, oy, oz);
            var d = new Vec3d(dx, dy, dz);
            int i = l.SailAt(o, d, range, Brace, Sheet, PartMotion.DrawnSail(SailFraction), out double t, LeeShown, LateenSheet, SailMeshRay == null ? null : (si, so, sd, sr) => SailMeshRay(this, si, so, sd, sr));
            if (i < 0 || got && t > hit.Distance - 0.02) return got;
            var sd = Def.Sails[l.Parts[i].Sail];
            if (sd.Mast != null && OpenFittings.Contains(sd.Mast)) return got;
            hit.Distance = t;
            hit.Fixture = null;
            hit.Part = l.Parts[i].Part;
            hit.Local = o.AddCopy(d.X * t, d.Y * t, d.Z * t);
            ShipFrame.ToWorld(Pos.Yaw, Pitch, Roll, hit.Local.X, hit.Local.Y, hit.Local.Z, out double wx, out double wy, out double wz);
            hit.World = new Vec3d(Pos.X + wx, Pos.InternalY + wy, Pos.Z + wz);
            hit.LocalNormal = new Vec3d(-d.X, -d.Y, -d.Z);
            hit.Face = BlockFacing.FromVector(-dir.X, -dir.Y, -dir.Z);
            return true;
        }

        /// <summary>Her seams and bottom as they'd be after this many more days afloat in a calm (the test command).</summary>
        public void Age(double days)
        {
            var l = DamageParts;
            if (l == null || PartDamage == null) return;
            foreach (var p in l.Parts)
                if (p.Kind == DamageKind.Hull) PartDamage.Wear[p.Index] = (float)Math.Clamp(PartDamage.Wear[p.Index] + days / l.D.SeamWearDays * p.WearRate, 0, 1);
            if (!Clad) Fouling = (float)Math.Clamp(Fouling + days / l.D.FoulingDays, 0, 1);
            SaveDamage(true);
            SaveState(true);
        }

        public PartState StateOfPart(int i) =>
            PartDamage == null || i < 0 || i >= PartDamage.Count ? PartState.Sound : DamageParts.StateOf(DamageParts.Parts[i].Kind, PartDamage.Hurt[i], PartDamage.Wear[i]);

        // ------------------------------------------------------------------ grounding and collisions (server)

        /// <summary>
        /// She ran into the ground (a move PoseToward refused): each place she newly touches takes the energy of her way
        /// there (½·mass·v² × GroundShare, v that point's own speed: her way and her turning), spread round it; a broad
        /// touch shares it out. Once in a while at most: driven on, she bumps every few ticks.
        /// </summary>
        void Grounded(double nx, double nz, double newYaw, float dt)
        {
            lastContacts.Clear();
            foreach (var (sample, _) in contactsAhead) if (!lastContacts.Contains(sample)) lastContacts.Add(sample);
            lastContactMs = World.ElapsedMilliseconds;
            if (!Mod.Config.Damage || PartDamage == null || lastContacts.Count == 0 || dt <= 0) return;
            if (World.ElapsedMilliseconds - lastBumpMs < 400) return;
            lastBumpMs = World.ElapsedMilliseconds;
            var d = DamageParts.D;
            double mass = DamageParts.Displacement + Water, share = Math.Min(1, 2.0 / lastContacts.Count);
            DamageResult all = null;
            foreach (int si in lastContacts)
            {
                var s = Def.HullSamples[si];
                ShipFrame.ToWorld(Pos.Yaw, s[0], s[1], s[2], out double ax, out _, out double az);
                ShipFrame.ToWorld(newYaw, s[0], s[1], s[2], out double bx, out _, out double bz);
                double vx = (nx + bx - Pos.X - ax) / dt, vz = (nz + bz - Pos.Z - az) / dt;
                double v = Math.Sqrt(vx * vx + vz * vz);
                ShipFrame.ToLocal(Pos.Yaw, vx, 0, vz, out double lx, out double ly, out double lz);
                double e = ShipDamage.ImpactEnergy(d, mass, v) * share * Mod.Config.GroundingDamage;
                var res = ShipDamage.Apply(DamageParts, PartDamage, Grid, new Vec3d(s[0], s[1], s[2]), new Vec3d(lx, ly, lz), e, d.GroundRadius, DamageKinds.Bottom, HurtFactors("grounding"));
                if (all == null) all = res;
                else { all.Hits.AddRange(res.Hits); all.StateChanged |= res.StateChanged; }
            }
            AfterDamage(all, "grounding");
        }

        /// <summary>
        /// She ran into another ship (or it into her): both take the energy of the blow where they meet (½·μ·v², μ the
        /// two masses' reduced mass, v how fast they closed there), each in her own frame.
        /// </summary>
        void Rammed(EntityShip other, Vec3d at, double nx, double nz, double newYaw, float dt)
        {
            if (!Mod.Config.Damage || PartDamage == null || other?.PartDamage == null || at == null || dt <= 0) return;
            long now = World.ElapsedMilliseconds;
            if (now - lastBumpMs < 400 || now - other.lastBumpMs < 400) return;
            lastBumpMs = other.lastBumpMs = now;
            // how fast the point where they meet was going on each
            var l0 = WorldToLocal(at.X, at.Y, at.Z);
            ShipFrame.ToWorld(newYaw, l0.X, l0.Y, l0.Z, out double bx, out _, out double bz);
            double v1x = (nx + bx - at.X) / dt, v1z = (nz + bz - at.Z) / dt;
            // (review H2-6: her drift sideways too, to starboard of her heading (-cos yaw, sin yaw))
            double oy = other.Pos.Yaw;
            double v2x = Math.Sin(oy) * other.Speed - Math.Cos(oy) * other.Leeway, v2z = Math.Cos(oy) * other.Speed + Math.Sin(oy) * other.Leeway;
            double rx = v1x - v2x, rz = v1z - v2z, v = Math.Sqrt(rx * rx + rz * rz);
            double m1 = DamageParts.Displacement + Water, m2 = other.DamageParts.Displacement + other.Water;
            double e = ShipDamage.ImpactEnergy(DamageParts.D, m1 * m2 / (m1 + m2), v) * Mod.Config.GroundingDamage;
            var mine = WorldDirToLocal(rx, 0, rz);
            Damage(l0, mine, (float)e, DamageParts.D.GroundRadius, "collision", DamageKinds.Planking | DamageKinds.Rudder);
            var theirs = other.WorldDirToLocal(-rx, 0, -rz);
            other.Damage(other.WorldToLocal(at.X, at.Y, at.Z), theirs, (float)e, other.DamageParts.D.GroundRadius, "collision", DamageKinds.Planking | DamageKinds.Rudder);
        }

        /// <summary>
        /// Lying on the ground in a seaway she grinds on it: where she touched it lately, and where her bottom would be in
        /// the waves' troughs, a little each second, more in bigger waves.
        /// </summary>
        void Grind(float dt)
        {
            grindAccum += dt;
            if (grindAccum < 1) return;
            float step = grindAccum;
            grindAccum = 0;
            double hs = Waves.SignificantHeight * (SeaSys?.WaveFactorAt(Pos.X, Pos.Z) ?? 1);
            if (hs < 0.2 || PartDamage == null) return;
            var touching = new List<int>();
            if (World.ElapsedMilliseconds - lastContactMs < 3000) touching.AddRange(lastContacts);
            var trough = new List<(int, long)>();
            if (CheckPose(World, Def, Pos.X, FloatY - hs / 2, Pos.Z, Pos.Yaw, Pos.Dimension, new HashSet<long>(), trough) == PoseState.Blocked)
                foreach (var (s, _) in trough) if (!touching.Contains(s)) touching.Add(s);
            if (touching.Count == 0) return;
            var d = DamageParts.D;
            float e = (float)(d.GrindRate * hs * step * Mod.Config.GroundingDamage);
            DamageResult all = null;
            foreach (int si in touching)
            {
                var s = Def.HullSamples[si];
                var res = ShipDamage.Apply(DamageParts, PartDamage, Grid, new Vec3d(s[0], s[1], s[2]), new Vec3d(0, -1, 0), e, 1.0, DamageKinds.Bottom, HurtFactors("grounding"));
                if (all == null) all = res;
                else { all.Hits.AddRange(res.Hits); all.StateChanged |= res.StateChanged; }
            }
            AfterDamage(all, "grinding");
        }

        // ------------------------------------------------------------------ leaks, pumps, wear, foundering (server)

        /// <summary>The sea's surface (world y) at a world x, z now: the still-water level and Sea's shared waves.</summary>
        double SeaSurface(double x, double z, double factor) => BaseY + Waves.HeightAt(x, z) * factor;

        /// <summary>Her pose as it really is (the server keeps her level for walking): the waves' pitch and roll, the heel from the wind, her list.</summary>
        void TruePose(out double pitch, out double roll)
        {
            double heel = ServerHeel;
            pitch = seaAttitude.Pitch + Trim;
            roll = seaAttitude.Roll + heel + ListAngle;
        }

        /// <summary>
        /// Every quarter second: what leaks in through her damaged panels (by the head of water over each, outside and
        /// in, with the waves there), what pours in over her side, what the pumps take out; the seams' and sails' wear and
        /// her bottom's fouling; how deep and how far over the water puts her; and whether she founders or goes down.
        /// </summary>
        bool pumpsDry;

        void FloodTick(float dt)
        {
            var l = DamageParts;
            if (l == null || PartDamage == null) return;
            var cfg = Mod.Config;
            if (cfg.Damage != DamageOn) { DamageOn = cfg.Damage; SaveState(true); }
            if (!cfg.Damage) { PumpedRate = 0; PumpsWorked = ""; return; }
            Grind(dt);
            floodAccum += dt;
            if (floodAccum < 0.25f) return;
            float step = floodAccum;
            floodAccum = 0;
            var d = l.D;

            double factor = SeaSys?.WaveFactorAt(Pos.X, Pos.Z) ?? 1;
            TruePose(out double pitch, out double roll);
            var pose = new FloodPose { X = Pos.X, Y = BaseY - Sinkage + Heave, Z = Pos.Z, Yaw = Pos.Yaw, Pitch = pitch, Roll = roll, Surface = (x, z) => SeaSurface(x, z, factor) };
            // leaks, by the head of water over each damaged panel outside and in
            double leak = ShipFlood.Leaks(l, PartDamage, Water, pose, cfg.Leaks, out double sideFlow);
            // Shipped water stays on deck until the scuppers, an open hatch/cell or sound-deck seepage drain it.
            double pour = DeckFlooding.Step(l, pose, DownfloodOpen, step, ref DeckWater, HatchCoaming).Below;
            // the pumps
            Pumpers = CountPumpers();
            // (the intake at the bilge's lowest point: a full, steady stream while there's water, none once she's pumped
            // dry, until a little more has come in: PumpResidual)
            if (Water <= d.PumpResidual) pumpsDry = true;
            else if (Water >= 3 * d.PumpResidual) pumpsDry = false;
            double pumped = pumpsDry ? 0 : Math.Min(ShipFlood.PumpRate(d, Pumpers, cfg.Pumps), Math.Max(0, Water / Math.Max(1e-3, step) + leak + pour));
            PumpedRate = (float)pumped;
            double net = leak + pour - pumped;
            Water = Math.Clamp(Water + net * step, 0, l.Capacity);
            WaterRate = (float)(WaterRate + (net - WaterRate) * Math.Min(1, step / 2));
            if (Math.Abs(sideFlow) > 1e-6) leakSide += sideFlow * step;

            Wear(step);
            SailStrainTick(step);

            // how deep the water puts her, unless she's on the bottom
            double want = l.SinkageFor(Water + DeckWater);
            double next = Sinkage + Math.Clamp(want - Sinkage, -0.3 * step, 0.3 * step);
            if (next > Sinkage && SettleBlocked(next))
            {
                if (!OnBottom) { OnBottom = true; SaveState(true); }
            }
            else
            {
                if (next < Sinkage - 0.01 && OnBottom) OnBottom = false;
                Sinkage = Math.Max(0, next);
                // (review H1-7) pumped up slowly, or the ground dug away under her: off the bottom once a settling step down is free
                if (OnBottom && !SettleBlocked(Sinkage + 0.3 * step)) OnBottom = false;
            }
            // the water runs to the low side: the way she heels, or else the side it came in on
            double heel = ServerHeel;
            if (Math.Abs(heel) > 1 * GameMath.DEG2RAD) listSide = Math.Sign(heel);
            else if (Math.Abs(leakSide) > 1e-4) listSide = Math.Sign(leakSide);
            // (resting on the bottom she sits on her flat floor, upright)
            double list = OnBottom ? 0 : ShipFlood.List(l, Water, heel, listSide);
            ListAngle += Math.Clamp(list - ListAngle, -2 * GameMath.DEG2RAD * step, 2 * GameMath.DEG2RAD * step);

            // foundering, and going down
            // (a sea breaking over the rail now and then isn't foundering: the water getting below for some seconds faster
            // than the pumps take it out is)
            bool awash = WaterRate > d.FounderRate;
            bool founder = awash || Water >= d.FounderAt * l.Reserve;
            double freeboard = Def.DeckY - Def.Waterline;
            if (want >= freeboard - 1e-3)
            {
                // her reserve buoyancy is gone: she settles till her deck's edge is at the water, then goes down; in
                // shallow water she may come to rest on the bottom first, and she's sunk there only if her deck's under
                founderSinceMs = World.ElapsedMilliseconds;
                if (OnBottom && DeckAwash()) { StartSinking(); SunkNow(); }
                else if (!OnBottom && Sinkage >= freeboard - 0.05) StartSinking();
                else SetStage(SinkStage.Foundering);
            }
            else if (founder)
            {
                founderSinceMs = World.ElapsedMilliseconds;
                SetStage(SinkStage.Foundering);
            }
            else if (Stage == SinkStage.Foundering && World.ElapsedMilliseconds - founderSinceMs > 10000) SetStage(SinkStage.Afloat);

            // what wear changed goes out every 10 s at most (a blow's damage goes out at once)
            damageSaveAccum += step;
            if (damageSaveAccum > 10)
            {
                damageSaveAccum = 0;
                SaveDamage(true);
            }
        }

        void SetStage(SinkStage s)
        {
            if (Stage == s) return;
            var was = Stage;
            Stage = s;
            // (said once a minute at most: in a seaway she can come in and out of it)
            if (s == SinkStage.Foundering && was == SinkStage.Afloat && World.ElapsedMilliseconds - founderNoticeMs > 60000)
            {
                founderNoticeMs = World.ElapsedMilliseconds;
                NotifyAboard("hansa:foundering-notice");
            }
            SaveState(true);
        }

        long founderNoticeMs = -60000;

        /// <summary>
        /// Seams age while she's afloat (more in a seaway), each panel at its own rate; an unsheathed bottom fouls; sails
        /// set in a strong wind wear. Game time, counted only while she's loaded (a jump of more than an hour is left out).
        /// </summary>
        void Wear(float step)
        {
            double now = World.Calendar.TotalHours;
            if (double.IsNaN(wearHours) || now < wearHours) { wearHours = now; return; }
            double dh = now - wearHours;
            wearHours = now;
            // (0.3.10: a jump in the calendar, /time set or the game catching up, isn't time at sea: it used to count as an
            // hour each time. Sleeping aboard still counts: the calendar only runs faster then.)
            if (dh > 0.25) return;
            if (dh <= 0 || Stage != SinkStage.Afloat && Stage != SinkStage.Foundering) return;
            var l = DamageParts;
            var d = l.D;
            var cfg = Mod.Config;
            float k = cfg.Wear;
            if (k <= 0) return;
            double hs = Waves.SignificantHeight * (SeaSys?.WaveFactorAt(Pos.X, Pos.Z) ?? 1);
            double seam = dh / (24 * Math.Max(0.1, d.SeamWearDays)) * (1 + hs / 2) * k * Math.Max(0, cfg.SeamWear);
            if (seam > 0)
                foreach (var p in l.Parts)
                    if (p.Kind == DamageKind.Hull) PartDamage.Wear[p.Index] = (float)Math.Min(1, PartDamage.Wear[p.Index] + seam * p.WearRate);
            if (!Clad) Fouling = (float)Math.Min(1, Fouling + dh / (24 * Math.Max(0.1, d.FoulingDays)) * k * Math.Max(0, cfg.Fouling));
            // the sails: chafe while they're set (tearing in a gale or taken aback is the strain's, in real seconds:
            // SailStrainTick, 0.3.12)
            double set = SailFraction;
            if (set <= 0.05) return;
            var w = WindHere(Pos.X, Pos.Z);
            double chafe = SailWear.Chafe(d, set, w.Strength) * Math.Max(0, cfg.SailWear);
            if (chafe <= 0) return;
            foreach (var p in l.Parts)
            {
                if (p.Kind != DamageKind.Sail) continue;
                PartDamage.Hurt[p.Index] = (float)Math.Min(1, PartDamage.Hurt[p.Index] + chafe * dh * k * p.WearRate);
            }
        }

        /// <summary>
        /// The sails' strain (0..1, 0.3.12; synced for the wind panel and the helm's warning): it builds while they're
        /// over-pressed (a gale with too much sail set, or taken aback hard) and eases off when they're not; only full do
        /// they tear (<see cref="DamageDef.SailStrainSeconds"/>). And whether the server tears sails at all (the Damage and
        /// SailDamage settings), so clients warn only when it does.
        /// </summary>
        public float SailStrain;
        public bool SailsTear = true;
        float syncedGale, syncedAback, syncedTear;
        DamageDef sailLimits;
        (DamageDef, float, float, float) sailLimitsKey;
        (DamageDef, float, float) serverForcesKey;

        /// <summary>
        /// (0.3.24) Her design's sail limits with the server's SailStrainForce and SailTearForce applied (on clients, as the
        /// server sent them): what strains and tears her sails and what the panel warns of.
        /// </summary>
        public DamageDef SailLimits
        {
            get
            {
                var d = DamageParts?.D;
                if (d == null) return null;
                float g, a, t;
                if (Api?.Side == EnumAppSide.Server)
                {
                    var cfg = Mod?.Config;
                    float strain = cfg?.SailStrainForce ?? 0, tear = cfg?.SailTearForce ?? 0;
                    // (review H1-24: the forces as they were last time, the same limits: no copy made to find that out)
                    if (sailLimits != null && serverForcesKey == (d, strain, tear)) return sailLimits;
                    var w = SailWear.WithForces(d, strain, tear);
                    (g, a, t) = (w.SailGaleWind, w.SailAbackWind, w.SailTearWind);
                    serverForcesKey = (d, strain, tear);
                }
                else if (syncedGale > 0) (g, a, t) = (syncedGale, syncedAback > 0 ? syncedAback : syncedGale, syncedTear > 0 ? syncedTear : d.SailTearWind);
                else return d;
                if (sailLimits != null && sailLimitsKey == (d, g, a, t)) return sailLimits;
                var c = d.Copy();
                c.SailGaleWind = g; c.SailAbackWind = a; c.SailTearWind = t;
                sailLimitsKey = (d, g, a, t);
                return sailLimits = c;
            }
        }
        // (what's been said of it: the strain beginning, a tear close; said once an episode, 0.3.13: EpisodeNotice)
        readonly EpisodeNotice strainNotice = new(), strainCloseNotice = new() { QuietSeconds = 30 };
        bool strainCloseOn;

        /// <summary>
        /// Every quarter second, in real seconds (the calendar runs fast while someone sleeps: the old hours of a gale went by
        /// in moments): the load on the sails from the apparent wind (a gale's on all of them, aback on the square ones),
        /// the strain building or easing, those aboard told once when it begins and when a tear is close, and the tearing
        /// once it's full.
        /// </summary>
        internal void SailStrainTick(float step)
        {
            var l = DamageParts;
            // (0.3.24) the design's limits with the server's SailStrainForce / SailTearForce
            var d = SailLimits ?? l.D;
            var cfg = Mod.Config;
            float factor = Math.Max(0, cfg.SailDamage);
            SailsTear = factor > 0;
            double set = SailFraction;
            double gale = 0, back = 0, apparent = 0;
            bool aback = false;
            bool afloat = Stage == SinkStage.Afloat || Stage == SinkStage.Foundering;
            if (afloat && SailsTear && set > 0.05)
            {
                var w = WindHere(Pos.X, Pos.Z);
                Sailing.Apparent(w.X, w.Z, Pos.Yaw, Speed, out double ax, out double az);
                apparent = Math.Sqrt(ax * ax + az * az);
                gale = SailWear.GaleLoad(d, set, apparent);
                aback = Sailing.Aback(w.X, w.Z, Pos.Yaw, SailingNow);
                if (aback) back = SailWear.AbackLoad(d, set, apparent);
            }
            double load = Math.Max(gale, back);
            SailStrain = (float)Math.Clamp(SailStrain + SailWear.StrainRate(d, load) * step, 0, 1);

            // (real seconds: the world's clock in milliseconds)
            double now = World.ElapsedMilliseconds / 1000.0;
            if (strainNotice.Due(SailStrain > 0, now))
            {
                NotifyAboard("hansa:sails-strain-notice", SailWear.Warning(d, set, apparent, aback, 0) >= 2
                    ? "hansa:sails-strain-storm" : "hansa:sails-strain-gale");
            }
            // ((0.3.24) a tear close only where they'd tear: from SailTearWind)
            bool tears = SailWear.Tears(d, gale, back);
            if (strainCloseNotice.Due(tears && (SailStrain >= 0.7f || SailStrain >= 0.4f && strainCloseOn), now)) NotifyAboard("hansa:sails-strain-close");
            strainCloseOn = tears && (SailStrain >= 0.7f || SailStrain >= 0.4f && strainCloseOn);

            if (SailStrain < 1 || load <= 1) return;
            double tearGale = SailWear.Gale(d, gale) * factor, tearBack = SailWear.Aback(d, back) * factor;
            if (tearGale + tearBack <= 0) return;
            PartState worst = PartState.Sound;
            int worstPart = -1;
            foreach (var p in l.Parts)
            {
                if (p.Kind != DamageKind.Sail) continue;
                bool square = p.Sail < 0 || p.Sail >= l.SailFrames.Length || l.SailFrames[p.Sail]?.ForeAndAft != true;
                double hurt = (tearGale + (square ? tearBack : 0)) * step;
                var before = StateOfPart(p.Index);
                PartDamage.Hurt[p.Index] = (float)Math.Min(1, PartDamage.Hurt[p.Index] + hurt * p.WearRate);
                var after = StateOfPart(p.Index);
                if (after != before && after > worst) { worst = after; worstPart = p.Index; }
            }
            if (worstPart >= 0)
            {
                // a sail torn or blown out: sent at once, and said
                SaveDamage(true);
                NotifyAboard("hansa:damage-notice", "hansa:state-" + worst.ToString().ToLowerInvariant(), PartName(worstPart));
            }
        }

        /// <summary>
        /// The server's factors on what a blow does, by DamageKind: HullDamage on the hull, decks, rudder, masts and
        /// rigging, SailDamage on the sails, and CannonDamage on both for a shot (tests: none).
        /// </summary>
        float[] HurtFactors(string cause)
        {
            var cfg = Mod.Config;
            if (cause == "test") return null;
            float hull = Math.Max(0, cfg.HullDamage), sail = Math.Max(0, cfg.SailDamage), shot = cause == "cannon" ? Math.Max(0, cfg.CannonDamage) : 1;
            var f = new float[Enum.GetValues(typeof(DamageKind)).Length];
            for (int i = 0; i < f.Length; i++) f[i] = (i == (int)DamageKind.Sail ? sail : hull) * shot;
            return f;
        }

        /// <summary>Would settling this deep put her on something (she touches nothing new: the ground she's aground on doesn't count)?</summary>
        bool SettleBlocked(double sinkage)
        {
            double baseY = double.IsNaN(BaseY) ? Pos.Y : BaseY;
            var ahead = new HashSet<long>();
            var pose = CheckPose(World, Def, Pos.X, baseY - sinkage, Pos.Z, Pos.Yaw, Pos.Dimension, ahead);
            if (pose == PoseState.Unloaded) return true;
            if (pose != PoseState.Blocked) return false;
            var here = new HashSet<long>();
            CheckPose(World, Def, Pos.X, baseY - Sinkage, Pos.Z, Pos.Yaw, Pos.Dimension, here);
            return !here.IsSupersetOf(ahead);
        }

        /// <summary>Is her main deck's edge in the waist under the still water (she's on the bottom, and the sea is on her deck)?</summary>
        bool DeckAwash()
        {
            foreach (var f in DamageParts.Downflood)
            {
                if (f.OpenFixture != null || f.Weight >= 1) continue;
                ShipFrame.ToWorld(Pos.Yaw, Trim, ListAngle, f.X, f.Y, f.Z, out _, out double wy, out _);
                if (FloatY + wy < BaseY) return true;
            }
            return false;
        }

        /// <summary>Server: a pump's brake worked once (held right-click at it: a stroke a second).</summary>
        public void PumpStroke(IServerPlayer player, FixtureDef pump) => PumpStroke(player?.PlayerUID ?? "?", pump);

        public void PumpStroke(string who, FixtureDef pump)
        {
            if (pump == null || GoneDown || !Mod.Config.Damage) return;
            if (!pumping.TryGetValue(pump.Code, out var at)) pumping[pump.Code] = at = new Dictionary<string, long>();
            at[who] = World.ElapsedMilliseconds;
            LocalToWorld(pump.Pos[0], pump.Pos[1] + 1, pump.Pos[2], out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/block/pump"), x, y, z, null, true, 16, Water > 0.01 ? 1f : 0.4f);
            Pumpers = CountPumpers();
        }

        /// <summary>Everyone who worked a pump within the last stroke and a half, at most PumpersPerPump at each.</summary>
        int CountPumpers()
        {
            var d = DamageParts?.D;
            if (d == null) return 0;
            long now = World.ElapsedMilliseconds, keep = (long)(1500 * Math.Max(0.2, d.PumpStroke));
            int n = 0;
            string worked = "";
            foreach (var pa in pumping)
            {
                var at = pa.Value;
                foreach (var kv in at.ToArray()) if (now - kv.Value > keep) at.Remove(kv.Key);
                n += Math.Min(at.Count, d.PumpersPerPump);
                if (at.Count > 0) worked = worked.Length == 0 ? pa.Key : worked + "," + pa.Key;
            }
            PumpsWorked = worked;
            return n;
        }

        // ------------------------------------------------------------------ repairs (server)

        /// <summary>
        /// A repair asked for at a part (held right-click with the items, a few seconds): from aboard, or a diver outside
        /// under the water for a panel below it. Takes the items from the hand and then the inventory (none in creative).
        /// </summary>
        public void TryRepair(IServerPlayer player, int part, string held)
        {
            var ep = player?.Entity;
            if (ep == null || !ep.Alive || PartDamage == null) return;
            string err = RepairReach(ep, part);
            if (err != null) { player.SendIngameError("hansa-repair", Lang.GetL(player.LanguageCode, err)); return; }
            var inv = player.InventoryManager;
            var slots = new List<ItemSlot>();
            if (inv.ActiveHotbarSlot != null) slots.Add(inv.ActiveHotbarSlot);
            foreach (string name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
                if (inv.GetOwnInventory(name) is IInventory own)
                    foreach (var s in own) if (s != null && !slots.Contains(s)) slots.Add(s);
            bool free = player.WorldData.CurrentGameMode == EnumGameMode.Creative;
            string msg = Repair(part, slots, held, free, out var job, out object[] args);
            if (job == null) { player.SendIngameError("hansa-repair", Lang.GetL(player.LanguageCode, msg, args)); return; }
            player.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(player.LanguageCode, msg, args), EnumChatType.Notification);
        }

        /// <summary>Can this someone reach the part to mend it? Null if so, else why not (a lang code).</summary>
        public string RepairReach(EntityAgent ep, int part)
        {
            var l = DamageParts;
            if (l == null || part < 0 || part >= l.Count) return "hansa:repair-nothing";
            if (GoneDown) return "hansa:repair-gone-down";
            var p = l.Parts[part];
            var local = WorldToLocal(ep.Pos.X, ep.Pos.Y + 1, ep.Pos.Z);
            if (p.DistanceTo(local) > 7 && p.Kind != DamageKind.Sail && p.Kind != DamageKind.Rigging && p.Kind != DamageKind.Rudder) return "hansa:repair-too-far";
            bool aboard = ep.MountedOn is ShipSeat seat && seat.Ship == this;
            // below the water a panel is mended from inside her, or by a diver outside
            if (p.Kind == DamageKind.Hull && p.Center.Y < Sinkage + Def.Waterline && !aboard && !ep.IsEyesSubmerged()) return "hansa:repair-below";
            if ((p.Kind == DamageKind.Sail || p.Kind == DamageKind.Rigging || p.Kind == DamageKind.Rudder || p.Kind == DamageKind.Mast) && !aboard) return "hansa:repair-aboard";
            return null;
        }

        /// <summary>
        /// Mend a part with what these slots hold (the held item's kind of repair first; free: take nothing). Returns the
        /// message (a lang code, with args); job is null if nothing was mended.
        /// </summary>
        public string Repair(int part, IList<ItemSlot> slots, string held, bool free, out RepairJob job, out object[] args)
        {
            // ((0.3.30, the user: "repairing missing hull elements from inside plugs only inner layer ... better would be ability
            // to plug both from inside") a ceiling that's out with her planking behind it out too: the planking laid first
            // (it keeps the sea out), then the ceiling if what's in hand goes round
            var l0 = DamageParts;
            if (l0 != null && part >= 0 && part < l0.Count && l0.Parts[part].Slot is int sl && sl >= 0 && HullBehind(sl) is int bh && bh >= 0 &&
                HullPanels != null && !HullPanels.Filled(sl))
            {
                string first = RepairOne(bh, slots, held, free, out job, out args);
                if (job == null) return first;
                var firstJob = job;
                var firstArgs = args;
                string second = RepairOne(part, slots, held, free, out var job2, out var args2);
                if (job2 != null) { args = args2; return second; }
                job = firstJob;
                args = firstArgs;
                return first;
            }
            return RepairOne(part, slots, held, free, out job, out args);
        }

        string RepairOne(int part, IList<ItemSlot> slots, string held, bool free, out RepairJob job, out object[] args)
        {
            job = null;
            args = Array.Empty<object>();
            var l = DamageParts;
            if (l == null || PartDamage == null || part < 0 || part >= l.Count) return "hansa:repair-nothing";
            var needs = ShipRepairs.Needs(l, PartDamage, part, held);
            if (needs.Count == 0) return "hansa:repair-nothing";
            var want = (held != null ? ShipRepairs.For(l, PartDamage, part, held) : null) ?? needs[0];
            args = new object[] { PartName(want.Part) };
            if (!want.AtSea) return "hansa:repair-port";
            // ((0.3.27) a hull panel that's out is laid again (the repair tool's way too): nothing in its space, nobody in it)
            int laySlot = want.What == "lay" ? l.Parts[want.Part].Slot : -1;
            // ((0.3.28) a deck cell stove in: laid again as a cell is)
            int layCell = want.What == "lay" ? l.Parts[want.Part].Cell : -1;
            if (want.What == "lay")
            {
                string lp = layCell >= 0 ? LayProblem(layCell, true) : laySlot < 0 ? "hansa:hull-fixed" : HullLayProblem(laySlot, true);
                if (lp != null) return lp;
            }
            if (!free)
            {
                var have = slots.Select(s => (s?.Itemstack?.Collectible?.Code?.ToString(), s?.Itemstack?.StackSize ?? 0)).ToList();
                if (!ShipRepairs.Plan(want, have, out int[] take, out _))
                {
                    args = new object[] { PartName(want.Part), KitsText(want) };
                    return "hansa:repair-needs";
                }
                for (int i = 0; i < slots.Count; i++)
                {
                    if (take[i] <= 0) continue;
                    slots[i].TakeOut(take[i]);
                    slots[i].MarkDirty();
                }
            }
            ShipRepairs.Apply(PartDamage, want);
            job = want;
            if (laySlot >= 0) SetHullState(laySlot, SlotState.Filled);
            if (layCell >= 0) LayCell(layCell, false);
            // ((0.3.27, note 207) the planking mended: the scars on it gone, and on her fixed planking round it)
            if (want.What is "planks" or "lay")
            {
                var mp = l.Parts[want.Part];
                ClearScars(new[] { mp.Min[0], mp.Min[1], mp.Min[2], mp.Max[0], mp.Max[1], mp.Max[2] }, 0.6);
            }
            SaveDamage(true);
            // ((0.3.26) a cracked hull panel mended is sound again)
            HullFollowsHurt();
            var c = l.Parts[want.Part].Center;
            LocalToWorld(c.X, c.Y, c.Z, out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation(want.What is "sail" ? "sounds/block/cloth" : want.What is "rigging" ? "sounds/effect/ropestretch" : "sounds/block/planks"),
                x, y, z, null, true, 16);
            return "hansa:repair-done-" + want.What;
        }

        /// <summary>(0.3.26) How many planks laying a hull panel takes.</summary>
        int LayCount(int part) => DamageParts?.Parts[part].Slot is int s && s >= 0 && Def.Hull != null ? Math.Max(1, Def.Hull.Slots[s].Mat.Count)
            : DamageParts?.Parts[part].Cell >= 0 ? Math.Max(1, DamageParts.Parts[part].LayBlocks) : 4;

        /// <summary>"4 planks, 2 nails and strips".</summary>
        public static string KitText(RepairKit kit) =>
            kit == null ? "" : string.Join(", ", kit.Items.Select(it => Lang.Get("hansa:item-" + ShipRepairs.ItemKey(it.Code), it.Count)));

        /// <summary>Every way a job can be done: "1 sail, or 4 cloth or linen (any colour)".</summary>
        public static string KitsText(RepairJob job) =>
            job == null ? "" : string.Join(Lang.Get("hansa:repair-or"), job.Kits.Select(KitText));

        // ------------------------------------------------------------------ going down (server)

        /// <summary>
        /// Her reserve buoyancy is gone: she goes down. Everyone aboard is put off into the water, the boats on her gallows
        /// float free, her hearth's fire goes out, the spawns bound to her end. Then she settles to the bottom
        /// (<see cref="SinkTick"/>).
        /// </summary>
        void StartSinking()
        {
            if (GoneDown) return;
            Stage = SinkStage.Sinking;
            sinkSpeed = 0;
            Water = DamageParts.Capacity;
            Anchored = true;
            SailSet = 0;
            Speed = 0;
            YawRate = 0;
            Rowing = 0;
            if (Math.Abs(leakSide) < 1e-6) leakSide = listSide;
            NotifyAboard("hansa:sinking-notice");
            // everyone off (they swim), and not back aboard when they log in again
            foreach (var s in seatArray.ToArray())
                if (s.Passenger is EntityAgent a) a.TryUnmount();
            Attributes.RemoveAttribute("hansaCrew");
            pendingPassengers.Clear();
            ReleaseBoats();
            // the sea puts her hearth's fire out (ShipHearth)
            DouseHearth();
            Mod.Spawns?.EndShip(EntityId, ShipSpawns.EndSunk);
            LocalToWorld(0, Def.DeckY, 0, out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/effect/woodgrind"), x, y, z, null, true, 48);
            World.PlaySoundAt(new AssetLocation("sounds/effect/water-pour"), x, y, z, null, true, 48);
            World.Logger.Audit("[hansa] {0} #{1} is going down at {2}", GetName(), EntityId, Pos.AsBlockPos);
            SaveState(true);
        }

        /// <summary>
        /// Going down: faster and faster, over to her list and down by the head or the stern, until she's on the bottom
        /// (or the world's floor, or sea that isn't loaded). The waves let go of her.
        /// </summary>
        void SinkTick(float dt)
        {
            if (Stage == SinkStage.Sunk) { Speed = 0; YawRate = 0; return; }
            var d = DamageParts.D;
            sinkSpeed = Math.Min(d.SinkSpeed, sinkSpeed + d.SinkAcceleration * dt);
            double next = Sinkage + sinkSpeed * dt;
            if (SettleBlocked(next) || FloatY - sinkSpeed * dt + Def.KeelY < 2) SunkNow();
            else Sinkage = next;
            // over and down by the head or the stern, as she goes
            int side = leakSide < 0 ? -1 : 1;
            ListAngle += Math.Clamp(side * d.SinkList * GameMath.DEG2RAD - ListAngle, -4 * GameMath.DEG2RAD * dt, 4 * GameMath.DEG2RAD * dt);
            Trim += Math.Clamp(-d.SinkTrim * GameMath.DEG2RAD - Trim, -1 * GameMath.DEG2RAD * dt, 1 * GameMath.DEG2RAD * dt);
            Heave = seaAttitude.Heave * Math.Max(0, 1 - Sinkage / 6);
            Pos.Y = BaseY - Sinkage + Heave;
            writtenY = Pos.Y;
        }

        /// <summary>She's on the bottom: the wreck's hook (<see cref="ShipWreck.OnSunk"/>).</summary>
        void SunkNow()
        {
            if (Stage == SinkStage.Sunk) return;
            Stage = SinkStage.Sunk;
            OnBottom = true;
            Speed = 0;
            YawRate = 0;
            Heave = 0;
            Pos.Y = BaseY - Sinkage;
            writtenY = Pos.Y;
            World.Logger.Audit("[hansa] {0} #{1} sank at {2}", GetName(), EntityId, Pos.AsBlockPos);
            SaveState(true);
            ShipWreck.OnSunk(this);
        }

        /// <summary>The boats on her gallows float free: each comes back as the boat it was (a rowing boat, a raft), on the water beside her.</summary>
        void ReleaseBoats()
        {
            foreach (var f in Def.Fixtures)
            {
                if (f.Type != "attach" || f.Kind != "boat" || !Attached.TryGetValue(f.Code, out var stack)) continue;
                Attached.Remove(f.Code);
                double side = f.Pos[2] < 0 ? -1 : 1;
                double lz = side * (Def.Beam / 2 + 2.5 + Math.Abs(f.Pos[2]));
                LocalToWorld(f.Pos[0], 0, lz, out double x, out _, out double z);
                double y = double.IsNaN(BaseY) ? Pos.Y : BaseY;
                var type = World.GetEntityType(stack.Collectible.Code);
                if (type == null)
                {
                    World.SpawnItemEntity(stack, new Vec3d(x, y + 0.5, z));
                    continue;
                }
                var boat = World.ClassRegistry.CreateEntity(type);
                boat.Pos.SetPos(x, y, z);
                boat.Pos.Yaw = Pos.Yaw;
                boat.Pos.Dimension = Pos.Dimension;
                boat.PositionBeforeFalling.Set(x, y, z);
                boat.Attributes.SetString("origin", "playerplaced");
                World.SpawnEntity(boat);
                ReleasedBoats.Add(boat.EntityId);
            }
            FittingsChanged();
        }

        /// <summary>The boats her sinking set free (for the self-test).</summary>
        public readonly List<long> ReleasedBoats = new();

        void NotifyAboard(string langCode, params object[] args)
        {
            foreach (var s in seatArray)
            {
                if (s.Passenger is EntityPlayer ep && ep.Player is IServerPlayer sp)
                {
                    var a = args.Select(o => o is string str && str.StartsWith("hansa:") ? Lang.GetL(sp.LanguageCode, str) : o).ToArray();
                    sp.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(sp.LanguageCode, langCode, a), EnumChatType.Notification);
                }
            }
        }

        // ------------------------------------------------------------------ what's said (clients)

        /// <summary>Client: roughly what a panel lets in now (litres a second), in still water.</summary>
        public double LeakEstimate(int part)
        {
            var l = DamageParts;
            if (l == null || PartDamage == null || part < 0 || part >= l.Count) return 0;
            var p = l.Parts[part];
            double area = l.LeakArea(p, PartDamage.Hurt[part], PartDamage.Wear[part]);
            if (area <= 0) return 0;
            double outside = Sinkage + Def.Waterline - p.Center.Y, inside = Water > 0 ? FloodLevel - p.Center.Y : -1;
            return Math.Max(0, 1000 * ShipFlood.Flow(l.D.LeakCoefficient, area, outside, inside));
        }

        /// <summary>The damage lines of the look-at box for a part the crosshair is on: its state, its leak, what mends it.</summary>
        public void DamageInfo(ShipPart part, System.Text.StringBuilder sb)
        {
            var l = DamageParts;
            if (l == null || PartDamage == null || !DamageOn) return;
            var parts = new List<int>();
            string behindLine = null;
            switch (part.Kind)
            {
                case ShipPartKind.Hull:
                case ShipPartKind.Deck:
                case ShipPartKind.Cell:
                    int i = l.Find(part);
                    if (i >= 0) parts.Add(i);
                    // ((0.3.30) a ceiling: her planking behind it, if that's hurt (it leaks there though the ceiling's whole))
                    if (i >= 0 && l.Parts[i].Inner && l.Parts[i].Partner is int q && q >= 0 && q < PartDamage.Count && StateOfPart(q) != PartState.Sound)
                    {
                        behindLine = StateOfPart(q) is PartState.Holed ? "hansa:damage-behind-out" : "hansa:damage-behind-cracked";
                        parts.Add(q);
                    }
                    break;
                case ShipPartKind.Piece:
                    // ((0.3.28) a partition, a door, a window, a railing shots hurt)
                    PieceDamageInfo(part.Index, sb);
                    break;
                case ShipPartKind.Mast:
                    // the mast, its rigging and its sails
                    foreach (var p in l.Parts)
                        if (p.Mast == part.Index && (p.Kind == DamageKind.Mast || p.Kind == DamageKind.Rigging || p.Kind == DamageKind.Sail)) parts.Add(p.Index);
                    break;
                case ShipPartKind.Sail:
                    // a sail looked at (SailRay): its name, then its state
                    int si = l.Find(part.Encode());
                    if (si >= 0)
                    {
                        parts.Add(si);
                        string name = PartName(si);
                        if (!string.IsNullOrEmpty(name)) sb.AppendLine(char.ToUpperInvariant(name[0]) + name.Substring(1));
                    }
                    break;
                case ShipPartKind.Fixture when part.Index < Def.Fixtures.Length:
                    var f = Def.Fixtures[part.Index];
                    if (f.Type == "helm") foreach (var p in l.Parts) if (p.Kind == DamageKind.Rudder) parts.Add(p.Index);
                    if (f.Type == "pump") sb.AppendLine(WaterText());
                    break;
            }
            foreach (int i in parts)
            {
                var p = l.Parts[i];
                var st = StateOfPart(i);
                // round a mast: the mast itself, and what of its rigging and sails isn't sound
                if (parts.Count > 1 && p.Kind != DamageKind.Mast && st == PartState.Sound) continue;
                string state = Lang.Get("hansa:state-" + st.ToString().ToLowerInvariant());
                string who = parts.Count > 1 ? PartName(i) + ": " : "";
                double leak = LeakEstimate(i);
                string line = leak >= 0.05 ? Lang.Get("hansa:damage-line-leak", who + state, leak) : who + state;
                sb.AppendLine(line.Length > 0 ? char.ToUpperInvariant(line[0]) + line.Substring(1) : line);
                if (p.Kind == DamageKind.Hull && Fouling > 0.2f && p.Center.Y < Def.Waterline && !Clad && parts.Count == 1)
                    sb.AppendLine(Lang.Get("hansa:damage-foul", (int)Math.Round(Fouling * 100)));
            }
            if (behindLine != null) sb.AppendLine(Lang.Get(behindLine));
            // what mends it: the repair for what's in hand, else what it needs
            if (parts.Count > 0)
            {
                var held = (Api as ICoreClientAPI)?.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString();
                foreach (int i in part.Kind == ShipPartKind.Mast ? parts.Take(1) : parts)
                {
                    var needs = ShipRepairs.Needs(l, PartDamage, i, held);
                    foreach (var job in needs.Take(part.Kind == ShipPartKind.Mast ? 3 : 2))
                        sb.AppendLine(!job.AtSea ? Lang.Get("hansa:repair-hint-port") : job.What == "lay" ? Lang.Get("hansa:repair-hint-lay", LayCount(job.Part), LayCount(job.Part) * BoardsPerPlanks)
                            : Lang.Get("hansa:repair-hint-" + job.What, KitsText(job)));
                }
            }
        }

        /// <summary>"Water in the hold: 12 t, rising (4 l/s)", or dry.</summary>
        public string WaterText()
        {
            if (Water < 0.05) return Lang.Get("hansa:water-dry");
            string trend = WaterRate > 0.002 ? Lang.Get("hansa:water-rising", WaterRate * 1000)
                : WaterRate < -0.002 ? Lang.Get("hansa:water-falling", -WaterRate * 1000) : Lang.Get("hansa:water-steady");
            double deep = FloodLevel - (DamageParts?.HoldBottom ?? 0);
            return Lang.Get("hansa:water-in-hold", Water, deep, trend) + (Pumpers > 0 ? " · " + Lang.Get("hansa:water-pumpers", Pumpers) : "");
        }

        /// <summary>Client: this frame's drawn sinkage, list and trim, eased toward the synced ones.</summary>
        void EaseFlood(float dt)
        {
            if (double.IsNaN(sinkShown)) { sinkShown = Sinkage; listShown = ListAngle; trimShown = Trim; }
            double k = 1 - Math.Exp(-dt * (GoneDown ? 4 : 1.5));
            sinkShown += (Sinkage - sinkShown) * k;
            listShown += (ListAngle - listShown) * k;
            trimShown += (Trim - trimShown) * k;
        }
    }

    /// <summary>
    /// What becomes of a ship that has gone down (the wreck: task 5 of the damage plan builds it). Until then she lies on
    /// the bottom as she is, frozen: no motion, no sailing, no sea kept out of her; her storages are kept and a diver can
    /// open them.
    /// </summary>
    public static class ShipWreck
    {
        /// <summary>Server: she's resting on the bottom (called once, when she gets there). Replace this to make the wreck.</summary>
        public static Action<EntityShip> Sunk;

        /// <summary>How many have sunk since the server started (for the self-test).</summary>
        public static int SunkCount;

        public static void OnSunk(EntityShip ship)
        {
            SunkCount++;
            ship.Api?.Logger.Notification("[hansa] {0} #{1} lies on the bottom at {2}", ship.GetName(), ship.EntityId, ship.Pos.AsBlockPos);
            Sunk?.Invoke(ship);
        }
    }
}
