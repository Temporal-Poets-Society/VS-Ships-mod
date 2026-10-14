"""
Hansa's own ship's boat: a launch about 5.6 long and 1.9 wide for four, of the kind a carrack carried on her boat
gallows, for going ashore and for rowing up rivers where she can't go. It's one of Hansa's own vessels like the
carrack (not vanilla's boat): you stand and walk in it, it rides the waves, it's dry inside.

  - two rowing thwarts: right-click one to sit and row (W/S), A/D pulls round; the oars come out while you're there and
    lie stowed on the thwarts otherwise; two rowers go faster than one
  - the stern sheets with the tiller: the helm (A/D the rudder, W/S the sail, Space the grapnel)
  - a mast that folds down forward (right-click it): up, a lug sail like vanilla's sailboat's, sailing by the wind
  - two places for a chest, a storage vessel or a crate, in the bow and aft of the rowers
  - Shift + right-click it, empty, to pick it up

Writes the design (config/ships/skiff.json, its shapes and mesh), the entity, the item (with a model of boxes for the
inventory and the hand), and close-up views in docs/views/.

Parked (2026-09-30): the user chose vanilla's sailboat made a rowing boat instead (rowboat.py). The launch stays
loadable, for worlds that have one, but has no recipe and isn't in the creative inventory.

  python boat.py [--no-preview]
"""
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
from planking import BANDS, flat_between, skin, sweep_cap, sweep_patch, transom  # noqa: E402
from shipgen import Design, Material, interp  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ASSETS = os.path.join(ROOT, "assets", "hansa")
DOCS = os.path.join(ROOT, "docs")

CODE = "skiff"                 # the design; the item and entity are hansa:boat-skiff
ITEM = "boat-skiff"
B2 = 0.95                      # half beam at the gunwale amidships
KEEL = -0.6                    # bottom of the hull (the keel's underside a little lower)
FLOOR = -0.25                  # top of the bottom boards
GUNWALE = 0.6                  # the gunwale amidships; it sweeps up toward the ends (sheer)
SHEER = 0.25
HULL_T = 0.07                  # the planking's thickness
THWART_Y = 0.25                # top of the thwarts: half a block over the bottom boards
SIDE_TOP = 1.0                 # the sides stop you walking out (a step from a thwart can't reach over them): jump out
END_TOP = 1.45                 # and higher at the ends (from the stern sheets or a thwart you can't step out there)
BOW_LOCKER = 2.25              # the bow is closed off forward of this: a locker under a little foredeck
MIDBODY = (-0.9, 0.5)          # full width between these; narrowing to the transom aft and the stem forward
SECTION = [(-0.6, 0.05), (-0.5, 0.32), (-0.35, 0.62), (-0.2, 0.8), (0.0, 0.92), (0.2, 0.98), (0.45, 1.0), (1.2, 1.03)]
BOW_X = [(-0.6, 2.3), (-0.3, 2.55), (0.0, 2.66), (0.5, 2.78), (1.2, 2.92)]
STERN_X = [(-0.6, -2.5), (-0.3, -2.66), (0.0, -2.74), (1.2, -2.88)]
STERN_P = [(-0.6, 1.8), (0.0, 2.2), (1.2, 2.8)]
TRANSOM_W = [(-0.6, 0.0), (-0.4, 0.2), (0.0, 0.5), (1.2, 0.64)]

# the thwarts are on the collision grid (a quarter block wide, centred on its cells)
MAST_THWART = 1.375            # the mast is stepped through this thwart, in a tabernacle, and folds down forward
ROWERS = {"oars-fwd": 0.375, "oars-aft": -0.875}   # the rowing thwarts (their centres)
STERN_SHEETS = (-2.75, -2.0)   # the benches round the stern, the helmsman's seat
ROWLOCK_DX = 0.35              # the rowlocks, forward of each rowing thwart (rowed facing forward)
MAST_HINGE = (MAST_THWART, 0.75)
MAST_TOP = 3.95
MAST_FOLD = -86.0              # degrees about +z: forward and down, onto the stem head
YARD_A = np.array([MAST_THWART + 0.55, 3.05, 0.12])    # the lug's yard: fore end (lower) and after end (higher)
YARD_B = np.array([MAST_THWART - 1.95, 3.7, 0.12])
SAIL_FOOT = 1.9                # the sail's foot, over the heads of the crew sitting
CARGO = {"cargo": -1.5}        # a place for a chest, a storage vessel or a crate, aft of the rowers
BRACE_DEG_PER_RAD = round(180 / math.pi, 5)


def sheer(x):
    return GUNWALE + SHEER * (np.asarray(x, float) / 2.8) ** 2


def hull_hw(x, y):
    x, y = np.asarray(x, float), np.asarray(y, float)
    sec = B2 * interp(y, SECTION)
    xb = interp(y, BOW_X)
    t = np.clip((x - MIDBODY[1]) / (xb - MIDBODY[1]), 0, None)
    fb = np.where(t < 1, np.clip(1 - t ** 1.8, 0, 1) ** 0.6, 0.0)
    xs = interp(y, STERN_X)
    ts = np.clip((MIDBODY[0] - x) / (MIDBODY[0] - xs), 0, None)
    ps = interp(y, STERN_P)
    wt = interp(y, TRANSOM_W)
    fs0 = np.clip(1 - np.clip(ts, 0, 1) ** ps, 0, 1) ** (1 / ps)
    fs = np.where(ts <= 1, wt + (1 - wt) * fs0, 0.0)
    return np.where(y >= KEEL, sec * np.minimum(fb, fs), 0.0)


