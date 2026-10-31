"""
The waist guns (backlog item 16): a cast bronze muzzle-loader of about 1500-1520 on a wooden truck carriage, after the
Mary Rose's bronze culverins and demi-culverins: a long slender barrel with reinforcing rings (a base ring, a ring at the
end of each reinforce, a chase girdle), a muzzle swell, trunnions a little forward of its balance with their rimbases,
and a cascabel the breeching rope is made fast round; its carriage two stepped cheeks on two axletrees with four small
solid wheels (trucks), iron cap squares over the trunnions, a bed under the breech.

Shortened for the game: the barrel 2.34 blocks long, a cut demi-culverin (a drake), the bore 0.14 (a demi-culverin's, a
9-pound iron ball). (Since 0.3.8, the user's note 45: "cannons need to be a bit shorter; after firing they needed
cleaning the barrel, loading etc., so they need time and space". It was 3.0, the Mary Rose's culverins' 3 m at the
carrack's 1.25x less a little; the barrel and the carriage are 0.78 of their length before, the bore, the rings and
the heights as they were.)

Gun-local coordinates: x along the barrel toward the muzzle (outboard), y up from the deck, z across; the origin on the
deck under the trunnions.

Laid (Hansa 0.3.6): the whole gun trains about an upright axis in its port (handspikes shift the trail, the muzzle
stays in the port); it runs back along the way it's laid; the barrel elevates about its trunnions, from 5° down to 15° up
(through a port open to the rail), its breech coming down onto a low stool bed at full elevation. No quoin: a wedge under
the breech can't follow it over that range without the cascabel coming down into it (to come, maybe: an elevating
block). Moving groups (the mod composes them, parent first):
  part:<code>-train    nothing of its own: the turn about the upright through the port (source "train:<code>")
  part:<code>          the carriage: cheeks, cap squares, transom, bed, axletrees (it runs back, "fire:<code>")
  part:<code>-barrel   the barrel and its trunnions (it elevates about them, "elevate:<code>")
  part:<code>-trucks-* the trucks (they turn as it rolls back)
  part:<code>-breeching-a/b the breeching from an eyebolt on the cheek round the cascabel (it follows the barrel, its
                       end on the cheek going with the carriage)
  part:<code>-breeching-a/b-out the breeching from the ring bolt on the side to that eyebolt (it follows the carriage)

  add_gun(d, origin, out, code, ...)   the gun on a ship design
  add_shot(d, group, centre)           a cannon ball (the mod draws the shot in flight with it)
  shape()                              the same gun as a block shape of boxes (the placeable gun, ships:gun-bronze-*)
  check()                              the barrel clear of its own carriage, laid

  python gun.py                        writes assets/ships/shapes/block/gun-bronze.json (and runs check())
"""
import json
import math
import os

import numpy as np

