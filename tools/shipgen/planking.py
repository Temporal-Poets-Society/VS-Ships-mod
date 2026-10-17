"""
Planked hull surfaces as a mesh. Voxels stair-step every curve; here the hull is a smooth surface made of rings (hull
outlines at given heights) joined into quads that share their corners: no gaps, no overlaps. Each strake shows exactly
one plank of the vanilla plank texture (with its seam along the top edge), so the planking reads like real strakes.

The same rings pushed inboard give the inside of the hull (the ceiling), so the hull bends inside like outside. The
voxel hull stays underneath for collision only.

Openings (windows, entry ports) are cut out of the strakes exactly: ring levels are placed at the openings' top and
bottom, and quads are split at their sides.

The hull's planking quads are marked (hull="out" outside, "in" the ceiling): the mesh file lists them per group, so the
mod can draw each hull panel's damage on them and cut a hole through both.
"""
import numpy as np

# the three planks of a vanilla 32x32 plank texture, in texels (0..16): seams at rows 0, 11 and 22 of 32
BANDS = ((0.0, 5.5), (5.5, 11.0), (11.0, 16.0))


def outline(half_width, y, xa, xb, n=320):
    """Starboard hull outline at height y from xa to xb, sampled densely near the ends where it curves fastest."""
    t = np.linspace(0, 1, n)
    x = xa + (xb - xa) * (1 - np.cos(np.pi * t)) / 2
    z = half_width(x, np.full_like(x, y))
    return x, z


def offset_outline(x, z, d):
    """Move a starboard outline (x increasing, z >= 0) inboard by d in the horizontal plane; drop what folds over."""
    tx, tz = np.gradient(x), np.gradient(z)
    ln = np.hypot(tx, tz) + 1e-12
    nx, nz = -tz / ln, tx / ln                     # outward
    xo, zo = x - nx * d, z - nz * d
    keep = zo > 0.03
    xo, zo = xo[keep], zo[keep]
    if len(xo) == 0:
        return xo, zo
    # keep x increasing (the offset folds over itself in tight curves)
    mono = [0]
    for i in range(1, len(xo)):
        if xo[i] > xo[mono[-1]] + 1e-6:
            mono.append(i)
    return xo[mono], zo[mono]


def resample(x, z, m):
    s = np.concatenate([[0.0], np.cumsum(np.hypot(np.diff(x), np.diff(z)))])
    u = np.linspace(0, s[-1], m + 1)
    return np.interp(u, s, x), np.interp(u, s, z), s[-1]


def ring(half_width, y, xa, xb, m, offset=0.0):
    x, z = outline(half_width, y, xa, xb)
    if offset:
        x, z = offset_outline(x, z, offset)
    rx, rz, _ = resample(x, z, m)
    return rx, rz


# (Hansa) a faceted hull's offset outline (the ceiling) has short steep spikes at each crease: xgrid_rings looks for
# where a ring's ends start after a median over this many points (1: as carrack-m)
KINK_WINDOW = 1


