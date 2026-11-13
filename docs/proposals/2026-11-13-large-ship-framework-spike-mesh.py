"""THROWAWAY SPIKE for docs/proposals/2026-11-13-large-ship-framework.md, section 5 (backlog SH-0014).

Not part of any pipeline, not used by the mod or its tests. It shows one thing: an arbitrary 3D model of a ship can be
turned into the same half-breadth field the generator works on, and so into planking cut by block cells, without the
model being watertight or clean. Three inputs:

  caravel   the caravel's own game mesh (assets/ships/config/shipmeshes/caravel.json), where the true lines are known
  bbmodel   a Blockbench model in Vintage Story's format (cubes in named groups), e.g. Knarr.bbmodel
  glb       a glTF binary, e.g. a 3D Warehouse download

    python docs/proposals/2026-11-13-large-ship-framework-spike-mesh.py caravel
    python docs/proposals/2026-11-13-large-ship-framework-spike-mesh.py bbmodel <file.bbmodel>
    python docs/proposals/2026-11-13-large-ship-framework-spike-mesh.py glb <file.glb> <hull up to this height>

It needs numpy only and writes nothing. In every case the ship is brought to: L along her length, U up, B across.
"""
import collections
import json
import os
import struct
import sys
import time

import numpy as np

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
H = 0.05          # node spacing of the field, m (blocks)
SKIN = 0.2        # planking thickness for the cell cut, m


# ---------------------------------------------------------------------------------------------- mesh -> field

def sample_points(tri, per_m2=1600, seed=0):
    """Points spread over triangles (n x 3 x 3), about per_m2 a square metre, the same every run."""
    a, b, c = tri[:, 0], tri[:, 1], tri[:, 2]
    area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    n = np.maximum(1, np.ceil(area * per_m2)).astype(int)
    idx = np.repeat(np.arange(len(tri)), n)
    r = np.random.default_rng(seed).random((len(idx), 2))
    flip = r.sum(axis=1) > 1
    r[flip] = 1 - r[flip]
    return a[idx] + r[:, :1] * (b - a)[idx] + r[:, 1:] * (c - a)[idx]


def field_from_points(p, centre, l0, l1, u0, u1):
    """Two fields, one a side: w[k, i] is the outermost |B - centre| of any point at (L node i, U node k). A hull needs
    no closed surface for this: a node nothing hit stays 0 and is filled later from its neighbours along the length."""
    nl, nu = int(round((l1 - l0) / H)), int(round((u1 - u0) / H))
    i = np.floor((p[:, 0] - l0) / H).astype(int)
    k = np.floor((p[:, 1] - u0) / H).astype(int)
    ok = (i >= 0) & (i < nl) & (k >= 0) & (k < nu)
    side = []
    for sgn in (1, -1):
        w = np.zeros(nl * nu)
        d = sgn * (p[:, 2] - centre)
        m = ok & (d > 0)
        np.maximum.at(w, k[m] * nl + i[m], d[m])
        side.append(w.reshape(nu, nl))
    return side[0], side[1]


def fill_holes(w, widest=0.5):
    """Nodes with no hit between two hits along the length take a straight line between them, across a gap of up to
    `widest` m (a hole in the mesh, a port). A longer gap stays open: there is no hull there (the waist between castles)."""
    holes = 0
    for row in w:
        nz = np.nonzero(row)[0]
        for a, b in zip(nz[:-1], nz[1:]):
            if 1 < b - a <= widest / H:
                miss = np.arange(a + 1, b)
                holes += len(miss)
                row[miss] = np.interp(miss, [a, b], [row[a], row[b]])
    return holes


def erode(w, r):
    """The half-breadth of the points at least r inside her (a ball of radius r), as tools/master/master_fields.py."""
    n = int(np.ceil(r / H))
    top = np.nonzero(w.any(axis=1))[0].max()
    pad = np.zeros((w.shape[0] + 2 * n, w.shape[1] + 2 * n))
    pad[n:-n, n:-n] = w
    pad[n + top + 1:n + top + 1 + n] = pad[n + top]            # her side goes on up: the top is open, not a lid
    out = np.full(w.shape, np.inf)
    for dk in range(-n, n + 1):
        for di in range(-n, n + 1):
            d2 = (dk * H) ** 2 + (di * H) ** 2
            if d2 <= r * r:
                out = np.minimum(out, pad[n + dk:n + dk + w.shape[0], n + di:n + di + w.shape[1]] - np.sqrt(r * r - d2))
    return np.clip(np.minimum(out, w), 0, None)


