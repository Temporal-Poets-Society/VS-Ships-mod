using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public partial class SelfTest
    {
        // Only=flooding, Ship=caravel. Uses the installed Sea spectrum and its wind override; no game client.
        void StormFlooding(HansaMod mod, ShipDefinition def, BlockPos spawn, int y0)
        {
            var sea = sapi.ModLoader.GetModSystem<Sea.SeaSystem>();
            if (sea?.Spectrum == null) { Check(false, "storm flooding: Sea is ready"); Finish(); return; }
            sea.SetOverride(new Sea.WindOverride { FromDegrees = 90, Strength = 1.5, StartHours = sea.Hours - 24 });
            double wy = EntityShip.FindWaterSurface(sapi.World, new BlockPos(spawn.X, y0 + 5, spawn.Z, 0), 2, 6);
            var type = sapi.World.GetEntityType(new AssetLocation("ships", def.Code));
            var ship = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
            ship.Pos.SetPos(spawn.X + .5, wy, spawn.Z + .5);
            ship.Pos.Yaw = 0;
            sapi.World.SpawnEntity(ship);
            ship.Anchored = true;
            ship.SailFraction = 0;
            sapi.Event.RegisterCallback(_ =>
            {
                try
                {
                    var l = def.DamageParts;
                    var hatch = ship.Pieces.All.SingleOrDefault(p => p.Role == PieceRole.Hatch);
                    Check(hatch is { Open: false }, "force-12: a new caravel has her main hatch installed and shut");
                    Check(!l.Downflood.Any(p => p.OpenFixture != null && ship.DownfloodOpen(p.OpenFixture)), "force-12: all default flood openings are closed");
                    var state = sea.SeaAt(ship.Pos.X, ship.Pos.Z);
                    var wind = sea.WindAt(ship.Pos.X, ship.Pos.Z);
                    Log($"force-12 override: wind {wind.Strength:F2}, Hs {state.Height:F3}, Lp {state.Wavelength:F2}; full open-sea factor 1 for the accelerated scenarios");
                    Check(state.Height >= 5.3 && wind.Strength >= 1.45, "force-12 sea override is fully developed");
                    def.Waterplane(out double hl, out double hb, out double cx);
                    (double hold, double pond, double maxHold) Run(double yaw, int people, double seconds)
                    {
                        var waves = new Sea.WavePlace();
                        var trains = new WaveTrains();
                        double hold = 0, pond = 0, sink = 0, list = 0, maxHold = 0;
                        bool dry = true;
                        for (double t = 600; t < 600 + seconds; t += .25)
                        {
                            waves.Update(sea.Spectrum, t, state, wind);
                            var a = ShipMotion.Waves(trains.CopyFrom(waves), 1, t, ship.Pos.X, ship.Pos.Z, yaw, 0, hl, hb, cx, ShipFlood.Motion(l, def.Motion, hold));
                            var pose = new FloodPose { X = ship.Pos.X, Y = a.Heave - sink, Z = ship.Pos.Z, Yaw = yaw, Pitch = a.Pitch, Roll = a.Roll + list, Surface = waves.HeightAt };
                            var f = DeckFlooding.Step(l, pose, ship.DownfloodOpen, .25, ref pond);
                            if (hold <= l.D.PumpResidual) dry = true;
                            else if (hold >= 3 * l.D.PumpResidual) dry = false;
                            double pump = dry ? 0 : Math.Min(ShipFlood.PumpRate(l.D, people), hold / .25 + f.Below);
                            hold += (f.Below - pump) * .25;
                            maxHold = Math.Max(maxHold, hold);
                            sink += Math.Clamp(l.SinkageFor(hold + pond) - sink, -.075, .075);
                            list += Math.Clamp(ShipFlood.List(l, hold, 0, 1) - list, -Math.PI / 360, Math.PI / 360);
                        }
                        return (hold, pond, maxHold);
                    }
                    foreach (double yaw in new[] { 0.0, Math.PI / 4, Math.PI / 2 })
                        foreach (int people in new[] { 0, 2 })
                        {
                            var r = Run(yaw, people, 1800);
                            Check(r.maxHold < (people > 0 ? .5 : 5) && r.hold + r.pond < .1 * l.Reserve,
                                $"force-12 closed 30 min, heading {yaw * 180 / Math.PI:F0}, {people} pumpers: hold {r.hold:F3} t, peak {r.maxHold:F3}, deck {r.pond:F3}");
                        }
                    if (hatch != null) ship.ToggleHatch(null, hatch);
                    foreach (var f in def.Fixtures.Where(f => f.Type is "gun" or "gate")) ship.OpenFittings.Add(f.Code);
                    var opened = Run(0, 0, 600);
                    Check(opened.hold > 1, $"force-12 open ports, gates and hatch 10 min: hold {opened.hold:F2} t");
                    if (hatch != null) ship.ToggleHatch(null, hatch);
                    ship.OpenFittings.Clear();
                    Check(ship.Water < .5 && !ship.GoneDown, $"live flood tick: closed ship stays dry ({ship.Water:F3} t below, {ship.DeckWater:F3} on deck)");
                    Check(ship.PartDamage.Hurt.All(h => h == 0), "live storm ship remains undamaged in the deep pool");
                    ship.DeckWater = 2;
                    ship.Flood(5); // writes both stored water volumes to the same synced/saved state
                    var copy = (EntityShip)sapi.World.ClassRegistry.CreateEntity(type);
                    copy.WatchedAttributes["ships"] = ship.WatchedAttributes.GetTreeAttribute("ships").Clone();
                    copy.Pos.SetPos(ship.Pos.X + 32, wy, ship.Pos.Z);
                    sapi.World.SpawnEntity(copy);
                    Check(Math.Abs(copy.DeckWater - 2) < .01 && Math.Abs(copy.Water - ship.Water) < .02,
                        "hold water and transient deck water survive state save/load");
                    copy.Die(EnumDespawnReason.Removed);
                }
                catch (Exception e) { Check(false, "storm flooding exception: " + e); }
                finally
                {
                    sea.SetOverride(null);
                    ship.Die(EnumDespawnReason.Removed);
                    Finish();
                }
            }, 8000);
        }
    }
}
