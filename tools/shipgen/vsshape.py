"""
Reading, editing and drawing the game's own shape files (the JSON models entities and items are made of): elements are
boxes with a rotation about an origin, their children placed in the parent's frame (from its "from" corner), all in
sixteenths of a block. The transform is the game's (ShapeElement.GetLocalTransformMatrix, animVersion 0):
    parent * T(origin) * Rx * Ry * Rz * T(from - origin)
applied to the element's box (0 .. to - from).
"""
import copy
import json
import math

import numpy as np

FACE_DIRS = {"north": (0, 0, -1), "south": (0, 0, 1), "east": (1, 0, 0), "west": (-1, 0, 0), "up": (0, 1, 0), "down": (0, -1, 0)}


def load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def save(shape, path):
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(shape, fh, separators=(",", ":"))


def _rot(axis, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    m = np.eye(4)
    if axis == "x":
        m[1:3, 1:3] = [[c, -s], [s, c]]
    elif axis == "y":
        m[0, 0], m[0, 2], m[2, 0], m[2, 2] = c, s, -s, c
    else:
        m[0:2, 0:2] = [[c, -s], [s, c]]
    return m


def _trans(v):
    m = np.eye(4)
    m[:3, 3] = v
    return m


def local_matrix(el, pose=None):
    """The element's transform in its parent's frame (blocks). pose: extra degrees {rotationX, ...} (an animation)."""
    o = np.array(el.get("rotationOrigin") or [0, 0, 0], float) / 16
    f = np.array(el["from"], float) / 16
    rx = el.get("rotationX", 0) + (pose or {}).get("rotationX", 0)
    ry = el.get("rotationY", 0) + (pose or {}).get("rotationY", 0)
    rz = el.get("rotationZ", 0) + (pose or {}).get("rotationZ", 0)
    return _trans(o) @ _rot("x", rx) @ _rot("y", ry) @ _rot("z", rz) @ _trans(f - o)


def walk(shape, poses=None):
    """(element, world matrix, path) for every element, depth first. poses: {element name: {rotationX: ..}}."""
    out = []

    def rec(el, parent, path):
        m = parent @ local_matrix(el, (poses or {}).get(el["name"]))
        out.append((el, m, path + [el["name"]]))
        for c in el.get("children") or []:
            rec(c, m, path + [el["name"]])

    for el in shape["elements"]:
        rec(el, np.eye(4), [])
    return out


def find(shape, name):
    """(element, parent's children list) of the element with this name."""
    def rec(lst):
        for el in lst:
            if el["name"] == name:
                return el, lst
            r = rec(el.get("children") or [])
            if r:
                return r
        return None
    r = rec(shape["elements"])
    if r is None:
        raise KeyError(name)
    return r


def remove(shape, name):
    el, lst = find(shape, name)
    lst.remove(el)
    return el


def shift(el, d):
    """Move an element (and so its children) by d sixteenths in its parent's frame."""
    for k in ("from", "to", "rotationOrigin"):
        if el.get(k) is not None:
            el[k] = [round(a + b, 4) for a, b in zip(el[k], d)]


def corners(el, m):
    f, t = np.array(el["from"], float), np.array(el["to"], float)
    size = (t - f) / 16
    pts = []
    for i in (0, 1):
        for j in (0, 1):
            for k in (0, 1):
                p = np.array([size[0] * i, size[1] * j, size[2] * k, 1.0])
                pts.append((m @ p)[:3])
    return np.array(pts)


def bounds(shape, skip=(), poses=None):
    lo, hi = np.full(3, np.inf), np.full(3, -np.inf)
    for el, m, path in walk(shape, poses):
        if any(s in path for s in skip) or not _drawn(el):
            continue
        c = corners(el, m)
        lo, hi = np.minimum(lo, c.min(axis=0)), np.maximum(hi, c.max(axis=0))
    return lo, hi


def _drawn(el):
    faces = el.get("faces") or {}
    return any(fc.get("texture") and fc.get("enabled", True) is not False for fc in faces.values())


def attachment_points(shape, poses=None):
    """{code: (position (blocks), 3x3 rotation)} in the model's frame."""
    out = {}
    for el, m, path in walk(shape, poses):
        for ap in el.get("attachmentpoints") or []:
            p = np.array([float(ap.get("posX", 0)), float(ap.get("posY", 0)), float(ap.get("posZ", 0))]) / 16
            r = _rot("x", float(ap.get("rotationX", 0))) @ _rot("y", float(ap.get("rotationY", 0))) @ _rot("z", float(ap.get("rotationZ", 0)))
            mm = m @ _trans(p) @ r
            out[ap["code"]] = (mm[:3, 3], mm[:3, :3])
    return out


def quads(shape, colours, skip=(), poses=None, model=np.eye(4)):
    """[(pts 4x3, normal, colour)] of every drawn face (for view.render). colours: {texture key: (r, g, b)}."""
    out = []
    for el, m, path in walk(shape, poses):
        if any(s in path for s in skip):
            continue
        mm = model @ m
        f, t = np.array(el["from"], float), np.array(el["to"], float)
        sx, sy, sz = (t - f) / 16
        box = {
            "north": [(sx, 0, 0), (0, 0, 0), (0, sy, 0), (sx, sy, 0)],
            "south": [(0, 0, sz), (sx, 0, sz), (sx, sy, sz), (0, sy, sz)],
            "east": [(sx, 0, sz), (sx, 0, 0), (sx, sy, 0), (sx, sy, sz)],
            "west": [(0, 0, 0), (0, 0, sz), (0, sy, sz), (0, sy, 0)],
            "up": [(0, sy, sz), (sx, sy, sz), (sx, sy, 0), (0, sy, 0)],
            "down": [(0, 0, 0), (sx, 0, 0), (sx, 0, sz), (0, 0, sz)],
        }
        for name, fc in (el.get("faces") or {}).items():
            tex = (fc.get("texture") or "").lstrip("#")
            if not tex or fc.get("enabled", True) is False or tex == "transparent":
                continue
            pts = np.array([(mm @ np.array([*p, 1.0]))[:3] for p in box[name]])
            n = mm[:3, :3] @ np.array(FACE_DIRS[name], float)
            out.append((pts, n / (np.linalg.norm(n) + 1e-12), np.array(colours.get(tex, (200, 0, 200)), float)))
    return out


def deepcopy(shape):
    return copy.deepcopy(shape)
