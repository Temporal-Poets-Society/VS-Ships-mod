"""One-off bootstrap of the master hull model (docs/master-model.md): make its editable inputs in AstoCAD from a lines
file (tools/master/sample_lines.py), build its bodies (master_rebuild.py), check them against Magelan's lines and save.
Once the user edits the model, the model is the source: this is not run again (that would overwrite the edits).

    (from the Hansa folder)
    C:\\AstoCAD\\bin\\AstoCADcmd.exe -c "exec(open(r'tools/master/create_master.py').read())"

Environment: MASTER_ROOT (default: the current folder), MASTER_LINES (default models/hansa-carrack/magelan-lines.json),
MASTER_OUT (default models/hansa-carrack/hansa-carrack.FCStd), MASTER_FORCE=1 to overwrite an existing model.

Millimetres; X along her, Y to port, Z up from her underside; cell borders every 1000 in X and Z, at +-500, +-1500 ... in Y.
"""
import importlib
import json
import os
import sys

import FreeCAD as App
import Part
import Sketcher
import numpy as np

V = App.Vector
ROOT = os.environ.get("MASTER_ROOT", os.getcwd())
LINES = os.environ.get("MASTER_LINES", os.path.join(ROOT, "models", "hansa-carrack", "magelan-lines.json"))
OUT = os.environ.get("MASTER_OUT", os.path.join(ROOT, "models", "hansa-carrack", "hansa-carrack.FCStd"))

DEG = 3
NPOLES = 30                                            # poles per waterline piece (one knot vector for every piece)
BIG = 60000.0


# ---------------------------------------------------------------- the waterlines' B-splines
def _knots(npoles, deg):
    inner = npoles - deg - 1
    knots = [0.0] + [k / (inner + 1) for k in range(1, inner + 1)] + [1.0]
    mults = [deg + 1] + [1] * inner + [deg + 1]
    return knots, mults


def _basis(u, npoles, deg):
    """Every basis function of the clamped uniform knot vector at parameters u: (len(u), npoles)."""
    knots, mults = _knots(npoles, deg)
    t = np.repeat(knots, mults).astype(float)
    u = np.clip(np.asarray(u, float), 0.0, 1.0 - 1e-12)
    n = len(t) - 1
    b = np.zeros((len(u), n))
    for i in range(n):
        b[:, i] = (t[i] <= u) & (u < t[i + 1])
    for k in range(1, deg + 1):
        nb = np.zeros((len(u), n - k))
        for i in range(n - k):
            d1, d2 = t[i + k] - t[i], t[i + k + 1] - t[i + 1]
            a = (u - t[i]) / d1 * b[:, i] if d1 > 0 else 0.0
            c = (t[i + k + 1] - u) / d2 * b[:, i + 1] if d2 > 0 else 0.0
            nb[:, i] = a + c
        b = nb
    return b


def fit_piece(pts):
    """A cubic B-spline through a piece's samples (least squares, its ends exact), parametrised by sample index: every
    piece is sampled alike (closer at its ends), so their poles correspond. Returns the curve and its worst error (mm)."""
    p = np.asarray(pts, float)
    u = np.linspace(0.0, 1.0, len(p))
    b = _basis(u, NPOLES, DEG)
    b[-1, :] = 0.0
    b[-1, -1] = 1.0
    rhs = p - np.outer(b[:, 0], p[0]) - np.outer(b[:, -1], p[-1])
    sol, *_ = np.linalg.lstsq(b[:, 1:-1], rhs, rcond=None)
    poles = np.vstack([p[0], sol, p[-1]])
    err = np.abs(b @ poles - p).max()
    knots, mults = _knots(NPOLES, DEG)
    curve = Part.BSplineCurve()
    curve.buildFromPolesMultsKnots([V(x, y, 0) for x, y in poles], mults, knots, False, DEG)
    return curve, float(err)


