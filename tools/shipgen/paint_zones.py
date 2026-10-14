"""
Paint zones of the carrack: the parts of her a player would pick one colour for (the castles' sides, the rail caps, the
balusters, the great cabin's front, its walls, its mouldings, the lower deck's walls...), each with the mesh quads (and
voxel faces) that belong to it, worked out from the built design. Nothing in the generator is changed: build() wraps
carrack's helpers (every module function whose first argument is the design: hull, skin, raised_strip, balustrades,
front_dress, lathe, _quad_facing, ...) and the Design's mesh primitives (add_box_mesh, add_prism, add_beam_mesh,
add_cylinder) for the length of a build, so each quad says which helpers made it (outermost first) and with what box;
zones() then sorts the quads by those tags, by where they are (which deck, which room, above the wales or not) and by
which way they face.

  import paint_zones
  d = paint_zones.build()                    # carrack.build(), tagged
  boxes, _ = d.mesh_voxels()
  z = paint_zones.zones(d, boxes)            # {code: {"name", "kind", "default", "quads": [...], "voxel_faces": [...]}}

  python tools/shipgen/paint_zones.py        # docs/views/paint/zones.json: each zone's code, name, kind, default, area

"quads" are indices into d.quads; "voxel_faces" are (index into the boxes mesh_voxels() returned, face name) for the
voxel walls and decks drawn as blocks (the castle fronts' boards, the decks' undersides, the companion house's walls).
Everything not in a zone (the decks you walk on, the tarred and coppered bottom, masts, spars, rigging, guns, glass,
metal, the stairs' treads, the boat gallows, the hearth...) isn't paintable.
"""
import collections
import inspect
import json
import os
import sys
import time

sys.dont_write_bytecode = True     # (the generator's own __pycache__ is left alone)

import numpy as np  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "docs", "views", "paint")

