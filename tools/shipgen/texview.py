"""
Textured close-ups of a design: like view.py's perspective renders, but each quad is drawn with the game's own texture
(the vanilla PNGs from the installed game, with Hansa's tar and caulk overlays composited the way the game does), so
texture problems (which way the grain runs, a board showing a different plank or colour) can be seen without the game.
Slow (pure numpy); a few views at a time.

  python texview.py gate-out gate-in       # names from VIEWS below, or view.py's
  python texview.py damage [name...]       # the damage drawn on her hull (DAMAGE_VIEWS; run the tests' damage-views first)
  python texview.py notes [path] [n...]    # the surveyor's chalk's notes, textured, from where the player stood
                                           # (docs/views/notes/tex-note-<n>.png; view.py notes draws them flat)
"""
import math
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
import view  # noqa: E402

GAME = os.path.join(os.environ.get("APPDATA", ""), "Vintagestory", "assets")
MOD = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "assets", "ships"))
_cache = {}


def _png(domain, path):
    """A texture's pixels (RGBA float 0..1), from the game's assets (game, survival, creative) or Hansa's."""
    rel = os.path.join("textures", *path.split("/")) + ".png"
    roots = [MOD] if domain == "ships" else [os.path.join(GAME, d) for d in ("survival", "game", "creative")]
    for r in roots:
        p = os.path.join(r, rel)
        if os.path.exists(p):
            return np.asarray(Image.open(p).convert("RGBA"), float) / 255
    return None


def texture(code):
    """'game:block/wood/planks/oak1++3~ships:block/tar': the base with its overlays blended as the game does
    (3 multiply, 0 normal)."""
    if code in _cache:
        return _cache[code]
    parts = code.split("++")
    dom, path = parts[0].split(":", 1) if ":" in parts[0] else ("game", parts[0])
    img = _png(dom, path)
    if img is None:
        img = np.full((16, 16, 4), (1.0, 0.0, 1.0, 1.0))
    for ov in parts[1:]:
        mode, rest = ov.split("~", 1) if "~" in ov else ("0", ov)
        od, op = rest.split(":", 1) if ":" in rest else ("game", rest)
        o = _png(od, op)
        if o is None or o.shape != img.shape:
            continue
        if mode == "3":
            img = np.concatenate([img[..., :3] * (1 - o[..., 3:] + o[..., 3:] * o[..., :3]), img[..., 3:]], axis=-1)
        else:
            a = o[..., 3:]
            img = np.concatenate([img[..., :3] * (1 - a) + o[..., :3] * a, img[..., 3:]], axis=-1)
    if "block/liquid/" in code:
        # the mod tints vanilla's grey water (the shader's rgbaTint); here opaque
        img = img.copy()
        img[..., :3] *= np.array([0.36, 0.5, 0.52]) * 1.6
        img[..., 3] = 1.0
    _cache[code] = img
    return img


