"""
Close-up views of a ship design for checking its fit and finish without starting the game: a perspective camera and
a depth buffer (the preview images in docs/ are flat and painter-sorted, fine for proportions but not for seeing whether
a corner pokes through the hull). Slow (pure numpy), but only a few views are needed.

  python view.py                     # the standard set of close-ups, into docs/views/
  python view.py stern-water bow     # only these
  python view.py notes [path]        # the surveyor's chalk's notes (default: VintagestoryData/ModData/ships-notes.json):
                                     # each one's spot on the carrack marked and numbered, into docs/views/notes/
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))


def look_at(eye, target, up=(0, 1, 0)):
    eye, target, up = (np.asarray(v, float) for v in (eye, target, up))
    f = target - eye
    f /= np.linalg.norm(f)
    r = np.cross(f, up)
    r /= np.linalg.norm(r)
    u = np.cross(r, f)
    return eye, np.array([r, u, f])        # rows: camera right, up, forward


def render(quads, eye, target, size=(960, 640), fov=70.0, bg=(206, 222, 235), light=(0.35, 0.85, 0.4),
           water=None, depth=None):
    """quads: [(pts[4x3], normal, colour)]. water: a height to draw a translucent-looking sea plane at (or None).
    depth: a list to put the depth buffer in (distance along the view, inf where nothing was drawn)."""
    W, H = size
    eye, R = look_at(eye, target)
    focal = 0.5 * W / math.tan(math.radians(fov) / 2)
    L = np.asarray(light, float)
    L /= np.linalg.norm(L)
    img = np.empty((H, W, 3), float)
    img[:] = bg
    zbuf = np.full((H, W), np.inf)
    near = 0.05

    tris = []
    for pts, n, col in quads:
        pts = np.asarray(pts, float)
        shade = 0.4 + 0.6 * abs(float(np.dot(n, L)))
        c = (55 + np.asarray(col, float) * 0.85) * shade      # lifted, so tarred wood still shows its shapes
        tris.append((pts[[0, 1, 2]], c))
        if np.linalg.norm(pts[3] - pts[2]) > 1e-9 and np.linalg.norm(pts[3] - pts[0]) > 1e-9:
            tris.append((pts[[0, 2, 3]], c))
    if water is not None:
        s = 60.0
        wq = np.array([[eye[0] - s, water, eye[2] - s], [eye[0] + s, water, eye[2] - s], [eye[0] + s, water, eye[2] + s],
                       [eye[0] - s, water, eye[2] + s]])
        wc = np.array([60, 110, 150], float)
        tris.append((wq[[0, 1, 2]], wc))
        tris.append((wq[[0, 2, 3]], wc))

    def to_cam(p):
        return (p - eye) @ R.T

    for tri, col in tris:
        cam = to_cam(tri)
        behind = cam[:, 2] < near
        if behind.all():
            continue
        polys = [cam]
        if behind.any():
            # clip against the near plane
            out = []
            for i in range(3):
                a, b = cam[i], cam[(i + 1) % 3]
                ia, ib = a[2] >= near, b[2] >= near
                if ia:
                    out.append(a)
                if ia != ib:
                    t = (near - a[2]) / (b[2] - a[2])
                    out.append(a + (b - a) * t)
            if len(out) < 3:
                continue
            polys = [np.array([out[0], out[k], out[k + 1]]) for k in range(1, len(out) - 1)]
        for pc in polys:
            sx = W / 2 + focal * pc[:, 0] / pc[:, 2]
            sy = H / 2 - focal * pc[:, 1] / pc[:, 2]
            x0, x1 = int(max(0, math.floor(sx.min()))), int(min(W - 1, math.ceil(sx.max())))
            y0, y1 = int(max(0, math.floor(sy.min()))), int(min(H - 1, math.ceil(sy.max())))
            if x0 > x1 or y0 > y1:
                continue
            area = (sx[1] - sx[0]) * (sy[2] - sy[0]) - (sx[2] - sx[0]) * (sy[1] - sy[0])
            if abs(area) < 1e-9:
                continue
            xs = np.arange(x0, x1 + 1) + 0.5
            ys = np.arange(y0, y1 + 1) + 0.5
            X, Y = np.meshgrid(xs, ys)
            w0 = ((sx[1] - X) * (sy[2] - Y) - (sx[2] - X) * (sy[1] - Y)) / area
            w1 = ((sx[2] - X) * (sy[0] - Y) - (sx[0] - X) * (sy[2] - Y)) / area
            w2 = 1 - w0 - w1
            inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
            if not inside.any():
                continue
            iz = w0 / pc[0, 2] + w1 / pc[1, 2] + w2 / pc[2, 2]
            z = 1.0 / np.maximum(iz, 1e-9)
            sub = zbuf[y0:y1 + 1, x0:x1 + 1]
            win = inside & (z < sub)
            sub[win] = z[win]
            img[y0:y1 + 1, x0:x1 + 1][win] = col
    if depth is not None:
        depth.append(zbuf.copy())
    # a little haze with distance
    fog = np.clip((zbuf - 25) / 80, 0, 0.6)[..., None]
    fog[np.isinf(zbuf)] = 0
    img = img * (1 - fog) + np.asarray(bg, float) * fog
    from PIL import Image
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))


VIEWS = {
    # name: eye, target, sea level (or None), fov
    "stern-water": ((-22.0, 2.2, 6.5), (-13.0, 0.5, 0.0), 0.0, 60),
    "rudder": ((-19.0, 0.8, 4.5), (-15.0, 0.5, 0.0), None, 60),
    "bow": ((26.0, 5.0, 7.0), (17.5, 4.5, 0.0), None, 60),
    "bow-top": ((23.5, 9.5, 4.5), (19.5, 7.0, 0.0), None, 60),
    "bow-tip": ((22.6, 8.6, 1.6), (21.0, 7.4, 0.0), None, 60),
    "figurehead": ((22.5, 4.0, 3.5), (18.6, 4.6, 0.0), None, 55),
    "waterline-side": ((6.0, 1.2, 9.5), (6.0, 0.5, 3.0), None, 65),
    "hold-bow": ((-2.0, -0.6, 0.0), (12.0, -1.4, 0.0), None, 80),
    "hold-stern": ((5.0, -0.6, 0.0), (-10.0, -1.4, 0.0), None, 80),
    "hold-side": ((4.0, -0.5, 0.0), (7.0, -1.8, 1.6), None, 80),
    "lowerdeck-bow": ((1.0, 2.2, 1.0), (15.0, 1.6, 1.0), None, 80),
    "lowerdeck-stern": ((0.0, 2.2, 1.0), (-13.0, 1.8, 1.0), None, 80),
    "waist-aft": ((8.0, 5.4, 1.5), (-6.5, 5.0, -0.5), None, 80),
    "waist-fwd": ((-2.5, 5.4, -1.8), (11.0, 5.2, 0.5), None, 80),
    "stair-quarter": ((-1.0, 5.6, 0.5), (-5.0, 5.2, 2.8), None, 75),
    "stair-forecastle": ((6.0, 5.6, 0.0), (10.0, 5.2, 2.4), None, 75),
    "companion": ((-2.0, 5.6, -0.5), (-5.0, 4.0, -2.8), None, 75),
    "capstan": ((7.0, 5.8, 0.6), (10.5, 4.0, -1.8), None, 70),
    "gate": ((5.9, 5.2, 0.5), (5.9, 4.2, 4.2), None, 70),
    "gate-open": ((3.0, 5.6, 0.8), (6.2, 4.0, 3.6), None, 70),
    "gate-out": ((5.9, 4.0, 8.5), (5.9, 3.8, 3.0), None, 60),
    "quarter-side": ((-8.0, 6.5, 9.0), (-8.0, 6.0, 3.0), None, 65),
    "forecastle-side": ((13.0, 6.5, 8.5), (13.0, 5.8, 2.5), None, 65),
    "forecastle-in": ((12.6, 5.2, 0.0), (17.0, 4.6, 0.0), None, 80),
    "greatcabin": ((-7.0, 5.1, 0.0), (-14.0, 4.6, 0.0), None, 80),
    "quarterdeck": ((-4.0, 8.2, 0.0), (-9.0, 7.0, 0.0), None, 80),
    "quarterdeck-landing": ((-5.2, 8.3, 3.0), (-9.5, 7.2, -0.5), None, 80),
    "helm-gallows": ((-6.0, 8.3, 0.0), (3.0, 5.8, 0.0), None, 75),
    "gallows": ((0.0, 4.9, -3.0), (3.5, 6.6, 0.5), None, 80),
    "frames": ((0.0, 2.0, -1.0), (3.0, 1.8, 3.5), None, 70),
    # the deep carracks' raked forecastle underside and overhanging poop (python view.py --variant=2 ...)
    "bow-under": ((24.5, 2.6, 6.0), (19.0, 5.0, 0.0), 0.0, 60),
    "stern-overhang": ((-23.0, 4.5, 7.0), (-16.0, 6.5, 0.0), 0.0, 60),
    "poopcabin": ((-9.8, 8.2, 0.5), (-17.5, 7.4, -0.3), None, 80),
    # the carrack since 0.3.4: the companionway, hatch and pump forward of the mainmast; the binnacle before the wheel
    "companion-waist": ((0.0, 5.6, 2.2), (4.5, 3.6, -0.3), None, 75),
    "hatch-pump": ((10.5, 5.8, 2.0), (5.0, 3.8, -0.3), None, 75),
    "helm-binnacle": ((-2.2, 8.5, 1.9), (-5.2, 7.0, -0.2), None, 70),
    "companion-below": ((6.5, 2.2, 1.5), (2.0, 1.2, 0.0), None, 80),
    "waist-long": ((-2.0, 6.2, 2.8), (9.5, 3.8, -0.8), None, 75),
    "capstan-aft": ((6.6, 5.7, -2.6), (10.25, 4.0, -1.9), None, 70),
    # 0.3.6 (the sixth client test, 8): the glazed hatch and the hold hatch right after the companionway, the pump on
    # the centreline at the hatch's forward end, the gallows' posts as frames, the capstan in the bow and the cable to
    # it from the port hawse, the hearth in the forecastle's starboard after corner
    "waist-hatches": ((11.3, 6.3, 1.2), (3.8, 4.4, -0.3), None, 80),
    "waist-hatches-open": ((11.3, 6.3, 1.2), (3.8, 4.4, -0.3), None, 80),
    "ladders-cutaway": ((4.4, 1.0, 8.0), (4.4, 0.7, 0.0), None, 70),
    "lowerdeck-ladders": ((4.4, 2.1, 2.7), (4.4, 0.6, -0.6), None, 85),
    "lowerdeck-landing": ((8.1, 2.2, 0.9), (3.0, 0.9, -0.1), None, 80),
    "bow-port": ((25.0, 4.8, -6.8), (18.6, 4.6, -1.4), None, 60),
    "bow-deck": ((13.0, 5.1, -0.2), (18.6, 4.0, -1.4), None, 80),
    "capstan-cable": ((9.0, 7.4, -4.4), (10.8, 3.6, -1.4), None, 70),
    "hearth": ((14.3, 5.0, -1.5), (13.0, 4.3, 2.6), None, 80),
    "hearth-close": ((13.1, 4.7, 0.6), (13.1, 4.1, 2.9), None, 75),
    "hearth-deck": ((14.8, 8.2, 0.6), (13.1, 7.0, 3.3), None, 75),
    # the working hearth (0.3.6): lit, a clay pot on it with its lid
    "hearth-pot": ((13.1, 4.7, 0.6), (13.1, 4.1, 2.9), None, 75),
    "hearth-pot-side": ((14.6, 4.6, 1.2), (13.1, 4.0, 2.8), None, 60),
    "gallows-post": ((4.6, 5.3, 2.4), (6.6, 5.4, 4.5), None, 75),
    "gallows-out": ((4.4, 6.0, 9.5), (4.4, 5.6, 4.0), None, 70),
    # the metal plate for the ship's spawn point on the poop cabin's front, port of its door, and the spot in front of it
    "helm-plate": ((-4.6, 8.3, 1.2), (-8.5, 7.4, -1.5), None, 75),
    "plate": ((-6.6, 8.1, -0.4), (-8.5, 7.6, -1.5), None, 70),
    # the bilge pump (a fixture since 0.3.5: held right-click works it), its brake at rest and rocked over
    "pump": ((10.4, 5.5, 2.1), (8.5, 4.4, 0.0), None, 60),
    "pump-stroke": ((10.4, 5.5, 2.1), (8.5, 4.4, 0.0), None, 60),
    # the waist guns (0.3.5, item 16): along the starboard side from the deck, one close run out and run back (fired),
    # through the ports from outside, and the port side's past the companionway and the hatch
    "guns-deck": ((1.0, 5.6, -2.6), (5.5, 3.8, 3.9), None, 75),
    "gun-close": ((1.2, 5.1, 1.6), (3.0, 3.9, 3.9), None, 65),
    "gun-recoil": ((1.2, 5.1, 1.6), (3.0, 3.9, 3.9), None, 65),
    "guns-out": ((5.0, 4.6, 11.0), (5.0, 3.9, 4.5), None, 60),
    "guns-port": ((9.8, 6.2, 1.2), (3.5, 3.6, -3.8), None, 75),
    "guns-port-recoil": ((9.8, 6.2, 1.2), (3.5, 3.6, -3.8), None, 75),
    # 0.3.9 (the ninth client test): hull R on the one block grid, the waist 2 longer (python view.py grid)
    "grid-greatcabin": ((-3.6, 5.1, 1.6), (-14.0, 4.5, -0.8), None, 85),
    "grid-greatcabin-companion": ((-9.0, 5.4, 0.8), (-3.2, 4.0, -4.0), None, 80),
    "grid-greatcabin-front": ((-8.0, 5.3, 0.0), (-3.0, 4.6, -1.5), None, 80),
    "grid-waist": ((-1.5, 6.4, 2.6), (12.0, 4.0, -1.0), None, 80),
    "grid-waist-aft": ((13.5, 6.4, -2.2), (-2.5, 4.4, 1.0), None, 80),
    "grid-hold": ((-9.0, -0.8, 0.6), (8.0, -1.8, 0.0), None, 85),
    "grid-hold-side": ((2.0, -0.9, -1.0), (6.0, -1.7, 3.6), None, 85),
    "grid-lowerdeck": ((-12.0, 2.1, 0.6), (8.0, 1.5, 0.0), None, 85),
    "grid-lowerdeck-side": ((0.0, 2.1, -2.2), (3.0, 1.4, 5.5), None, 85),
    "grid-gates": ((8.0, 5.4, 0.6), (11.0, 4.2, 4.9), None, 75),
    "grid-gates-open": ((8.0, 5.4, 0.6), (11.0, 4.2, 4.9), None, 75),
    "grid-gate-out": ((11.0, 4.6, 10.5), (11.0, 4.0, 5.0), None, 60),
    "grid-gate-out-open": ((11.0, 4.6, 10.5), (11.0, 4.0, 5.0), None, 60),
    "grid-capstan": ((10.2, 5.9, 1.4), (12.5, 4.0, -1.0), None, 75),
    "grid-heads": ((19.6, 8.3, 0.0), (22.5, 6.8, 0.0), None, 75),
    "grid-forecastle-in": ((15.3, 5.2, -1.2), (17.4, 4.2, 2.4), None, 85),
    "grid-poopcabin": ((-9.6, 8.2, 0.6), (-16.5, 7.4, -0.4), None, 85),
    "grid-stern": ((-25.0, 6.0, 8.0), (-15.0, 4.5, 0.0), 0.0, 60),
    "grid-bow": ((33.0, 5.0, 9.0), (22.0, 3.5, 0.0), 0.0, 60),
    "grid-side": ((3.0, 5.0, 30.0), (3.0, 4.0, 0.0), 0.0, 70),
    "grid-section": ((15.0, 3.0, 0.0), (0.0, 3.0, 0.0), None, 70),
    "grid-section-aft": ((5.0, 5.0, 0.0), (-10.0, 5.0, 0.0), None, 70),
    # 0.3.10 (the tenth client test): the keel round the forefoot and the heel, the hold's floor at the bow, the
    # companion's door, the beams' knees, the pin rail by the mainmast
    "grid-keel-forefoot": ((21.0, -5.2, 5.5), (16.0, -2.5, 0.0), None, 60),
    "grid-keel-heel": ((-16.5, -3.4, 5.0), (-12.3, -2.2, 0.0), None, 60),
    "grid-hold-bow": ((14.6, -1.7, 1.2), (16.6, -2.45, -0.1), None, 70),
    "grid-door-companion-in": ((-6.5, 5.3, -1.4), (-3.0, 4.6, -4.0), None, 75),
    "grid-knees-lowerdeck": ((3.0, 1.8, 0.0), (4.5, 2.9, 4.8), None, 80),
    "grid-knees-hold": ((3.0, -1.2, 0.0), (4.5, 0.0, 4.2), None, 80),
    "grid-waist-pinrail": ((3.2, 5.4, -1.0), (-1.4, 4.2, -5.0), None, 75),
    # 0.3.12 (chalk notes 107-126 on 0.3.10): the great cabin's front, the walled stairwell and its landing and doors, the
    # quarterdeck stair a block outboard, one post where a stair meets a railing, the compass, the hold's upright ladder,
    # the pin board, the curved knees, the side windows' posts
    "0312-front": ((2.2, 5.6, 0.6), (-2.75, 4.9, 0.6), None, 75),
    "0312-front-stbd": ((0.6, 5.5, 4.3), (-2.75, 5.1, 2.6), None, 75),
    "0312-stairwell": ((-3.3, 5.3, 4.1), (-7.2, 2.2, 3.9), None, 80),
    "0312-cabin-stairwell": ((-8.8, 5.1, -0.6), (-4.2, 4.6, 3.3), None, 80),
    "0312-lowerdeck-stairs": ((-10.0, 2.2, 1.0), (-5.5, 1.0, 3.6), None, 80),
    "0312-qd-stairhead": ((-5.2, 8.4, 0.6), (-2.8, 7.0, 3.0), None, 75),
    "0312-poop-stairhead": ((-11.4, 11.4, -0.4), (-8.8, 10.0, 2.0), None, 75),
    "0312-binnacle": ((-5.0, 8.7, 0.9), (-3.5, 7.6, 0.0), None, 45),
    "0312-holdladder": ((4.6, -1.0, 1.6), (8.6, -0.2, 0.0), None, 80),
    "0312-holdladder-waist": ((10.6, 6.2, 1.8), (8.4, 3.6, 0.0), None, 75),
    "0312-pinrail": ((2.4, 5.2, 1.4), (-1.0, 4.5, 4.9), None, 70),
    "0312-knees-cabin": ((-8.0, 5.0, 1.2), (-10.0, 5.9, 4.4), None, 70),
    "0312-knees-hold": ((3.0, -1.2, 0.5), (4.0, -0.6, 4.6), None, 75),
    "0312-gallows-knee": ((2.3, 6.1, 2.9), (4.0, 6.0, 4.3), None, 60),
    "0312-qd-stair-side": ((0.6, 6.0, -0.6), (-2.4, 6.0, 2.6), None, 70),
    "0312-poop-window-in": ((-11.0, 8.0, 0.4), (-11.0, 7.9, 4.0), None, 75),
    "0312-poop-window-out": ((-11.0, 8.2, 8.0), (-11.0, 8.0, 3.5), None, 60),
    "0312-greatcabin-port": ((-7.5, 5.0, 1.5), (-11.0, 4.8, -4.6), None, 75),
    # 0.3.13 (chalk notes 127-140 on 0.3.12): the dark strip by the forecastle's break, the main channel down at the
    # rail, the capstan a block aft, the front window's casing inside its opening and the lead cames, the glazed hatch's
    # panes, the gallows' knees, the heads' seats, the hold stair under the companion, the hold's two ladders
    "0313-note129": ((12.79, 5.2, -2.52), (14.56, 5.48, -4.0), None, 70),
    "0313-channel-waist": ((-0.6, 5.19, -2.66), (0.6, 5.0, -5.6), None, 75),
    "0313-channel-out": ((1.2, 5.2, -10.0), (1.0, 4.7, -5.0), None, 60),
    "0313-capstan": ((7.2, 6.2, 2.4), (10.5, 3.7, 0.0), None, 70),
    "0313-front-window": ((-0.8, 5.2, -1.4), (-2.75, 4.95, -2.0), None, 70),
    "0313-window-cames": ((-1.9, 5.0, -2.0), (-2.75, 4.95, -2.0), None, 60),
    "0313-stern-cames": ((-21.0, 6.0, 1.5), (-16.0, 5.6, 0.5), None, 45),
    "0313-hatch": ((9.6, 6.0, 1.2), (7.0, 3.7, 0.0), None, 70),
    "0313-gallows-knee": ((11.9, 5.2, 4.16), (10.0, 5.95, 4.3), None, 70),
    "0313-gallows-knee-side": ((9.9, 5.4, 0.8), (9.9, 5.6, 4.6), None, 70),
    "0313-heads": ((20.6, 8.2, 0.02), (22.2, 6.85, 0.83), None, 70),
    "0313-lowerdeck-stairs": ((-10.0, 2.2, 1.0), (-5.5, 1.0, 3.0), None, 80),
    "0313-holdladders": ((4.6, -1.0, 1.6), (8.6, -0.2, 0.0), None, 80),
    "0313-holdladders-side": ((8.3, 0.6, 4.0), (8.3, 0.0, 0.0), None, 80),
    "0313-front": ((2.2, 5.6, 0.6), (-2.75, 4.9, 1.6), None, 80),
    "0313-companion-door": ((-0.5, 5.2, 4.0), (-2.7, 5.4, 4.07), None, 75),
    "0313-companion-door-wide": ((1.5, 5.6, 2.0), (-2.75, 5.0, 3.5), None, 75),
    "0313-door-room-port": ((-0.6, 5.6, -2.8), (-1.95, 5.5, -4.6), None, 70),
    "0313-door-room-stbd": ((-0.6, 5.6, 2.8), (-1.95, 5.5, 4.6), None, 70),
    "0313-plan-main": ((-4.5, 14.0, 2.4), (-4.5, 3.5, 2.5), None, 50),
    "0313-plan-lower": ((-4.5, 11.0, 2.4), (-4.5, 0.5, 2.5), None, 50),
    "0313-plan-hold": ((-4.5, 8.0, 2.4), (-4.5, -2.5, 2.5), None, 50),
    # 0.3.16 carrack-m (HANSA_MODULAR=1 python view.py m): the laid pieces (stand-ins: the mod draws them) at the
    # fronts, the stairs and round the wells; plans of each deck cut over it
    "m-waist-aft": ((9.0, 6.0, 0.4), (-6.0, 5.0, 0.6), None, 80),
    "m-waist-fwd": ((-1.5, 6.0, -1.2), (14.0, 5.0, 0.8), None, 80),
    "m-front": ((2.4, 5.4, 0.2), (-3.0, 4.9, 0.6), None, 80),
    "m-front-stbd": ((0.8, 5.6, -0.6), (-3.0, 5.4, 2.8), None, 75),
    "m-front-in": ((-8.5, 5.2, -0.4), (-3.0, 4.7, 0.8), None, 80),
    "m-stairwell": ((-3.4, 5.4, 4.1), (-6.5, 2.4, 3.0), None, 80),
    "m-stairwell-cabin": ((-9.4, 5.2, -0.6), (-5.0, 4.4, 3.4), None, 80),
    "m-lowerdeck-stairs": ((-10.0, 2.2, 0.2), (-4.5, 1.0, 2.8), None, 80),
    "m-hold-stair": ((-1.0, -1.0, -0.4), (-5.5, -1.8, 2.2), None, 80),
    "m-stair-quarter": ((1.4, 5.6, -1.6), (-2.6, 5.4, 2.0), None, 75),
    "m-stair-forecastle": ((10.2, 5.6, -1.0), (14.4, 5.2, 2.0), None, 75),
    "m-forecastle-front": ((10.5, 5.8, -0.4), (15.0, 5.0, 0.0), None, 80),
    "m-quarterdeck": ((-6.5, 8.4, -1.6), (-2.6, 7.2, 1.2), None, 80),
    "m-poop-front": ((-4.0, 8.4, -0.6), (-9.0, 7.8, 0.6), None, 80),
    "m-plaque": ((0.6, 5.2, 0.6), (2.1, 4.9, 0.0), None, 60),
    "m-plate": ((2.4, 5.2, -2.6), (2.4, 4.9, -0.4), None, 60),
    # 0.3.18 carrack-m: the ladder-stairs side on, the forecastle's galley (hearth, oven) and the flue's top
    "m-stair-quarter-side": ((-1.6, 5.4, -2.6), (-2.0, 4.9, 2.0), None, 75),
    "m-stair-forecastle-side": ((13.6, 5.4, -2.6), (14.0, 4.9, 2.0), None, 75),
    "m-galley": ((15.4, 5.4, -1.6), (17.3, 4.0, 2.0), None, 80),
    "m-flue-top": ((13.4, 8.8, -1.2), (17.0, 7.0, 2.0), None, 75),
    "m-companion-well": ((-6.7, 5.5, 4.1), (-4.6, 2.6, 2.9), None, 80),
    # 0.3.19 carrack-m: the castles' walked stairs (1 x 3) from the waist and side on, the poop's from the quarterdeck
    "m-stair-quarter-walk": ((1.4, 6.0, -0.9), (-1.5, 5.0, 2.2), None, 80),
    "m-stair-quarter-side19": ((-1.5, 5.8, -3.4), (-1.5, 5.0, 2.0), None, 80),
    "m-stair-poop-walk": ((-3.2, 9.2, -1.2), (-7.5, 8.0, 2.0), None, 75),
    "m-stair-poop-side19": ((-7.5, 8.8, -3.0), (-7.5, 8.0, 2.0), None, 80),
    "m-stair-forecastle-walk": ((8.6, 6.2, -1.4), (13.5, 5.0, 2.0), None, 75),
    "m-stair-forecastle-side19": ((13.5, 5.8, -3.4), (13.5, 5.0, 2.0), None, 80),
    "m-plan-main": ((2.0, 22.0, 0.0), (2.0, 3.5, 0.01), None, 95),
    "m-plan-castles": ((2.0, 24.0, 0.0), (2.0, 6.5, 0.01), None, 95),
    "m-plan-lower": ((-4.5, 11.0, 1.4), (-4.5, 0.5, 1.5), None, 55),
    "m-plan-hold": ((-4.5, 8.0, 1.4), (-4.5, -2.5, 1.5), None, 55),
    # 0.3.23 carrack-m (HANSA_MODULAR=1 python view.py m23): the castle guns run out (lids open) and housed (run back,
    # lids shut) from their decks and from outside; the castle fronts without breast beams (the third row of partitions
    # under the deck's edge); the companion and the hold's steep stairs in their 3 x 1 wells
    "m23-quarter-guns-out": ((-1.6, 8.9, 2.4), (-6.2, 7.0, -3.6), None, 75),
    "m23-quarter-guns-housed": ((-1.6, 8.9, 2.4), (-6.2, 7.0, -3.6), None, 75),
    "m23-quarter-ports-out": ((-6.0, 8.6, -11.5), (-6.0, 7.3, -3.5), 0.0, 60),
    "m23-quarter-ports-shut": ((-6.0, 8.6, -11.5), (-6.0, 7.3, -3.5), 0.0, 60),
    "m23-quarter-stbd-gun-out": ((-8.2, 8.9, -1.0), (-4.6, 7.0, 3.6), None, 75),
    "m23-poop-guns-out": ((-15.6, 11.8, -0.4), (-10.6, 10.0, 0.4), None, 85),
    "m23-poop-guns-housed": ((-15.6, 11.8, -0.4), (-10.6, 10.0, 0.4), None, 85),
    "m23-poop-ports-out": ((-11.0, 11.4, 10.5), (-11.0, 10.2, 3.0), 0.0, 60),
    "m23-forecastle-gun-out": ((15.8, 8.8, -1.6), (19.4, 7.0, 2.8), None, 75),
    "m23-forecastle-gun-housed": ((15.8, 8.8, -1.6), (19.4, 7.0, 2.8), None, 75),
    "m23-forecastle-port-out": ((19.0, 8.4, 10.0), (19.0, 7.2, 2.6), 0.0, 60),
    "m23-side-port": ((-2.0, 9.0, -16.0), (2.0, 6.5, 0.0), 0.0, 60),
    "m23-front-quarter": ((2.2, 5.4, -1.2), (-3.0, 5.9, 0.6), None, 75),
    "m23-front-quarter-in": ((-9.0, 4.9, -1.2), (-3.0, 5.9, 0.6), None, 75),
    "m23-front-quarter-beams": ((-1.2, 4.6, -0.4), (-5.0, 6.2, -0.2), None, 80),
    "m23-front-poop": ((-4.2, 8.3, -0.8), (-9.0, 8.9, 0.6), None, 75),
    "m23-front-poop-in": ((-14.5, 7.9, -1.0), (-9.0, 8.9, 0.6), None, 75),
    "m23-front-forecastle": ((10.4, 5.4, -1.0), (15.0, 5.9, 0.4), None, 75),
    "m23-front-forecastle-in": ((19.5, 4.9, -0.6), (15.0, 5.9, 0.4), None, 75),
    "m23-companion": ((-3.4, 5.6, 4.0), (-6.4, 2.0, 2.9), None, 80),
    "m23-companion-up": ((-8.0, 1.6, 3.0), (-4.0, 3.8, 3.0), None, 80),
    "m23-lowerdeck-stairs": ((-0.6, 2.3, 0.0), (-6.0, 1.0, 2.4), None, 80),
    "m23-hold-stair": ((-0.8, -0.9, -0.4), (-5.5, -1.6, 2.0), None, 80),
    "m23-stairs-cut": ((-4.5, 1.2, 10.0), (-4.5, 0.2, 2.0), None, 70),
    "m23-plan-main": ((-4.5, 14.0, 2.4), (-4.5, 3.5, 2.5), None, 50),
    "m23-plan-lower": ((-4.5, 11.0, 1.9), (-4.5, 0.5, 2.0), None, 50),
    "m23-plan-castles": ((2.0, 24.0, 0.0), (2.0, 6.5, 0.01), None, 95),
    # 0.3.24 carrack-m (HANSA_MODULAR=1 python view.py 0324): the castle decks' new edges and their beams (notes 184,
    # 185, 194), the castle guns both sides (179-183, plans with the lids open), the hatches between the beams and the
    # hold without its carlings (186, 187), the gallows' timbers (190), the pump's brake and spear (196), the heads (195),
    # the mainmast's lantern over the plaque (193), the stairs as the user laid them
    "0324-quarter-front": ((3.4, 6.2, -1.6), (-2.5, 5.4, 1.0), None, 75),
    "0324-quarter-edge-under": ((-0.6, 4.9, -2.2), (-2.6, 6.2, -3.7), None, 80),
    "0324-forecastle-front": ((8.6, 6.2, -1.6), (14.5, 5.4, 1.0), None, 75),
    "0324-forecastle-edge-under": ((12.4, 4.9, 2.0), (14.4, 6.2, 3.6), None, 80),
    "0324-quarter-guns": ((-6.0, 13.5, 0.01), (-6.0, 6.5, 0.0), None, 75),
    "0324-poop-guns": ((-13.6, 16.5, 0.01), (-13.6, 9.5, 0.0), None, 75),
    "0324-forecastle-guns": ((18.0, 13.5, 0.01), (18.0, 6.5, 0.0), None, 75),
    "0324-side-port": ((-1.0, 9.5, -19.0), (-1.0, 7.0, 0.0), 0.0, 70),
    "0324-side-stbd": ((-1.0, 9.5, 19.0), (-1.0, 7.0, 0.0), 0.0, 70),
    "0324-poop-ports-in": ((-12.0, 11.4, -1.5), (-15.0, 10.0, 3.3), None, 80),
    "0324-forecastle-ports-in": ((15.2, 8.4, -1.4), (17.8, 7.0, 3.0), None, 80),
    "0324-hatch": ((3.4, 8.6, -3.0), (7.0, 3.5, 0.0), None, 70),
    "0324-hatch-below": ((3.6, 1.6, 1.8), (7.0, 3.4, 0.0), None, 75),
    "0324-hold-beams": ((-3.5, -1.4, 0.4), (2.0, 0.3, -1.2), None, 80),
    "0324-hold-hatch": ((3.6, -1.4, 1.8), (7.0, 0.4, 0.0), None, 75),
    "0324-gallows": ((0.1, 5.3, -3.6), (3.9, 5.8, -4.2), None, 75),
    "0324-gallows-across": ((4.0, 5.6, 0.0), (4.0, 5.4, -4.2), None, 75),
    "0324-pump": ((4.9, 5.4, 1.5), (3.6, 4.5, 0.0), None, 55),
    "0324-pump-up": ((3.6, 4.7, 2.0), (3.6, 4.6, 0.0), None, 55),
    "0324-pump-down": ((3.6, 4.7, 2.0), (3.6, 4.6, 0.0), None, 55),
    "0324-heads": ((20.4, 7.6, 0.0), (22.5, 6.6, 1.1), None, 70),
    "0324-mainmast-lantern": ((-0.2, 5.4, 0.8), (2.4, 5.8, 0.0), None, 60),
    # (0.3.25, note 179) carrack-m's stern chasers: the transom from aft (their lids open, and shut), the poop's after end
    # from above (run out), and from the poop deck looking aft at them
    "0325-stern": ((-27.0, 11.0, 0.01), (-18.0, 9.6, 0.0), 0.0, 45),
    "0325-stern-shut": ((-27.0, 11.0, 0.01), (-18.0, 9.6, 0.0), 0.0, 45),
    "0325-stern-quarter": ((-24.0, 11.5, 6.5), (-18.0, 10.0, 1.0), 0.0, 55),
    "0325-poop-aft": ((-15.6, 16.5, 0.01), (-15.6, 9.5, 0.0), None, 70),
    "0325-poop-stern-in": ((-13.2, 11.5, 0.0), (-17.8, 10.0, 0.0), None, 80),
    # (0.3.26, step D1: hull panels as slots) carrack-m's hull, all panels in (it should look as 0.3.25's: re-spaced strakes,
    # snapped wales), and with panels taken out (--empty=hull:s:12:3,...): her starboard side with and without the sea, the
    # bow and stern quarters, a close look at the planking amidships, inside the hold and the lower deck at her side, her
    # section looking aft
    # (0.3.27) chalk notes 198-222: the gallows (213, 214, 220), the castle decks' edge beams (216, 217), the poop's guns
    # (203), the bow guns and their lids (205, 206), the hold's beam end aft (208), the forecastle deck's underside (218),
    # a well with a beam's length out (its carlings; 211, 222), from under it and from over it
    "0327-gallows": ((0.1, 5.3, -3.6), (3.9, 5.8, -4.2), None, 75),
    "0327-gallows-across": ((7.4, 4.6, 1.2), (4.0, 6.2, -3.4), None, 75),
    "0327-gallows-rail": ((1.8, 5.1, 4.3), (4.0, 4.9, 4.9), None, 75),
    "0327-quarter-edge-under": ((-0.6, 4.9, -2.2), (-2.6, 6.2, -3.7), None, 80),
    "0327-forecastle-edge-under": ((12.4, 4.9, 2.0), (14.4, 6.2, 3.6), None, 80),
    "0327-poop-guns": ((-13.6, 16.5, 0.01), (-13.6, 9.5, 0.0), None, 75),
    "0327-forecastle-guns": ((18.0, 13.5, 0.01), (18.0, 6.5, 0.0), None, 75),
    "0327-forecastle-lids": ((21.5, 9.0, -8.5), (18.0, 7.2, -3.0), None, 60),
    "0327-poop-side": ((-13.0, 11.0, -12.0), (-13.0, 9.5, -3.0), None, 65),
    "0327-hold-stern": ((-12.6, -2.6, 5.6), (-12.07, -0.5, 3.04), None, 70),
    "0327-forecastle-under": ((12.2, 4.7, -1.6), (15.4, 6.3, 0.4), None, 80),
    "0327-well-below": None,
    # (0.3.28) chalk notes 223-231 and step D2: the castle ports (the poop's planked over, 223; the quarterdeck's third,
    # 224), the fronts on the edge beams' lines (225, 227), the rib and beam joint with its straight knee (228, 229) and the
    # gallows' as theirs, the poop stair on the centreline with the door beside it (231), one-block panels out (halves, a
    # pair with only the rib between them), the hood over a well on the waist (230)
    "0328-poop-side": ((-12.5, 11.0, -12.0), (-12.5, 9.8, -3.0), None, 70),
    "0328-poop-in": ((-12.0, 11.3, 0.5), (-12.5, 10.0, -3.5), None, 75),
    "0328-quarter-side": ((-5.0, 8.5, -11.5), (-5.0, 7.2, -3.0), None, 65),
    "0328-quarter-in": ((-4.6, 8.3, -0.3), (-3.6, 7.0, 3.6), None, 75),
    "0328-cabin-front": ((3.0, 5.6, -3.0), (-2.0, 4.8, 0.0), None, 70),
    "0328-cabin-front-in": ((-7.0, 5.4, -1.0), (-2.0, 5.0, -2.5), None, 75),
    "0328-forecastle-front": ((9.0, 5.6, 3.0), (14.0, 4.8, 0.0), None, 70),
    "0328-knee": ((-5.4, 5.3, -2.6), (-4.0, 5.9, -4.3), None, 60),
    "0328-knee-hold": ((2.6, 0.6, -2.8), (4.0, -0.2, -4.9), None, 65),
    "0328-gallows": ((0.1, 5.3, -3.6), (3.9, 5.8, -4.2), None, 75),
    "0328-poop-stair": ((-3.6, 8.4, 0.4), (-9.0, 7.6, 0.4), None, 75),
    "0328-holes-out": ((2.0, 0.6, 11.5), (1.0, -0.8, 4.0), None, 55),
    "0328-holes-close": ((1.6, -0.3, 8.2), (1.0, -0.9, 4.5), None, 50),
    "0328-holes-hold": ((1.0, -0.6, -1.8), (1.0, -1.2, 5.0), None, 75),
    "0328-hood": None,
    "0327-well-above": None,
    "0326-side": ((4.0, 4.5, 30.0), (4.0, 3.0, 0.0), 0.0, 70),
    "0326-side-dry": ((4.0, 3.0, 30.0), (4.0, 2.0, 0.0), None, 70),
    "0326-bow-quarter": ((31.0, 5.0, 15.0), (18.0, 2.5, 2.0), None, 55),
    "0326-stern-quarter": ((-25.0, 5.0, 13.0), (-12.0, 2.5, 2.0), None, 55),
    "0326-close": ((5.0, 2.0, 10.5), (5.0, 1.0, 3.0), None, 65),
    "0326-hold-side": ((2.0, -0.9, -1.0), (6.0, -1.7, 3.6), None, 85),
    "0326-lowerdeck-side": ((0.0, 2.1, -2.2), (3.0, 1.4, 5.5), None, 85),
    "0326-section": ((15.0, 2.0, 0.0), (0.0, 2.0, 0.0), None, 70),
    # (with --empty=demo: EMPTY_DEMO's panels out) close to the holes, from outside, inside the hold and in the great cabin
    "0326-holes-out": ((3.5, 1.2, 15.0), (3.5, 0.3, 4.0), None, 62),
    "0326-holes-close": ((1.0, 0.4, 9.5), (1.0, -0.6, 4.5), None, 55),
    "0326-holes-hold": ((1.0, -1.3, -2.5), (1.5, -1.0, 4.5), None, 80),
    "0326-holes-lowerdeck": ((-2.0, 2.1, -2.0), (0.5, 1.6, 5.0), None, 80),
    "0326-holes-cabin": ((-11.0, 4.8, -2.4), (-11.0, 4.2, 4.0), None, 75),
}
# (0.3.23) the views with the castle guns' lids opened (run out), and with them run back (housed, the lids shut)
M23_LIDS_OPEN = {"m23-quarter-guns-out", "m23-quarter-ports-out", "m23-quarter-stbd-gun-out", "m23-poop-guns-out",
                 "m23-poop-ports-out", "m23-forecastle-gun-out", "m23-forecastle-port-out", "m23-side-port"}
M24_LIDS_OPEN = {"0324-quarter-guns", "0324-poop-guns", "0324-forecastle-guns", "0324-side-port", "0324-side-stbd",
                 "0324-poop-ports-in", "0324-forecastle-ports-in"}
M24_LIDS_OPEN |= {"0325-stern", "0325-stern-quarter", "0325-poop-aft", "0325-poop-stern-in"}
M24_LIDS_OPEN |= {"0327-poop-guns", "0327-forecastle-guns", "0327-forecastle-lids", "0327-poop-side"}
M24_PUMP = {"0324-pump-up": 1.0, "0324-pump-down": -1.0}
M23_HOUSED = {"m23-quarter-guns-housed", "m23-quarter-ports-shut", "m23-poop-guns-housed", "m23-forecastle-gun-housed"}
OPEN_0312 = {"0313-holdladders": ("hatch-waist-s", "hatch-waist-p"),
             "0312-holdladder": ("hatch-hold-s", "hatch-hold-p", "hatch-waist-s", "hatch-waist-p"),
             "0312-holdladder-waist": ("hatch-waist-s", "hatch-waist-p", "hatch-hold-s", "hatch-hold-p")}


# views that show moving parts turned all the way (their "angle"): the gates and hatches open
OPEN_IN = {"gate-open": ("gate-starboard", "gate-port", "hatch-waist-s", "hatch-waist-p"), "pump-stroke": ("pump-brake",),
           "waist-hatches-open": ("hatch-waist-s", "hatch-waist-p", "hatch-hold-s", "hatch-hold-p"),
           "lowerdeck-ladders": ("hatch-hold-s", "hatch-hold-p"), "lowerdeck-landing": ("hatch-hold-s", "hatch-hold-p"),
           "ladders-cutaway": ("hatch-waist-p", "hatch-hold-p"),
           "grid-gates-open": ("gate-starboard", "gate-port"), "grid-gate-out-open": ("gate-starboard", "gate-port"),
           **OPEN_0312}
# views of her cut open: only what's on this side of the cut is drawn (the hull's starboard half taken away to show
# the companionway's and the hold's ladders and the landing between them from the side)
CUTS = {"ladders-cutaway": lambda c: c[2] < 0.55 and c[1] < 5.2 and -1.5 < c[0] < 10.5,
        # (0.3.13) plans of the decks round the companion: everything over the deck's height cut away
        "0313-plan-main": lambda c: c[1] < 4.7,
        "0313-plan-lower": lambda c: c[1] < 1.7,
        "0313-plan-hold": lambda c: c[1] < -1.3,
        # (0.3.16) carrack-m's decks cut a little over each (the castle decks', the castles' fronts and walls under them)
        "m-plan-main": lambda c: c[1] < 5.2 and not (c[1] > 5.0 and c[1] < 6.6 and abs(c[2]) > 3.6),
        "m-plan-castles": lambda c: c[1] < 8.2,
        "m-plan-lower": lambda c: c[1] < 1.7,
        "m-plan-hold": lambda c: c[1] < -1.3,
        # (0.3.23) the companion and the hold stair side on (her starboard side and what's over the main deck cut away),
        # and the plans
        "m23-stairs-cut": lambda c: c[2] < 3.6 and c[1] < 5.2 and -10.0 < c[0] < 1.0,
        "m23-plan-main": lambda c: c[1] < 4.7,
        "m23-plan-lower": lambda c: c[1] < 1.7,
        "m23-plan-castles": lambda c: c[1] < 8.2,
        "0324-quarter-guns": lambda c: c[1] < 8.2,
        "0324-forecastle-guns": lambda c: c[1] < 8.2,
        "0324-poop-guns": lambda c: c[1] < 11.2,
        "0325-poop-aft": lambda c: c[1] < 11.2,
        # (0.3.9) her cross-sections looking aft: cut at x = -0.9 (aft of the mainmast), and at -10.5 (the cabins)
        "grid-section": lambda c: c[0] < -0.9 and c[1] < 11.0,
        # (0.3.26) her section through a frame space amidships, looking aft
        "0326-section": lambda c: c[0] < 4.9 and c[1] < 8.0,
        "grid-section-aft": lambda c: c[0] < -10.5 and c[1] < 11.0}
# views with the guns fired: every part moved by "fire:..." at its full value (run back, the trucks turned, the
# breeching stretched)
FIRED = {"gun-recoil", "guns-port-recoil"}


def _rot(axis, deg):
    a = np.asarray(axis, float)
    a /= np.linalg.norm(a)
    t = math.radians(deg)
    K = np.array([[0, -a[2], a[1]], [a[2], 0, -a[0]], [-a[1], a[0], 0]])
    return np.eye(3) + math.sin(t) * K + (1 - math.cos(t)) * K @ K


def fire_guns(d):
    """Move every part a gun's firing moves to its full value (as the mod does, ShipRenderer.ComputeParts): the gun slid
    back, its trucks turned on it (their parent), its breeching stretched from the ring bolts to the moved cascabel."""
    pose_parts(d, {p["source"]: 1.0 for p in d.moving_parts if str(p.get("source", "")).startswith("fire:")})


def pose_parts(d, values):
    """
    Move the moving parts by the values of what moves them ({source: value}; the rest stay at 0), composed as the mod
    composes them (PartMotion.Place: each on its parent, parents first): a gun trained ("train:<gun>", degrees), run back
    ("fire:<gun>", 0..1) and elevated ("elevate:<gun>", degrees), its trucks, quoin and breeching with it.
    """
    mats = {}
    for p in d.moving_parts:
        if p.get("kind") == "follow":
            continue
        v = float(values.get(p.get("source", ""), 0.0))
        M = np.eye(4)
        if p.get("kind") == "slide":
            M[:3, 3] = np.asarray(p["axisDir"], float) * p.get("distance", 0.0) * v
        elif v:
            c = np.asarray(p["axisPoint"], float)
            R = _rot(p["axisDir"], p.get("angle", 30.0) * v)
            M[:3, :3] = R
            M[:3, 3] = c - R @ c
        if p.get("parent") in mats:
            M = mats[p["parent"]] @ M
        if v or p.get("parent") in mats:
            mats[p["code"]] = M
    for p in d.moving_parts:
        if p.get("kind") == "follow" and (p.get("follow") in mats or p.get("anchorFollow") in mats):
            a, tip = np.asarray(p["anchor"], float), np.asarray(p["tip"], float)
            F = mats.get(p["follow"], np.eye(4))
            nt = F[:3, :3] @ tip + F[:3, 3]
            # (its other end on another part: a gun's breeching from the cheek's eyebolt, PartMotion.FollowMatrix)
            A = mats.get(p.get("anchorFollow"), np.eye(4))
            b = A[:3, :3] @ a + A[:3, 3]
            u0, u1 = tip - a, nt - b
            l0, l1 = np.linalg.norm(u0), np.linalg.norm(u1)
            u0, u1 = u0 / l0, u1 / l1
            v = np.cross(u0, u1)
            c = float(np.dot(u0, u1))
            K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
            R = np.eye(3) + K + K @ K / (1 + c)
            L = R @ (np.eye(3) + (l1 / l0 - 1) * np.outer(u0, u0))
            M = np.eye(4)
            M[:3, :3] = L
            M[:3, 3] = b - L @ a
            mats[p["code"]] = M
    for q in d.quads:
        g = q["group"]
        if g.startswith("part:") and g[5:] in mats:
            M = mats[g[5:]]
            q["pts"] = [tuple(M[:3, :3] @ np.asarray(pt) + M[:3, 3]) for pt in q["pts"]]


# the guns laid (0.3.6): one gun at the corners of its lay range, run out and run back, from her deck and from outside,
# on contact sheets; and the view of the gun captain behind its breech
LAY_GUN = "gun-starboard-2"
LAY_SHEETS = {
    # name: eye, target, fov
    "gun-lay-deck": ((3.0, 5.7, 1.6), (4.7, 3.85, 5.0), 72),
    "gun-lay-out": ((5.6, 4.7, 9.8), (4.4, 4.0, 5.2), 55),
}
GUN_CAPTAIN = ((4.375, 5.1, 2.33), (4.375, 4.15, 14.0), 75)


def lay_values(gun, train, elev, recoil):
    return {f"train:{gun}": train, f"elevate:{gun}": elev, f"fire:{gun}": recoil}


def gun_sheets(d, boxes, names, out):
    """The laid gun's contact sheets (4 lays x run out / run back) and the gun captain's view, into docs/views/."""
    from PIL import Image, ImageDraw
    import carrack
    f = next(x for x in d.fixtures if x.get("code") == LAY_GUN)
    t0, t1 = f["trainLimits"]
    e0, e1 = f["elevationLimits"]
    lays = [(f"trained {t1:g} deg to the bow (full left)", t1, 0.0), (f"trained {-t0:g} deg to the stern (full right)", t0, 0.0),
            (f"elevated {e1:g} deg (fully up)", 0.0, e1), (f"depressed {-e0:g} deg (fully down)", 0.0, e0)]
    saved = [list(x["pts"]) for x in d.quads]

    def posed(values):
        pose_parts(d, values)
        q = scene(d, boxes)
        for x, pts in zip(d.quads, saved):
            x["pts"] = pts
        return q

    for name in names:
        if name in LAY_SHEETS:
            eye, target, fov = LAY_SHEETS[name]
            w, h = 480, 320
            sheet = Image.new("RGB", (w * 4, h * 2 + 24), (255, 255, 255))
            draw = ImageDraw.Draw(sheet)
            for col, (label, t, e) in enumerate(lays):
                for row, back in enumerate((0.0, 1.0)):
                    img = render(posed(lay_values(LAY_GUN, t, e, back)), eye, target, size=(w, h), fov=fov)
                    sheet.paste(img, (col * w, 24 + row * h))
                draw.text((col * w + 6, 6), label + "; top: run out, bottom: run back", fill=(0, 0, 0))
            path = os.path.join(out, f"{d.code}-{name}.png")
            sheet.save(path)
            print(path)
        elif name == "gun-captain":
            eye, target, fov = GUN_CAPTAIN
            img = render(posed(lay_values(LAY_GUN, 0.0, 1.0, 0.0)), eye, target, fov=fov, water=carrack.WATER)
            path = os.path.join(out, f"{d.code}-{name}.png")
            img.save(path)
            print(path)


# ---- what the mod draws aboard from the game's own models, drawn here too (in place of the previews' plain marker boxes):
# Hansa's rowing boat on each place on the gallows, the vanilla firepit in the hearth

FIREPIT = os.path.join(os.environ.get("APPDATA") or os.path.expanduser("~"), "Vintagestory", "assets", "survival", "shapes",
                       "block", "wood", "firepit", "cold-normal.json")
FIREPIT_COLOURS = {"ashes": (72, 66, 60), "birch": (205, 190, 165), "walnut-h": (92, 70, 52), "log": (110, 80, 55),
                   "charred": (40, 34, 30), "aged": (120, 112, 100), "tinder": (190, 170, 90), "fire": (255, 150, 40),
                   "ember": (230, 80, 20), "ceramic": (170, 95, 70)}
# the working hearth (0.3.6): the lit firepit with a clay pot on it, as ShipRenderer.HearthContent places vanilla's
# PotInFirepitRenderer's pot (1/16 up) and lid (13/32 up) in the firepit block's space
POT_DIR = os.path.join(os.path.dirname(FIREPIT), "..", "..", "clay")
POT_COLOURS = {"ceramic": (176, 92, 64), "dark": (120, 60, 42), "lid": (150, 80, 56)}


def _vanilla_shape(path):
    """A shape file of the game's (comments allowed), its disabled and #null faces dropped."""
    import json
    import re
    with open(path, encoding="utf-8") as fh:
        text = re.sub(r"//.*", "", fh.read())
    shape = json.loads(text)

    def clean(els):
        for el in els:
            el["faces"] = {k: v for k, v in (el.get("faces") or {}).items()
                           if v.get("enabled", True) is not False and v.get("texture") not in (None, "#null")}
            clean(el.get("children") or [])
    clean(shape["elements"])
    return shape