# code, name, kind. kind: "panel" (broad boards), "band" (a strake run as a band), "moulding" (rails, caps, casings,
# pilasters, frames), "carving" (turned or carved work), "board" (the name board), "ceiling" (beams and deck undersides),
# "room" (all of a working space's inside at once)
ZONES = [
    ("wales", "Wales, and the hull's sides from the waterline up to them", "moulding"),
    ("topsides", "Topsides above the wales, up to the waist's rail (outside, with the gates and the gun ports' lids)",
     "panel"),
    ("frieze", "Frieze: the top strake under each level rail cap (outside)", "band"),
    ("sterncastle-sides", "Sterncastle's sides above the waist rail, and the poop overhang's underside", "panel"),
    ("forecastle-sides", "Forecastle's sides above the waist rail, and its underside over the bow", "panel"),
    ("transom", "Transom (the flat stern) above the wales", "panel"),
    ("castle-mouldings", "Mouldings along the castles' deck levels outside, and the knees under the poop", "moulding"),
    ("rail-caps", "Rail caps (waist, castles, castle breaks, the gates' pieces)", "moulding"),
    # ((0.3.14, the user's note 144) the carrack's side windows as the stern gallery's: their pilasters and rails too)
    ("side-windows", "Castles' side windows (outside): casings, sashes, pilasters, cornice and sill rail", "moulding"),
    ("stern-gallery", "Stern gallery: frames, sashes, cornices and sill rails of the lights", "moulding"),
    ("stern-carvings", "Stern carvings: pilasters and capitals, the gallery's balustrade, taffrail, name board's frame",
     "carving"),
    ("nameboard", "Name board (the mod paints her name on it)", "board"),
    ("figurehead", "Figurehead (the mermaid)", "carving"),
    ("waist-bulwarks", "Inside of the waist's bulwarks (with the gates' and the gun ports' lids' insides, the ports' "
     "linings)", "panel"),
    ("castle-bulwarks", "Inside of the castle decks' bulwarks (quarterdeck, poop deck, forecastle deck)", "panel"),
    ("bulwark-frames", "Frames (ribs) standing inside the open bulwarks", "moulding"),
    ("great-cabin-front", "Great cabin's front toward the waist (its boards)", "panel"),
    ("poop-cabin-front", "Poop cabin's front toward the quarterdeck (its boards)", "panel"),
    ("forecastle-front", "Forecastle's bulkhead toward the waist (its boards)", "panel"),
    ("front-mouldings", "Cabin fronts' mouldings: pilasters with their bases and capitals, cornice, sill rail, panel "
     "mouldings, window casings", "moulding"),
    ("door-casings", "Door casings (both faces)", "moulding"),
    ("balusters", "Balusters of the castle fronts' railings, the stairs' and the companion's", "carving"),
    ("balustrade-rails", "Railings' plinths, handrails, caps and newel posts, and the stairs' stringers (castle fronts, "
     "stairs, the companion)", "moulding"),
    ("great-cabin-walls", "Great cabin's walls (the sides, the inside of its front and of the transom)", "panel"),
    ("poop-cabin-walls", "Poop cabin's walls", "panel"),
    ("cabin-trim", "Cabins' trim: frames, stern timbers, window casings inside", "moulding"),
    ("cabin-ceilings", "Cabins' ceilings: beams, knees, clamps, the deck above's underside", "ceiling"),
    ("forecastle-room", "Forecastle's room inside: walls, frames, ceiling", "room"),
    ("lower-deck", "Lower deck: walls, frames, beams and the main deck's underside", "room"),
    ("hold", "Hold: walls, frames, beams and the lower deck's underside", "room"),
]
if getattr(sys.modules.get("carrack") or __import__("carrack"), "GRID", False):
    # (0.3.12, the user's notes 117: "beams and ceiling should have different painting groups so they can be separately
    # painted", and 125: "in lower decks also allow painting ribs and walls and ceiling different colors") the cabins'
    # beams apart from their ceilings; the forecastle's, the lower deck's and the hold's walls, frames (ribs), beams and
    # ceilings each a zone of its own. The rooms' old codes are their walls now, so a colour saved for one stays on its
    # walls
    _Z = dict((c, (n, k)) for c, n, k in ZONES)
    ZONES = [z for z in ZONES if z[0] not in ("cabin-ceilings", "forecastle-room", "lower-deck", "hold")]
    ZONES += [
        ("cabin-ceilings", "Cabins' ceilings: the deck above's underside (great cabin, poop cabin)", "ceiling"),
        ("cabin-beams", "Cabins' beams: the deck beams under the deck above, the carlings and headers", "ceiling"),
        ("forecastle-room", "Forecastle's walls: the inside planking, the inside of its bulkhead", "panel"),
        ("forecastle-frames", "Forecastle's frames (ribs): the riders, their knees, the inlays down the walls", "moulding"),
        ("forecastle-beams", "Forecastle's beams under the forecastle deck", "ceiling"),
        ("forecastle-ceiling", "Forecastle's ceiling: the forecastle deck's underside, the grating", "ceiling"),
        ("lower-deck", "Lower deck's walls: the inside planking", "panel"),
        ("lower-deck-frames", "Lower deck's frames (ribs): the riders, their knees, the inlays down the walls", "moulding"),
        ("lower-deck-beams", "Lower deck's beams: the main deck's beams, the carlings and headers round its openings",
         "ceiling"),
        ("lower-deck-ceiling", "Lower deck's ceiling: the main deck's underside", "ceiling"),
        ("hold", "Hold's walls: the inside planking", "panel"),
        ("hold-frames", "Hold's frames (ribs): the riders, their knees, the inlays down the walls", "moulding"),
        ("hold-beams", "Hold's beams: the lower deck's beams, the carlings between them and round its openings",
         "ceiling"),
        ("hold-ceiling", "Hold's ceiling: the lower deck's underside", "ceiling"),
    ]
    # (0.3.13, the user's note 134: "All doors and other elements like ribs are grouped and can only have one color")
    # each door's casing its own zone (door-casings: the great cabin's middle door, as before), the cabin fronts'
    # windows' casings apart from the pilasters and rails (note 135), the castle decks' frames apart from the waist's,
    # and the cabins' frames (ribs, with their riders and knees) apart from their other trim
    def _after(code, *new):
        i = [z[0] for z in ZONES].index(code)
        ZONES[i + 1:i + 1] = list(new)

    ZONES = [(c, "Great cabin's door casing (its middle door, both faces)" if c == "door-casings" else
              "Frames (ribs) standing inside the waist's bulwarks" if c == "bulwark-frames" else
              "Cabins' trim: the stern timbers, the side windows' casings inside" if c == "cabin-trim" else n, k)
             for c, n, k in ZONES]
    _after("door-casings",
           ("door-casings-companion", "Companion's door casings: its door in the great cabin's front, the stairwell's "
            "door (both faces)", "moulding"),
           ("door-casings-forecastle", "Forecastle's door casing (both faces)", "moulding"),
           ("door-casings-poopcabin", "Poop cabin's door casing (both faces)", "moulding"))
    _after("front-mouldings", ("window-casings", "Cabin fronts' windows: casings, sills, mullions and transoms (both "
                               "faces)", "moulding"))
    _after("bulwark-frames", ("castle-frames", "Frames (ribs) standing inside the castle decks' bulwarks", "moulding"))
    _after("cabin-trim", ("cabin-frames", "Cabins' frames (ribs): the riders, their knees, the inlays down the walls",
                          "moulding"))