def hw(x, y):
    return float(hull_hw(np.array(x, float), np.array(y, float)))


def stern_x(y):
    return float(interp(y, STERN_X))


def bow_x(y):
    return float(interp(y, BOW_X))


def transom_hw(y):
    return float(B2 * interp(y, SECTION) * interp(y, TRANSOM_W))


def inner_hw(x, y):
    """The inside of the planking (the offset outline would do; the boat's planking is thin)."""
    return np.maximum(hull_hw(x, y) - HULL_T, 0.0)


def build():
    d = Design(CODE, -3.25, 3.25, -1.0, 1.5, -1.25, 1.25)
    d.hull_thickness = HULL_T
    d.material(Material("tarred", [f"game:block/wood/planks/oak{i}++3~hansa:block/tar" for i in range(1, 5)], (42, 30, 22), grain=True))
    d.material(Material("oak", [f"game:block/wood/planks/oak{i}" for i in range(1, 5)], (150, 112, 70), grain=True))
    d.material(Material("timber", ["game:block/wood/debarked/oak"], (160, 124, 84), grain=True))
    d.material(Material("pine", ["game:block/wood/debarked/pine"], (196, 160, 112), grain=True))
    d.material(Material("iron", ["game:block/metal/plate/iron"], (70, 70, 75)))
    d.material(Material("cloth", ["game:block/linen"], (235, 228, 210), solid=False, opaque=False))
    d.material(Material("rope", ["game:block/cloth/reedrope"], (120, 95, 60), solid=False, opaque=False))
    d.material(Material("hidden", ["game:block/wood/planks/oak1"], (0, 0, 0), render=False, opaque=False))

    X, Y, Z = d.X, d.Y, d.Z
    hwv = hull_hw(X, Y)
    top = np.where(np.abs(X) > 1.9, END_TOP, SIDE_TOP)
    hull_in = (np.abs(Z) < hwv) & (Y >= KEEL)
    inside = hull_in & (Y < top)
    # the planked hull is drawn by the mesh; the voxels only stop people: the cells at the hull's sides and bottom up
    # to where you'd step over (a little over the gunwale amidships), and the bottom boards
    out = ~hull_in
    shell = np.zeros_like(inside)
    for ax, sh in ((0, 1), (0, -1), (2, 1), (2, -1), (1, 1)):
        nb = np.roll(out, sh, axis=ax)
        idx = [slice(None)] * 3
        idx[ax] = 0 if sh == 1 else -1
        nb[tuple(idx)] = True
        shell |= inside & nb
    # and above the hull's top, over the planking's line, up to SIDE_TOP (the rail you can't step over)
    ring = (np.abs(Z) >= hull_hull_inner(X, Y) - 0.125) & (np.abs(Z) < hwv + 0.05) & (Y >= KEEL) & (Y < SIDE_TOP)
    d.grid[shell | (ring & (hwv > 0.3))] = d.mid("hidden")
    d.box("hidden", -3.25, 3.25, -0.5, FLOOR, -1.25, 1.25, where=inside)          # the bottom boards
    # the thwarts and the stern sheets: seats you step up onto
    for x in [MAST_THWART] + list(ROWERS.values()):
        d.box("hidden", x - 0.125, x + 0.125, 0.0, THWART_Y, -1.25, 1.25, where=inside)
    d.box("hidden", STERN_SHEETS[0] - 0.5, STERN_SHEETS[1], 0.0, THWART_Y, -1.25, 1.25, where=inside)
    d.box("hidden", BOW_LOCKER, 3.25, -0.5, END_TOP, -1.25, 1.25, where=inside)          # the bow locker and foredeck
    d.hide_toward = ~hull_in
    d.hide_applies = hull_in

    hull(d)
    fittings(d)
    rudder_and_tiller(d)
    oars(d)
    mast_and_sail(d)
    fixtures(d)

    d.props = {
        "name": "Ship's boat",
        "length": 5.6, "beam": 2 * B2, "draft": 0.6,
        "hullHalfWidthAtDeck": B2,
        "waterline": 0.0,
        "keelY": KEEL,
        "deckY": GUNWALE,
        "bounds": [[-3.4, KEEL - 0.1, -2.8], [5.2, MAST_TOP + 0.3, 2.8]],
        # under sail (the lug); rowing is rowSpeed for one rower, more for two
        "maxSpeed": 3.2, "acceleration": 0.8, "deceleration": 1.2, "turnRate": 32.0, "rudderSpeed": 180.0,
        "reverseSpeed": 1.2,
        "rowSpeed": 1.9,
        "maxAboard": 4,
        "boardFromWater": True,
        "pickUpItem": "hansa:" + ITEM,
        "claddingPlates": 0,
        "hullSamples": hull_samples(),
        "waterClip": water_clip(),
        "motion": {"heavePeriod": 1.6, "pitchPeriod": 1.4, "rollPeriod": 2.2, "heaveDamping": 0.35, "pitchDamping": 0.35,
                   "rollDamping": 0.25, "maxPitch": 15, "maxRoll": 25, "heelPerWind": 26, "maxHeel": 18,
                   "waterplaneFraction": 0.85},
        # a lug sail: close to the wind for its size, fast on a reach, swinging out to leeward like a lateen
        "sailing": {"noGo": 55, "polar": [[0, 0], [55, 0], [65, 0.55], [90, 0.9], [120, 1.0], [150, 0.9], [180, 0.75]],
                    "fullWind": 0.25, "aback": 0.06, "maxSheet": 75, "slowSteer": 0.4, "tackSteer": 0.7},
    }
    return d


