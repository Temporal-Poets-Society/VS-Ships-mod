"""
Mesh quality check of a generated ship: what the game draws of her (the skin quads of config/shipmeshes/<ship>.json and
the faces of her shapes' elements, shapes/entity/ship/<ship>/exterior.json and interior.json), checked for

  1. z-fighting: two faces in (nearly) the same plane over the same area, so the depth test flickers between them
     ("conflict": within 0.002 blocks; "near": 0.002..0.01, flickers at a distance or at grazing angles; "duplicate": the
     same texture with the same uvs, so only the light differs). Two single-sided faces back to back (box elements pressed
     together) don't count: each is culled from the other's side.
  2. holes: (a) cracks, a face's edge stopping 0.0015..0.05 short of the face it should meet (a sliver of what's behind
     shows through); hull cavity exposure, the back of the hull's outer planking (or the outboard side of its inner
     planking) seen from somewhere a player can be, i.e. there's an opening into the hull's thickness; open edges (an
     edge with nothing next to it, seen from both sides); (b) light leaks: each room (the design's rooms) flooded from
     inside on a 1/8 block grid with her designed openings (doors, windows, hatches, gates, gun ports, ladders: fixtures
     and keepClear boxes) shut; every passage left to the outside or to another room is reported with its width.
  3. T-junctions: a corner of one face lying inside the edge of the face next to it (the seam can sparkle).

Only what a player can see matters: everything is classified visible / hidden by a flood fill of the air on a 1/16 block
grid from where a player can be (outside her, above or under the water, and every walkable deck and room: points with
room for a player, 0.6 x 1.6 x 0.6, on each deck). A face side is visible if the air in front of it is reachable. Hidden
findings (inside the hull's thickness, a beam end buried in planking, the back of a part pressed against another) are
only counted.

  python tools/shipgen/meshcheck.py [ship] [--clad] [--no-images] [--no-leaks] [--top N]

Writes docs/views/meshcheck/report.md, meshcheck-<ship>.json (every finding) and a few images of the worst spots (the
faces involved red and yellow). Exit code 1 if visible z-fighting conflicts of 0.001 m^2 or more exist (else 0).
Coordinates are ship-local blocks as the surveyor's chalk's notes give them (x to the bow, y up, z to starboard).
"""
import argparse
import json
import math
import os
import sys
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
ASSETS = os.path.join(ROOT, "assets", "ships")
OUT_DIR = os.path.join(ROOT, "docs", "views", "meshcheck")

# tolerances (blocks)
COPLANAR = 0.002          # planes this close over the same area: z-fighting
NEAR = 0.01               # up to this: a warning (flickers at a distance / grazing angles)
ANGLE_COS = math.cos(math.radians(2.0))
MIN_AREA = 1e-4           # overlap area (m^2 = blocks^2) worth reporting
SERIOUS_AREA = 1e-3       # visible conflicts this big fail the check
CRACK_MIN, CRACK_MAX = 0.0015, 0.05
H_VIS = 1.0 / 16          # visibility grid
H_LEAK = 1.0 / 8          # light leak grid
OPEN_R = 3                # visibility: the view gets into passages wider than ~2 * OPEN_R + 1 cells of H_VIS ...
VIEW_IN = 5               # ... and this many cells further into narrower ones
SKIP_GROUPS = ("shot",)   # drawn elsewhere (the cannon balls in flight)


def log(msg, t0=[time.time()]):
    print(f"[{time.time() - t0[0]:6.1f}s] {msg}", flush=True)


# ----------------------------------------------------------------------------------------------------------- loading

class Mesh:
    """Every drawn face as a planar polygon of 3 or 4 corners (a non-planar quad split into its two triangles as the game
    draws it). P (F,4,3): corners, a triangle's 4th = its 3rd. N unit normal (from the winding), C centroid."""

    def __init__(self):
        self.P, self.UV, self.tex, self.src, self.ds, self.owner = [], [], [], [], [], []
        self.textures = []
        self.tex_index = {}
        self.sources = []          # per source: dict(kind, group, idx, face, extra)
        self.skin_pos = {}         # group -> (n, 4, 3) corners as written
        self._part_cache = {}

    def texture(self, path):
        if path not in self.tex_index:
            self.tex_index[path] = len(self.textures)
            self.textures.append(path)
        return self.tex_index[path]

    def finish(self):
        self.P = np.concatenate(self.P).astype(float)
        self.UV = np.concatenate(self.UV).astype(float)
        self.tex = np.concatenate(self.tex).astype(int)
        self.src = np.concatenate(self.src).astype(int)
        self.ds = np.concatenate(self.ds).astype(bool)
        self.owner = np.concatenate(self.owner).astype(int)
        P = self.P
        n = np.cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1])
        tri = np.linalg.norm(P[:, 3] - P[:, 2], axis=1) < 1e-7
        n[tri] = np.cross(P[tri, 1] - P[tri, 0], P[tri, 2] - P[tri, 0])
        ln = np.linalg.norm(n, axis=1)
        self.area = ln / 2
        keep = ln > 1e-9
        for k in ("P", "UV", "tex", "src", "ds", "owner", "area"):
            setattr(self, k, getattr(self, k)[keep])
        n = n[keep] / ln[keep, None]
        self.N = n
        self.tri = tri[keep]
        nv = np.where(self.tri, 3, 4)
        self.C = (P[keep, 0] + P[keep, 1] + P[keep, 2] + np.where(self.tri[:, None], 0, P[keep, 3])) / nv[:, None]
        self.lo = self.P.min(axis=1)
        self.hi = self.P.max(axis=1)
        self.F = len(self.P)


def _clean_poly(p, uv):
    """Drop repeated corners: a quad, a triangle (4th corner = 3rd) or None."""
    pts, uvs = [], []
    for k in range(4):
        if not pts or np.linalg.norm(p[k] - pts[-1]) > 1e-6:
            pts.append(p[k])
            uvs.append(uv[k])
    if len(pts) > 1 and np.linalg.norm(pts[-1] - pts[0]) <= 1e-6:
        pts.pop()
        uvs.pop()
    if len(pts) < 3:
        return None
    if len(pts) == 3:
        pts.append(pts[2])
        uvs.append(uvs[2])
    return np.array(pts), np.array(uvs)


def _ranges_lookup(ranges, n, step=2, value=None):
    """Half-open index ranges [s, e, (v,) ...] -> per index: True / the value (or -1)."""
    out = np.full(n, -1 if value else 0, int)
    for i in range(0, len(ranges) - step + 1, step):
        s, e = int(ranges[i]), int(ranges[i + 1])
        out[s:e] = int(ranges[i + 2]) if value else 1
    return out


def slot_states(cfg, mode, seed=1):
    """(0.3.20, docs/slots.md) Each deck slot filled (True) or empty in a check's state: "design" (as each starts),
    "filled", "empty", "checker" (every other cell of each deck taken up), "checker2" (the other half)."""
    slots = cfg.get("deckSlots") or []
    if mode in ("wells", "wells-in"):
        # ((0.3.27, docs/slots.md §10) wells: about a third of the beam segments with both their cells taken up, and (wells)
        # the segment itself out too (its carlings drawn), at most two of a beam, next to each other)
        rng = np.random.default_rng(seed)
        ids = {sl["id"]: i for i, sl in enumerate(slots)}
        out = [sl.get("start", "filled") != "empty" for sl in slots]
        per_beam = {}
        for i, sl in enumerate(slots):
            if sl.get("kind") != "beam" or rng.random() > 0.33:
                continue
            taken = per_beam.setdefault(sl["beam"], [])
            if len(taken) >= 2 or (taken and abs(slots[taken[0]]["box"][2] - sl["box"][2]) > 1.01):
                continue
            taken.append(i)
            for c in sl.get("beside", []):
                out[ids[c]] = False
            if mode == "wells":
                out[i] = False
        return out
    out = []
    for s in slots:
        if mode == "design":
            out.append(s.get("start", "filled") != "empty")
        elif mode == "filled":
            out.append(True)
        elif mode == "empty":
            out.append(False)
        else:
            b = s["box"]
            par = (int(round(b[0])) + int(round(b[2] - 0.5))) % 2 == 0
            out.append(par if mode == "checker" else not par)
    return out


def hull_states(cfg, mode, seed=1):
    """(0.3.26, docs/slots.md §9) Each hull panel filled (True) or empty in a check's state: "filled" (as each starts:
    all in), "empty", "checker" (every other panel by station and band), "random" (about half, by seed)."""
    slots = cfg.get("hullSlots") or []
    if mode == "random":
        rng = np.random.default_rng(seed)
        return [bool(v) for v in rng.random(len(slots)) < 0.5]
    out = []
    for s in slots:
        if mode == "empty":
            out.append(False)
        elif mode == "checker":
            out.append((s["station"] + s["band"]) % 2 == 0)
        else:
            out.append(s.get("start", "filled") != "empty")
    return out