def design_quads(d, boxes, groups=None):
    """[(pts 4x3, uv corners 4x2 in texels, texture code)] for the skin mesh and the voxel boxes, as the game draws
    them (corner uvs as in Design._write_skin)."""
    out = []
    for i, q in enumerate(d.quads):
        if groups is not None and q["group"] not in groups and not any(q["group"].startswith(g) for g in groups if g.endswith(":")):
            continue
        if q["group"].startswith("bottomclad"):
            continue
        m = d.materials[d.mat_index[q["mat"]]]
        if not m.render:
            continue
        code = d.quad_texture(i, q)
        out.append((np.asarray(q["pts"], float), np.array(d.corner_uvs(q), float), code))
    for b in boxes:
        m = d.materials[b["mat"]]
        code = m.textures[b["variant"] % len(m.textures)]
        lo, hi = b["lo"], b["hi"]
        for f, (dx, dy, dz) in zip(("east", "west", "up", "down", "south", "north"), ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))):
            if b["faces"][f] == 0:
                continue
            x0, y0, z0 = lo
            x1, y1, z1 = hi
            if f in ("east", "west"):
                x = x1 if f == "east" else x0
                pts = [(x, y0, z0), (x, y0, z1), (x, y1, z1), (x, y1, z0)]
            elif f in ("up", "down"):
                y = y1 if f == "up" else y0
                pts = [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)]
            else:
                z = z1 if f == "south" else z0
                pts = [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
            ua = d._uv_aabb(f, lo, hi)          # [u0, v0, u1, v1] as the shape file has it
            if f in ("east", "west"):
                uv = [(ua[0], ua[3]), (ua[2], ua[3]), (ua[2], ua[1]), (ua[0], ua[1])]
            elif f in ("up", "down"):
                uv = [(ua[0], ua[1]), (ua[2], ua[1]), (ua[2], ua[3]), (ua[0], ua[3])]
            else:
                uv = [(ua[0], ua[3]), (ua[2], ua[3]), (ua[2], ua[1]), (ua[0], ua[1])]
            out.append((np.asarray(pts, float), np.asarray(uv, float), code, np.array((dx, dy, dz), float)))
    return out


def render(quads, eye, target, size=(960, 640), fov=70.0, bg=(206, 222, 235), light=(0.35, 0.85, 0.4)):
    W, H = size
    eye, R = view.look_at(eye, target)
    focal = 0.5 * W / math.tan(math.radians(fov) / 2)
    L = np.asarray(light, float)
    L /= np.linalg.norm(L)
    img = np.empty((H, W, 3), float)
    img[:] = bg
    zbuf = np.full((H, W), np.inf)
    near = 0.05
    for q in quads:
        pts, uv, code = q[:3]
        tex = texture(code)
        th, tw = tex.shape[:2]
        # (a voxel face's normal is its face's; a mesh quad's from its corners, counter-clockwise from the side it faces)
        n = q[3] if len(q) > 3 else np.cross(pts[1] - pts[0], pts[3] - pts[0]) + np.cross(pts[3] - pts[2], pts[1] - pts[2])
        ln = np.linalg.norm(n)
        if ln < 1e-12:
            continue
        # lit by its normal as the game's shader lights a ship's mesh (normalshading.fsh: half lit plus half the light's
        # angle, at least 0.45, up faces bright), so a face whose normal points the wrong way shows up dark here as in
        # the game (the game draws her mesh both sides: the winding doesn't hide it)
        nn = n / ln
        shade = max(0.45, 0.5 + 0.5 * float(np.dot(nn, L)), 0.95 * float(nn[1]))
        for tri in ((0, 1, 2), (0, 2, 3)):
            P = pts[list(tri)]
            T = uv[list(tri)]
            cam = (P - eye) @ R.T
            if (cam[:, 2] < near).any():
                continue            # (no clipping: close-ups keep the eye clear of the geometry)
            sx = W / 2 + focal * cam[:, 0] / cam[:, 2]
            sy = H / 2 - focal * cam[:, 1] / cam[:, 2]
            x0, x1 = int(max(0, math.floor(sx.min()))), int(min(W - 1, math.ceil(sx.max())))
            y0, y1 = int(max(0, math.floor(sy.min()))), int(min(H - 1, math.ceil(sy.max())))
            if x0 > x1 or y0 > y1:
                continue
            area = (sx[1] - sx[0]) * (sy[2] - sy[0]) - (sx[2] - sx[0]) * (sy[1] - sy[0])
            if abs(area) < 1e-9:
                continue
            X, Y = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
            w0 = ((sx[1] - X) * (sy[2] - Y) - (sx[2] - X) * (sy[1] - Y)) / area
            w1 = ((sx[2] - X) * (sy[0] - Y) - (sx[0] - X) * (sy[2] - Y)) / area
            w2 = 1 - w0 - w1
            inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
            if not inside.any():
                continue
            iz = w0 / cam[0, 2] + w1 / cam[1, 2] + w2 / cam[2, 2]
            z = 1.0 / np.maximum(iz, 1e-9)
            sub = zbuf[y0:y1 + 1, x0:x1 + 1]
            win = inside & (z < sub)
            if not win.any():
                continue
            # perspective-correct texture coordinates
            u = (w0 * T[0, 0] / cam[0, 2] + w1 * T[1, 0] / cam[1, 2] + w2 * T[2, 0] / cam[2, 2]) * z
            v = (w0 * T[0, 1] / cam[0, 2] + w1 * T[1, 1] / cam[1, 2] + w2 * T[2, 1] / cam[2, 2]) * z
            px = np.clip((u / 16 * tw).astype(int), 0, tw - 1)
            py = np.clip((v / 16 * th).astype(int), 0, th - 1)
            col = tex[py, px]
            opaque = col[..., 3] > 0.3
            win &= opaque
            sub[win] = z[win]
            img[y0:y1 + 1, x0:x1 + 1][win] = col[..., :3][win] * 255 * shade
    fog = np.clip((zbuf - 25) / 80, 0, 0.6)[..., None]
    fog[np.isinf(zbuf)] = 0
    img = img * (1 - fog) + np.asarray(bg, float) * fog
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))