# (0.3.6: the axis 0.03 higher, so the muzzle clears the port's sill laid 5° down as it runs back in through it; the
# carriage's steps, its transom and bed lower to let the breech come down as it's laid up to 15°)
AXIS_Y = 0.55           # the barrel's axis above the deck (the trunnions')
# (0.3.8) the barrel and the carriage this much of their length before (3.0 from the button to the muzzle): 2.34
SHORT = 0.78125
BREECH_X, MUZZLE_X = round(-1.15 * SHORT, 4), round(1.60 * SHORT, 4)
BORE = 0.14
CASCABEL = (round(-1.28 * SHORT, 4), AXIS_Y)          # where the breeching is made fast (the cascabel's neck)
TRUNNION_R, TRUNNION_Z = 0.055, 0.30
# (0.3.27, note 204: "we can and should reduce the distance guns are pulled in in storage and the recoil position: that
# would give more space") 0.75, run back when fired and housed: the muzzle still in behind the shut lid (by 0.13)
RECOIL = 0.75           # how far she runs back when fired (blocks)
TRUCK_R, TRUCK_Z, TRUCK_T = 0.15, 0.345, 0.07
AXLES = {"fore": 0.16, "aft": -0.39}
CHEEK_Z, CHEEK_T = 0.225, 0.09
# the cheeks, stepped down aft: (from x, to x, top, bottom: on the axletrees, aft of them lower; the last step on aft to
# the breech's face, where the breeching's eyebolts are; the middle one low enough for the breeching to pass over it to
# them run back); the beds between them: forward of the after axletree on the
# axletrees, and aft of it a stool bed on the deck under the breech (x0, x1, y0, y1); the transom across their fore ends
CHEEKS = ((-0.14, 0.30, AXIS_Y - TRUNNION_R - 0.005, 0.205), (-0.39, -0.14, 0.315, 0.205), (-0.94, -0.39, 0.22, 0.10))
BEDS = ((-1.02, AXLES["aft"] - 0.065, 0.0, 0.05), (AXLES["aft"] + 0.065, 0.08, 0.10, 0.15))
TRANSOM = (0.22, 0.28, 0.12, 0.30)
SEAT_X = -1.65          # the gun captain stands here, behind the breech
SHOT_R = 0.065          # the ball: a little under the bore (windage)
# the breeching's eyebolt on the top of each cheek, at its after end, beside the breech's face: the breeching runs from
# the ring bolt on the ship's side to it, through it and on round the cascabel's neck (0.3.8, the user's note 45:
# "cannon ropes attach to end of cannon and go through cannon itself": it ran straight from the ring bolt to the
# cascabel, through the cheek and the breech)
EYE = (-0.87, 0.22 + 0.035)
ROPE_R = 0.035

# the barrel's outline (x, outer radius) from the cascabel's button to the muzzle, and in down the bore: a lathe profile.
# A step (two points at one x) is a ring's shoulder.
# (the demi-culverin's of 0.3.5-0.3.7, its lengths SHORT of them: its rings and its swell as they were, a little closer)
PROFILE = [(round(x * SHORT, 4), r) for x, r in [
    (-1.40, 0.0), (-1.385, 0.035), (-1.36, 0.055), (-1.33, 0.058), (-1.305, 0.045), (-1.28, 0.03),   # button, neck
    (-1.22, 0.034), (-1.205, 0.09), (-1.18, 0.15), (-1.15, 0.19),                                     # the breech
    (-1.10, 0.195), (-1.10, 0.184),                                                                    # base ring
    (-0.45, 0.175), (-0.45, 0.19), (-0.40, 0.19), (-0.40, 0.17),                                      # first reinforce, ring
    (0.30, 0.157), (0.30, 0.17), (0.34, 0.17), (0.34, 0.15),                                          # second reinforce, ring
    (1.30, 0.118), (1.30, 0.132), (1.34, 0.132), (1.34, 0.114),                                       # chase, girdle
    (1.44, 0.111), (1.50, 0.13), (1.56, 0.142), (1.60, 0.142),                                        # the muzzle swell
    (1.60, BORE / 2), (1.30, BORE / 2),                                                               # the face, the bore
]]


class Frame:
    """Gun-local -> ship: origin on the deck under the trunnions, `out` the level direction the barrel points."""

    def __init__(self, origin, out):
        self.o = np.asarray(origin, float)
        u = np.asarray(out, float)
        self.u = u / np.linalg.norm(u)
        self.up = np.array([0.0, 1.0, 0.0])
        self.w = np.cross(self.u, self.up)
        self.R = np.column_stack([self.u, self.up, self.w])

    def __call__(self, x, y, z):
        return self.o + x * self.u + y * self.up + z * self.w


