"""Build the caravel master model in AstoCAD (FreeCAD 1.1 fork) from models/caravel/caravel-lines.json.

    python tools/caravel/caravel_lines.py
    C:/AstoCAD/bin/AstoCADCmd.exe -c "exec(open(r'C:/VS mods/Hansa/tools/caravel/build_caravel.py').read())"

Writes models/caravel/caravel.FCStd, models/caravel/caravel-report.json and models/caravel/views/caravel-mesh.js
(the triangles the shared ship viewer, tools/shipview/viewer.html, draws; the build bundles them into caravel.html). Proposal: see models/caravel/caravel-design.md.

Frame and units as docs/master-model.md: mm, X toward the bow, Y to port, Z up, Z 0 = the hull's underside.
Bodies: Envelope, Shell, Decks and Reference follow the carrack's master-model plan; Rig_Latina, Rig_Redonda,
Fittings and Guns are placeholders to judge proportions and gun room, not slicer input.
"""
import hashlib
import json
import math
import os
import time

import FreeCAD as App
import Part
import MeshPart

V = App.Vector
ROOT = os.environ.get("CARAVEL_ROOT", r"C:/VS mods/Hansa")     # (a worktree's own checkout: set CARAVEL_ROOT)
OUT = os.environ.get("CARAVEL_OUT") or os.path.join(ROOT, "models", "caravel")
T0 = time.time()


def log(*a):
    print(f"[{time.time() - T0:6.1f}s]", *a, flush=True)


with open(os.path.join(OUT, "caravel-lines.json")) as f:
    L = json.load(f)
LV = L["levels"]
SKIN = L["skin"]
DECK_T = L["deckThickness"]
LAY = L["layout"]
BIG = 40000.0


# ------------------------------------------------------------------------------------------------ hull envelope
def waterline_wire(line, spline=False):
    """A closed waterline, port half mirrored. Solids use the polygon through the lines' points: AstoCAD 1.1's
    booleans failed silently on lofts through periodic splines (ruled and smooth alike, 17 October 2026), and
    points 125-250 mm apart in height and ~100-300 mm along make facets far below a block. The spline form is
    kept in the document as an editable reference."""
    z = line["z"]
    port = [V(x, y, z) for x, y in line["pts"]]
    stbd = [V(p.x, -p.y, z) for p in reversed(port)]
    pts = port + stbd
    if not spline:
        return Part.makePolygon(pts + [pts[0]])
    c = Part.BSplineCurve()
    c.interpolate(pts, PeriodicFlag=True)
    return Part.Wire([c.toShape()])


def prism_xz(pts_xz, y0=-BIG / 2, y1=BIG / 2):
    """A solid from a closed XZ polygon, spanning Y."""
    poly = Part.makePolygon([V(x, y0, z) for x, z in pts_xz] + [V(pts_xz[0][0], y0, pts_xz[0][1])])
    return Part.Face(poly).extrude(V(0, y1 - y0, 0))


sheer = L["sheer"]
def sheer_prism_solid():
    """The volume under the sheer, spanning Y: every sheer point in the quarterdeck break, every fifth elsewhere."""
    qf = L["quarterdeckFront"]
    pts = [(x, z) for k, (x, z) in enumerate(sheer) if k % 5 == 0 or qf - 200 <= x <= qf + 2700 or k == len(sheer) - 1]
    return prism_xz(pts + [(pts[-1][0], -2000.0), (pts[0][0], -2000.0)])


sheer_prism = sheer_prism_solid()
(tx0, tz0), (tx1, tz1) = L["transomPlane"]["low"], L["transomPlane"]["high"]
# (25 October 2026, the user's sketch) the stern's after outline [x, z] from the bottom: the post, a counter over it, the
# transom; and its inner face SKIN in, square to it (a plane before: the post and the transom in one)
STERN = [tuple(q) for q in L["sternProfile"]]
STERN_IN = [tuple(q) for q in L["sternProfileInner"]]
COUNTER = L.get("counter")


def transom_x(z, shift=0.0):
    """The stern's after outline at height z (the transom over a counter, the post under it)."""
    if z <= STERN[0][1]:
        return STERN[0][0] + shift
    for (xa_, za_), (xb_, zb_) in zip(STERN, STERN[1:]):
        if z <= zb_:
            return xa_ + (xb_ - xa_) * (z - za_) / ((zb_ - za_) or 1.0) + shift
    return STERN[-1][0] + shift


def _forward_of(outline, shift=0.0):
    pts = [(x + shift, z) for x, z in outline]
    return prism_xz([(pts[0][0], -2000.0)] + pts + [(pts[-1][0], 14000.0), (20000.0, 14000.0), (20000.0, -2000.0)])


def forward_of_transom(shift=0.0):
    """Everything forward of the stern's after outline (moved `shift` along X)."""
    return _forward_of(STERN, shift)


def inside_stern():
    """Everything forward of the stern's inner face: the shell SKIN thick at the post, the counter and the transom."""
    return _forward_of(STERN_IN)


rake = math.atan2(tx0 - tx1, tz1 - tz0)
# the rudder's line: the sternpost's after face; over a counter its stock goes up through the counter to the tiller
RUD_GAP, RUD_STOCK_R = 30.0, 100.0
(rlx0, rlz0), (rlx1, rlz1) = L["rudderLine"]["low"], L["rudderLine"]["high"]
STOCK_X = rlx0 - RUD_GAP - RUD_STOCK_R
stock_hole = (Part.makeCylinder(RUD_STOCK_R + 10.0, LV["mainDeck"] + 200.0 - COUNTER["post_top"] + 300.0,
                                V(STOCK_X, 0, COUNTER["post_top"] - 300.0)) if COUNTER else None)
# backbone: keel, stem and sternpost in one piece, its side outline from caravel_lines, BB half width each side
BB = L["backbone"]
_bb_pts = [V(x, -BB["halfWidth"], z) for x, z in BB["outline"]]
backbone = Part.Face(Part.makePolygon(_bb_pts + [_bb_pts[0]])).extrude(V(0, 2 * BB["halfWidth"], 0))
backbone = backbone.removeSplitter()

# the hull solids depend only on the lines; a rebuild with unchanged lines reuses them (the shell boolean is slow)
LINES_HASH = hashlib.md5(json.dumps({k: L[k] for k in ("hull", "inner", "sheer", "transomPlane", "skin", "sternProfile")},
                                    sort_keys=True).encode()).hexdigest()[:12]
CACHE = os.path.join(os.environ.get("TEMP", OUT), "caravel-cache", LINES_HASH)
if os.environ.get("CARAVEL_NOCACHE") != "1" and all(
        os.path.exists(os.path.join(CACHE, n + ".brep")) for n in ("envelope", "inner", "shell")):
    envelope, inner, shell = (Part.read(os.path.join(CACHE, n + ".brep")) for n in ("envelope", "inner", "shell"))
    envelope, inner, shell = envelope.Solids[0], inner.Solids[0], shell.Solids[0]
    inner_ok = True
    wires = [waterline_wire(l) for l in L["hull"]]
    log("hull solids from cache", CACHE)
else:
    log("lofting", len(L["hull"]), "waterlines")
    wires = [waterline_wire(l) for l in L["hull"]]
    RULED = True
    loft = Part.makeLoft(wires, True, RULED, False, 3)
    log("loft valid", loft.isValid(), "volume m3", round(loft.Volume / 1e9, 2))

    fwd = forward_of_transom()
    envelope = loft.common(sheer_prism.common(fwd))
    envelope = envelope.removeSplitter()
    log("envelope valid", envelope.isValid(), "solids", len(envelope.Solids))

    log("lofting the inner skin lines")
    inner = Part.makeLoft([waterline_wire(l) for l in L["inner"]], True, RULED, False, 3)
    inner_ok = inner.isValid() and inner.Volume > 0
    log("inner valid", inner.isValid(), "volume m3", round(inner.Volume / 1e9, 2))
    inner = inner.common(inside_stern())
    inner = inner.removeSplitter()
    shell = envelope.cut(inner).removeSplitter()
    os.makedirs(CACHE, exist_ok=True)
    for n, shp in (("envelope", envelope), ("inner", inner), ("shell", shell)):
        shp.exportBrep(os.path.join(CACHE, n + ".brep"))
log("shell", round(shell.Volume / 1e9, 3), "m3; cutting the backbone out of it; backbone valid", backbone.isValid())
shell = shell.cut(backbone).removeSplitter()
GATE = LAY["gate"]
GATE_POST = 150.0
gate_cuts = [Part.makeBox(GATE["x"][1] - GATE["x"][0] - 2 * GATE_POST / 2, 2500, GATE["head"] + 300 - GATE["sill"],
                          V(GATE["x"][0] + GATE_POST / 2, (2000 if sgn > 0 else -4500), GATE["sill"]))
             for sgn in (1, -1)]
for c in gate_cuts:
    shell = shell.cut(c)
# the waist's gun ports (20 October 2026, the guns in the game): 750 wide in the cell's middle, from the sill up through
# the bulwark's top (the port's head is the rail, 1 block over the deck), both sides; no lids yet. The quarterdeck's
# guns fire over its side, through the railing's open cells.
PORT_CUT = LAY["port"]
for c in LAY["guns"]:
    for sgn in (1, -1):
        shell = shell.cut(Part.makeBox(1000 - 2 * PORT_CUT["inset"], 2500, 1500,
                                       V(c * 1000 + PORT_CUT["inset"], 2000 if sgn > 0 else -4500, LV["mainDeck"] + PORT_CUT["sill"])))
shell = shell.removeSplitter()
log("shell valid", shell.isValid(), "volume m3", round(shell.Volume / 1e9, 3))


# ------------------------------------------------------------------------------------------------ decks
def slab(z_top, thick, x0=-BIG, x1=BIG):
    return Part.makeBox(x1 - x0, BIG, thick, V(x0, -BIG / 2, z_top - thick))


