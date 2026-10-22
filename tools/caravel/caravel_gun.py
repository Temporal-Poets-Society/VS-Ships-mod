"""
The caravel's guns and their lids (22 October 2026, the user's chalk notes 14: "Bad detail cannons. Magelan had much
better", and 17: "no cannon pull in animation - no parking like in Magelan"). Drawn by caravel_layout.py over the master
model's plain guns (bed, two truck pairs, a cone), which the exporter still writes and the layout replaces.

The gun is the carrack's (tools/shipgen/gun.py: a cast bronze barrel turned from its lathe profile, with its base ring,
reinforce rings, chase girdle, muzzle swell, cascabel, trunnions and rimbases; a carriage of two stepped cheeks with cap
squares, a transom, a bed and a stool bed, on two axletrees with four trucks, hubs and linchpins; the breeching from ring
bolts on her side through eyebolts on the cheeks round the cascabel), fitted to her lighter gun as the master build lays it
out (build_caravel.py, the report's "guns" and "gunGeometry"): the barrel from its button 0.62 aft of the trunnions to the
muzzle 1.05 forward of them, its axis 0.625 over the deck (her ports' middle), the carriage 0.7 wide from 0.65 aft of the
trunnions to 0.35 forward, trucks of 0.15. Everything stays inside the master build's gun, whose run-out place it found
clear of her side, railing and gates (check() confirms it), except the button and the cheeks' tops, inboard of it.

Housed (the carrack's way, ShipGuns): a gun with a lid ("lid": its moving part, source "open:<gun>") starts run in by its
recoil, its lid shut; laying it opens the lid and runs it out, Shift + right-click houses it again. Her lids:
  waist      a side-hinged port lid (the user: "maybe can be gate type sideways"): a panel of her planks on her side over
             the port, under the cap rail, hinged on its after edge outside, swinging out LID_OPEN degrees
  quarterdeck a section of her railing between the posts over the gun (stiles, rails and balusters as the railing's),
             hinged on the after post's fore outer edge, swinging out the same way

Gun-local coordinates as gun.py's: x out along the barrel, y up from the deck, z across; origin on the deck under the
trunnions (the fixture's pos).
"""
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "shipgen"))
import gun as G                                  # noqa: E402

AXIS_Y = 0.625                                   # the barrel's axis over the deck (her ports' middle)
BUTTON_X, MUZZLE_X = -0.62, 1.05                 # the cascabel's button and the muzzle's face (the trunnions at 0)
# the carrack's profile, its breech end scaled KA, its chase KF, its radii KR (the bore as the carrack's: the shot is)
KA = -BUTTON_X / -G.PROFILE[0][0]
KF = MUZZLE_X / G.PROFILE[-2][0]
KR = 0.82
PROFILE = [(round(x * (KA if x < 0 else KF), 4), r if n >= len(G.PROFILE) - 2 else round(r * KR, 4))
           for n, (x, r) in enumerate(G.PROFILE)]
CASCABEL = (round(G.CASCABEL[0] * KA, 4), AXIS_Y)
TRUNNION_R, TRUNNION_Z, RIMBASE_R, RIMBASE_Z = 0.045, 0.265, 0.06, 0.11
TRUCK_R, TRUCK_Z, TRUCK_T = 0.15, 0.293, 0.07   # (TRUCK_R as gun.py's: its moving parts turn the trucks by it)
AXLES = {"fore": 0.18, "aft": -0.36}
AXLE_HALF, AXLE_Y, AXLE_Z = 0.055, (0.095, 0.21), 0.35
CHEEK_Z, CHEEK_T = 0.21, 0.08
# the cheeks, stepped down aft (from x, to x, top, bottom): over the trunnions, aft of them lower, the last step aft of
# the after axletree to the breech, where the breeching's eyebolts are
CHEEKS = ((-0.10, 0.35, AXIS_Y - TRUNNION_R - 0.005, AXLE_Y[1]), (-0.36, -0.10, 0.42, AXLE_Y[1]), (-0.62, -0.36, 0.30, 0.10))
BEDS = ((-0.65, AXLES["aft"] - AXLE_HALF, 0.0, 0.05), (AXLES["aft"] + AXLE_HALF, AXLES["fore"] - AXLE_HALF, 0.12, 0.17))
TRANSOM = (0.27, 0.33, 0.14, 0.38)
EYE = (-0.57, 0.30 + 0.03)
ROPE_R = 0.028
RING_Y = 0.55                                    # the ring bolts on her side, over the deck
# the master build's gun this one stays inside (build_caravel.py: its cone barrel, its bed and trucks)
OLD = {"barrel": (-0.55, 0.17, 1.05, 0.12), "x": (-0.65, 0.35), "half": 0.35}

