using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// The watch (0.3.11, config ShipWatch): while anyone sleeps in a bunk aboard, she holds her course (nobody at the
    /// helm: the rudder comes amidships and she keeps her heading; nothing changes it) and they're turned out, all hands
    /// told why, when something needs them:
    /// - land or a shoal ahead within her next WatchAheadSeconds of way (her hull's own samples, at the waves' troughs);
    /// - water rising in her (foundering, or a leak gaining with a tenth of her reserve already in her);
    /// - aground;
    /// - taken aback or in irons with sail set (the wind shifted);
    /// - the sails straining (0.3.12: over-pressed in a gale, or aback hard, <see cref="SailStrain"/> building: some
    ///   minutes before they'd tear);
    /// - the sea getting up (her waves' height a block, and half again, over what it was when the watch began);
    /// - hit (a gun, a ram, grounding: any damage), or another mod's alarm (<see cref="RaiseAlarm"/>: the Kraken).
    /// A condition wakes them when it begins, not again while it lasts (a ship left aground on purpose lets them sleep);
    /// what's already so when the first of them lies down doesn't count.
    /// </summary>
    public partial class EntityShip
    {
        float watchAccum;
        double watchHs = double.NaN;
        readonly HashSet<string> watchActive = new();
        string pendingAlarm;
        long lastEventAlarmMs = -100000;

        /// <summary>
        /// Something outside her needs all hands (server): wakes anyone asleep aboard, telling them "ships:watch-" +
        /// reason if there's such a text (else the reason as it is). The Sea Monsters mod calls it with "kraken" when one
        /// starts circling her.
        /// </summary>
        public void RaiseAlarm(string reason)
        {
            if (Api?.Side != EnumAppSide.Server || string.IsNullOrEmpty(reason)) return;
            pendingAlarm ??= reason;
        }

        IEnumerable<BunkSeat> Sleepers() => seatArray.OfType<BunkSeat>().Where(b => b.Passenger is EntityPlayer);

        readonly EpisodeNotice watchSailsNotice = new();

        void WatchTick(float dt)
        {
            watchAccum += dt;
            if (watchAccum < 1) return;
            watchAccum = 0;
            var sleepers = Sleepers().ToList();
            if (!Mod.Config.ShipWatch || sleepers.Count == 0)
            {
                watchHs = double.NaN;
                watchActive.Clear();
                pendingAlarm = null;
                return;
            }
            double hs = Waves.SignificantHeight * (SeaSys?.WaveFactorAt(Pos.X, Pos.Z) ?? 1);
            bool starting = double.IsNaN(watchHs);
            if (starting) watchHs = hs;

            var now = new HashSet<string>();
            if (GoneDown) now.Add("sinking");
            if (DamageOn && DamageParts != null && (Stage == SinkStage.Foundering || WaterRate > 0.02f && Water > 0.1 * DamageParts.Reserve)) now.Add("water");
            if (Aground || OnBottom) now.Add("aground");
            if (SailSet > 0 && (WhySlow == "ships:why-aback" || WhySlow == "ships:why-irons")) now.Add("aback");
            if (DamageOn && SailStrain > 0) now.Add("sails");
            // (0.3.13: the strain comes and goes with the gusts: turned out for it once an episode, EpisodeNotice)
            bool sailsDue = watchSailsNotice.Due(DamageOn && SailStrain > 0, World.ElapsedMilliseconds / 1000.0);
            if (hs > watchHs + Math.Max(1.0, 0.5 * watchHs)) now.Add("weather");
            if (Speed > 0.2f && !Anchored && LandAhead()) now.Add("land");

            string why = null;
            if (pendingAlarm != null && World.ElapsedMilliseconds - lastEventAlarmMs > 20000)
            {
                why = pendingAlarm;
                lastEventAlarmMs = World.ElapsedMilliseconds;
            }
            pendingAlarm = null;
            if (!starting && why == null)
            {
                // (the worst first)
                foreach (string c in new[] { "sinking", "land", "aground", "water", "sails", "aback", "weather" })
                    if (c == "sails" ? sailsDue : now.Contains(c) && !watchActive.Contains(c)) { why = c; break; }
            }
            watchActive.Clear();
            watchActive.UnionWith(now);
            if (why == null) return;

            string key = Lang.HasTranslation("ships:watch-" + why, false, false) ? "ships:watch-" + why : null;
            NotifyAboard("ships:watch-allhands", key ?? why);
            foreach (var b in sleepers)
                if (b.Passenger is EntityPlayer ep) StandUp(ep, b.Fixture);
            LocalToWorld(0, Def.DeckY + 1, 0, out double x, out double y, out double z);
            World.PlaySoundAt(new AssetLocation("sounds/effect/receptionbell"), x, y, z, null, false, 32);
            World.Logger.Notification("[ships] {0} #{1}: the watch turns out all hands ({2})", GetName(), EntityId, why);
        }

        /// <summary>
        /// Land or a shoal ahead: her hull, at the waves' troughs, would touch solid ground somewhere along her heading
        /// within her next WatchAheadSeconds of way (at least 16 blocks, at most 96), checked every 4 blocks.
        /// </summary>
        bool LandAhead()
        {
            double reach = Math.Clamp(Speed * Mod.Config.WatchAheadSeconds, 16, 96);
            double hs = Waves.SignificantHeight * (SeaSys?.WaveFactorAt(Pos.X, Pos.Z) ?? 1);
            double fx = Math.Sin(Pos.Yaw), fz = Math.Cos(Pos.Yaw);
            for (double d = 4; d <= reach + 0.01; d += 4)
            {
                if (CheckPose(World, Def, Pos.X + fx * d, FloatY - hs / 2, Pos.Z + fz * d, Pos.Yaw, Pos.Dimension) == PoseState.Blocked)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Leaving the helm under sail: it's lashed, and she holds her heading (the rudder comes amidships; she sails on
        /// as she's going, the wind and the sail she has set deciding her way).
        /// </summary>
        void HelmLeft(EntityAgent agent)
        {
            if (Def?.PickUpItem != null || Anchored || SailSet <= 0 || agent is not EntityPlayer ep || ep.Player is not IServerPlayer sp) return;
            Notify(sp, "ships:helm-lashed");
        }
    }
}
