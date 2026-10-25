"""Caravel hull lines and layout (proposal, 17 October 2026). See models/caravel/caravel-design.md.

Pure Python + numpy. Writes models/caravel/caravel-lines.json, which tools/caravel/build_caravel.py lofts in
AstoCAD, and a lines-plan PNG for a quick look.

    python tools/caravel/caravel_lines.py

Model frame as docs/master-model.md: millimetres, X toward the bow, Y to port, Z up, Z 0 = the hull's underside.
Cell borders on whole metres in X and Z, at +-500, +-1500 ... in Y (the centreline runs through a cell's middle).
Everything below is authored in metres and written in millimetres.
"""
import json
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("CARAVEL_OUT") or os.path.normpath(os.path.join(HERE, "..", "..", "models", "caravel"))
MM = 1000.0

# (24 October 2026, chalk note 73: "we should make our ship 1 block longer here so we can place stairs and have decent
# layout", just aft of the main mast) more length in her parallel middle body; the bow, the main mast and the waist's
# guns keep their cells, the stern goes aft. With ribs 1 or 3 cells apart every rib keeps its parity, so with one block
# the two masts (a rib each side) and the quarterdeck's front (on a rib's forward face) can't both keep their places. Two
# drafts went to the user (CARAVEL_VARIANT A, B: models/caravel/variants, the views in variant_views.py):
#   A  1 longer, the waist 1 longer: the front at -6 (no rib under its edge), the mizzen at -9, the wheel aft of it
#   B  1 longer, the waist 2 longer, the quarterdeck 1 shorter: the front at -7 over rib -8, the mizzen at -11
# The user chose C: 2 blocks longer, B's waist (a clear cell between the main mast and the stairs' opening, the front
# over a rib) and the quarterdeck, its cabin, the mizzen, the wheel and the quarterdeck's guns where they were from the
# transom (the guns 1 aft of that: note 62)
VARIANT = os.environ.get("CARAVEL_VARIANT", "D")
LAYOUTS = {
    "A": {"longer": 1.0, "qd_front": -6.0, "mizzen": -9, "wheel": [-11, 0], "qd_guns": [-12, -10],
          "stairs": {"quarterdeck": -6, "hold": -4}},
    "B": {"longer": 1.0, "qd_front": -7.0, "mizzen": -11, "wheel": [-9, 0], "qd_guns": [-13, -11],
          "stairs": {"quarterdeck": -7, "hold": -5}},
    # (25 October 2026, dev.9's notes) the main mast 2 aft to the middle of her keel (Castro: a caravel's main mast
    # stands over the keel's middle; hers runs -12.4..5.6), one clear cell between it and the hold stair's opening, the
    # quarterdeck's stair to starboard against her side (the user: "At least on Nao Victoria it is done like that"), a
    # bulwark round the quarterdeck instead of its railing and no carriage guns there (note 103); the pump forward of
    # the main mast
    "D": {"longer": 2.0, "qd_front": -7.0, "mizzen": -11, "wheel": [-9, 0], "qd_guns": [], "main": -3,
          "pump": [-2, 0], "qd_bulwark": True, "swivels": [-9.5, -13.5],
          # (25 October 2026, note 102, the user: the rudder about an upright axis, a level tiller under the quarterdeck;
          # the stern as Boa Esperança's: a flat square transom over an upright sternpost, X -14.5)
          "stern_post_x": -14.5,
          # (the user's choice B of the study for Boa Esperança's sails: main 155 m2, mizzen 80 m2) yards balanced on
          # their masts, 2.2 of head room over the decks, the main's clew 0.9 forward of the mizzen's tack (metres)
          # (the user: "bottoms of sails should be about parallel to deck") each foot level, its clew as high as its tack's
          # corner (0.1 under the yard's end): the main's over the waist 3.8 up, the mizzen's over the bumpkin's end
          "sails": {"main": {"tack": [6.98, 7.86], "peak": [-10.26, 29.93], "clew": [-6.5, "level"], "area": 155.0},
                    "mizzen": {"tack": [-7.42, 10.79], "peak": [-13.58, 27.71], "clew": ["bumpkin", "level"], "area": 80.0}},
          "stairs": {"quarterdeck": -7, "hold": -5}, "stairs_y": {"quarterdeck": -2.0, "hold": 0.0},
          "hold_up": "fore"},
    "C": {"longer": 2.0, "qd_front": -7.0, "mizzen": -11, "wheel": [-9, 0], "qd_guns": [-14, -12],
          "stairs": {"quarterdeck": -7, "hold": -5}},
}
LAY_V = LAYOUTS[VARIANT]
LONGER = LAY_V["longer"]          # the extra length (m), aft of the main mast

# ---------------------------------------------------------------- vertical rules (decks 3 apart, 2 clear + 1 structure)
Z_HOLD = 1.0          # hold floor top: the bottom block 0..1 holds planking, floors, keel
Z_DECK = 4.0          # main (weather) deck floor top
Z_WL = 2.875          # waterline 0.875 above a cell border, so the deck sits on a world block border at rest
Z_QD = 7.0            # quarterdeck (tolda) floor top
RAIL = 1.0            # bulwark 1 block above its deck (the user, 17 October)
Z_WAIST_RAIL = Z_DECK + RAIL
Z_QD_RAIL = Z_QD + RAIL
Z_TOP = 9.5           # the loft goes above every sheer so the sheer cut and the shell's inner solid are open
QD_FRONT = LAY_V["qd_front"]   # quarterdeck front on a cell face (-5 before the lengthening, over rib cell -6's forward
                      # face): about a third of the length, as the reference caravels (the user, 17 October: the castle
                      # was too big)
QD_SIDE = 0.0         # the quarterdeck's solid side ends at its deck (the user, 18 October); the railing stands on it
QD_BULWARK = LAY_V.get("qd_bulwark", False)
if QD_BULWARK:        # (25 October 2026, note 103: "should we make hull board on top of castle instead of rails") her side
    QD_SIDE = 1.0     # planked up a block over the quarterdeck all round it, as the waist's bulwark, a cap rail on it
SKIN = 0.2            # hull planking 0.2 block (the user, 17 October; decks and floors the same)
LIFT = 0.125          # the hull's underside sits 125 above the bottom cell border: draft 2.75, so she passes over
                      # a bottom 3 blocks under the surface (the surface is 0.875 up a water block)
POST_HW = 0.1         # stem and sternpost half width where the sides close on them: the backbone's 0.2