def cells(w_out, w_in, l0, u0):
    """Litres of planking in each block cell of one side (borders on whole blocks along L and U, at 0.5, 1.5 ... across;
    the centre cell counted whole)."""
    ci = np.floor((l0 + (np.arange(w_out.shape[1]) + 0.5) * H)).astype(int)
    ck = np.floor((u0 + (np.arange(w_out.shape[0]) + 0.5) * H)).astype(int)
    out = collections.defaultdict(float)
    for j in range(0, 8):
        lo, hi = (0.0 if j == 0 else j - 0.5), j + 0.5
        part = np.clip(np.minimum(w_out, hi) - np.maximum(w_in, lo), 0, None) * (2 if j == 0 else 1)
        for k in np.unique(ck):
            row = part[ck == k].sum(axis=0)
            for i in np.unique(ci):
                v = row[ci == i].sum() * H * H * 1000
                if v > 0.0005:
                    out[(int(i), j, int(k))] = v
    return out


def report(name, wp, ws, l0, u0, waterline=None):
    sym = np.abs(wp - ws)[(wp > 0) & (ws > 0)]
    w = np.maximum(wp, ws)
    holes = fill_holes(w)
    ln = np.nonzero(w.any(axis=0))[0]
    un = np.nonzero(w.any(axis=1))[0]
    print("%s: length %.2f, beam %.2f, height %.2f; port against starboard: median %.3f, 95 %% %.3f; %d nodes filled"
          % (name, (ln[-1] - ln[0] + 1) * H, 2 * w.max(), (un[-1] - un[0] + 1) * H, np.median(sym), np.percentile(sym, 95), holes))
    if waterline is not None:
        k = (u0 + (np.arange(w.shape[0]) + 0.5) * H) < waterline
        print("  displacement below the waterline: %.1f m3" % (2 * w[k].sum() * H * H))
    return w


# ---------------------------------------------------------------------------------------------- the caravel

