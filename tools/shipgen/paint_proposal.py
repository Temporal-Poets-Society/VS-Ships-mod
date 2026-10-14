"""
The carrack's paint zones (paint_zones.py), and example paint schemes a player could give her, drawn with the game's
textures from five views. Nothing in the generator or the assets is changed: the design is built with its quads tagged
(paint_zones.build()), sorted into zones, and drawn here with painted textures made by tinting vanilla's oak.

  python tools/shipgen/paint_proposal.py               # everything: the zone images, the schemes, the sheet
  python tools/shipgen/paint_proposal.py zones         # only the zone images (docs/views/paint/zones-<view>.png)
  python tools/shipgen/paint_proposal.py tudor front   # only these schemes and/or views (the sheet is remade from
                                                       # the images there)

Images go to docs/views/paint/: zones-<view>.png (each zone a flat colour, labelled, with a legend; what can't be
painted in grey), <scheme>-<view>.png for scheme in current (as built), iberian, tudor, whitegold, and schemes.png
(rows = views, columns = schemes). The painted textures are written to docs/views/paint/tex/.

Views: front (the great cabin's front from the waist, where the user's note 53 was made), waist (from the forecastle,
looking aft), quarter (outside, from the starboard quarter), cabin (inside the great cabin, looking aft), stern (from
astern). The sails are left off; the doors (vanilla blocks the mod draws) are stand-ins.
"""
import math
import os
import sys
import time

sys.dont_write_bytecode = True

import numpy as np  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import paint_zones as pz  # noqa: E402

OUT = pz.OUT
TEXDIR = os.path.join(OUT, "tex")
SIZE = (960, 640)
SS = 2                      # supersampling


def _modules():
    for attempt in range(10):
        try:
            import texview
            import view
            return texview, view
        except Exception as e:  # noqa: BLE001  (the generator being edited)
            print(f"import failed ({type(e).__name__}: {e}); trying again in 60 s")
            for m in ("texview", "view"):
                sys.modules.pop(m, None)
            time.sleep(60)
    raise SystemExit("couldn't import texview/view")


texview, view = _modules()


# ==================================================================== painted textures
def _lum(img):
    return img[..., :3] @ np.array([0.299, 0.587, 0.114])


def _smooth_noise(seed, n=32, cells=5):
    rng = np.random.default_rng(seed)
    small = Image.fromarray((rng.random((cells, cells)) * 255).astype(np.uint8))
    return np.asarray(small.resize((n, n), Image.BILINEAR), float) / 255


def paint(base, rgb, grain=0.32, wear=0.035, seed=0):
    """Paint on a wood texture: its colour, the grain showing a little through it, the seams between the planks still
    dark, a few specks worn through to the wood."""
    lum = _lum(base)
    rel = lum / max(lum.mean(), 1e-6)
    shade = 1 + grain * (rel - 1)
    seam = np.clip((0.6 - rel) / 0.35, 0, 1)
    shade = shade * (1 - 0.55 * seam)
    col = np.asarray(rgb, float)[None, None, :] / 255 * shade[..., None]
    rng = np.random.default_rng(seed)
    worn = (rng.random(lum.shape) < wear) | ((seam > 0.3) & (rng.random(lum.shape) < wear * 3))
    col[worn] = 0.5 * col[worn] + 0.5 * base[..., :3][worn]
    col *= (0.96 + 0.08 * _smooth_noise(seed + 7))[..., None]
    return np.concatenate([np.clip(col, 0, 1), base[..., 3:]], axis=-1)


def gilt(base, seed=0):
    """Gold leaf on carved wood: the wood's light and dark mapped onto a ramp from shadowed to burnished gold."""
    lum = _lum(base)
    rel = lum / max(lum.mean(), 1e-6)
    t = np.clip(0.55 + 0.8 * (rel - 1), 0, 1)
    stops = np.array([0.0, 0.3, 0.55, 0.8, 1.0])
    ramp = np.array([(96, 60, 18), (160, 112, 34), (212, 166, 62), (240, 206, 112), (255, 238, 168)], float) / 255
    col = np.stack([np.interp(t, stops, ramp[:, k]) for k in range(3)], axis=-1)
    rng = np.random.default_rng(seed)
    spark = rng.random(lum.shape) < 0.03
    col[spark] = np.minimum(1, col[spark] * 1.18)
    return np.concatenate([col, base[..., 3:]], axis=-1)