# ribs (proposal cells): each the section from caravel_lines.rib_profiles, RIB_SIDED along X at its cell's middle
RIB_SIDED = 200.0
rib_solids = []
inside_transom = inside_stern()
for r, prof in L["ribProfiles"].items():
    x0 = int(r) * 1000 + 500 - RIB_SIDED / 2
    pts = [V(x0, y, z) for y, z in prof]
    rib = Part.Face(Part.makePolygon(pts + [pts[0]])).extrude(V(RIB_SIDED, 0, 0))
    rib = rib.common(inside_transom).common(sheer_prism).cut(backbone)
    # (25 October 2026, the slot check: draft D's ribs under the quarterdeck rise through the main deck) in the main deck's
    # planking a rib whose inner edge reaches under 100 into the next cell inboard stops at that cell's border: the cell's
    # planking whole (its faces squares), not notched round a sliver of rib (rib -14 by the upright transom: 40)
    _lay = Part.makeBox(RIB_SIDED + 2, 2 * BIG, DECK_T, V(x0 - 1, -BIG, LV["mainDeck"] - DECK_T))
    _in_deck = rib.common(_lay)
    for sgn in (1, -1):
        ys_ = [sgn * v.Point.y for v in _in_deck.Vertexes if sgn * v.Point.y > 1.0]
        if not ys_:
            continue
        b_ = math.ceil((min(ys_) - 500.0) / 1000.0) * 1000.0 + 500.0      # (the cell border at or outboard of its edge)
        if 1.0 < b_ - min(ys_) < 100.0:
            rib = rib.cut(Part.makeBox(RIB_SIDED + 2, b_, DECK_T, V(x0 - 1, 0.0 if sgn > 0 else -b_, LV["mainDeck"] - DECK_T)))
            log("rib", r, "kept out of the main deck's cell inboard of", sgn * b_, "by", round(b_ - min(ys_)))
    if rib.Volume > 1e6:
        rib_solids.append(rib)
ribs = Part.makeCompound(rib_solids)
log("ribs", len(rib_solids), "valid", all(r.isValid() for r in rib_solids))

hold = slab(LV["hold"], DECK_T).common(inner).cut(ribs).cut(backbone)
# the deck planking stops at the ribs (they run through); the hatch is a replacement block (Reference shows it)
hx, hy = LAY["hatch"]["x"], LAY["hatch"]["y"]
hatch_cut = Part.makeBox(hx[1] - hx[0], hy[1] - hy[0], 1200, V(hx[0], hy[0], LV["mainDeck"] - 1000))
main = slab(LV["mainDeck"], DECK_T).common(inner).cut(ribs).cut(backbone).cut(hatch_cut)
if stock_hole is not None:                          # (the rudder's stock up through the main deck to the tiller)
    main = main.cut(stock_hole)
qd = slab(LV["quarterdeck"], DECK_T, x1=L["quarterdeckFront"]).common(inner).cut(ribs).cut(backbone)
decks = Part.makeCompound([hold, main, qd])
beam_solids = []
for x0, x1, z0, z1, half in L["beams"]:
    # (21 October 2026, chalk note 23) each beam runs into its rib and is cut by it: it ends on the rib's curved face
    # (22 October 2026, chalk note 35) square-ended, seated in a notch cut in its rib (below, once all are made)
    b_ = Part.makeBox(x1 - x0, 2 * half, z1 - z0, V(x0, -half, z0)).cut(backbone).common(inner)
    if b_.BoundBox.intersect(hatch_cut.BoundBox):
        b_ = b_.cut(hatch_cut)                       # a beam across the hatch stops at its sides
    beam_solids += [so for so in b_.Solids if so.Volume > 5e6]
# (21 October 2026, chalk note 22: "no side beams to hatch") the hatchway framed under the deck: a carling along each
# side from beam to beam past its ends, and a headledge across each end, the beams' size, flush with the opening
BEAM_W, BEAM_H = L["beams"][0][1] - L["beams"][0][0], L["beams"][0][3] - L["beams"][0][2]
bz0 = LV["mainDeck"] - DECK_T - BEAM_H
hatch_frame = []
# (25 October 2026, chalk notes 93, 94: "if we are cutting floor beams and placing side beams then side beams should go
# till the next floor beams") each carling from the nearest whole beam aft of the hatch to the nearest forward of it
_main_beams = [(b_[0], b_[1]) for b_ in L["beams"] if abs(b_[3] - (LV["mainDeck"] - DECK_T)) < 1]
_ca0 = max((b1 for b0, b1 in _main_beams if b1 <= hx[0] - BEAM_W + 1), default=hx[0] - BEAM_W)
_ca1 = min((b0 for b0, b1 in _main_beams if b0 >= hx[1] + BEAM_W - 1), default=hx[1] + BEAM_W)
for sgn in (1, -1):
    hatch_frame.append(Part.makeBox(_ca1 - _ca0, BEAM_W, BEAM_H, V(_ca0, hy[1] if sgn > 0 else hy[0] - BEAM_W, bz0)))
for xe in (hx[0] - BEAM_W, hx[1]):
    hatch_frame.append(Part.makeBox(BEAM_W, hy[1] - hy[0], BEAM_H, V(xe, hy[0], bz0)))
_frame = Part.makeCompound(hatch_frame)
_cut = []
for b_ in beam_solids:
    if b_.BoundBox.intersect(_frame.BoundBox):
        _cut += [so for so in b_.cut(_frame).Solids if so.Volume > 5e6]
    else:
        _cut.append(b_)
beam_solids = _cut + hatch_frame
beams = Part.makeCompound(beam_solids)
# the ribs' notches the beams' ends sit in (an L seat each: the beam's underside and its end on the rib)
rib_solids = [r_.cut(beams) if r_.BoundBox.intersect(beams.BoundBox) else r_ for r_ in rib_solids]
rib_solids = [so for r_ in rib_solids for so in r_.Solids if so.Volume > 1e6]
# (25 October 2026, the slot check: her collision refused the quarterdeck's stair; 26 October 2026, the user's choice B:
# keep it against her side) over the quarterdeck's level, from the stair's foot to the end of its landing a block aft of
# its head, where her tumblehome brings her side (and rib -8's head) into the stair's cell: her planking and the ribs stop
# 15 outboard of the cell; the cap rail, 5 outboard of it, overhangs the cut by 10
STAIR_CLEAR = []
for st_ in LAY["stairs"]:
    if st_["name"] == "quarterdeck" and abs(st_["y"]) > 1:
        xa_, xb_ = sorted((st_["foot"][0], st_["head"][0]))
        yb_ = abs(st_["y"]) + 515.0
        STAIR_CLEAR.append(Part.makeBox(xb_ - xa_ + 1000.0, yb_, 2000.0,
                                        V(xa_ - 1000.0, 0.0 if st_["y"] > 0 else -yb_, LV["quarterdeck"] + 5.0)))
# (26 October 2026, the game views: shaved to the cell, rib -8's head left a 50 sliver on her planking, as if broken off)
# the ribs' heads over the landing go whole from the quarterdeck up on that side: the planking and the cap close it
STAIR_CLEAR_RIBS = [Part.makeBox(c.BoundBox.XLength, BIG, c.BoundBox.ZLength + 5.0,
                                 V(c.BoundBox.XMin, 0.0 if c.BoundBox.YMax > 1.0 else -BIG, LV["quarterdeck"])) for c in STAIR_CLEAR]
for _clear in STAIR_CLEAR_RIBS:
    _cut = sum(r_.common(_clear).Volume for r_ in rib_solids if r_.BoundBox.intersect(_clear.BoundBox))
    rib_solids = [max(r_.cut(_clear).Solids, key=lambda so: so.Volume)
                  if r_.BoundBox.intersect(_clear.BoundBox) and r_.common(_clear).Volume > 1.0 else r_ for r_ in rib_solids]
    log("the quarterdeck stair's landing kept clear of the ribs:", round(_cut / 1e6, 4), "litres off")
ribs = Part.makeCompound(rib_solids)
log("beams", len(beam_solids))
log("decks", [round(s.Volume / 1e9, 2) for s in (hold, main, qd)])


# ------------------------------------------------------------------------------------------------ reference
def half_breadth(x, z):
    """Envelope half-breadth at (x, z), from the shape itself."""
    line = Part.LineSegment(V(x, 0, z), V(x, 9000, z)).toShape()
    sec = envelope.section(line)
    ys = [v.Point.y for v in sec.Vertexes]
    return max(ys) if ys else None


def inner_half_breadth(x, z):
    sec = inner.section(Part.LineSegment(V(x, 0, z), V(x, 9000, z)).toShape())
    ys = [v.Point.y for v in sec.Vertexes]
    return max(ys) if ys else None


ref_edges = []
xs_cells = range(-16, 13)
for z in (0.0, LV["mainDeck"]):
    for x in xs_cells:
        ref_edges.append(Part.makeLine(V(x * 1000, -3500, z), V(x * 1000, 3500, z)))
    for y in (-3500, -2500, -1500, -500, 500, 1500, 2500, 3500):
        ref_edges.append(Part.makeLine(V(-16000, y, z), V(12000, y, z)))
# waterline, deck levels on the centreline, quarterdeck front
wl = envelope.slice(V(0, 0, 1), LV["waterline"])
ref_edges += [e for w in wl for e in w.Edges]
for z in (LV["hold"], LV["mainDeck"], LV["quarterdeck"]):
    ref_edges.append(Part.makeLine(V(-16000, 0, z), V(12000, 0, z)))
ref_edges.append(Part.makeLine(V(L["quarterdeckFront"], -3500, LV["quarterdeck"]),
                               V(L["quarterdeckFront"], 3500, LV["quarterdeck"])))
# mast cells: wire boxes from heel to the highest deck they pass
for name, m in LAY["masts"].items():
    x0 = m["cell"] * 1000
    top = LV["quarterdeck"] if x0 < L["quarterdeckFront"] else LV["mainDeck"] + 1000
    top = max(top, m["heel"] + 1000)                      # a mast stepped on a deck: one cell over it
    ref_edges += Part.makeBox(1000, 1000, top - m["heel"], V(x0, -500, m["heel"])).Edges