# ---------------------------------------------------------------- inputs
def waterline_sketch(doc, name, z, pieces):
    """A waterline's port half as a closed sketch at height z: the side's pieces (B-splines, from the after end
    forward, each starting where the last ended), square in to the centreline at the forward end (the stem's or the
    tip's half face), the centreline aft, and square out at the after end (the transom's, sternpost's or overhang's)."""
    sk = doc.addObject("Sketcher::SketchObject", name)
    sk.Placement = App.Placement(V(0, 0, z), App.Rotation())
    g, err = [], 0.0
    for pts in pieces:
        curve, e = fit_piece(pts)
        err = max(err, e)
        g.append(sk.addGeometry(curve, False))
    (xa, wa), (xf, wf) = pieces[0][0], pieces[-1][-1]
    g.append(sk.addGeometry(Part.LineSegment(V(xf, wf, 0), V(xf, 0, 0)), False))
    g.append(sk.addGeometry(Part.LineSegment(V(xf, 0, 0), V(xa, 0, 0)), False))
    g.append(sk.addGeometry(Part.LineSegment(V(xa, 0, 0), V(xa, wa, 0)), False))
    for i in range(len(g)):
        sk.addConstraint(Sketcher.Constraint("Coincident", g[i], 2, g[(i + 1) % len(g)], 1))
    return sk, err


def xz_profile(doc, name, poly):
    """A closed polygon in the centreline plane as a sketch (sketch x = model X, sketch y = model Z)."""
    sk = doc.addObject("Sketcher::SketchObject", name)
    sk.Placement = App.Placement(V(0, 0, 0), App.Rotation(V(1, 0, 0), 90))
    n = len(poly)
    for i in range(n):
        (x0, z0), (x1, z1) = poly[i], poly[(i + 1) % n]
        sk.addGeometry(Part.LineSegment(V(x0, z0, 0), V(x1, z1, 0)), False)
    for i in range(n):
        sk.addConstraint(Sketcher.Constraint("Coincident", i, 2, (i + 1) % n, 1))
    return sk


def simplify(poly, tol=1.0):
    """Douglas-Peucker on a polyline (mm)."""
    p = np.asarray(poly, float)
    if len(p) < 3:
        return p.tolist()
    a, b = p[0], p[-1]
    ab = b - a
    d = np.abs(ab[0] * (p[:, 1] - a[1]) - ab[1] * (p[:, 0] - a[0])) / (np.hypot(*ab) or 1.0)
    i = int(np.argmax(d))
    if d[i] <= tol:
        return [a.tolist(), b.tolist()]
    return simplify(p[: i + 1], tol)[:-1] + simplify(p[i:], tol)


def box(doc, name, x0, x1, y0, y1, z0, z1):
    b = doc.addObject("Part::Box", name)
    b.Length, b.Width, b.Height = x1 - x0, y1 - y0, z1 - z0
    b.Placement = App.Placement(V(x0, y0, z0), App.Rotation())
    return b


def group(doc, name, objs):
    g = doc.addObject("App::DocumentObjectGroup", name)
    g.addObjects(objs)
    return g


