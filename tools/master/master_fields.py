"""The master model's structure as half-breadth fields (docs/master-model.md section 6). Pure numpy, except sample().

A hull is y-convex: at every (x, z) she spans |y| <= w(x, z), tumblehome or not. So every body of her structure is an
interval of |y| at each (x, z): the shell w_t..w, a rib w_rib..w_t, a deck 0..w_t, the backbone w_bb..125. The inner
surfaces are erosions of w by a ball (erode; the ball sampled on the grid, within about 6 mm of exact for a 250 ball):
no CAD offsets, which fail on her converging ends and ledges.

A body whose interval changes by rule across a line (a deck narrower inside a rib's cell, a rib's floor timber running
in to the centreline) is made of parts, one per rule, meeting on that line (parts): a node grid can't step there.

Grid: nodes every H mm in X and Z. w is sampled from her uncut solid just above each node's height (so a horizontal
face on a node's height, her bottom or the overhang's underside, belongs to the cells above it). Outside her, w = OUT.
"""
import numpy as np

H = 62.5                     # 1/16 block: the texel
OUT = -1.0e9


def grid(x0=-20000.0, x1=28000.0, z0=0.0, z1=15500.0, h=H):
    return np.arange(x0, x1 + h / 2, h), np.arange(z0, z1 + h / 2, h)


def sample(shape, xs, zs):
    """w[k, i]: the port half's breadth at (xs[i], zs[k]) of a half-hull solid (its centreline face on y = 0), from a
    horizontal slice of it just above zs[k] (just below for the top row). OUT where she isn't."""
    import FreeCAD as App
    w = np.full((len(zs), len(xs)), OUT)
    top = shape.BoundBox.ZMax              # (OCC's box: at or over her top)
    for k, z in enumerate(zs):
        if z > top:
            continue
        zz = z + 0.5 if z + 0.5 < top else z - 0.5
        pts = []
        for wire in shape.slice(App.Vector(0, 0, 1), zz):
            p = np.array([(v.x, v.y) for v in wire.discretize(Distance=20.0)])
            pts.append(np.vstack([p, p[:1]]))
        if not pts:
            continue
        for p in pts:
            a, b = p[:-1], p[1:]
            lo, hi = np.minimum(a[:, 0], b[:, 0]), np.maximum(a[:, 0], b[:, 0])
            span = (xs[None, :] >= lo[:, None]) & (xs[None, :] <= hi[:, None]) & (hi - lo > 1e-9)[:, None]
            t = np.where(span, (xs[None, :] - a[:, 0:1]) / np.where(hi - lo > 1e-9, b[:, 0] - a[:, 0], 1.0)[:, None], 0.0)
            y = a[:, 1:2] + t * (b[:, 1] - a[:, 1])[:, None]
            ymax = np.where(span, y, OUT).max(axis=0)
            w[k] = np.maximum(w[k], np.where(ymax > 0.5, ymax, OUT))
    return w


def erode(w, r, h=H):
    """w eroded by a ball of radius r: at each (x, z) the half-breadth of the points at least r inside her (OUT where
    there are none; may be negative: then nothing at that (x, z) is r deep)."""
    n = int(np.ceil(r / h))
    nz, nx = w.shape
    pad = np.full((nz + 2 * n, nx + 2 * n), OUT)
    pad[n:n + nz, n:n + nx] = w
    out = w - r
    for dk in range(-n, n + 1):
        for di in range(-n, n + 1):
            d2 = (dk * h) ** 2 + (di * h) ** 2
            if d2 > r * r or (dk == 0 and di == 0):
                continue
            sh = pad[n + dk:n + dk + nz, n + di:n + di + nx]
            out = np.minimum(out, sh - np.sqrt(r * r - d2))
    return np.where(w > 0, out, OUT)


def parts(base, labels, variants):
    """A body made of parts, one per label (a rule for its interval): variants[label] = (lo, hi) arrays. A part is its
    label's nodes, and the nodes of higher labels next to them (8 around) with this label's interval: so every grid quad
    on a line between labels belongs to exactly one part, the lowest label's, and no quad is left out at a corner."""
    out = []
    nz, nx = labels.shape
    for lab in sorted(variants):
        lo, hi = variants[lab]
        own = base & (labels == lab)
        if not own.any():
            continue
        pad = np.pad(own, 1, constant_values=False)
        near = np.zeros_like(own)
        for dk in (-1, 0, 1):
            for di in (-1, 0, 1):
                near |= pad[1 + dk:1 + dk + nz, 1 + di:1 + di + nx]
        ex = (own | (near & base & (labels > lab))) & (hi > lo)
        out.append((ex, lo, hi))
    return out


