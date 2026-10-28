"""Two caravel builds side by side (27 October 2026, the build speed-up: the output must stay the same): the solids'
fingerprints (model_fingerprint.py), cells.json, the build's report, the exported design and mesh. Prints what differs,
and by how much where it's numbers.

    python tools/caravel/compare_builds.py <baseline dir>

The baseline dir holds fingerprint.json and the files under their repo paths with "/" as "_" (as `git show <rev>:<path>`
saves them): models_caravel_cells.json, models_caravel_caravel-report.json, assets_hansa_config_ships_caravel.json,
assets_hansa_config_shipmeshes_caravel.json. The current ones are read from this checkout.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))


def diff(a, b, path="", out=None, tol=0.0):
    """Paths where a and b differ: numbers beyond tol, other values when not equal, missing keys."""
    out = [] if out is None else out
    if isinstance(a, dict) and isinstance(b, dict):
        for k in sorted(set(a) | set(b), key=str):
            if k not in a or k not in b:
                out.append((f"{path}/{k}", "only in " + ("baseline" if k in a else "new"), None))
            else:
                diff(a[k], b[k], f"{path}/{k}", out, tol)
    elif isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            out.append((path, f"length {len(a)} -> {len(b)}", None))
        for i, (x, y) in enumerate(zip(a, b)):
            diff(x, y, f"{path}[{i}]", out, tol)
    elif isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        if abs(a - b) > tol:
            out.append((path, f"{a} -> {b}", abs(a - b)))
    elif a != b:
        out.append((path, f"{str(a)[:60]} -> {str(b)[:60]}", None))
    return out


def report(name, a, b, tol=0.0, show=12):
    d = diff(a, b, tol=tol)
    nums = [x[2] for x in d if x[2] is not None]
    print(f"{name}: {'the same' if not d else f'{len(d)} differences'}" +
          (f" (numbers up to {max(nums):g} apart)" if nums else ""))
    for p, what, _ in d[:show]:
        print(f"    {p}: {what}")


base = sys.argv[1]
cur = {"fingerprint.json": os.path.join(ROOT, "models", "caravel", "model-fingerprint.json"),
       "models_caravel_cells.json": os.path.join(ROOT, "models", "caravel", "cells.json"),
       "models_caravel_caravel-report.json": os.path.join(ROOT, "models", "caravel", "caravel-report.json"),
       "assets_hansa_config_ships_caravel.json": os.path.join(ROOT, "assets", "hansa", "config", "ships", "caravel.json"),
       "assets_hansa_config_shipmeshes_caravel.json": os.path.join(ROOT, "assets", "hansa", "config", "shipmeshes", "caravel.json")}
for k, p in cur.items():
    a = json.load(open(os.path.join(base, k), encoding="utf-8"))
    b = json.load(open(p, encoding="utf-8"))
    if k == "models_caravel_caravel-report.json":
        a.pop("buildSeconds", None)
        b.pop("buildSeconds", None)
    report(k, a, b)