LID_T = 0.06                                     # the waist lid's thickness, outside her planking
LID_OPEN = 110.0                                 # how far a lid swings out (degrees)
RAIL_T = 0.05                                    # the railing section's frame, across
POST_HALF = 0.075                                # the railing's posts (150 square)
KNUCKLE_R = 0.016                                # the railing section's hinge knuckles, against the post's corner


def radius(x):
    """The barrel's outer radius at gun-local x (the widest where the profile steps)."""
    best = 0.0
    for (x0, r0), (x1, r1) in zip(PROFILE[:-2], PROFILE[1:-1]):
        if x0 - 1e-9 <= x <= x1 + 1e-9:
            best = max(best, r0, r1) if x1 - x0 < 1e-9 else max(best, r0 + (r1 - r0) * (x - x0) / (x1 - x0))
    return best


def _ring(d, group, mat, c, a, b, rad, r, n=8, half=False):
    """A ring (or half of one) of rod r about c in the plane of unit vectors a, b."""
    top = math.pi if half else 2 * math.pi
    pts = [c + rad * (math.cos(t) * a + math.sin(t) * b) for t in np.linspace(0, top, n + 1)]
    for p0, p1 in zip(pts[:-1], pts[1:]):
        d.add_cylinder(group, mat, p0, p1, r, r, sides=5, caps=False)


