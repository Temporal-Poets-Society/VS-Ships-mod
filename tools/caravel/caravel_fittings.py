"""The caravel's fittings as the carrack's (Magelan's) are drawn, fitted to her (22 October 2026, the user's chalk notes
26, 27, 29, 31, 33: "capstan detail is far from Magelan", "pump - Magelan had good model to use", "the wheel model is
crap. Why did not reuse Magelan", "lantern attachment to mast missing ... check magelan", the tiller's ropes with
blocks to the wheel). Each function is the carrack generator's (tools/shipgen/carrack.py: helm, capstan_and_cable /
capstan_cable_grid, anchor_gear_grid, pump, the mast hooks and the stern lantern's crane in fixtures) with her positions
passed in instead of the carrack's constants. Ship-local blocks, x fore, y up, z to starboard; d is a shipgen Design.
They return the fixtures and moving parts they make; caravel_layout.py places them.
"""
import math

import numpy as np

from carrack import rope_path, ring_points, disk, annulus, block_at, helix       # noqa: F401 (the carrack's helpers)


def iron_ring(d, c, radius=0.085, rod=0.018, group="exterior", normal=(0, 0, 1)):
    """A closed forged ring: a torus with shared edges, without overlapping cylinder caps at its joints."""
    c, n = np.asarray(c, float), np.asarray(normal, float)
    n /= np.linalg.norm(n)
    a = np.cross(n, (0, 1, 0) if abs(n[1]) < 0.9 else (1, 0, 0))
    a /= np.linalg.norm(a)
    b = np.cross(n, a)
    def p(i, j):
        t, u = 2 * math.pi * i / 20, 2 * math.pi * j / 8
        r = math.cos(t) * a + math.sin(t) * b
        return c + (radius + rod * math.cos(u)) * r + rod * math.sin(u) * n
    for i in range(20):
        for j in range(8):
            d.add_quad(group, "iron", [p(i, j), p(i + 1, j), p(i + 1, j + 1), p(i, j + 1)],
                       uvs=[(ii / 20 * 2 * math.pi * radius * 16, jj / 8 * 2 * math.pi * rod * 16)
                            for ii, jj in ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))])


def chain_channel(d, xs, top, side, hull_at):
    """A channel outside the hull, with bolted knees, chainplates, paired deadeyes and reeved lanyards.
    Returns each shroud's lower end, on the upper deadeye's strop. All dimensions are physical metres."""
    x0, x1 = min(xs) - 0.28, max(xs) + 0.28
    outer = max(hull_at(x, top) for x in np.linspace(x0, x1, 16)) + 0.6
    inner = min(hull_at(x, top) for x in np.linspace(x0, x1, 16)) - 0.06
    d.add_box_mesh("exterior", "oak", ((x0 + x1) / 2, top - 0.06, side * (inner + outer) / 2),
                   (x1 - x0, 0.12, outer - inner))
    ends = []
    for x in xs:
        z = side * (outer - 0.1)
        lower, upper = np.array([x, top + 0.16, z]), np.array([x, top + 0.68, z])
        # Straps go below the channel to bolts in the hull; the knee bears on the planking further down.
        foot = np.array([x, top - 0.85, side * (hull_at(x, top - 0.85) + 0.01)])
        d.add_beam_mesh("exterior", "iron", foot, lower, 0.025, 0.06, up=(1, 0, 0))
        d.add_beam_mesh("exterior", "timber", foot + (0, 0.12, 0), (x, top - 0.13, z), 0.10, 0.12, up=(1, 0, 0))
        for y in (foot[1], foot[1] + 0.17):
            zz = side * hull_at(x, y)
            d.add_cylinder("exterior", "iron", (x, y, zz - side * 0.03), (x, y, zz + side * 0.04), 0.035, 0.035, sides=8)
        for c in (lower, upper):
            d.add_cylinder("exterior", "timber", c - (0, 0, 0.055), c + (0, 0, 0.055), 0.135, 0.135, sides=16)
            iron_ring(d, c, 0.143, 0.012)
            for dx, dy in ((-0.055, 0.025), (0.055, 0.025), (0, -0.065)):
                for s in (-1, 1):
                    disk(d, "exterior", "black", c + (dx, dy, s * 0.058), (0, 0, s), 0.021, 8)
        for dx in (-0.055, 0.0, 0.055):
            rope_path(d, [lower + (dx, 0.04, -0.07), upper + (dx, -0.04, -0.07),
                          upper + (dx, -0.04, 0.07), lower + (dx, 0.04, 0.07)], r=0.012)
        ends.append(upper + (0, 0.155, 0))
    return ends


