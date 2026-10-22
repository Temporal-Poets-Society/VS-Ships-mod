"""Export the caravel master model into the mod (first version, 18 October 2026). Run in AstoCAD after build_caravel.py
and slice_caravel.py:

    C:/AstoCAD/bin/AstoCADCmd.exe -c "exec(open(r'C:/VS mods/Hansa/tools/caravel/export_caravel.py').read())"

Writes, for ship code "caravel":
  assets/hansa/config/ships/caravel.json        the definition: voxel grid (collision), dimensions, sails, rudder...
  assets/hansa/config/shipmeshes/caravel.json   the mesh: quads by group, 16 texels a block, cut at cell borders
The entity, the charter (ship deed) and the lang lines are kept by hand (assets/hansa/entities/ship/caravel.json,
itemtypes/shipdeed.json, lang/en.json).

Frames: the model is in mm, X fore, Y port, Z up from the bottom cell border. The game's ship-local frame is in blocks,
x fore, y up, z starboard, y = 0 at the waterline: x = X/1000, y = Z/1000 - 2.875, z = -Y/1000.
"""
import json
import math
import os
import time
import zlib
import base64

import FreeCAD as App
import Part
import Mesh
import MeshPart

V = App.Vector
ROOT = r"C:/VS mods/Hansa"
MOD = os.path.join(ROOT, "models", "caravel")
ASSETS = os.path.join(ROOT, "assets", "hansa")
CODE = "caravel"
T0 = time.time()


def log(*a):
    print(f"[{time.time() - T0:6.1f}s]", *a, flush=True)


L = json.load(open(os.path.join(MOD, "caravel-lines.json")))
REP = json.load(open(os.path.join(MOD, "caravel-report.json")))
LV = L["levels"]
WL = LV["waterline"] / 1000.0
doc = App.openDocument(os.path.join(MOD, "caravel.FCStd"))
OBJ = {o.Name: o for o in doc.Objects}


def g(p):
    """Model mm -> game blocks."""
    return (p[0] / 1000.0, p[2] / 1000.0 - WL, -p[1] / 1000.0)


def gn(n):
    return (n[0], n[2], -n[1])


def r4(v):
    return round(v, 4)


# ---------------------------------------------------------------- textures
TEX = ["game:block/wood/planks/oak1++3~hansa:block/tar", "game:block/wood/planks/oak4++3~hansa:block/tar",
       "game:block/wood/planks/oak1", "game:block/wood/planks/oak3", "game:block/wood/debarked/oak",
       "game:block/wood/debarked/pine", "game:block/linen", "game:block/metal/sheet/copper2",
       "game:block/cloth/reedrope", "game:block/metal/plate/tinbronze", "game:block/metal/tarnished/iron"]
T_HULL, T_HULL2, T_DECK, T_DECK2, T_TIMBER, T_SPAR, T_SAIL, T_COPPER, T_ROPE, T_BRONZE, T_IRON = range(len(TEX))
BODY_TEX = {"Shell": T_HULL, "Decks": T_DECK, "Ribs": T_TIMBER, "Beams": T_TIMBER, "Backbone": T_TIMBER,
            "Railings": T_DECK2, "Stairs": T_DECK2, "Gates": T_ROPE}   # (the Gates body: the rope ladders)

groups = {}


def add_tri(group, tex, pts, uvs, n):
    """One triangle as a quad (its last corner doubled), winding reversed for the mirrored frame."""
    q = [tex]
    order = [0, 2, 1, 1]
    for k in order:
        x, y, z = g(pts[k])
        nx, ny, nz = gn(n)
        q += [r4(x), r4(y), r4(z), round(uvs[k][0], 3), round(uvs[k][1], 3), round(nx, 3), round(ny, 3), round(nz, 3)]
    groups.setdefault(group, []).append(q)


def uv_box(p, n, origin):
    """16 texels a block, local to the piece's cell; planks run along X on sides and decks, along Y on end faces."""
    ax = max(range(3), key=lambda a: abs(n[a]))
    lx, ly, lz = (p[0] - origin[0]) / 1000.0, (p[1] - origin[1]) / 1000.0, (p[2] - origin[2]) / 1000.0
    if ax == 2:
        return (lx * 16, ly * 16)
    if ax == 1:
        return (lx * 16, (1 - lz) * 16)
    return (ly * 16, (1 - lz) * 16)


def uv_grain(p, n, origin, grain):
    """(21 October 2026, chalk note 20: "texture directions on ribs are wrong") As uv_box, but the wood's grain (the
    texture's u) along the piece's own length: grain 0 X, 1 Y, 2 Z (model axes). A face across the grain (end grain)
    keeps uv_box's way."""
    ax = max(range(3), key=lambda a: abs(n[a]))
    l = [(p[a] - origin[a]) / 1000.0 for a in range(3)]
    if grain == ax:
        return uv_box(p, n, origin)
    other = [a for a in range(3) if a != ax and a != grain][0]
    return (l[grain] * 16, (1 - l[other]) * 16 if other == 2 else l[other] * 16)


def piece_grain(body, so):
    """Which way a piece's wood runs: the shell's and the decks' planks along the keel; a beam athwartships; a rib, the
    backbone and a carling along their own longest extent in the cell."""
    if body in ("Shell", "Decks"):
        return 0
    b = so.BoundBox
    ext = [b.XLength, b.YLength, b.ZLength]
    if body == "Ribs":
        return 1 if ext[1] >= ext[2] else 2
    if body == "Beams":
        return 0 if ext[0] > 1.5 * ext[1] else 1
    return max(range(3), key=lambda a: ext[a])


def border_axis(pts):
    """The axis (0 X, 1 Y, 2 Z) of the cell border plane a face lies in, or None."""
    for a, step, off in ((0, 1000.0, 0.0), (2, 1000.0, 0.0), (1, 1000.0, 500.0)):
        vals = [q[a] for q in pts]
        if max(vals) - min(vals) < 0.5:
            m = (vals[0] + off) / step
            if abs(m - round(m)) < 1e-3:
                return a
    return None


def covered(tri, n, cell, a, cell_solids):
    """Is a face on a cell border hidden by the neighbouring cell (master-model 7)? Only where that cell's pieces fill
    the space just across it: at its centre and near each corner, so a face only partly covered is drawn (its covered
    part lies inside the neighbour's solid, unseen)."""
    d = 1 if n[a] > 0 else -1
    nb = list(cell)
    nb[a] += d
    solids = cell_solids.get(tuple(nb))
    if not solids:
        return False
    c = [sum(q[k] for q in tri) / 3 for k in range(3)]
    for q in [c] + [[c[k] + 0.9 * (p[k] - c[k]) for k in range(3)] for p in tri]:
        q = list(q)
        q[a] += d * 1.0                                   # 1 mm across the border
        p = V(*q)
        if not any(so.BoundBox.isInside(p) and so.isInside(p, 0.1, False) for so in solids):
            return False
    return True


def mesh_of(shape, defl=8.0):
    m = MeshPart.meshFromShape(Shape=shape, LinearDeflection=defl, AngularDeflection=0.4, Relative=False)
    return m


# (outside her: from her lines, fast; the envelope's own inside test took over 20 minutes for every hull face)
import sys as _sys
_sys.path.insert(0, os.path.join(ROOT, "tools", "caravel"))
import numpy as _np
import caravel_lines as _CL
_SHEER = L["sheer"]


def outside_hull(x, y, z):
    """Is the point (model mm) outside her hull's envelope: beyond her half-breadth there, below her underside or above
    her sheer?"""
    if z < LV["underside"] or z > float(_np.interp(x, [q[0] for q in _SHEER], [q[1] for q in _SHEER])):
        return True
    zb = round(z / 1000.0, 2)                            # (waterlines are cached by height: 10 mm steps)
    return abs(y) / 1000.0 > float(_CL.half_breadth(_np.array([x / 1000.0]), zb)[0])