def hull_hull_inner(X, Y):
    return hull_hw(X, Y) - HULL_T


def hull_samples():
    pts = [[round(float(x), 3), KEEL + 0.1, 0.0] for x in np.arange(-2.25, 2.3, 0.5)]
    for y in (0.05, 0.45, 0.8):
        xb, xs = bow_x(y) - 0.1, stern_x(y) + 0.1
        pts.append([round(xb, 3), y, 0.0])
        for zz in (0.0, 0.45):
            if zz < transom_hw(y) - 0.05:
                pts += [[round(xs, 3), y, zz], [round(xs, 3), y, -zz]]
        for x in np.arange(-2.25, 2.3, 0.5):
            w = hw(x, y) - 0.05
            if w > 0.2:
                pts += [[round(float(x), 3), y, round(w, 3)], [round(float(x), 3), y, round(-w, 3)]]
    return pts


def water_clip():
    """The hull's exact half-width on a fine grid, for the dry outline the mod fits for the Sea mod."""
    x0, y0, dx, dy = -3.25, KEEL - 0.25, 0.0625, 0.125
    xs = np.arange(x0, 3.25 + 1e-9, dx)
    ys = np.arange(y0, GUNWALE + SHEER + 0.25 + 1e-9, dy)
    widths = [[round(float(hull_hw(x, y)), 3) for x in xs] for y in ys]
    return {"centerX": 0.0, "halfLength": 3.0, "halfWidth": B2 + 0.3, "height": 0.05,
            "dryOutline": {"x0": x0, "dx": dx, "y0": y0, "dy": dy, "nx": len(xs), "ny": len(ys),
                           "halfWidths": [v for row in widths for v in row]}}


# ==================================================================== the planking
def hull(d):
    def full_length(y):
        return stern_x(y), bow_x(y)

    ys = np.linspace(KEEL, GUNWALE, 400)
    zs = hull_hw(np.zeros_like(ys), ys)
    girth = np.concatenate([[0.0], np.cumsum(np.hypot(np.diff(ys), np.diff(zs)))])
    levels = list(np.interp(np.linspace(0, girth[-1], 7), girth, ys))     # 6 strakes up to the gunwale amidships
    below = [y for y in levels if y <= 0.1]
    above = [y for y in levels if y >= below[-1] - 1e-6]
    skin(d, hull_hw, below, full_length, 40, lambda j, a, b: "tarred", group="bottom", bottom=True)
    skin(d, hull_hw, above, full_length, 40, lambda j, a, b: "oak")
    # inside, from the bottom boards up (under them it isn't seen)
    in_levels = [FLOOR - 0.02] + [y for y in levels if y > FLOOR + 0.05]
    skin(d, hull_hw, in_levels, full_length, 40, lambda j, a, b: "oak", group="auto", offset=HULL_T)
    # the sheer strakes, rising toward the ends: cut to the sweep of the gunwale
    sheer_levels = [GUNWALE, GUNWALE + 0.125, GUNWALE + 0.25]
    xa, xb = stern_x(GUNWALE + 0.25) + 0.01, bow_x(GUNWALE + 0.25) - 0.01
    sweep_patch(d, hull_hw, sheer_levels, xa, xb, sheer, lambda j, a, b: "oak", n=40)
    sweep_patch(d, hull_hw, sheer_levels, xa, xb, sheer, lambda j, a, b: "oak", group="auto", offset=HULL_T, n=40)
    sweep_cap(d, hull_hw, xa, xb, sheer, "timber", out=0.05, inner=HULL_T + 0.04, depth=0.1, n=40)
    # the transom: tarred below the waterline, oak above
    t_levels = sorted(set(round(float(v), 4) for v in levels + sheer_levels + [0.1, float(sheer(-2.85))] if v >= -0.45))
    transom(d, [y for y in t_levels if y <= 0.1 + 1e-6], stern_x, transom_hw, "tarred", group="bottom")
    transom(d, [y for y in t_levels if y >= 0.1 - 1e-6], stern_x, transom_hw, "oak")
    transom(d, t_levels, lambda y: stern_x(y) + HULL_T, lambda y: max(0.0, transom_hw(y) - HULL_T), "oak", group="auto", inward=True)
    # the keel and the stem and sternpost
    d.add_beam_mesh("bottom", "tarred", (stern_x(KEEL) + 0.05, KEEL - 0.04, 0), (bow_x(KEEL) - 0.02, KEEL - 0.04, 0), 0.1, 0.12)
    stem = [np.array([bow_x(y) + 0.02, y, 0.0]) for y in np.linspace(KEEL, sheer(2.9) + 0.08, 8)]
    for a, b in zip(stem[:-1], stem[1:]):
        d.add_beam_mesh("exterior", "timber", a, b, 0.1, 0.1, up=(0, 0, 1))
    # the bottom boards: a floor from side to side at FLOOR
    flat_between(d, FLOOR, stern_x(FLOOR) + 0.1, bow_x(FLOOR) - 0.15, lambda x: np.zeros_like(x),
                 lambda x: inner_hw(x, np.full_like(x, FLOOR)), "oak", group="auto", facing=1, n=24)
    # ribs, inside the planking, every 0.45
    for xf in np.arange(-2.3, 2.35, 0.45):
        for s in (1, -1):
            pts = []
            for y in np.linspace(FLOOR, float(sheer(xf)) - 0.02, 7):
                w = float(inner_hw(xf, y))
                if w < 0.15:
                    pts = []
                    continue
                pts.append(np.array([xf, y, s * (w - 0.025)]))
            for a, b in zip(pts[:-1], pts[1:]):
                d.add_beam_mesh("auto", "timber", a, b, 0.06, 0.05, up=(0, 0, s))