def _r(p):
    return [round(float(v), 4) for v in p]


# ---------------------------------------------------------------------------------------------- the wheel (helm)
def wheel(d, wheel_x, deck_y, ropes_to=None):
    """carrack.helm: posts forward of the wheel tapering up to the axle, the drum between them the wheel ropes wind on,
    the wheel (a rim of prisms, eight spokes with turned handles and bronze ferrules, the hub with bronze bosses). The
    wheel ropes, when ropes_to gives their run: off the drum, down through the deck, out under the beams to blocks at
    her sides and aft to the tiller's blocks (ropes_to(side) -> the points after the deck's hole). Returns the moving
    part, the helm fixture, and the drum's x."""
    wc = np.array([wheel_x, deck_y + 0.85, 0.0])
    xd = wheel_x + 0.43
    for dx in (0.24, 0.62):
        xp = wc[0] + dx
        pb, pt = deck_y, wc[1] + 0.12
        quad = [(-0.22, pb), (0.22, pb), (0.09, pt), (-0.09, pt)]
        d.add_prism("exterior", "timber", [(xp + 0.05, y, z) for z, y in quad], [(xp - 0.05, y, z) for z, y in quad])
    d.add_box_mesh("exterior", "timber", (xd, deck_y + 0.05, 0), (0.62, 0.1, 0.5))
    d.add_cylinder("exterior", "iron", wc - np.array([0.1, 0, 0]), wc + np.array([0.72, 0, 0]), 0.045, 0.045, sides=8,
                   smooth=False)
    d.add_cylinder("exterior", "timber", (xd - 0.13, wc[1], 0), (xd + 0.13, wc[1], 0), 0.13, 0.13, sides=10, smooth=False)
    R_RIM, R_HANDLE = 0.36, 0.56
    n_rim = 16
    for k in range(n_rim):
        a0_, a1_ = 2 * math.pi * k / n_rim, 2 * math.pi * (k + 1) / n_rim

        def rp(a_, r_, x_):
            return wc + np.array([x_, r_ * math.cos(a_), r_ * math.sin(a_)])
        ring = [(a0_, R_RIM - 0.0325), (a1_, R_RIM - 0.0325), (a1_, R_RIM + 0.0325), (a0_, R_RIM + 0.0325)]
        d.add_prism("part:wheel", "timber", [rp(a_, r_, 0.0375) for a_, r_ in ring], [rp(a_, r_, -0.0375) for a_, r_ in ring])
    for k in range(8):
        a = 2 * math.pi * k / 8
        rad = np.array([0, math.cos(a), math.sin(a)])
        tang = np.array([0, -math.sin(a), math.cos(a)])
        d.add_box_mesh("part:wheel", "timber", wc + rad * (R_RIM / 2 + 0.03), (0.05, 0.045, R_RIM - 0.06),
                       np.column_stack([[1.0, 0, 0], tang, rad]))
        d.add_cylinder("part:wheel", "timber", wc + rad * (R_RIM + 0.02), wc + rad * R_HANDLE, 0.025, 0.035, sides=6)
        d.add_cylinder("part:wheel", "timber", wc + rad * (R_HANDLE - 0.02), wc + rad * (R_HANDLE + 0.04), 0.04, 0.025, sides=6)
        d.add_cylinder("part:wheel", "bronze", wc + rad * (R_RIM + 0.025), wc + rad * (R_RIM + 0.075), 0.037, 0.037,
                       sides=8, smooth=False)
    d.add_cylinder("part:wheel", "timber", wc - np.array([0.08, 0, 0]), wc + np.array([0.08, 0, 0]), 0.09, 0.09,
                   sides=10, smooth=False)
    for sgn in (1, -1):
        a_ = wc + np.array([sgn * 0.08, 0, 0])
        d.add_cylinder("part:wheel", "bronze", a_, a_ + np.array([sgn * 0.022, 0, 0]), 0.078, 0.07, sides=12, smooth=False)
        d.add_cylinder("part:wheel", "bronze", a_ + np.array([sgn * 0.022, 0, 0]), a_ + np.array([sgn * 0.04, 0, 0]),
                       0.05, 0.035, sides=12)
    # the rope's turns on the drum
    # (apart by more than the rope's thickness: touching turns flickered where their faces met)
    for k in range(4):
        x = xd - 0.0825 + 0.055 * k
        rope_path(d, ring_points((x, wc[1], 0.0), 0.15, "x", 12), r=0.024)
    part = {"code": "wheel", "axisPoint": _r(wc), "axisDir": [1.0, 0.0, 0.0], "source": "rudder", "angle": 240.0}
    helm = {"type": "helm", "code": "helm", "pos": _r((wheel_x - 0.75, deck_y, 0.0)), "yaw": 0.0,
            "sel": _r((wheel_x - 0.2, deck_y + 0.25, -0.62, wheel_x + 0.75, deck_y + 1.5, 0.62))}
    return part, helm, xd, wc


