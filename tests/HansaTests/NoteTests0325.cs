using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.25) Stern chasers (note 179: ports through the transom, guns firing aft), the older carracks archived (only
        /// carrack-m's charter to be had), crawling under the sweeps at the wheel (the speed and turn maths), the cannon's
        /// report (our own short shots).
        ///   dotnet run -c Debug -- notes0325
        /// </summary>
        static void Notes0325(string root)
        {
            Console.WriteLine("0.3.25: stern chasers, the older carracks archived, crawling under the sweeps");
            string assets = Path.Combine(root, "assets", "hansa");
            var md = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(assets, "config", "ships", "carrack-m.json")));
            md.Init();
            SternChasers(md);
            Archived(assets, md);
            CrawlMaths();
        }

        static void SternChasers(ShipDefinition md)
        {
            var places = md.GunPlaces ?? Array.Empty<GunPlaceDef>();
            var stern = places.Where(p => ShipCompartments.AlongX(md.Fixture(p.Gun))).ToList();
            Check(stern.Count == 2 && stern.All(p => !p.StartsLaid && md.Fixture(p.Gun).Out[0] < -0.99 && md.Fixture(p.Gun).Lid != null),
                $"179: two stern chasers, empty places, each pointing aft through a lidded port ({string.Join(", ", stern.Select(p => p.Id))})");
            if (stern.Count != 2) return;
            var a = md.Fixture(stern[0].Gun);
            var b = md.Fixture(stern[1].Gun);
            double sx = md.Fixtures.Where(f => f.Type == "gun" && f.Pos != null).Min(f => f.Pos[0]);
            // symmetric: mirrored z, the same x, deck and limits mirrored (a trim to port on one is a trim to starboard on the other)
            Check(Math.Abs(a.Pos[0] - b.Pos[0]) < 1e-3 && Math.Abs(a.Pos[1] - b.Pos[1]) < 1e-3 && Math.Abs(a.Pos[2] + b.Pos[2]) < 1e-3 &&
                  Math.Abs(a.TrainLimits[0] + b.TrainLimits[1]) < 0.01 && Math.Abs(a.TrainLimits[1] + b.TrainLimits[0]) < 0.01 &&
                  a.Pos[0] <= sx + 1e-6 && Math.Abs(a.Pos[2]) > 1.0,
                $"179: port and starboard of the centre ({a.Pos[2]:0.000}, {b.Pos[2]:0.000}), symmetric, the aftmost guns (x {a.Pos[0]:0.00}), trained {a.TrainLimits[0]:0.#}..{a.TrainLimits[1]:0.#} / {b.TrainLimits[0]:0.#}..{b.TrainLimits[1]:0.#}");
            foreach (var g in new[] { a, b })
            {
                // laid square it points aft, out of the transom; a positive train turns the muzzle to starboard
                GunLaying.MuzzleLocal(g, new GunLay(0, 0), out var m0, out var d0);
                var dPlus = GunLaying.Dir(g, new GunLay(10, 0));
                var pb = ShipCompartments.PortBox(g);
                double outer = pb[0];
                Check(d0.X < -0.999 && GunLaying.TrainAcross(g) == 1 && dPlus.Z > 0.1 && m0.X < outer &&
                      pb[3] - pb[0] < 0.6 && pb[3] - pb[0] > 0.2 && pb[2] < g.Pos[2] && pb[5] > g.Pos[2] && pb[1] > g.Pos[1] && pb[4] < g.Pos[1] + 1.1,
                    $"179: {g.Code} fires aft ({d0.X:0.000}), trained + to starboard ({dPlus.Z:0.00}), its muzzle {outer - m0.X:0.00} out of the transom; " +
                    $"its port box x {pb[0]:0.00}..{pb[3]:0.00} (the transom's thickness), y {pb[1]:0.00}..{pb[4]:0.00}, z {pb[2]:0.00}..{pb[5]:0.00}");
                // it trains about the transom's middle, its lid hinged across her (z), opening out and up
                var lid = md.MovingParts.FirstOrDefault(p => p.Code == g.Lid);
                Check(lid != null && Math.Abs(lid.AxisDir[2]) > 0.99 && lid.Angle < -100 && lid.Source == "open:" + g.Code && md.GunOfPart.ContainsKey(g.Code + "-barrel"),
                    $"179: {g.Code}'s lid hinged across her, opening {(lid == null ? 0 : -lid.Angle):0}° out and up by its port; its barrel is one of its parts");
                // on the open poop: its port closes no room; the sea through it lands on the deck
                Check(ShipCompartments.PortRoom(md, g) == null, $"179: {g.Code} stands on an open deck (its port closes no room)");
            }
            // which guns stand in each other's way: the stern chasers and the poop's aftmost broadside guns on their side, no
            // pair of guns laid as designed, not the two chasers
            var guns = md.Fixtures.Where(f => f.Type == "gun").ToList();
            var clash = new List<string>();
            for (int i = 0; i < guns.Count; i++)
                for (int j = i + 1; j < guns.Count; j++)
                    if (GunPlaces.RoomsClash(guns[i], guns[j])) clash.Add(guns[i].Code + "/" + guns[j].Code);
            bool laidClash = places.Where(p => p.StartsLaid).Any(p => places.Where(q => q.StartsLaid && q != p).Any(q => GunPlaces.RoomsClash(md.Fixture(p.Gun), md.Fixture(q.Gun))));
            bool chaserOnly = clash.All(c => c.Contains("-stern"));
            Check(!laidClash && chaserOnly && clash.Count > 0 && !GunPlaces.RoomsClash(a, b) &&
                  clash.All(c => c.Split('/').Select(x => x.Split('-')[1]).Distinct().Count() == 1),
                $"179: only the stern chasers and broadside guns on their own side stand in each other's way ({string.Join(", ", clash)}); no guns laid as designed do");
            // the mod's rule: one of a clashing pair mounted, the other refused; taken up, allowed
            var st = new GunPlaceStates(md);
            var sp = stern.First(p => md.Fixture(p.Gun).Pos[2] > 0);
            var other = places.FirstOrDefault(p => p != sp && GunPlaces.RoomsClash(md.Fixture(sp.Gun), md.Fixture(p.Gun)));
            string first = GunPlaces.MountProblem(md, sp, st, null, null);
            st.Set(sp, true);
            string refused = other == null ? null : GunPlaces.MountProblem(md, other, st, null, null);
            var inWay = other == null ? null : GunPlaces.GunInTheWay(md, other, st);
            st.Set(sp, false);
            string then = other == null ? "none" : GunPlaces.MountProblem(md, other, st, null, null);
            var oppo = stern.First(p => p != sp);
            st.Set(sp, true);
            string bothChasers = GunPlaces.MountProblem(md, oppo, st, null, null);
            st.Set(sp, false);
            Check(first == null && other != null && refused == "hansa:gunplace-gun-room" && inWay == sp && then == null && bothChasers == null,
                $"179: {sp.Id} mounted ({first ?? "ok"}), {other?.Id} is refused ({refused}, {inWay?.Id} in the way); {sp.Id} taken up, it may ({then ?? "ok"}); both chasers at once ({bothChasers ?? "ok"})");
            // the sea: no waist downflood point from a stern chaser's port (its "port" is across her, not along her side)
            var points = md.DamageParts?.Downflood;
            Check(points != null && !points.Any(p => stern.Any(s => p.OpenFixture == s.Gun)),
                $"179: no downflood point from the stern chasers' ports (open deck) among her {points?.Length ?? 0}");
            // the fire command's sides: port and starboard are her broadsides, stern the chasers
            Check(guns.Count(f => ShipCompartments.AlongX(f) && f.Out[0] < 0) == 2 && guns.Where(f => !ShipCompartments.AlongX(f)).All(f => Math.Abs(f.Out[2]) > 0.99),
                "179: /hansa fire port|starboard picks the broadside guns, stern the two chasers");
        }

        static void Archived(string assets, ShipDefinition md)
        {
            var deed = JObject.Parse(File.ReadAllText(Path.Combine(assets, "itemtypes", "shipdeed.json")));
            var states = deed["variantgroups"]?[0]?["states"]?.Select(t => (string)t).ToList() ?? new List<string>();
            var tabs = deed["creativeinventory"]?["general"]?.Select(t => (string)t).ToList() ?? new List<string>();
            bool Matches(string code) => tabs.Any(w => w == "*" || w.StartsWith("*") ? code.EndsWith(w.TrimStart('*')) : code == w);
            var lang = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Path.Combine(assets, "lang", "en.json")));
            Check(states.Contains("carrack") && states.Contains("carrack-a") && states.Contains("carrack-m") &&
                  Matches("shipdeed-carrack-m") && !Matches("shipdeed-carrack") && !Matches("shipdeed-carrack-a") &&
                  File.Exists(Path.Combine(assets, "entities", "ship", "carrack.json")) && File.Exists(Path.Combine(assets, "entities", "ship", "carrack-a.json")),
                $"archived: only carrack-m's charter in the creative inventory ({string.Join(", ", tabs)}); carrack's and carrack-a's charters and ships still load (old deeds launch)");
            lang.TryGetValue("entity-carrack-m", out var en); lang.TryGetValue("item-shipdeed-carrack-m", out var dn);
            Check(en == "Carrack" && dn == "Charter of a carrack" &&
                  md.Name == "Carrack" && !Directory.GetFiles(Path.Combine(assets, "recipes"), "*.json", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("shipdeed")),
                $"archived: carrack-m is plainly \"{en}\" (her charter \"{dn}\", her design \"{md.Name}\"); no recipe gives a charter");
        }

        static void CrawlMaths()
        {
            var cfg = (CrawlSpeed: Crawl.DefaultSpeed, CrawlAsternSpeed: Crawl.DefaultAsternSpeed, CrawlTurn: Crawl.DefaultTurn, CrawlRampSeconds: Crawl.DefaultRampSeconds);
            // ahead, astern, holding
            Check(Crawl.Target(1, cfg.CrawlSpeed, cfg.CrawlAsternSpeed) == cfg.CrawlSpeed && Crawl.Target(-1, cfg.CrawlSpeed, cfg.CrawlAsternSpeed) == -cfg.CrawlAsternSpeed &&
                  Crawl.Target(0, cfg.CrawlSpeed, cfg.CrawlAsternSpeed) == 0 && cfg.CrawlSpeed >= 1.2f && cfg.CrawlSpeed <= 2f && cfg.CrawlAsternSpeed < cfg.CrawlSpeed,
                $"sweeps: W held drives her to {cfg.CrawlSpeed} b/s ahead (about a walk), S to {cfg.CrawlAsternSpeed} astern, neither to a stop");
            // W held: up to speed in CrawlRampSeconds; let go, stopped as quickly
            const float dt = 0.05f;
            float v = 0, x = 0, t = 0;
            float tUp = -1;
            for (; t < 4f; t += dt)
            {
                v = Crawl.StepSpeed(v, Crawl.Target(1, cfg.CrawlSpeed, cfg.CrawlAsternSpeed), Math.Max(cfg.CrawlSpeed, cfg.CrawlAsternSpeed), cfg.CrawlRampSeconds, 0.5f, dt);
                x += v * dt;
                if (tUp < 0 && v >= cfg.CrawlSpeed - 1e-4) tUp = t + dt;
            }
            float tDown = -1;
            for (float t2 = 0; t2 < 4f; t2 += dt)
            {
                v = Crawl.StepSpeed(v, 0, Math.Max(cfg.CrawlSpeed, cfg.CrawlAsternSpeed), cfg.CrawlRampSeconds, 0.5f, dt);
                if (v <= 1e-4) { tDown = t2 + dt; break; }
            }
            Check(Math.Abs(tUp - cfg.CrawlRampSeconds) < 0.11 && Math.Abs(tDown - cfg.CrawlRampSeconds) < 0.11 && x > 4 && x < 5,
                $"sweeps: up to speed in {tUp:0.00} s, {x:0.0} blocks in 4 s; let go, stopped in {tDown:0.00} s");
            // coasting in from under sail (5 b/s), she loses it at least as fast as she would anyway
            float c = 5;
            for (int i = 0; i < 20; i++) c = Crawl.StepSpeed(c, 0, cfg.CrawlSpeed, cfg.CrawlRampSeconds, 0.5f, dt);
            Check(Math.Abs(c - (5 - 20 * dt * 0.75f)) < 0.01, $"sweeps taken coasting at 5 b/s: she slows at the sweeps' rate (0.75 b/s²) or her own if quicker ({c:0.00} after 1 s)");
            // D (+1) swings her bow to starboard as the helm does with way on (the yaw growing smaller), at CrawlTurn
            double helm = HelmMath.YawRate(1, 5, 1);
            float want = Crawl.YawTarget(1, cfg.CrawlTurn);
            Check(want < 0 && Math.Sign(want) == Math.Sign(helm) && Math.Abs(want * 180 / Math.PI + cfg.CrawlTurn) < 1e-4 && Crawl.YawTarget(-1, cfg.CrawlTurn) == -want,
                $"sweeps: D turns her bow to starboard (yaw rate {want * 180 / Math.PI:0.0}°/s, as the helm to starboard with way on), A to port");
            // 90° standing: D held till she's round, about the time it should take; let go, she stops turning in half a second
            float yaw = 0, rate = 0, tt = 0;
            while (yaw > -Math.PI / 2 && tt < 60)
            {
                rate = Crawl.StepYaw(rate, Crawl.YawTarget(1, cfg.CrawlTurn), cfg.CrawlTurn, dt);
                yaw += rate * dt;
                tt += dt;
            }
            float settle = 0;
            while (Math.Abs(rate) > 1e-6 && settle < 5)
            {
                rate = Crawl.StepYaw(rate, 0, cfg.CrawlTurn, dt);
                yaw += rate * dt;
                settle += dt;
            }
            Check(tt > 90 / cfg.CrawlTurn && tt < 90 / cfg.CrawlTurn + 1 && settle <= 0.55f && yaw * 180 / Math.PI > -92.5,
                $"sweeps: D held, she comes round 90° in {tt:0.0} s ({cfg.CrawlTurn}°/s); let go, she stops turning in {settle:0.00} s ({-yaw * 180 / Math.PI:0.0}° in all)");
            // under the sweeps only furled, at anchor never, a ship (not a boat with oars)
            Check(Crawl.Holds(0, false, false, 0) && !Crawl.Holds(1, false, false, 0) && !Crawl.Holds(0, true, false, 0) && !Crawl.Holds(0, false, true, 0) && !Crawl.Holds(0, false, false, 1.2f),
                "sweeps: she stays under them only furled, the anchor up, afloat, and a ship (a boat has her oars)");
        }
    }
}
