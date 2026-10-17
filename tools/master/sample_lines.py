"""One-off bootstrap for the master hull model (docs/master-model.md): sample Magelan's hull lines from carrack-m's
generator into a plain data file that build_master.py lofts in AstoCAD. Once the user has edited the master model,
the model is the source and this script is not run again.

    python tools/master/sample_lines.py [out.json]

Model frame (mm): X = design x, Y = -z (port positive), Z = design y + 3.5 blocks. 1 block = 1000 mm.
Every waterline is its port (+Y) half: (x, half-breadth) pairs from the after end to the forward one.
"""
import json
import math
import os
import sys

os.environ.update(HANSA_MODULAR="1", HANSA_SHIP="hansa-carrack", HANSA_FACETS="0")
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "shipgen"))
import numpy as np  # noqa: E402
import carrack as c  # noqa: E402

MM = 1000.0
DZ = 3.5                      # model Z = design y + DZ
N = 81                        # points per waterline piece
XJ = -6.0                     # every hull waterline in pieces split where carrack-m's formulas change (their curvature
XB = c.MIDBODY_FWD            # jumps there, which one B-spline can't follow): the run begins aft of XJ, the bow forward
                              # of XB. Above the poop's overhang the after piece is the poop's, the others always the
                              # hull's, so the overhang's ledge and the hull below it share their forward edges exactly
POST_HW = 0.125               # the stem's, the sternpost's and the forecastle tip's half width: every waterline's ends
                              # blend out to it (w + p exp(-w/p)), so the sides close on a 250 face, never a knife edge


# v1's castle deck edges, each on its last beam (docs/master-model.md section 4): the quarterdeck's at -1 (Magelan -2), the
# forecastle's at 16 (Magelan 14), the poop's front at -9 as Magelan's. As carrack-m, each castle's side stays full
# height a quarter block past its edge, then its break sweeps down
QD_EDGE, FC_EDGE, POOP_FRONT = -1.0, 16.0, -9.0


def rail_y(x):
    """carrack-m's rail_y (tools/shipgen/carrack.py) with v1's castle edges."""
    x = np.asarray(x, float)
    castle = c.QUARTER + 1.0 + c.CASTLE_UP
    waist = c.MAIN + c.WAIST_RAIL
    qa, fa, pf = QD_EDGE + 0.25, FC_EDGE - 0.25, POOP_FRONT + 0.25
    y = np.full_like(x, waist)
    y = np.where(x < qa, castle, y)
    y = np.where(x < pf, c.POOP + 1.0 + c.CASTLE_UP, y)
    y = np.where(x >= fa, castle, y)
    rise, bl = castle - waist, c.BREAK_L_FRONT
    y = np.where((x >= qa) & (x < qa + bl), waist + rise * c._sweep((qa + bl - x) / bl), y)
    y = np.where((x > fa - bl) & (x < fa), waist + rise * c._sweep((x - fa + bl) / bl), y)
    y = np.where((x >= pf) & (x < pf + c.BREAK_L), castle + 3.0 * c._sweep((pf + c.BREAK_L - x) / c.BREAK_L), y)
    return y


def levels(lo, hi, fine_to=None, fine=0.125, step=0.5):
    ys = set()
    y = lo
    while y < hi - 1e-9:
        ys.add(round(y, 4))
        y += fine if (fine_to is not None and y < fine_to - 1e-9) else step
    ys.add(round(hi, 4))
    return sorted(ys)


def ends_clustered(n):
    s = np.linspace(0.0, 1.0, n)
    return 0.5 - 0.5 * np.cos(np.pi * s)


def blend(w):
    w = np.maximum(np.asarray(w, float), 0.0)
    return w + POST_HW * np.exp(-w / POST_HW)


def piece(fn, xa, xf, y):
    xs = xa + (xf - xa) * ends_clustered(N)
    ws = blend(fn(xs, np.full_like(xs, y)))
    return [[round(x * MM, 3), round(w * MM, 3)] for x, w in zip(xs, ws)]


def hull_waterline(y, upper):
    """Above the overhang's underside (upper) the after piece is the poop's: carrack-m's poop_hw from the overhang's end."""
    xf = float(c.interp(y, c.BOW_X))          # the stem
    xa = float(c.interp(y, c.STERN_X))        # the hull's own after end
    aft = piece(c.poop_hw, xa - c.POOP_OVERHANG, XJ, y) if upper else piece(c.hull_hw, xa, XJ, y)
    return {"z": round((y + DZ) * MM, 3), "pieces": [aft, piece(c.hull_hw, XJ, XB, y), piece(c.hull_hw, XB, xf, y)]}


def fc_tri(xs, ys):
    """carrack-m's fc_tri, carried a block aft of FC_X0 (inside the hull, which is wider there) at its width there."""
    y = float(ys[0])
    w14 = float(c.hull_hw(c.FC_X0, y))
    u = np.clip((xs - c.FC_X0) / (c.FC_TIP - c.FC_X0), 0, 1)
    # (25 mm in at FC_X0, fading out by 2 blocks forward: its pieces' joint at FC_X0 is then inside the hull, and its side
    # crosses the hull's inside a face. On her surface, it lay along her: the booleans missed every overlap)
    inset = 0.025 * np.clip(1 - (xs - c.FC_X0) / 2.0, 0, 1)
    return np.where(xs < c.FC_X0, w14, w14 * (1 - u ** 1.5)) - inset