def add_gun(d, f, ring_bolts, wood="timber", planks="planks", bronze="bronze", iron="iron", rope="rope", dark="black"):
    """
    Her gun at its fixture f (pos, out), run out and laid square, in the moving groups the mod expects (gun.py's top):
    part:<code> the carriage, -barrel, -trucks-fore / -aft, -breeching-a/b and -a/b-out from the ring bolts (ship-local
    points on her side, drawn in "exterior"). Returns what gun.moving_parts needs.
    """
    code = f["code"]
    fr = G.Frame(f["pos"], f["out"])
    g, gb = f"part:{code}", f"part:{code}-barrel"
    G.lathe(d, gb, bronze, fr, PROFILE, axis_y=AXIS_Y, sid=f"{code}-barrel")
    G._disk(d, gb, dark, fr(PROFILE[-1][0], AXIS_Y, 0), fr.u, G.BORE / 2 + 0.002)
    for s in (1, -1):
        d.add_cylinder(gb, bronze, fr(0, AXIS_Y, s * RIMBASE_Z), fr(0, AXIS_Y, s * (CHEEK_Z - CHEEK_T / 2)), RIMBASE_R,
                       RIMBASE_R, sides=12, smooth=False)
        d.add_cylinder(gb, bronze, fr(0, AXIS_Y, s * (CHEEK_Z - CHEEK_T / 2)), fr(0, AXIS_Y, s * TRUNNION_Z), TRUNNION_R,
                       TRUNNION_R, sides=12, smooth=False)
    for s in (1, -1):
        for x0, x1, top, bottom in CHEEKS:
            G._box(d, g, wood, fr, (x0, bottom, s * CHEEK_Z - CHEEK_T / 2), (x1, top, s * CHEEK_Z + CHEEK_T / 2))
        # the cap square over the trunnion, its straps down the cheek either side
        G._box(d, g, iron, fr, (-0.06, AXIS_Y + TRUNNION_R, s * CHEEK_Z - 0.045), (0.06, AXIS_Y + TRUNNION_R + 0.02, s * CHEEK_Z + 0.045))
        for xb in (-0.06, 0.06):
            G._box(d, g, iron, fr, (xb - 0.01, AXIS_Y - 0.05, s * CHEEK_Z - 0.036), (xb + 0.01, AXIS_Y + TRUNNION_R + 0.012, s * CHEEK_Z + 0.036))
        for xb, yb in ((0.25, 0.36), (-0.23, 0.32), (-0.5, 0.2)):
            d.add_cylinder(g, iron, fr(xb, yb, s * (CHEEK_Z + CHEEK_T / 2)), fr(xb, yb, s * (CHEEK_Z + CHEEK_T / 2 + 0.012)),
                           0.018, 0.018, sides=6, smooth=False)
    x0, x1, y0, y1 = TRANSOM
    G._box(d, g, wood, fr, (x0, y0, -CHEEK_Z + CHEEK_T / 2), (x1, y1, CHEEK_Z - CHEEK_T / 2))
    for x0, x1, y0, y1 in BEDS:
        G._box(d, g, planks, fr, (x0, y0, -CHEEK_Z + CHEEK_T / 2), (x1, y1, CHEEK_Z - CHEEK_T / 2))
    for ax in AXLES.values():
        G._box(d, g, wood, fr, (ax - AXLE_HALF, AXLE_Y[0], -AXLE_Z), (ax + AXLE_HALF, AXLE_Y[1], AXLE_Z))
    for name, ax in AXLES.items():
        tg = f"part:{code}-trucks-{name}"
        for s in (1, -1):
            zo = s * (TRUCK_Z + TRUCK_T / 2)
            d.add_cylinder(tg, planks, fr(ax, TRUCK_R, s * (TRUCK_Z - TRUCK_T / 2)), fr(ax, TRUCK_R, zo), TRUCK_R, TRUCK_R,
                           sides=12, smooth=False)
            d.add_cylinder(tg, iron, fr(ax, TRUCK_R, zo), fr(ax, TRUCK_R, zo + s * 0.01), 0.045, 0.045, sides=8, smooth=False)
            # a mark on the truck's face, to see it turn
            G._box(d, tg, planks, fr, (ax - 0.018, TRUCK_R + 0.055, zo), (ax + 0.018, TRUCK_R + 0.12, zo + s * 0.005))
            # the linchpin through the axletree's end
            G._box(d, g, iron, fr, (ax - 0.01, TRUCK_R - 0.028, zo + s * 0.012), (ax + 0.01, TRUCK_R + 0.028, zo + s * 0.022))
    # the breeching: from each ring bolt to the eyebolt on its side's cheek, through it and round the cascabel's neck
    breeching = []
    for i, rb in enumerate(ring_bolts):
        rb = np.asarray(rb, float)
        d.add_cylinder("exterior", iron, rb - fr.u * 0.015, rb + fr.u * 0.02, 0.04, 0.04, sides=8, smooth=False)
        _ring(d, "exterior", iron, rb - fr.u * 0.05, fr.up, fr.w, 0.035, 0.008, n=8)
        zs = 1.0 if float(np.dot(rb - fr.o, fr.w)) > 0 else -1.0
        eye = fr(EYE[0], EYE[1], zs * CHEEK_Z)
        G._box(d, g, iron, fr, (EYE[0] - 0.03, EYE[1] - 0.033, zs * CHEEK_Z - 0.026), (EYE[0] + 0.03, EYE[1] - 0.02, zs * CHEEK_Z + 0.026))
        _ring(d, g, iron, eye + fr.up * -0.02, fr.u, fr.up, 0.03, 0.008, n=6, half=True)
        rc = f"{code}-breeching-{'ab'[i % 2]}"
        anchor = rb - fr.u * 0.05
        side = fr(CASCABEL[0], CASCABEL[1], zs * 0.04)
        d.add_rope(anchor, eye, r=ROPE_R, group=f"part:{rc}-out", mat=rope)
        d.add_rope(eye, side, r=ROPE_R, group=f"part:{rc}", mat=rope)
        breeching.append({"code": f"{rc}-out", "anchor": anchor, "tip": eye, "follow": code})
        breeching.append({"code": rc, "anchor": eye, "tip": side, "follow": f"{code}-barrel", "anchorFollow": code})
    for k in range(8):
        a0, a1 = 2 * math.pi * k / 8, 2 * math.pi * (k + 1) / 8
        d.add_cylinder(gb, rope, fr(CASCABEL[0], AXIS_Y + 0.04 * math.cos(a0), 0.04 * math.sin(a0)),
                       fr(CASCABEL[0], AXIS_Y + 0.04 * math.cos(a1), 0.04 * math.sin(a1)), 0.02, 0.02, sides=5, caps=False)
    return {"pivot": np.asarray(f["pivot"], float), "trunnion": np.asarray(f["trunnion"], float), "frame": fr,
            "axles": {name: fr(ax, TRUCK_R, 0) for name, ax in AXLES.items()}, "breeching": breeching}


def moving_parts(code, info, recoil):
    return G.moving_parts(code, info, recoil)