def limewash(base, seed=0):
    """Limewash: thin and chalky, the grain and seams showing through, the wood's colour coming through in places."""
    lum = _lum(base)
    rel = lum / max(lum.mean(), 1e-6)
    shade = 1 + 0.4 * (rel - 1)
    seam = np.clip((0.6 - rel) / 0.35, 0, 1)
    shade = shade * (1 - 0.45 * seam)
    col = np.array([228, 225, 212], float)[None, None, :] / 255 * shade[..., None]
    thin = np.clip((_smooth_noise(seed + 3, cells=6) - 0.55) * 1.6, 0, 0.35)[..., None]
    col = col * (1 - thin) + np.clip(base[..., :3] * 1.35, 0, 1) * thin
    return np.concatenate([np.clip(col, 0, 1), base[..., 3:]], axis=-1)


PAINTS = {                     # name: colour, how much grain shows
    "redochre": ((148, 60, 40), 0.34),
    "redochre-dark": ((116, 46, 32), 0.34),
    "yellowochre": ((198, 150, 68), 0.3),
    "deepgreen": ((44, 80, 58), 0.3),
    "tudorgreen": ((46, 114, 74), 0.3),
    "leadwhite": ((226, 222, 204), 0.28),
    "tudorred": ((150, 40, 36), 0.3),
    "white": ((238, 236, 228), 0.24),
    "pale": ((222, 210, 178), 0.3),
    "blue": ((42, 66, 138), 0.3),
    "black": ((40, 36, 34), 0.4),
}
TEX = {}                       # name -> {variant: RGBA float}; variants 1..4 on vanilla's oak planks, "t" on debarked oak


def make_textures(save=True):
    bases = {str(k): texview.texture(f"game:block/wood/planks/oak{k}") for k in range(1, 5)}
    bases["t"] = texview.texture("game:block/wood/debarked/oak")
    for i, (name, (rgb, grain)) in enumerate(PAINTS.items()):
        TEX[name] = {v: paint(b, rgb, grain, seed=17 * i + j) for j, (v, b) in enumerate(bases.items())}
    TEX["gilt"] = {v: gilt(b, seed=j) for j, (v, b) in enumerate(bases.items())}
    TEX["limewash"] = {v: limewash(b, seed=j) for j, (v, b) in enumerate(bases.items())}
    # the sea (outside views) and the doors' stand-ins, as texture codes texview knows
    rng = np.random.default_rng(5)
    sea = np.ones((32, 32, 4))
    sea[..., :3] = np.array([44, 82, 96]) / 255 * (0.93 + 0.1 * _smooth_noise(9, cells=8) +
                                                  0.03 * rng.random((32, 32)))[..., None]
    texview._cache["paint:sea"] = sea
    panel = texview._png("game", "block/wood/door/sleek/oak")
    if panel is None:
        panel = bases["1"]
    solid = np.concatenate([panel, panel], axis=0)               # 32 x 64: the door's two blocks
    windowed = solid.copy()
    windowed[6:26, 6:26, 3] = 0.0                                # its upper panel glazed (see-through here)
    windowed[15:17, 6:26] = panel[15:17, 6:26]                   # a glazing bar each way
    windowed[6:26, 15:17] = panel[6:26, 15:17]
    windowed[15:17, 6:26, 3] = windowed[6:26, 15:17, 3] = 1.0
    texview._cache["paint:door-solid"] = solid
    texview._cache["paint:door-windowed"] = windowed
    if save:
        os.makedirs(TEXDIR, exist_ok=True)
        for name, vs in TEX.items():
            for v, img in vs.items():
                Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), "RGBA").save(
                    os.path.join(TEXDIR, f"{name}-{v}.png"))


def variant_of(code):
    base = code.split("++")[0]
    if "/planks/" in base and base[-1] in "1234":
        return base[-1]
    return "t" if ("debarked" in base or "metal/" in base) else "1"


# ==================================================================== what each pixel is drawn with
def _sample(tex, u, v):
    th, tw = tex.shape[:2]
    px = np.clip((np.nan_to_num(u) / 16 * tw).astype(int) % tw, 0, tw - 1)
    py = np.clip((np.nan_to_num(v) / 16 * th).astype(int), 0, th - 1)
    t = tex[py, px]
    return t[:, :3], t[:, 3]


class Keep:
    """The texture it has now."""
    pos = False

    def sample(self, u, v, P, n, code):
        return _sample(texview.texture(code), u, v)


class Solid:
    """A painted texture (paint over the plank or timber the part is made of)."""
    pos = False

    def __init__(self, name):
        self.name = name

    def sample(self, u, v, P, n, code):
        rgb, _ = _sample(TEX[self.name][variant_of(code)], u, v)
        _, a = _sample(texview.texture(code), u, v)
        return rgb, a


class Flat:
    """One flat colour with a little of the texture's light and dark (the zone images)."""
    pos = False

    def __init__(self, rgb):
        self.rgb = np.asarray(rgb, float) / 255

    def sample(self, u, v, P, n, code):
        rgb, a = _sample(texview.texture(code), u, v)
        lum = rgb @ np.array([0.299, 0.587, 0.114])
        f = 0.82 + 0.18 * np.clip(lum / 0.42, 0, 2)
        return np.clip(self.rgb[None, :] * f[:, None], 0, 1), a