def xgrid_rings(half_width, levels, x_range, offset, xgrid, seg=0.7, slope=1.0):
    """
    (Hansa 0.3.26, step D: hull panels as slots) The rings of a skin sampled so that each has a point at every x of
    xgrid within its own run (between its ends, where it doesn't curve hard: |dz/dx| under `slope`, 0.3 in from its
    end at least), each grid x at the same index on every ring that has it: the planking's quads between two grid x that
    both rings have are cut square across at them (a frame strip's edges, a panel's), on the outside and on the ceiling
    alike. Each ring's ends (the stern, the bow) are sampled by arclength as skin() does (`seg` long at most), with as
    many more points as the grid x it doesn't have there. Returns (rings [(y, x[], z[])], each ring's (i_lo, i_hi): the
    first and last grid index in its run, c: grid x i is at index c + i), or None if a ring has no run.
    """
    G = sorted(float(g) for g in xgrid)
    outs = []
    for y in levels:
        xa, xb = x_range(y)
        x, z = outline(half_width, y, xa, xb, 900)
        off = offset(y) if callable(offset) else offset
        if off:
            x, z = offset_outline(x, z, off)
        if len(x) < 4:
            return None
        x, z = np.asarray(x, float), np.asarray(z, float)
        # ((Hansa 0.3.29) a run cut at a grid x (the ceiling's parts where the weather deck steps): its end exactly
        # there (the offset moved it a little), and the grid run to it)
        on_g = [any(abs(e - g) < 1e-6 for g in G) for e in (xa, xb)]
        if on_g[0] or on_g[1]:
            keep = np.ones(len(x), bool)
            if on_g[0]:
                keep &= x > xa + 1e-6
            if on_g[1]:
                keep &= x < xb - 1e-6
            xs_, zs_ = x[keep], z[keep]
            if on_g[0]:
                xs_, zs_ = np.concatenate([[xa], xs_]), np.concatenate([[np.interp(xa, x, z)], zs_])
            if on_g[1]:
                xs_, zs_ = np.concatenate([xs_, [xb]]), np.concatenate([zs_, [np.interp(xb, x, z)]])
            x, z = xs_, zs_
        s = np.abs(np.gradient(z, x))
        if KINK_WINDOW > 1:
            from scipy.ndimage import median_filter
            s = median_filter(s, size=KINK_WINDOW, mode="nearest")
        mid = int(np.argmax(z))
        aft = np.nonzero(s[:mid + 1] >= slope)[0]
        fwd = np.nonzero(s[mid:] >= slope)[0]
        lo = max(x[aft[-1]] + 0.05 if len(aft) else x[0], x[0] + (0.0 if on_g[0] else 0.3))
        hi = min(x[mid + fwd[0]] - 0.05 if len(fwd) else x[-1], x[-1] - (0.0 if on_g[1] else 0.3))
        idx = [i for i, g in enumerate(G) if lo - 1e-9 <= g <= hi + 1e-9]
        if len(idx) < 2:
            return None
        outs.append((y, x, z, idx[0], idx[-1]))

    def part(x, z, a, b):
        """The outline from x = a to b (the points between and the ends interpolated)."""
        keep = (x > a + 1e-9) & (x < b - 1e-9)
        xs = np.concatenate([[a], x[keep], [b]])
        zs = np.concatenate([[np.interp(a, x, z)], z[keep], [np.interp(b, x, z)]])
        return xs, zs

    def arclen(xs, zs):
        return float(np.sum(np.hypot(np.diff(xs), np.diff(zs))))

    c = max(max(1, int(np.ceil(arclen(*part(x, z, x[0], G[il])) / seg))) - il for _, x, z, il, _ in outs)
    c2 = max(max(1, int(np.ceil(arclen(*part(x, z, G[ih], x[-1])) / seg))) + ih for _, x, z, _, ih in outs)
    rings, runs = [], []
    for y, x, z, il, ih in outs:
        n_a, n_b = c + il, c2 - ih
        ax, az, _ = resample(*part(x, z, x[0], G[il]), n_a)
        bx, bz, _ = resample(*part(x, z, G[ih], x[-1]), n_b)
        ix = np.array(G[il:ih + 1])
        iz = np.interp(ix, x, z)
        ax[-1], bx[0] = G[il], G[ih]        # (exactly the grid's)
        rings.append((y, np.concatenate([ax[:-1], ix, bx[1:]]), np.concatenate([az[:-1], iz, bz[1:]])))
        runs.append((il, ih))
    return rings, runs, c


def _cut_ranges(xa, xb, y0, y1, side, cuts, splits=(), inward=False):
    """Parts [s0, s1] of a quad (x from xa at s=0 to xb at s=1, heights y0..y1) that aren't in an opening; split again
    at each x in splits (a castle wall's face: no piece shows on both sides of it). ((Hansa 0.3.30) inward: the inside
    planking's, where an opening's x may be its own (x0in, x1in: a gun port's jambs square to her side where it runs
    aslant, the inside's opening that much aft of the outside's))"""
    keep = [(0.0, 1.0)]
    if abs(xb - xa) > 1e-9:
        for xs_ in splits:
            sp = (xs_ - xa) / (xb - xa)
            keep = [r for a, b in keep for r in (((a, sp), (sp, b)) if a + 1e-4 < sp < b - 1e-4 else ((a, b),))]
    for c in cuts:
        if c.get("side", 0) not in (0, side):
            continue
        if y0 < c["y0"] - 1e-6 or y1 > c["y1"] + 1e-6:
            continue
        lo, hi = sorted((xa, xb))
        cx0, cx1 = (c.get("x0in", c["x0"]), c.get("x1in", c["x1"])) if inward else (c["x0"], c["x1"])
        if hi <= cx0 + 1e-9 or lo >= cx1 - 1e-9:
            continue
        sa = (cx0 - xa) / (xb - xa) if abs(xb - xa) > 1e-9 else 0.0
        sb = (cx1 - xa) / (xb - xa) if abs(xb - xa) > 1e-9 else 1.0
        sa, sb = sorted((sa, sb))
        out = []
        for a, b in keep:
            if a < sa:
                out.append((a, min(b, sa)))
            if b > sb:
                out.append((max(a, sb), b))
        keep = [(a, b) for a, b in out if b - a > 1e-4]
    return keep


def _lerp(p, q, s):
    return tuple(p[i] + (q[i] - p[i]) * s for i in range(3))