def lathe(d, group, mat, f, profile, axis_y=AXIS_Y, sides=16, sid=None):
    """A body of revolution about the gun-local x axis at height axis_y: bands between the profile's points, each
    smooth round (its own smoothing id) with a hard edge where two bands meet."""
    angles = [2 * math.pi * k / sides for k in range(sides + 1)]
    for n, ((x0, r0), (x1, r1)) in enumerate(zip(profile[:-1], profile[1:])):
        if abs(x1 - x0) < 1e-9 and abs(r1 - r0) < 1e-9:
            continue
        # outward: the profile's tangent turned to the right in the (x, r) plane
        nx, nr = -(r1 - r0), (x1 - x0)
        length = math.hypot(x1 - x0, r1 - r0)
        smooth = f"{sid or group}-{n}"
        for k in range(sides):
            a0, a1 = angles[k], angles[k + 1]

            def P(x, r, a):
                return f(x, axis_y + r * math.cos(a), r * math.sin(a))
            pts = [P(x0, r0, a0), P(x0, r0, a1), P(x1, r1, a1), P(x1, r1, a0)]
            am = (a0 + a1) / 2
            want = nx * f.u + nr * (math.cos(am) * f.up + math.sin(am) * f.w)
            nrm = np.cross(pts[1] - pts[0], pts[3] - pts[0]) + np.cross(pts[3] - pts[2], pts[1] - pts[2])
            if np.dot(nrm, want) < 0:
                pts = [pts[1], pts[0], pts[3], pts[2]]
            if r0 < 1e-6 or r1 < 1e-6:          # a point at the axis: a triangle
                tip = pts[0] if np.allclose(pts[0], pts[1]) else pts[3] if np.allclose(pts[3], pts[2]) else None
                if tip is not None and tip is pts[0]:
                    pts = [pts[0], pts[2], pts[3], pts[3]]
                elif tip is not None:
                    pts = [pts[0], pts[1], pts[2], pts[2]]
            d.add_quad(group, mat, pts, (16 * k / sides, 0, 16 * (k + 1) / sides, min(16.0, max(1.0, length * 16))),
                       smooth=smooth)