def _place(pos, yaw_deg, scale, offset):
    """T(pos) . RotateY(yaw) . Scale . T(offset), as the mod builds a vanilla mesh's matrix (ShipRenderer)."""
    import vsshape as vs
    m = vs._trans(pos) @ vs._rot("y", yaw_deg) @ np.diag([scale, scale, scale, 1.0]) @ vs._trans(offset)
    return m


def vanilla_quads(d, firepit="cold-normal"):
    """The rowing boats on the gallows (as ShipRenderer.BoatMesh lays the item's model on its place) and the firepit in
    the hearth (the block centred on its place, its bottom on it; "lit-wide": burning with a clay pot on it)."""
    import vsshape as vs
    import carrack
    import rowboat
    out = []
    boat_path = os.path.join(carrack.ASSETS, "shapes", "entity", "rowboat-stowed.json")
    if os.path.exists(boat_path):
        boat = vs.load(boat_path)
        lo, hi = vs.bounds(boat, skip=("hideWater",))
        ex, ez = hi[0] - lo[0], hi[2] - lo[2]
        turn = ez > ex
        length, width = max(ex, ez), max(1e-3, min(ex, ez))
        for f in d.fixtures:
            if f.get("kind") != "boat":
                continue
            b = f["box"]
            s = min(1.0, (b[3] - b[0]) / length, (b[5] - b[2]) / width)
            m = _place(f["pos"], f.get("yaw", 0.0) + (90.0 if turn else 0.0), s,
                       (-(lo[0] + hi[0]) / 2, -lo[1], -(lo[2] + hi[2]) / 2))
            out += vs.quads(boat, rowboat.COLOURS, skip=("hideWater",), model=m)
    pit_path = FIREPIT.replace("cold-normal", firepit)
    if os.path.exists(pit_path):
        pit = _vanilla_shape(pit_path)
        for f in d.fixtures:
            if f.get("type") == "hearth" and f.get("block"):
                out += vs.quads(pit, FIREPIT_COLOURS, model=_place(f["pos"], f.get("yaw", 0.0), 1.0, (-0.5, 0.0, -0.5)))
                if firepit.endswith("wide"):
                    for part, dy in (("pot-opened-empty.json", 0.0625), ("pot-part-lid.json", 13 / 32)):
                        path = os.path.join(POT_DIR, part)
                        if os.path.exists(path):
                            out += vs.quads(_vanilla_shape(path), POT_COLOURS,
                                            model=_place(f["pos"], f.get("yaw", 0.0), 1.0, (-0.5, dy, -0.5)))
    return out


