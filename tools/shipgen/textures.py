"""
The overlays Hansa puts on vanilla oak planks (the game composites them in its texture atlas, "base++mode~overlay"):

  tar    multiplied over the planks (++3~): near-black pitch that still lets the grain and the seams show through
  caulk  laid over the deck planks (++0~): black pitched seams, twice as wide as the plain plank seams (no butt ends:
         the texture repeats every block, and a butt every block makes the deck look like brickwork)

The vanilla oak plank texture already has a thin dark seam on rows 0, 11 and 22 (of 32). The first caulk overlay drew
its lines on exactly those rows, so a caulked deck looked like plain oak; the first tar only darkened the planks to
about 40 %, which reads as dark oak, not tar. Both are made stronger here.

Damage (Hansa 0.3.5), laid over a hull panel's planking (++0~, over the tar or the copper, or the plain oak inside),
two of each (a and b, so neighbouring strakes differ):

  dmg-worn      weathered: the pitch greying in faint streaks, pale oakum showing in the seams (subtle)
  dmg-scraped   long gouges along the planks showing lighter bare oak, a thin shadow under each
  dmg-cracked   dark splits along the grain, with lighter splinters beside them
  dmg-leaking   seams failing, outside: pale oakum working out of them in strands, dark wet runs under them
  dmg-holed     broken planks: a dark gap in each, its ends splintered pale along the grain (round a real hole)
  dmg-wet       seams leaking, inside (the ceiling): dark wet stains spreading from them and running down

And on the sails' linen (square sails and the lateen), a torn or shredded sail: sail-hole-* (++8~, cut out: where it
has any alpha the sail becomes see-through, and the mod draws sails alpha-tested) with sail-fray-* laid over first
(++0~, the frayed darker edges round the holes).

And (0.3.12) glass-clear, the windows' clear panes (glass_clear).

  python tools/shipgen/textures.py [--preview out.png]
  python tools/shipgen/textures.py --glass          # only glass-clear.png
"""
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "ships", "textures", "block")
SEAMS = (0, 11, 22)          # the vanilla plank seams (rows of a 32 x 32 texture)
N = 32


def tar():
    rng = np.random.default_rng(7)
    img = np.zeros((N, N, 4), np.float64)
    base = np.array([72.0, 61.0, 52.0])
    streak = rng.normal(0, 4.0, (N, 1))          # brushed along the planks
    noise = rng.normal(0, 3.0, (N, N, 1))
    img[..., :3] = base + streak + noise
    for r in SEAMS:                              # the pitch sits thicker in the seams
        img[r, :, :3] *= 0.55
        img[(r - 1) % N, :, :3] *= 0.8
    img[..., 3] = 255
    return np.clip(img, 0, 255).astype(np.uint8)


def caulk():
    img = np.zeros((N, N, 4), np.uint8)
    pitch = (18, 14, 10, 255)
    for r in SEAMS:
        img[r] = pitch
        img[(r - 1) % N] = pitch
    return img


# ---------------------------------------------------------------- damage on the planking

PLANKS = ((1, 10), (12, 21), (23, 31))      # the rows of each plank between the seams (inclusive)
DAMAGE = ("worn", "scraped", "cracked", "leaking", "holed", "wet")


def _put(img, r, c, rgb, a):
    """Lay a pixel (row, column; the column wraps: the texture repeats along the planks) at alpha a (0..1), over what's
    there (the stronger wins)."""
    if not 0 <= r < N:
        return
    c %= N
    if a > img[r, c, 3] / 255:
        img[r, c, :3] = rgb
        img[r, c, 3] = int(round(255 * min(1.0, a)))


def worn(seed):
    """Weathered planking: faint grey streaks in the pitch along the grain, pale oakum showing in stretches of the seams."""
    rng = np.random.default_rng(100 + seed)
    img = np.zeros((N, N, 4), np.uint8)
    for r0, r1 in PLANKS:
        for _ in range(3):
            r, c, n = int(rng.integers(r0, r1 + 1)), int(rng.integers(0, N)), int(rng.integers(6, 20))
            for k in range(n):
                _put(img, r, c + k, (150, 142, 128), 0.16 + 0.08 * np.sin(np.pi * k / n))
    for s in SEAMS:
        c = int(rng.integers(0, N))
        for k in range(int(rng.integers(8, 16))):
            _put(img, s, c + k, (176, 156, 116), 0.45)
            if rng.random() < 0.4:
                _put(img, s + 1, c + k, (150, 132, 98), 0.25)
    return img