def add_gun(d, origin, out, code, ring_bolts=(), pivot_x=0.0, bronze="bronze", wood="timber", planks="oak", iron="iron",
            rope="rope", dark="black"):
    """
    The gun and its carriage on a ship design, run out and laid square (level), in its moving groups (see the top):
    the carriage in "part:<code>" (it slides back when it's fired), the barrel in "part:<code>-barrel" (it elevates about
    its trunnions), its trucks in "part:<code>-trucks-fore" / "-aft" (they turn as it
    rolls), and its breeching (ropes from the ring bolts beside the port to the cascabel, which stretch as it runs back)
    as follow parts. pivot_x: where (gun-local x, in its port) the upright axis it trains about is. Returns what the
    fixture and the moving parts need: its muzzle, the trunnions, the pivot, the gun captain's place, the cascabel, the
    axles (ship-local).
    """
    f = Frame(origin, out)
    g = f"part:{code}"
    gb = f"part:{code}-barrel"
    lathe(d, gb, bronze, f, PROFILE, sid=f"{code}-barrel")
    # the bore, dark inside the muzzle
    c = f(PROFILE[-1][0], AXIS_Y, 0)
    _disk(d, gb, dark, c, f.u, BORE / 2 + 0.002)
    # the trunnions and their rimbases (they turn in the cheeks with the barrel)
    for s in (1, -1):
        d.add_cylinder(gb, bronze, f(0, AXIS_Y, s * 0.13), f(0, AXIS_Y, s * 0.18), 0.075, 0.075, sides=12, smooth=False)
        d.add_cylinder(gb, bronze, f(0, AXIS_Y, s * 0.18), f(0, AXIS_Y, s * TRUNNION_Z), TRUNNION_R, TRUNNION_R, sides=12,
                       smooth=False)
    # the carriage: two cheeks, stepped down aft (the breech comes down there laid up), on two axletrees
    for s in (1, -1):
        for x0, x1, top, bottom in CHEEKS:
            _box(d, g, wood, f, (x0, bottom, s * CHEEK_Z - CHEEK_T / 2), (x1, top, s * CHEEK_Z + CHEEK_T / 2))
        # the cap square: an iron strap over the trunnion, bolted down to the cheek either side
        _box(d, g, iron, f, (-0.075, AXIS_Y + TRUNNION_R, s * CHEEK_Z - 0.05), (0.075, AXIS_Y + TRUNNION_R + 0.025, s * CHEEK_Z + 0.05))
        for xb in (-0.075, 0.075):
            # (Hansa 0.3.10, clean surfaces: the straps 0.01 inside the cap square's sides and under its top)
            _box(d, g, iron, f, (xb - 0.012, AXIS_Y - 0.06, s * CHEEK_Z - 0.04), (xb + 0.012, AXIS_Y + TRUNNION_R + 0.015, s * CHEEK_Z + 0.04))
        # bolts through the cheek, their heads outside
        for xb, yb in ((0.23, 0.3), (-0.31, 0.27), (-0.62, 0.16)):
            d.add_cylinder(g, iron, f(xb, yb, s * (CHEEK_Z + CHEEK_T / 2)), f(xb, yb, s * (CHEEK_Z + CHEEK_T / 2 + 0.015)), 0.022,
                           0.022, sides=6, smooth=False)
    # the transom between the cheeks at the front (low: the chase passes over it laid down), the beds: on the axletrees
    # between them, and a stool bed on the deck under the breech (it comes down onto it laid fully up)
    x0, x1, y0, y1 = TRANSOM
    _box(d, g, wood, f, (x0, y0, -CHEEK_Z + CHEEK_T / 2), (x1, y1, CHEEK_Z - CHEEK_T / 2))
    for x0, x1, y0, y1 in BEDS:
        _box(d, g, planks, f, (x0, y0, -CHEEK_Z + CHEEK_T / 2), (x1, y1, CHEEK_Z - CHEEK_T / 2))
    # axletrees
    for ax in AXLES.values():
        _box(d, g, wood, f, (ax - 0.065, TRUCK_R - 0.055, -0.4), (ax + 0.065, 0.205, 0.4))
    # the trucks, each pair turning on its axle; linchpins
    for name, ax in AXLES.items():
        tg = f"part:{code}-trucks-{name}"
        for s in (1, -1):
            a, b = f(ax, TRUCK_R, s * (TRUCK_Z - TRUCK_T / 2)), f(ax, TRUCK_R, s * (TRUCK_Z + TRUCK_T / 2))
            d.add_cylinder(tg, planks, a, b, TRUCK_R, TRUCK_R, sides=12, smooth=False)
            # a spoke-less solid truck: an iron hub to show it turn
            d.add_cylinder(tg, iron, f(ax, TRUCK_R, s * (TRUCK_Z + TRUCK_T / 2)), f(ax, TRUCK_R, s * (TRUCK_Z + TRUCK_T / 2 + 0.012)),
                           0.05, 0.05, sides=8, smooth=False)
            _box(d, tg, planks, f, (ax - 0.02, TRUCK_R + 0.06, s * (TRUCK_Z + TRUCK_T / 2)),
                 (ax + 0.02, TRUCK_R + 0.13, s * (TRUCK_Z + TRUCK_T / 2 + 0.006)))
            _box(d, g, iron, f, (ax - 0.012, TRUCK_R - 0.03, s * (TRUCK_Z + TRUCK_T / 2 + 0.03)), (ax + 0.012, TRUCK_R + 0.03, s * (TRUCK_Z + TRUCK_T / 2 + 0.045)))
    # the breeching: a stout rope from each ring bolt beside the port to the eyebolt on the top of the cheek on its side
    # at the cheek's after end, through it, and on round the cascabel's neck behind the breech (follow parts: the first
    # goes with the carriage, stretching as she runs back and turning as she's trained; the second from the eyebolt, on
    # the carriage, to the cascabel, which goes with the barrel as it's elevated). Until 0.3.8 it ran straight from the
    # ring bolt to the cascabel, through the cheek and the breech (the user's note 45).
    cas = f(CASCABEL[0], CASCABEL[1], 0)
    breeching = []
    for i, rb in enumerate(ring_bolts):
        rb = np.asarray(rb, float)
        d.add_cylinder("exterior", iron, rb - f.u * 0.02, rb + f.u * 0.03, 0.045, 0.045, sides=8, smooth=False)
        # the cheek on this ring bolt's side
        zs = 1.0 if float(np.dot(rb - f.o, f.w)) > 0 else -1.0
        eye = f(EYE[0], EYE[1], zs * CHEEK_Z)
        _box(d, g, iron, f, (EYE[0] - 0.035, 0.22, zs * CHEEK_Z - 0.03), (EYE[0] + 0.035, 0.235, zs * CHEEK_Z + 0.03))
        ring = [f(EYE[0] + 0.035 * math.cos(a), EYE[1] + 0.035 * math.sin(a), zs * CHEEK_Z) for a in np.linspace(0, math.pi, 7)]
        for a_, b_ in zip(ring[:-1], ring[1:]):
            d.add_cylinder(g, iron, a_, b_, 0.009, 0.009, sides=4, caps=False)
        rc = f"{code}-breeching-{'ab'[i % 2]}"
        side = f(CASCABEL[0], CASCABEL[1], zs * 0.05)
        d.add_rope(rb - f.u * 0.03, eye, r=ROPE_R, group=f"part:{rc}-out", mat=rope)
        d.add_rope(eye, side, r=ROPE_R, group=f"part:{rc}", mat=rope)
        breeching.append({"code": f"{rc}-out", "anchor": rb - f.u * 0.03, "tip": eye, "follow": code})
        breeching.append({"code": rc, "anchor": eye, "tip": side, "follow": f"{code}-barrel", "anchorFollow": code})
    # a turn of the breeching round the cascabel's neck (it goes with the barrel)
    for k in range(8):
        a0, a1 = 2 * math.pi * k / 8, 2 * math.pi * (k + 1) / 8
        p0 = f(CASCABEL[0], AXIS_Y + 0.05 * math.cos(a0), 0.05 * math.sin(a0))
        p1 = f(CASCABEL[0], AXIS_Y + 0.05 * math.cos(a1), 0.05 * math.sin(a1))
        d.add_cylinder(gb, rope, p0, p1, 0.025, 0.025, sides=5, caps=False)
    return {
        "muzzle": f(MUZZLE_X + 0.05, AXIS_Y, 0),
        "trunnion": f(0, AXIS_Y, 0),
        "pivot": f(pivot_x, 0, 0),
        "seat": f(SEAT_X, 0, 0),
        "cascabel": cas,
        "axles": {name: f(ax, TRUCK_R, 0) for name, ax in AXLES.items()},
        "frame": f,
        "breeching": breeching,
    }