def _box_quads(lo, hi, col):
    lo, hi = np.asarray(lo, float), np.asarray(hi, float)
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    out = []
    for n, pts in (((1, 0, 0), [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)]),
                   ((-1, 0, 0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)]),
                   ((0, 1, 0), [(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)]),
                   ((0, -1, 0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)]),
                   ((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]),
                   ((0, 0, -1), [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)])):
        out.append((np.array(pts, float), np.array(n, float), col))
    return out


MOD_COLOURS = {"partition": (178, 140, 92), "window": (150, 112, 70), "glass": (175, 205, 225), "door": (128, 86, 50),
               "railing": (150, 110, 66), "stair": (168, 128, 80), "brick": (168, 92, 70), "clay": (176, 128, 96),
               "iron": (70, 70, 75)}


def _beam_quads(a, b, w, h, col):
    """(0.3.18) A timber from a to b (its middle line), w across (along z), h deep (square to it in the x-y plane)."""
    a, b = np.asarray(a, float), np.asarray(b, float)
    u = (b - a) / np.linalg.norm(b - a)
    n = np.array([-u[1], u[0], 0.0])                    # square to it, in the x-y plane
    zv = np.array([0.0, 0.0, 1.0])
    c = [a + n * dh + zv * dw for dh, dw in ((-h / 2, -w / 2), (h / 2, -w / 2), (h / 2, w / 2), (-h / 2, w / 2))]
    e = [p + (b - a) for p in c]
    out = []
    for i in range(4):
        j = (i + 1) % 4
        pts = np.array([c[i], e[i], e[j], c[j]])
        nn = np.cross(pts[1] - pts[0], pts[3] - pts[0])
        nn /= np.linalg.norm(nn)
        if np.dot(nn, pts.mean(axis=0) - (a + b) / 2) < 0:
            pts, nn = pts[::-1], -nn
        out.append((pts, nn, col))
    for ring, nn in ((c, -u), (e, u)):
        out.append((np.array(ring, float), nn, col))
    return out