def load(ship, clad=False, slots="design", hull="filled", seed=1):
    m = Mesh()
    cfg_path = os.path.join(ASSETS, "config", "ships", ship + ".json")
    cfg = json.load(open(cfg_path, encoding="utf-8")) if os.path.exists(cfg_path) else {}
    m.cfg = cfg
    zones = [z.get("code", str(i)) for i, z in enumerate(cfg.get("paintZones") or [])]
    skin_path = os.path.join(ASSETS, "config", "shipmeshes", ship + ".json")
    skin = json.load(open(skin_path, encoding="utf-8"))
    m.skin_mtime = os.path.getmtime(skin_path)
    texmap = [m.texture(t) for t in skin["textures"]]
    skip = set(SKIP_GROUPS) | ({"bottom", "slot:hull:bottom"} if clad else {"bottomclad", "slot:hull:bottomclad"})
    owner = 0
    states = slot_states(cfg, slots, seed)
    hstates = hull_states(cfg, hull, seed)
    # ((0.3.29, docs/slots.md §12) a port in her side is in only while both its halves are out: with a half in, it's out;
    # in, its halves out)
    hid_ = {h["id"]: i for i, h in enumerate(cfg.get("hullSlots") or [])}
    for i, sl in enumerate(cfg.get("deckSlots") or []):
        hv = [hid_.get(h, -1) for h in sl.get("halves") or []]
        if sl.get("kind") != "port" or len(hv) != 2 or min(hv) < 0:
            continue
        if any(hstates[h] for h in hv):
            states[i] = False
        elif states[i]:
            for h in hv:
                hstates[h] = False
    m.slot_mode = slots if hull == "filled" else f"{slots}, hull panels {hull}" + (f" (seed {seed})" if hull == "random" else "")
    for grp, rows in skin["groups"].items():
        if grp in skip or grp.startswith("sail:"):
            continue
        sq = (skin.get("slotQuads") or {}).get(grp)
        if sq is not None:
            # (0.3.20) a slot group: only its quads drawn in this state (owner -1 or filled, facing -1 or empty); the
            # others written as empty rows so the indices (paint, hull ranges) keep
            st_ = hstates if grp.startswith("slot:hull") else states
            drawn = [(o < 0 or st_[o]) and (f < 0 or not st_[f]) for o, f in zip(sq[0::2], sq[1::2])]
            rows = [r for r, ok in zip(rows, drawn) if ok]
            if (skin.get("paintQuads") or {}).get(grp):
                pr = skin["paintQuads"][grp]
                newi = np.cumsum([0] + [int(x) for x in drawn])
                skin["paintQuads"][grp] = [int(newi[v]) if k % 3 < 2 else v for k, v in enumerate(pr)]
        a = np.asarray(rows, float)
        if a.size == 0:
            continue
        pos = a[:, 1:].reshape(-1, 4, 8)
        m.skin_pos[grp] = pos[:, :, :3]
        hull_out = _ranges_lookup((skin.get("hullOut") or {}).get(grp, []), len(a))
        hull_in = _ranges_lookup((skin.get("hullIn") or {}).get(grp, []), len(a))
        zone = _ranges_lookup((skin.get("paintQuads") or {}).get(grp, []), len(a), step=3, value=True)
        Ps, UVs, T, S, O = [], [], [], [], []
        for i in range(len(a)):
            c = _clean_poly(pos[i, :, :3], pos[i, :, 3:5])
            if c is None:
                continue
            p, uv = c
            sid = len(m.sources)
            m.sources.append(dict(kind="skin", group=grp, idx=i, tex=texmap[int(a[i, 0])],
                                  hull="out" if hull_out[i] else ("in" if hull_in[i] else None),
                                  zone=zones[zone[i]] if 0 <= zone[i] < len(zones) else None))
            # planar? else the game's two triangles (0,1,2) (0,2,3)
            nrm = np.cross(p[2] - p[0], p[3] - p[1])
            ln = np.linalg.norm(nrm)
            dev = abs(np.dot(nrm / ln, p[3] - p[0])) if ln > 1e-12 else 0.0
            if dev > COPLANAR * 0.5:
                for tri in ((0, 1, 2, 2), (0, 2, 3, 3)):
                    Ps.append(p[list(tri)])
                    UVs.append(uv[list(tri)])
                    T.append(texmap[int(a[i, 0])])
                    S.append(sid)
                    O.append(owner)
            else:
                Ps.append(p)
                UVs.append(uv)
                T.append(texmap[int(a[i, 0])])
                S.append(sid)
                O.append(owner)
            owner += 1
        if Ps:
            m.P.append(np.array(Ps))
            m.UV.append(np.array(UVs))
            m.tex.append(np.array(T))
            m.src.append(np.array(S))
            m.ds.append(np.ones(len(Ps), bool))      # skin quads are drawn with culling off (ShipRenderer)
            m.owner.append(np.array(O))
    # the shapes (voxel boxes and free elements): culled, one-sided
    sys.path.insert(0, HERE)
    import vsshape as vs
    box = None
    for name in ("exterior", "interior"):
        path = os.path.join(ASSETS, "shapes", "entity", "ship", ship, name + ".json")
        if not os.path.exists(path):
            continue
        shape = vs.load(path)
        texcodes = {k: m.texture(v) for k, v in (shape.get("textures") or {}).items()}
        Ps, UVs, T, S, O = [], [], [], [], []
        for el, mat, _path in vs.walk(shape):
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
            for fname, fc in (el.get("faces") or {}).items():
                tex = (fc.get("texture") or "").lstrip("#")
                if not tex or fc.get("enabled", True) is False or tex == "transparent":
                    continue
                pts = np.array([(mat @ np.array([*q, 1.0]))[:3] for q in box[fname]])
                u0, v0, u1, v1 = (fc.get("uv") or [0, 0, 16, 16])
                c = _clean_poly(pts, np.array([(u0, v1), (u1, v1), (u1, v0), (u0, v0)], float))
                if c is None:
                    continue
                # VS faces are wound for their outward normal; make sure ours points that way
                p, uv = c
                want = mat[:3, :3] @ np.array(vs.FACE_DIRS[fname], float)
                nrm = np.cross(p[2] - p[0], p[3] - p[1])
                if np.dot(nrm, want) < 0:
                    p, uv = p[::-1].copy(), uv[::-1].copy()
                    if np.linalg.norm(p[0] - p[1]) < 1e-9:      # keep a triangle's repeat at the end
                        p, uv = np.roll(p, -1, 0), np.roll(uv, -1, 0)
                sid = len(m.sources)
                m.sources.append(dict(kind="shape", group="shape:" + name, idx=el["name"], face=fname,
                                      tex=texcodes.get(tex, m.texture(tex)),
                                      box=[round(x, 4) for x in list(f / 16) + list(t / 16)]))
                Ps.append(p)
                UVs.append(uv)
                T.append(texcodes.get(tex, m.texture(tex)))
                S.append(sid)
                O.append(owner)
                owner += 1
        if Ps:
            m.P.append(np.array(Ps))
            m.UV.append(np.array(UVs))
            m.tex.append(np.array(T))
            m.src.append(np.array(S))
            m.ds.append(np.zeros(len(Ps), bool))
            m.owner.append(np.array(O))
    m.finish()
    m.group_of = np.array([m.sources[s]["group"] for s in m.src])
    m.is_glass = np.array([g.startswith("glass") for g in m.group_of])
    m.is_part = np.array([g.startswith("part:") for g in m.group_of])
    return m


def short_tex(path):
    p = path.replace("game:block/", "").replace("game:item/", "").replace("ships:block/", "")
    return p.replace("wood/planks/", "").replace("metal/", "")


def part_extent(m, group, idx, span=24):
    """The piece a skin quad belongs to (the generator writes a box's or a mesh's quads one after another): the quads
    near it in the list connected to it by shared corners -> (lo, hi) of their corners."""
    key = (group, idx)
    if key in m._part_cache:
        return m._part_cache[key]
    pos = m.skin_pos[group]
    i0, i1 = max(0, idx - span), min(len(pos), idx + span + 1)
    corners = [set(map(tuple, np.round(pos[i], 3))) for i in range(i0, i1)]
    comp = {idx - i0}
    grown = True
    while grown:
        grown = False
        for j in range(len(corners)):
            if j not in comp and any(corners[j] & corners[c] for c in comp):
                comp.add(j)
                grown = True
    pts = np.concatenate([pos[i0 + j] for j in comp])
    out = (pts.min(axis=0), pts.max(axis=0), len(comp))
    m._part_cache[key] = out
    return out


def describe(m, f):
    s = m.sources[m.src[f]]
    tex = short_tex(m.textures[m.tex[f]])
    if s["kind"] == "skin":
        bits = [f"skin {s['group']} quad {s['idx']}"]
        if s.get("hull"):
            bits.append(f"hull planking {'outside' if s['hull'] == 'out' else 'inside'}")
        if s.get("zone"):
            bits.append(f"zone {s['zone']}")
        bits.append(tex)
        lo, hi, n = part_extent(m, s["group"], s["idx"])
        if n > 1:
            bits.append(f"piece of {n} quads spanning x {lo[0]:.3f}..{hi[0]:.3f}, y {lo[1]:.3f}..{hi[1]:.3f}, "
                        f"z {lo[2]:.3f}..{hi[2]:.3f}")
        return ", ".join(bits)
    b = s["box"]
    return (f"{s['group']} {s['idx']} {s['face']} face (box {b[0]:g}..{b[3]:g}, {b[1]:g}..{b[4]:g}, {b[2]:g}..{b[5]:g}), "
            f"{tex}")


def where(m, p):
    x, y, z = p
    side = "centreline" if abs(z) < 0.3 else ("port" if z < 0 else "starboard")
    rooms = [r["code"] for r in m.cfg.get("rooms") or [] if all(r["min"][k] <= p[k] <= r["max"][k] for k in range(3))]
    return f"({x:.3f}, {y:.3f}, {z:.3f}) {side}" + (f", in {'/'.join(rooms)}" if rooms else "")


# ------------------------------------------------------------------------------------------------- spatial hashing

def _hash(cols):
    h = np.zeros(len(cols[0]), np.int64)
    with np.errstate(over="ignore"):
        for k, c in enumerate(cols):
            h = h * np.int64(1000003) + c.astype(np.int64) * np.int64(2654435761 + 97 * k) + np.int64(7919 * (k + 1))
    return h