def fittings(d):
    """The thwarts, the stern sheets, the risers they rest on, the tabernacle."""
    def span(x, y):
        return float(inner_hw(x, y)) - 0.01

    for x in [MAST_THWART] + list(ROWERS.values()):
        w = min(span(x - 0.12, THWART_Y - 0.1), span(x + 0.12, THWART_Y - 0.1))
        d.add_box_mesh("auto", "oak", (x, THWART_Y - 0.04, 0.0), (0.25, 0.08, 2 * w))
        d.add_box_mesh("auto", "timber", (x, (FLOOR + THWART_Y - 0.08) / 2, 0.0), (0.07, THWART_Y - 0.08 - FLOOR, 0.07))
    # the risers along the sides under the thwarts
    for s in (1, -1):
        run = [np.array([x, THWART_Y - 0.12, s * (span(x, THWART_Y - 0.12) - 0.02)]) for x in np.linspace(-2.4, 1.9, 12)]
        for a, b in zip(run[:-1], run[1:]):
            d.add_beam_mesh("auto", "timber", a, b, 0.05, 0.06)
    # the stern sheets: a bench across the stern and along both sides of it
    x0, x1 = STERN_SHEETS
    for k, (xa, xb) in enumerate(((x0 + 0.02, x1), )):
        xs = np.linspace(xa, xb, 6)
        for i in range(5):
            xm = (xs[i] + xs[i + 1]) / 2
            w = min(span(xs[i], THWART_Y - 0.1), span(xs[i + 1], THWART_Y - 0.1))
            d.add_box_mesh("auto", "oak", (xm, THWART_Y - 0.04, 0.0), (xs[i + 1] - xs[i] + 0.01, 0.08, 2 * w))
    d.add_box_mesh("auto", "oak", (x1 - 0.02, (FLOOR + THWART_Y) / 2, 0.0), (0.04, THWART_Y - FLOOR, 2 * span(x1, 0.0)))
    # the bow locker: a little foredeck at the gunwale over the bow, and the locker's front with its door under it
    fy = float(sheer(BOW_LOCKER)) - 0.12
    flat_between(d, fy, BOW_LOCKER, bow_x(fy) - 0.05, lambda x: np.zeros_like(x), lambda x: inner_hw(x, np.full_like(x, fy)), "oak",
                 group="auto", facing=1, n=6)
    wf = float(inner_hw(BOW_LOCKER, 0.2)) - 0.01
    d.add_box_mesh("auto", "oak", (BOW_LOCKER - 0.02, (FLOOR + fy) / 2, 0.0), (0.04, fy - FLOOR, 2 * wf))
    d.add_box_mesh("auto", "timber", (BOW_LOCKER - 0.05, (FLOOR + fy) / 2 + 0.05, 0.0), (0.03, fy - FLOOR - 0.25, 0.55))
    d.add_box_mesh("auto", "iron", (BOW_LOCKER - 0.075, (FLOOR + fy) / 2 + 0.1, 0.2), (0.02, 0.08, 0.05))
    # the tabernacle: two cheeks the mast's heel is pinned between
    mx, hy = MAST_HINGE
    for s in (1, -1):
        d.add_box_mesh("auto", "timber", (mx, (FLOOR + hy + 0.12) / 2, s * 0.1), (0.2, hy + 0.12 - FLOOR, 0.05))
    d.add_cylinder("auto", "iron", (mx, hy, -0.14), (mx, hy, 0.14), 0.02, 0.02, sides=6)
    # a crutch on the stem head the lowered mast rests on
    sx = bow_x(sheer(2.8)) - 0.12
    for s in (1, -1):
        d.add_beam_mesh("exterior", "timber", (sx, float(sheer(2.8)), s * 0.02), (sx, float(sheer(2.8)) + 0.2, s * 0.09), 0.04)
    # rowlocks: two thole pins each side of each rowing thwart, on the gunwale
    for x in ROWERS.values():
        xr = x + ROWLOCK_DX
        for s in (1, -1):
            zr = s * (hw(xr, GUNWALE) - 0.02)
            gy = float(sheer(xr)) + 0.02
            for dx in (-0.06, 0.06):
                d.add_cylinder("exterior", "timber", (xr + dx, gy, zr), (xr + dx, gy + 0.14, zr), 0.018, 0.015, sides=5)