# ---------------------------------------------------------------- plan (cells are named by their aft X in blocks)
MASTS = {                       # cell, heel Z, used by which rig (the user, 17 October: main about midships, 2 masts)
    "fore": {"cell": 7, "heel": Z_HOLD, "rigs": ["redonda"]},
    "main": {"cell": LAY_V.get("main", -1), "heel": Z_HOLD, "rigs": ["latina", "redonda"]},
    # (24 October 2026, chalk note 58: "we can remove this beam and have mast end with bottom of main deck") its heel flush
    # with the main deck's planking underneath, no step under it in the hold (it went down to the beams' underside)
    "mizzen": {"cell": LAY_V["mizzen"], "heel": Z_DECK - 0.2, "rigs": ["latina", "redonda"]},
}
RIBS = list(range(-14, 7, 2))           # every 2nd cell, 1 clear between (the user, 17 October); masts in clear cells.
                                        # None at 8: the forefoot there is too narrow for a frame (the user, 17 October).
                                        # (note 73) from -14: the stern 2 aft (the drafts, 1 aft: a short frame there)
GUN_CELLS = [1, 3]                       # main deck, waist (the user, 18 October)
QD_GUN_CELLS = LAY_V["qd_guns"]          # quarterdeck; the railing is open over these cells (22 October 2026: from
                                         # -7 to -9, beside the mizzen, the wheel taking -7: chalk note 30; 24 October,
                                         # note 62: "move all 4 cannons 1 block to aft")
# 11) the main hatch on the centreline cell, with a stair down to the hold (the user, 18 October)
# (21 October 2026, chalk notes 18 and 22) The stairs are the game's walked ship's stairs (the user: "stair animation
# and not ladder"): the steep one is 1 x 2 cells (foot F, head H, about 56 degrees, 3 blocks up), with a free landing
# before F and after H, and through a deck it needs a 3 x 1 well (the cell before F too) for the walker's head. Aft of
# the main mast there was no room for that (the mast's cell was the hold stair's landing and the quarterdeck stair's
# landing fell into the hatch), so the hatch is forward of the main mast: cells 2..4, the stair rising aft from F (cell
# 3, its foot on the hold floor) to H (cell 2), out onto cell 1 of the main deck. The quarterdeck stair: F cell -4, H
# cell -5, up to the quarterdeck's front at -5, its landing cell -3.
# (the same day, the game's checks) The guns at cell 3 swing their trails over the centreline there, and no opening may
# lie in a gun's working room; a ship's stair is solid with its own handrails a quarter block either side, so a
# stair's walkers keep a row away from another stair. So: the quarterdeck stair one row to starboard (Y cell -1), the
# railing's gap with it; the hatch under the quarterdeck in the port row (Y cell 1), cells -7..-5, the hold stair
# rising aft in it from its foot in cell -6 out onto cell -8 of the main deck, beside the mizzen.
# (22 October 2026, chalk note 36, the user's OK for the step it needs) the one-cell ship's stair, about 72 degrees.
# (24 October 2026, the user: "stairs should be moved to the center of the ship (if we can sort rope management)") both
# on the centreline, rising aft in a line: the hold's up through its hatch onto the cell before the quarterdeck's foot,
# the quarterdeck's in front of the cabin's wall, its head at the quarterdeck's front
_QS, _HS = LAY_V["stairs"]["quarterdeck"], LAY_V["stairs"]["hold"]
_SY = LAY_V.get("stairs_y", {"quarterdeck": 0.0, "hold": 0.0})
if LAY_V.get("hold_up") == "fore":
    # (draft D) the hold's stair rising forward out of its hatch onto the clear cell aft of the main mast: the hatch the
    # stair's cell and the two aft of it, where the walker's head passes
    HATCH = {"x": [_HS - 2, _HS + 1], "y": [_SY["hold"] - 0.5, _SY["hold"] + 0.5]}
    _hold = {"name": "hold", "cell": [_HS, 0], "foot": [float(_HS), Z_HOLD], "head": [_HS + 1.0, Z_DECK], "y": _SY["hold"]}
else:
    HATCH = {"x": [_HS, _HS + 3], "y": [-0.5, 0.5]}
    _hold = {"name": "hold", "cell": [_HS, 0], "foot": [_HS + 1.0, Z_HOLD], "head": [float(_HS), Z_DECK], "y": 0.0}
STAIRS = [
    {"name": "quarterdeck", "cell": [_QS, 0], "foot": [_QS + 1.0, Z_DECK], "head": [float(_QS), Z_QD], "y": _SY["quarterdeck"]},
    _hold,
]
# (24 October 2026, the user: "add default partition wall with doors and window to rear to form cabin, similar to how in
# current test") the steerage closed at the quarterdeck's front by the pieces a player places: oak plank partitions, a
# door either side of the quarterdeck stair, a window over the middle between them (the places outboard of the doors, at
# z +-2, are the side's wedge cells: neither a window nor a door takes one); no top row behind the stair's head
CABIN = {"x": QD_FRONT, "doors": [-1, 1], "windows": [0]}
# (25 October 2026, note 105, the user: "see what kitchen i made. this could be default. This would probably match better
# history with exposed kitchen") draft D: the galley the user built aboard (world "gloomy hermit valley", ship #227), as
# her start pieces. Game frame: x fore, z to starboard; a wall on the line x (or z) = at, over cells (their middles) along
# it, rows up from the main deck. Open to the waist on the port side under the quarterdeck's front: its after wall on
# x -9, its starboard wall on z 0.5; the cabin's front on x -7 to starboard of it, a doorway at z 1 (the user's: no door)
KITCHEN = {
    "walls": [
        {"line": "x", "at": QD_FRONT, "cells": [0, 2], "rows": [0, 1, 2]},          # the cabin's front either side of the doorway
        {"line": "x", "at": QD_FRONT, "cells": [1], "rows": [2]},                   # over the doorway
        {"line": "x", "at": QD_FRONT, "cells": [3], "rows": [0, 1]},                # the side's wedge
        {"line": "x", "at": QD_FRONT - 2.0, "cells": [-2, 0], "rows": [0, 1, 2]},       # the galley's after wall,
        {"line": "x", "at": QD_FRONT - 2.0, "cells": [-1], "rows": [1, 2]},             # over the hearth (it stands in
                                                                                         # the bottom row's place)
        {"line": "z", "at": 0.5, "cells": [QD_FRONT - 2.0, QD_FRONT - 1.0], "rows": [0, 1, 2]},   # its starboard wall
    ],
    "hearth": {"cell": [QD_FRONT - 2.0, -1], "facing": "+x", "block": "game:firepit-extinct"},
    "oven": {"cell": [QD_FRONT - 2.0, -2], "level": 1, "facing": "+x"},
    "things": [{"block": "game:table-normal", "cell": [QD_FRONT - 2.0, -2]},        # the oven stands on it
               {"block": "game:table-normal", "cell": [QD_FRONT - 2.0, 0]},
               {"block": "game:storagevessel-tan-fired", "cell": [QD_FRONT - 1.0, 0]}],
} if VARIANT == "D" else None
# 10) gates in the waist's bulwark with a rope ladder down the side (the user, 18 October)
GATE_CELL = -1
# (21 October 2026, the user's list after trying her: no pump, no anchor, a wheel on the quarterdeck because the mizzen
# blocks the helmsman's sight from under it, lanterns) where her fittings stand: cells [x cell, Y cell] on a deck (the Y
# cell 0 is the centreline cell); the exporter keeps the deck cells under them fixed, the layout step draws them
FITTINGS = {
    "pump": {"deck": "main", "cell": LAY_V.get("pump", [0, 0])},   # forward of the main mast; its brake athwartships
    "capstan": {"deck": "main", "cell": [6, 0]},           # on the foredeck abreast the cathead (the user, 22 October
                                                           # 2026), the cable on the deck straight from the hawse
    "hawse": {"x": 6.75},                                  # the hawse hole's middle, on the anchor's side, just forward
                                                           # of rib 6 (chalk note 47: "next to the rib")
    "wheel": {"deck": "quarterdeck", "cell": LAY_V["wheel"]},   # (22 October 2026, chalk note 30) forward of the
                                                           # mizzen, where the user marked it (x -6.77): its rim there, the
                                                           # helmsman aft of it, the waist in sight. (note 73, draft A: aft
                                                           # of the mizzen, the centreline stair's head where it stood)
    "anchor": {"side": "port", "x": 8.0},                  # hung at the port cathead (on the rib nearest this x)
}                                                          # (no stern lantern: chalk note 63, the user, 24 October 2026)
PORT = {"sill": 0.25, "head": 1.0, "inset": 0.125}  # 750 x 750 in a cell's middle; sill and head above its deck