def caravel():
    t0 = time.time()
    mesh = json.load(open(os.path.join(ROOT, "assets", "ships", "config", "shipmeshes", "caravel.json")))
    lines = json.load(open(os.path.join(ROOT, "models", "caravel", "caravel-lines.json")))
    rep = json.load(open(os.path.join(ROOT, "models", "caravel", "caravel-report.json")))
    cad = json.load(open(os.path.join(ROOT, "models", "caravel", "cells.json")))
    wl = lines["levels"]["waterline"] / 1000.0             # the game's y 0 is her waterline
    l0, l1, u0, u1 = -17.0, 11.0, 0.0, 8.0

    def tris(groups):
        out = []
        for g in groups:
            q = np.array([[r[1 + 8 * c:4 + 8 * c] for c in range(4)] for r in mesh["groups"][g]], float)   # a quad: 4 x (x y z u v n)
            out += [q[:, [0, 1, 2]], q[:, [0, 2, 3]]]
        t = np.concatenate(out)
        t[:, :, 1] += wl                                    # U from the bottom cell border, as the lines
        return t

    # the truth: her lines, as the generator would hold them
    xs = l0 + (np.arange(int(round((l1 - l0) / H))) + 0.5) * H
    zs = u0 + (np.arange(int(round((u1 - u0) / H))) + 0.5) * H
    zz = np.array([l["z"] for l in lines["hull"]]) / 1000.0
    rows = []
    for l in lines["hull"]:
        p = np.array(l["pts"]) / 1000.0
        o = np.argsort(p[:, 0], kind="stable")
        rows.append(np.where((xs >= p[o[0], 0]) & (xs <= p[o[-1], 0]), np.interp(xs, p[o, 0], p[o, 1]), 0.0))
    true = np.zeros((len(zs), len(xs)))
    for k, z in enumerate(zs):
        if zz[0] <= z <= zz[-1]:
            j = min(np.searchsorted(zz, z, side="right") - 1, len(zz) - 2)
            t = (z - zz[j]) / (zz[j + 1] - zz[j])
            true[k] = np.where((rows[j] > 0) & (rows[j + 1] > 0), rows[j] + t * (rows[j + 1] - rows[j]), 0.0)
    sh = np.array(lines["sheer"]) / 1000.0
    true *= zs[:, None] <= np.interp(xs, sh[:, 0], sh[:, 1])[None, :]

    everything = sorted(mesh["groups"])
    tagged = [g for g in everything if g.startswith("slot:hull") or g in ("exterior", "interior")]
    for label, groups in (("every group of the mesh (guns, rig, sails, anchor and all)", everything),
                          ("the hull's groups only, picked by name (the sidecar's job)", tagged)):
        t = tris(groups)
        t1 = time.time()
        wp, ws = field_from_points(sample_points(t), 0.0, l0, l1, u0, u1)
        print("caravel, %s: %d triangles, sampled in %.1f s" % (label, len(t), time.time() - t1))
        w = report("  field", ws, wp, l0, u0, wl)
        err = np.abs(w - true)[(w > 0) & (true > 0)]
        print("  against her true lines: median %.3f m, 95 %% %.3f m, over 0.1 m at %.1f %% of nodes; CAD displacement %.1f m3"
              % (np.median(err), np.percentile(err, 95), 100 * (err > 0.1).mean(), rep["waterline"]["displacement_m3"]))
    mine = cells(w, erode(w, SKIN), l0, u0)
    ref = collections.defaultdict(float)
    for c in cad["cells"]:
        v = sum(p["litres"] for p in c["pieces"] if p["body"] == "Shell")
        if v:
            ref[(c["cell"][0], abs(c["cell"][1]), c["cell"][2])] += v / (1 if c["cell"][1] == 0 else 2)
    both = sorted(set(mine) & set(ref))
    rel = np.array([abs(mine[c] - ref[c]) / max(ref[c], 1.0) for c in both])
    print("  planking cut by cells from that field: %d cells a side, the CAD slice %d, in both %d; within 10 %% of the CAD's"
          " volume %d, within 25 %% %d" % (len(mine), len(ref), len(both), (rel < 0.10).sum(), (rel < 0.25).sum()))
    print("  total %.1f s" % (time.time() - t0))


# ---------------------------------------------------------------------------------------------- Blockbench

def rot_xyz(deg):
    x, y, z = np.radians(deg)
    rx = np.array([[1, 0, 0], [0, np.cos(x), -np.sin(x)], [0, np.sin(x), np.cos(x)]])
    ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]])
    rz = np.array([[np.cos(z), -np.sin(z), 0], [np.sin(z), np.cos(z), 0], [0, 0, 1]])
    return rx @ ry @ rz                                    # Vintage Story's order (a hull's two sides mirror only so)


BOX_TRIS = [(0, 1, 3), (0, 3, 2), (4, 6, 7), (4, 7, 5), (0, 4, 5), (0, 5, 1), (2, 3, 7), (2, 7, 6), (0, 2, 6), (0, 6, 4), (1, 5, 7), (1, 7, 3)]


