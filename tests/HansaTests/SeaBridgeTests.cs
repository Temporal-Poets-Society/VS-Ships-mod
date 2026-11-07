using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Func0 = System.Func<System.Reflection.MethodInfo, object[], object>;

namespace HansaTests
{
    /// <summary>A stand-in for one of the game's interfaces: every call goes to <see cref="On"/>.</summary>
    public class FakeApi : DispatchProxy
    {
        public Func0 On;
        protected override object Invoke(MethodInfo m, object[] a)
        {
            var r = On?.Invoke(m, a);
            if (r == null && m.ReturnType.IsValueType && m.ReturnType != typeof(void)) r = Activator.CreateInstance(m.ReturnType);
            return r;
        }

        public static T Make<T>(Func0 on)
        {
            var p = Create<T, FakeApi>();
            ((FakeApi)(object)p).On = on;
            return p;
        }
    }

    static partial class Program
    {
        /// <summary>
        /// A load context that loads the given assemblies from their files and notes every assembly anything in it asks
        /// for; the game's (VintagestoryAPI and the rest the tests already have) are shared with the tests, and the rest of
        /// the game's come from its folders. "Sea" is loaded only if it is given.
        /// </summary>
        sealed class WatchedContext : AssemblyLoadContext
        {
            public readonly List<string> Asked = new();
            readonly Dictionary<string, string> own;
            readonly string[] gameDirs;

            public WatchedContext(string name, Dictionary<string, string> own, string game) : base(name)
            {
                this.own = own;
                gameDirs = new[] { game, Path.Combine(game, "Lib"), Path.Combine(game, "Mods") };
            }