def skin(d, half_width, levels, x_range, m, mat_of, group="exterior", cuts=(), bottom=False, smooth="hull",
         offset=0.0, seed=0, splits=(), xgrid=None):
    """
    Both sides of the hull between the given heights. x_range(y) -> (xa, xb): the outline's extent at height y; every
    ring is resampled at the same m+1 points so neighbouring strakes share corners. mat_of(strake_index, y0, y1) ->
    material. offset > 0: the inside of the hull, that far inboard, facing in (or a function of the height: a shell
    thicker toward the bottom). cuts: openings {x0, x1, y0, y1, side (+1 starboard, -1 port, 0 both)}; their y0/y1
    must be ring levels. Returns the rings [(y, x[], z[])] for callers that need to close the ends.
    xgrid: (Hansa 0.3.26) x the rings all have a point at in the hull's run (xgrid_rings): the quads there are
    square across at them; each quad is marked "xend" (in a ring's end, not between two of xgrid's x) or not, and the
    skin is recorded in d.skin_record (its rings, its interior's ends, inward or not, its group).
    """
    rings = []
    square = None
    xg = xgrid_rings(half_width, levels, x_range, offset, xgrid) if xgrid is not None else None
    if xg is not None:
        rings, runs, c0 = xg
        m = len(rings[0][1]) - 1

        def square(j, k):
            """Is quad k of strake j between two grid x that both its rings have (square across at them)?"""
            i = k - c0
            return all(il <= i and i + 1 <= ih for il, ih in (runs[j], runs[j + 1]))
    else:
        for y in levels:
            xa, xb = x_range(y)
            rx, rz = ring(half_width, y, xa, xb, m, offset(y) if callable(offset) else offset)
            rings.append((y, rx, rz))
        if xgrid is not None:
            def square(j, k):           # (no run between its ends: every quad is in an end)
                return False
    inward = (offset(levels[0]) if callable(offset) else offset) > 0
    if xgrid is not None:
        if not hasattr(d, "skin_record"):
            d.skin_record = []
        Gs = sorted(float(g) for g in xgrid)
        d.skin_record.append({"rings": rings, "inward": inward, "group": group, "cuts": list(cuts), "bottom": bottom,
                              # (each ring's run: the x its grid points span)
                              "runs": [(Gs[il], Gs[ih]) for il, ih in runs] if xg else [None] * len(rings)})
    # ((Hansa 0.3.30, note 242: "textures here and aft and deck above the same locations are distorted and stretched")
    # each corner's u by where it is along its own ring (its arclength from a common x, 16 texels a block): where her
    # rings run apart at the bow and the stern (the stem's rake, the ceiling's easing in over the hold's floor) a quad
    # between two of them is sheared and its edges unequal; its texture follows them (the grain along the planks, a block
    # a texture) instead of being stretched over it. A quad that would span more than one texture is cut along its
    # length into pieces that don't)
    ref_x = float(np.mean([0.5 * (float(np.min(r[1])) + float(np.max(r[1]))) for r in rings])) if rings else 0.0
    pos = []
    for _, rx, rz in rings:
        rx, rz = np.asarray(rx, float), np.asarray(rz, float)
        sc = np.concatenate([[0.0], np.cumsum(np.hypot(np.diff(rx), np.diff(rz)))])
        if rx[0] <= ref_x <= rx[-1] and len(rx) > 1:
            s_ref = float(np.interp(ref_x, rx, sc))
        elif ref_x < rx[0]:
            s_ref = float(ref_x - rx[0])
        else:
            s_ref = float(sc[-1] + (ref_x - rx[-1]))
        pos.append(sc - s_ref)
    for j in range(len(rings) - 1):
        (y0, x0, z0), (y1, x1, z1) = rings[j], rings[j + 1]
        mat = mat_of(j, y0, y1)
        if mat is None:
            continue
        v0, v1 = BANDS[j % 3]
        for k in range(m):
            length = float(np.hypot(x0[k + 1] - x0[k], z0[k + 1] - z0[k]))
            xa = (x0[k] + x1[k]) / 2
            xb = (x0[k + 1] + x1[k + 1]) / 2
            if (abs(x0[k + 1] - x0[k]) + abs(z0[k + 1] - z0[k]) < 1e-7 and
                    abs(x1[k + 1] - x1[k]) + abs(z1[k + 1] - z1[k]) < 1e-7):
                continue        # ((Hansa 0.3.29) nothing between them: a run cut at a grid x, its end's points all there)
            # (texels along the planks: the lower ring's ends, the upper's)
            ua0, ua1 = 16.0 * float(pos[j][k]), 16.0 * float(pos[j][k + 1])
            ub0, ub1 = 16.0 * float(pos[j + 1][k]), 16.0 * float(pos[j + 1][k + 1])
            for side in (1, -1):
                p00 = (x0[k], y0, side * z0[k])
                p10 = (x0[k + 1], y0, side * z0[k + 1])
                p11 = (x1[k + 1], y1, side * z1[k + 1])
                p01 = (x1[k], y1, side * z1[k])
                outward_ccw = side > 0
                if inward:
                    outward_ccw = not outward_ccw
                sg = 1.0 if outward_ccw else -1.0       # (the far side's mirrored, as it was)
                for s0, s1 in _cut_ranges(xa, xb, y0, y1, side, cuts, splits, inward):
                    # (cut into pieces each within one texture)
                    span = max(abs((ua1 - ua0) * (s1 - s0)), abs((ub1 - ub0) * (s1 - s0)))
                    shear = abs((ub0 + (ub1 - ub0) * s0) - (ua0 + (ua1 - ua0) * s0))
                    n_p = 1
                    if span + shear > 16.0 + 1e-6 and 16.0 - shear > 1.0:
                        n_p = int(np.ceil(span / (16.0 - shear) - 1e-9))
                    for i_p in range(n_p):
                        t0, t1 = s0 + (s1 - s0) * i_p / n_p, s0 + (s1 - s0) * (i_p + 1) / n_p
                        a0, a1 = _lerp(p00, p10, t0), _lerp(p00, p10, t1)
                        b0, b1 = _lerp(p01, p11, t0), _lerp(p01, p11, t1)
                        us = [sg * (ua0 + (ua1 - ua0) * t0), sg * (ua0 + (ua1 - ua0) * t1),
                              sg * (ub0 + (ub1 - ub0) * t1), sg * (ub0 + (ub1 - ub0) * t0)]      # a0 a1 b1 b0
                        lo_, hi_ = min(us), max(us)
                        if hi_ - lo_ > 16.0:
                            # (sheared past a texture: squeezed into one, about its middle)
                            mid_ = 0.5 * (lo_ + hi_)
                            us = [mid_ + (u_ - mid_) * 16.0 / (hi_ - lo_) for u_ in us]
                            lo_, hi_ = min(us), max(us)
                        room = max(0.0, 16.0 - (hi_ - lo_))
                        u_base = ((k * 37 + j * 11 + i_p * 5 + seed) % 8) * room / 7 - lo_
                        uv_c = {"a0": (us[0] + u_base, v1), "a1": (us[1] + u_base, v1), "b1": (us[2] + u_base, v0),
                                "b0": (us[3] + u_base, v0)}
                        if outward_ccw:
                            pts, uvs = [a0, a1, b1, b0], [uv_c["a0"], uv_c["a1"], uv_c["b1"], uv_c["b0"]]
                        else:
                            pts, uvs = [a1, a0, b0, b1], [uv_c["a1"], uv_c["a0"], uv_c["b0"], uv_c["b1"]]
                        d.add_quad(group, mat, pts, smooth=smooth, row=j, hull="in" if inward else "out", uvs=uvs)
                        if square is not None:
                            d.quads[-1]["xend"] = not square(j, k)
    if bottom:
        y0, x0, z0 = rings[0]
        mat = mat_of(-1, y0, y0)
        for k in range(m):
            pts = [(x0[k + 1], y0, z0[k + 1]), (x0[k], y0, z0[k]), (x0[k], y0, -z0[k]), (x0[k + 1], y0, -z0[k + 1])]
            if inward:
                pts = pts[::-1]
            d.add_quad(group, mat, pts, (0, 0, 16, 5.5), hull="in" if inward else "out")
            if square is not None:
                d.quads[-1]["floor"] = True          # (the flat floor across her: fixed, the hold's floor over it)
    return rings