# ---------------------------------------------------------------- midship section half-breadth B(z)
SEC_Z = [0.0, 0.25, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 5.25, 6.5, 8.25, 9.5]
SEC_B = [1.6, 2.05, 2.35, 2.72, 2.96, 3.12, 3.22, 3.28, 3.30, 3.27, 3.10, 2.92, 2.62, 2.42]   # widest 3.30: beam 6.6

# parallel middle body (note 73: 1 m longer at its after end)
XM_A, XM_F = -2.5 - LONGER, 0.5

# stem: (z, x) from the forefoot up, raked, a forefoot arc into the flat floor
STEM = [(0.0, 5.6), (0.25, 6.85), (0.6, 7.55), (1.0, 8.0), (1.6, 8.45), (2.5, 8.95), (3.5, 9.45),
        (4.5, 9.88), (5.0, 10.0), (5.5, 10.05), (6.0, 10.02), (6.5, 9.95), (8.0, 9.6), (9.5, 9.2)]
# sternpost: (z, x), raked, up to where the transom starts
# the sternpost and the transom in one plane (the user, 17 October): the post's slope from the keel's after end
# continued as one straight line to the top, so the rudder hangs on one axis. The stern reaches further aft above,
# so rib -12 stands over the post with a main-deck beam.
STERN_KEEL_X = -10.4 - LONGER       # the keel's after end at the underside (Z = LIFT): 0.3 aft of before, so rib -12's (now -14's) band and its main-deck beam fit; aft with note 73
STERN_RAKE = 0.45                    # aft per unit up: the post's rake from the bottom, as before
if "stern_post_x" in LAY_V:          # (note 102) upright, the transom in its plane
    STERN_KEEL_X, STERN_RAKE = LAY_V["stern_post_x"], 0.0
STERN_TOP_X = STERN_KEEL_X - STERN_RAKE * (Z_QD_RAIL - LIFT)   # -13.94 at the quarterdeck rail
TRANSOM_FOOT_Z = 2.4                 # (shape height) where the wide transom starts; below it the hull narrows to the post


def stern_line(zr):
    """X of the sternpost/transom plane at real height zr."""
    return STERN_KEEL_X + (STERN_TOP_X - STERN_KEEL_X) * (zr - LIFT) / (Z_QD_RAIL - LIFT)


TRANSOM_LOW = (TRANSOM_FOOT_Z, None)  # [0] only: the transom's foot (shape height)
# transom half-width (its outline): narrows to the post at its foot
TR_Z = [2.4, 2.6, 3.0, 3.5, 4.0, 5.25, 7.0, 8.25, 9.5]
TR_E = [0.0, 0.75, 1.35, 1.75, 1.98, 2.2, 2.25, 2.15, 2.0]

# sheer (top of side) as (x, z): quarterdeck rail, a concave break sweeping down over 2.5 m, waist rail, rising to the bow
BREAK_LEN = 2.5
def sheer(x):
    x = np.asarray(x, float)
    z = np.full_like(x, Z_WAIST_RAIL)
    brk0, brk1 = QD_FRONT, QD_FRONT + BREAK_LEN
    aft = x <= brk0
    z[aft] = Z_QD + QD_SIDE
    m = (x > brk0) & (x < brk1)
    u = (x[m] - brk0) / (brk1 - brk0)                       # 0 at the deck edge .. 1 at the waist
    z[m] = Z_WAIST_RAIL + (Z_QD + QD_SIDE - Z_WAIST_RAIL) * (1.0 - u) ** 2   # concave: eases into the waist
    fwd = x > 4.0
    z[fwd] = Z_WAIST_RAIL + 1.0 * ((x[fwd] - 4.0) / 6.0) ** 2   # 1 block up at the stem head (x ~10)
    return z


def interp(z, table):
    zs, vs = zip(*table)
    return float(np.interp(z, zs, vs, left=None, right=None)) if zs[0] <= z <= zs[-1] else \
        float(vs[0] + (vs[1] - vs[0]) * (z - zs[0]) / (zs[1] - zs[0])) if z < zs[0] else \
        float(vs[-1] + (vs[-1] - vs[-2]) * (z - zs[-1]) / (zs[-1] - zs[-2]))


def shape_z(z):
    """Real Z to the height the shape tables are written in: below the waterline the body is squeezed so the
    underside sits at LIFT while the waterline's shape stays where it was."""
    return (z - LIFT) * Z_WL / (Z_WL - LIFT) if z < Z_WL else z


def real_z(s):
    return LIFT + s * (Z_WL - LIFT) / Z_WL if s < Z_WL else s


def B(z):
    return float(np.interp(z, SEC_Z, SEC_B))


def x_stem(z):
    return interp(z, STEM)


def x_transom(z):
    """The sternpost/transom plane at shape height z."""
    return stern_line(real_z(z))


def x_post(z):
    return x_transom(z)


def e_transom(z):
    return float(np.interp(z, TR_Z, TR_E)) if z >= TR_Z[0] else 0.0


def smooth(a, b, t):
    t = min(max(t, 0.0), 1.0)
    t = t * t * (3 - 2 * t)
    return a + (b - a) * t


def bow_q(z):
    """Waterline fullness at the bow: fine and hollow low down, round at the rail."""
    return smooth(0.95, 1.75, z / 5.0)


def run_q(z):
    """Fullness of the run aft: a hollow run that meets the post tangentially low down (q < 1); from the transom's
    foot up the side meets the transom at a finite angle (q = 1)."""
    return smooth(0.8, 1.0, z / TRANSOM_LOW[0])


def g(u, p, q):
    u = np.clip(u, 0.0, 1.0)
    return (1.0 - u ** p) ** (1.0 / q)


