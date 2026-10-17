"""Rebuild the master hull model's bodies from its editable inputs (docs/master-model.md). Generic: nothing here knows
the ship; it reads the document.

Inputs, in the document:
- `LowerWaterlines`, `UpperWaterlines`, `ForecastleWaterlines`: groups of closed sketches, each a waterline's port half
  at its height. Each group is lofted into a solid: the lower hull, the upper hull (from the poop overhang's underside),
  the forecastle.
- `ForecastleUnderProfile`, `SheerProfile`: sketches in the centreline plane (sketch x = model X, sketch y = model Z);
  everything inside them is cut away: the forecastle's below its underside, the hull above its sheer.
- `RibCells`: boxes, one per rib; a rib timber stands where its box is along X (its X position and length).
- `DeckSlabs`: boxes, one per deck: its planking's X range and Z range. A box with HasBeams gets a beam under its
  planking in every rib inside its X range.
- `Parameters`: a spreadsheet of thicknesses (aliases: shellThickness, ribMoulded, beamDepth, floorTimberBottom,
  floorTimberTop, backboneSided, backboneMoulded, backboneTop).

Outputs, recomputed here, each with a SliceRole:
- `Envelope` (Part::Feature, her outside, cut at the sheer; "envelope": never sliced);
- `Shell`, `Backbone`, `Ribs`, `Beams`, `Decks` (Mesh::Feature; "structure": the slicer cuts these into blocks), from her
  half-breadth fields (master_fields.py), which are also saved next to the model (<name>-fields.npz) for the slicer.

In AstoCAD: run tools/master/Rebuild_master.FCMacro after editing an input. Headless: create_master.py calls rebuild().
Not used, and why:
- the document's own boolean features: FreeCAD 1.1's element maps rejected fuses ("hasher mismatch") that the shapes'
  own booleans make valid;
- CAD offsets for the shell's inside: OCC's offset failed on her lofted solid (her sides converge on the stem and the
  sternpost, near-tangent seams), even on a plain midship piece of it.
"""
import os

import numpy as np
import FreeCAD as App
import Mesh
import Part

try:
    import master_fields as mf
except ImportError:                  # as a macro: this file's folder
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import master_fields as mf

V = App.Vector
BIG = 60000.0
OUTPUTS = ("Envelope", "Shell", "Backbone", "Ribs", "Beams", "Decks")


def _group(doc, name):
    g = doc.getObject(name)
    if g is None:
        raise RuntimeError(f"the model has no {name}")
    return list(g.Group)


def _loft(sketches):
    wires = [sk.Shape.Wires[0] for sk in sorted(sketches, key=lambda s: s.Placement.Base.z)]
    return Part.makeLoft(wires, True, False)


def _prism(sketch):
    face = Part.Face(sketch.Shape.Wires[0])
    return face.translated(V(0, -BIG / 4, 0)).extrude(V(0, BIG / 2, 0))


def _box(x0, x1, y0, y1, z0, z1):
    return Part.makeBox(x1 - x0, y1 - y0, z1 - z0, V(x0, y0, z0))


def _clean(shape, what):
    """removeSplitter where it keeps the shape valid; every result must be valid."""
    if not shape.isValid():
        raise RuntimeError(f"{what}: invalid shape")
    try:
        r = shape.removeSplitter()
        if r.isValid() and abs(r.Volume - shape.Volume) < 1e-6 * max(shape.Volume, 1.0) + 1.0:
            return r
    except Part.OCCError:
        pass
    return shape


def _param(doc, alias):
    return float(doc.getObject("Parameters").get(alias))


def _output(doc, name, shape, role):
    o = doc.getObject(name)
    if o is None:
        o = doc.addObject("Part::Feature" if isinstance(shape, Part.Shape) else "Mesh::Feature", name)
        o.addProperty("App::PropertyString", "SliceRole", "Hansa", "envelope: never sliced; structure: cut into blocks")
        g = doc.getObject("Bodies") or doc.addObject("App::DocumentObjectGroup", "Bodies")
        g.addObject(o)
    if isinstance(shape, Part.Shape):
        o.Shape = shape
    else:
        o.Mesh = shape
    o.SliceRole = role
    return o


def _mesh(tris):
    """A Mesh from triangles (n, 3, 3), through a binary STL (a Python list of a million facets is far slower)."""
    import tempfile
    if not len(tris):
        return Mesh.Mesh()
    t = np.asarray(tris, np.float32)
    n = np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0])
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    rec = np.zeros(len(t), dtype=[("n", "<f4", 3), ("v", "<f4", (3, 3)), ("a", "<u2")])
    rec["n"], rec["v"] = n, t
    fd, path = tempfile.mkstemp(suffix=".stl")
    with os.fdopen(fd, "wb") as f:
        f.write(b"\0" * 80 + np.uint32(len(t)).tobytes() + rec.tobytes())
    m = Mesh.Mesh(path)
    os.remove(path)
    return m


