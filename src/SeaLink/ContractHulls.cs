using System;
using System.Collections.Generic;

namespace Hansa
{
    /// <summary>
    /// Without Sea: the open sea contracts' <c>seacontracts:hulls</c> (Sea's docs/open-sea-contracts.md, section 8). When
    /// some mod publishes it (the mod whose shader draws the water), Ships registers its hulls there as records (client:
    /// <c>register</c>, the water cut out of each hull) and its dry places (both sides: <c>registerDry</c>: no underwater fog,
    /// tint or drowning in her hold). Only .NET base types cross; the entry is looked up again at every check (every couple
    /// of seconds), and if its owner changes or goes away Ships registers with the new one or falls back to its own
    /// (<see cref="OwnDryPlaces"/>, <see cref="ShipWaterLids"/>). Never used while Sea itself is bound: then Sea's typed API
    /// holds the hulls. An owner that publishes <c>recordLength</c> (Sea since 7 November) takes the detailed record: every
    /// field of the clip, so it cuts as finely as Sea's typed API; any other gets the plain record.
    /// </summary>
    public sealed class ContractHulls
    {
        public const string HullsKey = "seacontracts:hulls";
        /// <summary>The id Ships registers under (one per side).</summary>
        public const string Id = "ships";

        // record indices (section 8)
        public const int HX = 0, HZ = 1, HWaterY = 2, HYaw = 3, HHalfLength = 4, HHalfBeam = 5, HCentreOffset = 6,
            HSlopeAlong = 7, HSlopeAcross = 8, HDry = 9, HDryDepth = 10, HDryHeight = 11, HFloodY = 12, HProfile = 13,
            RecordLength = HProfile + WaterClipDef.Stations,
            // the detailed record (contract section 8, 7 November): calm level and ring, inset, rake, dry outline by levels
            HCalmSlopeAcross = RecordLength, HCalmZone = HCalmSlopeAcross + 1, HDryInset = HCalmZone + 1, HDryRake = HDryInset + 1,
            HDryLevel0 = HDryRake + 1, HDryLevelStep = HDryLevel0 + 1, HDryProfile = HDryLevelStep + 1,
            DetailedLength = HDryProfile, DetailedWithDryProfile = HDryProfile + WaterClipDef.DryStations * WaterClipDef.DryLevels;

        readonly IDictionary<string, object> cache;
        readonly Action<string> log;
        IDictionary<string, object> dryWith, hullsWith;
        Func<double, double, double, bool> dryFunc;
        Func<double[][]> hullsFunc;
        bool warned;

        /// <param name="cache">the side's <c>api.ObjectCache</c></param>
        public ContractHulls(IDictionary<string, object> cache, Action<string> log)
        {
            this.cache = cache;
            this.log = log;
        }

        IDictionary<string, object> Entry()
        {
            if (cache == null) return null;
            lock (cache) return cache.TryGetValue(HullsKey, out var o) ? o as IDictionary<string, object> : null;
        }

        static T Member<T>(IDictionary<string, object> e, string name) where T : class =>
            e != null && e.TryGetValue(name, out var o) ? o as T : null;

        /// <summary>The owner's mod id, or null when nobody publishes the hull cut on this side.</summary>
        public string Owner => Member<string>(Entry(), "owner");

        /// <summary>Does the owner Ships registered its hulls with read the detailed record (its <c>recordLength</c>)?</summary>
        public bool Detailed { get; private set; }

        static bool ReadsDetail(IDictionary<string, object> e) =>
            e != null && e.TryGetValue("recordLength", out var o) && o is int n && n >= DetailedWithDryProfile;

        /// <summary>Is a hull cut published on this side now (with the members Ships registers through)?</summary>
        public bool Available
        {
            get
            {
                var e = Entry();
                return Member<Action<string, Func<double[][]>>>(e, "register") != null || Member<Action<string, Func<double, double, double, bool>>>(e, "registerDry") != null;
            }
        }

        /// <summary>
        /// Keep these dry places registered with the hull cut's owner (both sides). True while an owner holds them; false:
        /// nobody publishes the cut now (Ships keeps its own dry places).
        /// </summary>
        public bool KeepDry(Func<double, double, double, bool> isDry)
        {
            var e = Entry();
            var register = Member<Action<string, Func<double, double, double, bool>>>(e, "registerDry");
            if (register == null) { dryWith = null; return false; }
            if (ReferenceEquals(e, dryWith) && ReferenceEquals(isDry, dryFunc)) return true;
            try
            {
                register(Id, isDry);
                dryWith = e;
                dryFunc = isDry;
                return true;
            }
            catch (Exception ex) { Fail("hulls.registerDry failed: " + ex.Message); dryWith = null; return false; }
        }