def wheel_ropes(d, xd, wc, deck_y, deck_t, beam_y, side_z, tiller_x, tiller_tip):
    """The wheel ropes as the carrack's: off the drum down through the deck (an iron-lined hole), to a lead block under
    the deck, out under the beams to a block at each side, aft along it to a block abreast the tiller's end, and from
    there to the tiller's end (a moving part following the tiller). side_z(x) -> how far out the side's block hangs.
    Returns the two moving parts."""
    parts = []
    for s, side in ((1, "starboard"), (-1, "port")):
        drum = np.array([xd, wc[1], s * 0.13])
        hole_q = np.array([xd, deck_y, s * 0.28])
        lead = np.array([xd, deck_y - deck_t - 0.18, s * 0.28])
        fwd_side = np.array([xd, beam_y, s * side_z(xd)])
        aft_side = np.array([tiller_x, beam_y, s * side_z(tiller_x)])
        rope_path(d, [drum, hole_q], r=0.03)
        rope_path(d, [hole_q, lead, fwd_side, aft_side], r=0.03, group="auto")
        for p in (lead, fwd_side, aft_side):
            block_at(d, "auto", p + np.array([0, 0.1, 0]))
        disk(d, "auto", "black", hole_q + np.array([0, 0.006, 0]), (0, 1, 0), 0.07, 8)
        d.add_cylinder("auto", "iron", hole_q - np.array([0, 0.01, 0]), hole_q + np.array([0, 0.02, 0]), 0.1, 0.1, sides=8,
                       smooth=False, caps=False)
        d.add_rope(aft_side, tiller_tip, r=0.03, group=f"part:tillerrope-{side}")
        parts.append({"code": f"tillerrope-{side}", "kind": "follow", "follow": "tiller", "anchor": _r(aft_side),
                      "tip": _r(tiller_tip), "interior": True})
    return parts


