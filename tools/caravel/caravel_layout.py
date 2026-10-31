"""The caravel's fittings, rigging, sails and gameplay data, on top of what export_caravel.py takes from the master model
(21 October 2026, the user's list after trying her in the game: sails that flicker and have no roping, no plaque, no
lanterns, no anchor, no pump, the helm blind behind the mizzen; and the chalk notes 11-24). Plain Python and numpy:

    python tools/caravel/caravel_lines.py           (the layout data, models/caravel/caravel-lines.json)
    python tools/caravel/caravel_layout.py          (rerun alone in seconds; export_caravel.py runs it at its end)

Reads models/caravel/export/caravel-def-base.json and caravel-mesh-base.json (the exporter's: the sliced hull, its
collision, the guns, the gates, the deck cells), the lines and the build's report; writes
assets/ships/config/ships/caravel.json and config/shipmeshes/caravel.json. The fittings are drawn here with the carrack
generator's mesh helpers (tools/shipgen/shipgen.py, Design: ship-local blocks, x fore, y up from the waterline, z to
starboard), not modelled in AstoCAD: they aren't sliced into blocks. Where each stands is ship data
(caravel_lines.FITTINGS), not code here. The guns are drawn again here, the carrack's fitted to hers, with their lids
(caravel_gun.py), over the exporter's plain ones.
"""
import base64
import json
import math
import os
import sys
import zlib

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
MOD = os.path.join(ROOT, "models", "caravel")
ASSETS = os.path.join(ROOT, "assets", "ships")
sys.path.insert(0, os.path.join(ROOT, "tools", "shipgen"))
sys.path.insert(0, HERE)
from shipgen import Design, Material          # noqa: E402
import caravel_gun as CG                       # noqa: E402
import caravel_lines as CL                     # noqa: E402
import caravel_fittings as CF                  # noqa: E402

CODE = "caravel"


def r4(v):
    return round(float(v), 4)


def v4(p):
    return [r4(c) for c in p]


# ---------------------------------------------------------------------------------------------------------- inputs
def load():
    L = json.load(open(os.path.join(MOD, "caravel-lines.json")))
    REP = json.load(open(os.path.join(MOD, "caravel-report.json")))
    base = os.environ.get("CARAVEL_LAYOUT_BASE", os.path.join(MOD, "export"))
    D = json.load(open(os.path.join(base, "caravel-def-base.json")))
    M = json.load(open(os.path.join(base, "caravel-mesh-base.json")))
    return L, REP, D, M


class Frame:
    """Model mm (X fore, Y port, Z up from the underside) to the game's ship-local blocks."""
    def __init__(self, L):
        self.WL = L["levels"]["waterline"] / 1000.0

    def g(self, x, y, z):
        return np.array([x / 1000.0, z / 1000.0 - self.WL, -y / 1000.0])

    def y_of(self, z_mm):
        return z_mm / 1000.0 - self.WL


def sheer_at(L, x_blocks):
    pts = L["sheer"]
    return float(np.interp(x_blocks * 1000.0, [p[0] for p in pts], [p[1] for p in pts])) / 1000.0


def hb(x_blocks, z_blocks):
    """The hull's outer half-breadth (blocks) at model height z (blocks above the underside)."""
    return float(CL.half_breadth(np.array([x_blocks]), z_blocks)[0])


def hb_in(x_blocks, z_blocks):
    return max(0.0, hb(x_blocks, z_blocks) - CL.SKIN)


def border_places(L):
    LV=L["levels"]
    deck=LV["mainDeck"]/1000-2.875
    qd=LV["quarterdeck"]/1000-2.875
    # A narrow top wedge beside the stair: authored from the hull section, beyond the walking voxel's resolution.
    wedge=[]
    for y0 in np.arange(deck+2, qd-.2-.001, .0625):
        y1=min(y0+.0625,qd-.2)
        edge=min(hb_in(CL.QD_FRONT+dx, yy+2.875) for dx in [-.0625,.0625] for yy in [y0,y1])-.002
        if edge>2.5+.005: wedge.append([2.5,r4(y0),r4(edge),r4(y1)])
    return [{"code":"cabin-front-starboard-upper","deck":"main","at":[CL.QD_FRONT,deck+2,3],"along":"z","anchor":"-x","clip":wedge}]


# ---------------------------------------------------------------------------------------------------------- meshes
TEXTURES = {
    "rope": "game:block/cloth/reedrope", "cloth": "game:block/linen", "timber": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1", "iron": "game:block/metal/tarnished/iron",
    "bronze": "game:block/metal/plate/tinbronze", "spar": "game:block/wood/debarked/pine",
    "brass": "game:block/metal/plate/brass", "tarred-in": "game:block/wood/planks/oak4++3~ships:block/tar",
    "tarred": "game:block/wood/planks/oak1++3~ships:block/tar", "black": "game:block/black",
    # (the carrack's fittings' materials: caravel_fittings)
    "oak": "game:block/wood/planks/oak3", "gold": "game:block/metal/plate/gold",
}


def design():
    d = Design(CODE, -1, 1, -1, 1, -1, 1)          # (a token voxel box: only its mesh helpers are used)
    for code, tex in TEXTURES.items():
        d.material(Material(code, [tex], (128, 128, 128), grain=code in ("timber", "spar", "oak")))
    return d


def merge_quads(d, M, inside=None):
    """The design's quads into the mesh file's groups ([texture, x y z u v nx ny nz x 4]), its textures added. A quad in
    the group "auto" (the carrack's fittings' way) goes to "interior" where inside(its middle) says so, else "exterior"."""
    tex = M["textures"]

    def face_normal(p):
        p = np.asarray(p)
        n = np.cross(p[1] - p[0], p[3] - p[0]) + np.cross(p[3] - p[2], p[1] - p[2])
        if np.linalg.norm(n) < 1e-12:
            n = np.cross(p[1] - p[0], p[2] - p[0])
        ln = np.linalg.norm(n)
        return n / ln if ln > 1e-12 else np.array([0.0, 1.0, 0.0])

    acc, normals = {}, []
    for q in d.quads:
        n = face_normal(q["pts"])
        normals.append(n)
        if q.get("smooth"):
            for p in q["pts"]:
                key = (q["smooth"], round(p[0], 4), round(p[1], 4), round(p[2], 4))
                acc[key] = acc.get(key, 0) + n
    count = {}
    for i, q in enumerate(d.quads):
        path = d.quad_texture(i, q)
        if path not in tex:
            tex.append(path)
        row = [tex.index(path)]
        cuv = d.corner_uvs(q)
        for k, p in enumerate(q["pts"]):
            if q.get("smooth"):
                n = acc[(q["smooth"], round(p[0], 4), round(p[1], 4), round(p[2], 4))]
                n = n / (np.linalg.norm(n) + 1e-12)
            else:
                n = normals[i]
            row += [r4(p[0]), r4(p[1]), r4(p[2]), round(float(cuv[k][0]), 3), round(float(cuv[k][1]), 3),
                    round(float(n[0]), 3), round(float(n[1]), 3), round(float(n[2]), 3)]
        grp = q["group"]
        if grp == "auto":
            mid = np.mean(np.asarray(q["pts"], float), axis=0)
            grp = "interior" if inside is not None and inside(mid) else "exterior"
        M["groups"].setdefault(grp, []).append(row)
        count[grp] = count.get(grp, 0) + 1
    return count


# ---------------------------------------------------------------------------------------------------------- sails
def lateen_sail(d, code, pts, area):
    """A lateen sail as the carrack's: rows from the yard down to the clew, each from the luff to the leech, bellied to
    starboard (the mod mirrors it on the other tack); one side only (the flat sail drawn both ways round flickered)."""
    ya, yb, clew = (np.asarray(p, float) for p in pts)
    L_ = float(np.linalg.norm(yb - ya))
    eu = (yb - ya) / L_
    ev0 = clew - ya
    ev = ev0 - np.dot(ev0, eu) * eu
    cd = float(np.linalg.norm(ev))
    ev /= cd
    sc = float(np.dot(clew - ya, eu))
    en = np.cross(eu, ev)
    if en[2] < 0:
        en = -en
    amp = 0.104 * math.sqrt(area)              # the carrack's 0.7 for her 45 m2, as the sail's size

    def belly(s, t):
        f = t / cd
        s0, s1 = sc * f, L_ + (sc - L_) * f
        u = (s - s0) / max(1e-6, s1 - s0)
        return amp * math.sin(math.pi * min(max(u, 0), 1)) * math.sin(math.pi * min(f * 0.9 + 0.1, 1))

    def LP(u, f):
        t = f * cd
        s0, s1 = sc * f, L_ + (sc - L_) * f
        sv = s0 + (s1 - s0) * u
        return ya + sv * eu + t * ev + belly(sv, t) * en

    nu, nv = 18, 16
    for iv in range(nv):
        for iu in range(nu):
            f0, f1, u0, u1 = iv / nv, (iv + 1) / nv, iu / nu, (iu + 1) / nu
            d.add_quad("sail:" + code, "cloth", [LP(u0, f0), LP(u1, f0), LP(u1, f1), LP(u0, f1)],
                       (u0 * 16, f0 * 16, u1 * 16, f1 * 16), smooth="lateen-" + code)
    # (the sail's axis: its tack up along the yard, as the carrack's lateen; its clew from its sheet's tip)
    return {"code": code, "mesh": "sail:" + code, "axisPoint": v4(ya), "axisDir": v4(eu), "area": area}


def merge_slot_triangles(M):
    """The exporter writes triangles (a quad with its last corner doubled). In the slot groups, two triangles of one
    cell's face sharing an edge become one quad (the mod's checks take a quad's box for its face, and two triangles of
    one square are each the whole square's box: "two faces in one plane")."""
    def corners(q):
        return [tuple(q[1 + 8 * k: 9 + 8 * k]) for k in range(4)]

    def key(c):
        return (round(c[0], 4), round(c[1], 4), round(c[2], 4))

    merged_total = 0
    for g in [g for g in M["groups"] if g.startswith("slot:")]:
        quads, pairs = M["groups"][g], M["slotQuads"][g]
        tri = [n for n, q in enumerate(quads) if key(corners(q)[2]) == key(corners(q)[3])]
        by = {}
        for n in tri:
            q = quads[n]
            c = corners(q)[:3]
            nrm = tuple(round(v, 2) for v in c[0][5:8])
            by.setdefault((q[0], pairs[2 * n], pairs[2 * n + 1], nrm), []).append(n)
        used, out_q, out_p = set(), [], []
        new_of = {}
        for n_list in by.values():
            for a in n_list:
                if a in used:
                    continue
                ca = corners(quads[a])[:3]
                ka = [key(c) for c in ca]
                for b in n_list:
                    if b == a or b in used:
                        continue
                    cb = corners(quads[b])[:3]
                    kb = [key(c) for c in cb]
                    shared = [k for k in ka if k in kb]
                    if len(shared) != 2:
                        continue
                    # tri a cyclic (p, q, r) with the shared edge q -> r; b's free corner s goes between: p, q, s, r
                    for rot in range(3):
                        p_, q_, r_ = ca[rot], ca[(rot + 1) % 3], ca[(rot + 2) % 3]
                        if key(q_) in shared and key(r_) in shared:
                            break
                    s_ = next((c for c in cb if key(c) not in shared), None)
                    # (a degenerate triangle, two corners in one place: not paired)
                    if s_ is None or not pair_ok(p_, q_, r_, s_, cb):       # (flat, convex, wound alike, one texture mapping over both)
                        continue
                    quad = [quads[a][0]] + [v for c in (p_, q_, s_, r_) for v in c]
                    new_of[a] = quad
                    used |= {a, b}
                    merged_total += 1
                    break
        for n, q in enumerate(quads):
            if n in used and n not in new_of:
                continue
            out_q.append(new_of.get(n, q))
            out_p += [pairs[2 * n], pairs[2 * n + 1]]
        M["groups"][g], M["slotQuads"][g] = out_q, out_p
    return merged_total


