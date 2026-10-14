"""
Hansa's ship's boat since 0.3.3 (the user, 2026-09-30): vanilla's sailboat made a rowing boat. From the game's own
model (shapes/entity/nonliving/boat/sailed.json): the mast, sail, rigging, rudder, tiller and the two stern seats come
out, the second midships section too (a block shorter: 7.5 long), and a thwart goes across the middle with a pair of
oars on thole pins: at rest they lie out over the water on their pins, rowing they sweep ("row"), stowed on a ship
they're shipped along the sides (the item's model). Vanilla's places for chests, baskets, vessels, boat seats (and so
animals) stay where they were (bar the two the rower sits on), plus two at the stern where the seats were.

Writes assets/hansa/shapes/entity/rowboat.json (in the water) and rowboat-stowed.json (the item: on the gallows,
in hand), and previews in docs/rowboat-*.png.

  python rowboat.py
"""
import copy
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import vsshape as vs  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ASSETS = os.path.join(ROOT, "assets", "hansa")
DOCS = os.path.join(ROOT, "docs")
VANILLA = os.path.join(os.environ.get("APPDATA", ""), "Vintagestory", "assets", "survival", "shapes", "entity",
                       "nonliving", "boat", "sailed.json")

CUT = (-1.0, 0.0)          # the midships section taken out (model x, blocks): the stern closes up by 1
THWART_X = -1.53           # the rower's thwart (model x; the prow is toward -x)
THWART_Y = 0.78            # its top
PIVOT_X = -1.83            # the thole pins, a little forward of the thwart (the rower faces the bow and pushes)
PIVOT_Y = 1.36
PIN_Z = (-0.8, 1.92)       # on the gunwales (the boat's middle is at z 0.56)
INBOARD, OUTBOARD = 0.75, 1.75   # the oar's loom inboard and outboard of the pin
REST = (19.0, 15.0)        # at rest: the blade down to the water, trailing aft (degrees)


def model_box(name, lo, hi, tex="oak1", origin=None, rot=(0, 0, 0), children=None, aps=None, uv_scale=16):
    """A top-level element from model-space corners (blocks)."""
    lo, hi = np.asarray(lo, float) * 16, np.asarray(hi, float) * 16
    faces = {}
    size = hi - lo
    dims = {"north": (size[0], size[1]), "south": (size[0], size[1]), "east": (size[2], size[1]),
            "west": (size[2], size[1]), "up": (size[0], size[2]), "down": (size[0], size[2])}
    for f, (w, h) in dims.items():
        faces[f] = {"texture": "#" + tex, "uv": [0, 0, round(min(16.0, w), 3), round(min(16.0, h), 3)]}
    el = {"name": name, "from": [round(v, 4) for v in lo], "to": [round(v, 4) for v in hi], "faces": faces}
    if origin is not None:
        el["rotationOrigin"] = [round(v * 16, 4) for v in origin]
    for k, v in zip(("rotationX", "rotationY", "rotationZ"), rot):
        if v:
            el["rotationX" if k == "rotationX" else k] = v
    if children:
        el["children"] = children
    if aps:
        el["attachmentpoints"] = aps
    return el


def child_box(name, lo, hi, parent_from, tex="oak1"):
    """A child element given in model space, placed in an unrotated parent whose 'from' is parent_from (blocks)."""
    p = np.asarray(parent_from, float)
    return model_box(name, np.asarray(lo) - p, np.asarray(hi) - p, tex)


