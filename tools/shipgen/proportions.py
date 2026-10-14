"""
Proportion drawings for talking about a hull's shape before building it: a side elevation (keel, stem, transom, sheer
and castles, the decks, the masts, yards and bowsprit, and where the main shrouds come down), the midship section, a
plan, and a table of the numbers the period's rules talk about (beam : keel : length, draught, flat of floor, where the
mainmast stands), of the carrack as she is and of a proposed variant drawn over it. Both are drawn floating: their own
waterlines on the blue one, so a deeper draught shows as the hull sitting lower. Pure drawing from the hull's own
formulas (the same as carrack.py's), so a variant is a dict of numbers, not a new build.

  python proportions.py            # docs/proportions.png: current (filled) against the proposal (outlined)
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(__file__))
from shipgen import interp  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


class Hull:
    """The carrack's hull formulas (carrack.py's hull_hw, rail_y ...) with their numbers as parameters. Heights are the
    design's own (the build's waterline at 0); WATER is where the sea comes to on them."""

    def __init__(self, **p):
        self.p = p

    def __getattr__(self, k):
        try:
            return self.p[k]
        except KeyError:
            raise AttributeError(k)

    def hull_hw(self, x, y):
        x, y = np.asarray(x, float), np.asarray(y, float)
        sec = self.B * interp(y, self.SECTION)
        xb = interp(y, self.BOW_X)
        t = np.clip((x - self.MID[1]) / (xb - self.MID[1]), 0, None)
        fb = np.where(t < 1, np.sqrt(np.clip(1 - t ** 2, 0, 1)), 0.0)
        xs = interp(y, self.STERN_X)
        ts = np.clip((self.MID[0] - x) / (self.MID[0] - xs), 0, None)
        ps = interp(y, self.STERN_P)
        wt = interp(y, self.TRANSOM_W)
        fs0 = np.clip(1 - np.clip(ts, 0, 1) ** ps, 0, 1) ** (1 / ps)
        fs = np.where(ts <= 1, wt + (1 - wt) * fs0, 0.0)
        return np.where(y >= self.KEEL, sec * np.minimum(fb, fs), 0.0)

    def sweep(self, u):
        return 1 - math.sqrt(max(0.0, 1 - min(1.0, max(0.0, u)) ** 2))

    def rail(self, x):
        """Top of the side along the ship: waist, castle breaks sweeping up, the castles."""
        y = self.MAIN + 1.0
        if x < self.POOP_FRONT:
            y = self.POOP + 1.0
        elif x < self.WAIST_AFT:
            y = self.QUARTER + 1.0
        elif x >= self.WAIST_FWD:
            y = self.QUARTER + 1.0
        br = self.BREAK_L
        if self.WAIST_AFT <= x < self.WAIST_AFT + br:
            y = self.MAIN + 1.0 + (self.QUARTER - self.MAIN) * self.sweep((self.WAIST_AFT + br - x) / br)
        if self.WAIST_FWD - br < x < self.WAIST_FWD:
            y = self.MAIN + 1.0 + (self.QUARTER - self.MAIN) * self.sweep((x - self.WAIST_FWD + br) / br)
        if self.POOP_FRONT <= x < self.POOP_FRONT + br:
            y = self.QUARTER + 1.0 + (self.POOP - self.QUARTER) * self.sweep((self.POOP_FRONT + br - x) / br)
        return y

    def stern_x(self, y):
        return float(interp(y, self.STERN_X))

    def bow_x(self, y):
        return float(interp(y, self.BOW_X))

    def fc_under(self, x):
        """The forecastle's underside where it overhangs the stem (rising straight by FC_UNDER to the tip)."""
        xs0 = self.bow_x(self.FC_Y0)
        u = max(0.0, (x - xs0) / max(1e-6, self.FC_TIP - xs0))
        return self.FC_Y0 + self.FC_UNDER * u

    def poop_aft(self):
        """The aft end of the poop cabin: the transom at its top, plus the overhang."""
        return self.stern_x(self.POOP + 1.0) - self.POOP_OVERHANG

    def profile(self):
        """The side elevation's outline: [(x, y)] round the hull (keel, stem, forecastle, rail, castles, transom)."""
        pts = [(self.stern_x(self.KEEL - 0.5), self.KEEL - 0.5), (self.bow_x(self.KEEL), self.KEEL - 0.5)]
        for y in np.linspace(self.KEEL, self.FC_Y0, 20):
            pts.append((self.bow_x(y), y))
        for x in np.linspace(self.bow_x(self.FC_Y0), self.FC_TIP, 12):
            pts.append((x, self.fc_under(x)))
        pts.append((self.FC_TIP, self.QUARTER + 1.0))
        top = self.stern_x(self.POOP + 1.0)
        for x in np.linspace(self.FC_TIP, top, 500):
            pts.append((x, self.rail(x)))
        if self.POOP_OVERHANG > 0:
            # the poop cabin carried out over the transom on its deck beams (a cantilever from the quarterdeck's level)
            pts.append((top - self.POOP_OVERHANG, self.POOP + 1.0))
            pts.append((top - self.POOP_OVERHANG + 0.4, self.QUARTER + 0.1))
            pts.append((self.stern_x(self.QUARTER + 0.1), self.QUARTER + 0.1))
            y0 = self.QUARTER + 0.1
        else:
            y0 = self.POOP + 1.0
        for y in np.linspace(y0, self.KEEL - 0.5, 40):
            pts.append((self.stern_x(y), y))
        return pts

    # ---------------------------------------------------------------- the numbers
    def numbers(self):
        xs = np.arange(-22, 28, 0.05)
        ys = np.arange(self.KEEL, self.WATER, 0.02) + 0.01
        X, Y = np.meshgrid(xs, ys)
        vol = float((2 * self.hull_hw(X, Y)).sum() * 0.05 * 0.02) + 0.25 * 0.5 * self.KEEL_LEN
        hw_wl = self.hull_hw(xs, self.WATER)
        wl = xs[hw_wl > 0.01]
        lwl, bwl = float(wl.max() - wl.min()), 2 * float(hw_wl.max())
        draught = self.WATER - (self.KEEL - 0.5)
        beam = 2 * self.B * max(v for _, v in self.SECTION)
        keel = self.KEEL_LEN
        deck = self.bow_x(self.MAIN) - self.stern_x(self.MAIN)
        loa = self.FC_TIP - self.poop_aft()
        # the flat of the floor: where the bottom (at the keel's top + 0.1) is still within 0.25 of the flat
        floor = 2 * float(self.hull_hw(0.0, self.KEEL + 0.02))
        hold_w = 2 * (float(self.hull_hw(0.0, self.HOLD)) - 0.3)
        k0 = self.stern_x(self.KEEL)
        main_x = self.MASTS["main"][0]
        return [
            ("beam (widest)", f"{beam:.1f}"),
            ("keel", f"{keel:.1f}"),
            ("length on the main deck", f"{deck:.1f}"),
            ("length overall (castles)", f"{loa:.1f}"),
            ("beam : keel : deck", f"1 : {keel / beam:.2f} : {deck / beam:.2f}"),
            ("draught (keel bottom)", f"{draught:.2f}"),
            ("draught / beam", f"{draught / beam:.2f}"),
            ("waterline length x beam", f"{lwl:.1f} x {bwl:.1f}"),
            ("displacement (t)", f"{vol * 1.025:.0f}"),
            ("block coefficient", f"{vol / (lwl * bwl * (draught - 0.5)):.2f}"),
            ("flat of floor / beam", f"{floor / beam:.2f}"),
            ("hold floor, inside width", f"{hold_w:.1f}"),
            ("lower deck to the water", f"{self.LOWER - self.WATER:+.1f}"),
            ("main deck above the water", f"{self.MAIN - self.WATER:.1f}"),
            ("waist (castle to castle)", f"{self.WAIST_FWD - self.WAIST_AFT:.1f}"),
            ("quarterdeck front to transom", f"{self.WAIST_AFT - self.stern_x(self.QUARTER):.1f}"),
            ("poop cabin", f"{self.POOP_FRONT - self.poop_aft():.1f}"),
            ("forecastle (overhang)", f"{self.FC_TIP - self.WAIST_FWD:.1f} ({self.FC_TIP - self.bow_x(self.FC_Y0):.1f})"),
            ("mainmast, % of keel from aft", f"{(main_x - k0) / keel * 100:.0f}%"),
            ("main shrouds land", self.shroud_note()),
        ]

    def shroud_note(self):
        a, b = self.SHROUDS
        front = self.WAIST_AFT + self.BREAK_L
        if b <= front:
            return "on the castle"
        if a >= front:
            return "in the waist"
        return f"castle {min(b, front) - a:.1f}/{b - a:.1f}"