# ---------------------------------------------------------------------------------------------- the capstan and cable
def capstan(d, cx, cz, deck_y, hawse_in, radius=0.48, bars=0.95):
    """carrack.capstan_and_cable (GRID) and capstan_cable_grid: its base, the barrel with eight whelps, the drumhead, a
    pawl ring on the deck with two pawls, two bars shipped; the cable from the hawse in along the deck onto the barrel's
    outboard side, two and a half turns round it, off and down into a coil of its slack on the deck. Returns the
    fixture."""
    cap = np.array([cx, deck_y, cz])
    up = np.array([0.0, 1.0, 0.0])
    k_ = radius / 0.52
    d.add_cylinder("exterior", "timber", cap, cap + up * 0.12, min(0.58, radius), min(0.58, radius), sides=12, smooth=False)
    d.add_cylinder("exterior", "timber", cap + up * 0.12, cap + up * 0.85, 0.42 * k_, 0.37 * k_, sides=12, smooth=False)
    d.add_cylinder("exterior", "oak", cap + up * 0.85, cap + up * 1.0, radius, radius, sides=12, smooth=False)
    for k in range(8):
        a = 2 * math.pi * k / 8
        rad = np.array([math.cos(a), 0, math.sin(a)])
        tang = np.array([-math.sin(a), 0, math.cos(a)])
        d.add_box_mesh("exterior", "timber", cap + up * 0.48 + rad * 0.42 * k_, (0.09, 0.66, 0.07), np.column_stack([rad, up, tang]))
    pr = 0.6
    d.add_cylinder("exterior", "iron", cap - up * 0.01, cap + up * 0.03, pr, pr, sides=16, smooth=False)
    for a in (0.3, math.pi + 0.3):
        rad = np.array([math.cos(a), 0, math.sin(a)])
        tang = np.array([-math.sin(a), 0, math.cos(a)])
        d.add_box_mesh("exterior", "iron", cap + up * 0.08 + rad * (pr - 0.12) + tang * 0.1, (0.07, 0.07, 0.3),
                       np.column_stack([rad, up, tang]))
    for i in range(2):
        a = i * math.pi / 2 + math.pi / 4
        p = bars * np.array([math.cos(a), 0, math.sin(a)])
        c = cap + up * 0.92
        d.add_beam_mesh("exterior", "timber", c - p, c + p, 0.08)
        for q in (c - p, c + p):
            d.add_box_mesh("exterior", "iron", q, (0.1, 0.1, 0.1))
    # the cable: (22 October 2026, the user: "check how rope should go in most direct way while being on floor") down
    # from the hawse's inside to the deck, straight along the deck to the barrel's foot, meeting it on a tangent, and
    # wound up it the way it arrives: two and a half turns, off the top on the far side and down into a coil of its slack
    r = 0.06
    w = 0.40 * k_
    y_bot, y_top = deck_y + r + 0.12, deck_y + 0.62
    hx, hz = float(hawse_in[0]) - cx, float(hawse_in[2]) - cz
    dist = math.hypot(hx, hz)
    a_h = math.atan2(hz, hx)
    t_ang = a_h + math.acos(min(1.0, w / dist))
    tp = np.array([cx + w * math.cos(t_ang), y_bot, cz + w * math.sin(t_ang)])
    hin = np.asarray(hawse_in, float)
    run = math.hypot(tp[0] - hin[0], tp[2] - hin[2])
    lay = hin + (np.array([tp[0], hin[1], tp[2]]) - hin) * (0.35 / max(0.35, run))
    lay[1] = deck_y + r
    arrive = np.array([tp[0] - hin[0], tp[2] - hin[2]])
    spin = 1.0 if (-math.sin(t_ang)) * arrive[0] + math.cos(t_ang) * arrive[1] > 0 else -1.0
    rope_path(d, [hin, lay, np.array([tp[0], deck_y + r, tp[2]]), tp], r=r)
    turns, n = 2.5, 50
    coil = [np.array([cx + w * math.cos(t_ang + spin * 2 * math.pi * turns * k / n), y_bot + (y_top - y_bot) * k / n,
                      cz + w * math.sin(t_ang + spin * 2 * math.pi * turns * k / n)]) for k in range(n + 1)]
    rope_path(d, coil, r=r)
    off = coil[-1]
    a_off = t_ang + spin * 2 * math.pi * turns
    lead_dir = np.array([-math.sin(a_off), 0.0, math.cos(a_off)]) * spin
    fk = off + lead_dir * 0.75
    rope_path(d, [off, off + lead_dir * 0.35 + np.array([0, deck_y + 0.12 - off[1], 0]),
                  np.array([fk[0], deck_y + r, fk[2]]) - lead_dir * 0.2], r=r)
    for layer in range(2):
        for k, rad in enumerate((0.31, 0.19)):              # (two turns, a rope's width apart: they touched)
            rope_path(d, ring_points((fk[0], deck_y + r + 0.12 * layer, fk[2]), rad, "y", 14, a0=0.3 * k), r=r)
    return {"type": "anchor", "code": "capstan", "pos": _r((cx, deck_y, cz)), "yaw": 0.0,
            "sel": _r((cx - 0.6, deck_y, cz - 0.6, cx + 0.6, deck_y + 1.1, cz + 0.6))}


def hawse(d, h_out, h_in, n_out):
    """A hawse hole through the bow: an iron-lined ring and its dark mouth outside and in."""
    for c_, n_ in ((h_out, n_out), (h_in, -np.asarray(n_out))):
        c_, n_ = np.asarray(c_, float), np.asarray(n_, float)
        disk(d, "exterior", "black", c_ + n_ * 0.004, n_, 0.09, 10)
        annulus(d, "exterior", "iron", c_ + n_ * 0.008, n_, 0.09, 0.14, 10)