class Grey:
    """What isn't paintable, in the zone images: its texture, greyed and lightened."""
    pos = False

    def sample(self, u, v, P, n, code):
        rgb, a = _sample(texview.texture(code), u, v)
        lum = rgb @ np.array([0.299, 0.587, 0.114])
        g = 0.35 + 0.45 * lum
        return np.stack([g, g, g], axis=-1), a


class Pattern:
    """A pattern laid out on her in the ship's own coordinates (fn(P, n) -> which of fams each pixel takes)."""
    pos = True

    def __init__(self, fn, fams):
        self.fn, self.fams = fn, fams

    def sample(self, u, v, P, n, code):
        idx = self.fn(P, n)
        rgb = np.zeros((len(u), 3))
        a = np.zeros(len(u))
        for k, f in enumerate(self.fams):
            m = idx == k
            if m.any():
                rgb[m], a[m] = f.sample(u[m], v[m], P[m] if P is not None else None, n, code)
        return rgb, a


class Sea:
    """The sea round her (not inside her hull)."""
    pos = True

    def sample(self, u, v, P, n, code):
        rgb, a = _sample(texview.texture("paint:sea"), u, v)
        C = pz._C()
        inside = np.abs(P[:, 2]) < C.half_width(P[:, 0], np.full(len(P), C.WATER)) + 0.02
        return rgb, np.where(inside, 0.0, 1.0)


KEEP = Keep()


# ---- patterns
def _axes(P, n):
    """Along and up a surface: x along her sides, z across a transom or a castle front."""
    a = P[:, 2] if abs(n[0]) > abs(n[2]) else P[:, 0]
    return a, P[:, 1]


def chevrons(P, n):
    """Panels between the frames (a divider over each), chevrons in two colours in each: 0, 1 the chevrons, 2 the
    dividers."""
    C = pz._C()
    a, b = _axes(P, n)
    if abs(n[0]) > abs(n[2]):
        u = np.mod(a + 0.75, 1.5)
    else:
        u = np.mod(a - C.frame_stations()[0], 1.5)
    idx = (np.floor((b + np.abs(u - 0.75)) / 0.25).astype(int) % 2)
    idx[(u < 0.05) | (u > 1.45)] = 2
    return idx


def lozenges(P, n):
    a, b = _axes(P, n)
    s = 0.3
    return (np.floor((a + b) / s).astype(int) + np.floor((a - b) / s).astype(int)) % 2


def frieze_diamonds(P, n):
    """A band with a lozenge every half block: 0 the ground, 1 the lozenges."""
    C = pz._C()
    a, b = _axes(P, n)
    bc = C.rail_y(P[:, 0]) - 0.375
    d = np.abs(np.mod(a, 0.5) - 0.25) / 0.17 + np.abs(b - bc) / 0.095
    return (d < 1).astype(int)


def panels(P, n):
    """Painted panelling: a chair rail, a lower and an upper panel between each two frames (across a wall, every 1.2),
    outlined: 0 the field, 1 the lines."""
    C = pz._C()
    a, b = _axes(P, n)
    floor = np.where(b < C.QUARTER - 0.3, C.MAIN, C.QUARTER)
    ceil = np.where(b < C.QUARTER - 0.3, C.QUARTER - 0.5, C.POOP - 0.5)
    if abs(n[0]) > abs(n[2]):
        lo = np.floor((a + 0.6) / 1.2) * 1.2 - 0.6
        hi = lo + 1.2
    else:
        S = np.asarray(C.frame_stations(), float)
        k = np.clip(np.searchsorted(S, a) - 1, 0, len(S) - 2)
        lo, hi = S[k] + 0.125, S[k + 1] - 0.125
    ins, t = 0.16, 0.025
    a0, a1 = lo + ins, hi - ins
    line = np.abs(b - (floor + 0.93)) < 0.03
    for r0, r1 in ((floor + 0.18, floor + 0.78), (floor + 1.08, ceil - 0.22)):
        inb = (b > r0 - t) & (b < r1 + t)
        ina = (a > a0 - t) & (a < a1 + t)
        line |= inb & ((np.abs(a - a0) < t) | (np.abs(a - a1) < t))
        line |= ina & ((np.abs(b - r0) < t) | (np.abs(b - r1) < t))
    return line.astype(int)


# ==================================================================== the schemes (examples of what a player could do)
SCHEME_TITLES = {"current": "As built (oak, tar)", "iberian": "Iberian carrack, c. 1500-1550",
                 "tudor": "Tudor royal (Anthony Roll, 1546)", "whitegold": "White and gold (18th c. French)"}