import carrack as _carrack  # noqa: E402
if getattr(_carrack, "GRID", False):
    # (0.3.9, the user's note 66: no figurehead on the carrack on the grid; 0.3.10, note 103: the transom's planking is
    # in the sides' bands, no zone of its own)
    ZONES = [z for z in ZONES if z[0] not in ("figurehead", "transom")]
ZONE_CODES = [z[0] for z in ZONES]
PAINTABLE_MATS = ("oak", "tarred", "timber", "deck", "gold")
VOXEL_FACES = ("east", "west", "up", "down", "south", "north")
VOXEL_DIRS = {"east": (1, 0, 0), "west": (-1, 0, 0), "up": (0, 1, 0), "down": (0, -1, 0), "south": (0, 0, 1),
              "north": (0, 0, -1)}

PRIMS = ("add_box_mesh", "add_prism", "add_beam_mesh", "add_cylinder")
_installed = {}          # id(module) -> True


# ==================================================================== tagging the build
def _scalar_args(sig, a, k):
    try:
        b = sig.bind_partial(*a, **k).arguments
    except TypeError:
        return {}
    return {n: v for n, v in b.items() if isinstance(v, (int, float, str, bool, np.floating, np.integer))}


def install():
    """(Before 0.3.8 this wrapped the generator's helpers to tag their quads; they're tagged at the source now.)"""


def build(retries=10, wait=60):
    """carrack.build(). (Another hand may be editing the generator: an import or a build that fails is retried after a
    minute, the generator's modules imported afresh.)"""
    for attempt in range(retries):
        try:
            import carrack
            return carrack.build()
        except Exception as e:  # noqa: BLE001
            if attempt == retries - 1:
                raise
            print(f"paint_zones: the build failed ({type(e).__name__}: {e}); trying again in {wait} s")
            for m in ("carrack", "planking", "gun", "shipgen", "texview", "view"):
                sys.modules.pop(m, None)
            time.sleep(wait)