# ---------------------------------------------------------------------------------------------- the anchor
def cathead_and_anchor(d, ci, co, knee_from, side, hawse_out, shank=1.9, knee_to=None):
    """anchor_gear_grid's cathead (a timber from inboard on the rail out over the side, its end a block with a sheave
    pin, a knee under it from the side) and the working anchor catted under it: its ring on a short strop, the shank
    down to the crown, the stock across near the ring along the side, two arms with their palms. The anchor is the
    moving part "anchor" (the mod lets it go 8.5 down); its cable from the hawse to its ring follows it."""
    d.add_beam_mesh("exterior", "timber", ci, co, 0.24, 0.26)
    dirv = (co - ci) / np.linalg.norm(co - ci)
    # (22 October 2026, the user: "place it outside hull and attach from side to the rib") its heel against the side,
    # bolted through the planking into the rib: two bolts' heads on each face and an iron strap round it by the side
    for dx in (-0.13, 0.13):
        for dy in (-0.06, 0.06):
            q = ci + dirv * 0.15 + np.array([dx, dy, 0])
            d.add_cylinder("exterior", "iron", q, q + np.array([0.02 if dx > 0 else -0.02, 0, 0]), 0.025, 0.025, sides=6)
    d.add_box_mesh("exterior", "iron", ci + dirv * 0.32, (0.27, 0.29, 0.05), np.column_stack([[1.0, 0, 0], [0, 1.0, 0], dirv]))
    d.add_box_mesh("exterior", "timber", co + dirv * 0.1, (0.28, 0.3, 0.28))
    d.add_cylinder("exterior", "iron", co + np.array([0, -0.15, 0]), co + np.array([0, -0.28, 0]), 0.03, 0.03, sides=6)
    # its knee: from into the side up into the cathead's underside (each end buried a little in what it meets)
    d.add_beam_mesh("exterior", "timber", knee_from, knee_to if knee_to is not None else co + np.array([-0.12, -0.15, -side * 0.22]), 0.13)
    grp = "part:anchor"
    rc = co + np.array([0, -0.5, 0])
    rope_path(d, ring_points(rc, 0.17, "z", 14), r=0.03, group=grp, mat="iron")
    rope_path(d, [co + np.array([0, -0.28, 0]), rc + np.array([0, 0.16, 0])], r=0.028, group=grp)
    eye = rc - np.array([0, 0.17, 0])
    crown = eye - np.array([0, shank, 0])
    u = (eye - crown) / np.linalg.norm(eye - crown)
    side_dir = np.array([0, 0, float(side)])
    d.add_beam_mesh(grp, "iron", crown, eye, 0.09, 0.09)
    d.add_box_mesh(grp, "iron", crown, (0.12, 0.14, 0.12))
    sy = eye - u * 0.25
    stock_dir = np.cross(u, side_dir)
    stock_dir /= np.linalg.norm(stock_dir)
    w = np.cross(stock_dir, u)
    w *= np.sign(w @ side_dir) or 1.0
    Rs = np.column_stack([stock_dir, u, w])
    d.add_box_mesh(grp, "timber", sy, (1.3, 0.13, 0.13), Rs)
    for dx in (-0.4, 0.4):
        d.add_box_mesh(grp, "iron", sy + stock_dir * dx, (0.045, 0.15, 0.15), Rs)
    for sgn, reach in ((1, 0.58), (-1, 0.58)):
        tipa = crown + u * 0.55 + w * sgn * reach
        ua = (tipa - crown) / np.linalg.norm(tipa - crown)
        perp = np.cross(ua, stock_dir)
        perp /= np.linalg.norm(perp)
        d.add_beam_mesh(grp, "iron", crown, tipa, 0.08)
        d.add_box_mesh(grp, "iron", tipa - ua * 0.1, (0.33, 0.04, 0.26), np.column_stack([ua, perp, stock_dir]))
    tipr = rc - np.array([0, 0.16, 0])
    d.add_rope(hawse_out, tipr, r=0.06, group="part:anchorcable")
    return [{"code": "anchor", "kind": "slide", "axisPoint": [0.0, 0.0, 0.0], "axisDir": [0.0, -1.0, 0.0], "distance": 8.5,
             "source": "anchor"},
            {"code": "anchorcable", "kind": "follow", "follow": "anchor", "anchor": _r(hawse_out), "tip": _r(tipr)}]