# rib cells on the main deck plane
for r in LAY["ribs"]:
    ref_edges += Part.makePlane(1000, 7000, V(r * 1000, -3500, LV["mainDeck"] + 5)).Edges
# gun ports on the hull side
port = LAY["port"]
GUN_DECKS = [(c, LV["mainDeck"]) for c in LAY["guns"]] + [(c, LV["quarterdeck"]) for c in LAY["quarterdeckGuns"]]
for c, deck in GUN_DECKS:
    x0, x1 = c * 1000 + port["inset"], c * 1000 + 1000 - port["inset"]
    z0, z1 = deck + port["sill"], deck + port["head"]
    for sgn in (1, -1):
        y = half_breadth((x0 + x1) / 2, (z0 + z1) / 2) or 3300
        ref_edges.append(Part.makePolygon([V(x0, sgn * (y + 5), z0), V(x1, sgn * (y + 5), z0),
                                           V(x1, sgn * (y + 5), z1), V(x0, sgn * (y + 5), z1),
                                           V(x0, sgn * (y + 5), z0)]))
hx, hy = LAY["hatch"]["x"], LAY["hatch"]["y"]
ref_edges += Part.makePlane(hx[1] - hx[0], hy[1] - hy[0], V(hx[0], hy[0], LV["mainDeck"] + 10)).Edges
reference = Part.makeCompound(ref_edges)


# ------------------------------------------------------------------------------------------------ rigs
def spar(p0, p1, r0, r1):
    d = p1 - p0
    if abs(r0 - r1) < 1e-6:
        return Part.makeCylinder(r0, d.Length, p0, d.normalize())
    return Part.makeCone(r0, r1, d.Length, p0, d.normalize())


def sail(pts, y=None):
    if y is not None:
        pts = [V(p.x, y, p.z) for p in pts]
    return Part.Face(Part.makePolygon(pts + [pts[0]]))


def cross_for_tack(tack_z, fore_len, angle_deg=56):
    """The height where a yard must cross its mast to put its tack at tack_z."""
    return tack_z + fore_len * math.sin(math.radians(angle_deg))


def mast(cell, heel, top, r0, r1=None, name=""):
    x = cell * 1000 + 500
    RIG_RECORD[CUR_RIG]["masts"].append({"name": name or f"cell {cell}", "x": x, "heel": heel, "top": round(top),
                                          "r0": r0, "r1": r1 or r0 * 0.6})
    return spar(V(x, 0, heel), V(x, 0, top), r0, r1 or r0 * 0.6)


SAIL_FEET = []      # (name, tack, clew): the foot lines checked for 2 blocks of head room over the decks
RIG_RECORD = {"latina": {"masts": [], "sails": [], "yards": []}, "redonda": {"masts": [], "sails": [], "yards": []}}
CUR_RIG = "latina"


def record_sail(name, pts):
    RIG_RECORD[CUR_RIG]["sails"].append({"name": name, "pts": [[round(c) for c in (q.x, q.y, q.z)] for q in pts]})


def lateen(cell, cross_z, fore_len, aft_len, angle_deg, clew, r, y_yard=350.0, y_sail=520.0, name="", sail_to=None):
    """A lateen yard crossing the mast at cross_z, its fore end low; the sail from tack to peak to clew."""
    x = cell * 1000 + 500
    a = math.radians(angle_deg)
    cross = V(x, y_yard, cross_z)
    tack = cross + V(fore_len * math.cos(a), 0, -fore_len * math.sin(a))
    SAIL_FEET.append((name, (tack.x, tack.z - 100), clew))
    peak = cross + V(-aft_len * math.cos(a), 0, aft_len * math.sin(a))
    # (21 October 2026, the user: the main yard "from outside looks maybe too long or thin") thicker toward its ends
    # (22 October 2026, the user: "Mast yards now are too thick") between the two: the sling 240 / 170, tapering
    # (22 October 2026, the user: "yard looks too thick in middle compared to masts") under the mast's own thickness at
    # its sling, tapering to its ends
    yard = Part.makeCompound([spar(cross, tack, r, r * 0.6), spar(cross, peak, r, r * 0.5)])
    head = peak + V(300 * math.cos(a), 0, -300 * math.sin(a))
    if sail_to is not None:                          # the sail stops short of the peak: its after edge at X sail_to
        f = (sail_to - tack.x) / (peak.x - tack.x)
        head = tack + (peak - tack) * f
    pts = [tack + V(0, 0, -100), head, V(clew[0], 0, clew[1])]
    s = sail(pts, y=y_sail)
    record_sail(name, [V(q.x, y_sail, q.z) for q in pts])
    RIG_RECORD[CUR_RIG]["yards"].append({"name": name, "mastX": x, "cross": [round(cross.x), round(cross.z)],
                                         "tack": [round(tack.x), round(tack.z)], "peak": [round(peak.x), round(peak.z)],
                                         "length": round(fore_len + aft_len)})
    return yard, s, tack, peak


def square(cell, yard_z, yard_len, foot_z, foot_len, r, x_off=450.0, name=""):
    x = cell * 1000 + 500 + x_off
    yard = spar(V(x, -yard_len / 2, yard_z), V(x, yard_len / 2, yard_z), r, r)
    pts = [V(x + 60, -yard_len / 2 + 250, yard_z - 150), V(x + 60, yard_len / 2 - 250, yard_z - 150),
           V(x + 350, foot_len / 2, foot_z), V(x + 350, -foot_len / 2, foot_z)]
    s = sail(pts)
    record_sail(name or f"square {cell}", pts)
    return yard, s


M = LAY["masts"]
D, Q = LV["mainDeck"], LV["quarterdeck"]
HEAD = 2000.0 + 200.0           # every sail's foot at least this far over the deck below it (player passage)
HULL_L = envelope.BoundBox.XLength
# The redonda's main mast as the reference replica's proportion (the user, 18 October, photo DSC06858): 0.80 of the
# hull's length. The latina's masts, yards and sails come from the user's sketch (SKETCH, below).
RIG_P = {"main": {"mast": 0.80, "yard": 1.05, "sling": 1 / 8}, "mizzen": {"mast": 0.50, "yard": 0.60, "sling": 0.20}}


def mast_dims(key):
    m, rp = M[key], RIG_P[key]
    length = rp["mast"] * HULL_L
    top = m["heel"] + length
    return top, top - rp["sling"] * length, rp["yard"] * HULL_L


# the mizzen's clew goes to a bumpkin, a spar out through the transom at the quarterdeck's floor (item 1)
# (24 October 2026, chalk note 59: "would make sense that this part goes till the deck beam") its heel in to the after face
# of the first quarterdeck beam forward of the transom, on the same line (it stopped 0.6 inside the transom, in the air)
BK_OUT = V(transom_x(Q) - (1600 if COUNTER else 2400), 0, Q + 150)   # (over a counter: the sketch's clew, 1.6 out)
_bk_in = V(transom_x(Q) + 600, 0, Q - 330)
_bk_beam = min((b_[0] for b_ in L["beams"] if abs(b_[3] - (Q - DECK_T)) < 1 and b_[0] > _bk_in.x), default=_bk_in.x)
BK_IN = _bk_in + (_bk_in - BK_OUT) * ((_bk_beam - _bk_in.x) / (_bk_in.x - BK_OUT.x))
bumpkin = spar(BK_IN, BK_OUT, 110, 80)   # under the deck's planking
# The latina's sails and yards from the user's sketch (18 October): each yard balanced on its mast, crossing it
# about halfway along (a yard slung near one end does not work); the mizzen's sail with a level foot 2.2 over the
# quarterdeck and its after edge over the stern; the main's foot sloping up aft. Points (x, z) mm.
SKETCH = {
    "main": {"tack": (8100.0, D + HEAD + 100), "peak": (-8700.0, 29200.0), "clew": (-3800.0, 9000.0), "top": 24800.0},
    "mizzen": {"tack": (-4600.0, Q + HEAD + 100), "peak": (-14000.0, 25700.0), "clew": (-12000.0, Q + HEAD),
               "top": 21800.0, "sail_to": -12000.0},   # the sail cut from aft at X -12.0 (the user, 18 October:
                                                       # balance): 48 m2, the sail centre -11.6 % of LWL
}


# (note 73) the mizzen's sketch drawn about its mast where it stood (cell -9): it moves with it
# (draft D) the main's with the main mast (sketched at cell -1)
_dmm = (M["main"]["cell"] + 1) * 1000.0
SKETCH["main"] = {k: ((v[0] + _dmm, v[1]) if isinstance(v, tuple) else v) for k, v in SKETCH["main"].items()}
_dm = (M["mizzen"]["cell"] + 9) * 1000.0
SKETCH["mizzen"] = {k: ((v[0] + _dm, v[1]) if isinstance(v, tuple) else (v + _dm if k == "sail_to" else v))
                    for k, v in SKETCH["mizzen"].items()}
YARD_SCALE = 0.85