def create_inputs(doc, data):
    ref = data["reference"]
    worst = 0.0
    for key in ("lower", "upper", "forecastle"):
        sks = []
        for i, wl in enumerate(data[key]):
            sk, err = waterline_sketch(doc, f"{key.capitalize()}WL{i:02d}", wl["z"], wl["pieces"])
            sk.Label = f"{key} waterline Z {wl['z']:g}"
            worst = max(worst, err)
            sks.append(sk)
        group(doc, f"{key.capitalize()}Waterlines", sks)

    (xa, za), (xs0, _), (xt, zt) = data["forecastleUnderside"]
    slope = (zt - za) / (xt - xs0)
    under = xz_profile(doc, "ForecastleUnderProfile",
                       [(xa - 500, -1000), (xa - 500, za), (xs0, za), (xt + 1500, zt + slope * 1500), (xt + 1500, -1000)])
    sh = simplify(data["sheer"], 0.5)
    sheer = xz_profile(doc, "SheerProfile", [(sh[0][0], 30000.0)] + [tuple(p) for p in sh] + [(sh[-1][0], 30000.0)])
    group(doc, "Profiles", [under, sheer])

    sided = ref["timberSided"]
    ribs = []
    for cell in ref["ribCells"]:
        x0 = cell * 1000 + (1000 - sided) / 2
        b = box(doc, f"RibCell{'M' if cell < 0 else 'P'}{abs(cell):02d}", x0, x0 + sided, -500, 500, 0, 16000)
        b.Label = f"Rib cell {cell}"
        ribs.append(b)
    group(doc, "RibCells", ribs)

    dt = ref["deckThickness"]
    decks = []
    for d in ref["decks"]:
        lo, hi = d["x"] if d["x"] else (None, None)
        b = box(doc, f"Deck{d['name'].capitalize()}", -20000 if lo is None else lo, 28000 if hi is None else hi,
                -7000, 7000, d["top"] - dt, d["top"])
        b.addProperty("App::PropertyBool", "HasBeams", "Hansa", "a beam under this deck's planking in each rib cell")
        b.HasBeams = d["name"] != "hold"
        decks.append(b)
    group(doc, "DeckSlabs", decks)

    sheet = doc.addObject("Spreadsheet::Sheet", "Parameters")
    rows = [("shellThickness", ref["shellThickness"], "the hull's planking, normal to it"),
            ("ribMoulded", ref["ribMoulded"], "a rib's depth inside the planking"),
            ("beamDepth", ref["beamDepth"], "a beam's depth under its deck's planking"),
            ("floorTimberBottom", ref["floorTimbers"][0], "the floor timbers across the bottom: from"),
            ("floorTimberTop", ref["floorTimbers"][1], "to (the hold floor's underside)"),
            ("backboneSided", ref["backboneSided"], "the keel's, stem's and sternpost's width"),
            ("backboneMoulded", ref["backboneMoulded"], "their depth in from her outside"),
            ("backboneTop", ref["backboneTop"], "the stem and sternpost up to here")]
    for row, (alias, value, about) in enumerate(rows, start=1):
        sheet.set(f"A{row}", alias)
        sheet.set(f"B{row}", str(value))
        sheet.set(f"C{row}", about)
        sheet.setAlias(f"B{row}", alias)
    reference(doc, data)
    for o in doc.Objects:
        if o.TypeId == "Sketcher::SketchObject" or (o.TypeId == "Part::Box" and o.Name.startswith(("RibCell", "Deck"))):
            o.Visibility = False
    doc.recompute()
    return worst


def reference(doc, data):
    """Reference geometry (never sliced): the cell grid on the centreline plane and in plan at the waterline, the
    waterline, deck levels, mast cells and castle edges."""
    ref = data["reference"]
    objs = []
    lines = [Part.makeLine(V(x * 1000, 0, 0), V(x * 1000, 0, 16000)) for x in range(-20, 29)]
    lines += [Part.makeLine(V(-20000, 0, z * 1000), V(28000, 0, z * 1000)) for z in range(0, 17)]
    f = doc.addObject("Part::Feature", "GridCentreline")
    f.Shape = Part.makeCompound(lines)
    objs.append(f)
    wl = ref["waterline"]
    plan = [Part.makeLine(V(x * 1000, -6500, wl), V(x * 1000, 6500, wl)) for x in range(-20, 29)]
    plan += [Part.makeLine(V(-20000, y, wl), V(28000, y, wl)) for k in range(0, 7) for y in (k * 1000 + 500, -(k * 1000 + 500))]
    f = doc.addObject("Part::Feature", "GridPlanAtWaterline")
    f.Shape = Part.makeCompound(plan)
    objs.append(f)
    p = doc.addObject("Part::Plane", "Waterline")
    p.Length, p.Width = 48000, 14000
    p.Placement = App.Placement(V(-20000, -7000, wl), App.Rotation())
    objs.append(p)
    for d in ref["decks"]:
        p = doc.addObject("Part::Plane", f"Level{d['name'].capitalize()}")
        p.Length, p.Width = 48000, 14000
        p.Placement = App.Placement(V(-20000, -7000, d["top"]), App.Rotation())
        p.Visibility = False
        objs.append(p)
    for name, (x0, x1) in ref["mastCells"].items():
        objs.append(box(doc, f"MastCell{name.capitalize()}", x0, x1, -500, 500, 1000, 16000))
    for name, x in ref["castleEdges"].items():
        f = doc.addObject("Part::Feature", f"Edge{name[0].upper()}{name[1:]}")
        f.Shape = Part.makeLine(V(x, 0, 0), V(x, 0, 16000))
        objs.append(f)
    group(doc, "Reference", objs)