def ladder_of(e):
    """(0.3.18, docs/modular-pieces.md 7.1) A stair piece's ladder along x: (sg, x_low, (xf, yf), (xt, yt))."""
    import carrack as C
    x, y, z = e["at"]
    sg = 1 if e["up"] == "+x" else -1
    x_low = x - 0.5 * sg
    (xf, yf), (xt, yt) = C.ladder_line(x_low, sg, y, e.get("rise", 3.0))
    return sg, x_low, (xf, yf), (xt, yt)


def modular_quads(d, cut=None):
    """(0.3.16) carrack-m's laid pieces as the mod draws them, roughly (stand-ins for the views: the pieces are vanilla
    blocks the mod fits on their places, not in her meshes): partitions, windows, doors on their grid lines 1/8 thick, cut
    to her leaning side; railings (raked beside a stair); the 2 x 1 stairs' treads and stringers."""
    import carrack as C
    out = []
    pieces = getattr(d, "modular", None) or []
    stairs = [e for e in pieces if e["kind"] == "stair"]
    h = 0.0625

    def wall(e, rows, col):
        x, y, z = e["at"]
        alx = e["along"] == "x"
        line, mid = (z, x) if alx else (x, z)
        res = []
        for r in range(rows):
            for k in range(4):
                ya, yb = y + r + k * 0.25, y + r + (k + 1) * 0.25
                a, b = mid - 0.5, mid + 0.5
                if not alx:
                    c = C.ceil_z(line, (ya + yb) / 2) - 0.02
                    a, b = max(a, -c), min(b, c)
                    if b - a < 0.02:
                        continue
                lo, hi = ((a, ya, line - h), (b, yb, line + h)) if alx else ((line - h, ya, a), (line + h, yb, b))
                res += _box_quads(lo, hi, col)
        return res

    def rake(e):
        """The handrail's rise at the segment's two ends if it's beside a stair on the stair's deck (else None).
        (0.3.19, §8) Beside a walked stair (1 x 3): its nosing line, 0 at its foot cell's near edge, the rise at its head
        cell's far edge (ShipPieces.FlightHeight); none beside a ladder."""
        x, y, z = e["at"]
        for s in stairs:
            sx, sy, sz = s["at"]
            if abs(sy - y) > 0.01 or e["along"] != "x" or abs(abs(z - sz) - 0.5) > 0.01:
                continue
            sg = 1 if s["up"] == "+x" else -1
            start, rise = sx - 0.5 * sg, s.get("rise", 3.0)
            run = float(s.get("length", 3))
            u0, u1 = sg * (x - 0.5 - start), sg * (x + 0.5 - start)
            if min(u0, u1) < -0.01 or max(u0, u1) > run + 0.01:
                continue
            H = lambda xx: rise * min(1.0, max(0.0, sg * (xx - start) / run))      # noqa: E731
            return (x - 0.5, H(x - 0.5)), (x + 0.5, H(x + 0.5))
        return None

    for e in pieces:
        k = e["kind"]
        if k == "partition":
            out += wall(e, 1, MOD_COLOURS["partition"])
        elif k == "door":
            out += wall(e, 2, MOD_COLOURS["door"])
        elif k == "panel":
            # ((0.3.28, note 230) a plank slab lying under its plane, a cell square: a shelf, a hood's roof)
            x, y, z = e["at"]
            out += _box_quads((x - 0.5, y - 0.125, z - 0.5), (x + 0.5, y, z + 0.5), MOD_COLOURS["partition"])
        elif k == "window":
            x, y, z = e["at"]
            alx = e["along"] == "x"
            line, mid = (z, x) if alx else (x, z)

            def bx(a, b, ya, yb, t, col):
                lo, hi = ((a, ya, line - t), (b, yb, line + t)) if alx else ((line - t, ya, a), (line + t, yb, b))
                return _box_quads(lo, hi, col)
            cw = MOD_COLOURS["window"]
            out += bx(mid - 0.5, mid + 0.5, y, y + 0.1, h, cw) + bx(mid - 0.5, mid + 0.5, y + 0.9, y + 1.0, h, cw)
            out += bx(mid - 0.5, mid - 0.4, y + 0.1, y + 0.9, h, cw) + bx(mid + 0.4, mid + 0.5, y + 0.1, y + 0.9, h, cw)
            out += bx(mid - 0.4, mid + 0.4, y + 0.1, y + 0.9, 0.01, MOD_COLOURS["glass"])
            for t in (-0.24, -0.08, 0.08, 0.24):
                out += bx(mid + t - 0.008, mid + t + 0.008, y + 0.1, y + 0.9, 0.015, (90, 92, 96))
                out += bx(mid - 0.4, mid + 0.4, y + 0.5 + t - 0.008, y + 0.5 + t + 0.008, 0.015, (90, 92, 96))
        elif k == "railing":
            x, y, z = e["at"]
            alx = e["along"] == "x"
            line, mid = (z, x) if alx else (x, z)
            rk = rake(e)
            col = MOD_COLOURS["railing"]

            def off(p):
                if rk is None:
                    return 0.0
                (pa, ha), (pb, hb) = rk
                return ha + (hb - ha) * (p - pa) / (pb - pa)
            a, b = mid - 0.5, mid + 0.5
            if not alx:
                c = C.ceil_z(line, y + 0.5) - 0.02
                a, b = max(a, -c), min(b, c)
            n = 6
            for i in range(n + 1):
                p = a + (b - a) * i / n
                w = 0.06 if 0 < i < n else 0.1
                y0 = y + off(p)
                lo, hi = ((p - w / 2, y0, line - w / 2), (p + w / 2, y0 + 0.95, line + w / 2)) if alx else \
                    ((line - w / 2, y0, p - w / 2), (line + w / 2, y0 + 0.95, p + w / 2))
                out += _box_quads(lo, hi, col)
            # ((0.3.19) beside a walked stair the handrail 0.9 over its nosing line)
            for yy, t in (((0.83 if rk else 0.88), 0.07), (0.06, 0.05)):
                for i in range(4):
                    pa, pb = a + (b - a) * i / 4, a + (b - a) * (i + 1) / 4
                    ya = y + yy + off((pa + pb) / 2)
                    lo, hi = ((pa, ya, line - t), (pb, ya + 0.07, line + t)) if alx else \
                        ((line - t, ya, pa), (line + t, ya + 0.07, pb))
                    out += _box_quads(lo, hi, col)
        elif k == "stair":
            # (0.3.19, §8) the walked stair: two stringers 1/16 in from the cell's sides under the nosing line from its
            # foot cell's near edge to its head cell's far edge at the deck above, a voxel-thick tread every riser
            x, y, z = e["at"]
            sg = 1 if e["up"] == "+x" else -1
            start, rise = x - 0.5 * sg, e.get("rise", 3.0)
            run = float(e.get("length", 3))         # ((0.3.23) 3: the long stair, 2: the steep one)
            col = MOD_COLOURS["stair"]
            for zs in (z - 0.5 + h + 0.035, z + 0.5 - h - 0.035):
                a = np.array([start, y + 0.02, zs])
                b = np.array([start + sg * run, y + rise - 0.3, zs])
                out += _beam_quads(a, b, 0.07, 0.3, (120, 88, 55))
                # ((0.3.23) its handrails built in (shipstair-long / -steep): a post at its foot, middle and head each
                # side, a rail 0.9 over the nosings' line)
                for f_ in (0.0, 0.5, 1.0):
                    px, py = start + sg * run * f_, y + rise * f_
                    out += _box_quads((px - 0.03, py, zs - 0.03), (px + 0.03, py + 0.95, zs + 0.03), col)
                out += _beam_quads(np.array([start, y + 0.9, zs]), np.array([start + sg * run, y + rise + 0.9, zs]),
                                   0.06, 0.06, col)
            for u0, u1, top in C.flight_treads(rise, run):
                xa, xb = sorted((start + sg * u0, start + sg * u1))
                out += _box_quads((xa, y + top - 0.06, z - 0.5 + h + 0.07), (xb, y + top, z + 0.5 - h - 0.07), col)
                # (the riser board under its nosing)
                xr_ = xa if sg > 0 else xb
                out += _box_quads((xr_ - 0.015, y + top - 0.25, z - 0.5 + h + 0.07), (xr_ + 0.015, y + top - 0.06,
                                                                                     z + 0.5 - h - 0.07), col)
        elif k == "ladder":
            # (0.3.18, 7.1) the companion ladder-stair: two stringers 1/16 in from the cell's sides from its feet (the foot
            # cell's middle) to the head cell's far edge at the deck above, flat treads every rise / 10,
            # iron feet, iron hooks over the coaming
            x, y, z = e["at"]
            sg, start, (xf, yf), (xt, yt) = ladder_of(e)
            col, iron = MOD_COLOURS["stair"], MOD_COLOURS["iron"]
            ux, uy = (xt - xf), (yt - yf)
            ln = math.hypot(ux, uy)
            nx, ny = -uy / ln * sg, ux / ln * sg        # square to it, toward its upper (tread) side
            for zs in (z - 0.5 + h + 0.035, z + 0.5 - h - 0.035):
                a = np.array([xf - nx * 0.08, yf - ny * 0.08, zs])
                b = np.array([xt - nx * 0.08, yt - ny * 0.08, zs])
                out += _beam_quads(a, b, 0.07, 0.16, (120, 88, 55))
                out += _box_quads((xf - 0.07, yf, zs - 0.05), (xf + 0.07, yf + 0.04, zs + 0.05), iron)
                hx = xt + 0.08 * sg
                out += _box_quads((min(xt, hx) - 0.02, yt - 0.02, zs - 0.03), (max(xt, hx) + 0.02, yt + 0.03, zs + 0.03), iron)
                # ((0.3.19, §8) its own handrails, no collision: a stanchion at its foot, middle and head (on the
                # coaming) each side, a rail 0.9 over its line)
                for f_ in (0.0, 0.5, 1.0):
                    px, py = xf + ux * f_, yf + uy * f_
                    out += _box_quads((px - 0.025, py, zs - 0.025), (px + 0.025, py + 0.9, zs + 0.025), col)
                out += _beam_quads(np.array([xf, yf + 0.9, zs]), np.array([xt, yt + 0.9, zs]), 0.05, 0.05, col)
            for i in range(1, 10):
                f = i / 10
                xm, ym = xf + ux * f, yf + uy * f
                out += _box_quads((xm - 0.1, ym - 0.04, z - 0.5 + h + 0.07), (xm + 0.1, ym, z + 0.5 - h - 0.07), col)
        elif k in ("hearth", "oven", "flue"):
            out += _galley_quads(e, pieces)
    if cut is not None:
        out = [q for q in out if cut(q[0].mean(axis=0))]
    return out