# ---------------------------------------------------------------------------------------------- the bilge pump
def pump(d, px, pz, deck_y, deck_t, foot_y, brake_dir=1.0, pipe_x=None, pipe_mat="brass"):
    """carrack.pump: a cast bronze chamber on a flange with moulded rings and a cap, a spout cast on to starboard and a
    little down, the brake on a bronze fork and pin with its spear hung over the bore. (24 October 2026, chalk note 81:
    "pumps pipe down now takes 1 block floor space on lower deck and also pentetrates the deck beam ... piping next to mast
    to fit in that block and piping would start above deck and pipe could be thinner and from brass") Its pipe leaves the
    chamber's foot above the deck, runs aft to pipe_x (in the main mast's cell, beside the mast), down through the deck
    there to a strainer on the hold floor: brass, 0.09 across. Returns the fixture and its two moving parts."""
    up = np.array([0.0, 1.0, 0.0])
    base = np.array([px, deck_y, pz])
    d.add_cylinder("exterior", "bronze", base, base + up * 0.05, 0.23, 0.21, sides=16, smooth=False)
    d.add_cylinder("exterior", "bronze", base + up * 0.05, base + up * 1.0, 0.17, 0.155, sides=16)
    for h, r in ((0.09, 0.19), (0.48, 0.18), (0.94, 0.175)):
        d.add_cylinder("exterior", "bronze", base + up * (h - 0.03), base + up * (h + 0.03), r, r, sides=16, smooth=False)
    d.add_cylinder("exterior", "bronze", base + up * 1.0, base + up * 1.06, 0.195, 0.18, sides=16, smooth=False)
    so = 1.0 if abs(pz) < 0.3 else math.copysign(1.0, pz)
    sd = np.array([0.0, -math.sin(math.radians(15)), so * math.cos(math.radians(15))])
    s0 = base + up * 0.74 + np.array([0, 0, so * 0.1])
    s1 = s0 + sd * 0.36
    d.add_cylinder("exterior", "bronze", s0, s1, 0.055, 0.045, sides=10)
    d.add_cylinder("exterior", "bronze", s1 - sd * 0.035, s1 + sd * 0.005, 0.06, 0.06, sides=10, smooth=False, caps=False)
    annulus(d, "exterior", "bronze", s1 + sd * 0.005, sd, 0.034, 0.06, 10)
    disk(d, "exterior", "black", s1 + sd * 0.005, sd, 0.034, 10)
    spout = s1 + sd * 0.005
    bd = brake_dir
    handle = np.array([px + 1.05 * bd, deck_y + 1.02, pz])
    piv = np.array([px + 0.2 * bd, deck_y + 1.30, pz])
    slope = (handle[1] - piv[1]) / (handle[0] - piv[0])
    pin = np.array([px, piv[1] + slope * (px - piv[0]), pz])
    short = np.array([px - 0.08 * bd, piv[1] + slope * (px - 0.08 * bd - piv[0]), pz])
    d.add_box_mesh("exterior", "bronze", (px + 0.2 * bd, deck_y + 1.04, pz), (0.12, 0.08, 0.2))
    for dz in (-0.07, 0.07):
        d.add_box_mesh("exterior", "bronze", (piv[0], (deck_y + 1.07 + piv[1] + 0.06) / 2, pz + dz),
                       (0.08, piv[1] + 0.06 - deck_y - 1.07, 0.035))
    d.add_beam_mesh("part:pump-brake", "timber", short, handle, 0.07, 0.08)
    d.add_cylinder("exterior", "bronze", piv + np.array([0, 0, -0.11]), piv + np.array([0, 0, 0.11]), 0.025, 0.025, sides=8)
    ub = (handle - short) / np.linalg.norm(handle - short)
    vb = np.cross(np.array([0, 0, 1.0]), ub)
    d.add_box_mesh("part:pump-brake", "bronze", piv, (0.12, 0.13, 0.1), np.column_stack([ub, vb, [0, 0, 1.0]]))
    for dz in (-0.055, 0.055):
        d.add_box_mesh("part:pump-brake", "bronze", pin + np.array([0, 0, dz]), (0.1, 0.13, 0.02), np.column_stack([ub, vb, [0, 0, 1.0]]))
    d.add_cylinder("part:pump-brake", "timber", handle + np.array([0, 0, -0.22]), handle + np.array([0, 0, 0.22]), 0.035, 0.035, sides=6)
    d.add_cylinder("part:pump-spear", "iron", pin + np.array([0, -0.02, 0]), np.array([px, deck_y + 0.9, pz]), 0.02, 0.02, sides=6)
    d.add_box_mesh("part:pump-spear", "iron", pin, (0.06, 0.07, 0.07))
    d.add_cylinder("part:pump-spear", "iron", pin + np.array([0, 0, -0.075]), pin + np.array([0, 0, 0.075]), 0.014, 0.014, sides=6)
    disk(d, "exterior", "black", (px, deck_y + 1.063, pz), (0, 1, 0), 0.035, 8)
    rx = pin[0] - piv[0]
    # the pipe: out of the chamber's after side 0.13 over the deck, aft along it, a bend, down beside the mast
    PR = 0.045
    qx = px - 0.5 if pipe_x is None else pipe_x
    yb = deck_y + 0.13
    c0 = np.array([px - 0.12, yb, pz])                       # in the chamber (r 0.155..0.17 there)
    c1 = np.array([qx + 0.09, yb, pz])
    c2 = np.array([qx, yb - 0.09, pz])
    d.add_cylinder("exterior", pipe_mat, c0, c1, PR, PR, sides=8)
    bend = [np.array([qx + 0.09 - 0.09 * math.sin(a_), yb - 0.09 + 0.09 * math.cos(a_), pz]) for a_ in np.linspace(0, math.pi / 2, 4)]
    for a_, b_ in zip(bend[:-1], bend[1:]):
        d.add_cylinder("exterior", pipe_mat, a_, b_, PR, PR, sides=8)
    d.add_cylinder("exterior", pipe_mat, c2, np.array([qx, deck_y + 0.002, pz]), PR, PR, sides=8)
    d.add_cylinder("exterior", pipe_mat, np.array([qx, deck_y, pz]), np.array([qx, deck_y + 0.03, pz]), PR + 0.025, PR + 0.025, sides=8, smooth=False)
    d.add_cylinder("auto", pipe_mat, np.array([qx, deck_y - deck_t, pz]), np.array([qx, foot_y + 0.16, pz]), PR, PR, sides=8)
    for yy in (deck_y - deck_t - 0.04, (deck_y - deck_t + foot_y) / 2):     # (a collar under the deck, one halfway down)
        d.add_cylinder("auto", pipe_mat, np.array([qx, yy - 0.03, pz]), np.array([qx, yy + 0.03, pz]), PR + 0.015, PR + 0.015, sides=8, smooth=False)
    # the strainer on the hold floor: a brass rose, holes round it
    d.add_cylinder("auto", pipe_mat, np.array([qx, foot_y, pz]), np.array([qx, foot_y + 0.16, pz]), 0.075, 0.06, sides=8, smooth=False)
    for k in range(6):
        a_ = 2 * math.pi * k / 6
        nrm = np.array([math.cos(a_), 0.0, math.sin(a_)])
        disk(d, "auto", "black", np.array([qx, foot_y + 0.06, pz]) + nrm * 0.0715, nrm, 0.012, 5)
    parts = [{"code": "pump-brake", "axisPoint": _r(piv), "axisDir": [0.0, 0.0, 1.0], "source": "pump:pump", "angle": 12.0},
             {"code": "pump-spear", "kind": "slide", "axisPoint": _r(pin), "axisDir": [0.0, 1.0, 0.0], "source": "pump:pump",
              "distance": round(float(rx * math.sin(math.radians(12.0))), 4)}]
    lo, hi = min(px - 0.35, handle[0] - 0.1), max(px + 0.35, handle[0] + 0.1)
    fixture = {"type": "pump", "code": "pump", "pos": _r((px, deck_y, pz)), "yaw": 0.0,
               "sel": _r((lo, deck_y, pz - 0.3, hi, deck_y + 1.5, pz + 0.3)), "spout": _r(spout), "spoutDir": _r(sd)}
    return fixture, parts