# ---------------------------------------------------------------- checks
def check(data, r, report):
    env = r["envelope"]
    devs = np.array([Part.Vertex(V(x, y, z)).distToShape(env)[0] for x, y, z in data["checkPoints"]])
    report["magelanDeviationMm"] = {"points": len(devs), "over10": int((devs > 10).sum()), "max": round(float(devs.max()), 2),
                                   "p95": round(float(np.percentile(devs, 95)), 2), "mean": round(float(devs.mean()), 2)}
    wl = data["reference"]["waterline"]
    report["displacementM3"] = round(env.common(Part.makeBox(BIG, BIG, wl, V(-BIG / 2, -BIG / 2, 0))).Volume / 1e9, 2)
    bb = env.BoundBox
    report["envelope"] = {"valid": env.isValid(), "solids": len(env.Solids),
                          "bboxMm": [round(v) for v in (bb.XMin, bb.XMax, bb.YMin, bb.YMax, bb.ZMin, bb.ZMax)]}
    # the bodies: their closed meshes' volumes; no two bodies' parts sharing |y| over any grid quad; none outside her
    names = list(r["parts"])
    for n in names:
        m = r["meshes"][n]
        report[n] = {"volumeM3": round(m.Volume / 1e9, 3), "triangles": m.CountFacets, "parts": len(r["parts"][n])}
    quad = lambda e: e[:-1, :-1] & e[1:, :-1] & e[:-1, 1:] & e[1:, 1:]   # noqa: E731
    flat = [(n, j, part) for n in names for j, part in enumerate(r["parts"][n])]
    clash = {}
    for i, (na, ja, (ea, la, ha)) in enumerate(flat):
        for nb, jb, (eb, lb, hb) in flat[i + 1:]:
            gap = np.minimum(ha, hb) - np.maximum(la, lb)
            both = quad(ea) & quad(eb) & quad(gap > 1.0)
            if both.any():
                key = f"{na}/{nb}"
                clash[key] = clash.get(key, 0) + int(both.sum())
    report["overlappingQuads"] = clash
    report["outsideHullNodes"] = int(sum((e & (h > r["w"] + 1.0)).sum() for n in names for e, _, h in r["parts"][n]))
    report["ok"] = (not clash and report["outsideHullNodes"] == 0 and report["envelope"]["valid"]
                    and report["magelanDeviationMm"]["p95"] <= 10.0)

if True:
    if os.path.exists(OUT) and os.environ.get("MASTER_FORCE") != "1":
        raise SystemExit(f"{OUT} exists and may hold the user's edits (MASTER_FORCE=1 overwrites it)")
    sys.path.insert(0, os.path.join(ROOT, "tools", "master"))
    import master_rebuild
    importlib.reload(master_rebuild)
    data = json.load(open(LINES, encoding="utf-8"))
    doc = App.newDocument("HansaCarrack")
    report = {"waterlineFitMm": round(create_inputs(doc, data), 2)}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    doc.saveAs(OUT)                         # (named first: the rebuild saves its fields next to it)
    built = master_rebuild.rebuild(doc)
    check(data, built, report)
    doc.save()
    print("MASTER", json.dumps(report))