def scraped(seed):
    """Grounding scrapes: gouges along the planks (they scrape along her length) down to bare, lighter oak."""
    rng = np.random.default_rng(200 + seed)
    img = np.zeros((N, N, 4), np.uint8)
    for r0, r1 in PLANKS:
        for _ in range(2):
            r, c, n = int(rng.integers(r0 + 1, r1)), int(rng.integers(0, N)), int(rng.integers(10, 26))
            h = 1 + int(rng.random() < 0.5)
            for k in range(n):
                taper = min(1.0, min(k, n - 1 - k) / 3 + 0.35)
                rr = r + (1 if rng.random() < 0.15 else 0)
                for dh in range(h):
                    _put(img, rr + dh, c + k, (184, 146, 104) if dh == 0 else (160, 124, 86), 0.9 * taper)
                _put(img, rr + h, c + k, (40, 30, 22), 0.35 * taper)        # its shadow under it
        for _ in range(4):                                                   # scuffs
            _put(img, int(rng.integers(r0, r1 + 1)), int(rng.integers(0, N)), (150, 118, 84), 0.5)
    return img


def cracked(seed):
    """Split planks: dark cracks along the grain that wander a little, the split's lip lifted pale (fresh wood) along
    one side, so they show on tar as on bare oak."""
    rng = np.random.default_rng(300 + seed)
    img = np.zeros((N, N, 4), np.uint8)
    for r0, r1 in PLANKS:
        for _ in range(1 + int(rng.random() < 0.6)):
            r, c, n = int(rng.integers(r0 + 1, r1)), int(rng.integers(0, N)), int(rng.integers(12, 28))
            lip = -1 if rng.random() < 0.5 else 1
            for k in range(n):
                if rng.random() < 0.18:
                    r = int(np.clip(r + rng.choice((-1, 1)), r0 + 1, r1 - 1))
                end = min(k, n - 1 - k)
                _put(img, r, c + k, (18, 12, 8), 0.95)
                if end > 0:
                    _put(img, r + lip, c + k, (176, 140, 98), 0.45 + 0.1 * min(end, 3))
                if rng.random() < 0.07:                                    # a short branch
                    _put(img, r - lip, c + k + 1, (26, 18, 12), 0.85)
                    _put(img, r - 2 * lip, c + k + 2, (26, 18, 12), 0.6)
    return img


def leaking(seed):
    """Seams failing, seen outside: the oakum working out of them in pale strands, some hanging loose, and dark wet
    runs under the worst stretches."""
    rng = np.random.default_rng(400 + seed)
    img = np.zeros((N, N, 4), np.uint8)
    for s in SEAMS:
        c = int(rng.integers(0, N))
        for _ in range(3):
            n = int(rng.integers(5, 12))
            for k in range(n):
                _put(img, s, c + k, (196, 176, 130), 0.9)
                if rng.random() < 0.45:
                    _put(img, s + 1, c + k, (170, 150, 108), 0.7)
                if rng.random() < 0.12:                                    # a strand hanging out
                    for dr in range(2, 2 + int(rng.integers(1, 4))):
                        _put(img, s + dr, c + k + (dr > 2), (184, 164, 120), 0.75)
            for dc in range(0, n, 3):                                      # wet runs under it
                for dr in range(2, 2 + int(rng.integers(2, 6))):
                    _put(img, s + dr, c + dc, (10, 12, 14), 0.4)
            c += n + int(rng.integers(2, 8))
    return img


def wet(seed):
    """Seams leaking, seen inside (the ceiling): dark wet stains spreading from the seams and running down the planks,
    a little green where it has stood."""
    rng = np.random.default_rng(700 + seed)
    img = np.zeros((N, N, 4), np.uint8)
    for s in SEAMS:
        for c in range(N):
            _put(img, s, c, (14, 12, 10), 0.7)
        for _ in range(3):
            c, w = int(rng.integers(0, N)), int(rng.integers(4, 10))
            depth = rng.integers(3, 9, w)
            depth = np.maximum(depth, np.roll(depth, 1) - 2)            # the stain's lower edge runs smoothly
            for dc in range(w):
                for dr in range(1, int(depth[dc]) + 1):
                    _put(img, s + dr, c + dc, (26, 28, 26), 0.62 - 0.4 * dr / (depth[dc] + 1))
    for _ in range(4):
        r, c = int(rng.integers(0, N)), int(rng.integers(0, N))
        for dr in range(-1, 2):
            for dc in range(-2, 3):
                _put(img, r + dr, c + dc, (46, 62, 38), 0.2)
    return img


