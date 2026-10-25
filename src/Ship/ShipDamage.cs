using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// How a design takes damage, leaks, floods and founders (ships/*.json "damage"; the defaults suit the carrack). Energy
    /// is in kJ, water in cubic blocks (a block is a metre: a cubic block of water is a tonne), leak areas in square
    /// blocks.
    /// </summary>
    public class DamageDef
    {
        // ---- what a part takes: the energy that wrecks a sound one (it soaks up at most what it has left)
        public float HullToughness = 80, DeckToughness = 60, RudderToughness = 60, MastToughness = 400, SailToughness = 25, RiggingToughness = 80;
        /// <summary>
        /// (0.3.29, LINED: her side under her weather decks two layers) A ceiling panel (the inner layer) takes this (kJ), and
        /// of a blow on the planking in front of it (the outer layer) it takes CeilBacking's share (up to what it has left; the
        /// planking the rest). A grounding or scraping blow (not a shot) hurts it only once the planking is cracked or out
        /// (what it takes till then is only absorbed). Behind sound ceiling a holed panel lets in LinedLeak of its flow; a
        /// ceiling panel itself never leaks.
        /// </summary>
        public float CeilToughness = 40, CeilBacking = 0.3f, LinedLeak = 0.5f;
        /// <summary>What woodwork inside her (a bulkhead, a beam, a chest) takes out of a shot passing through it.</summary>
        public float StructureToughness = 30;
        /// <summary>
        /// (0.3.28, step D2) A deck cell of a design with deck slots (a block of a deck's planking, a damage part of its own):
        /// what it takes, as a hull panel (28 kJ cracks one, 56 stoves it in; the shot goes on through the opening).
        /// </summary>
        public float CellToughness = 80;
        /// <summary>
        /// (0.3.28, step D2) Pieces put aboard that take shots: a partition (a block of plank wall), a door, a window, a
        /// railing. Cracked at Cracked, shot away at Holed (as the planking's ladder); what they take is lost to the shot.
        /// </summary>
        public float PartitionToughness = 30, DoorToughness = 40, WindowToughness = 15, RailingToughness = 20;

        // ---- the state ladder (0..1): the planking's hurt, and its seams' wear (the caulking working loose)
        public float Scraped = 0.05f, Cracked = 0.35f, Holed = 0.7f;
        public float Worn = 0.3f, Leaking = 0.7f;
        /// <summary>Sails: torn, shredded. Masts: sprung, broken. Rigging: frayed, parted. The rudder: damaged, wrecked.</summary>
        public float Torn = 0.3f, Shredded = 0.75f, Sprung = 0.4f, Broken = 0.85f, Frayed = 0.3f, Parted = 0.75f, RudderDamaged = 0.3f, RudderWrecked = 0.75f;
        /// <summary>
        /// How much of a sail's cloth still draws (its area for drive, heel and steadying her roll): a sound sail all of it
        /// (a worn one a little less, down to 0.9 just short of torn), a torn one TornArea, a shredded one ShreddedArea.
        /// </summary>
        public float TornArea = 0.6f, ShreddedArea = 0.15f;
        /// <summary>A blow to the planking starts the seams round it: their wear goes up by this much of the hurt.</summary>
        public float SeamShock = 0.8f;

        // ---- leaks: a panel's effective hole (square blocks) in each state; the flow is LeakCoefficient × area × √head
        public float WornLeak = 5e-6f, CrackedLeak = 3e-4f, LeakingLeak = 8e-4f;
        /// <summary>A hole: this big when the panel is just holed, up to HoleLeakMax when it's stove in altogether.</summary>
        public float HoleLeak = 0.03f, HoleLeakMax = 0.2f;
        /// <summary>
        /// (0.3.26, step D1) A hull panel out (a design with hull slots: stove in or taken out, the whole panel gone): its
        /// effective hole for a panel of 1.5 square blocks (bigger ones in proportion), once the sea is over its head; between
        /// its sill and its head the sea pours over its sill (a weir). One out 1-2 deep lets in about 0.75 tonnes a second.
        /// </summary>
        public float OpenLeak = 0.2f;
        /// <summary>Cd·√(2g): a sharp-edged hole (0.6) under a head of water.</summary>
        public float LeakCoefficient = 2.66f;

        // ---- the sea coming aboard over her side (a weir: coefficient × length × depth^1.5). What comes over the rail,
        // through a port or the scuppers lands on the open deck and mostly runs off again: DeckShare of it gets below
        // (gratings, the seams), OpenHatchShare more for each hatch in the main deck standing open (and the companion
        // house's door: since 0.3.7 the companionway has one). The sea over an open hatch or door itself goes straight
        // down.
        public float DownfloodCoefficient = 1.7f, DeckShare = 0.05f, OpenHatchShare = 0.15f;
        /// <summary>Foundering (besides a hold nearly full): the sea getting below faster than this (tonnes a second) beyond what the pumps take out.</summary>
        public float FounderRate = 0.3f;

        // ---- pumps: water a stroke (a vanilla bucket's 10 litres), seconds a stroke, people at one pump's brake
        // (10 l a stroke at first, a real pump's; the user found it too slow: 80 in 0.3.5, 200 in 0.3.10 (the user: "something
        // like 200 l/s"; a fresh hole is then held by one man at the brake)
        public float PumpRate = 0.2f, PumpStroke = 1f;
        /// <summary>
        /// The pump's intake is the bilge's lowest point (the pump well by the keel): it brings up its full rate whatever the
        /// water does (heel, pitch, sloshing) until less than this is left in her (tonnes), then sucks air until this much
        /// again and twice over has come in.
        /// </summary>
        public float PumpResidual = 0.05f;
        public int PumpersPerPump = 2;

        // ---- what the water in her does
        /// <summary>Her speed and turning are divided by 1 + this × water / displacement.</summary>
        public float SlowPerWater = 2f;
        /// <summary>A list (degrees) toward the side the water came in or the side she heels, at water = displacement, before the free surface adds to it.</summary>
        public float ListPerWater = 12f;
        /// <summary>Her list afloat, at most (degrees).</summary>
        public float MaxList = 35f;
        /// <summary>Roll damping the water sloshing inside adds (as a fraction of critical, at a free surface as big as her stability).</summary>
        public float FreeSurfaceDamping = 0.3f;

        // ---- foundering and sinking
        /// <summary>Foundering: water past this share of her reserve buoyancy (or the sea coming in over her side).</summary>
        public float FounderAt = 0.8f;
        /// <summary>Going down: how fast at most (blocks/s), how fast she gets there, and the list and trim she goes down with (degrees).</summary>
        public float SinkSpeed = 1.5f, SinkAcceleration = 0.12f, SinkList = 30f, SinkTrim = 6f;

        // ---- grounding and collisions
        /// <summary>The share of her kinetic energy (½·mass·v², at each place she touches) that goes into the planking there.</summary>
        public float GroundShare = 0.05f;      // (0.3.9: hull R is heavier and her panels 2 long: 0.06 holed her at 2.5 b/s)
        /// <summary>How far round a place she touches the damage spreads (blocks).</summary>
        public float GroundRadius = 1.25f;
        /// <summary>Lying on the ground in a seaway: kJ a second at each place she touches, per block of the waves' height.</summary>
        public float GrindRate = 3f;

        // ---- wear (game time, while she's afloat and loaded)
        /// <summary>Days for sound seams to wear right out (worn at a third of it, leaking at 0.7), more in a seaway.</summary>
        public float SeamWearDays = 60;
        /// <summary>Days for an unsheathed bottom to foul right up; a foul bottom makes her FoulingSlow slower.</summary>
        public float FoulingDays = 90, FoulingSlow = 0.15f;
        // Sails (0.3.10; wind in vanilla units, 25 m/s each: Sea's Beaufort, force 8 from 0.69, 10 from 0.98, 12 from 1.31).
        // Real canvas lasted months: it chafed slowly, and was blown out by a gale carried with too much sail set, or taken
        // aback hard. See SailWear.
        /// <summary>Chafe: hurt a day under full sail in a working wind (SailChafeWind and up; less in lighter airs, none furled).</summary>
        public float SailChafePerDay = 0.002f, SailChafeWind = 0.3f;
        /// <summary>
        /// Gales: the load on the sails, sail set^SailLoadExponent × (wind / SailGaleWind)² (the apparent wind); past 1 they're
        /// over-pressed. (0.3.24, the user: "sails strain already at force 7-8, which is probably realistic but not fun: after
        /// a calm I want to go fast") Full sail is over-pressed from force 9 (0.832: 20.8 m/s), reefed (0.55 set) only past
        /// force 12. The server's SailStrainForce setting moves it (<see cref="SailWear.WithForces"/>).
        /// </summary>
        public float SailGaleWind = 0.832f, SailLoadExponent = 2f;
        /// <summary>
        /// Taken aback (her head within SailingDef.NoGo of the true wind: Sailing.Aback): the square sails' load, set² × (wind / SailAbackWind)².
        /// A tack in a fresh breeze costs nothing; caught aback under full sail in a gale they go in minutes. (0.3.24: from
        /// force 9 as the gale's: below it nothing strains.)
        /// </summary>
        public float SailAbackWind = 0.832f;
        /// <summary>
        /// (0.3.24) The sails tear only in this much apparent wind on full sail (force 10: 0.98, 24.5 m/s), once their strain
        /// is full: in force 9 they strain and the panel warns, but they hold. Reefed they'd tear past SailTearWind / 0.55.
        /// The server's SailTearForce setting moves it.
        /// </summary>
        public float SailTearWind = 0.98f;
        /// <summary>
        /// (0.3.12, the user: storm winds tore the sails too fast while they weren't watching.) The strain: over-pressed
        /// (the load past 1, a gale's or aback), the sails' strain builds from 0 toward 1 in real seconds, SailStrainSeconds
        /// at a load of 2 or more (longer the less it's over: a load of 1.25 takes four times as long); back under, it eases
        /// off in SailStrainRelaxSeconds. Only at 1 do they tear: SailTearPerMinute × (load − 1) of a sail a real minute in
        /// a gale, SailAbackTearPerMinute × (load − 1) the square sails taken aback. Real seconds, not the calendar's hours,
        /// so sleeping aboard (the calendar running fast) doesn't shred them in an instant; the watch wakes the sleepers when
        /// the strain begins. (0.3.24) Full sail strains from force 9 but tears only from force 10 (after about 5 real
        /// minutes of it in the middle of force 10, 2 in force 12); reefed, never short of a hurricane.
        /// </summary>
        public float SailStrainSeconds = 150, SailStrainRelaxSeconds = 90, SailTearPerMinute = 0.04f, SailAbackTearPerMinute = 0.12f;
        /// <summary>(0.3.24) A shallow copy (the server's sail settings applied to it: <see cref="SailWear.WithForces"/>).</summary>
        public DamageDef Copy() => (DamageDef)MemberwiseClone();

        /// <summary>How far a square sail bellies forward of its yard at most (blocks): looking at a sail, and a shot hitting it.</summary>
        public float SailBelly = 1.4f;

        // ---- repairs
        /// <summary>Seconds of held right-click a repair takes.</summary>
        public float RepairSeconds = 4;
        /// <summary>A hit on a spot this small (radius, blocks) or smaller is a shot: it goes on through what it holes.</summary>
        public float PierceRadius = 0.5f;
        /// <summary>
        /// A shot that breaks through her planking (the hull's panels took some of it) tumbles and throws splinters: it keeps
        /// this much of what it had left. Across a sound hull its far side is then usually cracked and leaking, not holed
        /// (a second hit there, or a shot in through a hole already made, holes it). The user's choice, the eighth test.
        /// </summary>
        public float ThroughKeeps = 0.75f;
        /// <summary>
        /// (0.3.30, note 234, the user: "my mind we had idea that panel has damaged texture and then removed. Nothing about holes
        /// from cannon"; "a ball can go through a panel and damage the panels next to it") A shot on a design with slots
        /// (hull panels, deck cells) leaves only two marks: a slot cracked (its damaged texture: mend it) or out (lay it
        /// again). Where it strikes her fixed planking (a frame's strip, a wale, a cap) the nearest slot within RouteReach
        /// takes it instead; the slots round where it strikes within SplashReach take SplashShare of its energy between them
        /// (as hurt: what they take isn't lost to the shot), cracked or knocked out as the one it hit.
        /// </summary>
        public float RouteReach = 0.9f, SplashReach = 0.8f, SplashShare = 0.3f;

        /// <summary>The rudder (ship-local x0 y0 z0 x1 y1 z1), from the design; null: a box round the rudder's moving part.</summary>
        public double[] Rudder;
    }

    /// <summary>
    /// (0.3.13, the user's screenshots: "The wind's too strong for the sail she has set…" several times in a row) A
    /// notice said once per episode: due when the condition comes on, and not again until it has been off for
    /// <see cref="QuietSeconds"/> on end (a little hysteresis: a gusty wind hovering at the threshold took the sails'
    /// strain on and off every few seconds) and, if one is set, <see cref="CooldownSeconds"/> have passed since it was
    /// last said (none by default: a fresh episode is said).
    /// </summary>
    public sealed class EpisodeNotice
    {
        public double QuietSeconds = 60, CooldownSeconds = 0;
        double saidAt = double.NegativeInfinity, offSince = double.NegativeInfinity;
        bool inEpisode;

        /// <summary>Is the condition on now (at this time, seconds), and should it be said?</summary>
        public bool Due(bool on, double now)
        {
            if (!on)
            {
                if (inEpisode && double.IsNegativeInfinity(offSince)) offSince = now;
                if (inEpisode && now - offSince >= QuietSeconds) inEpisode = false;
                return false;
            }
            offSince = double.NegativeInfinity;
            if (inEpisode) return false;
            inEpisode = true;
            if (now - saidAt < CooldownSeconds) return false;
            saidAt = now;
            return true;
        }
    }

    /// <summary>
    /// How sails wear (before each sail's own rate and the server's factors): slow chafe whenever they're set (an hour of
    /// the calendar), and real damage only over-pressed for a while: in a gale with too much sail set, or taken aback hard
    /// (the square sails). The strain (0.3.12) runs in real seconds: see <see cref="DamageDef.SailStrainSeconds"/>.
    /// </summary>
    public static class SailWear
    {
        /// <summary>Chafe an hour: set (0..1) in this wind (vanilla units).</summary>
        public static double Chafe(DamageDef d, double set, double wind)
        {
            if (set <= 0.05 || wind <= 0) return 0;
            return d.SailChafePerDay / 24 * set * Math.Min(1, wind / Math.Max(0.01, d.SailChafeWind));
        }

        /// <summary>The load on a sail set this far in this wind, against what it stands (1 = where it starts to strain).</summary>
        public static double Load(double set, double wind, double stands, double exponent) =>
            set <= 0.05 ? 0 : Math.Pow(set, exponent) * (wind / Math.Max(0.01, stands)) * (wind / Math.Max(0.01, stands));

        /// <summary>The load in a gale: set this far in this apparent wind (1 = over-pressed).</summary>
        public static double GaleLoad(DamageDef d, double set, double wind) => Load(set, wind, d.SailGaleWind, d.SailLoadExponent);

        /// <summary>The square sails' load taken aback (the apparent wind on their fore side).</summary>
        public static double AbackLoad(DamageDef d, double set, double wind) => Load(set, wind, d.SailAbackWind, 2);

        /// <summary>
        /// How the strain changes a real second under this load (the worse of the gale's and aback's): up toward 1 while
        /// it's past 1 (at the full rate from a load of 2), down while it's under.
        /// </summary>
        public static double StrainRate(DamageDef d, double load) =>
            load > 1 ? Math.Min(1, load - 1) / Math.Max(1, d.SailStrainSeconds) : -1 / Math.Max(1, d.SailStrainRelaxSeconds);

        /// <summary>Real seconds of this load, from no strain, before the sails start to tear (infinite while it's under 1).</summary>
        public static double SecondsToTear(DamageDef d, double load) =>
            load > 1 ? Math.Max(1, d.SailStrainSeconds) / Math.Min(1, load - 1) : double.PositiveInfinity;

        /// <summary>(0.3.24) The gale's load from which strained sails tear (SailTearWind on full sail): (SailTearWind / SailGaleWind)², at least 1.</summary>
        public static double TearLoad(DamageDef d) => Math.Max(1, Sq(d.SailTearWind / Math.Max(0.01, d.SailGaleWind)));

        /// <summary>(0.3.24) The load aback from which strained square sails tear (SailTearWind on full sail).</summary>
        public static double AbackTearLoad(DamageDef d) => Math.Max(1, Sq(d.SailTearWind / Math.Max(0.01, d.SailAbackWind)));

        static double Sq(double v) => v * v;

        /// <summary>Tearing a real second in a gale, once the strain is full (0.3.24: none short of <see cref="TearLoad"/>).</summary>
        public static double Gale(DamageDef d, double load) => load > 1 && load >= TearLoad(d) ? d.SailTearPerMinute / 60 * (load - 1) : 0;

        /// <summary>Tearing a real second taken aback (a square sail), once the strain is full (0.3.24: from <see cref="AbackTearLoad"/>).</summary>
        public static double Aback(DamageDef d, double load) => load > 1 && load >= AbackTearLoad(d) ? d.SailAbackTearPerMinute / 60 * (load - 1) : 0;

        /// <summary>(0.3.24) Do strained sails tear under these loads (the gale's, and aback's on the square sails)?</summary>
        public static bool Tears(DamageDef d, double gale, double aback) => Gale(d, gale) > 0 || Aback(d, aback) > 0;

        /// <summary>Sea's Beaufort scale: the upper bound of each force 0..11 (m/s); force 12 from 32.7.</summary>
        static readonly double[] BeaufortUpper = { 0.5, 1.6, 3.4, 5.5, 8.0, 10.8, 13.9, 17.2, 20.8, 24.5, 28.5, 32.7 };

        /// <summary>
        /// (0.3.24) Where a Beaufort force begins, in vanilla wind units (25 m/s each, Sea's): force 9 gives 0.832. A fraction
        /// goes that far into the force (8.5: halfway through force 8); 1..13 (force 12 taken 4.2 m/s wide).
        /// </summary>
        public static double ForceFloor(double force)
        {
            force = Math.Clamp(force, 1, 13);
            int f = Math.Min(12, (int)Math.Floor(force));
            double lo = BeaufortUpper[f - 1], hi = f < 12 ? BeaufortUpper[f] : BeaufortUpper[11] + 4.2;
            return (lo + (hi - lo) * (force - f)) / 25.0;
        }

        /// <summary>
        /// (0.3.24) The design's sail limits with the server's settings: full sail strains from the bottom of force
        /// strainForce and tears only from tearForce (never below strainForce); the aback limit moves with the gale's (in
        /// the design's proportion). The design's own when both are 0 or less.
        /// </summary>
        public static DamageDef WithForces(DamageDef d, double strainForce, double tearForce)
        {
            if (d == null || strainForce <= 0 && tearForce <= 0) return d;
            var c = d.Copy();
            if (strainForce > 0)
            {
                c.SailGaleWind = (float)ForceFloor(strainForce);
                c.SailAbackWind = d.SailAbackWind / Math.Max(0.01f, d.SailGaleWind) * c.SailGaleWind;
            }
            if (tearForce > 0) c.SailTearWind = (float)ForceFloor(tearForce);
            c.SailTearWind = Math.Max(c.SailTearWind, c.SailGaleWind);
            return c;
        }

        /// <summary>
        /// What the helmsman is told of the sails' load (the wind panel, the helm's line): 0 nothing; 1 over-pressed, the
        /// strain building ("Gale: shorten sail!"); 2 so much that even reefed (0.55) they'd be over-pressed ("Storm: furl
        /// or she'll lose her sails"); 3 about to tear (the strain past 0.7) or tearing.
        /// </summary>
        public static int Warning(DamageDef d, double set, double wind, bool aback, double strain)
        {
            double gl = GaleLoad(d, set, wind), bl = aback ? AbackLoad(d, set, wind) : 0;
            double load = Math.Max(gl, bl);
            // (0.3.24: "about to tear" only where they would tear: from SailTearWind)
            if (strain >= 0.7 && Tears(d, gl, bl)) return 3;
            if (load <= 1) return 0;
            double reefed = Math.Max(GaleLoad(d, Math.Min(set, 0.55), wind), aback ? AbackLoad(d, Math.Min(set, 0.55), wind) : 0);
            return reefed > 1 ? 2 : 1;
        }
    }

    public enum DamageKind { Hull, Deck, Rudder, Mast, Sail, Rigging }

    [Flags]
    public enum DamageKinds { None = 0, Hull = 1, Deck = 2, Rudder = 4, Mast = 8, Sail = 16, Rigging = 32, Bottom = Hull | Rudder, Planking = Hull | Deck, All = 63 }

    /// <summary>A part's state, the worst step of its ladder it has reached.</summary>
    public enum PartState { Sound, Worn, Scraped, Cracked, Leaking, Holed, Torn, Shredded, Sprung, Broken, Frayed, Parted, Damaged, Wrecked }

    /// <summary>
    /// One part of a ship the damage model keeps a state for: a hull panel (the planking of one side between two frames by
    /// a band a block high: <see cref="ShipPart.Hull"/>), a deck between two frames, the rudder, a mast, a sail, a mast's
    /// rigging. Positions are ship-local.
    /// </summary>
    public class DamagePart
    {
        public int Index;
        public DamageKind Kind;
        /// <summary>What the crosshair calls it (hull and deck parts are the crosshair's own); its code is the key it's saved by.</summary>
        public ShipPart Part;
        public int Code;
        /// <summary>Its box (the cells of its planking, or round a spar or sail).</summary>
        public double[] Min, Max;
        /// <summary>Where it would leak: the middle of its outer face (a panel), else its middle.</summary>
        public Vec3d Center;
        /// <summary>Outward (a panel).</summary>
        public Vec3d Normal;
        /// <summary>Its outer face (square blocks): a hull panel's planking, a deck's top.</summary>
        public double Area;
        /// <summary>A sail's or rigging's mast (index into the design's masts), a mast's own; -1 else.</summary>
        public int Mast = -1;
        /// <summary>A sail's index into the design's sails; -1 else.</summary>
        public int Sail = -1;
        /// <summary>How fast its seams age, relative (panels wear unevenly: 0.7..1.3).</summary>
        public float WearRate = 1;
        /// <summary>(0.3.26, step D1) A hull panel of a design with hull slots: its slot (= its index: they come first); -1 else.</summary>
        public int Slot = -1;
        /// <summary>(0.3.29, LINED) A ceiling panel (her side's inner layer); the other layer's part (its index: hull parts are their slots), -1 none.</summary>
        public bool Inner;
        public int Partner = -1;
        /// <summary>(0.3.27) A hull panel's planks blocks to lay it (its slot's material); 0 else.</summary>
        public int LayBlocks;
        /// <summary>(0.3.28, step D2) A deck cell of a design with deck slots: its deck slot's index; -1 else.</summary>
        public int Cell = -1;

        public bool Planking => Kind == DamageKind.Hull || Kind == DamageKind.Deck;

        /// <summary>How far a point is from its box (0 inside).</summary>
        public double DistanceTo(Vec3d p)
        {
            double dx = Math.Max(0, Math.Max(Min[0] - p.X, p.X - Max[0]));
            double dy = Math.Max(0, Math.Max(Min[1] - p.Y, p.Y - Max[1]));
            double dz = Math.Max(0, Math.Max(Min[2] - p.Z, p.Z - Max[2]));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    /// <summary>A place where the sea comes aboard once it's under the waves: the waist's deck edge (the scuppers), its rail or a gun port, an open hatch or gate.</summary>
    public struct DownfloodPoint
    {
        public double X, Y, Z;
        /// <summary>Blocks of edge it stands for, and how freely water gets in there (the scuppers let in less than the open rail).</summary>
        public double Length, Weight;
        /// <summary>A hatch or gate: only while it stands open (null: always).</summary>
        public string OpenFixture;
        /// <summary>A hatch in the main deck: the sea over it goes straight below, and while it's open more of what's on deck does.</summary>
        public bool Hatch;
        /// <summary>(0.3.20, step B) A deck cell of the main deck (OpenFixture "cell:" + its index): open while it's taken up and no hatch is shut over it.</summary>
        public bool Cell;
    }

    /// <summary>
    /// Worked out once per design: every part the damage model keeps a state for, and her hydrostatics from the collision
    /// grid (the hull's section and the hold's air, layer by layer): how much deeper water in her puts her, where it stands
    /// inside, its free surface, her reserve buoyancy, and where the sea comes aboard.
    /// </summary>
    public partial class DamageLayout
    {
        public readonly ShipDefinition Def;
        public readonly DamageDef D;
        public readonly DamagePart[] Parts;
        readonly Dictionary<int, int> byCode = new();
        public readonly DownfloodPoint[] Downflood;
        /// <summary>Each sail's own frame and outline (by the design's sail index; null if it has no axis).</summary>
        public readonly SailFrame[] SailFrames;

        // per layer j of the grid (from OY + j·V): the hull's section (hull and hold, m²), the hold's air (m²), and the air's
        // free surface moment across her (Σ width³/12 · length, m⁴)
        readonly double[] hullArea, airArea, freeMoment;
        readonly double y0, v;
        readonly int top;
        /// <summary>Her displacement at her waterline (the ship's weight), what the hull takes between the waterline and the main deck's edge, what the hold holds.</summary>
        public readonly double Displacement, Reserve, Capacity;
        /// <summary>The waterplane's area, its moment across her (BM = that / displacement), and her metacentric height (from her roll period).</summary>
        public readonly double WaterplaneArea, BM, GM;
        public readonly double HoldBottom;

        public int Count => Parts.Length;
        /// <summary>(0.3.28, step D2) Each deck slot's part (by its index in the design's deckSlots), -1 for one that isn't a part (a beam's length).</summary>
        public readonly int[] CellParts;
        /// <summary>(0.3.28) Has she deck cells as parts (her decks' fixed strips are her structure)?</summary>
        public bool CellSlotted => CellParts.Length > 0;
        /// <summary>(0.3.28) A deck slot's part, or -1.</summary>
        public int CellPart(int slot) => slot >= 0 && slot < CellParts.Length ? CellParts[slot] : -1;

        public DamageLayout(ShipDefinition def)
        {
            Def = def;
            D = def.Damage ?? new DamageDef();
            var g = def.Grid;
            var map = def.PartMap;
            var mask = def.DryMask;
            v = g.V;
            y0 = g.OY;
            SailFrames = new SailFrame[def.Sails.Length];
            var parts = new List<DamagePart>();

            // ---- (0.3.26, step D1) a design with hull slots: its panels are the hull's parts, first (part i = slot i); the
            // fixed planking between them (the frames' strips, the wales, her ends) is her structure
            if (def.Hull != null)
                foreach (var s in def.Hull.Slots)
                {
                    var c = s.Centre;
                    var nrm = s.Normal?.Length == 3 ? new Vec3d(s.Normal[0], s.Normal[1], s.Normal[2]) : new Vec3d(0, 0, s.Side);
                    parts.Add(new DamagePart
                    {
                        Kind = DamageKind.Hull, Part = ShipPart.Hull(s.Side, s.Station, s.Band, s.HalfIndex, s.IsInner), Min = new[] { s.Box[0], s.Box[1], s.Box[2] },
                        Max = new[] { s.Box[3], s.Box[4], s.Box[5] }, Center = c, Normal = nrm, Area = s.Area > 0 ? s.Area : 1.5, Slot = s.Index, LayBlocks = Math.Max(1, s.Mat?.Count ?? 1),
                        Inner = s.IsInner, Partner = s.PartnerIndex,
                    });
                }
            // ---- (0.3.28, step D2) a design with deck slots: each plain deck cell is a part (cracked, stove in); her decks'
            // fixed strips are her structure (no per-station deck parts)
            CellParts = new int[def.Slots?.Count ?? 0];
            Array.Fill(CellParts, -1);
            if (def.Slots != null)
                foreach (var s in def.Slots.Slots)
                {
                    if (s?.Box == null || !s.IsCell) continue;
                    CellParts[s.Index] = parts.Count;
                    parts.Add(new DamagePart
                    {
                        Kind = DamageKind.Deck, Part = def.Slots.PartOf(s.Index, false), Min = new[] { s.Box[0], s.Box[1], s.Box[2] },
                        Max = new[] { s.Box[3], s.Box[4], s.Box[5] }, Center = new Vec3d(s.CX, s.Top, s.CZ), Normal = new Vec3d(0, 1, 0),
                        Area = Math.Max(0.1, (s.Box[3] - s.Box[0]) * (s.Box[5] - s.Box[2])), Cell = s.Index, LayBlocks = Math.Max(1, s.Mat?.Count ?? 1),
                    });
                }
            // ---- hull panels: the shell cells (the part map's), by side, station and band
            var panels = new SortedDictionary<int, PanelAcc>();
            double mid = (map.HullX0 + map.HullX1) / 2;
            int[][] dirs = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
            for (int i = 0; i < (def.Hull != null ? 0 : g.NX); i++)
                for (int j = 0; j < g.NY; j++)
                    for (int k = 0; k < g.NZ; k++)
                    {
                        if (!g.Solid(i, j, k) || !map.IsShell(i, j, k)) continue;
                        double x = g.OX + (i + 0.5) * v, y = g.OY + (j + 0.5) * v, z = g.OZ + (k + 0.5) * v;
                        if (y > map.HullTop || InMast(def, x, y, z)) continue;
                        int side = z < 0 ? -1 : 1;
                        int code = ShipPart.Hull(side, map.StationAt(x), map.BandAt(y)).Encode();
                        if (!panels.TryGetValue(code, out var acc)) panels[code] = acc = new PanelAcc();
                        acc.Add(g.OX + i * v, g.OY + j * v, g.OZ + k * v, v);
                        // its outer faces: away from her middle (down, out to its side, toward her nearer end), onto
                        // what's neither her nor her inside
                        foreach (var d in dirs)
                        {
                            bool outward = d[1] < 0 || d[2] != 0 && Math.Sign(d[2]) == side || d[0] != 0 && Math.Sign(d[0]) == Math.Sign(x - mid);
                            if (!outward) continue;
                            int ni = i + d[0], nj = j + d[1], nk = k + d[2];
                            if (g.Solid(ni, nj, nk) || mask != null && mask.Solid(ni, nj, nk)) continue;
                            acc.Face(x + d[0] * v / 2, y + d[1] * v / 2, z + d[2] * v / 2, d);
                        }
                    }
            foreach (var kv in panels)
            {
                var a = kv.Value;
                if (a.Cells < 2) continue;
                parts.Add(new DamagePart
                {
                    // (its box is its outer face's: the planking's cells of neighbouring panels overlap round her curves)
                    Kind = DamageKind.Hull, Part = ShipPart.Decode(kv.Key), Code = kv.Key, Min = a.FaceMin(v), Max = a.FaceMax(v),
                    Center = a.Faces > 0 ? new Vec3d(a.FX / a.Faces, a.FY / a.Faces, a.FZ / a.Faces) : new Vec3d(a.X / a.Cells, a.Y / a.Cells, a.Z / a.Cells),
                    Normal = a.Normal(ShipPart.Decode(kv.Key).Side), Area = Math.Max(a.Faces, 1) * v * v,
                });
            }

            // ---- decks: the tops at a deck's height, between two frames
            var decks = new SortedDictionary<int, PanelAcc>();
            for (int n = 0; n < (def.Slots != null ? 0 : def.Decks.Length); n++)
            {
                var dk = def.Decks[n];
                int j = (int)Math.Round((dk.Y - g.OY) / v) - 1;
                if (j < 0 || j >= g.NY) continue;
                for (int i = 0; i < g.NX; i++)
                {
                    double x = g.OX + (i + 0.5) * v;
                    if (x < dk.X0 || x > dk.X1) continue;
                    for (int k = 0; k < g.NZ; k++)
                    {
                        if (!g.Solid(i, j, k) || g.Solid(i, j + 1, k)) continue;
                        double z = g.OZ + (k + 0.5) * v;
                        if (InMast(def, x, dk.Y - 0.1, z)) continue;
                        int code = ShipPart.Deck(n, map.StationAt(x), false).Encode();
                        if (!decks.TryGetValue(code, out var acc)) decks[code] = acc = new PanelAcc();
                        acc.Add(g.OX + i * v, g.OY + j * v, g.OZ + k * v, v);
                        acc.Face(x, dk.Y, z, dirs[2]);
                    }
                }
            }
            foreach (var kv in decks)
            {
                var a = kv.Value;
                if (a.Faces < 4) continue;
                parts.Add(new DamagePart
                {
                    Kind = DamageKind.Deck, Part = ShipPart.Decode(kv.Key), Code = kv.Key, Min = a.Min, Max = a.Max,
                    Center = new Vec3d(a.FX / a.Faces, a.FY / a.Faces, a.FZ / a.Faces), Normal = new Vec3d(0, 1, 0), Area = a.Faces * v * v,
                });
            }

            // ---- the rudder
            var rb = D.Rudder ?? RudderBox(def);
            if (rb != null && rb.Length == 6)
                parts.Add(Spar(DamageKind.Rudder, new ShipPart { Kind = ShipPartKind.Rudder }, rb, -1));

            // ---- the masts, their sails and their rigging
            for (int n = 0; n < def.Masts.Length; n++)
            {
                var m = def.Masts[n];
                parts.Add(Spar(DamageKind.Mast, ShipPart.Mast(n), new[] { m.X - m.Radius, m.Y0, m.Z - m.Radius, m.X + m.Radius, m.Y1, m.Z + m.Radius }, n));
            }
            for (int s = 0; s < def.Sails.Length; s++)
            {
                var sd = def.Sails[s];
                // the sail as it's set full and not turned: a square sail from yardarm to yardarm down to its clews, bellied
                // forward; the lateen a triangle under its slanting yard (0.3.10: it was a square sail's box round the
                // yard's lower end, mostly forward of the mizzen, where the lateen isn't)
                var frame = SailFrames[s] = SailFrame.Of(def, sd, D.SailBelly);
                if (frame == null) continue;
                var box = new[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
                foreach (var c in frame.Corners())
                    for (int i = 0; i < 3; i++) { box[i] = Math.Min(box[i], c[i]); box[i + 3] = Math.Max(box[i + 3], c[i]); }
                int mast = MastOf(def, sd.Code);
                var p = Spar(DamageKind.Sail, new ShipPart { Kind = ShipPartKind.Sail, Index = s }, box, mast);
                p.Sail = s;
                p.Area = sd.Area;
                parts.Add(p);
            }
            for (int n = 0; n < def.Masts.Length; n++)
            {
                var m = def.Masts[n];
                double hw = Math.Max(1, def.Beam / 2);
                parts.Add(Spar(DamageKind.Rigging, new ShipPart { Kind = ShipPartKind.Rigging, Index = n },
                    new[] { m.X - 3.5, def.DeckY + 1, -hw, m.X + 3.5, m.Y1, hw }, n));
            }

            Parts = parts.ToArray();
            for (int i = 0; i < Parts.Length; i++)
            {
                Parts[i].Index = i;
                Parts[i].Code = Parts[i].Part.Encode();
                byCode[Parts[i].Code] = i;
                // uneven wear: a fixed spread by the part's code
                uint h = (uint)Parts[i].Code * 2654435761u;
                Parts[i].WearRate = 0.7f + 0.6f * ((h >> 16) & 0xFFFF) / 65535f;
            }

            // ---- hydrostatics, layer by layer
            int ny = g.NY;
            hullArea = new double[ny];
            airArea = new double[ny];
            freeMoment = new double[ny];
            top = 0;
            double hold = double.NaN;
            var cols = new int[g.NX];
            for (int j = 0; j < ny; j++)
            {
                Array.Clear(cols);
                int hull = 0, air = 0;
                double wpMoment = 0;
                for (int i = 0; i < g.NX; i++)
                {
                    int hw = 0;
                    for (int k = 0; k < g.NZ; k++)
                    {
                        if (mask == null || !mask.Solid(i, j, k)) continue;
                        hull++;
                        hw++;
                        if (!g.Solid(i, j, k)) { air++; cols[i]++; }
                    }
                    double w = hw * v, wa = cols[i] * v;
                    wpMoment += w * w * w / 12 * v;
                    freeMoment[j] += wa * wa * wa / 12 * v;
                }
                hullArea[j] = hull * v * v;
                airArea[j] = air * v * v;
                if (hull > 0) top = j;
                if (air > 0 && double.IsNaN(hold)) hold = g.OY + j * v;
                if (y0 + j * v <= def.Waterline && y0 + (j + 1) * v > def.Waterline) { WaterplaneArea = hullArea[j]; BM = wpMoment; }
            }
            HoldBottom = double.IsNaN(hold) ? def.KeelY : hold;
            Displacement = HullVolume(def.Waterline);
            Reserve = HullVolume(def.DeckY) - Displacement;
            Capacity = AirVolume(y0 + (top + 1) * v);
            BM = Displacement > 0 ? BM / Displacement : 0;
            // GM from her roll period: T = 2π·k / √(g·GM), with the radius of gyration k about 0.4 of her beam
            double kr = 0.4 * Math.Max(1, def.Beam), wr = 2 * Math.PI / Math.Max(0.5, def.Motion?.RollPeriod ?? 7.5);
            GM = wr * wr * kr * kr / 9.81;

            Downflood = DownfloodPoints(def, D);
        }

        class PanelAcc
        {
            public int Cells, Faces;
            public double X, Y, Z, FX, FY, FZ, NX, NY, NZ;
            public double[] Min = { double.MaxValue, double.MaxValue, double.MaxValue }, Max = { double.MinValue, double.MinValue, double.MinValue };
            public double[] FMin = { double.MaxValue, double.MaxValue, double.MaxValue }, FMax = { double.MinValue, double.MinValue, double.MinValue };

            public void Add(double x0, double y0, double z0, double v)
            {
                Cells++;
                X += x0 + v / 2; Y += y0 + v / 2; Z += z0 + v / 2;
                Min[0] = Math.Min(Min[0], x0); Min[1] = Math.Min(Min[1], y0); Min[2] = Math.Min(Min[2], z0);
                Max[0] = Math.Max(Max[0], x0 + v); Max[1] = Math.Max(Max[1], y0 + v); Max[2] = Math.Max(Max[2], z0 + v);
            }

            public void Face(double x, double y, double z, int[] d)
            {
                Faces++;
                FX += x; FY += y; FZ += z;
                NX += d[0]; NY += d[1]; NZ += d[2];
                FMin[0] = Math.Min(FMin[0], x); FMin[1] = Math.Min(FMin[1], y); FMin[2] = Math.Min(FMin[2], z);
                FMax[0] = Math.Max(FMax[0], x); FMax[1] = Math.Max(FMax[1], y); FMax[2] = Math.Max(FMax[2], z);
            }

            /// <summary>The box of its outer face (the face squares' middles, half a cell round them): where a blow on it lands. Else its cells'.</summary>
            public double[] FaceMin(double v) => Faces > 0 ? new[] { FMin[0] - v / 2, FMin[1] - v / 2, FMin[2] - v / 2 } : Min;
            public double[] FaceMax(double v) => Faces > 0 ? new[] { FMax[0] + v / 2, FMax[1] + v / 2, FMax[2] + v / 2 } : Max;

            public Vec3d Normal(int side)
            {
                double l = Math.Sqrt(NX * NX + NY * NY + NZ * NZ);
                return l > 1e-6 ? new Vec3d(NX / l, NY / l, NZ / l) : new Vec3d(0, 0, side);
            }
        }

        static bool InMast(ShipDefinition def, double x, double y, double z)
        {
            foreach (var m in def.Masts)
            {
                double dx = x - m.X, dz = z - m.Z;
                if (y >= m.Y0 - 0.1 && y <= m.Y1 + 0.1 && dx * dx + dz * dz <= (m.Radius + 0.3) * (m.Radius + 0.3)) return true;
            }
            return false;
        }

        static DamagePart Spar(DamageKind kind, ShipPart part, double[] box, int mast) => new()
        {
            Kind = kind, Part = part, Min = new[] { box[0], box[1], box[2] }, Max = new[] { box[3], box[4], box[5] },
            Center = new Vec3d((box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2), Normal = new Vec3d(0, 1, 0), Mast = mast,
        };

        /// <summary>The mast a sail is set on: its code's first word (main-course: the mainmast).</summary>
        public static int MastOf(ShipDefinition def, string sailCode)
        {
            string m = sailCode?.Split('-')[0];
            for (int n = 0; n < def.Masts.Length; n++) if (def.Masts[n].Code == m) return n;
            return def.Masts.Length > 0 ? 0 : -1;
        }

        /// <summary>A box round the rudder's moving part: from its heel up its (raked) stock to the main deck, 1.5 aft of it.</summary>
        static double[] RudderBox(ShipDefinition def)
        {
            foreach (var p in def.MovingParts)
            {
                if (p.Code != "rudder" || p.AxisPoint == null || p.AxisDir == null || Math.Abs(p.AxisDir[1]) < 0.1) continue;
                double len = (def.DeckY + 0.4 - p.AxisPoint[1]) / p.AxisDir[1];
                double x1 = p.AxisPoint[0] + p.AxisDir[0] * len, y1 = p.AxisPoint[1] + p.AxisDir[1] * len;
                return new[] { Math.Min(p.AxisPoint[0], x1) - 1.5, p.AxisPoint[1] - 0.2, -0.25, Math.Max(p.AxisPoint[0], x1) + 0.2, y1, 0.25 };
            }
            return null;
        }

        /// <summary>
        /// Where the sea comes aboard: along both sides of the waist (the main deck open to the sky) at the deck's edge
        /// (through the scuppers, a fifth as freely, a point each frame space) and where the side is lowest, every
        /// column of the grid: the bulwark's top, a gun port's sill (a lidded one only while it's open, the gun run out:
        /// shut, the bulwark over it), an entry port's gate (its top while it's shut); the hatches in the main deck, a door
        /// on it that leads below (one that unseals a room, over its sill) and the entry port gates while they stand open.
        /// </summary>
        static DownfloodPoint[] DownfloodPoints(ShipDefinition def, DamageDef d)
        {
            var list = new List<DownfloodPoint>();
            var g = def.Grid;
            double space = def.Frames?.Space ?? 1.5;
            int j = g.CellY(def.DeckY + 0.125);
            // the deck's edge at a column x: the outermost cells of the side just over the deck, or (-1, -1)
            (int kp, int ks) Sides(int i)
            {
                int kp = -1, ks = -1;
                for (int k = 0; k < g.NZ; k++) if (g.Solid(i, j, k)) { if (kp < 0) kp = k; ks = k; }
                return kp < 0 || ks <= kp ? (-1, -1) : (kp, ks);
            }
            for (double x = def.PartMap.HullX0 + space / 2; x < def.PartMap.HullX1; x += space)
            {
                if (def.RoomAt(x, def.DeckY + 0.5, 0) != null) continue;     // under a castle
                var (kp, ks) = Sides(g.CellX(x));
                if (kp < 0) continue;
                foreach (double z in new[] { g.OZ + kp * g.V, g.OZ + (ks + 1) * g.V })
                    list.Add(new DownfloodPoint { X = x, Y = def.DeckY, Z = z, Length = space, Weight = 0.2 });
            }
            // the side's lowest edge, each column: up from the deck while anything stands across the side (a block in from
            // the planking's outside: the bulwark leans in above the deck); a gun port is a gap in it
            for (int i = g.CellX(def.PartMap.HullX0); i <= g.CellX(def.PartMap.HullX1); i++)
            {
                double x = g.OX + (i + 0.5) * g.V;
                if (def.RoomAt(x, def.DeckY + 0.5, 0) != null) continue;
                var (kp, ks) = Sides(i);
                if (kp < 0) continue;
                foreach (int side in new[] { -1, 1 })
                {
                    int outer = side < 0 ? kp : ks, band = 4;
                    int jr = j;
                    for (; jr < g.NY; jr++)
                    {
                        bool across = false;
                        for (int n = 0; n < band && !across; n++) across = g.Solid(i, jr, outer - side * n);
                        if (!across) break;
                    }
                    double top = g.OY + jr * g.V;
                    double z = side < 0 ? g.OZ + kp * g.V : g.OZ + (ks + 1) * g.V;
                    // an entry port: its gate, shut, is the bulwark there (open, it's its own place below)
                    foreach (var f in def.Fixtures)
                        if (f.Type == "gate" && f.Box != null && x >= f.Box[0] && x <= f.Box[3] && Math.Sign(f.Box[2] + f.Box[5]) == side)
                            top = Math.Max(top, f.Box[4]);
                    // a gun port with a lid: its sill only while it's open; shut, the bulwark over the port
                    FixtureDef lidded = null;
                    foreach (var f in def.Fixtures)
                        if (f.Type == "gun" && f.Lid != null && f.Port?.Length >= 4 && !ShipCompartments.AlongX(f) && x >= f.Port[0] && x <= f.Port[2] &&
                            Math.Sign(f.Pos[2]) == side && top < f.Port[3] - 0.05)
                            lidded = f;
                    if (lidded != null)
                    {
                        list.Add(new DownfloodPoint { X = x, Y = Math.Max(def.DeckY, top), Z = z, Length = g.V, Weight = 1, OpenFixture = lidded.Code });
                        int jt = g.CellY(lidded.Port[3] + 0.01);
                        for (; jt < g.NY; jt++)
                        {
                            bool across = false;
                            for (int n = 0; n < band && !across; n++) across = g.Solid(i, jt, outer - side * n);
                            if (!across) break;
                        }
                        top = Math.Max(lidded.Port[3], g.OY + jt * g.V);
                    }
                    list.Add(new DownfloodPoint { X = x, Y = Math.Max(def.DeckY, top), Z = z, Length = g.V, Weight = 1 });
                }
            }
            // (0.3.22, step C) a lidded gun port in a castle's side (the waist's are in the columns above): its sill, each
            // column across it, only while it's open (the gun run out); shut (housed, or an empty gun place), the lid is
            // her side there, whether the port is cut in her collision or not
            foreach (var f in ShipCompartments.LiddedPorts(def))
            {
                // ((0.3.23, note 171) a gun on an open castle deck: the sea through its port lands on that deck, not below)
                if (ShipCompartments.PortRoom(def, f) == null) continue;
                var pb = ShipCompartments.PortBox(f);
                int side = ShipCompartments.PortSide(f);
                if (ShipCompartments.AlongX(f))
                {
                    // ((0.3.25) a stern chaser's port in a closed room: its sill, each column across it, at the transom's outside)
                    for (int k = g.CellZ(pb[2] + 1e-6); k <= g.CellZ(pb[5] - 1e-6); k++)
                    {
                        double z = g.OZ + (k + 0.5) * g.V;
                        if (z < pb[2] || z > pb[5]) continue;
                        list.Add(new DownfloodPoint { X = f.Out[0] < 0 ? pb[0] : pb[3], Y = Math.Max(def.DeckY, pb[1]), Z = z, Length = g.V, Weight = 1, OpenFixture = f.Code });
                    }
                    continue;
                }
                for (int i = g.CellX(pb[0] + 1e-6); i <= g.CellX(pb[3] - 1e-6); i++)
                {
                    double x = g.OX + (i + 0.5) * g.V;
                    if (x < pb[0] || x > pb[3] || def.RoomAt(x, def.DeckY + 0.5, 0) == null) continue;   // (the waist's: done)
                    list.Add(new DownfloodPoint { X = x, Y = Math.Max(def.DeckY, pb[1]), Z = side < 0 ? pb[2] : pb[5], Length = g.V, Weight = 1, OpenFixture = f.Code });
                }
            }
            foreach (var f in def.Fixtures)
            {
                bool below = f.Type == "door" && f.Unseals != null;          // a door to below decks: like a hatch
                if (f.Box == null || f.Type != "hatch" && f.Type != "gate" && !below) continue;
                // (a hatch or door in or on the main deck, not the hold's hatch in the lower deck)
                if (f.Box[1] < def.DeckY - 0.3) continue;
                list.Add(new DownfloodPoint
                {
                    X = (f.Box[0] + f.Box[3]) / 2, Y = f.Type == "hatch" ? f.Box[4] : f.Box[1], Z = (f.Box[2] + f.Box[5]) / 2,
                    Length = Math.Max(f.Box[3] - f.Box[0], f.Box[5] - f.Box[2]), Weight = 1, OpenFixture = f.Code,
                    Hatch = f.Type == "hatch" || below
                });
            }
            // (0.3.26, step D1; docs/slots.md §9) each hull panel over her main deck (a bulwark's, a castle's side): the sea
            // over its sill comes aboard onto her deck there while it's out
            if (def.Hull != null)
                foreach (var s in def.Hull.Slots)
                {
                    if (s?.Box == null || !s.Above(def) || s.IsInner) continue;      // ((0.3.29) the planking's: the sea's way)
                    list.Add(new DownfloodPoint { X = (s.Box[0] + s.Box[3]) / 2, Y = s.Sill, Z = s.OuterZ(s.Sill), Length = s.Length, Weight = 1, OpenFixture = "hull:" + s.Index });
                }
            // (0.3.20, step B; docs/slots.md §4) each deck cell of the main deck out in the open (not a castle's floor): an
            // opening while it's taken up and no hatch is shut over it, the sea over it straight below
            if (def.Slots != null)
                foreach (var s in def.Slots.Slots)
                {
                    if (s?.Box == null || !s.IsCell || Math.Abs(s.Top - def.DeckY) > 0.05 || def.RoomAt(s.CX, s.Top + 0.5, s.CZ)?.Enclosed == true) continue;
                    list.Add(new DownfloodPoint { X = s.CX, Y = s.Top, Z = s.CZ, Length = 1, Weight = 1, OpenFixture = "cell:" + s.Index, Hatch = true, Cell = true });
                }
            return list.ToArray();
        }

        // ------------------------------------------------------------------ finding parts

        /// <summary>The part with this code (a <see cref="ShipPart"/> index), or -1.</summary>
        public int Find(int code) => byCode.TryGetValue(code, out int i) ? i : -1;

        /// <summary>
        /// The damage part for what the crosshair calls a part: a hull panel or deck (the nearest one of the same side and
        /// deck, a station or band off, when the crosshair's hit point was on the edge of one), a mast. -1 if none.
        /// </summary>
        public int Find(ShipPart p)
        {
            int i = Find(p.Encode());
            if (i >= 0) return i;
            if (p.Kind == ShipPartKind.Hull)
            {
                for (int r = 1; r <= 2; r++)
                    for (int ds = -r; ds <= r; ds++)
                        for (int db = -r; db <= r; db++)
                            if ((i = Find(ShipPart.Hull(p.Side, p.Station + ds, p.Band + db).Encode())) >= 0) return i;
            }
            // ((0.3.28) a deck cell: its top's part, whichever side it's seen from)
            if (p.Kind == ShipPartKind.Cell) return Find(p.CellTop.Encode());
            if (p.Kind == ShipPartKind.Deck)
            {
                for (int ds = 1; ds <= 2; ds++)
                    foreach (int s in new[] { p.Station - ds, p.Station + ds })
                        if ((i = Find(ShipPart.Deck(p.Index, s, false).Encode())) >= 0) return i;
            }
            return -1;
        }

        /// <summary>
        /// The part of these kinds at a ship-local point: the rudder or a mast it's in; the hull panel or deck the
        /// crosshair's rule puts it in (side, station, band), when it's on or in its planking; else the nearest part's box
        /// within reach (a sail or the rigging only when nothing else is). -1 if none.
        /// </summary>
        public int PartAt(Vec3d p, DamageKinds kinds = DamageKinds.All, double reach = 1.5)
        {
            var map = Def.PartMap;
            foreach (var part in Parts)
                if ((part.Kind == DamageKind.Rudder || part.Kind == DamageKind.Mast) && Has(kinds, part.Kind) && part.DistanceTo(p) <= 0) return part.Index;
            if (Has(kinds, DamageKind.Deck))
            {
                for (int n = 0; n < Def.Decks.Length; n++)
                {
                    var dk = Def.Decks[n];
                    if (p.X < dk.X0 || p.X > dk.X1 || p.Y > dk.Y + 0.15 || p.Y < dk.Y - 0.4) continue;
                    int i = Find(ShipPart.Deck(n, map.StationAt(p.X), false).Encode());
                    if (i >= 0 && Parts[i].DistanceTo(p) < 0.3) return i;
                }
            }
            if (Has(kinds, DamageKind.Hull))
            {
                // ((0.3.26) a design with hull slots: the panel it's in or nearest, its box's)
                int i = Def.Hull != null ? HullSlotNear(p, 0.6) : Find(ShipPart.Hull(p.Z < 0 ? -1 : 1, map.StationAt(p.X), map.BandAt(p.Y)));
                if (i >= 0 && Parts[i].DistanceTo(p) < 0.6) return i;
            }
            int best = -1;
            foreach (bool aloft in new[] { false, true })
            {
                double bd = reach;
                foreach (var part in Parts)
                {
                    if (!Has(kinds, part.Kind) || (part.Kind is DamageKind.Sail or DamageKind.Rigging) != aloft) continue;
                    double d = part.DistanceTo(p);
                    // inside several boxes: the nearest middle
                    if (d <= 0) d = -1.0 / (1 + p.DistanceTo(part.Center));
                    if (d < bd) { bd = d; best = part.Index; }
                }
                if (best >= 0) return best;
            }
            return best;
        }

        /// <summary>
        /// The sail a ray (ship-local, o + t d) meets first as the sails are set now: the square yards braced by brace and
        /// a fore-and-aft sail sheeted by sheet (radians), furled to frac; (note 64) a lateen that dips where it is for the lee
        /// shown and its own sheet (<see cref="LateenRig"/>; NaN: the sheet). (note 104: "i point to main mast, but tooltip says
        /// mizzen sail") A lateen is met where its sail is drawn, as meshHit says (sail index, ray, range: t, -1 missed, NaN not
        /// known: the client's drawn mesh), or its yard; without it, its box. Its damage part, or -1; t where it meets it.
        /// </summary>
        public int SailAt(Vec3d o, Vec3d d, double range, double brace, double sheet, float frac, out double t, float leeShown = float.NaN,
                          double lateenSheet = double.NaN, Func<int, Vec3d, Vec3d, double, double> meshHit = null)
        {
            t = double.MaxValue;
            int best = -1;
            var rigs = LateenRig.Of(Def);
            foreach (var p in Parts)
            {
                if (p.Kind != DamageKind.Sail || p.Sail < 0 || p.Sail >= SailFrames.Length || SailFrames[p.Sail] is not SailFrame f) continue;
                double ts;
                if (p.Sail < rigs.Length && rigs[p.Sail] is LateenRig rig)
                {
                    // (in its own frame, as made: mirrored back and its yard's dip and turn undone)
                    var pose = rig.Pose(leeShown, double.IsNaN(lateenSheet) ? sheet : lateenSheet, frac);
                    rig.RayToFrame(pose, o, d, out var fo, out var fd);
                    double tm = meshHit?.Invoke(p.Sail, o, d, range) ?? double.NaN;
                    if (double.IsNaN(tm)) ts = PartMotion.RaySail(Def, f, 0, pose.Frac, fo, fd, range);
                    else
                    {
                        double ty = rig.RayYard(f, fo, fd, range);
                        ts = tm < 0 ? ty : ty < 0 ? tm : Math.Min(tm, ty);
                    }
                }
                else ts = PartMotion.RaySail(Def, f, f.ForeAndAft ? sheet : brace, frac, o, d, range);
                if (ts >= 0 && ts < t) { t = ts; best = p.Index; }
            }
            return best;
        }

        public static bool Has(DamageKinds kinds, DamageKind k) => ((int)kinds & (1 << (int)k)) != 0;

        /// <summary>(0.3.26) Has she hull slots (her hull's parts are her panels; the rest of her planking is structure)?</summary>
        public bool HullSlotted => Def.Hull != null;

        /// <summary>(0.3.26) The hull panel (its part) a point is in, or the nearest within reach of its box; -1 if none.</summary>
        public int HullSlotNear(Vec3d p, double reach)
        {
            int best = -1;
            double bd = reach;
            if (Def.Hull == null) return -1;
            for (int n = 0; n < Def.Hull.Count && n < Parts.Length; n++)
            {
                if (Parts[n].Slot != n) continue;
                double d = Parts[n].DistanceTo(p);
                if (d <= 0) d = -1.0 / (1 + p.DistanceTo(Parts[n].Center));
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        /// <summary>(0.3.28) What a part takes: a deck cell its own (<see cref="DamageDef.CellToughness"/>), else its kind's.</summary>
        public double Toughness(DamagePart p) => p.Cell >= 0 ? D.CellToughness : p.Inner ? D.CeilToughness : Toughness(p.Kind);

        /// <summary>
        /// (0.3.26, 0.3.28) A slot's part that's out by its hurt (a hull panel or a deck cell stove in): nothing there to take a
        /// blow, a shot goes through its opening.
        /// </summary>
        public bool OutByHurt(DamagePart p, float hurt) => (p.Slot >= 0 || p.Cell >= 0) && hurt >= D.Holed;

        public double Toughness(DamageKind k) => k switch
        {
            DamageKind.Hull => D.HullToughness,
            DamageKind.Deck => D.DeckToughness,
            DamageKind.Rudder => D.RudderToughness,
            DamageKind.Mast => D.MastToughness,
            DamageKind.Sail => D.SailToughness,
            _ => D.RiggingToughness,
        };

        // ------------------------------------------------------------------ hydrostatics

        /// <summary>The hull's volume (hull and hold) below ship-local y: what she displaces floating with her waterline there.</summary>
        public double HullVolume(double y) => Integrate(hullArea, y, true);

        /// <summary>The air in her hold below ship-local y (what water can fill).</summary>
        public double AirVolume(double y) => Integrate(airArea, y, false);

        double Integrate(double[] area, double y, bool extend)
        {
            double t = (y - y0) / v, vol = 0;
            int n = (int)Math.Floor(t);
            for (int j = 0; j < Math.Min(n, area.Length); j++) vol += area[j] * v;
            if (n >= 0 && n < area.Length) vol += area[n] * v * (t - n);
            // above the hull's inside mask (the main deck): she goes on as wide as she is there
            if (extend && t > top + 1) vol += area[top] * v * (t - (top + 1));
            return vol;
        }

        static double Solve(Func<double, double> f, double target, double lo, double hi)
        {
            for (int it = 0; it < 60; it++)
            {
                double m = (lo + hi) / 2;
                if (f(m) < target) lo = m; else hi = m;
            }
            return (lo + hi) / 2;
        }

        /// <summary>How much deeper she floats (blocks) with this much water in her: it weighs what the hull then displaces more.</summary>
        public double SinkageFor(double water)
        {
            if (water <= 0) return 0;
            double wl = Def.Waterline;
            return Solve(HullVolume, Displacement + water, wl, wl + 60) - wl;
        }

        /// <summary>Where the water in her stands (ship-local y, level in her own frame), or her hold's bottom when there's none.</summary>
        public double FloodLevel(double water)
        {
            if (water <= 0) return HoldBottom;
            double hi = y0 + (top + 1) * v;
            return water >= Capacity ? hi : Solve(AirVolume, water, y0, hi);
        }

        /// <summary>The water's free surface at this level (ship-local y): its moment across her (m⁴).</summary>
        public double FreeSurfaceMoment(double level)
        {
            int j = (int)Math.Floor((level - y0) / v);
            return j >= 0 && j < freeMoment.Length ? freeMoment[j] : 0;
        }

        // ------------------------------------------------------------------ the ladder

        /// <summary>The state of a part at this hurt and seam wear: the worst step of its ladder reached.</summary>
        public PartState StateOf(DamageKind k, float hurt, float wear)
        {
            var d = D;
            switch (k)
            {
                case DamageKind.Hull:
                case DamageKind.Deck:
                    if (hurt >= d.Holed) return PartState.Holed;
                    if (wear >= d.Leaking) return PartState.Leaking;
                    if (hurt >= d.Cracked) return PartState.Cracked;
                    if (hurt >= d.Scraped) return PartState.Scraped;
                    if (wear >= d.Worn) return PartState.Worn;
                    return PartState.Sound;
                case DamageKind.Sail:
                    return hurt >= d.Shredded ? PartState.Shredded : hurt >= d.Torn ? PartState.Torn : PartState.Sound;
                case DamageKind.Mast:
                    return hurt >= d.Broken ? PartState.Broken : hurt >= d.Sprung ? PartState.Sprung : PartState.Sound;
                case DamageKind.Rigging:
                    return hurt >= d.Parted ? PartState.Parted : hurt >= d.Frayed ? PartState.Frayed : PartState.Sound;
                default:
                    return hurt >= d.RudderWrecked ? PartState.Wrecked : hurt >= d.RudderDamaged ? PartState.Damaged : PartState.Sound;
            }
        }

        /// <summary>
        /// A panel's effective hole (square blocks): its seams (worn a little, leaking more) and its planking (cracked, or
        /// holed: bigger the more it's stove in). Decks and spars don't leak.
        /// </summary>
        public double LeakArea(DamagePart p, float hurt, float wear)
        {
            if (p.Kind != DamageKind.Hull) return 0;
            var d = D;
            double a = wear >= d.Leaking ? d.LeakingLeak : wear >= d.Worn ? d.WornLeak : 0;
            // ((0.3.26) a hull panel out: the whole panel's opening (ShipFlood.Leaks: an orifice once the sea's over its head, a weir under it))
            if (p.Slot >= 0 && hurt >= d.Holed) a += OpenArea(p);
            else if (hurt >= d.Holed) a += d.HoleLeak + (d.HoleLeakMax - d.HoleLeak) * Math.Clamp((hurt - d.Holed) / Math.Max(1e-3, 1 - d.Holed), 0, 1);
            else if (hurt >= d.Cracked) a += d.CrackedLeak;
            return a;
        }
    }

    public partial class DamageLayout
    {
        /// <summary>(0.3.26) A hull panel's opening while it's out: <see cref="DamageDef.OpenLeak"/> for 1.5 square blocks, in proportion.</summary>
        public double OpenArea(DamagePart p) => D.OpenLeak * Math.Max(0.1, p.Area) / 1.5;
    }

    /// <summary>The damage a ship has: each part's hurt (0 sound .. 1 wrecked) and seam wear, on the server as floats, synced as a byte each.</summary>
    public class DamageState
    {
        public readonly float[] Hurt, Wear;

        public DamageState(int n)
        {
            Hurt = new float[n];
            Wear = new float[n];
        }

        public int Count => Hurt.Length;

        public static byte Q(float v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

        /// <summary>For clients: two bytes a part (hurt, wear).</summary>
        public byte[] Pack()
        {
            var b = new byte[2 * Hurt.Length];
            for (int i = 0; i < Hurt.Length; i++)
            {
                b[2 * i] = Q(Hurt[i]);
                b[2 * i + 1] = Q(Wear[i]);
            }
            return b;
        }

        /// <summary>What <see cref="Pack"/> gave; false if it doesn't fit this layout (then nothing changes).</summary>
        public bool Unpack(byte[] b)
        {
            if (b == null || b.Length != 2 * Hurt.Length) return false;
            for (int i = 0; i < Hurt.Length; i++)
            {
                Hurt[i] = b[2 * i] / 255f;
                Wear[i] = b[2 * i + 1] / 255f;
            }
            return true;
        }

        /// <summary>
        /// Read what was saved by part code (so a design whose parts changed keeps what still matches): codes[i] is the part
        /// hurt[i] and wear[i] belong to.
        /// </summary>
        public void Load(DamageLayout layout, int[] codes, float[] hurt, float[] wear)
        {
            Array.Clear(Hurt);
            Array.Clear(Wear);
            if (codes == null || hurt == null || wear == null) return;
            for (int i = 0; i < codes.Length && i < hurt.Length && i < wear.Length; i++)
            {
                int n = layout.Find(codes[i]);
                if (n < 0) continue;
                Hurt[n] = Math.Clamp(hurt[i], 0, 1);
                Wear[n] = Math.Clamp(wear[i], 0, 1);
            }
        }

        public int[] Codes(DamageLayout layout)
        {
            var c = new int[layout.Count];
            for (int i = 0; i < c.Length; i++) c[i] = layout.Parts[i].Code;
            return c;
        }

        public void Clear()
        {
            Array.Clear(Hurt);
            Array.Clear(Wear);
        }
    }

    /// <summary>A part a blow reached: how much more hurt it has, and its state before and after.</summary>
    public struct DamageHit
    {
        public int Part;
        public float Added;
        public PartState Before, After;
    }

    /// <summary>(0.3.28, step D2) A piece put aboard a shot reached: its id, its hurt before and after.</summary>
    public struct PieceHit
    {
        public int Id;
        public float Before, After;
    }

    /// <summary>
    /// (0.3.28, step D2) What a blow needs to know of her beyond her parts' damage (null: nothing more): which of her slots'
    /// parts are out (a deck cell taken up or a design opening: nothing there to take a blow), how far a part may be hurt
    /// (a deck cell something stands on stops at cracked), and the pieces put aboard that take shots (a partition, a door, a
    /// window, a railing: their hurt, kept with the piece).
    /// </summary>
    public sealed class ShotHooks
    {
        /// <summary>A part (index) that's out by its slot's state, whatever its hurt says; null: by its hurt only.</summary>
        public Func<int, bool> Out;
        /// <summary>The most hurt a part may have (1: no limit); null: 1.</summary>
        public Func<int, float> Cap;
        /// <summary>The piece (id) a voxel of her grid is a solid of, if it's one that takes shots; -1 else. Null: none.</summary>
        public Func<int, int, int, int> PieceAt;
        /// <summary>A piece's toughness (kJ), its hurt now, the most it may have (a partition something hangs on: cracked), and its hurt set.</summary>
        public Func<int, double> PieceToughness;
        public Func<int, float> PieceHurt, PieceCap;
        public Action<int, float> SetPieceHurt;
    }

    /// <summary>What a blow did: the parts it hurt, whether it went through her planking, where it stopped or came out (ship-local).</summary>
    public class DamageResult
    {
        public readonly List<DamageHit> Hits = new();
        /// <summary>(0.3.28) The pieces put aboard it hurt (or shot away).</summary>
        public readonly List<PieceHit> PieceHits = new();
        /// <summary>A shot went through what it holed (at least once).</summary>
        public bool Pierced;
        /// <summary>Where it came out of her (ship-local), or null if it stopped in her.</summary>
        public Vec3d Exit;
        /// <summary>Energy it still had when it came out (kJ).</summary>
        public double Remaining;
        /// <summary>Anything whose state (the step of its ladder) changed.</summary>
        public bool StateChanged;
        /// <summary>(0.3.28) Did it do anything (a part or a piece hurt)?</summary>
        public bool Any => Hits.Count > 0 || PieceHits.Count > 0;
    }

    /// <summary>
    /// Blows to a ship, pure (both sides, and the offline tests). A blow at a point (ship-local) with an energy (kJ) is shared
    /// among the parts within its radius, the nearest most ((1 − d/r)², d the distance to a part's box); each takes up to
    /// what it has left (its toughness × (1 − hurt)), and the planking's seams round it start too. Penetration: a blow on a
    /// spot no bigger than <see cref="DamageDef.PierceRadius"/> is a shot, and what the parts it holed couldn't take goes
    /// on along its direction, through her voxels to the next thing in its way (a deck, the woodwork inside, the far side),
    /// losing what each takes, until it's spent or out of her. (0.3.28, step D2) A deck cell is a part as a hull panel is
    /// (stove in, the shot goes on through it); a piece put aboard in its way (a partition, a door, a window, a railing)
    /// takes what it can and is shot away when holed (<see cref="ShotHooks"/>).
    /// </summary>
    public static class ShipDamage
    {
        public const int MaxPierce = 6;

        /// <param name="factors">The server's factors on the hurt a blow does, by DamageKind (null: 1); the energy each part takes (and what goes on past it) is the same.</param>
        public static DamageResult Apply(DamageLayout layout, DamageState st, VoxelGrid grid, Vec3d point, Vec3d dir, double energy, double radius,
            DamageKinds kinds = DamageKinds.All, float[] factors = null, ShotHooks hooks = null)
        {
            var res = new DamageResult();
            radius = Math.Max(0.05, radius);
            // ((0.3.28) a shot landing on a piece put aboard first (a railing, a partition): it takes it, then on through her)
            if (hooks?.PieceAt != null && grid != null && dir != null && radius <= layout.D.PierceRadius && energy > 0 && dir.Length() > 1e-9)
            {
                double len = dir.Length();
                var u = new Vec3d(dir.X / len, dir.Y / len, dir.Z / len);
                var inside = point.AddCopy(u.X * 0.02, u.Y * 0.02, u.Z * 0.02);
                int id = hooks.PieceAt(grid.CellX(inside.X), grid.CellY(inside.Y), grid.CellZ(inside.Z));
                if (id >= 0)
                {
                    double left = HitPiece(layout, hooks, id, energy, res);
                    if (left >= 1) Pierce(layout, st, grid, point.AddCopy(u.X * 0.3, u.Y * 0.3, u.Z * 0.3), u, left, radius, kinds, res, 0, factors, hooks);
                    return res;
                }
            }
            Blow(layout, st, grid, point, dir, energy, radius, kinds, res, 0, factors, hooks);
            return res;
        }

        static bool IsOut(DamageLayout layout, DamageState st, ShotHooks hooks, int n) =>
            layout.OutByHurt(layout.Parts[n], st.Hurt[n]) || hooks?.Out?.Invoke(n) == true;

        static void Blow(DamageLayout layout, DamageState st, VoxelGrid grid, Vec3d point, Vec3d dir, double energy, double radius, DamageKinds kinds,
            DamageResult res, int depth, float[] factors, ShotHooks hooks)
        {
            if (energy <= 0) return;
            var d = layout.D;
            var near = new List<(int part, double w)>();
            double sum = 0;
            bool opening = false;
            foreach (var p in layout.Parts)
            {
                if (!DamageLayout.Has(kinds, p.Kind)) continue;
                // ((0.3.26) a hull panel that's out, (0.3.28) a deck cell that's out: nothing there to take it, the shot goes
                // through the opening)
                if ((p.Slot >= 0 || p.Cell >= 0) && IsOut(layout, st, hooks, p.Index)) { opening |= p.DistanceTo(point) < 1e-3; continue; }
                double dist = p.DistanceTo(point);
                if (dist >= radius) continue;
                double w = (1 - dist / radius) * (1 - dist / radius);
                near.Add((p.Index, w));
                sum += w;
            }
            // ((0.3.29, LINED) a ceiling panel whose planking is in the blow too: it takes its share through the planking
            // (CeilBacking), not on its own as well
            if (near.Count > 1)
                near.RemoveAll(nw => layout.Parts[nw.part].Inner && layout.Parts[nw.part].Partner is int pp && pp >= 0 && near.Any(o => o.part == pp));
            // (a grounding or scraping blow (not a shot): a ceiling whose planking is sound isn't reached)
            if (radius > d.PierceRadius)
                near.RemoveAll(nw => layout.Parts[nw.part].Inner && layout.Parts[nw.part].Partner is int pq && pq >= 0 && pq < st.Count && st.Hurt[pq] < d.Cracked);
            sum = near.Sum(nw => nw.w);
            // ((0.3.26) a shot on her fixed planking (a frame's strip, a wale, her ends; (0.3.28) a deck's fixed strip) where no
            // panel or cell is in reach: her structure, it takes some and the shot goes on; a broad blow (grounding, ramming)
            // the nearest part as before)
            bool structure = near.Count == 0 && (layout.HullSlotted || layout.CellSlotted) && radius <= d.PierceRadius;
            bool slotted = (layout.HullSlotted || layout.CellSlotted) && radius <= d.PierceRadius;
            if (structure && !opening)
            {
                // ((0.3.30, note 234) her fixed planking struck: the nearest slot (a hull panel, a deck cell) takes it, so it
                // shows as one cracked or out, mended as they are; none near: her structure, as before (nothing to show))
                int best = -1;
                double bd = d.RouteReach;
                foreach (var p in layout.Parts)
                {
                    if ((p.Slot < 0 && p.Cell < 0) || !DamageLayout.Has(kinds, p.Kind) || IsOut(layout, st, hooks, p.Index)) continue;
                    if (p.Inner && p.Partner >= 0) continue;            // (the planking, not the ceiling behind it)
                    double dist = p.DistanceTo(point);
                    if (dist < bd) { bd = dist; best = p.Index; }
                }
                if (best >= 0) { near.Add((best, 1)); sum = 1; structure = false; }
            }
            // ((0.3.28) a shot landing in an opening (a panel or a cell that's out, its voxels still in the grid it was given):
            // on through it, nothing taken)
            bool through = structure && opening;
            if (near.Count == 0 && !structure)
            {
                // ((0.3.26) a design with hull panels: her keel and flat floor are fixed; touching ground there the bilge's
                // panels nearest take it, up to her half beam away)
                int n = layout.PartAt(point, kinds, radius + (layout.HullSlotted ? 4 : 1));
                if (n >= 0 && !((layout.Parts[n].Slot >= 0 || layout.Parts[n].Cell >= 0) && IsOut(layout, st, hooks, n))) { near.Add((n, 1)); sum = 1; }
            }
            double left = through ? energy : structure ? energy - d.StructureToughness : 0, broke = 0;
            bool ceiling = false;
            foreach (var (n, w) in near)
            {
                var p = layout.Parts[n];
                double share = energy * w / sum, tough = layout.Toughness(p);
                // ((0.3.29, LINED) a planking panel with its ceiling behind it (not out): the ceiling takes CeilBacking of its
                // share (a grounding or scraping blow: only absorbed while the planking's sound)
                if (p.Kind == DamageKind.Hull && !p.Inner && p.Partner >= 0 && p.Partner < st.Count && !IsOut(layout, st, hooks, p.Partner))
                {
                    double back = share * Math.Clamp(d.CeilBacking, 0, 1);
                    share -= back;
                    var c = layout.Parts[p.Partner];
                    bool shot = radius <= d.PierceRadius;
                    double tc = layout.Toughness(c);
                    double tookC = Math.Min(back, Math.Max(0, tc * (1 - st.Hurt[c.Index])));
                    left += back - tookC;
                    if (tookC > 0 && (shot || st.Hurt[n] >= d.Cracked))
                    {
                        float fc = factors != null && (int)c.Kind < factors.Length ? factors[(int)c.Kind] : 1;
                        if (fc > 0)
                        {
                            var b0 = layout.StateOf(c.Kind, st.Hurt[c.Index], st.Wear[c.Index]);
                            float addc = (float)(tookC / tc * fc);
                            st.Hurt[c.Index] = Math.Min(1, st.Hurt[c.Index] + addc);
                            if (shot && (layout.HullSlotted || layout.CellSlotted)) st.Hurt[c.Index] = ShotStep(d, st.Hurt[c.Index]);
                            var a0 = layout.StateOf(c.Kind, st.Hurt[c.Index], st.Wear[c.Index]);
                            res.Hits.Add(new DamageHit { Part = c.Index, Added = addc, Before = b0, After = a0 });
                            if (a0 != b0) res.StateChanged = true;
                            broke += tookC;
                        }
                    }
                }
                // ((0.3.28) a deck cell with something standing on it stops at cracked: the rest goes on)
                float cap = hooks?.Cap != null ? Math.Clamp(hooks.Cap(n), 0, 1) : 1;
                double took = Math.Min(share, Math.Max(0, tough * (cap - st.Hurt[n])));
                left += share - took;
                if (took <= 0) continue;
                if (p.Kind == DamageKind.Hull || p.Cell >= 0) broke += took;
                if (p.Inner) ceiling = true;
                float factor = factors != null && (int)p.Kind < factors.Length ? factors[(int)p.Kind] : 1;
                if (factor <= 0) continue;
                var before = layout.StateOf(p.Kind, st.Hurt[n], st.Wear[n]);
                float added = (float)(took / tough * factor);
                st.Hurt[n] = Math.Min(Math.Max(cap, st.Hurt[n]), st.Hurt[n] + added);
                // (a deck cell's seams: no wear, its leak isn't the sea's)
                // ((0.3.30, note 234) a shot on a slot: cracked or out, nothing between (no seams worn, nothing scraped))
                if (p.Planking && p.Cell < 0 && !(slotted && p.Slot >= 0)) st.Wear[n] = Math.Min(1, st.Wear[n] + added * d.SeamShock);
                if (slotted && (p.Slot >= 0 || p.Cell >= 0)) st.Hurt[n] = ShotStep(d, st.Hurt[n], cap);
                var after = layout.StateOf(p.Kind, st.Hurt[n], st.Wear[n]);
                res.Hits.Add(new DamageHit { Part = n, Added = added, Before = before, After = after });
                if (after != before) res.StateChanged = true;
            }
            // ((0.3.30, note 234: "a ball can go through a panel and damage the panels next to it") the slots round where it
            // struck: SplashShare of its energy between them, cracked or knocked out as the one it hit)
            if (slotted && near.Count > 0)
                Splash(layout, st, point, energy, kinds, res, factors, hooks, near.Select(nw => nw.part).ToHashSet());
            // a shot goes on with what it had left, through her to the next thing in its way; broken through her planking
            // it tumbles (and throws splinters): less of it goes on
            if (broke >= 1 && radius <= d.PierceRadius) left *= Math.Clamp(d.ThroughKeeps, 0, 1);
            if (radius > d.PierceRadius || depth >= MaxPierce || left < 1 || grid == null || dir == null) return;
            double len = dir.Length();
            if (len < 1e-9) return;
            var u = new Vec3d(dir.X / len, dir.Y / len, dir.Z / len);
            // ((0.3.29, LINED) a ceiling it broke from inside: her planking is the next voxel on (not past the solid run it's in)
            double step = ceiling ? 0.02 : 0.3;
            Pierce(layout, st, grid, point.AddCopy(u.X * step, u.Y * step, u.Z * step), u, left, radius, kinds, res, depth, factors, hooks, !ceiling);
        }

        /// <summary>(0.3.30, note 234) A slot's hurt after a shot: cracked at least (its damaged texture), or out; never between.</summary>
        public static float ShotStep(DamageDef d, float hurt, float cap = 1)
        {
            if (hurt <= 0 || hurt >= d.Holed) return hurt;
            return Math.Max(hurt, Math.Min(d.Cracked, Math.Max(cap, hurt)));
        }

        /// <summary>
        /// (0.3.30, note 234) The slots (hull panels, deck cells) round a shot's strike, but those it hit: SplashShare of its
        /// energy shared among them by nearness within SplashReach ((1 − d/r)²), each as hurt (cracked or out: <see cref="ShotStep"/>).
        /// </summary>
        static void Splash(DamageLayout layout, DamageState st, Vec3d point, double energy, DamageKinds kinds, DamageResult res, float[] factors,
            ShotHooks hooks, HashSet<int> hit)
        {
            var d = layout.D;
            if (d.SplashShare <= 0 || d.SplashReach <= 0) return;
            var round = new List<(int part, double w)>();
            foreach (var p in layout.Parts)
            {
                if ((p.Slot < 0 && p.Cell < 0) || hit.Contains(p.Index) || !DamageLayout.Has(kinds, p.Kind) || IsOut(layout, st, hooks, p.Index)) continue;
                // (a ceiling behind a panel it hit: that one's backing took its share)
                if (p.Inner && p.Partner >= 0 && hit.Contains(p.Partner)) continue;
                double dist = p.DistanceTo(point);
                if (dist >= d.SplashReach) continue;
                double w = (1 - dist / d.SplashReach) * (1 - dist / d.SplashReach);
                round.Add((p.Index, w));
            }
            double sum = round.Sum(r => r.w);
            if (sum <= 0) return;
            double share = energy * d.SplashShare;
            foreach (var (n, w) in round)
            {
                var p = layout.Parts[n];
                float factor = factors != null && (int)p.Kind < factors.Length ? factors[(int)p.Kind] : 1;
                if (factor <= 0) continue;
                float cap = hooks?.Cap != null ? Math.Clamp(hooks.Cap(n), 0, 1) : 1;
                double tough = layout.Toughness(p);
                double took = Math.Min(share * w / sum, Math.Max(0, tough * (cap - st.Hurt[n])));
                if (took <= 0) continue;
                var before = layout.StateOf(p.Kind, st.Hurt[n], st.Wear[n]);
                float added = (float)(took / tough * factor);
                st.Hurt[n] = ShotStep(d, Math.Min(Math.Max(cap, st.Hurt[n]), st.Hurt[n] + added), cap);
                var after = layout.StateOf(p.Kind, st.Hurt[n], st.Wear[n]);
                res.Hits.Add(new DamageHit { Part = n, Added = added, Before = before, After = after });
                if (after != before) res.StateChanged = true;
            }
        }

        /// <summary>A shot going on from a point along u (normalised) with this much left: through her voxels to what's next.</summary>
        static void Pierce(DamageLayout layout, DamageState st, VoxelGrid grid, Vec3d from, Vec3d u, double left, double radius, DamageKinds kinds,
            DamageResult res, int depth, float[] factors, ShotHooks hooks, bool skip = true)
        {
            res.Pierced = true;
            var d = layout.D;
            var def = layout.Def;
            for (int guard = 0; guard < 40 && left >= 1; guard++)
            {
                double t = grid.RaycastCell(from, u, 60, out int ci, out int cj, out int ck, out int axis, skipStart: skip);
                skip = true;
                if (t > 60)
                {
                    res.Exit = from.AddCopy(u.X * 2, u.Y * 2, u.Z * 2);
                    res.Remaining = left;
                    return;
                }
                var at = from.AddCopy(u.X * t, u.Y * t, u.Z * t);
                // ((0.3.26) a hull panel it has just stove in (still in the grid it was given): on through its opening)
                int hs = def.Hull?.AtVoxel(grid, ci, cj, ck) ?? -1;
                if (hs >= 0 && hs < st.Count && st.Hurt[hs] >= d.Holed)
                {
                    // ((0.3.29, LINED) a ceiling stove in with its planking sound right behind it: on into the next voxel)
                    var hp = layout.Parts[hs];
                    if (hp.Inner && hp.Partner >= 0 && hp.Partner < st.Count && st.Hurt[hp.Partner] < d.Holed)
                    {
                        from = at.AddCopy(u.X * (LeaveCell(grid, at, u, ci, cj, ck) + 1e-4), u.Y * (LeaveCell(grid, at, u, ci, cj, ck) + 1e-4), u.Z * (LeaveCell(grid, at, u, ci, cj, ck) + 1e-4));
                        skip = false;
                        continue;
                    }
                    from = at.AddCopy(u.X * 0.3, u.Y * 0.3, u.Z * 0.3);
                    continue;
                }
                // ((0.3.28) a deck cell it has just stove in, or one that's out: on through the opening)
                int cp = layout.CellPart(def.Slots?.AtVoxel(grid, ci, cj, ck) ?? -1);
                if (cp >= 0 && IsOut(layout, st, hooks, cp))
                {
                    from = at.AddCopy(u.X * 0.3, u.Y * 0.3, u.Z * 0.3);
                    continue;
                }
                // ((0.3.28) a piece put aboard in its way: it takes what it can (shot away when holed), the shot goes on)
                int pid = hooks?.PieceAt?.Invoke(ci, cj, ck) ?? -1;
                if (pid >= 0)
                {
                    left = HitPiece(layout, hooks, pid, left, res);
                    from = at.AddCopy(u.X * 0.3, u.Y * 0.3, u.Z * 0.3);
                    continue;
                }
                var part = def.PartMap.Classify(ci, cj, ck, axis, u, at);
                if (part.Kind == ShipPartKind.Hull || part.Kind == ShipPartKind.Deck || part.Kind == ShipPartKind.Mast || part.Kind == ShipPartKind.Cell)
                {
                    Blow(layout, st, grid, at, u, left, radius, kinds, res, depth + 1, factors, hooks);
                    return;
                }
                // her woodwork inside (a bulkhead, a beam, a chest): it takes some and the shot goes on
                left -= d.StructureToughness;
                from = at.AddCopy(u.X * 0.3, u.Y * 0.3, u.Z * 0.3);
            }
            if (left < 1) res.Remaining = 0;
        }

        /// <summary>(0.3.29) How far along u (normalised) from p the ray leaves the voxel (i, j, k).</summary>
        static double LeaveCell(VoxelGrid g, Vec3d p, Vec3d u, int i, int j, int k)
        {
            double best = g.V * 2;
            double[] pp = { p.X, p.Y, p.Z }, uu = { u.X, u.Y, u.Z }, lo = { g.OX + i * g.V, g.OY + j * g.V, g.OZ + k * g.V };
            for (int a = 0; a < 3; a++)
            {
                if (Math.Abs(uu[a]) < 1e-12) continue;
                double b = uu[a] > 0 ? lo[a] + g.V : lo[a];
                best = Math.Min(best, Math.Max(0, (b - pp[a]) / uu[a]));
            }
            return best;
        }

        /// <summary>
        /// (0.3.28) A shot of this energy meets a piece put aboard: it takes up to what it has left (its toughness × (its
        /// cap − its hurt)); one that doesn't take shots is her woodwork (StructureToughness). Returns what goes on.
        /// </summary>
        static double HitPiece(DamageLayout layout, ShotHooks hooks, int id, double energy, DamageResult res)
        {
            double tough = hooks.PieceToughness?.Invoke(id) ?? 0;
            if (tough <= 0 || hooks.PieceHurt == null || hooks.SetPieceHurt == null) return energy - layout.D.StructureToughness;
            float hurt = hooks.PieceHurt(id), cap = Math.Clamp(hooks.PieceCap?.Invoke(id) ?? 1, 0, 1);
            double took = Math.Min(energy, Math.Max(0, tough * (cap - hurt)));
            if (took <= 0) return energy;
            float after = Math.Min(cap, hurt + (float)(took / tough));
            hooks.SetPieceHurt(id, after);
            res.PieceHits.Add(new PieceHit { Id = id, Before = hurt, After = after });
            return energy - took;
        }

        /// <summary>Hurt one part directly (0..1 more); returns what it did.</summary>
        public static DamageResult Hurt(DamageLayout layout, DamageState st, int part, float amount)
        {
            var res = new DamageResult();
            if (part < 0 || part >= layout.Count || amount == 0) return res;
            var p = layout.Parts[part];
            var before = layout.StateOf(p.Kind, st.Hurt[part], st.Wear[part]);
            float was = st.Hurt[part];
            st.Hurt[part] = Math.Clamp(st.Hurt[part] + amount, 0, 1);
            if (p.Planking && amount > 0) st.Wear[part] = Math.Min(1, st.Wear[part] + amount * layout.D.SeamShock);
            var after = layout.StateOf(p.Kind, st.Hurt[part], st.Wear[part]);
            res.Hits.Add(new DamageHit { Part = part, Added = st.Hurt[part] - was, Before = before, After = after });
            res.StateChanged = before != after;
            return res;
        }

        /// <summary>
        /// Running aground or into another ship: the energy that goes into her planking where she touches, for a touch at
        /// this speed (blocks/s) with this mass (tonnes: her displacement, or two ships' reduced mass).
        /// </summary>
        public static double ImpactEnergy(DamageDef d, double mass, double speed) => d.GroundShare * 0.5 * mass * speed * speed;
    }

    /// <summary>Where a ship is and how she really lies (the waves' pitch and roll, her heel and list), for what comes in through her side.</summary>
    public struct FloodPose
    {
        /// <summary>Her origin (world): Y is where her design waterline's middle is now (the still-water level − sinkage + heave).</summary>
        public double X, Y, Z;
        public double Yaw, Pitch, Roll;
        /// <summary>The sea's surface (world y) at a world x, z, now (waves and all).</summary>
        public Func<double, double, double> Surface;

        public void ToWorld(double lx, double ly, double lz, out double wx, out double wy, out double wz)
        {
            ShipFrame.ToWorld(Yaw, Pitch, Roll, lx, ly, lz, out wx, out wy, out wz);
            wx += X;
            wy += Y;
            wz += Z;
        }
    }

    /// <summary>
    /// The water in a ship, pure: what leaks in through a hole, what the sea pours in over her side, and what the water
    /// does to her (how deep she floats, her list, her roll, her speed).
    /// </summary>
    public static class ShipFlood
    {
        /// <summary>
        /// What leaks in (cubic blocks a second; negative: out) through her damaged panels: each by the sea's height over it
        /// outside (with the waves there) and the water's over it inside (level in the world). sideFlow: how much of it
        /// comes in to starboard (+) or port (−).
        /// </summary>
        public static double Leaks(DamageLayout l, DamageState st, double water, in FloodPose pose, double factor, out double sideFlow)
        {
            sideFlow = 0;
            double inside = double.NegativeInfinity;
            if (water > 0)
            {
                pose.ToWorld(0, l.FloodLevel(water), 0, out _, out inside, out _);
            }
            double total = 0;
            var hull = l.Def?.Hull;
            foreach (var p in l.Parts)
            {
                if (p.Kind != DamageKind.Hull || p.Inner) continue;      // ((0.3.29) a ceiling panel never leaks)
                double q;
                if (p.Slot >= 0 && hull != null && st.Hurt[p.Index] >= l.D.Holed)
                {
                    // (0.3.26, step D1) a hull panel out: over her main deck it's a downflooding point (Downflooding); else
                    // an orifice once the sea is over its head, a weir over its sill while the sea's between
                    var s = hull.Slots[p.Slot];
                    if (s.Above(l.Def)) continue;
                    q = OpenPanel(l, s, l.OpenArea(p), inside, pose) * factor;
                    // ((0.3.29) its ceiling behind it in (not out): it slows the water)
                    if (p.Partner >= 0 && p.Partner < st.Count && st.Hurt[p.Partner] < l.D.Holed) q *= l.D.LinedLeak;
                }
                else
                {
                    double area = l.LeakArea(p, st.Hurt[p.Index], st.Wear[p.Index]);
                    if (area <= 0) continue;
                    pose.ToWorld(p.Center.X, p.Center.Y, p.Center.Z, out double wx, out double wy, out double wz);
                    q = Flow(l.D.LeakCoefficient, area, pose.Surface(wx, wz) - wy, inside - wy) * factor;
                }
                total += q;
                sideFlow += q * Math.Sign(p.Center.Z);
            }
            return total;
        }

        /// <summary>
        /// (0.3.26, step D1) What comes in (cubic blocks a second; negative: out) through a hull panel that's out, an opening of
        /// this effective area: the sea over its head, through it as through a hole at its middle; the sea between its sill and
        /// its head, pouring over its sill (a weir as long as the panel, as deep as the sea is over the sill); none under it.
        /// inside: the water's level in her (world y; −∞ dry).
        /// </summary>
        public static double OpenPanel(DamageLayout l, HullSlotDef s, double area, double inside, in FloodPose pose)
        {
            var c = s.Centre;
            pose.ToWorld(c.X, s.Sill, s.OuterZ(s.Sill), out double sx, out double sy, out double sz);
            pose.ToWorld(c.X, s.Head, s.OuterZ(s.Head), out double hx, out double hy, out double hz);
            pose.ToWorld(c.X, c.Y, c.Z, out double cx, out double cy, out double cz);
            double sea = pose.Surface(cx, cz);
            if (sea >= hy) return Flow(l.D.LeakCoefficient, area, sea - cy, inside - cy);
            double over = sea - Math.Max(sy, inside);
            return over > 0 ? Pour(l.D, s.Length, 1, over) : 0;
        }

        /// <summary>
        /// What the sea pours into her hold over her side (cubic blocks a second): every place it can come aboard that's
        /// under the surface (hatches and gates only while open). What lands on the open deck mostly runs off again
        /// (<see cref="DamageDef.DeckShare"/> of it gets below, more with hatches open); over an open hatch it all goes down.
        /// </summary>
        public static double Downflooding(DamageLayout l, in FloodPose pose, Func<string, bool> isOpen)
        {
            var d = l.D;
            double openHatches = 0;
            // ((0.3.20) an open deck cell counts as a sixth of a hatch: carrack's hatches were about six cells)
            foreach (var f in l.Downflood)
                if (f.Hatch && isOpen?.Invoke(f.OpenFixture) == true) openHatches += f.Cell ? 1.0 / 6 : 1;
            double deckShare = Math.Min(1, d.DeckShare + d.OpenHatchShare * openHatches);
            double total = 0;
            foreach (var f in l.Downflood)
            {
                if (f.OpenFixture != null && isOpen?.Invoke(f.OpenFixture) != true) continue;
                pose.ToWorld(f.X, f.Y, f.Z, out double wx, out double wy, out double wz);
                total += Pour(d, f.Length, f.Weight, pose.Surface(wx, wz) - wy) * (f.Hatch ? 1 : deckShare);
            }
            return total;
        }

        /// <summary>
        /// Through a hole of this area (square blocks) with the sea this far over it outside and the water inside this far
        /// over it (0 or less: none over it): coefficient × area × √(head difference), in (positive) or out.
        /// </summary>
        public static double Flow(double coefficient, double area, double outside, double inside)
        {
            if (area <= 0) return 0;
            double h = Math.Max(0, outside) - Math.Max(0, inside);
            return h >= 0 ? coefficient * area * Math.Sqrt(h) : -coefficient * area * Math.Sqrt(-h);
        }

        /// <summary>The sea pouring in over an edge of this length, this deep under the water (a weir).</summary>
        public static double Pour(DamageDef d, double length, double weight, double depth) =>
            depth <= 0 ? 0 : d.DownfloodCoefficient * length * weight * Math.Pow(depth, 1.5);

        /// <summary>A pump's work a second with this many at its brake.</summary>
        public static double PumpRate(DamageDef d, int pumpers, double factor = 1) => Math.Max(0, pumpers) * d.PumpRate / Math.Max(0.1, d.PumpStroke) * Math.Max(0, factor);

        /// <summary>How much slower and less handy the water makes her (a factor on her speed and turning).</summary>
        public static double Sluggish(DamageLayout l, double water) => 1 / (1 + l.D.SlowPerWater * Math.Max(0, water) / Math.Max(1, l.Displacement));

        /// <summary>
        /// Her metacentric height with the water's free surface in her (the water running to the low side takes it away):
        /// GM − i/∇, with i the free surface's moment across her and ∇ her displacement with the water.
        /// </summary>
        public static double EffectiveGM(DamageLayout l, double water)
        {
            if (water <= 0) return l.GM;
            return l.GM - l.FreeSurfaceMoment(l.FloodLevel(water)) / (l.Displacement + water);
        }

        /// <summary>
        /// Her list from the water in her (radians, + starboard down): toward side (+1 starboard; the way she heels, or the
        /// side it came in), growing with the water, amplified as the free surface eats her stability (GM / GM effective),
        /// and a lolling angle once it's all gone; the heel she has (from the wind) is amplified the same way.
        /// </summary>
        public static double List(DamageLayout l, double water, double heel, int side)
        {
            if (water <= 0) return 0;
            var d = l.D;
            double gm = l.GM, gme = EffectiveGM(l, water);
            double amp = gm / Math.Max(gme, 0.15 * gm);
            double deg = d.ListPerWater * water / Math.Max(1, l.Displacement) * amp;
            if (gme < 0 && l.BM > 0) deg = Math.Max(deg, Math.Atan(Math.Sqrt(-2 * gme / l.BM)) * 180 / Math.PI);
            double list = (side < 0 ? -deg : deg) * Math.PI / 180 + heel * (amp - 1);
            return ShipMotion.SoftCap(list, d.MaxList * Math.PI / 180);
        }

        /// <summary>
        /// Her roll with the water in her: slower (the period grows as √(GM / GM effective)) and more damped (it sloshes),
        /// as a copy of her motion settings. The heave and pitch are left as they are (the server's heave is real and
        /// every client works it out alike from what's shared).
        /// </summary>
        public static MotionDef Motion(DamageLayout l, MotionDef m, double water)
        {
            if (water <= 0) return m;
            double gm = l.GM, gme = EffectiveGM(l, water);
            var c = m.Clone();
            c.RollPeriod = (float)(m.RollPeriod * Math.Sqrt(gm / Math.Max(gme, 0.2 * gm)));
            c.RollDamping = (float)(m.RollDamping + l.D.FreeSurfaceDamping * Math.Clamp((gm - gme) / gm, 0, 1));
            return c;
        }
    }

    /// <summary>What a repair takes: one of these sets of items (item code patterns, "*" at the end for any variant, and counts).</summary>
    public class RepairKit
    {
        public (string Code, int Count)[] Items;

        public RepairKit(params (string, int)[] items) { Items = items; }

        /// <summary>Does the item code fit the pattern ("*" at the end for any variant; "a|b": either)?</summary>
        public static bool Matches(string pattern, string code)
        {
            if (code == null || pattern == null) return false;
            if (pattern.IndexOf('|') >= 0)
            {
                foreach (var alt in pattern.Split('|')) if (Matches(alt, code)) return true;
                return false;
            }
            return pattern.EndsWith("*") ? code.StartsWith(pattern.Substring(0, pattern.Length - 1)) : code == pattern;
        }
    }

    /// <summary>A repair a part needs: what it mends (the planking, the seams, a sail...), the kits it can be done with, and what it leaves.</summary>
    public class RepairJob
    {
        public int Part;
        /// <summary>planks, caulk, sail, rigging, fish, rudder; "port" when it can't be done at sea.</summary>
        public string What;
        public RepairKit[] Kits = Array.Empty<RepairKit>();
        /// <summary>The part's hurt or wear after it (a fished mast isn't as good as new).</summary>
        public float HurtAfter = -1, WearAfter = -1;
        public bool AtSea => What != "port";
    }

    /// <summary>
    /// Repairs with vanilla items at the part you look at (held right-click for a few seconds): planks and nails and strips
    /// for the planking, flax fibres and resin to caulk the seams, a sail or cloth for a sail, rope for the rigging, planks
    /// and rope to fish a sprung mast (a broken one needs a port), planks and nails for the rudder. What's held picks the
    /// repair when a part needs more than one.
    /// </summary>
    public static class ShipRepairs
    {
        /// <summary>(0.3.27) Boards to a planks block (vanilla's grid recipe: 2 × 2).</summary>
        public const int BoardsPerPlanks = 4;
        public const string Plank = "game:plank-*", PlanksBlock = "game:planks-*", Nails = "game:metalnailsandstrips-*", Flax = "game:flaxfibers", Resin = "game:resin",
            Rope = "game:rope",
            // a sail: vanilla's (the windmill's, game:sail) or a boat's large sail; cloth: any colour of cloth, or linen (the
            // decor block linen-<pattern>-<side>, what a sail is made of)
            Sail = "game:sail|game:sail-large-*", Cloth = "game:cloth-*|game:linen-*";

        /// <summary>The lang key of an item pattern (hansa:item-&lt;key&gt;).</summary>
        public static string ItemKey(string pattern) => pattern switch
        {
            Plank => "plank",
            PlanksBlock => "planksblock",
            Nails => "nails",
            Flax => "flax",
            Resin => "resin",
            Sail => "sail",
            Cloth => "cloth",
            _ => "rope"
        };

        /// <summary>
        /// The repairs the part needs, the one the held item is for first (null: whichever is worst first). Empty if it's
        /// sound. For a mast: its own fish, its rigging and its sails (their parts, not the mast's).
        /// </summary>
        public static List<RepairJob> Needs(DamageLayout l, DamageState st, int part, string held = null)
        {
            var jobs = new List<RepairJob>();
            if (part < 0 || part >= l.Count) return jobs;
            var d = l.D;
            var p = l.Parts[part];
            float h = st.Hurt[part], w = st.Wear[part];
            switch (p.Kind)
            {
                case DamageKind.Hull:
                case DamageKind.Deck:
                    // ((0.3.26, step D1) a hull panel out is laid again in building mode, with planks: not a repair kit's)
                    // ((0.3.27, "made holes in ship but cannot repair") the repair tool lays it too: with planks blocks or boards
                    // (4 a block), held right-click on the opening as any mending, building mode or not)
                    if (h >= d.Holed && (p.Slot >= 0 || p.Cell >= 0)) jobs.Add(new RepairJob { Part = part, What = "lay", Kits = LayKits(p.LayBlocks), HurtAfter = 0, WearAfter = 0 });
                    else if (h >= d.Holed) jobs.Add(new RepairJob { Part = part, What = "planks", Kits = new[] { new RepairKit((Plank, 4), (Nails, 2)) }, HurtAfter = 0 });
                    else if (h >= d.Cracked) jobs.Add(new RepairJob { Part = part, What = "planks", Kits = new[] { new RepairKit((Plank, 2), (Nails, 1)) }, HurtAfter = 0 });
                    else if (h >= d.Scraped) jobs.Add(new RepairJob { Part = part, What = "planks", Kits = new[] { new RepairKit((Plank, 1)) }, HurtAfter = 0 });
                    if (w >= d.Leaking) jobs.Add(new RepairJob { Part = part, What = "caulk", Kits = new[] { new RepairKit((Flax, 2), (Resin, 1)) }, WearAfter = 0 });
                    else if (w >= d.Worn) jobs.Add(new RepairJob { Part = part, What = "caulk", Kits = new[] { new RepairKit((Flax, 1), (Resin, 1)) }, WearAfter = 0 });
                    break;
                case DamageKind.Rudder:
                    if (h >= d.RudderWrecked) jobs.Add(new RepairJob { Part = part, What = "rudder", Kits = new[] { new RepairKit((Plank, 4), (Nails, 2)) }, HurtAfter = 0 });
                    else if (h >= d.RudderDamaged) jobs.Add(new RepairJob { Part = part, What = "rudder", Kits = new[] { new RepairKit((Plank, 2), (Nails, 1)) }, HurtAfter = 0 });
                    break;
                case DamageKind.Sail:
                    // torn: a new sail bent on, or patched with 4 cloth; shredded: 2 sails, a sail and 4 cloth, or (0.3.10: a
                    // crew with cloth aboard isn't stranded) 8 cloth
                    if (h >= d.Shredded) jobs.Add(new RepairJob { Part = part, What = "sail", Kits = new[] { new RepairKit((Sail, 2)), new RepairKit((Sail, 1), (Cloth, 4)), new RepairKit((Cloth, 8)) }, HurtAfter = 0 });
                    else if (h >= d.Torn) jobs.Add(new RepairJob { Part = part, What = "sail", Kits = new[] { new RepairKit((Sail, 1)), new RepairKit((Cloth, 4)) }, HurtAfter = 0 });
                    break;
                case DamageKind.Rigging:
                    if (h >= d.Parted) jobs.Add(new RepairJob { Part = part, What = "rigging", Kits = new[] { new RepairKit((Rope, 4)) }, HurtAfter = 0 });
                    else if (h >= d.Frayed) jobs.Add(new RepairJob { Part = part, What = "rigging", Kits = new[] { new RepairKit((Rope, 2)) }, HurtAfter = 0 });
                    break;
                case DamageKind.Mast:
                    if (h >= d.Broken) jobs.Add(new RepairJob { Part = part, What = "port" });
                    else if (h >= d.Sprung) jobs.Add(new RepairJob { Part = part, What = "fish", Kits = new[] { new RepairKit((Plank, 4), (Rope, 2)) }, HurtAfter = Math.Max(0, d.Sprung - 0.1f) });
                    // its rigging, then its sails (worst first)
                    foreach (var q in l.Parts)
                        if (q.Mast == p.Mast && q.Kind == DamageKind.Rigging) jobs.AddRange(Needs(l, st, q.Index));
                    var sails = new List<RepairJob>();
                    foreach (var q in l.Parts)
                        if (q.Mast == p.Mast && q.Kind == DamageKind.Sail) sails.AddRange(Needs(l, st, q.Index));
                    sails.Sort((a, b) => st.Hurt[b.Part].CompareTo(st.Hurt[a.Part]));
                    jobs.AddRange(sails);
                    break;
            }
            if (held != null)
            {
                // the repair the held item is for comes first (the one it's the first item of, else any it's in: rope
                // splices the rigging before it fishes the mast)
                int i = jobs.FindIndex(j => Leads(j, held));
                if (i < 0) i = jobs.FindIndex(j => Uses(j, held));
                if (i > 0)
                {
                    var j = jobs[i];
                    jobs.RemoveAt(i);
                    jobs.Insert(0, j);
                }
            }
            return jobs;
        }

        static bool Leads(RepairJob j, string held) => Array.Exists(j.Kits, k => k.Items.Length > 0 && RepairKit.Matches(k.Items[0].Code, held));
        static bool Uses(RepairJob j, string held) => Array.Exists(j.Kits, k => Array.Exists(k.Items, it => RepairKit.Matches(it.Code, held)));

        /// <summary>From outside her the repair crosshair looks no further than her planking's thickness past her outside.</summary>
        public const double OutsideLookBehind = 0.6;

        /// <summary>
        /// The panel (hull or deck) the repair crosshair picks along a ray (ship-local o, d normalised): through what's put
        /// aboard (design: her design's collision, without pieces, partitions, panels, shut doors) and up to lookBehind past
        /// the first of her surfaces it meets, the nearest panel whose repair the held item is for. The first surface must
        /// be within range. Returns the part and the crosshair's distance (the nearer of the surface and the panel), or -1.
        /// </summary>
        public static int Pick(DamageLayout l, DamageState st, VoxelGrid design, Vec3d o, Vec3d d, double range, string held, double lookBehind, out double t)
        {
            t = double.MaxValue;
            if (held == null || l == null || st == null || design == null || !IsRepairItem(held)) return -1;
            double tFirst = design.RaycastCell(o, d, range + lookBehind, out _, out _, out _, out _, skipStart: true);
            int best = -1;
            double bt = Math.Min(tFirst, range) + lookBehind;
            for (int i = 0; i < l.Count; i++)
            {
                var p = l.Parts[i];
                if (!p.Planking || st.Hurt[i] < 1e-3f && st.Wear[i] < 1e-3f) continue;
                double te = ShipFittings.RayBox(o, d, new Vintagestory.API.MathTools.Cuboidd(p.Min[0] - 0.05, p.Min[1] - 0.05, p.Min[2] - 0.05, p.Max[0] + 0.05, p.Max[1] + 0.05, p.Max[2] + 0.05));
                if (te < 0 || te >= bt) continue;
                if (For(l, st, i, held) == null) continue;
                bt = te;
                best = i;
            }
            if (best < 0) return -1;
            t = Math.Min(bt, tFirst);
            return t > range ? -1 : best;
        }

        /// <summary>Is this item one that mends something of hers (planks, nails, flax, resin, rope, a sail, cloth)?</summary>
        public static bool IsRepairItem(string code) =>
            code != null && new[] { Plank, PlanksBlock, Nails, Flax, Resin, Rope, Sail, Cloth }.Any(pt => RepairKit.Matches(pt, code));

        /// <summary>(0.3.27) Laying a hull panel that's out: its planks blocks, or boards (<see cref="BoardsPerPlanks"/> a block).</summary>
        public static RepairKit[] LayKits(int blocks)
        {
            blocks = Math.Max(1, blocks);
            return new[] { new RepairKit((PlanksBlock, blocks)), new RepairKit((Plank, blocks * BoardsPerPlanks)) };
        }

        /// <summary>The repair the held item is for (null if it's for none of them).</summary>
        public static RepairJob For(DamageLayout l, DamageState st, int part, string held)
        {
            if (held == null) return null;
            var jobs = Needs(l, st, part, held);
            return jobs.Count > 0 && Uses(jobs[0], held) ? jobs[0] : null;
        }

        /// <summary>
        /// Which items to take for a job from what someone has (item code and count per slot, the held one first): the
        /// first kit they have everything for; take[i] is what to take from slot i. False if they don't have enough.
        /// </summary>
        public static bool Plan(RepairJob job, IList<(string Code, int Count)> have, out int[] take, out RepairKit kit)
        {
            take = new int[have.Count];
            kit = null;
            // the kits with what's in hand (the first slot) first: cloth in hand patches a sail even with a spare sail aboard
            string inHand = have.Count > 0 && have[0].Count > 0 ? have[0].Code : null;
            var kits = job.Kits.OrderBy(k => Array.Exists(k.Items, it => RepairKit.Matches(it.Code, inHand)) ? 0 : 1).ToArray();
            foreach (var k in kits)
            {
                Array.Clear(take);
                bool ok = true;
                foreach (var (code, count) in k.Items)
                {
                    int need = count;
                    for (int s = 0; s < have.Count && need > 0; s++)
                    {
                        if (!RepairKit.Matches(code, have[s].Code)) continue;
                        int n = Math.Min(need, have[s].Count - take[s]);
                        if (n <= 0) continue;
                        take[s] += n;
                        need -= n;
                    }
                    if (need > 0) { ok = false; break; }
                }
                if (ok) { kit = k; return true; }
            }
            Array.Clear(take);
            return false;
        }

        /// <summary>Do it: the part's hurt or wear as the job leaves it.</summary>
        public static void Apply(DamageState st, RepairJob job)
        {
            if (job.HurtAfter >= 0) st.Hurt[job.Part] = Math.Min(st.Hurt[job.Part], job.HurtAfter);
            if (job.WearAfter >= 0) st.Wear[job.Part] = Math.Min(st.Wear[job.Part], job.WearAfter);
        }
    }

    /// <summary>What of her sails still draws: each sail's whole cloth by its state, times what its mast and rigging leave (EntityShip.SailDrive).</summary>
    public static class SailArea
    {
        /// <summary>A sail's cloth that still draws at this hurt (<see cref="DamageDef.TornArea"/>, <see cref="DamageDef.ShreddedArea"/>).</summary>
        public static double Cloth(DamageDef d, float hurt) =>
            hurt >= d.Shredded ? d.ShreddedArea : hurt >= d.Torn ? d.TornArea : 1 - 0.1 * Math.Clamp(hurt / Math.Max(1e-3, d.Torn), 0, 1);

        public static double Drive(DamageLayout l, DamageState st)
        {
            {
                var d = l.D;
                double all = 0, got = 0;
                foreach (var p in l.Parts)
                {
                    if (p.Kind != DamageKind.Sail) continue;
                    double area = Math.Max(1, p.Area), f = Cloth(d, st.Hurt[p.Index]);
                    foreach (var q in l.Parts)
                    {
                        if (q.Mast != p.Mast || p.Mast < 0) continue;
                        float h = st.Hurt[q.Index];
                        if (q.Kind == DamageKind.Mast) f *= h >= d.Broken ? 0 : h >= d.Sprung ? 0.5 : 1 - 0.3 * h;
                        if (q.Kind == DamageKind.Rigging) f *= h >= d.Parted ? 0.2 : h >= d.Frayed ? 0.7 : 1;
                    }
                    all += area;
                    got += area * Math.Max(0, f);
                }
                return all > 0 ? got / all : 1;
            }
        }
    }
}