def schemes():
    S, G, lime = Solid, Solid("gilt"), Solid("limewash")
    ib_panels = Pattern(panels, [S("deepgreen"), G])
    iberian = {
        "topsides": S("redochre"), "frieze": Pattern(frieze_diamonds, [S("black"), S("leadwhite")]),
        "sterncastle-sides": S("redochre"), "forecastle-sides": S("redochre"), "transom": S("redochre"),
        "castle-mouldings": S("yellowochre"), "rail-caps": S("yellowochre"), "side-windows": S("yellowochre"),
        "stern-gallery": S("yellowochre"), "stern-carvings": G, "figurehead": G,
        "waist-bulwarks": S("redochre"), "castle-bulwarks": S("redochre"), "bulwark-frames": S("redochre-dark"),
        "great-cabin-front": S("redochre"), "poop-cabin-front": S("redochre"), "forecastle-front": S("redochre"),
        "front-mouldings": S("yellowochre"), "front-capitals": G, "door-casings": S("yellowochre"), "balusters": G,
        "balustrade-rails": S("yellowochre"), "stairs": S("redochre"), "companion-house": S("redochre"),
        "companion-house-trim": S("yellowochre"), "great-cabin-walls": ib_panels, "poop-cabin-walls": ib_panels,
        "cabin-trim": G, "forecastle-room": lime, "lower-deck": lime, "hold": lime,
    }
    chev = Pattern(chevrons, [S("tudorgreen"), S("leadwhite"), G])
    loz = Pattern(lozenges, [S("tudorgreen"), S("leadwhite")])
    td_panels = Pattern(panels, [S("tudorred"), G])
    tudor = {
        "topsides": chev, "frieze": chev, "sterncastle-sides": chev, "forecastle-sides": chev, "transom": chev,
        "castle-mouldings": G, "rail-caps": G, "side-windows": G, "stern-gallery": G, "stern-carvings": G,
        "figurehead": G, "waist-bulwarks": S("redochre"), "castle-bulwarks": S("redochre"),
        "bulwark-frames": S("redochre-dark"), "great-cabin-front": loz, "poop-cabin-front": loz,
        "forecastle-front": loz, "front-mouldings": G, "front-capitals": G, "door-casings": G, "balusters": G,
        "balustrade-rails": G, "stairs": S("redochre"), "companion-house": loz, "companion-house-trim": G,
        "great-cabin-walls": td_panels, "poop-cabin-walls": td_panels, "cabin-trim": G, "forecastle-room": lime,
        "lower-deck": lime, "hold": lime,
    }
    wg_panels = Pattern(panels, [S("white"), G])
    whitegold = {
        "topsides": S("white"), "frieze": S("blue"), "sterncastle-sides": S("white"), "forecastle-sides": S("white"),
        "transom": S("white"), "castle-mouldings": G, "rail-caps": G, "side-windows": G, "stern-gallery": G,
        "stern-carvings": G, "figurehead": G, "waist-bulwarks": S("pale"), "castle-bulwarks": S("pale"),
        "bulwark-frames": S("pale"), "great-cabin-front": S("white"), "poop-cabin-front": S("white"),
        "forecastle-front": S("white"), "front-mouldings": G, "front-capitals": G, "door-casings": G, "balusters": G,
        "balustrade-rails": G, "stairs": S("pale"), "companion-house": S("white"), "companion-house-trim": G,
        "great-cabin-walls": wg_panels, "poop-cabin-walls": wg_panels, "cabin-trim": G, "cabin-ceilings": S("white"),
        "forecastle-room": lime, "lower-deck": lime, "hold": lime,
    }
    return {"current": {}, "iberian": iberian, "tudor": tudor, "whitegold": whitegold}