def oar(side):
    """One oar on its pin: the loom out along z (+z for the pin at the high-z gunwale), the blade at its end."""
    s = 1 if side > 0 else -1
    pz = PIN_Z[1] if s > 0 else PIN_Z[0]
    px, py = PIVOT_X, PIVOT_Y
    t = 0.035
    z0, z1 = (pz - INBOARD, pz + OUTBOARD) if s > 0 else (pz - OUTBOARD, pz + INBOARD)
    loom_lo, loom_hi = (px - t, py - t, z0), (px + t, py + t, z1)
    bz0, bz1 = (pz + OUTBOARD - 0.6, pz + OUTBOARD) if s > 0 else (pz - OUTBOARD, pz - OUTBOARD + 0.6)
    blade = child_box(f"OarBlade{'B' if s > 0 else 'A'}", (px - 0.016, py - 0.1, bz0), (px + 0.016, py + 0.1, bz1), loom_lo)
    grip_z = (pz - INBOARD, pz - INBOARD + 0.22) if s > 0 else (pz + INBOARD - 0.22, pz + INBOARD)
    grip = child_box(f"OarGrip{'B' if s > 0 else 'A'}", (px - 0.045, py - 0.045, grip_z[0]), (px + 0.045, py + 0.045, grip_z[1]),
                     loom_lo, tex="plainoak")
    return model_box(f"Oar{'B' if s > 0 else 'A'}", loom_lo, loom_hi, "oak", origin=(px, py, pz),
                     rot=(s * REST[0], s * REST[1], 0), children=[blade, grip])


def rowing_gear():
    """The thwart the rower sits on (and the seat's attachment point), the thole pins and the oars."""
    zmid = 0.56
    # the seat's point: on the thwart's middle, on a box the size of vanilla's seats (10 x 16 x 16), which is what the
    # game makes the seat's click box from (EntityBehaviorSelectionBoxes: the point's element's size); drawn not at all
    seat_ap = {"code": "RowSeatAP", "posX": "5", "posY": "2", "posZ": "8", "rotationX": "0", "rotationY": "0", "rotationZ": "0"}
    thwart = model_box("Thwart", (THWART_X - 0.17, THWART_Y - 0.08, -0.62), (THWART_X + 0.17, THWART_Y, 1.74), "oak1")
    ap_el = child_box("RowSeat", (THWART_X - 0.3125, THWART_Y - 0.125, zmid - 0.5), (THWART_X + 0.3125, THWART_Y + 0.875, zmid + 0.5),
                      (THWART_X - 0.17, THWART_Y - 0.08, -0.62))
    ap_el["faces"] = {}
    ap_el["attachmentpoints"] = [seat_ap]
    thwart["children"] = [ap_el]
    pins = []
    for i, pz in enumerate(PIN_Z):
        pins.append(model_box(f"TholePin{i}", (PIVOT_X - 0.035, 1.24, pz - 0.035), (PIVOT_X + 0.035, PIVOT_Y + 0.12, pz + 0.035),
                              "plainoak"))
        pins.append(model_box(f"TholeBlock{i}", (PIVOT_X - 0.16, 1.2, pz - 0.07), (PIVOT_X + 0.16, 1.28, pz + 0.07), "oak"))
    return [thwart] + pins + [oar(-1), oar(1)]


def row_animation():
    """A stroke and a recovery, 40 frames (the mod plays it while the boat moves under oars)."""
    def pose(ry, rx):
        return {"OarB": {"rotationX": rx, "rotationY": ry, "rotationZ": 0},
                "OarA": {"rotationX": -rx, "rotationY": -ry, "rotationZ": 0}}
    keys = [(0, -40, 4), (18, 20, 4), (22, 22, -8), (38, -38, -8)]
    return {"name": "Row", "code": "row", "quantityframes": 40, "onActivityStopped": "Rewind", "onAnimationEnd": "Repeat",
            "keyframes": [{"frame": f, "elements": pose(ry, rx)} for f, ry, rx in keys]}


def map_x(x):
    """Model x after the section is taken out."""
    a, b = CUT
    return x if x <= a else (a if x < b else x - (b - a))


