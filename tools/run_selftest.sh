#!/usr/bin/env bash
# Runs Hansa's server self-test on a local dedicated server (data in testserver/): a small fresh world, a pool of
# water built in the sky, a carrack launched and sailed into the wall. Prints the SELFTEST lines and stops.
# Needs the Sea mod next to Hansa (../Sea, built by the same dotnet build).
#   dotnet build -c Release && tools/run_selftest.sh [--with-earth]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TS="$ROOT/testserver"
VS="${VINTAGE_STORY:-$APPDATA/Vintagestory}"
SEA="${SEA_ROOT:-$ROOT/../Sea}"
TEMPLATE="${TEMPLATE_CONFIG:-$ROOT/../Earth/testserver/serverconfig.json}"
WITH_EARTH=0
[ "${1:-}" = "--with-earth" ] && WITH_EARTH=1

mkdir -p "$TS/Mods" "$TS/ModConfig"
# the release zip of a mod folder, named with the version in its modinfo.json (release/<modid>-<version>.zip)
modzip() { echo "$1/release/$2-$(sed -n 's/.*"version"[^"]*"\([^"]*\)".*/\1/p' "$1/modinfo.json" | head -1).zip"; }
rm -f "$TS/Mods/"*.zip
cp "$(modzip "$ROOT" hansa)" "$(modzip "$SEA" sea)" "$TS/Mods/"
if [ "$WITH_EARTH" = 1 ]; then
  cp "$(modzip "$ROOT/../Cworld" cylinderworld)" "$(modzip "$ROOT/../Earth" earthmap)" "$TS/Mods/"
fi
rm -rf "$TS/Saves" "$TS/Logs"
[ -f "$TS/serverconfig.json" ] || cp "$TEMPLATE" "$TS/serverconfig.json"

python - "$TS" <<'EOF'
import json, sys
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
json.dump({"Enabled": True, "Shutdown": True}, open(ts + "/ModConfig/hansa-selftest.json", "w"))
EOF

cd "$VS"
./VintagestoryServer.exe --dataPath "$(cygpath -w "$TS")" > "$TS/server-console.log" 2>&1 || true
echo "server exited"
grep -h "SELFTEST\|\[hansa\]\|\[sea\]\|Hansa\|hansa.*rror\|sea.*rror\|Exception" "$TS/Logs/server-main.log" | head -80 || true
