"""Slice the caravel master model into one-block cells (docs/master-model.md section 7). Run in AstoCAD:

    C:/AstoCAD/bin/AstoCADCmd.exe -c "exec(open(r'C:/VS mods/Hansa/tools/caravel/slice_caravel.py').read())"

Reads models/caravel/caravel.FCStd; every object whose SliceRole is "structure" is cut by the cell grid (X and Z on
whole metres, Y on +-500, +-1500 ...). A cell's pieces of all bodies form its one master block. A piece smaller than
SLIVER_M3 joins, once, the face neighbour that keeps a piece of the same body, largest shared face first (the user's
direction, departing from docs/master-model.md 7); one with no such neighbour stays and is flagged.
Writes models/caravel/cells.json (cells, their pieces' bodies and volumes, merges, checks) and
models/caravel/cells.brep (the pieces, for the exporter). Generic: nothing here knows it is a caravel.
"""
import json
import math
import os
import time

import FreeCAD as App
import Part

V = App.Vector
ROOT = os.environ.get("CARAVEL_ROOT", r"C:/VS mods/Hansa")     # (a worktree's own checkout: set CARAVEL_ROOT)
MODEL = os.path.join(ROOT, "models", "caravel", "caravel.FCStd")
OUT = os.path.join(ROOT, "models", "caravel")
CELL = 1000.0
SLIVER_M3 = 0.004          # 4 litres: a piece smaller than this joins a neighbour (a 0.2 x 0.2 x 0.1 m crumb)
T0 = time.time()


def log(*a):
    print(f"[{time.time() - T0:6.1f}s]", *a, flush=True)


doc = App.openDocument(MODEL)
bodies = {o.Name: o.Shape for o in doc.Objects if getattr(o, "SliceRole", "") == "structure"}
log("structure bodies:", {k: round(v.Volume / 1e9, 3) for k, v in bodies.items()})

BIG = 1e5


def y_cell(y):
    return int(math.floor(y / CELL + 0.5))


def slab_x(i):
    return Part.makeBox(CELL, BIG, BIG, V(i * CELL, -BIG / 2, -BIG / 2))


def slab_z(k):
    return Part.makeBox(BIG, BIG, CELL, V(-BIG / 2, -BIG / 2, k * CELL))


def slab_y(j):
    return Part.makeBox(BIG, CELL, BIG, V(-BIG / 2, (j - 0.5) * CELL, -BIG / 2))


def solids_of(shp):
    return [s for s in shp.Solids if s.Volume > 1.0]


# ---------------------------------------------------------------- cut: X slabs, then Z layers, then Y columns
pieces = {}            # (i, j, k) -> list of [body, solid]
for name, shp in bodies.items():
    bb = shp.BoundBox
    for i in range(int(math.floor(bb.XMin / CELL)), int(math.ceil(bb.XMax / CELL))):
        sx = shp.common(slab_x(i))
        if sx.Volume < 1.0:
            continue
        bx = sx.BoundBox
        for k in range(int(math.floor(bx.ZMin / CELL)), int(math.ceil(bx.ZMax / CELL))):
            sz = sx.common(slab_z(k))
            if sz.Volume < 1.0:
                continue
            bz = sz.BoundBox
            for j in range(y_cell(bz.YMin), y_cell(bz.YMax) + 1):
                sy = sz.common(slab_y(j))
                for so in solids_of(sy):
                    pieces.setdefault((i, j, k), []).append([name, so])
    log(name, "cut;", len(pieces), "cells so far")

# ---------------------------------------------------------------- slivers join a neighbour
# (the user's direction, a departure from master-model 7: models/caravel/caravel-design.md, "In the mod")
def touching_face(so, cell):
    """The cell faces a piece touches (by its bounding box flush with the face), largest first, as neighbour offsets."""
    i, j, k = cell
    b = so.BoundBox
    lo = (i * CELL, (j - 0.5) * CELL, k * CELL)
    hi = (lo[0] + CELL, lo[1] + CELL, lo[2] + CELL)
    cand = []
    ext = (b.XLength, b.YLength, b.ZLength)
    mins, maxs = (b.XMin, b.YMin, b.ZMin), (b.XMax, b.YMax, b.ZMax)
    for ax in range(3):
        other = [ext[a] for a in range(3) if a != ax]
        area = other[0] * other[1]
        if abs(mins[ax] - lo[ax]) < 1.0:
            d = [0, 0, 0]; d[ax] = -1; cand.append((area, tuple(d)))
        if abs(maxs[ax] - hi[ax]) < 1.0:
            d = [0, 0, 0]; d[ax] = 1; cand.append((area, tuple(d)))
    cand.sort(reverse=True)
    return [c[1] for c in cand]


def is_sliver(so):
    return so.Volume / 1e9 < SLIVER_M3


# Every move is decided on the pieces as cut, then made: a sliver moves at most once, into a face neighbour that keeps
# a piece of its body that is no sliver itself (moving while iterating let a moved sliver be judged again in its new
# cell and travel on, or come to rest diagonally away from where it lies).
whole = {(c, b) for c, p in pieces.items() for b, so in p if not is_sliver(so)}
moves = []                 # (from, index in its cell, to)
merges = []
for cell in sorted(pieces):
    for n, (body, so) in enumerate(pieces[cell]):
        if not is_sliver(so):
            continue
        target = next((nb for d in touching_face(so, cell)
                       for nb in [(cell[0] + d[0], cell[1] + d[1], cell[2] + d[2])] if (nb, body) in whole), None)
        merges.append({"from": list(cell), "to": list(target) if target else None, "body": body,
                       "litres": round(so.Volume / 1e6, 2)})
        if target:
            moves.append((cell, n, target))
moved = {(c, n) for c, n, _ in moves}
for c, n, t in moves:
    pieces[t].append(pieces[c][n])
pieces = {c: [q for n, q in enumerate(p) if (c, n) not in moved] for c, p in pieces.items()}
pieces = {c: p for c, p in pieces.items() if p}

# ---------------------------------------------------------------- checks and output
vol_cells = {}
for c, p in pieces.items():
    for body, so in p:
        vol_cells[body] = vol_cells.get(body, 0.0) + so.Volume
check = {b: {"body_m3": round(bodies[b].Volume / 1e9, 4), "pieces_m3": round(vol_cells.get(b, 0) / 1e9, 4)} for b in bodies}
cells = []
comp = []
for c in sorted(pieces):
    entry = {"cell": list(c), "pieces": []}
    for body, so in pieces[c]:
        entry["pieces"].append({"body": body, "litres": round(so.Volume / 1e6, 2), "index": len(comp)})
        comp.append(so)
    cells.append(entry)
small = [e for e in cells if sum(p["litres"] for p in e["pieces"]) < SLIVER_M3 * 1000]
summary = {"cells": len(cells), "pieces": len(comp), "slivers_merged": sum(1 for m in merges if m["to"]),
           "slivers_unplaced": [m for m in merges if not m["to"]], "cells_still_tiny": [e["cell"] for e in small],
           "volume_check": check, "sliver_m3": SLIVER_M3, "seconds": round(time.time() - T0, 1)}
with open(os.path.join(OUT, "cells.json"), "w") as f:
    json.dump({"summary": summary, "cells": cells, "merges": merges}, f, indent=0)
Part.makeCompound(comp).exportBrep(os.path.join(OUT, "cells.brep"))
log(json.dumps(summary)[:1500])