def train_sign(out):
    """Which way the upright axis a gun trains about points: + turns its muzzle toward the bow (+x) by the right hand
    rule, so about +y for a gun pointing to starboard (+z), about -y for one pointing to port."""
    return 1.0 if out[2] >= 0 else -1.0


def moving_parts(code, info, recoil=RECOIL):
    """
    The gun's moving parts, each after the one it rides on (MovingPartDef.Parent): it trains about the upright through
    its port ("train:<code>", degrees toward the bow), runs back along the way it's laid when fired ("fire:<code>"), its
    barrel elevates about the trunnions ("elevate:<code>", degrees up), its trucks turn as it rolls, its breeching
    stretches and follows the cascabel.
    """
    f = info["frame"]
    inboard = -f.u
    turn = np.cross(f.up, inboard)       # a wheel rolling inboard turns about up x its way
    deg = math.degrees(recoil / TRUCK_R)
    train = f"{code}-train"
    parts = [{"code": train, "kind": "rotate", "axisPoint": _r(info["pivot"]), "axisDir": [0.0, train_sign(f.u), 0.0],
              "source": f"train:{code}", "angle": 1.0},
             {"code": code, "kind": "slide", "axisPoint": _r(f.o), "axisDir": _r(inboard), "distance": recoil,
              "source": f"fire:{code}", "parent": train},
             {"code": f"{code}-barrel", "kind": "rotate", "axisPoint": _r(info["trunnion"]), "axisDir": _r(f.w),
              "source": f"elevate:{code}", "angle": 1.0, "parent": code}]
    for name, p in info["axles"].items():
        parts.append({"code": f"{code}-trucks-{name}", "kind": "rotate", "axisPoint": _r(p), "axisDir": _r(turn),
                      "source": f"fire:{code}", "angle": round(deg, 3), "parent": code})
    for b in info["breeching"]:
        p = {"code": b["code"], "kind": "follow", "follow": b.get("follow", f"{code}-barrel"), "anchor": _r(b["anchor"]),
             "tip": _r(b["tip"])}
        if b.get("anchorFollow"):
            p["anchorFollow"] = b["anchorFollow"]
        parts.append(p)
    return parts