# ---------------------------------------------------------------------------------------------------------- lids
def _quad(d, group, mat, pts, want, uvs):
    """A quad facing `want` (its corners turned round if they face the other way)."""
    pts = [np.asarray(p, float) for p in pts]
    n = np.cross(pts[1] - pts[0], pts[3] - pts[0]) + np.cross(pts[3] - pts[2], pts[1] - pts[2])
    if np.dot(n, want) < 0:
        pts, uvs = [pts[1], pts[0], pts[3], pts[2]], [uvs[1], uvs[0], uvs[3], uvs[2]]
    d.add_quad(group, mat, pts, uvs=uvs)


def waist_lid(d, f, outer, outside="tarred", inside="planks", iron="iron"):
    """
    The waist port's lid: a panel of her planks over the port (0.04 past its jambs and under its sill, its top under the
    cap rail), lying on her side outside (outer(x, y): her outer half-breadth, ship-local), LID_T thick, hinged on its
    after edge outside: two strap hinges across it from their knuckles, a ring bolt near its fore edge for its lanyard.
    Returns its moving part (swung out LID_OPEN degrees about the hinge by "open:<gun>").
    """
    code = f["code"]
    grp = f"part:{code}-lid"
    s = 1.0 if f["out"][2] > 0 else -1.0
    px0, py0, px1, py1 = f["port"]
    x0, x1, y0, y1 = px0 - 0.04, px1 + 0.04, py0 - 0.04, py1 - 0.02

    def P(x, y, out):
        return np.array([x, y, s * (outer(x, y) + (LID_T if out else 0.004))])

    xs = np.linspace(x0, x1, 5)
    ys = [y0] + [v for v in np.arange(math.ceil((y0 - py0) / 0.25) * 0.25 + py0, y1, 0.25) if y0 + 1e-6 < v < y1 - 1e-6] + [y1]
    for out, mat in ((True, outside), (False, inside)):
        for ya, yb in zip(ys[:-1], ys[1:]):
            for xa, xb in zip(xs[:-1], xs[1:]):
                uv = [(16 * (xa - x0), 16 * (y1 - ya)), (16 * (xb - x0), 16 * (y1 - ya)), (16 * (xb - x0), 16 * (y1 - yb)),
                      (16 * (xa - x0), 16 * (y1 - yb))]
                _quad(d, grp, mat, [P(xa, ya, out), P(xb, ya, out), P(xb, yb, out), P(xa, yb, out)], (0, 0, s if out else -s), uv)
    # its edges, through its thickness
    t16 = 16 * (LID_T - 0.004)
    for (xa, ya), (xb, yb), want in (((x0, y0), (x1, y0), (0, -1, 0)), ((x0, y1), (x1, y1), (0, 1, 0)),
                                     ((x0, y0), (x0, y1), (-1, 0, 0)), ((x1, y0), (x1, y1), (1, 0, 0))):
        ln = 16 * math.hypot(xb - xa, yb - ya)
        _quad(d, grp, outside, [P(xa, ya, False), P(xb, yb, False), P(xb, yb, True), P(xa, ya, True)], want,
              [(0, t16), (ln, t16), (ln, 0), (0, 0)])
    # the hinge's line: her side's own lean at the lid's after edge, outside it
    h0, h1 = P(x0, y0, True), P(x0, y1, True)
    axis = (h1 - h0) / np.linalg.norm(h1 - h0)
    for yh in (y0 + 0.14, y1 - 0.14):
        a, b = P(x0 + 0.02, yh, True), P(x0 + 0.42, yh, True)
        nrm = np.cross(P(x0 + 0.22, yh + 0.05, True) - P(x0 + 0.22, yh - 0.05, True), b - a)
        nrm = nrm / np.linalg.norm(nrm) * (1.0 if nrm[2] * s > 0 else -1.0)
        d.add_beam_mesh(grp, iron, a + nrm * 0.008, b + nrm * 0.008, 0.045, 0.012, up=tuple(nrm))
        k = h0 + axis * float(np.dot(P(x0, yh, True) - h0, axis))
        d.add_cylinder(grp, iron, k - axis * 0.05, k + axis * 0.05, 0.018, 0.018, sides=6, smooth=False)
    eye = P(x1 - 0.1, (y0 + y1) / 2, True) + np.array([0, 0, s * 0.012])
    d.add_box_mesh(grp, iron, eye, (0.06, 0.06, 0.02))
    _ring(d, grp, iron, eye + np.array([0, -0.03, s * 0.012]), np.array([1.0, 0, 0]), np.array([0, -1.0, 0]), 0.03, 0.008, n=6, half=True)
    return {"code": f"{code}-lid", "axisPoint": [round(float(v), 4) for v in (h0 + h1) / 2],
            "axisDir": [round(float(v), 5) for v in axis], "source": f"open:{code}", "angle": round(-s * LID_OPEN, 3)}