VIEWS = {
    # (0.3.8: the gates in the bay aft of the gallows, x 0.75..2.0)
    "gate-out": ((1.4, 4.3, 7.0), (1.4, 3.9, 3.0), 60, ()),
    "gate-in": ((1.4, 5.0, 1.2), (1.4, 3.9, 4.0), 60, ()),
    "gate-out-open": ((2.3, 4.5, 6.8), (1.4, 3.9, 3.6), 60, ("gate-starboard", "gate-port")),
    "gate-in-open": ((-0.3, 5.0, 1.5), (1.5, 3.9, 3.8), 60, ("gate-starboard", "gate-port")),
    "frames": ((0.0, 2.0, -1.0), (3.0, 1.8, 3.5), 70, ()),
    "stair-quarter": ((-2.2, 5.2, 0.2), (-5.2, 4.9, 2.6), 70, ()),
    "stair-forecastle": ((7.2, 5.2, 0.3), (10.2, 5.0, 2.3), 70, ()),
    "stair-poop": ((-6.2, 8.2, 0.3), (-9.2, 7.9, 2.6), 70, ()),
    "beams": ((-2.0, 1.6, 0.0), (1.0, 3.0, 1.5), 75, ()),
    "mast": ((2.5, 5.0, -2.5), (-0.1, 6.0, 0.0), 60, ()),
    # 0.3.6: the firehearth's bricks and copper hood, the gallows' post (a frame carried up over the rail)
    "hearth": ((13.1, 4.75, 0.5), (13.1, 4.2, 2.9), 72, ()),
    "gallows-post": ((4.6, 5.3, 2.4), (6.6, 5.4, 4.5), 75, ()),
    # 0.3.7 (the user's chalk notes): the companion house and its door, the bowsprit stepped against the foremast and
    # resting on the stem head, the heads facing aft, the apron and the inner sternpost, the cabins' fronts, the stern
    # timbers, the taffrail's corner and the stern lantern, the hawse piece and the round turn on the bitt, the cable
    # tier under its hole, the gallows' post through the rail cap, the forecastle break's cap
    "companion-house": ((-1.2, 5.6, -2.6), (2.4, 4.6, 0.0), 70, ()),
    "companion-in": ((0.6, 5.2, 0.0), (3.0, 3.0, 0.0), 75, ()),          # (the vanilla door isn't drawn here)
    "bowsprit-heel": ((17.9, 5.4, -1.3), (16.0, 3.9, 0.0), 75, ()),
    "bowsprit-stem": ((21.5, 9.0, -3.2), (24.3, 7.6, 0.0), 65, ()),
    "heads": ((20.6, 8.4, 0.0), (22.4, 6.6, 0.0), 75, ()),
    "bow-apron": ((16.0, 2.3, 0.0), (19.4, 2.4, 0.0), 75, ()),
    "stern-post-in": ((-11.8, 2.4, -1.4), (-14.2, 1.2, 0.0), 75, ()),
    "cabin-fronts": ((1.2, 5.6, -0.2), (-3.0, 5.2, -0.6), 75, ()),
    "poop-front": ((-5.2, 8.0, -0.6), (-8.5, 7.8, -0.6), 75, ()),
    "stern-timbers": ((-13.5, 5.1, 0.0), (-15.5, 4.8, 0.0), 80, ()),
    "taffrail-corner": ((-16.6, 11.0, 2.0), (-17.9, 10.4, 3.4), 70, ()),
    "stern-lantern": ((-15.6, 11.4, -1.5), (-18.3, 10.6, 0.0), 65, ()),
    "hawse-piece": ((17.2, 5.0, -0.2), (18.6, 4.3, -1.4), 70, ()),
    "bitt-turn": ((16.6, 4.6, -0.3), (17.9, 3.7, -1.3), 65, ()),
    "cable-tier": ((9.0, 2.3, 0.6), (10.6, 0.7, -1.05), 70, ()),
    "fc-break-cap": ((8.4, 5.6, 3.4), (9.6, 4.5, 4.6), 70, ()),
    "stair-balusters": ((0.4, 5.4, 0.6), (-2.2, 4.9, 2.6), 70, ()),
    # 0.3.8 (the user's chalk notes 33-54): the poop's overhang from the starboard quarter and from under it behind the
    # transom, the forecastle's raked underside by the stem, the forecastle break's corner, the taffrail's corner block
    # outside, the great cabin's after corner, the capstan's drumhead, the main top's tub, the bronze pump and the
    # wheel's bronze, the shorter gun and its breeching, the stern gallery and the nameboard lit the right way round
    "poop-overhang": ((-18.5, 4.6, 6.5), (-15.0, 6.2, 3.8), 70, ()),
    "poop-overhang-under": ((-18.8, 3.4, -2.6), (-16.2, 6.25, 0.4), 72, ()),
    "fc-underside": ((21.2, 2.6, 2.2), (19.8, 4.9, 0.2), 75, ()),
    "fc-break-corner": ((10.06, 5.1, -3.95), (11.86, 6.25, -3.84), 50, ()),
    "taffrail-outside": ((-19.5, 11.6, -5.5), (-17.9, 10.4, -3.4), 60, ()),
    "great-cabin-aft": ((-14.33, 5.1, -1.92), (-15.15, 6.25, -3.54), 70, ()),
    "capstan": ((9.89, 5.1, -0.15), (10.14, 4.6, -1.52), 70, ()),
    "main-top": ((-0.38, 23.1, 1.06), (1.02, 21.5, 0.35), 70, ()),
    "main-top-out": ((4.5, 23.5, 4.5), (-0.1, 22.0, 0.0), 55, ()),
    "pump": ((8.9, 4.9, 2.0), (8.5, 4.2, 0.2), 60, ()),
    "wheel": ((-6.0, 7.6, 1.2), (-5.1, 7.35, 0.0), 55, ()),
    "gun": ((1.0, 4.6, 3.4), (2.875, 3.9, 3.9), 60, ()),
    "gun-breeching": ((2.875, 6.2, 2.6), (2.875, 3.8, 3.9), 60, ()),
    "stern-gallery": ((-22.5, 8.0, -3.5), (-16.5, 7.0, 0.0), 60, ()),
    "stern-gallery-in": ((-12.5, 5.2, 1.5), (-15.8, 5.0, -1.0), 75, ()),
    # 0.3.8 (the agreed redesign): the companion stair in the great cabin from inside its door and from the lower deck;
    # the waist's five guns a side, their ports' lids shut and open; a scupper; the oven's base and flue by the hearth
    "companion": ((-3.7, 5.2, 0.4), (-5.2, 3.6, -3.4), 75, ()),
    "companion-below": ((-8.2, 2.3, -1.6), (-5.0, 2.4, -3.5), 75, ()),
    "waist-guns": ((11.0, 8.2, 0.0), (3.0, 3.6, 0.0), 80, ()),
    "waist-lids": ((5.75, 5.6, -9.5), (5.75, 4.0, -4.5), 70, ()),
    "waist-lids-open": ((5.75, 5.6, -9.5), (5.75, 4.0, -4.5), 70, tuple(f"gun-port-{k}-lid" for k in range(1, 6))),
    "waist-lids-starboard": ((5.75, 5.6, 9.5), (5.75, 4.0, 4.5), 70, tuple(f"gun-starboard-{k}-lid" for k in range(1, 6))),
    "scupper": ((2.375, 3.9, 6.0), (2.375, 3.6, 5.1), 45, ()),
    "oven": ((13.2, 5.3, 0.3), (14.4, 4.0, 2.7), 70, ()),
}


