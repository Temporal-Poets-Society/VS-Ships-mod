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
            if a is not None and covered(tri, n, (i, j, k), a, cell_solids):
                hidden += 1
                continue
            if body == "Shell":
                outward = (n[1] * (1 if cy >= 0 else -1) > 0.25) or n[2] < -0.5 or (n[0] < -0.6 and cz > 2000)
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
def add_shape(group, tex, shape, defl=6.0, cell_uv=True):
    m = mesh_of(shape, defl)
    pts_all, facets = m.Topology
    normals = [f.Normal for f in m.Facets]
    for fi, f in enumerate(facets):
        tri = [(pts_all[f[q]].x, pts_all[f[q]].y, pts_all[f[q]].z) for q in range(3)]
        nv = normals[fi]
        n = (nv.x, nv.y, nv.z)
        c = [sum(q[a] for q in tri) / 3 for a in range(3)]
        origin = (math.floor(c[0] / 1000) * 1000, (math.floor(c[1] / 1000 + 0.5) - 0.5) * 1000, math.floor(c[2] / 1000) * 1000)
        add_tri(group, tex, tri, [uv_box(q, n, origin) for q in tri], n)


for name in ("Railings", "Gates"):          # (21 October 2026) the stairs are the game's ship's stairs (pieces) now
    add_shape("exterior", BODY_TEX[name], OBJ[name].Shape)
for side, leaf in zip(("port", "starboard"), OBJ["GateLeaves"].Shape.SubShapes):
    add_shape("part:gate-" + side, T_DECK2, leaf, 4.0)
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

skin = {"textures": TEX, "groups": groups,
        "hullOut": {"exterior": [0, n_ext], "bottom": [0, n_bot]}, "hullIn": {"interior": [0, n_int]},
        "hullThickness": L["skin"] / 1000.0, "slotQuads": slot_pairs}
BASE = os.path.join(MOD, "export")
os.makedirs(BASE, exist_ok=True)
with open(os.path.join(BASE, "caravel-mesh-base.json"), "w") as f:
    json.dump(skin, f, separators=(",", ":"))
log("wrote export/caravel-mesh-base.json")

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
        port_box = [c + 0.075, r4(RAILS["bottom"][0][2] / 1000 + 0.04 - WL), c + 0.925, r4(RAILS["top"][0][2] / 1000 - 0.1 - WL)]
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
               "enclosed": False}],
    "lights": [],
    # each gun's working room from its carriage's front to behind its gun captain (the mod keeps the fixtures' own)
    "keepClear": gun_keep,
    "deckSlots": deck_slots,
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
