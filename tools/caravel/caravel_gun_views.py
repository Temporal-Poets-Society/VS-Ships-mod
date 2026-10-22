"""
Contact sheets of the caravel's guns as the mod poses them (22 October 2026, chalk notes 14 and 17): one waist gun and one
quarterdeck gun, each housed (run in, its lid shut), run out (the lid open) and laid (trained toward the bow, elevated to
its limit), seen from her deck and from outside. Reads what caravel_layout.py wrote:

    python tools/caravel/caravel_gun_views.py [out dir]      (models/caravel/views/guns_<kind>.png)

CARAVEL_LAYOUT_OUT (as the layout's): read the definition and mesh from there instead of the mod's assets.
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "shipgen"))
import view                                     # noqa: E402

COLOURS = {"tinbronze": (176, 128, 64), "iron": (70, 70, 74), "reedrope": (170, 150, 100), "black": (20, 18, 16),
           "tar": (52, 42, 34), "debarked/oak": (132, 96, 60), "planks/oak": (160, 120, 78), "pine": (170, 140, 96),
           "linen": (220, 214, 196), "copper": (180, 110, 70)}


def colour(tex):
    for k, c in COLOURS.items():
        if k in tex:
            return c
    return (140, 140, 140)


class Scene:
    """The mesh's quads near a point, as view.pose_parts wants them (a design's quads and moving parts)."""

    def __init__(self, D, M, centre, reach=3.2):
        self.moving_parts = D["movingParts"]
        self.quads = []
        c = np.asarray(centre, float)
        for group, qs in M["groups"].items():
            for q in qs:
                pts = [tuple(q[1 + 8 * k: 4 + 8 * k]) for k in range(4)]
                if min(np.max(np.abs(np.asarray(p) - c)) for p in pts) > reach:
                    continue
                self.quads.append({"group": group, "pts": pts, "col": colour(M["textures"][q[0]])})

    def render(self, eye, target, size, fov):
        out = []
        for q in self.quads:
            p = np.asarray(q["pts"], float)
            n = np.cross(p[1] - p[0], p[3] - p[0]) + np.cross(p[3] - p[2], p[1] - p[2])
            ln = np.linalg.norm(n)
            if ln < 1e-12:
                n = np.cross(p[1] - p[0], p[2] - p[0])
                ln = np.linalg.norm(n)
            out.append((p, n / ln if ln > 1e-12 else np.array([0.0, 1.0, 0.0]), q["col"]))
        return view.render(out, eye, target, size=size, fov=fov)


def sheet(D, M, gun, title, path, size=(520, 400)):
    f = next(x for x in D["fixtures"] if x.get("code") == gun)
    pos = np.asarray(f["pos"], float)
    s = 1.0 if f["out"][2] > 0 else -1.0
    states = [("housed", {f"fire:{gun}": 1.0}),
              ("run out", {f"open:{gun}": 1.0}),
              (f"laid {f['trainLimits'][1]:+.0f} / {f['elevationLimits'][1]:+.0f} deg",
               {f"open:{gun}": 1.0, f"train:{gun}": f["trainLimits"][1], f"elevate:{gun}": f["elevationLimits"][1]})]
    views = [("from her deck", pos + np.array([1.7, 1.55, -s * 2.0]), pos + np.array([-0.1, 0.55, s * 0.6]), 62),
             ("from outside", pos + np.array([1.9, 0.75, s * 3.6]), pos + np.array([-0.2, 0.55, s * 0.6]), 55)]
    W, H = size
    img = Image.new("RGB", (W * len(states), H * len(views) + 30), (240, 240, 236))
    dr = ImageDraw.Draw(img)
    dr.text((8, 8), title, fill=(0, 0, 0))
    for i, (name, values) in enumerate(states):
        for j, (vname, eye, target, fov) in enumerate(views):
            sc = Scene(D, M, pos + np.array([0, 0.5, s * 0.6]))
            view.pose_parts(sc, values)
            im = sc.render(eye, target, size, fov)
            img.paste(im, (i * W, 30 + j * H))
            dr.text((i * W + 6, 30 + j * H + 4), f"{name}, {vname}", fill=(0, 0, 0))
    img.save(path)
    return path


def main():
    base = os.environ.get("CARAVEL_LAYOUT_OUT")
    dpath = os.path.join(base, "caravel.json") if base else os.path.join(ROOT, "assets", "hansa", "config", "ships", "caravel.json")
    mpath = os.path.join(base, "caravel-mesh.json") if base else os.path.join(ROOT, "assets", "hansa", "config", "shipmeshes", "caravel.json")
    D, M = json.load(open(dpath)), json.load(open(mpath))
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "models", "caravel", "views")
    os.makedirs(out, exist_ok=True)
    guns = [x for x in D["fixtures"] if x.get("type") == "gun"]
    deck = min(x["pos"][1] for x in guns)
    waist = next(x["code"] for x in guns if x["pos"][1] == deck and x["out"][2] < 0)
    qd = next(x["code"] for x in guns if x["pos"][1] > deck and x["out"][2] < 0)
    for kind, gun in (("waist", waist), ("quarterdeck", qd)):
        print(sheet(D, M, gun, f"caravel {gun} ({kind}): housed, run out, laid", os.path.join(out, f"guns_{kind}.png")))


if __name__ == "__main__":
    main()
