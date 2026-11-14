#!/usr/bin/env bash
# Runs Hansa's server self-test on a local dedicated server (data in testserver/): a small fresh world, a pool of
# water built in the sky, a carrack launched and sailed into the wall. Prints the SELFTEST lines and stops.
# Needs a Release build of Sea (the suite's Sea folder, or SEA_ROOT).
#   dotnet build -c Release && tools/run_selftest.sh [--with-earth]
# Packages (each may also be a mod folder):
#   SHIPS_ZIP  default this checkout's release/ships-<modinfo version>.zip
#   SEA_ZIP    default Sea's release/sea-<modinfo version>.zip, else its bin/Release/mod
#   EARTH_ZIP, HYDROLOGY_ZIP, CWORLD_ZIP (--with-earth) default the newest zip in each mod's release folder
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# the suite root: the first folder above this checkout with suite.json (a worktree sits deeper than the main checkout)
SUITE="$ROOT"; while [ "$SUITE" != "/" ] && [ ! -f "$SUITE/suite.json" ]; do SUITE="$(dirname "$SUITE")"; done
TS="$ROOT/testserver"
VS="${VINTAGE_STORY:-$APPDATA/Vintagestory}"
SEA="${SEA_ROOT:-$SUITE/Sea}"
TEMPLATE="${TEMPLATE_CONFIG:-$SUITE/Earth/testserver/serverconfig.json}"
WITH_EARTH=0
[ "${1:-}" = "--with-earth" ] && WITH_EARTH=1

mkdir -p "$TS/Mods" "$TS/ModConfig"
# the release zip of a mod folder, named with the version in its modinfo.json (release/<modid>-<version>.zip)
modzip() { echo "$1/release/$2-$(sed -n 's/.*"version"[^"]*"\([^"]*\)".*/\1/p' "$1/modinfo.json" | head -1).zip"; }
# the newest <modid>-<version>.zip in a folder, by version (a pre-release sorts below its release)
newest() {
  python - "$@" <<'EOF'
import glob, os, re, sys
mod, folder = sys.argv[1], sys.argv[2]
def key(p):
    m = re.match(re.escape(mod) + r"-(\d+)\.(\d+)\.(\d+)(?:-(.+))?\.zip$", os.path.basename(p))
    if not m: return ((0,), 0, ())
    pre = m.group(4)
    return (tuple(int(g) for g in m.group(1, 2, 3)), 0 if pre else 1,
            tuple((0, int(t), "") if t.isdigit() else (1, 0, t) for t in re.split(r"[.-]", pre or "")))
found = [p for p in glob.glob(os.path.join(folder, mod + "-*.zip")) if key(p)[0] != (0,)]
print(max(found, key=key) if found else "")
EOF
}
# copy a package into the test server's Mods: a zip as it is, a mod folder as Mods/<modid>; stop if it is missing
install() { [ -n "$2" ] && [ -e "$2" ] || { echo "no $1 package found ($2); build it or set its *_ZIP" >&2; exit 1; }
  echo "$1: $2"; if [ -d "$2" ]; then cp -r "$2" "$TS/Mods/$1"; else cp "$2" "$TS/Mods/"; fi; }
rm -rf "$TS/Mods/"*
SEA_ZIP="${SEA_ZIP:-$(modzip "$SEA" sea)}"
[ -e "$SEA_ZIP" ] || SEA_ZIP="$SEA/bin/Release/mod"
install ships "${SHIPS_ZIP:-$(modzip "$ROOT" ships)}"
install sea "$SEA_ZIP"
if [ "$WITH_EARTH" = 1 ]; then
  install earth "${EARTH_ZIP:-$(newest earth "$SUITE/Earth/release")}"
  install hydrology "${HYDROLOGY_ZIP:-$(newest hydrology "$SUITE/Hydrology/release")}"
  install cylinderworld "${CWORLD_ZIP:-$(newest cylinderworld "$SUITE/Cworld/release")}"
fi
rm -rf "$TS/Saves" "$TS/Logs"
[ -f "$TS/serverconfig.json" ] || cp "$TEMPLATE" "$TS/serverconfig.json"

python - "$TS" <<'EOF'
import json, sys, os
ts = sys.argv[1]
p = ts + "/serverconfig.json"
d = json.load(open(p, encoding="utf-8-sig"))
d["MapSizeX"], d["MapSizeZ"], d["MapSizeY"] = 4096, 4096, 256
d["ModPaths"] = ["Mods", ts.replace("/", "\\") + "\\Mods"]
wc = d["WorldConfig"]
wc["Seed"] = "4242"
wc["SaveFileLocation"] = ts.replace("/", "\\") + "\\Saves\\hansatest.vcdbs"
wc["WorldName"] = "Hansa test"
wc["MapSizeY"] = 256
wc["WorldConfiguration"] = {"worldWidth": "4096", "worldLength": "4096", "worldEdge": "blocked", "gameMode": "creative"}
json.dump(d, open(p, "w", encoding="utf-8"), indent=2)
json.dump({"Enabled": True, "Shutdown": True, "Only": os.environ.get("HANSA_SELFTEST_ONLY")}, open(ts + "/ModConfig/ships-selftest.json", "w"))
EOF

cd "$VS"
./VintagestoryServer.exe --dataPath "$(cygpath -w "$TS")" > "$TS/server-console.log" 2>&1 || true
echo "server exited"
grep -h "SELFTEST\|\[ships\]\|\[sea\]\|Hansa\|ships.*rror\|sea.*rror\|Exception" "$TS/Logs/server-main.log" | head -80 || true