def rebuild_hide_water(shape):
    """vanilla's water-hiding planes (flat, under ORIGIN), shortened like the hull: each leaf plane's model rectangle
    mapped by map_x, all made children of one plane again under ORIGIN."""
    origin_el, _ = vs.find(shape, "ORIGIN")
    om = vs.local_matrix(origin_el)
    inv = np.linalg.inv(om)
    rects = []
    for el, m, path in vs.walk(shape):
        if "hideWater" not in path:
            continue
        c = vs.corners(el, m)
        x0, x1 = map_x(c[:, 0].min()), map_x(c[:, 0].max())
        if x1 - x0 < 0.01:
            continue
        rects.append((el["name"], x0, x1, c[:, 1].min(), c[:, 2].min(), c[:, 2].max()))
    vs.remove(shape, "hideWater")

    def local(x0, x1, y, z0, z1):
        pts = np.array([inv @ np.array([x, y, z, 1.0]) for x in (x0, x1) for z in (z0, z1)])[:, :3] * 16
        return [round(v, 4) for v in pts.min(axis=0)], [round(v, 4) for v in pts.max(axis=0)]

    nul = {f: {"texture": "#null", "uv": [0, 0, 0, 0]} for f in ("north", "east", "south", "west", "up", "down")}
    main = max(rects, key=lambda r: (r[2] - r[1]) * (r[5] - r[4]))
    f0, t0 = local(main[1], main[2], main[3], main[4], main[5])
    root = {"name": "hideWater", "from": f0, "to": t0, "faces": copy.deepcopy(nul), "children": []}
    for name, x0, x1, y, z0, z1 in rects:
        if name == main[0]:
            continue
        f, t = local(x0, x1, y, z0, z1)
        root["children"].append({"name": name, "from": [a - b for a, b in zip(f, f0)], "to": [a - b for a, b in zip(t, f0)],
                                 "faces": copy.deepcopy(nul)})
    origin_el["children"].append(root)


def build():
    s = vs.load(VANILLA)
    # its textures are the game's (unqualified paths would be looked for in our own domain)
    s["textures"] = {k: v if ":" in v else "game:" + v for k, v in s["textures"].items()}
    # the sailing rig, the rudder and tiller, the stern seats and their foot rail, the midships section, and the
    # places the rower's thwart takes (vanilla's second pair)
    for name in ("MastORIGIN", "UpperMastStorage", "LowerMastStorage", "RatlineL", "RatlineR", "TillerORIGIN",
                 "SeatPlankN1", "SeatPlankS1", "Foot rail", "LeftStorage2", "RightStorage2", "LeftStorage3",
                 "RightStorage3", "MastRope1"):
        vs.remove(s, name)
    rebuild_hide_water(s)
    vs.remove(s, "ORIGIN-Section2")
    # two places at the stern where the seats were: vanilla's aftmost pair, before they move up with the stern
    for side in ("Left", "Right"):
        el, lst = vs.find(s, f"{side}Storage4")
        new = copy.deepcopy(el)
        new["name"] = f"{side}Storage5"
        for ap in new.get("attachmentpoints") or []:
            ap["code"] = ap["code"].replace("4", "5")
        lst.append(new)
    # the stern closes up: under ORIGIN (turned half round) its +x is the model's -x
    for name in ("ORIGIN-Prow2", "LeftStorage4", "RightStorage4"):
        el, _ = vs.find(s, name)
        vs.shift(el, (16 * (CUT[1] - CUT[0]), 0, 0))
    s["elements"].extend(rowing_gear())
    s["animations"] = [row_animation()]
    uv_within_texture(s["elements"])
    return s