def elevated(points, elev):
    """Gun-local points (rows of x out, y up, z across) of the barrel turned up about its trunnions by elev degrees."""
    p = np.array(points, float).reshape(-1, 3).copy()
    e = math.radians(elev)
    x, y = p[:, 0].copy(), p[:, 1] - AXIS_Y
    p[:, 0] = x * math.cos(e) - y * math.sin(e)
    p[:, 1] = x * math.sin(e) + y * math.cos(e) + AXIS_Y
    return p


def radius(x):
    """The barrel's outer radius at gun-local x (the widest where the profile steps)."""
    best = 0.0
    for (x0, r0), (x1, r1) in zip(PROFILE[:-2], PROFILE[1:-1]):
        if x0 - 1e-9 <= x <= x1 + 1e-9:
            best = max(best, r0, r1) if x1 - x0 < 1e-9 else max(best, r0 + (r1 - r0) * (x - x0) / (x1 - x0))
    return best


def check(elev_limits=(-5.0, 15.0), verbose=True):
    """
    The barrel laid at its limits clear of its own carriage (the cheeks, the transom, the beds, the axletrees). Returns
    the worst margin (blocks; negative: into it).
    """
    # the barrel's surface: rings along the profile
    pts = []
    for x in np.arange(PROFILE[0][0], MUZZLE_X + 1e-9, 0.01):
        r = radius(x)
        for k in range(32):
            a = 2 * math.pi * k / 32
            pts.append((x, AXIS_Y + r * math.cos(a), r * math.sin(a)))
    pts = np.array(pts)
    boxes = [((x0, bottom, CHEEK_Z - CHEEK_T / 2), (x1, top, CHEEK_Z + CHEEK_T / 2)) for x0, x1, top, bottom in CHEEKS]
    boxes += [((x0, bottom, -CHEEK_Z - CHEEK_T / 2), (x1, top, -CHEEK_Z + CHEEK_T / 2)) for x0, x1, top, bottom in CHEEKS]
    boxes.append(((TRANSOM[0], TRANSOM[2], -CHEEK_Z), (TRANSOM[1], TRANSOM[3], CHEEK_Z)))
    boxes += [((x0, y0, -CHEEK_Z), (x1, y1, CHEEK_Z)) for x0, x1, y0, y1 in BEDS]
    boxes += [((ax - 0.065, TRUCK_R - 0.055, -0.4), (ax + 0.065, 0.205, 0.4)) for ax in AXLES.values()]
    worst = 9.0
    for e in (elev_limits[0], 0.0, elev_limits[1]):
        p = elevated(pts, e)
        here = 9.0
        for lo, hi in boxes:
            lo, hi = np.asarray(lo), np.asarray(hi)
            # how far each point is outside the box (negative: inside, by that much)
            here = min(here, float(np.max(np.maximum(lo - p, p - hi), axis=1).min()))
        worst = min(worst, here)
        if verbose:
            print(f"  elevation {e:+5.1f}: the barrel {here:.3f} clear of its carriage (the breech {AXIS_Y + BREECH_X * math.sin(math.radians(e)) - 0.19 * math.cos(math.radians(e)):.3f} over the deck)")
    # the breeching from the cheeks' eyebolts round the cascabel: clear of the barrel, laid anywhere
    for e in np.arange(elev_limits[0], elev_limits[1] + 1e-9, 2.5):
        clear = breeching_clear(e)
        worst = min(worst, clear)
    if verbose:
        print(f"  the breeching from the cheeks' eyebolts to the cascabel {min(breeching_clear(e) for e in np.arange(elev_limits[0], elev_limits[1] + 1e-9, 2.5)):.3f} clear of the barrel, laid anywhere")
    return worst