def ends_clustered(n):
    s = np.linspace(0.0, 1.0, n)
    return 0.5 - 0.5 * np.cos(np.pi * s)


N_CLOSE = 6


def end_close(z):
    """Behind the after end each waterline rounds off over a short distance: at the post it is the post's own
    rounded after face; above the transom's foot it lies aft of the transom plane and is cut away."""
    e = max(e_transom(z), POST_HW)
    return e, 0.06 + 0.3 * min(e_transom(z) / 1.5, 1.0)


def waterline(z, n_aft=34, n_mid=4, n_fwd=40):
    """Port half-breadths (x, y) from the after end to the stem at height z.

    Every line ends on the sternpost (below the transom's foot) or on the transom plane (above it), so the ends
    move smoothly from one level to the next; there the half-breadth is the transom's outline e(z)."""
    z = shape_z(z)
    b = B(z)
    xf = x_stem(z)
    xa = x_post(z)
    e, delta = end_close(z)
    th = np.radians(np.linspace(75.0, 0.0, N_CLOSE + 1)[:-1])
    xs_c = xa - delta * np.sin(th)
    ys_c = e * np.cos(th)
    s = ends_clustered(n_aft)
    xs_a = xa + (XM_A - xa) * s[:-1]
    xs_m = np.linspace(XM_A, XM_F, n_mid)[:-1]
    xs_f = XM_F + (xf - XM_F) * ends_clustered(n_fwd)
    xs = np.concatenate([xs_a, xs_m, xs_f])
    ys = np.full_like(xs, b)
    ma = xs < XM_A
    ys[ma] = e + (b - e) * g((XM_A - xs[ma]) / (XM_A - xa), 2.0, run_q(z))
    mf = xs > XM_F
    ys[mf] = POST_HW + (b - POST_HW) * g((xs[mf] - XM_F) / (xf - XM_F), 2.0, bow_q(z))
    return np.concatenate([xs_c, xs]), np.concatenate([ys_c, ys])


def levels():
    zs = set()
    for z in np.arange(LIFT, 1.0, 0.125):
        zs.add(round(float(z), 4))
    for z in np.arange(1.0, 3.6, 0.25):
        zs.add(round(float(z), 4))
    for z in np.arange(3.5, Z_TOP + 1e-6, 0.25):
        zs.add(round(float(z), 4))
    zs.update([round(real_z(TRANSOM_LOW[0]), 4), round(real_z(2.6), 4), Z_WL, Z_TOP])
    return sorted(zs)


_WL_CACHE = {}


def half_breadth(xq, z):
    """The outer half-breadth field f(x, z); 0 outside the hull's ends and below its underside."""
    if z < LIFT - 1e-9:
        return np.zeros_like(xq)
    key = round(z, 6)
    if key not in _WL_CACHE:
        _WL_CACHE[key] = waterline(z)
    xs, ys = _WL_CACHE[key]
    return np.interp(xq, xs, ys, left=0.0, right=0.0)


N_INNER = 121


def inner_waterline(z, r=SKIN, nx=900):
    """The shell's inner face at height z: the outer half-breadth field eroded by a ball of radius r, so every
    inner point lies at least r from the outside in any direction, the stem, the post and the floor included.
    Returns (xs, ys) with N_INNER points, ends clustered, between the ends where the eroded field is still open."""
    xs0, _ = waterline(z)
    xg = np.linspace(xs0[0] - 0.1, xs0[-1] + 0.1, nx)
    yi = np.full_like(xg, np.inf)
    for dz in np.linspace(-r, r, 11):
        for dx in np.linspace(-r, r, 21):
            h2 = r * r - dx * dx - dz * dz
            if h2 < 0:
                continue
            yi = np.minimum(yi, half_breadth(xg + dx, z + dz) - math.sqrt(h2))
    open_ = np.nonzero(yi > 0.03)[0]
    i0, i1 = open_[0], open_[-1]
    xa, xf = xg[i0], xg[i1]
    xs = xa + (xf - xa) * ends_clustered(N_INNER)
    ys = np.maximum(np.interp(xs, xg[i0:i1 + 1], yi[i0:i1 + 1]), 0.03)
    return xs, ys


RIB_SIDED, RIB_MOULDED, FLOOR_TOP = 0.2, 0.4, 0.8   # ribs 0.2 along X, 0.4 deep; floors up to the hold floor
RIB_HEAD = 0.2        # (22 October 2026) a rib's depth at its head under the cap rail
RIB_GAP = 10.0   # mm inside the skin: the ruled inner loft joins waterline points by index, not by x, so sections taken
                 # at fixed x stray a few mm into the skin between waterlines


RIB_INNER = {}      # rib -> its port inner edge [(y, z)] mm, top down to the centreline (the beams end on it)
RIB_CHECK = {}      # rib -> what its section measured


