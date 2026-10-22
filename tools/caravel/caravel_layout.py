"""The caravel's fittings, rigging, sails and gameplay data, on top of what export_caravel.py takes from the master model
(21 October 2026, the user's list after trying her in the game: sails that flicker and have no roping, no plaque, no
lanterns, no anchor, no pump, the helm blind behind the mizzen; and the chalk notes 11-24). Plain Python and numpy:

    python tools/caravel/caravel_lines.py           (the layout data, models/caravel/caravel-lines.json)
    python tools/caravel/caravel_layout.py          (rerun alone in seconds; export_caravel.py runs it at its end)

Reads models/caravel/export/caravel-def-base.json and caravel-mesh-base.json (the exporter's: the sliced hull, its
collision, the guns, the gates, the deck cells), the lines and the build's report; writes
assets/hansa/config/ships/caravel.json and config/shipmeshes/caravel.json. The fittings are drawn here with the carrack
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
ASSETS = os.path.join(ROOT, "assets", "hansa")
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


# ---------------------------------------------------------------------------------------------------------- meshes
TEXTURES = {
    "rope": "game:block/cloth/reedrope", "cloth": "game:block/linen", "timber": "game:block/wood/debarked/oak",
    "planks": "game:block/wood/planks/oak1", "iron": "game:block/metal/tarnished/iron",
    "bronze": "game:block/metal/plate/tinbronze", "spar": "game:block/wood/debarked/pine",
    "tarred": "game:block/wood/planks/oak1++3~hansa:block/tar", "black": "game:block/black",
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
                    s_ = next(c for c in cb if key(c) not in shared)
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
            b = slots[own]["box"]
            y = b[4] if sg > 0 else b[1]
            if not all(abs(quads[n][2 + 8 * k] - y) < 1e-3 for n in ns for k in range(4)):
                continue
            # u, v as affine functions of (x, z), from the first face with a non-degenerate triangle
            q = quads[ns[0]]
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
        keep_q = [q for n, q in enumerate(quads) if n not in drop]
        keep_p = [v for n in range(len(quads)) if n not in drop for v in (pairs[2 * n], pairs[2 * n + 1])]
        M["groups"][g] = keep_q + [r for r, _ in add]
        M["slotQuads"][g] = keep_p + [v for _, o in add for v in (o, -1)]


# ---------------------------------------------------------------------------------------------------------- paint
PAINT_ZONES = [
    {"code": "topsides", "name": "Topsides: her sides outside from the waterline up to the rail, the transom, the gates", "kind": "panel", "default": "tarred"},
    {"code": "rail-caps", "name": "Cap rails on the waist's bulwarks", "kind": "moulding", "default": "oak"},
    {"code": "quarterdeck-railing", "name": "Quarterdeck's railing: posts, rails and balusters", "kind": "carving", "default": "oak"},
    {"code": "waist-bulwarks", "name": "Inside of the bulwarks over the main deck", "kind": "panel", "default": "tarred"},
    {"code": "bulwark-frames", "name": "Frames (ribs) standing inside the bulwarks", "kind": "moulding", "default": "timber"},
    {"code": "hold", "name": "Hold's walls: the inside planking", "kind": "panel", "default": "tarred"},
    {"code": "hold-timbers", "name": "Hold's frames and beams, the keelson, the hatchway's carlings", "kind": "moulding", "default": "timber"},
    {"code": "hold-ceiling", "name": "Hold's ceiling: the main deck's underside", "kind": "ceiling", "default": "deck"},
    {"code": "steerage-ceiling", "name": "Steerage's ceiling: the quarterdeck's underside", "kind": "ceiling", "default": "deck"},
]


def paint_quads(M, L, F):
    """(21 October 2026, the user: "no painting ability (see magelan)") Each quad of her hull, its rails and timbers in
    a paint zone, by what it is (its texture), where it is and which way it faces: per group [start, end, zone, ...]."""
    tex = M["textures"]
    t_hull, t_hull2 = tex.index("game:block/wood/planks/oak1++3~hansa:block/tar"), tex.index("game:block/wood/planks/oak4++3~hansa:block/tar")
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
            return "topsides"
        if t == t_hull:
            return "topsides"
        if t == t_hull2:
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
        if not (group in ("exterior", "interior") or group.startswith("slot:") or group.startswith("part:gate-")):
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
def deflicker(M, D):
    """(22 October 2026, the user: "For ANY visible elements - check 3D model overlaps ... visible surface overlaps
    create texture flickers ... This is also rule for any new or modified models") Drawn faces in one plane, facing one
    way and overlapping (overlap_check.py) flicker. Each cluster of them (faces joined by overlaps) is spread out along
    its normal 2 mm a face: a slot's faces first and staying (the hull's and the decks' meet their neighbours exactly),
    then the larger, the smaller ones in front. Returns (moved, still overlapping after)."""
    import overlap_check as OC
    empty = OC.empty_slots(D)
    found = OC.find_clashes(M, empty)
    link, nrm_of, area_of = {}, {}, {}
    for ga, na, gb, nb, ov, c, nrm, aa, ab in found:
        a, b = (ga, na), (gb, nb)
        link.setdefault(a, set()).add(b)
        link.setdefault(b, set()).add(a)
        nrm_of[a] = nrm_of[b] = nrm
        area_of[a], area_of[b] = aa, ab
    seen, moved = set(), 0
    for start in link:
        if start in seen:
            continue
        comp, todo = [], [start]
        seen.add(start)
        while todo:
            f = todo.pop()
            comp.append(f)
            for o in link[f]:
                if o not in seen:
                    seen.add(o)
                    todo.append(o)
        comp.sort(key=lambda f: (not f[0].startswith("slot:"), -area_of[f]))
        nrm = nrm_of[comp[0]]
        for k, (g_, n_) in enumerate(comp[1:], start=1):
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

    def rope(a, b, group="exterior", r=0.025):
        d.add_rope(a, b, r=r, group=group)

    def follow(code, follow_part, anchor, tip, sail=None, interior=False, r=0.025):
        rope(anchor, tip, "part:" + code, r)
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

    bumpkin_end = F.g(transom_x(Q) - 2400, 0, Q + 150)
    for name, code in (("latina main", "main"), ("latina mizzen", "mizzen")):
        sp = next(s for s in rig["sails"] if s["name"] == name)["pts"]           # tack, head, clew (mm)
        pts = [F.g(*p) for p in sp]
        area = round(0.5 * float(np.linalg.norm(np.cross(pts[1] - pts[0], pts[2] - pts[0]))), 1)
        sails.append(lateen_sail(d, code, pts, area))
        yd = yards[name]
        mast = masts[code]
        mx = mast["x"] / 1000.0
        cross = F.g(yd["cross"][0], 350, yd["cross"][1])
        tack = F.g(yd["tack"][0], 350, yd["tack"][1])
        top = F.g(mast["x"], 0, mast["top"] - 300)
        clew = pts[2]
        if code == "main":
            sheet_to = np.array([L["quarterdeckFront"] / 1000.0 - 0.05, qd + 0.95, -0.9])     # the quarterdeck's front rail
            xs_ = 9.0
            tack_to = np.array([xs_, sheer_at(L, xs_) - F.WL - 0.15, 0.0])                  # the stem head, inside
        else:
            sheet_to = bumpkin_end                                                           # out on the bumpkin
            tack_to = np.array([L["quarterdeckFront"] / 1000.0 - 0.05, qd + 0.95, 0.9])
        follow(f"sheet-{code}", f"yard-{code}", sheet_to, clew, sail=code, r=0.03)
        follow(f"tack-{code}", f"yard-{code}", tack_to, tack, r=0.03)
        follow(f"halyard-{code}", f"yard-{code}", top, cross, r=0.035)
        heel_deck = deck if code == "main" else qd
        rope(top, np.array([mx - mast["r0"] / 1000.0 - 0.08, heel_deck + 0.35, 0.18]), r=0.03)      # its fall to the deck
    # shrouds: the main's three a side to its cap rails aft of the gate, the mizzen's two to the quarterdeck's rail
    for code, xs, top_drop in (("main", (-1.25, -2.05, -2.85), 1.2), ("mizzen", (-9.75, -10.25), 0.9)):
        mast = masts[code]
        mx = mast["x"] / 1000.0
        head = mast["top"] / 1000.0 - top_drop
        for x in xs:
            if code == "main":
                zs = sheer_at(L, x)
                y_low, half = zs + 0.08 - F.WL, hb(x, zs - 0.02) - 0.08
            else:
                zr = LV["quarterdeckRail"] / 1000.0
                y_low, half = zr - F.WL - 0.02, hb(x, zr - 0.02) - 0.12
            for sgn in (1, -1):
                a = np.array([mx, head - F.WL, sgn * 0.18])
                b = np.array([x, y_low, sgn * half])
                rope(a, b, r=0.03)
                standing.append(v4(list(b) + list(a)) + [0.04])
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
    pf, pparts = CF.pump(d, px, pz, deck, CL.DECK_T, hold, 1.0)
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
    # mast, 2.62 over its deck; the stern lantern on the carrack's crane from a post on the taffrail's middle
    hooks = []
    for code, deck_of_mast in (("main", deck), ("mizzen", qd)):
        mast = masts[code]
        y = deck_of_mast + 2.62
        heel, top_ = F.y_of(mast["heel"]), F.y_of(mast["top"])
        r_ = (mast["r0"] + (mast["r1"] - mast["r0"]) * (y - heel) / (top_ - heel)) / 1000.0
        hp = CF.mast_band(d, mast["x"] / 1000.0, y, r_)
        hooks.append({"code": f"lantern-{code}mast", "pos": v4(hp), "hang": "down", "kinds": ["light"]})
    tr_top = max((q for q in L["railings"]["top"] if abs(q[1]) < 600), key=lambda q: -q[0])
    rail_top = (tr_top[2] - 50 + 70) / 1000.0 - F.WL
    plate = CF.stern_lantern(d, tr_top[0] / 1000.0 - 0.1, tr_top[0] / 1000.0 + 0.02, rail_top)
    hooks.append({"code": "lantern-stern", "pos": v4(plate), "hang": "up", "kinds": ["light"], "shown": True})
    lantern = {"block": "game:lantern-small-up", "attributes": {"material": "tinbronze", "lining": "plain", "glass": "quartz"}}
    start = [dict(lantern, mount="h:" + h["code"]) for h in hooks]

    # ---------------------------------------------------------------- stairs, the hatch (chalk notes 18, 22)
    modular = []
    for st in LAY["stairs"]:
        fx_, fz_ = st["foot"]
        hx2, hz_ = st["head"]
        up = "-x" if hx2 < fx_ else "+x"
        at_x = fx_ / 1000.0 - 0.5 if up == "-x" else fx_ / 1000.0 + 0.5
        modular.append({"kind": "stair", "at": [r4(at_x), r4(F.y_of(fz_)), r4(-st.get("y", 0.0) / 1000.0)], "up": up,
                        "rise": r4((hz_ - fz_) / 1000.0), "length": int(round(abs(hx2 - fx_) / 1000.0)), "code": "stair-" + st["name"]})
    hxs, hys = LAY["hatch"]["x"], LAY["hatch"]["y"]
    hzc = -(hys[0] + hys[1]) / 2000.0                  # (model Y port = game z negative)
    modular.append({"kind": "hatch", "at": [r4((hxs[0] + hxs[1]) / 2000.0), r4(deck), r4(hzc)],
                    "size": [int((hxs[1] - hxs[0]) / 1000), int((hys[1] - hys[0]) / 1000)],
                    "block": "game:trapdoor-solid-oak-1", "open": False, "code": "hatch-main"})

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

    # ---------------------------------------------------------------- the water kept out of her (chalk note 24)
    # the hull's exact half-breadth from her lines (the box clip before held the water down round her curved bow:
    # gaps between the water and her side); the carrack generator's way (water_clip)
    keel_y, deck_y = D["keelY"], D["deckY"]
    X0, DX, DY = -14.5, 0.125, 0.25
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
    guns = [f for f in fixtures if f.get("type") == "gun"]
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
        if on_qd:
            bolts = [np.array([x, y, s * (rail_line(x, y) - CG.POST_HALF)]) for x in (c, c + 1.0)]
        else:
            bolts = [np.array([x, y, s * (outer(x, y) - CL.SKIN)]) for x in (c + 0.03, c + 0.97)]
        info = CG.add_gun(d, f, bolts)
        parts += CG.moving_parts(code, info, recoil[code])
        lid = CG.rail_lid(d, f, rail_line, f["port"][1], f["port"][3]) if on_qd else CG.waist_lid(d, f, outer)
        parts.append(lid)
        f["lid"], f["housed"] = lid["code"], recoil[code]
        # its checks: the barrel clear of its carriage and inside the master build's gun; the lid swung clear of her side
        # (the post or the cap rail), clear of the gun run out and laid to its limits, the muzzle in behind it housed
        el = f["elevationLimits"]
        own, inside = CG.own_clearance(el), CG.within_master(el)
        if on_qd:
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
    print("[layout] guns:", json.dumps(gun_checks))
    if problems:
        raise SystemExit("[layout] the guns don't fit: " + "; ".join(problems))

    # ---------------------------------------------------------------- write
    D["fixtures"] = fixtures
    D["movingParts"] = parts
    D["sails"] = sails
    D["hooks"] = hooks
    D["startPieces"] = start
    D["modularPieces"] = modular
    D["waterClip"] = water_clip
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
                       if abs(s["box"][4] - f["pos"][1]) < 0.05 and s["box"][0] < x1 - 0.02 and s["box"][3] > x0 + 0.02
                       and s["box"][2] < zb - 0.02 and s["box"][5] > za + 0.02]
    n_merged = merge_slot_triangles(M)
    square_slot_faces(M, D)
    D["paintZones"] = PAINT_ZONES
    M["paintQuads"] = paint_quads(M, L, F)          # (before the fittings' quads join the groups: they aren't painted)
    M["paintFaces"] = {}
    qf_x = L["quarterdeckFront"] / 1000.0
    counts = merge_quads(d, M, lambda p: p[1] < deck - 0.1 or (p[0] < qf_x and p[1] < qd - 0.1))
    n_pushed, n_left = deflicker(M, D)
    print(f"[layout] overlapping faces in one plane: {n_pushed} pushed 2 mm out, {n_left} left")
    out = os.environ.get("CARAVEL_LAYOUT_OUT")             # (a check: somewhere else than the mod's assets)
    with open(os.path.join(out, "caravel.json") if out else os.path.join(ASSETS, "config", "ships", f"{CODE}.json"), "w") as f:
        json.dump(D, f, indent=1)
    with open(os.path.join(out, "caravel-mesh.json") if out else os.path.join(ASSETS, "config", "shipmeshes", f"{CODE}.json"), "w") as f:
        json.dump(M, f, separators=(",", ":"))
    print(f"[layout] {len(fixtures)} fixtures, {len(parts)} moving parts, {len(sails)} sails, {len(hooks)} hooks, "
          f"{len(modular)} pieces, {len(standing)} shrouds; masts' collision +{n_set} voxels, {n_fill} under overhangs; quads {counts}")


if __name__ == "__main__":
    main()