# a distinct colour for each zone (the zone images)
ZONE_COLOURS = {
    "wales": (52, 56, 150), "topsides": (232, 120, 36), "frieze": (250, 232, 60), "sterncastle-sides": (196, 52, 52),
    "forecastle-sides": (150, 84, 200), "transom": (52, 176, 166), "castle-mouldings": (34, 112, 40),
    "rail-caps": (240, 86, 178), "side-windows": (96, 214, 240), "stern-gallery": (255, 156, 116),
    "stern-carvings": (255, 210, 0), "nameboard": (24, 24, 24), "figurehead": (255, 190, 70),
    "waist-bulwarks": (214, 64, 64), "castle-bulwarks": (110, 160, 255), "bulwark-frames": (112, 70, 28),
    "great-cabin-front": (70, 196, 84), "poop-cabin-front": (196, 176, 255), "forecastle-front": (0, 140, 196),
    "front-mouldings": (255, 132, 0), "front-capitals": (255, 255, 128), "door-casings": (140, 36, 120),
    "balusters": (255, 226, 0), "balustrade-rails": (0, 84, 160), "stairs": (184, 124, 64),
    "companion-house": (150, 220, 56), "companion-house-trim": (120, 36, 36), "great-cabin-walls": (36, 156, 116),
    "poop-cabin-walls": (232, 146, 200), "cabin-trim": (250, 196, 40), "cabin-ceilings": (164, 164, 224),
    "forecastle-room": (204, 200, 116), "lower-deck": (146, 210, 210), "hold": (212, 180, 146),
    # (0.3.12's zones: the rooms' frames, beams and ceilings apart)
    "cabin-beams": (90, 90, 170), "forecastle-frames": (150, 120, 40), "forecastle-beams": (120, 120, 60),
    "forecastle-ceiling": (230, 230, 170), "lower-deck-frames": (40, 130, 130), "lower-deck-beams": (60, 90, 110),
    "lower-deck-ceiling": (200, 240, 240), "hold-frames": (150, 110, 70), "hold-beams": (110, 80, 50),
    "hold-ceiling": (240, 220, 200),
    # (0.3.13, the user's notes 134, 135) the window casings and the cabins' frames apart
    "window-casings": (0, 200, 255), "cabin-frames": (180, 120, 0),
}

VIEWS = {   # name: title, eye, target, fov, light, reach
    "front": ("The great cabin's front from the waist", (0.2, 5.35, -3.4), (-3.0, 6.3, 0.0), 84,
              (0.55, 0.8, 0.25), 40),
    "waist": ("The waist from the forecastle, looking aft", (13.2, 8.5, 2.5), (-3.0, 5.0, 1.4), 74,
              (0.35, 0.85, 0.4), 45),
    "quarter": ("Outside, from the starboard quarter", (-34.0, 9.5, 21.0), (-4.5, 6.6, 0.3), 46,
                (-0.45, 0.8, 0.55), 90),
    "cabin": ("Inside the great cabin, looking aft", (-3.6, 5.35, 1.1), (-15.0, 4.9, -0.3), 80,
              (0.35, 0.85, 0.4), 30),
    "stern": ("The stern, from astern", (-40.0, 7.2, 5.5), (-16.0, 6.4, 0.0), 36, (-0.6, 0.7, 0.35), 90),
}


# ==================================================================== what's drawn
class Item:
    __slots__ = ("pts", "uv", "n", "code", "zone", "kind")

    def __init__(self, pts, uv, n, code, zone, kind="ship"):
        self.pts, self.uv, self.n, self.code, self.zone, self.kind = pts, uv, n, code, zone, kind