def assign(d, boxes):
    """
    Sorts her into the zones (before Design.translate_y: the rooms are known by the design's heights): each quad's zone
    index in q["pz"], each voxel face's in d.paint_voxel {(box index, face): zone index}, and d.paint_zone_list (the
    ship definition's "paintZones": code, name, kind, default, in ZONES' order, their indices).
    """
    zmap = zones(d, boxes)
    index = {code: k for k, code in enumerate(ZONE_CODES)}
    for q in d.quads:
        q.pop("pz", None)
    for code, zd in zmap.items():
        for i in zd["quads"]:
            d.quads[i]["pz"] = index[code]
    d.paint_voxel = {(bi, f): index[code] for code, zd in zmap.items() for bi, f in zd["voxel_faces"]}
    # each face's cells (the writer finds a box's face's zone by them: its boxes merge differently once moved)
    cells = {}
    for (bi, f), z in d.paint_voxel.items():
        b = boxes[bi]
        lo = np.round((np.asarray(b["lo"], float) - d.o) / 0.25).astype(int)
        hi = np.round((np.asarray(b["hi"], float) - d.o) / 0.25).astype(int)
        for i in range(lo[0], hi[0]):
            for j in range(lo[1], hi[1]):
                for k in range(lo[2], hi[2]):
                    cells[(i, j, k, f)] = z
    d.paint_voxel_cells = cells
    d.paint_zone_list = [{"code": code, "name": zmap[code]["name"], "kind": zmap[code]["kind"],
                          "default": zmap[code]["default"] or "oak"} for code in ZONE_CODES]
    return zmap


