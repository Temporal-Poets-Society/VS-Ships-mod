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


def find_clashes(mesh, empty):
    """[(group a, quad a, group b, quad b, overlap m2, middle, normal, area a, area b)] of the drawn faces in one plane,
    facing one way, overlapping."""
    P = polys(mesh, empty)
    buckets = defaultdict(list)
    for i, (g, n, pts, nrm) in enumerate(P):
        dist = float(nrm @ pts[0])
        key = (round(nrm[0], 2), round(nrm[1], 2), round(nrm[2], 2), round(dist * 1000))
        buckets[key].append(i)
    out = []
    for key, idx in buckets.items():
        if len(idx) < 2:
            continue
        for a_ in range(len(idx)):
            ga, na, pa, nra = P[idx[a_]]
            ba_lo, ba_hi = pa.min(0), pa.max(0)
            for b_ in range(a_ + 1, len(idx)):
                gb, nb, pb, nrb = P[idx[b_]]
                if ga == gb and na == nb:
                    continue
                if np.any(pb.max(0) < ba_lo - 1e-6) or np.any(pb.min(0) > ba_hi + 1e-6):
                    continue
                if math.degrees(math.acos(min(1.0, float(nra @ nrb)))) > 0.5:
                    continue
                if abs(float(nra @ (pb[0] - pa[0]))) > 0.001:
                    continue
                ov = overlap_area(pa, pb, nra)
                if ov > 2e-6:
                    out.append((ga, na, gb, nb, ov, (pa.mean(0) + pb.mean(0)) / 2, nra,
                                overlap_area(pa, pa, nra), overlap_area(pb, pb, nra)))
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