def scene(d, boxes, zmap):
    """Every quad and voxel face as drawn (texview's way), with its zone; the doors' stand-ins; the sea."""
    qz, fz = {}, {}
    for code, zd in zmap.items():
        for i in zd["quads"]:
            qz[i] = code
        for bf in zd["voxel_faces"]:
            fz[tuple(bf)] = code
    items = []
    for i, q in enumerate(d.quads):
        g = q["group"]
        if g.startswith(("bottomclad", "sail:")):
            continue
        m = d.materials[d.mat_index[q["mat"]]]
        if not m.render:
            continue
        code = m.textures[(i * 7 + q.get("row", 0)) % len(m.textures)]
        u0, v0, u1, v1 = q["uv"]
        pts = np.asarray(q["pts"], float)
        n = None
        box = q.get("_pinfo", {}).get("add_box_mesh")
        if box is not None:
            # (a box's face is lit by its outward normal: some of the generator's boxes are wound inside out, the
            # stern gallery's and the name board's all of them (their R is left-handed), and would show dark here)
            _, c, n = pz.quad_geometry(q)
            if np.dot(n, c - np.asarray(box["center"], float)) < 0:
                n = -n
        items.append(Item(pts, np.array([(u0, v1), (u1, v1), (u1, v0), (u0, v0)], float), n, code, qz.get(i)))
    for bi, b in enumerate(boxes):
        m = d.materials[b["mat"]]
        code = m.textures[b["variant"] % len(m.textures)]
        lo, hi = b["lo"], b["hi"]
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        for f in pz.VOXEL_FACES:
            if b["faces"][f] == 0:
                continue
            if f in ("east", "west"):
                x = x1 if f == "east" else x0
                pts = [(x, y0, z0), (x, y0, z1), (x, y1, z1), (x, y1, z0)]
            elif f in ("up", "down"):
                y = y1 if f == "up" else y0
                pts = [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)]
            else:
                z = z1 if f == "south" else z0
                pts = [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
            ua = d._uv_aabb(f, lo, hi)
            if f in ("east", "west"):
                uv = [(ua[0], ua[3]), (ua[2], ua[3]), (ua[2], ua[1]), (ua[0], ua[1])]
            elif f in ("up", "down"):
                uv = [(ua[0], ua[1]), (ua[2], ua[1]), (ua[2], ua[3]), (ua[0], ua[3])]
            else:
                uv = [(ua[0], ua[3]), (ua[2], ua[3]), (ua[2], ua[1]), (ua[0], ua[1])]
            items.append(Item(np.asarray(pts, float), np.asarray(uv, float), np.asarray(pz.VOXEL_DIRS[f], float),
                              code, fz.get((bi, f))))
    # the doors (vanilla blocks the mod draws: stand-ins, shut), both faces
    C = pz._C()
    for k, (x0, x1, yb) in enumerate(pz._doors()):
        xm = (x0 + x1) / 2
        code = "paint:door-windowed" if k == 0 else "paint:door-solid"
        for sgn in (1, -1):
            x = xm + sgn * 0.05
            pts = [(x, yb, -0.5), (x, yb, 0.5), (x, yb + 2.0, 0.5), (x, yb + 2.0, -0.5)]
            if sgn < 0:
                pts = [pts[1], pts[0], pts[3], pts[2]]
            uv = [(0, 16), (16, 16), (16, 0), (0, 0)] if sgn > 0 else [(16, 16), (0, 16), (0, 0), (16, 0)]
            items.append(Item(np.asarray(pts, float), np.asarray(uv, float), np.array([sgn, 0.0, 0.0]), code, None,
                              "door"))
    # the sea
    step = 10.0
    for x in np.arange(-90, 90, step):
        for z in np.arange(-90, 90, step):
            pts = [(x, C.WATER, z), (x, C.WATER, z + step), (x + step, C.WATER, z + step), (x + step, C.WATER, z)]
            items.append(Item(np.asarray(pts, float), np.array([(0, 0), (0, 16), (16, 16), (16, 0)], float),
                              np.array([0.0, 1.0, 0.0]), "paint:sea", None, "sea"))
    return items


# ==================================================================== drawing
def _clip_near(C3, T3, P3, near):
    """A triangle in camera space cut at the near plane: [(cam, uv, world)] triangles."""
    keep = C3[:, 2] >= near
    if keep.all():
        return [(C3, T3, P3)]
    out = []
    for i in range(3):
        j = (i + 1) % 3
        if keep[i]:
            out.append((C3[i], T3[i], P3[i]))
        if keep[i] != keep[j]:
            t = (near - C3[i, 2]) / (C3[j, 2] - C3[i, 2])
            out.append((C3[i] + (C3[j] - C3[i]) * t, T3[i] + (T3[j] - T3[i]) * t, P3[i] + (P3[j] - P3[i]) * t))
    if len(out) < 3:
        return []
    tris = []
    for k in range(1, len(out) - 1):
        a, b, c = out[0], out[k], out[k + 1]
        tris.append((np.array([a[0], b[0], c[0]]), np.array([a[1], b[1], c[1]]), np.array([a[2], b[2], c[2]])))
    return tris


def render(items, fam_of, eye, target, fov, light, size=SIZE, ss=SS, reach=60.0):
    """items drawn with fam_of(item) -> family, lit by their normals as texview (and the game) light them; a sky behind.
    Returns the image and the zone each pixel shows (or None)."""
    W, H = size[0] * ss, size[1] * ss
    e, R = view.look_at(eye, target)
    focal = 0.5 * W / math.tan(math.radians(fov) / 2)
    L = np.asarray(light, float)
    L /= np.linalg.norm(L)
    rows = np.arange(H) + 0.5
    dirs = np.stack([np.zeros(H), (H / 2 - rows) / focal, np.ones(H)], axis=1) @ R
    elev = dirs[:, 1] / np.linalg.norm(dirs, axis=1)
    t = np.clip(elev * 2.4, 0, 1)[:, None]
    horizon, zenith = np.array([214, 226, 236], float), np.array([118, 160, 210], float)
    bg = horizon * (1 - t) + zenith * t
    img = np.repeat(bg[:, None, :], W, axis=1)
    zbuf = np.full((H, W), np.inf)
    zid = np.full((H, W), -1, np.int16)
    near = 0.05
    codes = {c: k for k, c in enumerate(pz.ZONE_CODES)}
    for it in items:
        if it.kind == "ship" and np.min(np.linalg.norm(it.pts - e, axis=1)) > reach:
            continue
        pts = it.pts
        n = it.n if it.n is not None else (np.cross(pts[1] - pts[0], pts[3] - pts[0]) +
                                           np.cross(pts[3] - pts[2], pts[1] - pts[2]))
        ln = np.linalg.norm(n)
        if ln < 1e-12:
            continue
        nn = n / ln
        shade = max(0.45, 0.5 + 0.5 * float(np.dot(nn, L)), 0.95 * float(nn[1]))
        cam = (pts - e) @ R.T
        if (cam[:, 2] < near).all():
            continue
        fam = fam_of(it)
        zk = codes.get(it.zone, -1)
        for tri in ((0, 1, 2), (0, 2, 3)):
            for C3, T3, P3 in _clip_near(cam[list(tri)], it.uv[list(tri)], pts[list(tri)], near):
                sx = W / 2 + focal * C3[:, 0] / C3[:, 2]
                sy = H / 2 - focal * C3[:, 1] / C3[:, 2]
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
                iz = w0 / C3[0, 2] + w1 / C3[1, 2] + w2 / C3[2, 2]
                z = 1.0 / np.maximum(iz, 1e-9)
                sub = zbuf[y0:y1 + 1, x0:x1 + 1]
                win = inside & (z < sub)
                if not win.any():
                    continue
                a0, a1, a2, zz = w0[win] / C3[0, 2], w1[win] / C3[1, 2], w2[win] / C3[2, 2], z[win]
                u = (a0 * T3[0, 0] + a1 * T3[1, 0] + a2 * T3[2, 0]) * zz
                v = (a0 * T3[0, 1] + a1 * T3[1, 1] + a2 * T3[2, 1]) * zz
                P = None
                if fam.pos:
                    P = (a0[:, None] * P3[0] + a1[:, None] * P3[1] + a2[:, None] * P3[2]) * zz[:, None]
                rgb, alpha = fam.sample(u, v, P, nn, it.code)
                op = alpha > 0.3
                if not op.any():
                    continue
                yy, xx = np.nonzero(win)
                yy, xx = yy[op] + y0, xx[op] + x0
                zbuf[yy, xx] = zz[op]
                img[yy, xx] = rgb[op] * 255 * shade
                zid[yy, xx] = zk
    fog = np.clip((zbuf - 30) / 90, 0, 0.5)[..., None]
    fog[np.isinf(zbuf)] = 0
    img = img * (1 - fog) + bg[:, None, :] * fog
    if ss > 1:
        img = img.reshape(size[1], ss, size[0], ss, 3).mean(axis=(1, 3))
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)), zid