def transom(d, levels, x_of, z_of, mat, group="exterior", inward=False, cuts=(), smooth=None):
    """
    The flat stern: horizontal planks across the ship between the given heights, at x_of(y), z from -z_of(y) to
    +z_of(y). Faces aft (inward=True: forward, the inside). cuts: stern windows {z0, z1, y0, y1}.
    """
    for j in range(len(levels) - 1):
        y0, y1 = levels[j], levels[j + 1]
        xa, xb = x_of(y0), x_of(y1)
        za, zb = z_of(y0), z_of(y1)
        if za < 0.02 and zb < 0.02:
            continue
        v0, v1 = BANDS[j % 3]
        # split across the ship at window sides and every ~0.9 block (so the plank texture isn't stretched)
        zmax = max(za, zb)
        edges = set(np.linspace(-zmax, zmax, max(1, int(np.ceil(2 * zmax / 0.9))) + 1))
        wins = [c for c in cuts if y0 >= c["y0"] - 1e-6 and y1 <= c["y1"] + 1e-6]
        for c in wins:
            edges.update((c["z0"], c["z1"]))
        edges = sorted(e for e in edges if -zmax - 1e-9 <= e <= zmax + 1e-9)
        for e0, e1 in zip(edges[:-1], edges[1:]):
            zm = (e0 + e1) / 2
            if any(c["z0"] - 1e-6 <= zm <= c["z1"] + 1e-6 for c in wins):
                continue
            a0, a1 = float(np.clip(e0, -za, za)), float(np.clip(e1, -za, za))
            b0, b1 = float(np.clip(e0, -zb, zb)), float(np.clip(e1, -zb, zb))
            if a1 - a0 < 1e-4 and b1 - b0 < 1e-4:
                continue
            p00 = (xa, y0, a0)
            p10 = (xa, y0, a1)
            p11 = (xb, y1, b1)
            p01 = (xb, y1, b0)
            w = e1 - e0
            uv = (0, v0, min(16, w * 16), v1)
            # seen from aft (looking forward), +z is to the right: counter-clockwise from outside is p00, p10, p11, p01
            # (it was the other way round until 0.3.7: the transom's normals pointed into her and the inside's aft, so
            # the game lit the inside of the stern as if it faced away: dark, like tarred wood)
            pts = [p10, p00, p01, p11] if inward else [p00, p10, p11, p01]
            d.add_quad(group, mat, pts, uv, smooth=smooth, row=j, hull="in" if inward else "out")
            d.quads[-1]["transom"] = True