def rail_lid(d, f, line, bottom, top, wood="timber", iron="iron"):
    """
    The quarterdeck gun's lid: the railing's section between the two posts over the gun (line(x, y): the railing's
    middle line's half-breadth, ship-local; bottom, top: the bottom rail's top and the top rail's underside), a small
    gate in the railing's own style (22 October 2026, chalk note 45): the member between the posts every other span has,
    the posts' size, held by a light rail under the top rail and over the bottom one, hinged against the after post's
    fore outer edge (two knuckles on straps). Returns its moving part.
    """
    code = f["code"]
    grp = f"part:{code}-lid"
    s = 1.0 if f["out"][2] > 0 else -1.0
    c = math.floor(f["pos"][0])
    x0, x1 = c + POST_HALF + 0.005, c + 1 - POST_HALF - 0.005
    y0, y1 = bottom + 0.005, top - 0.012          # (it rises a little as it swings out about the post's lean)

    def P(x, y, off=0.0):
        return np.array([x, y, s * (line(x, y) + off)])

    for ya, yb in ((y0, y0 + 0.06), (y1 - 0.06, y1)):                                    # its rails
        ym = (ya + yb) / 2
        d.add_beam_mesh(grp, wood, P(x0, ym), P(x1, ym), RAIL_T, yb - ya, up=(0, 1.0, 0))
    # the member, between the rails (not into them: their faces would meet in one plane)
    d.add_beam_mesh(grp, wood, P(c + 0.5, y0 + 0.06), P(c + 0.5, y1 - 0.06), 2 * POST_HALF, 2 * POST_HALF, up=(1.0, 0, 0))
    # the hinge: on the after post's fore outer edge
    h0 = np.array([c + POST_HALF + KNUCKLE_R, y0, s * (line(c, y0) + POST_HALF + KNUCKLE_R)])
    h1 = np.array([c + POST_HALF + KNUCKLE_R, y1, s * (line(c, y1) + POST_HALF + KNUCKLE_R)])
    axis = (h1 - h0) / np.linalg.norm(h1 - h0)
    for yh in (y0 + 0.12, y1 - 0.12):
        k = h0 + axis * ((yh - h0[1]) / axis[1])
        d.add_cylinder(grp, iron, k - axis * 0.045, k + axis * 0.045, KNUCKLE_R, KNUCKLE_R, sides=6, smooth=False)
        a, b = k, P(x0 + 0.28, yh, RAIL_T / 2 + 0.006)
        d.add_beam_mesh(grp, iron, a, b, 0.035, 0.01, up=(0, 0, s))
    return {"code": f"{code}-lid", "axisPoint": [round(float(v), 4) for v in (h0 + h1) / 2],
            "axisDir": [round(float(v), 5) for v in axis], "source": f"open:{code}", "angle": round(-s * LID_OPEN, 3)}


# ---------------------------------------------------------------------------------------------------------- checks
def _rot(axis, deg):
    a = np.asarray(axis, float)
    a = a / np.linalg.norm(a)
    t = math.radians(deg)
    K = np.array([[0, -a[2], a[1]], [a[2], 0, -a[0]], [-a[1], a[0], 0]])
    return np.eye(3) + math.sin(t) * K + (1 - math.cos(t)) * K @ K


def barrel_points(step=0.01, ring=24):
    """The barrel's surface (gun-local rows x, y, z), button to muzzle."""
    pts = []
    for x in np.arange(PROFILE[0][0], MUZZLE_X + 1e-9, step):
        r = radius(x)
        for k in range(ring):
            a = 2 * math.pi * k / ring
            pts.append((x, AXIS_Y + r * math.cos(a), r * math.sin(a)))
    return np.array(pts)


def elevated(p, elev):
    p = np.array(p, float).reshape(-1, 3).copy()
    e = math.radians(elev)
    x, y = p[:, 0].copy(), p[:, 1] - AXIS_Y
    p[:, 0] = x * math.cos(e) - y * math.sin(e)
    p[:, 1] = x * math.sin(e) + y * math.cos(e) + AXIS_Y
    return p