def carrack_now():
    import carrack as c
    masts = {}
    for k, v in c.MASTS.items():
        top = (v[2] or v[1])[1]
        masts[k] = (v[0], v[1][0], top, v[3][0] if v[3] else None, v[1][1])
    return Hull(B=c.B, KEEL=c.KEEL, KEEL_LEN=11.5 + c.STRETCH + 12.5, SECTION=c.SECTION, BOW_X=c.BOW_X,
                STERN_X=c.STERN_X, STERN_P=c.STERN_P, TRANSOM_W=c.TRANSOM_W, MID=(-6.0, c.MIDBODY_FWD), LOWER=c.LOWER,
                MAIN=c.MAIN, QUARTER=c.QUARTER, POOP=c.POOP, WAIST_AFT=c.WAIST_AFT, WAIST_FWD=c.WAIST_FWD,
                POOP_FRONT=c.POOP_FRONT, BREAK_L=c.BREAK_L, FC_Y0=c.FC_Y0, FC_TIP=c.FC_TIP, FC_UNDER=0.0,
                POOP_OVERHANG=0.0, WATER=0.0, HOLD=c.HOLD_FLOOR, MASTS=masts,
                SHROUDS=(c.MAIN_SHROUDS[0], c.MAIN_SHROUDS[-1]),
                YARDS=[(x, y, h) for x, y, h, _ in c.YARDS.values()],
                BOWSPRIT=(tuple(c.BOWSPRIT_A[:2]), tuple(c.BOWSPRIT_B[:2])),
                LATEEN=(tuple(c.LATEEN_A[:2]), tuple(c.LATEEN_B[:2])))