def _galley_quads(e, pieces):
    """(0.3.18, 7.2) Stand-ins for the hearth (a brick firebed filling its cell, 0.25 high, a kerb 0.5 high on its three
    sides away from `facing`, a firepit's stones and fire on it), the oven (a clay block, its mouth to `facing`) and a
    flue segment (a brick chute 0.5 square from a block over the deck it rises from, up through the deck above to a
    block over it)."""
    import carrack as C
    x, y, z = e["at"]
    out = []
    brick = MOD_COLOURS["brick"]
    if e["kind"] == "hearth":
        out += _box_quads((x - 0.5, y, z - 0.5), (x + 0.5, y + 0.25, z + 0.5), brick)
        f = e.get("facing", "-z")
        for side in ("+x", "-x", "+z", "-z"):
            if side == f:
                continue
            sgn = 1 if side[0] == "+" else -1
            if side[1] == "x":
                a = x + sgn * 0.4375
                out += _box_quads((a - 0.0625, y + 0.25, z - 0.5), (a + 0.0625, y + 0.5, z + 0.5), brick)
            else:
                a = z + sgn * 0.4375
                out += _box_quads((x - 0.5, y + 0.25, a - 0.0625), (x + 0.5, y + 0.5, a + 0.0625), brick)
        out += _box_quads((x - 0.25, y + 0.25, z - 0.25), (x + 0.25, y + 0.4, z + 0.25), (60, 52, 46))
        out += _box_quads((x - 0.12, y + 0.4, z - 0.12), (x + 0.12, y + 0.6, z + 0.12), (230, 150, 60))
    elif e["kind"] == "oven":
        out += _box_quads((x - 0.45, y, z - 0.45), (x + 0.45, y + 0.9, z + 0.45), MOD_COLOURS["clay"])
        f = e.get("facing", "-z")
        sgn = 1 if f[0] == "+" else -1
        if f[1] == "x":
            out += _box_quads((x + sgn * 0.45 - 0.01, y + 0.15, z - 0.2), (x + sgn * 0.45 + 0.01, y + 0.5, z + 0.2), (40, 30, 26))
        else:
            out += _box_quads((x - 0.2, y + 0.15, z + sgn * 0.45 - 0.01), (x + 0.2, y + 0.5, z + sgn * 0.45 + 0.01), (40, 30, 26))
    else:
        y0 = y + 1.0                # ((0.3.18, §7.2 as amended) the first from a block over the hearth's deck: the hood)
        above = min((t for t in (C.LOWER, C.MAIN, C.QUARTER, C.POOP) if t > y + 1.0), default=y + 3.0)
        out += _box_quads((x - 0.25, y0, z - 0.25), (x + 0.25, above + 1.0, z + 0.25), brick)
        out += _box_quads((x - 0.3, above + 0.9, z - 0.3), (x + 0.3, above + 1.0, z + 0.3), brick)
    return out