def pair_ok(p_, q_, r_, s_, other):
    """(after Codex's second review) May triangle p q r (q -> r the edge it shares) and the other triangle (its corners,
    s its third) be drawn as the quad p q s r? Their shared corners alike in both (place, UV, normal), the other wound
    the same way, flat (s in p q r's plane), convex (p and s either side of q r, q and r either side of p s), and one
    texture mapping and normal over all four (affine_ok)."""
    def same(a, b):
        return all(abs(a[k] - b[k]) < (1e-4 if k < 3 else 1e-3) for k in range(8))

    if sum(1 for c in other if same(c, q_) or same(c, r_)) != 2:
        return False
    P = [np.array(c[:3]) for c in (p_, q_, s_, r_)]
    na = np.cross(P[1] - P[0], P[3] - P[0])
    o = [np.array(c[:3]) for c in other]
    nb = np.cross(o[1] - o[0], o[2] - o[0])
    ln = float(np.linalg.norm(na))
    if ln < 1e-12 or float(na @ nb) <= 0 or abs(float((P[2] - P[0]) @ (na / ln))) > 1e-4:
        return False

    def side(a, b, c):
        return float(np.cross(b - a, c - a) @ na)
    if side(P[1], P[3], P[0]) * side(P[1], P[3], P[2]) >= 0 or side(P[0], P[2], P[1]) * side(P[0], P[2], P[3]) >= 0:
        return False
    return affine_ok(p_, q_, r_, s_)


def affine_ok(p_, q_, r_, s_, tol=0.01):
    """Does the triangle p q r's texture mapping (affine over its plane) put s's UV where s has it, and are the four
    corners' normals one? Then a quad p q s r drawn across either diagonal looks the same."""
    P, Q, R, S = (np.array(c[:3]) for c in (p_, q_, r_, s_))
    e1, e2, w = Q - P, R - P, S - P
    a11, a12, a22 = e1 @ e1, e1 @ e2, e2 @ e2
    det = a11 * a22 - a12 * a12
    if abs(det) < 1e-18:
        return False
    b1, b2 = w @ e1, w @ e2
    a_, b_ = (a22 * b1 - a12 * b2) / det, (a11 * b2 - a12 * b1) / det
    for k in (3, 4):
        if abs(p_[k] + a_ * (q_[k] - p_[k]) + b_ * (r_[k] - p_[k]) - s_[k]) > tol:
            return False
    return all(abs(c[k] - p_[k]) < 1e-3 for c in (q_, r_, s_) for k in (5, 6, 7))


def merge_plain_triangles(M):
    """(22 October 2026, the user: "Specifically look at performance aspects") The exporter's meshes are triangles, each
    a quad with its last corner doubled (four corners drawn for three): 81,000 of her 102,000. In the groups drawn whole
    (not the slot groups, which merge_slot_triangles pairs by owner), two triangles of one texture and one flat normal
    sharing an edge, its two corners with the same UVs in both (one texture mapping), become one quad where it's convex.
    Returns how many were merged."""
    def corner(q, k):
        return tuple(q[1 + 8 * k: 9 + 8 * k])

    def pkey(c):
        return (round(c[0], 4), round(c[1], 4), round(c[2], 4), round(c[3], 3), round(c[4], 3))

    merged_total = 0
    for g in [g for g in M["groups"] if not g.startswith("slot:") and g != "shot"]:
        quads = M["groups"][g]
        tris = [n for n, q in enumerate(quads) if pkey(corner(q, 2)) == pkey(corner(q, 3))]
        edges = {}
        for n in tris:
            q = quads[n]
            nk = (q[0], round(q[6], 2), round(q[7], 2), round(q[8], 2))
            ks = [pkey(corner(q, k)) for k in range(3)]
            for e in range(3):
                a, b = ks[e], ks[(e + 1) % 3]
                edges.setdefault((nk, min(a, b), max(a, b)), []).append(n)
        used, new_of = set(), {}
        for n in tris:
            if n in used:
                continue
            q = quads[n]
            nk = (q[0], round(q[6], 2), round(q[7], 2), round(q[8], 2))
            ca = [corner(q, k) for k in range(3)]
            ks = [pkey(c) for c in ca]
            for rot in range(3):
                # tri n cyclic (p, q, r), the shared edge q -> r
                p_, q_, r_ = ca[rot], ca[(rot + 1) % 3], ca[(rot + 2) % 3]
                kq, kr = ks[(rot + 1) % 3], ks[(rot + 2) % 3]
                other = next((m for m in edges.get((nk, min(kq, kr), max(kq, kr)), []) if m != n and m not in used), None)
                if other is None:
                    continue
                cb = [corner(quads[other], k) for k in range(3)]
                s_ = next((c for c in cb if pkey(c) not in (kq, kr)), None)
                if s_ is None or not pair_ok(p_, q_, r_, s_, cb):
                    continue
                # the same winding as the triangle: p, q, s, r
                new_of[n] = [q[0]] + [v for c in (p_, q_, s_, r_) for v in c]
                used |= {n, other}
                merged_total += 1
                break
        M["groups"][g] = [new_of.get(n, qq) for n, qq in enumerate(quads) if n not in used or n in new_of]
    return merged_total


def split_tiles(M, start=None):
    """(22 October 2026, after Codex's review; chalk notes 42, 44: "textures are complete mess") The ship's texture atlas
    doesn't repeat: the mod brings a quad's UVs inside one 16-texel tile (ShapeUvs.FitQuad), shifting one that crosses a
    tile's edge and squashing one longer than a tile (a rail, a cap, a yard: up to 4 blocks of wood on one block's
    texture). Each quad is cut at the tiles' edges instead (its corners' positions, UVs and normals along its edges, its
    mapping affine over it), each piece's UVs brought into its tile: texture density follows the surface and runs on
    across the cuts. start: {group: the first quad to look at} (the fittings added after her paint's ranges: those
    before stay where they are). Returns (quads cut, pieces made)."""
    def corners(q):
        out = []
        for k in range(4):
            c = list(q[1 + 8 * k: 9 + 8 * k])
            if not out or any(abs(c[a] - out[-1][a]) > 1e-7 for a in range(5)):
                out.append(c)
        if len(out) > 1 and all(abs(out[0][a] - out[-1][a]) <= 1e-7 for a in range(5)):
            out.pop()
        return out

    def clip(poly, axis, t, keep_above):
        res = []
        for i in range(len(poly)):
            a, b = poly[i], poly[(i + 1) % len(poly)]
            ina = (a[axis] >= t - 1e-6) if keep_above else (a[axis] <= t + 1e-6)
            inb = (b[axis] >= t - 1e-6) if keep_above else (b[axis] <= t + 1e-6)
            if ina:
                res.append(list(a))                 # (a copy: each piece's UVs are moved into its own tile)
            if ina != inb:
                f = (t - a[axis]) / (b[axis] - a[axis])
                res.append([a[k] + f * (b[k] - a[k]) for k in range(8)])
        return res

    copper = []
    for i in range(1, 6):
        path = f"game:block/metal/sheet/copper{i}"
        if path not in M["textures"]:
            M["textures"].append(path)
        copper.append(M["textures"].index(path))
    def tile_tex(t, iu, iv):
        return copper[(iu * 73 + iv * 37) % 5] if t in copper else t
    cut, made = 0, 0
    for g in list(M["groups"]):
        if g.startswith("sail:") or g == "shot":
            continue
        quads = M["groups"][g]
        pairs = M.get("slotQuads", {}).get(g)
        out_q, out_p = [], []
        first = start.get(g, 0) if start else 0
        for n, q in enumerate(quads):
            poly = corners(q) if n >= first else []
            us, vs = [c[3] for c in poly], [c[4] for c in poly]
            iu0, iu1 = (math.floor(min(us) / 16 + 1e-5), math.floor(max(us) / 16 - 1e-5)) if poly else (0, 0)
            iv0, iv1 = (math.floor(min(vs) / 16 + 1e-5), math.floor(max(vs) / 16 - 1e-5)) if poly else (0, 0)
            if len(poly) < 3 or (iu0 == iu1 and iv0 == iv1):
                if poly:
                    q = [tile_tex(q[0], iu0, iv0)] + q[1:]
                if poly and (iu0 or iv0):
                    # (in one tile, not the first: brought into it whole)
                    q = list(q)
                    for k in range(4):
                        q[4 + 8 * k] = round(q[4 + 8 * k] - 16 * iu0, 3)
                        q[5 + 8 * k] = round(q[5 + 8 * k] - 16 * iv0, 3)
                out_q.append(q)
                if pairs is not None:
                    out_p += pairs[2 * n: 2 * n + 2]
                continue
            cut += 1
            for iu in range(iu0, iu1 + 1):
                col = clip(clip(poly, 3, 16 * iu, True), 3, 16 * (iu + 1), False) if poly else []
                for iv in range(iv0, iv1 + 1):
                    piece = clip(clip(col, 4, 16 * iv, True), 4, 16 * (iv + 1), False) if len(col) >= 3 else []
                    # (drop slivers and repeated corners)
                    pc = []
                    for c in piece:
                        if not pc or any(abs(c[a] - pc[-1][a]) > 1e-6 for a in range(3)):
                            pc.append(c)
                    if len(pc) > 1 and all(abs(pc[0][a] - pc[-1][a]) <= 1e-6 for a in range(3)):
                        pc.pop()
                    if len(pc) < 3:
                        continue
                    for c in pc:
                        c[3] -= 16 * iu
                        c[4] -= 16 * iv
                    # a fan of quads (convex: the cut of a convex quad), the last a triangle if it comes out odd
                    k = 1
                    while k + 1 < len(pc):
                        four = [pc[0], pc[k], pc[k + 1], pc[k + 2] if k + 2 < len(pc) else pc[k + 1]]
                        out_q.append([tile_tex(q[0], iu, iv)] + [round(v, 4) if a < 3 else round(v, 3) for c in four for a, v in enumerate(c)])
                        if pairs is not None:
                            out_p += pairs[2 * n: 2 * n + 2]
                        made += 1
                        k += 2
        M["groups"][g] = out_q
        if pairs is not None:
            M["slotQuads"][g] = out_p
    return cut, made