# Her hull with a row of panels amidships (port side) in each state, as the mod draws it: sound, worn, scraped, cracked,
# leaking, holed (just), holed (stove in), from aft to forward. The hull groups come from the mod's own code (the offline
# tests write them: dotnet run --project tests/HansaTests -c Release -- damage-views), the rest from the design.
#   name: the tests' scenario, eye, target, fov (design heights: the game's + carrack.WATER)
DAMAGE_VIEWS = {
    "damage-topsides": ("topsides", (0.0, 2.9, -14.5), (0.0, 2.9, -5.0), 62),
    "damage-bottom": ("bottom", (0.0, 0.7, -13.5), (0.0, 0.6, -5.0), 60),
    "damage-clad": ("clad", (0.0, 0.7, -13.5), (0.0, 0.6, -5.0), 60),
    "damage-hold": ("hold", (3.0, -0.9, 2.2), (0.5, -1.1, -4.6), 95),
    "damage-hole": ("topsides", (4.8, 3.0, -8.2), (4.6, 2.6, -5.0), 45),
    "damage-hole-inside": ("hold", (4.0, -0.4, -1.2), (4.3, -1.1, -3.9), 70),
    # the water in her hold (250 t) with her rolled 10° to starboard and 2° down by the head: level in the world; seen
    # from the hold, and cut away (all of her forward of the mainmast taken off, seen from ahead)
    "flooded-hold": ("flooded", (4.5, -0.05, 0.0), (-3.0, -0.6, -0.6), 90),
    "flooded-cutaway": ("flooded", (14.0, 1.6, 0.0), (0.0, -0.2, 0.0), 58),
}