def holed(seed):
    """Stove in: in every plank a break, a dark gap between broken ends, the ends splintered pale (raw wood) along the
    grain, so it reads on tar, copper and bare oak alike."""
    rng = np.random.default_rng(500 + seed)
    img = np.zeros((N, N, 4), np.uint8)
    for r0, r1 in PLANKS:
        c0 = int(rng.integers(0, N))
        left, right = 0, int(rng.integers(5, 9))
        for r in range(r0, r1 + 1):
            # the break's two edges wander a little from row to row
            left = int(np.clip(left + rng.integers(-1, 2), -2, 2))
            right = int(np.clip(right + rng.integers(-1, 2), 4, 10))
            for c in range(c0 + left, c0 + right):
                _put(img, r, c, (14, 10, 8), 0.9)
            # splinters sticking out of both broken ends, along the grain
            for end, way in ((c0 + left - 1, -1), (c0 + right, 1)):
                n = int(rng.integers(1, 6))
                for k in range(n):
                    _put(img, r, end + way * k, (186, 150, 106), 0.9 - 0.12 * k)
        for _ in range(3):                                                 # loose splinters round it
            r = int(rng.integers(r0, r1 + 1))
            c = c0 + int(rng.choice((-1, 1))) * int(rng.integers(7, 12))
            for k in range(int(rng.integers(2, 5))):
                _put(img, r, c + k, (170, 136, 96), 0.7)
    return img


def sail_hole(seed, shredded=False):
    """Where the sail is torn away (cut out: any alpha here makes the linen see-through): slits and ragged holes; a
    shredded sail has big ones."""
    rng = np.random.default_rng(600 + seed + (50 if shredded else 0))
    img = np.zeros((N, N, 4), np.uint8)
    for _ in range(3 if shredded else 1):
        # a ragged hole
        cr, cc = rng.integers(6, N - 6, 2)
        rad = rng.uniform(5, 9) if shredded else rng.uniform(2.5, 4.5)
        for r in range(N):
            for c in range(N):
                a = np.arctan2(r - cr, c - cc)
                rr = rad * (1 + 0.35 * np.sin(3 * a + seed) + 0.2 * np.sin(7 * a + 2 * seed))
                if np.hypot(r - cr, c - cc) < rr:
                    img[r, c] = (0, 0, 0, 255)
    for _ in range(3 if shredded else 2):
        # a slit, torn along the weave
        r, c = int(rng.integers(2, N - 2)), int(rng.integers(0, N))
        horizontal = rng.random() < 0.5
        for k in range(int(rng.integers(8, 22 if shredded else 14))):
            img[r % N, c % N] = (0, 0, 0, 255)
            if horizontal:
                c += 1
                r += int(rng.random() < 0.2) * int(rng.choice((-1, 1)))
            else:
                r += 1
                c += int(rng.random() < 0.2) * int(rng.choice((-1, 1)))
    return img


def mirrored(hole):
    """A sail piece is a thin box whose two faces show the texture mirrored left to right: a hole must be there on both,
    so it's made the same both ways round."""
    img = hole.copy()
    img[hole[:, ::-1, 3] > 0] = (0, 0, 0, 255)
    return img


def sail_fray(hole):
    """The frayed, darker edges round a sail's holes (laid on before they're cut out)."""
    img = np.zeros((N, N, 4), np.uint8)
    cut = hole[..., 3] > 0
    near = np.zeros_like(cut)
    for dr in (-1, 0, 1):
        for dc in (-1, 0, 1):
            near |= np.roll(np.roll(cut, dr, 0), dc, 1)
    ring = near & ~cut
    img[ring] = (92, 84, 70, 200)
    return img


def damage_overlays():
    """{name: pixels} of every damage overlay the mod asks for."""
    out = {}
    for s, fn in zip(DAMAGE, (worn, scraped, cracked, leaking, holed, wet)):
        for v, seed in (("a", 0), ("b", 1)):
            out[f"dmg-{s}-{v}"] = fn(seed)
    for v, seed in (("a", 0), ("b", 1), ("c", 2)):
        h = mirrored(sail_hole(seed))
        out[f"sail-hole-{v}"], out[f"sail-fray-{v}"] = h, sail_fray(h)
    for v, seed in (("a", 0), ("b", 1)):
        h = mirrored(sail_hole(seed, shredded=True))
        out[f"sail-shred-{v}"], out[f"sail-shredfray-{v}"] = h, sail_fray(h)
    return out


def glass_clear():
    """
    (0.3.12, the user's note 109: "glass in windows ... makes sea not recognizable") the windows' clear panes: clearer than
    vanilla's plain glass (its alpha is 0.2 all over): 0.08, a faint cool tint, two soft diagonal glints (0.16) so a
    pane still reads as glass, its edge a pixel at 0.24 (the putty line round each pane)
    """
    img = np.zeros((N, N, 4), np.uint8)
    img[..., :3] = (214, 226, 232)
    a = np.full((N, N), 0.08)
    r, c = np.mgrid[0:N, 0:N]
    for off, w in ((-6, 2.5), (5, 1.2)):
        a = np.maximum(a, 0.16 * np.exp(-((r + c - N + off) / w) ** 2))
    edge = (r == 0) | (c == 0) | (r == N - 1) | (c == N - 1)
    a[edge] = 0.24
    img[..., 3] = np.round(a * 255).astype(np.uint8)
    return img