# ==================================================================== rudder and tiller
def rudder_and_tiller(d):
    ra = np.array([stern_x(KEEL + 0.05) - 0.04, KEEL + 0.05, 0.0])
    rb = np.array([stern_x(0.95) - 0.04, 0.95, 0.0])
    axis = (rb - ra) / np.linalg.norm(rb - ra)
    aft = np.array([-axis[1], axis[0], 0.0])
    if aft[0] > 0:
        aft = -aft
    heel = ra - axis * 0.12
    corners = [(heel, 0.0), (rb, 0.0), (rb + aft * 0.22, 1.0), (heel + aft * 0.48, 1.0)]
    z_ = np.array([0, 0, 1.0])
    d.add_prism("part:rudder", "timber", [p + z_ * (0.035 - 0.012 * w) for p, w in corners],
                [p - z_ * (0.035 - 0.012 * w) for p, w in corners])
    for f in (0.2, 0.7):
        p = ra + (rb - ra) * f
        d.add_cylinder("exterior", "iron", p - axis * 0.03, p + axis * 0.03, 0.03, 0.03, sides=6)
    d.moving_parts.append({"code": "rudder", "axisPoint": [round(float(v), 4) for v in ra],
                           "axisDir": [round(float(v), 5) for v in axis], "source": "rudder", "angle": 35.0})
    # the tiller: from the rudder head forward over the stern sheets, at the helmsman's hand
    head = rb + axis * 0.05
    tip = np.array([STERN_SHEETS[1] + 0.25, 0.85, 0.0])
    d.add_beam_mesh("part:tiller", "timber", head, tip, 0.05, 0.06)
    d.add_cylinder("part:tiller", "timber", tip, tip + np.array([0.1, 0.0, 0]), 0.03, 0.03, sides=6)
    d.moving_parts.append({"code": "tiller", "axisPoint": [round(float(v), 4) for v in ra],
                           "axisDir": [round(float(v), 5) for v in axis], "source": "rudder", "angle": 35.0})


# ==================================================================== the oars
def oar(d, group, handle, lock, length=2.55, blade_len=0.55, blade_w=0.13):
    """An oar from its handle through the rowlock, its blade out in the water."""
    handle, lock = np.asarray(handle, float), np.asarray(lock, float)
    u = (lock - handle) / np.linalg.norm(lock - handle)
    end = handle + u * length
    root = end - u * blade_len
    d.add_cylinder(group, "timber", handle, handle + u * 0.14, 0.025, 0.025, sides=6)          # the grip
    d.add_cylinder(group, "timber", handle + u * 0.14, root, 0.032, 0.028, sides=6)          # the loom
    w = np.cross(u, [0, 1.0, 0])
    w /= np.linalg.norm(w)
    v = np.cross(w, u)
    d.add_box_mesh(group, "timber", (root + end) / 2, (blade_len, blade_w, 0.02), np.column_stack([u, v, w]))


def oars(d):
    """Out in their rowlocks while someone sits on the thwart, each turning about its rowlock with the stroke; lying
    along the thwarts otherwise."""
    for code, x in ROWERS.items():
        xr = x + ROWLOCK_DX
        for s, side in ((1, "s"), (-1, "p")):
            gy = float(sheer(xr)) + 0.1
            lock = np.array([xr, gy, s * (hw(xr, GUNWALE) - 0.02)])
            handle = np.array([x + 0.28, THWART_Y + 0.55, s * 0.22])
            part = f"oar-{code}-{side}"
            oar(d, f"part:{part}", handle, lock)
            d.moving_parts.append({"code": part, "axisPoint": [round(float(v), 4) for v in lock], "axisDir": [0.0, 1.0, 0.0],
                                   "source": f"stroke:{code}", "angle": 24.0 * s, "shown": f"manned:{code}"})
        # stowed: both oars along the thwarts, inboard
        grp = f"oars-stowed-{code}"
        zz = 0.3 if code == "oars-fwd" else 0.5
        for s in (1, -1):
            a = np.array([-0.95, THWART_Y + 0.035, s * zz])
            b = np.array([1.6, THWART_Y + 0.035, s * zz])
            oar(d, f"part:{grp}", a, b, length=2.55)
        d.moving_parts.append({"code": grp, "kind": "static", "axisPoint": [0.0, 0.0, 0.0], "axisDir": [0.0, 1.0, 0.0],
                               "source": "rudder", "angle": 0.0, "shown": f"unmanned:{code}"})