def scene(d, boxes):
    """Everything drawn of her as she is now (moving parts where they are), the game's models aboard with it ((0.3.16)
    and carrack-m's laid pieces, as stand-ins)."""
    # ((0.3.28, D2.4) a port planked over (its cover in): no lid
    slots_ = d.props.get("deckSlots") or []
    st_ = getattr(d, "slot_states", None) or [x.get("start", "filled") != "empty" for x in slots_]
    covered = tuple(f"part:{x['gun']}" for i, x in enumerate(slots_) if x.get("kind") == "port" and st_[i])
    saved = d.quads
    if covered:
        # (and no gun: none can be mounted there)
        d.quads = [x for x in saved if not x["group"].startswith(covered)]
    try:
        return d._all_quads(boxes, {}, skip_markers=("boat",)) + vanilla_quads(d) + modular_quads(d)
    finally:
        d.quads = saved


def turn_part(d, code):
    """Turn a moving part's mesh quads by its full angle about its axis (in place)."""
    part = next(p for p in d.moving_parts if p["code"] == code)
    c = np.asarray(part["axisPoint"], float)
    a = np.asarray(part["axisDir"], float)
    a /= np.linalg.norm(a)
    t = math.radians(part["angle"])
    K = np.array([[0, -a[2], a[1]], [a[2], 0, -a[0]], [-a[1], a[0], 0]])
    R = np.eye(3) + math.sin(t) * K + (1 - math.cos(t)) * K @ K
    for q in d.quads:
        if q["group"] == f"part:{code}" or q["group"] == f"glass:{code}":
            q["pts"] = [tuple(c + R @ (np.asarray(p) - c)) for p in q["pts"]]


# ---- the surveyor's chalk's notes (python view.py notes [path]): each note's ship-local point on the carrack, marked and
# numbered, seen from where the player stood (or from in front of the spot), and an overview; into docs/views/notes/

NOTE_COLOUR = (255, 0, 200)        # the marker drawn in the views: nothing of the design is this colour


def default_notes_path():
    """Where the game writes them: VintagestoryData/ModData/ships-notes.json."""
    base = os.environ.get("APPDATA") or os.path.expanduser("~")
    return os.path.join(base, "VintagestoryData", "ModData", "ships-notes.json")


def _to_local(yaw, pitch, roll, d):
    """A world direction into a ship's frame (ShipFrame.ToLocal): yaw is vanilla's, pitch and roll her drawn tilt (radians)."""
    s, c = math.sin(yaw), math.cos(yaw)
    x2, y2, z1 = d[0] * s + d[2] * c, d[1], -d[0] * c + d[2] * s
    cr, sr, cp, sp = math.cos(roll), math.sin(roll), math.cos(pitch), math.sin(pitch)
    lx = x2 * cp + y2 * sp
    y1 = -x2 * sp + y2 * cp
    return np.array([lx, y1 * cr + z1 * sr, -y1 * sr + z1 * cr])


def _note_eye(note, mark, point, normal, water):
    """
    Where to look at a note's spot from (in the drawing's frame): the player's eye when they were aboard her, else their
    eye brought into her frame from the world (through the mark's world point and her heading, pitch and roll), else a
    little out from the spot along its surface's normal. Kept between 1.5 and 8 blocks from the spot.
    """
    eye = None
    aboard = (note.get("player") or {}).get("aboard") or {}
    if aboard.get("shipId") == mark.get("shipId") and aboard.get("eyeLocal"):
        eye = np.asarray(aboard["eyeLocal"], float) + [0, water, 0]
    elif (note.get("player") or {}).get("eye") and mark.get("world"):
        ship = next((s for s in note.get("ships") or [] if s.get("id") == mark.get("shipId")), None)
        if ship is not None and ship.get("heading") is not None:
            yaw = math.pi - math.radians(ship["heading"])           # vanilla's yaw from a compass heading
            d = np.asarray(note["player"]["eye"], float) - np.asarray(mark["world"], float)
            eye = point + _to_local(yaw, math.radians(ship.get("pitch", 0.0)), math.radians(ship.get("roll", 0.0)), d)
    if eye is None:
        eye = point + normal * 3.5 + np.array([0.0, 1.2 if abs(normal[1]) < 0.7 else 0.0, 0.0]) + \
            (np.array([-2.0, 0.0, 1.5]) if abs(normal[1]) >= 0.7 else 0.0)
    v = eye - point
    dist = float(np.linalg.norm(v))
    if dist > 8.0:
        eye = point + v / dist * 8.0
    elif dist < 1.5:
        eye = point + (v / dist if dist > 1e-6 else normal) * 1.5
    return eye


