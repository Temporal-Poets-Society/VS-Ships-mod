"""Find faces of a ship's mesh that would flicker: two drawn faces in one plane, facing the same way, overlapping
(22 October 2026, the user: "For ANY visible elements - check 3D model overlaps at ends ... visible surface overlaps
create texture flickers ... This is also rule for any new or modified models"). Plain Python and numpy:

    python tools/caravel/overlap_check.py [assets/hansa/config/shipmeshes/caravel.json] [--all]

Faces are the mesh file's quads (a triangle is a quad with its last corner doubled), as a new ship draws them: a slot
group's quad only if its owner is in and its facing slot (if any) is out as designed (docs/legacy/slots.md §3); a
sail's and the shot's groups are left out (drawn alone). Two faces clash when their planes agree within 1 mm and 0.5°,
their normals point the same way, and their outlines overlap by more than 2 mm2. Prints the clashes by group pair with
where they are (ship-local blocks); exits 1 if there are any.
"""
import json
import math
import os
import sys
from collections import defaultdict

import numpy as np

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def polys(mesh, empty_slots):
    """empty_slots: {"deck": set, "hull": set} (the slot:hull* groups' indices are the hull panels', the others' the
    deck cells')."""
    out = []
    for g, quads in mesh["groups"].items():
        # (the copper bottom is the tarred one's twin, one of the two drawn)
        if g.startswith("sail:") or g == "shot" or g.endswith("bottomclad"):
            continue
        pairs = mesh.get("slotQuads", {}).get(g)
        empty = empty_slots["hull" if g.startswith("slot:hull") else "deck"]
        for n, q in enumerate(quads):
            if pairs is not None:
                own, fac = pairs[2 * n], pairs[2 * n + 1]
                if not ((own < 0 or own not in empty) and (fac < 0 or fac in empty)):
                    continue
            pts = [np.array(q[1 + 8 * k: 4 + 8 * k]) for k in range(4)]
            uniq = [pts[0]]
            for p in pts[1:]:
                if all(np.linalg.norm(p - u) > 1e-6 for u in uniq):
                    uniq.append(p)
            if len(uniq) < 3:
                continue
            nrm = np.cross(uniq[1] - uniq[0], uniq[2] - uniq[0])
            ln = np.linalg.norm(nrm)
            if ln < 1e-9:
                continue
            nrm /= ln
            # the drawn normal's side (the quad's stored normals), so a back-to-back pair isn't a clash
            stored = np.mean([q[6 + 8 * k: 9 + 8 * k] for k in range(4)], axis=0)
            if np.dot(stored, nrm) < 0:
                nrm = -nrm
            out.append((g, n, np.array(uniq), nrm))
    return out


def overlap_area(a, b, nrm):
    """Area of overlap of two convex polygons in one plane (Sutherland-Hodgman in the plane's own 2D frame)."""
    e1 = np.cross(nrm, [0, 1.0, 0] if abs(nrm[1]) < 0.9 else [1.0, 0, 0])
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(nrm, e1)
    A = [(float(p @ e1), float(p @ e2)) for p in a]
    B = [(float(p @ e1), float(p @ e2)) for p in b]

    def area(P):
        return 0.5 * sum(P[i][0] * P[(i + 1) % len(P)][1] - P[(i + 1) % len(P)][0] * P[i][1] for i in range(len(P)))

    if area(A) < 0:
        A = A[::-1]
    if area(B) < 0:
        B = B[::-1]

    def clip(subject, c1, c2):
        def inside(p):
            return (c2[0] - c1[0]) * (p[1] - c1[1]) - (c2[1] - c1[1]) * (p[0] - c1[0]) >= -1e-12

        def inter(p, q):
            dx1, dy1 = c2[0] - c1[0], c2[1] - c1[1]
            dx2, dy2 = q[0] - p[0], q[1] - p[1]
            den = dx1 * dy2 - dy1 * dx2
            if abs(den) < 1e-15:
                return q
            t = (dx1 * (c1[1] - p[1]) - dy1 * (c1[0] - p[0])) / den
            return (p[0] + t * dx2, p[1] + t * dy2)

        out = []
        for i in range(len(subject)):
            cur, prev = subject[i], subject[i - 1]
            if inside(cur):
                if not inside(prev):
                    out.append(inter(prev, cur))
                out.append(cur)
            elif inside(prev):
                out.append(inter(prev, cur))
        return out

    P = A
    for i in range(len(B)):
        if not P:
            return 0.0
        P = clip(P, B[i], B[(i + 1) % len(B)])
    return abs(area(P)) if len(P) >= 3 else 0.0


