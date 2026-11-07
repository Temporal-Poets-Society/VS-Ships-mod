using System;
using System.IO;
using System.Reflection;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// Finds the Sea mod and, when it is there and new enough, loads Ships' bridge to it (ShipsSea.dll) and binds it.
    /// <para>
    /// Why a bridge: the game (1.22.7) has no optional dependencies, and an assembly that names Sea's types fails to load
    /// its types without Sea.dll. So the main assembly (Ships.dll) has no reference to Sea.dll at all, and everything that
    /// does lives in ShipsSea.dll, which references both.
    /// </para>
    /// <para>
    /// Where it lives: in the mod's <c>native/</c> folder. The game's mod loader (ModContainer.Unpack, 1.22.7) takes every
    /// .dll in a mod's root as one of its assemblies, loads them all and looks for mod systems in each (which would log
    /// errors for the bridge's types without Sea, and load it before Sea's assembly is there); a .dll in any other
    /// subfolder makes it refuse the mod, except in <c>native/</c>, which it leaves alone (meant for unmanaged libraries).
    /// The zip is unpacked whole, so the bridge is on disk beside Ships.dll.
    /// </para>
    /// <para>
    /// When: by <see cref="HansaMod.Start"/> (execute order 0.2): after every mod's StartPre, after Sea's own Start (0.15),
    /// and with every mod's assembly already loaded by the game (it loads all of them before any mod system starts), so
    /// Sea.dll is there for the bridge to bind to. Assembly.LoadFrom puts the bridge in the same load context as Ships.dll
    /// and Sea.dll, so its references to both resolve to the very assemblies the game loaded. It is loaded once per process
    /// (the singleplayer server and client share it) and bound once per side.
    /// </para>
    /// </summary>
    public static class SeaBridgeLoader
    {
        public const string SeaModId = "sea";
        /// <summary>The oldest Sea this Ships binds to (the version the bridge was built against: 0.6.0's typed API).</summary>
        public const string MinSeaVersion = "0.6.0-pre.1";
        public const string BridgeFolder = "native", BridgeFile = "ShipsSea.dll";
        public const string BridgeType = "ShipsSea.SeaBridge", BindMethod = "Bind";

        static readonly object sync = new();
        static Assembly bridge;
        static string bridgeError;
        static readonly System.Collections.Generic.HashSet<string> said = new();

        /// <summary>Each of these notes once a game session (not once a side: singleplayer runs both sides in one process).</summary>
        static bool Once(string what) { lock (said) return said.Add(what); }

        /// <summary>Where the bridge is: native/ShipsSea.dll beside Ships.dll.</summary>
        public static string BridgePath(string shipsAssemblyPath = null)
        {
            string dir = Path.GetDirectoryName(shipsAssemblyPath ?? typeof(SeaBridgeLoader).Assembly.Location) ?? "";
            return Path.Combine(dir, BridgeFolder, BridgeFile);
        }

        /// <summary>
        /// Is this Sea version one the bridge binds to? Unparsable versions are refused. (A version string, not the mod,
        /// so the check runs offline.)
        /// </summary>
        public static bool VersionOk(string version, out string why)
        {
            why = null;
            if (!ProperVersion.SemVer.TryParse(version ?? "", out var have, out string error))
            {
                why = $"its version '{version}' can't be read ({error})";
                return false;
            }
            var need = ProperVersion.SemVer.Parse(MinSeaVersion);
            if (have < need)
            {
                why = $"it is {version}, older than {MinSeaVersion}";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Sea's typed API on this side through the bridge, or null: Sea isn't installed, is too old, or the bridge can't
        /// be loaded or bound (each logged once, saying what Ships does instead).
        /// </summary>
        public static ISeaLink Bind(ICoreAPI api, ILogger log)
        {
            // (development) ModConfig/ships-dev.json { "SeaBridge": false }: run as without Sea even with Sea installed, so
            // Ships reads Sea only through the open contracts, as another mod's boat would (for checking them in game)
            ShipsDevConfig dev = null;
            try { dev = api?.LoadModConfig<ShipsDevConfig>("ships-dev.json"); } catch (Exception) { }
            if (dev != null && !dev.SeaBridge)
            {
                if (Once("dev")) log?.Warning("ships-dev.json: SeaBridge is off, so Ships reads Sea only through the open sea contracts (a development setting).");
                return null;
            }
            var sea = api?.ModLoader?.GetMod(SeaModId);
            if (sea == null)
            {
                if (Once("absent")) log?.Notification("Sea is not installed: the wind is the open sea contracts' (seacontracts:weather) or vanilla's, " +
                    "the waves the contracts' (seacontracts:surface) or calm, and the water in her hull is hidden by a seacontracts:hulls owner or Ships' own lids.");
                return null;
            }
            string version = sea.Info?.Version;
            if (!VersionOk(version, out string why))
            {
                if (Once("old")) log?.Warning("Sea is installed, but {0}: Ships runs as without Sea (update Sea to bind to it).", why);
                return null;
            }
            lock (sync)
            {
                if (bridge == null && bridgeError == null)
                {
                    string path = BridgePath();
                    try
                    {
                        if (!File.Exists(path)) throw new FileNotFoundException("not found", path);
                        bridge = Assembly.LoadFrom(path);
                    }
                    catch (Exception e) { bridgeError = $"{path}: {e.Message}"; }
                }
            }
            if (bridge == null)
            {
                if (Once("unloadable")) log?.Error("Sea {0} is installed, but Ships' bridge to it can't be loaded ({1}): Ships runs as without Sea.", version, bridgeError);
                return null;
            }
            try
            {
                var bind = bridge.GetType(BridgeType, true).GetMethod(BindMethod, BindingFlags.Public | BindingFlags.Static)
                    ?? throw new MissingMethodException(BridgeType, BindMethod);
                if (bind.Invoke(null, new object[] { api }) is ISeaLink link)
                {
                    log?.Notification("bound to Sea {0} through {1}", link.SeaVersion, BridgeFile);
                    return link;
                }
                log?.Warning("Sea {0} is installed, but its mod system isn't running on this side: Ships runs as without Sea here.", version);
                return null;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
                if (Once("unbound")) log?.Error("Sea {0} is installed, but Ships' bridge to it doesn't bind ({1}: {2}): Ships runs as without Sea.",
                    version, inner.GetType().Name, inner.Message);
                return null;
            }
        }
    }
}

namespace Hansa
{
    /// <summary>ModConfig/ships-dev.json, for development only (absent in play): see <see cref="SeaBridgeLoader.Bind"/>.</summary>
    public class ShipsDevConfig
    {
        /// <summary>False: don't bind to Sea's typed API; read Sea through the open sea contracts only.</summary>
        public bool SeaBridge = true;
    }
}
