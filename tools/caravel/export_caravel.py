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
            "Railings": T_DECK2, "Stairs": T_DECK2, "Gates": T_DECK2}

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
        for fi, f in enumerate(facets):
            tri = [pts_all[f[0]], pts_all[f[1]], pts_all[f[2]]]
            tri = [(q.x, q.y, q.z) for q in tri]
            nv = normals[fi]
            n = (nv.x, nv.y, nv.z)
            a = border_axis(tri)
            if a is not None and covered(tri, n, (i, j, k), a, cell_solids):
                hidden += 1
                continue
            cz = sum(q[2] for q in tri) / 3
            cy = sum(q[1] for q in tri) / 3
            uvs = [uv_box(q, n, origin) for q in tri]
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
                grp = "interior" if cz < LV["mainDeck"] - 200 else "exterior"
                add_tri(grp, BODY_TEX[body], tri, uvs, n)
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


for name in ("Railings", "Stairs", "Gates"):
    add_shape("exterior", BODY_TEX[name], OBJ[name].Shape)
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
# barrel) in the report's order; named gun-<side>-<n>, aft to fore along each side
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


add_sail("sail:main", rig[2])
add_sail("sail:mizzen", rig[5])


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
        "hullThickness": L["skin"] / 1000.0}
with open(os.path.join(ASSETS, "config", "shipmeshes", f"{CODE}.json"), "w") as f:
    json.dump(skin, f, separators=(",", ":"))
log("wrote shipmeshes/caravel.json")

# ---------------------------------------------------------------- the voxel grid: rays across the ship through each body
VOX = 0.25
SOLID_BODIES = ["Shell", "Backbone", "Ribs", "Beams", "Decks", "Railings"]
meshes = {n: mesh_of(OBJ[n].Shape, 10.0) for n in SOLID_BODIES}
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


rays = 0
for i in range(nx):
    for j in range(ny):
        for su in (0.25, 0.75):
            for sv in (0.25, 0.75):
                X = (ox + (i + su) * VOX) * 1000.0
                Z = (oy + (j + sv) * VOX + WL) * 1000.0
                for name, m in meshes.items():
                    b = OBJ[name].Shape.BoundBox
                    if X < b.XMin or X > b.XMax or Z < b.ZMin or Z > b.ZMax:
                        continue
                    hits = m.foraminate(V(X, 6000.0, Z), V(0, -1, 0))
                    ys = sorted({round((h[1] if not hasattr(h, "y") else h.y), 1) for h in hits.values()})
                    rays += 1
                    for a in range(0, len(ys) - 1, 2):
                        z0, z1 = -ys[a + 1] / 1000.0, -ys[a] / 1000.0      # game z = -Y: the interval's ends swap
                        mark(i, j, z0, z1)
