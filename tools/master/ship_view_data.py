"""A master model's structure as the shared ship viewer's data (tools/shipview): window.SHIP in <ship>-mesh.js, rebuilt
from the model's saved fields (<ship>-fields.npz) on a coarser grid, and bundled into one page, <ship>.html. Plain
Python (numpy); no AstoCAD.

    python tools/master/ship_view_data.py [models/hansa-carrack/hansa-carrack-fields.npz] [--step 125]

The grid step must divide every rule's edge (rib timbers, decks, beams, floor timbers): 125 mm does for the carrack.
"""
import argparse
import base64
import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "shipview"))
import master_fields as mf  # noqa: E402
import bundle  # noqa: E402

BODIES = ("Shell", "Backbone", "Ribs", "Beams", "Decks")
UNIT = 10                                              # mm per stored step: int16 centimetres


def indexed(tris):
    """Shared vertices (int16 centimetres) and indices, base64."""
    q = np.round(tris.reshape(-1, 3) / UNIT).astype(np.int16)
    verts, inv = np.unique(q, axis=0, return_inverse=True)
    return {"pq": base64.b64encode(verts.astype("<i2").tobytes()).decode(), "unit": UNIT,
            "iq": base64.b64encode(inv.reshape(-1).astype("<u4").tobytes()).decode()}, len(verts)


def reference(ref):
    """Reference lines (model mm): each mast's cell outline on the centreline, each castle deck's edge, the decks' levels."""
    out = []
    for x0, x1 in ref["mastCells"].values():
        out.append([x0, 0, 1000, x1, 0, 1000, x1, 0, 16000, x0, 0, 16000, x0, 0, 1000])
    for x in ref["castleEdges"].values():
        out.append([x, 0, 0, x, 0, 16000])
    for d in ref["decks"]:
        lo, hi = d["x"] or (None, None)
        out.append([-20000 if lo is None else lo, 0, d["top"], 28000 if hi is None else hi, 0, d["top"]])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("fields", nargs="?", default=os.path.join(HERE, "..", "..", "models", "hansa-carrack",
                                                              "hansa-carrack-fields.npz"))
    ap.add_argument("--step", type=float, default=125.0)
    a = ap.parse_args()
    folder = os.path.dirname(os.path.abspath(a.fields))
    ship = os.path.basename(a.fields).replace("-fields.npz", "")
    d = np.load(a.fields)
    k = int(round(a.step / (d["xs"][1] - d["xs"][0])))
    xs, zs = d["xs"][::k], d["zs"][::k]
    bodies, stats = {}, {}
    for name in BODIES:
        body, j = [], 0
        while f"{name}{j}_exists" in d:
            body.append(tuple(d[f"{name}{j}_{p}"][::k, ::k] for p in ("exists", "lo", "hi")))
            j += 1
        t = mf.mesh(xs, zs, body) if body else np.zeros((0, 3, 3))
        if len(t):
            bodies[name], nv = indexed(t)
            stats[name] = (len(t), nv)
    lines_file = os.path.join(folder, "magelan-lines.json")
    ref = json.load(open(lines_file, encoding="utf-8"))["reference"] if os.path.exists(lines_file) else None
    data = {"name": "Hansa carrack" if ship == "hansa-carrack" else ship, "units": "mm", "frame": "X fore, Y port, Z up",
            "levels": {"waterline": ref["waterline"] if ref else 0,
                       **({"mainDeck": next(d["top"] for d in ref["decks"] if d["name"] == "main")} if ref else {})},
            "bodies": bodies,
            "lines": {"Reference": reference(ref)} if ref else {},
            "about": f"structure from {os.path.basename(a.fields)} on a {a.step:g} mm grid (tools/master/ship_view_data.py)"}
    views = os.path.join(folder, "views")
    os.makedirs(views, exist_ok=True)
    js = os.path.join(views, ship + "-mesh.js")
    with open(js, "w", encoding="utf-8") as f:
        f.write("window.SHIP = " + json.dumps(data) + ";\n")
    print("wrote", js, {n: f"{t} tris, {v} verts" for n, (t, v) in stats.items()})
    bundle.bundle(js, os.path.join(folder, ship + ".html"))


if __name__ == "__main__":
    main()