def _font(size):
    return view._font(size)


def caption(img, text):
    over = Image.new("RGBA", img.size, (0, 0, 0, 0))
    dr = ImageDraw.Draw(over)
    f = _font(17)
    w = dr.textlength(text, font=f)
    dr.rectangle([0, 0, w + 16, 26], fill=(0, 0, 0, 150))
    dr.text((8, 4), text, fill=(255, 255, 255, 255), font=f)
    return Image.alpha_composite(img.convert("RGBA"), over).convert("RGB")


def _label_spots(zid, ss, min_frac=0.004):
    """For each zone showing enough: a spot well inside its biggest patch (in the downsampled image's pixels)."""
    H, W = zid.shape
    out = {}
    total = H * W
    for k in np.unique(zid):
        if k < 0:
            continue
        m = zid == k
        cnt = int(m.sum())
        if cnt < min_frac * total:
            continue
        core = m
        for _ in range(60):                       # erode while something's left
            e = core.copy()
            e[1:, :] &= core[:-1, :]
            e[:-1, :] &= core[1:, :]
            e[:, 1:] &= core[:, :-1]
            e[:, :-1] &= core[:, 1:]
            if e.sum() < 4:
                break
            core = e
        ys, xs = np.nonzero(core)
        cy, cx = ys.mean(), xs.mean()
        j = int(np.argmin((ys - cy) ** 2 + (xs - cx) ** 2))
        out[int(k)] = (xs[j] / ss, ys[j] / ss, cnt / total)
    return out


def zone_image(img, zid, title):
    """The zone render with each zone's code on it and a legend at the side."""
    spots = _label_spots(zid, SS)
    legend_w = 380
    out = Image.new("RGB", (img.width + legend_w, img.height), (250, 250, 248))
    out.paste(img, (0, 0))
    over = Image.new("RGBA", out.size, (0, 0, 0, 0))
    dr = ImageDraw.Draw(over)
    f, fb = _font(13), _font(16)
    for k, (x, y, _) in spots.items():
        code = pz.ZONE_CODES[k]
        w = dr.textlength(code, font=f)
        dr.rectangle([x - w / 2 - 3, y - 9, x + w / 2 + 3, y + 8], fill=(0, 0, 0, 165))
        dr.text((x - w / 2, y - 8), code, fill=(255, 255, 255, 255), font=f)
    out = Image.alpha_composite(out.convert("RGBA"), over).convert("RGB")
    dr = ImageDraw.Draw(out)
    dr.text((img.width + 12, 10), title, fill=(20, 20, 20), font=fb)
    dr.text((img.width + 12, 32), "zones seen here (grey: not paintable)", fill=(90, 90, 90), font=f)
    seen = sorted(spots.items(), key=lambda kv: -kv[1][2])
    # zones showing but too small to label, too
    small = [int(k) for k in np.unique(zid) if k >= 0 and int(k) not in spots and (zid == k).sum() > 40 * SS * SS]
    yy = 56
    for k in [k for k, _ in seen] + small:
        code = pz.ZONE_CODES[k]
        dr.rectangle([img.width + 12, yy, img.width + 34, yy + 15], fill=ZONE_COLOURS[code], outline=(60, 60, 60))
        dr.text((img.width + 42, yy), code + ("" if k in spots else "  (small)"), fill=(20, 20, 20), font=f)
        yy += 19
        if yy > img.height - 20:
            break
    return out