def tilt(quads, pitch, roll, lift):
    """Her quads turned as she's drawn rolled and pitched (ShipFrame: roll about her keel, then pitch), about her origin."""
    cr, sr, cp, sp = math.cos(roll), math.sin(roll), math.cos(pitch), math.sin(pitch)
    out = []
    for q in quads:
        pts, uv, code = q[:3]
        x, y, z = pts[:, 0], pts[:, 1] - lift, pts[:, 2]
        y1, z1 = y * cr - z * sr, y * sr + z * cr
        x2, y2 = x * cp - y1 * sp, x * sp + y1 * cp
        item = (np.stack([x2, y2 + lift, z1], axis=1), uv, code)
        if len(q) > 3:                      # a voxel face's own normal, turned the same way
            nx, ny, nz = q[3]
            ny1, nz1 = ny * cr - nz * sr, ny * sr + nz * cr
            item += (np.array([nx * cp - ny1 * sp, nx * sp + ny1 * cp, nz1]),)
        out.append(item)
    return out


def damage_quads(scenario, lift):
    """The hull groups as the mod draws them for a scenario (from the offline tests' JSON), lifted into design heights."""
    import json
    import tempfile
    path = os.path.join(tempfile.gettempdir(), "ships-damage-views", f"carrack-damage-{scenario}.json")
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    out = []
    for quads in data["groups"].values():
        for q in quads:
            c = np.asarray(q[1:], float).reshape(4, 8)
            pts = c[:, :3] + np.array([0.0, lift, 0.0])
            out.append((pts, c[:, 3:5], data["textures"][int(q[0])]))
    return out, data.get("tilt")