def proposal():
    """The proposal of 2026-09-30 (the fourth client test's points 7-9), from the period's rules and wrecks
    (docs/backlog.md has the sources): she floats deeper, is fuller and a little wider amidships, the quarterdeck comes
    forward to just abaft the mainmast so the main shrouds come down on the castle's side, the poop is longer and
    overhangs the transom, the transom rakes more, the forecastle is longer with a longer, rising overhang, and the
    castle breaks sweep more gently."""
    now = carrack_now()
    p = dict(now.p)
    p.update(
        B=5.25,              # beam 10.5 (9.2): beam : keel : deck about 1 : 2.5 : 3.1 (the rule's 1 : 2 : 3, the wrecks' 2.1-2.7)
        WATER=1.5,           # 1.5 deeper: draught 0.43 of the beam (wrecks and replicas 0.35-0.44)
        # the midship section: a flat floor a third of the beam (Palacio, Cano, the Pepper Wreck), a firm round bilge,
        # widest a little over the waterline, a little tumblehome above
        SECTION=[(-2.75, 0.02), (-2.5, 0.33), (-2.25, 0.44), (-2.0, 0.53), (-1.5, 0.65), (-1.0, 0.75), (-0.5, 0.83),
                 (0.0, 0.89), (0.5, 0.93), (1.0, 0.96), (1.5, 0.985), (2.0, 1.0), (3.0, 1.0), (4.0, 0.97), (5.0, 0.93),
                 (6.5, 0.88), (8.0, 0.85), (11.0, 0.81)],
        # the transom raked more above the water (about 15 degrees; the sternpost's 17 below it), 1.2 further aft at the top
        STERN_X=[(-2.75, -12.25), (-2.5, -12.5), (0.0, -14.2), (1.5, -14.6), (3.5, -15.2), (6.5, -15.9), (11.0, -16.9)],
        POOP_OVERHANG=1.25,  # the poop cabin carried out over the transom
        POOP_FRONT=-9.5,     # and half a block forward: 3 longer in all
        WAIST_AFT=-3.0,      # the quarterdeck 3 forward, to just abaft the mainmast (the tolda "from the mast aft")
        WAIST_FWD=10.0,      # the forecastle 1 longer aft
        FC_TIP=22.5,         # and 1.25 further out over the stem
        FC_Y0=4.75,          # its underside leaving the stem 1.25 lower, just over the waist's rail,
        FC_UNDER=1.5,        # and rising 1.5 to the tip (under the forecastle's deck there)
        BREAK_L=2.75,        # gentler sweeps up to the castles (1.75)
    )
    masts = dict(now.MASTS)
    fx = 14.25               # the foremast nearer the stem (the treatises: 4 palmos abaft it)
    masts["fore"] = (fx,) + tuple(now.MASTS["fore"][1:])
    p["MASTS"] = masts
    p["YARDS"] = [(fx if abs(x - now.MASTS["fore"][0]) < 0.01 else x, y, h) for x, y, h in now.YARDS]
    return Hull(**p)