def _sheer_z(doc, xs):
    """The sheer's height at each x: the SheerProfile's lowest edge points at that x (its edges discretized, so a curve
    drawn in it counts as drawn)."""
    sk = doc.getObject("SheerProfile")
    pts = np.array([(v.x, v.z) for e in sk.Shape.Edges for v in e.discretize(Distance=10.0)])
    pts = pts[pts[:, 1] < 29000]
    order = np.argsort(pts[:, 0])
    px, pz = pts[order, 0], pts[order, 1]
    ux, inv = np.unique(np.round(px, 3), return_inverse=True)
    uz = np.full(len(ux), np.inf)
    np.minimum.at(uz, inv, pz)
    return np.interp(xs, ux, uz)


def rebuild(doc):
    """Recompute every output body from the inputs. Returns a dict of what was built."""
    doc.recompute()

    # her outside
    lower = _loft(_group(doc, "LowerWaterlines"))
    upper = _loft(_group(doc, "UpperWaterlines"))
    fc = _loft(_group(doc, "ForecastleWaterlines")).cut(_prism(doc.getObject("ForecastleUnderProfile")))
    half = _clean(lower.fuse([upper, fc]), "the half hull")
    # (a boolean can fail silently, valid but wrong: the forecastle once fused to nothing. Inclusion-exclusion must hold)
    lu = lower.fuse(upper)
    lu_expect = lower.Volume + upper.Volume - lower.common(upper).Volume
    expect = lu.Volume + fc.Volume - lu.common(fc).Volume
    if abs(lu.Volume - lu_expect) > 1e-3 * lu_expect or lu.common(fc).Volume <= 0 or \
            abs(half.Volume - expect) > 1e-3 * expect:
        raise RuntimeError(f"the half hull's fuse is wrong: {half.Volume / 1e9:.2f} m3, expected {expect / 1e9:.2f}")
    full = _clean(half.fuse(half.mirror(V(0, 0, 0), V(0, 1, 0))), "the hull")
    envelope = _clean(full.cut(_prism(doc.getObject("SheerProfile"))), "the envelope")

    # her structure, from her half-breadth (the port half: the half hull's centreline face on y = 0)
    xs, zs = mf.grid()
    w = mf.sample(half, xs, zs)
    p = {a: _param(doc, a) for a in ("shellThickness", "ribMoulded", "beamDepth", "floorTimberBottom", "floorTimberTop",
                                     "backboneSided", "backboneMoulded", "backboneTop")}
    ribs = [(b.Placement.Base.x, b.Placement.Base.x + float(b.Length)) for b in _group(doc, "RibCells")]
    decks = [(b.Placement.Base.x, b.Placement.Base.x + float(b.Length), b.Placement.Base.z,
              b.Placement.Base.z + float(b.Height), bool(getattr(b, "HasBeams", False))) for b in _group(doc, "DeckSlabs")]
    sheer = _sheer_z(doc, xs)
    parts, extra = mf.bodies(xs, zs, w, sheer, p, ribs, decks)
    fields = os.path.splitext(doc.FileName)[0] + "-fields.npz" if doc.FileName else None
    built = {"Envelope": envelope}
    meshes = {}
    for name, body in parts.items():
        meshes[name] = _mesh(mf.mesh(xs, zs, body))
        built[name] = body
    _output(doc, "Envelope", envelope, "envelope")
    for name, m in meshes.items():
        _output(doc, name, m, "structure")
    doc.recompute()
    result = {"xs": xs, "zs": zs, "w": w, "sheer": sheer, "parts": parts, "fields": extra, "envelope": envelope,
              "meshes": meshes}
    if fields:
        save_fields(fields, result)
    return result


def save_fields(path, r):
    arrays = {"xs": r["xs"], "zs": r["zs"], "w": r["w"], "sheer": r["sheer"]}
    arrays.update({k: v for k, v in r["fields"].items()})
    for name, body in r["parts"].items():
        for j, (ex, lo, hi) in enumerate(body):
            arrays[f"{name}{j}_exists"], arrays[f"{name}{j}_lo"], arrays[f"{name}{j}_hi"] = ex, lo, hi
    np.savez_compressed(path, **arrays)


if __name__ == "__main__":       # as a macro in AstoCAD
    rebuild(App.ActiveDocument)