def _resample(c, step=50.0):
    out = [c[0]]
    for q in c[1:]:
        p0 = np.asarray(out[-1]); q = np.asarray(q)
        n = int(np.hypot(*(q - p0)) // step)
        out += [tuple(p0 + (q - p0) * k / (n + 1)) for k in range(1, n + 1)] + [tuple(q)]
    return np.array(out, float)


def _dist_to_curve(pt, c):
    a, b = c[:-1], c[1:]
    ab = b - a
    t = np.clip(((pt - a) * ab).sum(1) / np.maximum((ab * ab).sum(1), 1e-9), 0, 1)
    return float(np.min(np.hypot(*(a + ab * t[:, None] - pt).T)))


def rib_profiles(inner):
    """Each rib's section at its cell's middle, in the YZ plane: a band RIB_MOULDED thick square to the inside of the
    skin, from the sheer on one side across the bottom to the sheer on the other. Where the hull is narrower than two
    bands the band fills to the centreline. A closed polygon [(y, z)], mm; the build extrudes it RIB_SIDED along X at
    the cell's middle and cuts the keel's notch out of it."""
    out = {}
    m = RIB_MOULDED * MM
    for r in RIBS:
        xm = (r + 0.5) * MM
        across = [xm - RIB_SIDED * MM / 2, xm, xm + RIB_SIDED * MM / 2]     # the narrowest across the rib's thickness
        zs, ys = [], []
        for l in inner:
            px = [p[0] for p in l["pts"]]
            py = [p[1] for p in l["pts"]]
            if all(px[0] < x < px[-1] for x in across):
                zs.append(l["z"])
                ys.append(min(float(np.interp(x, px, py)) for x in across) - RIB_GAP)
        top = float(sheer(np.array([r + 0.5]))[0])                    # in the waist and forward: up to the rail
        if r + 0.5 < QD_FRONT and not QD_BULWARK:                      # under the quarterdeck: end under its planking,
            top = min(top, Z_QD - DECK_T)                              # unseen from above (the user, 18 October)
        top *= MM
        keep = [(y, z) for y, z in zip(ys, zs) if z < top and y > 30.0]
        if len(keep) < 3:
            continue
        keep.append((float(np.interp(top, zs, ys)), top))
        z_lo = keep[0][1]
        # the outer edge, port: from the top down the side, then across the bottom to the centreline
        c = _resample([(y, z) for y, z in reversed(keep)] + [(0.0, z_lo)])
        d = np.gradient(c, axis=0)
        n = np.stack([d[:, 1], -d[:, 0]], axis=1)                    # toward the inside: in on the side, up at the bottom
        n /= np.linalg.norm(n, axis=1)[:, None]
        # (22 October 2026, chalk note 37: "Ribs tops (which are ending) we should cut to narrow down to fit the top
        # board. Cut could start with deck top level.") in the waist and forward the band narrows from the main deck's
        # top to the rail, to RIB_HEAD at the top, where the cap rail covers it
        mm_ = np.full(len(c), m)
        # ((25 October 2026, note 103) under a bulwarked quarterdeck: up through it to the cap, narrowing from its top)
        d_top = (Z_QD if r + 0.5 < QD_FRONT else Z_DECK) * MM
        if (r + 0.5 >= QD_FRONT or QD_BULWARK) and top > d_top + 100:
            f = np.clip((c[:, 1] - d_top) / (top - d_top), 0.0, 1.0)
            mm_ = m - (m - RIB_HEAD * MM) * f
        i_ = untangle(c + n * mm_[:, None])
        # clip the inner edge below the top and to the port half, ending on the centreline
        pts = []
        for k in range(len(i_)):
            q = i_[k]
            if q[1] > top:
                if k + 1 < len(i_) and i_[k + 1][1] <= top:
                    q1 = i_[k + 1]
                    t = (top - q[1]) / (q1[1] - q[1])
                    pts.append((q[0] + t * (q1[0] - q[0]), top))
                continue
            if q[0] <= 0.0:
                q0 = np.asarray(pts[-1]) if pts else q
                t = q0[0] / max(q0[0] - q[0], 1e-9)
                pts.append((0.0, q0[1] + t * (q[1] - q0[1])))
                break
            pts.append((float(q[0]), float(q[1])))
        if pts[-1][0] > 0.0:
            pts.append((0.0, pts[-1][1]))
        inn = np.array(pts)
        RIB_INNER[r] = inn
        # thickness check: inner points' distance to the outer edge, where the band is not filled to the centreline
        th = [_dist_to_curve(q, c) for q in inn[:-1] if q[0] > m * 0.6]
        RIB_CHECK[r] = {"zFrom": round(z_lo), "zTo": round(top), "thicknessMin": round(min(th)) if th else None,
                        "thicknessMax": round(max(th)) if th else None,
                        "filledToCentreFromZ": round(inn[-1][1])}
        outer_port = [tuple(q) for q in c]                           # top -> bottom -> (0, z_lo)
        outer_stbd = [(-y, z) for y, z in reversed(outer_port[:-1])]  # bottom -> top
        inner_stbd = [(-y, z) for y, z in inn[:-1]]                   # top -> near the centre
        inner_port = [tuple(q) for q in inn[::-1]]                    # (0, z_c) -> top
        ring = outer_port + outer_stbd + inner_stbd + inner_port
        out[str(r)] = [[round(y, 1), round(z, 1)] for y, z in ring]
    return out


DECK_T = 0.2          # deck and floor planking

# ---------------------------------------------------------------- backbone: keel, stem and sternpost
BB_SIDED, BB_MOULDED = 0.2, 0.6   # the user, 17 October: keel 0.6 high, the ribs' bottoms sit 0.4 into it
BB_OUT_KEEL = 0.0       # keel and post flush with the planking: 0.6 = planking 0.2 + the ribs' full 0.4
BB_OUT_STEM = 0.25      # the stem stands out ahead of the planking above the waterline, more visible at the bow
STEM_HEAD = 1.0         # the stem rises this far over the bow rail, on its own line (the user, 18 October: decorative)


def backbone_outline(step=0.1):
    """The backbone's side outline in the XZ plane (closed, metres): the centreline profile of the hull from the
    sternpost's head down the post, along the bottom and up the stem to the bow rail, moved out by the protrusion, and
    the same line BB_MOULDED further in."""
    pts = []
    # (22 October 2026, chalk note 46: "rudder handle perforates keel rib") the post's head under the tiller, which runs
    # in over it from the rudder's head to the wheel's ropes (the tiller's axis 150 under the quarterdeck's beams)
    z_post_top = Z_QD - DECK_T - 0.4 - 0.15 - 0.25
    for z in np.arange(z_post_top, LIFT, -step):
        pts.append((x_post(shape_z(z)), z))
    for x in np.arange(x_post(0.0), x_stem(0.0), step * 2):
        pts.append((x, LIFT))
    z = LIFT                                                # the stem up to where it meets the bow rail
    while z < Z_TOP and z < float(sheer(np.array([x_stem(shape_z(z))]))[0]) + STEM_HEAD:
        pts.append((x_stem(shape_z(z)), z))
        z += step
    p = np.array(pts)
    # drop near-duplicates
    keep = [0]
    for i in range(1, len(p)):
        if np.hypot(*(p[i] - p[keep[-1]])) > 0.02:
            keep.append(i)
    p = p[keep]
    d = np.gradient(p, axis=0)
    n = np.stack([d[:, 1], -d[:, 0]], axis=1)                 # outward: aft on the post, down on the keel, fore on the stem
    n /= np.linalg.norm(n, axis=1)[:, None]
    stem_part = (p[:, 0] > x_stem(0.0) - 1e-6) & (np.arange(len(p)) > len(p) // 3)
    post_part = (p[:, 0] < x_post(0.0) + 1e-6) & (p[:, 1] > LIFT + 1e-6)
    foot = real_z(TRANSOM_FOOT_Z)
    # above the transom's foot: behind its planking, unseen. (25 October 2026, the stern upright) 12 into the planking, not
    # on its inner face: the post's face and the planking's in one plane crashed the shell's boolean
    behind = -(SKIN - 0.012) * np.clip((p[:, 1] - foot) / 0.4, 0, 1)
    out = np.where(stem_part, BB_OUT_KEEL + (BB_OUT_STEM - BB_OUT_KEEL) * np.clip((p[:, 1] - 1.0) / 2.0, 0, 1),
                   np.where(post_part, BB_OUT_KEEL + behind, BB_OUT_KEEL))
    outer = p + n * out[:, None]
    inner = p + n * (out - BB_MOULDED)[:, None]
    ring = np.concatenate([outer, inner[::-1]])
    aft = ring[:, 0] < x_post(0.0) - 0.01                       # the post's head: cut level at z_post_top
    ring[:, 1] = np.where(aft & (ring[:, 1] > z_post_top), z_post_top, ring[:, 1])
    ring = untangle(ring)
    return [[round(x * MM, 1), round(z * MM, 1)] for x, z in ring]


def _cross(a, b, c, d):
    """Intersection point of segments ab and cd, or None."""
    r, s_ = b - a, d - c
    den = r[0] * s_[1] - r[1] * s_[0]
    if abs(den) < 1e-12:
        return None
    t = ((c[0] - a[0]) * s_[1] - (c[1] - a[1]) * s_[0]) / den
    u = ((c[0] - a[0]) * r[1] - (c[1] - a[1]) * r[0]) / den
    return a + t * r if 0 < t < 1 and 0 < u < 1 else None


def untangle(ring, span=40):
    """Cut the small loops an offset line makes at a tight inside corner: where segment i crosses segment j a few
    segments on, the points between go and the crossing takes their place."""
    pts = [np.asarray(q, float) for q in ring]
    k = 0
    while k < len(pts) - 1:
        cut = None
        for j in range(k + 2, min(k + span, len(pts) - 1)):
            x = _cross(pts[k], pts[k + 1], pts[j], pts[j + 1])
            if x is not None:
                cut = (j, x)
                break
        if cut:
            j, x = cut
            pts = pts[:k + 1] + [x] + pts[j + 1:]
        else:
            k += 1
    return np.array(pts)


# ---------------------------------------------------------------- deck beams: 0.2 along X, 0.4 deep, at every rib
BEAM_SIDED, BEAM_MOULDED = 0.2, 0.4
BEAM_SEAT = 0.1            # how far a beam's square end goes into its rib's notch


def inner_at(x, z, inner):
    """Inner half-breadth (mm) at x, z (mm), linear between the inner waterlines; 0 outside."""
    zs = [l["z"] for l in inner]
    if z < zs[0] or z > zs[-1]:
        return 0.0
    k = max(i for i, zz in enumerate(zs) if zz <= z)
    k1 = min(k + 1, len(zs) - 1)

    def at(l):
        px = [q[0] for q in l["pts"]]
        return float(np.interp(x, px, [q[1] for q in l["pts"]], left=0.0, right=0.0))
    if k1 == k:
        return at(inner[k])
    t = (z - zs[k]) / (zs[k1] - zs[k])
    return (1 - t) * at(inner[k]) + t * at(inner[k1])


def deck_beams(inner):
    """Beams under the main deck at every rib, and under the quarterdeck at the ribs it covers: boxes
    [x0, x1, z0, z1, half length] in mm, ending at the ribs' inner faces."""
    out = []
    for deck, x_max in ((Z_DECK, None), (Z_QD, QD_FRONT)):
        z1 = (deck - DECK_T) * MM
        z0 = z1 - BEAM_MOULDED * MM
        for r in RIBS:
            xm = (r + 0.5) * MM
            if x_max is not None and xm > x_max * MM:
                continue
            xs = [xm - BEAM_SIDED * MM / 2, xm, xm + BEAM_SIDED * MM / 2]
            if r not in RIB_INNER:
                continue
            inn = RIB_INNER[r][::-1]                                   # z rising
            # (22 October 2026, chalk note 35: "do not overlap joints ... make L cut in rib and place beam with 90deg
            # end ... I would make carpentry joint with L seat") the beam's end square, BEAM_SEAT into its rib past the
            # rib's inner face at the beam's underside; the build cuts the rib's notch for it (an L seat), not the beam
            half = float(np.interp(z0, inn[:, 1], inn[:, 0])) + BEAM_SEAT * MM
            if half > 300:
                out.append([round(xs[0], 1), round(xs[-1], 1), round(z0, 1), round(z1, 1), round(half, 1)])
    return out


BALUSTER_STEP = 0.2


def railings():
    """The quarterdeck's railing (the user, 18 October): a post on every cell border along both sides, across the
    transom at the Y cell borders and across the front, plus the corners; a top and a bottom rail joining all posts
    without gaps; balusters between, except over the quarterdeck's gun cells, which stay open for the guns. The front
    leaves the centre cell open for the stair. All in mm: posts [bottom, top], rails polylines, balusters [b, t]."""
    zb, zt = Z_QD + QD_SIDE, Z_QD_RAIL

    def side_y(x, z):
        xs, ys = waterline(z)
        return float(np.interp(x, xs, ys)) - SKIN / 2

    def tr_x(z):
        return stern_line(z) + 0.15

    corner_x = tr_x(zb)
    side_xs = [corner_x] + [float(x) for x in range(int(np.ceil(corner_x + 0.3)), int(QD_FRONT) + 1)]
    yc = side_y(corner_x, zb)
    tr_ys = sorted({-yc, yc} | {float(y) for y in (-2.5, -1.5, -0.5, 0.5, 1.5, 2.5) if abs(y) < yc - 0.3})
    yf = side_y(QD_FRONT, zb)
    # the front's posts on the Y cell borders (none crowding the side's corner post), but in the quarterdeck stair's gap;
    # the gap's two posts stand 0.1 out of it, so their collision keeps out of the stair's walkers' room
    qs = next(t for t in STAIRS if t["name"] == "quarterdeck")
    gap_lo, gap_hi = qs["y"] - 0.5, qs["y"] + 0.5
    borders = [y for y in (-2.5, -1.5, -0.5, 0.5, 1.5, 2.5) if abs(y) < yf - 0.5]
    port_front = [gap_hi + 0.1] + [y for y in borders if y > gap_hi + 1e-6]
    stbd_front = [y for y in borders if y < gap_lo - 1e-6] + [gap_lo - 0.1]

    def pt(x, y, z):
        return (x, y, z)

    # one path round the deck: the front's port end, aft along port, across the transom, forward along starboard,
    # the front's starboard end; each point is (bottom, top) at a post
    path = []
    breaks = []                                       # spans with no rails (between the front's two parts)
    if QD_BULWARK:
        # (25 October 2026, note 103) her side planked up round the quarterdeck: only the front's railing, standing on the
        # deck, from the stair's gap out to a post against the bulwark's inside each side (none crowding the gap's post)
        yin = side_y(QD_FRONT, Z_QD) - SKIN / 2 - 0.075
        zb = Z_QD
        port_y = port_front + ([yin] if yin - port_front[-1] > 0.3 else [])
        stbd_y = ([-yin] if stbd_front[0] + yin > 0.3 else []) + stbd_front
        if gap_hi < -yin + 0.3:                       # (the gap against the starboard side: no starboard part)
            stbd_y = []
        if gap_lo > yin - 0.3:
            port_y = []
        for y in port_y:
            path.append((pt(QD_FRONT - 0.05, y, zb), pt(QD_FRONT - 0.05, y, zt), "front"))
        if port_y and stbd_y:
            breaks.append(len(path) - 1)
        for y in stbd_y:
            path.append((pt(QD_FRONT - 0.05, y, zb), pt(QD_FRONT - 0.05, y, zt), "front"))
        side_xs, tr_ys = [], []
    for y in (port_front if not QD_BULWARK else []):  # front, port of the stair gap, from the gap out to the side
        path.append((pt(QD_FRONT - 0.05, y, zb), pt(QD_FRONT - 0.05, y, zt), "front"))
    for x in reversed(side_xs):                       # port side, forward to aft
        path.append((pt(x, side_y(x, zb), zb), pt(x, side_y(x, zt), zt), "side"))
    for y in reversed(tr_ys[1:-1]):                   # transom, port to starboard
        # (22 October 2026, the user: "aft rails that now are sloped can be just vertical") upright, not raked with the
        # transom
        path.append((pt(tr_x(zb), y, zb), pt(tr_x(zb), y, zt), "transom"))
    for x in side_xs:                                 # starboard side, aft to forward
        path.append((pt(x, -side_y(x, zb), zb), pt(x, -side_y(x, zt), zt), "side"))
    for y in (stbd_front if not QD_BULWARK else []):  # front, starboard of the gap, from the side in to the gap
        path.append((pt(QD_FRONT - 0.05, y, zb), pt(QD_FRONT - 0.05, y, zt), "front"))

    posts = [[b, t] for b, t, _ in path]
    top = [t for _, t, _ in path]
    # (22 October 2026, chalk note 32: "rails should be more robust with maybe just 1 thick vertical (same as main posts)
    # member in between. Thicker top connecting rail and bottom flush to the deck") the bottom rail lies on the deck;
    # one member the posts' size halfway along each span that isn't open for a gun, no balusters
    bottom = [(b[0], b[1], zb) for b, _, _ in path]
    bal = []
    for k_, ((b0, t0, k0), (b1, t1, k1)) in enumerate(zip(path[:-1], path[1:])):
        xm = 0.5 * (b0[0] + b1[0])
        if k_ in breaks:
            continue
        if k0 == k1 == "side" and any(c < xm < c + 1 for c in QD_GUN_CELLS):
            continue                                  # open for a gun
        if np.hypot(b1[0] - b0[0], b1[1] - b0[1]) < 0.6:
            continue
        bal.append([tuple(0.5 * (np.asarray(b0) + np.asarray(b1))), tuple(0.5 * (np.asarray(t0) + np.asarray(t1)))])
    mm = lambda q: [round(c * MM, 1) for c in q]
    return {"posts": [[mm(b), mm(t)] for b, t in posts], "kinds": [k for _, _, k in path], "breaks": breaks, "top": [mm(q) for q in top],
            "bottom": [mm(q) for q in bottom], "balusters": [[mm(b), mm(t)] for b, t in bal]}


def rope_ladders():
    """A rope ladder down each side from the gate's sill to below the waterline, its ropes 50 off the hull (mm)."""
    out = []
    for sgn in (1, -1):
        ropes = []
        for x in (GATE_CELL + 0.25, GATE_CELL + 0.75):
            line = []
            for z in np.arange(Z_DECK, Z_WL - 0.4, -0.25):
                xs, ys = waterline(z)
                line.append((x, sgn * (float(np.interp(x, xs, ys)) + 0.05), z))
            ropes.append([[round(c * MM, 1) for c in q] for q in line])
        out.append(ropes)
    return out


def hull_samples(step=0.5):
    """Points on the hull's outside where it meets the world (grounding, docks, walls), in the game's frame
    (x fore, y up from the waterline, z starboard): the keel, the stem and the sternpost on the centreline, the
    outline of the hull at the waterline, the bilge and the deck's edge, both sides."""
    pts = []
    g = lambda x, z, y: [round(x, 3), round(z - Z_WL, 3), round(-y, 3)]
    for x in np.arange(x_post(0.0), x_stem(0.0), step):              # the keel
        pts.append(g(float(x), LIFT, 0.0))
    for z in np.arange(LIFT, Z_DECK + 1.01, step):                   # the stem and the post/transom line
        pts.append(g(x_stem(shape_z(z)), float(z), 0.0))
        pts.append(g(x_post(shape_z(z)), float(z), 0.0))
    for z in (LIFT + 0.4, 1.0, 2.0, Z_WL, Z_WL + 0.25, Z_DECK):     # outlines: bilge, waterline (and 0.25 over it), deck edge
        xs, ys = waterline(z)
        x0 = max(xs[0], x_post(shape_z(z)))
        for x in np.arange(x0, xs[-1], step):
            y = float(np.interp(x, xs, ys))
            if y > 0.05:
                pts += [g(float(x), z, y), g(float(x), z, -y)]
    return pts


def build():
    lines = []
    inner = []
    for z in levels():
        xs, ys = waterline(z)
        lines.append({"z": round(z * MM, 3), "pts": [[round(x * MM, 3), round(y * MM, 3)] for x, y in zip(xs, ys)]})
        if z >= LIFT + SKIN - 1e-9:
            xi, yi = inner_waterline(z)
            inner.append({"z": round(z * MM, 3), "pts": [[round(x * MM, 3), round(y * MM, 3)] for x, y in zip(xi, yi)]})
    sx = np.linspace(-16.0, 14.0, 301)
    sz = sheer(sx)
    data = {
        "about": "Caravel hull lines, a proposal (models/caravel/caravel-design.md). Port half-breadths per "
                 "waterline from the after end to the stem; build_caravel.py mirrors, lofts and cuts them.",
        "units": "mm; X toward the bow, Y to port, Z up from the hull's underside; 1 block = 1000 mm",
        "levels": {"underside": LIFT * MM, "hold": Z_HOLD * MM, "waterline": Z_WL * MM, "mainDeck": Z_DECK * MM, "quarterdeck": Z_QD * MM, "quarterdeckSide": (Z_QD + QD_SIDE) * MM,
                   "waistRail": Z_WAIST_RAIL * MM, "quarterdeckRail": Z_QD_RAIL * MM, "top": Z_TOP * MM},
        "skin": SKIN * MM,
        "quarterdeckFront": QD_FRONT * MM,
        "hull": lines,
        "inner": inner,
        "ribProfiles": rib_profiles(inner),
        "backbone": {"halfWidth": BB_SIDED / 2 * MM, "outline": backbone_outline()},
        "beams": deck_beams(inner),
        "ribCheck": {str(k): v for k, v in RIB_CHECK.items()},
        "railings": railings(),
        "ropeLadders": rope_ladders(),
        "hullSamples": hull_samples(),
        "deckThickness": DECK_T * MM,
        "transomPlane": {"low": [round(stern_line(LIFT) * MM, 3), LIFT * MM],
                         "high": [round(stern_line(Z_QD_RAIL) * MM, 3), Z_QD_RAIL * MM]},
        "stem": [[x * MM, round(real_z(z) * MM, 3)] for z, x in STEM],
        "post": [[round(stern_line(z) * MM, 3), round(z * MM, 3)] for z in (LIFT, real_z(TRANSOM_FOOT_Z))],
        "sheer": [[round(x * MM, 3), round(z * MM, 3)] for x, z in zip(sx, sz)],
        "layout": {"masts": {k: {"cell": v["cell"], "heel": v["heel"] * MM, "rigs": v["rigs"]} for k, v in MASTS.items()},
                   "ribs": RIBS, "guns": GUN_CELLS, "quarterdeckGuns": QD_GUN_CELLS,
                   "hatch": {"x": [v * MM for v in HATCH["x"]], "y": [v * MM for v in HATCH["y"]]},
                   "stairs": [{"name": t["name"], "cell": t["cell"], "foot": [v * MM for v in t["foot"]],
                               "head": [v * MM for v in t["head"]], "y": t.get("y", 0.0) * MM} for t in STAIRS],
                   "gate": {"x": [GATE_CELL * MM, (GATE_CELL + 1) * MM], "sill": Z_DECK * MM, "head": Z_WAIST_RAIL * MM},
                   "port": {k: v * MM for k, v in PORT.items()},
                   "cabin": {"x": CABIN["x"] * MM, "doors": CABIN["doors"], "windows": CABIN["windows"]} if not KITCHEN else None,
                   "kitchen": KITCHEN,
                   "swivels": LAY_V.get("swivels", []),
                   "sails": {k: {kk: ([vv * MM if isinstance(vv, (int, float)) else vv for vv in v] if isinstance(v, list) else v)
                                 for kk, v in sl.items()} for k, sl in LAY_V.get("sails", {}).items()},
                   "variant": VARIANT,
                   "fittings": FITTINGS},
    }
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "caravel-lines.json"), "w") as f:
        json.dump(data, f, indent=1)
    return data