def raised_strip(d, half_width, y0, y1, x_range, m, mat, out=0.08, inner=0.0, group="exterior", sheer=None, cuts=()):
    """A wale or rail cap: the band between y0 and y1 pushed out from the hull by `out`, with top and bottom faces
    back to the hull. inner > 0 extends the top face that far inboard (a rail cap covering the bulwark top).
    sheer(x) -> extra height (the wales sweep up toward the ends). cuts: x ranges {x0, x1, side} left open.
    Returns its top's edges (starboard) at its start and end, for a cap carried on from it (sweep_cap)."""
    def strip_ring(y):
        xs, zs = outline(half_width, y, *x_range(y))
        rx, rz, _ = resample(xs, zs, m)
        if sheer is not None:
            dy = sheer(rx)
            # sample the hull at the swept height so the strip stays on it
            rz = half_width(rx, y + dy)
        else:
            dy = np.zeros_like(rx)
        tx, tz = np.gradient(rx), np.gradient(rz)
        ln = np.hypot(tx, tz) + 1e-9
        nx, nz = -tz / ln, tx / ln          # outward in the horizontal plane (starboard side)
        # where port and starboard meet (the bow's tip) each side's strip stops at the centreline: mitred, not crossed
        return (rx, rz, rx + nx * out, np.maximum(rz + nz * out, 0.0), rx - nx * inner, np.maximum(rz - nz * inner, 0.0),
                y + dy)
    x0, z0, ox0, oz0, ix0, iz0, ya = strip_ring(y0)
    x1, z1, ox1, oz1, ix1, iz1, yb = strip_ring(y1)
    rings = (x0, z0, ox0, oz0, ix0, iz0, ya, x1, z1, ox1, oz1, ix1, iz1, yb)
    lift = 0.02 if inner > 0 else 0

    def spans(k, s):
        """The parts of segment k (fractions along it) left on side s: a cut marked "exact" (a gunport's notch) is cut at
        its own x; the others (the entry ports, closed by their gates' own cap) take whole segments by their middle."""
        xa, xb = x0[k], x0[k + 1]
        keep = [(0.0, 1.0)]
        at_x = {}                     # (Hansa 0.3.10) a fraction where an exact cut ends it -> the cut's x
        for c in cuts:
            if c.get("side", 0) not in (0, s):
                continue
            if not c.get("exact") or abs(xb - xa) < 1e-9:
                if c["x0"] - 1e-6 <= (xa + xb) / 2 <= c["x1"] + 1e-6:
                    return []
                continue
            lo, hi = sorted(((c["x0"] - xa) / (xb - xa), (c["x1"] - xa) / (xb - xa)))
            at_x[lo], at_x[hi] = (c["x0"], c["x1"]) if xb > xa else (c["x1"], c["x0"])
            left = []
            for a, b in keep:
                if hi <= a or lo >= b:
                    left.append((a, b))
                    continue
                if lo > a:
                    left.append((a, lo))
                if hi < b:
                    left.append((hi, b))
            keep = left
        return [(a, b, at_x.get(a), at_x.get(b)) for a, b in keep if b - a > 1e-6]

    XS = (0, 2, 4, 7, 9, 11)          # the rings' x entries (the hull's line, outside and inside, at y0 and y1)
    for k in range(m):
        for s in (1, -1):
            for ta_, tb_, xca, xcb in spans(k, s):
                # this stretch of the segment: the rings' points at its ends
                A = [r[k] + (r[k + 1] - r[k]) * ta_ for r in rings]
                Bv = [r[k] + (r[k + 1] - r[k]) * tb_ for r in rings]
                # (Hansa 0.3.10, clean surfaces: an exact cut's end square across at its x, outside and inside too:
                # the offset rings' points at the hull line's fraction were up to 0.03 past it, over the gate's cap)
                for P_, xc_ in ((A, xca), (Bv, xcb)):
                    if xc_ is not None:
                        for i_ in XS:
                            P_[i_] = xc_
                (xa0, za0, oxa0, oza0, ixa0, iza0, yaa, xa1, za1, oxa1, oza1, ixa1, iza1, yba) = A
                (xb0, zb0, oxb0, ozb0, ixb0, izb0, yab, xb1, zb1, oxb1, ozb1, ixb1, izb1, ybb) = Bv
                length = float(np.hypot(oxb0 - oxa0, ozb0 - oza0))
                uv = (0, 5.5, min(16, length * 16), 11)
                ta, tb = yba + lift, ybb + lift
                faces = [
                    [(oxa0, yaa, s * oza0), (oxb0, yab, s * ozb0), (oxb1, tb, s * ozb1), (oxa1, ta, s * oza1)],
                    [(oxa1, ta, s * oza1), (oxb1, tb, s * ozb1), (ixb1, tb, s * izb1), (ixa1, ta, s * iza1)],
                    [(xa0, yaa, s * za0), (xb0, yab, s * zb0), (oxb0, yab, s * ozb0), (oxa0, yaa, s * oza0)],
                ]
                if inner > 0:  # inboard face of the cap, down over the top of the inner planking
                    faces.append([(ixb0, ybb - 0.14, s * izb0), (ixa0, yba - 0.14, s * iza0), (ixa1, ta, s * iza1), (ixb1, tb, s * izb1)])
                for pts in faces:
                    if s < 0:
                        pts = [pts[1], pts[0], pts[3], pts[2]]
                    # (on the planking: a hole through the hull goes through a wale or a rail cap over it too)
                    d.add_quad(group, mat, pts, uv, row=1, hull="out")
                    d.quads[-1]["raised"] = True

    # an exact cut's ends closed (its "wall": the bulwark's thickness under the cap, whose own reveal lines the cut)
    for c in cuts:
        if not c.get("exact") or not c.get("wall"):
            continue
        for xc, face in ((c["x0"], 1.0), (c["x1"], -1.0)):
            k = next((i for i in range(m) if min(x0[i], x0[i + 1]) - 1e-9 <= xc <= max(x0[i], x0[i + 1]) + 1e-9), None)
            if k is None:
                continue
            t = (xc - x0[k]) / (x0[k + 1] - x0[k]) if abs(x0[k + 1] - x0[k]) > 1e-9 else 0.0
            (xh0, zh0, xo0, zo0, xi0, zi0, yy0, xh1, zh1, xo1, zo1, xi1, zi1, yy1) = [r[k] + (r[k + 1] - r[k]) * t for r in rings]
            xh0 = xo0 = xi0 = xh1 = xo1 = xi1 = xc          # (0.3.10: square across at the cut)
            top, ceil = yy1 + lift, zh1 - c["wall"]
            for s in (1, -1):
                if c.get("side", 0) not in (0, s):
                    continue
                quads = [[(xh0, yy0, zh0), (xo0, yy0, zo0), (xo1, top, zo1), (xh1, top, zh1)],       # outside the planking
                         [(xh1, yy1, zh1), (xh1, top, zh1), (xh1, top, ceil), (xh1, yy1, ceil)]]       # over the bulwark's top
                if inner > 0:
                    quads.append([(xi0, yy1 - 0.14, zi0), (xi1, top, zi1), (xh1, top, ceil), (xh1, yy1 - 0.14, ceil)])   # inside
                for q in quads:
                    pts = [np.array([p[0], p[1], s * p[2]]) for p in q]
                    n = np.cross(pts[1] - pts[0], pts[3] - pts[0]) + np.cross(pts[3] - pts[2], pts[1] - pts[2])
                    if n[0] * face < 0:
                        pts = [pts[1], pts[0], pts[3], pts[2]]
                    d.add_quad(group, mat, [tuple(p) for p in pts], (0, 5.5, 4, 11), row=1, hull="out")
                    d.quads[-1]["raised"] = True

    # its top's ends (starboard: the outboard and inboard edge, the hull's line), for a cap carried on from it
    def edge(k):
        # (and its outboard face's foot and its inboard face's foot there: its whole section, for a cap mitred onto it)
        return {"out": (float(ox1[k]), float(yb[k] + lift), float(oz1[k])), "in": (float(ix1[k]), float(yb[k] + lift), float(iz1[k])),
                "out_lo": (float(ox0[k]), float(ya[k]), float(oz0[k])),
                "in_lo": (float(ix0[k]), float(yb[k] - 0.14), float(iz0[k]))}
    return {"start": edge(0), "end": edge(-1)}