def carriage_boxes():
    """The carriage's boxes the barrel must keep clear of (gun-local lo, hi)."""
    boxes = []
    for s in (1, -1):
        boxes += [((x0, b, s * CHEEK_Z - CHEEK_T / 2), (x1, t, s * CHEEK_Z + CHEEK_T / 2)) for x0, x1, t, b in CHEEKS]
    boxes.append(((TRANSOM[0], TRANSOM[2], -CHEEK_Z), (TRANSOM[1], TRANSOM[3], CHEEK_Z)))
    boxes += [((x0, y0, -CHEEK_Z), (x1, y1, CHEEK_Z)) for x0, x1, y0, y1 in BEDS]
    boxes += [((ax - AXLE_HALF, AXLE_Y[0], -AXLE_Z), (ax + AXLE_HALF, AXLE_Y[1], AXLE_Z)) for ax in AXLES.values()]
    return [(np.array(lo, float), np.array(hi, float)) for lo, hi in boxes]


def own_clearance(elev_limits):
    """The barrel laid anywhere in its range clear of its own carriage, and the breeching off the barrel: the worst margin."""
    pts = barrel_points()
    worst = 9.0
    for e in np.arange(elev_limits[0], elev_limits[1] + 1e-9, 2.0):
        p = elevated(pts, e)
        for lo, hi in carriage_boxes():
            worst = min(worst, float(np.max(np.maximum(lo - p, p - hi), axis=1).min()))
        cas = elevated([CASCABEL[0], CASCABEL[1], 0.0], e)[0]
        for zs in (1, -1):
            a = np.array([EYE[0], EYE[1], zs * CHEEK_Z])
            b = cas + np.array([0, 0, zs * 0.04])
            for t in np.linspace(0.0, 0.85, 35):
                q = elevated(a + (b - a) * t, -e)[0]
                if PROFILE[0][0] <= q[0] <= MUZZLE_X:
                    worst = min(worst, math.hypot(q[1] - AXIS_Y, q[2]) - radius(q[0]) - ROPE_R)
    return worst


def within_master(elev_limits):
    """How far the barrel (laid anywhere) and the carriage stay inside the master build's gun where it was checked clear
    of her side: the barrel's radius under its cone's from its breech on, the carriage within its length and width."""
    x0, r0, x1, r1 = OLD["barrel"]
    worst = 9.0
    for x, r in PROFILE[:-2]:
        if x >= x0:
            worst = min(worst, r0 + (r1 - r0) * (x - x0) / (x1 - x0) - r)
    for lo, hi in carriage_boxes():
        worst = min(worst, lo[0] - OLD["x"][0], OLD["x"][1] - hi[0], OLD["half"] - abs(lo[2]), OLD["half"] - abs(hi[2]))
    worst = min(worst, OLD["half"] - (TRUCK_Z + TRUCK_T / 2 + 0.022))
    return worst


def lid_sweep(d, part, outer_ok, steps=12):
    """The lid's points swung through its arc: the worst of outer_ok(points) (>= 0: clear) over it."""
    grp = "part:" + part["code"]
    pts = np.array([p for q in d.quads if q["group"] == grp for p in q["pts"]], float)
    c = np.asarray(part["axisPoint"], float)
    worst = 9.0
    for k in range(1, steps + 1):
        R = _rot(part["axisDir"], part["angle"] * k / steps)
        worst = min(worst, float(outer_ok((pts - c) @ R.T + c)))
    return worst


def lid_points(d, part, value=1.0):
    grp = "part:" + part["code"]
    pts = np.array([p for q in d.quads if q["group"] == grp for p in q["pts"]], float)
    c = np.asarray(part["axisPoint"], float)
    R = _rot(part["axisDir"], part["angle"] * value)
    return (pts - c) @ R.T + c


def gun_posed_points(f, train, elev, back=0.0):
    """The barrel's surface and the muzzle, ship-local, the gun trained, elevated and run back `back`."""
    fr = G.Frame(f["pos"], f["out"])
    p = elevated(barrel_points(0.02, 16), elev)
    p[:, 0] -= back
    world = np.array([fr(*q) for q in p])
    piv = np.asarray(f["pivot"], float)
    R = _rot((0, G.train_sign(fr.u), 0), train)
    return (world - piv) @ R.T + piv