def cell_entries(lo, hi, cs, ids=None):
    """(poly id, ix, iy, iz) for every cell of size cs each AABB touches."""
    a = np.floor(lo / cs).astype(np.int64)
    b = np.floor(hi / cs).astype(np.int64)
    n = b - a + 1
    cnt = n.prod(axis=1)
    ids = np.arange(len(lo)) if ids is None else ids
    poly = np.repeat(ids, cnt)
    start = np.cumsum(cnt) - cnt
    local = np.arange(cnt.sum()) - np.repeat(start, cnt)
    nx, ny = np.repeat(n[:, 0], cnt), np.repeat(n[:, 1], cnt)
    ix = np.repeat(a[:, 0], cnt) + local % nx
    iy = np.repeat(a[:, 1], cnt) + (local // nx) % ny
    iz = np.repeat(a[:, 2], cnt) + local // (nx * ny)
    return poly, ix, iy, iz


def pairs_from_keys(keys, ids, nmax):
    order = np.argsort(keys, kind="stable")
    k, v = keys[order], ids[order]
    n = len(k)
    A, B = [], []
    idx = np.nonzero(k[:-1] == k[1:])[0]
    step = 1
    while idx.size:
        A.append(v[idx])
        B.append(v[idx + step])
        step += 1
        idx = idx[idx + step < n]
        idx = idx[k[idx] == k[idx + step]]
    if not A:
        return np.zeros((0, 2), int)
    a, b = np.concatenate(A), np.concatenate(B)
    lo, hi = np.minimum(a, b), np.maximum(a, b)
    keep = lo != hi
    code = np.unique(lo[keep].astype(np.int64) * nmax + hi[keep])
    return np.stack([code // nmax, code % nmax], axis=1)


def plane_basis(n):
    a = np.where(np.abs(n[:, :1]) < 0.9, np.array([[1.0, 0, 0]]), np.array([[0, 1.0, 0]]))
    u = np.cross(n, a)
    u /= np.linalg.norm(u, axis=1, keepdims=True)
    v = np.cross(n, u)
    return u, v


def inside_poly(m, f, X, tol=1e-3):
    """Is each point X (k,3) inside polygon f (k,) (seen along its normal; convex), within tol."""
    P = m.P[f]
    N = m.N[f]
    ok = np.ones(len(f), bool)
    for k in range(4):
        e = P[:, (k + 1) % 4] - P[:, k]
        L = np.linalg.norm(e, axis=1)
        c = np.einsum("ij,ij->i", np.cross(e, X - P[:, k]), N)
        ok &= (L < 1e-9) | (c >= -tol * np.maximum(L, 1e-9))
    return ok


# ------------------------------------------------------------------------------------------------------ visibility

class Grid:
    def __init__(self, lo, hi, h):
        self.h = h
        self.o = np.asarray(lo, float)
        self.n = (np.ceil((np.asarray(hi, float) - self.o) / h).astype(int) + 1)

    def ijk(self, p):
        q = np.floor((np.asarray(p, float) - self.o) / self.h).astype(np.int64)
        ok = np.all((q >= 0) & (q < self.n), axis=-1)
        return q, ok

    def centre(self, q):
        return self.o + (np.asarray(q, float) + 0.5) * self.h


def rasterize(grid, P, spacing, occ=None):
    occ = np.zeros(tuple(grid.n), bool) if occ is None else occ
    if len(P) == 0:
        return occ
    L01 = np.linalg.norm(P[:, 1] - P[:, 0], axis=1)
    L32 = np.linalg.norm(P[:, 2] - P[:, 3], axis=1)
    L03 = np.linalg.norm(P[:, 3] - P[:, 0], axis=1)
    L12 = np.linalg.norm(P[:, 2] - P[:, 1], axis=1)
    nu = np.maximum(np.ceil(np.maximum(L01, L32) / spacing).astype(int) + 1, 2)
    nv = np.maximum(np.ceil(np.maximum(L03, L12) / spacing).astype(int) + 1, 2)
    cnt = nu * nv
    cum = np.cumsum(cnt)
    start = 0
    while start < len(P):
        end = int(np.searchsorted(cum, (cum[start - 1] if start else 0) + 4_000_000, side="right"))
        end = max(end, start + 1)
        sl = slice(start, end)
        c = cnt[sl]
        poly = np.repeat(np.arange(start, end), c)
        off = np.cumsum(c) - c
        local = np.arange(c.sum()) - np.repeat(off, c)
        nvr = np.repeat(nv[sl], c)
        s = (local // nvr) / (np.repeat(nu[sl], c) - 1)
        t = (local % nvr) / (nvr - 1)
        Q = P[poly]
        pts = (((1 - s) * (1 - t))[:, None] * Q[:, 0] + (s * (1 - t))[:, None] * Q[:, 1] + (s * t)[:, None] * Q[:, 2]
               + ((1 - s) * t)[:, None] * Q[:, 3])
        q, ok = grid.ijk(pts)
        q = q[ok]
        occ[q[:, 0], q[:, 1], q[:, 2]] = True
        start = end
    return occ


def player_seeds(m, grid, occ):
    """Points with room for a player (0.6 x 1.6 x 0.6 of air) on every deck: (feet points, deck code)."""
    decks = [d for d in (m.cfg.get("decks") or []) if grid.o[1] < d["y"] < grid.o[1] + grid.n[1] * grid.h - 1.7]
    seeds = []
    h = grid.h
    for d in decks:
        y0 = d["y"] + 0.1
        for x in np.arange(max(d["x0"], grid.o[0] + 0.5), min(d["x1"], grid.o[0] + grid.n[0] * h - 0.5), 0.5):
            for z in np.arange(-6.5, 6.51, 0.5):
                lo, ok1 = grid.ijk([x - 0.3, y0, z - 0.3])
                hi, ok2 = grid.ijk([x + 0.3, y0 + 1.6, z + 0.3])
                if not (ok1 and ok2):
                    continue
                if not occ[lo[0]:hi[0] + 1, lo[1]:hi[1] + 1, lo[2]:hi[2] + 1].any():
                    seeds.append((x, y0 + 0.8, z, d["code"]))
    return seeds


def visibility(m, h=H_VIS):
    """The air a player can see from: (grid, reach bool array, occ, seeds)."""
    from scipy import ndimage
    rooms = m.cfg.get("rooms") or []
    ytop = max([r["max"][1] for r in rooms] + [d["y"] for d in (m.cfg.get("decks") or []) if d["y"] < 15] + [5.0]) + 3.0
    lo = m.lo.min(axis=0) - 0.5
    hi = m.hi.max(axis=0) + 0.5
    hi[1] = min(hi[1], ytop)
    grid = Grid(lo, hi, h)
    solid = ~m.is_glass
    occ = rasterize(grid, m.P[solid], h * 0.5)
    seeds = player_seeds(m, grid, occ)
    air = ~occ
    lab, nlab = ndimage.label(air)
    del air
    keep = set()
    for sl in ((0, slice(None), slice(None)), (-1, slice(None), slice(None)), (slice(None), 0, slice(None)),
               (slice(None), -1, slice(None)), (slice(None), slice(None), 0), (slice(None), slice(None), -1)):
        keep |= set(np.unique(lab[sl]).tolist())
    outside = set(keep)
    for x, y, z, _ in seeds:
        q, ok = grid.ijk([x, y, z])
        if ok:
            keep.add(int(lab[q[0], q[1], q[2]]))
    keep.discard(0)
    lut = np.zeros(nlab + 1, bool)
    lut[list(keep)] = True
    reach = lut[lab]
    grid.inner_components = len(keep - outside)
    del lab
    # what can be seen: the reachable air a player's view can get into (passages at least ~0.4 wide: the morphological
    # opening of it), plus a little way into narrower slots (0.3) from there. Without this one crack into the hull's
    # thickness would make all of its inside "reachable".
    r = OPEN_R
    core = ndimage.distance_transform_cdt(reach, metric="chessboard") > r
    lab, nlab = ndimage.label(core)
    keep = set()
    for x, y, z, _ in seeds:
        q, ok = grid.ijk([x, y, z])
        if ok:
            keep.add(int(lab[q[0], q[1], q[2]]))
    for sl in ((0, slice(None), slice(None)), (-1, slice(None), slice(None)), (slice(None), 0, slice(None)),
               (slice(None), -1, slice(None)), (slice(None), slice(None), 0), (slice(None), slice(None), -1)):
        keep |= set(np.unique(lab[sl]).tolist())
    keep.discard(0)
    lut = np.zeros(nlab + 1, bool)
    lut[list(keep)] = True
    core = lut[lab]
    del lab
    # grown through the air only (a plain dilation would reach through the planking into the hull's thickness)
    seen = ndimage.binary_dilation(core, ndimage.generate_binary_structure(3, 1), iterations=r + VIEW_IN, mask=reach)
    return grid, seen, occ, seeds


def reachable(grid, reach, pts):
    q, ok = grid.ijk(pts)
    out = np.ones(len(q), bool)          # outside the grid (high in the rig): in the open
    qq = q[ok]
    out[ok] = reach[qq[:, 0], qq[:, 1], qq[:, 2]]
    return out


def face_side_visible(m, grid, reach, faces=None, off=None):
    """Per face: (front visible, back visible): the air 1.5 cells in front of / behind its centroid or one of its
    corners (pulled 25 % in) reachable."""
    faces = np.arange(m.F) if faces is None else faces
    off = 1.5 * grid.h if off is None else off
    P, C, N = m.P[faces], m.C[faces], m.N[faces]
    samples = [C] + [C + 0.75 * (P[:, k] - C) for k in range(4)]
    fv = np.zeros(len(faces), bool)
    bv = np.zeros(len(faces), bool)
    for S in samples:
        fv |= reachable(grid, reach, S + N * off)
        bv |= reachable(grid, reach, S - N * off)
    return fv, bv


# ------------------------------------------------------------------------------------------------- 1. z-fighting

def coplanar_pairs(m):
    N, C = m.N, m.C
    r = np.array([0.5737, 0.6831, 0.4519])
    r /= np.linalg.norm(r)
    nr = N @ r
    sign = np.where(nr < 0, -1.0, 1.0)
    base_ids = [np.arange(m.F)]
    base_n = [N * sign[:, None]]
    amb = np.nonzero(np.abs(nr) < 0.05)[0]
    if amb.size:
        base_ids.append(amb)
        base_n.append(-N[amb] * sign[amb, None])
    ids = np.concatenate(base_ids)
    nc = np.concatenate(base_n)
    d = np.einsum("ij,ij->i", nc, C[ids])
    w = np.array([0.05, 0.05, 0.05, 0.05])
    mg = np.array([0.02, 0.02, 0.02, NEAR + 0.002])
    v = np.column_stack([nc, d]) / w
    b = np.floor(v).astype(np.int64)
    fr = v - b
    alt = np.where(fr < mg / w, -1, np.where(fr > 1 - mg / w, 1, 0))
    E_ids, E_bins = [], []
    for mask in range(16):
        bits = np.array([(mask >> j) & 1 for j in range(4)], bool)
        ok = np.all(alt[:, bits] != 0, axis=1) if bits.any() else np.ones(len(ids), bool)
        E_ids.append(ids[ok])
        E_bins.append(b[ok] + alt[ok] * bits)
    bid = np.concatenate(E_ids)
    bins = np.concatenate(E_bins)
    order = np.argsort(bid, kind="stable")
    bid, bins = bid[order], bins[order]
    nb = np.bincount(bid, minlength=m.F)
    bstart = np.cumsum(nb) - nb
    # cells of 1 block
    poly, ix, iy, iz = cell_entries(m.lo - 0.005, m.hi + 0.005, 1.0)
    rep = nb[poly]
    e_poly = np.repeat(poly, rep)
    off = np.cumsum(rep) - rep
    local = np.arange(rep.sum()) - np.repeat(off, rep)
    e_bin = bins[np.repeat(bstart[poly], rep) + local]
    keys = _hash([np.repeat(ix, rep), np.repeat(iy, rep), np.repeat(iz, rep), e_bin[:, 0], e_bin[:, 1], e_bin[:, 2],
                  e_bin[:, 3]])
    return pairs_from_keys(keys, e_poly, m.F)


def uv_map(m, f):
    """uv as an affine function of the 3D point, fitted to the face's corners."""
    P, UV = m.P[f], m.UV[f]
    A = np.column_stack([P, np.ones(4)])
    coef, *_ = np.linalg.lstsq(A, UV, rcond=None)
    return coef


def z_fighting(m, grid, reach):
    import shapely
    pairs = coplanar_pairs(m)
    log(f"  coplanar candidates: {len(pairs)}")
    a, b = pairs[:, 0], pairs[:, 1]
    keep = m.owner[a] != m.owner[b]
    dot = np.einsum("ij,ij->i", m.N[a], m.N[b])
    keep &= np.abs(dot) > ANGLE_COS
    keep &= ~(~m.ds[a] & ~m.ds[b] & (dot < 0))          # two culled faces back to back
    keep &= np.all(m.lo[a] <= m.hi[b] + NEAR, axis=1) & np.all(m.lo[b] <= m.hi[a] + NEAR, axis=1)
    a, b, dot = a[keep], b[keep], dot[keep]
    da = np.einsum("ikj,ij->ik", m.P[b] - m.C[a][:, None], m.N[a])
    db = np.einsum("ikj,ij->ik", m.P[a] - m.C[b][:, None], m.N[b])
    close = ((np.abs(da).min(axis=1) <= NEAR + 1e-4) | (da.min(axis=1) * da.max(axis=1) < 0)) & \
            ((np.abs(db).min(axis=1) <= NEAR + 1e-4) | (db.min(axis=1) * db.max(axis=1) < 0))
    a, b, dot = a[close], b[close], dot[close]
    log(f"  parallel and close: {len(a)}")
    if len(a) == 0:
        return []
    u, v = plane_basis(m.N[a])
    Ca = m.C[a]
    ca = np.stack([np.einsum("ikj,ij->ik", m.P[a] - Ca[:, None], u), np.einsum("ikj,ij->ik", m.P[a] - Ca[:, None], v)], -1)
    cb = np.stack([np.einsum("ikj,ij->ik", m.P[b] - Ca[:, None], u), np.einsum("ikj,ij->ik", m.P[b] - Ca[:, None], v)], -1)
    pa = shapely.polygons(ca)
    pb = shapely.polygons(cb)
    bad = ~shapely.is_valid(pa)
    pa[bad] = shapely.make_valid(pa[bad])
    bad = ~shapely.is_valid(pb)
    pb[bad] = shapely.make_valid(pb[bad])
    inter = shapely.intersection(pa, pb)
    area = shapely.area(inter)
    sel = np.nonzero(area > MIN_AREA)[0]
    log(f"  overlapping by more than {MIN_AREA:g} m^2: {len(sel)}")
    findings = []
    pidx = poly_cells(m, 0.25, 0.1)
    for i in sel:
        fa, fb = int(a[i]), int(b[i])
        g = inter[i]
        xy = shapely.get_coordinates(g)
        cen = np.array(shapely.get_coordinates(shapely.centroid(g))[0])
        pts2 = np.vstack([cen[None], cen + 0.7 * (xy - cen)])
        X = Ca[i] + pts2[:, :1] * u[i] + pts2[:, 1:2] * v[i]
        nb_ = m.N[fb]
        den = float(np.dot(nb_, m.N[fa]))
        hgt = (np.einsum("ij,j->i", m.C[fb] - X, nb_)) / den
        sep = float(np.abs(hgt).max())
        if sep > NEAR:
            continue
        kind = "conflict" if sep <= COPLANAR else "near"
        # identical texture and uvs over the overlap: a duplicate (only the light / normals differ)
        if kind == "conflict" and m.tex[fa] == m.tex[fb]:
            Xb = X + hgt[:, None] * m.N[fa]
            ua = np.column_stack([X, np.ones(len(X))]) @ uv_map(m, fa)
            ub = np.column_stack([Xb, np.ones(len(Xb))]) @ uv_map(m, fb)
            dd = np.abs(ua - ub)
            dd = np.minimum(dd % 16, 16 - dd % 16)
            if dd.max() < 0.1:
                kind = "duplicate"
        # visible? from the sides both are drawn on
        sides = []
        if m.ds[fa] and m.ds[fb]:
            sides = [1.0, -1.0]
        elif not m.ds[fa]:
            sides = [1.0]
        else:
            sides = [1.0 if dot[i] > 0 else -1.0]
        # seen from a side if, from some point of the overlap, the air 1.5 cells off is seen and nothing (a board laid
        # over it, the rest of a box) is in between
        vis = False
        excl = np.tile([m.owner[fa], m.owner[fb]], (len(X), 1))
        for s in sides:
            D = np.tile(m.N[fa] * s, (len(X), 1))
            ok = reachable(grid, reach, X + D * 1.5 * grid.h) & ~segment_blocked(m, pidx, X, D, 1.5 * grid.h, excl)
            if ok.any():
                vis = True
                break
        seen_side = s if vis else sides[0]
        findings.append(dict(kind=kind, a=fa, b=fb, area=float(area[i]), sep=sep, at=X[0].tolist(),
                             normal=(m.N[fa] * seen_side).tolist(), visible=bool(vis),
                             same_group=m.group_of[fa] == m.group_of[fb]))
    return findings


# ------------------------------------------------------------------------------------------------------ 2a. cracks

def edges_of(m, faces):
    """(face, a, b) of every non-degenerate edge of the faces."""
    F, A, B = [], [], []
    for k in range(4):
        pa, pb = m.P[faces, k], m.P[faces, (k + 1) % 4]
        ok = np.linalg.norm(pb - pa, axis=1) > 1e-6
        F.append(faces[ok])
        A.append(pa[ok])
        B.append(pb[ok])
    return np.concatenate(F), np.concatenate(A), np.concatenate(B)


def poly_cells(m, cs, pad):
    poly, ix, iy, iz = cell_entries(m.lo - pad, m.hi + pad, cs)
    key = _hash([ix, iy, iz])
    order = np.argsort(key, kind="stable")
    return key[order], poly[order]


def query_cells(pkey, ppoly, qkey):
    """For each query key: the polys in its cell -> (query index, poly)."""
    l = np.searchsorted(pkey, qkey, side="left")
    r = np.searchsorted(pkey, qkey, side="right")
    cnt = r - l
    qi = np.repeat(np.arange(len(qkey)), cnt)
    off = np.cumsum(cnt) - cnt
    local = np.arange(cnt.sum()) - np.repeat(off, cnt)
    return qi, ppoly[np.repeat(l, cnt) + local]


def segment_blocked(m, pidx, X, D, length, exclude):
    """Is the segment X + D * (1e-4 .. length) crossed by a face (other than the owners in exclude (k,2))?
    pidx: poly_cells(m, 0.25, length)."""
    pkey, ppoly = pidx
    cs = 0.25
    Q = X + D * (length / 2)
    qk = _hash(list(np.floor(Q / cs).astype(np.int64).T))
    qi, pf = query_cells(pkey, ppoly, qk)
    keep = (m.owner[pf] != exclude[qi, 0]) & (m.owner[pf] != exclude[qi, 1])
    qi, pf = qi[keep], pf[keep]
    dn = np.einsum("ij,ij->i", D[qi], m.N[pf])
    ok = np.abs(dn) > 1e-6
    qi, pf, dn = qi[ok], pf[ok], dn[ok]
    t = np.einsum("ij,ij->i", m.C[pf] - X[qi], m.N[pf]) / dn
    ok = (t > 2e-4) & (t <= length)
    qi, pf, t = qi[ok], pf[ok], t[ok]
    hit = inside_poly(m, pf, X[qi] + D[qi] * t[:, None], tol=1e-4)
    out = np.zeros(len(X), bool)
    out[qi[hit]] = True
    return out


def cracks(m, grid, reach, fvis, bvis):
    """Edges of visible faces that stop short of the face they should meet."""
    faces = np.nonzero(fvis | (bvis & m.ds))[0]
    ef, ea, eb = edges_of(m, faces)
    L = np.linalg.norm(eb - ea, axis=1)
    d = (eb - ea) / L[:, None]
    t = np.cross(d, m.N[ef])
    mid = (ea + eb) / 2
    t *= np.where(np.einsum("ij,ij->i", mid - m.C[ef], t) < 0, -1.0, 1.0)[:, None]
    # samples along each edge
    fr = [np.full(len(ef), 0.5)]
    multi = L > 0.15
    for x in (0.2, 0.8):
        fr.append(np.where(multi, x, np.nan))
    fr = np.stack(fr, 1)
    ei = np.repeat(np.arange(len(ef)), 3)
    frs = fr.reshape(-1)
    ok = ~np.isnan(frs)
    ei, frs = ei[ok], frs[ok]
    O = ea[ei] + (eb[ei] - ea[ei]) * frs[:, None]
    T = t[ei]
    own = m.owner[ef[ei]]
    G = np.array([0.0012, 0.003, 0.006, 0.012, 0.02, 0.03, 0.04, 0.05])
    pkey, ppoly = poly_cells(m, 0.25, 0.06)
    gmin = np.full(len(O), np.inf)
    cs = 0.25
    for s in range(0, len(O), 60000):
        sl = slice(s, s + 60000)
        Q = O[sl] + T[sl] * 0.025
        qk = _hash(list(np.floor(Q / cs).astype(np.int64).T))
        qi, pf = query_cells(pkey, ppoly, qk)
        qg = qi + s
        keep = m.owner[pf] != own[qg]
        qg, pf = qg[keep], pf[keep]
        o, tt = O[qg], T[qg]
        dO = np.einsum("ij,ij->i", o - m.C[pf], m.N[pf])
        td = np.einsum("ij,ij->i", tt, m.N[pf])
        best = np.full(len(qg), np.inf)
        # in (nearly) the same plane: the first gap sample covered by it
        cop = (np.abs(dO) < 2e-3) & (np.abs(td) < 0.05)
        if cop.any():
            ci = np.nonzero(cop)[0]
            for g in G[::-1]:
                X = o[ci] + tt[ci] * g
                ins = inside_poly(m, pf[ci], X)
                best[ci[ins]] = g
        # crossing it
        cr = np.abs(td) >= 0.05
        if cr.any():
            ci = np.nonzero(cr)[0]
            g = -dO[ci] / td[ci]
            okg = (g > -1e-4) & (g <= CRACK_MAX)
            ci, g = ci[okg], g[okg]
            X = o[ci] + tt[ci] * g[:, None]
            ins = inside_poly(m, pf[ci], X, tol=2e-3)
            best[ci[ins]] = np.minimum(best[ci[ins]], np.maximum(g[ins], 0.0))
        np.minimum.at(gmin, qg, best)
    crack = (gmin > CRACK_MIN) & (gmin <= CRACK_MAX)
    free = np.isinf(gmin)
    # per edge
    res = []
    n_samples = np.bincount(ei, minlength=len(ef))
    n_crack = np.bincount(ei[crack], minlength=len(ef))
    n_free = np.bincount(ei[free], minlength=len(ef))
    width = np.zeros(len(ef))
    np.maximum.at(width, ei[crack], gmin[crack])
    for e in np.nonzero(n_crack)[0]:
        f = ef[e]
        res.append(dict(kind="crack", face=int(f), a=ea[e].tolist(), b=eb[e].tolist(), at=mid[e].tolist(),
                        length=float(L[e] * n_crack[e] / n_samples[e]), width=float(width[e]),
                        normal=m.N[f].tolist(), visible=True))
    # open edges: nothing beyond the edge, seen from both sides (a double-sided sheet's free edge: thin, but if it is
    # a skin's edge with a cavity behind it, a hole)
    both = fvis[ef] & bvis[ef] & m.ds[ef]
    open_e = np.nonzero((n_free == n_samples) & both)[0]
    return res, open_e, (ef, ea, eb)


def cavity_exposure(m, grid, reach):
    """The hull planking's hidden sides seen: the inboard side of the outside planking, the outboard side of the
    ceiling: there's a way into the hull's thickness."""
    out = []
    for kind, sgn in (("out", -1.0), ("in", 1.0)):
        f = np.nonzero(np.array([m.sources[s].get("hull") == kind for s in m.src]))[0]
        if not f.size:
            continue
        C, N = m.C[f], m.N[f]
        radial = C - np.column_stack([C[:, 0], np.full(len(C), 1.0), np.zeros(len(C))])
        radial /= np.linalg.norm(radial, axis=1, keepdims=True) + 1e-9
        outward = np.einsum("ij,ij->i", N, radial)
        # the planking's broad faces, not its edges and ends, nor the rail caps' tops and undersides
        broad = (np.abs(outward) > 0.6) & (np.abs(N[:, 1]) < 0.8)
        f, C, N, outward = f[broad], C[broad], N[broad], outward[broad]
        nn = N * np.where(outward < 0, -1.0, 1.0)[:, None]      # pointing outboard
        P = m.P[f]
        hit = np.zeros(len(f), bool)
        for S in [C] + [C + 0.75 * (P[:, k] - C) for k in range(4)]:
            hit |= reachable(grid, reach, S + nn * sgn * 1.5 * grid.h)
        for i in np.nonzero(hit)[0]:
            out.append(dict(kind="cavity", face=int(f[i]), side=kind, at=C[i].tolist(), area=float(m.area[f[i]]),
                            normal=(nn[i] * sgn).tolist(), visible=True))
    return out


def cluster(findings, cs=1.0, weight="area"):
    """Findings grouped by the cell of size cs they're in: [(cell centre, findings)], biggest first."""
    groups = {}
    for x in findings:
        k = tuple(np.floor(np.asarray(x["at"]) / cs).astype(int))
        groups.setdefault(k, []).append(x)
    out = sorted(groups.values(), key=lambda g: -sum(y.get(weight, 1.0) for y in g))
    return out


# ------------------------------------------------------------------------------------------------- 2b. light leaks

def opening_boxes(m):
    cfg = m.cfg
    boxes = []
    for f in cfg.get("fixtures") or []:
        if f.get("type") in ("door", "hatch", "gate") and f.get("box"):
            boxes.append((f["type"] + " " + f.get("code", ""), f["box"]))
    for k in cfg.get("keepClear") or []:
        if k.get("what") in ("window", "gun port", "door", "doorway", "hatch", "gate", "ladder"):
            boxes.append((k["what"], k["box"]))
    return boxes


def cut_boxes(m):
    out = []
    for c in m.cfg.get("sideCuts") or []:
        z0, z1 = (0.0, 9.0) if c.get("side", 1) > 0 else (-9.0, 0.0)
        out.append((c.get("kind", "cut"), [c["x0"], c["y0"], z0, c["x1"], c["y1"], z1]))
    for s in m.cfg.get("stairs") or []:
        a, b, w = np.array(s["foot"]), np.array(s["head"]), s.get("width", 1.0)
        lo, hi = np.minimum(a, b), np.maximum(a, b)
        # the stairwell with its opening in the deck above (and its headroom): half a block round the flight
        out.append(("stairs " + s.get("code", ""), [lo[0] - 0.5, lo[1], lo[2] - w / 2 - 0.5, hi[0] + 0.5, hi[1] + 2,
                                                    hi[2] + w / 2 + 0.5]))
    return out


def near_opening(boxes, p, pad=0.3):
    for name, b in boxes:
        if all(b[k] - pad <= p[k] <= b[k + 3] + pad for k in range(3)):
            return name
    return None


def enclosed(occ, grid, p, reach_max=40.0):
    """Something solid in all six directions from p (inside her, not under her overhangs)."""
    q, ok = grid.ijk(p)
    if not ok:
        return False
    i, j, k = q
    lim = int(reach_max / grid.h)
    return (occ[i, j, k + 1:k + lim].any() and occ[i, j, max(0, k - lim):k].any() and occ[i, j + 1:j + lim, k].any()
            and occ[i, max(0, j - lim):j, k].any() and occ[i + 1:i + lim, j, k].any() and occ[max(0, i - lim):i, j, k].any())


def _bfs(src, mask, cap, stop=None):
    """Steps through the mask's cells (6-connected) from the source cells; inf beyond cap (or beyond the step that
    reaches a stop cell). Returns (distances, steps taken)."""
    from scipy import ndimage
    dist = np.full(src.shape, np.inf, np.float32)
    dist[src] = 0
    seen = src.copy()
    front = src
    st = ndimage.generate_binary_structure(3, 1)
    k = 0
    for k in range(1, cap + 1):
        grown = ndimage.binary_dilation(front, st) & mask & ~seen
        if not grown.any():
            break
        dist[grown] = k
        seen |= grown
        front = grown
        if stop is not None and (grown & stop).any():
            break
    return dist, k


def light_leaks(m, h=H_LEAK, kmax=8, iters=10):
    from scipy import ndimage
    rooms = m.cfg.get("rooms") or []
    if not rooms:
        return [], []
    lo = np.min([r["min"] for r in rooms], axis=0) - 1.5
    hi = np.max([r["max"] for r in rooms], axis=0) + 1.5
    grid = Grid(lo, hi, h)
    occ = rasterize(grid, m.P, h * 0.5)
    shut = opening_boxes(m) + [b for b in cut_boxes(m) if b[0].startswith("stairs")]
    for _, b in shut:
        q0, _ = grid.ijk(np.clip(b[:3], grid.o, grid.o + (grid.n - 1) * h))
        q1, _ = grid.ijk(np.clip(b[3:], grid.o, grid.o + (grid.n - 1) * h))
        occ[q0[0]:q1[0] + 1, q0[1]:q1[1] + 1, q0[2]:q1[2] + 1] = True
    # what a passage next to is "designed": the openings, the side cuts, and stairwells with a block round them (a deck's
    # opening for a flight is bigger than the flight)
    named = shut + cut_boxes(m) + [(n, [b[0] - 1.0, b[1], b[2] - 1.0, b[3] + 1.0, b[4], b[5] + 1.0])
                                   for n, b in cut_boxes(m) if n.startswith("stairs")]
    # ((0.3.18) the modular designs' stair wells (docs/modular-pieces.md §3, `wells`): open by design, the room under one
    # open to the deck above through it; with the storey under it, where its ladder-stair stands)
    named += [(f"well {w['code']}", [w["box"][0], w["box"][1] - 3.0, w["box"][2], w["box"][3], w["box"][4] + 0.5,
                                     w["box"][5]]) for w in m.cfg.get("wells") or []]
    leaks, notes = [], []
    st = ndimage.generate_binary_structure(3, 1)
    for r in rooms:
        rlo, rhi = np.array(r["min"], float), np.array(r["max"], float)
        q0, _ = grid.ijk(np.maximum(rlo - 1.0, grid.o))
        q1, _ = grid.ijk(np.minimum(rhi + 1.0, grid.o + (grid.n - 1) * h))
        sub = occ[q0[0]:q1[0] + 1, q0[1]:q1[1] + 1, q0[2]:q1[2] + 1].copy()
        origin = grid.o + q0 * h
        # seeds: mid-height of the room, clear of anything by 0.3, inside her
        ymid = (rlo[1] + rhi[1]) / 2
        seeds = []
        for x in np.arange(rlo[0] + 0.5, rhi[0] - 0.4, 0.5):
            # ((0.3.12) within the room's own z range: the carrack's stairwell is a room in one side of the great cabin)
            for z in np.arange(max(-4.0, rlo[2] + 0.5), min(4.0, rhi[2] - 0.4) + 0.01, 0.5):
                p = np.array([x, ymid, z])
                qq, ok = grid.ijk(p)
                if not ok:
                    continue
                c = 3
                win = occ[max(0, qq[0] - c):qq[0] + c + 1, max(0, qq[1] - c):qq[1] + c + 1, max(0, qq[2] - c):qq[2] + c + 1]
                if win.any() or not enclosed(occ, grid, p):
                    continue
                seeds.append(qq - q0)
        if not seeds:
            notes.append(f"{r['code']}: no seed point found (no clear air inside her at mid-height)")
            continue
        seeds = np.array(seeds)
        found = 0
        for it in range(iters):
            air = ~sub
            lab, _ = ndimage.label(air, st)
            S = set(lab[seeds[:, 0], seeds[:, 1], seeds[:, 2]].tolist()) - {0}
            B = set()
            for sl in ((0, slice(None), slice(None)), (-1, slice(None), slice(None)), (slice(None), 0, slice(None)),
                       (slice(None), -1, slice(None)), (slice(None), slice(None), 0), (slice(None), slice(None), -1)):
                B |= set(np.unique(lab[sl]).tolist())
            B.discard(0)
            if not (S & B):
                if it == 0:
                    notes.append(f"{r['code']}: closed (no passage to the outside or another room once the designed "
                                 f"openings are shut)")
                break
            dist = ndimage.distance_transform_cdt(air, metric="chessboard")
            kk = None
            for k in range(1, kmax + 1):
                pas = dist > k
                lk, _ = ndimage.label(pas, st)
                sv = pas[seeds[:, 0], seeds[:, 1], seeds[:, 2]]
                if not sv.any():
                    break
                Sk = set(lk[seeds[sv, 0], seeds[sv, 1], seeds[sv, 2]].tolist()) - {0}
                Bk = set()
                for sl in ((0, slice(None), slice(None)), (-1, slice(None), slice(None)), (slice(None), 0, slice(None)),
                           (slice(None), -1, slice(None)), (slice(None), slice(None), 0), (slice(None), slice(None), -1)):
                    Bk |= set(np.unique(lk[sl]).tolist())
                Bk.discard(0)
                if not (Sk & Bk):
                    kk = k
                    break
            if kk is None:
                notes.append(f"{r['code']}: open through a passage more than ~{(2 * kmax) * h:.1f} blocks wide that is "
                             f"not one of the design's openings (not located)")
                break
            lut_o = np.zeros(lk.max() + 1, bool)
            lut_o[list(Bk)] = True
            lut_i = np.zeros(lk.max() + 1, bool)
            lut_i[list(Sk)] = True
            Out, In = lut_o[lk], lut_i[lk]
            # the crop's edge is outside too (a slot leading out of the box: into the hull's thickness, say)
            shell = np.zeros_like(air)
            shell[0], shell[-1], shell[:, 0], shell[:, -1], shell[:, :, 0], shell[:, :, -1] = True, True, True, True, True, True
            Out |= shell & air & ~In
            # the passage: the air on the shortest ways from the outside's air to the room's (geodesic distances)
            gO, steps = _bfs(Out, air, 2000, stop=In)
            gI, _ = _bfs(In, air, steps + 1)
            tot = gO + gI
            best = tot.min()
            if not np.isfinite(best):
                notes.append(f"{r['code']}: a leak at width ~{(2 * kk - 1) * h:.3f} could not be located")
                break
            corridor = (tot <= best + 1) & ~Out & ~In
            lr, nr = ndimage.label(corridor, ndimage.generate_binary_structure(3, 3))
            objs = ndimage.find_objects(lr)
            for c in range(1, nr + 1):
                sl = objs[c - 1]
                cells = np.argwhere(lr[sl] == c) + np.array([s_.start for s_ in sl])
                narrow = cells[dist[cells[:, 0], cells[:, 1], cells[:, 2]] <= kk]
                pts = origin + ((narrow if len(narrow) else cells) + 0.5) * h
                lo_, hi_ = pts.min(axis=0) - h / 2, pts.max(axis=0) + h / 2
                # where it opens into the room (what's seen from inside) and where it comes out
                gi = gI[cells[:, 0], cells[:, 1], cells[:, 2]]
                go = gO[cells[:, 0], cells[:, 1], cells[:, 2]]
                at_in = (origin + (cells[gi <= gi.min() + 1] + 0.5) * h).mean(axis=0)
                at_out = (origin + (cells[go <= go.min() + 1] + 0.5) * h).mean(axis=0)
                op = near_opening(named, at_in) or near_opening(named, at_out)
                leaks.append(dict(kind="leak", room=r["code"], at=at_in.tolist(), exit=at_out.tolist(), lo=lo_.tolist(),
                                  hi=hi_.tolist(), length=float(best * h), width_min=(2 * kk - 2) * h,
                                  width_max=2 * kk * h, cells=int(len(narrow)), opening=op, visible=True))
                sub[cells[:, 0], cells[:, 1], cells[:, 2]] = True
                found += 1
        if found and found >= iters:
            notes.append(f"{r['code']}: stopped after {iters} passes, there may be more")
    return leaks, notes


# ----------------------------------------------------------------------------------------------- 3. T-junctions

def t_junctions(m, fvis, bvis):
    faces = np.nonzero(fvis | (bvis & m.ds))[0]
    ef, ea, eb = edges_of(m, faces)
    L = np.linalg.norm(eb - ea, axis=1)
    d = (eb - ea) / L[:, None]
    # edges into 0.25 cells, by points along them
    cs = 0.25
    ns = np.ceil(L / 0.1).astype(int) + 1
    ei = np.repeat(np.arange(len(ef)), ns)
    off = np.cumsum(ns) - ns
    k = np.arange(ns.sum()) - np.repeat(off, ns)
    tt = k / (np.repeat(ns, ns) - 1)
    pts = ea[ei] + (eb[ei] - ea[ei]) * tt[:, None]
    key = _hash(list(np.floor(pts / cs).astype(np.int64).T))
    pair = np.unique(np.stack([key, ei], 1), axis=0)
    pkey, pedge = pair[:, 0], pair[:, 1]
    order = np.argsort(pkey, kind="stable")
    pkey, pedge = pkey[order], pedge[order]
    # the corners of the visible faces, with their two edges' directions
    V, Vf, Vd1, Vd2 = [], [], [], []
    P = m.P[faces]
    tri = m.tri[faces]
    for c in range(4):
        nxt = np.where(tri & (c == 2), 0, (c + 1) % 4)
        prv = np.where(tri & (c == 0), 2, (c - 1) % 4)
        ok = ~(tri & (c == 3))
        p = P[:, c]
        a1 = P[np.arange(len(faces)), nxt] - p
        a2 = P[np.arange(len(faces)), prv] - p
        V.append(p[ok])
        Vf.append(faces[ok])
        Vd1.append(a1[ok])
        Vd2.append(a2[ok])
    V, Vf, Vd1, Vd2 = map(np.concatenate, (V, Vf, Vd1, Vd2))
    vk = _hash(list(np.floor(V / cs).astype(np.int64).T))
    qi, e = query_cells(pkey, pedge, vk)
    keep = m.owner[Vf[qi]] != m.owner[ef[e]]
    qi, e = qi[keep], e[keep]
    w = V[qi] - ea[e]
    t = np.einsum("ij,ij->i", w, d[e])
    perp = np.linalg.norm(w - t[:, None] * d[e], axis=1)
    on = (perp < 5e-4) & (t > 2e-3) & (t < L[e] - 2e-3)
    qi, e, t = qi[on], e[on], t[on]

    def par(x):
        n = np.linalg.norm(x, axis=1)
        return np.linalg.norm(np.cross(x / np.maximum(n, 1e-12)[:, None], d[e]), axis=1) < 2e-3

    seam = par(Vd1[qi]) | par(Vd2[qi])
    qi, e = qi[seam], e[seam]
    # one per (vertex position, edge)
    keyv = np.unique(np.column_stack([np.round(V[qi] * 1000).astype(np.int64), e]), axis=0, return_index=True)[1]
    qi, e = qi[keyv], e[keyv]
    out = [dict(kind="tjunction", face=int(Vf[i]), other=int(ef[j]), at=V[i].tolist()) for i, j in zip(qi, e)]
    return out


# ---------------------------------------------------------------------------------------------------- pictures

def tex_colour(path):
    p = path.lower()
    if "glass" in p:
        return (150, 190, 215)
    if "tar" in p:
        return (70, 58, 48)
    if "copper" in p:
        return (190, 110, 70)
    if "brick" in p or "clay" in p:
        return (170, 80, 60)
    if "gold" in p:
        return (215, 180, 60)
    if "iron" in p or "lead" in p or "black" in p:
        return (90, 92, 98)
    if "bronze" in p:
        return (175, 130, 70)
    if "rope" in p or "linen" in p or "paper" in p:
        return (205, 190, 150)
    if "pine" in p or "debarked" in p:
        return (160, 120, 80)
    if "caulk" in p:
        return (150, 115, 80)
    return (165, 125, 85)


def pick_eye(grid, reach, occ, at, pref):
    """A place to look at the spot from: in air a player can see from, with nothing in between (on the grid)."""
    at = np.asarray(at, float)
    up = np.array([0, 1.0, 0])
    pref = np.asarray(pref, float)
    pref = pref / (np.linalg.norm(pref) + 1e-12)
    side = np.cross(pref, up)
    side = side / np.linalg.norm(side) if np.linalg.norm(side) > 1e-3 else np.array([1.0, 0, 0])
    dirs = [pref + side * a + up * b for a, b in ((0.35, 0.3), (-0.35, 0.3), (0, 0), (0.6, -0.2), (-0.6, -0.2))]
    dirs += [np.array(v, float) for v in ((1, 0.3, 0), (-1, 0.3, 0), (0, 0.3, 1), (0, 0.3, -1), (0, 1, 0.2), (0, -1, 0.2),
                                          (1, -0.3, 0), (-1, -0.3, 0), (0, -0.3, 1), (0, -0.3, -1))]
    for dist in (2.4, 1.7, 1.2, 0.8, 0.5):
        for d in dirs:
            d = d / np.linalg.norm(d)
            e = at + d * dist
            if not reachable(grid, reach, e[None])[0]:
                continue
            # the line of sight: no occupied cell between (but the last bit at the spot)
            n = max(2, int(dist / (grid.h * 0.5)))
            pts = e[None] + (at - e)[None] * np.linspace(0, 1, n)[:, None]
            pts = pts[np.linalg.norm(pts - at, axis=1) > 2.5 * grid.h]
            q, ok = grid.ijk(pts)
            q = q[ok]
            if not occ[q[:, 0], q[:, 1], q[:, 2]].any():
                return e
    return at + pref * 1.5 + up * 0.3


def render_spot(m, grid, reach, occ, at, normal, hl, path, title, radius=6.0, size=(640, 440)):
    sys.path.insert(0, HERE)
    import view
    from PIL import ImageDraw
    at = np.asarray(at, float)
    eye = pick_eye(grid, reach, occ, at, normal)
    up = np.array([0, 1.0, 0])
    vd = (at - eye) / np.linalg.norm(at - eye)
    if abs(np.dot(vd, up)) > 0.98:
        eye = eye + np.array([0.2, 0, 0.1])
    near = np.all(m.lo <= np.maximum(at, eye) + radius, axis=1) & np.all(m.hi >= np.minimum(at, eye) - radius, axis=1)
    hl = [int(f) for f in hl if f is not None]
    quads = []
    for f in np.nonzero(near)[0]:
        col = tex_colour(m.textures[m.tex[f]])
        pts = m.P[f]
        if f in hl:
            col = (235, 30, 30) if hl.index(f) == 0 else (250, 210, 30)
            pts = pts - vd * (0.004 * (2 - hl.index(f)))      # drawn over what it's coplanar with
        quads.append((pts, m.N[f], np.array(col, float)))
    img = view.render(quads, eye, at, size=size, fov=60)
    dr = ImageDraw.Draw(img)
    eye_, R = view.look_at(eye, at)
    c = (at - eye_) @ R.T
    if c[2] > 0.05:
        focal = 0.5 * size[0] / math.tan(math.radians(60) / 2)
        sx, sy = size[0] / 2 + focal * c[0] / c[2], size[1] / 2 - focal * c[1] / c[2]
        dr.ellipse([sx - 14, sy - 14, sx + 14, sy + 14], outline=(255, 0, 200), width=2)
    dr.rectangle([0, 0, size[0], 18], fill=(255, 255, 255))
    dr.text((4, 3), title, fill=(0, 0, 0))
    img.save(path)


def overview(m, items, path):
    """Side and top views of her (the hull's outline from the planking) with the findings marked."""
    from PIL import Image, ImageDraw
    W, H = 1100, 760
    img = Image.new("RGB", (W, H), (245, 245, 240))
    dr = ImageDraw.Draw(img)
    lo, hi = m.lo.min(0), m.hi.max(0)
    hi = hi.copy()
    hi[1] = min(hi[1], 14.0)
    s = (W - 40) / (hi[0] - lo[0])
    side_h = (hi[1] - lo[1]) * s
    top_h = (hi[2] - lo[2]) * s

    def P1(p):   # side view (starboard side: bow right)
        return 20 + (p[0] - lo[0]) * s, 30 + (hi[1] - p[1]) * s

    def P2(p):   # from above, port up
        return 20 + (p[0] - lo[0]) * s, 60 + side_h + (p[2] - lo[2]) * s

    pts = m.C[(m.C[:, 1] < hi[1])]
    pts = pts[:: max(1, len(pts) // 40000)]
    for p in pts:
        x, y = P1(p)
        dr.point((x, y), fill=(185, 185, 180))
        x, y = P2(p)
        dr.point((x, y), fill=(185, 185, 180))
    cols = {"conflict": (220, 20, 20), "near": (240, 150, 0), "duplicate": (150, 60, 200), "crack": (0, 120, 220),
            "leak": (0, 160, 60), "cavity": (0, 0, 0)}
    for it in items:
        p = it["at"]
        c = cols.get(it["kind"], (0, 0, 0))
        for P in (P1, P2):
            x, y = P(p)
            r = 3 if it["kind"] != "leak" else 5
            dr.ellipse([x - r, y - r, x + r, y + r], outline=c, width=2)
    dr.text((20, 8), f"{m.ship}: visible findings, side view (bow right) and from above (port up)", fill=(0, 0, 0))
    lx = 20
    for k, c in cols.items():
        dr.ellipse([lx, H - 22, lx + 10, H - 12], outline=c, width=2)
        dr.text((lx + 14, H - 24), k, fill=(0, 0, 0))
        lx += 110
    img.save(path)


# ------------------------------------------------------------------------------------------------------- report

def fmt_xyz(p):
    return f"({p[0]:.3f}, {p[1]:.3f}, {p[2]:.3f})"


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("ship", nargs="?", default="carrack")
    ap.add_argument("--clad", action="store_true", help="check the copper-sheathed bottom (bottomclad) instead of bottom")
    ap.add_argument("--no-images", action="store_true")
    ap.add_argument("--no-leaks", action="store_true")
    ap.add_argument("--top", type=int, default=50)
    ap.add_argument("--out", default=OUT_DIR)
    ap.add_argument("--slots", default="design", choices=("design", "filled", "empty", "checker", "checker2", "wells", "wells-in"),
                    help="(0.3.20) the deck slots' state to check: as designed, all filled, all empty, a checkerboard")
    ap.add_argument("--hull", default="filled", choices=("filled", "empty", "checker", "random"),
                    help="(0.3.26) the hull panels' state to check: all in, all out, a checkerboard, random")
    ap.add_argument("--seed", type=int, default=1, help="(0.3.26) the random hull panels' seed")
    args = ap.parse_args(argv)
    t_start = time.time()
    os.makedirs(args.out, exist_ok=True)

    m = load(args.ship, args.clad, args.slots, args.hull, args.seed)
    m.ship = args.ship
    log(f"loaded {m.F} faces ({int(m.ds.sum())} skin, {int((~m.ds).sum())} shape) from {len(m.sources)} quads/faces")
    grid, reach, occ, seeds = visibility(m)
    log(f"visibility grid {tuple(grid.n)} at {grid.h:g}, {len(seeds)} player points, "
        f"{grid.inner_components} enclosed air spaces reached only from inside")
    fvis, bvis = face_side_visible(m, grid, reach)
    vis_face = fvis | (bvis & m.ds)
    log(f"faces visible: {int(vis_face.sum())} of {m.F}")

    zf = z_fighting(m, grid, reach)
    log(f"z-fighting: {len(zf)} overlaps")
    cr, open_e, (ef, ea, eb) = cracks(m, grid, reach, fvis, bvis)
    log(f"cracks: {len(cr)} edges, open edges seen from both sides: {len(open_e)}")
    cav = cavity_exposure(m, grid, reach)
    log(f"hull cavity seen: {len(cav)} planking faces")
    tj = t_junctions(m, fvis, bvis)
    log(f"T-junctions: {len(tj)}")
    leaks, leak_notes = ([], ["(skipped)"]) if args.no_leaks else light_leaks(m)
    log(f"light leaks: {len(leaks)}")

    # ---- ranking
    vis_zf = [x for x in zf if x["visible"]]
    hid_zf = [x for x in zf if not x["visible"]]
    conflicts = sorted([x for x in vis_zf if x["kind"] == "conflict"], key=lambda x: -x["area"])
    nears = sorted([x for x in vis_zf if x["kind"] == "near"], key=lambda x: -x["area"])
    dups = sorted([x for x in vis_zf if x["kind"] == "duplicate"], key=lambda x: -x["area"])
    cr_sorted = sorted(cr, key=lambda x: -(x["length"] * x["width"]))
    bad_leaks = sorted([x for x in leaks if not x["opening"]], key=lambda x: -x["width_max"])
    ok_leaks = [x for x in leaks if x["opening"]]
    serious = [x for x in conflicts if x["area"] >= SERIOUS_AREA]

    def parts(x):
        return f"{describe(m, x['a'])} **vs** {describe(m, x['b'])}"

    # ---- images of the worst
    images = []
    if not args.no_images:
        shots = [("conflict", x) for x in conflicts[:6]] + [("near", x) for x in nears[:1]] + \
                [("crack", x) for x in cr_sorted[:2]] + [("leak", x) for x in bad_leaks[:2]]
        for i, (k, x) in enumerate(shots):
            name = f"{m.ship}-{i + 1:02d}-{k}.png"
            hl = [x["a"], x["b"]] if "a" in x and isinstance(x["a"], int) else [x.get("face")] if "face" in x else []
            if k == "leak":
                nrm = np.array(x["at"]) - np.array(x["exit"])
                nrm = nrm if np.linalg.norm(nrm) > 1e-6 else np.array([0, 1.0, 0])
                title = f"{i + 1}: light leak from the {x['room']}, ~{x['width_min']:.2f}-{x['width_max']:.2f} wide at {fmt_xyz(x['at'])}"
            elif k == "crack":
                nrm = np.array(x["normal"])
                title = f"{i + 1}: crack {x['width'] * 1000:.0f} mm x {x['length']:.2f} at {fmt_xyz(x['at'])}"
            else:
                nrm = np.array(x["normal"])
                title = f"{i + 1}: {k} {x['area']:.4f} m2 (gap {x['sep'] * 1000:.1f} mm) at {fmt_xyz(x['at'])}"
            path = os.path.join(args.out, name)
            try:
                render_spot(m, grid, reach, occ, x["at"], nrm, hl, path, title)
                x["image"] = name
                images.append(name)
            except Exception as exc:          # a picture is never worth failing the check
                print("image failed:", name, exc)
        overview(m, conflicts + nears + cr_sorted[:200] + bad_leaks, os.path.join(args.out, f"{m.ship}-overview.png"))
        images.append(f"{m.ship}-overview.png")
        log(f"{len(images)} images")

    # ---- report
    tj_cells = cluster(tj, 1.0, weight="none")
    L = []
    L.append(f"# Mesh check: {m.ship}\n")
    L.append(f"Mesh file of {time.strftime('%Y-%m-%d %H:%M', time.localtime(m.skin_mtime))}; checked "
             f"{time.strftime('%Y-%m-%d %H:%M')} in {time.time() - t_start:.0f} s. `python tools/shipgen/meshcheck.py "
             f"{m.ship}`. Ship-local coordinates in blocks (x to the bow, y up, z to starboard; port is z < 0), as the "
             f"chalk's notes give them.\n")
    L.append(f"Faces: {m.F} ({int(m.ds.sum())} skin quads/triangles drawn two-sided, {int((~m.ds).sum())} shape faces "
             f"drawn one-sided); {int(vis_face.sum())} visible from somewhere a player can be "
             f"({len(seeds)} standing points on her decks, and all round her).\n")
    L.append("## Counts\n")
    L.append("| check | visible | hidden |\n|---|---:|---:|")
    for name, kind in (("z-fighting conflict (< 2 mm apart)", "conflict"), ("near-coplanar (2-10 mm, warning)", "near"),
                       ("duplicate (same texture and uvs)", "duplicate")):
        L.append(f"| {name} | {sum(1 for x in vis_zf if x['kind'] == kind)} "
                 f"({sum(x['area'] for x in vis_zf if x['kind'] == kind):.3f} m2) | "
                 f"{sum(1 for x in hid_zf if x['kind'] == kind)} |")
    L.append(f"| of which conflicts >= {SERIOUS_AREA:g} m2 (fail the check) | {len(serious)} | |")
    L.append(f"| cracks 1.5-50 mm (edges of visible faces) | {len(cr)} ({sum(x['length'] for x in cr):.1f} blocks long) | - |")
    L.append(f"| open edges seen from both sides | {len(open_e)} | - |")
    L.append(f"| hull cavity seen (planking faces whose hidden side is reachable) | {len(cav)} | - |")
    L.append(f"| light leaks not at a designed opening | {len(bad_leaks)} | - |")
    L.append(f"| light leaks at/next to a designed opening | {len(ok_leaks)} | - |")
    L.append(f"| T-junctions on visible faces | {len(tj)} (in {len(tj_cells)} block cells) | - |")
    L.append("")
    L.append(f"Exit status: {'FAIL' if serious else 'pass'} ({len(serious)} visible conflicts of {SERIOUS_AREA:g} m2 or more).\n")
    if images:
        L.append("Images: " + ", ".join(f"[{i}]({i})" for i in images) + " (the first face red, the second yellow; the "
                 "spot ringed in magenta).\n")

    L.append(f"## Worst visible z-fighting ({min(args.top, len(conflicts))} of {len(conflicts)})\n")
    L.append("| # | where | overlap m2 | gap mm | faces |\n|---:|---|---:|---:|---|")
    for i, x in enumerate(conflicts[:args.top]):
        img = f" [img]({x['image']})" if x.get("image") else ""
        L.append(f"| {i + 1} | {where(m, x['at'])}{img} | {x['area']:.4f} | {x['sep'] * 1000:.2f} | {parts(x)} |")
    L.append("")
    if nears:
        L.append(f"## Near-coplanar warnings (worst {min(20, len(nears))} of {len(nears)})\n")
        L.append("| # | where | overlap m2 | gap mm | faces |\n|---:|---|---:|---:|---|")
        for i, x in enumerate(nears[:20]):
            L.append(f"| {i + 1} | {where(m, x['at'])} | {x['area']:.4f} | {x['sep'] * 1000:.2f} | {parts(x)} |")
        L.append("")
    if dups:
        L.append(f"## Duplicates (same texture, same uvs; worst {min(15, len(dups))} of {len(dups)})\n")
        L.append("| # | where | overlap m2 | faces |\n|---:|---|---:|---|")
        for i, x in enumerate(dups[:15]):
            L.append(f"| {i + 1} | {where(m, x['at'])} | {x['area']:.4f} | {parts(x)} |")
        L.append("")
    # z-fighting by group pair
    from collections import Counter
    gp = Counter()
    for x in conflicts:
        k = tuple(sorted((m.group_of[x["a"]], m.group_of[x["b"]])))
        gp[k] += x["area"]
    if gp:
        L.append("Visible conflict area by the groups involved: " + "; ".join(
            f"{a} / {b}: {v:.3f} m2" for (a, b), v in gp.most_common(12)) + "\n")

    L.append(f"## Light leaks (rooms flooded on a {H_LEAK:g} grid, designed openings shut)\n")
    for n in leak_notes:
        L.append(f"- {n}")
    if bad_leaks or ok_leaks:
        L.append("\n| # | room | where | width | size (box) | next to |\n|---:|---|---|---|---|---|")
        for i, x in enumerate(bad_leaks + ok_leaks):
            img = f" [img]({x['image']})" if x.get("image") else ""
            L.append(f"| {i + 1} | {x['room']} | {where(m, x['at'])}{img} | {x['width_min']:.3f}-{x['width_max']:.3f} | "
                     f"{fmt_xyz(x['lo'])} - {fmt_xyz(x['hi'])} | {x['opening'] or '**nothing designed**'} |")
    L.append("")
    L.append(f"## Worst cracks ({min(args.top, len(cr_sorted))} of {len(cr_sorted)}; a visible face's edge stopping "
             f"{CRACK_MIN * 1000:.1f}-{CRACK_MAX * 1000:.0f} mm short of the next face)\n")
    L.append("| # | where | width mm | length | face |\n|---:|---|---:|---:|---|")
    for i, x in enumerate(cr_sorted[:args.top]):
        img = f" [img]({x['image']})" if x.get("image") else ""
        L.append(f"| {i + 1} | {where(m, x['at'])}{img} | {x['width'] * 1000:.1f} | {x['length']:.3f} | {describe(m, x['face'])} |")
    L.append("")
    if cav:
        cc = cluster(cav, 1.0)
        L.append(f"## Hull cavity seen ({len(cav)} faces in {len(cc)} block cells, biggest first)\n")
        L.append("The hidden side of the hull planking (the inboard side of the outside planking, the outboard side of the "
                 "ceiling) is reachable from where a player can be: an opening into the hull's thickness nearby.\n")
        L.append("| # | cell centre | faces | area m2 | e.g. |\n|---:|---|---:|---:|---|")
        for i, g in enumerate(cc[:25]):
            c = np.mean([x["at"] for x in g], axis=0)
            L.append(f"| {i + 1} | {where(m, c)} | {len(g)} | {sum(x['area'] for x in g):.3f} | {describe(m, g[0]['face'])} |")
        L.append("")
    if len(open_e):
        oe = [dict(at=((ea[e] + eb[e]) / 2).tolist(), area=float(np.linalg.norm(eb[e] - ea[e])), face=int(ef[e]))
              for e in open_e]
        cc = cluster(oe, 1.0)
        L.append(f"## Open edges seen from both sides ({len(open_e)} edges, {sum(x['area'] for x in oe):.1f} blocks; "
                 f"busiest cells)\n")
        L.append("A two-sided skin face's edge with nothing beyond it and air on both sides: a sheet's free edge (fine "
                 "for a sail, rope or thin board) or the rim of a hole.\n")
        L.append("| # | cell centre | edges | length | e.g. |\n|---:|---|---:|---:|---|")
        for i, g in enumerate(cc[:15]):
            c = np.mean([x["at"] for x in g], axis=0)
            L.append(f"| {i + 1} | {where(m, c)} | {len(g)} | {sum(x['area'] for x in g):.2f} | {describe(m, g[0]['face'])} |")
        L.append("")
    L.append(f"## T-junctions ({len(tj)} on visible faces; the busiest block cells)\n")
    L.append("| # | cell centre | count | e.g. |\n|---:|---|---:|---|")
    for i, g in enumerate(tj_cells[:15]):
        c = np.mean([x["at"] for x in g], axis=0)
        x = g[0]
        L.append(f"| {i + 1} | {where(m, c)} | {len(g)} | corner of {describe(m, x['face'])} on the edge of "
                 f"{describe(m, x['other'])} |")
    L.append("")
    with open(os.path.join(args.out, "report.md" if m.ship == "carrack" else f"report-{m.ship}.md"), "w",
              encoding="utf-8") as fh:
        fh.write("\n".join(L))

    def clean(x):
        y = dict(x)
        for k in ("a", "b", "face", "other"):
            if k in y and isinstance(y[k], int):
                y[k + "_desc"] = describe(m, y[k])
        return y

    with open(os.path.join(args.out, f"meshcheck-{m.ship}.json"), "w", encoding="utf-8") as fh:
        json.dump(dict(ship=m.ship, conflicts=[clean(x) for x in conflicts], near=[clean(x) for x in nears],
                       duplicates=[clean(x) for x in dups], hidden_overlaps=len(hid_zf),
                       cracks=[clean(x) for x in cr_sorted[:2000]], leaks=leaks, leak_notes=leak_notes,
                       cavity=[clean(x) for x in cav[:2000]], tjunctions=len(tj)), fh, indent=0)
    log(f"report: {os.path.join(args.out, 'report.md')}")
    print(f"z-fighting visible: {len(conflicts)} conflicts ({len(serious)} >= {SERIOUS_AREA:g} m2), {len(nears)} near, "
          f"{len(dups)} duplicates; hidden {len(hid_zf)}. cracks {len(cr)}, cavity {len(cav)}, leaks {len(bad_leaks)} "
          f"(+{len(ok_leaks)} at openings), T-junctions {len(tj)}. {time.time() - t_start:.0f} s")
    for i, x in enumerate(conflicts[:10]):
        print(f"  {i + 1:2d}. {x['area']:.4f} m2 at {where(m, x['at'])}: {describe(m, x['a'])} / {describe(m, x['b'])}")
    return 1 if serious else 0


if __name__ == "__main__":
    sys.exit(main())
