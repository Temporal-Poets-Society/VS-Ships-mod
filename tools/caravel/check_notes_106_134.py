"""Check the dev.14 model changes in the exported game data (not the CAD placeholders).

Run after export_caravel.py. The C# design checks still verify actual placement and walking.
"""
import json
import numpy as np
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
D = json.loads((ROOT / "assets/ships/config/ships/caravel.json").read_text())
M = json.loads((ROOT / "assets/ships/config/shipmeshes/caravel.json").read_text())
L = json.loads((ROOT / "models/caravel/caravel-lines.json").read_text())
R = json.loads((ROOT / "models/caravel/caravel-report.json").read_text())
failed = 0


def check(ok, message):
    global failed
    print(("ok   " if ok else "FAIL ") + message)
    failed += not ok


def points(q):
    return [q[1 + k * 8:4 + k * 8] for k in range(4)]


def zone_at(group, i):
    runs = M["paintQuads"].get(group, [])
    return next((D["paintZones"][z]["code"] for a, b, z in zip(runs[::3], runs[1::3], runs[2::3]) if a <= i < b), None)


rudder = M["groups"]["part:rudder"]
chords = []
for y in (-2.5, -1.5, -0.5, -0.05):
    xs = []
    for q in rudder:
        p = points(q)
        for a, b in zip(p, p[1:] + p[:1]):
            if min(a[1], b[1]) <= y <= max(a[1], b[1]) and abs(a[1] - b[1]) > 1e-8:
                xs.append(a[0] + (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]))
    chords.append((min(xs), max(xs)))
check(all(abs(b - a - 1.5) < 0.002 for a, b in chords) and max(a for a, _ in chords) - min(a for a, _ in chords) < 0.002,
      "106: rectangular submerged rudder, 1.5 m chord and a straight after edge")
heel, end = R["bumpkin"]["heel"], R["bumpkin"]["end"]
beam = next(b for b in L["beams"] if b[0] < heel[0] < b[1] and b[2] < heel[2] < b[3])
check(abs(end[0] - L["transomPlane"]["high"][0] + 2800) < 1 and beam[0] + 90 <= heel[0] <= beam[1] - 90,
      "107/128: bumpkin 2.8 m outboard; heel seated 100 mm into the first beam")
sheet = next(p for p in D["movingParts"] if p["code"] == "sheet-mizzen")
check(max(abs(a - b) for a, b in zip(sheet["anchor"], (end[0] / 1000, end[2] / 1000 - 2.875, -end[1] / 1000))) < 0.001,
      "128: mizzen sheet follows the new outer end")

pieces = D["modularPieces"]
check(any(p['kind']=='partition' and p['at']==[-9,1.125,-1] for p in pieces),
      '108: lower hearth partition supplied (runtime contact mesh checked by integrate)')
check(any(p['kind']=='partition' and p['at']==[-7,3.125,3] for p in pieces) and
      any(p['code']=='cabin-front-starboard-upper' and p['clip'] for p in D['borderPlaces']),
      '109: upper front partition and its exact narrow border supplied')
check(any(p["kind"] == "door" and p["at"] == [-7.0, 1.125, 1.0] for p in pieces) and
      any(p["kind"] == "window" and p["at"] == [-7.0, 2.125, 2.0] for p in pieces),
      "110: starboard cabin door and outboard window")

gates = [f for f in D["fixtures"] if f["type"] == "gate"]
check(len(gates) == 2 and all(abs(f["pos"][0] + 2.5) < 0.02 for f in gates), "116: entry gates abreast the main mast")
for f in gates:
    group, side = "part:" + f["code"], 1 if f["pos"][2] > 0 else -1
    tests = {"inside": [], "cap": [], "hinges": []}
    for i, q in enumerate(M["groups"][group]):
        t = M["textures"][q[0]]
        if "oak4++" in t and sum(q[8 + 8 * k] for k in range(4)) * side < -2.8:
            tests["inside"].append(zone_at(group, i) == "waist-bulwarks")
        if t == "game:block/wood/planks/oak3" and sum(q[7 + 8 * k] for k in range(4)) > 3.6:
            tests["cap"].append(zone_at(group, i) == "rail-caps")
        if "iron" in t:
            tests["hinges"].append(True)
    check(all(tests.values()) and all(tests["inside"]) and all(tests["cap"]), f"122/127: {f['code']} iron hinges; inside and cap paint separately")

