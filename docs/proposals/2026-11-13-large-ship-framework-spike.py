"""THROWAWAY SPIKE for docs/proposals/2026-11-13-large-ship-framework.md, section 1.6 (backlog SH-0014).

Not part of any pipeline, not used by the mod or its tests. It shows one thing: the caravel's hull planking can be
rebuilt and cut into block cells from the committed lines file alone, in numpy, without AstoCAD. It does not cut the
gun ports, gates, backbone, transom or counter, and makes no ribs, beams or decks.

    python docs/proposals/2026-11-13-large-ship-framework-spike.py [repo root]

It reads models/caravel/caravel-lines.json, caravel-report.json and cells.json, and writes nothing.
"""
import collections
import json
import os
import sys
import time

import numpy as np

t0 = time.time()
root = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
model = os.path.join(root, "models", "caravel")
L = json.load(open(os.path.join(model, "caravel-lines.json")))
R = json.load(open(os.path.join(model, "caravel-report.json")))
C = json.load(open(os.path.join(model, "cells.json")))

H = 12.5                                                  # node spacing, mm
xs = np.arange(-16500.0, 10500.0, H) + H / 2              # node centres along the ship
zs = np.arange(0.0, 8000.0, H) + H / 2                    # and up from the bottom cell border
NODE = (H * 1e-3) ** 2                                    # a node's area, m2; times a breadth in mm it gives litres


def field(lines):
    """w[k, i]: the port half-breadth (mm) at (zs[k], xs[i]); linear along each waterline's polygon and linear in
    height between two waterlines, as the CAD build's ruled loft is. 0 where she isn't."""
    zz = np.array([l["z"] for l in lines])
    rows = []
    for l in lines:
        p = np.array(l["pts"])
        o = np.argsort(p[:, 0], kind="stable")
        x, y = p[o, 0], p[o, 1]
        rows.append(np.where((xs >= x[0]) & (xs <= x[-1]), np.interp(xs, x, y), 0.0))
    w = np.zeros((len(zs), len(xs)))
    for k, z in enumerate(zs):
        if zz[0] <= z <= zz[-1]:
            j = min(np.searchsorted(zz, z, side="right") - 1, len(zz) - 2)
            t = (z - zz[j]) / (zz[j + 1] - zz[j])
            a, b = rows[j], rows[j + 1]
            w[k] = np.where((a > 0) & (b > 0), a + t * (b - a), 0.0)
    return w


sheer = np.array(L["sheer"])
under_sheer = zs[:, None] <= np.interp(xs, sheer[:, 0], sheer[:, 1])[None, :]
w_out = field(L["hull"]) * under_sheer                     # her outside
w_in = np.minimum(field(L["inner"]) * under_sheer, w_out)  # the planking's inside (the lines script's erosion)
t_field = time.time() - t0

print("fields: %d x %d nodes at %.1f mm in %.2f s" % (len(xs), len(zs), H, t_field))
print("displacement below the waterline: numpy %.2f m3, CAD %.1f m3" %
      (2 * w_out[zs < L["levels"]["waterline"]].sum() * NODE / 1000, R["waterline"]["displacement_m3"]))
print("envelope (transom and counter not cut here): numpy %.2f m3, CAD %.2f m3" %
      (2 * w_out.sum() * NODE / 1000, R["envelope"]["volume_m3"]))
print("planking (ports, gates, backbone not cut here): numpy %.2f m3, CAD %.2f m3" %
      (2 * (w_out - w_in).sum() * NODE / 1000, R["shell"]["volume_m3"]))

# The planking in each block cell: borders on whole metres in X and Z, at +-500, +-1500 ... in Y.
t1 = time.time()
ci, ck = np.floor(xs / 1000.0).astype(int), np.floor(zs / 1000.0).astype(int)
mine = collections.defaultdict(float)
for j in range(-4, 5):
    lo, hi = (0.0 if j == 0 else (abs(j) - 0.5) * 1000.0), (abs(j) + 0.5) * 1000.0
    part = np.clip(np.minimum(w_out, hi) - np.maximum(w_in, lo), 0, None) * (2 if j == 0 else 1)
    for k in np.unique(ck):
        row = part[ck == k].sum(axis=0)
        for i in np.unique(ci):
            litres = row[ci == i].sum() * NODE
            if litres > 0.0005:
                mine[(int(i), j, int(k))] = litres
cad = {}
for c in C["cells"]:
    litres = sum(p["litres"] for p in c["pieces"] if p["body"] == "Shell")
    if litres:
        cad[tuple(c["cell"])] = litres
both = sorted(set(mine) & set(cad))
rel = np.array([abs(mine[c] - cad[c]) / max(cad[c], 1.0) for c in both])
print("planking by cell in %.2f s: numpy %d cells, CAD slice %d cells (after its sliver merges), in both %d" %
      (time.time() - t1, len(mine), len(cad), len(both)))
print("  within 2 %% of the CAD's volume: %d of %d; median difference %.2f litres" %
      ((rel < 0.02).sum(), len(both), np.median([abs(mine[c] - cad[c]) for c in both])))
extra = sorted(set(mine) - set(cad))
print("  cells only here: %d, %.1f litres in all (the CAD slice moved its slivers to neighbours)" %
      (len(extra), sum(mine[c] for c in extra)))
print("  pieces under 4 litres, known at once: %d" % sum(1 for c in mine if mine[c] < 4.0))
worst = sorted(both, key=lambda c: -abs(mine[c] - cad[c]))[:8]
print("  largest differences (cell, litres here, litres CAD):", [(c, round(mine[c], 1), round(cad[c], 1)) for c in worst])
print("total %.2f s" % (time.time() - t0))