            protected override Assembly Load(AssemblyName n)
            {
                lock (Asked) Asked.Add(n.Name);
                if (own.TryGetValue(n.Name, out var path)) return LoadFromAssemblyPath(path);
                if (Default.Assemblies.Any(a => a.GetName().Name == n.Name)) return null;
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, n.Name + ".dll"))) return null;
                foreach (var d in gameDirs)
                {
                    string p = Path.Combine(d, n.Name + ".dll");
                    if (File.Exists(p)) return LoadFromAssemblyPath(p);
                }
                return null;
            }
        }

        static string Meta(string key) => typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

        /// <summary>
        /// (7 November 2026) Ships without Sea.dll. Ships.dll names nothing of Sea (its metadata); without Sea its own code
        /// (loaded where Sea can't be found) takes the open sea contracts' wind, else vanilla's, keeps its hulls with a
        /// <c>seacontracts:hulls</c> owner, reads Sea's version; the bridge ShipsSea.dll loads and binds against the Sea build
        /// it was built with (every reference into Sea resolves, the types match field for field); the contract's hull record
        /// cuts what Sea's own shader cuts; the lid's outline lies level inside her hull.
        /// Needs a Release build of Ships (dotnet build -c Release -p:SeaRoot=...). dotnet run -c Release -- sea-bridge
        /// </summary>
        static void SeaBridgeTests(string root)
        {
            Console.WriteLine("sea bridge: Ships.dll without Sea, the bridge against Sea, the contract's hull record, the lids");
            string mod = Path.Combine(root, "bin", "Release", "mod");
            string shipsDll = Path.Combine(mod, "Ships.dll"), bridgeDll = Path.Combine(mod, "native", "ShipsSea.dll");
            string seaRoot = Meta("SeaRoot"), game = Meta("VintageStoryPath");
            string seaDll = seaRoot == null ? null : Path.Combine(seaRoot, "bin", "Release", "mod", "Sea.dll");
            if (!File.Exists(shipsDll) || !File.Exists(bridgeDll) || seaDll == null || !File.Exists(seaDll) || game == null)
            {
                Check(false, $"sea-bridge: a Release build of Ships with its bridge ({shipsDll}, {bridgeDll}) and Sea's ({seaDll})");
                return;
            }

            // ---- (a) Ships.dll's metadata: no reference to Sea at all
            using (var pe = new PEReader(File.OpenRead(shipsDll)))
            {
                var md = pe.GetMetadataReader();
                var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
                var seaTypes = md.TypeReferences.Select(h => md.GetTypeReference(h)).Count(t => md.GetString(t.Namespace) == "Sea" || md.GetString(t.Namespace).StartsWith("Sea."));
                Check(!refs.Contains("Sea") && seaTypes == 0 && refs.Contains("VintagestoryAPI"),
                    $"Ships.dll references no Sea assembly and no Sea type ({refs.Count} references: {string.Join(", ", refs)})");
            }
            using (var pe = new PEReader(File.OpenRead(bridgeDll)))
            {
                var md = pe.GetMetadataReader();
                var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
                Check(refs.Contains("Sea") && refs.Contains("Ships"), $"the bridge ShipsSea.dll references Sea and Ships ({string.Join(", ", refs)})");
            }

            // ---- (b) without Sea: Ships.dll where Sea can't be found
            var alone = new WatchedContext("ships-without-sea", new Dictionary<string, string> { ["Ships"] = shipsDll }, game);
            var ships = alone.LoadFromAssemblyName(new AssemblyName("Ships"));
            WithoutSea(root, ships);
            lock (alone.Asked)
                Check(!alone.Asked.Contains("Sea") && !alone.Asked.Contains("ShipsSea"),
                    $"without Sea nothing of Ships asks for Sea or its bridge (asked for: {string.Join(", ", alone.Asked.Distinct())})");

            // ---- (c) with Sea: the bridge against the Sea build it was built with
            var both = new WatchedContext("ships-with-sea", new Dictionary<string, string> { ["Ships"] = shipsDll, ["ShipsSea"] = bridgeDll, ["Sea"] = seaDll }, game);
            WithSea(both, seaRoot);

            // ---- (e) the contract's hull record, as Sea reads it, cuts what Sea's own shader cuts (no detailed outline)
            HullRecords();
        }

        static object Get(object o, string name)
        {
            var t = o.GetType();
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f.GetValue(o);
            return t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
        }

        static void WithoutSea(string root, Assembly ships)
        {
            // the version Ships binds to (its own check, offline)
            var loader = ships.GetType("Hansa.SeaBridgeLoader", true);
            var ok = loader.GetMethod("VersionOk");
            bool V(string v) => (bool)ok.Invoke(null, new object[] { v, null });
            var yes = new[] { "0.6.0-pre.1", "0.6.0-pre.2", "0.6.0-rc.1", "0.6.0", "0.6.1", "0.7.0-dev.1", "1.0.0" };
            var no = new[] { "0.5.9", "0.6.0-pre.0", "0.6.0-dev.4", "0.4.2", "", null, "sea", "0.6" };
            Check(yes.All(V) && !no.Any(V),
                $"Sea's version: binds to {string.Join(", ", yes.Where(V))}; not to {string.Join(", ", no.Where(v => !V(v)).Select(v => v ?? "null"))}");

            // Sea not installed: no bridge, a note once, nothing loaded
            var notes = new List<string>();
            var log = FakeApi.Make<ILogger>((m, a) => { if (m.Name is "Notification" or "Warning" or "Error") notes.Add(m.Name + ": " + a[0]); return null; });
            var modLoader = FakeApi.Make<IModLoader>((m, a) => null);
            var noSea = FakeApi.Make<ICoreAPI>((m, a) => m.Name == "get_ModLoader" ? modLoader : null);
            var bind = loader.GetMethod("Bind");
            object l1 = bind.Invoke(null, new object[] { noSea, log }), l2 = bind.Invoke(null, new object[] { noSea, log });
            Check(l1 == null && l2 == null && notes.Count == 1 && notes[0].StartsWith("Notification") && notes[0].Contains("Sea is not installed"),
                $"no Sea: no link, and one note, once ({string.Join(" | ", notes)})");

            // the wind: the contracts' (in Sea's units), else vanilla's at sea level
            var cache = new Dictionary<string, object>();
            var asked = new List<Vec3d>();
            double hoursAsked = double.NaN;
            var blocks = FakeApi.Make<IBlockAccessor>((m, a) =>
            {
                if (m.Name != "GetWindSpeedAt") return null;
                var p = a[0] as Vec3d;
                if (p == null) return null;
                asked.Add(p);
                return new Vec3d(0.4, 0, 0);
            });
            var calendar = FakeApi.Make<IGameCalendar>((m, a) => m.Name == "get_TotalHours" ? 1234.5 : null);
            long ms = 1000;
            var world = FakeApi.Make<IWorldAccessor>((m, a) => m.Name switch
            {
                "get_BlockAccessor" => blocks, "get_Calendar" => calendar, "get_SeaLevel" => (object)110, "get_ElapsedMilliseconds" => (object)ms, _ => null
            });
            var api = FakeApi.Make<ICoreAPI>((m, a) => m.Name switch { "get_World" => (object)world, "get_ObjectCache" => cache, "get_Side" => EnumAppSide.Server, _ => null });
            var modType = ships.GetType("Hansa.HansaMod", true);
            var hansa = Activator.CreateInstance(modType);
            modType.GetField("api", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(hansa, api);
            var contractsType = ships.GetType("Hansa.ContractWaves", true);
            modType.GetProperty("Contracts").SetValue(hansa, Activator.CreateInstance(contractsType, cache, (Action<string>)(_ => { })));
            var fallback = modType.GetMethod("FallbackWind");
            var windHere = modType.GetMethod("WindHere");
            (double X, double Z, double Strength) W(MethodInfo f, double x, double z)
            {
                var w = f.Invoke(hansa, new object[] { x, z });
                return ((double)Get(w, "X"), (double)Get(w, "Z"), (double)Get(w, "Strength"));
            }
            var v1 = W(windHere, 100, 200);
            var v2 = W(fallback, 110, 205);   // the same 32 blocks: the second second of the cache
            Check(modType.GetProperty("SeaLink").GetValue(hansa) == null && v1.X == 0.4 && v1.Z == 0 && Math.Abs(v1.Strength - 0.4) < 1e-12 && v2 == v1 &&
                  asked.Count == 1 && asked[0].Y == 111 && asked[0].X == 100 && asked[0].Z == 200,
                $"no Sea, no contract: vanilla's wind just above sea level ({v1.X}, {v1.Z} at y {(asked.Count > 0 ? asked[0].Y : double.NaN)}), looked up once a second per 32 blocks ({asked.Count})");
            ms += 1500;
            W(windHere, 100, 200);
            Check(asked.Count == 2, "a second later vanilla's wind is looked up again");

            cache["seacontracts:weather"] = new Dictionary<string, object>
            {
                ["owner"] = "test",
                ["wind"] = (System.Func<double, double, double, bool, (double X, double Z)>)((x, z, h, gusts) => { hoursAsked = h; return (5, -2.5); }),
            };
            var c1 = W(windHere, 100, 200);
            Check(Math.Abs(c1.X - 0.2) < 1e-12 && Math.Abs(c1.Z + 0.1) < 1e-12 && hoursAsked == 1234.5 && asked.Count == 2,
                $"no Sea, a seacontracts:weather wind: it, in Sea's units (5, -2.5 m/s → {c1.X:0.###}, {c1.Z:0.###}), at the calendar's hours; vanilla's not asked");
            cache["seacontracts:weather"] = new Dictionary<string, object>
            {
                ["wind"] = (System.Func<double, double, double, bool, (double X, double Z)>)((x, z, h, gusts) => (double.NaN, 0)),
            };
            var c2 = W(windHere, 100, 200);
            Check(c2.X == 0.4 && c2.Z == 0, "a contract wind with no answer (NaN): vanilla's again");
            // (7 November, seen on the bench) no weather owner but a sea owner: its own wind, before vanilla's
            cache["seacontracts:seastate"] = new Dictionary<string, object>
            {
                ["at"] = (System.Func<double, double, double, (double, double, double, double, double, double, double)>)((x, z, h) => (1.5, 6, 1, 0, -7.5, 2.5, 0)),
            };
            var c3 = W(windHere, 100, 200);
            bool vanillaOnly = (bool)modType.GetProperty("VanillaWindOnly").GetValue(hansa);
            cache.Remove("seacontracts:seastate"); cache.Remove("seacontracts:weather");
            bool vanillaOnlyAfter = (bool)modType.GetProperty("VanillaWindOnly").GetValue(hansa);
            Check(Math.Abs(c3.X + 0.3) < 1e-12 && Math.Abs(c3.Z - 0.1) < 1e-12 && !vanillaOnly && vanillaOnlyAfter,
                $"no weather owner, a sea owner: its sea's wind (-7.5, 2.5 m/s → {c3.X:0.###}, {c3.Z:0.###}), no 'no wind model' warning; without either, the warning");

            // the waves: the contracts' surface, else calm (no trains)
            var waves = Activator.CreateInstance(contractsType, cache, (Action<string>)(_ => { }));
            var trains = Activator.CreateInstance(ships.GetType("Hansa.WaveTrains", true));
            bool sampled = (bool)contractsType.GetMethod("Sample").Invoke(waves, new[] { trains, 0.0, 0.0 });
            cache["seacontracts:surface"] = new Dictionary<string, object>
            {
                ["seconds"] = (System.Func<double>)(() => 42),
                ["trains"] = (System.Func<double, double, double, double[], double[], double[], double[], double[], int>)((x, z, t, kx, kz, om, ph, amp) =>
                    { kx[0] = 0.1; kz[0] = 0; om[0] = 0.8; ph[0] = 0; amp[0] = 0.5; return 1; }),
            };
            bool sampled2 = (bool)contractsType.GetMethod("Sample").Invoke(waves, new[] { trains, 0.0, 0.0 });
            Check(!sampled && sampled2 && (int)Get(trains, "Count") == 1,
                "no Sea: no surface contract, calm (no waves); a seacontracts:surface, its trains");

            // the hull cut: with a seacontracts:hulls owner, registered there (once, again when the owner changes)
            var hullsType = ships.GetType("Hansa.ContractHulls", true);
            var hulls = Activator.CreateInstance(hullsType, cache, (Action<string>)(_ => { }));
            System.Func<double, double, double, bool> dry = (x, y, z) => y < 0;
            System.Func<double[][]> records = () => Array.Empty<double[]>();
            bool KeepDry() => (bool)hullsType.GetMethod("KeepDry").Invoke(hulls, new object[] { dry });
            bool KeepHulls() => (bool)hullsType.GetMethod("KeepHulls").Invoke(hulls, new object[] { records });
            bool none = !KeepDry() && !KeepHulls();
            var reg = new List<string>();
            Dictionary<string, object> Owner(string who) => new()
            {
                ["owner"] = who,
                ["register"] = (Action<string, System.Func<double[][]>>)((id, f) => reg.Add($"{who}:register:{id}:{f == records}")),
                ["unregister"] = (Action<string>)(id => reg.Add($"{who}:unregister:{id}")),
                ["registerDry"] = (Action<string, System.Func<double, double, double, bool>>)((id, f) => reg.Add($"{who}:registerDry:{id}:{f == dry}")),
                ["unregisterDry"] = (Action<string>)(id => reg.Add($"{who}:unregisterDry:{id}")),
            };
            cache["seacontracts:hulls"] = Owner("a");
            bool held = KeepDry() && KeepHulls() && KeepDry() && KeepHulls();
            cache["seacontracts:hulls"] = Owner("b");
            bool moved = KeepDry() && KeepHulls();
            hullsType.GetMethod("Release").Invoke(hulls, null);
            Check(none && held && moved && string.Join(",", reg) ==
                  "a:registerDry:ships:True,a:register:ships:True,b:registerDry:ships:True,b:register:ships:True,b:unregister:ships,b:unregisterDry:ships",
                $"no Sea: no seacontracts:hulls, Ships' own; an owner holds her dry places and hulls (once), a new owner again, released on leaving ({string.Join(",", reg)})");

            // the lid's outline: level, inside her hull less the inset, across her waterplane, and none over her deck
            var json = File.ReadAllText(Path.Combine(root, "assets/ships/config/ships/caravel.json"));
            var defType = ships.GetType("Hansa.ShipDefinition", true);
            var def = JsonConvert.DeserializeObject(json, defType);
            defType.GetMethod("Init").Invoke(def, null);
            var mine = JsonConvert.DeserializeObject<ShipDefinition>(json);
            mine.Init();
            var lidType = ships.GetType("Hansa.ShipWaterLids", true);
            var points = lidType.GetMethod("OutlinePoints");
            double inset = (double)lidType.GetField("Inset").GetValue(null);
            var o = mine.WaterClip.DryOutline;
            int pts = 0, off = 0, outside = 0;
            double widest = 0;
            foreach (var (yaw, pitch, roll) in new[] { (0.0, 0.0, 0.0), (1.1, 0.03, 0.12), (2.5, -0.05, -0.2) })
            {
                double lidY = 50 + mine.WaterClip.Height + 0.12;
                var r = ((Vec3d, Vec3d)[])points.Invoke(null, new object[] { def, 100.0, 50.0, 200.0, yaw, pitch, roll, lidY });
                if (r == null) { off += 1000; continue; }
                foreach (var (a, b) in r)
                    foreach (var p in new[] { a, b })
                    {
                        pts++;
                        if (Math.Abs(p.Y - lidY) > 1e-3) off++;
                        ShipFrame.ToLocal(yaw, pitch, roll, p.X - 100, p.Y - 50, p.Z - 200, out double lx, out double ly, out double lz);
                        double w = o.At(lx, ly);
                        if (Math.Abs(lz) > Math.Max(0, w - inset) + 1e-3) outside++;
                        widest = Math.Max(widest, Math.Abs(lz));
                    }
            }
            var over = points.Invoke(null, new object[] { def, 100.0, 50.0, 200.0, 0.0, 0.0, 0.0, 50.0 + mine.DeckY + 3 });
            var under = points.Invoke(null, new object[] { def, 100.0, 50.0, 200.0, 0.0, 0.0, 0.0, 50.0 + mine.KeelY - 1 });
            Check(o != null && pts == 3 * 2 * 32 && off == 0 && outside == 0 && widest > mine.Beam / 2 * 0.6 && over == null && under == null,
                $"the lid without Sea: {pts} corners level at the still water (+0.12) as she lies ({off} off), inside her hull less {inset} ({outside} out), " +
                $"{widest:0.00} of her half-beam {mine.Beam / 2:0.00} at most; none over her deck or under her keel");
        }

        static void WithSea(WatchedContext both, string seaRoot)
        {
            var ships = both.LoadFromAssemblyName(new AssemblyName("Ships"));
            var bridge = both.LoadFromAssemblyName(new AssemblyName("ShipsSea"));
            var sea = both.LoadFromAssemblyName(new AssemblyName("Sea"));
            Type[] types = null;
            string error = null;
            try { types = bridge.GetTypes(); }
            catch (ReflectionTypeLoadException e) { error = string.Join("; ", e.LoaderExceptions.Select(x => x?.Message).Distinct()); }
            Check(types != null && !types.Any(t => typeof(ModSystem).IsAssignableFrom(t)),
                $"the bridge's types load against Sea's build ({error ?? types.Length + " types"}), none a mod system (the game never sees it)");
            if (types == null) return;

            // every method of the bridge compiled: each reference into Sea (and Ships) resolves
            int methods = 0;
            var broken = new List<string>();
            foreach (var t in types)
            {
                if (t.ContainsGenericParameters) continue;
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                             .Cast<MethodBase>().Concat(t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    try { RuntimeHelpers.PrepareMethod(m.MethodHandle); methods++; }
                    catch (Exception e) { broken.Add($"{t.Name}.{m.Name}: {e.GetType().Name} {e.Message}"); }
                }
            }
            // and each member reference into Sea names a member Sea has
            int seaRefs = 0;
            var missing = new List<string>();
            using (var pe = new PEReader(File.OpenRead(bridge.Location)))
            {
                var md = pe.GetMetadataReader();
                foreach (var h in md.MemberReferences)
                {
                    var mr = md.GetMemberReference(h);
                    if (mr.Parent.Kind != HandleKind.TypeReference) continue;
                    var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
                    string tn = md.GetString(tr.Name), ns = md.GetString(tr.Namespace);
                    // (nested types: Sea's are not nested)
                    if (tr.ResolutionScope.Kind != HandleKind.AssemblyReference) continue;
                    var ar = md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope);
                    if (md.GetString(ar.Name) != "Sea") continue;
                    seaRefs++;
                    string name = md.GetString(mr.Name);
                    var st = sea.GetType(ns + "." + tn);
                    bool found = st != null && st.GetMember(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Length > 0;
                    if (!found) missing.Add($"{ns}.{tn}.{name}");
                }
            }
            Check(broken.Count == 0 && missing.Count == 0 && seaRefs > 20,
                $"the bridge binds against Sea: {methods} methods compiled, {seaRefs} member references into Sea all found" +
                (broken.Count + missing.Count > 0 ? $" (broken: {string.Join("; ", broken.Concat(missing))})" : ""));

            // what it gives Ships is Ships' interface, from the Ships beside it
            var link = ships.GetType("Hansa.ISeaLink", true);
            var bind = bridge.GetType("ShipsSea.SeaBridge", true).GetMethod("Bind");
            var impl = bridge.GetType("ShipsSea.SeaLink", true);
            var modLoader = FakeApi.Make<IModLoader>((m, a) => null);
            var noSystem = FakeApi.Make<ICoreAPI>((m, a) => m.Name == "get_ModLoader" ? modLoader : null);
            Check(bind != null && bind.IsStatic && bind.ReturnType == link && link.IsAssignableFrom(impl) && bind.Invoke(null, new object[] { noSystem }) == null,
                "SeaBridge.Bind(api) gives Ships' ISeaLink (SeaLink implements it), and null where Sea's mod system isn't running");

            // the loader finds the bridge where the build put it, and the Sea build it was made against is new enough
            var loader = ships.GetType("Hansa.SeaBridgeLoader", true);
            string at = (string)loader.GetMethod("BridgePath").Invoke(null, new object[] { ships.Location });
            string seaVersion = (string)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(seaRoot, "modinfo.json")))["version"];
            bool versionOk = (bool)loader.GetMethod("VersionOk").Invoke(null, new object[] { seaVersion, null });
            Check(string.Equals(Path.GetFullPath(at), Path.GetFullPath(bridge.Location), StringComparison.OrdinalIgnoreCase) && versionOk,
                $"the loader's path is the bridge's (native/ShipsSea.dll), and Sea {seaVersion} is new enough for it");

            // the two clip types are the same, field for field; and the constants Ships keeps match Sea's
            var seaClip = sea.GetType("Sea.WaterClipBox", true);
            var shipsClip = ships.GetType("Hansa.HullWaterClip", true);
            string Fields(Type t) => string.Join(",", t.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name + ":" + x.FieldType.Name).OrderBy(x => x));
            var shader = sea.GetType("Sea.WaterShader", true);
            var clipDef = ships.GetType("Hansa.WaterClipDef", true);
            object C(Type t, string n) => t.GetField(n, BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();
            var renderer = ships.GetType("Hansa.ShipRenderer", true);
            var beaufortSea = sea.GetType("Sea.Beaufort", true);
            var beaufortShips = ships.GetType("Hansa.Beaufort", true);
            Check(Fields(seaClip) == Fields(shipsClip) && C(shader, "DryStations").Equals(C(clipDef, "DryStations")) && C(shader, "DryLevels").Equals(C(clipDef, "DryLevels")) &&
                  C(shader, "ProfileStations").Equals(C(clipDef, "Stations")) && C(shader, "CalmZone").Equals(C(renderer, "StillCalmZone")) &&
                  C(beaufortSea, "MetersPerSecondPerUnit").Equals(C(beaufortShips, "MetersPerSecondPerUnit")),
                $"Ships' HullWaterClip is Sea's WaterClipBox field for field; outline stations {C(clipDef, "DryStations")}x{C(clipDef, "DryLevels")}, profile {C(clipDef, "Stations")}, " +
                $"still calm ring {C(renderer, "StillCalmZone")}, Beaufort unit {C(beaufortShips, "MetersPerSecondPerUnit")} m/s as Sea's");
        }

        static void HullRecords()
        {
            // a clip copied into Sea's and back is the same, every field
            var rnd = new Random(7);
            float F() => (float)(rnd.NextDouble() * 4 - 1);
            var full = new HullWaterClip
            {
                X = 101.25, Z = -33.5, WaterY = 110.875, Yaw = 1.1, HalfLength = 19.5f, CentreOffset = -0.75f,
                Profile = Enumerable.Range(0, 16).Select(i => 2f + 3f * (float)Math.Sin(Math.PI * i / 15)).ToArray(), HalfWidth = 5f,
                SlopeAlong = F(), SlopeAcross = F(), CalmSlopeAcross = F(), CalmZone = 2.5f, Dry = true, DryInset = 1f, DryDepth = 3.2f, DryHeight = 5.5f,
                DryProfile = Enumerable.Range(0, 128).Select(_ => F() + 3).ToArray(), DryLevel0 = -2f, DryLevelStep = 1.5f, DryRake = -0.24f, Flooded = true, FloodY = 109.3,
            };
            var back = full.ToSea().ToShips();
            bool same = typeof(HullWaterClip).GetFields().All(f => f.FieldType.IsArray ? ReferenceEquals(f.GetValue(full), f.GetValue(back)) : Equals(f.GetValue(full), f.GetValue(back)));
            Check(same, "a hull's clip handed to Sea and back is the same, field for field (Sea gets exactly what Ships computed)");

            // the record: what Sea reads from it (no inset, no detailed outline) is the outline it cuts for the clip itself
            int cases = 0, agree = 0, points = 0, nearEdge = 0;
            foreach (bool profiled in new[] { false, true })
                foreach (bool flooded in new[] { false, true })
                {
                    var c = full;
                    c.SlopeAlong = 0.03f; c.SlopeAcross = -0.08f; c.DryProfile = null; c.Flooded = flooded;
                    if (!profiled) c.Profile = null;
                    var r = ContractHulls.ToRecord(c);
                    bool read = Sea.SeaContractMath.ToClip(r, out var fromRecord);
                    var own = c.ToSea();
                    cases++;
                    int bad = 0;
                    for (int i = 0; i < 4000; i++)
                    {
                        double x = c.X + (rnd.NextDouble() * 2 - 1) * 24, z = c.Z + (rnd.NextDouble() * 2 - 1) * 24, y = c.WaterY + (rnd.NextDouble() * 2 - 1) * 6;
                        bool a = own.InDryHull(x, y, z, false), b = fromRecord.InDryHull(x, y, z, false);
                        points++;
                        if (a == b) continue;
                        // (between the profile's stations the shorter record interpolates its own: off by a hair at the edge)
                        bool edge = false;
                        foreach (double d in new[] { -0.06, 0.06 })
                            foreach (var (dx, dz) in new[] { (d, 0.0), (0.0, d) })
                                if (own.InDryHull(x + dx, y, z + dz, false) != a) edge = true;
                        if (edge) nearEdge++; else bad++;
                    }
                    if (read && bad == 0 && r.Length == ContractHulls.RecordLength && (flooded ? r[ContractHulls.HFloodY] == c.FloodY : double.IsNaN(r[ContractHulls.HFloodY]))) agree++;
                }
            Check(agree == cases && nearEdge < points / 500,
                $"a dry hull's contract record (footprint less the inset, its 16 widths resampled) cuts what Sea cuts for its own clip: {cases - agree} of {cases} cases off, " +
                $"{nearEdge} of {points} points differ only within 0.06 of the edge");

            // the detailed record, for an owner that publishes recordLength (Sea since 7 November): Sea reads back exactly
            // the clip Ships hands its typed API, detailed outline, rake, inset and calm ring included, and cuts the same
            int detailedCases = 0, detailedSame = 0, detailedPoints = 0, detailedOff = 0;
            foreach (bool dryOutline in new[] { false, true })
                foreach (bool flooded in new[] { false, true })
                {
                    var c = full;
                    c.Flooded = flooded;
                    if (!dryOutline) c.DryProfile = null;
                    if (!flooded) c.FloodY = 0;   // (a flood height without a flood is no field of the record)
                    var r = ContractHulls.ToRecord(c, detailed: true);
                    var own = c.ToSea();
                    detailedCases++;
                    if (!Sea.SeaContractMath.ToClip(r, out var fromRecord)) continue;
                    var asShips = fromRecord.ToShips();
                    bool fields = typeof(HullWaterClip).GetFields().All(f => f.FieldType.IsArray
                        ? (f.GetValue(c) is float[] a ? f.GetValue(asShips) is float[] b && a.SequenceEqual(b) : f.GetValue(asShips) == null)
                        : Equals(f.GetValue(c), f.GetValue(asShips)));
                    int off = 0;
                    for (int i = 0; i < 4000; i++)
                    {
                        double x = c.X + (rnd.NextDouble() * 2 - 1) * 24, z = c.Z + (rnd.NextDouble() * 2 - 1) * 24, y = c.WaterY + (rnd.NextDouble() * 2 - 1) * 6;
                        detailedPoints++;
                        if (own.InDryHull(x, y, z) != fromRecord.InDryHull(x, y, z)) off++;
                    }
                    detailedOff += off;
                    if (fields && off == 0 && r.Length == (dryOutline ? ContractHulls.DetailedWithDryProfile : ContractHulls.DetailedLength)) detailedSame++;
                }
            Check(detailedSame == detailedCases && Sea.SeaContractMath.HullRecordWithDryProfile == ContractHulls.DetailedWithDryProfile
                && Sea.SeaContractMath.HDryProfile == ContractHulls.HDryProfile,
                $"a detailed contract record is Sea's typed clip field for field and cuts the same: {detailedCases - detailedSame} of {detailedCases} cases off, {detailedOff} of {detailedPoints} points differ");
        }
    }
}