def collapse_slivers(M, D, width=0.005):
    """(25 October 2026) Two faces still overlapping in one plane (two slots' faces, which never move) where one is a sliver
    less than `width` across (its area over its longest edge: the slicer leaves a few where a cell's border meets her
    side): the sliver is collapsed to a point, its place in its group kept (paint ranges, slot pairs). Returns how many."""
    import overlap_check as OC
    def width_of(q):
        P = np.array([q[1 + 8 * k: 4 + 8 * k] for k in range(4)])
        area = 0.5 * (np.linalg.norm(np.cross(P[1] - P[0], P[2] - P[0])) + np.linalg.norm(np.cross(P[2] - P[0], P[3] - P[0])))
        edge = max(np.linalg.norm(P[(k + 1) % 4] - P[k]) for k in range(4))
        return area / edge if edge > 0 else 0.0
    n = 0
    for ga, na, gb, nb, *_ in OC.find_clashes(M, OC.empty_slots(D)):
        for g, k in sorted(((ga, na), (gb, nb)), key=lambda f: width_of(M["groups"][f[0]][f[1]]))[:1]:
            q = M["groups"][g][k]
            if width_of(q) < width:
                for c in range(1, 4):
                    q[1 + 8 * c: 4 + 8 * c] = q[1:4]
                n += 1
    return n


def square_slot_faces(M, D):
    """A whole deck cell's top and underside are flat squares: each drawn as one quad (the mesher's fans of triangles
    round a vertex overlapped in their boxes), its texture where the cell's own faces had it (their UVs, fitted)."""
    slots = D.get("deckSlots", [])
    for g in [g for g in M["groups"] if g.startswith("slot:deck")]:
        quads, pairs = M["groups"][g], M["slotQuads"][g]
        groups_ = {}
        for n, q in enumerate(quads):
            own, fac = pairs[2 * n], pairs[2 * n + 1]
            ny = sum(q[7 + 8 * k] for k in range(4)) / 4
            if own < 0 or fac != -1 or abs(ny) < 0.9:
                continue
            groups_.setdefault((own, 1 if ny > 0 else -1, q[0]), []).append(n)
        drop, add = set(), []
        for (own, sg, tex), ns in groups_.items():
            if slots[own].get("kind") not in (None, "beam"):
                continue
            b = slots[own]["box"]
            y = b[4] if sg > 0 else b[1]
            if slots[own].get("kind") == "beam":
                # ((25 October 2026) a beam's length: its top and underside from its own faces, split where a carling meets
                # it; their boxes overlapped. One rectangle over their extent, at their height)
                cs = [quads[n][1 + 8 * k: 4 + 8 * k] for n in ns for k in range(4)]
                y = cs[0][1]
                b = [min(c[0] for c in cs), y, min(c[2] for c in cs), max(c[0] for c in cs), y, max(c[2] for c in cs)]
            if not all(abs(quads[n][2 + 8 * k] - y) < 1e-3 for n in ns for k in range(4)):
                continue
            # u, v as affine functions of (x, z), from the face with the largest first triangle ((25 October 2026) the
            # first face could be a sliver beside a rib: its fit skewed the grain, and the tile cuts made slivers of it)
            q = max((quads[n] for n in ns), key=lambda q_: abs(np.linalg.det(np.array([[q_[1 + 8 * k], q_[3 + 8 * k], 1.0] for k in range(3)]))))
            P = np.array([[q[1 + 8 * k], q[3 + 8 * k], 1.0] for k in range(3)])
            if abs(np.linalg.det(P)) < 1e-9:
                continue
            cu = np.linalg.solve(P, np.array([q[4 + 8 * k] for k in range(3)]))
            cv = np.linalg.solve(P, np.array([q[5 + 8 * k] for k in range(3)]))
            pts = [(b[0], b[2]), (b[3], b[2]), (b[3], b[5]), (b[0], b[5])]
            # wound as the faces it replaces (their first three corners' turn about the normal)
            c0, c1, c2 = (np.array(q[1 + 8 * k: 4 + 8 * k]) for k in range(3))
            turn = np.cross(c1 - c0, c2 - c0)[1]
            mine = np.cross(np.array([pts[1][0] - pts[0][0], 0, pts[1][1] - pts[0][1]]), np.array([pts[2][0] - pts[0][0], 0, pts[2][1] - pts[0][1]]))[1]
            if (turn > 0) != (mine > 0):
                pts = pts[::-1]
            row = [tex]
            for x, z in pts:
                row += [r4(x), r4(y), r4(z), round(float(cu @ [x, z, 1]), 3), round(float(cv @ [x, z, 1]), 3), 0.0, float(sg), 0.0]
            drop |= set(ns)
            add.append((row, own))
        # ((25 October 2026) a cell's side on its box's side, split into a fan where a rib meets it: as one quad too,
        # when its faces cover the side whole (their boxes overlapped in one plane))
        sides_ = {}
        for n, q in enumerate(quads):
            own, fac = pairs[2 * n], pairs[2 * n + 1]
            if own < 0 or fac != -1 or n in drop:
                continue
            nv = [sum(q[6 + a + 8 * k] for k in range(4)) / 4 for a in range(3)]
            for a in (0, 2):
                if abs(nv[a]) > 0.9:
                    sides_.setdefault((own, a, 1 if nv[a] > 0 else -1, q[0]), []).append(n)
        for (own, a, sg, tex), ns in sides_.items():
            b = slots[own]["box"]
            c_ = b[a + 3] if sg > 0 else b[a]
            h = 2 if a == 0 else 0
            if not all(abs(quads[n][1 + a + 8 * k] - c_) < 1e-3 for n in ns for k in range(4)):
                continue
            def area(q_):
                P_ = [np.array([q_[1 + h + 8 * k], q_[2 + 8 * k]]) for k in range(4)]
                return 0.5 * (abs(np.cross(P_[1] - P_[0], P_[2] - P_[0])) + abs(np.cross(P_[2] - P_[0], P_[3] - P_[0])))
            whole = (b[h + 3] - b[h]) * (b[4] - b[1])
            if len(ns) < 2 or abs(sum(area(quads[n]) for n in ns) - whole) > 0.01 * whole:
                continue
            q = max((quads[n] for n in ns), key=area)
            P = np.array([[q[1 + h + 8 * k], q[2 + 8 * k], 1.0] for k in range(3)])
            if abs(np.linalg.det(P)) < 1e-9:
                continue
            cu = np.linalg.solve(P, np.array([q[4 + 8 * k] for k in range(3)]))
            cv = np.linalg.solve(P, np.array([q[5 + 8 * k] for k in range(3)]))
            pts = [(b[h], b[1]), (b[h + 3], b[1]), (b[h + 3], b[4]), (b[h], b[4])]

            def p3(hv, yv):
                p_ = [0.0, yv, 0.0]
                p_[a], p_[h] = c_, hv
                return np.array(p_)
            c0, c1, c2 = (np.array(q[1 + 8 * k: 4 + 8 * k]) for k in range(3))
            if (np.cross(c1 - c0, c2 - c0)[a] > 0) != (np.cross(p3(*pts[1]) - p3(*pts[0]), p3(*pts[2]) - p3(*pts[0]))[a] > 0):
                pts = pts[::-1]
            row = [tex]
            for hv, yv in pts:
                p_ = p3(hv, yv)
                nn = [0.0, 0.0, 0.0]
                nn[a] = float(sg)
                row += [r4(p_[0]), r4(p_[1]), r4(p_[2]), round(float(cu @ [hv, yv, 1]), 3), round(float(cv @ [hv, yv, 1]), 3)] + nn
            drop |= set(ns)
            add.append((row, own))
        keep_q = [q for n, q in enumerate(quads) if n not in drop]
        keep_p = [v for n in range(len(quads)) if n not in drop for v in (pairs[2 * n], pairs[2 * n + 1])]
        M["groups"][g] = keep_q + [r for r, _ in add]
        M["slotQuads"][g] = keep_p + [v for _, o in add for v in (o, -1)]


# ---------------------------------------------------------------------------------------------------------- paint
PAINT_ZONES = [
    # ((25 October 2026, chalk note 101: "Above water line we sohuld be able to paint hull in block level stripes") a zone
    # a block row, none under the waterline's row and one over it: the paint session's "rows")
    {"code": "topsides", "name": "Topsides: her sides outside from the waterline up to the rail, the transom, the gates", "kind": "panel", "default": "tarred", "rows": True},
    {"code": "rail-caps", "name": "Cap rails on the waist's bulwarks", "kind": "moulding", "default": "oak"},
    {"code": "quarterdeck-railing", "name": "Quarterdeck's railing: posts, rails and balusters", "kind": "carving", "default": "oak"},
    {"code": "waist-bulwarks", "name": "Inside of the bulwarks over the main deck", "kind": "panel", "default": "tarred"},
    {"code": "bulwark-frames", "name": "Frames (ribs) standing inside the bulwarks", "kind": "moulding", "default": "timber"},
    {"code": "hold", "name": "Hold's walls: the inside planking", "kind": "panel", "default": "tarred"},
    {"code": "hold-timbers", "name": "Hold's frames and beams, the keelson, the hatchway's carlings", "kind": "moulding", "default": "timber"},
    {"code": "hold-ceiling", "name": "Hold's ceiling: the main deck's underside", "kind": "ceiling", "default": "deck"},
    {"code": "steerage-ceiling", "name": "Steerage's ceiling: the quarterdeck's underside", "kind": "ceiling", "default": "deck"},
    {"code": "steerage-walls", "name": "Inside the steerage: sides and transom", "kind": "panel", "default": "tarred", "rows": True},
]