def write_summary(d, zmap):
    os.makedirs(OUT, exist_ok=True)
    rows = summary(d, zmap)
    # ((0.3.14) the carrack's (the shipped, option B) in zones.json, which the offline tests compare to her mesh; another
    # design's (carrack-a, option A) beside it in zones-<code>.json, so building it doesn't make zones.json disagree)
    path = os.path.join(OUT, "zones.json" if d.code == "carrack" else f"zones-{d.code}.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump({"design": d.code, "unit": "m² (a block is 1 m)", "zones": rows}, fh, indent=1, ensure_ascii=False)
    return path, rows


# ==================================================================== where things are
def _C():
    import carrack
    return carrack


def room_at(x, y):
    """Which space a point inside her is in: hold, lower, greatcabin, poopcabin, forecastle, waist (the open waist's
    bulwarks), castledeck (the quarterdeck's, poop deck's and forecastle deck's bulwarks)."""
    C = _C()
    if y < C.LOWER - 0.03:
        return "hold"
    if y < C.MAIN - 0.03:
        return "lower"
    # ((0.3.24, notes 184, 185) carrack-m: the quarterdeck's and the forecastle deck's edges a block out over the waist
    # (EDGE_OUT): their sides full height from there; under them, before the fronts, still the waist)
    qa, fa = C.WAIST_AFT + C.EDGE_OUT, C.WAIST_FWD - C.EDGE_OUT
    if qa - 0.02 <= x <= fa + 0.02:
        return "waist"              # (all the way up the castle breaks' sweeps: they rise over the waist)
    if y < C.QUARTER - C.DECK_T + 0.02:
        # ((0.3.28, notes 225, 227) carrack-m: the fronts on the edges' lines, -2 and 14)
        wa_, wf_ = (C.FRONT_AFT, C.FRONT_FWD) if C.EDGE_OUT else (C.WAIST_AFT, C.WAIST_FWD)
        if wa_ - 0.02 <= x <= wf_ + 0.02:
            return "waist"
        return "greatcabin" if x < wa_ else "forecastle"
    if x < C.POOP_FRONT - 0.05 and y < C.POOP - C.DECK_T + 0.02:
        return "poopcabin"
    return "castledeck"


def deck_tops(x):
    """The decks there are at x (the castle decks only under the castles)."""
    C = _C()
    tops = [C.LOWER, C.MAIN]
    if x < C.WAIST_AFT + C.EDGE_OUT or x > C.WAIST_FWD - C.EDGE_OUT:
        tops.append(C.QUARTER)
    if x < C.POOP_FRONT:
        tops.append(C.POOP)
    return tops


def room_of(d, p, c):
    """The space a quad of the inside (planking, a frame's piece, a ceiling) shows in. One running on through a deck
    or a castle wall is in the space where more of it shows (the part inside the deck's or the wall's thickness is
    hidden); straddle() says how much shows on the other side."""
    C = _C()
    x, y = float(c[0]), float(c[1])
    lo, hi = float(p[:, 1].min()), float(p[:, 1].max())
    for Y in deck_tops(x):
        if lo < Y - 0.01 and hi > Y + 0.01:
            above, below = hi - Y, max(0.0, Y - C.DECK_T - lo)
            y = (Y + hi) / 2 if above >= below else (lo + min(hi, Y - C.DECK_T)) / 2
            break
    xl, xh = float(p[:, 0].min()), float(p[:, 0].max())
    for x0, x1, y0, y1 in getattr(d, "bulkheads", []):
        if xl < x0 - 0.01 and xh > x1 + 0.01 and y0 - 0.05 < y < y1 + 0.05:
            aft, fwd = x0 - xl, xh - x1
            x = (xl + x0) / 2 if aft >= fwd else (x1 + xh) / 2
            break
    return room_at(x, y)


def straddle(d, p):
    """How much of a quad of the inside shows on the side of a deck or castle wall it isn't sorted to (0: none)."""
    C = _C()
    lo, hi = float(p[:, 1].min()), float(p[:, 1].max())
    for Y in deck_tops(float(p[:, 0].mean())):
        if lo < Y - 0.01 and hi > Y + 0.01:
            return min(hi - Y, max(0.0, Y - C.DECK_T - lo))
    xl, xh = float(p[:, 0].min()), float(p[:, 0].max())
    yc = float(p[:, 1].mean())
    for x0, x1, y0, y1 in getattr(d, "bulkheads", []):
        if xl < x0 - 0.01 and xh > x1 + 0.01 and y0 - 0.05 < yc < y1 + 0.05:
            return min(x0 - xl, xh - x1)
    return 0.0


INSIDE_ZONE = {"hold": "hold", "lower": "lower-deck", "greatcabin": "great-cabin-walls",
               "poopcabin": "poop-cabin-walls", "forecastle": "forecastle-room", "waist": "waist-bulwarks",
               "castledeck": "castle-bulwarks"}
FRAME_ZONE = {"hold": "hold", "lower": "lower-deck", "greatcabin": "cabin-trim", "poopcabin": "cabin-trim",
              "forecastle": "forecastle-room", "waist": "bulwark-frames", "castledeck": "bulwark-frames"}
CEILING_ZONE = {"hold": "hold", "lower": "lower-deck", "greatcabin": "cabin-ceilings", "poopcabin": "cabin-ceilings",
                "forecastle": "forecastle-room"}
BEAM_ZONE = dict(CEILING_ZONE)
if "cabin-beams" in ZONE_CODES:
    # (0.3.12, notes 117 and 125) the rooms' frames, beams and ceilings each their own (tag "beam": the deck beams,
    # carlings and headers)
    FRAME_ZONE.update({"hold": "hold-frames", "lower": "lower-deck-frames", "forecastle": "forecastle-frames"})
    CEILING_ZONE.update({"hold": "hold-ceiling", "lower": "lower-deck-ceiling", "forecastle": "forecastle-ceiling"})
    BEAM_ZONE = {"hold": "hold-beams", "lower": "lower-deck-beams", "greatcabin": "cabin-beams", "poopcabin": "cabin-beams",
                 "forecastle": "forecastle-beams"}
if "cabin-frames" in ZONE_CODES:
    # (0.3.13, note 134) the cabins' frames, and the castle decks' bulwarks' frames, each their own
    FRAME_ZONE.update({"greatcabin": "cabin-frames", "poopcabin": "cabin-frames", "castledeck": "castle-frames"})


def wale_top(x):
    C = _C()
    if hasattr(C, "wale_top"):
        return C.wale_top(x)
    return C.WALES[-1] + 0.25 + C.sheer(x)


def outside_zone(c, frieze=True, p=None):
    """The outside planking at c: the wales' zone below the upper wale's top; the frieze (the strake under a level rail
    cap; (0.3.9, the user's note 69) and its band on up each castle break's sweep, frieze="sweep": the break's
    planking is cut along it); above the waist's rail the castles' sides; else the topsides. (A strake running up
    behind the upper wale is sorted by the part of it that shows, above or below the wale.)"""
    C = _C()
    x, y = float(c[0]), float(c[1])
    wt = wale_top(x)
    if p is not None:
        lo, hi = float(p[:, 1].min()), float(p[:, 1].max())
        if lo < wt - 0.01 < hi:
            y = (wt + hi) / 2 if hi - wt > max(0.0, (wt - 0.25) - lo) else (lo + wt) / 2 - 0.125
    if y < wt + 1e-3:
        return "wales"
    r = float(C.rail(x))
    if frieze and r - 0.5 < y < r - 0.25:
        return "frieze"
    if y > C.MAIN + getattr(C, "WAIST_RAIL", 1.0) - 0.01:
        return "sterncastle-sides" if x < 0 else "forecastle-sides"
    return "topsides"


def _walls(d):
    """The castle walls (bulkheads) with the zones of their two faces: (x0, x1, y0, y1, the front's facing (+1 forward,
    -1 aft), front zone, back zone)."""
    C = _C()
    out = []
    for x0, x1, y0, y1 in getattr(d, "bulkheads", []):
        if abs(x1 - C.WAIST_AFT) < 1e-6:
            out.append((x0, x1, y0, y1, 1, "great-cabin-front", "great-cabin-walls"))
        elif abs(x1 - C.POOP_FRONT) < 1e-6:
            out.append((x0, x1, y0, y1, 1, "poop-cabin-front", "poop-cabin-walls"))
        elif abs(x0 - C.WAIST_FWD) < 1e-6:
            out.append((x0, x1, y0, y1, -1, "forecastle-front", "forecastle-room"))
    return out


def wall_zone(d, c, n):
    """A face of a castle wall (or its end strip): the front's zone facing out of the room, else the room's. (0.3.12) The
    end strip of the castle deck over a front, toward the waist, the front's too (the user's notes 115 and 116: "different
    surface or texture that does not paint together and dark horizontal line": it was in no zone, beside the deck's edge
    cells in the front's); the stairwell's walls in the great cabin, the cabin's walls (both faces)."""
    C = _C()
    for x0, x1, z0, z1 in getattr(d, "partitions", []):
        if x0 - 0.06 <= c[0] <= x1 + 0.06 and z0 - 0.06 <= c[2] <= (z1 if z1 < 6 else 99) + 0.06 and                 C.MAIN - 0.06 <= c[1] <= C.QUARTER - C.DECK_T + 0.06:
            return None if abs(n[1]) > 0.5 else "great-cabin-walls"
    for x0, x1, y0, y1, sign, front, back in _walls(d):
        top = y1 + (C.DECK_T if n[0] * sign > 0.5 else 0.0)
        if x0 - 0.06 <= c[0] <= x1 + 0.06 and y0 - 0.06 <= c[1] <= top + 0.06:
            if abs(n[0]) < 0.5:
                return None
            return front if n[0] * sign > 0 else back
    return None


def _companion_box():
    C = _C()
    if not getattr(C, "COMPANION_HOUSE", False):
        return None
    cx0, cx1, cz0, cz1 = C.COMPANION
    return (cx0 - 0.25, cx1 + 0.25, C.MAIN + 0.25, C.HOUSE_TOP, cz0 - 0.25, cz1 + 0.25)


def _doors():
    """The doorways' walls: (x0, x1, floor). The same as build()'s DOORS."""
    C = _C()
    out = [(C.WAIST_AFT - 0.25, C.WAIST_AFT, C.MAIN), (C.WAIST_FWD, C.WAIST_FWD + 0.25, C.MAIN),
           (C.POOP_FRONT - 0.25, C.POOP_FRONT, C.QUARTER)]
    if getattr(C, "COMPANION_HOUSE", False):
        out.append((C.COMPANION[0] - 0.25, C.COMPANION[0], C.MAIN + 0.25))
    return out


def quad_geometry(q):
    p = np.asarray(q["pts"], float)
    n = np.cross(p[1] - p[0], p[3] - p[0]) + np.cross(p[3] - p[2], p[1] - p[2])
    ln = np.linalg.norm(n)
    return p, p.mean(axis=0), (n / ln if ln > 1e-12 else n)


def quad_area(p):
    return 0.5 * float(np.linalg.norm(np.cross(p[2] - p[0], p[3] - p[1])))


def _near(v, target, eps=2e-3):
    return abs(float(v) - target) < eps


# ==================================================================== which zone
def quad_zone(d, q):
    """
    The zone a mesh quad belongs to, or None (not paintable), from what the generator tagged it as where it made it
    (Design.paint: q["paint"], a zone's code, or a kind of thing whose zone is where it is, or a dict of either by
    material). (0.3.8: the tags at the source; before, this told them apart by the helpers that made them and their boxes'
    sizes.)
    """
    tag = q.get("paint")
    mat, grp = q["mat"], q["group"]
    if isinstance(tag, dict):
        tag = tag.get(mat)
    if tag is None or mat not in PAINTABLE_MATS or grp.startswith(("bottom", "sail:")):
        return None
    if tag in ZONE_CODES:
        return tag
    p, c, n = quad_geometry(q)
    if tag in ("outside", "outside-sweep"):
        # (0.3.9: the sweeps' planking is cut along the frieze's band in the carrack on the grid, so it takes it on)
        return outside_zone(c, frieze=tag == "outside" or getattr(_C(), "GRID", False), p=p)
    if tag == "transom":
        if getattr(_C(), "GRID", False):
            # (0.3.10, the user's note 103: "if going with stripes, in the back those lines should continue") the
            # transom's planking in the sides' bands, by the same heights (its strakes are at the sides' heights): the
            # wales, the topsides, the sterncastle's sides, the frieze under the taffrail
            return outside_zone(c, frieze=True, p=p)
        z = outside_zone(c, frieze=False, p=p)
        return "wales" if z == "wales" else "transom"
    if tag == "inside":
        return INSIDE_ZONE.get(room_of(d, p, c))
    if tag == "frame":
        return FRAME_ZONE.get(room_of(d, p, c))
    if tag == "ceiling":
        return CEILING_ZONE.get(room_of(d, p, c))
    if tag == "beam":
        return BEAM_ZONE.get(room_of(d, p, c))
    if tag == "wall":
        return wall_zone(d, c, n)
    return None


def voxel_face_zone(d, box, face):
    """The zone of one face of a voxel box (mesh_voxels), or None."""
    mat = d.materials[box["mat"]].code
    lo, hi = np.asarray(box["lo"], float), np.asarray(box["hi"], float)
    n = np.asarray(VOXEL_DIRS[face], float)
    c = (lo + hi) / 2
    ax = int(np.argmax(np.abs(n)))
    c[ax] = hi[ax] if n[ax] > 0 else lo[ax]
    if mat == "oak":
        z = wall_zone(d, c, n)
        if z:
            return z
        return None
    if mat == "deck":
        C = _C()
        if face == "down":
            # (not the cargo deck's underside, over the ballast: nobody sees it)
            return CEILING_ZONE.get(room_at(c[0], c[1] - 0.01)) if c[1] > C.HOLD_FLOOR - 0.1 else None
        if face in ("east", "west"):
            # a castle deck's edge over its front (between the front's boards and the railing's plinth): the front's
            for x_edge, sign, y_top, front in ((C.WAIST_AFT, 1, C.QUARTER, "great-cabin-front"),
                                               (C.POOP_FRONT, 1, C.POOP, "poop-cabin-front"),
                                               (C.WAIST_FWD, -1, C.QUARTER, "forecastle-front")):
                if abs(c[0] - x_edge) < 0.01 and n[0] * sign > 0 and y_top - C.DECK_T - 0.01 <= c[1] <= y_top + 0.01:
                    return front
    return None


def zones(d, boxes=None):
    """
    {zone code: {"name", "kind", "default" (the material it has now), "quads": [indices into d.quads],
    "voxel_faces": [(index into boxes, face)], "area" (m²)}} for a design built with build() (or after install()).
    boxes: what d.mesh_voxels() returned (made here if not given).
    """
    if boxes is None:
        boxes, _ = d.mesh_voxels()
    out = {code: {"name": name, "kind": kind, "default": None, "quads": [], "voxel_faces": [], "area": 0.0}
           for code, name, kind in ZONES}
    mats = {code: collections.Counter() for code in out}
    for i, q in enumerate(d.quads):
        z = quad_zone(d, q)
        if z is None:
            continue
        p = np.asarray(q["pts"], float)
        a = quad_area(p)
        out[z]["quads"].append(i)
        out[z]["area"] += a
        mats[z][q["mat"]] += a
    for bi, b in enumerate(boxes):
        for f in VOXEL_FACES:
            if not b["faces"].get(f):
                continue
            z = voxel_face_zone(d, b, f)
            if z is None:
                continue
            lo, hi = np.asarray(b["lo"], float), np.asarray(b["hi"], float)
            ext = hi - lo
            ax = int(np.argmax(np.abs(VOXEL_DIRS[f])))
            a = float(np.prod([ext[k] for k in range(3) if k != ax]))
            out[z]["voxel_faces"].append((bi, f))
            out[z]["area"] += a
            mats[z][d.materials[b["mat"]].code] += a
    for code, zd in out.items():
        zd["default"] = mats[code].most_common(1)[0][0] if mats[code] else None
        zd["area"] = round(zd["area"], 2)
    return out


def straddlers(d, zmap, tol=0.03):
    """Quads of the room-sorted zones (inside planking, frames, ceilings) that run on through a deck or a castle wall
    and show more than tol on its other side, where they take the colour of the space they're sorted to: they'd have
    to be cut there in the generator. [(quad index, zone, how much shows on the other side)]."""
    room_kinds = set(INSIDE_ZONE.values()) | set(FRAME_ZONE.values()) | set(CEILING_ZONE.values()) | set(BEAM_ZONE.values())
    out = []
    for code, zd in zmap.items():
        if code not in room_kinds:
            continue
        for i in zd["quads"]:
            p = np.asarray(d.quads[i]["pts"], float)
            s = straddle(d, p)
            if s > tol:
                out.append((i, code, s))
    return out


def summary(d, zmap):
    return [{"code": code, "name": zd["name"], "kind": zd["kind"], "default": zd["default"], "quads": len(zd["quads"]),
             "voxelFaces": len(zd["voxel_faces"]), "area": zd["area"]} for code, zd in zmap.items()]


def main():
    d = build()
    boxes, _ = d.mesh_voxels()
    zmap = assign(d, boxes)
    path, rows = write_summary(d, zmap)
    for r in rows:
        print(f"{r['code']:22s} {r['area']:8.1f} m²  {r['quads']:6d} quads {r['voxelFaces']:4d} voxel faces  "
              f"(now {r['default']})")
    st = straddlers(d, zmap)
    print(f"{len(st)} quads run across two spaces")
    print(path)


if __name__ == "__main__":
    main()