guns = [f for f in D["fixtures"] if f["type"] == "gun" and f.get("kind") != "swivel"]
quarter = [f for f in guns if abs(f["pos"][1] - 4.125) < 0.01]
check(len(guns) == 10 and len(quarter) == 4 and sum(f["out"][2] < 0 for f in quarter) == 2 and
      all(f.get("port") and f.get("lid") and f.get("housed") for f in guns), "121/133: six waist and four quarterdeck carriage guns, all with ports and lids")
check(all(any(f["code"] == f"gun-{s}-{n}" and f["pos"][0] == x for f in guns)
          for s in ("port", "starboard") for n, x in ((1, 1.5), (2, 3.5), (3, -0.5))), "121: old waist gun identities preserved; new gun at the old gate")
swivels = [f for f in D["fixtures"] if f.get("kind") == "swivel"]
check(len(swivels) == 2 and all(f["pos"][0] >= 7.0 and f["trainLimits"] == [-100, 100] and f["elevationLimits"] == [-25, 40] and len(f["rail"]) == 2 for f in swivels),
      "133: two bow swivel sockets, ±100° train, -25..40° elevation and measured rails")

clad = [q for g, qs in M["groups"].items() if g.endswith("bottomclad") for q in qs]
check(clad and abs(max(p[1] for q in clad for p in points(q)) - 0.125) < 0.0002,
      "125: copper terminates on one straight horizontal line, y = 0.125")
check(len({M['textures'][q[0]] for q in clad}) == 5, "126: Magelan's five copper sheet textures")
floor_density = []
for q in clad:
    p = points(q)
    if not all(abs(v[1] - D["keelY"]) < 0.001 for v in p):
        continue
    area = abs(sum(a[0] * b[2] - b[0] * a[2] for a, b in zip(p, p[1:] + p[:1]))) / 2
    uv = [(q[4 + k * 8], q[5 + k * 8]) for k in range(4)]
    tex_area = abs(sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(uv, uv[1:] + uv[:1]))) / 2 / 256
    if area > 0.001:
        floor_density.append(tex_area / area)
check(floor_density and all(0.98 < r < 1.02 for r in floor_density), "126: flat-floor copper UV area equals its physical area")
def visible(q):
    a, b, c, d = np.asarray(points(q))
    return np.linalg.norm(np.cross(b - a, c - a)) + np.linalg.norm(np.cross(c - a, d - a)) > 1e-8


check(all(-0.002 <= q[4 + k * 8] <= 16.002 and -0.002 <= q[5 + k * 8] <= 16.002
          for g, qs in M["groups"].items() if g != "shot" and not g.startswith("sail:") for q in qs if visible(q) for k in range(4)),
      "all visible solid faces tiled within the atlas; no long face clamped")
inside_transom = [(g, i, q) for g, qs in M["groups"].items() for i, q in enumerate(qs)
                  if all(abs(p[0] + 15.3) < 0.04 and 1.125 <= p[1] <= 3.925 for p in points(q)) and "oak4++" in M["textures"][q[0]]]
check(inside_transom and all(zone_at(g, i) == "steerage-walls" for g, i, q in inside_transom),
      "124: transom inside uses the steerage wall zone")

check(all(s[1] > 2.4 and abs(s[2]) > 3.0 for s in D["rigCheck"]["standing"] if s[0] > -6),
      "120: main shrouds end above deadeyes outside the hull")
for side in (-1, 1):
    ps = [p for g, qs in M["groups"].items() if not g.startswith("part:") for q in qs
          if M["textures"][q[0]] == "game:block/wood/debarked/oak"
          and all(abs(v[2] - side * 0.5) < 0.001 and 0.524 <= v[1] <= 0.926 and -7.401 <= v[0] <= -3.599 for v in points(q))
          for p in points(q)]
    check(ps and abs(min(p[0] for p in ps) + 7.4) < 0.001 and abs(max(p[0] for p in ps) + 3.6) < 0.001,
          f"112/114: default hatch carling on side {side} ends on the full beams")

print(f"{failed} failures")
raise SystemExit(1 if failed else 0)