def write_glass():
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(glass_clear(), "RGBA").save(os.path.join(OUT, "glass-clear.png"))


def write():
    os.makedirs(OUT, exist_ok=True)
    write_glass()
    Image.fromarray(tar(), "RGBA").save(os.path.join(OUT, "tar.png"))
    Image.fromarray(caulk(), "RGBA").save(os.path.join(OUT, "caulk.png"))
    for name, img in damage_overlays().items():
        Image.fromarray(img, "RGBA").save(os.path.join(OUT, name + ".png"))


def composite(base_rgba, overlay, mode):
    """What the game's atlas makes of base++mode~overlay (0 normal, 3 multiply, 8 cut out), for previews."""
    b = base_rgba.astype(np.float64)
    o = overlay.astype(np.float64)
    out = b.copy()
    if mode == 3:
        out[..., :3] = b[..., :3] * o[..., :3] / 255
    elif mode == 8:
        out[..., 3] = np.where(o[..., 3] > 0, 0, b[..., 3])
    else:
        a = o[..., 3:4] / 255
        out[..., :3] = b[..., :3] * (1 - a) + o[..., :3] * a
    return out.astype(np.uint8)


def preview(path):
    """The overlays on vanilla's textures, 2 x 2 tiles each: plain, tarred and caulked oak; then a row each for tarred
    oak, copper sheathing and plain oak (the ceiling inside) worn, scraped, cracked, leaking and holed; then linen
    whole, torn (three) and shredded (two), over a dark ground so the holes show."""
    tex = os.path.join(os.path.expandvars(os.environ.get("VINTAGE_STORY", r"%APPDATA%\Vintagestory")),
                       "assets", "survival", "textures", "block")
    load = lambda *p: np.array(Image.open(os.path.join(tex, *p)).convert("RGBA"))
    oak, copper, linen = load("wood", "planks", "oak1.png"), load("metal", "sheet", "copper1.png"), load("linen.png")
    tarred = composite(oak, tar(), 3)
    ov = damage_overlays()
    rows = [[oak, tarred, composite(oak, caulk(), 0)]]
    for base in (tarred, copper, oak):
        rows.append([base] + [composite(base, ov[f"dmg-{s}-a"], 0) for s in DAMAGE])
    sails = [linen]
    for v in "abc":
        sails.append(composite(composite(linen, ov[f"sail-fray-{v}"], 0), ov[f"sail-hole-{v}"], 8))
    for v in "ab":
        sails.append(composite(composite(linen, ov[f"sail-shredfray-{v}"], 0), ov[f"sail-shred-{v}"], 8))
    rows.append(sails)
    width = max(len(r) for r in rows) * 2 * N + (max(len(r) for r in rows) - 1) * 4
    out = []
    for r in rows:
        tiles = []
        for t in r:
            t = np.tile(t, (2, 2, 1)).astype(np.float64)
            a = t[..., 3:4] / 255
            tiles.append((t[..., :3] * a + np.array([40, 44, 50]) * (1 - a)).astype(np.uint8))
            tiles.append(np.full((2 * N, 4, 3), 255, np.uint8))
        line = np.concatenate(tiles[:-1], axis=1)
        line = np.concatenate([line, np.full((2 * N, width - line.shape[1], 3), 255, np.uint8)], axis=1)
        out += [line, np.full((4, width, 3), 255, np.uint8)]
    img = np.concatenate(out[:-1], axis=0)
    Image.fromarray(img).resize((img.shape[1] * 3, img.shape[0] * 3), Image.NEAREST).save(path)
    for name, t in zip(("oak", "tarred", "caulked"), rows[0]):
        print(f"{name:8s} mean rgb {t[..., :3].reshape(-1, 3).mean(axis=0).round(0)}")
    for s, t in zip(DAMAGE, rows[1][1:]):
        print(f"tarred, {s:8s} mean rgb {t[..., :3].reshape(-1, 3).mean(axis=0).round(0)}")


if __name__ == "__main__":
    if "--glass" in sys.argv:
        write_glass()           # (only the clear glass: python textures.py --glass)
        sys.exit(0)
    write()
    if "--preview" in sys.argv:
        preview(sys.argv[sys.argv.index("--preview") + 1])