def uv_within_texture(elements):
    """(Hansa 0.3.10) Every face's uv inside its texture, 0..16: vanilla's sailboat has a few running past it (a hull
    plank's v -0.5..4.5, the name plaque's 32 x 14 on #transparent, disabled #null faces), which in an atlas sample its
    neighbours. Shifted back inside where the face fits, else scaled down to fit (the plaque is transparent anyway)."""
    for el in elements:
        for face in (el.get("faces") or {}).values():
            uv = face.get("uv")
            if not uv or (min(uv) >= 0 and max(uv) <= 16):
                continue
            out = list(uv)
            for a, b in ((0, 2), (1, 3)):
                lo, hi = min(uv[a], uv[b]), max(uv[a], uv[b])
                k = min(1.0, 16.0 / (hi - lo)) if hi > lo else 1.0
                nlo = min(max(lo * k, 0.0), 16.0 - (hi - lo) * k)
                nhi = nlo + (hi - lo) * k
                out[a], out[b] = (nlo, nhi) if uv[a] <= uv[b] else (nhi, nlo)
            face["uv"] = [round(v, 4) for v in out]
        uv_within_texture(el.get("children") or [])


def stowed(shape):
    """The oars shipped: turned in along the sides, lying on the thwart (the item's model, and on a ship's gallows)."""
    st = copy.deepcopy(shape)
    for side in ("A", "B"):
        el, _ = vs.find(st, f"Oar{side}")
        s = 1 if side == "B" else -1
        el["rotationX"] = 0
        el["rotationY"] = s * 90
        vs.shift(el, (0, -0.52 * 16, -s * 0.36 * 16))
    st["animations"] = []
    return st


COLOURS = {"oak": (150, 112, 70), "oak1": (168, 128, 82), "plainoak": (185, 150, 105), "birch1": (205, 180, 140),
           "plain": (235, 228, 210), "iron1": (90, 90, 95), "rope": (140, 110, 70)}


def previews(shape, st):
    import view
    os.makedirs(DOCS, exist_ok=True)
    views = {
        "side": ((-1.5, 1.2, -9.0), (-1.5, 0.6, 0.56), 50, shape, None),
        "oblique": ((5.5, 5.5, 6.5), (-1.5, 0.6, 0.56), 55, shape, None),
        "top": ((-1.5, 10.5, 0.57), (-1.5, 0.0, 0.56), 50, shape, None),
        "rowing": ((5.5, 5.5, 6.5), (-1.5, 0.6, 0.56), 55, shape, {"OarB": {"rotationY": -40, "rotationX": 4},
                                                                  "OarA": {"rotationY": 40, "rotationX": -4}}),
        "stowed": ((5.5, 5.5, 6.5), (-1.5, 0.6, 0.56), 55, st, None),
    }
    for name, (eye, target, fov, sh, poses) in views.items():
        q = vs.quads(sh, COLOURS, skip=("hideWater",), poses=poses)
        img = view.render(q, eye, target, fov=fov, water=0.7 if name != "top" else None)
        img.save(os.path.join(DOCS, f"rowboat-{name}.png"))


def main():
    s = build()
    st = stowed(s)
    out = os.path.join(ASSETS, "shapes", "entity")
    os.makedirs(out, exist_ok=True)
    vs.save(s, os.path.join(out, "rowboat.json"))
    vs.save(st, os.path.join(out, "rowboat-stowed.json"))
    lo, hi = vs.bounds(s, skip=("hideWater", "OarA", "OarB"))
    los, his = vs.bounds(st, skip=("hideWater",))
    print(f"hull: x {lo[0]:.2f}..{hi[0]:.2f} ({hi[0] - lo[0]:.2f} long), y {lo[1]:.2f}..{hi[1]:.2f}, z {lo[2]:.2f}..{hi[2]:.2f}")
    print(f"stowed: x {los[0]:.2f}..{his[0]:.2f}, y {los[1]:.2f}..{his[1]:.2f}, z {los[2]:.2f}..{his[2]:.2f}")
    for k, (p, R) in sorted(vs.attachment_points(s).items()):
        if "Store" in k or "Seat" in k:
            print(f"  {k}: {np.round(p, 2)}")
    if "--no-preview" not in sys.argv:
        previews(s, st)


if __name__ == "__main__":
    main()