def empty_slots(d):
    """The slots a new ship starts without (their owners' quads not drawn, their facing quads drawn)."""
    return {"deck": {n for n, s in enumerate(d.get("deckSlots") or []) if s.get("start") == "empty"},
            "hull": {n for n, s in enumerate(d.get("hullSlots") or []) if s.get("start") == "empty"}}


TOL = 0.001                 # (1 mm: two faces' planes this close, and 0.5 degrees, are one plane)


def find_clashes(mesh, empty):
    """[(group a, quad a, group b, quad b, overlap m2, middle, normal, area a, area b)] of the drawn faces in one plane,
    facing one way, overlapping. (22 October 2026, after Codex's review: a face near a rounding edge of its normal's or
    its plane's bucket was missed, and every pair in a plane was compared) Faces by their normal on a 0.02 grid (a face
    near a cell's edge in the neighbouring cell too), then swept along their plane's distance (1 mm) and along one axis
    in the plane (their boxes 1 mm apart or nearer)."""
    P = polys(mesh, empty)
    items = []
    for g, n, pts, nrm in P:
        items.append((g, n, pts, nrm, float(nrm @ pts[0]), pts.min(0) - TOL, pts.max(0) + TOL))
    byn = defaultdict(list)
    for i, it in enumerate(items):
        keys = [[]]
        for c in it[3]:
            x = c * 50
            k = int(math.floor(x + 0.5))
            near = [k] + ([k - 1] if x - (k - 0.5) < 0.25 else []) + ([k + 1] if (k + 0.5) - x < 0.25 else [])
            keys = [kk + [v] for kk in keys for v in near]
        for kk in keys:
            byn[tuple(kk)].append(i)
    seen = set()
    out = []
    for idx in byn.values():
        if len(idx) < 2:
            continue
        idx.sort(key=lambda i: items[i][4])
        # clusters of planes within 1 mm of the next
        start = 0
        for e in range(1, len(idx) + 1):
            if e < len(idx) and items[idx[e]][4] - items[idx[e - 1]][4] <= TOL:
                continue
            cl = idx[start:e]
            start = e
            if len(cl) < 2:
                continue
            nrm0 = items[cl[0]][3]
            ax = int(np.argmin(np.abs(nrm0)))           # an axis in the plane
            cl.sort(key=lambda i: items[i][5][ax])
            active = []
            for i in cl:
                ga, na, pa, nra, da, loa, hia = items[i]
                active = [j for j in active if items[j][6][ax] >= loa[ax]]
                for j in active:
                    gb, nb, pb, nrb, db, lob, hib = items[j]
                    if (ga == gb and na == nb) or (min(i, j), max(i, j)) in seen:
                        continue
                    if np.any(hib < loa) or np.any(lob > hia):
                        continue
                    if float(nra @ nrb) < math.cos(math.radians(0.5)):
                        continue
                    if abs(float(nra @ (pb[0] - pa[0]))) > TOL:
                        continue
                    ov = overlap_area(pa, pb, nra)
                    if ov > 2e-6:
                        seen.add((min(i, j), max(i, j)))
                        out.append((gb, nb, ga, na, ov, (pa.mean(0) + pb.mean(0)) / 2, nrb,
                                    overlap_area(pb, pb, nrb), overlap_area(pa, pa, nra)))
                active.append(i)
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    path = args[0] if args else os.path.join(ROOT, "assets", "hansa", "config", "shipmeshes", "caravel.json")
    defp = path.replace("shipmeshes", "ships")
    mesh = json.load(open(path))
    empty = {"deck": set(), "hull": set()}
    if os.path.exists(defp):
        empty = empty_slots(json.load(open(defp)))
    found = find_clashes(mesh, empty)
    clashes = defaultdict(list)
    for ga, na, gb, nb, ov, c, nrm, aa, ab in found:
        clashes[tuple(sorted((ga, gb)))].append((ov, c))
    P = polys(mesh, empty)
    total = sum(len(v) for v in clashes.values())
    print(f"{os.path.basename(path)}: {len(P)} faces drawn, {total} overlapping pairs in one plane")
    for pair, lst in sorted(clashes.items(), key=lambda kv: -len(kv[1])):
        lst.sort(key=lambda t: -t[0])
        where = sorted({(round(c[0], 1), round(c[1], 1), round(c[2], 1)) for _, c in lst})
        print(f"  {pair[0]} / {pair[1]}: {len(lst)} pairs, {sum(o for o, _ in lst):.3f} m2; at e.g. "
              + ", ".join(f"({x}, {y}, {z})" for x, y, z in where[:6]))
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