def flat_between(d, y, x0, x1, inner_hw, outer_hw, mat, group="exterior", facing=-1, n=40):
    """A horizontal surface at height y between two starboard outlines (mirrored to port), e.g. the underside of the
    forecastle where it overhangs the bow. facing -1 = seen from below."""
    xs = np.linspace(x0, x1, n + 1)
    zi, zo = inner_hw(xs), outer_hw(xs)
    for k in range(n):
        if zo[k] - zi[k] < 1e-3 and zo[k + 1] - zi[k + 1] < 1e-3:
            continue
        for s in (1, -1):
            pts = [(xs[k], y, s * zi[k]), (xs[k + 1], y, s * zi[k + 1]), (xs[k + 1], y, s * zo[k + 1]), (xs[k], y, s * zo[k])]
            # counter-clockwise seen from below (facing -1) on the starboard side
            n_up = np.cross(np.subtract(pts[1], pts[0]), np.subtract(pts[3], pts[0]))[1]
            if (n_up > 0) == (facing < 0):
                pts = [pts[1], pts[0], pts[3], pts[2]]
            length = float(xs[k + 1] - xs[k])
            d.add_quad(group, mat, pts, (0, BANDS[k % 3][0], min(16, length * 16), BANDS[k % 3][1]), row=k)