        /// <summary>
        /// Keep this client's hulls registered with the hull cut's owner: it calls <paramref name="records"/> every frame
        /// before it draws water. True while an owner holds them.
        /// </summary>
        public bool KeepHulls(Func<double[][]> records)
        {
            var e = Entry();
            var register = Member<Action<string, Func<double[][]>>>(e, "register");
            if (register == null) { hullsWith = null; return false; }
            if (ReferenceEquals(e, hullsWith) && ReferenceEquals(records, hullsFunc)) return true;
            try
            {
                Detailed = ReadsDetail(e);
                register(Id, records);
                hullsWith = e;
                hullsFunc = records;
                return true;
            }
            catch (Exception ex) { Fail("hulls.register failed: " + ex.Message); hullsWith = null; return false; }
        }

        /// <summary>Take Ships' hulls and dry places away from whoever holds them (leaving the world).</summary>
        public void Release()
        {
            try { Member<Action<string>>(hullsWith, "unregister")?.Invoke(Id); } catch (Exception) { }
            try { Member<Action<string>>(dryWith, "unregisterDry")?.Invoke(Id); } catch (Exception) { }
            hullsWith = dryWith = null;
            hullsFunc = null;
            dryFunc = null;
        }

        void Fail(string what)
        {
            if (warned) return;
            warned = true;
            log?.Invoke("sea contracts: " + what + "; Ships keeps its own dry places (logged once)");
        }

        /// <summary>
        /// A hull's clip as the contract's record. Detailed (for an owner that reads it): every field, so the owner cuts what
        /// Sea's typed API cuts. Plain: her footprint, her tilted waterline, dry inside, the water in her; the plain record has
        /// no inset (nor the detailed outline, rake or calm ring), so a dry hull's record is its outline: the footprint less
        /// the inset, its 16 half-widths resampled over the shorter length, the outline Sea's own shader cuts for a dry hull
        /// it doesn't give the detailed outline to.
        /// </summary>
        public static double[] ToRecord(in HullWaterClip c, bool detailed)
        {
            if (!detailed) return ToRecord(c);
            bool dryProfile = c.DryProfile != null && c.DryProfile.Length == WaterClipDef.DryStations * WaterClipDef.DryLevels && c.DryLevelStep > 0;
            var r = new double[dryProfile ? DetailedWithDryProfile : DetailedLength];
            r[HX] = c.X; r[HZ] = c.Z; r[HWaterY] = c.WaterY; r[HYaw] = c.Yaw;
            r[HHalfLength] = c.HalfLength; r[HHalfBeam] = c.HalfWidth; r[HCentreOffset] = c.CentreOffset;
            r[HSlopeAlong] = c.SlopeAlong; r[HSlopeAcross] = c.SlopeAcross; r[HDry] = c.Dry ? 1 : 0;
            r[HDryDepth] = c.DryDepth; r[HDryHeight] = c.DryHeight; r[HFloodY] = c.Flooded ? c.FloodY : double.NaN;
            if (c.Profile != null && c.Profile.Length == WaterClipDef.Stations)
                for (int i = 0; i < c.Profile.Length; i++) r[HProfile + i] = c.Profile[i];
            else r[HProfile] = double.NaN;
            r[HCalmSlopeAcross] = c.CalmSlopeAcross; r[HCalmZone] = c.CalmZone; r[HDryInset] = c.DryInset;
            r[HDryRake] = c.DryRake; r[HDryLevel0] = c.DryLevel0; r[HDryLevelStep] = c.DryLevelStep;
            if (dryProfile) for (int i = 0; i < c.DryProfile.Length; i++) r[HDryProfile + i] = c.DryProfile[i];
            return r;
        }

        /// <summary>The plain record (<see cref="ToRecord(in HullWaterClip, bool)"/>).</summary>
        public static double[] ToRecord(in HullWaterClip c)
        {
            var r = new double[RecordLength];
            double inset = c.Dry ? c.DryInset : 0;
            double hl = Math.Max(0, c.HalfLength - inset);
            r[HX] = c.X;
            r[HZ] = c.Z;
            r[HWaterY] = c.WaterY;
            r[HYaw] = c.Yaw;
            r[HHalfLength] = hl;
            r[HHalfBeam] = Math.Max(0, c.HalfWidth - inset);
            r[HCentreOffset] = c.CentreOffset;
            r[HSlopeAlong] = c.SlopeAlong;
            r[HSlopeAcross] = c.SlopeAcross;
            r[HDry] = c.Dry ? 1 : 0;
            r[HDryDepth] = c.DryDepth;
            r[HDryHeight] = c.DryHeight;
            r[HFloodY] = c.Flooded ? c.FloodY : double.NaN;
            for (int i = 0; i < WaterClipDef.Stations; i++)
            {
                double u = -1 + 2.0 * i / (WaterClipDef.Stations - 1);
                double along = c.HalfLength > 0 ? u * hl / c.HalfLength : 0;
                r[HProfile + i] = Math.Max(0, c.HalfWidthAt(along) - inset);
            }
            return r;
        }
    }
}