log("voxels: rays", rays, "solid", sum(solid), "of", nx * ny * nz)

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
    "boardPoints": [[r4(gate_x), r4(deck_y), r4(gate_hb - 0.6)], [r4(gate_x), r4(deck_y), r4(-gate_hb + 0.6)], [2.5, r4(deck_y), 0.0]],
    "fixtures": [{"type": "helm", "code": "helm", "pos": [-8.9, r4(deck_y), 0.0], "yaw": 0.0,
                  "sel": [-9.4, r4(deck_y), -0.6, -8.4, r4(deck_y + 1.2), 0.6]},
                 # the cargo hold: about a quarter of the carrack's (3708 slots), as its volume
                 {"type": "storage", "code": "hold", "pos": [0.0, r4(hold_y), 0.0], "yaw": 0.0, "slots": 900},
                 # the pump on the centreline forward of the main mast (its brake not drawn yet); the spawn plate and the
                 # plaque on the mizzen in the steerage, by the helm
                 {"type": "pump", "code": "pump", "pos": [0.5, r4(deck_y), 0.0], "yaw": 0.0,
                  "sel": [0.15, r4(deck_y), -0.3, 0.85, r4(deck_y + 1.5), 0.3], "spout": [0.5, r4(deck_y + 0.65), 0.45],
                  "spoutDir": [0.0, -0.2588, 0.9659]},
                 {"type": "attach", "code": "plate", "pos": [-8.5, r4(deck_y + 1.45), -0.27], "yaw": 90.0,
                  "sel": [-8.85, r4(deck_y + 1.1), -0.52, -8.15, r4(deck_y + 1.8), -0.25], "kind": "plate", "spawn": [-8.5, r4(deck_y), -1.0]},
                 {"type": "plaque", "code": "plaque", "pos": [-8.27, r4(deck_y + 1.5), 0.0], "yaw": 180.0,
                  "sel": [-8.39, r4(deck_y + 1.27), -0.3, -8.25, r4(deck_y + 1.73), 0.3]}] + gun_fixtures,
    "ladders": [stair_ladder(t) for t in stairs] + [
        {"code": f"sideladder-{side}", "kind": "rope", "outside": True,
         "bottom": [r4(gate_x), -0.6, r4(sgn * (gate_hb + 0.15))], "top": [r4(gate_x), r4(deck_y - 0.2), r4(sgn * (gate_hb + 0.1))],
         "exitBottom": [r4(gate_x), -1.0, r4(sgn * (gate_hb + 0.9))], "exitTop": [r4(gate_x), r4(deck_y), r4(sgn * (gate_hb - 0.7))]}
        for side, sgn in (("starboard", 1), ("port", -1))],
    # the hold under the main deck's beams (enclosed); the steerage under the quarterdeck, open at its front
    "rooms": [{"code": "hold", "min": [r4(ox), r4(hold_y), r4(oz)], "max": [10.0, r4(deck_y - 0.6), r4(-oz)], "enclosed": True},
              {"code": "steerage", "min": [r4(ox), r4(deck_y), r4(oz)], "max": [L["quarterdeckFront"] / 1000, r4(qd_y - 0.6), r4(-oz)],
               "enclosed": False}],
    "lights": [],
    # what a player's build must leave clear: the pump's brake, the helm, the hatch and its stair, the spawn place, the
    # plaque, and each gun's working room from its carriage's front to behind its gun captain
    "keepClear": [{"what": "pump", "box": [-0.2, r4(deck_y), -0.6, 1.2, r4(deck_y + 2.0), 0.6]},
                  {"what": "helm", "box": [-9.4, r4(deck_y), -0.7, -8.75, r4(deck_y + 1.6), 0.7]},
                  {"what": "hatch", "box": [L["layout"]["hatch"]["x"][0] / 1000, r4(deck_y), -0.5, L["layout"]["hatch"]["x"][1] / 1000, r4(deck_y + 1.0), 0.5]},
                  {"what": "spawn", "box": [-9.0, r4(deck_y), -1.5, -8.0, r4(deck_y + 2.0), -0.5]},
                  {"what": "plaque", "box": [-8.4, r4(deck_y + 1.25), -0.325, -8.25, r4(deck_y + 1.75), 0.325]}] + gun_keep,
    # every gun on a place of its own, laid on a new ship (no deck slots yet: the places list no cells)
    "gunPlaces": gun_places,
    "sails": [{"code": "main", "mesh": "sail:main", "axisPoint": am, "axisDir": dm, "area": sail_area("latina main"), "mast": "main"},
              {"code": "mizzen", "mesh": "sail:mizzen", "axisPoint": az, "axisDir": dz, "area": sail_area("latina mizzen"), "mast": "mizzen"}],
    "movingParts": [
        {"code": "rudder", "axisPoint": rud_pt, "axisDir": rud_dir, "source": "rudder", "angle": 30.0},
        {"code": "tiller", "axisPoint": rud_pt, "axisDir": rud_dir, "source": "rudder", "angle": 30.0, "interior": True},
        {"code": "yard-main", "axisPoint": [masts["main"]["x"] / 1000, r4(ym["cross"][1] / 1000 - WL), 0.0], "axisDir": [0.0, 1.0, 0.0],
         "source": "sheet", "angle": 57.29578, "limit": 25.0},
        {"code": "yard-mizzen", "axisPoint": [masts["mizzen"]["x"] / 1000, r4(yz["cross"][1] / 1000 - WL), 0.0], "axisDir": [0.0, 1.0, 0.0],
         "source": "sheet", "angle": 57.29578, "limit": 25.0}] + gun_parts_def,
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
with open(os.path.join(ASSETS, "config", "ships", f"{CODE}.json"), "w") as f:
    json.dump(definition, f, indent=1)
log("wrote ships/caravel.json; done")