def _clip_side(poly, f, keep_below):
    """The part of a convex polygon [(x, y)...] where f(x, y) <= 0 (keep_below) or >= 0."""
    out = []
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        fa, fb = f(*a), f(*b)
        ia = fa <= 1e-9 if keep_below else fa >= -1e-9
        ib = fb <= 1e-9 if keep_below else fb >= -1e-9
        if ia:
            out.append(a)
        if ia != ib:
            t = fa / (fa - fb)
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    # (no corner twice)
    return [p for k, p in enumerate(out) if abs(p[0] - out[k - 1][0]) + abs(p[1] - out[k - 1][1]) > 1e-9]


def sweep_patch(d, half_width, levels, x0, x1, top, mat_of, group="exterior", offset=0.0, n=28, dense_at=None,
                bands_below=()):
    """
    Planking under a curved top edge: where the sheer sweeps up from the waist to a castle (the castle break). Columns
    from x0 to x1 (bunched toward dense_at, where the curve is steepest), each strake between levels[j] and levels[j+1]
    cut off at top(x). The strakes use the same bands and rows as skin() with the same levels, so the planks run on
    round the break. offset > 0: the inside planking, facing in. bands_below: (0.3.9) depths under top(x) its pieces are
    cut at as well (each column's straight line between its ends), so a band following the sweep (the frieze under the
    rail cap, paint_zones) is made of whole pieces.
    """
    t = np.linspace(0, 1, n + 1)
    if dense_at is not None:
        # bunch the columns toward one end: t^2 from that end
        t = t ** 2 if dense_at == x0 else 1 - (1 - t) ** 2
    xs = x0 + (x1 - x0) * t
    inward = offset > 0
    for k in range(n):
        xa, xb = float(xs[k]), float(xs[k + 1])
        ta, tb = float(top(xa)), float(top(xb))
        for j in range(len(levels) - 1):
            y0, y1 = levels[j], levels[j + 1]
            if y0 >= max(ta, tb) - 1e-6:
                break
            ya1, yb1 = max(y0, min(y1, ta)), max(y0, min(y1, tb))
            if ya1 - y0 < 1e-4 and yb1 - y0 < 1e-4:
                continue
            mat = mat_of(j, y0, y1)
            v0, v1 = BANDS[j % 3]
            # the top of a cut strake shows the part of the plank that's left
            fa, fb = (ya1 - y0) / (y1 - y0), (yb1 - y0) / (y1 - y0)
            if bands_below:
                # (0.3.9) cut along each band's lines: pieces, each corner on the hull, its uv by its place in the plank
                poly = [(xa, y0), (xb, y0), (xb, yb1), (xa, ya1)]
                pieces = [poly]
                for o in sorted(bands_below):
                    def f(x, y, o=o):
                        return y - (ta + (tb - ta) * (x - xa) / (xb - xa) - o)
                    nxt = []
                    for pc in pieces:
                        for keep in (False, True):
                            q = _clip_side(pc, f, keep)
                            if len(q) >= 3:
                                nxt.append(q)
                    pieces = nxt
                lu = min(16.0, abs(xb - xa) * 16)
                u0 = ((k * 5 + j * 3) % 7) * max(0.0, 16 - lu) / 6
                for side in (1, -1):
                    outward_ccw = (side > 0) != inward
                    if xb < xa:
                        outward_ccw = not outward_ccw
                    for pc in pieces:
                        q = pc if outward_ccw else pc[::-1]
                        pts = [(x, y, side * max(float(half_width(np.array(x), np.array(y))) - offset, 0.02)) for x, y in q]
                        uvs = [(u0 + lu * ((x - xa) if outward_ccw else (xb - x)) / (xb - xa),
                                v1 - (v1 - v0) * (y - y0) / (y1 - y0)) for x, y in q]
                        i = 1
                        while i + 1 < len(q):
                            idx = [0, i, i + 1, min(i + 2, len(q) - 1)]
                            d.add_quad(group, mat, [pts[m_] for m_ in idx], uvs=[uvs[m_] for m_ in idx], row=j,
                                       hull="in" if inward else "out")
                            i += 2
                continue
            for side in (1, -1):
                def P(x, y):
                    z = float(half_width(np.array(x), np.array(y))) - offset
                    return (x, y, side * max(z, 0.02))
                p00, p10, p11, p01 = P(xa, y0), P(xb, y0), P(xb, yb1), P(xa, ya1)
                outward_ccw = (side > 0) != inward
                if xb < xa:
                    outward_ccw = not outward_ccw
                pts = [p00, p10, p11, p01] if outward_ccw else [p10, p00, p01, p11]
                lu = min(16.0, abs(xb - xa) * 16)
                u0 = ((k * 5 + j * 3) % 7) * max(0.0, 16 - lu) / 6
                vt = (v1 - (v1 - v0) * max(fa, fb), v1)
                d.add_quad(group, mat, pts, (u0, vt[0], u0 + lu, vt[1]), row=j, hull="in" if inward else "out")


