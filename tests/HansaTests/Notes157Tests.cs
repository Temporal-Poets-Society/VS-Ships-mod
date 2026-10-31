using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hansa;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    public class Notes157Sound : DispatchProxy
    {
        public int Starts, Stops, Disposals;
        public float Volume;
        public readonly List<string> Calls = new();
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Calls.Add(method.Name);
            if (method.Name == "Start") Starts++;
            if (method.Name == "Stop") Stops++;
            if (method.Name == "Dispose") Disposals++;
            if (method.Name == "SetVolume") Volume = (float)args[0];
            return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    static partial class Program
    {
        static void Notes157(string root)
        {
            int before = failures;
            var (def, _) = LoadHullDesign(root, "caravel");
            var entry = def.ModularPieces.Single(e => e.Code == "hatch-main");
            var cells = def.Slots.Rect(entry.At[1], -6, -.5, -4, .5);
            foreach (bool glazed in new[] { false, true })
            {
                var hatch = ShipPiece.From(HatchPlanner.Plan(def.Slots, cells, glazed, entry.Hinge, entry.Coaming, entry.DoubleLeaf), 1, null);
                var tree = new TreeAttribute(); hatch.ToTree(tree);
                var saved = ShipPiece.FromTree(tree);
                Check(saved.Box.SequenceEqual(hatch.Box) && saved.Dir == 0 && saved.DoubleLeaf && saved.Coaming == .25 && saved.Slots == (glazed ? 1 : 0),
                    "157 hatch: save/load retains the actual box, hinge, coaming, double leaves and glazing " + glazed);
                bool clear = true;
                foreach (var at in new[] { new[] { -6.425, 0.0 }, new[] { -3.575, 0.0 }, new[] { -5.0, -.925 }, new[] { -5.0, .925 }, new[] { -6.7, .965 } })
                    clear &= !HatchPlanner.BodyInSwing(saved.Box, saved.Dir, saved.DoubleLeaf, saved.Slots > 0,
                        new[] { at[0] - .3, 1.125, at[1] - .3, at[0] + .3, 2.975, at[1] + .3 });
                Check(clear, "157 hatch: saved " + (glazed ? "glazed" : "solid") + " leaves clear bodies at both ends and sides");
                var relative = new[] { -.6, 0, -.1, .6, 1, .1 };
                var native = HatchPlanner.WorldBody(relative, -6.5, 1.125, 0, 0, 0, 0);
                bool wrong = HatchPlanner.BodyInSwing(saved.Box, saved.Dir, true, glazed, new[] { -7.1, 1.125, -.1, -5.9, 2.125, .1 });
                Check(wrong && !HatchPlanner.BodyInSwing(saved.Box, saved.Dir, true, glazed, native),
                    "157 hatch: a world-axis body beside the end clears; the old unrotated box falsely blocked it");
                Check(HatchPlanner.BodyInSwing(saved.Box, saved.Dir, true, glazed,
                    HatchPlanner.WorldBody(relative, -5, 1.125, 0, .7, 0, 0)), "157 hatch: a native body really through the leaves still blocks");
            }

            bool lays = true, reaches = true, seats = true;
            foreach (var gun in def.Fixtures.Where(f => f.Type == "gun"))
            {
                foreach (double yaw in new[] { 0, .9, 3.8 })
                {
                    var original = new GunLay(3, 4);
                    var view = GunLaying.FromView(gun, 2.1, 3.35, yaw, .08, -.12);
                    var offset = GunLaying.AimOffset(original, view);
                    var start = GunLaying.Relative(gun, view, ref offset);
                    var moved = GunLaying.Relative(gun, new GunLay(view.Train + 2, view.Elevation + 1), ref offset);
                    lays &= Math.Abs(start.Train - 3) < .001 && Math.Abs(start.Elevation - 4) < .001 &&
                        Math.Abs(moved.Train - 5) < .001 && Math.Abs(moved.Elevation - 5) < .001;
                }
                var breech = GunLaying.Breech(gun, default);
                reaches &= GunButtons.LaysGun(null, gun, GunLaying.InReach(gun, default, breech.X, breech.Y, breech.Z, 2.5), false) == gun &&
                    GunButtons.LaysGun(null, gun, GunLaying.InReach(gun, default, breech.X + 9, breech.Y, breech.Z, 2.5), false) == null &&
                    !GunLaying.InReach(gun, default, breech.X, breech.Y + 3, breech.Z, 3.5);
                var seat = GunLaying.SeatLocal(gun, new GunLay(3, 4), 0);
                seats &= GunLaying.InReach(gun, new GunLay(3, 4), seat.X, seat.Y, seat.Z, 2.5);
            }
            Check(lays, "157 guns: unchanged view starts at the existing lay; mouse delta trains/elevates on every heading and a rolling deck");
            Check(reaches && seats, "157 guns: behind-breech seats are reachable; distant and different-deck clicks cannot select a gun for work");
            Check(GunButtons.LaysGun(def.Fixtures.First(f => f.Type == "gun"), null, false, true) == null,
                "157 guns: a mounted player can still use an item with its own right-click");
            var mountedGun = def.Fixtures.First(f => f.Type == "gun");
            Check(GunButtons.LaysGun(mountedGun, def.Fixtures.Last(f => f.Type == "gun"), false, false) == mountedGun,
                "157 guns: seeing a distant gun while mounted keeps aiming the mounted gun");

            var work = new PumpActivity();
            for (long now = 0; now <= 3000; now += PumpActivity.HeartbeatMs) work.Touch("pump-a", "one", now);
            Check(work.Read(3100, 2) == (1, "pump-a"), "157 pumps: heartbeat keeps work alive across strokes without adding hands");
            work.Touch("pump-a", "two", 3000); work.Touch("pump-a", "three", 3000); work.Touch("pump-b", "four", 3000);
            Check(work.Read(3100, 2).Count == 3, "157 pumps: capacity is per pump, not per heartbeat");
            work.Stop("one"); work.Stop("two"); work.Stop("three");
            Check(work.Read(3100, 2) == (1, "pump-b"), "157 pumps: release removes only that pumper; another pump keeps working");
            Check(work.Read(3450, 2) == (0, "") && PumpActivity.HoldMs + 250 + 200 < 1000,
                "157 pumps: a missing release expires, including flood/sync intervals, in under a second");

            var clips = new List<Notes157Sound>();
            var audio = new PumpAudio((_, _, _) => {
                var sound = DispatchProxy.Create<ILoadedSound, Notes157Sound>();
                clips.Add((Notes157Sound)sound); return sound;
            });
            var pos = new Vec3f(1, 2, 3);
            for (int frame = 0; frame < 100; frame++) audio.Update("1:pump-a", pos, true, .6f);
            Check(clips.Count == 2 && clips.All(s => s.Starts == 1 && s.Stops == 0), "157 pump audio: repeated frames/strokes never stack mechanical or pouring clips");
            audio.Update("1:pump-a", pos, false, 0);
            Check(clips[0].Volume == .4f && clips[1].Stops == 1 && clips[1].Disposals == 1,
                "157 pump audio: dry pump stops/disposes pouring and keeps one quieter mechanical sound");
            audio.Retain(new HashSet<string>());
            Check(clips.All(s => s.Stops == 1 && s.Disposals == 1 && s.Calls.IndexOf("Stop") < s.Calls.IndexOf("Dispose")),
                "157 pump audio: release/timeout stops each clip before disposing it");
            audio.Update("1:pump-a", pos, true, .6f); audio.Update("2:pump-b", pos, false, 0);
            audio.RemoveShip(1);
            Check(clips[2].Stops == 1 && clips[3].Stops == 1 && clips[4].Stops == 0, "157 pump audio: unloading stops that ship's sounds only");
            audio.Dispose(); audio.Dispose();
            Check(clips.All(s => s.Stops == 1 && s.Disposals == 1), "157 pump audio: shutdown stops all remaining sounds exactly once");
            Console.WriteLine(failures == before ? "notes157 ALL OK" : $"notes157 {failures - before} FAILED");
        }
    }
}