# ---------------------------------------------------------------------------------------------- lanterns
def figurehead_seat(d, outline, hook, angle=20):
    """Timber saddle under the bear's neck, backed into the stem just below its head."""
    x, y, _ = hook
    t = math.radians(angle)
    c, s = math.cos(t), math.sin(t)
    def fitted(u, v):
        return np.array([x + c * (u - 0.05865828) + s * v,
                         y - s * (u - 0.05865828) + c * v])
    heel, toe, back_top = fitted(.25, 0), fitted(.75, 0), fitted(.25, .245)
    # Only the stem's fore outline, before its top returns along the inner edge.
    top_index = max((i for i,p in enumerate(outline) if p[0]>5000), key=lambda i:outline[i][1])
    fore = outline[:top_index+1]
    stem = sorted([(p[1] / 1000 - 2.875, p[0] / 1000) for p in fore if p[0] > 10000], key=lambda p:p[0])
    def stem_x(h):
        return float(np.interp(h, [p[0] for p in stem], [p[1] for p in stem])) - .01
    low = toe[1] - .09
    profile = [toe - [0, .09], toe, heel, back_top, np.array([stem_x(back_top[1]), back_top[1]])]
    profile += [np.array([xx-.01, yy]) for yy, xx in reversed(stem) if low < yy < back_top[1]]
    profile.append(np.array([stem_x(low), low]))
    # Ear-clip the saddle's concave profile before the convex face/UV tiler.
    vertices=list(range(len(profile)))
    cross=lambda a,b,c:float(np.cross(b-a,c-a))
    sign=1 if sum(np.cross(a,b) for a,b in zip(profile,profile[1:]+profile[:1]))>0 else -1
    triangles=[]
    while len(vertices)>3:
        for j in range(len(vertices)):
            ia,ib,ic=vertices[j-1],vertices[j],vertices[(j+1)%len(vertices)]
            a,b,c_=profile[ia],profile[ib],profile[ic]
            if sign*cross(a,b,c_)<1e-10:continue
            if any(sign*cross(a,b,profile[k])>=-1e-10 and sign*cross(b,c_,profile[k])>=-1e-10 and sign*cross(c_,a,profile[k])>=-1e-10 for k in vertices if k not in (ia,ib,ic)):continue
            triangles.append((ia,ib,ic));vertices.pop(j);break
        else:raise ValueError("Cannot triangulate figurehead saddle")
    triangles.append(tuple(vertices))
    for side in (-1, 1):
        for triangle in triangles:
            poly = [np.array([profile[i][0], profile[i][1], side * .095]) for i in triangle]
            d.add_planar("exterior", "oak", poly, (0, 0, side), u_dir=(1, 0, 0))
    for a, b in zip(profile, profile[1:] + profile[:1]):
        pts = [np.array([a[0],a[1],-.095]),np.array([b[0],b[1],-.095]),np.array([b[0],b[1],.095]),np.array([a[0],a[1],.095])]
        d.add_planar("exterior", "oak", pts, np.cross(pts[1]-pts[0],pts[2]-pts[0]), u_dir=pts[1]-pts[0])