def breeching_clear(elev):
    """How far the breeching's ropes from the cheeks' eyebolts to the cascabel stay off the barrel (its surface less the
    rope's radius), the barrel laid elev degrees: gun-local, the carriage still."""
    worst = 9.0
    cas = elevated([CASCABEL[0], CASCABEL[1], 0.0], elev)[0]
    for zs in (1, -1):
        a = np.array([EYE[0], EYE[1], zs * CHEEK_Z])
        b = cas + np.array([0, 0, zs * 0.05])
        for t in np.linspace(0.0, 0.85, 35):          # (its last stretch is round the neck itself)
            p = a + (b - a) * t
            # into the barrel's own frame: turned back down by elev about the trunnions
            q = elevated(p, -elev)[0]
            rad = math.hypot(q[1] - AXIS_Y, q[2])
            if PROFILE[0][0] <= q[0] <= MUZZLE_X:
                worst = min(worst, rad - radius(q[0]) - ROPE_R)
    return worst


def add_shot(d, group, centre, mat="shot", sides=10):
    """A cast iron ball (the mod draws the shot in flight with this group), centred on `centre`."""
    f = Frame(np.asarray(centre, float) - np.array([0, AXIS_Y, 0]), (1.0, 0, 0))
    prof = [(SHOT_R * math.cos(math.pi * (1 - k / 8)), SHOT_R * math.sin(math.pi * k / 8)) for k in range(9)]
    lathe(d, group, mat, f, prof, sides=sides, sid="shot")


def _r(v):
    return [round(float(x), 4) for x in v]


def _box(d, group, mat, f, lo, hi):
    """A box between two gun-local corners, turned with the gun."""
    lo, hi = np.asarray(lo, float), np.asarray(hi, float)
    c = (lo + hi) / 2
    d.add_box_mesh(group, mat, f(*c), hi - lo, f.R)


def _disk(d, group, mat, c, nrm, r, sides=12):
    """A flat round face facing nrm (it faces back out of the bore, toward the muzzle), textured by its plane."""
    n = np.asarray(nrm, float) / np.linalg.norm(nrm)
    e1 = np.cross(n, [0, 1.0, 0] if abs(n[1]) < 0.9 else [1.0, 0, 0])
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(n, e1)
    pts = [c + r * (math.cos(2 * math.pi * k / sides) * e1 + math.sin(2 * math.pi * k / sides) * e2) for k in range(sides)]
    d.add_planar(group, mat, pts, n, centre=c)


# ==================================================================== the gun as a block shape (boxes)

TEXTURES = {"bronze": "game:block/metal/plate/tinbronze", "wood": "game:block/wood/debarked/oak",
            "planks": "game:block/wood/planks/oak1", "iron": "game:block/metal/plate/iron", "rope": "game:block/cloth/reedrope"}