def sweep_cap(d, half_width, x0, x1, top, mat, out=0.07, inner=0.38, depth=0.2, group="exterior", n=28, dense_at=None,
              start=None, end=None, in_depth=None):
    """
    The rail cap along a sweeping top edge: its top face, and its outboard and inboard faces hanging down `depth`
    (in_depth: the inboard one, if other). start, end: the edges of the rail caps it carries on from at x0 and x1
    (raised_strip's), so they meet exactly; then its edges are offset square to the hull's line at the bulwark's top,
    as theirs are (otherwise straight out and in, from the hull's line a little under its top).
    """
    in_depth = depth if in_depth is None else in_depth
    t = np.linspace(0, 1, n + 1)
    if dense_at is not None:
        t = t ** 2 if dense_at == x0 else 1 - (1 - t) ** 2
    xs = x0 + (x1 - x0) * t
    exact = start is not None or end is not None

    def edges(k):
        """Column k's top height, and its outboard and inboard edges (starboard x, z)."""
        x = float(xs[k])
        y = float(top(x)) + 0.02
        for at, e in ((0, start), (n, end)):
            if k == at and e is not None:
                return e["out"][1], (e["out"][0], e["out"][2]), (e["in"][0], e["in"][2])
        if not exact:
            z = float(half_width(np.array(x), np.array(y - 0.1)))
            return y, (x, z + out), (x, z - inner)
        h = 0.01
        z = float(half_width(np.array(x), np.array(y - 0.02)))
        dz = (float(half_width(np.array(x + h), np.array(y - 0.02))) - float(half_width(np.array(x - h), np.array(y - 0.02)))) / (2 * h)
        nx, nz = -dz / np.hypot(dz, 1.0), 1.0 / np.hypot(dz, 1.0)
        return y, (x + nx * out, z + nz * out), (x - nx * inner, max(0.0, z - nz * inner))

    cols = [edges(k) for k in range(n + 1)]
    for k in range(n):
        (ya, (oxa, oza), (ixa, iza)), (yb, (oxb, ozb), (ixb, izb)) = cols[k], cols[k + 1]
        xa, xb = float(xs[k]), float(xs[k + 1])
        length = float(np.hypot(xb - xa, yb - ya))
        uv = (0, 5.5, min(16, length * 16), 11)
        for s in (1, -1):
            oa, ob, ia, ib = s * oza, s * ozb, s * iza, s * izb
            faces = [
                ([(oxa, ya, oa), (oxb, yb, ob), (ixb, yb, ib), (ixa, ya, ia)], (0, 1.0, 0)),                    # top
                ([(oxa, ya - depth, oa), (oxb, yb - depth, ob), (oxb, yb, ob), (oxa, ya, oa)], (0, 0, s)),       # outboard
                ([(ixb, yb - in_depth, ib), (ixa, ya - in_depth, ia), (ixa, ya, ia), (ixb, yb, ib)], (0, 0, -s)),  # inboard
            ]
            for pts, want in faces:
                nrm = np.cross(np.subtract(pts[1], pts[0]), np.subtract(pts[3], pts[0]))
                if np.dot(nrm, want) < 0:
                    pts = [pts[1], pts[0], pts[3], pts[2]]
                d.add_quad(group, mat, pts, uv, row=1)


def reveal(d, a, b, c, e, mat, group="auto"):
    """One quad a-b-c-e (already counter-clockwise from the side it faces): the inside face of an opening."""
    length = float(np.linalg.norm(np.subtract(b, a)))
    height = float(np.linalg.norm(np.subtract(e, a)))
    d.add_quad(group, mat, [a, b, c, e], (0, 0, min(16, length * 16), min(16, height * 16)))