def report(data):
    zs = [l["z"] / MM for l in data["hull"]]
    widest = max((max(p[1] for p in l["pts"]), l["z"]) for l in data["hull"])
    xs_wl, ys_wl = waterline(Z_WL)
    print(f"waterlines {len(zs)}  z {zs[0]}..{zs[-1]}")
    print(f"widest half-breadth {widest[0]:.0f} mm at Z {widest[1]:.0f} (cell limit 3500)")
    print(f"at the waterline: x {xs_wl[0]:.2f}..{xs_wl[-1]:.2f}, LWL {xs_wl[-1] - max(xs_wl[0], x_transom(Z_WL)):.2f} m")
    print(f"stem at deck {x_stem(Z_DECK):.2f}, transom at deck {x_transom(Z_DECK):.2f}: "
          f"length on deck {x_stem(Z_DECK) - x_transom(Z_DECK):.2f} m")
    for c in GUN_CELLS:
        xc = c + 0.5
        xs, ys = waterline(Z_DECK + 0.25)
        print(f"gun cell {c:+d}: half-breadth at the sill {np.interp(xc, xs, ys):.2f} m")


def plot(data):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        return
    fig, (a1, a2, a3) = plt.subplots(3, 1, figsize=(14, 13), gridspec_kw={"height_ratios": [1.1, 1, 1]})
    # profile
    sx = np.array([p[0] for p in data["sheer"]]) / MM
    sz = np.array([p[1] for p in data["sheer"]]) / MM
    keep = (sx > x_transom(9.0)) & (sx < x_stem(7.0))
    a1.plot(sx[keep], sz[keep], "k")
    zz = np.linspace(0, 6.5, 50)
    a1.plot([x_stem(z) for z in zz], [real_z(z) for z in zz], "k")
    zz = np.linspace(0, Z_QD_RAIL, 50)
    a1.plot([x_post(z) for z in zz], [real_z(z) for z in zz], "k")
    a1.plot([x_post(0), x_stem(0)], [LIFT, LIFT], "k")
    a1.axhline(0.0, color="0.6", lw=0.6, ls=":")
    for z, c in [(Z_WL, "b"), (Z_HOLD, "0.6"), (Z_DECK, "0.3"), (Z_QD, "0.3")]:
        a1.axhline(z, color=c, lw=0.6)
    for k, m in MASTS.items():
        a1.axvspan(m["cell"], m["cell"] + 1, color="tab:red", alpha=0.25)
    for r in RIBS:
        a1.axvspan(r, r + 1, color="tab:brown", alpha=0.12)
    for c in GUN_CELLS:
        a1.add_patch(plt.Rectangle((c + 0.125, Z_DECK + PORT["sill"]), 0.75, 0.75, fc="none", ec="tab:orange"))
    for c in QD_GUN_CELLS:
        a1.add_patch(plt.Rectangle((c + 0.125, Z_QD + PORT["sill"]), 0.75, 0.75, fc="none", ec="tab:orange"))
    bb = np.array(data["backbone"]["outline"]) / MM
    a1.fill(bb[:, 0], bb[:, 1], color="saddlebrown", alpha=0.6, lw=0)
    for x0, x1, z0, z1, _ in data["beams"]:
        a1.add_patch(plt.Rectangle((x0 / MM, z0 / MM), (x1 - x0) / MM, (z1 - z0) / MM, color="saddlebrown"))
    a1.set_title("Profile (mast cells red, rib cells brown, gun ports orange; backbone and beams filled)")
    # plan
    for l in data["hull"]:
        z = l["z"] / MM
        if abs(z * 2 - round(z * 2)) > 1e-6 and z not in (Z_WL,):
            continue
        p = np.array(l["pts"]) / MM
        m = p[:, 0] >= x_post(z) - 1e-6
        a2.plot(p[m, 0], p[m, 1], lw=1.4 if z == Z_WL else 0.6, color="b" if z == Z_WL else "k")
    a2.set_title("Half-breadth plan: waterlines every 0.5 m (waterline blue)")
    # body plan
    for x in np.arange(-12.0, 11.0, 1.0):
        pts = []
        for l in data["hull"]:
            z = l["z"] / MM
            p = np.array(l["pts"]) / MM
            if x < p[0, 0] or x > p[-1, 0]:
                continue
            if z > TRANSOM_LOW[0] and x < x_transom(z):
                continue
            pts.append((float(np.interp(x, p[:, 0], p[:, 1])), z))
        if pts:
            pts = np.array(pts)
            side = 1 if x >= XM_A else -1
            a3.plot(side * pts[:, 0], pts[:, 1], "k", lw=0.6)
    a3.axhline(Z_WL, color="b", lw=0.6)
    for y in np.arange(-3.5, 3.6, 1.0):
        a3.axvline(y, color="0.85", lw=0.5)
    a3.set_title("Body plan (fore right, aft left), stations every metre; grey = cell borders")
    for a in (a1, a2, a3):
        a.set_aspect("equal")
        a.grid(True, color="0.9", lw=0.5)
    a1.set_xticks(range(-14, 13))
    a2.set_xticks(range(-14, 13))
    a3.set_xticks(range(-4, 5))
    fig.tight_layout()
    fig.savefig(os.path.join(OUT, "views", "lines.png"), dpi=110)


if __name__ == "__main__":
    d = build()
    report(d)
    os.makedirs(os.path.join(OUT, "views"), exist_ok=True)
    if "--no-plot" not in sys.argv:
        plot(d)