def shape():
    """
    The gun on its carriage as a vanilla block shape (boxes, like vanilla's own models): the muzzle toward north (-z),
    the trunnions over the block's middle; the barrel as octagonal sections (a square and the same square turned 45°),
    the trucks as octagonal disks. For the placeable gun (ships:gun-bronze-*): a gun a wreck gives back.
    """
    els = []

    def B(x, y, z):          # gun-local -> block (1/16): x' north, y' up, z' east
        return (8 + 16 * z, 16 * y, 8 - 16 * x)

    def box(name, tex, lo, hi, rot=None):
        a, b = B(*lo), B(*hi)
        fr = [min(a[i], b[i]) for i in range(3)]
        to = [max(a[i], b[i]) for i in range(3)]
        size = [to[i] - fr[i] for i in range(3)]
        faces = {}
        for face, (du, dv) in (("north", (size[0], size[1])), ("south", (size[0], size[1])), ("east", (size[2], size[1])),
                               ("west", (size[2], size[1])), ("up", (size[0], size[2])), ("down", (size[0], size[2]))):
            faces[face] = {"texture": "#" + tex, "uv": [0, 0, round(min(16, max(0.5, du)), 3), round(min(16, max(0.5, dv)), 3)]}
        el = {"name": name, "from": [round(v, 3) for v in fr], "to": [round(v, 3) for v in to], "faces": faces}
        if rot:
            axis, deg, origin = rot
            el["rotationOrigin"] = [round(v, 3) for v in B(*origin)]
            el["rotation" + axis] = deg
        els.append(el)

    # the barrel: each band of the profile with a length of its own, as an octagon of its mean radius
    n = 0
    for (x0, r0), (x1, r1) in zip(PROFILE[:-1], PROFILE[1:]):
        if x1 - x0 < 0.02 or max(r0, r1) <= BORE / 2 + 1e-6 and x0 >= MUZZLE_X - 1e-6:
            continue
        r = max(0.02, (r0 + r1) / 2)
        s = r * 2 * math.cos(math.pi / 8)
        for turn in (0, 45):
            box(f"barrel{n}-{turn}", "bronze", (x0, AXIS_Y - s / 2, -s / 2), (x1, AXIS_Y + s / 2, s / 2),
                ("Z", turn, ((x0 + x1) / 2, AXIS_Y, 0)) if turn else None)
        n += 1
    # the trunnions
    for sgn in (1, -1):
        s = TRUNNION_R * 2 * math.cos(math.pi / 8)
        for turn in (0, 45):
            box(f"trunnion{sgn}-{turn}", "bronze", (-s / 2, AXIS_Y - s / 2, sgn * 0.13), (s / 2, AXIS_Y + s / 2, sgn * TRUNNION_Z),
                ("X", turn, (0, AXIS_Y, sgn * 0.2)) if turn else None)
    # the carriage
    for sgn in (1, -1):
        for i, (x0, x1, top, bottom) in enumerate(CHEEKS):
            box(f"cheek{sgn}-{i}", "wood", (x0, bottom, sgn * CHEEK_Z - CHEEK_T / 2), (x1, top, sgn * CHEEK_Z + CHEEK_T / 2))
        box(f"capsquare{sgn}", "iron", (-0.075, AXIS_Y + TRUNNION_R, sgn * CHEEK_Z - 0.05), (0.075, AXIS_Y + TRUNNION_R + 0.025, sgn * CHEEK_Z + 0.05))
    x0, x1, y0, y1 = TRANSOM
    box("transom", "wood", (x0, y0, -CHEEK_Z + CHEEK_T / 2), (x1, y1, CHEEK_Z - CHEEK_T / 2))
    for i, (x0, x1, y0, y1) in enumerate(BEDS):
        box(f"bed{i}", "planks", (x0, y0, -CHEEK_Z + CHEEK_T / 2), (x1, y1, CHEEK_Z - CHEEK_T / 2))
    for name, ax in AXLES.items():
        box(f"axletree-{name}", "wood", (ax - 0.065, TRUCK_R - 0.055, -0.4), (ax + 0.065, 0.205, 0.4))
        for sgn in (1, -1):
            s = TRUCK_R * 2 * math.cos(math.pi / 8)
            for turn in (0, 45):
                box(f"truck-{name}{sgn}-{turn}", "planks", (ax - s / 2, TRUCK_R - s / 2, sgn * (TRUCK_Z - TRUCK_T / 2)),
                    (ax + s / 2, TRUCK_R + s / 2, sgn * (TRUCK_Z + TRUCK_T / 2)), ("X", turn, (ax, TRUCK_R, sgn * TRUCK_Z)) if turn else None)
    return {"textureWidth": 16, "textureHeight": 16, "textures": dict(TEXTURES), "elements": els}


def write_shape(asset_dir):
    path = os.path.join(asset_dir, "shapes", "block", "gun-bronze.json")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(shape(), fh, indent=1)
    return path


if __name__ == "__main__":
    root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    print(write_shape(os.path.join(root, "assets", "ships")))
    check()