# ==================================================================== the folding mast and the lug sail
def mast_and_sail(d):
    mx, hy = MAST_HINGE
    g = "part:mast"
    d.add_cylinder(g, "pine", (mx, hy - 0.45, 0), (mx, MAST_TOP, 0), 0.075, 0.05, sides=10, smooth=False)
    d.add_box_mesh(g, "timber", (mx, MAST_TOP + 0.03, 0), (0.13, 0.06, 0.13))
    d.add_cylinder(g, "timber", (mx - 0.03, MAST_TOP - 0.25, 0), (mx + 0.03, MAST_TOP - 0.25, 0), 0.05, 0.05, sides=8)   # sheave
    d.moving_parts.append({"code": "mast", "axisPoint": [mx, hy, 0.0], "axisDir": [0.0, 0.0, 1.0],
                           "source": "open:mast", "angle": MAST_FOLD})
    # the forestay, from the stem head to the masthead: it stays on both as the mast goes down
    stem_head = (bow_x(sheer(2.8)) - 0.05, float(sheer(2.8)) + 0.05, 0.0)
    head = (mx, MAST_TOP - 0.15, 0.0)
    d.add_rope(stem_head, head, r=0.018, group="part:forestay")
    d.moving_parts.append({"code": "forestay", "kind": "follow", "follow": "mast",
                           "anchor": [round(float(v), 4) for v in stem_head], "tip": [round(float(v), 4) for v in head]})
    # the shrouds, hooked on at the gunwales while it's up (cast off to lower it)
    for s in (1, -1):
        d.add_rope((mx, MAST_TOP - 0.2, s * 0.04), (mx - 0.25, float(sheer(mx - 0.25)) + 0.02, s * (hw(mx - 0.25, GUNWALE) - 0.02)),
                   r=0.016, group="part:shrouds")
    d.moving_parts.append({"code": "shrouds", "axisPoint": [mx, hy, 0.0], "axisDir": [0.0, 0.0, 1.0],
                           "source": "open:mast", "angle": MAST_FOLD, "shown": "shut:mast"})
    # the yard, turned about the mast by the crew's trim (like the carrack's lateen), and the sail under it
    piv_y = float(YARD_A[1] + (YARD_B[1] - YARD_A[1]) * (mx - YARD_A[0]) / (YARD_B[0] - YARD_A[0]))
    yg = "part:yard-lug"
    d.add_cylinder(yg, "pine", YARD_A, YARD_B, 0.035, 0.03, sides=8)
    d.add_rope((mx, MAST_TOP - 0.25, 0.06), (mx, piv_y + 0.02, 0.1), r=0.015, group=yg)     # halyard
    d.moving_parts.append({"code": "yard-lug", "axisPoint": [mx, round(piv_y, 4), 0.0], "axisDir": [0.0, 1.0, 0.0],
                           "source": "sheet", "angle": BRACE_DEG_PER_RAD, "limit": 75.0, "shown": "shut:mast"})
    # the sail: a quadrilateral under the yard (tack forward at the mast, clew aft), a little belly, a smooth mesh
    tack = np.array([mx + 0.45, SAIL_FOOT, 0.12])
    clew = np.array([mx - 2.05, SAIL_FOOT, 0.12])
    ya, yb = YARD_A + np.array([0, -0.05, 0]), YARD_B + np.array([0, -0.05, 0])
    nu, nv = 10, 8

    def P(u, v):
        top = ya + (yb - ya) * u
        bot = tack + (clew - tack) * u
        p = top + (bot - top) * v
        belly = 0.22 * math.sin(math.pi * u) * math.sin(math.pi * min(1.0, v * 0.9 + 0.1))
        return p + np.array([0, 0, belly])

    for iv in range(nv):
        for iu in range(nu):
            u0, u1, v0, v1 = iu / nu, (iu + 1) / nu, iv / nv, (iv + 1) / nv
            d.add_quad("sail:lug", "cloth", [P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1)], (u0 * 16, v0 * 16, u1 * 16, v1 * 16),
                       smooth="lug")
    ax = (YARD_B - YARD_A) / np.linalg.norm(YARD_B - YARD_A)
    d.sails.append({"code": "lug", "mesh": "sail:lug", "axisPoint": [float(v) for v in ya], "axisDir": [float(v) for v in ax],
                    "area": 5.5, "mast": "mast"})
    # the sheet, from the clew to the stern sheets
    d.add_rope(clew, (STERN_SHEETS[1] - 0.2, float(sheer(-2.0)) + 0.02, 0.5), r=0.014, group="part:sheet-lug")
    d.moving_parts.append({"code": "sheet-lug", "kind": "follow", "follow": "yard-lug", "sail": "lug", "shown": "shut:mast",
                           "anchor": [round(float(v), 4) for v in (STERN_SHEETS[1] - 0.2, float(sheer(-2.0)) + 0.02, 0.5)],
                           "tip": [round(float(v), 4) for v in clew]})