def lateen_points(key, r, name):
    """A lateen yard straight from its tack to its peak, slung where it crosses its mast; the sail tack-peak-clew."""
    x = M[key]["cell"] * 1000 + 500
    if key in LAY.get("sails", {}):
        # (25 October 2026, the user's choice B: Boa Esperança's sail areas) the study's yard and clew as given, the sail
        # cut along its yard to its area (its head short of the peak)
        sd = LAY["sails"][key]
        (tx, tz), (px, pz) = sd["tack"], sd["peak"]
        cx_, cz_ = sd["clew"] if isinstance(sd["clew"], list) else (sd["clew"], Q + HEAD)
        cl = (BK_OUT.x if cx_ == "bumpkin" else cx_, tz - 100.0 if cz_ == "level" else cz_)     # (a level foot: as high as its tack's corner)
        cross = tz + (pz - tz) * (tx - x) / (tx - px)
        t1 = (tx, tz - 100.0)

        def area_to(f):
            hx, hz = tx + (px - tx) * f, tz + (pz - tz) * f
            return 0.5 * abs((hx - t1[0]) * (cl[1] - t1[1]) - (hz - t1[1]) * (cl[0] - t1[0])) / 1e6
        a0, a1 = area_to(0.0), area_to(1.0)
        f = min(1.0, (sd["area"] - a0) / (a1 - a0)) if sd.get("area") else 1.0   # (no area: the sail to the yard's end)
        angle = math.degrees(math.atan2(pz - tz, tx - px))
        fore, aft = math.hypot(tx - x, cross - tz), math.hypot(x - px, pz - cross)
        yard, sl, _, _ = lateen(M[key]["cell"], cross, fore, aft, angle, cl, r, name=name, sail_to=tx + (px - tx) * f)
        return yard, sl, SKETCH[key]["top"]
    sk = SKETCH[key]
    (tx, tz), (px, pz) = sk["tack"], sk["peak"]
    cross = tz + (pz - tz) * (tx - x) / (tx - px)
    # (22 October 2026, the user's choice: the main yard 1.2 times her length looked too long; both shortened by 15 %)
    # the yard, its sail's corners and where its sail is cut drawn in toward where the yard crosses its mast
    k = YARD_SCALE
    tx, tz = x + k * (tx - x), cross + k * (tz - cross)
    px, pz = x + k * (px - x), cross + k * (pz - cross)
    clew = (x + k * (sk["clew"][0] - x), cross + k * (sk["clew"][1] - cross))
    sk = dict(sk, tack=(tx, tz), peak=(px, pz), clew=clew,
              **({"sail_to": x + k * (sk["sail_to"] - x)} if sk.get("sail_to") is not None else {}))
    angle = math.degrees(math.atan2(pz - tz, tx - px))
    fore, aft = math.hypot(tx - x, cross - tz), math.hypot(x - px, pz - cross)
    yard, sl, _, _ = lateen(M[key]["cell"], cross, fore, aft, angle, sk["clew"], r, name=name, sail_to=sk.get("sail_to"))
    return yard, sl, sk["top"]


# caravela latina, two masts, as the reference replica (Vera Cruz). Masts upright (the replica's rake forward).
CUR_RIG = "latina"
lat = []
y, s_, top = lateen_points("main", 165, "latina main")
lat += [mast(M["main"]["cell"], M["main"]["heel"], top, 300, name="main"), y, s_]
y, s_, top = lateen_points("mizzen", 125, "latina mizzen")
lat += [mast(M["mizzen"]["cell"], M["mizzen"]["heel"], top, 220, name="mizzen"), y, s_, bumpkin]
rig_latina = Part.makeCompound(lat)

# caravela redonda: square fore and main, the same lateen mizzen, a bowsprit
CUR_RIG = "redonda"
red = []
main_top = mast_dims("main")[0]
top_main = Part.makeCylinder(850, 350, V(M["main"]["cell"] * 1000 + 500, 0, main_top - 1500))
red.append(mast(M["fore"]["cell"], M["fore"]["heel"], D + 11500, 210, name="fore"))
y, s_ = square(M["fore"]["cell"], D + 10200, 8600, D + HEAD + 400, 9600, 120, name="redonda fore course")
red += [y, s_]
red.append(mast(M["main"]["cell"], M["main"]["heel"], main_top - 1000, 280, 220, name="main"))
red.append(top_main)
red.append(spar(V(M["main"]["cell"] * 1000 + 500, 0, main_top - 2000), V(M["main"]["cell"] * 1000 + 500, 0, main_top + 5000),
                150, 90))
y, s_ = square(M["main"]["cell"], main_top - 3000, 12400, D + HEAD + 200, 13400, 170, name="redonda main course")
red += [y, s_]
y, s_ = square(M["main"]["cell"], main_top + 3800, 6200, main_top - 2600, 11000, 100, name="redonda main topsail")
red += [y, s_]
y, s_, top = lateen_points("mizzen", 160, "redonda mizzen")
red += [mast(M["mizzen"]["cell"], M["mizzen"]["heel"], top, 220, name="mizzen"), y, s_, bumpkin]
stem_head_x = max(v.Point.x for v in envelope.Vertexes)
red.append(spar(V(stem_head_x - 3500, 0, D + 1700), V(stem_head_x + 6000, 0, D + 4200), 190, 110))
rig_redonda = Part.makeCompound(red)


# ------------------------------------------------------------------------------------------------ fittings and guns
post = L["post"]
# (22 October 2026, chalk note 29: "lift rudders handle (and connected things) to ceiling and shorten the handle a bit")
# the rudder's head up to just under the quarterdeck's beams (planking 200, beams 400, 150 clear), the tiller there
RUD_FOOT, RUD_HEAD_W, RUD_TOP = 1500.0, 350.0, Q - DECK_T - 400.0 - 150.0
z_b = post[0][1] + 25.0


def rudder_lead(z):
    """The rudder's leading edge: RUD_GAP aft of the sternpost's after face."""
    return rlx0 + (rlx1 - rlx0) * (z - rlz0) / (rlz1 - rlz0) - RUD_GAP


# (25 October 2026, the user's sketch) under a counter the blade ends 100 under it and a round stock, its fore side on the
# blade's leading edge, goes on up through the counter and the main deck to the tiller under the quarterdeck's beams
RUD_BLADE_TOP = COUNTER["post_top"] - 100.0 if COUNTER else RUD_TOP
rudder_face_pts = [V(rudder_lead(z_b), 0, z_b), V(rudder_lead(RUD_BLADE_TOP), 0, RUD_BLADE_TOP),
                   V(rudder_lead(RUD_BLADE_TOP) - RUD_HEAD_W, 0, RUD_BLADE_TOP), V(rudder_lead(z_b) - RUD_FOOT, 0, z_b)]
rudder = Part.Face(Part.makePolygon(rudder_face_pts + [rudder_face_pts[0]])).extrude(V(0, 200, 0))
rudder.translate(V(0, -100, 0))
if COUNTER:
    _stock = Part.makeCylinder(RUD_STOCK_R, RUD_TOP + 60.0 - (RUD_BLADE_TOP - 400.0), V(STOCK_X, 0, RUD_BLADE_TOP - 400.0))
    rudder = rudder.fuse(_stock).removeSplitter()
    TILLER_HEAD = V(STOCK_X - 60.0, 0, RUD_TOP)
else:
    _t_rise = 0.0 if abs(tx1 - tx0) < 1 else 60.0
    TILLER_HEAD = V(rudder_lead(RUD_TOP - _t_rise) - 200, 0, RUD_TOP - _t_rise)
TILLER_END = TILLER_HEAD.x + 60.0 + 2360.0 if COUNTER else rudder_lead(RUD_TOP - 60) - 200 + 2360.0   # 2.4 long from the head
# (25 October 2026, note 102) square to the rudder's stock: level, the stock upright (the sternpost upright since)
tiller = spar(TILLER_HEAD, V(TILLER_END, 0, RUD_TOP), 90, 70)
# (24 October 2026, chalk note 58) no mizzen step: its heel is flush with the main deck's planking underneath
fittings = Part.makeCompound([rudder, tiller])
# openings through the transom for the tiller (with room to swing) and the bumpkin (a fitted socket)
# (25 October 2026, chalk note 86: "why there is hole around?") a slot only as wide as the tiller's swing where it passes
# the transom, close by the rudder's axis, and as high as the tiller (it was 900 x 400: the sky round it)
tiller_port = Part.makeBox(800, 420, 240, V(transom_x(RUD_TOP) - 500, -210, RUD_TOP - 180))
bk0, bk1 = BK_IN, BK_OUT                                 # the bumpkin's ends
# (26 October 2026: the sky showed round the spar) the socket the spar's own taper, 5 larger round it
bumpkin_socket = spar(bk0 - (bk1 - bk0).normalize() * 50, bk1 + (bk1 - bk0).normalize() * 50, 115, 85)
# (over a counter the tiller is inside her, under the quarterdeck: no port; the stock's hole through the counter instead)
shell = shell.cut(stock_hole if COUNTER else tiller_port).cut(bumpkin_socket).removeSplitter()
# (25 October 2026, the slot check: her collision refused the quarterdeck's stair) over the quarterdeck's level, where the
# castle break's tumblehome brings her side into the stair's cell beside it, her planking stops 5 outboard of the cell
# (at most 65 off its inside, at the quarterdeck's front corner); the cap rail covers what's left
for _clear in STAIR_CLEAR:          # (the stair's flight and its landing: above, by the ribs)
    _cut = shell.common(_clear).Volume
    if _cut > 1.0:
        shell = shell.cut(_clear).removeSplitter()
    log("the quarterdeck stair's cell and landing kept clear of her side:", round(_cut / 1e6, 4), "litres of planking off")
RAIL = L["railings"]


def bar(p0, p1, w, h):
    """A horizontal bar w wide and h high, centred on the segment p0-p1 (at p0's height)."""
    d = p1 - p0
    length = math.hypot(d.x, d.y)
    b_ = Part.makeBox(length, w, h, V(0, -w / 2, -h / 2))
    b_.rotate(V(0, 0, 0), V(0, 0, 1), math.degrees(math.atan2(d.y, d.x)))
    b_.translate(p0)
    return b_


POST_W, CAP_W = 150.0, 200.0
rail_parts = []
def post(b0, t0, w):
    """A square post from its bottom to its top point, leaning with the hull's tumblehome or the transom's rake."""
    def sq(p):
        x, y, z = p
        pts = [V(x - w / 2, y - w / 2, z), V(x + w / 2, y - w / 2, z), V(x + w / 2, y + w / 2, z), V(x - w / 2, y + w / 2, z)]
        return Part.makePolygon(pts + [pts[0]])
    return Part.makeLoft([sq(b0), sq(t0)], True)