def bodies(xs, zs, w, sheer_z, p, ribs, decks):
    """Every body as a list of parts (exists, lo, hi) over the node grid: present where exists, spanning
    lo <= |y| <= hi. p: the parameters (mm); ribs: [(x0, x1)] the rib timbers' X ranges; decks:
    [(x0, x1, z0, z1, has_beams)]."""
    X, Z = np.meshgrid(xs, zs)
    t, rib_m, bd = p["shellThickness"], p["ribMoulded"], p["beamDepth"]
    w_t = erode(w, t)
    w_r = erode(w, t + rib_m)
    w_b = erode(w, p["backboneMoulded"])
    inside = (w > 0) & (Z <= sheer_z[None, :] + 1e-6)
    pos = lambda a: np.maximum(a, 0.0)     # noqa: E731
    rib_x = np.zeros_like(X, bool)
    for x0, x1 in ribs:
        rib_x |= (X >= x0 - 1e-6) & (X <= x1 + 1e-6)
    half_bb = p["backboneSided"] / 2
    bb_on = inside & (pos(w_b) < half_bb) & (Z <= p["backboneTop"] + 1e-6)
    bb = bb_on.astype(int)
    hb = np.full_like(w, half_bb)
    zero = np.zeros_like(w)
    out = {}

    out["Backbone"] = parts(bb_on, zero.astype(int), {0: (pos(w_b), np.minimum(half_bb, w))})
    # the shell stops at the backbone
    out["Shell"] = parts(inside, bb, {0: (pos(w_t), w), 1: (np.maximum(pos(w_t), hb), w)})
    # a rib: its timber ribMoulded deep inside the shell; across the bottom a floor timber, in to the backbone
    floor = (Z >= p["floorTimberBottom"] - 1e-6) & (Z <= p["floorTimberTop"] + 1e-6)
    out["Ribs"] = parts(inside & rib_x, floor.astype(int) * 2 + bb,
                        {0: (pos(w_r), pos(w_t)), 1: (np.maximum(pos(w_r), hb), pos(w_t)),
                         2: (zero, pos(w_t)), 3: (hb, pos(w_t))})

    d_on = np.zeros_like(X, bool)
    b_on = np.zeros_like(X, bool)
    for x0, x1, z0, z1, beams in decks:
        inx = (X >= x0 - 1e-6) & (X <= x1 + 1e-6)
        d_on |= inx & (Z >= z0 - 1e-6) & (Z <= z1 + 1e-6)
        if beams:
            b_on |= inx & rib_x & (Z >= z0 - bd - 1e-6) & (Z <= z0 + 1e-6)
    # decks out to the shell between ribs, to the ribs' faces in their cells; decks and beams butt the backbone (the
    # stem and the sternpost run up through them). A beam shares its top row of nodes with its deck, no volume
    out["Decks"] = parts(inside & d_on, rib_x.astype(int) * 2 + bb,
                         {0: (zero, pos(w_t)), 1: (hb, pos(w_t)), 2: (zero, pos(w_r)), 3: (hb, pos(w_r))})
    out["Beams"] = parts(inside & b_on, bb, {0: (zero, pos(w_r)), 1: (hb, pos(w_r))})
    return out, {"w_t": w_t, "w_rib": w_r, "w_bb": w_b}


def mesh(xs, zs, body):
    """Triangles (n, 3, 3) of a body's parts' boundaries, both sides of the centreline, wound outward: each part's
    outer surface |y| = hi and inner |y| = lo over every grid quad it fills, and walls along the quads' edges where it
    stops."""
    t = [_mesh_part(xs, zs, ex, lo, hi) for ex, lo, hi in body]
    t = [x for x in t if len(x)]
    return np.concatenate(t, 0) if t else np.zeros((0, 3, 3))


def _mesh_part(xs, zs, exists, lo, hi):
    q = exists[:-1, :-1] & exists[1:, :-1] & exists[:-1, 1:] & exists[1:, 1:]
    tris = []
    kk, ii = np.nonzero(q)

    def quad(k, i, ys_fn, flip):
        # corners (k,i) (k,i+1) (k+1,i+1) (k+1,i) in (x, z), y from ys_fn
        c = [(k, i), (k, i + 1), (k + 1, i + 1), (k + 1, i)]
        P = np.stack([np.stack([xs[i_], ys_fn(k_, i_), zs[k_]], -1) for k_, i_ in c], 1)   # (n, 4, 3)
        a, b = (P[:, [0, 1, 2]], P[:, [0, 2, 3]]) if not flip else (P[:, [0, 2, 1]], P[:, [0, 3, 2]])
        return [a, b]

    hi_f = lambda k, i: hi[k, i]           # noqa: E731
    lo_f = lambda k, i: lo[k, i]           # noqa: E731
    tris += quad(kk, ii, hi_f, False)
    inner = (lo[kk, ii] > 1) | (lo[kk + 1, ii] > 1) | (lo[kk, ii + 1] > 1) | (lo[kk + 1, ii + 1] > 1)
    tris += quad(kk[inner], ii[inner], lo_f, True)

    # walls: an edge of a filled quad whose neighbour across it isn't filled
    qp = np.pad(q, 1, constant_values=False)
    for dk, di, e in ((-1, 0, ((0, 0), (0, 1))), (1, 0, ((1, 1), (1, 0))), (0, -1, ((1, 0), (0, 0))), (0, 1, ((0, 1), (1, 1)))):
        nb = qp[1 + dk:1 + dk + q.shape[0], 1 + di:1 + di + q.shape[1]]
        k, i = np.nonzero(q & ~nb)
        (ka, ia), (kb, ib) = e
        A = np.stack([xs[i + ia], lo[k + ka, i + ia], zs[k + ka]], -1)
        B = np.stack([xs[i + ib], lo[k + kb, i + ib], zs[k + kb]], -1)
        C = np.stack([xs[i + ib], hi[k + kb, i + ib], zs[k + kb]], -1)
        D = np.stack([xs[i + ia], hi[k + ka, i + ia], zs[k + ka]], -1)
        tris += [np.stack([A, B, C], 1), np.stack([A, C, D], 1)]
    tris = [x for x in tris if len(x)]
    if not tris:
        return np.zeros((0, 3, 3))
    t = np.concatenate(tris, 0)
    t = t[:, ::-1]                          # (wound outward: the quads above are built inward)
    m = t.copy()
    m[:, :, 1] *= -1
    m = m[:, ::-1]                          # the starboard half: mirrored, its winding reversed
    return np.concatenate([t, m], 0)