# ==================================================================== what the mod makes usable
def fixtures(d):
    def fx(ftype, code, pos, yaw=0.0, sel=None, **kw):
        f = {"type": ftype, "code": code, "pos": [round(float(v), 4) for v in pos], "yaw": yaw}
        if sel is not None:
            f["sel"] = [round(float(v), 4) for v in sel]
        f.update(kw)
        d.fixtures.append(f)

    # the helm: sitting on the stern sheets beside the tiller, the tiller in hand
    hx = STERN_SHEETS[0] + 0.35
    fx("helm", "helm", (hx, THWART_Y, 0.42), 0.0, sel=(STERN_SHEETS[0], THWART_Y - 0.1, -0.5, STERN_SHEETS[1] + 0.35, 1.0, 0.7),
       pose="sit")
    # the bow locker, under the little foredeck
    fx("storage", "locker", (BOW_LOCKER + 0.2, 0.3, 0.0), 0.0, sel=(BOW_LOCKER - 0.05, FLOOR, -0.4, BOW_LOCKER + 0.35, 0.8, 0.4), slots=16)
    # the rowing thwarts: sit down there and the oars come out
    for code, x in ROWERS.items():
        w = float(inner_hw(x, THWART_Y))
        fx("oars", code, (x, THWART_Y, 0.0), 0.0, sel=(x - 0.2, THWART_Y - 0.12, -w, x + 0.2, THWART_Y + 0.15, w), pose="sit")
    # the mast: right-click its foot to raise or lower it; a new boat's is down
    mx, hy = MAST_HINGE
    fx("mast", "mast", (mx, THWART_Y, 0.0), 0.0, sel=(mx - 0.25, THWART_Y - 0.1, -0.3, mx + 0.25, hy + 0.9, 0.3), startOpen=True)
    # places for a chest, a storage vessel or a crate
    for code, x in CARGO.items():
        fx("attach", code, (x, FLOOR, 0.0), 0.0, sel=(x - 0.45, FLOOR, -0.45, x + 0.45, FLOOR + 0.9, 0.45), kind="cargo")
    d.board_points = [[-0.25, FLOOR, 0.0], [0.875, FLOOR, 0.0], [-1.5, FLOOR, 0.0]]


# ==================================================================== the item's model (inventory, in the hand)
def item_shape():
    """A model of boxes for the inventory and the hand (the boat itself is drawn from its mesh)."""
    m = Design("skiff-item", -3, 3, -1, 1.5, -1.2, 1.2)
    m.material(Material("planks", ["game:block/wood/planks/oak1"], (150, 112, 70)))
    m.material(Material("tar", ["game:block/wood/planks/oak1++3~hansa:block/tar"], (60, 45, 30)))
    m.material(Material("timber", ["game:block/wood/debarked/oak"], (160, 124, 84)))
    n = 12
    xs = np.linspace(stern_x(0.5) + 0.02, bow_x(0.5) - 0.02, n + 1)
    strakes = [(KEEL + 0.05, -0.3), (-0.3, 0.05), (0.05, 0.35), (0.35, GUNWALE + 0.1)]
    for k, (y0, y1) in enumerate(strakes):
        mat = "tar" if k < 2 else "planks"
        for i in range(n):
            xa, xb = xs[i], xs[i + 1]
            for s in (1, -1):
                a = np.array([(xa + xb) / 2, y0, s * float((hull_hw(xa, y0) + hull_hw(xb, y0)) / 2)])
                b = np.array([(xa + xb) / 2, y1, s * float((hull_hw(xa, y1) + hull_hw(xb, y1)) / 2)])
                up = b - a
                h = float(np.linalg.norm(up))
                if h < 1e-3:
                    continue
                up /= h
                along = np.array([xb - xa, 0, s * float(hull_hw(xb, (y0 + y1) / 2) - hull_hw(xa, (y0 + y1) / 2))])
                along -= np.dot(along, up) * up
                ln = float(np.linalg.norm(along))
                if ln < 1e-3:
                    continue
                along /= ln
                nrm = np.cross(along, up)
                m.add_box("exterior", mat, (a + b) / 2, np.column_stack([along, up, nrm]), (ln + 0.03, h + 0.03, 1.2 / 16))
    m.add_aabb("exterior", "tar", stern_x(KEEL) + 0.1, bow_x(KEEL) - 0.1, KEEL - 0.05, KEEL + 0.05, -0.05, 0.05)
    for x in [MAST_THWART] + list(ROWERS.values()) + [STERN_SHEETS[1] - 0.25]:
        w = float(inner_hw(x, THWART_Y))
        m.add_aabb("exterior", "planks", x - 0.12, x + 0.12, THWART_Y - 0.08, THWART_Y, -w, w)
    m.add_aabb("exterior", "planks", -2.0, 2.0, FLOOR - 0.04, FLOOR, -0.45, 0.45)
    for s in (1, -1):
        m.add_aabb("exterior", "timber", -1.6, 1.0, THWART_Y, THWART_Y + 0.05, s * 0.28, s * 0.28 + 0.05 * s)
    els = []
    for i, e in enumerate(m.elements):
        el = m._free_element(f"part{i}", e)
        el["from"] = [el["from"][0] + 8, el["from"][1] + 10, el["from"][2] + 8]
        el["to"] = [el["to"][0] + 8, el["to"][1] + 10, el["to"][2] + 8]
        if "rotationOrigin" in el:
            o = el["rotationOrigin"]
            el["rotationOrigin"] = [o[0] + 8, o[1] + 10, o[2] + 8]
        els.append(el)
    return {"textureWidth": 16, "textureHeight": 16, "textures": m._textures(), "elements": els}