def paint_quads(M, L, F, only_lids=False):
    """(21 October 2026, the user: "no painting ability (see magelan)") Each quad of her hull, its rails and timbers in
    a paint zone, by what it is (its texture), where it is and which way it faces: per group [start, end, zone, ...]."""
    tex = M["textures"]
    t_hull, t_hull2 = tex.index("game:block/wood/planks/oak1++3~ships:block/tar"), tex.index("game:block/wood/planks/oak4++3~ships:block/tar")
    t_deck, t_deck2 = tex.index("game:block/wood/planks/oak1"), tex.index("game:block/wood/planks/oak3")
    t_timber = tex.index("game:block/wood/debarked/oak")
    zi = {z["code"]: n for n, z in enumerate(PAINT_ZONES)}
    deck, qd = F.y_of(L["levels"]["mainDeck"]), F.y_of(L["levels"]["quarterdeck"])
    qf = L["quarterdeckFront"] / 1000.0

    def zone(group, q):
        t = q[0]
        c = [sum(q[1 + 8 * k + a] for k in range(4)) / 4 for a in range(3)]
        ny_ = sum(q[1 + 8 * k + 6] for k in range(4)) / 4
        if group.startswith("part:gate-"):
            return {t_hull: "topsides", t_hull2: "waist-bulwarks", t_deck2: "rail-caps"}.get(t)
        if group.endswith("-lid"):
            return {t_hull: "topsides", t_hull2: "waist-bulwarks", t_deck2: "quarterdeck-railing"}.get(t)
        if t == t_hull:
            return "topsides"
        if t == t_hull2:
            if c[0] < qf and deck - 0.01 <= c[1] < qd - 0.2:
                return "steerage-walls"
            return "waist-bulwarks" if c[1] > deck - 0.25 else "hold"
        if t == t_deck2:
            return "quarterdeck-railing" if c[0] < qf - 0.02 else "rail-caps"
        if t == t_timber:
            return "bulwark-frames" if c[1] > deck else "hold-timbers"
        if t == t_deck and ny_ < -0.5:
            if abs(c[1] - (deck - 0.2)) < 0.06:
                return "hold-ceiling"
            if abs(c[1] - (qd - 0.2)) < 0.06:
                return "steerage-ceiling"
        return None

    out = {}
    for group, quads in M["groups"].items():
        if not (group in ("exterior", "interior") or group.startswith("slot:") or group.startswith("part:gate-")
                or (only_lids and group.endswith("-lid"))):
            continue
        if only_lids and not group.endswith("-lid"):
            continue
        ranges = []
        for n, q in enumerate(quads):
            z = zone(group, q)
            if z is None:
                continue
            if ranges and ranges[-2] == n and ranges[-1] == zi[z]:
                ranges[-2] = n + 1
            else:
                ranges += [n, n + 1, zi[z]]
        if ranges:
            out[group] = ranges
    return out


# ---------------------------------------------------------------------------------------------------------- the build
def copper_view(M):
    """The metal alternative, sharing the actual face lists with the exported mesh."""
    return dict(M, groups={g.removesuffix("clad") if g.endswith("bottomclad") else g: qs
                           for g, qs in M["groups"].items() if not g.endswith("bottom")},
                slotQuads={g.removesuffix("clad") if g.endswith("bottomclad") else g: qs
                           for g, qs in M.get("slotQuads", {}).items() if not g.endswith("bottom")})


def deflicker(M, D, copper=False):
    """(22 October 2026, the user: "For ANY visible elements - check 3D model overlaps ... visible surface overlaps
    create texture flickers ... This is also rule for any new or modified models") Drawn faces in one plane, facing one
    way and overlapping (overlap_check.py) flicker. Each cluster of them (faces joined by overlaps) is spread out along
    its normal 2 mm a face: a slot's faces first and staying (the hull's and the decks' meet their neighbours exactly),
    then the larger, the smaller ones in front. Returns (moved, still overlapping after)."""
    import overlap_check as OC
    if copper:
        # Copper has its own UV tile cuts, so its faces do not have the tarred alternative's indices. Check and
        # separate its actual faces too; keep references to the original lists so the corrections are exported.
        M = copper_view(M)
    empty = OC.empty_slots(D)
    print("[layout] checking coplanar faces" + (" (copper)" if copper else ""), flush=True)
    found = OC.find_clashes(M, empty)
    print(f"[layout] {len(found)} coplanar pairs to resolve", flush=True)
    link, nrm_of, area_of = {}, {}, {}
    for ga, na, gb, nb, ov, c, nrm, aa, ab in found:
        a, b = (ga, na), (gb, nb)
        link.setdefault(a, set()).add(b)
        link.setdefault(b, set()).add(a)
        nrm_of[a] = nrm_of[b] = nrm
        area_of[a], area_of[b] = aa, ab
    seen, moved = set(), 0
    # ((27 October 2026) in a fixed order, ties by group and index: a set's order changed with Python's string hashing,
    # and which of two equal faces moved changed from run to run)
    for start in sorted(link):
        if start in seen:
            continue
        comp, todo = [], [start]
        seen.add(start)
        while todo:
            f = todo.pop()
            comp.append(f)
            for o in sorted(link[f]):
                if o not in seen:
                    seen.add(o)
                    todo.append(o)
        # (after Codex's review) a slot's faces never move (her panels' and cells' faces meet their neighbours exactly,
        # their openings and collision are where they are); the others in front of them, the larger first. Two slots'
        # faces in one plane are left as they are (counted in what's left)
        slots_ = [f for f in comp if f[0].startswith("slot:")]
        rest = sorted((f for f in comp if not f[0].startswith("slot:")), key=lambda f: (-area_of[f], f))
        # (a face of a slot's own overlapping another of the same slot's, one group: the mesher's overlapping fan; the
        # smaller moves with it. Only faces that overlap each other: two side by side under one fitting both stay)
        def owner(f):
            pr = M["slotQuads"][f[0]][2 * f[1]: 2 * f[1] + 2]
            return (f[0], pr[0], pr[1]) if pr[0] >= 0 else None
        anchored = []
        for f in sorted(slots_, key=lambda f: (-area_of[f], f)):
            if owner(f) is not None and any(owner(a) == owner(f) and a in link[f] for a in anchored):
                rest.append(f)
            else:
                anchored.append(f)
        slots_ = anchored
        nrm = nrm_of[comp[0]]
        movable = rest if slots_ else rest[1:]
        for k, (g_, n_) in enumerate(movable, start=1):
            q = M["groups"][g_][n_]
            for c_ in range(4):
                for a_ in range(3):
                    q[1 + 8 * c_ + a_] = round(q[1 + 8 * c_ + a_] + 0.002 * k * float(nrm[a_]), 4)
            moved += 1
    return moved, len(OC.find_clashes(M, empty))