def _marker_quads(point, normal, size=0.45):
    """A chalk X across the surface at the spot and a short pin out of it along the normal, in NOTE_COLOUR."""
    n = np.asarray(normal, float)
    n = n / (np.linalg.norm(n) or 1.0)
    ref = np.array([0.0, 1.0, 0.0]) if abs(n[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
    t1 = np.cross(ref, n)
    t1 /= np.linalg.norm(t1)
    t2 = np.cross(n, t1)
    c = point + n * 0.03
    quads = []
    w = size * 0.12
    for a in ((t1 + t2) / math.sqrt(2), (t1 - t2) / math.sqrt(2)):
        b = np.cross(n, a)
        h = a * size / 2
        quads.append((np.array([c - h - b * w, c + h - b * w, c + h + b * w, c - h + b * w]), n, NOTE_COLOUR))
    # the pin, two crossed strips so it shows from any side
    for b in (t1, t2):
        quads.append((np.array([c - b * 0.03, c + b * 0.03, c + b * 0.03 + n * 0.6, c - b * 0.03 + n * 0.6]), np.cross(n, b), NOTE_COLOUR))
    return quads


def _project(p, eye, target, size, fov):
    """Where a point lands in a view made by render() (pixels) and how far along the view it is, or None behind the eye."""
    W, H = size
    e, R = look_at(eye, target)
    cam = (np.asarray(p, float) - e) @ R.T
    if cam[2] < 0.05:
        return None
    focal = 0.5 * W / math.tan(math.radians(fov) / 2)
    return W / 2 + focal * cam[0] / cam[2], H / 2 - focal * cam[1] / cam[2], cam[2]


def _font(size):
    from PIL import ImageFont
    try:
        return ImageFont.load_default(size=size)
    except TypeError:          # an older Pillow: one size
        return ImageFont.load_default()


def _label(img, at, text, zbuf=None, big=True, placed=None):
    """
    A number in a dark box with a ring round the spot; grey and "(hidden)" when something of her is in front of it
    (the depth buffer nearer than the spot by more than a little, round the spot).
    """
    from PIL import ImageDraw
    if at is None:
        return
    x, y, z = at
    hidden = False
    if zbuf is not None:
        H, W = zbuf.shape
        i, j = int(round(y)), int(round(x))
        if 0 <= i < H and 0 <= j < W:
            near = zbuf[max(0, i - 1):i + 2, max(0, j - 1):j + 2]
            hidden = bool(np.max(near) < z - 0.25)
    draw = ImageDraw.Draw(img)
    font = _font(22 if big else 16)
    r = 13 if big else 9
    ring = (150, 150, 150) if hidden else (255, 255, 255)
    draw.ellipse([x - r, y - r, x + r, y + r], outline=ring, width=3)
    draw.ellipse([x - r - 2, y - r - 2, x + r + 2, y + r + 2], outline=(0, 0, 0), width=1)
    text = text + (" (hidden)" if hidden else "")
    tx, ty = x + r + 4, y - r - 22
    bb = draw.textbbox((tx, ty), text, font=font)
    # (not over another label: moved down past it)
    while placed is not None and any(bb[0] < q[2] + 6 and bb[2] > q[0] - 6 and bb[1] < q[3] + 6 and bb[3] > q[1] - 6 for q in placed):
        ty += bb[3] - bb[1] + 10
        bb = draw.textbbox((tx, ty), text, font=font)
    if placed is not None:
        placed.append(bb)
    draw.rectangle([bb[0] - 4, bb[1] - 3, bb[2] + 4, bb[3] + 3], fill=(0, 0, 0))
    draw.text((tx, ty), text, fill=(170, 170, 170) if hidden else (255, 255, 255), font=font)


def note_views(d, quads, path, out_dir):
    """One close-up per note on this design (its spot marked and numbered) and an overview with all of them."""
    import json
    import carrack
    from PIL import Image, ImageDraw
    with open(path, encoding="utf-8-sig") as f:
        notes = json.load(f)
    os.makedirs(out_dir, exist_ok=True)
    for old in os.listdir(out_dir):
        if old.startswith("note-") and old.endswith(".png"):
            os.remove(os.path.join(out_dir, old))
    water = carrack.WATER          # the game's ship-local y + this = the drawing's (the design is drawn round another waterline)
    spots = []
    for note in notes:
        num = note.get("number")
        mark = note.get("mark") or {}
        target = note.get("target") or {}
        code = mark.get("shipCode") or (target.get("ship") or {}).get("code")
        summary = target.get("summary") or target.get("kind") or "?"
        if not mark.get("shipId") or mark.get("local") is None:
            print(f"note {num}: not on a ship ({summary}): no view")
            continue
        if code != d.code:
            print(f"note {num}: on a {code}, not the {d.code}: no view")
            continue
        point = np.asarray(mark["local"], float) + [0, water, 0]
        normal = np.asarray(mark.get("normal") or [0.0, 1.0, 0.0], float)
        normal = normal / (np.linalg.norm(normal) or 1.0)
        spots.append((num, point, normal))
        eye = _note_eye(note, mark, point, normal, water)
        size, fov = (960, 640), 70
        zb = []
        img = render(quads + _marker_quads(point, normal), eye, point, size=size, fov=fov, depth=zb)
        _label(img, _project(point, eye, point, size, fov), str(num), zb[0])
        draw = ImageDraw.Draw(img)
        comment = (note.get("comment") or "").replace("\n", " ")
        lines = [f"Note {num} ({note.get('time', '?')}): {summary}", f"ship-local {mark['local']}" + (f"  \"{comment[:100]}\"" if comment else "")]
        draw.rectangle([0, 0, size[0], 44], fill=(0, 0, 0))
        for i, line in enumerate(lines):
            draw.text((6, 3 + 20 * i), line, fill=(255, 255, 255), font=_font(15))
        p = os.path.join(out_dir, f"note-{num}.png")
        img.save(p)
        print(p)
    if not spots:
        print("no notes on the", d.code, "in", path)
        return
    # the overview: her starboard and port sides from above, every note's spot numbered
    sheet = Image.new("RGB", (1920, 664), (255, 255, 255))
    marks = [q for _, pt, nm in spots for q in _marker_quads(pt, nm, 0.8)]
    for i, (eye, target) in enumerate((((2.0, 26.0, 46.0), (2.0, 8.0, 0.0)), ((2.0, 26.0, -46.0), (2.0, 8.0, 0.0)))):
        zb = []
        img = render(quads + marks, eye, target, size=(960, 640), fov=55, depth=zb)
        placed = []
        for num, pt, _ in spots:
            _label(img, _project(pt, eye, target, (960, 640), 55), str(num), zb[0], big=False, placed=placed)
        sheet.paste(img, (i * 960, 24))
    ImageDraw.Draw(sheet).text((6, 4), f"{len(spots)} notes on the {d.code} ({os.path.abspath(path)}); left: from starboard, right: from port",
                               fill=(0, 0, 0), font=_font(15))
    p = os.path.join(out_dir, "overview.png")
    sheet.save(p)
    print(p)


def slot_views(d, boxes, out):
    """(0.3.20, step B: HANSA_MODULAR=1 python view.py slots) carrack-m's deck cells in a few states (docs/slots.md):
    as designed (the hatchways and wells open), a checkerboard of the main deck's and the lower deck's cells taken up,
    the main deck's all taken up; each from above the waist and from the great cabin's door, into docs/views/slots/."""
    import carrack as C
    slots = d.props.get("deckSlots") or []
    if not slots:
        print("no deck slots (build carrack-m: HANSA_MODULAR=1)")
        return
    os.makedirs(out, exist_ok=True)
    design = [s.get("start", "filled") != "empty" for s in slots]

    def checker(decks):
        return [f if s["deck"] not in decks else (int(round(s["box"][0])) + int(round(s["box"][2] - 0.5))) % 2 == 0
                for s, f in zip(slots, design)]

    states = {"design": design, "checker": checker(("main", "lower")),
              "main-empty": [False if s["deck"] == "main" else f for s, f in zip(slots, design)]}
    M = C.MAIN
    eyes = {"waist": ((-3.0, M + 6.5, -7.5), (6.0, M - 0.5, 0.5)),
            "below": ((-1.5, C.LOWER + 1.6, -3.0), (6.5, M + 0.4, 1.0)),
            "cabin": ((-13.5, M + 1.7, -2.5), (-6.0, M - 0.6, 1.0))}
    for name, st in states.items():
        d.slot_states = st
        q = d._all_quads(boxes, {}, skip_markers=("boat",)) + vanilla_quads(d) + modular_quads(d)
        for view, (eye, target) in eyes.items():
            img = render(q, eye, target, fov=75.0)
            path = os.path.join(out, f"{d.code}-slots-{name}-{view}.png")
            img.save(path)
            print(path)
    d.slot_states = None


# (0.3.26, step D1) the panels taken out for the views with holes (--empty=demo): below the waterline amidships (two
# over each other and one beside them), one at the waterline, one high on the side, one in the great cabin's side
EMPTY_DEMO = ["hull:s:12:1", "hull:s:12:2", "hull:s:13:2", "hull:s:16:3", "hull:s:10:4", "hull:s:6:5", "hull:p:12:2"]
# ((0.3.28, D2.1, D2.2) one-block panels: two halves out side by side (only the rib between them), one half by itself)
EMPTY_DEMO_HALVES = ["hull:s:12:1:a", "hull:s:12:1:f", "hull:s:12:2:a", "hull:s:12:2:f", "hull:s:13:2:a", "hull:s:16:3:f",
                     "hull:s:10:4:a", "hull:s:10:4:f", "hull:s:6:5:f", "hull:p:12:2:a", "hull:p:12:2:f"]


def empty_states(d, names):
    """(0.3.26) The hull panels' states for --empty=id,id,... (or demo): each filled but those named; None: all in."""
    ids = set()
    for n in names:
        if n.startswith("--empty="):
            for v in n.split("=", 1)[1].split(","):
                ids |= set(EMPTY_DEMO + EMPTY_DEMO_HALVES) if v == "demo" else {v}
    slots = d.props.get("hullSlots") or []
    have = {s["id"] for s in slots}
    if any(i in have for i in EMPTY_DEMO_HALVES):
        ids -= set(EMPTY_DEMO) - have
    if not ids or not slots:
        return None
    missing = ids - have
    if missing:
        print("not hull panels (fixed, or no such):", sorted(missing))
    return [s["id"] not in ids for s in slots]


# (0.3.29) the views of 0.3.29's changes: the companion beside the quarterdeck stair (notes 230, 233), the quarterdeck's
# third guns (232), a bow gun trained to either limit, the castle ports' boards and ports, her side's two layers (D2.3)
V0329 = {
    "0329-companion-out": ((3.0, 6.0, -2.9), (-1.0, 4.3, 0.0), 70),
    "0329-companion-in": ((-0.3, 5.25, -0.15), (-2.7, 2.7, 0.15), 85),
    "0329-companion-below": ((-5.2, 2.15, 1.7), (-1.2, 1.7, 0.0), 75),
    "0329-quarter3": ((-0.6, 9.2, 0.0), (-3.2, 6.8, 0.0), 95),
    "0329-bow-left": ((16.4, 8.5, 0.6), (19.3, 6.7, -2.4), 70),
    "0329-bow-right": ((16.4, 8.5, 0.6), (19.3, 6.7, -2.4), 70),
    "0329-ports": ((-10.5, 9.2, -10.5), (-9.5, 8.0, -2.8), 60),
    "0329-ceiling-out": ((1.6, -0.6, 0.5), (4.0, -2.0, 4.8), 70),
    "0329-both-out": ((1.6, -0.6, 0.5), (4.0, -2.0, 4.8), 70),
    "0329-both-out-outside": ((4.4, 0.6, 11.5), (4.0, -1.8, 5.2), 40),
    "0329-step": ((1.2, 5.3, 1.6), (-2.2, 4.7, 3.7), 70),
    "0329-outside": ((2.0, 5.0, 19.0), (2.0, 2.0, 0.0), 75),
    "0329-hold": ((7.0, -0.7, -0.5), (-4.0, -1.6, 3.2), 80),
}
# (0.3.30) chalk notes 234-243: the forecastle's forward gun (note 237) at each of its train limits from above and from
# the deck, the poop's plain sides (236), the gallows beam's end (235), the binnacle (240), the lower deck's panel (238)
V0330 = {
    "0330-bow-top-aft": ((18.2, 11.8, -1.2), (19.0, 6.8, -2.7), 62),
    "0330-bow-top-fwd": ((18.2, 11.8, -1.2), (19.0, 6.8, -2.7), 62),
    "0330-bow-top-square": ((18.2, 11.8, -1.2), (19.0, 6.8, -2.7), 62),
    "0330-bow-aft": ((16.4, 8.5, 0.6), (19.3, 6.7, -2.4), 70),
    "0330-bow-fwd": ((16.4, 8.5, 0.6), (19.3, 6.7, -2.4), 70),
    "0330-poop-side": ((-12.5, 11.0, -11.0), (-13.0, 9.8, -3.0), 55),
    "0330-poop-in": ((-12.0, 11.3, 1.2), (-13.0, 10.0, -4.0), 75),
    "0330-gallows-end": ((0.9, 5.3, 3.4), (2.2, 6.25, 4.55), 60),
    "0330-gallows-out": ((2.2, 6.0, 9.0), (2.2, 6.0, 4.0), 50),
    "0330-binnacle": ((-4.9, 7.9, -1.9), (-3.5, 7.0, -0.2), 60),
    "0330-panel-238": ((7.0, 2.25, -2.6), (7.0, 1.6, -5.4), 75),
}
CEIL_OUT_0329 = ["ceil:s:13:2:f", "ceil:s:14:2:a", "ceil:s:13:1:f"]
BOTH_OUT_0329 = CEIL_OUT_0329 + ["hull:s:13:2:f", "hull:s:14:2:a", "hull:s:13:1:f"]


def views_0329(d, boxes, out, wanted):
    import carrack
    hs_ = d.props.get("hullSlots") or []
    ds_ = d.props.get("deckSlots") or []
    design_h = [x.get("start", "filled") != "empty" for x in hs_]
    design_d = [x.get("start", "filled") != "empty" for x in ds_]
    for name in wanted:
        eye, target, fov = V0329[name] if name in V0329 else V0330[name]
        hstates, dstates, pose = list(design_h), list(design_d), {}
        if name in ("0329-ceiling-out", "0329-both-out", "0329-both-out-outside"):
            off = CEIL_OUT_0329 if name == "0329-ceiling-out" else BOTH_OUT_0329
            hstates = [st and x["id"] not in off for x, st in zip(hs_, hstates)]
        if name == "0329-ports":
            # (the poop's ports, port side: the after one boarded over as she starts, the middle one an opening (its
            # board out), the forward one a port put in (its board out, the port in)
            ids = {x["id"]: i for i, x in enumerate(ds_)}
            for k, (board, port) in {"gun-port-poop-3": (False, False), "gun-port-poop-2": (False, True)}.items():
                if "port:" + k not in ids:
                    continue
                dstates[ids["port:" + k]] = board
                dstates[ids["gunport:" + k]] = port
        if name.startswith("0330-bow"):
            # ((0.3.30, note 237) the forecastle's forward gun, port side, to its after limit, its forward one, square)
            f = next(x for x in d.fixtures if x.get("code") == "gun-port-forecastle-1")
            t0, t1 = f["trainLimits"]
            # (the port side's gun: + trains it toward the bow)
            pose = {"train:gun-port-forecastle-1": t1 if name.endswith("fwd") else t0 if name.endswith("aft") else 0.0}
        if name in ("0329-bow-left", "0329-bow-right"):
            f = next(x for x in d.fixtures if x.get("code") == "gun-port-forecastle-1")
            t0, t1 = f["trainLimits"]
            pose = {"train:gun-port-forecastle-1": t1 if name.endswith("left") else t0}
        saved = [list(x["pts"]) for x in d.quads]
        d.hull_states, d.slot_states = hstates, dstates
        try:
            if pose:
                pose_parts(d, pose)
            q = scene(d, boxes)
        finally:
            for x, pts in zip(d.quads, saved):
                x["pts"] = pts
            d.hull_states, d.slot_states = None, None
        img = render(q, eye, target, fov=fov)
        path = os.path.join(out, f"{d.code}-{name}.png")
        img.save(path)
        print(path, pose or "")


def main(names):
    import carrack
    d = carrack.build()
    d.hull_states = empty_states(d, names)
    boxes, rg = d.mesh_voxels()
    quads = scene(d, boxes)
    out = os.path.join(carrack.DOCS, "views")
    os.makedirs(out, exist_ok=True)
    if names and names[0] == "slots":
        slot_views(d, boxes, os.path.join(out, "slots"))
        return
    if names and names[0] == "notes":
        note_views(d, quads, names[1] if len(names) > 1 else default_notes_path(), os.path.join(out, "notes"))
        return
    wanted = [n for n in names if not n.startswith("--")] or [n for n in VIEWS if not n.startswith("m-")] + list(LAY_SHEETS) + ["gun-captain"]
    if wanted == ["m"]:
        wanted = [n for n in VIEWS if n.startswith("m-")]
    if wanted == ["m23"]:
        wanted = [n for n in VIEWS if n.startswith("m23-")]
    if wanted == ["0324"]:
        wanted = [n for n in VIEWS if n.startswith("0324-")]
    if wanted == ["0325"]:
        wanted = [n for n in VIEWS if n.startswith("0325-")]
    if wanted == ["0326"]:
        wanted = [n for n in VIEWS if n.startswith("0326-")]
    if wanted == ["0327"]:
        wanted = [n for n in VIEWS if n.startswith("0327-")]
    if wanted == ["0328"]:
        wanted = [n for n in VIEWS if n.startswith("0328-")]
    if wanted == ["0329"] or any(n in V0329 for n in wanted):
        views_0329(d, boxes, out, list(V0329) if wanted == ["0329"] else [n for n in wanted if n in V0329])
        return
    if wanted == ["0330"] or any(n in V0330 for n in wanted):
        views_0329(d, boxes, out, list(V0330) if wanted == ["0330"] else [n for n in wanted if n in V0330])
        return
    if wanted == ["grid"]:
        wanted = [n for n in VIEWS if n.startswith("grid-")]
    gun_sheets(d, boxes, [n for n in wanted if n in LAY_SHEETS or n == "gun-captain"], out)
    asked = d.hull_states
    for name in [n for n in wanted if n in VIEWS]:
        q = quads
        if name == "0328-hood":
            # ((0.3.28, note 230) a companion hood on the waist (the user's well was by the quarterdeck's edge, x -3..0; here
            # one forward of the mainmast, x 2..5, z 1.5..2.5, clear of the pump): a steep stair down in it, partitions round
            # it two rows high (a window), a door at its forward end, its roof plank slabs on the walls' tops)
            slots_ = d.props["deckSlots"]
            st_ = [s_.get("start", "filled") != "empty" for s_ in slots_]
            for i, s_ in enumerate(slots_):
                bb = s_["box"]
                if s_.get("kind") is None and s_["deck"] == "main" and abs((bb[2] + bb[5]) / 2 - 2.0) < 0.01 and 1.99 < bb[0] < 4.01:
                    st_[i] = False
                if s_.get("kind") == "beam" and s_["deck"] == "main" and abs((bb[2] + bb[5]) / 2 - 2.0) < 0.01 and 3.5 < bb[3] < 4.5:
                    st_[i] = False
            d.slot_states = st_
            M = carrack.MAIN
            extra = [{"kind": "stair", "at": [3.5, carrack.LOWER, 2.0], "up": "+x", "rise": 3.0, "length": 2, "code": "hood-stair"}]
            for xm in (2.5, 3.5, 4.5):
                for zl in (1.5, 2.5):
                    for row in (0, 1):
                        extra.append({"kind": "window" if row == 1 and xm == 3.5 else "partition", "at": [xm, M + row, zl], "along": "x"})
            for row in (0, 1):
                extra.append({"kind": "partition", "at": [2.0, M + row, 2.0], "along": "z"})
            extra.append({"kind": "door", "at": [5.0, M, 2.0], "along": "z", "block": carrack.MOD_DOOR, "open": True, "code": "hood-door"})
            for xm in (2.5, 3.5, 4.5):
                extra.append({"kind": "panel", "at": [xm, M + 2.0, 2.0]})
            saved_mod = d.modular
            d.modular = saved_mod + extra
            try:
                q = scene(d, boxes)
            finally:
                d.modular = saved_mod
                d.slot_states = None
            eye, target, sea, fov = (7.6, 6.4, -0.8), (3.6, 4.4, 2.0), None, 65
        elif name.startswith("0327-well"):
            # ((0.3.27) a main deck beam's length out under a 3 x 1 well (and the well's cells), seen from the lower deck
            # and from over the well: its carlings, the cut beam)
            slots_ = d.props["deckSlots"]
            segs = [i for i, s_ in enumerate(slots_) if s_.get("kind") == "beam" and s_["deck"] == "main"]
            if not segs:
                continue
            seg = min(segs, key=lambda i: abs(slots_[i]["box"][3] - 4.0) + abs((slots_[i]["box"][2] + slots_[i]["box"][5]) / 2 - 2.0))
            b_ = slots_[seg]["box"]
            xc, zc = b_[3], (b_[2] + b_[5]) / 2
            ids = {s_["id"]: i for i, s_ in enumerate(slots_)}
            st_ = [s_.get("start", "filled") != "empty" for s_ in slots_]
            st_[seg] = False
            top = b_[4] + carrack.DECK_T
            for i, s_ in enumerate(slots_):
                bb = s_["box"]
                if s_.get("kind") != "beam" and s_["deck"] == "main" and abs((bb[2] + bb[5]) / 2 - zc) < 0.01 and xc - 1.01 < bb[0] < xc + 1.01:
                    st_[i] = False
            d.slot_states = st_
            q = scene(d, boxes)
            d.slot_states = None
            if name == "0327-well-below":
                eye, target, sea, fov = (xc + 0.6, top - 1.6, zc - 2.4), (xc, top - 0.5, zc), None, 80
            else:
                eye, target, sea, fov = (xc + 0.4, top + 1.8, zc + 2.6), (xc, top - 1.2, zc), None, 75
        else:
            eye, target, sea, fov = VIEWS[name]
        # ((0.3.26) the "0326-holes-" views with EMPTY_DEMO's panels out, unless --empty= says which)
        holes = name.startswith(("0326-holes", "0328-holes")) and asked is None
        if holes:
            d.hull_states = empty_states(d, ["--empty=demo"])
            q = scene(d, boxes)
        if name.startswith("hearth-pot"):
            q = d._all_quads(boxes, {}, skip_markers=("boat",)) + vanilla_quads(d, firepit="lit-wide")
        if (name in OPEN_IN or name in FIRED or name in CUTS or name in M23_LIDS_OPEN or name in M23_HOUSED or
                name in M24_LIDS_OPEN or name in M24_PUMP):
            saved = [list(x["pts"]) for x in d.quads]
            for code in OPEN_IN.get(name, ()):
                turn_part(d, code)
            if name in FIRED:
                fire_guns(d)
            if name in M23_LIDS_OPEN:
                for g in getattr(d, "castle_guns", []):
                    turn_part(d, f"{g}-lid")
            if name in M24_LIDS_OPEN:
                for g in getattr(d, "castle_guns", []):
                    turn_part(d, f"{g}-lid")
            if name in M24_PUMP:
                pose_parts(d, {"pump:pump": M24_PUMP[name]})
            if name in M23_HOUSED:
                pose_parts(d, {f"fire:{g}": 1.0 for g in getattr(d, "castle_guns", [])})
            if name in CUTS:
                q = d._all_quads(boxes, {}, cut=CUTS[name], skip_markers=("boat",)) + modular_quads(d, CUTS[name])
            else:
                q = scene(d, boxes)
            for x, pts in zip(d.quads, saved):
                x["pts"] = pts
        img = render(q, eye, target, fov=fov, water=None if sea is None else sea + carrack.WATER)
        path = os.path.join(out, f"{d.code}-{name}{'-holes' if asked is not None and not name.startswith(('0326-holes', '0328-holes')) else ''}.png")
        if holes:
            d.hull_states = asked
        img.save(path)
        print(path)


if __name__ == "__main__":
    main(sys.argv[1:])