def entity_json():
    return {
        "code": ITEM,
        "class": "hansa.EntityShip",
        "tags": ["inanimate", "vehicle"],
        "weight": 600,
        "hitboxSize": {"x": 2, "y": 1},
        "attributes": {"ship": CODE},
        "client": {"renderer": "Dummy", "behaviors": []},
        "server": {"behaviors": []},
        "sounds": {},
    }


def item_json():
    return {
        "code": ITEM,
        "class": "hansa.ItemShipDeed",
        "attributes": {"ship": CODE, "boat": True},
        "shape": {"base": "hansa:item/boat-skiff"},
        "heldTpIdleAnimation": "holdunderarm",
        "maxstacksize": 1,
        "materialDensity": 600,
        "liquidSelectable": 1,
        "creativeinventory": {},
        "guiTransform": {"translation": {"x": 1, "y": 3, "z": 0}, "rotation": {"x": -30, "y": -35, "z": 180},
                         "origin": {"x": 0.5, "y": 0.6, "z": 0.5}, "scale": 0.4},
        "groundTransform": {"translation": {"x": 0, "y": 0, "z": 0}, "rotation": {"x": 0, "y": 0, "z": 0},
                            "origin": {"x": 0.5, "y": 0, "z": 0.5}, "scale": 1.2},
        "tpHandTransform": {"translation": {"x": -0.9, "y": 0.2, "z": -0.6}, "rotation": {"x": -85, "y": -34, "z": 0},
                            "origin": {"x": 0.5, "y": 0.6, "z": 0.5}, "scale": 0.3},
        "fpHandTransform": {"translation": {"x": 0, "y": 0.1, "z": 0}, "rotation": {"x": 0, "y": 90, "z": 0},
                            "origin": {"x": 0.5, "y": 0.6, "z": 0.5}, "scale": 0.35},
    }


def recipe_json():
    # a launch: planks for the hull, a pair of oars (sticks), a linen sail, rope
    return {
        "ingredientPattern": "PSP,PLP,PRP",
        "ingredients": {"P": {"type": "item", "code": "game:plank-*", "quantity": 2},
                        "S": {"type": "item", "code": "game:stick", "quantity": 4},
                        "L": {"type": "block", "code": "game:linen-*", "quantity": 1},
                        "R": {"type": "item", "code": "game:rope"}},
        "width": 3,
        "height": 3,
        "output": {"type": "item", "code": "hansa:" + ITEM},
    }


def main():
    d = build()
    stats = d.write(ASSETS, None)
    print("skiff:", stats)
    out = {
        os.path.join(ASSETS, "shapes", "item", "boat-skiff.json"): item_shape(),
        os.path.join(ASSETS, "entities", ITEM + ".json"): entity_json(),
        os.path.join(ASSETS, "itemtypes", ITEM + ".json"): item_json(),
    }
    parked = os.path.join(ASSETS, "recipes", "grid", ITEM + ".json")     # parked: no recipe
    if os.path.exists(parked):
        os.remove(parked)
    for path, data in out.items():
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(data, fh, indent=1)
        print(path)
    old = os.path.join(ASSETS, "shapes", "entity", "boat", "skiff.json")
    if os.path.exists(old):
        os.remove(old)
    if "--no-preview" not in sys.argv:
        views(d)


VIEWS = {
    # name: eye, target, sea level (or None), fov, parts turned full (open / manned)
    "boat-side": ((1.0, 1.4, 7.5), (0.2, 0.4, 0.0), 0.0, 55, ()),
    "boat-oblique": ((5.5, 3.2, 4.5), (0.0, 0.2, 0.0), None, 55, ()),
    "boat-inside": ((-3.6, 2.2, 0.8), (1.0, -0.1, 0.0), None, 70, ()),
    "boat-sailing": ((5.0, 3.0, 6.5), (-0.2, 1.8, 0.0), 0.0, 60, ("mast-up",)),
    "boat-rowing": ((-4.5, 2.6, 3.8), (0.4, 0.3, 0.0), 0.0, 60, ("rowing",)),
}


def views(d):
    import view
    boxes, rg = d.mesh_voxels()
    out = os.path.join(DOCS, "views")
    os.makedirs(out, exist_ok=True)
    saved = [list(q["pts"]) for q in d.quads]
    for name, (eye, target, sea, fov, state) in VIEWS.items():
        for q, pts in zip(d.quads, saved):
            q["pts"] = pts
        hidden = set()
        if "mast-up" not in state:
            view.turn_part(d, "mast")
            hidden |= {"part:yard-lug", "sail:lug", "part:sheet-lug", "part:shrouds", "part:forestay"}
        if "rowing" in state:
            hidden |= {g for g in (f"part:oars-stowed-{c}" for c in ROWERS)}
        else:
            hidden |= {f"part:oar-{c}-{s}" for c in ROWERS for s in "sp"}
        keep = [q for q in d.quads if q["group"] not in hidden]
        all_q, d.quads = d.quads, keep
        quads = d._all_quads(boxes, {})
        d.quads = all_q
        img = view.render(quads, eye, target, fov=fov, water=sea)
        path = os.path.join(out, f"{name}.png")
        img.save(path)
        print(path)
    for q, pts in zip(d.quads, saved):
        q["pts"] = pts


if __name__ == "__main__":
    main()