def near(quads, eye, reach=24.0):
    e = np.asarray(eye, float)
    return [q for q in quads if np.min(np.linalg.norm(q[0] - e, axis=1)) < reach]


# A wreck (Hansa 0.3.5, task 5) as the client draws it: every face of its blocks from the mod's own code (the offline
# tests write them: dotnet run --project tests/HansaTests -c Release -- wreck-views), on a sand seabed, her guns,
# chests and the sand heaped against her as plain boxes. She lies heeled 19° to starboard and 5° down by the head, her
# bow toward +x (east), holed along her port side above the waterline; her masts fell to starboard.
#   name: eye, target, fov (relative to her origin: the waterline amidships, as she lies)
WRECK_VIEWS = {
    "wreck-overview": ((-26.0, 14.0, -24.0), (2.0, -3.0, 2.0), 62),
    "wreck-port": ((4.0, -1.0, -17.0), (2.0, -2.5, 0.0), 65),
    "wreck-starboard": ((10.0, -0.5, 20.0), (2.0, -3.5, 4.0), 70),
    "wreck-stern": ((-26.0, 1.0, -6.0), (-12.0, -1.5, 0.5), 60),
    "wreck-deck": ((-2.0, 9.0, -9.0), (4.0, -0.5, 1.5), 70),
    "wreck-hold": ((-6.0, -2.4, -0.8), (8.0, -3.4, 0.6), 85),
}


def wreck_quads():
    """The wreck's faces (from the tests' JSON), the seabed round her and her guns, chests and sand as boxes, all relative
    to her origin."""
    import json
    import tempfile
    path = os.path.join(tempfile.gettempdir(), "ships-wreck-views", "carrack-wreck.json")
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    o = np.asarray(data["origin"], float)
    out = []
    for q in data["quads"]:
        c = np.asarray(q[1:], float).reshape(4, 5)
        out.append((c[:, :3] - o, c[:, 3:5], data["textures"][int(q[0])]))

    def box(lo, hi, code, faces="neswud"):
        lo, hi = np.asarray(lo, float) - o, np.asarray(hi, float) - o
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        quads = {
            "e": [(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)],
            "w": [(x0, y0, z1), (x0, y0, z0), (x0, y1, z0), (x0, y1, z1)],
            "u": [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)],
            "d": [(x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0)],
            "s": [(x1, y0, z1), (x0, y0, z1), (x0, y1, z1), (x1, y1, z1)],
            "n": [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)],
        }
        for f in faces:
            pts = np.asarray(quads[f], float)
            # (a box longer than a block shows its texture stretched: these are only stand-ins)
            a = min(16.0, np.linalg.norm(pts[1] - pts[0]) * 16)
            b = min(16.0, np.linalg.norm(pts[3] - pts[0]) * 16)
            out.append((pts, np.array([(0, b), (a, b), (a, 0), (0, 0)], float), code))

    for b in data["boxes"]:
        box(b["lo"], b["hi"], b["tex"])
    # the seabed: a block of sand's top in every column round her
    y = float(data["seabed"])
    (x0, _, z0), (x1, _, z1) = data["min"], data["max"]
    for x in range(int(x0) - 10, int(x1) + 11):
        for z in range(int(z0) - 10, int(z1) + 11):
            box((x, y - 1, z), (x + 1, y, z + 1), "game:block/stone/sand/granite", faces="u")
    return out


def wreck_main(names):
    import carrack
    out = os.path.join(carrack.DOCS, "views")
    os.makedirs(out, exist_ok=True)
    quads = wreck_quads()
    for name in names or WRECK_VIEWS:
        eye, target, fov = WRECK_VIEWS[name]
        q = near(quads, eye, 60.0)
        img = render(q, eye, target, fov=fov, bg=(70, 105, 120))
        path = os.path.join(out, f"carrack-{name}.png")
        img.save(path)
        print(path)