# ---------------------------------------------------------------- structure: the sliced cells
cells = json.load(open(os.path.join(MOD, "cells.json")))
pieces = Part.read(os.path.join(MOD, "cells.brep")).Solids
log("cells", cells["summary"]["cells"], "pieces", len(pieces))
cell_solids = {tuple(c["cell"]): [pieces[p["index"]] for p in c["pieces"]] for c in cells["cells"]}
# (21 October 2026, chalk notes 16 and 22) her deck cells as slots (docs/legacy/slots.md): every whole cell of the main
# deck's and the quarterdeck's planking, but the fixed ones (under a mast, the pump, the capstan, the wheel; the rows of
# the gun places, whose crews work across the deck; the gates' row); the hatchway's cells are slots that start empty
FIT = L["layout"]["fittings"]
DECK_Z = {"main": LV["mainDeck"], "quarterdeck": LV["quarterdeck"]}
fixed = set()
for m in L["layout"]["masts"].values():
    for d_ in DECK_Z:
        fixed.add((d_, m["cell"], 0))
for key in ("pump", "capstan", "wheel"):
    fixed.add((FIT[key]["deck"], FIT[key]["cell"][0], FIT[key]["cell"][1]))
for c_ in L["layout"]["guns"]:
    fixed |= {("main", c_, j) for j in range(-4, 5)}
for c_ in L["layout"]["quarterdeckGuns"]:
    fixed |= {("quarterdeck", c_, j) for j in range(-4, 5)}