# ==================================================================== main
def main(args):
    sch = schemes()
    only_s = [a for a in args if a in sch]
    only_v = [a for a in args if a in VIEWS]
    do_zones = "zones" in args or not (only_s or [a for a in args if a == "schemes"])
    do_schemes = "zones" not in args or bool(only_s)
    os.makedirs(OUT, exist_ok=True)
    make_textures()
    d = pz.build()
    boxes, _ = d.mesh_voxels()
    zmap = pz.zones(d, boxes)
    items = scene(d, boxes, zmap)
    views = only_v or list(VIEWS)
    grey, sea = Grey(), Sea()
    for k_, c_ in enumerate(pz.ZONE_CODES):      # (a zone with no colour of its own yet: one made up from its index)
        ZONE_COLOURS.setdefault(c_, ((53 * k_) % 200 + 40, (97 * k_) % 200 + 40, (151 * k_) % 200 + 40))
    flats = {c: Flat(rgb) for c, rgb in ZONE_COLOURS.items()}
    if do_zones:
        for vname in views:
            title, eye, target, fov, light, reach = VIEWS[vname]
            t0 = time.time()

            def fam(it):
                if it.kind == "sea":
                    return sea
                return flats[it.zone] if it.zone else grey
            img, zid = render(items, fam, eye, target, fov, light, reach=reach)
            out = zone_image(img, zid, f"Paint zones: {title.lower()}")
            p = os.path.join(OUT, f"zones-{vname}.png")
            out.save(p)
            print(p, f"{time.time() - t0:.0f} s")
    if do_schemes:
        for sname in only_s or list(sch):
            table = sch[sname]
            for vname in views:
                title, eye, target, fov, light, reach = VIEWS[vname]
                t0 = time.time()

                def fam(it, table=table):
                    if it.kind == "sea":
                        return sea
                    return table.get(it.zone, KEEP) if it.zone else KEEP
                img, _ = render(items, fam, eye, target, fov, light, reach=reach)
                img = caption(img, f"{SCHEME_TITLES[sname]}  -  {title.lower()}")
                p = os.path.join(OUT, f"{sname}-{vname}.png")
                img.save(p)
                print(p, f"{time.time() - t0:.0f} s")
        sheet()


def sheet():
    """docs/views/paint/schemes.png: rows the views, columns the schemes, from the images there."""
    names = list(SCHEME_TITLES)
    cw, chh, left, top, gap = 560, 373, 150, 44, 6
    W = left + len(names) * (cw + gap)
    H = top + len(VIEWS) * (chh + gap)
    out = Image.new("RGB", (W, H), (245, 244, 240))
    dr = ImageDraw.Draw(out)
    f, fb = _font(15), _font(19)
    for c, s in enumerate(names):
        x = left + c * (cw + gap)
        dr.text((x + 6, 12), SCHEME_TITLES[s], fill=(20, 20, 20), font=fb)
    for r, (vname, (title, *_)) in enumerate(VIEWS.items()):
        y = top + r * (chh + gap)
        words, line, lines = title.split(), "", []
        for w in words:
            if dr.textlength(line + " " + w, font=f) > left - 16 and line:
                lines.append(line)
                line = w
            else:
                line = (line + " " + w).strip()
        lines.append(line)
        for i, ln in enumerate(lines):
            dr.text((8, y + chh / 2 - 10 * len(lines) + 20 * i), ln, fill=(20, 20, 20), font=f)
        for c, s in enumerate(names):
            p = os.path.join(OUT, f"{s}-{vname}.png")
            if os.path.exists(p):
                im = Image.open(p).convert("RGB").resize((cw, chh), Image.LANCZOS)
                out.paste(im, (left + c * (cw + gap), y))
    p = os.path.join(OUT, "schemes.png")
    out.save(p)
    print(p)


if __name__ == "__main__":
    main(sys.argv[1:])