def note_views(d, boxes, path, out_dir, only=()):
    """Each note on this design (or only these numbers), textured, seen as view.py's notes are (from the player's eye),
    its spot marked with a small magenta X; into out_dir as tex-note-<n>.png."""
    import json
    import carrack
    from PIL import ImageDraw
    with open(path, encoding="utf-8-sig") as f:
        notes = json.load(f)
    os.makedirs(out_dir, exist_ok=True)
    quads = design_quads(d, boxes)
    for note in notes:
        num = note.get("number")
        mark = note.get("mark") or {}
        if only and num not in only:
            continue
        if not mark.get("shipId") or mark.get("local") is None or (mark.get("shipCode") or "") not in ("", d.code):
            continue
        point = np.asarray(mark["local"], float) + [0, carrack.WATER, 0]
        normal = np.asarray(mark.get("normal") or [0.0, 1.0, 0.0], float)
        normal = normal / (np.linalg.norm(normal) or 1.0)
        eye = view._note_eye(note, mark, point, normal, carrack.WATER)
        img = render(near(quads, eye), eye, point, fov=70)
        at = view._project(point, eye, point, (960, 640), 70)
        draw = ImageDraw.Draw(img)
        if at is not None:
            x, y, _ = at
            for k in (-1, 1):
                draw.line([x - 9, y - 9 * k, x + 9, y + 9 * k], fill=(255, 0, 200), width=3)
        comment = (note.get("comment") or "").replace(chr(10), " ")
        draw.rectangle([0, 0, 960, 24], fill=(0, 0, 0))
        draw.text((6, 4), f"Note {num}: \"{comment[:110]}\"", fill=(255, 255, 255), font=view._font(15))
        p = os.path.join(out_dir, f"tex-note-{num}.png")
        img.save(p)
        print(p)


def main(names):
    if names and names[0] == "wreck":
        wreck_main(names[1:])
        return
    import carrack
    d = carrack.build()
    boxes, rg = d.mesh_voxels()
    out = os.path.join(carrack.DOCS, "views")
    os.makedirs(out, exist_ok=True)
    if names and names[0] == "notes":
        rest = names[1:]
        path = rest[0] if rest and not rest[0].isdigit() else view.default_notes_path()
        only = [int(n) for n in rest if n.isdigit()]
        note_views(d, boxes, path, os.path.join(out, "notes"), only)
        return
    saved = [list(q["pts"]) for q in d.quads]
    if names and names[0] == "damage":
        # everything but the hull groups from the design; those from the mod's damage build
        # ("auto" quads go to the exterior or the interior group when written: they're in the mod's too)
        hull = ("exterior", "bottom", "bottomclad", "interior", "auto")
        keep = [q for q in d.quads if q["group"] not in hull]
        all_q, d.quads = d.quads, keep
        rest = design_quads(d, boxes)
        d.quads = all_q
        for name in names[1:] or DAMAGE_VIEWS:
            scenario, eye, target, fov = DAMAGE_VIEWS[name]
            hull, tilted = damage_quads(scenario, carrack.WATER)
            quads = rest + hull
            if name.endswith("cutaway"):
                # all of her forward of the mainmast taken off, and the water in her there
                quads = [q for q in quads if q[0][:, 0].max() < 1.2]
            if tilted:
                quads = tilt(quads, tilted[0], tilted[1], carrack.WATER)
            quads = near(quads, eye)
            img = render(quads, eye, target, fov=fov)
            path = os.path.join(out, f"{d.code}-{name}.png")
            img.save(path)
            print(path)
        return
    for name in names or VIEWS:
        eye, target, fov, opened = VIEWS[name]
        for q, pts in zip(d.quads, saved):
            q["pts"] = pts
        for code in opened:
            view.turn_part(d, code)
        img = render(design_quads(d, boxes), eye, target, fov=fov)
        path = os.path.join(out, f"{d.code}-tex-{name}.png")
        img.save(path)
        print(path)


if __name__ == "__main__":
    main(sys.argv[1:])