def main():
    L, REP, D, M = load()
    F = Frame(L)
    LV, LAY = L["levels"], L["layout"]
    FIT = LAY["fittings"]
    deck = F.y_of(LV["mainDeck"])
    qd = F.y_of(LV["quarterdeck"])
    hold = F.y_of(LV["hold"])
    Zd, Zq = LV["mainDeck"] / 1000.0, LV["quarterdeck"] / 1000.0
    d = design()
    fixtures = list(D["fixtures"])
    parts = list(D["movingParts"])
    standing = []                                   # shrouds, for the mod to press the sails against (rigCheck)
    # A fixed timber head and forged ring for vanilla creature leads. The ring is clear of the hatch and galley.
    hitch = np.array([-5.5, deck + 0.85, -1.75])
    d.add_box_mesh("exterior", "timber", hitch - (0,0.425,0), (0.16,0.85,0.16))
    CF.iron_ring(d,hitch + (0.09,-0.12,0),0.065,0.014,normal=(0,0,1))
    fixtures.append({"code":"hitch-main", "type":"hitch", "pos":v4(hitch + (0.09,-0.12,0)),
                     "box":[-5.58,deck,-1.83,-5.42,deck+0.85,-1.67],
                     "sel":[-5.62,deck,-1.87,-5.3,deck+0.9,-1.63]})

    # Two working strap hinges on each entry gate. Alternate knuckles belong to jamb and leaf, with clear ends.
    for gl in REP["gateLeaves"]:
        code = "gate-" + gl["side"]
        s = -1.0 if gl["side"] == "port" else 1.0
        hinge = next(p for p in parts if p["code"] == code)["axisPoint"]
        inner_z = hinge[2]
        hinge[2] -= s * 0.020
        xh, _, zh = hinge
        for y in (F.y_of(gl["z0"]) + 0.22, F.y_of(gl["z1"]) - 0.23):
            for j in (-1, 0, 1):
                group = "part:" + code if j == 0 else "exterior"
                d.add_cylinder(group, "iron", (xh, y + j * 0.045 - 0.021, zh),
                               (xh, y + j * 0.045 + 0.021, zh), 0.015, 0.015, sides=10)
            d.add_box_mesh("part:" + code, "iron", (xh + 0.1525, y, inner_z - s * 0.008), (0.295, 0.045, 0.016))
            # Two fixed gudgeon tabs join the outer knuckles to the actual curved jamb. A strap across their
            # middle would foul the rotating centre knuckle; keep that height free and seat each tab in the hull.
            for j in (-1, 1):
                yy = y + j * 0.045
                anchor = (xh - 0.070, yy, s * (hb_in(xh - 0.070, yy + F.WL) + 0.008))
                d.add_beam_mesh("exterior", "iron", anchor, (xh, yy, zh), 0.016, 0.025, up=(0, 1, 0))

    def rope(a, b, group="exterior", r=0.025):
        d.add_rope(a, b, r=r, group=group)

    def follow(code, follow_part, anchor, tip, sail=None, interior=False, r=0.025):
        rope(anchor, tip, "part:" + code, r)
        # (24 October 2026, chalk note 64: "Rope attached to corner has empty space in connection") a ring at its end on
        # the sail's corner or the yard, the rope through it
        CF.rope_path(d, CF.ring_points(tip, 0.045, "z", 10), r=0.012, group="part:" + code, mat="iron")
        p = {"code": code, "kind": "follow", "follow": follow_part, "anchor": v4(anchor), "tip": v4(tip)}
        if sail:
            p["sail"] = sail
        if interior:
            p["interior"] = True
        parts.append(p)

    # ---------------------------------------------------------------- sails and running rigging (chalk: item 1)
    rig = REP["rigGeometry"]["latina"]
    yards = {y["name"]: y for y in rig["yards"]}
    masts = {m["name"]: m for m in rig["masts"]}
    sails = []
    Q = LV["quarterdeck"]
    tx0, tz0 = L["transomPlane"]["low"]
    tx1, tz1 = L["transomPlane"]["high"]

    def transom_x(z):
        return tx0 + (tx1 - tx0) * (z - tz0) / (tz1 - tz0)

    bumpkin_end = F.g(*REP["bumpkin"]["end"])
    # (chalk note 64: "The same rope attaches to railings just like that") a rope made fast on a rail ends on a cleat on its
    # top: a foot and two horns along the rail, the rope's last turns across them
    rail_y = (L["railings"]["top"][0][2] - 50 + 70) / 1000.0 - F.WL          # the top rail's top (140 high, 50 under the posts')

    def cleat(c, along, out=(0.0, 1.0, 0.0), group="exterior"):
        along, out = np.asarray(along, float), np.asarray(out, float)
        side = np.cross(along, out)
        R = np.column_stack([along, out, side])
        d.add_box_mesh(group, "oak", c + out * 0.02, (0.08, 0.05, 0.05), R)
        d.add_box_mesh(group, "oak", c + out * 0.06, (0.3, 0.035, 0.045), R)
        a_, b_ = c + out * 0.085 + along * 0.11, c + out * 0.085 - along * 0.11
        CF.rope_path(d, [a_ + side * 0.03, b_ - side * 0.03], r=0.018, group=group)
        CF.rope_path(d, [a_ - side * 0.03, b_ + side * 0.03], r=0.018, group=group)
        return a_

    front_x = L["quarterdeckFront"] / 1000.0 - 0.05
    belay = {}
    # ((25 October 2026, draft D) the quarterdeck stair's gap to starboard by her side: the mizzen's tack on one cleat in
    # the front rail's middle, both tacks)
    for key, z_ in (("main-sheet", 0.95), ("mizzen-tack", 0.0)):
        for sgn in ((-1.0, 1.0) if z_ else (1.0,)):   # (both sides: on the other tack the mod mirrors the ropes' ends)
            belay[(key, sgn)] = cleat(np.array([front_x, rail_y, sgn * z_]), (0.0, 0.0, 1.0))
        if not z_:
            belay[(key, -1.0)] = belay[(key, 1.0)]
    for name, code in (("latina main", "main"), ("latina mizzen", "mizzen")):
        sp = next(s for s in rig["sails"] if s["name"] == name)["pts"]           # tack, head, clew (mm)
        pts = [F.g(*p) for p in sp]
        area = round(0.5 * float(np.linalg.norm(np.cross(pts[1] - pts[0], pts[2] - pts[0]))), 1)
        sails.append(lateen_sail(d, code, pts, area))
        # (24 October 2026, the lateen session: a lateen's yard dips round its mast when she goes about; the mod mirrors
        # the yard, the sail and the ropes' ends across the mast's centre plane)
        sails[-1]["dip"] = {"mast": code, "standOff": 0.4, "ahead": 0.2, "seconds": 8}
        yd = yards[name]
        mast = masts[code]
        mx = mast["x"] / 1000.0
        cross = F.g(yd["cross"][0], 350, yd["cross"][1])
        tack = F.g(yd["tack"][0], 350, yd["tack"][1])
        top = F.g(mast["x"], 0, mast["top"] - 300)
        clew = pts[2]
        side_ = -1.0 if tack[2] < 0 else 1.0           # (the yard's and the sail's side: port)
        if code == "main":
            sheet_to = belay[("main-sheet", side_)]                                         # the quarterdeck's front rail
            # (chalk note 67: "rope ends nowhere with empty space") the tack to an eyebolt in the stem head's after face
            zs_ = sheer_at(L, 9.5) + 0.45
            sx_ = CL.x_stem(CL.shape_z(zs_)) + CL.BB_OUT_STEM - CL.BB_MOULDED
            tack_to = np.array([sx_ - 0.105, zs_ - F.WL, 0.0])
            d.add_cylinder("exterior", "iron", tack_to + (0.14, 0, 0), tack_to + (0.06, 0, 0), 0.024, 0.024, sides=10)
            d.add_cylinder("exterior", "iron", tack_to + (0.105, 0, 0), tack_to + (0.12, 0, 0), 0.065, 0.065, sides=12)
            CF.iron_ring(d, tack_to)
        else:
            sheet_to = bumpkin_end                                                           # out on the bumpkin
            # (chalk note 65: "this rope ends in stairs middle") the tack to the front rail on its yard's side, clear of
            # the stair on the centreline
            tack_to = belay[("mizzen-tack", side_)]
        follow(f"sheet-{code}", f"yard-{code}", sheet_to, clew, sail=code, r=0.03)
        follow(f"tack-{code}", f"yard-{code}", tack_to, tack, r=0.03)
        follow(f"halyard-{code}", f"yard-{code}", top, cross, r=0.035)
        # its fall: from a block at the masthead's after side down the mast to a cleat on it, chest high (it ended in the
        # air over the deck)
        heel_deck = deck if code == "main" else qd
        r_top, r_heel = mast["r1"] / 1000.0, mast["r0"] / 1000.0
        blk = np.array([mx - r_top - 0.1, top[1], 0.0])
        CF.block_at(d, "exterior", blk + np.array([0, 0.1, 0]))
        cy_ = heel_deck + 1.1
        rr_ = r_heel + (r_top - r_heel) * (cy_ - F.y_of(mast["heel"])) / (F.y_of(mast["top"]) - F.y_of(mast["heel"]))
        fall_to = cleat(np.array([mx - rr_ + 0.01, cy_, 0.0]), (0.0, 1.0, 0.0), out=(-1.0, 0.0, 0.0))
        rope(blk, fall_to, r=0.03)
    # shrouds: the main's three a side to its cap rails aft of the gate, the mizzen's two to the quarterdeck's rail
    mzx = LAY["masts"]["mizzen"]["cell"] + 0.5
    mnx = LAY["masts"]["main"]["cell"] + 0.5         # (draft D: the main mast 2 aft; its shrouds with it)
    for code, xs, top_drop in (("main", (mnx - 0.75, mnx - 1.55, mnx - 2.35), 1.2), ("mizzen", (mzx - 1.0, mzx - 1.35), 0.9)):
        mast = masts[code]
        mx = mast["x"] / 1000.0
        head = mast["top"] / 1000.0 - top_drop
        for sgn in (1, -1):
            group = f"rig:shrouds-{code}-{'port' if sgn < 0 else 'starboard'}"
            # Note 141: no channels, chainplates or deadeyes. Forged eyes are seated through the cap,
            # with the shroud seized through the eye, on the inboard half of the upper board.
            feet = []
            for x in xs:
                cap_y = sheer_at(L, x) + REP["capRail"]["h"] / 1000.0 - F.WL
                c = np.array([x, cap_y + 0.055, sgn * (hb(x, cap_y + F.WL - 0.08) - 0.16)])
                d.add_cylinder("exterior", "iron", c - (0, 0.11, 0), c - (0, 0.035, 0), 0.018, 0.018, sides=10)
                CF.iron_ring(d, c, 0.055, 0.012, normal=(1, 0, 0))
                feet.append(c + (0, 0.055, 0))
            legs = []
            for x, b in zip(xs, feet):
                a = np.array([mx, head - F.WL, sgn * 0.18])
                # (25 October 2026, note 104, the user: windward shrouds only, as lateen ships set them) each mast's and side's
                # in a group of their own: the mod shows the windward set and shifts them over with the dip
                rope(a, b, group=group, r=0.03)
                legs.append((a, b))
                standing.append(v4(list(b) + list(a)) + [0.04])
            for y in np.arange(max(b[1] for _, b in legs) + 0.35, head - F.WL - 0.7, 0.35):
                rung = [b + (a - b) * ((y - b[1]) / (a[1] - b[1])) for a, b in legs]
                for a, b in zip(rung, rung[1:]):
                    rope(a, b, group=group, r=0.009)
    for p in parts:
        if p["code"].startswith("yard-"):
            p["angle"] = 57.29578                     # (as before: the sheet's angle, radians to degrees)

    # ---------------------------------------------------------------- the wheel, its ropes to the tiller (notes 21, 29-31)
    # (22 October 2026) Magelan's wheel (caravel_fittings.wheel, the carrack's), on the quarterdeck where the user marked
    # it, forward of the mizzen; its ropes off the drum down through the deck, out under the beams to blocks at her sides,
    # aft to blocks abreast the tiller's end (the tiller now under the quarterdeck's beams) and to it
    wx = FIT["wheel"]["cell"][0] + 0.23
    part, helm, xd, wc = CF.wheel(d, wx, qd)
    parts.append(part)
    fixtures.append(helm)
    tl = REP.get("tiller", {}).get("end") or [-11000.0, 0.0, LV["quarterdeck"] - 750.0]   # (before a rebuild has it)
    tip = F.g(*tl)
    beam_y = qd - CL.DECK_T - 0.4 - 0.05
    parts += CF.wheel_ropes(d, xd, wc, qd, CL.DECK_T, beam_y, lambda x: hb_in(x, beam_y + F.WL) - 0.5, float(tip[0]) - 0.15, tip)

    # ---------------------------------------------------------------- the pump (chalk note 27: Magelan's)
    px, pz = FIT["pump"]["cell"][0] + 0.5, -FIT["pump"]["cell"][1] * 1.0
    # (chalk note 81) its pipe down through the main mast's cell, forward of the mast, in that cell
    mm_ = masts["main"]
    pipe_x = mm_["x"] / 1000.0 + mm_["r0"] / 1000.0 + 0.09
    pf, pparts = CF.pump(d, px, pz, deck, CL.DECK_T, hold, 1.0, pipe_x=pipe_x)
    fixtures.append(pf)
    parts += pparts

    # ---------------------------------------------------------------- the capstan, the anchor, its cable (notes 26)
    cx, cz = FIT["capstan"]["cell"][0] + 0.5, -FIT["capstan"]["cell"][1] * 1.0
    # (22 October 2026, the user: the cathead's "unrealistic attachment to the hull - it could be moved to closest rib and
    # be attached to ribs top"; its knee "does not fully connect with hull and cantilever beam") the cathead on the head
    # of the rib nearest the anchor's place, under the cap rail, out over the side; its knee from into the side up into
    # its underside
    ax = min((r_ + 0.5 for r_ in LAY["ribs"]), key=lambda x_: abs(x_ - FIT["anchor"]["x"]))
    sgn = -1 if FIT["anchor"]["side"] == "port" else 1
    hx_ = FIT.get("hawse", {}).get("x", ax + 0.6)
    h_out = np.array([hx_, deck + 0.3, sgn * (hb(hx_, Zd + 0.3) + 0.005)])
    h_in = np.array([hx_, deck + 0.3, sgn * (hb_in(hx_, Zd + 0.3) - 0.01)])
    CF.hawse(d, h_out, h_in, np.array([0.0, 0.0, float(sgn)]))
    fixtures.append(CF.capstan(d, cx, cz, deck, h_in))
    zs = sheer_at(L, ax)
    half = hb(ax, zs - 0.05)
    # (out past her side wherever the anchor hangs: the bow is fuller below the rail than at it)
    reach = max(hb(ax + dx_, zs - dz_) for dx_ in (-0.6, 0.0, 0.6) for dz_ in (0.2, 0.8, 1.4, 2.0, 2.5)) - half + 0.4
    # (the user: "place it outside hull and attach from side to the rib") level and square to her side, under the cap
    # rail, its heel against the planking over the rib, bolted through; its knee under it in the same plane
    cy = zs - 0.17 - F.WL
    side_at = hb(ax, cy + F.WL)
    ci = np.array([ax, cy, sgn * (side_at - 0.02)])
    co = np.array([ax, cy, sgn * (side_at + max(0.75, reach + half - side_at))])
    on_cat = ci + (co - ci) * 0.72 - np.array([0, 0.11, 0])                 # into the cathead's underside
    ky = on_cat[1] - 0.75
    knee_from = np.array([ax, ky, sgn * (hb(ax, ky + F.WL) - 0.03)])        # into her side
    parts += CF.cathead_and_anchor(d, ci, co, knee_from, sgn, h_out + np.array([0, 0, sgn * 0.06]), knee_to=on_cat)

    # ---------------------------------------------------------------- the plaque, the spawn plate, lanterns (items 2, 3)
    mz = LAY["masts"]["mizzen"]["cell"] + 0.5
    mr = masts["mizzen"]["r0"] / 1000.0
    pfx = mz + mr - 0.01
    d.add_box_mesh("exterior", "bronze", (pfx + 0.02, deck + 1.47, 0.0), (0.03, 0.3, 0.36))       # on the mizzen's fore side
    for yy in (deck + 1.34, deck + 1.6):
        for zz in (-0.15, 0.15):
            d.add_cylinder("exterior", "iron", (pfx + 0.03, yy, zz), (pfx + 0.045, yy, zz), 0.012, sides=5)
    fixtures.append({"type": "plaque", "code": "plaque", "pos": [r4(pfx + 0.035), r4(deck + 1.47), 0.0], "yaw": 180.0,
                     "sel": [r4(pfx - 0.02), r4(deck + 1.29), -0.21, r4(pfx + 0.08), r4(deck + 1.65), 0.21]})
    fixtures.append({"type": "attach", "code": "plate", "pos": [r4(mz), r4(deck + 1.45), r4(-mr - 0.05)], "yaw": 90.0,
                     "sel": [r4(mz - 0.35), r4(deck + 1.1), r4(-mr - 0.3), r4(mz + 0.35), r4(deck + 1.8), r4(-mr + 0.02)],
                     "kind": "plate", "spawn": [r4(mz), r4(deck), -1.0]})
    # (chalk note 33: "lantern attachment to mast missing ... check magelan") the carrack's iron band and arm on each
    # mast, 2.62 over its deck
    hooks = []
    for code, deck_of_mast in (("main", deck), ("mizzen", qd)):
        mast = masts[code]
        y = deck_of_mast + 2.62
        heel, top_ = F.y_of(mast["heel"]), F.y_of(mast["top"])
        r_ = (mast["r0"] + (mast["r1"] - mast["r0"]) * (y - heel) / (top_ - heel)) / 1000.0
        hp = CF.mast_band(d, mast["x"] / 1000.0, y, r_)
        hooks.append({"code": f"lantern-{code}mast", "pos": v4(hp), "hang": "down", "kinds": ["light"], "yaw": 0.0})
    # (24 October 2026, chalk note 63, the user: no stern lantern, its post or its crane on the taffrail)
    # (24 October 2026, chalk note 78: "we could have option to add vanilla figure heads"; the fittings session's place)
    # A raked figurehead seated high on the stem's fore face, under its head.
    figure = LAY["fittings"]["figurehead"]
    CF.figurehead_seat(d, L["backbone"]["outline"], figure["pos"], figure["rake"])
    a = math.radians(figure["rake"])
    hooks.append({"code": "figurehead", "pos": figure["pos"], "hang": "up",
                  "normal": [r4(math.cos(a)), r4(-math.sin(a)), 0.0], "kinds": ["figurehead"], "shown": True})
    lantern = {"block": "game:lantern-small-up", "attributes": {"material": "tinbronze", "lining": "plain", "glass": "quartz"}}
    start = [dict(lantern, mount="h:" + h["code"], yaw=0.0) for h in hooks if "light" in h.get("kinds", [])]

    # ---------------------------------------------------------------- swivel guns on the quarterdeck's cap (note 103)
    # (25 October 2026, the user: "gun port gates are trash here") her quarterdeck's 4 carriage guns gone with its railing;
    # a swivel gun (berço) in a socket on the bulwark's cap rail, 2 a side: the fittings session's gun, its place here
    # (the cap's top on its middle line, level and outboard; the shrouds' feet 1.25 or more along the rail, outside the
    # barrel's 1.0 swing)
    cap_top = (LV["quarterdeckSide"] + REP["capRail"]["h"]) / 1000.0 - F.WL
    cap_z = LV["quarterdeckSide"] / 1000.0 - 0.015
    _in_at = REP["capRail"]["inAtRibs"]

    def cap_in(x):
        """The cap's width inboard of her planking's outside at x (blocks), as the build made it at each rib."""
        return float(np.interp(x * 1000.0, [q[0] for q in _in_at], [q[1] for q in _in_at])) / 1000.0
    for side, sgn in (("port", -1.0), ("starboard", 1.0)):
        for n_, sx in enumerate(sorted(LAY.get("swivels", [])), 1):
            cap_z = sheer_at(L, sx) - 0.015
            cap_top = sheer_at(L, sx) + REP["capRail"]["h"] / 1000.0 - F.WL
            mid = hb(sx, cap_z) - (REP["capRail"]["in"] - REP["capRail"]["out"]) / 2000.0
            # ((27 October 2026, the fittings session's "rail": [inboard, outboard]) how far the cap reaches in and out from
            # the socket within the barrel's reach along it (its muzzle's 0.62), from the cap's own section: the runtime keeps
            # barrel and tail clear over that much)
            reach = LAY.get("swivelReach", 1.05)
            xs_ = np.linspace(sx - reach, sx + reach, 25)
            r_in = max(mid - (hb(x, cap_z) - cap_in(x)) for x in xs_)
            r_out = max(hb(x, cap_z) + REP["capRail"]["out"] / 1000.0 - mid for x in xs_)
            # (and as drawn: the cap's top faces' corners within that reach, their loft between stations reaching further)
            for q in M["groups"]["exterior"]:
                if sum(q[7 + 8 * k] for k in range(4)) < 3.6:
                    continue
                for k in range(4):
                    x, y, z = q[1 + 8 * k: 4 + 8 * k]
                    if abs(x - sx) <= reach and abs(y - cap_top) < 0.01 and z * sgn > mid - 0.6:
                        r_in, r_out = max(r_in, mid - z * sgn), max(r_out, z * sgn - mid)
            fixtures.append({"type": "gun", "kind": "swivel", "code": f"swivel-{side}-{n_}", "pos": v4((sx, cap_top, sgn * mid)),
                             "out": [0.0, 0.0, sgn], "trainLimits": [-100.0, 100.0], "elevationLimits": [-25.0, 40.0],
                             "rail": [math.ceil(r_in * 100) / 100, math.ceil(r_out * 100) / 100]})

    # ---------------------------------------------------------------- stairs, the hatch (chalk notes 18, 22)
    modular = []
    for st in LAY["stairs"]:
        fx_, fz_ = st["foot"]
        hx2, hz_ = st["head"]
        up = "-x" if hx2 < fx_ else "+x"
        at_x = fx_ / 1000.0 - 0.5 if up == "-x" else fx_ / 1000.0 + 0.5
        modular.append({"kind": "stair", "at": [r4(at_x), r4(F.y_of(fz_)), r4(-st.get("y", 0.0) / 1000.0)], "up": up,
                        "rise": r4((hz_ - fz_) / 1000.0), "length": int(round(abs(hx2 - fx_) / 1000.0)), "code": "stair-" + st["name"]})
    # (24 October 2026, the user: "add default partition wall with doors and window to rear to form cabin, similar to how
    # in current test") the steerage's front on the quarterdeck's front line, the pieces a player places (as carrack-m's
    # great cabin): oak plank partitions three rows up to the quarterdeck, a door either side of the quarterdeck stair
    # (two rows), a window (caravel_lines.CABIN); the pieces stand in the cell aft of the line, inside the cabin. None in
    # the top row behind the stair's head (its last tread meets that row's top)
    cab = LAY.get("cabin")
    if cab:
        lx = cab["x"] / 1000.0
        half_in = min(hb_in(lx - 0.5, Zd + 0.5), hb_in(lx - 0.5, Zq - 0.3))
        cols = [j for j in range(-3, 4) if abs(j) + 0.5 <= half_in - 0.05]
        for j in cols:
            for row in range(3):
                y_ = r4(deck + row)
                at = [r4(lx), y_, float(j)]
                if j in cab["doors"] and row < 2:
                    if row == 0:
                        modular.append({"kind": "door", "at": at, "along": "z", "code": f"door-cabin-{'port' if j < 0 else 'starboard'}",
                                        "block": "game:door-sleek-windowed-oak", "open": False})
                    continue
                if j in cab["windows"] and row == 1:
                    modular.append({"kind": "window", "at": at, "along": "z",
                                    "code": f"window-cabin-{'port' if j < 0 else 'starboard' if j > 0 else 'middle'}"})
                    continue
                if row == 2 and any(abs(st["head"][0] - cab["x"]) < 1 and abs(-st.get("y", 0.0) / 1000.0 - j) < 0.5 for st in LAY["stairs"]):
                    continue
                modular.append({"kind": "partition", "at": at, "along": "z", "code": f"partition@{int(lx) - 1},{y_},{j},z"})
    # (25 October 2026, note 105, draft D) the user's galley (ship #227) as her start pieces: its walls, hearth and oven
    # with the design's pieces, the tables and the storage vessel on their floor places (laid first: the oven stands on its
    # table)
    kit = LAY.get("kitchen")
    if kit:
        for door in kit.get("doors", []):
            modular.append({"kind": "door", "at": [door["at"][0], r4(deck), door["at"][1]], "along": door["along"],
                            "anchor": door["anchor"], "into": door["into"], "code": door["code"],
                            "block": "game:door-sleek-windowed-oak", "open": False})
        for window in kit.get("windows", []):
            modular.append({"kind": "window", "at": [window["at"][0], r4(deck + window["row"]), window["at"][1]],
                            "along": window["along"], "code": window["code"]})
        for w_ in kit["walls"]:
            for c_ in w_["cells"]:
                for row in w_["rows"]:
                    if w_["line"] == "x" and any(v["at"] == [w_["at"], c_] and v["row"] == row for v in kit.get("windows", [])):
                        continue
                    y_ = r4(deck + row)
                    if w_["line"] == "x":
                        modular.append({"kind": "partition", "at": [r4(w_["at"]), y_, float(c_)], "along": "z",
                                        "code": f"partition@{int(w_['at']) - 1},{y_},{c_},z", **({"anchor": w_["anchor"]} if w_.get("anchor") else {})})
                    else:
                        modular.append({"kind": "partition", "at": [r4(c_ + 0.5), y_, r4(w_["at"])], "along": "x",
                                        "code": f"partition@{int(c_)},{y_},{w_['at']},x", **({"anchor": w_["anchor"]} if w_.get("anchor") else {})})
        hc, oc = kit["hearth"]["cell"], kit["oven"]["cell"]
        modular.append({"kind": "hearth", "at": [r4(hc[0] + 0.5), r4(deck), float(hc[1])], "facing": kit["hearth"]["facing"],
                        "block": kit["hearth"]["block"], "code": "hearth"})
        modular.append({"kind": "oven", "at": [r4(oc[0] + 0.5), r4(deck + kit["oven"].get("level", 0)), float(oc[1])],
                        "facing": kit["oven"]["facing"], "code": "oven"})
        for t_ in kit["things"]:
            i_, zc_ = t_["cell"]
            start.append({"block": t_["block"], "mount": f"f:main:{int(i_)}:{int(zc_) - 1}", "yaw": 0.0})
    # (31 October 2026, the user: "next build place shelves into cargo hold") a row of shelves along each side of her hold,
    # a plank slab a block over the outer floor cells (the cells under them free for chests; the middle row the way along);
    # none beside the main mast and its pump
    for i_ in [i for i in range(-9, 4) if i != -3]:
        for k_ in (-2, 0):
            start.append({"block": "game:plankslab-oak-down-free", "mount": f"hb:hold:{i_}:{k_}:1", "yaw": 0.0})
    hxs, hys = LAY["hatch"]["x"], LAY["hatch"]["y"]
    hzc = -(hys[0] + hys[1]) / 2000.0                  # (model Y port = game z negative)
    modular.append({"kind": "hatch", "at": [r4((hxs[0] + hxs[1]) / 2000.0), r4(deck), r4(hzc)],
                    "size": [int((hxs[1] - hxs[0]) / 1000), int((hys[1] - hys[0]) / 1000)],
                    "block": "game:trapdoor-solid-oak-1", "open": False, "code": "hatch-main", "coaming": 0.25, "doubleLeaf": True, "hinge": "x"})

    # ---------------------------------------------------------------- collision: the masts (chalk note 11)
    ox, oy, oz = D["gridOrigin"]
    nx, ny, nz = D["gridSize"]
    V_ = D["voxelSize"]
    raw = bytearray(zlib.decompress(base64.b64decode(D["solid"])))
    n_set = 0
    for m in D["masts"]:
        for i in range(nx):
            x = ox + (i + 0.5) * V_
            if abs(x - m["x"]) > m["radius"] + V_:
                continue
            for k in range(nz):
                z = oz + (k + 0.5) * V_
                if (x - m["x"]) ** 2 + (z - m["z"]) ** 2 > (m["radius"] + 0.02) ** 2:
                    continue
                for j in range(ny):
                    y = oy + (j + 0.5) * V_
                    if m["y0"] <= y <= m["y1"]:
                        n_ = (i * ny + j) * nz + k
                        if not raw[n_ >> 3] & (1 << (n_ & 7)):
                            raw[n_ >> 3] |= 1 << (n_ & 7)
                            n_set += 1
    # (chalk note 15) nothing of her stands on air just over a deck: the mod takes that for a stair's tread and keeps the
    # cells round it clear of pieces (a rib's head leaning in with the tumblehome did it at the quarterdeck's break).
    # Within 1.25 over each deck, such a voxel's column is filled down to the deck
    def solid_at(i, j, k):
        n_ = (i * ny + j) * nz + k
        return raw[n_ >> 3] >> (n_ & 7) & 1

    n_fill = 0
    for dk in D["decks"]:
        j0 = int(round((dk["y"] - oy) / V_))
        for i in range(nx):
            x = ox + (i + 0.5) * V_
            if not (dk["x0"] <= x <= dk["x1"]):
                continue
            for k in range(nz):
                if not solid_at(i, j0 - 1, k):
                    continue
                for j in range(j0 + 1, min(ny, j0 + 6)):
                    if solid_at(i, j, k) and not solid_at(i, j - 1, k) and not any(solid_at(i, jj, k) for jj in range(j0, j)):
                        for jj in range(j0, j):
                            n_ = (i * ny + jj) * nz + k
                            raw[n_ >> 3] |= 1 << (n_ & 7)
                            n_fill += 1
                        break
    D["solid"] = base64.b64encode(zlib.compress(bytes(raw), 9)).decode()
    # (the lateen session) each mast's radius its widest, its heel's: the lee shrouds hang off it
    for m in D["masts"]:
        m["radius"] = r4(math.ceil(masts[m["code"]]["r0"] / 10.0) / 100.0)

    # ---------------------------------------------------------------- the water kept out of her (chalk note 24)
    # the hull's exact half-breadth from her lines (the box clip before held the water down round her curved bow:
    # gaps between the water and her side); the carrack generator's way (water_clip)
    keel_y, deck_y = D["keelY"], D["deckY"]
    X0, DX, DY = -16.5, 0.125, 0.25                 # (note 73: her stern 1 further aft; the counter's transom 1 more)
    Y0 = keel_y - 0.5
    xs = np.arange(X0, 11.0 + 1e-9, DX)
    ys = np.arange(Y0, deck_y + 1.0 + 1e-9, DY)
    widths = []
    for y in ys:
        z_model = y + F.WL
        widths.append([0.0] * len(xs) if z_model < CL.LIFT else [round(float(v), 3) for v in CL.half_breadth(xs, z_model)])
    wl_w = CL.half_breadth(xs, F.WL)
    inside = xs[wl_w > 1e-6]
    bow, stern = float(inside.max()), float(inside.min())
    ccx = (bow + stern) / 2
    margin = 0.7
    while True:
        hl = (bow - stern) / 2 + margin
        hwid = float(wl_w.max()) + margin
        if all(((x - ccx) / hl) ** 4 + (w / hwid) ** 2 < 1 for x, w in zip(xs, wl_w) if w > 0):
            break
        margin += 0.05
    water_clip = {"centerX": r4(ccx), "halfLength": r4(hl), "halfWidth": r4(hwid), "height": 0.1,
                  "dryOutline": {"x0": X0, "dx": DX, "y0": r4(Y0), "dy": DY, "nx": len(xs), "ny": len(ys),
                                 "halfWidths": [v for row in widths for v in row]}}

    # ---------------------------------------------------------------- the guns and their lids (chalk notes 14, 17)
    # the carrack's gun fitted to hers (caravel_gun.py), over the exporter's plain one: its meshes and moving parts go;
    # each gun housed behind a lid, the waist's a side-hinged port lid, the quarterdeck's a swinging railing section
    guns = [f for f in fixtures if f.get("type") == "gun" and f.get("kind") != "swivel"]     # (the swivels are the runtime's)
    gun_codes = {f["code"] for f in guns}

    def of_gun(code):
        return any(code == g_ or code.startswith(g_ + "-") for g_ in gun_codes)

    recoil = {p["code"]: p["distance"] for p in parts if p["code"] in gun_codes and p.get("kind") == "slide"}
    parts[:] = [p for p in parts if not of_gun(p["code"])]
    for grp in [k for k in M["groups"] if k.startswith("part:") and of_gun(k[5:])]:
        del M["groups"][grp]
    posts = sorted((p for p in L["railings"]["posts"] if p[0][1] > 0 and p[0][0] <= L["quarterdeckFront"]), key=lambda p: p[0][0])

    def rail_line(x, y):
        """The quarterdeck railing's middle line's half-breadth (blocks) at ship-local x, y: between its posts' lines."""
        zm = (y + F.WL) * 1000.0
        ys = [b[1] + (t[1] - b[1]) * (zm - b[2]) / (t[2] - b[2]) for b, t in posts]
        return float(np.interp(x * 1000.0, [b[0] for b, _ in posts], ys)) / 1000.0

    def outer(x, y):
        return hb(x, round(y + F.WL, 3))

    gun_checks, problems = [], []
    for f in guns:
        code = f["code"]
        s = 1.0 if f["out"][2] > 0 else -1.0
        c = math.floor(f["pos"][0])
        y = f["pos"][1] + CG.RING_Y
        on_qd = abs(f["pos"][1] - qd) < 1e-6
        if on_qd and LV["quarterdeckSide"] <= LV["quarterdeck"] + 1:
            bolts = [np.array([x, y, s * (rail_line(x, y) - CG.POST_HALF)]) for x in (c, c + 1.0)]
        else:
            bolts = [np.array([x, y, s * (outer(x, y) - CL.SKIN)]) for x in (c + 0.03, c + 0.97)]
        info = CG.add_gun(d, f, bolts)
        parts += CG.moving_parts(code, info, recoil[code])
        # (24 October 2026, chalk note 68: "gun port gate inside is different color than hull inside and cannot be
        # painted") the waist lid's inside the hull's inside planking, the quarterdeck's lid the railing's wood; both
        # painted with them (paint_lids)
        lid = CG.rail_lid(d, f, rail_line, f["port"][1], f["port"][3], wood="oak") if on_qd and LV["quarterdeckSide"] <= LV["quarterdeck"] + 1 else CG.waist_lid(d, f, outer, inside="tarred-in")
        parts.append(lid)
        f["lid"], f["housed"] = lid["code"], recoil[code]
        # its checks: the barrel clear of its carriage and inside the master build's gun; the lid swung clear of her side
        # (the post or the cap rail), clear of the gun run out and laid to its limits, the muzzle in behind it housed
        el = f["elevationLimits"]
        own, inside = CG.own_clearance(el), CG.within_master(el)
        if on_qd and LV["quarterdeckSide"] <= LV["quarterdeck"] + 1:
            def side_ok(p, c=c, s=s):
                w = []
                for x_, y_, z_ in p:
                    ln = rail_line(c, y_)
                    in_post = min(abs(x_ - c), abs(x_ - c - 1)) < CG.POST_HALF and abs(s * z_ - ln) < CG.POST_HALF
                    w.append(-0.01 if in_post else min(9.0, s * z_ - rail_line(x_, y_) - 0.07 if y_ > f["port"][3] else 9.0))
                return min(w)
            inner_face = lambda x_, y_: rail_line(x_, y_) - CG.RAIL_T / 2
        else:
            def side_ok(p, s=s):
                return min(s * z_ - outer(x_, y_) for x_, y_, z_ in p)
            inner_face = lambda x_, y_: outer(x_, y_) + 0.004
        swing = CG.lid_sweep(d, lid, side_ok)
        open_pts = CG.lid_points(d, lid, 1.0)
        laid = min(float(np.min(np.linalg.norm(open_pts[:, None, :] - CG.gun_posed_points(f, t, e)[None, :, :], axis=2)))
                   for t in f["trainLimits"] for e in el)
        housed_pts = np.vstack([CG.gun_posed_points(f, t, e, recoil[code]) for t in (0.0,) + tuple(f["trainLimits"]) for e in el])
        px0, _, px1, _ = f["port"]
        near = housed_pts[(housed_pts[:, 0] > px0 - 0.1) & (housed_pts[:, 0] < px1 + 0.1)]
        behind = min(inner_face(x_, y_) - s * z_ for x_, y_, z_ in near)
        gun_checks.append({"gun": code, "barrel_clear_of_carriage": round(own, 3), "inside_master_gun": round(inside, 3),
                           "lid_swing_clear_of_side": round(swing, 3), "lid_open_from_gun_laid": round(laid, 3),
                           "muzzle_housed_behind_lid": round(behind, 3)})
        for k_, v_ in gun_checks[-1].items():
            if k_ != "gun" and v_ < 0.0:
                problems.append(f"{code}: {k_} {v_}")
    print("[layout] guns:", json.dumps(gun_checks), flush=True)
    if problems:
        raise SystemExit("[layout] the guns don't fit: " + "; ".join(problems))

    # ---------------------------------------------------------------- write
    for f in fixtures:
        offset = LAY["fittings"].get("gunCrewOffsets", {}).get(f.get("code"))
        if offset and "seat" in f:
            f["seat"] = v4(np.array(f["seat"]) + offset)
    D["borderPlaces"]=border_places(L)
    D["beamFrames"] = [{"deck": "main", "stations": [r4((x + .5)) for x in CL.RIBS],
                        "top": r4(deck - .2), "width": .2, "depth": .4,
                        "interrupted": [x+.5 for x in CL.RIBS if hxs[0]/1000 < x+.5 < hxs[1]/1000],
                        "existing": [[r4(hxs[0]/1000-.5),r4(deck-.6),z-.1,r4(hxs[1]/1000+.5),r4(deck-.2),z+.1] for z in [-.6,.6]] +
                                    [[r4(x0),r4(deck-.6),-.5,r4(x1),r4(deck-.2),.5] for x0,x1 in [(hxs[0]/1000-.2,hxs[0]/1000),(hxs[1]/1000,hxs[1]/1000+.2)]]}]
    D["fixtures"] = fixtures
    D["movingParts"] = parts
    D["sails"] = sails
    D["hooks"] = hooks
    D["startPieces"] = start
    D["modularPieces"] = modular
    D["waterClip"] = water_clip
    # (24 October 2026, chalk note 82, the user: no panel places on the caravel; the build-place session's design flag)
    D["panelPlaces"] = False
    D["rigCheck"] = {"standing": standing}
    # what a player's pieces must leave clear, as the carrack's design lists it (the mod keeps the fixtures' margins too)
    def box(what, x0, y0, z0, x1, y1, z1):
        return {"what": what, "box": [r4(x0), r4(y0), r4(z0), r4(x1), r4(y1), r4(z1)]}
    keep = [box("pump", px - 0.3, deck, pz - 1.05, px + 0.3, deck + 2.0, pz + 1.05),
            box("capstan", cx - 1.3, deck, cz - 1.3, cx + 1.3, deck + 2.0, cz + 1.3),
            box("hatch", hxs[0] / 1000.0, deck, -hys[1] / 1000.0, hxs[1] / 1000.0, deck + 1.0, -hys[0] / 1000.0),
            box("helm", wx - 1.15, qd, -0.7, wx + 0.15, qd + 2.0, 0.7),
            box("spawn", mz - 0.5, deck, -1.5, mz + 0.5, deck + 2.0, -0.5),
            box("plaque", pfx - 0.02, deck + 1.25, -0.25, pfx + 0.12, deck + 1.7, 0.25)]
    D["keepClear"] = [k for k in D.get("keepClear", []) if k.get("what", "").startswith("gun")] + keep
    # each gun place's deck cells: the slots under its working room (its trail swept either way, its crew behind it),
    # which the mod won't let be taken up while a gun stands there (docs/legacy/slots.md §7)
    for gp in D.get("gunPlaces", []):
        f = next(x for x in fixtures if x.get("code") == gp["gun"])
        b, seat = f["box"], f["seat"]
        sgn = 1 if f["pos"][2] > 0 else -1
        z_in = seat[2] - sgn * 0.85
        z_out = b[5] if sgn > 0 else b[2]
        x0, x1 = f["pos"][0] - 1.4, f["pos"][0] + 1.4
        za, zb = min(z_in, z_out), max(z_in, z_out)
        gp["cells"] = [s["id"] for s in D.get("deckSlots", [])
                       if s.get("kind") is None and abs(s["box"][4] - f["pos"][1]) < 0.05 and s["box"][0] < x1 - 0.02 and s["box"][3] > x0 + 0.02
                       and s["box"][2] < zb - 0.02 and s["box"][5] > za + 0.02]
    # Keep only the cut ends of a beam in this visibility format. Whole carlings around connected player openings
    # need a union of the current openings; two owner/facing predicates cannot express that without duplicate faces.
    for group in [g for g in M["groups"] if g.startswith("slot:deck")]:
        quads, pairs = M["groups"][group], M["slotQuads"][group]
        kept, kp = [], []
        for i, q in enumerate(quads):
            facing = pairs[2 * i + 1]
            slot = D["deckSlots"][facing] if facing >= 0 else None
            if slot and slot.get("kind") == "beam":
                b = slot["box"]
                if any(not (b[0] - 0.001 <= q[1 + 8 * k] <= b[3] + 0.001 and
                            b[2] - 0.001 <= q[3 + 8 * k] <= b[5] + 0.001) for k in range(4)):
                    continue
            kept.append(q)
            kp += pairs[2 * i:2 * i + 2]
        M["groups"][group], M["slotQuads"][group] = kept, kp
    # The flat floor has width without height change: its metal sheets use physical X/Z distance in plan.
    for group, quads in M["groups"].items():
        if group.endswith("bottomclad"):
            for q in quads:
                if all(abs(q[2 + 8 * k] - D["keelY"]) < 0.001 for k in range(4)):
                    for k in range(4):
                        q[4 + 8 * k], q[5 + 8 * k] = q[1 + 8 * k] * 16, abs(q[3 + 8 * k]) * 16
    n_merged = merge_slot_triangles(M)
    square_slot_faces(M, D)
    n_plain = merge_plain_triangles(M)
    print(f"[layout] triangles paired into quads: {n_merged} in the slot groups, {n_plain} elsewhere", flush=True)
    n_cut, n_pieces = split_tiles(M)
    print(f"[layout] quads cut at their texture's tile edges: {n_cut}, into {n_pieces}", flush=True)
    D["paintZones"] = PAINT_ZONES
    M["paintQuads"] = paint_quads(M, L, F)          # (before the fittings' quads join the groups: they aren't painted)
    M["paintFaces"] = {}
    qf_x = L["quarterdeckFront"] / 1000.0
    before = {g: len(v) for g, v in M["groups"].items()}
    counts = merge_quads(d, M, lambda p: p[1] < deck - 0.1 or (p[0] < qf_x and p[1] < qd - 0.1))
    n_cut2, n_pieces2 = split_tiles(M, before)
    print(f"[layout] the fittings' quads cut at their texture's tile edges: {n_cut2}, into {n_pieces2}", flush=True)
    M["paintQuads"].update(paint_quads(M, L, F, only_lids=True))      # (note 68: the guns' lids with her hull and rails)
    # (a face moved out can meet another there: again, a few times at most)
    n_pushed, n_left, passes = 0, 1, 0
    while n_left and passes < 3:
        moved_, n_left = deflicker(M, D)
        n_pushed += moved_
        passes += 1
    if n_left:
        import overlap_check as OC
        from mesh_contacts import trim_contacts
        clipped = trim_contacts(M,OC.find_clashes(M,OC.empty_slots(D)))
        print(f"[layout] slot contact polygons clipped: {clipped}",flush=True)
        n_col = collapse_slivers(M, D)
        n_left = len(__import__("overlap_check").find_clashes(M, __import__("overlap_check").empty_slots(D)))
        print(f"[layout] slivers among them collapsed: {n_col}")
    print(f"[layout] overlapping faces in one plane: {n_pushed} pushed 2 mm out in {passes} passes, {n_left} left")
    if n_left:
        import overlap_check as OC
        for ga, na, gb, nb, ov, c, *_ in OC.find_clashes(M, OC.empty_slots(D))[:6]:
            print(f"[layout]   overlapping: {ga}[{na}] and {gb}[{nb}] at {[round(v, 3) for v in c]}, {ov:.4f} m2")
        with open(os.path.join(ROOT,"models","caravel","export","caravel-overlap-debug.json"),"w") as debug:
            json.dump(M,debug,separators=(",",":"))
        # (the user's rule: no visible faces overlapping; tools/caravel/overlap_check.py lists them)
        raise SystemExit(f"[layout] {n_left} pairs of faces still overlap in one plane: not written")
    import overlap_check as OC
    metal_slivers = collapse_slivers(copper_view(M), D)
    print(f"[layout] copper duplicate slivers collapsed: {metal_slivers}", flush=True)
    metal_moved, metal_left, metal_passes = 0, 1, 0
    while metal_left and metal_passes < 3:
        moved_, metal_left = deflicker(M, D, copper=True)
        metal_moved += moved_
        metal_passes += 1
        print(f"[layout] copper pass {metal_passes}: {moved_} faces separated, {metal_left} overlaps left", flush=True)
    if metal_left:
        from mesh_contacts import trim_contacts
        metal=copper_view(M)
        trim_contacts(metal,OC.find_clashes(metal,OC.empty_slots(D)))
        collapse_slivers(copper_view(M), D)
        metal_left = len(OC.find_clashes(copper_view(M), OC.empty_slots(D)))
    if metal_left:
        for ga, na, gb, nb, ov, c, *_ in OC.find_clashes(copper_view(M), OC.empty_slots(D))[:6]:
            print(f"[layout] copper clash: {ga}[{na}] / {gb}[{nb}], {ov:.6f} m2 at {c}", flush=True)
        raise SystemExit(f"[layout] {metal_left} copper overlaps: not written")
    if OC.find_clashes(M, OC.empty_slots(D)):
        raise SystemExit("[layout] copper correction disturbed the wooden alternative: not written")
    out = os.environ.get("CARAVEL_LAYOUT_OUT")             # (a check: somewhere else than the mod's assets)
    with open(os.path.join(out, "caravel.json") if out else os.path.join(ASSETS, "config", "ships", f"{CODE}.json"), "w") as f:
        json.dump(D, f, indent=1)
    with open(os.path.join(out, "caravel-mesh.json") if out else os.path.join(ASSETS, "config", "shipmeshes", f"{CODE}.json"), "w") as f:
        json.dump(M, f, separators=(",", ":"))
    print(f"[layout] {len(fixtures)} fixtures, {len(parts)} moving parts, {len(sails)} sails, {len(hooks)} hooks, "
          f"{len(modular)} pieces, {len(standing)} shrouds; masts' collision +{n_set} voxels, {n_fill} under overhangs; quads {counts}")


if __name__ == "__main__":
    main()
