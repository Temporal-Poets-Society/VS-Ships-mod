"""A caravel build's solids in numbers, to compare two builds (27 October 2026, the build speed-up: the output must stay
the same): each body in models/caravel/caravel.FCStd with its volume, area, face, edge and solid counts and bounding box.

    AstoCADCmd.exe -c "exec(open(r'<root>/tools/caravel/model_fingerprint.py').read())"

Env: CARAVEL_ROOT, FINGERPRINT_FCSTD (default models/caravel/caravel.FCStd), FINGERPRINT_OUT (default
models/caravel/model-fingerprint.json).
"""
import json
import os

import FreeCAD as App

_root = os.environ.get("CARAVEL_ROOT", r"C:/VS mods/Hansa")
_out = os.environ.get("FINGERPRINT_OUT", os.path.join(_root, "models", "caravel", "model-fingerprint.json"))
_doc = App.openDocument(os.environ.get("FINGERPRINT_FCSTD") or os.path.join(_root, "models", "caravel", "caravel.FCStd"))
_rows = {}
for _o in _doc.Objects:
    _s = getattr(_o, "Shape", None)
    if _s is None or _s.isNull():
        continue
    _b = _s.BoundBox
    _rows[_o.Name] = {"volume_mm3": round(_s.Volume, 0), "area_mm2": round(_s.Area, 0), "faces": len(_s.Faces),
                      "edges": len(_s.Edges), "solids": len(_s.Solids),
                      "bbox": [round(v, 2) for v in (_b.XMin, _b.YMin, _b.ZMin, _b.XMax, _b.YMax, _b.ZMax)]}
App.closeDocument(_doc.Name)
with open(_out, "w") as _f:
    json.dump(_rows, _f, indent=1, sort_keys=True)