# ---------------------------------------------------------------- drawing
INK = (40, 40, 40)
GREY = (120, 120, 120)
SEA = (40, 110, 190)


def draw(hulls, path, title, notes=()):
    """hulls: [(Hull, colour, filled, label)]. Side elevation (left), midship section and table (right), plan (below
    right). Everything drawn on the hull's own waterline."""
    W, H = 2000, 1300
    img = Image.new("RGB", (W, H), (250, 248, 243))
    dr = ImageDraw.Draw(img, "RGBA")
    try:
        font = ImageFont.truetype("arial.ttf", 22)
        mid = ImageFont.truetype("arial.ttf", 16)
        small = ImageFont.truetype("arial.ttf", 14)
    except OSError:
        font = mid = small = ImageFont.load_default()
    dr.text((24, 14), title, fill=INK, font=font)

    # ---- side elevation: x -21..27, y -5..35 above the water
    s, ox, oy = 21.0, 40 + 21 * 21.0, 90 + 35 * 21.0

    def S(x, y, h):
        return (ox + x * s, oy - (y - h.WATER) * s)

    dr.text((40, 52), "Side elevation, on the waterline. Dashed: hold floor, lower deck, main deck. Thick bar: where the "
                      "main shrouds come down.", fill=GREY, font=small)
    for x in range(-20, 28, 5):
        dr.line([(ox + x * s, oy + 5 * s), (ox + x * s, oy + 5 * s + 6)], fill=GREY)
        dr.text((ox + x * s - 8, oy + 5 * s + 8), f"{x}", fill=GREY, font=small)
    for y in range(-5, 36, 5):
        dr.line([(ox - 21 * s - 6, oy - y * s), (ox - 21 * s, oy - y * s)], fill=GREY)
        dr.text((ox - 21 * s - 34, oy - y * s - 8), f"{y}", fill=GREY, font=small)
    dr.polygon([(ox - 21 * s, oy), (ox + 27 * s, oy), (ox + 27 * s, oy + 5 * s), (ox - 21 * s, oy + 5 * s)],
               fill=SEA + (28,))
    for h, col, filled, label in hulls:
        prof = [S(x, y, h) for x, y in h.profile()]
        if filled:
            dr.polygon(prof, fill=col + (80,), outline=col + (255,))
        else:
            dr.line(prof + [prof[0]], fill=col + (255,), width=3)
        for yd in (h.HOLD, h.LOWER, h.MAIN):
            xa, xb = h.stern_x(yd) + 0.3, h.bow_x(yd) - 0.3
            for k in np.arange(xa, xb, 0.8):
                dr.line([S(k, yd, h), S(min(k + 0.45, xb), yd, h)], fill=col + (200,), width=1)
        mw = 3 if filled else 2
        for name, (mx, foot, top, tops, lower_top) in h.MASTS.items():
            dr.line([S(mx, max(foot, h.KEEL), h), S(mx, top, h)], fill=col + (230,), width=mw + 2)
            if tops:
                dr.line([S(mx - 1.75, tops, h), S(mx + 1.75, tops, h)], fill=col + (230,), width=mw + 2)
        for x, y, hl in h.YARDS:
            dr.line([S(x - 0.9, y, h), S(x + 0.9, y, h)], fill=col + (200,), width=mw)
        (ax, ay), (bx, by) = h.BOWSPRIT
        dr.line([S(ax, ay, h), S(bx, by, h)], fill=col + (230,), width=mw + 1)
        (ax, ay), (bx, by) = h.LATEEN
        dr.line([S(ax, ay, h), S(bx, by, h)], fill=col + (200,), width=mw)
        # the main shrouds: from under the top to the channels
        mx, _, _, tops, _ = h.MASTS["main"]
        a, b = h.SHROUDS
        for xs in np.linspace(a, b, 5):
            yc = h.rail(xs) + 0.2
            dr.line([S(mx, tops, h), S(xs, yc, h)], fill=col + (120,), width=1)
        dr.line([S(a, h.rail(a) + 0.2, h), S(b, h.rail(b) + 0.2, h)], fill=col + (255,), width=6)
        if not filled:
            # names, on the proposal
            for x, y, nm in ((h.poop_aft() + 2.0, h.POOP + 2.2, "poop"),
                             (h.WAIST_AFT - 4.6, h.QUARTER + 2.0, "quarterdeck"),
                             (0.5 * (h.WAIST_AFT + h.WAIST_FWD) - 0.5, h.MAIN + 1.9, "waist"),
                             (h.WAIST_FWD + 0.3, h.QUARTER + 2.2, "forecastle")):
                dr.text(S(x, y, h), nm, fill=col, font=mid)
            for name, (mx, foot, top, tops, lower_top) in h.MASTS.items():
                dr.text(S(mx + 0.4, top + 1.2, h), name, fill=col, font=small)
    dr.line([(ox - 21 * s, oy), (ox + 27 * s, oy)], fill=SEA + (255,), width=2)
    dr.text((ox + 22 * s, oy - 22), "waterline", fill=SEA, font=small)

    # ---- midship section at x = 0 (right, top)
    s2, ox2, oy2 = 34.0, 1330, 90 + 12 * 34.0

    def Q(z, y, h):
        return (ox2 + z * s2, oy2 - (y - h.WATER) * s2)

    dr.text((1130, 52), "Midship section (x = 0), on the waterline", fill=GREY, font=small)
    dr.polygon([(ox2 - 6.5 * s2, oy2), (ox2 + 6.5 * s2, oy2), (ox2 + 6.5 * s2, oy2 + 5.2 * s2), (ox2 - 6.5 * s2, oy2 + 5.2 * s2)],
               fill=SEA + (28,))
    for h, col, filled, label in hulls:
        ys = np.linspace(h.KEEL, h.rail(0.0), 120)
        right = [Q(float(h.hull_hw(0.0, y)), y, h) for y in ys]
        left = [Q(-float(h.hull_hw(0.0, y)), y, h) for y in ys[::-1]]
        poly = [Q(0.125, h.KEEL - 0.5, h)] + right + left + [Q(-0.125, h.KEEL - 0.5, h)]
        if filled:
            dr.polygon(poly, fill=col + (80,), outline=col + (255,))
        else:
            dr.line(poly + [poly[0]], fill=col + (255,), width=3)
        for yd, nm in ((h.HOLD, "hold"), (h.LOWER, "lower deck"), (h.MAIN, "main deck")):
            w = float(h.hull_hw(0.0, yd)) - 0.3
            dr.line([Q(-w, yd, h), Q(w, yd, h)], fill=col + (170,), width=2)
            if not filled:
                dr.text(Q(-1.2, yd + 0.55, h), nm, fill=col, font=small)
    dr.line([(ox2 - 6.5 * s2, oy2), (ox2 + 6.5 * s2, oy2)], fill=SEA + (255,), width=2)
    dr.text((ox2 - 6.5 * s2, oy2 + 4), "waterline", fill=SEA, font=small)
    for z in range(-6, 7, 2):
        dr.text((ox2 + z * s2 - 6, oy2 + 5.3 * s2), f"{z}", fill=GREY, font=small)

    # ---- the numbers (right of the section)
    tx, ty = 1580, 90
    rows = [h.numbers() for h, _, _, _ in hulls]
    dr.text((tx, ty - 32), "", fill=INK, font=mid)
    colx = (tx, tx + 212, tx + 310)
    for j, (h, col, filled, label) in enumerate(hulls):
        dr.text((colx[1 + j] if j < 2 else colx[-1], ty), "now" if filled else "proposed", fill=col, font=mid)
    ty += 28
    for i, (name, _) in enumerate(rows[0]):
        if i % 2 == 0:
            dr.rectangle([tx - 6, ty - 3, W - 16, ty + 21], fill=(236, 232, 224))
        dr.text((colx[0], ty), name, fill=INK, font=small)
        for j, r in enumerate(rows):
            val = r[i][1]
            if colx[1 + j] + dr.textlength(val, font=small) > W - 20 or (j == 0 and dr.textlength(val, font=small) > 98):
                pass
            dr.text((colx[1 + j], ty), val, fill=hulls[j][1], font=small)
        ty += 26
    # ---- plan (below the section)
    s3, ox3, oy3 = 17.0, 1130 + 22 * 17.0, 1010

    def P(x, z):
        return (ox3 + x * s3, oy3 - z * s3)

    dr.text((1130, 890), "Plan: waterline (below the middle), main deck (above); masts; castle fronts", fill=GREY, font=small)
    for h, col, filled, label in hulls:
        for y, flip in ((h.WATER, -1), (h.MAIN, 1)):
            xs = np.linspace(h.stern_x(y) - 0.5, h.bow_x(y) + 0.5, 300)
            pts = [P(x, flip * float(h.hull_hw(x, y))) for x in xs]
            pts = [p for p, x in zip(pts, xs) if float(h.hull_hw(x, y)) > 0] or pts
            pts = pts + [(pts[-1][0], oy3), (pts[0][0], oy3)]
            if filled:
                dr.polygon(pts, fill=col + (70,), outline=col + (255,))
            else:
                dr.line(pts + [pts[0]], fill=col + (255,), width=3)
        for name, (mx, *_rest) in h.MASTS.items():
            dr.ellipse([P(mx - 0.45, 0.45), P(mx + 0.45, -0.45)], outline=col + (255,), width=2)
        for xc in (h.WAIST_AFT, h.WAIST_FWD, h.POOP_FRONT):
            dr.line([P(xc, 0.2), P(xc, 5.8)], fill=col + (170,), width=2)

    # ---- legend and notes (bottom left)
    ly = 1150
    for h, col, filled, label in hulls:
        dr.rectangle([40, ly, 70, ly + 18], fill=col + (110 if filled else 0,), outline=col + (255,), width=3)
        dr.text((80, ly), label, fill=INK, font=mid)
        ly += 28
    ny = 1150
    for n in notes:
        dr.text((520, ny), n, fill=INK, font=small)
        ny += 20
    img.save(path)
    return path


NOTES = (
    "Blocks = metres.  Proposed: 1.5 deeper, beam 10.5 with a flat floor a third of it, the quarterdeck 3 forward (the main",
    "shrouds come down on its sides), the poop 3 longer and overhanging the transom, more transom rake, the forecastle 1 longer",
    "aft and 1.25 further out, its underside sloping up from the stem; gentler castle sweeps; the foremast 0.9 nearer the stem.",
    "The mainmast stays at the middle of the keel (Fernandez 1616). Deck heights, and the masts' heights, unchanged.",
)

if __name__ == "__main__":
    now = carrack_now()
    new = proposal()
    out = os.path.join(ROOT, "docs", "proportions.png")
    print(draw([(now, (120, 80, 40), True, "0.3.2 as built"), (new, (200, 40, 40), False, "proposed")],
               out, "Carrack proportions: as built and proposed", NOTES))
    for (name, a), (_, b) in zip(now.numbers(), new.numbers()):
        print(f"  {name:32s} {a:28s} {b}")