def fc_waterline(y):
    return {"z": round((y + DZ) * MM, 3), "pieces": [piece(fc_tri, c.FC_X0 - 1.0, c.FC_X0, y), piece(fc_tri, c.FC_X0, c.FC_TIP, y)]}


def check_points():
    """Points on carrack-m's outside (half_width, the port side) for checking the model against her: on a 1 x 0.5 block
    grid, below the sheer, clear of her ends and of the overhang's and the forecastle's steps."""
    out = []
    for x in np.arange(-18.0, 26.01, 1.0):
        for y in np.arange(c.KEEL + 0.125, 11.0, 0.5):
            hw = float(c.half_width(np.array(x), np.array(y)))
            if hw < 0.4 or y > float(rail_y(np.array([x]))[0]) - 0.15:
                continue
            if min(abs(y - c.OH_Y), abs(y - float(c.fc_under(x)))) < 0.1:
                continue
            ends = (float(c.interp(y, c.STERN_X)) - (c.POOP_OVERHANG if y >= c.OH_Y else 0.0), c.outer_bow_x(y))
            if min(abs(x - e) for e in ends) < 0.4 or abs(x - float(c.interp(y, c.STERN_X))) < 0.4:
                continue
            out.append([round(x * MM, 3), round(hw * MM, 3), round((y + DZ) * MM, 3)])
    return out


def main(out):
    top = 12.0                                         # above every rail (the poop's at 10.75); the sheer cuts it
    # finer round the bilge's turn and the forefoot; one at the transom's start; the lower hull up to the overhang's
    # underside, the upper from it
    lower_ys = sorted(set(levels(c.KEEL, c.OH_Y, fine_to=-2.5)) | {0.625} | {round(y, 4) for y in np.arange(-2.5, 2.5001, 0.25)})
    upper_ys = levels(c.OH_Y, top)
    fc_ys = levels(c.FC_LOW, top)
    xs = np.arange(-22.0, 30.0001, 0.0625)
    rail = rail_y(xs)
    assert abs(float(rail_y(np.array([-2.0 - 1.0]))[0]) - float(c.rail(-3.0))) < 1e-9
    xs0 = float(c.interp(c.FC_LOW, c.BOW_X))
    data = {
        "about": "Magelan's (carrack-m's) hull lines with Hansa's decision 14 bottom, sampled by tools/master/sample_lines.py. "
                 "Model mm: X along, Y to port, Z up from the hull's underside. Each waterline: its height z and its port "
                 "half's (x, half-breadth) from the after end to the forward end.",
        "units": "mm",
        "splits": [XJ * MM, XB * MM],
        "lower": [hull_waterline(y, False) for y in lower_ys],
        "upper": [hull_waterline(y, True) for y in upper_ys],
        "forecastle": [fc_waterline(y) for y in fc_ys],
        "forecastleUnderside": [[round(x * MM, 3), round((y + DZ) * MM, 3)] for x, y in
                                [(c.FC_X0 - 1.0, c.FC_LOW), (xs0, c.FC_LOW), (c.FC_TIP, c.FC_Y0)]],
        "sheer": [[round(x * MM, 3), round((y + DZ) * MM, 3)] for x, y in zip(xs, rail)],
        "checkPoints": check_points(),
        "reference": {
            "waterline": (c.WATER + DZ) * MM,
            "deckTops": {"hold": (c.HOLD_FLOOR + DZ) * MM, "lower": (c.LOWER + DZ) * MM, "main": (c.MAIN + DZ) * MM,
                         "quarterdeck": (c.QUARTER + DZ) * MM, "forecastle": (c.QUARTER + DZ) * MM, "poop": (c.POOP + DZ) * MM},
            "mastCells": {"mizzen": [-13000, -12000], "main": [3000, 4000], "fore": [17000, 18000]},
            "ribCells": [-16, -14, -12, -10, -6, -2, 2, 4, 8, 12, 16, 18, 20, 22, 24],
            "castleEdges": {"poopFront": POOP_FRONT * MM, "quarterdeckEdge": QD_EDGE * MM, "forecastleEdge": FC_EDGE * MM},
            # each deck: its planking's top, and its length (None: the whole ship)
            "decks": [{"name": "hold", "top": 1000, "x": None}, {"name": "lower", "top": 4000, "x": None},
                      {"name": "main", "top": 7000, "x": None},
                      {"name": "quarterdeck", "top": 10000, "x": [None, QD_EDGE * MM]},
                      {"name": "forecastle", "top": 10000, "x": [FC_EDGE * MM, None]},
                      {"name": "poop", "top": 13000, "x": [None, POOP_FRONT * MM]}],
            "shellThickness": 250, "deckThickness": 250, "beamDepth": 500, "timberSided": 250, "ribMoulded": 250,
            "floorTimbers": [250, 750], "backboneSided": 250, "backboneMoulded": 750,
            "backboneTop": (c.OH_Y + DZ) * MM,     # the quarterdeck's underside: no strip along the overhang's floor
        },
    }
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    print(f"{out}: lower hull {len(lower_ys)} waterlines, upper {len(upper_ys)}, forecastle {len(fc_ys)}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                                                             "models", "hansa-carrack", "magelan-lines.json"))