# (24 October 2026, chalk note 84: "this protrusion my mind is not needed on rails") no caps on the posts: each ends
# inside the top rail
post_solids = [post(b0, t0, POST_W) for b0, t0 in RAIL["posts"]]
rail_parts += post_solids
top_pts = [V(q[0], q[1], q[2] - 50) for q in RAIL["top"]]
bot_pts = [V(*q) for q in RAIL["bottom"]]
# (22 October 2026, chalk note 32) the top rail 200 x 140; the bottom rail 120 x 80 lying on the deck; between the posts
# one member the posts' size (railings() gives them as "balusters")
# (24 October 2026, chalk note 56: "cant remove rails") each span along the quarterdeck's sides between two posts (its
# top rail, bottom rail and member) is a solid of its own, ending on the posts' faces: a hull slot in the game, taken out
# as her side's panels are. The spans over her guns (their lids), across the transom and the front, and short ones by the
# corners stay with the posts
_kinds = RAIL.get("kinds") or ["side"] * len(top_pts)
_members = [post(b_, (t_[0], t_[1], t_[2] - 70), POST_W) for b_, t_ in RAIL["balusters"]]
_mem_mid = [V(*b_) for b_, _ in RAIL["balusters"]]
rail_sections, RAIL_SECTIONS = [], []
for k in range(len(top_pts) - 1):
    if k in RAIL.get("breaks", []):                  # (the front's two parts with a bulwarked quarterdeck: no rail between)
        continue
    a0, a1 = bot_pts[k], bot_pts[k + 1]
    d_ = a1 - a0
    span = math.hypot(d_.x, d_.y)
    mid = (a0 + a1) * 0.5
    mem = [m_ for m_, c_ in zip(_members, _mem_mid) if (c_ - mid).Length < 50]
    removable = (_kinds[k] == _kinds[k + 1] == "side" and a0.y * a1.y > 0 and span > 0.6 * 1000
                 and not any(c * 1000 < mid.x < (c + 1) * 1000 for c in LAY["quarterdeckGuns"]))
    if not removable:
        rail_parts += [bar(top_pts[k], top_pts[k + 1], 200, 140), bar(a0 + V(0, 0, 40), a1 + V(0, 0, 40), 120, 80)] + mem
        continue
    sec = bar(top_pts[k], top_pts[k + 1], 200, 140).multiFuse([bar(a0 + V(0, 0, 40), a1 + V(0, 0, 40), 120, 80)] + mem)
    sec = sec.cut(post_solids[k]).cut(post_solids[k + 1]).removeSplitter()
    # (between the posts' faces along it: the rails are wider than a post, their ends would meet the next span's)
    u_ = V(d_.x, d_.y, 0) * (1.0 / span)
    keep = Part.makeBox(span - POST_W, 2000, 4000, V(0, -1000, -2000))
    keep.rotate(V(0, 0, 0), V(0, 0, 1), math.degrees(math.atan2(u_.y, u_.x)))
    keep.translate(a0 + u_ * (POST_W / 2))
    sec = sec.common(keep).removeSplitter()
    rail_sections += list(sec.Solids)
    RAIL_SECTIONS.append({"side": "port" if mid.y > 0 else "starboard", "x0": round(min(a0.x, a1.x)), "x1": round(max(a0.x, a1.x)),
                          "cell": int(math.floor(mid.x / 1000)), "solids": len(sec.Solids)})

# (21 October 2026, chalk note 13: "no top closing rail on side boards. See how magelan did") a cap rail on the waist's
# bulwark each side, from the quarterdeck's front to the bow: CAP_IN inboard over the planking and the ribs' heads and
# CAP_OUT proud of the planking outside, CAP_H thick on the sheer (the shell's own top: the sheer prism's points),
# broken at the gates; over each gun port it is the port's head.
CAP_OUT, CAP_IN, CAP_H = 40.0, 400.0, 80.0          # (22 October 2026, note 37: the planking and half the rib)
# (24 October 2026, chalk note 69: "rail top does not cover rib tops") wide enough inboard to cover every rib's head in
# the waist and forward, measured on the ribs as built (a rib's section is the narrowest across its thickness, so where
# her side narrows toward the bow its head stands further in than 400 at its after face), 20 to spare
_qf0 = L["quarterdeckFront"]
QD_BULWARK = LV["quarterdeckSide"] > LV["quarterdeck"] + 1     # (25 October 2026, note 103: her side planked up round it)
_need = []
for r_ in rib_solids:
    b_ = r_.BoundBox
    if (b_.XMin + b_.XMax) / 2 < _qf0 and not QD_BULWARK or b_.ZMax < LV["mainDeck"] + 500:
        continue
    top_ = b_.ZMax
    head = r_.common(Part.makeBox(b_.XLength + 2, 4000, 150, V(b_.XMin - 1, 0, top_ - 150)))
    if head.Volume < 1e3:
        continue
    yo_ = max(half_breadth(x_, top_ - 15) or 0.0 for x_ in (b_.XMin, (b_.XMin + b_.XMax) / 2, b_.XMax))
    _need.append((round(yo_ - head.BoundBox.YMin), round((b_.XMin + b_.XMax) / 2)))
# (400 where that covers them; toward the bow, where her side narrows fastest, wider at each rib, tapering between)
_by_x = {}
for n_, x_ in _need:
    _by_x[x_] = max(_by_x.get(x_, CAP_IN), math.ceil((n_ + 20) / 10) * 10)
_need = sorted(_by_x.items())


def cap_in_at(x):
    if not _need:
        return CAP_IN
    if x <= _need[0][0]:
        return _need[0][1]
    for (xa, wa), (xb, wb) in zip(_need[:-1], _need[1:]):
        if x <= xb:
            return wa + (wb - wa) * (x - xa) / (xb - xa)
    return _need[-1][1]


log("cap rail: inboard at the ribs", _need)
_qf = L["quarterdeckFront"]
_sp = [(x, z) for k, (x, z) in enumerate(sheer) if k % 5 == 0 or _qf - 200 <= x <= _qf + 2700 or k == len(sheer) - 1]


def sheer_at(x):
    for (xa, za), (xb, zb) in zip(_sp[:-1], _sp[1:]):
        if xa <= x <= xb:
            return za + (zb - za) * (x - xa) / (xb - xa)
    return _sp[-1][1] if x > _sp[-1][0] else _sp[0][1]


def cap_section(x, sgn):
    z = sheer_at(x)
    yo = half_breadth(x, z - 15) or 0.0
    if yo < 60:
        return None
    # (22 October 2026, note 34: "does not meet at bow") on to the stem, the two meeting on the centreline
    y_out, y_in = yo + CAP_OUT, max(yo - cap_in_at(x), 0.0)
    # (25 October 2026, draft D) beside the quarterdeck's stair against her side, where the sheer drops through the castle's
    # break at a climber's head height, only over her planking (no rib there): 330 inboard over the stair's cell, back to
    # the full width a cell either side
    for st_ in LAY["stairs"]:
        if st_["name"] == "quarterdeck" and st_["y"] * sgn > 0:
            xa_, xb_ = sorted((st_["foot"][0], st_["head"][0]))
            d_ = max(xa_ - x, x - xb_, 0.0)
            if d_ < 1000.0:
                y_in = max(y_in, yo - (330.0 + (cap_in_at(x) - 330.0) * d_ / 1000.0))
            # (the game-mesh check, with the upright stern's lines) where the break's tumblehome brings her side in over
            # the stair's cell, the cap's inner edge 15 outboard of the cell: else her collision there refuses the stair
            if xa_ - 1010.0 <= x <= xb_ + 100.0:          # (the flight and its landing a block aft of its head)
                y_in = max(y_in, min(abs(st_["y"]) + 505.0, yo - 60.0))
    pts = [V(x, sgn * y_in, z), V(x, sgn * y_out, z), V(x, sgn * y_out, z + CAP_H), V(x, sgn * y_in, z + CAP_H)]
    return Part.makePolygon(pts + [pts[0]])


CAP_RECORD = {"out": CAP_OUT, "in": CAP_IN, "inAtRibs": _need, "h": CAP_H, "runs": []}
_bow_x = max(v.Point.x for v in envelope.Vertexes)
_xs = [_qf + 100 * k for k in range(31)] + [_qf + 3000 + 250 * k for k in range(1, int((_bow_x - _qf - 3000) / 250) + 1)]
if QD_BULWARK:
    # (note 103) on round the quarterdeck's bulwark from the transom; (note 85: "top of rails has holes and not
    # continious") one run each side from the transom to the gate, and a cap across the transom joining them
    _xa = transom_x(LV["quarterdeckSide"]) + 60
    _xs = [_xa + 250 * k for k in range(int((_qf - 100 - _xa) / 250) + 1)] + _xs
_g0, _g1 = GATE["x"][0] + GATE_POST / 2, GATE["x"][1] - GATE_POST / 2
# (stations at least 60 apart: a loft's sections too close together fail)
_runs = [[x for x in _xs if x <= _g0 - 60] + [_g0], [_g1] + [x for x in _xs if x >= _g1 + 60]]
for run in _runs:
    for sgn in (1, -1):
        secs = []
        for x in run:
            w_ = cap_section(x, sgn)
            if w_ is None:
                break
            secs.append(w_)
        if len(secs) >= 2:
            rail_parts.append(Part.makeLoft(secs, True, True))
            CAP_RECORD["runs"].append({"side": "port" if sgn > 0 else "starboard", "x0": run[0], "x1": run[len(secs) - 1],
                                       "stations": len(secs)})
if QD_BULWARK:
    _tz = LV["quarterdeckSide"]
    _tx = transom_x(_tz)
    _ty = (half_breadth(_tx + 60, _tz - 15) or 2000.0) + CAP_OUT
    _tin = cap_in_at(_xa)
    _tcap = Part.makeBox(_tin + CAP_OUT + 60, 2 * _ty, CAP_H, V(_tx - CAP_OUT, -_ty, _tz))
    rail_parts.append(_tcap.common(forward_of_transom(-CAP_OUT - 5)))
    CAP_RECORD["runs"].append({"side": "transom", "x0": round(_tx), "y": round(_ty)})
log("cap rail", CAP_RECORD["runs"])