fixed |= {("main", L["layout"]["gate"]["x"][0] // 1000, j) for j in range(-4, 5)}
HX = [int(v // 1000) for v in L["layout"]["hatch"]["x"]]
HY = [int(round(v / 1000 + 0.5)) for v in L["layout"]["hatch"]["y"]]          # Y cells: j spans (j - 0.5 .. j + 0.5) blocks
hatch_cells = {("main", i, j) for i in range(HX[0], HX[1]) for j in range(HY[0], HY[1])}


def deck_of(so):
    for d_, z in DECK_Z.items():
        if abs(so.BoundBox.ZMax - z) < 2 and abs(so.BoundBox.ZMin - (z - L["deckThickness"])) < 2:
            return d_
    return None


deck_piece = {}                      # (deck, i, j) -> the cell's planking piece
for c in cells["cells"]:
    for p in c["pieces"]:
        if p["body"] == "Decks":
            so = pieces[p["index"]]
            d_ = deck_of(so)
            if d_:
                deck_piece[(d_, c["cell"][0], c["cell"][1])] = so


def whole(so, i, j):
    b = so.BoundBox
    return (b.XMin <= i * 1000 + 1 and b.XMax >= (i + 1) * 1000 - 1 and b.YMin <= (j - 0.5) * 1000 + 1 and
            b.YMax >= (j + 0.5) * 1000 - 1 and abs(so.Volume - 1000 * 1000 * L["deckThickness"]) < 0.01 * 1e9 * 0.2)


slot_keys = sorted([k for k, so in deck_piece.items() if k not in fixed and whole(so, k[1], k[2])] + sorted(hatch_cells))
slot_of = {k: n for n, k in enumerate(slot_keys)}
deck_slots = []
for (d_, i, j) in slot_keys:
    top = DECK_Z[d_] / 1000.0 - WL
    z0 = -(j + 0.5)
    e = {"id": f"{d_}:{i}:{int(round(z0 - 0.5))}", "deck": d_,
         "box": [float(i), r4(top - L["deckThickness"] / 1000.0), z0, float(i + 1), r4(top), z0 + 1.0]}
    if (d_, i, j) in hatch_cells:
        e["start"] = "empty"
    deck_slots.append(e)
log("deck slots", len(deck_slots), "of", len(deck_piece), "deck cells;", len(hatch_cells), "start empty (the hatchway)")
slot_pairs = {}


def add_slot_tri(group, tex, pts, uvs, n, owner, facing):
    add_tri(group, tex, pts, uvs, n)
    slot_pairs.setdefault(group, []).extend([owner, facing])


def deck_lit(n, c):
    """A deck face is lit as outside unless it's an underside or under the quarterdeck (the steerage)."""
    if n[2] < -0.5:
        return False
    return not (c[0] < L["quarterdeckFront"] and c[2] < LV["quarterdeck"] - 150)


# ---------------------------------------------------------------- her hull's panels
# (22 October 2026, the user, the fourth time: "still cannot remove hull elements !!!") Her side's planking as hull
# panels (docs/legacy/slots.md §9, §11.1): one a cell of her side, a block along her by a block up, the shell's pieces in
# that cell on that side; its id hull:<p|s>:<station>:<band>:<a|f>, the band its cell row from the keel, the half which
# block of the frame space (frames every 2 from -12). Panels in the rows between her decks (the hold's 1 and 2, the
# steerage's and the waist's 4 and 5); fixed: her bottom row (the floor timbers, the keel), the decks' rows (the
# waterways), a cell reaching her centreline (the stem, the sternpost), the waist guns' ports, the gates, where the anchor
# hangs and its hawse; row 5 only under the quarterdeck's. A panel owns its shell's voxels not another body's (a rib
# crossing it stays when it's out).
PANEL_ROWS = (1, 2, 4, 5)
FR = {"x0": -12, "space": 2, "count": 10}
cell_pieces_ix = {tuple(c["cell"]): [p["index"] for p in c["pieces"]] for c in cells["cells"]}
shell_cells = {}
for c in cells["cells"]:
    for p in c["pieces"]:
        if p["body"] == "Shell":
            shell_cells.setdefault(tuple(c["cell"]), []).append(p["index"])


def side_of_j(j):
    """The game's side of a cell column: model +Y (j > 0) is port (-1), -Y starboard (+1); 0 the centreline's."""
    return -1 if j > 0 else 1 if j < 0 else 0


fixed_hull = set()                       # (side, i, row)
for gf_ in REP["guns"]:
    if abs(gf_["deck"] - LV["mainDeck"]) < 1:
        for k_ in (4, 5):
            fixed_hull.add((-1 if gf_["side"] == "port" else 1, gf_["cell"], k_))
for gx in range(int(math.floor(L["layout"]["gate"]["x"][0] / 1000)), int(math.ceil(L["layout"]["gate"]["x"][1] / 1000))):
    for s_ in (-1, 1):
        for k_ in (4, 5):
            fixed_hull.add((s_, gx, k_))
s_an = -1 if FIT["anchor"]["side"] == "port" else 1
for x_ in (FIT["hawse"]["x"], FIT["anchor"]["x"]):
    for i_ in range(int(math.floor(x_)) - 1, int(math.floor(x_)) + 2):
        for k_ in PANEL_ROWS:
            fixed_hull.add((s_an, i_, k_))
panel_cells = {}                          # (side, i, row) -> its cells
for (ci, cj, ck) in shell_cells:
    s_ = side_of_j(cj)
    if ck in PANEL_ROWS and s_ != 0 and FR["x0"] <= ci < FR["x0"] + FR["space"] * FR["count"] and (s_, ci, ck) not in fixed_hull:
        panel_cells.setdefault((s_, ci, ck), []).append((ci, cj, ck))
for key in list(panel_cells):
    s_, ci, ck = key
    if any((ci, 0, ck_) in shell_cells for ck_ in (ck,)) or \
            ck == 5 and not any((ci, cj, 6) in shell_cells and side_of_j(cj) == s_ for cj in range(-6, 7)):
        del panel_cells[key]
panel_of_cell = {c_: key for key, cl in panel_cells.items() for c_ in cl}
panel_of_piece = {ix: key for key, cl in panel_cells.items() for c_ in cl for ix in shell_cells[c_]}
log("hull panels: candidates", len(panel_cells))
hull_tris = []                            # (group, tex, tri, uvs, n, owner key or None, facing key or None)


def cover_pieces(tri, n, cell, a):
    """covered()'s samples just across a border face: the neighbouring cell's piece filling each (its index), or None
    if any isn't filled."""
    d = 1 if n[a] > 0 else -1
    nb = list(cell)
    nb[a] += d
    ixs = cell_pieces_ix.get(tuple(nb))
    if not ixs:
        return None
    c = [sum(q[k_] for q in tri) / 3 for k_ in range(3)]
    found = []
    for q in [c] + [[c[k_] + 0.9 * (p_[k_] - c[k_]) for k_ in range(3)] for p_ in tri]:
        q = list(q)
        q[a] += d * 1.0
        pt = V(*q)
        hit = next((ix for ix in ixs if pieces[ix].BoundBox.isInside(pt) and pieces[ix].isInside(pt, 0.1, False)), None)
        if hit is None:
            return None
        found.append(hit)
    return found


def in_room(cx, cz):
    """Is a point (model mm) in one of her rooms (the hold under the main deck, the steerage under the quarterdeck)?"""
    return cz < LV["mainDeck"] or (cx < L["quarterdeckFront"] and cz < LV["quarterdeck"] - 150)


def hull_tri(body, tri, uvs, n, c3, own, fac):
    """A triangle of her side's planking (or a fixed piece's face toward a panel) for the hull's slot groups."""
    cx, cy, cz = c3
    if own is not None and fac is None and body == "Shell":
        outward = outside_hull(cx + n[0] * 30.0, cy + n[1] * 30.0, cz + n[2] * 30.0)
        if outward and cz < LV["waterline"] + 50:
            hull_tris.append(("slot:hull:bottom", T_HULL, tri, uvs, n, own, fac))
            hull_tris.append(("slot:hull:bottomclad", T_COPPER, tri, uvs, n, own, fac))
        elif outward:
            hull_tris.append(("slot:hull", T_HULL, tri, uvs, n, own, fac))
        else:
            hull_tris.append(("slot:hull:interior" if in_room(cx, cz) else "slot:hull", T_HULL2, tri, uvs, n, own, fac))
        return
    tex = T_HULL2 if body == "Shell" else BODY_TEX[body]
    hull_tris.append(("slot:hull:interior" if in_room(cx, cz) else "slot:hull", tex, tri, uvs, n, own, fac))


hidden = 0
for c in cells["cells"]:
    i, j, k = c["cell"]
    origin = (i * 1000.0, (j - 0.5) * 1000.0, k * 1000.0)
    for p in c["pieces"]:
        so = pieces[p["index"]]
        body = p["body"]
        m = mesh_of(so)
        pts_all, facets = m.Topology
        normals = [f.Normal for f in m.Facets]           # (m.Facets builds the whole list on every access)
        grain = piece_grain(body, so)
        dk = deck_of(so) if body == "Decks" else None
        own = slot_of.get((dk, i, j)) if dk else None
        for fi, f in enumerate(facets):
            tri = [pts_all[f[0]], pts_all[f[1]], pts_all[f[2]]]
            tri = [(q.x, q.y, q.z) for q in tri]
            nv = normals[fi]
            n = (nv.x, nv.y, nv.z)
            a = border_axis(tri)
            cz = sum(q[2] for q in tri) / 3
            cy = sum(q[1] for q in tri) / 3
            cx = sum(q[0] for q in tri) / 3
            uvs = [uv_grain(q, n, origin, grain) for q in tri]
            if dk is not None:
                lit = deck_lit(n, (cx, cy, cz))
                sg = "slot:deck" if lit else "slot:deck:interior"
                nb = None
                if a in (0, 1):                   # a side of the cell's planking, on a border toward a neighbour
                    nb = (dk, i + (1 if n[0] > 0 else -1), j) if a == 0 else (dk, i, j + (1 if n[1] > 0 else -1))
                if nb is not None:
                    other = slot_of.get(nb)
                    if own is not None:
                        if other is not None:
                            add_slot_tri(sg, BODY_TEX[body], tri, uvs, n, own, other)
                        elif nb in deck_piece:
                            hidden += 1          # toward fixed planking: never seen while it's in
                        else:
                            add_slot_tri(sg, BODY_TEX[body], tri, uvs, n, own, -1)
                        continue
                    if other is not None:          # fixed planking's edge, seen while that cell is out
                        add_slot_tri(sg, BODY_TEX[body], tri, uvs, n, -1, other)
                        continue
                elif own is not None:
                    add_slot_tri(sg, BODY_TEX[body], tri, uvs, n, own, -1)
                    continue
            if body == "Beams" and n[2] > 0.9:
                # a beam's or a carling's top under a deck cell: seen only while that cell is out
                under = next((slot_of[(d_, i, j)] for d_, z in DECK_Z.items()
                              if (d_, i, j) in slot_of and abs(cz - (z - L["deckThickness"])) < 2), None)
                if under is not None:
                    add_slot_tri("slot:deck:interior", BODY_TEX[body], tri, uvs, n, -1, under)
                    continue
            P = panel_of_piece.get(p["index"]) if body == "Shell" else None
            nbP = None
            if a is not None:
                nbc = [i, j, k]
                nbc[a] += 1 if n[a] > 0 else -1
                nbP = panel_of_cell.get(tuple(nbc))
            if P is not None or nbP is not None:
                if a is not None:
                    cov = cover_pieces(tri, n, (i, j, k), a)
                    if cov is not None:
                        other = next((o for o in (panel_of_piece.get(ix) for ix in cov) if o is not None and o != P), None)
                        if other is not None:
                            # a panel's edge toward the panel next to it, or a fixed piece's face toward a panel: drawn
                            # while that one is out (and this one in)
                            hull_tri(body, tri, uvs, n, (cx, cy, cz), P, other)
                        else:
                            hidden += 1
                        continue
                if P is not None:
                    hull_tri(body, tri, uvs, n, (cx, cy, cz), P, None)
                    continue
            if a is not None and covered(tri, n, (i, j, k), a, cell_solids):
                hidden += 1
                continue
            if body == "Shell":
                # (22 October 2026, chalk note 40: "this block cannot be dyed") outside is where a point a little off the
                # face is out of her envelope: the bow's inside faces, facing aft, were taken for the transom's outside
                # (drawn tarred in the bottom's group, never painted)
                outward = outside_hull(cx + n[0] * 30.0, cy + n[1] * 30.0, cz + n[2] * 30.0)
                if outward and cz < LV["waterline"] + 50:
                    add_tri("bottom", T_HULL, tri, uvs, n)
                    add_tri("bottomclad", T_COPPER, tri, uvs, n)
                elif outward:
                    add_tri("exterior", T_HULL, tri, uvs, n)
                elif cz < LV["mainDeck"]:
                    add_tri("interior", T_HULL2, tri, uvs, n)
                else:
                    add_tri("exterior", T_HULL2, tri, uvs, n)
            else:
                inside = cz < LV["mainDeck"] - 200 or (cx < L["quarterdeckFront"] and cz < LV["quarterdeck"] - 150)
                if body == "Decks":
                    inside = not deck_lit(n, (cx, cy, cz))
                add_tri("interior" if inside else "exterior", BODY_TEX[body], tri, uvs, n)


# the hatchway's cells: no planking in the model (they start empty); their planking as a player lays it
def slot_box_quads(key, n_slot):
    d_, i, j = key
    z1 = DECK_Z[d_]
    z0 = z1 - L["deckThickness"]
    x0, x1, y0, y1 = i * 1000.0, (i + 1) * 1000.0, (j - 0.5) * 1000.0, (j + 0.5) * 1000.0
    origin = (x0, y0, (k_ := math.floor(z0 / 1000)) * 1000.0)
    faces = [((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], None),
             ((0, 0, -1), [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)], None),
             ((1, 0, 0), [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], (d_, i + 1, j)),
             ((-1, 0, 0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)], (d_, i - 1, j)),
             ((0, 1, 0), [(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)], (d_, i, j + 1)),
             ((0, -1, 0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (d_, i, j - 1))]
    for n, q, nb in faces:
        facing = -1
        if nb is not None:
            if nb in slot_of:
                facing = slot_of[nb]
            elif nb in deck_piece:
                continue
        sg = "slot:deck" if deck_lit(n, (x0 + 500, y0 + 500, z1 if n[2] >= 0 else z0)) else "slot:deck:interior"
        for tri in ([q[0], q[1], q[2]], [q[0], q[2], q[3]]):
            add_slot_tri(sg, T_DECK, tri, [uv_grain(t, n, origin, 0) for t in tri], n, n_slot, facing)


for key in sorted(hatch_cells):
    slot_box_quads(key, slot_of[key])
log("structure quads", {k: len(v) for k, v in groups.items()}, "border faces hidden by their neighbours", hidden)

# hull damage drawing (no hull slots yet): the shell's outer quads come first in exterior and bottom, its inner first
# in interior. Re-order so that holds.
def shell_first(group, tex_set):
    qs = groups.get(group, [])
    a = [q for q in qs if q[0] in tex_set]
    b = [q for q in qs if q[0] not in tex_set]
    groups[group] = a + b
    return len(a)


n_ext = shell_first("exterior", {T_HULL})
n_int = shell_first("interior", {T_HULL2})
n_bot = len(groups.get("bottom", []))

# ---------------------------------------------------------------- fittings drawn with the hull
def uv_along(tri, n):
    """16 texels a block, the texture's u (the grain) along the triangle's longest edge (a rail's, a cap's length:
    round the bow too), v across it in its plane."""
    e = max(((tri[a], tri[b]) for a, b in ((0, 1), (1, 2), (2, 0))),
            key=lambda ab: sum((ab[1][k] - ab[0][k]) ** 2 for k in range(3)))
    u = [e[1][k] - e[0][k] for k in range(3)]
    ln = math.sqrt(sum(c * c for c in u)) or 1.0
    u = [c / ln for c in u]
    v = [n[1] * u[2] - n[2] * u[1], n[2] * u[0] - n[0] * u[2], n[0] * u[1] - n[1] * u[0]]
    return [(sum(q[k] * u[k] for k in range(3)) / 1000.0 * 16 % 1024, sum(q[k] * v[k] for k in range(3)) / 1000.0 * 16 % 1024)
            for q in tri]


def add_shape(group, tex, shape, defl=6.0, cell_uv=True, along=False):
    m = mesh_of(shape, defl)
    pts_all, facets = m.Topology
    normals = [f.Normal for f in m.Facets]
    for fi, f in enumerate(facets):
        tri = [(pts_all[f[q]].x, pts_all[f[q]].y, pts_all[f[q]].z) for q in range(3)]
        nv = normals[fi]
        n = (nv.x, nv.y, nv.z)
        c = [sum(q[a] for q in tri) / 3 for a in range(3)]
        origin = (math.floor(c[0] / 1000) * 1000, (math.floor(c[1] / 1000 + 0.5) - 0.5) * 1000, math.floor(c[2] / 1000) * 1000)
        if along:
            uvs = uv_along(tri, n)
            # (one triangle's UVs from one offset: shift them near 0 so they stay small numbers)
            u0, v0 = math.floor(min(q[0] for q in uvs) / 16) * 16, math.floor(min(q[1] for q in uvs) / 16) * 16
            add_tri(group, tex, tri, [(q[0] - u0, q[1] - v0) for q in uvs], n)
        else:
            add_tri(group, tex, tri, [uv_box(q, n, origin) for q in tri], n)


def add_faces_grain(group, tex, shape, defl=6.0):
    """(22 October 2026, chalk notes 42, 44: "textures are complete mess", "railing textures all over the place") Each
    face of the shape meshed alone, the wood's grain (u) along that face's longest edge, from one origin for the whole
    ship, so a face's triangles and its neighbours along the same run share one continuous texture; v across it in its
    plane. 16 texels a block."""
    for face in shape.Faces:
        chords = []
        for e in face.Edges:
            vs = e.Vertexes
            if len(vs) == 2:
                d_ = vs[1].Point - vs[0].Point
                chords.append((d_.Length, (d_.x, d_.y, d_.z)))
        if not chords:
            continue
        ln_, ud = max(chords)
        ud = [c / ln_ for c in ud]
        big = max(range(3), key=lambda a_: abs(ud[a_]))
        if ud[big] < 0:
            ud = [-c for c in ud]
        m = mesh_of(face, defl)
        pts_all, facets = m.Topology
        normals = [f.Normal for f in m.Facets]
        tris = []
        for fi, f in enumerate(facets):
            tri = [(pts_all[f[q]].x, pts_all[f[q]].y, pts_all[f[q]].z) for q in range(3)]
            n = (normals[fi].x, normals[fi].y, normals[fi].z)
            dn = sum(ud[k] * n[k] for k in range(3))
            u = [ud[k] - dn * n[k] for k in range(3)]
            lu = math.sqrt(sum(c * c for c in u))
            if lu < 0.2:                                 # the face across its longest edge: an end, as uv_box
                tris.append((tri, n, [uv_box(q, n, (0.0, -500.0, 0.0)) for q in tri]))
                continue
            u = [c / lu for c in u]
            v = [n[1] * u[2] - n[2] * u[1], n[2] * u[0] - n[0] * u[2], n[0] * u[1] - n[1] * u[0]]
            tris.append((tri, n, [(sum(q[k] * u[k] for k in range(3)) / 1000.0 * 16,
                                   sum(q[k] * v[k] for k in range(3)) / 1000.0 * 16) for q in tri]))
        if not tris:
            continue
        # (the whole face shifted by whole tiles near 0: small numbers, its texture unbroken)
        u0 = math.floor(min(uv[0] for _, _, uvs in tris for uv in uvs) / 16) * 16
        v0 = math.floor(min(uv[1] for _, _, uvs in tris for uv in uvs) / 16) * 16
        for tri, n, uvs in tris:
            add_tri(group, tex, tri, [(q[0] - u0, q[1] - v0) for q in uvs], n)


for name in ("Railings", "Gates"):          # (21 October 2026) the stairs are the game's ship's stairs (pieces) now
    add_faces_grain("exterior", BODY_TEX[name], OBJ[name].Shape)
# (chalk note 41: "gate inside/outside not same as hull") the leaves' outer faces the hull's outside planking, their
# inner faces its inside, planks along the keel from the hull's own origin; their edges the deck's wood
for side, leaf in zip(("port", "starboard"), OBJ["GateLeaves"].Shape.SubShapes):
    s_ = 1.0 if side == "port" else -1.0               # (model Y is port)
    for face in leaf.Faces:
        m = mesh_of(face, 4.0)
        pts_all, facets = m.Topology
        normals = [f.Normal for f in m.Facets]
        for fi, f in enumerate(facets):
            tri = [(pts_all[f[q]].x, pts_all[f[q]].y, pts_all[f[q]].z) for q in range(3)]
            n = (normals[fi].x, normals[fi].y, normals[fi].z)
            tex = T_HULL if n[1] * s_ > 0.7 else T_HULL2 if n[1] * s_ < -0.7 else T_DECK2
            add_tri("part:gate-" + side, tex, tri, [uv_grain(q, n, (0.0, -500.0, 0.0), 0) for q in tri], n)
fit = OBJ["Fittings"].Shape.SubShapes            # rudder, tiller, mizzen step
add_shape("part:rudder", T_TIMBER, fit[0])
add_shape("part:tiller", T_TIMBER, fit[1])
add_shape("interior", T_TIMBER, fit[2])
rig = OBJ["Rig_Latina"].Shape.SubShapes            # main mast, main yard, main sail, mizzen mast, its yard, its sail, bumpkin
add_shape("exterior", T_SPAR, rig[0], 10.0)
add_shape("part:yard-main", T_SPAR, rig[1], 10.0)
add_shape("exterior", T_SPAR, rig[3], 10.0)
add_shape("part:yard-mizzen", T_SPAR, rig[4], 10.0)
add_shape("exterior", T_SPAR, rig[6], 10.0)

# the guns (20 October 2026): the model's Guns body, run out, four parts a gun (bed, outboard trucks, inboard trucks,
# barrel) in the report's order; named gun-<side>-<n>, aft to fore along each side. (22 October 2026) caravel_layout.py
# replaces these meshes and the guns' moving parts with the detailed gun and its lid (caravel_gun.py)
GF = REP["guns"]
GG = REP["gunGeometry"]
gun_subs = OBJ["Guns"].Shape.SubShapes
assert len(gun_subs) == 4 * len(GF), (len(gun_subs), len(GF))
for side in ("port", "starboard"):
    for n, k in enumerate(sorted((k for k, gf in enumerate(GF) if gf["side"] == side), key=lambda k: GF[k]["cell"]), 1):
        GF[k]["code"] = f"gun-{side}-{n}"
for k, gf in enumerate(GF):
    code = gf["code"]
    bed, fore, aft, barrel = gun_subs[4 * k: 4 * k + 4]
    add_shape("part:" + code, T_DECK2, bed, 4.0)
    add_shape(f"part:{code}-trucks-fore", T_TIMBER, fore, 4.0)
    add_shape(f"part:{code}-trucks-aft", T_TIMBER, aft, 4.0)
    add_shape(f"part:{code}-barrel", T_BRONZE, barrel, 4.0)


def add_sail(group, face):
    """A sail both ways round; its cloth stretched over the texture once (linen has no grain to show)."""
    m = mesh_of(face, 20.0)
    pts_all, facets = m.Topology
    bb = face.BoundBox
    for f in facets:
        tri = [(pts_all[f[q]].x, pts_all[f[q]].y, pts_all[f[q]].z) for q in range(3)]
        uvs = [((q[0] - bb.XMin) / max(bb.XLength, 1) * 16, (1 - (q[2] - bb.ZMin) / max(bb.ZLength, 1)) * 16) for q in tri]
        n = (0.0, 1.0, 0.0)
        add_tri(group, T_SAIL, tri, uvs, n)
        add_tri(group, T_SAIL, [tri[0], tri[2], tri[1]], [uvs[0], uvs[2], uvs[1]], (0.0, -1.0, 0.0))


# (21 October 2026) the sails are made by caravel_layout.py (bellied, one-sided as the carrack's: the flat sail drawn
# both ways round flickered)


def add_shot(group, r=0.065, sides=10, rings=8):
    """A cast iron ball at her origin (game frame; the carrack generator's gun.add_shot): the mod draws the balls in
    flight with this group, culled, so its corners turn counter-clockwise seen from outside, as the carrack's."""
    def p(k, s):
        phi = math.pi * (k / rings - 0.5)                  # -90 at the bottom .. 90 at the top
        lam = 2 * math.pi * s / sides
        return (r * math.cos(phi) * math.cos(lam), r * math.sin(phi), r * math.cos(phi) * math.sin(lam))
    for k in range(rings):
        for s in range(sides):
            c = [p(k, s), p(k, s + 1), p(k + 1, s + 1), p(k + 1, s)]
            uv = [(s / sides * 2 * math.pi * r * 16, k / rings * math.pi * r * 16),
                  ((s + 1) / sides * 2 * math.pi * r * 16, k / rings * math.pi * r * 16),
                  ((s + 1) / sides * 2 * math.pi * r * 16, (k + 1) / rings * math.pi * r * 16),
                  (s / sides * 2 * math.pi * r * 16, (k + 1) / rings * math.pi * r * 16)]
            mid = [sum(q[a] for q in c) / 4 for a in range(3)]
            a0, a1, a2 = (c[0], c[2], c[3]) if k == 0 else (c[0], c[1], c[2])     # (the bottom ring's c[0] = c[1])
            e1 = [a1[a] - a0[a] for a in range(3)]
            e2 = [a2[a] - a0[a] for a in range(3)]
            cr = (e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0])
            order = [0, 1, 2, 3] if sum(cr[a] * mid[a] for a in range(3)) > 0 else [3, 2, 1, 0]
            q = [T_IRON]
            for o in order:
                v = c[o]
                q += [r4(v[0]), r4(v[1]), r4(v[2]), round(uv[o][0], 3), round(uv[o][1], 3)] + [round(v[a] / r, 3) for a in range(3)]
            groups.setdefault(group, []).append(q)


add_shot("shot")
log("all quads", {k: len(v) for k, v in groups.items()})


# ---------------------------------------------------------------- the voxel grid: rays across the ship through each body
VOX = 0.25
SOLID_BODIES = ["Shell", "Backbone", "Ribs", "Beams", "Decks", "Railings"]
# (21 October 2026) each solid of a body on its own: two solids touching (a beam's end cut at the hatch on its carling)
# give one hit for their shared face, and the parity of the hits along a ray then marked the hatch's gap solid
meshes = {n: [(so.BoundBox, mesh_of(so, 10.0)) for so in OBJ[n].Shape.Solids] for n in SOLID_BODIES}
bbs = [OBJ[n].Shape.BoundBox for n in SOLID_BODIES]
xmin = min(b.XMin for b in bbs) / 1000.0
xmax = max(b.XMax for b in bbs) / 1000.0
ymin = min(b.ZMin for b in bbs) / 1000.0 - WL
ymax = max(b.ZMax for b in bbs) / 1000.0 - WL
ox = math.floor(xmin / VOX) * VOX
oy = -0.875 - math.ceil((-0.875 - ymin) / VOX) * VOX
oz = -3.5
nx = int(math.ceil((xmax - ox) / VOX))
ny = int(math.ceil((ymax - oy) / VOX))
nz = int(round(7.0 / VOX))
solid = bytearray(nx * ny * nz)


def mark(i, j, z_lo, z_hi):
    """Voxels of column (i, j) whose z range overlaps [z_lo, z_hi] (game z)."""
    k0 = max(0, int(math.floor((z_lo - oz) / VOX)))
    k1 = min(nz - 1, int(math.floor((z_hi - oz) / VOX - 1e-9)))
    for k in range(k0, k1 + 1):
        solid[(i * ny + j) * nz + k] = 1


rail = bytearray(nx * ny * nz)        # the railings' own voxels, filled down to what they stand on afterwards
shellv = bytearray(nx * ny * nz)      # (the hull panels') the shell's voxels, and every other body's
otherv = bytearray(nx * ny * nz)


def mark_in(arr, i, j, z_lo, z_hi):
    k0 = max(0, int(math.floor((z_lo - oz) / VOX)))
    k1 = min(nz - 1, int(math.floor((z_hi - oz) / VOX - 1e-9)))
    for k in range(k0, k1 + 1):
        arr[(i * ny + j) * nz + k] = 1


def mark_rail(i, j, z_lo, z_hi):
    k0 = max(0, int(math.floor((z_lo - oz) / VOX)))
    k1 = min(nz - 1, int(math.floor((z_hi - oz) / VOX - 1e-9)))
    for k in range(k0, k1 + 1):
        rail[(i * ny + j) * nz + k] = 1


rays = 0
for i in range(nx):
    for j in range(ny):
        for su in (0.25, 0.75):
            for sv in (0.25, 0.75):
                X = (ox + (i + su) * VOX) * 1000.0
                Z = (oy + (j + sv) * VOX + WL) * 1000.0
                for name, parts_ in meshes.items():
                    for b, m in parts_:
                        if X < b.XMin or X > b.XMax or Z < b.ZMin or Z > b.ZMax:
                            continue
                        hits = m.foraminate(V(X, 6000.0, Z), V(0, -1, 0))
                        ys = sorted({round((h[1] if not hasattr(h, "y") else h.y), 1) for h in hits.values()})
                        rays += 1
                        for a in range(0, len(ys) - 1, 2):
                            z0, z1 = -ys[a + 1] / 1000.0, -ys[a] / 1000.0      # game z = -Y: the interval's ends swap
                            (mark_rail if name == "Railings" else mark)(i, j, z0, z1)
                            if name != "Railings":
                                mark_in(shellv if name == "Shell" else otherv, i, j, z0, z1)
# (21 October 2026, chalk note 15) a railing is solid from its deck (or the bulwark it caps) up to its top rail: the
# mod takes anything with air under it near the deck for a stair's tread and keeps the cells round it clear, so a
# railing's bars and a cap rail's overhang left the quarterdeck no floor places, and nobody falls through a railing
filled = 0
SUPPORT = 6                           # voxels: a railing stands on something within 1.5 blocks under its top
for i in range(nx):
    for k in range(nz):
        top = next((j for j in range(ny - 1, -1, -1) if rail[(i * ny + j) * nz + k]), None)
        if top is None:
            continue
        for j in range(top + 1):
            if rail[(i * ny + j) * nz + k]:
                solid[(i * ny + j) * nz + k] = 1
        sup = next((j for j in range(top - 1, max(-1, top - SUPPORT - 1), -1)
                    if solid[(i * ny + j) * nz + k] and not rail[(i * ny + j) * nz + k]), None)
        if sup is None:
            continue                  # over the side (the cap rail's lip outside): only its own voxels
        for j in range(sup + 1, top):
            if not solid[(i * ny + j) * nz + k]:
                solid[(i * ny + j) * nz + k] = 1
                filled += 1
log("voxels: rays", rays, "solid", sum(solid), "of", nx * ny * nz, "; railings filled down", filled)

bits = bytearray((len(solid) + 7) // 8)
for n_, v in enumerate(solid):
    if v:
        bits[n_ >> 3] |= 1 << (n_ & 7)
solid_b64 = base64.b64encode(zlib.compress(bytes(bits), 9)).decode()

# ---------------------------------------------------------------- the hull panels settled
panel_vox = {}
for i in range(nx):
    xc_ = ox + (i + 0.5) * VOX
    for j in range(ny):
        row = int(math.floor(((oy + (j + 0.5) * VOX) + WL) * 1000.0 / 1000.0 + 1e-9))
        for k in range(nz):
            v = (i * ny + j) * nz + k
            if not (shellv[v] and solid[v]) or otherv[v] or rail[v]:
                continue
            zc_ = oz + (k + 0.5) * VOX
            key = (1 if zc_ > 0 else -1, int(math.floor(xc_)), row)
            if key in panel_cells:
                panel_vox.setdefault(key, []).append((i, j, k))


def gtri(tri):
    return [g(q) for q in tri]


info = {}
for grp, tex, tri, uvs, n, own, fac in hull_tris:
    if grp.endswith("bottomclad"):
        continue
    if own is not None and fac is None:
        e = info.setdefault(own, {"out": [], "in": [], "pts": [], "strips": 0})
        gt = gtri(tri)
        gn_ = gn(n)
        e["pts"] += gt
        (e["out"] if gn_[2] * own[0] > 0 else e["in"]).append((gt, gn_))
    elif own is None and fac is not None:
        info.setdefault(fac, {"out": [], "in": [], "pts": [], "strips": 0})["strips"] += 1
    if own is not None:
        info.setdefault(own, {"out": [], "in": [], "pts": [], "strips": 0})["pts"] += gtri(tri)


def tri_area(t):
    a_ = [t[1][q] - t[0][q] for q in range(3)]
    b_ = [t[2][q] - t[0][q] for q in range(3)]
    c_ = (a_[1] * b_[2] - a_[2] * b_[1], a_[2] * b_[0] - a_[0] * b_[2], a_[0] * b_[1] - a_[1] * b_[0])
    return 0.5 * math.sqrt(sum(v * v for v in c_))


def corners(tris):
    """A face's four corners (aft-low, fwd-low, fwd-high, aft-high) from its triangles (game coordinates)."""
    pts = [q for t, _ in tris for q in t]
    x0_, x1_ = min(q[0] for q in pts), max(q[0] for q in pts)
    aft = [q for q in pts if q[0] < x0_ + 0.02]
    fwd = [q for q in pts if q[0] > x1_ - 0.02]
    return [list(map(r4, min(aft, key=lambda q: q[1]))), list(map(r4, min(fwd, key=lambda q: q[1]))),
            list(map(r4, max(fwd, key=lambda q: q[1]))), list(map(r4, max(aft, key=lambda q: q[1])))]


def vox_boxes(vs):
    """A panel's voxels as boxes: runs along z, then runs of those of one height up."""
    cols = {}
    for (i, j, k) in sorted(vs):
        cols.setdefault((i, j), []).append(k)
    runs = []
    for (i, j), ks in cols.items():
        start = prev = ks[0]
        for k in ks[1:] + [None]:
            if k is not None and k == prev + 1:
                prev = k
                continue
            runs.append((i, j, start, prev))
            if k is not None:
                start = prev = k
    runs.sort()
    boxes = []
    for (i, j, k0, k1) in runs:
        if boxes and boxes[-1][0] == i and boxes[-1][4] == k0 and boxes[-1][5] == k1 and boxes[-1][3] == j - 1:
            boxes[-1][3] = j
            continue
        boxes.append([i, j, j, j, k0, k1])
    return [[r4(ox + i * VOX), r4(oy + j0 * VOX), r4(oz + k0 * VOX), r4(ox + (i + 1) * VOX), r4(oy + (j1 + 1) * VOX), r4(oz + (k1 + 1) * VOX)]
            for i, j0, _, j1, k0, k1 in boxes]


hull_slots, panel_index, why = [], {}, {}
for key in sorted(panel_cells, key=lambda t: (t[1], t[0], t[2])):
    s_, ci, ck = key
    e = info.get(key)
    vs = panel_vox.get(key, [])
    if e is None or not e["out"] or not e["in"]:
        why[key] = "no outside or ceiling"
        continue
    area = sum(tri_area(t) for t, _ in e["out"])
    nm = [sum(nn[q] * tri_area(t) for t, nn in e["out"]) for q in range(3)]
    ln_ = math.sqrt(sum(v * v for v in nm)) or 1.0
    nm = [v / ln_ for v in nm]
    if area <= 0.3 or nm[2] * s_ <= 0.2 or e["strips"] < 2:
        why[key] = f"area {area:.2f}, normal {nm[2] * s_:.2f}, {e['strips']} strips"
        continue
    pts = e["pts"]
    box = [min(q[0] for q in pts), min(q[1] for q in pts), min(q[2] for q in pts),
           max(q[0] for q in pts), max(q[1] for q in pts), max(q[2] for q in pts)]
    sol = vox_boxes([v for v in vs if box[0] <= ox + (v[0] + 0.5) * VOX <= box[3] and box[1] <= oy + (v[1] + 0.5) * VOX <= box[4]])
    if sum(round((b_[3] - b_[0]) * (b_[4] - b_[1]) * (b_[5] - b_[2]) / VOX ** 3) for b_ in sol) < 4:
        why[key] = f"{len(vs)} voxels of its own"
        continue
    for b_ in sol:
        box = [min(box[0], b_[0]), min(box[1], b_[1]), min(box[2], b_[2]), max(box[3], b_[3]), max(box[4], b_[4]), max(box[5], b_[5])]
    station = int(math.floor((ci + 0.5 - FR["x0"]) / FR["space"]))
    half = "a" if (ci - FR["x0"]) % FR["space"] == 0 else "f"
    out_c = corners(e["out"])
    ys = [q[1] for t, _ in e["out"] for q in t]
    panel_index[key] = len(hull_slots)
    hull_slots.append({
        "id": f"hull:{'s' if s_ > 0 else 'p'}:{station}:{ck}:{half}", "side": s_, "station": station, "band": ck, "half": half,
        "box": [r4(v) for v in box], "outer": out_c, "inner": corners(e["in"]),
        "normal": [round(v, 3) for v in nm], "area": round(area, 3), "sill": r4(min(ys)), "head": r4(max(ys)),
        "room": ("hold" if ck < 3 else "steerage" if ci + 1 <= L["quarterdeckFront"] / 1000 + 1e-6 else None),
        "solids": sol,
        # (a block of her side: one planks block, or 4 boards, lays it again, as carrack-m's)
        "material": {"block": "game:planks-oak-ud", "count": 1}})
log("hull panels:", len(hull_slots), "of", len(panel_cells), "candidates; fixed:", dict(list(why.items())[:12]))
# their triangles: a panel left fixed is drawn as her fixed planking (its faces always, the faces toward it gone)
n_hull = {}
for grp, tex, tri, uvs, n, own, fac in hull_tris:
    if fac is not None and fac not in panel_index:
        continue
    o_ = panel_index.get(own, -1) if own is not None else -1
    f_ = panel_index[fac] if fac is not None else -1
    if o_ < 0 and f_ < 0 and own is not None and fac is None:
        # (a fixed one's own faces: as the plain hull's)
        add_slot_tri(grp, tex, tri, uvs, n, -1, -1)
    else:
        add_slot_tri(grp, tex, tri, uvs, n, o_, f_)
    n_hull[grp] = n_hull.get(grp, 0) + 1
log("hull slot quads", n_hull)
for grp in ("slot:hull", "slot:hull:bottom", "slot:hull:bottomclad", "slot:hull:interior"):
    groups.setdefault(grp, [])
    slot_pairs.setdefault(grp, [])

skin = {"textures": TEX, "groups": groups, "slotQuads": slot_pairs}
BASE = os.path.join(MOD, "export")
os.makedirs(BASE, exist_ok=True)
with open(os.path.join(BASE, "caravel-mesh-base.json"), "w") as f:
    json.dump(skin, f, separators=(",", ":"))
log("wrote export/caravel-mesh-base.json")

# ---------------------------------------------------------------- the definition
def side_faces(shape, x, z):
    """A side's inner and outer faces out from the centreline (blocks, port half) where a line across at (x, z) mm
    meets the shape."""
    sec = shape.section(Part.LineSegment(V(x, 0, z), V(x, 9000, z)).toShape())
    ys = sorted(v.Point.y for v in sec.Vertexes)
    return (r4(ys[-2] / 1000), r4(ys[-1] / 1000)) if len(ys) >= 2 else (None, None)


gun_fixtures, gun_parts_def, gun_places, gun_keep = [], [], [], []
PORT = L["layout"]["port"]
RAILS = L["railings"]
for gf in GF:
    code, c, sgn = gf["code"], gf["cell"], 1 if gf["side"] == "port" else -1
    on_qd = gf["deck"] == LV["quarterdeck"]
    xc = c + 0.5
    dy = r4(gf["deck"] / 1000 - WL)
    ay = r4(gf["axisZ"] / 1000 - WL)

    def zz(mm):                       # |Y| mm on her side -> game z
        return r4(-sgn * mm / 1000)

    def zr(a, b):                     # a |Y| range -> game z0, z1
        return min(zz(a), zz(b)), max(zz(a), zz(b))

    rear, front, recoil = gf["rear_mm"], gf["front_mm"], gf["recoil_mm"] / 1000
    seat = rear - 500                 # the gun captain stands 0.5 behind the carriage (as the carrack's)
    bz0, bz1 = zr(rear, front)
    sz0, sz1 = zr(rear - 150, front)
    kz0, kz1 = zr(seat - 300, front)
    if on_qd:
        # over the side, between the railing's posts and under its top rail; its faces those of the bottom and top rails
        # (the bottom rail 80 high lying on the deck, the top rail 140 high centred 50 under the posts' tops: their faces
        # and 5 mm clear)
        port_box = [c + 0.075, r4(RAILS["bottom"][0][2] / 1000 + 0.085 - WL), c + 0.925, r4(RAILS["top"][0][2] / 1000 - 0.125 - WL)]
        wi0, wo0 = side_faces(OBJ["Railings"].Shape, xc * 1000, RAILS["bottom"][0][2])
        wi1, wo1 = side_faces(OBJ["Railings"].Shape, xc * 1000, RAILS["top"][0][2] - 50)
        bound = "the railing"
    else:
        port_box = [c + PORT["inset"] / 1000, r4(dy + PORT["sill"] / 1000), c + 1 - PORT["inset"] / 1000, r4(dy + PORT["head"] / 1000)]
        # (beside the port, where the side is whole)
        wi0, wo0 = side_faces(OBJ["Shell"].Shape, c * 1000 + 60, gf["deck"] + PORT["sill"])
        wi1, wo1 = side_faces(OBJ["Shell"].Shape, c * 1000 + 60, gf["deck"] + PORT["head"] - 20)
        bound = "its port"
    axis_b = [r4(float(sgn)), 0.0, 0.0]                  # the trunnions' line: out x up
    gun_fixtures.append({
        "type": "gun", "code": code, "pos": [xc, dy, zz(gf["trunnion_mm"])], "yaw": 90.0 if sgn > 0 else 270.0,
        "sel": [r4(xc - 0.55), dy, sz0, r4(xc + 0.55), r4(dy + 0.85), sz1],
        "box": [r4(xc - GG["carriage"][0] / 2000), dy, bz0, r4(xc + GG["carriage"][0] / 2000), r4(dy + 0.8), bz1],
        "muzzle": [xc, ay, zz(gf["muzzle_mm"])], "out": [0.0, 0.0, float(-sgn)],
        "elevation": 1.0, "material": "bronze", "bore": 0.14, "shot": 4.1,
        "trunnion": [xc, ay, zz(gf["trunnion_mm"])], "pivot": [xc, dy, zz(gf["pivot_mm"])], "seat": [xc, dy, zz(seat)],
        "trainLimits": gf["train"], "elevationLimits": gf["elevation"], "trainRate": 10.0, "elevationRate": 6.0,
        "port": port_box, "wall": [wi0, wi1, wo0, wo1],
        "trainBound": [bound, bound], "sweepElevations": sorted({gf["elevation"][0], gf["elevation"][1]} |
                                   {e for e in (-5.0, 0.0, 5.0, 10.0) if gf["elevation"][0] < e < gf["elevation"][1]})})
    gun_parts_def += [
        {"code": code + "-train", "kind": "rotate", "axisPoint": [xc, dy, zz(gf["pivot_mm"])], "axisDir": [0.0, float(-sgn), 0.0],
         "source": "train:" + code, "angle": 1.0},
        {"code": code, "kind": "slide", "axisPoint": [xc, dy, zz(gf["trunnion_mm"])], "axisDir": [0.0, 0.0, float(sgn)],
         "distance": r4(recoil), "source": "fire:" + code, "parent": code + "-train"},
        {"code": code + "-barrel", "kind": "rotate", "axisPoint": [xc, ay, zz(gf["trunnion_mm"])], "axisDir": axis_b,
         "source": "elevate:" + code, "angle": 1.0, "parent": code}] + [
        {"code": f"{code}-trucks-{w}", "kind": "rotate", "axisPoint": [xc, r4(dy + GG["truckRadius"] / 1000), zz(yc)],
         "axisDir": axis_b, "source": "fire:" + code, "angle": round(math.degrees(recoil / (GG["truckRadius"] / 1000)), 3), "parent": code}
        for w, yc in (("fore", front - GG["truckRadius"]), ("aft", rear + GG["truckRadius"]))]
    gun_places.append({"id": code, "gun": code})
    gun_keep.append({"what": "gun", "box": [r4(xc - 0.575), dy, kz0, r4(xc + 0.575), r4(dy + 1.0), kz1]})
log("guns", [(f["code"], f["pos"]) for f in gun_fixtures])

# (21 October 2026, chalk note 12) the gates: a leaf each (GateLeaves), hinged on its after edge, swinging in; solid
# while shut (its box)
gate_fixtures, gate_parts = [], []
for gl in REP["gateLeaves"]:
    sgn = 1 if gl["side"] == "port" else -1          # model +Y = port = game -z
    zc = r4(-gl["y"] / 1000)
    x0, x1 = r4(gl["x0"] / 1000), r4(gl["x1"] / 1000)
    y0, y1 = r4(gl["z0"] / 1000 - WL), r4(gl["z1"] / 1000 - WL)
    t = gl["t"] / 1000
    code = "gate-" + gl["side"]
    gate_fixtures.append({"type": "gate", "code": code, "pos": [r4((x0 + x1) / 2), y0, zc], "yaw": 0.0,
                          "sel": [x0, y0, r4(zc - 0.25), x1, y1, r4(zc + 0.25)],
                          "box": [x0, y0, r4(zc - t / 2 - 0.05), x1, y1, r4(zc + t / 2 + 0.05)], "normal": "z"})
    gate_parts.append({"code": code, "axisPoint": [x0, r4((y0 + y1) / 2), zc], "axisDir": [0.0, 1.0, 0.0],
                       "source": "open:" + code, "angle": -88.0 if sgn > 0 else 88.0})

rig_rec = REP["rigGeometry"]["latina"]


def yard(name):
    return next(y for y in rig_rec["yards"] if y["name"] == name)


def sail_area(name):
    s = next(s for s in rig_rec["sails"] if s["name"] == name)
    p = [V(*q) for q in s["pts"]]
    return round(abs((p[1] - p[0]).cross(p[2] - p[0]).Length) / 2e6, 1)


def axis(yd):
    t, pk = yd["tack"], yd["peak"]
    a = g((t[0], 0, t[1]))
    b = g((pk[0], 0, pk[1]))
    d = [b[q] - a[q] for q in range(3)]
    ln = math.sqrt(sum(c * c for c in d))
    mid = [(a[q] + b[q]) / 2 for q in range(3)]
    return [r4(c) for c in mid], [r4(c / ln) for c in d]


ym, yz = yard("latina main"), yard("latina mizzen")
am, dm = axis(ym)
az, dz = axis(yz)
masts = {m["name"]: m for m in rig_rec["masts"]}
post_lo = L["transomPlane"]["low"]
post_hi = L["transomPlane"]["high"]
dxr, dzr = post_hi[0] - post_lo[0], post_hi[1] - post_lo[1]      # the sternpost's line, up (model X, Z)
rl = math.hypot(dxr, dzr)
rud_dir = [r4(dxr / rl), r4(dzr / rl), 0.0]                       # game (x, y, z): x along X, y along Z
rud_pt = [r4(c) for c in g((post_lo[0], 0, post_lo[1]))]
deck_y = LV["mainDeck"] / 1000.0 - WL
qd_y = LV["quarterdeck"] / 1000.0 - WL
hold_y = LV["hold"] / 1000.0 - WL
keel_y = LV["underside"] / 1000.0 - WL
stairs = L["layout"]["stairs"]


def stair_ladder(t):
    fx, fz = t["foot"]
    hx, hz = t["head"]
    return {"code": "stair-" + t["name"], "kind": "ladder",
            "bottom": [r4(fx / 1000 - 0.1), r4(fz / 1000 - WL), 0.0], "top": [r4(hx / 1000), r4(hz / 1000 - WL), 0.0],
            "exitBottom": [r4(fx / 1000 + 0.5), r4(fz / 1000 - WL), 0.0], "exitTop": [r4(hx / 1000 - 0.5), r4(hz / 1000 - WL), 0.0]}


def half_breadth(x, z):
    sec = OBJ["Envelope"].Shape.section(Part.LineSegment(V(x, 0, z), V(x, 9000, z)).toShape())
    ys = [v.Point.y for v in sec.Vertexes]
    return max(ys) / 1000.0 if ys else None


samples = []
for x in range(-10, 6):
    samples.append([float(x) + 0.0, r4(keel_y + 0.02), 0.0])
for x in range(-9, 7, 2):
    hb = half_breadth(x * 1000.0, 600.0)
    if hb:
        samples += [[float(x), r4(0.6 - WL), r4(-hb + 0.02)], [float(x), r4(0.6 - WL), r4(hb - 0.02)]]
gx = L["layout"]["gate"]["x"]
gate_x = (gx[0] + gx[1]) / 2000.0
gate_hb = half_breadth(gate_x * 1000, LV["mainDeck"] + 300) or 3.2
definition = {
    "code": CODE, "name": "Caravel",
    "voxelSize": VOX, "gridOrigin": [r4(ox), r4(oy), r4(oz)], "gridSize": [nx, ny, nz], "solid": solid_b64,
    "shapes": {"skin": f"hansa:config/shipmeshes/{CODE}.json"},
    "length": round(REP["lengthOverall_mm"] / 1000, 2), "beam": round(REP["beam_mm"] / 1000, 2),
    "draft": round(REP["draft_mm"] / 1000, 3), "waterline": 0.0, "keelY": r4(keel_y), "deckY": r4(deck_y),
    "hullHalfWidthAtDeck": r4(half_breadth(0, LV["mainDeck"]) or 3.27),
    "bounds": [[r4(ox), r4(oy), r4(oz)], [r4(ox + nx * VOX), r4(max(masts["main"]["top"], ym["peak"][1]) / 1000 - WL), r4(-oz)]],
    "maxSpeed": 5.6, "acceleration": 0.45, "deceleration": 0.6, "turnRate": 7.0, "rudderSpeed": 60.0, "reverseSpeed": 0.6,
    "hullSamples": L["hullSamples"],
    "waterClip": {"centerX": -1.2, "halfLength": 10.6, "halfWidth": 3.4, "height": 0.1},
    # (the centreline's boarding point at x 2.5 is gone: it kept a cell of the waist clear, chalk note 15)
    "boardPoints": [[r4(gate_x), r4(deck_y), r4(gate_hb - 0.6)], [r4(gate_x), r4(deck_y), r4(-gate_hb + 0.6)]],
    # the cargo hold: about a quarter of the carrack's (3708 slots), as its volume; the guns; the gates. The helm, the
    # pump, the capstan, the spawn plate and the plaque are caravel_layout.py's
    "fixtures": [{"type": "storage", "code": "hold", "pos": [0.0, r4(hold_y), 0.0], "yaw": 0.0, "slots": 900}] + gun_fixtures + gate_fixtures,
    "ladders": [
        {"code": f"sideladder-{side}", "kind": "rope", "outside": True,
         "bottom": [r4(gate_x), -0.6, r4(sgn * (gate_hb + 0.15))], "top": [r4(gate_x), r4(deck_y - 0.2), r4(sgn * (gate_hb + 0.1))],
         "exitBottom": [r4(gate_x), -1.0, r4(sgn * (gate_hb + 0.9))], "exitTop": [r4(gate_x), r4(deck_y), r4(sgn * (gate_hb - 0.7))]}
        for side, sgn in (("starboard", 1), ("port", -1))],
    # the hold under the main deck's beams (enclosed); the steerage under the quarterdeck, open at its front
    "rooms": [{"code": "hold", "min": [r4(ox), r4(hold_y), r4(oz)], "max": [10.0, r4(deck_y - 0.6), r4(-oz)], "enclosed": True},
              {"code": "steerage", "min": [r4(ox), r4(deck_y), r4(oz)], "max": [L["quarterdeckFront"] / 1000, r4(qd_y - 0.6), r4(-oz)],
               # (22 October 2026, chalk note 38) a room its front's partitions close: sealed only while pieces close it
               "enclosed": True, "pieces": True}],
    "lights": [],
    # each gun's working room from its carriage's front to behind its gun captain (the mod keeps the fixtures' own)
    "keepClear": gun_keep,
    "deckSlots": deck_slots,
    "hullSlots": hull_slots,
    # every gun on a place of its own, laid on a new ship (no deck slots yet: the places list no cells)
    "gunPlaces": gun_places,
    "movingParts": [
        {"code": "rudder", "axisPoint": rud_pt, "axisDir": rud_dir, "source": "rudder", "angle": 30.0},
        {"code": "tiller", "axisPoint": rud_pt, "axisDir": rud_dir, "source": "rudder", "angle": 30.0, "interior": True},
        {"code": "yard-main", "axisPoint": [masts["main"]["x"] / 1000, r4(ym["cross"][1] / 1000 - WL), 0.0], "axisDir": [0.0, 1.0, 0.0],
         "source": "sheet", "angle": 57.29578, "limit": 25.0},
        {"code": "yard-mizzen", "axisPoint": [masts["mizzen"]["x"] / 1000, r4(yz["cross"][1] / 1000 - WL), 0.0], "axisDir": [0.0, 1.0, 0.0],
         "source": "sheet", "angle": 57.29578, "limit": 25.0}] + gun_parts_def + gate_parts,
    "masts": [{"code": "main", "x": masts["main"]["x"] / 1000, "z": 0.0, "radius": 0.3,
               "y0": r4(masts["main"]["heel"] / 1000 - WL), "y1": r4(masts["main"]["top"] / 1000 - WL)},
              {"code": "mizzen", "x": masts["mizzen"]["x"] / 1000, "z": 0.0, "radius": 0.22,
               "y0": r4(masts["mizzen"]["heel"] / 1000 - WL), "y1": r4(masts["mizzen"]["top"] / 1000 - WL)}],
    "decks": [{"code": "hold", "y": r4(hold_y), "x0": -30.0, "x1": 40.0}, {"code": "main", "y": r4(deck_y), "x0": -30.0, "x1": 40.0},
              {"code": "quarterdeck", "y": r4(qd_y), "x0": -30.0, "x1": L["quarterdeckFront"] / 1000}],
    "frames": {"x0": -12.0, "space": 2.0, "count": 10},
    "grid": {"origin": [0.0, 0.125, 0.5]},
    "motion": {"rollPeriod": 4.5},
    "sailing": {"maxBrace": 36.0, "noGo": 60.0, "maxSheet": 25.0},
    "damage": {"hullToughness": 70.0, "deckToughness": 55.0, "rudderToughness": 50.0, "mastToughness": 300.0,
               "sailToughness": 25.0, "riggingToughness": 70.0,
               "rudder": [r4(rud_pt[0] - 1.6), r4(keel_y - 0.05), -0.15, r4(rud_pt[0] + 0.1), r4(deck_y + 0.5), 0.15], "pumpersPerPump": 2},
}
with open(os.path.join(BASE, "caravel-def-base.json"), "w") as f:
    json.dump(definition, f, indent=1)
log("wrote export/caravel-def-base.json; the layout step")
# (21 October 2026) the fittings, rigging, sails and gameplay data on top (plain Python, rerun alone in seconds)
import importlib.util
_spec = importlib.util.spec_from_file_location("caravel_layout", os.path.join(ROOT, "tools", "caravel", "caravel_layout.py"))
_lay = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_lay)
_lay.main()
log("done")