def bbmodel(path):
    t0 = time.time()
    d = json.load(open(path, encoding="utf-8"))
    els = {e["uuid"]: e for e in d["elements"]}
    groups = {g["uuid"]: g for g in d.get("groups", [])}
    boxes = []

    def walk(n, chain, names):
        if isinstance(n, str):
            e = els.get(n)
            if e is not None:
                f, t = np.array(e["from"], float), np.array(e["to"], float)
                c = np.array([[x, y, z] for x in (f[0], t[0]) for y in (f[1], t[1]) for z in (f[2], t[2])])
                for o, r in [(np.array(e.get("origin", [0, 0, 0]), float), e.get("rotation", [0, 0, 0]))] + chain[::-1]:
                    if any(abs(a) > 1e-9 for a in r):
                        c = (c - o) @ rot_xyz(r).T + o
                boxes.append((names, c / 16.0, np.sort(np.abs(t - f)) / 16.0))   # 16 pixels a block
            return
        g = groups.get(n.get("uuid"), n)
        for ch in n.get("children", []):
            walk(ch, chain + [(np.array(g.get("origin", [0, 0, 0]), float), g.get("rotation", [0, 0, 0]))], names + [g.get("name")])

    for r in d["outliner"]:
        walk(r, [], [])
    by = collections.defaultdict(list)
    for names, c, size in boxes:
        by[names[1] if len(names) > 1 else names[0]].append((c, size))
    print("%s: format %s, %d cubes, %d animations; groups under the root:"
          % (os.path.basename(path), d["meta"].get("model_format"), len(boxes), len(d.get("animations", []))))
    for g, bs in by.items():
        p = np.concatenate([c for c, _ in bs])
        print("  %-18s %4d cubes, L %6.2f..%6.2f  U %5.2f..%5.2f  B %5.2f..%5.2f, median thickness %.3f"
              % (g, len(bs), p[:, 2].min(), p[:, 2].max(), p[:, 1].min(), p[:, 1].max(), p[:, 0].min(), p[:, 0].max(),
                 np.median([s[0] for _, s in bs])))
    hull = [g for g in by if g.lower().startswith("hull") or g.lower() == "keel"]
    corners = [c for g in hull for c, _ in by[g]]
    tri = np.array([c[list(t)] for c in corners for t in BOX_TRIS])[:, :, [2, 1, 0]]   # this model: length z, up y, across x
    centre = np.median(np.concatenate([c for c, _ in by["Keel"]])[:, 0])
    u_low = np.floor(tri[:, :, 1].min())
    l0, l1 = np.floor(tri[:, :, 0].min()) - 1, np.ceil(tri[:, :, 0].max()) + 1
    wp, ws = field_from_points(sample_points(tri), centre, l0, l1, u_low, u_low + 8)
    w = report("  the Hull* and Keel groups as a field (centre plane at %.2f)" % centre, wp, ws, l0, u_low)
    mine = cells(w, erode(w, SKIN), l0, u_low)
    print("  planking cut by cells: %d cells a side; cells across at her widest: %d; pieces under 4 litres: %d"
          % (len(mine), 2 * max(c[1] for c in mine) + 1, sum(1 for v in mine.values() if v < 4.0)))
    span = [len({tuple(q) for q in np.floor(c).astype(int)}) for c in corners]
    print("  hull and keel cubes whose corners lie in more than one block cell: %d of %d" % (sum(1 for s in span if s > 1), len(span)))
    if "Deck" in by:
        top = np.percentile(np.concatenate([c for c, _ in by["Deck"]])[:, 1], 95)
        mid = w[:, w.shape[1] // 2]
        k = int((top + 0.5 - u_low) / H)
        print("  deck top about U %.2f: %.2f over her lowest point; her side's top amidships %.2f over the deck; clear"
              " width half a block over the deck %.2f" % (top, top - tri[:, :, 1].min(), (np.nonzero(mid)[0].max() + 1) * H + u_low - top,
                                                          2 * erode(w, SKIN)[k].max()))
    print("  total %.1f s" % (time.time() - t0))


# ---------------------------------------------------------------------------------------------- glTF binary

def glb(path, cap):
    t0 = time.time()
    b = open(path, "rb").read()
    off, js, blob = 12, None, None
    while off < len(b):
        n, kind = struct.unpack_from("<II", b, off)
        chunk = b[off + 8:off + 8 + n]
        off += 8 + n
        if kind == 0x4E4F534A:
            js = json.loads(chunk)
        elif kind == 0x004E4942:
            blob = chunk
    types = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
    width = {"SCALAR": 1, "VEC3": 3}

    def acc(i):
        a = js["accessors"][i]
        bv = js["bufferViews"][a["bufferView"]]
        dt, nc = np.dtype(types[a["componentType"]]), width[a["type"]]
        start, stride = bv.get("byteOffset", 0) + a.get("byteOffset", 0), bv.get("byteStride", 0) or dt.itemsize * nc
        raw = np.frombuffer(blob, np.uint8, count=stride * (a["count"] - 1) + dt.itemsize * nc, offset=start)
        pick = (np.arange(a["count"])[:, None] * stride + np.arange(dt.itemsize * nc)[None, :]).ravel()
        return raw[pick].view(dt).reshape(a["count"], nc)

    def matrix(n):
        if "matrix" in n:
            return np.array(n["matrix"], float).reshape(4, 4).T
        m = np.eye(4)
        if "scale" in n:
            m = np.diag(list(n["scale"]) + [1.0])
        if "rotation" in n:
            x, y, z, w = n["rotation"]
            m = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), 0],
                          [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), 0],
                          [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y), 0], [0, 0, 0, 1]]) @ m
        if "translation" in n:
            t = np.eye(4)
            t[:3, 3] = n["translation"]
            m = t @ m
        return m

    parts = []

    def walk(i, m, name):
        n = js["nodes"][i]
        m = m @ matrix(n)
        name = n.get("name") or name
        if "mesh" in n:
            for p in js["meshes"][n["mesh"]]["primitives"]:
                if p.get("mode", 4) == 4:
                    v = acc(p["attributes"]["POSITION"]).astype(float) @ m[:3, :3].T + m[:3, 3]
                    f = acc(p["indices"]).astype(np.int64).reshape(-1, 3) if "indices" in p else np.arange(len(v)).reshape(-1, 3)
                    parts.append((name, v[f]))
        for c in n.get("children", []):
            walk(c, m, name)

    for r in js["scenes"][js.get("scene", 0)]["nodes"]:
        walk(r, np.eye(4), "")
    with_open_edges = 0
    for _, t in parts:
        _, inv = np.unique(np.round(t.reshape(-1, 3) / 1e-4).astype(np.int64), axis=0, return_inverse=True)
        f = inv.reshape(-1, 3)
        _, cnt = np.unique(np.sort(np.concatenate([f[:, [0, 1]], f[:, [1, 2]], f[:, [2, 0]]]), axis=1), axis=0, return_counts=True)
        with_open_edges += bool((cnt == 1).any())
    named = sorted({n for n, _ in parts if n and not n.rstrip("_#0123456789").endswith("Geom3D")})
    print("%s: %d parts, %d triangles, %d parts with open edges; part names that say something: %s"
          % (os.path.basename(path), len(parts), sum(len(t) for _, t in parts), with_open_edges, named[:12]))
    solid = [t for _, t in parts if (t.reshape(-1, 3).max(0) - t.reshape(-1, 3).min(0)).min() > 1e-6]   # no flat ground planes
    tri = np.concatenate(solid)
    tri = tri[tri[:, :, 1].max(axis=1) <= cap]              # the sidecar's "hull up to this height": no masts, yards, sails
    p = tri.reshape(-1, 3)
    ext = p.max(0) - p.min(0)
    la = 0 if ext[0] > ext[2] else 2                        # y is up in glTF; her length is the longer way across
    tri = tri[:, :, [la, 1, 2 - la]]
    p = tri.reshape(-1, 3)
    centre = (np.percentile(p[:, 2], 1) + np.percentile(p[:, 2], 99)) / 2
    u0, l0, l1 = np.floor(p[:, 1].min()), np.floor(p[:, 0].min()) - 1, np.ceil(p[:, 0].max()) + 1
    wp, ws = field_from_points(sample_points(tri), centre, l0, l1, u0, u0 + np.ceil(cap - u0) + 1)
    w = report("  everything under U %.1f as a field" % cap, wp, ws, l0, u0)
    mine = cells(w, erode(w, SKIN), l0, u0)
    print("  planking cut by cells: %d cells a side; pieces under 4 litres: %d; total %.1f s"
          % (len(mine), sum(1 for v in mine.values() if v < 4.0), time.time() - t0))


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "caravel"
    if mode == "caravel":
        caravel()
    elif mode == "bbmodel":
        bbmodel(sys.argv[2])
    elif mode == "glb":
        glb(sys.argv[2], float(sys.argv[3]))