def stair(foot, head, width=800.0, stringer=(50.0, 250.0), tread=(180.0, 40.0), rise=250.0):
    """A steep stair walked up from its foot to its head (x, z mm): two stringers and a tread every `rise`."""
    (xf, zf), (xh, zh) = foot, head
    d = V(xh - xf, 0, zh - zf)
    n = V(d.z, 0, -d.x)
    n.normalize()                                     # below the stair's line
    if n.z > 0:
        n = n * -1
    parts = []
    for y0 in (-width / 2 - stringer[0], width / 2):
        a_, b_ = V(xf, y0, zf), V(xh, y0, zh)
        pts = [a_, b_, b_ + n * stringer[1], a_ + n * stringer[1]]
        parts.append(Part.Face(Part.makePolygon(pts + [pts[0]])).extrude(V(0, stringer[0], 0)))
    k = 1
    while zf + k * rise < zh - 1:
        z = zf + k * rise
        x = xf + (xh - xf) * (z - zf) / (zh - zf)
        parts.append(Part.makeBox(tread[0], width, tread[1], V(x - tread[0] / 2, -width / 2, z - tread[1])))
        k += 1
    return parts


stairs = Part.makeCompound([q.translated(V(0, t.get("y", 0.0), 0)) for t in LAY["stairs"] for q in stair(t["foot"], t["head"])])

# (24 October 2026, the user: "add default partition wall with doors and window to rear to form cabin") the steerage's
# front wall under the quarterdeck's edge, a door each side of the quarterdeck stair and a window outboard of each: in
# the game these are the pieces a player places (partitions, doors, windows), laid on a new ship; here a placeholder
CAB = LAY.get("cabin")
cabin = None
if CAB:
    cx_ = CAB["x"]
    wall = Part.makeBox(100, 8000, LV["quarterdeck"] - DECK_T - LV["mainDeck"], V(cx_ - 100, -4000, LV["mainDeck"])).common(inner)
    for j in CAB["doors"]:
        wall = wall.cut(Part.makeBox(300, 900, 2000, V(cx_ - 200, j * 1000 - 450, LV["mainDeck"])))
    for j in CAB["windows"]:
        wall = wall.cut(Part.makeBox(300, 600, 600, V(cx_ - 200, j * 1000 - 300, LV["mainDeck"] + 1000)))
    cabin = wall.removeSplitter()
# (25 October 2026, note 105) draft D's galley, the user's: its walls (partitions), the hearth, the oven on its table,
# a table and a storage vessel, where the game lays them as pieces (placeholders here)
KIT = LAY.get("kitchen")
if KIT:
    parts_ = []
    d0, d1 = LV["mainDeck"], LV["quarterdeck"] - DECK_T
    for w_ in KIT["walls"]:
        for c_ in w_["cells"]:
            for row in w_["rows"]:
                z0_, z1_ = d0 + 1000 * row, min(d0 + 1000 * (row + 1), d1)
                if w_["line"] == "x":                    # a wall across her on x = at, over the z cell c_
                    parts_.append(Part.makeBox(120, 1000, z1_ - z0_, V(w_["at"] * 1000 - 60, -(c_ + 0.5) * 1000, z0_)))
                else:                                    # along her on z = at, over the x cell from c_
                    parts_.append(Part.makeBox(1000, 120, z1_ - z0_, V(c_ * 1000, -w_["at"] * 1000 - 60, z0_)))
    def cellbox(c_, z0_, h_, inset=0.0):
        x_, k_ = c_
        return Part.makeBox(1000 - 2 * inset, 1000 - 2 * inset, h_, V(x_ * 1000 + inset, -(k_ + 0.5) * 1000 + inset, z0_))
    parts_.append(cellbox(KIT["hearth"]["cell"], d0, 500, 60))
    for t_ in KIT["things"]:
        parts_.append(cellbox(t_["cell"], d0, 1000 if "table" in t_["block"] else 900, 60 if "table" in t_["block"] else 120))
    parts_.append(cellbox(KIT["oven"]["cell"], d0 + 1000, 750, 80))
    cabin = Part.makeCompound([q.common(inner) for q in parts_])
# (25 October 2026, note 103) the swivel guns' places on the quarterdeck's cap rail, 2 a side (the fittings session
# draws the gun: a placeholder here, a crotch's post and a short barrel)
swivels = []
for sx_ in LAY.get("swivels", []):
    for sgn in (1, -1):
        zc_ = LV["quarterdeckSide"] + 80.0
        yo_ = half_breadth(sx_ * 1000, zc_ - 100) or 2800.0
        p0_ = V(sx_ * 1000, sgn * (yo_ - 150), zc_)
        swivels += [Part.makeCylinder(40, 250, p0_), spar(p0_ + V(-500, 0, 300), p0_ + V(600, 0, 300), 70, 50)]
# the wheel, where caravel_layout.py draws it in the game (a placeholder here: rim, posts)
_wx = LAY["fittings"]["wheel"]["cell"][0] * 1000 + 230
wheel_ph = Part.makeCompound([Part.makeCylinder(560, 75, V(_wx - 37, 0, LV["quarterdeck"] + 850), V(1, 0, 0)),
                              Part.makeBox(100, 440, 970, V(_wx + 190, -220, LV["quarterdeck"])),
                              Part.makeBox(100, 440, 970, V(_wx + 570, -220, LV["quarterdeck"]))])

gate_parts = []
# (21 October 2026, chalk note 12: "no gate doors and some stupid post on sides") no posts: the opening's sides are the
# bulwark's cut edges; a leaf in each, hinged on its after edge, swinging in (GateLeaves, the moving parts gate-port and
# gate-starboard); (note 60) plain inside, as her side
gate_leaves = []
GATE_LEAF = []
GATE_REBATE = []
GATE_BATTENS = []
GX0, GX1 = GATE["x"][0] + GATE_POST / 2, GATE["x"][1] - GATE_POST / 2
LEAF_T, LEAF_GAP = 70.0, 15.0
for sgn in (1, -1):                                   # port (+Y) first
    xm, zm = (GX0 + GX1) / 2, (GATE["sill"] + GATE["head"]) / 2
    yo, yi = half_breadth(xm, zm) or 3200, inner_half_breadth(xm, zm) or 3000
    ym = (yo + yi) / 2
    # (22 October 2026, chalk note 28: "gate should be the shape of full") the bulwark's whole thickness (the planking)
    # and height up to the sheer, its own piece of the cap rail on top
    LEAF_T = yo - yi
    top_z = sheer_at(xm)
    # (24 October 2026, chalk note 61: "when gates are open then collide with hull side"; the fittings session's swing
    # check) hinged on its inner face at its after edge, it swings in clear of the after jamb. (25 October 2026, note 91:
    # "gate now does not line up with then hull and has gaps on sides") the opening's full width again, 15 clear each end,
    # its own cap over its planking (40 proud outside, as the cap rail; none inboard of its hinge, which would swing into
    # the after jamb); its outer forward corner passes the forward jamb through a rebate in the jamb's planking and the
    # cap rail's end there (GATE_REBATE)
    x0 = GX0 + LEAF_GAP
    w = GX1 - GX0 - 2 * LEAF_GAP
    h = top_z - GATE["sill"] - LEAF_GAP
    leaf = Part.makeBox(w, LEAF_T, h, V(x0, sgn * ym - LEAF_T / 2, GATE["sill"] + LEAF_GAP / 2))
    y_in = ym - LEAF_T / 2
    cap_ = Part.makeBox(w, LEAF_T + CAP_OUT, CAP_H, V(x0, y_in if sgn > 0 else -(y_in + LEAF_T + CAP_OUT), GATE["sill"] + LEAF_GAP / 2 + h))
    # (24 October 2026, chalk note 60: "why there is Z on the gate. told to make it same inside as hull") no ledges and
    # brace on its inboard face: inside it is her side's planking, as the bulwark round it
    gate_leaves.append(leaf.fuse(cap_).removeSplitter())
    r_sw = math.hypot(w, LEAF_T + CAP_OUT) + 12.0
    disc = Part.makeCylinder(r_sw, h + CAP_H + 400, V(x0, sgn * y_in, GATE["sill"] - 100))
    GATE_REBATE.append(disc.common(Part.makeBox(r_sw + 100, 5000, h + CAP_H + 400,          # (out past the cap's lip)
                                                V(GX1 - 1, 0 if sgn > 0 else -5000, GATE["sill"] - 100))))
    # (note 91, the fittings session's choice) a stop batten on each jamb outside the leaf, hiding the 15 gaps: 75 wide (25
    # over the leaf), 25 thick, from the sill to under the cap's lip; the leaf only ever moves inboard of it
    yb_ = ym + LEAF_T / 2 + 2.0
    for xb0_ in (GX0 - 35.0, GX1 - LEAF_GAP - 25.0):
        GATE_BATTENS.append(Part.makeBox(75.0, 25.0, top_z - GATE["sill"] - 20.0,
                                         V(xb0_, yb_ if sgn > 0 else -(yb_ + 25.0), GATE["sill"] + 5.0)))
    GATE_LEAF.append({"side": "port" if sgn > 0 else "starboard", "x0": x0, "x1": x0 + w, "y": sgn * ym,
                      "t": LEAF_T, "z0": GATE["sill"], "z1": GATE["sill"] + LEAF_GAP / 2 + h + CAP_H,
                      "hingeY": sgn * (ym - LEAF_T / 2)})
for ropes in L["ropeLadders"]:
    pair = [[V(*q) for q in line] for line in ropes]
    for line in pair:
        gate_parts += [spar(line[k], line[k + 1], 20, 20) for k in range(len(line) - 1)]
    for k in range(1, min(len(pair[0]), len(pair[1]))):
        gate_parts.append(spar(pair[0][k], pair[1][k], 18, 18))
gates = Part.makeCompound(gate_parts)
gate_leaves_shape = Part.makeCompound(gate_leaves)
for rb_ in GATE_REBATE:
    shell = shell.cut(rb_).removeSplitter()
    rail_parts = [q.cut(rb_) if q.BoundBox.intersect(rb_.BoundBox) else q for q in rail_parts]