def mast_band(d, mast_x, y, radius, face=-1.0):
    """A mast hook: an iron band round the mast, an arm reaching to port, a pin down at its end the
    lantern hangs from. Returns the hook's point."""
    d.add_cylinder("auto", "iron", (mast_x, y - 0.04, 0), (mast_x, y + 0.04, 0), radius + 0.025, radius + 0.025, sides=16, smooth=False)
    # The port arm clears the cast-off shrouds beside the after face of each mast.
    arm = np.array([mast_x, y, -0.7])
    d.add_beam_mesh("auto", "iron", (mast_x, y, -radius), arm, 0.04, 0.05)
    d.add_cylinder("auto", "iron", arm, arm - np.array([0, 0.06, 0]), 0.012, 0.012, sides=4)
    return np.array([arm[0], y - 0.06, arm[2]])


def stern_lantern(d, x_aft, x_post, rail_top):
    """The carrack's stern lantern bracket: a stout post on the taffrail's middle, an iron crane from its head out aft
    over the water, braced from below, a plate at its end the lantern stands on, a ring round its foot. Returns the
    plate's top."""
    lx, t1 = x_post, rail_top
    top = t1 + 0.6
    d.add_box_mesh("exterior", "timber", (lx, (t1 + top) / 2, 0.0), (0.16, top - t1, 0.16))
    d.add_box_mesh("exterior", "timber", (lx, top + 0.03, 0.0), (0.2, 0.06, 0.2))
    for dz in (-0.1, 0.1):
        d.add_box_mesh("exterior", "timber", (lx, t1 + 0.12, dz), (0.12, 0.24, 0.04))
    end = np.array([x_aft - 0.38, top - 0.06, 0.0])
    d.add_beam_mesh("exterior", "iron", (lx - 0.08, top - 0.06, 0.0), end, 0.05, 0.05)
    d.add_beam_mesh("exterior", "iron", (lx - 0.08, top - 0.4, 0.0), end + np.array([0.2, -0.02, 0.0]), 0.04, 0.04)
    d.add_box_mesh("exterior", "iron", (lx - 0.09, top - 0.4, 0.0), (0.02, 0.08, 0.1))
    plate = end + np.array([0.0, 0.02, 0.0])
    d.add_cylinder("exterior", "iron", plate - np.array([0, 0.015, 0]), plate + np.array([0, 0.015, 0]), 0.19, 0.19, sides=12, smooth=False)
    rope_path(d, ring_points(plate + np.array([0, 0.07, 0]), 0.19, "y", 12), r=0.012, mat="iron")
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        q = plate + np.array([0.19 * math.cos(a), 0.0, 0.19 * math.sin(a)])
        d.add_cylinder("exterior", "iron", q, q + np.array([0, 0.07, 0]), 0.012, 0.012, sides=4)
    return plate + np.array([0, 0.015, 0])