rail_parts += GATE_BATTENS
# (22 October 2026, the user: "visible surface overlaps create texture flickers ... railings") one solid: the posts'
# caps, the bars' joints and the members overlapped, their faces in one plane drawn twice
try:
    railings = rail_parts[0].multiFuse(rail_parts[1:]).removeSplitter()
    print("railings fused:", len(railings.Solids), "solids,", len(railings.Faces), "faces")
except Exception as e_:
    print("railings fuse failed, left a compound:", e_)
    railings = Part.makeCompound(rail_parts)
# (note 56) the removable spans first, in RAIL_SECTIONS' order (the exporter takes them by place), then the rest
railings = Part.makeCompound(rail_sections + list(railings.Solids))
log("railing: spans that come out", len(RAIL_SECTIONS), [(r_["side"][0], r_["cell"]) for r_ in RAIL_SECTIONS])

guns = []
parked = []
places = []
gun_fit = []
# A light gun for a caravel (the carrack's may differ): carriage 700 along X, 1000 deep, 500 high, its bed 500 wide
# between four trucks (150 radius, 100 wide); barrel 1600 from 100 ahead of the carriage's rear to 1700 ahead of it, on
# trunnions 650 ahead of the rear; so the whole gun fits 1 x 2 cells in plan.
# Parked (the user, 17 October): the gun stands in whole cells, its cell along X and Y 500..2500 from the
# centreline (the two cells inboard of the skin's cell), leaving the centre cell clear. Run out (20 October 2026, the
# guns in the game) it stands as far out as it can still be trained TRAIN either way about the upright PIVOT_AHEAD
# ahead of its carriage (in its port) and elevated ELEV (ELEV_QD on the quarterdeck), CLEAR of the side, the railing and the gates; its muzzle is
# then through the port (main deck) or over the side (quarterdeck). Fired, it runs back toward its parked place.
CAR_W, CAR_D, CAR_H, BARREL, GUN_L, CLEAR = 700.0, 1000.0, 500.0, 1600.0, 1700.0, 20.0
TRUCK_R, TRUCK_W, BED_W, BED_Z0, TRUNNION, PIVOT_AHEAD = 150.0, 100.0, 500.0, 120.0, 650.0, 150.0
# (the quarterdeck's top rail is 155 over the barrel at its muzzle: 7 degrees up keeps it clear as it runs back)
TRAIN, ELEV, ELEV_QD, RECOIL_MAX = 15.0, (-8.0, 12.0), (-8.0, 7.0), 750.0
PLACE_Y = (500.0, 2500.0)


def gun_parts(xc, rear, sgn, axis_z, deck):
    """A gun whose carriage's rear is |Y| = rear on side sgn (+1 port): its bed, fore (outboard) trucks, aft trucks and
    barrel, as the exporter splits them into moving parts."""
    y0 = rear if sgn > 0 else -(rear + CAR_D)
    bed = Part.makeBox(BED_W, CAR_D, CAR_H - BED_Z0, V(xc - BED_W / 2, y0, deck + BED_Z0))
    trucks = [Part.makeCompound([Part.makeCylinder(TRUCK_R, TRUCK_W, V(x0, sgn * yc, deck + TRUCK_R), V(1, 0, 0))
                                 for x0 in (xc - CAR_W / 2, xc + BED_W / 2)])
              for yc in (rear + CAR_D - TRUCK_R, rear + TRUCK_R)]
    barrel = spar(V(xc, sgn * (rear + GUN_L - BARREL), axis_z), V(xc, sgn * (rear + GUN_L), axis_z), 170, 120)
    return [bed, trucks[0], trucks[1], barrel]


def gun_posed(xc, rear, sgn, axis_z, deck, train, elev):
    """The gun laid: the barrel elevated about its trunnions (+ muzzle up), then all of it trained about the upright
    through its pivot (+ toward the bow), as GunLaying.Place does in the game."""
    parts = gun_parts(xc, rear, sgn, axis_z, deck)
    pv = V(xc, sgn * (rear + CAR_D + PIVOT_AHEAD), deck)
    parts[3].rotate(V(xc, sgn * (rear + TRUNNION), axis_z), V(sgn, 0, 0), elev)
    for p in parts:
        p.rotate(pv, V(0, 0, -sgn), train)
    return parts


def gun_hits(xc, rear, sgn, axis_z, deck, near, elev):
    """Does the gun, CLEAR further out than rear, meet anything near at its limits?"""
    poses = [(t, e) for t in (-TRAIN, TRAIN) for e in elev] + [(0.0, elev[0]), (0.0, 0.0), (0.0, elev[1])]
    for t, e in poses:
        for p in gun_posed(xc, rear + CLEAR, sgn, axis_z, deck, t, e):
            for so in p.Solids:
                for ob in near:
                    if so.BoundBox.intersect(ob.BoundBox) and so.common(ob).Volume > 1.0:
                        return True
    return False


for c, deck in GUN_DECKS:
    xc = c * 1000 + 500
    axis_z = deck + (port["sill"] + port["head"]) / 2
    on_qd = deck == LV["quarterdeck"]
    elev = ELEV_QD if on_qd else ELEV
    hw = half_breadth(xc, deck - 10 if on_qd else axis_z) or 3000      # the side's outside (the quarterdeck's at its top)
    rear_parked = PLACE_Y[0] + CLEAR
    for sgn in (1, -1):
        region = Part.makeBox(3200, 4300, 1700, V(xc - 1600, 0 if sgn > 0 else -4300, deck + 1))
        near = [s for s in (shell.common(region),) if s.Volume > 1.0] + \
               [so for so in railings.Solids + gates.Solids if so.BoundBox.intersect(region.BoundBox)]
        overlap = sum(so.common(shell).Volume for q in gun_parts(xc, rear_parked, sgn, axis_z, deck) for so in q.Solids)
        # out from the parked place in 50 mm steps to the first that meets something, then halving the step
        lo, hi = rear_parked, None
        while hi is None and lo < 4000:
            if gun_hits(xc, lo + 50, sgn, axis_z, deck, near, elev):
                hi = lo + 50
            else:
                lo += 50
        while hi is not None and hi - lo > 5:
            mid = (lo + hi) / 2
            if gun_hits(xc, mid, sgn, axis_z, deck, near, elev):
                hi = mid
            else:
                lo = mid
        rear_out = math.floor(lo / 10) * 10
        out_parts = gun_parts(xc, rear_out, sgn, axis_z, deck)
        overlap += sum(so.common(ob).Volume for p in out_parts for so in p.Solids for ob in near
                       if so.BoundBox.intersect(ob.BoundBox))
        guns += out_parts
        parked += gun_parts(xc, rear_parked, sgn, axis_z, deck)
        places.append(Part.makeBox(1000, PLACE_Y[1] - PLACE_Y[0], 1000,
                                   V(c * 1000, PLACE_Y[0] if sgn > 0 else -PLACE_Y[1], deck)))
        gun_fit.append({"cell": c, "deck": deck, "side": "port" if sgn > 0 else "starboard", "axisZ": axis_z,
                        "halfBreadth": round(hw), "rear_mm": rear_out, "front_mm": rear_out + CAR_D,
                        "trunnion_mm": rear_out + TRUNNION, "pivot_mm": rear_out + CAR_D + PIVOT_AHEAD,
                        "breech_mm": rear_out + GUN_L - BARREL, "muzzle_mm": rear_out + GUN_L,
                        "recoil_mm": min(RECOIL_MAX, rear_out - rear_parked),
                        "runOutMuzzleOutsideHull_mm": round(rear_out + GUN_L - hw),
                        "train": [-TRAIN, TRAIN], "elevation": list(elev), "trainBound": "the side or the railing" if hi else "none",
                        "skinOverlap_mm3": round(overlap), "parkedCells": f"x {c}, |y| cells 1..2",
                        "clearAisle": "centre cell, Y -500..500"})
        log("gun", c, gun_fit[-1]["side"], "run out: rear", rear_out, "muzzle outside the hull", gun_fit[-1]["runOutMuzzleOutsideHull_mm"],
            "recoil", gun_fit[-1]["recoil_mm"], "overlap", round(overlap))
gun_places = Part.makeCompound(places)
guns_parked = Part.makeCompound(parked)
guns_shape = Part.makeCompound(guns)
GUN_RECORD = {"carriage": [CAR_W, CAR_D, CAR_H], "bedWidth": BED_W, "bedBottom": BED_Z0, "truckRadius": TRUCK_R,
              "truckWidth": TRUCK_W, "barrel": BARREL, "length": GUN_L, "trunnion": TRUNNION, "pivotAhead": PIVOT_AHEAD,
              "clear": CLEAR, "partsPerGun": ["bed", "trucks-fore", "trucks-aft", "barrel"]}


# ------------------------------------------------------------------------------------------------ checks
def bbox(s):
    b = s.BoundBox
    return [round(b.XMin), round(b.YMin), round(b.ZMin), round(b.XMax), round(b.YMax), round(b.ZMax)]


def deck_under(x):
    if x < transom_x(LV["quarterdeck"]) or x > max(v.Point.x for v in envelope.Vertexes):
        return None
    return LV["quarterdeck"] if x <= L["quarterdeckFront"] else LV["mainDeck"]


sail_clear = []
for name, (tx, tz), (cx_, cz) in SAIL_FEET:
    worst = None
    for k in range(41):
        x = tx + (cx_ - tx) * k / 40
        z = tz + (cz - tz) * k / 40
        d = deck_under(x)
        if d is not None and (worst is None or z - d < worst[0]):
            worst = (z - d, x)
    sail_clear.append({"sail": name, "minHeadRoomOverDeck_mm": round(worst[0]) if worst else None,
                       "atX": round(worst[1]) if worst else None})


below = envelope.common(Part.makeBox(BIG, BIG, LV["waterline"], V(-BIG / 2, -BIG / 2, 0)))
disp_m3 = below.Volume / 1e9
wl_b = below.BoundBox


thick = []
for x, z in [(0, 2000), (0, 600), (-6000, 3000), (5000, 3000), (0, 6000), (-8000, 7500)]:
    yo, yi = half_breadth(x, z), inner_half_breadth(x, z)
    thick.append({"x": x, "z": z, "outer": round(yo or -1), "inner": round(yi or -1),
                  "horizontal": round((yo - yi) if yo and yi else -1)})
# the stem and post are solid: points just inside the envelope on the centreline must be in the shell
def end_points():
    pts = []
    for z in range(500, 6001, 500):
        line = Part.LineSegment(V(-BIG / 2, 0, z), V(BIG / 2, 0, z)).toShape()
        xs = sorted(v.Point.x for v in envelope.section(line).Vertexes)
        if xs:
            pts += [V(xs[0] + 60, 0, z), V(xs[-1] - 60, 0, z)]
    return pts


open_ends = [[round(p.x), round(p.z)] for p in end_points() if not shell.isInside(p, 1.0, True)]
def mass_props(shp):
    """Volume (m3) and centroid (mm) of a shape's solids."""
    v, c = 0.0, V(0, 0, 0)
    for so in shp.Solids:
        v += so.Volume
        c += so.CenterOfMass * so.Volume
    return {"volume_m3": round(v / 1e9, 4), "centroid_mm": [round(q) for q in (c * (1 / v) if v else c)]}


report = {
    "massProps": {n: mass_props(sh) for n, sh in (("Shell", shell), ("Backbone", backbone), ("Ribs", ribs),
                                                  ("Beams", beams), ("Decks", decks), ("Railings", railings),
                                                  ("Fittings", fittings), ("Stairs", stairs), ("Gates", gates),
                                                  ("Rig_Latina", rig_latina), ("Rig_Redonda", rig_redonda),
                                                  ("Guns", guns_shape))},
    "rigGeometry": RIG_RECORD, "hullLength_mm": round(HULL_L),
    "loft": "polygon waterlines, ruled",
    "linesFidelity": "exact at every lines point (polygon waterlines), linear between",
    "envelope": {"valid": envelope.isValid(), "closed": envelope.isClosed(), "solids": len(envelope.Solids),
                 "volume_m3": round(envelope.Volume / 1e9, 2), "bbox_mm": bbox(envelope)},
    "shell": {"valid": shell.isValid(), "solids": len(shell.Solids), "volume_m3": round(shell.Volume / 1e9, 3),
              "skinSamples": thick, "innerValid": inner_ok,
              "endsOpenOnCentreline": open_ends},
    "lengthOverall_mm": round(envelope.BoundBox.XLength), "beam_mm": round(envelope.BoundBox.YLength),
    "waterline": {"z_mm": LV["waterline"], "length_mm": round(wl_b.XLength), "beam_mm": round(wl_b.YLength),
                  "displacement_m3": round(disp_m3, 1), "displacement_t_seawater": round(disp_m3 * 1.025, 1),
                  "blockCoefficient": round(disp_m3 * 1e9 / (wl_b.XLength * wl_b.YLength * LV["waterline"]), 3)},
    "beamWithinCells": envelope.BoundBox.YMax <= 3500.0,
    "underside_mm": LV["underside"], "draft_mm": LV["waterline"] - LV["underside"],
    "ribs": {"count": len(rib_solids), "valid": all(r.isValid() for r in rib_solids),
             "skinOverlap_mm3": round(sum(r.common(shell).Volume for r in rib_solids)),
             "backboneOverlap_mm3": round(sum(r.common(backbone).Volume for r in rib_solids))},
    "backbone": {"valid": backbone.isValid(), "volume_m3": round(backbone.Volume / 1e9, 3),
                 "bottomZ_mm": round(backbone.BoundBox.ZMin), "skinOverlap_mm3": round(backbone.common(shell).Volume),
                 "draftToKeel_mm": round(LV["waterline"] - backbone.BoundBox.ZMin)},
    "beams": {"count": len(beam_solids),
              "ribOverlap_mm3": round(sum(b.common(r).Volume for b in beam_solids for r in rib_solids
                                          if b.BoundBox.intersect(r.BoundBox))),
              "deckOverlap_mm3": round(sum(b.common(decks).Volume for b in beam_solids)),
              "skinOverlap_mm3": round(sum(b.common(shell).Volume for b in beam_solids))},
    "sailFeet": sail_clear,
    "guns": gun_fit,
    "gateLeaves": GATE_LEAF,
    "tiller": {"end": [TILLER_END, 0.0, RUD_TOP], "head": [round(TILLER_HEAD.x), 0.0, round(TILLER_HEAD.z)]},
    "rudderAxis": [round(STOCK_X if COUNTER else rudder_lead(z_b) + RUD_GAP, 1), 0.0, z_b],
    "capRail": CAP_RECORD,
    "railSections": RAIL_SECTIONS,
    "gunGeometry": GUN_RECORD,
    "rigs": {"latina": bbox(rig_latina), "redonda": bbox(rig_redonda)},
    "buildSeconds": round(time.time() - T0, 1),
}
with open(os.path.join(OUT, "caravel-report.json"), "w") as f:
    json.dump(report, f, indent=1)
log(json.dumps({k: report[k] for k in ("lengthOverall_mm", "beam_mm", "draft_mm", "waterline", "beamWithinCells",
                                         "ribs", "backbone", "beams")}))


# ------------------------------------------------------------------------------------------------ document
doc = App.newDocument("Caravel")
bodies = {"Envelope": envelope, "Shell": shell, "Backbone": backbone, "Ribs": ribs, "Beams": beams, "Decks": decks,
          "Reference": reference,
          "Rig_Latina": rig_latina, "Rig_Redonda": rig_redonda, "Fittings": fittings, "Railings": railings, "Stairs": stairs, "Gates": gates,
          "GateLeaves": gate_leaves_shape, "Guns": guns_shape,
          "GunsParked": guns_parked, "GunPlaces": gun_places, "Wheel": wheel_ph}
if swivels:
    bodies["Swivels"] = Part.makeCompound(swivels)
if cabin is not None:
    bodies["Cabin"] = cabin
ROLES = {"Envelope": "envelope", "Shell": "structure", "Backbone": "structure", "Ribs": "structure",
         "Beams": "structure", "Decks": "structure"}
for name, shp in bodies.items():
    o = doc.addObject("Part::Feature", name)
    o.Shape = shp
    o.addProperty("App::PropertyString", "SliceRole", "Hansa",
                  "envelope: never sliced; structure: cut into blocks; none: placeholder or reference")
    o.SliceRole = ROLES.get(name, "none")
grp = doc.addObject("App::DocumentObjectGroup", "Waterlines")
for i, l in enumerate(L["hull"]):
    o = doc.addObject("Part::Feature", f"Waterline_{i:02d}")
    o.Shape = waterline_wire(l, spline=True)
    grp.addObject(o)
doc.recompute()
doc.saveAs(os.path.join(OUT, "caravel.FCStd"))
log("saved caravel.FCStd")


# ------------------------------------------------------------------------------------------------ meshes for the viewer
def tri_mesh(shp, lin=20.0, ang=0.35):
    m = MeshPart.meshFromShape(Shape=shp, LinearDeflection=lin, AngularDeflection=ang, Relative=False)
    pts, faces = m.Topology
    return {"p": [round(c) for v in pts for c in (v.x, v.y, v.z)], "i": [i for f in faces for i in f]}


def lines(shp, step=150.0):
    out = []
    for e in shp.Edges:
        n = max(2, int(e.Length / step) + 1)
        out.append([round(c) for v in e.discretize(n) for c in (v.x, v.y, v.z)])
    return out


mesh = {"units": "mm", "frame": "X fore, Y port, Z up",
        "bodies": {
            "Shell": tri_mesh(shell), "Decks": tri_mesh(decks), "Envelope": tri_mesh(envelope, 40.0),
            "Rig_Latina": tri_mesh(rig_latina, 30.0), "Rig_Redonda": tri_mesh(rig_redonda, 30.0),
            "Fittings": tri_mesh(fittings), "Railings": tri_mesh(railings, 15.0), "Stairs": tri_mesh(stairs),
            "Gates": tri_mesh(gates, 15.0), "GateLeaves": tri_mesh(gate_leaves_shape, 15.0), "Guns": tri_mesh(guns_shape, 15.0), "Ribs": tri_mesh(ribs), "Backbone": tri_mesh(backbone), "Beams": tri_mesh(beams),
            "GunsParked": tri_mesh(guns_parked, 15.0), "GunPlaces": tri_mesh(gun_places), "Wheel": tri_mesh(wheel_ph, 15.0),
            **({"Cabin": tri_mesh(cabin)} if cabin is not None else {}),
            **({"Swivels": tri_mesh(Part.makeCompound(swivels), 15.0)} if swivels else {})},
        "lines": {"Reference": lines(reference)},
        "levels": LV, "layout": LAY,
        "colours": {"Railings": "#6d4b2e", "GunsParked": "#3a3a3a", "Stairs": "#8a5a32", "Gates": "#6d4b2e", "GateLeaves": "#8a5a32", "Cabin": "#b08a5a", "Wheel": "#6d4b2e"}}
os.makedirs(os.path.join(OUT, "views"), exist_ok=True)
with open(os.path.join(OUT, "views", "caravel-mesh.js"), "w") as f:
    f.write("window.CARAVEL = ")
    json.dump(mesh, f, separators=(",", ":"))
    f.write(";\n")
log("wrote views/caravel-mesh.js")
# one self-contained page in the shared ship viewer (tools/shipview, used by the carrack too)
_bundle = os.path.join(ROOT, "tools", "shipview", "bundle.py")
_g = {"__file__": _bundle, "__name__": "shipview_bundle"}
exec(open(_bundle).read(), _g)
_g["bundle"](os.path.join(OUT, "views", "caravel-mesh.js"), os.path.join(OUT, "caravel.html"))
log("done")
